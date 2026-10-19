using System.Diagnostics;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;
namespace AIpet.App;
public sealed class PetApplication : Application
{
    private readonly RepeatedExceptionGuard exceptionGuard = new();
    public PetApplication()
    {
        DispatcherUnhandledException += (_, e) => {
            Program.Errors.Append(e.Exception);
            e.Handled = true;
            if (exceptionGuard.ShouldStop(e.Exception)) { quitting = true; Shutdown(1); }
        };
    }
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
    private readonly SettingsStore store = new();
    private readonly StatusMonitor monitor = new();
    private readonly UsageMonitor usage = new(new UsageSources());
    public UsageState Usage => usage.State;
    private PetSettings settings = new();
    private PetWindow? pet;
    private SpriteAtlas atlas = null!;
    private readonly WindowDiagnostics diagnostics = new();
    private readonly WindowRecreationGuard recreation = new();
    private readonly Stopwatch lifetime = Stopwatch.StartNew();
    private bool intendedVisible = true, creating;
    private PetStatus? currentStatus;
    private DispatcherTimer guardTimer = null!;
    private Forms.NotifyIcon tray = null!;
    private Forms.ContextMenuStrip menu = null!;
    private DashboardWindow? dashboard;
    private OptionsWindow? options;
    private DispatcherTimer pollTimer = null!;
    private AppUpdater updates = null!;
    private bool polling, quitting;
    public bool Paused { get; private set; }
    public string? SelectedID { get; private set; }
    public Snapshot Snapshot { get; private set; } = new(DateTimeOffset.Now, [], []);
    public PetIdentity Identity => new(settings.PetName);
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e); ShutdownMode = ShutdownMode.OnExplicitShutdown;
        settings = store.Load();
        usage.Configure(Paused, settings.ClaudeUsageDirectEnabled, settings.CodexPath);
        try
        {
            atlas = new SpriteAtlas(); updates = new AppUpdater(Quit); BuildTray(); CreatePet(); UpdatePet();
            new CopyLocations(CopyLocations.DefaultPath).Add(new WindowsCopies().CurrentPath);
            StartupRegistration.Apply(settings.LaunchAtLogin);
        }
        catch (Exception error) { Program.Errors.Append(error); MessageBox.Show(error.Message, "AIpet"); quitting = true; Shutdown(1); return; }
        guardTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        guardTimer.Tick += async (_, _) => {
            EnsurePet(); UpdatePet();
            await usage.Refresh(DateTimeOffset.UtcNow);
            if (!quitting) { UpdatePet(); dashboard?.RefreshUsage(); options?.RefreshUsage(); }
        }; guardTimer.Start();
        pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        pollTimer.Tick += async (_, _) => await Refresh(); pollTimer.Start();
        _ = Refresh();
        updates.Start();
    }
    private void OpenPetMenu() { SetForegroundWindow(menu.Handle); menu.Show(Forms.Cursor.Position); }
    private void PetClosed(object? sender, EventArgs e)
    {
        if (!quitting) diagnostics.Record("PET_CLOSED", $"intendedVisible={intendedVisible}");
        // The application timer recovers after WPF finishes closing the old window.
    }
    private void ReleasePet()
    {
        var previous = pet; pet = null;
        if (previous is null) return;
        settings = settings with { Position = previous.SavedPosition ?? settings.Position };
        previous.OpenDashboard -= ShowDashboard; previous.OpenMenu -= OpenPetMenu;
        previous.Moved -= SavePosition; previous.DozeChanged -= UpdatePet; previous.Closed -= PetClosed;
        try { previous.Retire(); }
        catch (Exception error) { Program.Errors.Append(error); }
    }
    private void CreatePet()
    {
        creating = true;
        try
        {
            pet = new PetWindow(atlas, () => quitting) { Identity = Identity };
            MainWindow = pet;
            // SourceInitialized installs the styles and hook before restoring position.
            new WindowInteropHelper(pet).EnsureHandle();
            pet.Restore(settings); pet.SetPaused(Paused);
            pet.SetStatus(currentStatus ?? PetStatusPresenter.Present(Snapshot, Identity, DateTimeOffset.Now, Paused, SelectedID));
            pet.OpenDashboard += ShowDashboard; pet.OpenMenu += OpenPetMenu;
            pet.Moved += SavePosition; pet.DozeChanged += UpdatePet; pet.Closed += PetClosed;
            pet.SetIntendedVisible(intendedVisible);
            if (!pet.IsAlive) throw new InvalidOperationException("Pet window creation failed.");
        }
        catch { ReleasePet(); throw; }
        finally { creating = false; }
    }
    private PetWindow? EnsurePet(bool force = false)
    {
        if (quitting || creating) return null;
        var exists = pet is { IsAlive: true };
        if (pet is not null && !exists)
        {
            diagnostics.Record("PET_LOST", $"intendedVisible={intendedVisible}");
            ReleasePet();
        }
        var decision = recreation.Decide(exists, intendedVisible, quitting, lifetime.Elapsed.TotalSeconds, force);
        if (decision == WindowRecreation.LimitReached)
            diagnostics.Record("RECREATE_LIMIT", $"attempts={recreation.AttemptsInWindow}; intervalSeconds=10");
        else if (decision == WindowRecreation.Recreate)
        {
            try
            {
                CreatePet();
                diagnostics.Record("RECREATE_SUCCESS", $"attempts={recreation.AttemptsInWindow}");
            }
            catch (Exception error)
            {
                Program.Errors.Append(error);
                diagnostics.Record("RECREATE_FAILED", $"attempts={recreation.AttemptsInWindow}; hresult=0x{error.HResult:X}");
            }
        }
        return pet is { IsAlive: true } ? pet : null;
    }
    private void SetPetVisible(bool visible)
    {
        intendedVisible = visible;
        EnsurePet(force: visible)?.SetIntendedVisible(visible);
    }
    private void RevealPet()
    {
        if (pet is { IsShown: true }) return;
        SetPetVisible(true);
    }
    private void ResetPetPosition()
    {
        var window = EnsurePet();
        settings = settings with { Position = null };
        window?.ResetPosition(); Save();
    }
    private void SaveDiagnostics()
    {
        try
        {
            var value = VisibilityDiagnostics.Build(typeof(PetApplication).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().First().InformationalVersion.Split('+')[0], DateTimeOffset.Now, intendedVisible,
                pet?.DiagnosticState() ?? new(false), ScreenCoordinates.Monitors(), settings.Position,
                settings.PetScale, recreation.Count(lifetime.Elapsed.TotalSeconds), Paused, diagnostics.RecentLines());
            var path = Path.Combine(DiagnosticOutput.DefaultRoot, "diagnostics.json");
            if (new DiagnosticOutput().Write(JsonData.Sorted(value, true), "diagnostics.json", path) != 0) throw new IOException();
            Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = "/select,\"" + path + "\"", UseShellExecute = true });
        }
        catch { MessageBox.Show("진단 정보를 저장하거나 저장 위치를 열 수 없습니다.", "AIpet"); }
    }
    private void BuildTray()
    {
        menu = new Forms.ContextMenuStrip();
        void Item(string label, Action action) { menu.Items.Add(label, null, (_, _) => Dispatcher.Invoke(action)); }
        Item("작업 목록 열기", ShowDashboard);
        Item("펫 보이기 / 숨기기", () => SetPetVisible(!intendedVisible));
        Item("잠시 쉬기 / 다시 시작", TogglePause);
        var bubbleItem = new Forms.ToolStripMenuItem("말풍선 항상 보이기") { Checked = settings.BubbleAlwaysShown, CheckOnClick = true };
        bubbleItem.Click += (_, _) => { settings = settings with { BubbleAlwaysShown = bubbleItem.Checked }; if (EnsurePet() is { } window) window.AlwaysShown = bubbleItem.Checked; Save(); };
        menu.Items.Add(bubbleItem); menu.Items.Add(new Forms.ToolStripSeparator());
        Item("진단 정보 저장", SaveDiagnostics);
        Item("옵션…", ShowOptions); Item("위치 초기화", ResetPetPosition);
        Item("동작 미리보기", () => Preview(false)); Item("마우스 시선 미리보기", () => Preview(true));
        menu.Items.Add(new Forms.ToolStripSeparator());
        Item("업데이트 확인…", () => _ = updates.Check(true));
        var automaticUpdates = new Forms.ToolStripMenuItem("업데이트 자동 확인") { Checked = updates.Automatic, CheckOnClick = true };
        automaticUpdates.Click += (_, _) => updates.Automatic = automaticUpdates.Checked;
        menu.Items.Add(automaticUpdates);
        var launchAtLogin = new Forms.ToolStripMenuItem("Windows 시작 시 자동 실행") { Checked = settings.LaunchAtLogin, CheckOnClick = true };
        launchAtLogin.Click += (_, _) => { settings = settings with { LaunchAtLogin = launchAtLogin.Checked }; Save(); StartupRegistration.Apply(launchAtLogin.Checked); };
        menu.Items.Add(launchAtLogin);
        menu.Items.Add(new Forms.ToolStripSeparator()); Item("AIpet 종료", Quit);
        using var resource = GetResourceStream(new Uri("pack://application:,,,/AIpet;component/Assets/Generated/AIpet.ico")).Stream;
        using var icon = new System.Drawing.Icon(resource);
        tray = new Forms.NotifyIcon { Icon = (System.Drawing.Icon)icon.Clone(), ContextMenuStrip = menu, Text = "AIpet", Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) Dispatcher.Invoke(RevealPet); };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowDashboard);
    }
    public async Task Refresh(bool force = false)
    {
        if (Paused || polling || quitting) return;
        polling = true;
        try
        {
            var result = await Task.Run(() => { var snapshot = monitor.Poll(forceDiscovery: force); return (snapshot, monitor.LatestCodexUsage); });
            var snapshot = result.snapshot;
            if (!Paused && !quitting) { usage.SetCodexRecord(result.LatestCodexUsage); Snapshot = snapshot; UpdatePet(); dashboard?.Refresh(); }
        }
        finally { polling = false; }
    }
    private void UpdatePet()
    {
        var subtitle = Usage.Subtitle(DateTimeOffset.UtcNow, lifetime.Elapsed.TotalSeconds, text =>
            new FormattedText(text, System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 11, Brushes.DimGray, 1).Width <= 298);
        var status = PetStatusPresenter.Present(Snapshot, Identity, DateTimeOffset.Now, Paused, SelectedID,
            pet is { IsAlive: true, IsDozing: true }, subtitle);
        currentStatus = status;
        EnsurePet()?.SetStatus(status);
        // NotifyIcon's native tooltip accepts at most 127 UTF-16 units.
        tray.Text = status.ToolTip.Length > 127 ? status.ToolTip[..127] : status.ToolTip;
    }
    public void Select(string? id) { SelectedID = id; UpdatePet(); dashboard?.Refresh(); }
    public void TogglePause()
    {
        Paused = !Paused; usage.Configure(Paused, settings.ClaudeUsageDirectEnabled, settings.CodexPath); EnsurePet()?.SetPaused(Paused); UpdatePet(); dashboard?.Refresh(); if (!Paused) _ = Refresh();
    }
    public void Preview() => Preview(false);
    private void Preview(bool look)
    {
        intendedVisible = true; EnsurePet()?.Preview(look);
    }
    public void ShowDashboard()
    {
        if (dashboard is null) { dashboard = new(this); dashboard.Closed += (_, _) => dashboard = null; }
        dashboard.Show(); if (dashboard.WindowState == WindowState.Minimized) dashboard.WindowState = WindowState.Normal; dashboard.Activate();
    }
    private void ShowOptions()
    {
        if (options is null) { options = new(this, settings); options.Closed += (_, _) => options = null; }
        options.Show(); options.Activate();
    }
    public bool SaveName(string input)
    {
        if (PetIdentity.ValidationMessage(input) is not null) return false;
        settings = settings with { PetName = PetIdentity.Normalized(input) }; if (EnsurePet() is { } window) window.Identity = Identity; Save(); UpdatePet(); dashboard?.Refresh(); return true;
    }
    public void SetDirectUsage(bool enabled)
    {
        settings = settings with { ClaudeUsageDirectEnabled = enabled }; Save();
        usage.Configure(Paused, enabled, settings.CodexPath); UpdatePet(); dashboard?.RefreshUsage(); options?.RefreshUsage();
    }
    public void Resize(double percent)
    {
        settings = settings with { PetScale = Math.Clamp(Math.Round(percent), 25, 200) };
        EnsurePet()?.ResizePet(settings.PetScale); Save();
    }
    private void SavePosition() { if (pet is { IsAlive: true }) settings = settings with { Position = pet.SavedPosition }; Save(); }
    private void Save()
    {
        try { store.Save(settings); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Program.Errors.Append(e); MessageBox.Show("설정을 저장할 수 없습니다.", "AIpet"); }
    }
    private void Quit() { quitting = true; SavePosition(); Shutdown(); }
    protected override void OnExit(ExitEventArgs e)
    {
        quitting = true; updates?.Dispose(); usage.Configure(true, settings.ClaudeUsageDirectEnabled, settings.CodexPath); pollTimer?.Stop(); guardTimer?.Stop(); ReleasePet();
        if (tray is not null) { tray.Visible = false; tray.Icon?.Dispose(); tray.Dispose(); }
        menu?.Dispose(); base.OnExit(e);
    }
}
