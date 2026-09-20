using System.IO;
using FischMacroCS.Vision;
using OpenCvSharp;

namespace FischMacroCS.Tests;

public class ShakeDetectionTests
{
    [Fact]
    public void DetectsLowerRightButtonInUserScreenshot()
    {
        using var frame = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "shake_lower_right.png"));
        Assert.False(frame.Empty());
        var vision = new VisionProcessor();
        var result = vision.DetectShakeIcon(frame, 0, 0, generateDebug: false);
        Assert.True(result.Found);
        Assert.InRange(result.Center.X, 1730, 1800);
        Assert.InRange(result.Center.Y, 1070, 1120);
    }

    [Theory]
    [InlineData(0, 0.15)]
    [InlineData(0, 0.78)]
    [InlineData(1, 0.15)]
    [InlineData(1, 0.78)]
    public void DetectsRecordedButtonAtWideViewportEdges(int side, double verticalPosition)
    {
        using var source = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "shake_lower_right.png"));
        Assert.False(source.Empty());
        using var button = new Mat(source, new Rect(1690, 1020, 155, 155));
        using var frame = new Mat(source.Height, 3440, MatType.CV_8UC3, Scalar.All(25));
        int x = side == 0 ? 10 : frame.Width - button.Width - 10;
        int y = (int)(frame.Height * verticalPosition);
        using (var destination = new Mat(frame, new Rect(x, y, button.Width, button.Height)))
            button.CopyTo(destination);
        var vision = new VisionProcessor();
        var result = vision.DetectShakeIcon(frame, 31, 47, generateDebug: false);
        Assert.True(result.Found);
        Assert.InRange(result.Center.X, x + 31 + 45, x + 31 + 110);
        Assert.InRange(result.Center.Y, y + 47 + 45, y + 47 + 110);
    }
}

