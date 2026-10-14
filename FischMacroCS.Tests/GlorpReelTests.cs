using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;
using System.IO;

namespace FischMacroCS.Tests;

public class GlorpReelTests
{
    [Theory]
    [InlineData("glorp_lava_lime.png", 503, 989, 750)]
    [InlineData("glorp_lava_brown.png", 337, 824, 922)]
    [InlineData("glorp_lava_left.png", 57, 551, 669)]
    [InlineData("glorp_lava_avatar_lime.png", 509, 995, 750)]
    [InlineData("glorp_lava_avatar_brown.png", 269, 755, 806)]
    [InlineData("glorp_rock_brown.png", 510, 980, 1031)]
    [InlineData("glorp_rock_left.png", 508, 994, 751)]
    [InlineData("glorp_rock_right.png", 787, 1273, 1032)]
    [InlineData("glorp_strike_narrow.png", 257, 474, 470)]
    [InlineData("glorp_strike_tilt.png", 651, 864, 864)]
    public void GlorpBarIsNotMergedIntoTheRedLavaBackground(string name, int left, int right, int fish)
    {
        using var frame = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        using var result = new VisionProcessor().ProcessTrack(frame, 968, 1013, 1369.0 / 1080,
            MinigameTheme.AutoCalibrate, false);
        Assert.True(result.FishFound);
        Assert.True(result.ReelProgressFound);
        Assert.True(result.BarFound);
        Assert.Equal(MinigameTheme.Glorp, result.ResolvedTheme);
        Assert.InRange(result.BarLeft, 968 + left - 12, 968 + left + 12);
        Assert.InRange(result.BarRight, 968 + right - 12, 968 + right + 12);
        Assert.InRange(result.FishX, 968 + fish - 10, 968 + fish + 10);
    }
    [Theory]
    [InlineData("glorp_catch_avatar.png")]
    [InlineData("glorp_catch_avatar_shifted.png")]
    [InlineData("glorp_catch_sea_raven.png")]
    public void CompletedGlorpCatchDoesNotTrackTheAvatarAsAControlBar(string name)
    {
        using var frame = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
        var vision = new VisionProcessor();
        Assert.True(vision.DetectCatchNotification(frame, 1369));
        using var result = vision.ProcessTrack(frame, 968, 1013, 1369.0 / 1080,
            MinigameTheme.AutoCalibrate, false);
        Assert.False(result.HasLiveReel);
        Assert.False(result.BarFound);
    }
    [Theory]
    [InlineData(87, 768)]
    [InlineData(95, 735)]
    [InlineData(101, 635)]
    [InlineData(138, 504)]
    [InlineData(143, 562)]
    [InlineData(109, 512)]
    [InlineData(113, 476)]
    [InlineData(115, 455)]
    public void TrialNeedle(int id, int fish)
    {
        using var frame = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"glorp_trial_{id}.png"));
        using var result = new VisionProcessor().ProcessTrack(frame, 968, 1013, 1369.0 / 1080, MinigameTheme.AutoCalibrate, false);
        Assert.True(result.FishFound);
        Assert.True(result.BarFound);
        Assert.True(result.HasLiveReel);
        Assert.Equal(MinigameTheme.Glorp, result.ResolvedTheme);
        Assert.InRange(result.FishX, 968 + fish - 10, 968 + fish + 10);
    }
    [Theory]
    [InlineData(128, 968, 1013, 849, 1335, 765)]
    [InlineData(140, 968, 1013, 454, 940, 643)]
    [InlineData(155, 0, 1027, 1223, 1644, 1793)]
    [InlineData(165, 0, 1027, 1273, 1759, 1837)]
    public void RecordedGlorpTracksBothBarColors(int id, int x, int y, int left, int right, int fish)
    {
        using var frame = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"glorp_{id}.png"));
        Assert.False(frame.Empty());
        foreach (var theme in new[] { MinigameTheme.Glorp, MinigameTheme.AutoCalibrate })
        {
            using var result = new VisionProcessor().ProcessTrack(frame, x, y, 1369.0 / 1080, theme, false);
            Assert.True(result.ReelProgressFound);
            Assert.True(result.FishFound);
            Assert.True(result.BarFound);
            Assert.Equal(MinigameTheme.Glorp, result.ResolvedTheme);
            Assert.InRange(result.BarLeft, x + left - 12, x + left + 12);
            Assert.InRange(result.BarRight, x + right - 12, x + right + 12);
            Assert.InRange(result.FishX, x + fish - 8, x + fish + 8);
        }
    }

    [Theory]
    [InlineData(720, 128)]
    [InlineData(1080, 128)]
    [InlineData(720, 140)]
    [InlineData(1080, 140)]
    public void GlorpScalesAndRequiresProgress(int height, int id)
    {
        using var frame = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"glorp_{id}.png"));
        double ratio = height / 1369.0;
        using var scaled = new Mat();
        Cv2.Resize(frame, scaled, new Size((int)Math.Round(frame.Width * ratio), (int)Math.Round(frame.Height * ratio)));
        using var result = new VisionProcessor().ProcessTrack(scaled, (int)(968 * ratio), (int)(1013 * ratio), height / 1080.0, MinigameTheme.AutoCalibrate, false);
        Assert.True(result.BarFound, $"theme={result.ResolvedTheme} fish={result.FishFound} progress={result.ReelProgressFound}");
        Assert.True(result.FishFound);
        Assert.Equal(MinigameTheme.Glorp, result.ResolvedTheme);
        using (var progress = new Mat(scaled, new Rect(0, (int)(215 * ratio), scaled.Width, scaled.Height - (int)(215 * ratio)))) progress.SetTo(Scalar.Black);
        using var absent = new VisionProcessor().ProcessTrack(scaled, (int)(968 * ratio), (int)(1013 * ratio), height / 1080.0, MinigameTheme.Glorp, false);
        Assert.False(absent.HasLiveReel);
    }
}
