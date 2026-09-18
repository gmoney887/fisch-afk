using System;
using System.IO;
using OpenCvSharp;
using FischMacroCS.Vision;
using Xunit;

namespace FischMacroCS.Tests;

public class CatchVerificationTests
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
    public void DetectCatchNotification_CaughtBannerFixture_ReturnsTrue()
    {
        string fixturePath = GetFixturePath("reel_catch_banner.png");
        Assert.True(File.Exists(fixturePath), $"Fixture file not found: {fixturePath}");

        using var frame = Cv2.ImRead(fixturePath);
        Assert.False(frame.Empty(), "Fixture image failed to load");

        bool isCaught = _vision.DetectCatchNotification(frame);
        Assert.True(isCaught, "DetectCatchNotification should identify real caught banner");
    }

    [Fact]
    public void DetectCatchNotification_EmptyWaterOrMinigame_ReturnsFalse()
    {
        string fixturePath = GetFixturePath("reel_wide_bar_arrow.png");
        Assert.True(File.Exists(fixturePath), $"Fixture file not found: {fixturePath}");

        using var frame = Cv2.ImRead(fixturePath);
        Assert.False(frame.Empty(), "Fixture image failed to load");

        bool isCaught = _vision.DetectCatchNotification(frame);
        Assert.False(isCaught, "DetectCatchNotification should return false for active minigame without catch banner");
    }

    [Fact]
    public void DetectCatchNotification_NullOrSmallMat_ReturnsFalse()
    {
        using var smallMat = new Mat(20, 20, MatType.CV_8UC3, Scalar.All(0));
        Assert.False(_vision.DetectCatchNotification(smallMat));
        Assert.False(_vision.DetectCatchNotification(new Mat()));
    }
}
