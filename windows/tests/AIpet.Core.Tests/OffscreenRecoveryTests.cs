using AIpet.Core;
namespace AIpet.Core.Tests;
public sealed class OffscreenRecoveryTests
{
    [Fact] public void ToleratesTwoPixelsButRecoversBeyondThem()
    {
        Rect[] areas = [new(0, 0, 100, 100)];
        var window = new Rect(-20, -20, 140, 140);
        Assert.False(VisibilityGuard.RecoverOffscreen(true, false, window, new(-2, -2, 104, 104), areas));
        Assert.False(VisibilityGuard.RecoverOffscreen(true, false, window, new(-2, 10, 50, 50), areas));
        Assert.False(VisibilityGuard.RecoverOffscreen(true, false, window, new(52, 52, 50, 50), areas));
        Assert.True(VisibilityGuard.RecoverOffscreen(true, false, window, new(52.01, 10, 50, 50), areas));
        Assert.True(VisibilityGuard.RecoverOffscreen(true, false, window, new(-2.01, 10, 50, 50), areas));
    }
    [Fact] public void OversizedSpriteOnlyNeedsOverlapWithNearestArea()
    {
        Rect[] areas = [new(-100, 0, 100, 100), new(500, 0, 1000, 1000)];
        Assert.False(VisibilityGuard.RecoverOffscreen(true, false, new(-120, -20, 200, 200), new(-110, -10, 120, 120), areas));
        Assert.True(VisibilityGuard.RecoverOffscreen(true, false, new(-50, 0, 400, 300), new(10, 10, 120, 120), areas));
    }
    [Fact] public void NormalAndFullyOffscreenCasesRemainCorrect()
    {
        Rect[] areas = [new(-1920, -300, 1920, 1080), new(0, 0, 1920, 1080)];
        Assert.False(VisibilityGuard.RecoverOffscreen(true, false, new(-200, 100, 300, 300), new(-180, 120, 100, 100), areas));
        Assert.True(VisibilityGuard.RecoverOffscreen(true, false, new(3000, 100, 300, 300), new(3020, 120, 100, 100), areas));
    }
    [Fact] public void RepeatedRecoveryWaitsThirtySeconds()
    {
        Assert.True(VisibilityGuard.RecoveryDue(true, 1, double.NegativeInfinity));
        Assert.False(VisibilityGuard.RecoveryDue(true, 2, 1));
        Assert.False(VisibilityGuard.RecoveryDue(true, 30.999, 1));
        Assert.True(VisibilityGuard.RecoveryDue(true, 31, 1));
        Assert.False(VisibilityGuard.RecoveryDue(false, 100, 1));
    }
    [Fact] public void OnlyMeasuredPositionChangeTriggersSave()
    {
        var before = new Rect(10, 20, 100, 100);
        Assert.False(VisibilityGuard.PositionChanged(before, before));
        Assert.False(VisibilityGuard.PositionChanged(before, before with { Width = 200 }));
        Assert.False(VisibilityGuard.PositionChanged(before, null));
        Assert.True(VisibilityGuard.PositionChanged(before, before with { X = 11 }));
        Assert.True(VisibilityGuard.PositionChanged(before, before with { Y = 19 }));
    }
}
