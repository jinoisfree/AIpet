using System.Text.Json.Nodes;
using Xunit;
namespace AIpet.Core.Tests;

public class UsageTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000), Reset = Now.AddHours(1);
    private static readonly UsageAccount Account = new("jino@example.com", "Example", true, "org-a");
    private static JsonNode J(string text) => JsonNode.Parse(text)!;
    [Fact]
    public void All83SwiftUsageChecks()
    {
        var count = 0;
        void Check(bool condition) { count++; Assert.True(condition, $"Swift usage check {count}"); }
        var five = new UsageWindow(UsagePeriod.FiveHour, 28, Reset); var week = new UsageWindow(UsagePeriod.Weekly, 52, Reset);
        var value = new ServiceUsage(UsageSource.Codex, Now, [five, week], Account);
        Check(value.Display(Now) == "주 48%");
        value = value with { Windows = [five] }; Check(value.Display(Now) == "72%");
        Check(value.Display(Now.AddHours(1)) == "—");
        value = value with { Source = UsageSource.History }; Check(value.Display(Now.AddMinutes(30)) == "—");
        value = value with { Source = UsageSource.Direct }; Check(value.Valid(Now.AddSeconds(3599)));
        value = value with { Windows = [new(UsagePeriod.FiveHour, 1, Now)] }; Check(value.Display(Now) == "—");
        value = value with { Windows = [new(UsagePeriod.FiveHour, -1, Reset), week] }; Check(value.Display(Now) == "주 48%");
        foreach (var input in new[] { -1d, 101, double.NaN, double.PositiveInfinity }) Check(UsageParser.Percent(JsonValue.Create(input)) is null);
        foreach (var input in new[] { true, false }) Check(UsageParser.Percent(JsonValue.Create(input)) is null);
        Check(UsageParser.Percent(JsonValue.Create("42")) is null);
        Check(UsageParser.Percent(J("0")) == 0 && UsageParser.Percent(J("100")) == 100);
        foreach (var (email, expected) in new[] { ("jino@example.com", "ji***@example.com"), ("ab@example.com", "a***@example.com"), ("a@example.com", "a***@example.com"), ("abc@example.com", "ab***@example.com"), ("missing", "***"), ("@example.com", "***"), ("a@", "***") }) Check(UsageAccount.Mask(email) == expected);
        Check(new UsageAccount("secret@example.com").Masked == "계정 확인 불가");
        var codexAccount = J("""{"result":{"account":{"email":"jino@example.com","planType":"plus"}}}""");
        var window = J("""{"usedPercent":28,"windowDurationMins":300,"resetsAt":1800003600}""");
        var rates = new JsonObject { ["primary"] = window.DeepClone(), ["secondary"] = null };
        var limits = new JsonObject { ["result"] = new JsonObject { ["rateLimitsByLimitId"] = new JsonObject { ["codex"] = rates.DeepClone() } } };
        var parsed = UsageParser.Codex(codexAccount, limits, Now)!;
        Check(parsed.Windows.Count == 1 && parsed.Display(Now) == "72%");
        Check(parsed.Account.Label == "jino@example.com" && parsed.Account.Detail == "plus");
        JsonNode Legacy(JsonNode row) => new JsonObject { ["result"] = new JsonObject { ["rateLimits"] = row.DeepClone() } };
        Check(UsageParser.Codex(null, Legacy(rates), Now)?.Display(Now) == "72%");
        Check(UsageParser.Codex(null, null, Now) is null);
        foreach (var invalid in new JsonNode[] { JsonValue.Create(true)!, JsonValue.Create(101d)!, JsonValue.Create(-1d)!, JsonValue.Create(double.NaN)! })
        {
            var row = window.DeepClone(); row["usedPercent"] = invalid;
            // NaN cannot roundtrip through JSON, so preserve the synthetic node exactly.
            var bad = new JsonObject { ["result"] = new JsonObject { ["rateLimits"] = new JsonObject { ["primary"] = row } } };
            Check(UsageParser.Codex(null, bad, Now) is null);
        }
        var longWindow = window.DeepClone(); longWindow["windowDurationMins"] = 10080d; longWindow["usedPercent"] = 52d;
        Check(UsageParser.Codex(null, Legacy(new JsonObject { ["primary"] = window.DeepClone(), ["secondary"] = longWindow }), Now)?.Display(Now) == "주 48%");
        var config = J("""{"oauthAccount":{"emailAddress":"jino@example.com","organizationName":"Example","organizationUuid":"org-a"}}""");
        Check(UsageParser.ClaudeAccount(config) == Account);
        var sample = J("""{"t":1800000000000,"org":"org-a","u":{"fh":28,"sd":52}}""");
        JsonNode History(params JsonNode[] samples) => new JsonObject { ["samples"] = new JsonArray(samples.Select(s => s.DeepClone()).ToArray()) };
        var history = UsageParser.History(History(sample), Account)!;
        Check(history.Account == Account && history.Display(Now) == "주 48%");
        var wrong = sample.DeepClone(); wrong["org"] = "other";
        Check(UsageParser.History(History(wrong), Account)?.Account.Label == "계정 확인 불가");
        Check(UsageParser.History(History(sample), new())?.Account.Masked == "계정 확인 불가");
        Check(UsageParser.History(History(wrong, sample), Account)?.Account.Verified == true);
        Check(UsageParser.History(History(), Account) is null);
        Check(history.Display(Now.AddHours(58)) == "—");
        var directData = new JsonObject { ["five_hour"] = new JsonObject { ["utilization"] = 28, ["resets_at"] = Reset.ToString("O") }, ["seven_day"] = new JsonObject { ["utilization"] = 52, ["resets_at"] = Reset.ToString("O") } }.ToJsonString();
        var direct = UsageParser.Direct(200, directData, Account, Now);
        Check(direct.Status == DirectUsageStatus.Success && direct.Value?.Display(Now) == "주 48%");
        Check(direct.Value?.Source == UsageSource.Direct && direct.Value?.Account == Account);
        foreach (var (code, status) in new[] { (401, DirectUsageStatus.Unauthorized), (403, DirectUsageStatus.Unauthorized), (429, DirectUsageStatus.Limited), (500, DirectUsageStatus.Connection), (302, DirectUsageStatus.Connection), (0, DirectUsageStatus.Connection) })
        { var result = UsageParser.Direct(code, directData, Account, Now); Check(result.Value is null && result.Status == status); }
        foreach (var invalid in new[] { "true", "-1", "101", "\"NaN\"" })
            Check(UsageParser.Direct(200, "{\"five_hour\":{\"utilization\":" + invalid + ",\"resets_at\":\"" + Reset.ToString("O") + "\"}}", Account, Now).Status == DirectUsageStatus.Connection);
        Check(UsageParser.Direct(200, "broken", Account, Now).Status == DirectUsageStatus.Connection);
        var credentials = UsageParser.Credentials("""{"claudeAiOauth":{"accessToken":"synthetic-token","expiresAt":1800003600000}}""")!;
        Check(credentials.Token == "synthetic-token" && credentials.ExpiresAt == Reset);
        Check(credentials.Status(Now) is null && credentials.Status(Reset) == DirectUsageStatus.Expired);
        Check(UsageParser.Credentials("""{"claudeAiOauth":{"accessToken":"synthetic-token"}}""")?.ExpiresAt is null);
        Check(UsageParser.Credentials("""{"claudeAiOauth":{"expiresAt":42}}""") is null);
        Check(UsageParser.Credentials("broken") is null);
        var retry = new UsageRetry(); foreach (var expected in new[] { 300, 600, 1200, 1200 }) Check(retry.Interval(DirectUsageStatus.Limited) == expected);
        Check(retry.Interval(DirectUsageStatus.Success) == 120 && retry.RateLimits == 0);
        Check(retry.Interval(DirectUsageStatus.Connection) == 120);
        retry.Interval(DirectUsageStatus.Denied); Check(retry.Denied);
        retry = new(); Check(!retry.Denied);
        Check(UsageState.Retaining(parsed, null)?.Display(Now) == "72%");
        Check(UsageState.Retaining(parsed, null)?.Display(Now.AddSeconds(3601)) == "—");
        var state = new UsageState { Codex = parsed, History = history, Direct = direct.Value };
        Check(state.Claude(Now)?.Source == UsageSource.History);
        state = state with { DirectEnabled = true }; Check(state.Claude(Now)?.Source == UsageSource.Direct);
        state = state with { Direct = state.Direct! with { ObservedAt = Now.AddSeconds(-3601) } }; Check(state.Claude(Now)?.Source == UsageSource.History);
        Check(state.Summary(Now) == "Codex 72% · Claude Code 주 48%");
        Check(state.Subtitle(Now, 0, _ => false) == "Codex 72%·Claude Code 주 48%");
        foreach (var elapsed in new[] { 0d, 4.9, 11, 15.9 }) Check(state.Subtitle(Now, elapsed) == state.Summary(Now));
        foreach (var elapsed in new[] { 5d, 7.9 }) Check(state.Subtitle(Now, elapsed) == "Codex ji***@example.com");
        foreach (var elapsed in new[] { 8d, 10.9 }) Check(state.Subtitle(Now, elapsed) == "Claude Code ji***@example.com");
        var diagnostic = JsonData.Sorted(state.Diagnostic(Now), true);
        Check(!diagnostic.Contains("jino@example.com") && !diagnostic.Contains("synthetic-token"));
        Check(UsageParser.Text(UsageParser.Get(UsageParser.Object(diagnostic), "directQuery")) == "실행 안 함");
        Check(state.Reason(history, "", Now.AddSeconds(1801)).Contains("기록이 오래됨"));
        Assert.Equal(83, count);
    }
    [Fact]
    public void MaskUsesTextElementsAndSubtitleIsIsolatedFromTooltip()
    {
        Assert.Equal(28, UsageParser.Percent(JsonValue.Create(28)));
        Assert.Equal(28.5, UsageParser.Percent(JsonValue.Create(28.5m)));
        Assert.Equal("👩‍💻***@example.com", UsageAccount.Mask("👩‍💻a@example.com"));
        var status = PetStatusPresenter.Present(new(Now, [], []), new(PetIdentity.DefaultName), Now, idleSubtitle: "Codex ji***@example.com");
        Assert.Equal("Codex ji***@example.com", status.Subtitle); Assert.DoesNotContain("@", status.ToolTip);
        Assert.DoesNotContain("@", PetStatusPresenter.Present(new(Now, [], []), new(PetIdentity.DefaultName), Now, paused: true, idleSubtitle: "Codex ji***@example.com").Subtitle);
    }
}
