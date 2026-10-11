using AIpet.Core;
namespace AIpet.Core.Tests;
public sealed class PreflightTests : TestSupport
{
    private static PetRenderState Visual => new(new(0, 0), new PetPlacement(new Point(100, 100), 1, new Rect(0, 0, 1000, 800)), 0, "대기", "Codex · Claude Code", WorkState.Idle, false, false, 0);
    [Fact] public void SameVisualDoesNotRedraw()
    {
        Assert.True(Visual.NeedsRedraw(null));
        for (var i = 0; i < 100; i++) Assert.False(Visual.NeedsRedraw(Visual));
    }
    [Fact] public void VisibleChangesRequireRedraw()
    {
        var previous = Visual;
        PetRenderState[] changes = [previous with { Frame = new(0, 1) }, previous with { Frame = new(1, 0) },
            previous with { BubbleOpacity = 0.5 }, previous with { Headline = "작업 중" }, previous with { Subtitle = "project" },
            previous with { State = WorkState.Running }, previous with { Paused = true }, previous with { ReducedMotion = true },
            previous with { Placement = new(new Point(200, 100), 1, new(0, 0, 1000, 800)) }];
        foreach (var next in changes) Assert.True(next.NeedsRedraw(previous));
    }
    [Fact] public void PulseOnlyRedrawsAtTwentyHzWhileVisible()
    {
        var previous = -1L; var changes = 0;
        for (var i = 0; i < 1000; i++)
        {
            var time = i / 1000.0;
            Assert.Equal(0, PetRenderState.GetPulseStep(time, 0, WorkState.Running, false, false));
            var step = PetRenderState.GetPulseStep(time, 1, WorkState.Running, false, false);
            if (step != previous) changes++;
            previous = step;
        }
        Assert.Equal(20, changes);
        Assert.Equal(0, PetRenderState.GetPulseStep(1, 1, WorkState.Running, true, false));
        Assert.Equal(0, PetRenderState.GetPulseStep(1, 1, WorkState.Running, false, true));
        Assert.Equal(0, PetRenderState.GetPulseStep(1, 1, WorkState.Idle, false, false));
        Assert.Equal(0.35, (Visual with { State = WorkState.Running, BubbleOpacity = 1, PulseStep = 16 }).DotOpacity, 8);
    }
    private static Exception Failure(string message)
    {
        try { throw new InvalidOperationException(message); } catch (Exception e) { return e; }
    }
    [Fact] public void LogAppendsTimestampTypeMessageStackWithoutContext()
    {
        var log = new ErrorLog(Path.Combine(Root, "nested", "error.log"));
        var error = Failure("test failure"); error.Data["transcript"] = "PRIVATE_BODY";
        var now = new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
        log.Append(error, now); log.Append(error, now);
        var text = File.ReadAllText(log.FilePath);
        Assert.Equal(2, text.Split("test failure").Length - 1);
        Assert.Contains("2026-10-12T00:00:00", text); Assert.Contains("System.InvalidOperationException", text); Assert.Contains(nameof(Failure), text);
        Assert.DoesNotContain("PRIVATE_BODY", text);
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIpet", "error.log"), new ErrorLog().FilePath);
    }
    [Fact] public void LogRotatesBeforeExceedingLimit()
    {
        var log = new ErrorLog(Path.Combine(Root, "error.log"));
        File.WriteAllBytes(log.FilePath, new byte[ErrorLog.MaxBytes + 1]);
        log.Append(Failure("new entry")); Assert.StartsWith("[", File.ReadAllText(log.FilePath));
        File.WriteAllText(log.FilePath, new string('x', ErrorLog.MaxBytes - 1));
        log.Append(Failure("replacement")); Assert.DoesNotContain("xxx", File.ReadAllText(log.FilePath));
        Assert.True(new FileInfo(log.FilePath).Length <= ErrorLog.MaxBytes);
    }
    [Fact] public void OversizedExceptionCannotCreateUnboundedLog()
    {
        var log = new ErrorLog(Path.Combine(Root, "error.log"));
        log.Append(new Exception(new string('가', ErrorLog.MaxBytes)));
        Assert.InRange(new FileInfo(log.FilePath).Length, 1, ErrorLog.MaxBytes);
    }
    [Fact] public void LoggingFailureIsIgnored()
    {
        var log = new ErrorLog(Root); // A directory cannot be opened as the log file.
        Assert.Null(Xunit.Record.Exception(() => log.Append(Failure("test"))));
    }
    [Fact] public void TenConsecutiveMatchingExceptionsStopAndDifferentOneResets()
    {
        var guard = new RepeatedExceptionGuard(); var failure = Failure("one");
        for (var i = 0; i < 9; i++) Assert.False(guard.ShouldStop(failure));
        Assert.False(guard.ShouldStop(Failure("different")));
        for (var i = 0; i < 9; i++) Assert.False(guard.ShouldStop(failure));
        Assert.True(guard.ShouldStop(failure));
    }
}
