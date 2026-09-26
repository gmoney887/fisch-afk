using FischMacroCS.Core;
using FischMacroCS.Vision;

namespace FischMacroCS.Tests;

public class FishingViewSafetyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfirmedCatchAllowsNextCycleButDoesNotExcuseStall(bool frozenReel)
    {
        var clock = new Clock();
        var safety = new FishingViewSafety(clock);
        Assert.False(safety.ConfirmCatch(60000)); // No suspicion existed at catch time.
        clock.Timestamp = 800;
        Assert.False(safety.Observe(FishingViewStatus.Uncertain, false, 35000));
        clock.Timestamp = 4000;
        Assert.False(safety.Observe(FishingViewStatus.Changed, false, 35000));
        Assert.True(safety.HasRecentProgress); // Engine must allow the next cast/lure.
        clock.Timestamp = 59999;
        Assert.False(safety.Observe(FishingViewStatus.Changed, frozenReel, 35000));
        clock.Timestamp = 60000;
        Assert.False(safety.HasRecentProgress);
        Assert.True(safety.Observe(FishingViewStatus.Changed, frozenReel, 35000));
    }

    [Fact]
    public void StableFramesDoNotRenewCatchDeadlineAndRestartClearsIt()
    {
        var clock = new Clock();
        var safety = new FishingViewSafety(clock);
        safety.ConfirmCatch(60000);
        clock.Timestamp = 50000;
        safety.Observe(FishingViewStatus.Stable, false, 35000);
        Assert.True(safety.HasRecentProgress);
        clock.Timestamp = 60000;
        Assert.False(safety.HasRecentProgress);
        safety.ConfirmCatch(60000);
        Assert.True(safety.HasRecentProgress);
        safety.Reset();
        Assert.False(safety.HasRecentProgress);
    }

    [Fact]
    public void RecordedReelAtMorningStopIsLiveAndDefersSceneOnlyStop()
    {
        using var reel = OpenCvSharp.Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "reel_morning_guard_stop.png"));
        using var evidence = new VisionProcessor().ProcessTrack(reel, 968, 1013, 1369 / 1080.0,
            new Settings().SelectedTheme, false);
        Assert.True(evidence.HasLiveReel);
        var clock = new Clock();
        var safety = new FishingViewSafety(clock);
        safety.Observe(FishingViewStatus.Uncertain, evidence.HasLiveReel, 35000);
        clock.Timestamp = 4000;
        Assert.False(safety.Observe(FishingViewStatus.Changed, evidence.HasLiveReel, 35000));
    }

    private sealed class Clock : IClock
    {
        public long Timestamp { get; set; }
        public double ElapsedMilliseconds(long since) => Timestamp - since;
        public void Delay(int milliseconds, CancellationToken cancellation) => Timestamp += milliseconds;
    }

    [Fact]
    public void ProductiveReelSurvivesSceneChangeUntilConfirmedCatch()
    {
        var clock = new Clock();
        var safety = new FishingViewSafety(clock);
        for (int t = 0; t <= 9000; t += 1000)
        {
            clock.Timestamp = t;
            Assert.False(safety.Observe(FishingViewStatus.Changed, true, 35000));
        }
        clock.Timestamp = 10000;
        Assert.False(safety.Observe(FishingViewStatus.Changed, false, 35000));
        Assert.True(safety.ConfirmCatch());
        Assert.False(safety.IsSuspected);
        Assert.False(safety.ConfirmCatch());
    }

    [Fact]
    public void LostReelStopsAfterShortCatchFinalizationGrace()
    {
        var clock = new Clock();
        var safety = new FishingViewSafety(clock);
        safety.Observe(FishingViewStatus.Uncertain, true, 35000);
        clock.Timestamp = 4000;
        Assert.False(safety.Observe(FishingViewStatus.Changed, true, 35000));
        clock.Timestamp = 5999;
        Assert.False(safety.Observe(FishingViewStatus.Changed, false, 35000));
        clock.Timestamp = 6000;
        Assert.True(safety.Observe(FishingViewStatus.Changed, false, 35000));
    }

    [Fact]
    public void NoFishingEvidenceStopsEvenWhenSuspicionStartedAtZero()
    {
        var clock = new Clock();
        var safety = new FishingViewSafety(clock);
        Assert.False(safety.Observe(FishingViewStatus.Uncertain, false, 35000));
        clock.Timestamp = 2999;
        Assert.False(safety.Observe(FishingViewStatus.Changed, false, 35000));
        clock.Timestamp = 3000;
        Assert.True(safety.Observe(FishingViewStatus.Changed, false, 35000));
    }

    [Fact]
    public void FrozenReelCannotDeferStopForever()
    {
        var clock = new Clock();
        var safety = new FishingViewSafety(clock);
        safety.Observe(FishingViewStatus.Uncertain, true, 35000);
        clock.Timestamp = 34999;
        Assert.False(safety.Observe(FishingViewStatus.Changed, true, 35000));
        clock.Timestamp = 35000;
        Assert.True(safety.Observe(FishingViewStatus.Changed, true, 35000));
    }

    [Fact]
    public void ReturningToOriginalViewClearsSuspicion()
    {
        var clock = new Clock();
        var safety = new FishingViewSafety(clock);
        safety.Observe(FishingViewStatus.Uncertain, false, 35000);
        clock.Timestamp = 4000;
        Assert.False(safety.Observe(FishingViewStatus.Stable, false, 35000));
        Assert.False(safety.IsSuspected);
        Assert.False(safety.Observe(FishingViewStatus.Changed, false, 35000));
    }
}
