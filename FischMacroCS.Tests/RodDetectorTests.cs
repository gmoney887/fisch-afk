using System;
using System.IO;
using OpenCvSharp;
using FischMacroCS.Vision;
using Xunit;

namespace FischMacroCS.Tests;

public class RodDetectorTests
{
    private readonly VisionProcessor _vision = new();

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
        Assert.True(result.ActivePixels < 10, $"Active pixels should be low, got {result.ActivePixels}");
        Assert.NotNull(result.AnnotatedFrame);
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
        Assert.True(result.ActivePixels >= 10, $"Active pixels should be >= 10, got {result.ActivePixels}");
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
}
