using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;

namespace FischMacroCS.Tests;

public class DuskwireTests
{
    public static Mat Frame(int id) => Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,
        "Fixtures", "duskwire-nov06", $"frame_{id:00000000}.png"));

    // Reviewed against the raw 3440x1369 pixels, including the left travel stop
    // and the gradient body when the white marker occludes the musical note.
    public static readonly (int Id,int Left,int Right,int Fish)[] Samples =
    [(12,1651,1791,1720),(20,1651,1791,1720),(29,1542,1682,1756),
     (36,1071,1211,1942),(44,1026,1166,2064),(82,1651,1791,1720),
     (85,1651,1791,1720),(94,1576,1716,1720),(102,1036,1176,1847),(109,1026,1166,1931)];

    [Theory]
    [InlineData(1369, false)]
    [InlineData(1080, false)]
    [InlineData(720, false)]
    [InlineData(1369, true)]
    public void RecordedDuskwireTracksActualDarkBodyAndWhiteMarker(int height,bool fullContext)
    {
        var vision=new VisionProcessor();
        var errors=new List<string>();
        foreach(var sample in Samples)
        {
            using var original=Frame(sample.Id);
            double scale=height/1369.0;
            using var full=new Mat();
            Cv2.Resize(original,full,new Size((int)Math.Round(original.Width*scale),height));
            int half=Math.Min(full.Width/2,(int)(height*.55)),top=(int)(height*.74),bottom=(int)(height*.94);
            var region=vision.GetReelCaptureRegion(new Rect(full.Width/2-half,top,half*2,bottom-top),full.Size());
            using var crop=new Mat(full,region);
            using var result=fullContext ? vision.ProcessReelContext(full,MinigameTheme.AutoCalibrate)
                : vision.ProcessTrack(crop,region.X,region.Y,height/1080.0,MinigameTheme.AutoCalibrate,false);
            double tolerance=Math.Max(3,6*scale);
            if(!result.BarFound || !result.FishFound || !result.ReelProgressFound ||
                Math.Abs(result.BarLeft-sample.Left*scale)>tolerance ||
                Math.Abs(result.BarRight-sample.Right*scale)>tolerance ||
                Math.Abs(result.FishX-sample.Fish*scale)>tolerance)
                errors.Add($"{sample.Id}: expected {sample.Left*scale:F1}..{sample.Right*scale:F1} fish{sample.Fish*scale:F1}; got {result.BarLeft}..{result.BarRight} fish{result.FishX} B{result.BarFound} F{result.FishFound} P{result.ReelProgressFound}");
        }
        Assert.True(errors.Count==0,string.Join(Environment.NewLine,errors));
    }

    [Fact]
    public void RecordedIdleAndVanishedDuskwireDoNotKeepAReelAlive()
    {
        var vision=new VisionProcessor();
        foreach(int id in new[]{12,53,69,80})
        {
            using var frame=Frame(id);
            using var result=vision.ProcessReelContext(frame,MinigameTheme.AutoCalibrate);
            if(id==12) Assert.True(result.HasLiveReel);
            else Assert.False(result.HasLiveReel,$"Frame{id} falsely kept a reel alive");
        }
    }

    [Fact]
    public void DuskwireFixturesRemainTheReviewedUnmodifiedRecordingPixels()
    {
        string directory=Path.Combine(AppContext.BaseDirectory,"Fixtures","duskwire-nov06");
        using var provenance=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"provenance.json")));
        foreach(var frame in provenance.RootElement.GetProperty("frames").EnumerateArray())
            Assert.Equal(frame.GetProperty("sha256").GetString(),Convert.ToHexStringLower(SHA256.HashData(
                File.ReadAllBytes(Path.Combine(directory,frame.GetProperty("file").GetString()!)))));
    }

    [Theory]
    [InlineData("body")]
    [InlineData("marker")]
    [InlineData("meter")]
    public void MissingCurrentComponentCannotReuseAPreviousDuskwireObservation(string component)
    {
        var vision=new VisionProcessor();
        using var visible=Frame(29);
        using(var initial=vision.ProcessReelContext(visible,MinigameTheme.AutoCalibrate))
            Assert.Equal("Duskwire",initial.DetectorPath);
        using var missing=visible.Clone();using var idle=Frame(69);
        Rect removed=component switch {
            "body"=>new Rect(1538,1127,150,80),
            "marker"=>new Rect(1745,1104,23,122),
            _=>new Rect(1300,1235,850,70)};
        using(var from=new Mat(idle,removed))using(var to=new Mat(missing,removed))from.CopyTo(to);
        using(var result=vision.ProcessReelContext(missing,MinigameTheme.AutoCalibrate))
            Assert.False(result.HasLiveReel,$"Missing {component} was treated as a current reel");
        using(var result=vision.ProcessReelContext(visible,MinigameTheme.AutoCalibrate))
            Assert.Equal("Duskwire",result.DetectorPath);
    }

    [Fact]
    public void DuskwireHintDoesNotHideTheNextOrdinaryRod()
    {
        var vision=new VisionProcessor();using var duskwire=Frame(29);
        using(var result=vision.ProcessReelContext(duskwire,MinigameTheme.AutoCalibrate))Assert.True(result.HasLiveReel);
        using var ordinary=Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,"Fixtures","reel-stall-nov05","frame_00000481.png"));
        using(var result=vision.ProcessReelContext(ordinary,MinigameTheme.AutoCalibrate))
        {Assert.True(result.HasLiveReel);Assert.NotEqual("Duskwire",result.DetectorPath);}
    }

    [Fact]
    public void ExplicitDuskwireAndColdEntryMeasureTheAnimatedReel()
    {
        foreach(var theme in new[]{MinigameTheme.AutoCalibrate,MinigameTheme.Duskwire})
        {
            var vision=new VisionProcessor();using var entry=Frame(82);
            using var result=vision.ProcessReelContext(entry,theme);
            Assert.Equal("Duskwire",result.DetectorPath);
            Assert.True(result.HasLiveReel && result.BarFound && result.ReelProgressFound);
            Assert.InRange(result.FishX,1717,1723);
        }
    }

    [Fact]
    public void DuskwireProgressMeasuresTheBlackFillRatherThanTheBrightBackground()
    {
        foreach(var sample in new[]{(12,70),(20,131),(29,202),(36,111),(82,48),(94,190)})
        {
            using var frame=Frame(sample.Item1);
            double? fill=VisionProcessor.MeasureReelProgressFill(frame,0,1369,MinigameTheme.Duskwire);
            Assert.True(fill.HasValue,$"No black fill measured on frame{sample.Item1}");
            Assert.InRange(fill!.Value*1369,sample.Item2-3,sample.Item2+3);
        }
        using var absent=Frame(69);
        Assert.Null(VisionProcessor.MeasureReelProgressFill(absent,0,1369,MinigameTheme.Duskwire));
    }
}

