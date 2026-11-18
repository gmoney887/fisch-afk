using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace FischMacroCS.Tests;

public partial class FishingEngineReplayTests
{
    [Fact]
    public void NatesOpeningFixturesMatchOriginalRecorderFiles()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "nates-opening-nov08");
        using var provenance = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "provenance.json")));
        foreach (var frame in provenance.RootElement.GetProperty("Frames").EnumerateArray())
        {
            string path = Path.Combine(directory, frame.GetProperty("File").GetString()!);
            Assert.Equal(frame.GetProperty("SHA256").GetString(), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
            using var image = Cv2.ImRead(path);
            Assert.Equal(new Size(3440, 1369), image.Size());
        }
    }

    [Theory]
    [InlineData(60)]
    [InlineData(200)]
    [InlineData(-20)]
    [InlineData(-80)]
    public async Task NativeNatesOpeningMovementPreemptsOldFrameSearches(int arrivalMs)
    {
        using var waiting = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "nates-opening-nov08", "frame_00000070.png"));
        using var active = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "nates-opening-nov08", "frame_00000442.png"));
        using var equipped = RodRecoveryFrame(2546);
        var wall = Stopwatch.StartNew(); var input = new Input(new Clock());
        FishingEngine? engine = null; double? started = null, onset = null, pressed = null;
        Mat Render()
        {
            if (engine!.CurrentState == MacroState.Luring) started ??= wall.Elapsed.TotalMilliseconds;
            // Negative arrivals place the bite inside a search after the bobber
            // has settled; early arrivals alone never exercise the slow Shake path.
            if (started.HasValue && !onset.HasValue && (arrivalMs >= 0 || wall.Elapsed.TotalMilliseconds - started >= 400))
                onset = arrivalMs >= 0 ? started + arrivalMs : wall.Elapsed.TotalMilliseconds - arrivalMs;
            var image = (onset.HasValue && wall.Elapsed.TotalMilliseconds >= onset ? active : waiting).Clone();
            if (engine.CurrentState == MacroState.Casting)
            {
                var band = new Rect(0, image.Height - 90, image.Width, 90);
                using var source = new Mat(equipped, band); using var target = new Mat(image, band); source.CopyTo(target);
            }
            return image;
        }
        using var frames = new Frames(Render, new Clock());
        using var running = engine = new FishingEngine(new Settings {
            EnableRecording = false, EnableAutoClaimAquarium = false, EnableAntiAfk = false,
            EnableWatchdogRecovery = false, EnableHumanizedJitter = false, EnableDynamicCastRelease = false,
            CastHoldMs = 80, PostCastDelayMs = 0, EnableShakeClicks = true, ShakeMode = "Visual"
        }, frames, frames, clock: new MonotonicClock(), desktop: new Desktop { ClientWidth = 3440, ClientHeight = 1369 }, hardware: input);
        input.AfterDown = (_, _) => {
            if (engine.CurrentState == MacroState.Reeling) { pressed = wall.Elapsed.TotalMilliseconds; engine.Stop(); }
        };
        engine.OnTelemetry += t => t.Dispose();
        engine.Start();
        try { await engine.Completion.WaitAsync(TimeSpan.FromSeconds(15)); }
        finally { engine.Stop(); }
        Assert.NotNull(started); Assert.NotNull(onset); Assert.NotNull(pressed);
        double responseMs = pressed.Value - onset.Value;
        Console.WriteLine($"Actual native first-reel response: {responseMs:F3}ms; arrival={arrivalMs}; lure onset={onset-started:F3}ms");
        Assert.InRange(responseMs, 0, 350);
        Assert.Equal(0, engine.TotalCatches + engine.TotalFails + engine.UnknownCatches);
        Assert.False(input.Held); Assert.Empty(input.HeldKeys); Assert.Null(engine.PauseReason);
    }
}
