using FischMacroCS.Core;

namespace FischMacroCS.Tests;

public class FishingRetryWaitTests
{
    [Fact]
    public void UnavailableGameKeepsWaitingAndReleasesInputsBeforeChecking()
    {
        var clock = new TestClock();
        bool released = false;
        int checks = 0, reports = 0;
        FishingRetryWait.Wait(clock, () =>
        {
            Assert.True(released);
            return ++checks == 100;
        }, () => released = true, () => reports++, CancellationToken.None);
        Assert.Equal(100, checks);
        Assert.True(reports >= 20);
    }

    [Fact]
    public void UserStopCancelsWaitingWithoutReturningReady()
    {
        var clock = new TestClock();
        using var stop = new CancellationTokenSource();
        int releases = 0;
        Assert.Throws<OperationCanceledException>(() => FishingRetryWait.Wait(clock, () => false,
            () => releases++, () => { if (clock.Timestamp >= 1000) stop.Cancel(); }, stop.Token));
        Assert.Equal(1, releases);
        Assert.Equal(1000, clock.Timestamp);
    }

    [Fact]
    public void StopAsContextReturnsStillPreventsResume()
    {
        var clock = new TestClock();
        using var stop = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => FishingRetryWait.Wait(clock,
            () => { stop.Cancel(); return true; }, () => { }, () => { }, stop.Token));
    }

    [Fact]
    public void RepeatedRecoveryUsesCooldownInsteadOfEndingFishingRequest()
    {
        var clock = new TestClock();
        var budget = new RecoveryBudget();
        for (int burst = 0; burst < 5; burst++)
        {
            for (int attempt = 0; attempt < 3; attempt++) Assert.True(budget.TryBegin());
            Assert.False(budget.TryBegin());
            FishingRetryWait.Wait(clock, () => true, () => { }, () => { }, CancellationToken.None, 5000);
            budget.ResetAfterCooldown();
        }
        Assert.Equal(25000, clock.Timestamp);
    }

    private sealed class TestClock : IClock
    {
        public long Timestamp { get; private set; }
        public double ElapsedMilliseconds(long since) => Timestamp - since;
        public void Delay(int milliseconds, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); Timestamp += milliseconds; }
    }
}
