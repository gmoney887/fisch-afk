using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;
using System.IO;

namespace FischMacroCS.Tests;

public class DimReelTests
{
    [Theory]
    [InlineData(1353)]
    [InlineData(1080)]
    [InlineData(720)]
    public void LiveUnknownOutcomeFrameIsStillAnActiveReel(int viewportHeight)
    {
        using var image = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "reel_live_false_exit.png"));
        Assert.False(image.Empty());
        double ratio = viewportHeight / 1353.0;
        using var scaled = new Mat();
        Cv2.Resize(image, scaled, new Size((int)Math.Round(image.Width * ratio), (int)Math.Round(image.Height * ratio)));
        using var result = new VisionProcessor().ProcessTrack(scaled, (int)Math.Round(383 * ratio), (int)Math.Round(1001 * ratio), viewportHeight / 1080.0,
            MinigameTheme.AutoCalibrate, false);
        Assert.True(result.FishFound);
        Assert.True(result.ReelProgressFound);
        Assert.True(result.HasLiveReel);
        Assert.True(result.BarFound);
        Assert.InRange(result.BarWidth, (int)(415 * ratio), (int)(440 * ratio));
    }

    [Fact]
    public void CompactDimSceneWithoutProgressDoesNotConfirmLiveReel()
    {
        using var image = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "reel_live_false_exit.png"));
        Assert.False(image.Empty());
        using (var progress = new Mat(image, new Rect(0, 210, image.Width, image.Height - 210))) progress.SetTo(Scalar.Black);
        using var result = new VisionProcessor().ProcessTrack(image, 383, 1001, 1353 / 1080.0, MinigameTheme.AutoCalibrate, false);
        Assert.False(result.ReelProgressFound); Assert.False(result.HasLiveReel); Assert.False(result.BarFound);
    }

    [Theory]
    [InlineData(MinigameTheme.AutoCalibrate)]
    [InlineData(MinigameTheme.Default)]
    [InlineData(MinigameTheme.Feline)]
    [InlineData(MinigameTheme.Golden)]
    [InlineData(MinigameTheme.Trident)]
    public void RecordedSceneryAtCaptureEdge_DoesNotEnterOrSustainReeling(MinigameTheme theme)
    {
        using var image = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "reel_absent_edge_glow.png"));
        Assert.False(image.Empty());
        var result = new VisionProcessor().ProcessTrack(image, 968, 1013, 1369 / 1080.0, theme, false);
        Assert.False(result.BarFound);
        Assert.False(result.ReelProgressFound);
        Assert.False(result.HasLiveReel);
        Assert.False(ReelEntryGuard.Confirm(() => result, _ => { }, CancellationToken.None));
    }

    [Theory]
    [InlineData(524)]
    [InlineData(525)]
    public void RecordedDimReel_RetainsControlTargetAndPreventsRecast(int frame)
    {
        using var image = Load(frame);
        var result = new VisionProcessor().ProcessTrack(image, 968, 1027, 1369 / 1080.0, generateDebug: false);
        Assert.True(result.ReelProgressFound);
        Assert.True(result.HasLiveReel);
        Assert.True(result.BarFound);
        Assert.InRange(result.BarLeft, 1020, 1030);
        Assert.InRange(result.BarWidth, 755, 775);
        Assert.True(ReelEntryGuard.Confirm(() => result, _ => { }, CancellationToken.None));
    }

    [Fact]
    public void DimSceneryWithoutProgress_DoesNotBecomeControlTarget()
    {
        using var image = Load(524);
        using var strip = new Mat(image, new Rect(0, 225, image.Width, image.Height - 225));
        strip.SetTo(Scalar.Black);
        var result = new VisionProcessor().ProcessTrack(image, 968, 1027, 1369 / 1080.0, generateDebug: false);
        Assert.False(result.ReelProgressFound);
        Assert.False(result.BarFound);
        Assert.False(result.HasLiveReel);
    }

    [Theory]
    [InlineData(720)]
    [InlineData(1080)]
    public void RecordedDimReel_ScalesWithViewportHeight(int viewportHeight)
    {
        using var image = Load(524);
        using var scaled = new Mat();
        double ratio = viewportHeight / 1369.0;
        Cv2.Resize(image, scaled, new Size((int)Math.Round(image.Width * ratio), (int)Math.Round(image.Height * ratio)));
        var result = new VisionProcessor().ProcessTrack(scaled, (int)Math.Round(968 * ratio),
            (int)Math.Round(1027 * ratio), viewportHeight / 1080.0, generateDebug: false);
        Assert.True(result.HasLiveReel);
        Assert.True(result.BarFound);
        Assert.InRange(result.BarWidth, (int)(750 * ratio), (int)(780 * ratio));
    }

    [Fact]
    public void ProgressWithoutNeedle_DoesNotConfirmReel()
    {
        Assert.False(ReelEntryGuard.Confirm(() => new DetectionResult { ReelProgressFound = true },
            _ => { }, CancellationToken.None));
        Assert.True(ReelEntryGuard.Confirm(() => new DetectionResult { ReelProgressFound = true, FishFound = true },
            _ => { }, CancellationToken.None));
    }

    private static Mat Load(int frame) => Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"reel_dim_bar_{frame}.png"));
}
