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
        string fixturePath = Path.Combine(baseDir, "Fixtures", "hotbar_ultrawide_unequipped.png");
        if (!File.Exists(fixturePath))
        {
            fixturePath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Fixtures", "hotbar_ultrawide_unequipped.png"));
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

        // Distance between Slot 8 and Slot 9 must be ~1 slot width (~68px at 1080p)
        int deltaX = res9.SlotCenter.X - res8.SlotCenter.X;
        Assert.InRange(deltaX, 55, 75);

        // On 1080p (1920x1080), Slot 8 center is ~1164 and Slot 9 center is ~1232
        Assert.InRange(res8.SlotCenter.X, 1155, 1175);
        Assert.InRange(res9.SlotCenter.X, 1220, 1245);

        // Slot 9 center must be strictly to the right of Slot 8 right edge
        Assert.True(res9.SlotCenter.X > res8.SlotBounds.Right, "Slot 9 center must not fall inside Slot 8 bounds!");

        // Now draw an equipped white outline border around Slot 8 on canvas
        var s8 = res8.SlotBounds;
        Cv2.Rectangle(canvas, new Rect(s8.X, s8.Bottom - s8.Width, s8.Width, s8.Width), new Scalar(255, 255, 255), thickness: 2);

        // Slot 8 must now be detected as EQUIPPED
        var res8Equipped = _vision.DetectRodEquipped(canvas, slotNum: 8, generateDebug: false, fullViewportHeight: height);
        Assert.True(res8Equipped.IsEquipped, "Slot 8 MUST be detected as EQUIPPED when it has the white outline border!");

        // Slot 9 must remain detected as UNEQUIPPED
        var res9Unequipped = _vision.DetectRodEquipped(canvas, slotNum: 9, generateDebug: false, fullViewportHeight: height);
        Assert.False(res9Unequipped.IsEquipped, "Slot 9 MUST NOT be detected as equipped when only Slot 8 has the white outline border!");
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

    [Fact]
    public void Test_UserScreenshot_Slot8AndSlot9_CorrectEquippedState()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string fixturePath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "Fixtures", "media_1789705793153.jpg"));
        if (!File.Exists(fixturePath)) return;

        using var img = Cv2.ImRead(fixturePath);
        _output.WriteLine($"Image size: {img.Width}x{img.Height}");

        // In media_1789705793153.jpg, the hotbar is at Y=513..545 (height=32).
        // MidX is 512. Hotbar total width is 324 (9 * 36).
        // Slot 8 (Great Dream Waker) has the equipped white border!
        // Slot 9 (Herodian Jinglestar Rod) is UNEQUIPPED!

        // Let's test slot 8 vs slot 9 with dynamic detection
        int vpH = img.Height;
        int totalW = (int)Math.Round(vpH * 0.5667);
        double slotW = totalW / 9.0;
        int midX = img.Width / 2;
        int hotbarLeft = midX - (totalW / 2);

        // Slot 8
        int s8Left = hotbarLeft + (int)Math.Round(7 * slotW);
        int s8Right = hotbarLeft + (int)Math.Round(8 * slotW);
        _output.WriteLine($"Slot 8 bounds: {s8Left}..{s8Right}, Center={(s8Left+s8Right)/2}");

        // Slot 9
        int s9Left = hotbarLeft + (int)Math.Round(8 * slotW);
        int s9Right = hotbarLeft + (int)Math.Round(9 * slotW);
        _output.WriteLine($"Slot 9 bounds: {s9Left}..{s9Right}, Center={(s9Left+s9Right)/2}");

        // Slot 8 center should be around 620-623 (in Great Dream Waker)
        Assert.InRange((s8Left+s8Right)/2, 615, 626);
        // Slot 9 center should be around 655-660 (in Herodian Jinglestar Rod)
        Assert.InRange((s9Left+s9Right)/2, 650, 663);
        // Slot 9 center must be strictly outside Slot 8 bounds
        Assert.True((s9Left+s9Right)/2 > s8Right);

        // In the screenshot, the desktop monitor includes taskbar at Y=545..574.
        // The actual Roblox client area is Y=0..545.
        using var robloxClient = new Mat(img, new Rect(0, 0, img.Width, 545));

        var res9 = _vision.DetectRodEquipped(robloxClient, slotNum: 9, generateDebug: false, fullViewportHeight: 545);
        _output.WriteLine($"DetectRodEquipped Slot 9: IsEquipped={res9.IsEquipped}, SlotCenter={res9.SlotCenter}, SlotBounds={res9.SlotBounds}");
        Assert.False(res9.IsEquipped, "Slot 9 MUST NOT be detected as equipped when Great Dream Waker (slot 8) is equipped!");

        var res8 = _vision.DetectRodEquipped(robloxClient, slotNum: 8, generateDebug: false, fullViewportHeight: 545);
        _output.WriteLine($"DetectRodEquipped Slot 8: IsEquipped={res8.IsEquipped}, SlotCenter={res8.SlotCenter}, SlotBounds={res8.SlotBounds}");
        Assert.True(res8.IsEquipped, "Slot 8 MUST be detected as equipped (Great Dream Waker with white border)!");
    }
}
