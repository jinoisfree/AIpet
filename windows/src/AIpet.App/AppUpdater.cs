using System.Diagnostics;
using System.Reflection;
using System.Windows.Threading;
namespace AIpet.App;

internal sealed class AppUpdater : IDisposable
{
    private readonly GitHubUpdateClient client = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly UpdatePreferencesStore store = new(Path.Combine(DiagnosticOutput.DefaultRoot, "updates.json"));
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(20) };
    private readonly Action quit;
    private UpdatePreferences preferences;
    private bool busy, disposed;
    private Window? progressWindow;
    public bool Automatic
    {
        get => preferences.Automatic;
        set { preferences = preferences with { Automatic = value }; Save(); }
    }
    private static AppVersion? Current => AppVersion.Parse(typeof(AppUpdater).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion);
    public AppUpdater(Action quit)
    {
        this.quit = quit; preferences = store.Load();
        timer.Tick += OnTick;
    }
    public void Start() => timer.Start();
    private async void OnTick(object? sender, EventArgs e)
    {
        timer.Interval = TimeSpan.FromMinutes(15);
        await Check(false);
    }
    private void Save()
    {
        try { store.Save(preferences); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
    public async Task Check(bool manual)
    {
        if (busy || disposed || !manual && !preferences.CheckDue(DateTimeOffset.UtcNow)) return;
        busy = true;
        try
        {
            if (Current is not { } current) throw new InvalidDataException();
            preferences = preferences with { LastCheck = DateTimeOffset.UtcNow }; Save();
            var update = await client.Check(current, lifetime.Token);
            if (disposed || !manual && !Automatic) return;
            if (update is null)
            {
                if (manual) MessageBox.Show($"현재 최신 버전입니다.\nAIpet {current}", "AIpet 업데이트", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (!manual && !preferences.ShouldPrompt(update.Version, DateTimeOffset.UtcNow)) return;
            var answer = MessageBox.Show($"AIpet {update.Version} 버전이 있습니다.\n현재 버전: {current}\n\n지금 업데이트할까요?\n기존 프로그램을 교체하고 이름·크기·위치 설정은 유지합니다.",
                "AIpet 업데이트", MessageBoxButton.YesNo, MessageBoxImage.Information, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                preferences = preferences.Defer(update.Version, DateTimeOffset.UtcNow); Save();
                return;
            }
            await Install(update);
        }
        catch (OperationCanceledException) when (disposed) { }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or System.Net.Http.HttpRequestException or System.Text.Json.JsonException or OperationCanceledException)
        {
            if (!disposed && manual) MessageBox.Show("새 버전을 확인할 수 없습니다. 인터넷 연결을 확인한 뒤 다시 시도해 주세요.", "AIpet 업데이트");
        }
        finally { busy = false; }
    }
    private async Task Install(AppUpdate update)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var label = new TextBlock { Text = "새 버전을 다운로드하고 있습니다.", Margin = new Thickness(0, 0, 0, 16) };
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 16, Margin = new Thickness(0, 0, 0, 16) };
        var cancel = new Button { Content = "취소", Width = 80, HorizontalAlignment = HorizontalAlignment.Right };
        cancel.Click += (_, _) => cancellation.Cancel();
        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(label); content.Children.Add(progress); content.Children.Add(cancel);
        var window = new Window { Title = "AIpet 업데이트", Width = 420, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = content };
        var installing = false;
        window.Closing += (_, _) => { if (!installing) cancellation.Cancel(); };
        progressWindow = window; window.Show();
        string? installer = null;
        try
        {
            installer = await client.Download(update, Path.Combine(DiagnosticOutput.DefaultRoot, "Updates"), new Progress<int>(percent => {
                progress.Value = percent; label.Text = percent == 100 ? "업데이트 파일을 확인하고 있습니다." : $"새 버전을 다운로드하고 있습니다. {percent}%";
            }), cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            var start = new ProcessStartInfo(installer) { UseShellExecute = false };
            foreach (var argument in new[] { "/SILENT", "/SP-", "/NORESTART", "/CLOSEAPPLICATIONS", "/AIPETUPDATE=1" }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("설치 프로그램을 실행할 수 없습니다.");
            installing = true; window.Close();
            quit();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or System.Net.Http.HttpRequestException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            if (!disposed) MessageBox.Show("업데이트 파일을 받거나 확인하지 못했습니다. 기존 프로그램은 유지됩니다. 잠시 후 다시 시도해 주세요.", "AIpet 업데이트");
        }
        finally
        {
            progressWindow = null;
            if (window.IsVisible) window.Close();
            if (!installing && installer is not null)
                try { Directory.Delete(Path.GetDirectoryName(installer)!, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; timer.Stop(); timer.Tick -= OnTick; lifetime.Cancel();
        progressWindow?.Close(); client.Dispose();
    }
}
