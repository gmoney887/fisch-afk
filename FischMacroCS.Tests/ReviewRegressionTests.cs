using FischMacroCS.Core;
using OpenCvSharp;
using System.IO;

namespace FischMacroCS.Tests;

public partial class FishingEngineReplayTests
{
    [Fact]
    public async Task WorkerDoesNotRetryARespondingHoldWhenLeftwardMotionSlows()
    {
        // Translate a reviewed reel image through the real capture/vision/worker
        // path. This exercises velocity history at the call site, not just the guard.
        var clock = new Clock { CaptureMs = 70 };
        var input = new Input(clock);
        using var source = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "oath_red_45.png"));
        using var reel = new Mat();
        double ratio = Desktop.Height / 1369.0;
        Cv2.Resize(source, reel, new Size((int)Math.Round(source.Width * ratio), (int)Math.Round(source.Height * ratio)));
        int shift = 200;
        using var frames = new Frames(() =>
        {
            var full = new Mat(Desktop.Height, Desktop.Width, MatType.CV_8UC3, Scalar.All(20));
            using var target = new Mat(full, new Rect((full.Width - reel.Width) / 2 + shift,
                (int)Math.Round(1013 * ratio), reel.Width, reel.Height));
            reel.CopyTo(target);
            return full;
        }, clock);
        using var engine = new FishingEngine(new Settings { EnableRecording = false, EnableAutoClaimAquarium = false,
            EnableAntiAfk = false, EnableWatchdogRecovery = false, EnableHumanizedJitter = false,
            EnableAdaptiveRodDynamics = false }, frames, frames, clock: clock, desktop: new Desktop(), hardware: input);
        var observations = new List<(long At, double Velocity, string Action, int Downs, bool Held)>();
        bool slowed = false;
        engine.OnTelemetry += t =>
        {
            if (t.State == MacroState.Reeling && t.BarWidth > 0)
            {
                observations.Add((clock.Timestamp, t.BarVelocity, t.Action, input.Events.Count(e => e == "down"), input.Held));
                if (slowed) engine.Stop();
                else if (clock.Timestamp - observations[0].At >= 500) { shift -= 3; slowed = true; }
                else shift -= 10;
            }
            t.Dispose();
        };
        clock.Tick = () => { if (clock.Timestamp > 12000) engine.Stop(); };
        engine.Start();
        try { await engine.Completion.WaitAsync(TimeSpan.FromSeconds(30)); }
        finally { engine.Stop(); }
        string trace = string.Join("\n", observations);
        Assert.True(slowed && observations.Count >= 3, trace);
        var previous = observations[^2]; var current = observations[^1];
        Assert.True(previous.Velocity < -40 && current.Velocity >= -40 && current.Velocity <= 15
            && current.Velocity > previous.Velocity + 15, trace);
        Assert.DoesNotContain("Reasserting", current.Action);
        Assert.Equal(previous.Downs, current.Downs);
        Assert.True(current.Held);
        Assert.False(input.Held);
        Assert.Empty(input.HeldKeys);
    }
}
