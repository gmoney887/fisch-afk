using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;
using System.IO;

namespace FischMacroCS.Tests;

public partial class FishingEngineReplayTests
{
    [Theory]
    [InlineData(70)]
    [InlineData(120)]
    [InlineData(200)]
    public async Task FreshSlowObservationsContinueCurrentPursuit(int cadence)
    {
        // Synthetic capture latency, with real native ProcessTrack on an
        // existing reviewed fixture. This is an input-continuity regression;
        // it does not certify sustained centering or a Perfect catch.
        var clock = new Clock { CaptureMs = cadence };
        var input = new Input(clock);
        using var source = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,"Fixtures","oath_red_45.png"));
        using var reel = new Mat();
        double ratio=Desktop.Height/1369.0;
        Cv2.Resize(source,reel,new Size((int)Math.Round(source.Width*ratio),(int)Math.Round(source.Height*ratio)));
        using var frames=new Frames(()=>{
            var full=new Mat(Desktop.Height,Desktop.Width,MatType.CV_8UC3,Scalar.All(20));
            using var target=new Mat(full,new Rect((full.Width-reel.Width)/2,(int)Math.Round(1013*ratio),reel.Width,reel.Height));
            reel.CopyTo(target);return full;
        },clock);
        using var engine=new FishingEngine(new Settings { EnableRecording=false,EnableAutoClaimAquarium=false,
            EnableAntiAfk=false,EnableWatchdogRecovery=false,EnableHumanizedJitter=false },frames,frames,
            clock:clock,desktop:new Desktop(),hardware:input);
        int fresh=0;
        engine.OnTelemetry+=t=>{
            if(t.State==MacroState.Reeling && t.BarWidth>0){
                Assert.True(t.Error>0,$"Current fish is right of the observed bar: {t.Error}");
                Assert.True(input.Held,$"Fresh {cadence}ms observation must pursue the visible fish: {t.Action}");
                if(++fresh==5)engine.Stop();
            }
            t.Dispose();
        };
        clock.Tick=()=>{if(clock.Timestamp>12000)engine.Stop();};
        engine.Start();
        try { await engine.Completion.WaitAsync(TimeSpan.FromSeconds(20)); }
        finally {engine.Stop();}
        Assert.Equal(5,fresh);
        Assert.False(input.Held);
    }
}
