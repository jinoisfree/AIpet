namespace AIpet.Core;
public sealed class BubbleReveal
{
    public const double AlertStay = 2;
    public const string SettingsKey = "bubbleAlwaysShown";
    private double until = double.NegativeInfinity;
    public void Alert(double now) => until = Math.Max(until, now + AlertStay);
    public bool Update(double now, bool held) => held || now < until;
}
