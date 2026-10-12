using System.Text;
using System.Text.Json;
using AIpet.Core;
namespace AIpet.Core.Tests;

public sealed class WindowGuardTests : TestSupport
{
    [Fact] public void IntendedHiddenNeverRecovers()
    {
        foreach (var visible in new[] { false, true })
        foreach (var minimized in new[] { false, true })
        foreach (var topmost in new[] { false, true })
        foreach (var cloaked in new[] { false, true })
            Assert.Equal(WindowRecovery.None, WindowGuard.Decide(false, visible, minimized, topmost, cloaked));
    }
    [Theory]
    [InlineData(false, false, true, WindowRecovery.Show)]
    [InlineData(true, true, true, WindowRecovery.Show)]
    [InlineData(true, false, false, WindowRecovery.RestoreTopmost)]
    [InlineData(false, true, false, WindowRecovery.Show | WindowRecovery.RestoreTopmost)]
    [InlineData(true, false, true, WindowRecovery.None)]
    public void VisibleIntentRecoversOnlyRequiredProperties(bool visible, bool minimized, bool topmost, WindowRecovery expected)
        => Assert.Equal(expected, WindowGuard.Decide(true, visible, minimized, topmost, false));
    [Fact] public void CloakedOnlyLogsEvenWhenHiddenMinimizedAndNotTopmost()
        => Assert.Equal(WindowRecovery.LogCloaked, WindowGuard.Decide(true, false, true, false, true));
    [Fact] public void WindowLogRateLimitsEachEventAndRetainsOnlyMetadata()
    {
        var log = new WindowLog(Path.Combine(Root, "window.log"));
        var now = DateTimeOffset.UtcNow;
        log.Append("WM_SHOWWINDOW", "wParam=0x0", "Codex", now);
        log.Append("WM_SHOWWINDOW", "wParam=0x1", "other", now.AddMilliseconds(999));
        log.Append("WM_CLOSE", "wParam=0x0", now: now);
        log.Append("WM_SHOWWINDOW", "wParam=0x0", now: now.AddMilliseconds(999));
        log.Append("WM_SHOWWINDOW", "wParam=0x1", now: now.AddSeconds(1));
        var lines = File.ReadAllLines(log.FilePath); Assert.Equal(3, lines.Length);
        using var doc = JsonDocument.Parse(lines[0]);
        Assert.Equal(new[] { "at", "event", "flags", "foregroundProcess" }, doc.RootElement.EnumerateObject().Select(p => p.Name));
        Assert.Equal("Codex", doc.RootElement.String("foregroundProcess"));
        using var missing = JsonDocument.Parse(lines[1]); Assert.False(missing.RootElement.TryGetProperty("foregroundProcess", out _));
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIpet", "window.log"), new WindowLog().FilePath);
    }
    [Fact] public void WindowLogRotatesAndEscapesNewlines()
    {
        var log = new WindowLog(Path.Combine(Root, "window.log"));
        File.WriteAllBytes(log.FilePath, new byte[WindowLog.MaxBytes + 1]);
        log.Append("WM_CLOSE", "flags=0\nextra", "process\nname");
        Assert.Single(File.ReadAllLines(log.FilePath)); Assert.True(new FileInfo(log.FilePath).Length <= WindowLog.MaxBytes);
        using var doc = JsonDocument.Parse(File.ReadAllText(log.FilePath)); Assert.Equal("flags=0\nextra", doc.RootElement.String("flags"));
    }
    [Fact] public void SharedWriterBoundsUtf8AndIgnoresFailures()
    {
        var path = Path.Combine(Root, "bounded.log");
        var log = new BoundedLogFile(path, 10); log.Append("가나다라마");
        var bytes = File.ReadAllBytes(path); Assert.True(bytes.Length <= 10);
        Assert.Equal("가나다", new UTF8Encoding(false, true).GetString(bytes));
        log.Append("new"); Assert.Equal("new", File.ReadAllText(path));
        Assert.Null(Xunit.Record.Exception(() => new WindowLog(Root).Append("WM_CLOSE", "flags=0")));
    }
}
