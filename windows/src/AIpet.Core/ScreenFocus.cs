using System.Text.Json;
using System.Text.RegularExpressions;
namespace AIpet.Core;

public sealed record ScreenTarget(string AppName, string? WindowTitle)
{
    public enum ObservationKind { Absent, Known, Ambiguous }
    public sealed record Observation(ObservationKind Kind, ScreenTarget? Target = null);
    public static ScreenTarget? FromOutput(JsonElement output) => Observe(output).Target;
    public static Observation Observe(JsonElement output)
    {
        var texts = output.String() is { } text ? new[] { text } : output.Array().Take(16).Where(b => b.String("type") is "text" or "input_text").Select(b => b.String("text")).OfType<string>();
        var targets = new HashSet<ScreenTarget>();
        foreach (var content in texts)
        foreach (Match match in Regex.Matches(TextElements.Prefix(content, 16384), "^Window: \"(.*)\", App: (.+)\\.\r?$", RegexOptions.Multiline))
        {
            var app = match.Groups[2].Value; var title = match.Groups[1].Value;
            if (app.Length == 0 || TextElements.Count(app) > 200 || TextElements.Count(title) > 500) continue;
            targets.Add(new(app, title.Length == 0 ? null : title));
        }
        return targets.Count switch { 1 => new(ObservationKind.Known, targets.First()), 0 => new(ObservationKind.Absent), _ => new(ObservationKind.Ambiguous) };
    }
}
public sealed record ScreenFocus(string TaskID, string TurnID, Provider Provider, DateTimeOffset? TaskStartedAt, DateTimeOffset FirstObservedAt, DateTimeOffset ExpiresAt, ScreenTarget? Target)
{
    public static ScreenFocus? Select(IEnumerable<ScreenFocus> candidates, DateTimeOffset now) => candidates.Where(c => c.ExpiresAt > now).OrderBy(c => c.TaskStartedAt ?? c.FirstObservedAt).ThenBy(c => c.TaskID + ":" + c.TurnID, StringComparer.Ordinal).FirstOrDefault();
}
public sealed record ScreenWindow(uint Id, string AppName, string? Title, Rect Bounds);
public static class ScreenWindowSelection
{
    public static ScreenWindow? Select(ScreenTarget target, IEnumerable<ScreenWindow> windows)
    {
        var matches = windows.Where(w => w.AppName == target.AppName).ToArray();
        if (target.WindowTitle is { } title)
        {
            var exact = matches.Where(w => w.Title == title).ToArray();
            if (exact.Length == 1) return exact[0];
            if (exact.Length == 0 && matches.Length == 1 && matches[0].Title is null) return matches[0];
            return null;
        }
        return matches.Length == 1 ? matches[0] : null;
    }
    public static Rect AppKitBounds(Rect quartz, double primaryTop) => new(quartz.MinX, primaryTop - quartz.MaxY, quartz.Width, quartz.Height);
}
