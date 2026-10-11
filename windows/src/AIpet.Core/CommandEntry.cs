using System.Text;
namespace AIpet.Core;

public static class CommandEntry
{
    // True means a command was handled and no GUI should be started.
    public static bool TryRun(string[] args)
    {
        var index = Array.IndexOf(args, "--hook");
        if (index >= 0)
        {
            if (index + 1 < args.Length && args[index + 1] is "codex" or "claude")
            {
                var root = Array.IndexOf(args, "--event-root");
                HookBridge.Run(args[index + 1] == "codex" ? Provider.Codex : Provider.Claude, Console.OpenStandardInput(),
                    root >= 0 && root + 1 < args.Length ? args[root + 1] : null);
            }
            return true;
        }
        if (!args.Contains("--snapshot")) return false;
        Console.OutputEncoding = new UTF8Encoding(false);
        Console.WriteLine(JsonData.Sorted(new StatusMonitor().Poll(forceDiscovery: true), true));
        return true;
    }
}
