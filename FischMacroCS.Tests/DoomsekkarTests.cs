using System;
using System.IO;
using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;
using Xunit;
using Xunit.Abstractions;

namespace FischMacroCS.Tests;

public class DoomsekkarTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(84, 1521, 514)]
    [InlineData(87, 1720, 514)]
    [InlineData(88, 1719, 514)]
    [InlineData(138, 1036, 514)]
    [InlineData(139, 1389, 514)]
    [InlineData(140, 1882, 514)]
    public void AutoCalibrate_DoomsekkarFrames_ResolvesThemeAndAccurateTracking(int frameNum, int expectedFishX, int expectedBarW)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", $"doomsekkar_frame_{frameNum}.png");
        Assert.True(File.Exists(path), "Fixture must exist: " + path);

        using var frame = Cv2.ImRead(path);
        double height = frame.Height;
        int cropY = (int)(height * 0.74);
        int cropH = (int)(height * 0.20);
        int cropW = (int)(height * 1.10);
        int cropX = (frame.Width - cropW) / 2;
        using var crop = new Mat(frame, new Rect(cropX, cropY, cropW, cropH));

        var vision = new VisionProcessor();
        using var result = vision.ProcessTrack(crop, cropX, cropY, height / 1080.0, MinigameTheme.AutoCalibrate, generateDebug: true);

        output.WriteLine($"Frame {frameNum}: ResolvedTheme={result.ResolvedTheme}, HasLiveReel={result.HasLiveReel}, FishFound={result.FishFound}, FishX={result.FishX} (exp ~{expectedFishX}), BarFound={result.BarFound}, BarLeft={result.BarLeft}, BarRight={result.BarRight}, BarWidth={result.BarWidth} (exp ~{expectedBarW}), BarCenter={result.BarCenter:F1}");

        Assert.Equal(MinigameTheme.Doomsekkar, result.ResolvedTheme);
        Assert.True(result.HasLiveReel, "Must detect live reel");
        Assert.True(result.FishFound, "Must detect pumpkin fish");
        Assert.True(result.BarFound, "Must detect control bar");
        Assert.InRange(result.FishX, expectedFishX - 25, expectedFishX + 25);
        Assert.InRange(result.BarWidth, expectedBarW - 40, expectedBarW + 40);
        Assert.NotNull(result.AnnotatedFrame);
    }

    [Theory]
    [InlineData(140, 1882, 514)]
    [InlineData(138, 1036, 514)]
    public void ExplicitTheme_Doomsekkar_ReturnsCorrectBounds(int frameNum, int expectedFishX, int expectedBarW)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", $"doomsekkar_frame_{frameNum}.png");
        using var frame = Cv2.ImRead(path);
        double height = frame.Height;
        int cropY = (int)(height * 0.74);
        int cropH = (int)(height * 0.20);
        int cropW = (int)(height * 1.10);
        int cropX = (frame.Width - cropW) / 2;
        using var crop = new Mat(frame, new Rect(cropX, cropY, cropW, cropH));

        var vision = new VisionProcessor();
        using var result = vision.ProcessTrack(crop, cropX, cropY, height / 1080.0, MinigameTheme.Doomsekkar, generateDebug: false);

        Assert.Equal(MinigameTheme.Doomsekkar, result.ResolvedTheme);
        Assert.True(result.FishFound);
        Assert.True(result.BarFound);
        Assert.InRange(result.FishX, expectedFishX - 25, expectedFishX + 25);
        Assert.InRange(result.BarWidth, expectedBarW - 40, expectedBarW + 40);
    }

    [Fact]
    public void ApplyRodProfile_Doomsekkar_ConfiguresCorrectKinetics()
    {
        using var engine = new FishingEngine(new Settings());
        engine.ApplyRodProfile("Doomsekkar");
        Assert.Equal("Doomsekkar", engine.Config.RodProfile);
    }
}
