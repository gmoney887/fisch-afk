using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;
using System.IO;
using System.Text.Json;
using System.Security.Cryptography;

namespace FischMacroCS.Tests;

public partial class FishingEngineReplayTests
{
    [Fact]
    public async Task WorkerClearsFinishedReelProgressBeforeRecoveringAnUnavailableNextCast()
    {
        string directory=Path.Combine(AppContext.BaseDirectory,"Fixtures","reel-stall-nov05");
        using var early=Cv2.ImRead(Path.Combine(directory,"frame_00000481.png"));
        using var later=Cv2.ImRead(Path.Combine(directory,"frame_00000625.png"));
        using var unavailable=DisconnectRecordingTests.Frame("roblox-home");
        // Controlled landmarks make the view change explicit; the recorded reel
        // and hotbar regions remain untouched. This is not one continuous clip.
        using var beforeScene=new Mat(1369,3440,MatType.CV_8UC1);
        using var afterScene=new Mat(1369,3440,MatType.CV_8UC1);
        var random=new RNG(321);
        random.Fill(beforeScene,DistributionType.Uniform,0,255);
        random.Fill(afterScene,DistributionType.Uniform,0,255);
        Mat WithScene(Mat source,bool changed)
        {
            var full=source.Clone();
            var area=new Rect(0,(int)(1369*.44),3440,(int)(1369*.29));
            using var scene=new Mat(changed ? afterScene : beforeScene,area);
            using var target=new Mat(full,area);
            Cv2.CvtColor(scene,target,ColorConversionCodes.GRAY2BGR);
            return full;
        }
        using(var guard=new FishingViewGuard())
        {
            using var before=WithScene(early,false);using var after=WithScene(unavailable,true);
            for(int i=0;i<3;i++)guard.Observe(before);
            Assert.True(guard.IsArmed);
            for(int i=0;i<2;i++)guard.Observe(after);
            Assert.Equal(FishingViewStatus.Changed,guard.Observe(after));
        }
        var clock=new Clock {CaptureMs=110};var input=new Input(clock);
        long? entered=null, disappeared=null, recovered=null;
        using var frames=new Frames(()=>
        {
            double elapsed=entered.HasValue ? clock.Timestamp-entered.Value : 0;
            if(elapsed>=15000) {disappeared??=clock.Timestamp;return WithScene(unavailable,true);}
            return WithScene(elapsed>=5000 ? later : early,false);
        },clock);
        using var engine=new FishingEngine(new Settings {EnableRecording=false,EnableAutoClaimAquarium=false,
            EnableAntiAfk=false,EnableHumanizedJitter=false,EnableWatchdogRecovery=false,ShakeMode="Disabled"},
            frames,frames,clock:clock,desktop:new Desktop {ClientWidth=3440,ClientHeight=1369},hardware:input);
        engine.OnTelemetry+=t=>
        {
            if(t.State==MacroState.Reeling) entered??=clock.Timestamp;
            if(disappeared.HasValue && t.Action.Contains("Fishing view changed"))
            {recovered=clock.Timestamp;engine.Stop();}
            t.Dispose();
        };
        clock.Tick=()=>{if(clock.Timestamp>30000) engine.Stop();};
        engine.Start();
        try {await engine.Completion.WaitAsync(TimeSpan.FromSeconds(30));}
        finally {engine.Stop();}
        Assert.True(recovered.HasValue,$"No recovery after reel ended: clock={clock.Timestamp}, pause={engine.PauseReason}");
        Assert.InRange(recovered!.Value-disappeared!.Value,0,10000);
        Assert.False(input.Held);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecordedSlowReelOnlyRecoversWhenProgressActuallyStalls(bool frozen)
    {
        string directory=Path.Combine(AppContext.BaseDirectory,"Fixtures","reel-stall-nov05");
        using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"provenance.json")));
        var samples=manifest.RootElement.GetProperty("Frames").EnumerateArray().Select(f => (
            At:f.GetProperty("Timestamp").GetInt64(),
            Pixels:Cv2.ImRead(Path.Combine(directory,f.GetProperty("File").GetString()!)))).OrderBy(f=>f.At).ToArray();
        Assert.True(samples.Length>=7);
        foreach(var f in manifest.RootElement.GetProperty("Frames").EnumerateArray())
            Assert.Equal(f.GetProperty("SHA256").GetString(),Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(Path.Combine(directory,f.GetProperty("File").GetString()!)))));
        Assert.All(samples,f=>Assert.False(f.Pixels.Empty()));
        var clock=new Clock {CaptureMs=110};
        var input=new Input(clock);
        long? entered=null;
        bool caughtAndRecast=false;
        var trace=new List<string>();
        int castPresses=0;
        using var caught=OvernightGameplayTests.Frame(213756);
        using var idle=OvernightGameplayTests.Frame(213799);
        using var frames=new Frames(() =>
        {
            double elapsed=entered.HasValue ? clock.Timestamp-entered.Value : 0;
            // Continue beyond both the 35s reel timeout and 50s workflow watchdog.
            // Then use the separately reviewed catch fixture as a controlled result.
            if(!frozen && elapsed>=52000) return (castPresses>0 ? idle : caught).Clone();
            int index=0;
            if(!frozen) while(index+1<samples.Length && (samples[index+1].At-samples[0].At)/10000.0<=elapsed) index++;
            return samples[index].Pixels.Clone();
        },clock);
        using var engine=new FishingEngine(new Settings {
            EnableRecording=false,EnableAutoClaimAquarium=false,EnableAntiAfk=false,
            EnableWatchdogRecovery=true,WatchdogStallTimeoutSeconds=50,ReelTimeoutMs=35000,
            EnableHumanizedJitter=false,EnableDynamicCastRelease=false,
            CastHoldMs=673,PostCastDelayMs=0,ShakeMode="Disabled"
        },frames,frames,clock:clock,desktop:new Desktop {ClientWidth=3440,ClientHeight=1369},hardware:input);
        input.AfterDown=(_,_)=>{if(engine.CurrentState==MacroState.Casting) castPresses++;};
        engine.OnTelemetry+=t=>
        {
            if(trace.Count==0 || !trace[^1].Contains($"{t.State}:{t.Action}"))
                trace.Add($"{clock.Timestamp} {t.State}:{t.Action} C={engine.TotalCatches} U={engine.UnknownCatches} R={engine.WatchdogRecoveryCount}");
            if(t.State==MacroState.Reeling) entered??=clock.Timestamp;
            if(castPresses>0 && t.State==MacroState.Luring) {caughtAndRecast=true;engine.Stop();}
            t.Dispose();
        };
        clock.Tick=()=>{if(clock.Timestamp>75000 || frozen && engine.WatchdogRecoveryCount>0) engine.Stop();};
        engine.Start();
        try {await engine.Completion.WaitAsync(TimeSpan.FromSeconds(60));}
        finally {engine.Stop();foreach(var sample in samples) sample.Pixels.Dispose();}
        Assert.True(entered.HasValue);
        Assert.False(input.Held);
        if(frozen)
        {
            Assert.True(engine.WatchdogRecoveryCount>0,"Frozen footage must still have a bounded recovery deadline.");
            Assert.InRange(clock.Timestamp-entered!.Value,34000,45000);
            return;
        }
        Assert.True(caughtAndRecast,$"clock={clock.Timestamp}, pause={engine.PauseReason}");
        Assert.True(engine.TotalCatches==1,string.Join('\n',trace.TakeLast(20)));
        Assert.Equal(0,engine.UnknownCatches);
        Assert.Equal(0,engine.WatchdogRecoveryCount);
        Assert.Equal(1,castPresses);
    }
}

