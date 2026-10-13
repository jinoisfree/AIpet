using System.Text;
using System.Text.Json;
using AIpet.Core;
namespace AIpet.Core.Tests;
public sealed class VisibilityTests : TestSupport
{
    private sealed class BrokenStream : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("synthetic");
        public override void Write(ReadOnlySpan<byte> buffer) => throw new IOException("synthetic");
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void OutputFailureFallsBackToFile(bool absent)
    {
        var output = new DiagnosticOutput(() => absent ? Stream.Null : new BrokenStream(), root: Root);
        Assert.Equal(0, output.Write("{\"상태\":\"확인 필요\"}", "snapshot.json"));
        var bytes = File.ReadAllBytes(Path.Combine(Root, "snapshot.json"));
        Assert.False(bytes.Take(3).SequenceEqual(new byte[] { 239, 187, 191 }));
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal("확인 필요", json.RootElement.GetProperty("상태").GetString());
    }
    [Fact] public void ExplicitOutputNeverOpensStdoutAndOverwrites()
    {
        var file = Path.Combine(Root, "custom.json"); File.WriteAllText(file, "old");
        var opened = false;
        var output = new DiagnosticOutput(() => { opened = true; throw new IOException(); });
        Assert.Equal(0, output.Write("{\"한글\":1}", "snapshot.json", file));
        Assert.False(opened); Assert.Contains("한글", File.ReadAllText(file));
    }
    [Fact] public void StandardStreamKeepsReadableUtf8()
    {
        var stream = new MemoryStream();
        Assert.Equal(0, new DiagnosticOutput(() => stream).Write("{\"한글\":true}", "snapshot.json"));
        Assert.Equal("{\"한글\":true}\n", Encoding.UTF8.GetString(stream.ToArray()));
    }
    [Fact] public void AllDestinationsFailWithExitCodeOne()
    {
        var output = new DiagnosticOutput(() => throw new IOException(), (_, _) => throw new UnauthorizedAccessException());
        Assert.Equal(1, output.Write("{}", "snapshot.json"));
        Assert.Equal(1, output.Write("{}", "snapshot.json", "explicit.json"));
        Assert.True(CommandEntry.TryRun(["--snapshot", "--output"], out var code)); Assert.Equal(1, code);
        Assert.True(CommandEntry.TryRun(["--hook", "invalid", "--output"], out code)); Assert.Equal(0, code);
    }
    [Theory]
    [InlineData(false, false, false)] [InlineData(true, true, false)]
    [InlineData(true, false, true)] [InlineData(true, null, false)]
    public void DesktopFollowsOnlyKnownDifferentDesktop(bool intended, bool? current, bool expected) =>
        Assert.Equal(expected, VisibilityGuard.FollowDesktop(intended, current));
    [Fact] public void OffscreenHandlesNegativeMonitorsAndDragging()
    {
        Rect[] screens = [new(-1920, -200, 1920, 1080), new(0, 0, 2560, 1400)];
        var window = new Rect(-300, 100, 400, 400); var sprite = new Rect(-250, 200, 100, 100);
        Assert.False(VisibilityGuard.RecoverOffscreen(true, false, window, sprite, screens));
        Assert.True(VisibilityGuard.RecoverOffscreen(true, false, window, new(-50, -100, 100, 100), screens));
        Assert.True(VisibilityGuard.RecoverOffscreen(true, false, new(4000, 0, 300, 300), new(4050, 10, 100, 100), screens));
        Assert.False(VisibilityGuard.RecoverOffscreen(true, true, new(4000, 0, 300, 300), sprite, screens));
        Assert.False(VisibilityGuard.RecoverOffscreen(false, false, window, new(-50, -100, 100, 100), screens));
        Assert.False(VisibilityGuard.RecoverOffscreen(true, false, window, sprite, []));
    }
    [Fact] public void LimitedRecreationRetriesEveryTenSecondsAndManualBypasses()
    {
        var guard = new WindowRecreationGuard();
        for (int i = 0; i < 5; i++) Assert.Equal(WindowRecreation.Recreate, guard.Decide(false, true, false, 0));
        Assert.Equal(WindowRecreation.LimitReached, guard.Decide(false, true, false, 9.99));
        Assert.Equal(WindowRecreation.Recreate, guard.Decide(false, true, false, 10));
        Assert.Equal(WindowRecreation.LimitReached, guard.Decide(false, true, false, 19.99));
        Assert.Equal(WindowRecreation.Recreate, guard.Decide(false, true, false, 20));
        Assert.Equal(WindowRecreation.Recreate, guard.Decide(false, true, false, 20.1, force: true));
        Assert.Equal(WindowRecreation.LimitReached, guard.Decide(false, true, false, 30));
        Assert.Equal(WindowRecreation.None, guard.Decide(false, false, false, 30, force: true));
        Assert.Equal(0, guard.Count(81));
    }
    [Fact] public void DiagnosticContainsOnlyTypedStateAndSanitizedLastFiftyLogs()
    {
        var now = DateTimeOffset.UtcNow;
        var line = JsonSerializer.Serialize(new { at = now, @event = "DESKTOP_FOLLOW", flags = "success=True; hresult=0x0; path=C:\\private; email=someone@example.com", foregroundProcess = "explorer" });
        var json = JsonData.Sorted(VisibilityDiagnostics.Build("1.0.11-preview.2", now, true,
            new(true, true, false, true, 1, false, new(-100, 20, 300, 400)),
            [new(new(-1920, 0, 1920, 1080), new(-1920, 0, 1920, 1040), 1.5)], new(-100, 200), 80, 6, true,
            Enumerable.Repeat(line, 60)), true);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(50, doc.RootElement.GetProperty("windowLog").GetArrayLength());
        Assert.Equal(6, doc.RootElement.GetProperty("recreationsLastMinute").GetInt32());
        Assert.Contains("explorer", json); Assert.Contains("hresult=0x0", json);
        Assert.DoesNotContain("private", json); Assert.DoesNotContain("@", json); Assert.DoesNotContain("someone", json);
    }
}
