namespace AIpet.Core;

public readonly record struct PetRenderState(PetAnimationFrame Frame, PetPlacement? Placement,
    double BubbleOpacity, string Headline, string Subtitle, WorkState State, bool Paused,
    bool ReducedMotion, long PulseStep)
{
    public static long GetPulseStep(double now, double bubbleOpacity, WorkState state, bool paused, bool reducedMotion)
        => bubbleOpacity > 0 && state == WorkState.Running && !paused && !reducedMotion
            ? (long)Math.Floor(now * 20) : 0;
    public bool NeedsRedraw(PetRenderState? previous) => previous is null || this != previous.Value;
    public double DotOpacity => BubbleOpacity > 0 && State == WorkState.Running && !Paused && !ReducedMotion
        ? 0.675 + 0.325 * Math.Cos(PulseStep / 20.0 * Math.PI * 2 / 1.6) : 1;
}
