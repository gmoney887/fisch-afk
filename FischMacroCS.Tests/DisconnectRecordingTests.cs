using FischMacroCS.Vision;
using FischMacroCS.Core;
using OpenCvSharp;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace FischMacroCS.Tests;

public class DisconnectRecordingTests
{
    internal static Mat Frame(string name) => Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,
        "Fixtures", "disconnect-nov05", name + ".png"));

    [Fact]
    public void RecordedFailurePixelsMatchReviewedProvenance()
    {
        string root = Path.Combine(AppContext.BaseDirectory,"Fixtures","disconnect-nov05");
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"provenance.json")));
        foreach(var frame in manifest.RootElement.GetProperty("Frames").EnumerateArray())
        {
            string path=Path.Combine(root,frame.GetProperty("File").GetString()!);
            Assert.Equal(frame.GetProperty("SHA256").GetString(),Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        }
    }

    [Theory]
    [InlineData("error279-initial", 1369)]
    [InlineData("error279-heldout", 1369)]
    [InlineData("error279-initial", 720)]
    [InlineData("error279-initial", 1080)]
    [InlineData("error279-initial", 1440)]
    public void RecordedConnectionFailureOffersRetryInsideTheActualButton(string name, int height)
    {
        using var source = Frame(name);
        Assert.False(source.Empty());
        double scale = height / 1369.0;
        using var full = new Mat();
        Cv2.Resize(source, full, new Size((int)Math.Round(source.Width * scale), height));
        int w = (int)(height * .62), h = (int)(height * .4);
        int x = (full.Width - w) / 2, y = (height - h) / 2;
        using var roi = new Mat(full, new Rect(x, y, w, h));
        Assert.True(DisconnectDetector.TryFindReconnect(roi, height, out var target));
        var retry = new Rect((int)(1725 * scale), (int)(754 * scale),
            (int)(175 * scale), (int)(36 * scale));
        Assert.True(retry.Contains(new Point(x + target.X, y + target.Y)), $"Retry missed: {target}");
    }

    [Theory]
    [InlineData("title")]
    [InlineData("retry")]
    [InlineData("cancel")]
    [InlineData("relocated")]
    public void RetryRequiresBothRecordedLabelsInTheirDialogGeometry(string mutation)
    {
        using var full = Frame("error279-initial");
        if (mutation == "title") Cv2.Rectangle(full, new Rect(1625,570,195,35), Scalar.All(56), -1);
        else if (mutation == "cancel")
        {
            using var cancel = new Mat(full, new Rect(1540,754,175,36)).Clone();
            using var retry = new Mat(full, new Rect(1725,754,175,36));
            cancel.CopyTo(retry);
        }
        else
        {
            using var retry = new Mat(full, new Rect(1725,754,175,36)).Clone();
            Cv2.Rectangle(full, new Rect(1725,754,175,36), Scalar.All(56), -1);
            if (mutation == "relocated")
            {
                using var elsewhere = new Mat(full, new Rect(1725,670,175,36));
                retry.CopyTo(elsewhere);
            }
        }
        using var roi = new Mat(full, new Rect(1296,411,848,547));
        Assert.False(DisconnectDetector.TryFindReconnect(roi, 1369, out _));
    }

    [Fact]
    public void RecordedRobloxHomeIsNotReconnectOrReel()
    {
        using var full = Frame("roblox-home");
        Assert.False(full.Empty());
        using var roi = new Mat(full, new Rect(1296,411,848,547));
        Assert.False(DisconnectDetector.TryFindReconnect(roi, 1369, out _));
        using var reel = new VisionProcessor().ProcessReelContext(full, FischMacroCS.Core.MinigameTheme.AutoCalibrate);
        Assert.False(reel.HasLiveReel);
    }
}

