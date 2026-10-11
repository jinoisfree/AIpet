using System.Text.Json;
namespace AIpet.Core;
// Retains call IDs, expiry and observed window headers only, never tool bodies.
public sealed class ComputerUseActivity
{
    private readonly Dictionary<string, DateTimeOffset> pending = new(StringComparer.Ordinal);
    private DateTimeOffset lingerUntil = DateTimeOffset.MinValue, targetCallStartedAt = DateTimeOffset.MinValue;
    public ScreenTarget? Target { get; private set; }
    public static bool IsScreenTool(string name) => name is "mcp__cua_repl.js" or "mcp__cua_repl__js" or "computer" or "computer_use" or "mcp__computer__computer" or "mcp__computer.computer" || name.StartsWith("mcp__computer_use__", StringComparison.Ordinal) || name.StartsWith("mcp__computer_use.", StringComparison.Ordinal);
    public void Start(string name, string id, DateTimeOffset at)
    {
        if (!IsScreenTool(name) || id.Length == 0 || (at - DateTimeOffset.UtcNow).TotalSeconds >= 60) return;
        foreach (var key in pending.Where(p => p.Value <= at).Select(p => p.Key).ToArray()) pending.Remove(key);
        if (pending.Count >= 32) return;
        pending[TextElements.Prefix(id, 200)] = at.AddSeconds(60);
    }
    public void Finish(string id, DateTimeOffset at, JsonElement output = default)
    {
        if (!pending.Remove(TextElements.Prefix(id, 200), out var expiry)) return;
        var started = expiry.AddSeconds(-60);
        if (started >= targetCallStartedAt)
        {
            var observation = ScreenTarget.Observe(output);
            if (observation.Kind != ScreenTarget.ObservationKind.Absent) { Target = observation.Target; targetCallStartedAt = started; }
        }
        if (at.AddSeconds(3) > lingerUntil) lingerUntil = at.AddSeconds(3);
    }
    public void Reset() { pending.Clear(); lingerUntil = targetCallStartedAt = DateTimeOffset.MinValue; Target = null; }
    public DateTimeOffset? ExpiresAt(DateTimeOffset now)
    {
        var expiry = pending.Values.Append(lingerUntil).Max();
        return expiry > now ? expiry : null;
    }
}
