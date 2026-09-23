using FischMacroCS.Vision;
using OpenCvSharp;

namespace FischMacroCS.Tests;

public class FishingViewGuardTests
{
    [Theory]
    [InlineData("00000074")]
    [InlineData("00000538")]
    public void RecordedFishingZoomDoesNotStopStationaryPlayer(string baselineName)
    {
        using var guard = new FishingViewGuard();
        using var baseline = Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", $"view_{baselineName}.png"));
        guard.Observe(baseline); guard.Observe(baseline); guard.Observe(baseline);
        Assert.True(guard.IsArmed);
        foreach (var name in new[] { "00000074", "00000087", "00000538", "00000604" })
        {
            using var scene = Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", $"view_{name}.png"));
            for (int i = 0; i < 3; i++) Assert.Equal(FishingViewStatus.Stable, guard.Observe(scene));
        }
    }
    private static Mat Scene(int seed)
    {
        using var noise = new Mat(540, 960, MatType.CV_8UC1);
        var rng = new RNG((ulong)seed);
        rng.Fill(noise, DistributionType.Uniform, 0, 255);
        var color = new Mat(); Cv2.CvtColor(noise, color, ColorConversionCodes.GRAY2BGR);
        return color;
    }

    [Fact]
    public void TranslationCannotBeExplainedAwayAsCameraZoom()
    {
        using var baseline = Scene(781);
        using var guard = new FishingViewGuard();
        guard.Observe(baseline); guard.Observe(baseline); guard.Observe(baseline);
        using var transform = Mat.Eye(2, 3, MatType.CV_64FC1).ToMat();
        transform.Set(0, 2, baseline.Height * .15);
        using var moved = new Mat();
        Cv2.WarpAffine(baseline, moved, transform, baseline.Size());
        Assert.Equal(FishingViewStatus.Uncertain, guard.Observe(moved));
        Assert.Equal(FishingViewStatus.Uncertain, guard.Observe(moved));
        Assert.Equal(FishingViewStatus.Changed, guard.Observe(moved));
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
