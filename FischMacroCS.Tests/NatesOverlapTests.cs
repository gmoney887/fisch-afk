using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace FischMacroCS.Tests;

public class NatesOverlapTests
{
    [Fact]
    public void ForegroundFailureFixturesRemainOriginalRecorderPixels()
    {
        string directory=Path.Combine(AppContext.BaseDirectory,"Fixtures","nates-overlap-nov08");
        using var provenance=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"provenance.json")));
        foreach(var frame in provenance.RootElement.GetProperty("Frames").EnumerateArray())
        {
            string path=Path.Combine(directory,frame.GetProperty("File").GetString()!);
            Assert.Equal(frame.GetProperty("SHA256").GetString(),Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
            using var image=Cv2.ImRead(path);var region=frame.GetProperty("Region");
            Assert.Equal(region.GetProperty("Width").GetInt32(),image.Width);
            Assert.Equal(region.GetProperty("Height").GetInt32(),image.Height);
        }
    }

    [Theory]
    [InlineData(720)]
    [InlineData(1080)]
    [InlineData(2160)]
    public void ForegroundAndMeterSeparationScalesWithTheViewport(int height)
    {
        using var frame=Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,"Fixtures","nates-overlap-nov08","frame_00000012.png"));
        using var scaled=new Mat();double ratio=height/1369.0;
        Cv2.Resize(frame,scaled,new Size((int)Math.Round(frame.Width*ratio),(int)Math.Round(frame.Height*ratio)));
        using var result=new VisionProcessor().ProcessTrack(scaled,(int)Math.Round(968*ratio),(int)Math.Round(1013*ratio),height/1080.0,MinigameTheme.AutoCalibrate,false);
        Assert.True(result.HasLiveReel);
        Assert.InRange(result.FishX,(int)(1714*ratio),(int)(1727*ratio));
        Assert.InRange(result.BarLeft,(int)(1475*ratio),(int)(1520*ratio));
        Assert.InRange(result.BarRight,(int)(1920*ratio),(int)(1960*ratio));
        Assert.Equal(MinigameTheme.Ruinous,result.ResolvedTheme);
    }
    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(24)]
    [InlineData(14)]
    public void BrightCompanionBehindTheOpeningGlyphCannotReplaceTheFishAndBar(int number)
    {
        using var frame = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "nates-overlap-nov08", $"frame_{number:D8}.png"));
        Assert.False(frame.Empty(), "Original fixture missing");
        var vision = new VisionProcessor();
        using var result = vision.ProcessTrack(frame, 968, 1013, 1369.0 / 1080, MinigameTheme.AutoCalibrate, false);
        string evidence = $"theme={result.ResolvedTheme}; body={result.BarLeft}..{result.BarRight}; fish={result.FishX}";
        Assert.True(result.HasLiveReel, evidence);
        Assert.InRange(result.FishX, 1714, 1727);
        Assert.InRange(result.BarLeft, 1475, 1520);
        Assert.InRange(result.BarRight, 1920, 1960);
        Assert.Equal(MinigameTheme.Ruinous, result.ResolvedTheme);
    }

    [Theory]
    [InlineData("fish")]
    [InlineData("bar")]
    [InlineData("meter")]
    public void ForegroundEffectCannotReplaceAMissingCurrentReelComponent(string missing)
    {
        using var frame = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "nates-overlap-nov08", "frame_00000012.png"));
        var box = missing switch {
            "fish" => new Rect(728,80,52,125),
            "bar" => new Rect(530,115,440,80),
            _ => new Rect(0,219,frame.Width,frame.Height-219)
        };
        using(var area=new Mat(frame,box))area.SetTo(Scalar.Black);
        using var result=new VisionProcessor().ProcessTrack(frame,968,1013,1369.0/1080,MinigameTheme.AutoCalibrate,false);
        Assert.False(result.HasLiveReel,$"Missing {missing}: {result.ResolvedTheme}, fish={result.FishX}, body={result.BarLeft}..{result.BarRight}");
    }

    [Fact]
    public void RecordedAppearanceSequenceCannotLockOntoTheCompanion()
    {
        var vision=new VisionProcessor();
        foreach(int number in new[]{6,8,9,11,12,14,20,24})
        {
            using var frame=Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,"Fixtures","nates-overlap-nov08",$"frame_{number:D8}.png"));
            Assert.False(frame.Empty());
            using var result=vision.ProcessTrack(frame,968,1013,1369.0/1080,MinigameTheme.AutoCalibrate,false);
            if(number<11)continue;
            Assert.True(result.HasLiveReel);
            Assert.InRange(result.FishX,1714,1727);
            Assert.InRange(result.BarLeft,1475,1520);
            Assert.InRange(result.BarRight,1920,1960);
            Assert.Equal(MinigameTheme.Ruinous,result.ResolvedTheme);
        }
    }
}

