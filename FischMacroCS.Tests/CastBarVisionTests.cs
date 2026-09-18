using System;
using System.IO;
using OpenCvSharp;
using FischMacroCS.Core;
using FischMacroCS.Vision;
using Xunit;

namespace FischMacroCS.Tests;

public class CastBarVisionTests
{
    private readonly VisionProcessor _vision = new();
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public CastBarVisionTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
    }

    private string GetFixturePath(string filename)
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string path = Path.Combine(baseDir, "Fixtures", filename);
        if (File.Exists(path)) return path;

        // Fallback for direct source inspection
        string srcPath = Path.Combine(baseDir, "..", "..", "..", "Fixtures", filename);
        return Path.GetFullPath(srcPath);
    }

    [Fact]
    public void DetectCastBarROI_IdleNeonBoat_ReturnsNotFound()
    {
        // Regression test: User's real screen capture with bright neon boat decoration and water
        // Previously caused false-positive green cap trigger, jumping straight to Luring.
        string fixturePath = GetFixturePath("test_cast_roi.png");
        Assert.True(File.Exists(fixturePath), $"Fixture file not found: {fixturePath}");

        using var mat = Cv2.ImRead(fixturePath);
        Assert.False(mat.Empty(), "Fixture image failed to load");

        var res = _vision.DetectCastBarROI(mat, 428, 358, 107, MinigameTheme.Default, false);

        // Crucial regression assertion: Must NOT find a cast bar on an idle boat frame!
        Assert.False(res.Found, "Idle neon boat must not trigger a false-positive cast bar detection!");
        Assert.Equal(0.0, res.FillPercent);
    }

    [Theory]
    [InlineData(20, 40, 60)]    // Dark ocean blue
    [InlineData(40, 80, 120)]   // Tropical turquoise water
    [InlineData(10, 15, 20)]    // Deep night water
    [InlineData(80, 80, 80)]    // Ambient fog
    public void DetectCastBarROI_PlainWaterOrBackground_ReturnsNotFound(byte b, byte g, byte r)
    {
        // 300x200 uniform background
        using var frame = new Mat(200, 300, MatType.CV_8UC3, new Scalar(b, g, r));
        var res = _vision.DetectCastBarROI(frame, 1080, 500, 300, MinigameTheme.Default, false);

        Assert.False(res.Found);
        Assert.Equal(0.0, res.FillPercent);
    }

    [Theory]
    [InlineData(0.25)] // 25% fill
    [InlineData(0.50)] // 50% fill
    [InlineData(0.75)] // 75% fill
    [InlineData(0.95)] // 95% fill (near peak)
    [InlineData(0.99)] // 99% fill (peak sweet spot)
    public void DetectCastBarROI_SyntheticCastBar_AccuratelyMeasuresFill(double targetFillRatio)
    {
        // Programmatically generate a realistic Fisch cast bar:
        // Frame: 200x350 (Dark ocean backdrop)
        int frameW = 200;
        int frameH = 350;
        using var frame = new Mat(frameH, frameW, MatType.CV_8UC3, new Scalar(30, 25, 20));

        // 1. Draw Green Cap at Y=30, center X=100
        int capW = 20;
        int capH = 8;
        int capX = 100 - capW / 2;
        int capY = 30;
        // In Fisch, the cap is vivid green (BGR: 40, 220, 60)
        Cv2.Rectangle(frame, new Rect(capX, capY, capW, capH), new Scalar(40, 220, 60), -1);

        // 2. Bar column under cap:
        // Starts at capY + capH = 38
        // Ends at frameH - 1 = 349
        int barTopY = capY + capH;
        int barBottomY = frameH - 1;
        int totalBarHeight = barBottomY - barTopY;
        int colW = 10;
        int colX = 100 - colW / 2;

        // Draw dark container background
        Cv2.Rectangle(frame, new Rect(colX, barTopY, colW, totalBarHeight), new Scalar(15, 15, 15), -1);

        // 3. Draw White Power Fill rising from the bottom:
        int fillHeight = (int)Math.Round(totalBarHeight * targetFillRatio);
        int fillTopY = barBottomY - fillHeight;
        Cv2.Rectangle(frame, new Rect(colX, fillTopY, colW, fillHeight), new Scalar(255, 255, 255), -1);

        // Execute Detection
        var res = _vision.DetectCastBarROI(frame, 1080, 0, 0, MinigameTheme.Default, false);

        Assert.True(res.Found, $"Synthetic cast bar with {targetFillRatio * 100}% fill should be detected!");
        double expectedFillPercent = targetFillRatio * 100.0;
        Assert.InRange(res.FillPercent, expectedFillPercent - 3.0, expectedFillPercent + 3.0);
    }

    [Fact]
    public void DetectCastBarROI_ZoomedOutTinyCap_Detected()
    {
        // Test high-resolution or zoomed out billboard scaling:
        // Cap size 8x3 pixels
        int frameW = 200;
        int frameH = 300;
        using var frame = new Mat(frameH, frameW, MatType.CV_8UC3, new Scalar(30, 25, 20));

        int capW = 8;
        int capH = 3;
        int capX = 100 - capW / 2;
        int capY = 40;
        Cv2.Rectangle(frame, new Rect(capX, capY, capW, capH), new Scalar(40, 220, 60), -1);

        int barTopY = capY + capH;
        int barBottomY = frameH - 1;
        int totalBarHeight = barBottomY - barTopY;
        int colW = 4;
        int colX = 100 - colW / 2;

        // 50% fill
        int fillHeight = totalBarHeight / 2;
        int fillTopY = barBottomY - fillHeight;
        Cv2.Rectangle(frame, new Rect(colX, fillTopY, colW, fillHeight), new Scalar(255, 255, 255), -1);

        var res = _vision.DetectCastBarROI(frame, 1080, 0, 0, MinigameTheme.Default, false);

        Assert.True(res.Found);
        Assert.InRange(res.FillPercent, 45.0, 55.0);
    }

    [Fact]
    public void DetectCastBarROI_RealScreenshots_AccuratelyDistinguishesCastingFromIdle()
    {
        string dir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "screenshots", "casting"));
        if (!Directory.Exists(dir)) return;

        var files = Directory.GetFiles(dir, "*.png");
        foreach (var file in files)
        {
            using var full = Cv2.ImRead(file);
            if (full.Empty()) continue;

            string fn = Path.GetFileName(file);
            int winW = full.Width;
            int winH = full.Height;
            int roiHalfW = (int)Math.Round(winH * 0.28);
            int roiX = Math.Max(0, (winW / 2) - roiHalfW);
            int roiW = Math.Min(winW - roiX, roiHalfW * 2);
            int roiY = (int)Math.Round(winH * 0.25);
            int roiH = (int)Math.Round(winH * 0.50);

            using var roi = new Mat(full, new Rect(roiX, roiY, roiW, roiH));
            var res = _vision.DetectCastBarROI(roi, winH, roiX, roiY, MinigameTheme.Default, true);

            _output.WriteLine($"Screenshot: {fn} -> Found={res.Found}, Fill={res.FillPercent:F1}%, BarBounds={res.BarBounds}");

            if (fn.Contains("222731751"))
            {
                // Non-casting frame (minigame ended / idle player): must NOT detect a cast bar
                Assert.False(res.Found, $"Non-casting frame {fn} must not trigger cast bar detection!");
                Assert.Equal(0.0, res.FillPercent);
            }
            else
            {
                // Active casting frames (including text occlusions and clan tags): must detect cast bar
                Assert.True(res.Found, $"Cast bar must be detected in active casting frame {fn}!");
                Assert.InRange(res.FillPercent, 40.0, 100.0);
                Assert.True(res.BarBounds.Height >= 140, $"Bar height {res.BarBounds.Height} in {fn} must be >= 140px!");
                Assert.True(res.GreenY > 0, $"Green target Y must be positive in {fn}!");
            }
        }
    }
}
