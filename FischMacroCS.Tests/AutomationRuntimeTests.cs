using FischMacroCS.Core;
using OpenCvSharp;

namespace FischMacroCS.Tests;

public class AutomationRuntimeTests
{
    [Fact]
    public async Task StopAlsoCancelsAnOperationAlreadyDequeued()
    {
        using var coordinator = new AutomationCoordinator();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        bool inputSent = false;
        var work = coordinator.Enqueue(() =>
        {
            entered.Set(); release.Wait();
            coordinator.ThrowIfCancelled();
            inputSent = true;
            return true;
        });
        Assert.True(entered.Wait(2000));
        coordinator.CancelPending(); release.Set();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await work);
        Assert.False(inputSent);
        Assert.True(await coordinator.Enqueue(() => { coordinator.ThrowIfCancelled(); return true; }));
    }

    [Fact]
    public async Task DisposedCoordinatorRejectsWorkWithoutThrowingAtCallSite()
    {
        var coordinator = new AutomationCoordinator();
        coordinator.Dispose(); coordinator.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await coordinator.Enqueue(() => true));
    }

    [Fact]
    public async Task CoordinatorCanDisposeOnItsOwnThread()
    {
        var coordinator = new AutomationCoordinator();
        Assert.True(await coordinator.Enqueue(() => { coordinator.Dispose(); return true; }).WaitAsync(TimeSpan.FromSeconds(2)));
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await coordinator.Enqueue(() => true));
    }
    [Fact]
    public void DelayedBannerFinalizesExactlyOnce()
    {
        var tracker = new CatchOutcomeTracker();
        Assert.Null(tracker.FinalizeOnce(false));
        tracker.Observe(true);
        Assert.Equal(ActionOutcome.ConfirmedSuccess, tracker.FinalizeOnce(true));
        Assert.Null(tracker.FinalizeOnce(true));
        tracker.Observe(false, true);
        Assert.Equal(ActionOutcome.ConfirmedSuccess, tracker.Outcome);
    }
    [Fact]
    public void MissingBannerIsUnknownAndConflictingEvidenceIsUnknown()
    {
        Assert.Equal(ActionOutcome.Unknown, new CatchOutcomeTracker().FinalizeOnce(true));
        var tracker = new CatchOutcomeTracker();
        tracker.Observe(true, true);
        Assert.Equal(ActionOutcome.Unknown, tracker.FinalizeOnce(true));
    }
    [Fact]
    public void RecoveryIsBoundedUntilVerifiedProgress()
    {
        var budget = new RecoveryBudget();
        Assert.True(budget.TryBegin()); Assert.True(budget.TryBegin()); Assert.True(budget.TryBegin());
        Assert.False(budget.TryBegin()); Assert.False(budget.TryBegin());
        budget.ConfirmProgress(); Assert.True(budget.TryBegin());
    }
    [Fact]
    public async Task CoordinatorSerializesRequestsOnOneOwner()
    {
        using var coordinator = new AutomationCoordinator();
        var owners = new System.Collections.Concurrent.ConcurrentBag<int>();
        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => coordinator.Enqueue(() =>
        { Assert.True(coordinator.IsOwner); owners.Add(Environment.CurrentManagedThreadId); return true; })));
        Assert.Single(owners.Distinct());
    }
    [Fact]
    public async Task StopCancelsQueuedCommandsBeforeTheyCanSendInputs()
    {
        using var coordinator = new AutomationCoordinator();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var active = coordinator.Enqueue(() => { entered.Set(); release.Wait(); return true; });
        Assert.True(entered.Wait(2000));
        bool ran = false;
        var pending = coordinator.Enqueue(() => ran = true);
        coordinator.CancelPending(); release.Set();
        await active;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await pending);
        Assert.False(ran);
    }
    private sealed class FakeClock : IClock
    {
        public long Timestamp { get; private set; }
        public double ElapsedMilliseconds(long since) => Timestamp - since;
        public void Delay(int ms, CancellationToken token) { token.ThrowIfCancellationRequested(); Timestamp += ms; }
    }
    private sealed class FakeVision(Func<string, bool> find) : IWorkflowVision
    {
        public (bool Found, Point Center, double Confidence) Find(Mat frame, WorkflowTarget target) => (find(target.Name), new Point(10, 10), 1);
    }
    [Fact]
    public void MissingTemplatesReportUnavailableWithoutCapturingOrClicking()
    {
        var clock = new FakeClock();
        var vision = new TemplateWorkflowVision(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N")));
        var runner = new VerifiedWorkflow(clock, vision,
            () => throw new Exception("Must not capture without templates"),
            _ => throw new Exception("Must not wait without templates"));
        var result = runner.Run([new("Open equipment", new("equipment-button", 0, 0, 1),
            new("equipment-search", 0, 0, 1), _ => throw new Exception("Must not click"))], default);
        Assert.Equal(ActionOutcome.Unknown, result.Outcome);
        Assert.Contains("reviewed visual templates are missing", result.Evidence);
        Assert.Contains("equipment-button", result.Evidence);
        Assert.Contains("equipment-search", result.Evidence);
    }

    [Fact]
    public void MissingPrerequisiteNeverClicksAndNeverConfirmsSuccess()
    {
        var clock = new FakeClock(); bool clicked = false;
        var runner = new VerifiedWorkflow(clock, new FakeVision(_ => false), () => new Mat(20, 20, MatType.CV_8UC3), ms => clock.Delay(ms, default));
        var result = runner.Run([new("claim", new("button", 0, 0, 1), new("reward", 0, 0, 1), _ => clicked = true, 300)], default);
        Assert.Equal(ActionOutcome.Unknown, result.Outcome); Assert.False(clicked);
    }
    [Fact]
    public void StaleRewardCannotConfirmANewAction()
    {
        var clock = new FakeClock(); bool clicked = false;
        var runner = new VerifiedWorkflow(clock, new FakeVision(_ => true), () => new Mat(20, 20, MatType.CV_8UC3), ms => clock.Delay(ms, default));
        var result = runner.Run([new("claim", new("button", 0, 0, 1), new("reward", 0, 0, 1), _ => clicked = true)], default);
        Assert.Equal(ActionOutcome.Unknown, result.Outcome); Assert.False(clicked);
    }
    [Fact]
    public void NewRewardConfirmsAnAction()
    {
        var clock = new FakeClock(); bool clicked = false;
        var runner = new VerifiedWorkflow(clock, new FakeVision(name => name == "button" || clicked), () => new Mat(20, 20, MatType.CV_8UC3), ms => clock.Delay(ms, default));
        var result = runner.Run([new("claim", new("button", 0, 0, 1), new("reward", 0, 0, 1), _ => clicked = true)], default);
        Assert.Equal(ActionOutcome.ConfirmedSuccess, result.Outcome);
    }
}
