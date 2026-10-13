using System.Text.Json;
using System.Text.RegularExpressions;
namespace AIpet.Core;

public static class VisibilityGuard
{
    public static bool FollowDesktop(bool intended, bool? onCurrent) => intended && onCurrent == false;
    // All rectangles here use physical pixels, consistently downward on Windows.
    public static bool RecoverOffscreen(bool intended, bool dragging, Rect window, Rect sprite, IReadOnlyList<Rect> workAreas) =>
        intended && !dragging && workAreas.Count > 0 &&
        (!workAreas.Any(r => r.Intersects(window)) || !workAreas.Any(r => r.Contains(sprite)));
}
public sealed record MonitorDiagnostic(Rect Bounds, Rect WorkArea, double Scale);
public sealed record PetWindowDiagnostic(bool Exists, bool? Visible = null, bool? Minimized = null,
    bool? Topmost = null, uint? Cloaked = null, bool? OnCurrentDesktop = null, Rect? Pixels = null);
public static class VisibilityDiagnostics
{
    // Export only known event identifiers and numeric/boolean flags, never arbitrary log text.
    private static readonly HashSet<string> Events = new(StringComparer.Ordinal) {
        "WM_CLOSE", "WM_SHOWWINDOW", "WM_DESTROY", "WM_SYSCOMMAND", "WM_WINDOWPOSCHANGING", "WM_WINDOWPOSCHANGED",
        "CLOSING_CANCELLED", "GUARD_CLOAKED", "GUARD_RECOVERY", "PET_CLOSED", "PET_LOST", "RECREATE_LIMIT",
        "RECREATE_SUCCESS", "RECREATE_FAILED", "DESKTOP_FOLLOW", "DESKTOP_ERROR", "OFFSCREEN_RECOVERY"
    };
    public static object Build(string version, DateTimeOffset at, bool intended, PetWindowDiagnostic window,
        IReadOnlyList<MonitorDiagnostic> monitors, Point? position, double scale, int attempts, bool paused, IEnumerable<string> logLines)
    {
        var logs = new List<object>();
        foreach (var line in logLines.TakeLast(50))
        {
            try
            {
                using var doc = JsonDocument.Parse(line); var row = doc.RootElement;
                var name = row.String("event"); if (name is null || !Events.Contains(name)) continue;
                if (!DateTimeOffset.TryParse(row.String("at"), out var time)) continue;
                var flags = Regex.Matches(row.String("flags") ?? "", @"\b([A-Za-z][A-Za-z0-9]*)=(0x[0-9A-Fa-f]+|-?\d+(?:\.\d+)?|True|False|true|false)(?=[,;\s]|$)")
                    .Select(m => m.Value).ToArray();
                var process = row.String("foregroundProcess");
                if (process is not null && !Regex.IsMatch(process, @"\A[\p{L}\p{N} _.-]{1,200}\z")) process = null;
                logs.Add(new { at = time, @event = name, flags, foregroundProcess = process });
            }
            catch { }
        }
        return new { version = Regex.IsMatch(version, @"\A[0-9.]+(?:-preview\.[0-9]+)?\z") ? version : "unknown",
            at, intendedVisible = intended, window, monitors, savedPosition = position, petScale = scale,
            recreationsLastMinute = attempts, paused, windowLog = logs };
    }
}