public partial class FishingEngineReplayTests
{
    [Theory]
    [InlineData("resume")]
    [InlineData("retry")]
    [InlineData("stale")]
    [InlineData("focus")]
    [InlineData("stop")]
    public async Task RecordedError279WorkerRetriesThenContinuesAndActuallyFishes(string scenario)
    {
        var clock = new Clock { CaptureMs = 20, RecoveryPollMs = 1000 };
        var input = new Input(clock);
        var desktop = new Desktop { ClientWidth = 3440, ClientHeight = 1369 };
        using var disconnected = DisconnectRecordingTests.Frame("error279-heldout");
        using var reel = OvernightGameplayTests.Frame(122060);
        using var caught = OvernightGameplayTests.Frame(213756);
        using var idle = OvernightGameplayTests.Frame(213799);
        using var blank = new Mat(1369,3440,MatType.CV_8UC3,Scalar.All(20));
        using var continueSource = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,"Fixtures","fisch_continue.png"));
        using var prompt = new Mat();
        Cv2.Resize(continueSource,prompt,new Size((int)Math.Round(continueSource.Width*1369.0/1353),
            (int)Math.Round(continueSource.Height*1369.0/1353)));
        using var continueFrame = blank.Clone();
        var promptOrigin = new Point((3440-prompt.Width)/2,(1369-prompt.Height)/2);
        using(var target = new Mat(continueFrame,new Rect(promptOrigin,prompt.Size()))) prompt.CopyTo(target);
        int stage=0, captures=0;
        long readyAt=long.MaxValue;
        long? reelAt=null;
        bool interrupted=false, completedCycle=false, focusChanged=false;
        var retries=new List<long>();
        var casts=new List<long>();
        var actions=new List<string>();
        var violations=new List<string>();
        using var frames=new Frames(() =>
        {
            if(stage==1 && clock.Timestamp>=readyAt) stage=scenario=="stale" ? 4 : 2;
            if(stage==3 && clock.Timestamp>=readyAt) stage=4;
            if(stage==4 && reelAt.HasValue && clock.Timestamp-reelAt>=1000) stage=5;
            return (stage switch {0=>disconnected,1 or 3=>blank,2=>continueFrame,4=>reel,5=>caught,_=>idle}).Clone();
        },clock);
        using var engine=new FishingEngine(new Settings {
            EnableRecording=false,EnableAutoClaimAquarium=false,EnableAntiAfk=false,
            EnableWatchdogRecovery=false,EnableHumanizedJitter=false,EnableDynamicCastRelease=false,
            CastHoldMs=721,PostCastDelayMs=0,ShakeMode="Disabled"
        },frames,frames,clock:clock,desktop:desktop,hardware:input);
        frames.BeforeCapture=region =>
        {
            // Enter the same persistent recovery wait used after a live view/capture interruption.
            if(!interrupted) {interrupted=true; throw new GameplayInterruptedException("Recorded disconnect");}
            if(region.Width!=848 || region.Height!=547 || stage!=0) return;
            captures++;
            if(captures==2 && scenario=="stale") {stage=1;readyAt=clock.Timestamp+1500;}
            if(captures==2 && scenario=="focus") {desktop.Focused=false;focusChanged=true;}
        };
        input.AfterDown=(x,y) =>
        {
            if(stage==0)
            {
                if(!new Rect(1725,754,175,36).Contains(new Point(x,y))) violations.Add($"Retry missed: {x},{y}");
                retries.Add(clock.Timestamp);actions.Add("Retry");
                if(scenario=="stop") {engine.Stop();return;}
                if(scenario!="retry" || retries.Count==2) {stage=1;readyAt=clock.Timestamp+1500;}
            }
            else if(stage==2)
            {
                double scale=1369.0/1353;
                if(!new Rect(promptOrigin.X+(int)(47*scale),promptOrigin.Y+(int)(281*scale),
                    (int)(310*scale),(int)(36*scale)).Contains(new Point(x,y))) violations.Add($"Continue missed: {x},{y}");
                actions.Add("Continue");stage=3;readyAt=clock.Timestamp+1500;
            }
            else if(stage<4) violations.Add("Input during blank loading");
            else if(engine.CurrentState==MacroState.Casting) {casts.Add(clock.Timestamp);stage=6;}
        };
        engine.OnTelemetry+=t =>
        {
            if(t.State==MacroState.Reeling) reelAt??=clock.Timestamp;
            if(casts.Count>0 && t.State==MacroState.Luring) {completedCycle=true;engine.Stop();}
            t.Dispose();
        };
        clock.Tick=()=>{if(clock.Timestamp>45000) engine.Stop();};
        engine.Start();
        try {await engine.Completion.WaitAsync(TimeSpan.FromSeconds(45));}
        finally {engine.Stop();}
        Assert.Empty(violations);
        Assert.False(input.Held);
        Assert.Empty(input.HeldKeys);
        if(scenario=="stop") {Assert.Single(retries);Assert.Empty(casts);Assert.Equal(0,engine.TotalCatches);return;}
        Assert.True(completedCycle,$"stage={stage}, actions={string.Join(',',actions)}, clock={clock.Timestamp}, pause={engine.PauseReason}");
        Assert.Single(casts);
        Assert.Equal(1,engine.TotalCatches);
        Assert.Equal(0,engine.UnknownCatches);
        if(scenario=="stale") Assert.Empty(actions);
        else Assert.Equal(scenario=="retry" ? new[]{"Retry","Retry","Continue"}:new[]{"Retry","Continue"},actions);
        if(scenario=="retry") Assert.True(retries[1]-retries[0]>=10000);
        if(scenario=="focus") {Assert.True(focusChanged);Assert.True(desktop.Activations>0);}
    }
}
