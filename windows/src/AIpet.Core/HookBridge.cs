using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace AIpet.Core;

public static class HookBridge
{
    public const int MaxInputBytes = 1024 * 1024;
    public static string DefaultEventRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIpet", "events");
    public static WorkItem? Item(Provider provider, JsonElement payload, DateTimeOffset? now = null)
    {
        if (payload.String("session_id") is not { Length: > 0 } id || payload.String("hook_event_name") is not { } eventName) return null;
        WorkState state;
        switch (eventName)
        {
            case "UserPromptSubmit": case "PreToolUse": case "PostToolUse": state = WorkState.Running; break;
            case "PermissionRequest": state = WorkState.Waiting; break;
            case "Notification":
                if (payload.String("notification_type") is not ("permission_prompt" or "idle_prompt")) return null;
                state = WorkState.Waiting; break;
            case "Stop": state = payload.Get("background_tasks").Array().Length == 0 && payload.Get("session_crons").Array().Length == 0 ? WorkState.Responded : WorkState.Running; break;
            case "StopFailure": state = WorkState.Failed; break;
            case "Interrupt": case "SessionEnd": state = WorkState.Interrupted; break;
            default: return null;
        }
        return new(provider, TextElements.Prefix(id, 200), TextElements.Prefix(payload.String("turn_id") ?? "", 200), TextElements.ProjectName(payload.String("cwd") ?? ""), state, now ?? DateTimeOffset.UtcNow, "공식 훅", eventName);
    }
    public static WorkItem? DecodeEvent(JsonElement value)
    {
        if (!value.Get("version").TryGetInt32Safe(out var version) || version != 1 ||
            !TryRawEnum(value.String("provider"), out Provider provider) || value.String("sessionID") is not { Length: > 0 } session || TextElements.Count(session) > 200 ||
            !TryRawEnum(value.String("state"), out WorkState state) || Dates.Parse(value.String("updatedAt")) is not { } date || (date - DateTimeOffset.UtcNow).TotalSeconds >= 60) return null;
        return new(provider, session, TextElements.Prefix(value.String("turnID") ?? "", 200), TextElements.Prefix(value.String("project") ?? "", 200), state, date, "공식 훅", TextElements.Prefix(value.String("event") ?? "", 80));
    }
    private static bool TryRawEnum<T>(string? value, out T parsed) where T : struct, Enum => Enum.TryParse(value, true, out parsed) && Enum.IsDefined(parsed) && parsed.ToString().ToLowerInvariant() == value;
    private static bool TryGetInt32Safe(this JsonElement value, out int result) { result = 0; return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out result); }
    public static void Run(Provider provider, Stream input, string? destination = null)
    {
        string? temporary = null;
        try
        {
            var data = new byte[MaxInputBytes]; var count = input.ReadAtLeast(data, data.Length, throwOnEndOfStream: false);
            using var doc = JsonDocument.Parse(data.AsMemory(0, count));
            if (Item(provider, doc.RootElement) is not { } item) return;
            var root = destination ?? DefaultEventRoot;
            Directory.CreateDirectory(root);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var filename = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.Id))).ToLowerInvariant() + ".json";
            var json = JsonData.Sorted(new { version = 1, provider, item.SessionID, item.TurnID, item.Project, item.State, updatedAt = item.UpdatedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'"), item.Event });
            temporary = Path.Combine(root, "." + Guid.NewGuid().ToString("N") + ".tmp");
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var file = new FileStream(temporary, options)) file.Write(Encoding.UTF8.GetBytes(json));
            File.Move(temporary, Path.Combine(root, filename), overwrite: true);
        }
        catch { /* A notification failure must never change or block the agent's task. */ }
        finally { if (temporary is not null) { try { File.Delete(temporary); } catch { } } }
    }
}
