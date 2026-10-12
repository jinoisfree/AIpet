using AIpet.Core;
namespace AIpet.Core.Tests;
public sealed class BubbleRevealTests : TestSupport
{
    [Fact]
    public void HoverAndAlertLifetime()
    {
        var reveal = new BubbleReveal();
        Check(!reveal.Update(0, false), "the bubble is put away until something brings it out");
        Check(reveal.Update(1, true) && reveal.Update(60, true) && !reveal.Update(60.01, false), "it is out for as long as the pointer is on it and goes when the pointer leaves");
        reveal.Alert(10);
        Check(reveal.Update(11.9, false) && !reveal.Update(12, false), "an alert shows it for two seconds");
        reveal.Alert(20);
        Check(reveal.Update(20.5, true) && reveal.Update(21.9, false) && !reveal.Update(22, false), "a touch during an alert does not cut it short");
        Check(reveal.Update(25, true) && !reveal.Update(25.1, false), "held past an alert, it goes when the pointer leaves");
    }
}
