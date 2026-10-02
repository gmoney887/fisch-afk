using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;

namespace FischMacroCS.Tests;

public class MissingNeedleRecoveryTests
{
    [Fact]
    public void PersistentBarWithoutNeedleRecoversAfterThreeSeconds()
    {
        var guard = new MissingNeedleRecovery();
        Assert.False(guard.Observe(true, false, 100));
        Assert.False(guard.Observe(true, false, 3099));
        Assert.True(guard.Observe(true, false, 3100));
    }

    [Fact]
    public void BriefOcclusionAndConcludingReelDoNotResetRod()
    {
        var guard = new MissingNeedleRecovery();
        Assert.False(guard.Observe(true, false, 0));
        Assert.False(guard.Observe(true, true, 2900));
        Assert.False(guard.Observe(true, false, 3000));
        Assert.False(guard.Observe(false, false, 6000));
        Assert.False(guard.Observe(true, false, 9000));
        guard.Reset();
        Assert.False(guard.Observe(true, false, 15000));
    }

    [Fact]
    public void RecordedCoralCannotConfirmReelAndTriggersMissingNeedleRecovery()
    {
        using var frame = Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "reel_false_coral.png"));
        Assert.False(frame.Empty());
        var vision = new VisionProcessor();
        var guard = new MissingNeedleRecovery();
        using var result = vision.ProcessTrack(frame, 968, 1013, 1369.0 / 1080, MinigameTheme.AutoCalibrate, false);
        Assert.True(result.BarFound); // Reproduces the misleading scenery candidate.
        Assert.False(result.HasLiveReel);
        Assert.False(ReelEntryGuard.Confirm(() => vision.ProcessTrack(frame, 968, 1013, 1369.0 / 1080,
            MinigameTheme.AutoCalibrate, false), _ => { }, CancellationToken.None));
        Assert.False(guard.Observe(result.BarFound, result.FishFound, 0));
        Assert.True(guard.Observe(result.BarFound, result.FishFound, 3000));
    }
}
