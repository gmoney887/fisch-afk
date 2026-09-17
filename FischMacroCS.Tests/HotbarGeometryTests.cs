using System;
using Xunit;

namespace FischMacroCS.Tests;

public class HotbarGeometryTests
{
    public static readonly object[][] StandardViewports = new object[][]
    {
        // 16:9 Standard & High DPI
        new object[] { 1280, 720, "720p HD" },
        new object[] { 1920, 1080, "1080p Full HD" },
        new object[] { 2560, 1440, "1440p QHD" },
        new object[] { 3840, 2160, "4K UHD" },

        // 16:10 Productivity
        new object[] { 1920, 1200, "1920x1200 16:10" },
        new object[] { 2560, 1600, "2560x1600 16:10" },

        // 21:9 Ultrawide
        new object[] { 2560, 1080, "2560x1080 Ultrawide" },
        new object[] { 3440, 1440, "3440x1440 Ultrawide" },
        new object[] { 5120, 2160, "5K2K Ultrawide" },

        // 32:9 Super Ultrawide
        new object[] { 5120, 1440, "32:9 Super Ultrawide" },

        // Compact / Windowed
        new object[] { 1024, 768, "1024x768 4:3" },
        new object[] { 1024, 428, "1024x428 Compact Windowed" }
    };

    [Theory]
    [MemberData(nameof(StandardViewports))]
    public void HotbarSlots_RemainStrictlyWithinScreenBounds(int clientW, int clientH, string label)
    {
        for (int slotNum = 1; slotNum <= 9; slotNum++)
        {
            double offsetRatio = (slotNum - 5) * 0.05607;
            int slotClientX = (clientW / 2) + (int)Math.Round(clientH * offsetRatio);
            int slotClientY = (clientH / 2) + (int)Math.Round(clientH * 0.4509);

            // Slot X must be safely within the horizontal bounds of the Roblox window
            Assert.True(slotClientX > 20, $"[{label}] Slot {slotNum} X={slotClientX} is too close to left border");
            Assert.True(slotClientX < clientW - 20, $"[{label}] Slot {slotNum} X={slotClientX} exceeds right border ({clientW})");

            // Slot Y must be docked near the bottom edge (bottom 10-15%)
            Assert.True(slotClientY >= (int)(clientH * 0.85), $"[{label}] Slot {slotNum} Y={slotClientY} should be in bottom hotbar region");
            Assert.True(slotClientY < clientH, $"[{label}] Slot {slotNum} Y={slotClientY} exceeds bottom border ({clientH})");
        }
    }

    [Theory]
    [MemberData(nameof(StandardViewports))]
    public void HotbarSlots_SymmetricAndEquallySpaced(int clientW, int clientH, string label)
    {
        // Slot 5 must be dead center
        int slot5X = (clientW / 2) + (int)Math.Round(clientH * (5 - 5) * 0.05607);
        Assert.True(slot5X == clientW / 2, $"[{label}] Slot 5 must be dead center");

        // Distance between consecutive slots must be uniform
        int previousX = -1;
        int? expectedDelta = null;

        for (int slotNum = 1; slotNum <= 9; slotNum++)
        {
            double offsetRatio = (slotNum - 5) * 0.05607;
            int currentX = (clientW / 2) + (int)Math.Round(clientH * offsetRatio);

            if (previousX >= 0)
            {
                int delta = currentX - previousX;
                if (!expectedDelta.HasValue)
                {
                    expectedDelta = delta;
                }
                else
                {
                    // Due to rounding, delta should vary by at most 1 pixel
                    Assert.InRange(delta, expectedDelta.Value - 1, expectedDelta.Value + 1);
                }
            }
            previousX = currentX;
        }
    }

    [Theory]
    [MemberData(nameof(StandardViewports))]
    public void IsRodEquipped_SamplingRoiStaysWithinCanvas(int clientW, int clientH, string label)
    {
        for (int slotNum = 1; slotNum <= 9; slotNum++)
        {
            double offsetRatio = (slotNum - 5) * 0.05607;
            int slotClientX = (clientW / 2) + (int)Math.Round(clientH * offsetRatio);
            int slotClientY = (clientH / 2) + (int)Math.Round(clientH * 0.4509);

            int boxHalfW = (int)Math.Round(clientH * 0.024);
            int topBorderY = slotClientY - (int)Math.Round(clientH * 0.033);

            int roiX = Math.Max(0, slotClientX - boxHalfW);
            int roiY = Math.Max(0, topBorderY - 4);
            int roiW = boxHalfW * 2;
            int roiH = 8;

            Assert.True(roiX >= 0);
            Assert.True(roiY >= 0);
            Assert.True(roiX + roiW <= clientW, $"[{label}] Hotbar slot {slotNum} ROI extends past window width");
            Assert.True(roiY + roiH <= clientH, $"[{label}] Hotbar slot {slotNum} ROI extends past window height");
        }
    }

    [Fact]
    public void IsRodEquipped_HotbarFixture_DistinguishesActiveSlot()
    {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string fixturePath = System.IO.Path.Combine(baseDir, "Fixtures", "hotbar_crop.png");
        if (!System.IO.File.Exists(fixturePath))
        {
            fixturePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(baseDir, "..", "..", "..", "Fixtures", "hotbar_crop.png"));
        }
        Assert.True(System.IO.File.Exists(fixturePath));

        using var crop = OpenCvSharp.Cv2.ImRead(fixturePath);
        Assert.False(crop.Empty());

        // In hotbar_crop.png, each slot is ~24px wide. Slot 1 is at x=0..24, Slot 2 at x=24..48, etc.
        // Test Slot 1 (active/equipped with blue selection background):
        int slot1Cyan = 0;
        for (int y = 12; y <= 20; y++)
        {
            for (int x = 4; x <= 20; x++)
            {
                var bgra = crop.At<OpenCvSharp.Vec3b>(y, x);
                if (bgra.Item0 > 130 && bgra.Item0 > bgra.Item2 + 25) slot1Cyan++;
            }
        }

        // Test Slot 2 (inactive dark container):
        int slot2Cyan = 0;
        for (int y = 12; y <= 20; y++)
        {
            for (int x = 28; x <= 44; x++)
            {
                var bgra = crop.At<OpenCvSharp.Vec3b>(y, x);
                if (bgra.Item0 > 130 && bgra.Item0 > bgra.Item2 + 25) slot2Cyan++;
            }
        }

        // Slot 1 must have strong active selection highlight, Slot 2 must have zero
        Assert.True(slot1Cyan >= 20, $"Slot 1 selection count: {slot1Cyan}");
        Assert.Equal(0, slot2Cyan);
    }
}
