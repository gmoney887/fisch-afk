using System;
using System.IO;
using OpenCvSharp;
using FischMacroCS.Core;
using FischMacroCS.Vision;
using Xunit;

namespace FischMacroCS.Tests;

public class ReelTrackingRegressionTests
{
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
}
