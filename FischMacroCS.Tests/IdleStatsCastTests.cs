using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;
using System.IO;

namespace FischMacroCS.Tests;

public partial class FishingEngineReplayTests
{
    [Theory]
    [InlineData(18)]
    [InlineData(84)]
    [InlineData(111)]
    public void RecordedIdleStatsTextCannotConfirmAReel(int id)
    {
        using var frame = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"idle_stats_{id}.png"));
        Assert.Equal(new Size(3440, 1369), frame.Size());
        var vision = new VisionProcessor();
        using var context = vision.ProcessReelContext(frame, MinigameTheme.AutoCalibrate);
        Assert.False(context.ReelProgressFound);
        Assert.False(context.HasLiveReel);
        using var track = new Mat(frame, new Rect(968, 1013, 1504, 273));
        using var result = vision.ProcessTrack(track, 968, 1013, 1369.0 / 1080, MinigameTheme.AutoCalibrate, false);
        Assert.False(result.ReelProgressFound);
        Assert.False(result.HasLiveReel);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecordedIdleSceneDispatchesFirstCastBeforeWaitingForBite(bool dynamicRelease)
    {
        var clock = new Clock();
        var input = new Input(clock);
        using var frame = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "idle_stats_18.png"));
        Assert.False(frame.Empty());
        using var frames = new Frames(frame.Clone, clock);
        using var engine = new FishingEngine(new Settings
        {
            EnableRecording = false, EnableAutoClaimAquarium = false, EnableAntiAfk = false,
            EnableWatchdogRecovery = false, EnableHumanizedJitter = false, EnableAutoTuneCastDelay = false,
            EnableDynamicCastRelease = dynamicRelease, CastHoldMs = 721, ShakeMode = "Disabled"
        }, frames, frames, clock: clock,
            desktop: new Desktop { ClientWidth = 3440, ClientHeight = 1369 }, hardware: input);
        var downs = new List<long>();
        var ups = new List<long>();
        input.AfterDown = (_, _) => { if (engine.CurrentState == MacroState.Casting) downs.Add(clock.Timestamp); };
        input.AfterUp = () => { if (engine.CurrentState == MacroState.Casting) ups.Add(clock.Timestamp); };
        bool luring = false, falseReel = false;
        engine.OnTelemetry += t =>
        {
            luring |= t.State == MacroState.Luring;
            falseReel |= t.State == MacroState.Reeling;
            if (t.State is MacroState.Luring or MacroState.Reeling) engine.Stop();
            t.Dispose();
        };
        clock.Tick = () => { if (clock.Timestamp > 10000) engine.Stop(); };
        engine.Start();
        try { await engine.Completion.WaitAsync(TimeSpan.FromSeconds(20)); }
        finally { engine.Stop(); }
        Assert.False(falseReel);
        Assert.True(luring);
        long down = Assert.Single(downs);
        long up = Assert.Single(ups, at => at >= down);
        Assert.InRange(up - down, 721, 800);
        Assert.Equal(0, engine.TotalCatches);
        Assert.False(input.Held);
        Assert.Empty(input.HeldKeys);
    }
}