public partial class FishingEngineReplayTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuskwireWorkerRenewsOnlyTheAdvancingBlackProgressDeadline(bool frozen)
    {
        var clock=new Clock {CaptureMs=110};var input=new Input(clock);
        using var early=DuskwireTests.Frame(12);using var middle=DuskwireTests.Frame(20);using var later=DuskwireTests.Frame(29);
        long? entered=null;bool survived=false,stalled=false;
        using var frames=new Frames(()=>{
            double elapsed=entered.HasValue ? clock.Timestamp-entered.Value : 0;
            return (!frozen && elapsed>=40000 ? later : !frozen && elapsed>=20000 ? middle : early).Clone();
        },clock);
        using var engine=new FishingEngine(new Settings {EnableRecording=false,EnableAutoClaimAquarium=false,
            EnableAntiAfk=false,EnableHumanizedJitter=false,EnableWatchdogRecovery=true,
            ReelTimeoutMs=35000,WatchdogStallTimeoutSeconds=50},frames,frames,
            clock:clock,desktop:new Desktop {ClientWidth=3440,ClientHeight=1369},hardware:input);
        engine.OnTelemetry+=t=>{
            if(t.State==MacroState.Reeling)entered??=clock.Timestamp;
            if(t.Action.Contains("Reel progress stalled")){stalled=true;engine.Stop();}
            if(entered.HasValue && clock.Timestamp-entered.Value>=53000 && t.State==MacroState.Reeling)
            {survived=true;engine.Stop();}
            t.Dispose();
        };
        clock.Tick=()=>{if(clock.Timestamp>65000)engine.Stop();};
        engine.Start();
        try {await engine.Completion.WaitAsync(TimeSpan.FromSeconds(45));}
        finally {engine.Stop();}
        Assert.NotNull(entered);
        Assert.Equal(!frozen,survived);Assert.Equal(frozen,stalled);Assert.False(input.Held);
    }

    [Fact]
    public async Task RecordedDuskwireWorkerMeasuresTheActualBarAndPullsTowardItsWhiteMarker()
    {
        var clock=new Clock();var input=new Input(clock);
        using var source=DuskwireTests.Frame(29);
        using var frames=new Frames(()=>source.Clone(),clock);
        using var engine=new FishingEngine(new Settings {EnableRecording=false,EnableAutoClaimAquarium=false,
            EnableAntiAfk=false,EnableWatchdogRecovery=false,EnableHumanizedJitter=false},frames,frames,
            clock:clock,desktop:new Desktop {ClientWidth=3440,ClientHeight=1369},hardware:input);
        bool acted=false;
        engine.OnTelemetry+=t=>{
            if(t.State==MacroState.Reeling && t.DetectorPath=="Duskwire" && t.BarRight>0)
            {
                Assert.InRange(t.BarLeft,1536,1548);Assert.InRange(t.BarRight,1676,1688);
                Assert.InRange(t.FishX,1753,1759);Assert.Contains("RIGHT",t.Action);
                acted=input.Held;engine.Stop();
            }
            t.Dispose();
        };
        clock.Tick=()=>{if(clock.Timestamp>10000)engine.Stop();};
        engine.Start();
        try {await engine.Completion.WaitAsync(TimeSpan.FromSeconds(30));}
        finally {engine.Stop();}
        Assert.True(acted,"Worker never controlled the measured Duskwire bar");
        Assert.False(input.Held);
    }
}