public class ReelProgressWatchTests
{
    private sealed class Clock : IClock
    {
        public long Timestamp {get;set;}
        public double ElapsedMilliseconds(long since)=>Timestamp-since;
        public void Delay(int milliseconds,CancellationToken cancellation)=>Timestamp+=milliseconds;
    }

    [Fact]
    public void OnlyConfirmedNewHighWaterExtendsTheStallDeadline()
    {
        var clock=new Clock(); var watch=new ReelProgressWatch(clock);watch.Reset();
        Assert.False(watch.Observe(.3));
        clock.Timestamp=30000;
        Assert.False(watch.Observe(.32));
        clock.Timestamp=30250;
        Assert.True(watch.Observe(.321));
        clock.Timestamp=60000;
        Assert.False(watch.IsStalled(35000));
        Assert.False(watch.Observe(.31));
        Assert.False(watch.Observe(.32)); // Revisiting an old high is not fresh progress.
        clock.Timestamp=65500;
        Assert.True(watch.IsStalled(35000));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(double.NaN)]
    [InlineData(.7)]
    [InlineData(.3)]
    public void MissingFrozenOrInvalidMeterNeverRenewsTheDeadline(double? fill)
    {
        var clock=new Clock();var watch=new ReelProgressWatch(clock);watch.Reset();watch.Observe(.3);
        for(int i=1;i<=40;i++){clock.Timestamp=i*1000;Assert.False(watch.Observe(fill));}
        Assert.True(watch.IsStalled(35000));
    }

    [Fact]
    public void IsolatedMeterSpikeCannotRenewTheDeadline()
    {
        var clock=new Clock();var watch=new ReelProgressWatch(clock);watch.Reset();watch.Observe(.3);
        clock.Timestamp=30000;
        Assert.False(watch.Observe(.5));
        Assert.False(watch.Observe(.3));
        clock.Timestamp=36000;
        Assert.True(watch.IsStalled(35000));
        watch.Reset();
        Assert.False(watch.IsStalled(35000));
    }

    [Fact]
    public void SceneChangeCannotPreemptAdvancingReelButStillRecoversWithoutProgress()
    {
        var clock=new Clock();var view=new FishingViewSafety(clock);
        Assert.False(view.Observe(FishingViewStatus.Changed,true,35000));
        clock.Timestamp=40000;
        view.ConfirmReelProgress();
        Assert.False(view.Observe(FishingViewStatus.Changed,true,35000));
        clock.Timestamp=75001;
        Assert.True(view.Observe(FishingViewStatus.Changed,true,35000));
        view.Reset();
        clock.Timestamp=80000;
        Assert.False(view.Observe(FishingViewStatus.Changed,true,35000));
        clock.Timestamp=116000;
        Assert.True(view.Observe(FishingViewStatus.Changed,true,35000));
    }

    [Fact]
    public void FinishedReelProgressCannotDelayRecoveryOnTheFollowingCast()
    {
        var clock=new Clock();var view=new FishingViewSafety(clock);
        view.ConfirmReelProgress();
        view.EndReel();
        Assert.False(view.Observe(FishingViewStatus.Changed,false,35000));
        clock.Timestamp=4000;
        Assert.True(view.Observe(FishingViewStatus.Changed,false,35000));
    }
}
