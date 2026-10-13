using AIpet.Core;
namespace AIpet.Core.Tests;

public sealed class WindowRecreationGuardTests
{
    [Theory]
    [InlineData(true, true, false, WindowRecreation.None)]
    [InlineData(false, true, false, WindowRecreation.Recreate)]
    [InlineData(false, false, false, WindowRecreation.None)]
    [InlineData(false, true, true, WindowRecreation.None)]
    public void RespectsLifetimeAndIntent(bool exists, bool visible, bool quitting, WindowRecreation expected)
    {
        var guard = new WindowRecreationGuard();
        Assert.Equal(expected, guard.Decide(exists, visible, quitting, 0));
        Assert.Equal(expected == WindowRecreation.Recreate ? 1 : 0, guard.AttemptsInWindow);
    }
    [Fact] public void FiveAttemptsIncludeFailuresAndExpireIndividually()
    {
        var guard = new WindowRecreationGuard();
        for (var i = 0; i < 5; i++) Assert.Equal(WindowRecreation.Recreate, guard.Decide(false, true, false, i));
        Assert.Equal(WindowRecreation.LimitReached, guard.Decide(false, true, false, 13.999));
        Assert.Equal(5, guard.AttemptsInWindow);
        Assert.Equal(WindowRecreation.Recreate, guard.Decide(false, true, false, 60));
        Assert.Equal(WindowRecreation.LimitReached, guard.Decide(false, true, false, 60.5));
        Assert.Equal(WindowRecreation.Recreate, guard.Decide(false, true, false, 61));
    }
    [Fact] public void HidingOrQuittingNeverRecreatesEvenAfterLimitExpires()
    {
        var guard = new WindowRecreationGuard();
        for (var i = 0; i < 5; i++) guard.Decide(false, true, false, 0);
        Assert.Equal(WindowRecreation.None, guard.Decide(false, false, false, 1));
        Assert.Equal(WindowRecreation.None, guard.Decide(false, true, true, 60));
        Assert.Equal(0, guard.AttemptsInWindow);
        Assert.Equal(WindowRecreation.Recreate, guard.Decide(false, true, false, 61));
    }
}
