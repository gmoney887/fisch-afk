using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;
using System.IO;

namespace FischMacroCS.Tests;

public class RuinousOathTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(90)]
    [InlineData(120)]
    [InlineData(160)]
    public void SameControlGeometryWorksAcrossUnseenHues(int hue)
    {
        using var frame = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "oath_red_34.png"));
        using var bar = new Mat(frame, new Rect(104, 118, 528, 72));
        using var hsv = new Mat();
        Cv2.CvtColor(bar, hsv, ColorConversionCodes.BGR2HSV);
        var channels = Cv2.Split(hsv);
        try
        {
            channels[0].SetTo(new Scalar(hue)); channels[1].SetTo(new Scalar(180));
            Cv2.Merge(channels, hsv);
            Cv2.CvtColor(hsv, bar, ColorConversionCodes.HSV2BGR);
        }
        finally { foreach (var channel in channels) channel.Dispose(); }
        using var result = new VisionProcessor().ProcessTrack(frame, 968, 1013, 1369.0 / 1080, MinigameTheme.AutoCalibrate, false);
        Assert.True(result.BarFound);
        Assert.InRange(result.BarLeft, 1060, 1084);
        Assert.InRange(result.BarRight, 1588, 1612);
    }

    [Theory]
    [InlineData(720)]
    [InlineData(1080)]
    public void GeometryScalesAndRequiresLiveReelEvidence(int height)
    {
        using var frame = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "oath_red_34.png"));
        double ratio = height / 1369.0;
        using var scaled = new Mat();
        Cv2.Resize(frame, scaled, new Size((int)Math.Round(frame.Width * ratio), (int)Math.Round(frame.Height * ratio)));
        using var result = new VisionProcessor().ProcessTrack(scaled, (int)(968 * ratio), (int)(1013 * ratio), height / 1080.0, MinigameTheme.AutoCalibrate, false);
        Assert.True(result.BarFound);
        Assert.InRange(result.BarLeft, (int)(1060 * ratio), (int)(1084 * ratio));
        Assert.InRange(result.BarRight, (int)(1588 * ratio), (int)(1612 * ratio));
        using (var meter = new Mat(scaled, new Rect(0, (int)(220 * ratio), scaled.Width, scaled.Height - (int)(220 * ratio)))) meter.SetTo(Scalar.Black);
        using var absent = new VisionProcessor().ProcessTrack(scaled, (int)(968 * ratio), (int)(1013 * ratio), height / 1080.0, MinigameTheme.AutoCalibrate, false);
        Assert.False(absent.BarFound);
        Assert.False(absent.HasLiveReel);
    }

    [Theory]
    [InlineData(21, 506, 961)]
    [InlineData(34, 104, 632)]
    [InlineData(45, 57, 585)]
    [InlineData(55, 57, 585)]
    public void RecordedRedBarWinsOverBrownScenery(int id, int left, int right)
    {
        using var frame = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"oath_red_{id}.png"));
        using var result = new VisionProcessor().ProcessTrack(frame, 968, 1013, 1369.0 / 1080, MinigameTheme.AutoCalibrate, false);
        Assert.True(result.BarFound);
        Assert.True(result.HasLiveReel);
        Assert.Equal(MinigameTheme.Ruinous, result.ResolvedTheme);
        Assert.InRange(result.BarLeft, 968 + left - 12, 968 + left + 12);
        Assert.InRange(result.BarRight, 968 + right - 12, 968 + right + 12);
        Assert.InRange(result.FishX, 1700, 1730);
    }

    [Fact]
    public void RuinousOathProfile_ConfiguresRodKinetics()
    {
        var engine = new FishingEngine(new Settings());
        engine.ApplyRodProfile("Ruinous Oath");
        Assert.Equal(680.0, engine.EstimatedRodPull);
    }

    [Theory]
    [InlineData("oath_green_238.png", 1098, 1358, 1835)]
    [InlineData("oath_slash_297.png", 1697, 1961, 1836)]
    [InlineData("oath_red_475.png", 1612, 1823, 1719)]
    [InlineData("oath_brown_484.png", 1035, 1246, 1440)]
    public void RuinousOath_DynamicPalettes_TrackAccurately(string fixture, int expLeft, int expRight, int expFishX)
    {
        using var frame = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        Assert.False(frame.Empty(), $"Fixture {fixture} must load");

        var vision = new VisionProcessor();
        using var result = vision.ProcessTrack(frame, 968, 1013, 1369.0 / 1080.0, MinigameTheme.Ruinous, false);

        Assert.True(result.HasLiveReel, $"Fixture {fixture} must detect live reel");
        Assert.True(result.BarFound, $"Fixture {fixture} must find bar");
        Assert.True(result.FishFound, $"Fixture {fixture} must find fish");
        Assert.InRange(result.BarLeft, expLeft - 25, expLeft + 25);
        Assert.InRange(result.BarRight, expRight - 25, expRight + 25);
        Assert.InRange(result.FishX, expFishX - 25, expFishX + 25);
    }

    [Fact]
    public void RuinousOath_AutoCalibrate_Sequence_DoesNotLockToDefaultAndLoseGreenOrBrownBar()
    {
        var vision = new VisionProcessor();

        // Step 1: Start with frame 210 from user session
        using var fStart = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "oath_start_210.png"));
        using var res1 = vision.ProcessTrack(fStart, 968, 1013, 1369.0 / 1080.0, MinigameTheme.AutoCalibrate, false);
        using var res2 = vision.ProcessTrack(fStart, 968, 1013, 1369.0 / 1080.0, MinigameTheme.AutoCalibrate, false);

        // Step 2: Bar shifts away to green (drifting)
        using var fGreen = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "oath_green_238.png"));
        using var resGreen = vision.ProcessTrack(fGreen, 968, 1013, 1369.0 / 1080.0, MinigameTheme.AutoCalibrate, false);

        // Must still find the green bar at ~1098..1358 and NOT lose it or report a false white ghost at 1829!
        Assert.True(resGreen.BarFound, "Must find green bar in AutoCalibrate after white frames");
        Assert.InRange(resGreen.BarLeft, 1070, 1125);
        // Step 3: Bar shifts to brown
        using var fBrown = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "oath_brown_484.png"));
        using var resBrown = vision.ProcessTrack(fBrown, 968, 1013, 1369.0 / 1080.0, MinigameTheme.AutoCalibrate, false);
        Assert.True(resBrown.BarFound, "Must find brown bar in AutoCalibrate");
        Assert.InRange(resBrown.BarLeft, 1010, 1060);
        Assert.InRange(resBrown.BarRight, 1220, 1270);
    }

    [Theory]
    [InlineData("oath_green_238.png", 1098, 1358, 1835)]
    [InlineData("oath_slash_297.png", 1697, 1961, 1836)]
    [InlineData("oath_red_475.png", 1612, 1823, 1719)]
    [InlineData("oath_brown_484.png", 1035, 1246, 1440)]
    public void RuinousOath_AutoCalibrate_IndividualFrames_ResolveThemeAndBounds(string fixture, int expLeft, int expRight, int expFishX)
    {
        using var frame = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        Assert.False(frame.Empty(), $"Fixture {fixture} must load");

        var vision = new VisionProcessor();
        using var result = vision.ProcessTrack(frame, 968, 1013, 1369.0 / 1080.0, MinigameTheme.AutoCalibrate, false);

        Assert.True(result.HasLiveReel, $"Fixture {fixture} must detect live reel");
        Assert.True(result.BarFound, $"Fixture {fixture} must find bar");
        Assert.True(result.FishFound, $"Fixture {fixture} must find fish");
        Assert.InRange(result.BarLeft, expLeft - 25, expLeft + 25);
        Assert.InRange(result.BarRight, expRight - 25, expRight + 25);
        Assert.InRange(result.FishX, expFishX - 25, expFishX + 25);
    }
}
