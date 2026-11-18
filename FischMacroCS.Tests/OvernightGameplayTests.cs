using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;
using System.IO;

namespace FischMacroCS.Tests;

public class OvernightGameplayTests
{
    internal static Mat Frame(int id) => Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,
        "Fixtures", "overnight-nov05", $"frame_{id:D8}.png"));
    internal static Mat Track(Mat frame) => frame.Width == 3440
        ? new Mat(frame, new Rect(968, 1013, 1504, 273)) : frame.Clone();

    [Theory]
    [InlineData(200836, false)] // Companion Dolphin, not a player catch.
    [InlineData(213756, true)]  // Player Salmon under inventory warnings.
    [InlineData(213799, false)] // Idle rod stats and inventory warnings.
    [InlineData(226397, true)]  // Player Barracuda.
    [InlineData(226432, true)]  // Fading Barracuda; false reel entry in live log.
    [InlineData(174219, true)]  // Player Yellowfin.
    public void RecordedNotificationsDoNotBecomeAReelAndPlayerCatchesAreRecognized(int id, bool caught)
    {
        using var frame = Frame(id);
        Assert.False(frame.Empty());
        using var crop = Track(frame);
        var vision = new VisionProcessor();
        using var detection = vision.ProcessTrack(crop, 968, 1013, 1369.0 / 1080, MinigameTheme.AutoCalibrate, false);
        Assert.False(detection.HasLiveReel, $"{id}: false reel {detection.BarLeft}..{detection.BarRight}, fish {detection.FishX}, meter {detection.ReelProgressFound}");
        Assert.Equal(caught, vision.DetectCatchNotification(crop, 1369));
        using var context = vision.ProcessReelContext(frame, MinigameTheme.AutoCalibrate);
        Assert.False(context.HasLiveReel);
    }

    [Theory]
    [InlineData(213799)]
    [InlineData(226432)]
    [InlineData(68679)]
    public void OvernightAquariumNavigationIsRecognizedInNightAndDay(int id)
    {
        using var frame = Frame(id);
        using var vision = new TemplateWorkflowVision(Path.Combine(AppContext.BaseDirectory, "Assets", "Workflows"));
        var target = vision.Find(frame, AquariumWorkflow.Navigation);
        Assert.True(target.Found, $"Navigation confidence {target.Confidence}");
        Assert.True(new Rect(1770,20,110,25).Contains(target.Center), $"Click {target.Center}");
        // The surrounding blue night scenery is not itself a navigation target.
        using var label = new Mat(frame, new Rect(1775,23,100,20));
        label.SetTo(new Scalar(77,53,53));
        Assert.False(vision.Find(frame, AquariumWorkflow.Navigation).Found);
    }


    [Fact]
    public void StationaryFishStaysAtTheVisibleMarkerThroughRecordedSlashAndBarMovement()
    {
        var vision = new VisionProcessor();
        var errors = new List<string>();
        foreach (int id in new[] {122050,122054,122055,122056,122057,122058,122059,122060,122061,122062})
        {
            using var frame = Frame(id);
            Assert.False(frame.Empty());
            using var crop = Track(frame);
            using var d = vision.ProcessTrack(crop, 968, 1013, 1369.0 / 1080, MinigameTheme.AutoCalibrate, false);
            if (!d.HasLiveReel || Math.Abs(d.FishX - 1720) > 8)
                errors.Add($"{id}: fish {d.FishX}, bar {d.BarLeft}..{d.BarRight}, live {d.HasLiveReel}");
        }
        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }
}

public partial class FishingEngineReplayTests
{
    [Theory]
    [InlineData(213799, false)]
    [InlineData(226432, false)]
    [InlineData(213799, true)]
    public void OvernightIdleAndCatchWarningsCannotDeferAquariumAsALiveReel(int idleFrame, bool claimFails)
    {
        var clock = new Clock();
        var input = new Input(clock);
        using var idle = OvernightGameplayTests.Frame(idleFrame);
        using var unclaimed = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "aquarium_nov04_96.png"));
        using var empty = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "aquarium_claim_animation_8.png"));
        bool opened = false, claimed = false;
        var clicks = new List<string>();
        using var frames = new Frames(() => (!opened ? idle : claimed && !claimFails ? empty : unclaimed).Clone(), clock);
        using var engine = new FishingEngine(new Settings { EnableRecording = false }, frames, frames,
            clock: clock, desktop: new Desktop { ClientWidth = 3440, ClientHeight = 1369 }, hardware: input);
        input.AfterDown = (x, y) =>
        {
            if (y < 100) { opened = true; clicks.Add("open"); }
            else if (y < 250) { opened = false; clicks.Add("close"); }
            else
            {
                Assert.True(new Rect(1343, 710, 175, 57).Contains(new Point(x,y)));
                claimed = true; clicks.Add("claim");
            }
        };
        Assert.Equal(!claimFails, engine.ExecuteAquariumClaim());
        Assert.Equal(claimFails ? new[] {"open","claim","claim","close"} : new[] {"open","claim","close"}, clicks);
        Assert.Equal(claimFails ? ActionOutcome.Unknown : ActionOutcome.ConfirmedSuccess, engine.LastAquariumOutcome);
        Assert.Equal(claimFails, engine.Config.LastAquariumClaimUtc == DateTime.MinValue);
        Assert.False(opened);
        Assert.False(input.Held);
    }

    [Theory]
    [InlineData(213756)]
    [InlineData(226397)]
    [InlineData(174219)]
    public async Task OvernightReelAndCatchSequenceCountsOnceAndActuallyRecasts(int catchFrame)
    {
        var clock = new Clock { CaptureMs = 40 };
        var input = new Input(clock);
        using var reel = OvernightGameplayTests.Frame(122060);
        using var caught = OvernightGameplayTests.Frame(catchFrame);
        long? enteredReel = null;
        using var frames = new Frames(() =>
            (enteredReel.HasValue && clock.Timestamp - enteredReel >= 1000 ? caught : reel).Clone(), clock);
        using var engine = new FishingEngine(new Settings
        {
            EnableRecording = false, EnableAutoClaimAquarium = false, EnableAntiAfk = false,
            EnableWatchdogRecovery = false, EnableHumanizedJitter = false,
            EnableDynamicCastRelease = false, CastHoldMs = 721, PostCastDelayMs = 0, ShakeMode = "Disabled"
        }, frames, frames, clock: clock,
            desktop: new Desktop { ClientWidth = 3440, ClientHeight = 1369 }, hardware: input);
        var casts = new List<long>();
        input.AfterDown = (_, _) => { if (engine.CurrentState == MacroState.Casting) casts.Add(clock.Timestamp); };
        bool resumed = false;
        var states = new List<MacroState>();
        engine.OnTelemetry += t =>
        {
            states.Add(t.State);
            if (t.State == MacroState.Reeling) enteredReel ??= clock.Timestamp;
            if (enteredReel.HasValue && t.State == MacroState.Luring) { resumed = true; engine.Stop(); }
            t.Dispose();
        };
        clock.Tick = () => { if (clock.Timestamp > 15000) engine.Stop(); };
        engine.Start();
        try { await engine.Completion.WaitAsync(TimeSpan.FromSeconds(40)); }
        finally { engine.Stop(); }
        Assert.True(resumed, $"States {string.Join(',', states.Distinct())}; pause {engine.PauseReason}");
        Assert.Single(casts);
        Assert.Equal(1, engine.TotalCatches);
        Assert.Equal(0, engine.UnknownCatches);
        Assert.Equal(0, engine.WatchdogRecoveryCount);
        Assert.False(input.Held);
    }
}