public partial class FishingEngineReplayTests
{
    [Fact]
    public async Task RecordedNateAppearanceKeepsItsRealTargetAndFollowsTheFirstMovement()
    {
        string directory=Path.Combine(AppContext.BaseDirectory,"Fixtures","nates-overlap-nov08");
        using var full=Cv2.ImRead(Path.Combine(directory,"frame_00000016.png"));
        using var initial=Cv2.ImRead(Path.Combine(directory,"frame_00000012.png"));
        using var movement=Cv2.ImRead(Path.Combine(directory,"frame_00000031.png"));
        var clock=new Clock();var input=new Input(clock);FishingEngine? engine=null;
        long? entered=null, firstRight=null;bool expired=false;
        var initialObservations=new List<(double Fish,int Left,int Right,string Action)>();
        Mat Render()
        {
            var image=full.Clone();
            if(engine!.CurrentState==MacroState.Reeling)entered??=clock.Timestamp;
            var strip=entered.HasValue&&clock.Timestamp-entered.Value>=350?movement:initial;
            using var target=new Mat(image,new Rect(968,1013,strip.Width,strip.Height));strip.CopyTo(target);
            return image;
        }
        using var frames=new Frames(Render,clock);
        using var running=engine=new FishingEngine(new Settings{EnableRecording=false,EnableAutoClaimAquarium=false,
            EnableAntiAfk=false,EnableWatchdogRecovery=false,EnableHumanizedJitter=false,EnableDynamicCastRelease=false,
            ShakeMode="Disabled"},frames,frames,clock:clock,desktop:new Desktop{ClientWidth=3440,ClientHeight=1369},hardware:input);
        engine.OnTelemetry+=t=>{
            if(t.State==MacroState.Reeling && t.ObservedFishX.HasValue && entered.HasValue)
            {
                long elapsed=clock.Timestamp-entered.Value;
                if(elapsed<350)
                {
                    initialObservations.Add((t.ObservedFishX.Value,t.BarLeft,t.BarRight,t.Action));
                }
                if(elapsed>=350 && t.ObservedFishX>=1746 && t.IsMouseDown){firstRight??=elapsed;engine.Stop();}
            }
            t.Dispose();
        };
        clock.Tick=()=>{if(clock.Timestamp>3000){expired=true;engine.Stop();}};
        engine.Start();
        try{await engine.Completion.WaitAsync(TimeSpan.FromSeconds(20));}
        finally{engine.Stop();}
        Assert.False(expired);Assert.True(initialObservations.Count>=2);Assert.NotNull(firstRight);
        Assert.All(initialObservations,t=>{
            Assert.InRange(t.Fish,1714,1727);
            Assert.InRange(t.Left,1475,1520);Assert.InRange(t.Right,1920,1960);
            Assert.DoesNotContain("EMERGENCY LEFT",t.Action);
        });
        Assert.InRange(firstRight.Value-350,0,200);
        Assert.False(input.Held);Assert.Empty(input.HeldKeys);Assert.Null(engine.PauseReason);
        Assert.Equal(0,engine.TotalCatches+engine.TotalFails+engine.UnknownCatches);
    }
}
