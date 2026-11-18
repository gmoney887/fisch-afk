using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;

namespace FischMacroCS.Tests;

public class DuskwireTrackingFailureTests
{
    internal static Mat Frame(string name) => Cv2.ImRead(System.IO.Path.Combine(
        AppContext.BaseDirectory,"Fixtures","duskwire-tracking-nov06",name+".png"));

    [Fact]
    public void RegressionPixelsMatchTheirPreservedRecordings()
    {
        string directory=System.IO.Path.Combine(AppContext.BaseDirectory,"Fixtures","duskwire-tracking-nov06");
        using var provenance=System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(
            System.IO.Path.Combine(directory,"provenance.json")));
        foreach(var frame in provenance.RootElement.GetProperty("Frames").EnumerateArray())
            Assert.Equal(frame.GetProperty("SHA256").GetString(),Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(
                    System.IO.Path.Combine(directory,frame.GetProperty("File").GetString()!)))));
    }

    // Coordinates independently annotated from the saved pixels, not detector output.
    [Theory]
    [InlineData("s2_1491",1034,1242,1595)]
    [InlineData("s2_1492",1049,1257,1578)]
    [InlineData("s1_121195",1026,1234,1982)]
    public void VisiblePoolBodyCannotDisappearOrBecomeAGenericBar(string name,int left,int right,int fish)
    {
        using var crop=Frame(name);
        using var result=new VisionProcessor().ProcessTrack(crop,968,1013,1369/1080.0,MinigameTheme.AutoCalibrate,false);
        Assert.True(result.DetectorPath=="Duskwire" && result.HasLiveReel,
            $"{name}: {result.DetectorPath} bar={result.BarFound}, fish={result.FishFound}, width={result.BarWidth}");
        Assert.InRange(result.BarLeft,left-4,left+4);
        Assert.InRange(result.BarRight,right-4,right+4);
        Assert.InRange(result.FishX,fish-4,fish+4);
    }

    [Fact]
    public void VerticallyShiftedLiveReelIsMeasuredFromItsCurrentPixels()
    {
        using var full=Frame("s1_126338");
        using var result=new VisionProcessor().ProcessReelContext(full,MinigameTheme.AutoCalibrate);
        Assert.True(result.DetectorPath=="Duskwire" && result.HasLiveReel,
            $"{result.DetectorPath} bar={result.BarFound}, fish={result.FishFound}, width={result.BarWidth}");
        Assert.InRange(result.BarWidth,201,214);
        Assert.InRange(result.FishX,result.BarLeft,result.BarRight);
    }

    [Fact]
    public void PointedFastPathStillReacquiresDuskwireOnTheNextFrame()
    {
        using var source=Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory,"Fixtures","webweaver","frame_00000021.png"));
        using var pointed=new Mat(1369,3440,MatType.CV_8UC3,Scalar.All(35));
        using(var target=new Mat(pointed,new Rect(840,800,source.Width,source.Height)))source.CopyTo(target);
        var vision=new VisionProcessor();
        using(var acquired=vision.ProcessReelContext(pointed,MinigameTheme.AutoCalibrate))Assert.Equal("PointedFlared",acquired.DetectorPath);
        using var dusk=Frame("s1_121152");
        using var crop=new Mat(dusk,new Rect(968,1013,1504,356));
        using var changed=vision.ProcessTrack(crop,968,1013,1369/1080.0,MinigameTheme.AutoCalibrate,false);
        Assert.Equal("Duskwire",changed.DetectorPath);Assert.True(changed.HasLiveReel);
        Assert.InRange(changed.BarWidth,201,214);
    }

    [Theory]
    [InlineData("s1_121206")]
    [InlineData("outcome_frame_00097385")]
    [InlineData("outcome_frame_00097402")]
    public void RecordedPlayerLossIsRecognizedUnderCompanionOverlay(string name)
    {
        using var full=Frame(name);
        using var crop=new Mat(full,new Rect(968,1013,1504,full.Height-1013));
        Assert.True(CatchFailureDetector.Detect(crop,1369),name);
    }

    [Theory]
    [InlineData("s1_126352")]
    [InlineData("outcome_frame_00112240")]
    [InlineData("live_1208")]
    [InlineData("live_1287")]
    [InlineData("live_1358")]
    [InlineData("live_1456")]
    [InlineData("live_1543")]
    public void RecordedPlayerCatchIsRecognized(string name)
    {
        using var full=Frame(name);
        using var crop=new Mat(full,new Rect(968,1013,1504,full.Height-1013));
        Assert.True(new VisionProcessor().DetectCatchNotification(crop,1369),name);
    }

    [Theory]
    [InlineData("s1_121206")]
    [InlineData("s1_121152")]
    public void CompanionLossAndActiveReelPixelsAreNotPlayerCatchEvidence(string name)
    {
        using var full=Frame(name);
        using var crop=new Mat(full,new Rect(968,1013,1504,full.Height-1013));
        Assert.False(new VisionProcessor().DetectCatchNotification(crop,1369),name);
    }

    [Theory]
    [InlineData("live_1208")]
    [InlineData("live_1456")]
    public void SettledPlayerCatchCannotInventAnotherLiveReel(string name)
    {
        using var full=Frame(name);
        using var result=new VisionProcessor().ProcessReelContext(full,MinigameTheme.AutoCalibrate);
        Assert.False(result.HasLiveReel,$"{name}: {result.DetectorPath} {result.BarWidth}px");
    }

    [Theory]
    [InlineData("outcome_frame_00097385")]
    [InlineData("outcome_frame_00097402")]
    [InlineData("s1_121206")]
    public void LossAndCompanionCaptionsCannotInventAnotherLiveReel(string name)
    {
        using var full=Frame(name);
        using var result=new VisionProcessor().ProcessReelContext(full,MinigameTheme.AutoCalibrate);
        Assert.False(result.HasLiveReel,$"{name}: {result.DetectorPath} {result.BarWidth}px");
    }

    private sealed class EvidenceClock : IClock
    {
        public long Timestamp {get;set;}
        public double ElapsedMilliseconds(long start)=>Timestamp-start;
        public void Delay(int ms,CancellationToken token)=>Timestamp+=ms;
    }

    [Fact]
    public void ShortRecognitionGapsCannotCountTheSameSavedLossTwice()
    {
        var clock=new EvidenceClock();var evidence=new FreshFailureEvidence(clock);
        using var first=Frame("outcome_frame_00097385");using var last=Frame("outcome_frame_00097402");
        bool Read(Mat full) {using var crop=new Mat(full,new Rect(968,1013,1504,full.Height-1013));return CatchFailureDetector.Detect(crop,1369);}
        Assert.False(evidence.Observe(Read(first)));clock.Timestamp=80;
        Assert.True(evidence.Observe(Read(first)));
        // Brief confidence loss during the same banner animation, not a new attempt.
        clock.Timestamp=160;Assert.False(evidence.Observe(false));
        clock.Timestamp=240;Assert.False(evidence.Observe(false));
        clock.Timestamp=320;Assert.False(evidence.Observe(Read(last)));
        clock.Timestamp=400;Assert.False(evidence.Observe(Read(last)));
        clock.Timestamp=600;Assert.False(evidence.Observe(false));
        clock.Timestamp=1200;Assert.False(evidence.Observe(false));
        using var next=Frame("s1_121206");
        clock.Timestamp=1300;Assert.False(evidence.Observe(Read(next)));
        clock.Timestamp=1380;Assert.True(evidence.Observe(Read(next)));
    }
}

