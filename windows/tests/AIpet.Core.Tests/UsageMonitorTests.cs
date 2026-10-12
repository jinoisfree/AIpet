using Xunit;
namespace AIpet.Core.Tests;
public sealed class UsageMonitorTests
{
    private sealed class Source : IUsageSources
    {
        public int CodexCalls, HistoryCalls, DirectCalls;
        public TaskCompletionSource? Block;
        public DirectUsageStatus Status = DirectUsageStatus.Success;
        public bool Fail;
        public async Task<(ServiceUsage? Value, string Reason)> Codex(string? configured)
        {
            Interlocked.Increment(ref CodexCalls);
            if (Block is { } block) await block.Task;
            return (Fail ? null : new(UsageSource.Codex, DateTimeOffset.UtcNow, [new(UsagePeriod.FiveHour, 28)], new("synthetic@example.com", Verified: true)), Fail ? "Codex 연결 실패" : "");
        }
        public ServiceUsage? History() { Interlocked.Increment(ref HistoryCalls); return null; }
        public Task<(ServiceUsage? Value, DirectUsageStatus Status)> Direct(Func<bool> allowed)
        {
            Interlocked.Increment(ref DirectCalls);
            return Task.FromResult<(ServiceUsage?, DirectUsageStatus)>((null, allowed() ? Status : DirectUsageStatus.Disabled));
        }
    }
    [Fact]
    public async Task PausedAndOverlappingRefreshesDoNotQueryAndOldResultsAreDiscarded()
    {
        var source = new Source { Block = new(TaskCreationOptions.RunContinuationsAsynchronously) }; var monitor = new UsageMonitor(source); var now = DateTimeOffset.UtcNow;
        monitor.Configure(true, true); Assert.False(await monitor.Refresh(now)); Assert.Equal(0, source.CodexCalls);
        monitor.Configure(false, true); var pending = monitor.Refresh(now);
        Assert.False(await monitor.Refresh(now));
        monitor.Configure(true, true); source.Block.SetResult(); Assert.False(await pending);
        Assert.Null(monitor.State.Codex); Assert.Equal(0, source.DirectCalls);
        monitor.Configure(false, true); Assert.True(await monitor.Refresh(now)); Assert.NotNull(monitor.State.Codex); Assert.Equal(1, source.DirectCalls);
    }
    [Fact]
    public async Task SchedulesBackoffRetentionAndOptOut()
    {
        var source = new Source { Status = DirectUsageStatus.Limited }; var monitor = new UsageMonitor(source); var now = DateTimeOffset.UtcNow;
        monitor.Configure(false, true); await monitor.Refresh(now);
        Assert.Equal((1, 1, 1), (source.CodexCalls, source.HistoryCalls, source.DirectCalls));
        await monitor.Refresh(now.AddSeconds(29)); Assert.Equal((1, 1, 1), (source.CodexCalls, source.HistoryCalls, source.DirectCalls));
        await monitor.Refresh(now.AddSeconds(30)); Assert.Equal(2, source.HistoryCalls);
        source.Fail = true; await monitor.Refresh(now.AddSeconds(300)); Assert.Equal(2, source.CodexCalls); Assert.Equal(2, source.DirectCalls);
        Assert.Equal("72%", monitor.State.Codex?.Display(now));
        await monitor.Refresh(now.AddSeconds(899)); Assert.Equal(2, source.DirectCalls);
        await monitor.Refresh(now.AddSeconds(900)); Assert.Equal(3, source.DirectCalls);
        monitor.Configure(false, false); await monitor.Refresh(now.AddSeconds(2100)); Assert.Equal(3, source.DirectCalls);
        Assert.Equal(DirectUsageStatus.Disabled, monitor.State.DirectStatus);
        Assert.Equal("—", monitor.State.Codex?.Display(now.AddSeconds(3601)));
    }
    [Fact]
    public async Task AccessDeniedStopsUntilToggleAndDefaultIsOff()
    {
        var source = new Source { Status = DirectUsageStatus.Denied }; var monitor = new UsageMonitor(source); var now = DateTimeOffset.UtcNow;
        await monitor.Refresh(now); Assert.Equal(0, source.DirectCalls);
        monitor.Configure(false, true); await monitor.Refresh(now); Assert.Equal(1, source.DirectCalls);
        await monitor.Refresh(now.AddHours(1)); Assert.Equal(1, source.DirectCalls);
        monitor.Configure(false, false); monitor.Configure(false, true); await monitor.Refresh(now.AddHours(1)); Assert.Equal(2, source.DirectCalls);
    }
}
