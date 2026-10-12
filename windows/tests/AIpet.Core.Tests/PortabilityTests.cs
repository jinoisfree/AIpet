using System.Text;
using AIpet.Core;
namespace AIpet.Core.Tests;

public sealed class PortabilityTests : TestSupport
{
    [Fact]
    public void MetadataUsesIsoDatesAndHandlesWindowsNewlines()
    {
        Assert.Null(Dates.Parse("10/11/2026"));
        Assert.Null(Dates.Parse("2026-10-11"));
        Assert.Equal(Dates.Parse("2026-10-11T00:00:00Z"), Dates.Parse("2026-10-11T09:00:00.000+09:00"));
        Assert.Equal(new ScreenTarget("AIpet", "Work"), ScreenTarget.FromOutput(J("Window: \"Work\", App: AIpet.\r\nPRIVATE BODY")));
    }
    [Theory]
    [InlineData(@"C:\Users\a\proj", "proj")]
    [InlineData(@"C:\Users\a\proj\", "proj")]
    [InlineData(@"\\server\share\proj", "proj")]
    [InlineData("/work/proj/", "proj")]
    [InlineData("", "")]
    public void CwdSeparators(string cwd, string expected)
    {
        Assert.Equal(expected, TextElements.ProjectName(cwd));
        var now = DateTimeOffset.UtcNow.AddSeconds(-1);
        var codex = new TranscriptDecoder(Provider.Codex, "s");
        codex.Consume(Record("session_meta", new { cwd }, now)); Assert.Equal(expected, codex.Project);
        var claude = new TranscriptDecoder(Provider.Claude, "s");
        claude.Consume(J(new { type = "user", timestamp = now.ToString("O"), cwd, message = new { content = "PRIVATE" } }));
        Assert.Equal(expected, claude.Project);
    }
    [Fact]
    public void IdentityUsesNfcAndGraphemesAndExactKorean()
    {
        Assert.Equal("가 나", PetIdentity.Normalized(" \t가\n나  "));
        var family = "👨‍👩‍👧‍👦";
        Assert.Null(PetIdentity.ValidationMessage(string.Concat(Enumerable.Repeat(family, 20))));
        Assert.Equal("이름은 20자까지 입력할 수 있어요.", PetIdentity.ValidationMessage(string.Concat(Enumerable.Repeat(family, 21))));
        Assert.Equal("펫 이름을 입력해 주세요.", PetIdentity.ValidationMessage(" \n"));
        Assert.Equal("표시할 수 있는 문자로 이름을 입력해 주세요.", PetIdentity.ValidationMessage("a\0"));
        Assert.Equal("태식이가 지켜보고 있어요", new PetIdentity("태식이").IdleHeadline);
        Assert.Equal("곰과 함께", new PetIdentity("곰").Greeting);
        Assert.Equal("AI가 일하는 동안, 곰이 살펴볼게요.", new PetIdentity("곰").DashboardIntroduction);
        var settings = new Dictionary<string, string>(); Assert.Equal(PetIdentity.DefaultName, PetIdentity.Load(settings).Name);
        new PetIdentity("곰").Save(settings); Assert.Equal("곰", PetIdentity.Load(settings).Name);
        settings[PetIdentity.SettingsKey] = ""; Assert.Equal(PetIdentity.DefaultName, PetIdentity.Load(settings).Name);
    }
    [Fact]
    public void TailBoundsHeaderAndTruncation()
    {
        var now = DateTimeOffset.UtcNow.AddSeconds(-1); var path = Path.Combine(Root, "large.jsonl");
        var header = Record("session_meta", new { id = "child", parent_thread_id = "parent" }, now);
        var work = Record("event_msg", new { type = "task_started" }, now);
        File.WriteAllText(path, header.GetRawText() + "\n" + new string('x', TranscriptTail.MaxBytes + 10) + "\n" + work.GetRawText() + "\n");
        var tail = new TranscriptTail(path, Provider.Codex);
        Assert.Empty(tail.Read()); Assert.True(tail.Limited); Assert.True(tail.Decoder.Excluded);
        File.WriteAllBytes(path, Line(work)); Assert.Single(tail.Read()); Assert.False(tail.Decoder.Excluded);
        File.WriteAllText(path, new string('x', TranscriptTail.MaxBytes)); tail.Read();
        File.AppendAllText(path, new string('x', 100)); Assert.Empty(tail.Read());
        File.AppendAllText(path, "\n" + work.GetRawText() + "\n"); Assert.Single(tail.Read());
    }
    [Fact]
    public void SameLengthReplacementUsesCreationTime()
    {
        var path = Path.Combine(Root, "rotate.jsonl"); var now = DateTimeOffset.UtcNow.AddSeconds(-1);
        var first = Line(Record("event_msg", new { type = "task_started", turn_id = "a" }, now));
        File.WriteAllBytes(path, first); var tail = new TranscriptTail(path, Provider.Codex); Assert.Equal("a", Assert.Single(tail.Read()).TurnID);
        var second = Line(Record("event_msg", new { type = "task_started", turn_id = "b" }, now)); Assert.Equal(first.Length, second.Length);
        var temp = path + ".tmp"; File.WriteAllBytes(temp, second); File.SetCreationTimeUtc(temp, DateTime.UtcNow.AddMinutes(-1)); File.Move(temp, path, true);
        Assert.Equal("b", Assert.Single(tail.Read()).TurnID);
    }
    [Fact]
    public void MonitorDiscoveryCapSubagentsAndHookPriority()
    {
        var now = DateTimeOffset.UtcNow.AddSeconds(-1); var sessions = Path.Combine(Root, "sessions"); var events = Path.Combine(Root, "events");
        Directory.CreateDirectory(sessions); Directory.CreateDirectory(Path.Combine(sessions, "subagents")); Directory.CreateDirectory(Path.Combine(sessions, ".hidden"));
        var work = Record("event_msg", new { type = "task_started" }, now);
        for (var i = 0; i < 201; i++) File.WriteAllBytes(Path.Combine(sessions, $"s{i:D3}.jsonl"), Line(work));
        File.WriteAllBytes(Path.Combine(sessions, "subagents", "child.jsonl"), Line(work));
        File.WriteAllBytes(Path.Combine(sessions, ".hidden", "hidden.jsonl"), Line(work));
        var monitor = new StatusMonitor(sessions, Path.Combine(Root, "absent"), events); var snapshot = monitor.Poll(now);
        Assert.Equal(200, snapshot.Tasks.Count); Assert.Equal(200, snapshot.Connections[0].FileCount);
        Assert.Equal("최근 세션이 많아 최대 200개까지 표시합니다.", snapshot.Warning);
        Assert.DoesNotContain(snapshot.Tasks, t => t.SessionID is "child" or "hidden");
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("{\"session_id\":\"hook\",\"hook_event_name\":\"PermissionRequest\"}"));
        HookBridge.Run(Provider.Claude, input, events);
        var hookSnapshot = monitor.Poll(now); Assert.Equal(WorkState.Waiting, hookSnapshot.Tasks[0].State);
        var waiting = hookSnapshot.Tasks[0]; monitor.Merge(waiting with { Source = "로컬 기록", Event = "assistant_activity", State = WorkState.Running, UpdatedAt = now.AddSeconds(5) });
        Assert.Equal(WorkState.Waiting, monitor.Poll(now).Tasks[0].State);
        Assert.Empty(monitor.Poll(now.AddDays(2)).Tasks);
    }
    [Fact]
    public void SpriteTransformsKeepGroundAndIndependentScale()
    {
        var basis = new SpriteContentMetrics(new(20, 30, 100, 150), 10000); var reference = new SpriteContentMetrics(new(54, 60, 118, 134), 5000);
        var presentation = PetSpritePresentation.Matching(basis, reference); var bounds = presentation.ContentBounds(basis.Bounds);
        Assert.Equal(reference.Bounds.MaxY, bounds.MaxY, 10); Assert.Equal(reference.Bounds.MidX, bounds.MidX, 10);
        Assert.True(presentation.Scale <= 1);
        var fixedScale = PetSpritePresentation.Matching(basis, reference, 1.5); Assert.Equal(1.5, fixedScale.Scale);
        Assert.Equal(reference.Bounds.MaxY, fixedScale.ContentBounds(basis.Bounds).MaxY, 10);
        var cell = new Rect(0, 0, 192, 208);
        Assert.Equal(PetLayout.VisibleSprite(cell), PetSpritePresentation.InteractionRect(reference.Bounds, cell));
        Assert.Equal(cell, new PetSpritePresentation(1, default).DrawingRect(cell));
    }
}
