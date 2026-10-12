using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AIpet.Core;

public enum Provider { Codex, Claude, Other }
public enum WorkState { Idle, Running, Waiting, Responded, Failed, Interrupted, Unknown }
public static class ModelLabels
{
    public static string Name(this Provider provider) => provider switch { Provider.Codex => "Codex", Provider.Claude => "Claude Code", _ => "외부 AI" };
    public static string Label(this WorkState state) => state switch {
        WorkState.Idle => "대기", WorkState.Running => "작업 중", WorkState.Waiting => "확인 필요",
        WorkState.Responded => "응답 종료", WorkState.Failed => "오류", WorkState.Interrupted => "중단됨", _ => "상태 확인 필요" };
    public static int Rank(this WorkState state) => state switch { WorkState.Waiting => 6, WorkState.Failed => 5, WorkState.Running => 4, WorkState.Responded => 3, WorkState.Interrupted => 2, WorkState.Unknown => 1, _ => 0 };
    public static int Row(this WorkState state) => state switch { WorkState.Running => 7, WorkState.Waiting => 6, WorkState.Responded => 3, WorkState.Failed => 5, _ => 0 };
}
public sealed record WorkItem(Provider Provider, string SessionID, string TurnID, string Project, WorkState State, DateTimeOffset UpdatedAt, string Source, string Event)
{
    [JsonIgnore] public string Id => $"{Provider.ToString().ToLowerInvariant()}:{SessionID}";
    [JsonIgnore] public string ShortID => TextElements.Prefix(SessionID, 8);
    public WorkItem Effective(DateTimeOffset now) => State is WorkState.Running or WorkState.Waiting && (now - UpdatedAt).TotalSeconds > 300 ? this with { State = WorkState.Unknown } : this;
}
public sealed record ConnectionInfo(Provider Provider, bool Available, int FileCount, string Detail)
{
    [JsonIgnore] public string Id => Provider.ToString().ToLowerInvariant();
}
public sealed record Snapshot(DateTimeOffset ObservedAt, IReadOnlyList<WorkItem> Tasks, IReadOnlyList<ConnectionInfo> Connections, string? Warning = null, DateTimeOffset? ScreenUseUntil = null, ScreenFocus? ScreenFocus = null);
public static class PetTaskSummary
{
    public static string? RunningHeadline(IEnumerable<WorkItem> tasks)
    {
        var running = tasks.Where(t => t.State == WorkState.Running).ToArray();
        if (running.Length <= 1) return null;
        var providers = Enum.GetValues<Provider>().Where(p => running.Any(t => t.Provider == p)).ToArray();
        var names = string.Join(" · ", providers.Select(p => p.Name()));
        return providers.Length > 1 ? $"{names} · 작업 중" : $"{names} · {running.Length}개 작업 중";
    }
    public static string RunningDetail(IEnumerable<WorkItem> tasks) => string.Join(" · ", Enum.GetValues<Provider>().Select(p => (p, count: tasks.Count(t => t.Provider == p && t.State == WorkState.Running))).Where(v => v.count > 0).Select(v => $"{v.p.Name()} {v.count}"));
}
public static class Dates
{
    public static DateTimeOffset? Parse(string? value) => value is not null &&
        Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:?\d{2})$") &&
        DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date) ? date : null;
}
public static class TextElements
{
    public static int Count(string value) => StringInfo.ParseCombiningCharacters(value).Length;
    public static string Prefix(string value, int length) { var indices = StringInfo.ParseCombiningCharacters(value); return indices.Length <= length ? value : value[..indices[length]]; }
    public static string ProjectName(string cwd) => cwd.TrimEnd('/', '\\').Split('/', '\\').LastOrDefault() ?? "";
}
public static class JsonData
{
    public static readonly JsonSerializerOptions Options = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };
    public static string Sorted(object value, bool indented = false)
    {
        var node = JsonSerializer.SerializeToNode(value, Options);
        static JsonNode? Sort(JsonNode? n) => n switch {
            JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, Sort(p.Value)))),
            JsonArray a => new JsonArray(a.Select(Sort).ToArray()), _ => n?.DeepClone()
        };
        return Sort(node)!.ToJsonString(new JsonSerializerOptions(Options) { WriteIndented = indented, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
    public static JsonElement Get(this JsonElement value, string key) => value.ValueKind == JsonValueKind.Object && value.TryGetProperty(key, out var item) ? item : default;
    public static string? String(this JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    public static string? String(this JsonElement value, string key) => value.Get(key).String();
    public static bool True(this JsonElement value, string key) => value.Get(key).ValueKind == JsonValueKind.True;
    public static JsonElement[] Array(this JsonElement value) => value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [];
}
