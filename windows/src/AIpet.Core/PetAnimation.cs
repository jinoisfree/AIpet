namespace AIpet.Core;
public enum PetAnimation { Idle, Right, Left, Waving, Jumping, Failed, Waiting, Running, Review }
public static class PetAnimations
{
    public static PetAnimation FromState(WorkState state) => (PetAnimation)state.Row();
    public static string Name(this PetAnimation animation) => new[] { "쉬는 중", "오른쪽 이동", "왼쪽 이동", "손 흔들기", "점프", "오류", "승인 대기 · 폰 흔들기", "작업 중", "검토" }[(int)animation];
    public static bool RepeatsWhileActive(this PetAnimation animation) => animation == PetAnimation.Waiting;
    public static bool AllowsLook(this PetAnimation animation) => animation is PetAnimation.Idle or PetAnimation.Running or PetAnimation.Waving;
}
public readonly record struct PetAnimationFrame(int Row, int Column);
public static class PetAnimationTimeline
{
    private static readonly double[] idleDurations = [0.280, 0.110, 0.110, 0.140, 0.140, 0.320];
    public static PetAnimationFrame Frame(PetAnimation animation, double elapsed, bool reducedMotion = false, bool loop = false)
    {
        if (reducedMotion) return new((int)animation, 0);
        double[] durations = animation switch {
            PetAnimation.Idle => idleDurations.Select(d => d * 6).ToArray(),
            PetAnimation.Right or PetAnimation.Left => [.. Enumerable.Repeat(0.120, 7), 0.220],
            PetAnimation.Running => [.. Enumerable.Repeat(0.120, 5), 0.220],
            PetAnimation.Waving => [.. Enumerable.Repeat(0.140, 3), 0.280],
            PetAnimation.Jumping => [.. Enumerable.Repeat(0.140, 4), 0.280],
            PetAnimation.Failed => [.. Enumerable.Repeat(0.140, 7), 0.240],
            PetAnimation.Waiting => [.. Enumerable.Repeat(0.120, 8)],
            _ => [.. Enumerable.Repeat(0.150, 5), 0.280]
        };
        var cycle = durations.Sum(); var time = Math.Max(0, double.IsFinite(elapsed) ? elapsed : 0);
        if (animation != PetAnimation.Idle && !animation.RepeatsWhileActive() && !loop && time >= cycle * 3) return Frame(PetAnimation.Idle, time - cycle * 3);
        var remaining = time % cycle;
        for (var column = 0; column < durations.Length; column++) { if (remaining < durations[column]) return new((int)animation, column); remaining -= durations[column]; }
        return new((int)animation, durations.Length - 1);
    }
    public static PetAnimationFrame? LookFrame(Point origin, Point target, PetAnimationFrame? previous = null)
    {
        var dx = target.X - origin.X; var dy = target.Y - origin.Y;
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) return null;
        var previousLook = previous is { Row: >= 9 and <= 10, Column: >= 0 and < 8 } ? previous : null;
        if (Math.Sqrt(dx * dx + dy * dy) <= 1) return previousLook;
        var angle = (Math.Atan2(dx, dy) * 180 / Math.PI + 360) % 360;
        if (previousLook is { } look)
        {
            var center = ((look.Row - 9) * 8 + look.Column) * 22.5;
            var distance = (angle - center + 540) % 360 - 180;
            if (Math.Abs(distance) <= 13.25) return look;
        }
        var direction = (int)Math.Round(angle / 22.5, MidpointRounding.AwayFromZero) % 16;
        return new(9 + direction / 8, direction % 8);
    }
    public static PetAnimation DragAnimation(double horizontalDelta, PetAnimation previous = PetAnimation.Right) => Math.Abs(horizontalDelta) < 0.5 ? previous : horizontalDelta < 0 ? PetAnimation.Left : PetAnimation.Right;
    public const int DozeRow = 11;
    public static IReadOnlyList<(int Image, double Seconds, int Zs)> DozeSteps { get; } = Array.AsReadOnly(new[] { (0, 0.900, 0), (1, 0.450, 0) }.Concat(Enumerable.Range(1, 3).SelectMany(n => new[] { (2, 0.600, n), (3, 0.200, n), (4, 0.440, n), (3, 0.200, n) })).Append((1, 0.150, 0)).ToArray());
    public static double DozeCycle => DozeSteps.Sum(s => s.Seconds);
    public static int DozeStill => Enumerable.Range(0, DozeSteps.Count).Last(i => DozeSteps[i].Image == 2);
    public static PetAnimationFrame DozeFrame(double elapsed, bool reducedMotion = false)
    {
        if (reducedMotion) return new(DozeRow, DozeStill);
        var remaining = Math.Max(0, double.IsFinite(elapsed) ? elapsed : 0) % DozeCycle;
        for (var column = 0; column < DozeSteps.Count; column++) { if (remaining < DozeSteps[column].Seconds) return new(DozeRow, column); remaining -= DozeSteps[column].Seconds; }
        return new(DozeRow, DozeSteps.Count - 1);
    }
    public static Rect[] DozeZs(int column) => column < 0 || column >= DozeSteps.Count ? [] : Enumerable.Range(0, DozeSteps[column].Zs).Select(i => {
        var size = new[] { 6, 8, 10.5 }[i]; return new Rect(132 + new[] { 0, 9.5, 21.5 }[i], 56 - new[] { 0, 12.5, 27.5 }[i], size, size);
    }).ToArray();
}
public sealed class PetDoze
{
    public const double Delay = 180;
    private double? quietSince;
    public double? Update(double now, bool quiet, double after = Delay)
    {
        if (!quiet) { quietSince = null; return null; }
        var since = quietSince ?? now; quietSince = since;
        return now - since >= after ? now - since - after : null;
    }
}
