using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;
using System.IO;

namespace FischMacroCS.Tests;

public class CatchFailureTests
{
    [Theory]
    [InlineData("reel_losing_control.png")]
    [InlineData("reel_losing_control_dim.png")]
    public void RedEffectDoesNotMakeAnActiveReelDisappear(string name)
    {
        using var image = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        using var detected = new VisionProcessor().ProcessTrack(image, 968, 1013, 1369 / 1080.0, MinigameTheme.AutoCalibrate, false);
        Assert.True(detected.FishFound);
        Assert.True(detected.ReelProgressFound);
        Assert.True(detected.BarFound);
        Assert.True(detected.HasLiveReel);
        Assert.InRange(detected.BarWidth, 760, 825);
    }
    [Theory]
    [InlineData(720)] [InlineData(1080)] [InlineData(1369)] [InlineData(2160)]
    public void StreakEndedIsFailureEvidenceAtDifferentScales(int height)
    {
        using var source = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "reel_streak_ended.png"));
        using var image = new Mat();
        Cv2.Resize(source, image, new Size((int)Math.Round(source.Width * height / 1369.0), (int)Math.Round(source.Height * height / 1369.0)));
        Assert.True(CatchFailureDetector.Detect(image, height));
        Assert.False(new VisionProcessor().DetectCatchNotification(image, height));
        var tracker = new CatchOutcomeTracker();
        tracker.Observe(false, CatchFailureDetector.Detect(image, height));
        Assert.Equal(ActionOutcome.ConfirmedFailure, tracker.FinalizeOnce(true));
        Assert.Null(tracker.FinalizeOnce(true));
    }
    [Theory]
    [InlineData("reel_losing_control.png")]
    [InlineData("reel_losing_control_dim.png")]
    [InlineData("reel_catch_banner.png")]
    [InlineData("reel_catch_live.png")]
    public void ActiveOrSuccessfulReelIsNotALoss(string name)
    {
        using var image = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        Assert.False(CatchFailureDetector.Detect(image, 1369));
    }
    [Theory]
    [InlineData(4, 1, 0, 80)]
    [InlineData(53, 1, 0, 98.148148)]
    [InlineData(53, 0, 1, 98.148148)]
    [InlineData(0, 0, 0, 0)]
    public void SuccessRateIncludesUnconfirmedAttempts(int caught, int lost, int unknown, double expected) =>
        Assert.Equal(expected, CatchStatistics.SuccessRate(caught, lost, unknown), 5);
    [Fact]
    public void LingeringFailureCannotBeCountedAgainOnTheNextReel()
    {
        var evidence = new FreshFailureEvidence();
        Assert.False(evidence.Observe(true)); Assert.True(evidence.Observe(true));
        for (int i = 0; i < 20; i++) Assert.False(evidence.Observe(true));
        Assert.False(evidence.Observe(false)); Assert.False(evidence.Observe(true));
        Assert.False(evidence.Observe(false)); Assert.False(evidence.Observe(false));
        Assert.False(evidence.Observe(true)); Assert.True(evidence.Observe(true));
    }
    [Fact]
    public void DisplayDoesNotInventAPerfectSession()
    {
        Assert.Equal("—", CatchStatistics.Display(0, 0, 0));
        Assert.DoesNotContain("100", CatchStatistics.Display(9999, 1, 0));
        Assert.DoesNotContain("100", CatchStatistics.Display(9999, 0, 1));
    }
    private sealed class Clock : IClock
    {
        public long Timestamp { get; set; }
        public double ElapsedMilliseconds(long start) => Timestamp - start;
        public void Delay(int ms, CancellationToken token) => Timestamp += ms;
    }
    [Fact]
    public void UnresponsiveHoldRecoveryIsBoundedAndNeedsVerifiedEvidence()
    {
        var clock = new Clock(); var guard = new ReelHoldRecovery(clock);
        Assert.False(guard.Observe(true, true, 100, 200, 80));
        clock.Timestamp = 449; Assert.False(guard.Observe(true, true, 90, 200, 80));
        clock.Timestamp = 450; Assert.True(guard.Observe(true, true, 90, 200, 80));
        clock.Timestamp = 1000; Assert.False(guard.Observe(true, true, 90, 200, 80));
        clock.Timestamp = 1450; Assert.False(guard.Observe(true, true, 90, 200, 80));
        clock.Timestamp = 1900; Assert.True(guard.Observe(true, true, 90, 200, 80));
        clock.Timestamp = 10000; Assert.False(guard.Observe(true, true, 90, 200, 80));
        guard.Reset(); Assert.False(guard.Observe(false, true, 90, 200, 80));
        clock.Timestamp += 500; Assert.False(guard.Observe(false, true, 90, 200, 80));
    }
    [Fact]
    public void RespondingHoldNeverTriggersRecovery()
    {
        var clock = new Clock(); var guard = new ReelHoldRecovery(clock);
        for (int i = 0; i < 30; i++)
        { clock.Timestamp += 100; Assert.False(guard.Observe(true, true, 100 + i * 4, 1000, 80)); }
    }
}
