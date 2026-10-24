using FischMacroCS.Core;
using OpenCvSharp;
using System.IO;
using Xunit.Abstractions;

namespace FischMacroCS.Tests;

public partial class FishingEngineReplayTests
{
    private readonly ITestOutputHelper _baselineOutput;
    public FishingEngineReplayTests(ITestOutputHelper output) => _baselineOutput=output;

    public static IEnumerable<object[]> BaselineProfilesAndCadences()
    {
        foreach(string profile in new[]{"Standard","Seraphic","Destiny / Mythic","Rod of the Depths","Heaven's / Sunken","Magma / Steady","Rapid / Carbon"})
        foreach(int cadence in new[]{70,120,200,-1})
        foreach(int width in new[]{168,1111})yield return new object[]{profile,cadence,width};
    }

    [Theory]
    [MemberData(nameof(BaselineProfilesAndCadences))]
    public async Task BaselineProfilesCenterUsingGuardedInputFeedback(string profile,int cadence,int width)
    {
        // Independent constant-acceleration plant. Actual guarded input edges
        // move the generated body. Synthetic capture times; native image cost
        // is measured separately. The plant starts at live entry; this trial
        // measures centering after entry, not acquisition. No Perfect acceptance.
        const int target=1127, railWidth=1374;
        (double pull,double fall)=profile switch {
            "Seraphic"=>(650,410),"Destiny / Mythic"=>(720,440),"Rod of the Depths"=>(620,390),
            "Heaven's / Sunken"=>(680,420),"Magma / Steady"=>(480,360),"Rapid / Carbon"=>(580,420),_=>(550,410)
        };
        var clock=new Clock(); var input=new Input(clock);
        double position=target,velocity=0,last=clock.Timestamp;
        double? started=null; bool held=false,active=false;
        var errors=new List<double>();var trace=new List<string>();var invalid=new List<string>();int observations=0,readback=0;
        FishingEngine? running=null;bool primary=false;
        double captured=position;
        void Advance(){
            double now=clock.Timestamp, dt=(now-last)/1000;
            double acceleration=(held?pull:-fall)*railWidth/1188.0;
            if(active){
                for(double at=Math.Floor(last)+1;at<=now;at++)
                    if(started.HasValue&&at-started.Value>=2000&&at-started.Value<=12000){
                        double t=(at-last)/1000;
                        errors.Add(position+velocity*t+acceleration*t*t/2-target);
                    }
                position+=velocity*dt+acceleration*dt*dt/2;velocity+=acceleration*dt;
                if(position<440+width/2.0||position>1814-width/2.0){position=Math.Clamp(position,440+width/2.0,1814-width/2.0);velocity=0;}
            }
            last=now;
        }
        input.AfterDown=(_,_)=>{Advance();held=true;};
        input.AfterUp=()=>{Advance();held=false;};
        using var frames=new Frames(()=>{
            Advance();if(primary)captured=position;
            var image=new Mat(Desktop.Height,Desktop.Width,MatType.CV_8UC3,Scalar.All(20));
            Cv2.Rectangle(image,new Rect(440,1126,railWidth,55),Scalar.All(65),-1);
            Cv2.Rectangle(image,new Rect(442,1128,railWidth-4,51),Scalar.All(20),-1);
            Cv2.Rectangle(image,new Rect((int)Math.Round(position-width/2.0),1115,width,77),Scalar.All(240),-1);
            // Existing126 capsule appearance; the detector is unchanged.
            Cv2.Rectangle(image,new Rect(target-7,1100,14,111),new Scalar(100,85,75),-1);
            Cv2.Ellipse(image,new Point(target,1100),new Size(7,7),0,180,360,new Scalar(100,85,75),-1);
            Cv2.Ellipse(image,new Point(target,1211),new Size(7,7),0,0,180,new Scalar(100,85,75),-1);
            Cv2.Rectangle(image,new Rect(757,1245,740,15),Scalar.All(110),1);
            Cv2.Rectangle(image,new Rect(758,1246,350,13),Scalar.All(240),-1);
            int slotLeft=Desktop.Width/2-(69*8+68)/2;
            for(int slot=0;slot<9;slot++)Cv2.Rectangle(image,new Rect(slotLeft+slot*69,Desktop.Height-70,68,68),slot==0?Scalar.White:Scalar.All(60),1);
            return image;
        },clock);
        frames.BeforeCapture=area=>{
            primary=area.Width==1488&&area.Height==270;
            active|=primary&&running?.CurrentState==MacroState.Reeling;
            clock.CaptureMs=area.Height<400?(cadence<0?new[]{40,120,70,210}[readback++%4]:cadence):5;
        };
        using var engine=running=new FishingEngine(new Settings {RodProfile=profile,EnableAdaptiveRodDynamics=false,
            EnableRecording=false,EnableAutoClaimAquarium=false,EnableAntiAfk=false,EnableWatchdogRecovery=false,
            EnableHumanizedJitter=false,SelectedTheme=MinigameTheme.Default},frames,frames,
            clock:clock,desktop:new Desktop(),hardware:input);
        engine.OnTelemetry+=t=>{
            if(t.State==MacroState.Reeling){
                Advance();started??=clock.Timestamp;observations++;
                if(Math.Abs(t.BarCenter-captured)>2)invalid.Add($"bar={t.BarCenter}, captured={captured}, at={clock.Timestamp}");
                if(Math.Abs(t.BarWidth-width)>2)invalid.Add($"width={t.BarWidth}");
                if(Math.Abs(t.FishX-target)>3)invalid.Add($"fish={t.FishX}");
                trace.Add($"{clock.Timestamp},{position},{velocity},{captured},{t.BarCenter},{t.FishX},{input.Held},{t.ControlBarCenter},{t.ControlBarVelocity},{t.BarVelocity},{t.Action}");
                if(clock.Timestamp-started>=12000)engine.Stop();
            }
            t.Dispose();
        };
        clock.Tick=()=>{Advance();if(clock.Timestamp>20000)engine.Stop();};
        engine.Start();
        try{await engine.Completion.WaitAsync(TimeSpan.FromSeconds(20));}
        finally{engine.Stop();}
        string path=Path.Combine(Path.GetTempPath(),$"baseline126-centering-{string.Concat(profile.Where(char.IsLetterOrDigit))}-{cadence}-w{width}-{Guid.NewGuid():N}.csv");
        File.WriteAllLines(path,trace);
        _baselineOutput.WriteLine($"Synthetic fixed-target feedback: {observations} observations; errors={errors.Count}; trace={path}");
        Assert.Empty(invalid);
        Assert.True(errors.Count>=9990,"Score the complete ten-second interval between actual input edges.");
        double within=errors.Count(e=>Math.Abs(e)<=5.04)/(double)errors.Count;
        _baselineOutput.WriteLine($"mean={errors.Average(Math.Abs):F3}px; max={errors.Max(Math.Abs):F3}px; within5.04px={within:P2}");
        Assert.InRange(errors.Average(Math.Abs),0,4.032);
        Assert.InRange(errors.Max(Math.Abs),0,10.08);
        Assert.InRange(within,.9,1);
        Assert.False(input.Held);
    }
}



