using System.IO;
using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;

namespace FischMacroCS.Tests;

public class RandomRodTests
{
    // Retained reel-region crops (968,1013) from the 3440x1369 session
    // session_20261116_024836_363_9d610db806794f3dba6885f2ef82a82a.
    private static Mat Frame(int id) => Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,
        "Fixtures", "random-rod", $"frame_{id:00000000}.png"));

    [Theory]
    [InlineData(MinigameTheme.AutoCalibrate, 1369)]
    [InlineData(MinigameTheme.Default, 1080)]
    [InlineData(MinigameTheme.RandomRod, 720)]
    public void FollowsEntryArrowOverlapEscapeAndExit(MinigameTheme theme, int height)
    {
        var vision = new VisionProcessor();
        double scale = height / 1369.0;
        int x = (int)Math.Round(968 * scale), y = (int)Math.Round(1013 * scale);
        foreach (var (id, left, right, fish) in new[] { (26,596,908,752), (28,596,908,752),
                     (40,470,783,752), (45,168,480,714), (54,58,370,657), (59,57,372,641), (62,0,0,0) })
        {
            using var source = Frame(id);
            using var crop = new Mat();
            Cv2.Resize(source, crop, new Size(), scale, scale);
            using var result = vision.ProcessTrack(crop, x, y, height / 1080.0, theme, false);
            if (id == 62) { Assert.False(result.HasLiveReel); continue; }
            Assert.True(result.ResolvedTheme == MinigameTheme.RandomRod && result.HasLiveReel &&
                Math.Abs(result.FishX - x - fish * scale) <= 3 &&
                Math.Abs(result.BarLeft - x - left * scale) <= 3 &&
                Math.Abs(result.BarRight - x - right * scale) <= 3,
                $"Frame {id} at {height}: {result.DetectorPath}, fish {result.FishX-x}, bar {result.BarLeft-x}..{result.BarRight-x}");
        }
    }

    [Fact]
    public void MeasuresWideSingleArrowBarIndependentlyOfPreviousReelWidth()
    {
        using var crop = Frame(324); // Earlier reel in session_20261116_022519.
        using var reading = new VisionProcessor().ProcessTrack(crop, 968, 1013, 1369 / 1080.0, MinigameTheme.AutoCalibrate, false);
        Assert.Equal(MinigameTheme.RandomRod, reading.ResolvedTheme);
        Assert.InRange(reading.BarLeft - 968, 509, 513);
        Assert.InRange(reading.BarRight - 968, 1416, 1420);
        Assert.InRange(reading.FishX - 968, 942, 946);
    }

    [Fact]
    public void MeasuresBlackProgressFromTheLeftEdge()
    {
        using var early = Frame(28);
        using var later = Frame(45);
        double? Fill(Mat crop) => VisionProcessor.MeasureReelProgressFill(crop, 1013, 1369, MinigameTheme.RandomRod);
        Assert.InRange(Fill(early) ?? -1, .038, .044);
        Assert.InRange(Fill(later) ?? -1, .090, .098);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RequiresFreshMarkerAndArrowEvidence(bool removeMarker)
    {
        var vision = new VisionProcessor();
        using var crop = Frame(28);
        DetectionResult Read() => vision.ProcessTrack(crop, 968, 1013, 1369 / 1080.0, MinigameTheme.RandomRod, false);
        using var first = Read();
        Assert.True(first.HasLiveReel);
        if (removeMarker)
            Cv2.Rectangle(crop, new Rect(742, 99, 20, 118), Scalar.White, -1);
        else
        {
            Cv2.Rectangle(crop, new Rect(614, 139, 47, 45), Scalar.White, -1);
            Cv2.Rectangle(crop, new Rect(844, 139, 47, 45), Scalar.White, -1);
        }
        using var next = Read();
        Assert.False(next.HasLiveReel);
    }
}
