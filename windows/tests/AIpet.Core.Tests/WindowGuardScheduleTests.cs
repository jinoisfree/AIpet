using AIpet.Core;
namespace AIpet.Core.Tests;

public sealed class WindowGuardScheduleTests
{
    [Fact] public void ChecksImmediatelyAndKeepsPeriodicFallbackWithoutEvents()
    {
        var schedule = new WindowGuardSchedule();
        Assert.True(schedule.CheckDue(0));
        Assert.False(schedule.CheckDue(0.999));
        Assert.True(schedule.CheckDue(1));
        Assert.False(schedule.CheckDue(1.999));
        Assert.True(schedule.CheckDue(2));
    }

    [Fact] public void NewWindowDoesNotWaitForTheNextPeriodicCheck()
    {
        var schedule = new WindowGuardSchedule();
        Assert.True(schedule.CheckDue(0));
        Assert.False(schedule.CheckDue(0.2));
        schedule.RequestCheck();
        Assert.True(schedule.CheckDue(0.2));
        Assert.False(schedule.CheckDue(0.3));
        Assert.True(schedule.CheckDue(1.2));
    }

    [Fact] public void EventBurstsCoalesceWithoutLosingAnEarlyRequest()
    {
        var schedule = new WindowGuardSchedule();
        Assert.True(schedule.CheckDue(0));
        for (var i = 1; i < 100; i++)
        {
            schedule.RequestCheck();
            Assert.False(schedule.CheckDue(i / 1000.0));
        }
        Assert.True(schedule.CheckDue(0.1));
        Assert.False(schedule.CheckDue(0.2));
        Assert.True(schedule.CheckDue(1.1));
    }

    [Fact] public void EventDuringRecoverySchedulesAnotherCheckWithoutReentering()
    {
        var schedule = new WindowGuardSchedule();
        Assert.True(schedule.CheckDue(0));
        schedule.RequestCheck();
        Assert.False(schedule.CheckDue(0));
        Assert.False(schedule.CheckDue(0.099));
        Assert.True(schedule.CheckDue(0.1));
        schedule.RequestCheck();
        Assert.True(schedule.CheckDue(0.21));
        Assert.False(schedule.CheckDue(0.22));
    }
}
