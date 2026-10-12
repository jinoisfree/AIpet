using System.Text.Json;
namespace AIpet.Core;

// Only state metadata is extracted. Prompts, assistant text and arguments are not retained.
public sealed class TranscriptDecoder(Provider provider, string sessionID)
{
    public Provider Provider { get; } = provider;
    public string SessionID { get; private set; } = sessionID;
    public string Project { get; private set; } = "";
    public string TurnID { get; private set; } = "";
    public bool Excluded { get; private set; }
    public WorkItem? Last { get; private set; }
    public ComputerUseActivity ComputerUse { get; } = new();
    public DateTimeOffset? TaskStartedAt { get; private set; }
    public DateTimeOffset? FirstObservedAt { get; private set; }
    public ScreenFocus? ScreenFocus(DateTimeOffset now) => Last?.State == WorkState.Running && FirstObservedAt is { } first && ComputerUse.ExpiresAt(now) is { } expiry
        ? new(Last.Id, TurnID, Provider, TaskStartedAt, first, expiry, ComputerUse.Target) : null;
    public WorkItem? Consume(JsonElement value) => Provider switch { Provider.Codex => Codex(value), Provider.Claude => Claude(value), _ => null };
    private bool Accepts(DateTimeOffset date) => (date - DateTimeOffset.UtcNow).TotalSeconds < 60 && (Last is null || date >= Last.UpdatedAt);
    private WorkItem? Emit(WorkState state, DateTimeOffset date, string eventName)
    {
        if (Excluded || SessionID.Length == 0 || !Accepts(date)) return null;
        FirstObservedAt ??= date;
        if (state != WorkState.Running) ComputerUse.Reset();
        return Last = new(Provider, SessionID, TurnID, Project, state, date, "로컬 기록", eventName);
    }
    public ServiceUsage? LatestUsage { get; private set; }
    private WorkItem? Codex(JsonElement value)
    {
        var kind = value.String("type"); var payload = value.Get("payload");
        if (kind is null || payload.ValueKind != JsonValueKind.Object) return null;
        if (kind == "session_meta")
        {
            SessionID = payload.String("id") ?? SessionID;
            if (payload.String("cwd") is { } cwd) Project = TextElements.ProjectName(cwd);
            Excluded = payload.Get("parent_thread_id").ValueKind != JsonValueKind.Undefined || payload.Get("source").Get("subagent").ValueKind != JsonValueKind.Undefined;
            return null;
        }
        LatestUsage = UsageState.Newest(LatestUsage, UsageParser.CodexRecord(value));
        if (Excluded) return null;
        if (kind == "turn_context")
        {
            if (payload.String("turn_id") is { } incoming && incoming != TurnID)
            { TurnID = incoming; TaskStartedAt = FirstObservedAt = null; ComputerUse.Reset(); Last = null; }
            if (payload.String("cwd") is { } cwd) Project = TextElements.ProjectName(cwd);
            return null;
        }
        if (Dates.Parse(value.String("timestamp")) is not { } date || payload.String("type") is not { } eventName || !Accepts(date)) return null;
        if (kind == "event_msg")
        {
            if (eventName == "task_started")
            {
                ComputerUse.Reset(); TurnID = payload.String("turn_id") ?? ""; TaskStartedAt = FirstObservedAt = date;
                return Emit(WorkState.Running, date, eventName);
            }
            if (eventName is "task_complete" or "turn_aborted" or "error")
            {
                if (payload.String("turn_id") is { } incoming) { if (TurnID.Length > 0 && incoming != TurnID) return null; TurnID = incoming; }
                return Emit(eventName == "task_complete" ? WorkState.Responded : eventName == "turn_aborted" ? WorkState.Interrupted : WorkState.Failed, date, eventName);
            }
        }
        if (kind == "response_item" && (Last is null || Last.State is WorkState.Running or WorkState.Waiting))
        {
            if (eventName is "function_call" or "custom_tool_call")
            {
                var name = payload.String("name") ?? ""; var ns = payload.String("namespace") ?? "";
                ComputerUse.Start(ns.Length == 0 ? name : ns + "." + name, payload.String("call_id") ?? "", date);
                var waiting = name == "request_user_input" || name.EndsWith(".request_user_input", StringComparison.Ordinal);
                return Emit(waiting ? WorkState.Waiting : WorkState.Running, date, waiting ? "request_user_input" : "tool_call");
            }
            if (eventName is "reasoning" or "function_call_output" or "custom_tool_call_output")
            {
                if (eventName != "reasoning") ComputerUse.Finish(payload.String("call_id") ?? "", date, payload.Get("output"));
                return Emit(WorkState.Running, date, "activity");
            }
        }
        return null;
    }
    private WorkItem? Claude(JsonElement value)
    {
        if (value.True("isSidechain")) return null;
        SessionID = value.String("sessionId") ?? SessionID;
        if (value.String("cwd") is { } cwd) Project = TextElements.ProjectName(cwd);
        if (value.String("type") is not { } kind || Dates.Parse(value.String("timestamp")) is not { } date || !Accepts(date)) return null;
        var message = value.Get("message"); var content = message.Get("content").Array();
        if (kind == "user")
        {
            if (content.Any(b => b.String("type") == "tool_result"))
            {
                foreach (var block in content.Where(b => b.String("type") == "tool_result")) ComputerUse.Finish(block.String("tool_use_id") ?? "", date, block.Get("content"));
                return Emit(WorkState.Running, date, "tool_result");
            }
            if (value.True("isMeta") || value.String("userType") == "internal" || value.String("turnOrigin") == "agent") return null;
            if (message.Get("content").ValueKind == JsonValueKind.String || content.Any(b => b.String("type") == "text"))
            {
                ComputerUse.Reset(); TurnID = value.String("promptId") ?? value.String("uuid") ?? ""; TaskStartedAt = FirstObservedAt = date;
                return Emit(WorkState.Running, date, "user_prompt");
            }
        }
        if (kind == "assistant")
        {
            foreach (var block in content.Where(b => b.String("type") == "tool_use")) ComputerUse.Start(block.String("name") ?? "", block.String("id") ?? "", date);
            if (value.True("isApiErrorMessage")) return Emit(WorkState.Failed, date, "api_error");
            if (content.Any(b => b.String("type") == "tool_use" && b.String("name") is "AskUserQuestion" or "ExitPlanMode")) return Emit(WorkState.Waiting, date, "user_input_tool");
            if (message.String("stop_reason") == "end_turn" && content.Any(b => b.String("type") == "text")) return Emit(WorkState.Responded, date, "end_turn");
            return Emit(WorkState.Running, date, "assistant_activity");
        }
        return kind == "system" && value.String("subtype") == "stop_hook_summary" ? Emit(value.True("preventedContinuation") ? WorkState.Running : WorkState.Responded, date, "stop_hook_summary") : null;
    }
}
