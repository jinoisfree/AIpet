namespace AIpet.Core;
public sealed class PetPointerInteraction
{
    public const double TrackingDuration = 10, JumpDuration = 0.140 * 4 + 0.280;
    public enum PhaseKind { Inactive, Jumping, Looking }
    public readonly record struct Phase(PhaseKind Kind, double Elapsed = 0)
    {
        public static Phase Inactive => new(PhaseKind.Inactive);
        public static Phase Looking => new(PhaseKind.Looking);
        public static Phase Jumping(double elapsed) => new(PhaseKind.Jumping, elapsed);
    }
    private bool wasInside;
    private double? jumpStarted;
    private double trackingUntil = double.NegativeInfinity;
    public void Reset() { wasInside = false; jumpStarted = null; trackingUntil = double.NegativeInfinity; }
    public Phase Update(Point pointer, Rect sprite, Rect display, double now, bool enabled = true, bool reducedMotion = false)
    {
        if (!enabled || !double.IsFinite(pointer.X) || !double.IsFinite(pointer.Y) || !display.Contains(pointer)) { Reset(); return Phase.Inactive; }
        var inside = sprite.Contains(pointer);
        if (inside && !wasInside) { if (now >= trackingUntil) jumpStarted = now; trackingUntil = now + TrackingDuration; }
        wasInside = inside;
        if (now >= trackingUntil) return Phase.Inactive;
        if (!reducedMotion && jumpStarted is { } start && now - start < JumpDuration) return Phase.Jumping(Math.Max(0, now - start));
        return Phase.Looking;
    }
}
