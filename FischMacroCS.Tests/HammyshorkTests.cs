using System.IO;
using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;

namespace FischMacroCS.Tests;

public class HammyshorkTests
{
    // Original reel crops from session_20261109_224036_422_2c88b341762d43e79fab97b3e44bb079.
    // The 3440 x 1369 client captured these at (968, 1013).
    private static Mat Frame(int id) => Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,
        "Fixtures", "hammyshork-nov09", $"frame_{id:00000000}.png"));

    [Theory]
    [InlineData(MinigameTheme.Default, 1369)]
    [InlineData(MinigameTheme.AutoCalibrate, 1080)]
    [InlineData(MinigameTheme.Hammyshork, 720)]
    public void TracksBodyAndMarkerAcrossOverlapEscapeAndReelExit(MinigameTheme theme, int height)
    {
        var vision = new VisionProcessor();
        double scale = height / 1369.0;
        int offsetX = (int)Math.Round(968 * scale);
        int offsetY = (int)Math.Round(1013 * scale);
        foreach (var (id, left, right, fish) in new[] { (102, 462, 1087, 752), (197, 58, 683, 843),
                     (215, 59, 684, 1160), (233, 0, 0, 0) })
        {
            using var original = Frame(id);
            using var crop = new Mat();
            Cv2.Resize(original, crop, new Size(), scale, scale);
            using var result = vision.ProcessTrack(crop, offsetX, offsetY, height / 1080.0, theme, false);
            if (id == 233)
            {
                Assert.False(result.HasLiveReel);
                continue;
            }
            Assert.Equal(MinigameTheme.Hammyshork, result.ResolvedTheme);
            Assert.True(result.HasLiveReel && result.BarFound && result.ReelProgressFound);
            Assert.InRange(Math.Abs(result.BarLeft - offsetX - left * scale), 0, 3);
            Assert.InRange(Math.Abs(result.BarRight - offsetX - right * scale), 0, 3);
            Assert.InRange(Math.Abs(result.FishX - offsetX - fish * scale), 0, 2);
        }
    }

    // Consecutive reel crops from session_20261109_224208_393_417030f216844ccbb1019558d227e699.
    [Theory]
    [InlineData(MinigameTheme.AutoCalibrate, 1369)]
    [InlineData(MinigameTheme.AutoCalibrate, 1080)]
    [InlineData(MinigameTheme.Hammyshork, 720)]
    public void FollowsMovingFishWhileTeethCloseAndReopen(MinigameTheme theme, int height)
    {
        double scale = height / 1369.0;
        int x = (int)Math.Round(968 * scale), y = (int)Math.Round(1013 * scale);
        var vision = new VisionProcessor();
        foreach (var (id, left, fish) in new[] { (73, 644, 1044), (74, 652, 1025),
                     (75, 660, 881), (76, 666, 838), (77, 677, 788) })
        {
            using var original = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures",
                "hammyshork-bite", $"frame_{id:00000000}.png"));
            using var crop = new Mat();
            Cv2.Resize(original, crop, new Size(), scale, scale);
            using var result = vision.ProcessTrack(crop, x, y, height / 1080.0, theme, false);
            Assert.True(result.ResolvedTheme == MinigameTheme.Hammyshork && result.HasLiveReel, $"frame {id}");
            Assert.InRange(Math.Abs(result.FishX - x - fish * scale), 0, 3);
            Assert.InRange(Math.Abs(result.BarLeft - x - left * scale), 0, 3);
            Assert.InRange(Math.Abs(result.BarWidth - 625 * scale), 0, 4);
        }
    }

    [Theory]
    [InlineData(1369, MinigameTheme.AutoCalibrate)]
    [InlineData(1080, MinigameTheme.Hammyshork)]
    [InlineData(720, MinigameTheme.AutoCalibrate)]
    public void EmpyreanSlashKeepsTheCurrentFishAndBothBarCaps(int height, MinigameTheme theme)
    {
        // session_20261110_032133_612_632267f1ffa441e8b30e005a4cd9d8e4.
        // Frame 508 is the same reel region cropped from the full context.
        var vision = new VisionProcessor();
        double scale = height / 1369.0;
        int x = (int)Math.Round(968 * scale), y = (int)Math.Round(1013 * scale);
        foreach (var (id, left, fish) in new[] { (496, 265, 585), (504, 151, 406),
                     (505, 103, 444), (506, 65, 511), (508, 138, 646), (511, 308, 715),
                     (513, 353, 737), (520, 0, 0) })
        {
            using var original = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures",
                "hammyshork-empyrean", $"frame_{id:00000000}.png"));
            using var crop = new Mat();
            Cv2.Resize(original, crop, new Size(), scale, scale);
            using var result = vision.ProcessTrack(crop, x, y, height / 1080.0, theme, false);
            if (id == 520)
            {
                Assert.False(result.HasLiveReel);
                continue;
            }
            Assert.True(result.ResolvedTheme == MinigameTheme.Hammyshork && result.HasLiveReel &&
                Math.Abs(result.FishX - x - fish * scale) <= 3 &&
                Math.Abs(result.BarLeft - x - left * scale) <= 3 &&
                Math.Abs(result.BarRight - x - (left + 625) * scale) <= 3,
                $"frame {id} at {height}: {result.DetectorPath} fish {result.FishX - x}, bar {result.BarLeft - x}..{result.BarRight - x}");
        }
    }

    [Theory]
    [InlineData(243, 644, 792)] // 03:23 UTC, full-screen restore
    [InlineData(633, 143, 584)]
    [InlineData(471, 696, 881)] // 03:24 UTC, closing teeth
    [InlineData(978, 449, 790)] // 03:26 UTC, steep slash against a cyan beam
    [InlineData(989, 309, 642)]
    public void FullscreenEmpyreanReelsUseTheCurrentMarkerAndBar(int id, int left, int fish)
    {
        using var crop = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures",
            "hammyshork-empyrean", $"frame_{id:00000000}.png"));
        using var result = new VisionProcessor().ProcessTrack(crop, 928, 1065, 1440 / 1080.0, MinigameTheme.AutoCalibrate, false);
        Assert.True(result.ResolvedTheme == MinigameTheme.Hammyshork &&
            Math.Abs(result.FishX - 928 - fish) <= 3 && Math.Abs(result.BarLeft - 928 - left) <= 3 &&
            Math.Abs(result.BarWidth - 625) <= 4,
            $"frame {id}: {result.DetectorPath}, fish {result.FishX - 928}, bar {result.BarLeft - 928}..{result.BarRight - 928}");
    }

    [Theory]
    [InlineData(45, 152)]
    [InlineData(152, 240)]
    public void OneJawAloneCannotSupplyAMissingFishMarker(int top, int bottom)
    {
        using var crop = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures",
            "hammyshork-bite", "frame_00000075.png"));
        using var covered = crop.RowRange(top, bottom);
        covered.SetTo(Scalar.Black);
        using var result = new VisionProcessor().ProcessTrack(crop, 968, 1013, 1369 / 1080.0, MinigameTheme.Hammyshork, false);
        Assert.False(result.FishFound);
    }

    [Theory]
    [InlineData(825, 90, 32, 130)] // Marker outside the body
    [InlineData(55, 117, 631, 74)] // Translucent body at the left stop
    public void MissingComponentCannotReuseTheLastObservation(int x, int y, int width, int height)
    {
        var vision = new VisionProcessor();
        using var crop = Frame(197);
        using var first = vision.ProcessTrack(crop, 968, 1013, 1369 / 1080.0, MinigameTheme.Hammyshork, false);
        Assert.True(first.HasLiveReel);
        Cv2.Rectangle(crop, new Rect(x, y, width, height), Scalar.Black, -1);
        using var next = vision.ProcessTrack(crop, 968, 1013, 1369 / 1080.0, MinigameTheme.Hammyshork, false);
        Assert.False(next.HasLiveReel);
    }
}
