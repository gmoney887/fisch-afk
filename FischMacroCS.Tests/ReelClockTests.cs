using FischMacroCS.Core;
using OpenCvSharp;
using System.IO;

namespace FischMacroCS.Tests;

public partial class FishingEngineReplayTests
{
    [Fact]
    public async Task ReelCadenceUsesTheSameClockAsCaptureAndInput()
    {
        var clock = new Clock { CaptureMs = 0 };
        var input = new Input(clock);
        using var source = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "nate_24.png"));
        Assert.False(source.Empty());
        using var reel = new Mat();
        double ratio = Desktop.Height / 1369.0;
        Cv2.Resize(source, reel, new Size((int)Math.Round(source.Width * ratio), (int)Math.Round(source.Height * ratio)));
        using var frames = new Frames(() =>
        {
            // Real rendering cost must not become extra/missing time in the
            // injected clock used by the actual capture/input feedback loop.
            Thread.Sleep(20);
            var full = new Mat(Desktop.Height, Desktop.Width, MatType.CV_8UC3, Scalar.All(20));
            using var area = new Mat(full, new Rect((full.Width - reel.Width) / 2,
                (int)Math.Round(1013 * ratio), reel.Width, reel.Height));
            reel.CopyTo(area);
            return full;
        }, clock);
        using var engine = new FishingEngine(new Settings { EnableRecording = false,
            EnableAutoClaimAquarium = false, EnableAntiAfk = false, EnableWatchdogRecovery = false },
            frames, frames, clock: clock, desktop: new Desktop(), hardware: input);
        var observations = new List<long>();
        engine.OnTelemetry += t =>
        {
            if (t.State == MacroState.Reeling && t.BarRight > 0)
            {
                observations.Add(t.ObservationTimestampTicks);
                if (observations.Count == 4) engine.Stop();
            }
            t.Dispose();
        };
        clock.Tick = () => { if (clock.Timestamp > 5000) engine.Stop(); };
        engine.Start();
        try { await engine.Completion.WaitAsync(TimeSpan.FromSeconds(10)); }
        finally { engine.Stop(); }
        Assert.Equal(4, observations.Count);
        Assert.All(observations.Zip(observations.Skip(1)), pair => Assert.Equal(10L, pair.Second - pair.First));
        Assert.False(input.Held);
    }
}