public partial class FishingEngineReplayTests
{
    [Theory]
    [InlineData("s1_121206",false)]
    [InlineData("outcome_frame_00097385",false)]
    [InlineData("outcome_frame_00112240",true)]
    [InlineData("live_1208",true)]
    public async Task RecordedOutcomeIsCountedOnceByTheRealWorker(string banner,bool success)
    {
        using var active=DuskwireTrackingFailureTests.Frame("s1_121152");
        using var outcome=DuskwireTrackingFailureTests.Frame(banner);
        var clock=new Clock {CaptureMs=45};var input=new Input(clock);long? reelStart=null;
        using var frames=new Frames(()=>reelStart.HasValue && clock.Timestamp-reelStart.Value>=1200
            ? outcome.Clone():active.Clone(),clock);
        using var engine=new FishingEngine(new Settings {EnableRecording=false,EnableAutoClaimAquarium=false,
            EnableAntiAfk=false,EnableWatchdogRecovery=false,EnableHumanizedJitter=false},frames,frames,
            clock:clock,desktop:new Desktop {ClientWidth=3440,ClientHeight=1369},hardware:input);
        engine.OnTelemetry+=t=>{if(t.State==MacroState.Reeling)reelStart??=clock.Timestamp;t.Dispose();};
        clock.Tick=()=>{if(clock.Timestamp>8000 || reelStart.HasValue && clock.Timestamp-reelStart.Value>5000)engine.Stop();};
        engine.Start();
        try {await engine.Completion.WaitAsync(TimeSpan.FromSeconds(40));}
        finally {engine.Stop();}
        Assert.NotNull(reelStart);Assert.Equal(success?1:0,engine.TotalCatches);
        Assert.Equal(success?0:1,engine.TotalFails);Assert.Equal(0,engine.UnknownCatches);
    }

