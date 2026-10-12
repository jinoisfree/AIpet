using AIpet.Core;
using static AIpet.Core.PetPointerInteraction;
namespace AIpet.Core.Tests;
public sealed class InteractionTests : TestSupport
{
    [Fact]
    public void PointerFocusAndWindowSelection()
    {
        var sprite = new Rect(200, 200, 80, 90); var display = new Rect(0, 0, 1920, 1080); var pointer = new PetPointerInteraction();
        Phase Move(Point point, double time, bool enabled = true, bool reduced = false) => pointer.Update(point, sprite, display, time, enabled, reduced);
        var inside = new Point(230, 230); var outside = new Point(1500, 800);
        Check(Move(outside, 0) == Phase.Inactive, "proximity alone does not arm");
        Check(Move(inside, 1) == Phase.Jumping(0), "entry starts exactly one jump");
        Check(Move(outside, 1.5) == Phase.Jumping(0.5), "jump finishes even when cursor leaves sprite");
        Check(Move(outside, 1 + JumpDuration) == Phase.Looking, "one full cycle then look");
        Check(Move(inside, 2) == Phase.Looking, "reentry while tracking never repeats jump");
        Check(Move(outside, 11.9) == Phase.Looking, "reentry refreshes ten second timer");
        Check(Move(outside, 12) == Phase.Inactive, "tracking expires at ten seconds");
        Check(Move(inside, 13) == Phase.Jumping(0), "fresh interaction can greet again");
        Check(Move(inside, 23) == Phase.Inactive && Move(inside, 24) == Phase.Inactive, "stationary hover never loops or rearms");
        Move(outside, 25);
        Check(Move(inside, 26, reduced: true) == Phase.Looking, "reduce motion skips jump");
        Check(Move(new(-1, 200), 27) == Phase.Inactive, "leaving pet display cancels");
        Check(Move(inside, 28) == Phase.Jumping(0), "new display entry can greet");
        Check(Move(inside, 29, enabled: false) == Phase.Inactive, "drag or pause cancels interaction");
        foreach (var scale in new[] { 0.25, 0.39, 1, 2 })
        {
            var small = new PetPointerInteraction(); var rect = new Rect(-1000, -200, 160 * scale, 160 * scale); var screen = new Rect(-1920, -1080, 1920, 1080);
            Check(small.Update(new(rect.MidX, rect.MidY), rect, screen, 0) == Phase.Jumping(0), "scaled entry on negative monitor");
            Check(small.Update(new(-1800, -900), rect, screen, 1) == Phase.Looking, "dot has no artificial radius at any scale");
        }
        var date = DateTimeOffset.UtcNow; var target = new ScreenTarget("AIpet", "AI 작업");
        ScreenFocus Focus(string id, double? started, double expiry = 60, ScreenTarget? target = null) => new(id, "t1", id == "first" ? Provider.Claude : Provider.Codex, started is { } s ? date.AddSeconds(s) : null, date, date.AddSeconds(expiry), target);
        var first = Focus("first", -100, target: target); var later = Focus("later", -50);
        Check(ScreenFocus.Select([later, first], date)?.TaskID == "first", "overall task start beats screen call order");
        Check(ScreenFocus.Select([first, later], date)?.TaskID == "first", "input ordering is irrelevant");
        Check(ScreenFocus.Select([Focus("first", -100), Focus("later", -50, target: target)], date)?.Target is null, "unknown target never steals another task window");
        Check(ScreenFocus.Select([Focus("first", -100, -1), later], date)?.TaskID == "later", "expired first task hands off");
        Check(ScreenFocus.Select([], date) is null, "no active screen task clears focus");
        Check(ScreenFocus.Select([Focus("b", -100), Focus("a", -100)], date)?.TaskID == "a", "deterministic equal-start tie");
        Check(ScreenFocus.Select([Focus("unknown", null)], date)?.TaskStartedAt is null, "partial transcript does not invent a start time");
        var output = J(new object[] { new { type = "input_text", text = "Window: \"AI 작업\", App: AIpet.\nPRIVATE BODY" }, new { type = "image", data = "PRIVATE IMAGE" } });
        Check(ScreenTarget.FromOutput(output) == target, "observation header preserves dots in app name");
        Check(ScreenTarget.FromOutput(J("PRIVATE BODY")) is null, "arbitrary output is not a target");
        Check(ScreenTarget.FromOutput(J("Window: \"a\", App: A.\nWindow: \"b\", App: B.")) is null, "multi-app observation is ambiguous");
        var bounds = new Rect(-800, 80, 600, 400);
        var a = new ScreenWindow(1, "AIpet", "AI 작업", bounds); var b = new ScreenWindow(2, "AIpet", "옵션", bounds); var hiddenTitle = new ScreenWindow(3, "AIpet", null, bounds);
        Check(ScreenWindowSelection.Select(target, [b, a])?.Id == 1, "exact title beats frontmost window");
        Check(ScreenWindowSelection.Select(target, [b]) is null, "closed target does not switch to another title");
        Check(ScreenWindowSelection.Select(target, [hiddenTitle])?.Id == 3, "unique app window works when macOS omits titles");
        Check(ScreenWindowSelection.Select(target, [hiddenTitle, b]) is null, "missing title and multiple windows stays unknown");
        Check(ScreenWindowSelection.Select(new("AIpet", null), [a, b]) is null, "app alone never guesses among windows");
        var converted = ScreenWindowSelection.AppKitBounds(bounds, 1080);
        Check(converted.MinX == -800 && converted.MinY == 600, "Quartz to AppKit coordinates across displays");
    }
}
