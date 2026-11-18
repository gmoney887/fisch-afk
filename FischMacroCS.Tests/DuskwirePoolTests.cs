using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;

namespace FischMacroCS.Tests;

public class DuskwirePoolTests
{
    internal static Mat Frame(int id)=>Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,
        "Fixtures","duskwire-pool-nov06",$"frame_{id:00000000}.png"));

    [Theory]
    [InlineData(768,1685,1755,1720)]
    [InlineData(769,1026,1096,1457)]
    public void PoolReelUsesItsActuallyNarrowBody(int id,int left,int right,int fish)
    {
        foreach(int height in new[]{1369,1080,720})
        {
            using var original=Frame(id);using var full=new Mat();double scale=height/1369.0;
            Cv2.Resize(original,full,new Size((int)Math.Round(original.Width*scale),height));
            var vision=new VisionProcessor();
            using(var reading=vision.ProcessReelContext(full,MinigameTheme.AutoCalibrate))
            {
                Assert.True(reading.DetectorPath=="Duskwire",$"Frame{id} at{height}px used{reading.DetectorPath}");
                Assert.True(reading.HasLiveReel && reading.ReelProgressFound && reading.BarFound);
                Assert.InRange(reading.BarLeft,left*scale-5,left*scale+5);
                Assert.InRange(reading.BarRight,right*scale-5,right*scale+5);
                Assert.InRange(reading.BarWidth,(right-left)*scale-6,(right-left)*scale+6);
                Assert.InRange(reading.FishX,fish*scale-4,fish*scale+4);
            }
            var ordinary=new Rect(full.Width/2-(int)(height*.55),(int)(height*.74),2*(int)(height*.55),(int)(height*.94)-(int)(height*.74));
            var region=vision.GetReelCaptureRegion(ordinary,full.Size());
            using var crop=new Mat(full,region);
            using var active=vision.ProcessTrack(crop,region.X,region.Y,height/1080.0,MinigameTheme.AutoCalibrate,false);
            Assert.Equal("Duskwire",active.DetectorPath);
            Assert.InRange(active.BarWidth,(right-left)*scale-6,(right-left)*scale+6);
            Assert.InRange(active.FishX,fish*scale-4,fish*scale+4);
        }
    }

    [Fact]
    public void PoolReelDisappearanceMustNotReuseTheNarrowBody()
    {
        var vision=new VisionProcessor();using var active=Frame(768);using var idle=Frame(770);
        using(var result=vision.ProcessReelContext(active,MinigameTheme.AutoCalibrate))Assert.True(result.HasLiveReel);
        using(var result=vision.ProcessReelContext(idle,MinigameTheme.AutoCalibrate))Assert.False(result.HasLiveReel);
    }

    [Fact]
    public void PoolFixturesRemainTheActualFailurePixels()
    {
        string directory=Path.Combine(AppContext.BaseDirectory,"Fixtures","duskwire-pool-nov06");
        using var provenance=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"provenance.json")));
        foreach(var frame in provenance.RootElement.GetProperty("Frames").EnumerateArray())
            Assert.Equal(frame.GetProperty("SHA256").GetString(),Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(Path.Combine(directory,frame.GetProperty("File").GetString()!)))));
    }
}

public partial class FishingEngineReplayTests
{
    [Fact]
    public async Task PoolDuskwireWorkerEntersReelingInsteadOfWaitingForTheLureDeadline()
    {
        using var source=DuskwirePoolTests.Frame(769);
        var clock=new Clock {CaptureMs=110};var input=new Input(clock);
        using var frames=new Frames(()=>source.Clone(),clock);
        using var engine=new FishingEngine(new Settings {EnableRecording=false,EnableAutoClaimAquarium=false,
            EnableAntiAfk=false,EnableWatchdogRecovery=false,EnableHumanizedJitter=false},frames,frames,
            clock:clock,desktop:new Desktop {ClientWidth=3440,ClientHeight=1369},hardware:input);
        bool controlled=false;
        engine.OnTelemetry+=t=>{
            if(t.State==MacroState.Reeling && t.DetectorPath=="Duskwire" && t.BarRight>0)
            {
                Assert.InRange(t.BarLeft,1021,1031);Assert.InRange(t.BarRight,1091,1101);
                Assert.InRange(t.FishX,1453,1461);Assert.Contains("RIGHT",t.Action);
                controlled=input.Held;engine.Stop();
            }
            t.Dispose();
        };
        clock.Tick=()=>{if(clock.Timestamp>5000)engine.Stop();};
        engine.Start();
        try {await engine.Completion.WaitAsync(TimeSpan.FromSeconds(25));}
        finally {engine.Stop();}
        Assert.True(controlled,"Visible narrow pool reel never received control");Assert.False(input.Held);
    }
}
