using FischMacroCS.Core;
using OpenCvSharp;
using System.IO;

namespace FischMacroCS.Tests;

public partial class FishingEngineReplayTests
{
    [Fact]
    public async Task CompanionRewardDuringLuringStillClicksTheRecordedShakeButton()
    {
        var clock = new Clock { CaptureMs = 20 };
        var input = new Input(clock);
        using var source = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "shake_companion_26.png"));
        Assert.False(source.Empty());
        using var frames = new Frames(() =>
        {
            var full = source.Clone();
            int left = full.Width / 2 - (69 * 8 + 68) / 2;
            for (int slot = 0; slot < 9; slot++)
                Cv2.Rectangle(full, new Rect(left + slot * 69, full.Height - 70, 68, 68),
                    slot == 0 ? Scalar.White : Scalar.All(60), 1);
            return full;
        }, clock);
        using var engine = new FishingEngine(new Settings
        {
            EnableRecording = false, EnableAutoClaimAquarium = false, EnableAntiAfk = false,
            EnableWatchdogRecovery = false, EnableHumanizedJitter = false, EnableDynamicCastRelease = false,
            CastHoldMs = 10, PostCastDelayMs = 0, ShakeMode = "Visual"
        }, frames, frames, clock: clock,
            desktop: new Desktop { ClientWidth = 3440, ClientHeight = 1369 }, hardware: input);
        var clicks = new List<Point>();
        input.AfterDown = (x, y) =>
        {
            if (engine.CurrentState == MacroState.Luring) clicks.Add(new Point(x, y));
        };
        bool falseReel = false;
        engine.OnTelemetry += t =>
        {
            falseReel |= t.State == MacroState.Reeling;
            if (t.State is MacroState.Reeling or MacroState.Luring && (falseReel || clicks.Count > 0)) engine.Stop();
            t.Dispose();
        };
        clock.Tick = () => { if (clock.Timestamp > 10000) engine.Stop(); };
        engine.Start();
        try { await engine.Completion.WaitAsync(TimeSpan.FromSeconds(30)); }
        finally { engine.Stop(); }
        Assert.False(falseReel);
        Assert.NotEmpty(clicks);
        Assert.All(clicks, point =>
        {
            Assert.InRange(point.X, 2590, 2640);
            Assert.InRange(point.Y, 450, 500);
        });
        Assert.False(input.Held);
        Assert.Empty(input.HeldKeys);
    }
}
