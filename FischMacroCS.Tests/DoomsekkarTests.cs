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

    [Theory]
    [InlineData("doom_live_08.png", 1799, 1225)]
    [InlineData("doom_live_10.png", 1355, 1686)]
    public void AutoCalibrate_LiveSessionFrames_ResolvesDoomsekkarAndAccurateTracking(string fixtureName, int expectedFishX, int expectedBarCenter)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName);
        Assert.True(File.Exists(path), "Fixture must exist: " + path);

        using var frame = Cv2.ImRead(path);
        int winW = frame.Width;
        int winH = frame.Height;
        double scaleFactor = winH / 1080.0;

        int halfW = (int)Math.Min(winW * 0.45, Math.Max(winH * 0.55, 450));
        halfW = Math.Min(halfW, (int)(winH * 0.65));
        int trackX1 = Math.Max(0, (winW / 2) - halfW);
        int trackX2 = Math.Min(winW, (winW / 2) + halfW);
        int trackY1 = (int)(winH * 0.74);
        int trackY2 = (int)(winH * 0.94);
        int trackW = trackX2 - trackX1;
        int trackH = trackY2 - trackY1;

        using var trackCrop = new Mat(frame, new Rect(trackX1, trackY1, trackW, trackH));
        var vision = new VisionProcessor();

        using var result = vision.ProcessTrack(trackCrop, trackX1, trackY1, scaleFactor, MinigameTheme.AutoCalibrate, generateDebug: true);

        output.WriteLine($"{fixtureName}: ResolvedTheme={result.ResolvedTheme}, HasLiveReel={result.HasLiveReel}, FishFound={result.FishFound}, FishX={result.FishX} (exp ~{expectedFishX}), BarFound={result.BarFound}, BarCenter={result.BarCenter:F1} (exp ~{expectedBarCenter}), BarWidth={result.BarWidth}");

        Assert.Equal(MinigameTheme.Doomsekkar, result.ResolvedTheme);
        Assert.True(result.HasLiveReel, "Must detect live reel");
        Assert.True(result.FishFound, "Must detect fish");
        Assert.True(result.BarFound, "Must detect control bar");
        Assert.InRange(result.FishX, expectedFishX - 45, expectedFishX + 45);
        Assert.InRange(result.BarCenter, expectedBarCenter - 30, expectedBarCenter + 30);
        Assert.NotNull(result.AnnotatedFrame);
    }

    [Fact]
    public void ApplyRodProfile_Doomsekkar_ConfiguresCorrectKinetics()
    {
        using var engine = new FishingEngine(new Settings());
        engine.ApplyRodProfile("Doomsekkar");
        Assert.Equal("Doomsekkar", engine.Config.RodProfile);
    }

    [Fact]
    public void Doomsekkar_PostCatchScenery_DoesNotDetectFalseReel()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "doom_live_hang.png");
        Assert.True(File.Exists(path), "Fixture must exist: " + path);

        using var img = Cv2.ImRead(path);
        int cropW = (int)(img.Height * 1.10);
        int cropH = (int)(img.Height * 0.20);
        int cropX = (img.Width - cropW) / 2;
        int cropY = (int)(img.Height * 0.74);
        using var crop = new Mat(img, new Rect(cropX, cropY, cropW, cropH));
        double scale = img.Height / 1080.0;

        var vp = new VisionProcessor();
        using var res = vp.ProcessTrack(crop, cropX, cropY, scale, MinigameTheme.Doomsekkar, generateDebug: false);
        Assert.False(res.HasLiveReel, "Must NOT detect live reel when minigame is gone!");
    }

    [Fact]
    public void AppVersion_MatchesCsprojVersionWithoutPreview()
    {
        var asm = typeof(FishingEngine).Assembly;
        var infoVerAttr = (System.Reflection.AssemblyInformationalVersionAttribute?)Attribute.GetCustomAttribute(asm, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
        string rawVer = infoVerAttr?.InformationalVersion?.Split('+')[0] ?? asm.GetName().Version?.ToString(3) ?? "1.0.0";
        Assert.DoesNotContain("preview", rawVer, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(asm.GetName().Version?.ToString(3), rawVer);
    }
}

