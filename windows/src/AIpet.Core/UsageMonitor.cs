namespace AIpet.Core;

// One background refresh at a time; immutable snapshots cross the UI boundary.
public sealed class UsageMonitor(IUsageSources sources)
{
    private readonly object gate = new();
    private UsageState state = new();
    private UsageRetry retry = new();
    private bool paused, enabled, busy;
    private string? codexPath;
    private int generation;
    private DateTimeOffset codexDue, historyDue, directDue;
    public UsageState State { get { lock (gate) return state; } }
    public void Configure(bool pause, bool directEnabled, string? path = null)
    {
        lock (gate)
        {
            if (paused != pause || enabled != directEnabled || codexPath != path) generation++;
            if (enabled != directEnabled)
            {
                retry = new(); directDue = default;
                state = state with { DirectEnabled = directEnabled, Direct = null, DirectStatus = DirectUsageStatus.Disabled, DirectSuccess = null };
            }
            if (codexPath != path) codexDue = default;
            paused = pause; enabled = directEnabled; codexPath = path;
        }
    }
    public async Task<bool> Refresh(DateTimeOffset now)
    {
        int version; string? path; UsageState next; bool codex, history, direct;
        lock (gate)
        {
            if (paused || busy) return false;
            busy = true; version = generation; path = codexPath; next = state;
            codex = now >= codexDue; history = now >= historyDue; direct = enabled && !retry.Denied && now >= directDue;
        }
        bool Allowed() { lock (gate) return !paused && enabled && generation == version; }
        bool Current() { lock (gate) return !paused && generation == version; }
        try
        {
            await Task.Run(async () => {
                if (codex && Current())
                {
                    var result = await sources.Codex(path).ConfigureAwait(false);
                    next = next with { Codex = UsageState.Retaining(next.Codex, result.Value), CodexReason = result.Reason };
                }
                if (history && Current()) next = next with { History = UsageState.Retaining(next.History, sources.History()) };
                if (direct && Allowed())
                {
                    var result = await sources.Direct(Allowed).ConfigureAwait(false);
                    next = next with { Direct = UsageState.Retaining(next.Direct, result.Value), DirectStatus = result.Status,
                        DirectSuccess = result.Status == DirectUsageStatus.Success ? now : next.DirectSuccess };
                }
            }).ConfigureAwait(false);
            lock (gate)
            {
                if (paused || generation != version) return false;
                state = next;
                if (codex) codexDue = now.AddSeconds(300);
                if (history) historyDue = now.AddSeconds(30);
                if (direct) directDue = now.AddSeconds(retry.Interval(next.DirectStatus));
                return true;
            }
        }
        // A source implementation must never leak credentials or account details into an unhandled exception.
        catch { return false; }
        finally { lock (gate) busy = false; }
    }
}
