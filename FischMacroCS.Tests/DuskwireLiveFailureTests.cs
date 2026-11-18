using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace FischMacroCS.Tests;

public class DuskwireLiveFailureTests
{
    internal static Mat Frame(int id) => Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,
        "Fixtures", "duskwire-live-failures-nov07", $"frame_{id:00000000}.png"));

    [Fact]
    public void OriginalFailurePixelsAndCaptureBoundsArePreserved()
    {
        string folder=Path.Combine(AppContext.BaseDirectory,"Fixtures","duskwire-live-failures-nov07");
        using var metadata=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"provenance.json")));
        foreach(var item in metadata.RootElement.GetProperty("Frames").EnumerateArray())
        {
            string path=Path.Combine(folder,item.GetProperty("File").GetString()!);
            Assert.Equal(item.GetProperty("SHA256").GetString(),Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
            using var pixels=Cv2.ImRead(path);
            Assert.Equal(item.GetProperty("Region").GetProperty("Width").GetInt32(),pixels.Width);
            Assert.Equal(item.GetProperty("Region").GetProperty("Height").GetInt32(),pixels.Height);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CroppedOutMeterDoesNotDiscardTheCurrentlyVisibleBodyAndNeedle(bool acquired)
    {
        var vision=new VisionProcessor();
        _=vision.GetReelCaptureRegion(new Rect(968,1013,1504,273),new Size(3440,1369));
        if(acquired)
        {
            using var prior=Frame(226524);
            using var initial=vision.ProcessTrack(prior,968,1013,1369/1080.0,MinigameTheme.AutoCalibrate,false);
            Assert.True(initial.HasLiveReel);
        }
        using var clipped=Frame(226511);
        using var reading=vision.ProcessTrack(clipped,968,1013,1369/1080.0,MinigameTheme.AutoCalibrate,false);
        Assert.True(reading.DetectorPath=="Duskwire" && reading.HasLiveReel,
            $"{reading.DetectorPath}: body={reading.BarFound}, needle={reading.FishFound}, meter={reading.ReelProgressFound}");
        // Manually reviewed original pixels: x=648..856 and marker center=752,
        // with the original capture's x offset 968. No pixel outside it exists.
        Assert.InRange(reading.BarLeft,1612,1620);
        Assert.InRange(reading.BarRight,1820,1828);
        Assert.InRange(reading.FishX,1716,1724);
        Assert.False(reading.ReelProgressFound);
        var next=vision.GetReelCaptureRegion(new Rect(968,1013,1504,273),new Size(3440,1369));
        Assert.Equal(1369,next.Bottom);
        using var absent=Frame(226508);
        using var missing=vision.ProcessTrack(absent,968,1013,1369/1080.0,MinigameTheme.AutoCalibrate,false);
        Assert.False(missing.HasLiveReel);
    }
}

public partial class FishingEngineReplayTests
{
    // Replay the original short crop at its real origin; do not fabricate the
    // missing pixels below it. Full-context requests use a separate original.
    private sealed class ClippedDuskwireFrames(Mat context,Mat clipped,Clock clock,Func<bool> useClipped) : ICooperativeFrameSource
    {
        public Mat? CaptureClientRegion(IntPtr window,int x,int y,int width,int height)
        {
            clock.CaptureElapsed();
            if(useClipped() && x==968 && y==1013 && width==1504) return clipped.Clone();
            using var roi=new Mat(context,new Rect(x,y,width,height));
            return roi.Clone();
        }
        public Mat? CaptureClientRegion(IntPtr window,int x,int y,int width,int height,Action heartbeat)
        {
            heartbeat();return CaptureClientRegion(window,x,y,width,height);
        }
        public void Dispose() { }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20000)]
    public async Task OriginalClippedCaptureDoesNotConcludeAnOngoingFight(int clippingAfterMs)
    {
        using var context=DuskwireLiveFailureTests.Frame(143238);
        using var clipped=DuskwireLiveFailureTests.Frame(226511);
        var clock=new Clock {CaptureMs=70};var input=new Input(clock);
        long? entered=null;bool clippedSeen=false,abandoned=false,completed=false;int measurements=0;
        using var frames=new ClippedDuskwireFrames(context,clipped,clock,()=>entered.HasValue &&
            clock.Timestamp-entered.Value>=clippingAfterMs && clock.Timestamp-entered.Value<clippingAfterMs+2000);
        using var engine=new FishingEngine(new Settings {EnableRecording=false,EnableAutoClaimAquarium=false,
            EnableAntiAfk=false,EnableWatchdogRecovery=false,EnableHumanizedJitter=false},
            frames,frames,clock:clock,desktop:new Desktop {ClientWidth=3440,ClientHeight=1369},hardware:input);
        engine.OnTelemetry+=t=>
        {
            if(t.State==MacroState.Reeling) entered??=clock.Timestamp;
            if(entered.HasValue && clock.Timestamp-entered.Value>=clippingAfterMs &&
                clock.Timestamp-entered.Value<clippingAfterMs+2000 && t.DetectorPath=="Duskwire" &&
                t.ObservedBarCenter is >=1716 and <=1724)
            {clippedSeen=true;measurements++;}
            if(entered.HasValue && t.State==MacroState.PostCatch) abandoned=true;
            if(!completed && entered.HasValue && clock.Timestamp-entered.Value>=clippingAfterMs+2300)
            {completed=true;engine.Stop();}
            t.Dispose();
        };
        clock.Tick=()=>{if(clock.Timestamp>clippingAfterMs+10000)engine.Stop();};
        engine.Start();
        try {await engine.Completion.WaitAsync(TimeSpan.FromSeconds(40));}
        finally {engine.Stop();}
        Assert.False(abandoned,"The recorded visible bar was abandoned during the clipped capture.");
        Assert.True(completed && clippedSeen && measurements>=5,$"completed={completed}; clipped measurements={measurements}");
        Assert.Equal(0,engine.UnknownCatches);Assert.Equal(0,engine.TotalFails);
        Assert.False(input.Held);
    }

    [Theory]
    [InlineData(20,false)]
    [InlineData(45,false)]
    [InlineData(100,false)]
    [InlineData(45,true)]
    public async Task RecordedUnresponsiveHoldProducesAReleaseAndNewPressInsteadOfAnotherDownOnly(int captureMs,bool stopDuringRelease)
    {
        using var stuck=DuskwireLiveFailureTests.Frame(143238);
        var clock=new Clock {CaptureMs=captureMs};var input=new Input(clock);
        using var frames=new Frames(()=>stuck.Clone(),clock);
        using var engine=new FishingEngine(new Settings {EnableRecording=false,EnableAutoClaimAquarium=false,
            EnableAntiAfk=false,EnableWatchdogRecovery=false,EnableHumanizedJitter=false},
            frames,frames,clock:clock,desktop:new Desktop {ClientWidth=3440,ClientHeight=1369},hardware:input);
        long? entered=null,releasedAt=null;int initialDown=-1;bool recovered=false,cancelled=false;var trace=new List<string>();
        engine.OnTelemetry+=t=>
        {
            trace.Add($"{clock.Timestamp} {t.State} {t.Action} {t.DetectorPath} bar={t.BarLeft}..{t.BarRight} fish={t.ObservedFishX}");
            if(t.State==MacroState.Reeling)
            {
                entered??=clock.Timestamp;
                if(initialDown<0 && input.Held) initialDown=input.Events.FindLastIndex(e=>e=="down");
                if(stopDuringRelease && initialDown>=0)
                    input.AfterUp=()=>{input.AfterUp=null;releasedAt=clock.Timestamp;cancelled=true;engine.Stop();};
                if(t.Action.Contains("unresponsive right hold"))
                {
                    var edges=input.Events.Skip(initialDown+1).Where(e=>e is "up" or "down").ToArray();
                    recovered=edges.SequenceEqual(new[]{"up","down"}) && input.Held;
                    engine.Stop();
                }
            }
            t.Dispose();
        };
        clock.Tick=()=>{if(clock.Timestamp>10000 || entered.HasValue && clock.Timestamp-entered.Value>2500)engine.Stop();};
        engine.Start();
        try {await engine.Completion.WaitAsync(TimeSpan.FromSeconds(30));}
        finally {engine.Stop();}
        if(stopDuringRelease)
        {
            Assert.True(cancelled);
            Assert.InRange(releasedAt!.Value-entered!.Value,450,1100);
            Assert.DoesNotContain("down",input.Events.Skip(initialDown+1));
        }
        else Assert.True(recovered,string.Join(",",input.Events)+"\n"+string.Join("\n",trace.TakeLast(20)));
        Assert.Equal(0,engine.UnknownCatches);Assert.Equal(0,engine.TotalFails);
        Assert.False(input.Held);
    }
}
