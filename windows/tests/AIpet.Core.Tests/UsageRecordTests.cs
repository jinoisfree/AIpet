using System.Text.Json;
using Xunit;
namespace AIpet.Core.Tests;
public sealed class UsageRecordTests : TestSupport
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static JsonElement Sample(DateTimeOffset? at = null, object? used = null, int minutes = 300) => J(new {
        type = "event_msg", timestamp = (at ?? Now).ToString("O"), payload = new { type = "token_count", rate_limits = new {
            primary = new { used_percent = used ?? 28, window_minutes = minutes, resets_at = Now.AddDays(2).ToUnixTimeSeconds() }, secondary = (object?)null, plan_type = "example" } } });
    [Fact]
    public void ParseMetadataAndRejectInvalidWindows()
    {
        var sample = UsageParser.CodexRecord(Sample())!;
        Assert.Equal(UsageSource.CodexRecord, sample.Source); Assert.Single(sample.Windows); Assert.Equal("72%", sample.Display(Now));
        Assert.Equal("example", sample.Account.Detail); Assert.Equal("계정 확인 불가", sample.Account.Label); Assert.Null(sample.Account.Email);
        Assert.Equal("주 72%", UsageParser.CodexRecord(Sample(minutes: 10080))?.Display(Now));
        Assert.Null(UsageParser.CodexRecord(Sample(minutes: 60)));
        foreach (var used in new object[] { -1, 101, true, false, "NaN", "Infinity" }) Assert.Null(UsageParser.CodexRecord(Sample(used: used)));
        Assert.Null(UsageParser.CodexRecord(J(new { type = "event_msg", payload = Sample().Get("payload") })));
        Assert.Null(UsageParser.CodexRecord(J(new { type = "event_msg", timestamp = "broken", payload = Sample().Get("payload") })));
        Assert.Null(UsageParser.CodexRecord(J(new { type = "response_item", timestamp = Now.ToString("O"), payload = Sample().Get("payload") })));
    }
    [Fact]
    public void RecordLifetimeResetAndSelectionNeverBorrowAccount()
    {
        var record = UsageParser.CodexRecord(Sample())!;
        Assert.True(record.Valid(Now.AddSeconds(86399))); Assert.False(record.Valid(Now.AddHours(24)));
        Assert.Equal("—", (record with { Windows = [new(UsagePeriod.FiveHour, 28, Now)] }).Display(Now));
        var server = record with { Source = UsageSource.Codex, ObservedAt = Now.AddSeconds(-1), Account = new("account@example.com", Verified: true) };
        var state = new UsageState { Codex = server, CodexRecord = record };
        Assert.Equal(UsageSource.CodexRecord, state.CodexUsage?.Source); Assert.Null(state.CodexUsage?.Account.Email);
        Assert.Equal("Codex 계정 확인 불가", state.Subtitle(Now, 5));
        Assert.Equal(UsageSource.Codex, (state with { Codex = server with { ObservedAt = Now.AddSeconds(1) } }).CodexUsage?.Source);
        Assert.DoesNotContain("account@example.com", JsonData.Sorted(state.Diagnostic(Now)));
        Assert.Equal("최근 24시간 Codex 작업 기록 없음", state.CodexFailure(Now.AddHours(24)));
    }
    [Fact]
    public void DecoderDoesNotChangeTaskStateAndReadsExcludedMetadata()
    {
        var decoder = new TranscriptDecoder(Provider.Codex, "test");
        decoder.Consume(J(new { type = "event_msg", timestamp = Now.AddSeconds(-120).ToString("O"), payload = new { type = "task_complete" } }));
        var before = decoder.Last;
        Assert.Null(decoder.Consume(Sample())); Assert.Equal(before, decoder.Last);
        Assert.Equal("72%", decoder.LatestUsage?.Display(Now));
        decoder.Consume(Sample(Now.AddSeconds(-60), 99)); Assert.Equal("72%", decoder.LatestUsage?.Display(Now));
        decoder.Consume(J(new { type = "session_meta", payload = new { id = "child", parent_thread_id = "parent" } }));
        Assert.Null(decoder.Consume(Sample(Now.AddSeconds(1), 42))); Assert.Equal(before, decoder.Last);
        Assert.Equal(42, decoder.LatestUsage!.Windows[0].Used);
    }
    [Fact]
    public void MonitorSelectsLatestAcrossFilesAndPartialTail()
    {
        var old = Path.Combine(Root, "older.jsonl"); var recent = Path.Combine(Root, "newer.jsonl");
        File.WriteAllBytes(old, Line(Sample(Now.AddSeconds(-120), 99)));
        var bytes = Line(Sample(Now.AddSeconds(-60), 28)); File.WriteAllBytes(recent, bytes[..^1]);
        var monitor = new StatusMonitor(Root, Path.Combine(Root, "none"), Path.Combine(Root, "events"));
        Assert.Empty(monitor.Poll(Now, true).Tasks); Assert.Equal(99, monitor.LatestCodexUsage!.Windows[0].Used);
        File.AppendAllText(recent, "\n"); Assert.Empty(monitor.Poll(Now).Tasks); Assert.Equal(28, monitor.LatestCodexUsage!.Windows[0].Used);
        Assert.Equal(28, monitor.LatestCodexUsage.Windows[0].Used); Assert.DoesNotContain("@", JsonData.Sorted(monitor.Poll(Now)));
    }
    [Fact]
    public void FailureReasonsAndExecutableKindAreDiagnosticOnly()
    {
        var state = new UsageState { CodexReason = "Codex 를 찾을 수 없음" };
        Assert.Equal("Codex 를 찾을 수 없음 · Codex 로 작업하면 기록에서 표시됩니다", state.CodexFailure(Now));
        state = state with { CodexReason = "Codex 앱 실행 불가", CodexExecutable = "store" };
        Assert.Equal("Codex 앱 실행 불가 · Codex 로 작업하면 기록에서 표시됩니다", state.CodexFailure(Now));
        Assert.Equal("옵션에서 Claude 사용량 직접 조회를 켜세요", state.ClaudeFailure(Now));
        foreach (var status in new[] { DirectUsageStatus.Missing, DirectUsageStatus.Expired, DirectUsageStatus.Unauthorized, DirectUsageStatus.Limited, DirectUsageStatus.Connection })
            Assert.Equal(status.Label(), (state with { DirectEnabled = true, DirectStatus = status }).ClaudeFailure(Now));
        Assert.Contains("store", JsonData.Sorted(state.Diagnostic(Now)));
    }
    [Fact]
    public async Task StoreSearchOnlyCodexOneLevelAndAccessDenialFallsBack()
    {
        var package = Path.Combine(Root, "package"); var resources = Path.Combine(package, "app", "resources");
        Directory.CreateDirectory(Path.Combine(resources, "bin", "deeper"));
        File.WriteAllText(Path.Combine(resources, "other.exe"), "");
        File.WriteAllText(Path.Combine(resources, "bin", "deeper", "codex.exe"), "");
        Assert.Empty(CodexStorePackages.Executables(() => [package]));
        var nested = Path.Combine(resources, "bin", "codex.exe"); File.WriteAllText(nested, "");
        Assert.Equal([nested], CodexStorePackages.Executables(() => [package]));
        var direct = Path.Combine(resources, "codex.exe"); File.WriteAllText(direct, "");
        Assert.Equal(direct, CodexStorePackages.Executables(() => [package]).First());
        Assert.Empty(CodexStorePackages.Executables(() => throw new UnauthorizedAccessException()));
        var sources = new UsageSources(new(Root, Root, Root, null, ""), runner: _ => throw new CodexStartException(), storeRoots: () => [package]);
        Assert.Equal("Codex 앱 실행 불가", (await sources.Codex(null)).Reason); Assert.Equal("store", sources.CodexExecutable);
        var cannotStart = new UsageSources(new(Root, Root, Root, null, ""), storeRoots: () => [package]);
        Assert.Equal("Codex 앱 실행 불가", (await cannotStart.Codex(null)).Reason);
        if (!OperatingSystem.IsWindows()) Assert.Empty(CodexStorePackages.Paths());
    }
}
