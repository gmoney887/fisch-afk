using System;
using System.IO;
using OpenCvSharp;
using FischMacroCS.Core;
using FischMacroCS.Vision;
using Xunit;

namespace FischMacroCS.Tests;

public class ReelTrackingRegressionTests
{
    [Theory]
    [InlineData("reel_cyan_left.png", 1030, 1794, 1446)]
    [InlineData("reel_cyan_center.png", 1135, 1899, 1402)]
    public void CyanBarWithLiveReelEvidenceRemainsControllable(string fixture, int left, int right, int fish)
    {
        using var frame = Cv2.ImRead(GetFixturePath(fixture));
        var vision = new VisionProcessor();
        // Repeated frames exercise the automatically locked default theme too.
        for (int i = 0; i < 4; i++)
        {
            using var result = vision.ProcessTrack(frame, 968, 1013, 1369.0 / 1080,
                MinigameTheme.AutoCalibrate, false);
            Assert.True(result.HasLiveReel);
            Assert.True(result.BarFound);
            Assert.InRange(result.BarLeft, left - 5, left + 5);
            Assert.InRange(result.BarRight, right - 5, right + 5);
            Assert.InRange(result.FishX, fish - 5, fish + 5);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CyanShapeWithoutIndependentReelEvidenceIsNotAControlBar(bool removeProgress)
    {
        using var frame = Cv2.ImRead(GetFixturePath("reel_cyan_center.png"));
        var removed = removeProgress ? new Rect(0, 220, frame.Width, frame.Height - 220)
            : new Rect(420, 90, 30, 125);
        Cv2.Rectangle(frame, removed, Scalar.Black, -1);
        using var result = new VisionProcessor().ProcessTrack(frame, 968, 1013, 1369.0 / 1080,
            MinigameTheme.AutoCalibrate, false);
        Assert.False(result.BarFound);
    }

    private readonly VisionProcessor _vision = new();

    private static string GetFixturePath(string filename)
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string path = Path.Combine(baseDir, "Fixtures", filename);
        if (File.Exists(path)) return path;

        string srcPath = Path.Combine(baseDir, "..", "..", "..", "Fixtures", filename);
        return Path.GetFullPath(srcPath);
    }

    [Fact]
    public void DetectTrack_WideCatchBarWithArrow_ReturnsFoundAndAccurateBounds()
    {
        string fixturePath = GetFixturePath("reel_wide_bar_arrow.png");
        Assert.True(File.Exists(fixturePath), $"Fixture file not found: {fixturePath}");

        using var frame = Cv2.ImRead(fixturePath);
        Assert.False(frame.Empty(), "Fixture image failed to load");

        double estWinH = frame.Height / 0.17;
        double scaleFactor = estWinH / 1080.0;

        var res = _vision.ProcessTrack(frame, 0, 0, scaleFactor, MinigameTheme.Default, generateDebug: false);

        Assert.True(res.BarFound, "Wide catch bar with internal arrow indicator should be found");
        Assert.InRange(res.BarWidth, 700, 800);
        Assert.True(res.FishFound, "Fish needle should be detected");
        Assert.InRange(res.FishX, 230, 270);
    }

    [Fact]
    public void DetectTrack_NeedleOverIlluminatedWater_ReturnsFoundAndAccurateFishX()
    {
        string fixturePath = GetFixturePath("reel_needle_illuminated_water.png");
        Assert.True(File.Exists(fixturePath), $"Fixture file not found: {fixturePath}");

        using var frame = Cv2.ImRead(fixturePath);
        Assert.False(frame.Empty(), "Fixture image failed to load");

        double estWinH = frame.Height / 0.17;
        double scaleFactor = estWinH / 1080.0;

        var res = _vision.ProcessTrack(frame, 0, 0, scaleFactor, MinigameTheme.Default, generateDebug: false);

        Assert.True(res.BarFound, "Catch bar should be found");
        Assert.True(res.FishFound, "Fish needle should be detected despite background water noise floor");
        Assert.InRange(res.FishX, 300, 320);
    }

    [Fact]
    public void DetectTrack_SeraphicTiltedBar_ReturnsFoundAndAccurateBounds()
    {
        string fixturePath = GetFixturePath("reel_seraphic_tilted_bar.png");
        Assert.True(File.Exists(fixturePath), $"Fixture file not found: {fixturePath}");

        using var frame = Cv2.ImRead(fixturePath);
        Assert.False(frame.Empty(), "Fixture image failed to load");

        double estWinH = 1369.0;
        double scaleFactor = estWinH / 1080.0;

        var res = _vision.ProcessTrack(frame, 968, 1013, scaleFactor, MinigameTheme.Default, generateDebug: false);

        Assert.True(res.BarFound, "Seraphic wide catch bar tilted during screen shake should be found");
        Assert.InRange(res.BarWidth, 750, 780);
        Assert.True(res.FishFound, "Fish needle should be detected");
        Assert.InRange(res.FishX, 1640, 1680);
    }
}
