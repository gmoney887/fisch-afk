using System;
using System.IO;
using OpenCvSharp;
using FischMacroCS.Vision;
using Xunit;

namespace FischMacroCS.Tests;

public class RodDetectorTests
{
    private readonly VisionProcessor _vision = new();
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public RodDetectorTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void DetectRodEquipped_UserScreenshot_CorrectlyDetectsUnequipped()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string fixturePath = Path.Combine(baseDir, "Fixtures", "user_screenshot.png");
        if (!File.Exists(fixturePath))
        {
            fixturePath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Fixtures", "user_screenshot.png"));
        }
        Assert.True(File.Exists(fixturePath), "Fixture must exist");

        using var img = Cv2.ImRead(fixturePath);
        Assert.False(img.Empty());

        var result = _vision.DetectRodEquipped(img, slotNum: 1, generateDebug: true);

        Assert.True(result.HotbarFound, "Hotbar container should be found dynamically on screen");
        Assert.InRange(result.HotbarBounds.X, 415, 425);
        Assert.InRange(result.HotbarBounds.Right, 600, 610);
        Assert.InRange(result.SlotCenter.X, 428, 435);
        Assert.InRange(result.SlotCenter.Y, 400, 415);
        Assert.False(result.IsEquipped, "Rod MUST be detected as NOT equipped in user screenshot!");
        Assert.True(result.ActiveDensity < 0.045, $"Active density should be low (<4.5%), got {result.ActiveDensity:P2}");
        Assert.NotNull(result.AnnotatedFrame);
    }

    [Fact]
    public void DetectRodEquipped_RealUltrawideScreen_UnequippedSlot1_CorrectlyDetectsUnequipped()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string fixturePath = Path.Combine(baseDir, "Fixtures", "user_screen_latest.png");
        if (!File.Exists(fixturePath))
        {
            fixturePath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Fixtures", "user_screen_latest.png"));
        }
        Assert.True(File.Exists(fixturePath), "Fixture must exist");

        using var img = Cv2.ImRead(fixturePath);
        Assert.False(img.Empty());

        var result = _vision.DetectRodEquipped(img, slotNum: 1, generateDebug: true);
        Assert.False(result.IsEquipped, $"Slot 1 MUST be detected as UNEQUIPPED! Got ActiveDensity={result.ActiveDensity:P2}, ActivePx={result.ActivePixels}");

        // Now test cropped bottom region as PreFlightDiagnostic does:
        int clientH = img.Height;
        int bottomH = Math.Max(50, (int)Math.Round(clientH * 0.25));
        int bottomY = clientH - bottomH;
        using var bottomCrop = new Mat(img, new Rect(0, bottomY, img.Width, bottomH));
        var cropRes = _vision.DetectRodEquipped(bottomCrop, slotNum: 1, generateDebug: true, fullViewportHeight: clientH);
        Assert.False(cropRes.IsEquipped, $"Cropped bottom Slot 1 MUST be detected as UNEQUIPPED! Got ActiveDensity={cropRes.ActiveDensity:P2}, ActivePx={cropRes.ActivePixels}");
    }

    [Fact]
    public void DetectRodEquipped_EquippedFixture_CorrectlyDetectsEquipped()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string fixturePath = Path.Combine(baseDir, "Fixtures", "user_screenshot.png");
        if (!File.Exists(fixturePath))
        {
            fixturePath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Fixtures", "user_screenshot.png"));
        }
        Assert.True(File.Exists(fixturePath), "Fixture must exist");

        using var img = Cv2.ImRead(fixturePath);
        Assert.False(img.Empty());

        // Clone and illuminate Slot 1 with Roblox active selection highlight (vibrant blue/cyan)
        using var equippedImg = img.Clone();
        // Slot 1 is x=421..441, y=393..418
        for (int y = 398; y <= 412; y++)
        {
            for (int x = 425; x <= 437; x++)
            {
                equippedImg.Set(y, x, new Vec3b(240, 160, 20)); // Bright cyan BGR
            }
        }

        var result = _vision.DetectRodEquipped(equippedImg, slotNum: 1, generateDebug: true);

        Assert.True(result.HotbarFound);
        Assert.True(result.IsEquipped, "Illuminated slot must be detected as EQUIPPED!");
        Assert.True(result.ActiveDensity >= 0.045, $"Active density should be >= 4.5%, got {result.ActiveDensity:P2}");
        Assert.NotNull(result.AnnotatedFrame);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(9)]
    public void DetectRodEquipped_AllSlotsWithinContainerBounds(int slotNum)
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string fixturePath = Path.Combine(baseDir, "Fixtures", "user_screenshot.png");
        if (!File.Exists(fixturePath))
        {
            fixturePath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Fixtures", "user_screenshot.png"));
        }

        using var img = Cv2.ImRead(fixturePath);
        var result = _vision.DetectRodEquipped(img, slotNum, generateDebug: false);

        Assert.True(result.HotbarFound);
        Assert.True(result.SlotBounds.X >= result.HotbarBounds.X, $"Slot {slotNum} left edge must be within hotbar");
        Assert.True(result.SlotBounds.Right <= result.HotbarBounds.Right + 1, $"Slot {slotNum} right edge must be within hotbar");
        Assert.True(result.SlotCenter.X >= result.SlotBounds.X && result.SlotCenter.X <= result.SlotBounds.Right);
    }

    [Fact]
    public void DetectRodEquipped_Slot8AndSlot9_DistinctCentersAndNoCollision()
    {
        // 1920x1080 resolution
        int width = 1920;
        int height = 1080;
        using var canvas = new Mat(new Size(width, height), MatType.CV_8UC3, new Scalar(25, 25, 25));

        var res8 = _vision.DetectRodEquipped(canvas, slotNum: 8, generateDebug: false, fullViewportHeight: height);
        var res9 = _vision.DetectRodEquipped(canvas, slotNum: 9, generateDebug: false, fullViewportHeight: height);

        Assert.True(res8.HotbarFound);
        Assert.True(res9.HotbarFound);

        // Distance between Slot 8 and Slot 9 must be ~1 slot width (~52px at 1080p)
        int deltaX = res9.SlotCenter.X - res8.SlotCenter.X;
        Assert.InRange(deltaX, 45, 60);

        // Slot 9 center must be strictly to the right of Slot 8 right edge
        Assert.True(res9.SlotCenter.X > res8.SlotBounds.Right, "Slot 9 center must not fall inside Slot 8 bounds!");
    }

    [Fact]
    public void DetectRodEquipped_RealPCEquippedSlot1_CorrectlyDetectsEquipped()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string p1 = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Fixtures", "roblox_screenshot_184947.png"));
        if (!File.Exists(p1)) return;

        using var img1 = Cv2.ImRead(p1);
        var res1 = _vision.DetectRodEquipped(img1, slotNum: 1, generateDebug: true);
        Assert.True(res1.HotbarFound, "Hotbar must be found");
        Assert.True(res1.IsEquipped, "Slot 1 (Divine Champions Rod with white border) MUST be detected as EQUIPPED!");

        // Slot 2 must be detected as unequipped
        var res2 = _vision.DetectRodEquipped(img1, slotNum: 2);
        Assert.False(res2.IsEquipped, "Slot 2 (Equipment Bag without white border) MUST be detected as UNEQUIPPED!");
    }

    [Fact]
    public void TestShakeDetectionOnUserScreenshot()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string p1 = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Fixtures", "roblox_screenshot_184947.png"));
        if (!File.Exists(p1)) return;

        using var img1 = Cv2.ImRead(p1);
        double scale1 = img1.Height / 1080.0;
        var shake1 = _vision.DetectShakeIcon(img1, 0, 0, scale1, generateDebug: true);
        _output.WriteLine($"Shake 184947 Found: {shake1.Found}, Center: {shake1.Center}, Score: {shake1.ContrastScore:F2}, Box: {shake1.BoundingBox}");
        Assert.False(shake1.Found);

        string p2 = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Fixtures", "roblox_screenshot_190543.png"));
        if (File.Exists(p2))
        {
            using var img2 = Cv2.ImRead(p2);
            _output.WriteLine($"Image 190543 size: {img2.Width}x{img2.Height}");
            double scale2 = img2.Height / 1080.0;
            var shake2 = _vision.DetectShakeIcon(img2, 0, 0, scale2, generateDebug: true);
            _output.WriteLine($"Shake 190543 Found: {shake2.Found}, Center: {shake2.Center}, Score: {shake2.ContrastScore:F2}, Box: {shake2.BoundingBox}");
            Assert.False(shake2.Found);

            var rodEquipped = _vision.DetectRodEquipped(img2, 1, generateDebug: true, fullViewportHeight: img2.Height);
            _output.WriteLine($"Rod 190543 isEquipped: {rodEquipped.IsEquipped}, HotbarFound: {rodEquipped.HotbarFound}, HotbarBounds: {rodEquipped.HotbarBounds}, SlotBounds: {rodEquipped.SlotBounds}, SlotCenter: {rodEquipped.SlotCenter}, ActivePixels: {rodEquipped.ActivePixels}, Density: {rodEquipped.ActiveDensity:F4}");

            int clientH = img2.Height;
            int bottomH = Math.Max(50, (int)Math.Round(clientH * 0.25));
            int bottomY = clientH - bottomH;
            using var bottomCrop = new Mat(img2, new Rect(0, bottomY, img2.Width, bottomH));
            var cropRes = _vision.DetectRodEquipped(bottomCrop, slotNum: 1, generateDebug: true, fullViewportHeight: clientH);
            _output.WriteLine($"CropRes 190543 isEquipped: {cropRes.IsEquipped}, HotbarFound: {cropRes.HotbarFound}, HotbarBounds: {cropRes.HotbarBounds}, SlotBounds: {cropRes.SlotBounds}");

            // Debug contours of maskWhite
            int borderPad = Math.Max(2, (int)Math.Round(img2.Height * 0.003));
            var slotRect = rodEquipped.SlotBounds;
            int borderX = Math.Clamp(slotRect.X - borderPad, 0, img2.Width - 1);
            int borderY = Math.Clamp(slotRect.Y - borderPad, 0, img2.Height - 1);
            int borderW = Math.Clamp(slotRect.Width + (2 * borderPad), 1, img2.Width - borderX);
            int borderH = Math.Clamp(slotRect.Height + (2 * borderPad), 1, img2.Height - borderY);
            using var slotBoxMat = new Mat(img2, new Rect(borderX, borderY, borderW, borderH));
            using var slotBoxHsv = new Mat();
            Cv2.CvtColor(slotBoxMat, slotBoxHsv, ColorConversionCodes.BGR2HSV);
            using var maskWhite = new Mat();
            Cv2.InRange(slotBoxHsv, new Scalar(0, 0, 215), new Scalar(180, 40, 255), maskWhite);
            Cv2.FindContours(maskWhite, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            foreach (var cnt in contours)
            {
                Rect r = Cv2.BoundingRect(cnt);
                _output.WriteLine($"Contour Rect: {r}, SlotRect: {slotRect.Width}x{slotRect.Height}, Ratio: {(double)r.Width / slotRect.Width:F2}x{(double)r.Height / slotRect.Height:F2}");
            }
            Assert.True(rodEquipped.HotbarFound);
        }
    }
}
