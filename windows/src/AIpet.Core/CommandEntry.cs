namespace AIpet.Core;

public static class CommandEntry
{
    // True means a command was handled and no GUI should be started.
    public static bool TryRun(string[] args) => TryRun(args, out _);
    public static bool TryRun(string[] args, out int exitCode, DiagnosticOutput? output = null)
    {
        exitCode = 0;
        var index = Array.IndexOf(args, "--hook");
        if (index >= 0)
        {
            try
            {
                if (index + 1 < args.Length && args[index + 1] is "codex" or "claude")
                {
                    var root = Array.IndexOf(args, "--event-root");
                    HookBridge.Run(args[index + 1] == "codex" ? Provider.Codex : Provider.Claude, Console.OpenStandardInput(),
                        root >= 0 && root + 1 < args.Length ? args[root + 1] : null);
                }
            }
            catch { }
            return true;
        }
        var usage = args.Contains("--usage-snapshot");
        if (!usage && !args.Contains("--snapshot")) return false;
        try
        {
            var destination = Array.IndexOf(args, "--output");
            if (destination >= 0 && (destination + 1 >= args.Length || args[destination + 1].StartsWith("--")))
            { exitCode = 1; return true; }
            var json = usage
                ? JsonData.Sorted(new UsageSources().Snapshot(new SettingsStore().Load().CodexPath).GetAwaiter().GetResult().Diagnostic(DateTimeOffset.UtcNow), true)
                : JsonData.Sorted(new StatusMonitor().Poll(forceDiscovery: true), true);
            exitCode = (output ?? new DiagnosticOutput()).Write(json, usage ? "usage-snapshot.json" : "snapshot.json",
                destination >= 0 ? args[destination + 1] : null);
        }
        catch { exitCode = 1; }
        return true;
    }
}
