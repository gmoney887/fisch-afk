using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;
using System.IO;
using System.Text.Json;
using System.Security.Cryptography;

namespace FischMacroCS.Tests;

public class DuskwireMegalodonTests
{
    internal static string DirectoryPath=>Path.Combine(AppContext.BaseDirectory,"Fixtures","duskwire-megalodon-nov06");
    internal static Mat Frame(int id)=>Cv2.ImRead(Path.Combine(DirectoryPath,$"frame_{id:00000000}.png"));

    // Body outline/capsule annotations from the raw pixels, independent of telemetry.
    [Theory]
    [InlineData(1507,0,0,1667,1807,1777)]
    [InlineData(1536,0,0,1468,1608,1555)]
    [InlineData(1555,0,0,1302,1442,1399)]
    [InlineData(1566,968,1013,1266,1406,1445)]
    [InlineData(1580,0,0,1026,1166,2019)]
    [InlineData(1597,0,0,2139,2279,2133)]
    [InlineData(1617,0,0,1988,2128,2050)]
    [InlineData(1620,968,1013,1980,2120,2043)]
    public void MegalodonPoolMeasuresTheBodyRatherThanItsBackground(int id,int x,int y,int left,int right,int fish)
    {
        using var frame=Frame(id);var vision=new VisionProcessor();
        using var result=y==0?vision.ProcessReelContext(frame,MinigameTheme.AutoCalibrate)
            :vision.ProcessTrack(frame,x,y,1369/1080.0,MinigameTheme.AutoCalibrate,false);
        Assert.Equal("Duskwire",result.DetectorPath);
        Assert.True(result.HasLiveReel && result.BarFound);
        Assert.InRange(result.BarLeft,left-5,left+5);Assert.InRange(result.BarRight,right-5,right+5);
        Assert.InRange(result.FishX,fish-4,fish+4);
    }

    [Fact]
    public void MegalodonFixturesPreserveRawPixelsAndActualCaptureOffsets()
    {
        using var provenance=JsonDocument.Parse(File.ReadAllText(Path.Combine(DirectoryPath,"provenance.json")));
        foreach(var entry in provenance.RootElement.GetProperty("Frames").EnumerateArray())
        {
            string path=Path.Combine(DirectoryPath,entry.GetProperty("File").GetString()!);
            Assert.Equal(entry.GetProperty("SHA256").GetString(),Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
            using var frame=Cv2.ImRead(path);var region=entry.GetProperty("Region");
            Assert.Equal(frame.Width,region.GetProperty("Width").GetInt32());
            Assert.Equal(frame.Height,region.GetProperty("Height").GetInt32());
        }
    }

    [Fact]
    public void WidePostCatchCaptureStillContainsTheSameLiveMegalodonReel()
    {
        using var frame=Frame(1574);var vision=new VisionProcessor();
        using var result=vision.ProcessTrack(frame,0,1027,1369/1080.0,MinigameTheme.AutoCalibrate,false);
        Assert.Equal("Duskwire",result.DetectorPath);Assert.True(result.HasLiveReel && result.BarFound);
        Assert.InRange(result.BarWidth,132,144);Assert.InRange(result.FishX,1858,1866);
    }

    [Fact]
    public void UnavailableGameplayDoesNotInheritTheMegalodonBody()
    {
        var vision=new VisionProcessor();using var active=Frame(1617);
        using(var result=vision.ProcessReelContext(active,MinigameTheme.AutoCalibrate))Assert.True(result.HasLiveReel);
        using var unavailable=Frame(1821);
        using(var result=vision.ProcessTrack(unavailable,0,1027,1369/1080.0,MinigameTheme.AutoCalibrate,false))
            Assert.False(result.HasLiveReel);
    }
}

public partial class FishingEngineReplayTests
{
    [Fact]
    public async Task MegalodonPoolWorkerKeepsControllingTheVisibleReelAcrossBackgroundChanges()
    {
        var ids=new[]{1507,1536,1555,1580,1597,1617};
        var sources=ids.Select(DuskwireMegalodonTests.Frame).ToArray();
        var clock=new Clock {CaptureMs=110};var input=new Input(clock);
        long? entered=null;int controlled=0;bool abandoned=false;
        using var frames=new Frames(()=>sources[entered.HasValue
            ?(int)Math.Min(ids.Length-1,(clock.Timestamp-entered.Value)/700):0].Clone(),clock);
        using var engine=new FishingEngine(new Settings {EnableRecording=false,EnableAutoClaimAquarium=false,
            EnableAntiAfk=false,EnableWatchdogRecovery=false,EnableHumanizedJitter=false},frames,frames,
            clock:clock,desktop:new Desktop {ClientWidth=3440,ClientHeight=1369},hardware:input);
        engine.OnTelemetry+=t=>{
            if(t.State==MacroState.Reeling)
            {
                entered??=clock.Timestamp;
                if(t.DetectorPath=="Duskwire" && t.BarRight-t.BarLeft is >=132 and <=144)controlled++;
            }
            if(entered.HasValue && t.State==MacroState.PostCatch)abandoned=true;
            t.Dispose();
        };
        clock.Tick=()=>{if(entered.HasValue && clock.Timestamp-entered.Value>=5000 || clock.Timestamp>=10000)engine.Stop();};
        engine.Start();
        try {await engine.Completion.WaitAsync(TimeSpan.FromSeconds(30));}
        finally {engine.Stop();foreach(var source in sources)source.Dispose();}
        Assert.NotNull(entered);Assert.True(controlled>=20,$"Only{controlled} valid body controls");
        Assert.False(abandoned,"A visible reel was falsely concluded");Assert.Equal(0,engine.UnknownCatches);
        Assert.False(input.Held);
    }
}
