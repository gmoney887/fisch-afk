using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;

namespace FischMacroCS.Tests;

public class DuskwireDualMeterTests
{
    internal static string Folder => Path.Combine(AppContext.BaseDirectory,"Fixtures","duskwire-dual-meter-nov07");
    internal static Mat Frame(int id) => Cv2.ImRead(Path.Combine(Folder,$"frame_{id:00000000}.png"));

    [Fact]
    public void SharkFailureOriginalsMatchTheirPreservedRecording()
    {
        using var provenance=JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder,"provenance.json")));
        foreach(var item in provenance.RootElement.GetProperty("Frames").EnumerateArray())
            Assert.Equal(item.GetProperty("SHA256").GetString(),Convert.ToHexString(SHA256.HashData(
                File.ReadAllBytes(Path.Combine(Folder,item.GetProperty("File").GetString()!)))));
    }

    [Theory]
    [InlineData(140,170)]
    [InlineData(150,179)]
    [InlineData(286,306)]
    [InlineData(335,344)]
    [InlineData(412,417)]
    [InlineData(442,444)]
    public void UpperCatchProgressIsMeasuredIndependentlyOfTheSecondBar(int id,int width)
    {
        using var full=Frame(id);
        using var crop=new Mat(full,new Rect(968,1013,1504,356));
        double? fill=VisionProcessor.MeasureReelProgressFill(crop,1013,1369,MinigameTheme.Duskwire);
        Assert.True(fill.HasValue,$"No primary progress read on original frame{id}");
        Assert.InRange(fill!.Value*1369,width-3,width+3);
    }
}

public partial class FishingEngineReplayTests
{
    [Theory]
    [InlineData(false,1.0)]
    [InlineData(false,1.6)]
    [InlineData(true,1.0)]
    public async Task RecordedTwoBarSharkKeepsReelingPastThirtyFiveSecondsUnlessProgressFreezes(bool frozen,double timeScale)
    {
        using var metadata=JsonDocument.Parse(File.ReadAllText(Path.Combine(DuskwireDualMeterTests.Folder,"provenance.json")));
        long start=metadata.RootElement.GetProperty("ReelEntryTimestamp").GetInt64();
        var samples=metadata.RootElement.GetProperty("Frames").EnumerateArray().Select(f=>(
            At:(f.GetProperty("Timestamp").GetInt64()-start)/10000.0*timeScale,
            Pixels:Cv2.ImRead(Path.Combine(DuskwireDualMeterTests.Folder,f.GetProperty("File").GetString()!)))).ToArray();
        var clock=new Clock{CaptureMs=110};var input=new Input(clock);
        long? entered=null;bool survived=false,recovered=false;int releasesBeforeStop=-1;
        using var frames=new Frames(()=>{
            double elapsed=entered.HasValue?clock.Timestamp-entered.Value:0;
            int index=0;
            if(!frozen)while(index+1<samples.Length && samples[index+1].At<=elapsed)index++;
            return samples[index].Pixels.Clone();
        },clock);
        using var engine=new FishingEngine(new Settings{EnableRecording=false,EnableAutoClaimAquarium=false,
            EnableAntiAfk=false,EnableHumanizedJitter=false,EnableWatchdogRecovery=true,
            ReelTimeoutMs=35000,WatchdogStallTimeoutSeconds=50},frames,frames,
            clock:clock,desktop:new Desktop{ClientWidth=3440,ClientHeight=1369},hardware:input);
        int initialReleaseCount=0;
        engine.OnTelemetry+=t=>{
            if(t.State==MacroState.Reeling && !entered.HasValue)
            {entered=clock.Timestamp;initialReleaseCount=input.Events.Count(e=>e=="release");}
            if(!survived && entered.HasValue && clock.Timestamp-entered.Value>=40000*timeScale && t.State==MacroState.Reeling)
            {survived=true;releasesBeforeStop=input.Events.Count(e=>e=="release")-initialReleaseCount;engine.Stop();}
            t.Dispose();
        };
        clock.Tick=()=>{
            if(engine.WatchdogRecoveryCount>0){recovered=true;engine.Stop();}
            if(clock.Timestamp>40000*timeScale+10000)engine.Stop();
        };
        engine.Start();
        try{await engine.Completion.WaitAsync(TimeSpan.FromSeconds(45));}
        finally{engine.Stop();foreach(var sample in samples)sample.Pixels.Dispose();}
        Assert.NotNull(entered);
        Assert.Equal(!frozen,survived);
        Assert.Equal(frozen,recovered);
        if(!frozen){Assert.Equal(0,releasesBeforeStop);Assert.Equal(0,engine.UnknownCatches);}
        else Assert.InRange(clock.Timestamp-entered!.Value,34000,40000);
        Assert.False(input.Held);
    }
}
