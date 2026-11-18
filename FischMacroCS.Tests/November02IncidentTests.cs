using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;

namespace FischMacroCS.Tests;

public partial class FishingEngineReplayTests
{
    private sealed class NovemberRecording : IDisposable
    {
        public sealed record Sample(int Id, double Ms, Rect Region, Mat Pixels);
        public List<Sample> Samples { get; } = new();
        private Mat? _canvas;
        private int _rendered = -1;
        public NovemberRecording(string prefix)
        {
            string root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "nov02");
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "sequence.json")));
            long? start = null;
            foreach (var entry in json.RootElement.EnumerateArray())
            {
                string file = entry.GetProperty("File").GetString()!;
                if (!file.StartsWith(prefix + "_", StringComparison.Ordinal)) continue;
                string path = Path.Combine(root, file);
                Assert.Equal(entry.GetProperty("SHA256").GetString(), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
                var pixels = Cv2.ImRead(path);
                Assert.False(pixels.Empty(), file);
                var r = entry.GetProperty("Region");
                var region = new Rect(r.GetProperty("X").GetInt32(), r.GetProperty("Y").GetInt32(),
                    r.GetProperty("Width").GetInt32(), r.GetProperty("Height").GetInt32());
                Assert.Equal(region.Size, pixels.Size());
                long timestamp = entry.GetProperty("Timestamp").GetInt64();
                start ??= timestamp;
                int id = entry.GetProperty("FrameId").GetInt32();
                Samples.Add(new(id, (timestamp - start.Value) / 10000.0, region, pixels));
            }
            Assert.NotEmpty(Samples); // Missing recordings must fail, never silently pass or skip.
        }
        public Mat Render(double ms)
        {
            // Preserve recorded order/times. Sparse frames are held between captures;
            // pixels outside each new ROI retain the last full context, not live evidence.
            _canvas ??= new Mat(1369, 3440, MatType.CV_8UC3, Scalar.All(35));
            while (_rendered + 1 < Samples.Count && Samples[_rendered + 1].Ms <= ms)
            {
                var sample = Samples[++_rendered];
                using var roi = new Mat(_canvas, sample.Region);
                sample.Pixels.CopyTo(roi);
            }
            return _canvas.Clone();
        }
        public void Dispose() { _canvas?.Dispose(); foreach (var sample in Samples) sample.Pixels.Dispose(); }
    }

    [Fact]
    public void November02RecordedTintedReelRemainsLiveThroughFalseExit()
    {
        using var recording = new NovemberRecording("oath");
        var vision = new VisionProcessor();
        var failures = new List<string>();
        foreach (var sample in recording.Samples)
        {
            using var full = recording.Render(sample.Ms);
            using var roi = new Mat(full, new Rect(968, 1013, 1504, 273));
            using var result = vision.ProcessTrack(roi, 968, 1013, 1369.0 / 1080, MinigameTheme.AutoCalibrate, false);
            // Frame176 is the entrance animation: the meter is still below the
            // capture band. From177 onward the reviewed reel is fully visible.
            if (sample.Id == 176) continue;
            if (!result.HasLiveReel)
                failures.Add($"frame {sample.Id}: bar={result.BarFound}, fish={result.FishFound}, meter={result.ReelProgressFound}, theme={result.ResolvedTheme}");
            if (sample.Id is 194 or 208)
            {
                Assert.True(result.BarFound, $"frame {sample.Id}: unobscured control must be located");
                Assert.InRange(result.BarLeft, sample.Id == 194 ? 1499 : 1046, sample.Id == 194 ? 1519 : 1066);
                Assert.InRange(result.BarRight, sample.Id == 194 ? 1927 : 1578, sample.Id == 194 ? 1947 : 1598);
                Assert.InRange(result.FishX, 1716, 1724);
            }
            Assert.False(vision.DetectCatchNotification(roi, 1369));
        }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void November02RecordedClownfishBannerIsRecognizedOnIndependentFrames()
    {
        using var recording = new NovemberRecording("catch");
        var vision = new VisionProcessor();
        var detections = new List<string>();
        int consecutive = 0;
        foreach (var sample in recording.Samples)
        {
            if (sample.Id == 99) continue; // Template source; never count it as held-out validation.
            using var full = recording.Render(sample.Ms);
            using var roi = new Mat(full, new Rect(968, 1013, 1504, 273));
            bool found = vision.DetectCatchNotification(roi, 1369);
            if (sample.Id <= 83) Assert.False(found, $"Active reel {sample.Id} is not a catch.");
            detections.Add($"frame {sample.Id} at {sample.Ms:F0}ms: {found}");
            consecutive = found ? consecutive + 1 : 0;
        }
        Assert.True(consecutive >= 2, string.Join(Environment.NewLine, detections));
    }

    [Theory]
    [InlineData("oath", 20)]
    [InlineData("catch", 20)]
    [InlineData("catch", 70)]
    public async Task November02RecordedTimelinePreservesLiveReelAndCountsCatch(string sequence, int cadence)
    {
        using var recording = new NovemberRecording(sequence);
        var clock = new Clock { CaptureMs = cadence };
        var input = new Input(clock);
        var desktop = new Desktop { ClientWidth = 3440, ClientHeight = 1369 };
        long start = clock.Timestamp;
        double end = recording.Samples[^1].Ms;
        var prematureStates = new List<string>();
        bool enteredReel = false, resumedCasting = false;
        FishingEngine? engine = null;
        using var frames = new Frames(() =>
        {
            double elapsed = clock.Timestamp - start;
            if (engine is not null)
            {
                enteredReel |= engine.CurrentState == MacroState.Reeling;
                if (sequence == "oath" && enteredReel && engine.CurrentState is MacroState.PostCatch or MacroState.Casting)
                    prematureStates.Add($"{elapsed}ms: {engine.CurrentState}");
                resumedCasting |= engine.TotalCatches == 1 && engine.CurrentState == MacroState.Casting;
            }
            return recording.Render(elapsed);
        }, clock);
        using var running = engine = new FishingEngine(new Settings { EnableRecording = false,
            EnableAutoClaimAquarium = false, EnableAntiAfk = false, EnableWatchdogRecovery = false,
            ShakeMode = "Disabled", SelectedTheme = MinigameTheme.AutoCalibrate },
            frames, frames, clock: clock, desktop: desktop, hardware: input);
        clock.Tick = () =>
        {
            if (clock.Timestamp - start > end + (sequence == "catch" ? 4000 : 500) || resumedCasting) engine.Stop();
        };
        engine.Start();
        try { await engine.Completion.WaitAsync(TimeSpan.FromSeconds(45)); }
        finally { engine.Stop(); }
        Assert.True(enteredReel);
        Assert.Empty(prematureStates);
        Assert.Equal(sequence == "catch" ? 1 : 0, engine.TotalCatches);
        Assert.Equal(0, engine.UnknownCatches);
        Assert.Equal(0, engine.TotalFails);
        Assert.Equal(0, engine.WatchdogRecoveryCount);
        if (sequence == "catch") Assert.True(resumedCasting, "The real catch must finish verification and permit another cast, without a scene cooldown.");
        Assert.False(input.Held);
        Assert.Empty(input.HeldKeys);
    }

    [Fact]
    public async Task November02DelayedCatchDuringSceneChangeFinishesVerificationBeforeRecovery()
    {
        // Controlled timing boundary, separate from the unchanged recorded timeline:
        // change camera landmarks at5s, end reel at10s, reveal the real banner at11.2s.
        // This puts the banner inside catch verification but after scene-only grace.
        var clock = new Clock { CaptureMs = 20 };
        var input = new Input(clock);
        string root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "nov02");
        using var reel = Cv2.ImRead(Path.Combine(root, "catch_074.png"));
        using var caught = Cv2.ImRead(Path.Combine(root, "catch_099.png"));
        Assert.False(reel.Empty()); Assert.False(caught.Empty());
        using var before = new Mat(397, 3440, MatType.CV_8UC1);
        using var after = new Mat(397, 3440, MatType.CV_8UC1);
        var rng = new RNG(123); rng.Fill(before, DistributionType.Uniform, 0, 255);
        rng.Fill(after, DistributionType.Uniform, 0, 255);
        bool resumed = false;
        FishingEngine? engine = null;
        using var frames = new Frames(() =>
        {
            var full = (clock.Timestamp < 10000 ? reel : caught).Clone();
            using var landmarks = new Mat(full, new Rect(0, 602, 3440, 397));
            Cv2.CvtColor(clock.Timestamp < 5000 ? before : after, landmarks, ColorConversionCodes.GRAY2BGR);
            if (clock.Timestamp >= 10000 && clock.Timestamp < 11200)
            {
                using var transition = new Mat(full, new Rect(968, 1013, 1504, 273));
                transition.SetTo(Scalar.All(20));
            }
            resumed |= engine!.TotalCatches == 1 && engine.CurrentState == MacroState.Casting;
            return full;
        }, clock);
        using var running = engine = new FishingEngine(new Settings { EnableRecording = false,
            EnableAutoClaimAquarium = false, EnableAntiAfk = false, ShakeMode = "Disabled" },
            frames, frames, clock: clock, desktop: new Desktop { ClientWidth = 3440, ClientHeight = 1369 }, hardware: input);
        clock.Tick = () => { if (resumed || clock.Timestamp > 15000) engine.Stop(); };
        engine.Start();
        try { await engine.Completion.WaitAsync(TimeSpan.FromSeconds(45)); }
        finally { engine.Stop(); }
        Assert.Equal(1, engine.TotalCatches);
        Assert.Equal(0, engine.UnknownCatches);
        Assert.True(resumed);
        Assert.False(input.Held);
        Assert.Empty(input.HeldKeys);
    }
}
