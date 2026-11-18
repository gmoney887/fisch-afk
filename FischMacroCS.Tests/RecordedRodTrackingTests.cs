using System.IO;
using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;

namespace FischMacroCS.Tests;

public class RecordedRodTrackingTests
{
    [Theory]
    [InlineData("blade")]
    [InlineData("oath")]
    public void AutoCalibrationTracksTheVisibleMarkerAndEntireMovingBar(string rod)
    {
        // Absolute pixel coordinates annotated on the Nov 4 gameplay captures.
        // Reuse the detector across frames, just as the reel worker does.
        (int Id, int Left, int Right, int Fish)[] samples = rod == "blade"
            ? [(57, 1388, 2107, 2004), (61, 1128, 1894, 2062), (64, 1412, 2203, 2097),
               (74, 1084, 2001, 2176), (179, 1337, 1816, 1720)]
            : [(236, 1373, 1764, 1720), (239, 1270, 1793, 1720), (258, 1658, 2185, 1720),
               (264, 1573, 2101, 1720), (276, 1260, 1787, 1720)];
        var vision = new VisionProcessor();
        var failures = new List<string>();
        foreach (var (id, left, right, fish) in samples)
        {
            using var frame = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "nov04-reel", $"{rod}_{id}.png"));
            Assert.Equal(new Size(1504, 273), frame.Size());
            using var reading = vision.ProcessTrack(frame, 968, 1013, 1369.0 / 1080, MinigameTheme.AutoCalibrate, false);
            if (!reading.BarFound || !reading.FishFound || !reading.ReelProgressFound ||
                Math.Abs(reading.BarLeft - left) > 10 || Math.Abs(reading.BarRight - right) > 10 ||
                Math.Abs(reading.FishX - fish) > 6)
                failures.Add($"{id}: expected {left}..{right}, fish {fish}; got {reading.BarLeft}..{reading.BarRight}, fish {reading.FishX}, meter {reading.ReelProgressFound}, {reading.ResolvedTheme}");
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
