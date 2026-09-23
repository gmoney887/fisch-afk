using System;
using System.IO;
using OpenCvSharp;
using FischMacroCS.Vision;
using Xunit;

namespace FischMacroCS.Tests;

public class CatchVerificationTests
{
    [Theory]
    [InlineData("catch_1009_stacked.png")]
    [InlineData("catch_1009_second.png")]
    public void SecondPcPlayerCatchIsRecognized(string file)
    {
        using var frame = Cv2.ImRead(GetFixturePath(file));
        Assert.True(_vision.DetectCatchNotification(frame, 1009));
    }

    [Fact]
    public void SecondPcActiveReelDoesNotCountAsCatch()
    {
        using var frame = Cv2.ImRead(GetFixturePath("reel_1009_active.png"));
        Assert.False(_vision.DetectCatchNotification(frame, 1009));
    }
    [Fact]
    public void StackedPlayerAndCompanionRewardsKeepPlayerPrefixAtCropEdge()
    {
        using var source = Cv2.ImRead(GetFixturePath("catch_stacked_rewards.png"));
        Assert.False(source.Empty());
        Assert.True(_vision.DetectCatchNotification(source, 1353));
        using var companionOnly = new Mat(source, new Rect(0, 33, source.Width, source.Height - 33));
        Assert.False(_vision.DetectCatchNotification(companionOnly, 1353));
    }

    [Theory]
    [InlineData(1369)]
    [InlineData(1080)]
    [InlineData(720)]
    public void MaximizedPlayerCatchIsConfirmed(int viewportHeight)
    {
        using var source = Cv2.ImRead(GetFixturePath("catch_maximized.png"));
        Assert.False(source.Empty());
        double ratio = viewportHeight / 1369.0;
        using var scaled = new Mat();
        Cv2.Resize(source, scaled, new Size((int)Math.Round(source.Width * ratio), (int)Math.Round(source.Height * ratio)));
        Assert.True(_vision.DetectCatchNotification(scaled, viewportHeight));
        using var independent = Cv2.ImRead(GetFixturePath("catch_maximized_second.png"));
        Assert.False(independent.Empty());
        Cv2.Resize(independent, scaled, scaled.Size());
        Assert.True(_vision.DetectCatchNotification(scaled, viewportHeight));
        using var companion = Cv2.ImRead(GetFixturePath("companion_maximized_only.png"));
        Assert.False(companion.Empty());
        Cv2.Resize(companion, scaled, new Size((int)Math.Round(companion.Width * ratio), (int)Math.Round(companion.Height * ratio)));
        Assert.False(_vision.DetectCatchNotification(scaled, viewportHeight));
    }

    [Theory]
    [InlineData(1353)]
    [InlineData(1080)]
    [InlineData(720)]
    public void CompanionBonusDoesNotConfirmPlayerCatch(int viewportHeight)
    {
        using var source = Cv2.ImRead(GetFixturePath("companion_bonus_only.png"));
        Assert.False(source.Empty());
        double ratio = viewportHeight / 1353.0;
        using var scaled = new Mat();
        Cv2.Resize(source, scaled, new Size((int)Math.Round(source.Width * ratio), (int)Math.Round(source.Height * ratio)));
        Assert.False(_vision.DetectCatchNotification(scaled, viewportHeight));
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
