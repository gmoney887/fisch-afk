using FischMacroCS.Capture;
using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;
using System.IO;

namespace FischMacroCS.Tests;

public class OptimizationRegressionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AfkModePreservesPreferencesAcrossSerialization(bool preference)
    {
        var settings = new Settings { ShowVisionPreview = preference, AlwaysOnTop = preference,
            EnableHumanizedJitter = preference, EnableRecording = true };
        Assert.True(settings.AfkPerformanceMode);
        Assert.False(settings.PreviewEnabled);
        Assert.False(settings.JitterEnabled);
        Assert.False(settings.KeepOnTop);
        var restored = System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        restored.AfkPerformanceMode = false;
        Assert.Equal(preference, restored.PreviewEnabled);
        Assert.Equal(preference, restored.JitterEnabled);
        Assert.Equal(preference, restored.KeepOnTop);
        Assert.True(restored.EnableRecording);
    }

    [Fact]
    public void QueuedStopIsClearedByEmergencyStop()
    {
        using var engine = new FishingEngine(new Settings { EnableRecording = false });
        engine.IsStopQueued = true;
        engine.Stop();
        Assert.False(engine.IsStopQueued);
        Assert.False(engine.IsRunning);
    }

    [Fact]
    public void HoldingHotkeyOnlyDispatchesOnceAndInjectedKeysDoNotAlterPhysicalState()
    {
        var keys = new KeyEdgeTracker();
        Assert.True(keys.Observe(0x75, true));
        for (int repeat = 0; repeat < 100; repeat++) Assert.False(keys.Observe(0x75, true));
        Assert.False(keys.Observe(0x75, false, injected: true));
        Assert.False(keys.Observe(0x75, true));
        Assert.False(keys.Observe(0x75, false));
        Assert.True(keys.Observe(0x75, true));
        keys.Reset();
        Assert.True(keys.Observe(0x75, true));
    }

    [Fact]
    public void ColoredRectanglesDoNotCountAsCatch()
    {
        using var frame = new Mat(273, 1504, MatType.CV_8UC3, Scalar.Black);
        for (int i = 0; i < 12; i++) Cv2.Rectangle(frame, new Rect(240 + i * 35, 60, 10, 15), new Scalar(0, 200, 255), -1);
        Assert.False(new VisionProcessor().DetectCatchNotification(frame, 1369));
    }

    [Theory]
    [InlineData(720)]
    [InlineData(1080)]
    [InlineData(1440)]
    public void CatchPhraseMatchesAtScaledResolutions(int height)
    {
        using var source = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "reel_catch_banner.png"));
        using var scaled = new Mat();
        double scale = height / 1353.0;
        Cv2.Resize(source, scaled, new Size((int)Math.Round(source.Width * scale), (int)Math.Round(source.Height * scale)));
        Assert.True(new VisionProcessor().DetectCatchNotification(scaled, height));
    }

    [Theory]
    [InlineData(720)]
    [InlineData(1080)]
    [InlineData(1353)]
    public void SettledLiveCatchPhraseMatches(int height)
    {
        using var source = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "reel_catch_live.png"));
        using var scaled = new Mat();
        double scale = height / 1353.0;
        Cv2.Resize(source, scaled, new Size((int)Math.Round(source.Width * scale), (int)Math.Round(source.Height * scale)));
        Assert.True(new VisionProcessor().DetectCatchNotification(scaled, height));
    }

    [Theory]
    [InlineData(255, 0, 0)]
    [InlineData(0, 255, 0)]
    [InlineData(0, 0, 255)]
    public void AutoThemeTracksWhiteReelAgainstColoredScenery(int b, int g, int r)
    {
        var vision = new VisionProcessor();
        using var frame = new Mat(200, 1188, MatType.CV_8UC3, new Scalar(b, g, r));
        Cv2.Rectangle(frame, new Rect(250, 85, 500, 60), Scalar.White, -1);
        Cv2.Rectangle(frame, new Rect(440, 75, 10, 80), new Scalar(95, 80, 65), -1);
        for (int sample = 0; sample < 3; sample++)
        {
            using var result = vision.ProcessTrack(frame, 0, 0, 1, MinigameTheme.AutoCalibrate, false);
            Assert.True(result.HasLiveReel);
            Assert.Equal(MinigameTheme.Default, result.ResolvedTheme);
            Assert.InRange(result.FishX, 438, 452);
        }
    }

    [Fact]
    public void ReadyCatchCanFinishEarlyButUncertainOutcomeKeepsObservationWindow()
    {
        Assert.False(FishingCyclePolicy.CanFinalize(true, 1, 100));
        Assert.True(FishingCyclePolicy.CanFinalize(true, 2, 120));
        Assert.False(FishingCyclePolicy.CanFinalize(false, 2, 120));
        Assert.True(FishingCyclePolicy.CanFinalize(false, 2, 1500));
    }

    [Fact]
    public void DetectionDisposesOrTransfersItsImageExactlyOnce()
    {
        var owned = new Mat(10, 10, MatType.CV_8UC3);
        using (var detection = new DetectionResult { AnnotatedFrame = owned }) { }
        Assert.True(owned.IsDisposed);
        using var transferred = new Mat(10, 10, MatType.CV_8UC3);
        using (var detection = new DetectionResult { AnnotatedFrame = transferred })
            Assert.Same(transferred, detection.TakeAnnotatedFrame());
        Assert.False(transferred.IsDisposed);
    }

    [Fact]
    public void OcclusionChecksHandleNegativeMonitorCoordinatesAndTouchingEdges()
    {
        Assert.True(CaptureVisibility.HasOpaqueBounds(0));
        Assert.True(CaptureVisibility.HasOpaqueBounds(0x00080000));
        Assert.False(CaptureVisibility.HasOpaqueBounds(0x00080000 | 0x00000020));
        var capture = new Rect(-1200, 100, 600, 200);
        Assert.True(CaptureVisibility.Overlaps(capture, new Rect(-900, 150, 50, 50)));
        Assert.False(CaptureVisibility.Overlaps(capture, new Rect(-600, 100, 100, 100)));
        Assert.False(CaptureVisibility.Overlaps(capture, new Rect(0, 100, 100, 100)));
    }

    [Fact]
    public void LogRotatesDuringTheSameSession()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "runtime.log");
        try
        {
            using var logger = new SessionLogger(path, maxFileSizeBytes: 1024);
            for (int i = 0; i < 100; i++) logger.Log("STATE", "rotation " + i + new string('x', 40));
            logger.Flush();
            Assert.True(File.Exists(path + ".old"));
            Assert.InRange(new FileInfo(path).Length, 1, 1024);
            Assert.InRange(new FileInfo(path + ".old").Length, 1, 1024);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            Assert.Contains("rotation 99", reader.ReadToEnd());
            Assert.Null(logger.LastError);
        }
        finally { File.Delete(path); File.Delete(path + ".old"); Directory.Delete(directory); }
    }
}
