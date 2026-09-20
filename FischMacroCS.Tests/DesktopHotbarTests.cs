using FischMacroCS.Vision;
using OpenCvSharp;

namespace FischMacroCS.Tests;

public class DesktopHotbarTests
{
    [Theory]
    [InlineData(1718, 664, 1)]
    [InlineData(1920, 1080, 1)]
    [InlineData(3440, 1369, 1)]
    [InlineData(1718, 664, 2)]
    [InlineData(1718, 664, 9)]
    public void FixedSizeSelectedOutlineLocatesCorrectSlot(int width, int height, int selected)
    {
        using var frame = new Mat(height, width, MatType.CV_8UC3, new Scalar(35, 20, 15));
        int pitch = 69, side = 68;
        int left = width / 2 - (pitch * 8 + side) / 2;
        int top = height - side - 2;
        var selectedBounds = new Rect(left + (selected - 1) * pitch, top, side, side);
        Cv2.Rectangle(frame, selectedBounds, Scalar.White, 1);
        var vision = new VisionProcessor();
        var result = vision.DetectRodEquipped(frame, 1, fullViewportHeight: height);
        Assert.True(result.HotbarFound);
        Assert.Equal(selected == 1, result.IsEquipped);
        Assert.InRange(Math.Abs(result.SlotCenter.X - (left + side / 2)), 0, 2);
        using var roi = new Mat(frame, new Rect(0, height * 3 / 4, width, height - height * 3 / 4));
        var cropped = vision.DetectRodEquipped(roi, 1, fullViewportHeight: height);
        Assert.Equal(result.IsEquipped, cropped.IsEquipped);
        Assert.Equal(result.SlotCenter.X, cropped.SlotCenter.X);
    }

    [Fact]
    public void BlankFrameDoesNotInventHotbar()
    {
        using var frame = new Mat(664, 1718, MatType.CV_8UC3, Scalar.Black);
        var result = new VisionProcessor().DetectRodEquipped(frame, fullViewportHeight: 664);
        Assert.False(result.GeometryConfirmed);
        Assert.False(result.IsEquipped);
    }
}
