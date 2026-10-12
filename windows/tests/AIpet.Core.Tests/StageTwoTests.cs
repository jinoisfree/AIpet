using AIpet.Core;
namespace AIpet.Core.Tests;
public sealed class StageTwoTests : TestSupport
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static WorkItem Task(WorkState state, Provider provider = Provider.Codex, double age = 0) => new(provider, "session123", "turn", "project", state, Now.AddSeconds(-age), "로컬 기록", "event");
    private static PetStatus Present(WorkItem[] tasks, bool paused = false, string? selected = null, bool dozing = false) => PetStatusPresenter.Present(new(Now, tasks, []), new("태식이"), Now, paused, selected, dozing);
    [Fact] public void UnchangedTailDoesNotOpenOrAllocateLargeBuffer()
    {
        var path = Path.Combine(Root, "test.jsonl"); File.WriteAllBytes(path, Line(Record("event_msg", new { type = "task_started" }, Now)));
        var tail = new TranscriptTail(path, Provider.Codex); Assert.Single(tail.Read());
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) { Assert.Empty(tail.Read()); Assert.Null(tail.LastError); }
        Assert.True(GC.GetAllocatedBytesForCurrentThread() - allocated < TranscriptTail.MaxBytes);
    }
    [Fact] public void PauseOverridesUrgency()
    {
        var status = Present([Task(WorkState.Waiting)], paused: true);
        Assert.Equal(WorkState.Idle, status.State); Assert.Equal("태식이가 쉬고 있어요", status.Headline); Assert.Null(status.AlertKey);
    }
    [Fact] public void UrgentOverridesPinAndListsRunningProviders()
    {
        var waiting = Task(WorkState.Waiting); var running = Task(WorkState.Running, Provider.Claude);
        var status = Present([waiting, running], selected: running.Id);
        Assert.Equal("Codex · 확인 필요", status.Headline); Assert.Equal("작업 중: Claude Code 1", status.Subtitle); Assert.Equal("task:codex:session123:waiting", status.AlertKey);
        Assert.NotNull(Present([Task(WorkState.Failed, age: 59)]).AlertKey);
        Assert.Null(Present([Task(WorkState.Failed, age: 60)]).AlertKey);
    }
    [Fact] public void PinAndMultipleTasksFollowSwiftPriority()
    {
        var terminal = Task(WorkState.Responded, age: 100); var running = Task(WorkState.Running, Provider.Claude);
        Assert.Equal("Codex · 응답 종료", Present([terminal, running], selected: terminal.Id).Headline);
        var multiple = Present([Task(WorkState.Running), running]);
        Assert.Equal("Codex · Claude Code · 작업 중", multiple.Headline); Assert.Equal("2개 작업 진행 중 · 클릭해서 보기", multiple.Subtitle);
        Assert.Equal("AIpet · 태식이 · 작업 중 2개", multiple.ToolTip);
    }
    [Fact] public void IdleUnknownDozeAndRecentResponse()
    {
        Assert.Equal("태식이가 지켜보고 있어요", Present([]).Headline);
        Assert.Equal("나 자는거 아니다", Present([], dozing: true).Headline);
        Assert.Equal("상태를 확인할 작업이 있어요", Present([Task(WorkState.Unknown)], dozing: true).Headline);
        Assert.Equal(WorkState.Responded, Present([Task(WorkState.Responded, age: 19)]).State);
        Assert.Equal(WorkState.Idle, Present([Task(WorkState.Responded, age: 20)]).State);
    }
    [Fact] public void MetricsThresholdAndBounds()
    {
        var rgba = new byte[4 * 4 * 4]; rgba[(1 * 4 + 1) * 4 + 3] = 64; rgba[(2 * 4 + 3) * 4 + 3] = 255; rgba[3] = 63;
        Assert.Equal(new SpriteContentMetrics(new(1, 1, 3, 2), 2), SpriteContentMetrics.Measure(rgba, 4, 4));
        Assert.Null(SpriteContentMetrics.Measure(new byte[16], 2, 2)); Assert.Null(SpriteContentMetrics.Measure(rgba, 4, 5));
    }
    [Fact] public void SettingsRoundtripCorruptionAndAtomicReplacement()
    {
        var store = new SettingsStore(Path.Combine(Root, "nested", "settings.json")); Assert.Equal(new PetSettings(), store.Load());
        var settings = new PetSettings(" 곰 ", 150, new(-100, 20), true); store.Save(settings);
        Assert.Equal(settings with { PetName = "곰" }, store.Load());
        store.Save(settings with { PetScale = 250 }); Assert.Equal(200, store.Load().PetScale);
        store.Save(settings with { PetScale = 1 }); Assert.Equal(25, store.Load().PetScale);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(store.FilePath)!));
        File.WriteAllText(store.FilePath, "{broken"); Assert.Equal(new PetSettings(), store.Load());
        File.WriteAllText(store.FilePath, "{\"petName\":null}"); Assert.Equal(PetIdentity.DefaultName, store.Load().PetName);
        store.Save(settings with { PetScale = double.NaN, Position = new(double.PositiveInfinity, 0) });
        Assert.Equal(100, store.Load().PetScale); Assert.Null(store.Load().Position);
    }
}
