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
    private PetSettings settings = new();
    private PetWindow pet = null!;
    private Forms.NotifyIcon tray = null!;
    private Forms.ContextMenuStrip menu = null!;
    private DashboardWindow? dashboard;
    private OptionsWindow? options;
    private DispatcherTimer pollTimer = null!;
    private bool polling, quitting;
    public bool Paused { get; private set; }
    public string? SelectedID { get; private set; }
    public Snapshot Snapshot { get; private set; } = new(DateTimeOffset.Now, [], []);
    public PetIdentity Identity => new(settings.PetName);
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e); ShutdownMode = ShutdownMode.OnExplicitShutdown;
        settings = store.Load();
        try { pet = new PetWindow(new SpriteAtlas()) { Identity = Identity }; }
        catch (Exception error) { Program.Errors.Append(error); MessageBox.Show(error.Message, "AIpet"); Shutdown(1); return; }
        pet.OpenDashboard += ShowDashboard;
        pet.OpenMenu += () => { SetForegroundWindow(menu.Handle); menu.Show(Forms.Cursor.Position); };
        pet.Moved += SavePosition; pet.DozeChanged += UpdatePet;
        BuildTray();
        // EnsureHandle raises SourceInitialized: styles and the message hook are installed before Restore.
        new WindowInteropHelper(pet).EnsureHandle();
        pet.Restore(settings); UpdatePet(); pet.Show();
        pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        pollTimer.Tick += async (_, _) => await Refresh(); pollTimer.Start();
        _ = Refresh();
    }
    private void BuildTray()
    {
        menu = new Forms.ContextMenuStrip();
        void Item(string label, Action action) { menu.Items.Add(label, null, (_, _) => Dispatcher.Invoke(action)); }
        Item("작업 목록 열기", ShowDashboard);
        Item("펫 보이기 / 숨기기", () => { if (pet.IsVisible) pet.Hide(); else { pet.Show(); pet.Constrain(); } });
        Item("잠시 쉬기 / 다시 시작", TogglePause);
        var bubbleItem = new Forms.ToolStripMenuItem("말풍선 항상 보이기") { Checked = settings.BubbleAlwaysShown, CheckOnClick = true };
        bubbleItem.Click += (_, _) => { pet.AlwaysShown = bubbleItem.Checked; settings = settings with { BubbleAlwaysShown = bubbleItem.Checked }; Save(); };
        menu.Items.Add(bubbleItem); menu.Items.Add(new Forms.ToolStripSeparator());
        Item("옵션…", ShowOptions); Item("위치 초기화", pet.ResetPosition);
        Item("동작 미리보기", () => pet.Preview(false)); Item("마우스 시선 미리보기", () => pet.Preview(true));
        menu.Items.Add(new Forms.ToolStripSeparator()); Item("AIpet 종료", Quit);
        using var resource = GetResourceStream(new Uri("pack://application:,,,/AIpet;component/Assets/Generated/AIpet.ico")).Stream;
        using var icon = new System.Drawing.Icon(resource);
        tray = new Forms.NotifyIcon { Icon = (System.Drawing.Icon)icon.Clone(), ContextMenuStrip = menu, Text = "AIpet", Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowDashboard);
    }
    public async Task Refresh(bool force = false)
    {
        if (Paused || polling || quitting) return;
        polling = true;
        try
        {
            var snapshot = await Task.Run(() => monitor.Poll(forceDiscovery: force));
            if (!Paused && !quitting) { Snapshot = snapshot; UpdatePet(); dashboard?.Refresh(); }
        }
        finally { polling = false; }
    }
    private void UpdatePet()
    {
        var status = PetStatusPresenter.Present(Snapshot, Identity, DateTimeOffset.Now, Paused, SelectedID, pet.IsDozing);
        pet.SetStatus(status);
        // NotifyIcon's native tooltip accepts at most 127 UTF-16 units.
        tray.Text = status.ToolTip.Length > 127 ? status.ToolTip[..127] : status.ToolTip;
    }
    public void Select(string? id) { SelectedID = id; UpdatePet(); dashboard?.Refresh(); }
    public void TogglePause()
    {
        Paused = !Paused; pet.SetPaused(Paused); UpdatePet(); dashboard?.Refresh(); if (!Paused) _ = Refresh();
    }
    public void Preview() => pet.Preview(false);
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
        settings = settings with { PetName = PetIdentity.Normalized(input) }; pet.Identity = Identity; Save(); UpdatePet(); dashboard?.Refresh(); return true;
    }
    public void Resize(double percent)
    {
        settings = settings with { PetScale = Math.Clamp(Math.Round(percent), 25, 200) };
        pet.ResizePet(settings.PetScale); Save();
    }
    private void SavePosition() { settings = settings with { Position = pet.SavedPosition }; Save(); }
    private void Save()
    {
        try { store.Save(settings); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Program.Errors.Append(e); MessageBox.Show("설정을 저장할 수 없습니다.", "AIpet"); }
    }
    private void Quit() { quitting = true; SavePosition(); Shutdown(); }
    protected override void OnExit(ExitEventArgs e)
    {
        quitting = true; pollTimer?.Stop();
        if (tray is not null) { tray.Visible = false; tray.Icon?.Dispose(); tray.Dispose(); }
        menu?.Dispose(); base.OnExit(e);
    }
}
