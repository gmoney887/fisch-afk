using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;
using System.IO;

namespace FischMacroCS.Tests;

public class NatesBladeTests
{
    private static Mat Load(int id) => Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"nate_{id}.png"));
    private static DetectionResult Detect(Mat frame, int height = 1369) => new VisionProcessor().ProcessTrack(
        frame, (int)(968 * height / 1369.0), (int)(1013 * height / 1369.0), height / 1080.0, MinigameTheme.AutoCalibrate, false);

    [Theory]
    [InlineData(21, 1513, 1929, 1721)]
    [InlineData(24, 1031, 1447, 1818)]
    [InlineData(27, 1277, 1694, 1537)]
    [InlineData(28, 1121, 1538, 1474)]
    public void RecordedInlineFishAndControlAreTracked(int id, int left, int right, int fish)
    {
        using var frame = Load(id);
        using var result = Detect(frame);
        Assert.True(result.HasLiveReel);
        Assert.True(result.ReelProgressFound);
        Assert.InRange(result.BarLeft, left - 8, left + 8);
        Assert.InRange(result.BarRight, right - 8, right + 8);
        Assert.InRange(result.FishX, fish - 5, fish + 5);
    }

    [Theory]
    [InlineData(720)]
    [InlineData(1080)]
    [InlineData(2160)]
    public void InlineGeometryScalesWithViewport(int height)
    {
        using var frame = Load(24);
        using var scaled = new Mat();
        double ratio = height / 1369.0;
        Cv2.Resize(frame, scaled, new Size((int)Math.Round(frame.Width * ratio), (int)Math.Round(frame.Height * ratio)));
        using var result = Detect(scaled, height);
        Assert.True(result.HasLiveReel);
        Assert.InRange(result.BarLeft, (int)(1020 * ratio), (int)(1042 * ratio));
        Assert.InRange(result.BarRight, (int)(1436 * ratio), (int)(1458 * ratio));
        Assert.InRange(result.FishX, (int)(1810 * ratio), (int)(1826 * ratio));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(90)]
    [InlineData(120)]
    [InlineData(160)]
    public void InlineShapesDoNotDependOnRodHue(int hue)
    {
        using var frame = Load(24);
        using var hsv = new Mat();
        Cv2.CvtColor(frame, hsv, ColorConversionCodes.BGR2HSV);
        var channels = Cv2.Split(hsv);
        try
        {
            channels[0].SetTo(new Scalar(hue));
            Cv2.Merge(channels, hsv);
            Cv2.CvtColor(hsv, frame, ColorConversionCodes.HSV2BGR);
        }
        finally { foreach (var channel in channels) channel.Dispose(); }
        using var result = Detect(frame);
        Assert.True(result.HasLiveReel);
        Assert.InRange(result.BarLeft, 1020, 1042);
        Assert.InRange(result.BarRight, 1436, 1458);
        Assert.InRange(result.FishX, 1810, 1826);
    }

    [Theory]
    [InlineData("meter")]
    [InlineData("fish")]
    [InlineData("bar")]
    public void IncompleteShapesCannotStartReeling(string missing)
    {
        using var frame = Load(24);
        var box = missing switch
        {
            "meter" => new Rect(0, 226, frame.Width, frame.Height - 226),
            "fish" => new Rect(810, 75, 80, 135),
            _ => new Rect(50, 110, 435, 90)
        };
        using (var absent = new Mat(frame, box)) absent.SetTo(Scalar.Black);
        using var result = Detect(frame);
        Assert.False(result.HasLiveReel);
    }

    [Fact]
    public void PreReelEffectsAreNotAReel()
    {
        using var frame = Load(19);
        using var result = Detect(frame);
        Assert.False(result.HasLiveReel);
    }
}
