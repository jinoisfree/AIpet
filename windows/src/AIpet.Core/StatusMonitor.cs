using System.Text.Json;
namespace AIpet.Core;

public sealed class StatusMonitor
{
    public ServiceUsage? LatestCodexUsage { get; private set; }
    public string CodexRoot { get; }
    public string ClaudeRoot { get; }
    public string EventRoot { get; }
    private readonly Dictionary<string, TranscriptTail> tails = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WorkItem> records = new(StringComparer.Ordinal);
    private DateTimeOffset lastDiscovery = DateTimeOffset.MinValue;
    private readonly Dictionary<Provider, int> counts = [];
    private readonly HashSet<Provider> discoveryErrors = [], receivedHooks = [];
    private bool capped;
    public StatusMonitor(string? codexRoot = null, string? claudeRoot = null, string? eventRoot = null)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        CodexRoot = codexRoot ?? Path.Combine(Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(home, ".codex"), "sessions");
        ClaudeRoot = claudeRoot ?? Path.Combine(Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Path.Combine(home, ".claude"), "projects");
        EventRoot = eventRoot ?? HookBridge.DefaultEventRoot;
    }
    public Snapshot Poll(DateTimeOffset? now = null, bool forceDiscovery = false)
    {
        var date = now ?? DateTimeOffset.UtcNow;
        if (forceDiscovery || (date - lastDiscovery).TotalSeconds >= 15) { Discover(date); lastDiscovery = date; }
        foreach (var tail in tails.Values) foreach (var item in tail.Read()) Merge(item);
        LatestCodexUsage = tails.Values.Where(t => t.LastError is null).Select(t => t.Decoder.LatestUsage).OfType<ServiceUsage>().OrderByDescending(u => u.ObservedAt).FirstOrDefault();
        ReadHookEvents(date);
        foreach (var id in records.Where(p => (date - p.Value.UpdatedAt).TotalSeconds > 86400).Select(p => p.Key).ToArray()) records.Remove(id);
        var tasks = records.Values.Select(i => i.Effective(date)).OrderByDescending(i => i.State.Rank()).ThenByDescending(i => i.UpdatedAt).ThenBy(i => i.Id, StringComparer.Ordinal).ToArray();
        var connections = new[] { Provider.Codex, Provider.Claude }.Select(provider => {
            var root = provider == Provider.Codex ? CodexRoot : ClaudeRoot;
            var readable = Directory.Exists(root) && !discoveryErrors.Contains(provider);
            var errors = tails.Values.Count(t => t.Decoder.Provider == provider && t.LastError is not null);
            var count = counts.GetValueOrDefault(provider);
            var hookStatus = receivedHooks.Contains(provider) ? "공식 훅 수신 확인" : "승인 알림: 훅 확인 필요";
            return new ConnectionInfo(provider, readable && errors == 0, count, !readable ? "기록 폴더에 접근할 수 없음" : errors > 0 ? "일부 기록 읽기 실패" : $"최근 기록 {count}개 · {hookStatus}");
        }).ToArray();
        var candidates = tails.Values.Where(t => t.LastError is null && t.Decoder.Last is { } item && records.TryGetValue(item.Id, out var current) && current.Effective(date).State == WorkState.Running && (current.TurnID.Length == 0 || current.TurnID == item.TurnID)).Select(t => t.Decoder.ScreenFocus(date)).OfType<ScreenFocus>();
        var focus = ScreenFocus.Select(candidates, date);
        return new(date, tasks.Take(200).ToArray(), connections, capped || tasks.Length > 200 ? "최근 세션이 많아 최대 200개까지 표시합니다." : null, focus?.ExpiresAt, focus);
    }
    public void Merge(WorkItem item)
    {
        if (item.SessionID.Length == 0) return;
        if (records.TryGetValue(item.Id, out var previous))
        {
            if (item.UpdatedAt < previous.UpdatedAt) return;
            if (previous.Source == "공식 훅" && item.Source != "공식 훅" && previous.State == WorkState.Waiting && item.Event is "activity" or "assistant_activity") return;
        }
        records[item.Id] = item;
    }
    private void Discover(DateTimeOffset now)
    {
        discoveryErrors.Clear(); capped = false;
        var active = new HashSet<string>(StringComparer.Ordinal);
        foreach (var provider in new[] { Provider.Codex, Provider.Claude })
        {
            var candidates = new List<(string Path, DateTimeOffset Date)>();
            void Walk(string root)
            {
                try
                {
                    foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos())
                    {
                        if (entry.Name.StartsWith('.') || (entry.Attributes & (FileAttributes.Hidden | FileAttributes.ReparsePoint)) != 0) continue;
                        if (entry is DirectoryInfo) { if (entry.Name != "subagents") Walk(entry.FullName); continue; }
                        if (entry.Extension == ".jsonl" && (now - new DateTimeOffset(entry.LastWriteTimeUtc)).TotalSeconds < 86400) candidates.Add((entry.FullName, entry.LastWriteTimeUtc));
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { discoveryErrors.Add(provider); }
            }
            Walk(provider == Provider.Codex ? CodexRoot : ClaudeRoot);
            if (candidates.Count > 200) capped = true;
            counts[provider] = Math.Min(candidates.Count, 200);
            foreach (var candidate in candidates.OrderByDescending(c => c.Date).Take(200))
            { active.Add(candidate.Path); if (!tails.ContainsKey(candidate.Path)) tails[candidate.Path] = new(candidate.Path, provider); }
        }
        foreach (var path in tails.Keys.Where(p => !active.Contains(p)).ToArray()) tails.Remove(path);
    }
    private void ReadHookEvents(DateTimeOffset now)
    {
        receivedHooks.Clear();
        try
        {
            foreach (var file in new DirectoryInfo(EventRoot).EnumerateFiles("*.json"))
            {
                try
                {
                    if ((file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length > 8192) continue;
                    using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var data = new byte[8193]; var length = stream.ReadAtLeast(data, data.Length, throwOnEndOfStream: false);
                    if (length > 8192) continue;
                    using var doc = JsonDocument.Parse(data.AsMemory(0, length));
                    if (HookBridge.DecodeEvent(doc.RootElement) is not { } item || (now - item.UpdatedAt).TotalSeconds >= 86400) continue;
                    receivedHooks.Add(item.Provider); Merge(item);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
