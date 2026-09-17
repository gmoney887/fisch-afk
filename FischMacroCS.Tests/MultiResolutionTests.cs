using System;
using System.IO;
using OpenCvSharp;
using FischMacroCS.Vision;
using FischMacroCS.Core;
using Xunit;

namespace FischMacroCS.Tests;

public class MultiResolutionTests
{
    private readonly VisionProcessor _vision = new();

    private static Mat GetUserScreenshot()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string fixturePath = Path.Combine(baseDir, "Fixtures", "user_screenshot.png");
        if (!File.Exists(fixturePath))
        {
            fixturePath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Fixtures", "user_screenshot.png"));
        }
        Assert.True(File.Exists(fixturePath), "Fixture must exist");
        var img = Cv2.ImRead(fixturePath);
        Assert.False(img.Empty());
        return img;
    }

    /// <summary>
    /// Constructs a realistic synthetic frame of any resolution (canvasW, canvasH)
    /// placing a properly scaled hotbar at the bottom center, surrounded by blue ocean water.
    /// If equipSlot1 is true, illuminates Slot 1 with Roblox vibrant blue/cyan highlight.
    /// </summary>
    private static Mat CreateSyntheticGameView(int canvasW, int canvasH, bool equipSlot1 = false)
    {
        using var rawFixture = GetUserScreenshot(); // 1024 x 428

        // Crop the hotbar region from the fixture: x=415..610, y=390..425
        int cropX = 415;
        int cropY = 390;
        int cropW = 195;
        int cropH = 35;
        using var hotbarCrop = new Mat(rawFixture, new Rect(cropX, cropY, cropW, cropH));

        // Roblox scales UI directly with viewport height relative to 428p base
        double scale = (double)canvasH / 428.0;
        int targetW = (int)Math.Round(cropW * scale);
        int targetH = (int)Math.Round(cropH * scale);

        using var scaledHotbar = new Mat();
        Cv2.Resize(hotbarCrop, scaledHotbar, new Size(targetW, targetH), 0, 0, InterpolationFlags.Linear);

        // Create canvas filled with typical ocean water color (B=166, G=108, R=80)
        Mat canvas = new Mat(new Size(canvasW, canvasH), MatType.CV_8UC3, new Scalar(166, 108, 80));

        // Place scaled hotbar centered at the bottom
        int destX = (canvasW / 2) - (targetW / 2);
        int destY = canvasH - targetH - Math.Max(2, (int)Math.Round(canvasH * 0.007));
        destX = Math.Clamp(destX, 0, canvasW - targetW);
        destY = Math.Clamp(destY, 0, canvasH - targetH);

        using var roi = new Mat(canvas, new Rect(destX, destY, targetW, targetH));
        scaledHotbar.CopyTo(roi);

        if (equipSlot1)
        {
            // Calculate slot 1 inside the placed scaled hotbar
            double slotW = targetW / 9.0;
            int s1X = destX + (int)Math.Round(slotW * 0.20);
            int s1W = (int)Math.Round(slotW * 0.60);
            int s1Y = destY + (int)Math.Round(targetH * 0.20);
            int s1H = (int)Math.Round(targetH * 0.60);

            for (int y = s1Y; y < s1Y + s1H; y++)
            {
                for (int x = s1X; x < s1X + s1W; x++)
                {
                    if (x >= 0 && x < canvasW && y >= 0 && y < canvasH)
                    {
                        canvas.Set(y, x, new Vec3b(240, 160, 20)); // Bright cyan BGR highlight
                    }
                }
            }
        }

        return canvas;
    }

    [Theory]
    [InlineData(1024, 428, "Compact Windowed")]
    [InlineData(1280, 720, "720p HD 16:9")]
    [InlineData(1920, 1080, "1080p FHD 16:9")]
    [InlineData(2560, 1440, "1440p QHD 16:9")]
    [InlineData(3840, 2160, "4K UHD 16:9")]
    [InlineData(1920, 1200, "16:10 WUXGA")]
    [InlineData(2560, 1080, "21:9 Ultrawide")]
    [InlineData(3440, 1440, "21:9 UWQHD")]
    [InlineData(5120, 1440, "32:9 Super Ultrawide")]
    public void DetectRodEquipped_MultiResolution_UnequippedStateCorrectlyIdentified(int width, int height, string label)
    {
        using var canvas = CreateSyntheticGameView(width, height, equipSlot1: false);

        var result = _vision.DetectRodEquipped(canvas, slotNum: 1, generateDebug: true);

        Assert.True(result.HotbarFound, $"Hotbar container must be dynamically located on {label} ({width}x{height})");
        Assert.False(result.IsEquipped, $"Rod must NOT be detected as equipped when unequipped on {label} ({width}x{height})");
        Assert.True(result.ActiveDensity < 0.045, $"Active density must be low on {label}, got {result.ActiveDensity:P2}");

        // Slot 1 must be within container
        Assert.True(result.SlotBounds.X >= result.HotbarBounds.X);
        Assert.True(result.SlotBounds.Right <= result.HotbarBounds.Right + 1);
        Assert.NotNull(result.AnnotatedFrame);
    }

    [Theory]
    [InlineData(1024, 428, "Compact Windowed")]
    [InlineData(1280, 720, "720p HD 16:9")]
    [InlineData(1920, 1080, "1080p FHD 16:9")]
    [InlineData(2560, 1440, "1440p QHD 16:9")]
    [InlineData(3840, 2160, "4K UHD 16:9")]
    [InlineData(1920, 1200, "16:10 WUXGA")]
    [InlineData(2560, 1080, "21:9 Ultrawide")]
    [InlineData(5120, 1440, "32:9 Super Ultrawide")]
    public void DetectRodEquipped_MultiResolution_EquippedStateCorrectlyIdentified(int width, int height, string label)
    {
        using var canvas = CreateSyntheticGameView(width, height, equipSlot1: true);

        var result = _vision.DetectRodEquipped(canvas, slotNum: 1, generateDebug: true);

        Assert.True(result.HotbarFound, $"Hotbar container must be dynamically located on {label} ({width}x{height})");
        Assert.True(result.IsEquipped, $"Rod MUST be detected as EQUIPPED when illuminated on {label} ({width}x{height})");
        Assert.True(result.ActiveDensity >= 0.045, $"Active density must be >= 4.5% on {label}, got {result.ActiveDensity:P2}");
        Assert.NotNull(result.AnnotatedFrame);
    }

    [Theory]
    [InlineData(720, 40)]
    [InlineData(1080, 60)]
    [InlineData(1440, 80)]
    [InlineData(2160, 120)]
    public void DynamicUISnap_MultiResolution_SnapsCorrectlyWithDrift(int height, int buttonSize)
    {
        int width = (int)Math.Round(height * (16.0 / 9.0));
        using var frame = new Mat(new Size(width, height), MatType.CV_8UC3, new Scalar(30, 30, 30));

        // Place a green button at expected location + drift
        int expectedX = width / 2;
        int expectedY = height / 2;
        int driftX = (int)Math.Round(buttonSize * 0.35);
        int driftY = (int)Math.Round(buttonSize * 0.25);
        int actualX = expectedX + driftX;
        int actualY = expectedY + driftY;

        // Draw green button
        Rect btnRect = new Rect(actualX - (buttonSize / 2), actualY - (buttonSize / 4), buttonSize, buttonSize / 2);
        Cv2.Rectangle(frame, btnRect, new Scalar(40, 200, 40), -1);

        var (found, snapped) = _vision.DynamicUISnapWithStatus(frame, expectedX, expectedY, VisionProcessor.UIColorType.GreenButton);

        Assert.True(found, $"Green button must be found on {width}x{height}");
        Assert.InRange(snapped.X, btnRect.X, btnRect.Right);
        Assert.InRange(snapped.Y, btnRect.Y, btnRect.Bottom);
    }

    [Theory]
    [InlineData(1920, 1080, 1.0)]
    [InlineData(2560, 1440, 1.33)]
    [InlineData(3840, 2160, 2.0)]
    [InlineData(1024, 428, 0.40)]
    public void ProcessTrack_MultiResolution_IsolatesNeedleCorrectly(int width, int height, double scaleFactor)
    {
        // Construct track crop (height ~17% of viewport, width ~50% of viewport)
        int trackW = (int)Math.Round(width * 0.50);
        int trackH = (int)Math.Round(height * 0.17);
        using var track = new Mat(new Size(trackW, trackH), MatType.CV_8UC3, new Scalar(40, 50, 60));

        // Draw catch bar (white)
        int barW = (int)Math.Max(30, Math.Round(80 * scaleFactor));
        int barH = (int)Math.Max(25, Math.Round(35 * scaleFactor));
        int barX = (trackW / 2) - (barW / 2);
        int barY = (trackH / 2) - (barH / 2);
        Cv2.Rectangle(track, new Rect(barX, barY, barW, barH), new Scalar(255, 255, 255), -1);

        // Draw fish needle (slate blue: B=100, G=80, R=65)
        int needleW = (int)Math.Max(6, Math.Round(14 * scaleFactor));
        int needleH = (int)Math.Max(20, Math.Round(32 * scaleFactor));
        int needleTargetX = barX + (int)Math.Round(barW * 0.65);
        int needleY = (trackH / 2) - (needleH / 2);
        Cv2.Rectangle(track, new Rect(needleTargetX - (needleW / 2), needleY, needleW, needleH), new Scalar(105, 80, 65), -1);

        var result = _vision.ProcessTrack(track, 0, 0, scaleFactor, MinigameTheme.Default, generateDebug: false);

        Assert.True(result.BarFound, $"Catch bar must be found at scale {scaleFactor}");
        Assert.True(result.FishFound, $"Fish needle must be found at scale {scaleFactor}");
        Assert.InRange(result.FishX, needleTargetX - (needleW / 2) - 3, needleTargetX + (needleW / 2) + 3);
    }

    [Theory]
    [InlineData(1920, 1080, "16:9 FHD")]
    [InlineData(2560, 1080, "21:9 Ultrawide")]
    [InlineData(3440, 1440, "21:9 UWQHD")]
    [InlineData(5120, 1440, "32:9 Super Ultrawide")]
    public void DetectCastBar_MultiAspectRatio_DetectsCenteredOverheadBar(int width, int height, string label)
    {
        using var fullFrame = new Mat(new Size(width, height), MatType.CV_8UC3, new Scalar(30, 25, 20));

        // Player avatar is at horizontal center (width / 2)
        int centerX = width / 2;
        int barTopY = (int)Math.Round(height * 0.35);

        // Draw green cap
        double scale = height / 1080.0;
        int capW = (int)Math.Round(24 * scale);
        int capH = (int)Math.Round(10 * scale);
        int capX = centerX - (capW / 2);
        Cv2.Rectangle(fullFrame, new Rect(capX, barTopY, capW, capH), new Scalar(40, 220, 60), -1);

        // Draw white power fill column
        int colW = (int)Math.Round(12 * scale);
        int colH = (int)Math.Round(120 * scale);
        int colX = centerX - (colW / 2);
        int colY = barTopY + capH;
        Cv2.Rectangle(fullFrame, new Rect(colX, colY, colW, colH), new Scalar(255, 255, 255), -1);

        var res = _vision.DetectCastBar(fullFrame, MinigameTheme.Default, false);

        Assert.True(res.Found, $"Overhead cast bar must be detected on {label} ({width}x{height})");
        Assert.InRange(res.FillPercent, 80.0, 100.0);
    }
}
