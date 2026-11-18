using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace FischMacroCS.Tests;

public partial class FishingEngineReplayTests
{
    private static Mat RodRecoveryFrame(int number) => Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,
        "Fixtures", "rod-recovery-nov08", $"frame_{number:D8}.png"));

    [Fact]
    public void RodRecoveryFixturesPreserveOriginalRecorderPixelsAndBounds()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "rod-recovery-nov08");
        using var provenance = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "provenance.json")));
        foreach (var frame in provenance.RootElement.GetProperty("Frames").EnumerateArray())
        {
            string path = Path.Combine(directory, frame.GetProperty("File").GetString()!);
            Assert.Equal(frame.GetProperty("SHA256").GetString(), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
            using var image = Cv2.ImRead(path);
            var region = frame.GetProperty("Region");
            Assert.Equal(region.GetProperty("Width").GetInt32(), image.Width);
            Assert.Equal(region.GetProperty("Height").GetInt32(), image.Height);
        }
    }

    [Fact]
    public void ThreeAlignedSlotContoursCannotStandInForTheWholeHotbar()
    {
        using var frame = new Mat(1369, 3440, MatType.CV_8UC3, Scalar.All(100));
        int left = 3440 / 2 - (69 * 8 + 68) / 2;
        foreach (int slot in new[] { 0, 1, 4 })
        {
            var bounds = new Rect(left + slot * 69, 1369 - 70, 68, 68);
            Cv2.Rectangle(frame, bounds, Scalar.All(20), -1);
            Cv2.Rectangle(frame, bounds, Scalar.All(110), 1);
        }
        var result = new VisionProcessor().DetectRodEquipped(frame, 1, fullViewportHeight: 1369);
        Assert.False(result.GeometryConfirmed);
        Assert.False(result.IsEquipped);
    }

    [Theory]
    [InlineData(2546, true)]
    [InlineData(2550, false)]
    [InlineData(2559, false)]
    [InlineData(2641, true)]
    [InlineData(2646, false)]
    [InlineData(2654, false)]
    public void RecordedAbyssalRodResetRetainsTheUnselectedHotbar(int number, bool selected)
    {
        using var frame = RodRecoveryFrame(number);
        var result = new VisionProcessor().DetectRodEquipped(frame, 1, fullViewportHeight: 1369);
        Assert.True(result.GeometryConfirmed, $"Frame {number}: selected={result.IsEquipped}; bounds={result.SlotBounds}; density={result.ActiveDensity}");
        Assert.Equal(selected, result.IsEquipped);
        Assert.InRange(result.SlotCenter.X, 1430, 1455);
    }

    [Theory]
    [InlineData(2546, 2559)]
    [InlineData(2641, 2654)]
    public async Task RecordedLureTimeoutReequipsRodAndCompletesTheNextCatch(int selectedFrame, int unselectedFrame)
    {
        var clock = new Clock(); var input = new Input(clock);
        using var selected = RodRecoveryFrame(selectedFrame);
        using var unselected = RodRecoveryFrame(unselectedFrame);
        using var reel = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "reel_active_recovery_21.png"));
        using var caught = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "reel_catch_live.png"));
        FishingEngine? engine = null; bool equipped = true, expired = false, queued = false;
        int toggles = 0, casts = 0; long? reelingSince = null;
        Mat Render()
        {
            var frame = (equipped ? selected : unselected).Clone();
            Mat? strip = null;
            if (casts >= 2 && engine!.CurrentState == MacroState.Luring) strip = reel;
            if (engine!.CurrentState == MacroState.Reeling)
            { reelingSince ??= clock.Timestamp; if (clock.Timestamp - reelingSince.Value < 600) strip = reel; }
            if (engine.CurrentState == MacroState.PostCatch) strip = caught;
            if (strip != null)
            {
                using var target = new Mat(frame, new Rect((frame.Width - strip.Width) / 2, 1015, strip.Width, strip.Height));
                strip.CopyTo(target);
            }
            return frame;
        }
        using var frames = new Frames(Render, clock);
        using var running = engine = new FishingEngine(new Settings {
            EnableRecording = false, EnableAutoClaimAquarium = false, EnableAntiAfk = false,
            EnableWatchdogRecovery = true, EnableHumanizedJitter = false, EnableDynamicCastRelease = false,
            CastHoldMs = 80, PostCastDelayMs = 0, PostCatchDelayMs = 800, LureTimeoutMs = 1500, ShakeMode = "Disabled"
        }, frames, frames, clock: clock, desktop: new Desktop { ClientWidth = 3440, ClientHeight = 1369 }, hardware: input);
        input.AfterKey = (key, flags) => { if (key == '1' && flags == 0) { equipped = !equipped; toggles++; } };
        input.AfterDown = (_, _) => {
            if (engine.CurrentState != MacroState.Casting) return;
            casts++;
            if (casts == 2) queued = engine.TryQueueStopAfterCycle();
        };
        engine.OnTelemetry += t => t.Dispose();
        clock.Tick = () => { if (clock.Timestamp > 15000) { expired = true; engine.Stop(); } };
        engine.Start();
        try { await engine.Completion.WaitAsync(TimeSpan.FromSeconds(30)); }
        finally { engine.Stop(); }
        Assert.False(expired, "Recovery stranded the rod after deselecting it");
        Assert.True(queued);
        Assert.True(equipped);
        Assert.Equal(2, toggles);
        Assert.Equal(2, casts);
        Assert.Equal(1, engine.WatchdogRecoveryCount);
        Assert.Equal(1, engine.TotalCatches);
        Assert.Equal(1, engine.CurrentStreak);
        Assert.Equal(0, engine.TotalFails);
        Assert.False(input.Held); Assert.Empty(input.HeldKeys); Assert.Null(engine.PauseReason);
    }
}
