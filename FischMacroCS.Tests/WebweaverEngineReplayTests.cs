using FischMacroCS.Core;
using OpenCvSharp;
using System.IO;

namespace FischMacroCS.Tests;

public partial class FishingEngineReplayTests
{
    [Theory]
    [InlineData(70)] [InlineData(120)]
    public async Task PointedRecordedOrderConfirmsPlayerCatchAndReentersNextReel(int cadence)
    {
        // Original21->24->25->29 relative times are unchanged. Holding the
        // sparse saved images at these cadences is explicitly synthetic.
        var clock=new Clock{CaptureMs=cadence};var input=new Input(clock);
        var desktop=new Desktop{ClientWidth=3440,ClientHeight=1369};
        var images=new[]{21,24,25,29}.ToDictionary(id=>id,id=>Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,"Fixtures","webweaver",$"frame_{id:D8}.png")));
        long? started=null;int phase=21;var phases=new HashSet<int>();bool postCatch=false,secondCast=false;int nextObservations=0;
        using var frames=new Frames(()=>
        {
            started??=clock.Timestamp;double elapsed=clock.Timestamp-started.Value;
            phase=elapsed<3065.541?21:elapsed<4150.0112?24:elapsed<8456.4077?25:29;
            var source=images[phase];var full=new Mat(1369,3440,MatType.CV_8UC3,Scalar.All(35));
            using var roi=new Mat(full,new Rect(840,800,source.Width,source.Height));source.CopyTo(roi);return full;
        },clock);
        using var engine=new FishingEngine(new Settings{EnableRecording=false,EnableAutoClaimAquarium=false,
            EnableAntiAfk=false,EnableWatchdogRecovery=false},frames,frames,clock:clock,desktop:desktop,hardware:input);
        engine.OnTelemetry+=t=>
        {
            if(t.State==MacroState.Reeling&&t.ObservedBarCenter.HasValue){phases.Add(phase);if(phase==29&&++nextObservations>=4)engine.Stop();}
            if(t.State==MacroState.PostCatch)postCatch=true;
            if(postCatch&&t.State==MacroState.Casting)secondCast=true;
            t.Dispose();
        };
        clock.Tick=()=>{if(clock.Timestamp>20000)engine.Stop();};
        engine.Start();
        try{await engine.Completion.WaitAsync(TimeSpan.FromSeconds(30));}
        finally{engine.Stop();foreach(var image in images.Values)image.Dispose();}
        Assert.True(postCatch);Assert.True(secondCast);Assert.True(nextObservations>=4,$"next={nextObservations}; pause={engine.PauseReason}");
        Assert.Contains(21,phases);Assert.Contains(24,phases);Assert.Contains(29,phases);
        Assert.Equal(1,engine.TotalCatches);Assert.Equal(0,engine.TotalFails);Assert.Equal(0,engine.UnknownCatches);
        Assert.False(input.Held);Assert.Empty(input.HeldKeys);
    }

    [Theory]
    [InlineData(70,"stop")] [InlineData(120,"focus")] [InlineData(120,"resize")]
    public async Task PointedLayoutAcquiresOutsideLegacyCropAndKeepsGuardedInput(int cadence,string interruption)
    {
        var clock=new Clock{CaptureMs=cadence};var input=new Input(clock);
        var desktop=new Desktop{ClientWidth=3440,ClientHeight=1369};
        using var source=Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,"Fixtures","webweaver","frame_00000021.png"));
        using var frames=new Frames(()=>
        {
            var full=new Mat(1369,3440,MatType.CV_8UC3,Scalar.All(35));
            using var roi=new Mat(full,new Rect(840,800,source.Width,source.Height));source.CopyTo(roi);return full;
        },clock);
        using var engine=new FishingEngine(new Settings{EnableRecording=false,EnableAutoClaimAquarium=false,
            EnableAntiAfk=false,EnableWatchdogRecovery=false},frames,frames,clock:clock,desktop:desktop,hardware:input);
        int observations=0;bool extended=false;bool interrupted=false;
        var samples=new List<(double Center,int Width,int Fish,string Path,double DetectionMs,long CaptureTicks)>();
        var states=new List<string>();
        frames.BeforeCapture=roi=>{if(engine.CurrentState==MacroState.Reeling&&roi.Bottom>=1320)extended=true;};
        engine.OnTelemetry+=t=>
        {
            states.Add($"{t.State}:{t.Action}");
            if(t.State==MacroState.Reeling&&t.ObservedBarCenter.HasValue)
            {
                observations++;
                samples.Add((t.ObservedBarCenter.Value,t.BarWidth,t.ObservedFishX??0,t.DetectorPath,t.DetectionLatencyMs,t.ObservationTimestampTicks));
                if(observations>=4){interrupted=true;if(interruption=="focus")desktop.Focused=false;
                    else if(interruption=="resize")desktop.ClientWidth++;else engine.Stop();}
            }
            t.Dispose();
        };
        clock.Tick=()=>{if(clock.Timestamp>12000)engine.Stop();};
        engine.Start();try{await engine.Completion.WaitAsync(TimeSpan.FromSeconds(20));}finally{engine.Stop();}
        Assert.True(interrupted,$"observations={observations}; pause={engine.PauseReason}; states={string.Join(';',states.TakeLast(10))}");
        Assert.True(observations>=4);Assert.True(extended);
        Assert.All(samples,s=>
        {
            // Telemetry BarLeft/Right are control estimates; check the actual
            // observed center and measured width against original pixels.
            Assert.InRange(s.Center-s.Width/2.0,1352,1356);Assert.InRange(s.Center+s.Width/2.0,1805,1809);
            Assert.InRange(s.Fish,1609,1613);Assert.Equal("PointedFlared",s.Path);
            Assert.True(s.DetectionMs>0);Assert.True(s.CaptureTicks>0);
        });
        Assert.Contains("down",input.Events);Assert.False(input.Held);Assert.Empty(input.HeldKeys);
    }
}
