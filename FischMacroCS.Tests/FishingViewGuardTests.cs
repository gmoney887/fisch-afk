using FischMacroCS.Vision;
using OpenCvSharp;

namespace FischMacroCS.Tests;

public class FishingViewGuardTests
{
    private static Mat Scene(int seed)
    {
        using var noise = new Mat(540, 960, MatType.CV_8UC1);
        var rng = new RNG((ulong)seed);
        rng.Fill(noise, DistributionType.Uniform, 0, 255);
        var color = new Mat(); Cv2.CvtColor(noise, color, ColorConversionCodes.GRAY2BGR);
        return color;
    }

    [Fact]
    public void PersistentDisplacementStopsButTransientAndCentralEffectsDoNot()
    {
        using var guard = new FishingViewGuard();
        using var baseline = Scene(123);
        guard.Observe(baseline); guard.Observe(baseline);
        Assert.Equal(FishingViewStatus.Stable, guard.Observe(baseline));
        using var effects = baseline.Clone();
        Cv2.Rectangle(effects, new Rect(390, 140, 180, 310), Scalar.White, -1);
        Assert.Equal(FishingViewStatus.Stable, guard.Observe(effects));
        using var changed = Scene(456);
        Assert.Equal(FishingViewStatus.Uncertain, guard.Observe(changed));
        Assert.Equal(FishingViewStatus.Stable, guard.Observe(baseline));
        Assert.Equal(FishingViewStatus.Uncertain, guard.Observe(changed));
        Assert.Equal(FishingViewStatus.Uncertain, guard.Observe(changed));
        Assert.Equal(FishingViewStatus.Changed, guard.Observe(changed));
    }

    [Fact]
    public void FeaturelessViewNeverClaimsToKnowPositionAndResetRequiresLearning()
    {
        using var guard = new FishingViewGuard();
        using var blank = new Mat(540, 960, MatType.CV_8UC3, Scalar.All(70));
        for (int i = 0; i < 5; i++) Assert.Equal(FishingViewStatus.Learning, guard.Observe(blank));
        using var scene = Scene(123);
        guard.Observe(scene); guard.Observe(scene); guard.Observe(scene);
        Assert.True(guard.IsArmed);
        guard.Reset();
        Assert.False(guard.IsArmed);
        Assert.Equal(FishingViewStatus.Learning, guard.Observe(scene));
    }
}
