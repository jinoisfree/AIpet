using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace AIpet.Core;

public enum UsageSource { Codex, History, Direct }
public enum UsagePeriod { FiveHour, Weekly }
public enum DirectUsageStatus { Disabled, Success, Missing, Expired, Unauthorized, Limited, Connection, Denied }
public static class UsageLabels
{
    public static string Label(this UsageSource source) => source switch { UsageSource.Codex => "Codex 앱 서버", UsageSource.History => "Claude 앱 기록", _ => "직접 조회" };
    public static string Label(this UsagePeriod period) => period == UsagePeriod.FiveHour ? "5시간" : "주간";
    public static string Label(this DirectUsageStatus status) => status switch {
        DirectUsageStatus.Disabled => "실행 안 함", DirectUsageStatus.Success => "성공", DirectUsageStatus.Missing => "로그인 정보 없음",
        DirectUsageStatus.Expired => "로그인 만료", DirectUsageStatus.Unauthorized => "인증 거부", DirectUsageStatus.Limited => "요청 제한",
        DirectUsageStatus.Denied => "로그인 파일 접근 거부", _ => "연결 실패" };
}
public sealed record UsageWindow(UsagePeriod Period, double Used, DateTimeOffset? ResetsAt = null)
{
    public int? Remaining(DateTimeOffset now) => double.IsFinite(Used) && Used is >= 0 and <= 100 && (ResetsAt is null || ResetsAt > now) ? (int)Math.Floor(100 - Used) : null;
}
public sealed record UsageAccount(string? Email = null, string? Detail = null, bool Verified = false, string? OrganizationID = null)
{
    public string Label => Verified ? Email ?? "계정 확인 불가" : "계정 확인 불가";
    public string Masked => Verified && Email is not null ? Mask(Email) : "계정 확인 불가";
    public static string Mask(string email)
    {
        var at = email.IndexOf('@'); if (at <= 0 || at == email.Length - 1) return "***";
        var local = new StringInfo(email[..at]);
        return local.SubstringByTextElements(0, local.LengthInTextElements <= 2 ? 1 : 2) + "***" + email[at..];
    }
}
public sealed record ServiceUsage(UsageSource Source, DateTimeOffset ObservedAt, IReadOnlyList<UsageWindow> Windows, UsageAccount Account)
{
    public bool Valid(DateTimeOffset now) => now >= ObservedAt.AddSeconds(-60) && (now - ObservedAt).TotalSeconds < (Source == UsageSource.History ? 1800 : 3600);
    public string Display(DateTimeOffset now)
    {
        if (!Valid(now)) return "—";
        var least = Windows.Select(w => (w.Period, Value: w.Remaining(now))).Where(w => w.Value.HasValue).OrderBy(w => w.Value).FirstOrDefault();
        return least.Value is { } value ? (least.Period == UsagePeriod.Weekly ? "주 " : "") + value + "%" : "—";
    }
    public string WindowLabel(UsagePeriod period, DateTimeOffset now) => Valid(now) && Windows.FirstOrDefault(w => w.Period == period)?.Remaining(now) is { } value ? value + "%" : "—";
}
public sealed class UsageRetry
{
    public int RateLimits { get; private set; }
    public bool Denied { get; private set; }
    public int Interval(DirectUsageStatus status)
    {
        if (status == DirectUsageStatus.Denied) Denied = true;
        if (status == DirectUsageStatus.Limited) { RateLimits = Math.Min(3, RateLimits + 1); return new[] { 300, 600, 1200 }[RateLimits - 1]; }
        RateLimits = 0; return 120;
    }
}
// Deliberately not a record: generated ToString must never expose a credential.
public sealed class UsageCredentials(string token, DateTimeOffset? expiresAt)
{
    public string Token { get; } = token;
    public DateTimeOffset? ExpiresAt { get; } = expiresAt;
    public DirectUsageStatus? Status(DateTimeOffset now) => ExpiresAt <= now ? DirectUsageStatus.Expired : null;
}
public sealed record UsageState
{
    public ServiceUsage? Codex { get; init; }
    public ServiceUsage? History { get; init; }
    public ServiceUsage? Direct { get; init; }
    public string CodexReason { get; init; } = "조회 대기";
    public string HistoryReason { get; init; } = "기록 없음";
    public DirectUsageStatus DirectStatus { get; init; }
    public DateTimeOffset? DirectSuccess { get; init; }
    public bool DirectEnabled { get; init; }
    public ServiceUsage? Claude(DateTimeOffset now) => DirectEnabled && Direct is { } direct && direct.Display(now) != "—" ? direct : History;
    public static ServiceUsage? Retaining(ServiceUsage? previous, ServiceUsage? success) => success ?? previous;
    public string Summary(DateTimeOffset now, bool compact = false) => $"Codex {Codex?.Display(now) ?? "—"}{(compact ? "·" : " · ")}Claude Code {Claude(now)?.Display(now) ?? "—"}";
    public string Subtitle(DateTimeOffset now, double elapsed, Func<string, bool>? fits = null)
    {
        var phase = Math.Max(0, elapsed) % 11;
        if (phase < 5) { var text = Summary(now); return (fits?.Invoke(text) ?? new StringInfo(text).LengthInTextElements <= 32) ? text : Summary(now, true); }
        return phase < 8 ? $"Codex {Codex?.Account.Masked ?? "계정 확인 불가"}" : $"Claude Code {Claude(now)?.Account.Masked ?? "계정 확인 불가"}";
    }
    public string Reason(ServiceUsage? usage, string fallback, DateTimeOffset now)
    {
        if (usage is null) return fallback;
        if (!usage.Valid(now)) return usage.Source == UsageSource.History ? "기록이 오래됨 · 옵션에서 직접 조회를 켤 수 있음" : "마지막 조회 값의 유효 기간이 지남";
        return usage.Display(now) == "—" ? "사용량 창 없음 또는 초기화 시각 경과" : "";
    }
    public object Diagnostic(DateTimeOffset now)
    {
        Dictionary<string, object> Service(ServiceUsage? usage, string fallback)
        {
            var result = new Dictionary<string, object> { ["display"] = usage?.Display(now) ?? "—", ["account"] = usage?.Account.Masked ?? "계정 확인 불가", ["reason"] = Reason(usage, fallback, now) };
            if (usage is not null)
            {
                result["source"] = usage.Source.Label(); result["observedAt"] = usage.ObservedAt;
                result["windows"] = usage.Windows.Select(w => { var row = new Dictionary<string, object> { ["period"] = w.Period.Label(), ["usedPercent"] = w.Used }; if (w.ResetsAt is { } date) row["resetsAt"] = date; return row; }).ToArray();
            }
            return result;
        }
        return new { codex = Service(Codex, CodexReason), claude = Service(Claude(now), HistoryReason), directQuery = "실행 안 함" };
    }
}
public static class UsageParser
{
    public static JsonNode? Object(string text) { try { return JsonNode.Parse(text) as JsonObject; } catch (JsonException) { return null; } }
    public static JsonNode? Get(JsonNode? node, string key) => (node as JsonObject)?[key];
    public static double? Number(JsonNode? node)
    {
        if (node is not JsonValue value) return null;
        if (value.TryGetValue<double>(out var result)) return double.IsFinite(result) ? result : null;
        if (value.GetValueKind() != JsonValueKind.Number) return null;
        using var document = JsonDocument.Parse(value.ToJsonString());
        return document.RootElement.TryGetDouble(out result) && double.IsFinite(result) ? result : null;
    }
    public static double? Percent(JsonNode? node) => Number(node) is { } number && number is >= 0 and <= 100 ? number : null;
    public static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    private static DateTimeOffset? Epoch(double? seconds)
    {
        if (seconds is not { } value) return null;
        try { return DateTimeOffset.UnixEpoch.AddSeconds(value); } catch (ArgumentOutOfRangeException) { return null; }
    }
    public static ServiceUsage? Codex(JsonNode? accountResponse, JsonNode? limits, DateTimeOffset now)
    {
        var account = Get(Get(accountResponse, "result"), "account"); var result = Get(limits, "result");
        var rate = Get(Get(result, "rateLimitsByLimitId"), "codex") as JsonObject ?? Get(result, "rateLimits") as JsonObject;
        var windows = new List<UsageWindow>();
        foreach (var key in new[] { "primary", "secondary" })
        {
            var row = Get(rate, key); var duration = Number(Get(row, "windowDurationMins"));
            if (Percent(Get(row, "usedPercent")) is { } used && duration is 300 or 10080)
                windows.Add(new(duration == 300 ? UsagePeriod.FiveHour : UsagePeriod.Weekly, used, Epoch(Number(Get(row, "resetsAt")))));
        }
        var email = Text(Get(account, "email"));
        return windows.Count == 0 ? null : new(UsageSource.Codex, now, windows, new(email, Text(Get(account, "planType")), email is not null));
    }
    public static UsageAccount ClaudeAccount(JsonNode? config)
    {
        var account = Get(config, "oauthAccount"); var email = Text(Get(account, "emailAddress"));
        return new(email, Text(Get(account, "organizationName")), email is not null, Text(Get(account, "organizationUuid")));
    }
    public static ServiceUsage? History(JsonNode? root, UsageAccount account)
    {
        var samples = Get(root, "samples") as JsonArray; var sample = samples?.LastOrDefault();
        if (Epoch(Number(Get(sample, "t")) / 1000) is not { } time) return null;
        var values = Get(sample, "u"); var windows = new List<UsageWindow>();
        foreach (var (key, period) in new[] { ("fh", UsagePeriod.FiveHour), ("sd", UsagePeriod.Weekly) })
            if (Percent(Get(values, key)) is { } used) windows.Add(new(period, used));
        var matches = account.OrganizationID is not null && Text(Get(sample, "org")) == account.OrganizationID;
        return windows.Count == 0 ? null : new(UsageSource.History, time, windows, matches ? account : new());
    }
    public static UsageCredentials? Credentials(string text)
    {
        var auth = Get(Object(text), "claudeAiOauth"); var token = Text(Get(auth, "accessToken"));
        return string.IsNullOrEmpty(token) ? null : new(token, Epoch(Number(Get(auth, "expiresAt")) / 1000));
    }
    public static (ServiceUsage? Value, DirectUsageStatus Status) Direct(int status, string data, UsageAccount account, DateTimeOffset now)
    {
        if (status is 401 or 403) return (null, DirectUsageStatus.Unauthorized);
        if (status == 429) return (null, DirectUsageStatus.Limited);
        if (status != 200) return (null, DirectUsageStatus.Connection);
        var root = Object(data); var windows = new List<UsageWindow>();
        foreach (var (key, period) in new[] { ("five_hour", UsagePeriod.FiveHour), ("seven_day", UsagePeriod.Weekly) })
        {
            var row = Get(root, key);
            if (Percent(Get(row, "utilization")) is { } used && Dates.Parse(Text(Get(row, "resets_at"))) is { } reset)
                windows.Add(new(period, used, reset));
        }
        return windows.Count == 0 ? (null, DirectUsageStatus.Connection) : (new(UsageSource.Direct, now, windows, account), DirectUsageStatus.Success);
    }
}