    [Fact]
    public async Task TwoSecondRecordedLayoutShiftDoesNotDropControlOrStartAnotherCast()
    {
        using var normal=DuskwireTrackingFailureTests.Frame("s1_121152");
        using var shifted=DuskwireTrackingFailureTests.Frame("s1_126338");
        var clock=new Clock {CaptureMs=45};var input=new Input(clock);
        long? reelStart=null;int shiftedMeasurements=0;var errors=new List<string>();
        using var frames=new Frames(()=>reelStart.HasValue && clock.Timestamp-reelStart.Value>=400 &&
            clock.Timestamp-reelStart.Value<2400 ? shifted.Clone():normal.Clone(),clock);
        using var engine=new FishingEngine(new Settings {EnableRecording=false,EnableAutoClaimAquarium=false,
            EnableAntiAfk=false,EnableWatchdogRecovery=false,EnableHumanizedJitter=false},frames,frames,
            clock:clock,desktop:new Desktop {ClientWidth=3440,ClientHeight=1369},hardware:input);
        engine.OnTelemetry+=t=>{
            if(t.State==MacroState.Reeling)reelStart??=clock.Timestamp;
            if(reelStart.HasValue && clock.Timestamp-reelStart.Value is >=600 and <2300)
            {
                if(t.State!=MacroState.Reeling || t.BarRight<=0 || t.DetectorPath!="Duskwire")
                    errors.Add($"{clock.Timestamp}: {t.State} {t.Action}, {t.DetectorPath} {t.BarLeft}..{t.BarRight}");
                else shiftedMeasurements++;
            }
            if(t.State==MacroState.Reeling && reelStart.HasValue && clock.Timestamp-reelStart.Value>=3000)engine.Stop();
            t.Dispose();
        };
        clock.Tick=()=>{if(clock.Timestamp>8000)engine.Stop();};
        engine.Start();
        try {await engine.Completion.WaitAsync(TimeSpan.FromSeconds(40));}
        finally {engine.Stop();}
        Assert.True(errors.Count==0,string.Join(Environment.NewLine,errors));
        Assert.True(shiftedMeasurements>=5,"Worker never continued measuring the shifted reel");
        Assert.Equal(0,engine.UnknownCatches);Assert.Equal(0,engine.TotalFails);
    }
}
