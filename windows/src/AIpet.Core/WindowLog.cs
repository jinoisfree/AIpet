using System.Text.Json;
namespace AIpet.Core;

public sealed class WindowLog(string? path = null)
{
    public const int MaxBytes = 256 * 1024;
    private readonly BoundedLogFile file = new(path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AIpet", "window.log"), MaxBytes);
    public string FilePath => file.FilePath;
    private readonly object gate = new();
    private readonly Dictionary<string, DateTimeOffset> lastEvents = new(StringComparer.Ordinal);
    // Only callers' event/flag metadata and a process name, never window titles or transcript data.
    public void Append(string eventName, string flags, string? foregroundProcess = null, DateTimeOffset? now = null)
    {
        try
        {
            var date = now ?? DateTimeOffset.Now;
            static string Limit(string value, int count) => value[..Math.Min(value.Length, count)];
            var name = Limit(eventName, 80);
            lock (gate)
            {
                if (lastEvents.TryGetValue(name, out var previous) && date - previous < TimeSpan.FromSeconds(1)) return;
                if (lastEvents.Count >= 64 && !lastEvents.ContainsKey(name)) lastEvents.Clear();
                lastEvents[name] = date;
                file.Append(JsonSerializer.Serialize(new {
                    at = date, @event = name, flags = Limit(flags, 1024),
                    foregroundProcess = foregroundProcess is null ? null : Limit(foregroundProcess, 200)
                }, JsonData.Options) + "\n");
            }
        }
        catch { }
    }
}
