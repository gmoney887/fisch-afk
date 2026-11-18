using FischMacroCS.Core;
using OpenCvSharp;
using System.IO;

namespace FischMacroCS.Tests;

public partial class FishingEngineReplayTests
{
    [Theory]
    [InlineData("initial-capture",false)]
    [InlineData("transition",false)]
    [InlineData("capture",false)]
    [InlineData("status",false)]
    [InlineData("target",false)]
    [InlineData("down",true)]
    [InlineData("release",true)]
    [InlineData("down-focus",true)]
    [InlineData("release-focus",true)]
    [InlineData("luring-focus",true)]
    [InlineData("reeling-focus",true)]
    public async Task QueuedStopCannotStartACastAndThenAbandonItsCatch(string boundary,bool castAlreadyStarted)
    {
        var clock=new Clock();var input=new Input(clock);var desktop=new Desktop();
        using var reel=Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,"Fixtures","reel_active_recovery_21.png"));
        using var caught=Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,"Fixtures","reel_catch_live.png"));
        FishingEngine? engine=null;long? reelingSince=null;
        int casts=0;bool queued=false,transitionArmed=false,expired=false;
        void Queue()
        {
            if(!queued)queued=engine!.TryQueueStopAfterCycle();
        }
        Mat Render()
        {
            var frame=new Mat(Desktop.Height,Desktop.Width,MatType.CV_8UC3,Scalar.All(20));
            Mat? strip=null;
            if(engine!.CurrentState==MacroState.Luring)strip=reel;
            if(engine.CurrentState==MacroState.Reeling)
            {reelingSince??=clock.Timestamp;if(clock.Timestamp-reelingSince.Value<600)strip=reel;}
            if(engine.CurrentState==MacroState.PostCatch)strip=caught;
            if(strip is not null)
            {
                using var target=new Mat(frame,new Rect((frame.Width-strip.Width)/2,1015,strip.Width,strip.Height));
                strip.CopyTo(target);
            }
            int left=Desktop.Width/2-(69*8+68)/2;
            for(int i=0;i<9;i++)Cv2.Rectangle(frame,new Rect(left+i*69,Desktop.Height-70,68,68),i==0?Scalar.White:Scalar.All(60),1);
            return frame;
        }
        using var frames=new Frames(Render,clock);
        using var running=engine=new FishingEngine(new Settings{EnableRecording=false,EnableAutoClaimAquarium=false,
            EnableAntiAfk=false,EnableWatchdogRecovery=false,EnableHumanizedJitter=false,
            EnableDynamicCastRelease=false,CastHoldMs=80,PostCastDelayMs=0,PostCatchDelayMs=800,ShakeMode="Disabled"},
            frames,frames,clock:clock,desktop:desktop,hardware:input);
        frames.BeforeCapture=_=>{
            if(engine.CurrentState==MacroState.Casting &&
                (boundary=="initial-capture" && casts==0 || boundary=="capture" && engine.TotalCatches==1))Queue();
            if(!queued && boundary=="luring-focus" && engine.CurrentState==MacroState.Luring && casts==2)
            {Queue();desktop.Focused=false;}
        };
        engine.OnTelemetry+=t=>{
            if(boundary=="status" && t.State==MacroState.Casting && engine.TotalCatches==1)Queue();
            if(boundary=="transition" && t.State==MacroState.PostCatch && engine.TotalCatches==1 && t.Action.Contains("Resting"))transitionArmed=true;
            if(!queued && boundary=="reeling-focus" && t.State==MacroState.Reeling && casts==2)
            {Queue();desktop.Focused=false;}
            t.Dispose();
        };
        // The transition releases its old mouse state after checking the queue,
        // before assigning Casting. This clock boundary deterministically places
        // the request in that gap, without sleeps or probabilistic thread races.
        clock.OnElapsed=since=>{
            if(boundary=="transition" && transitionArmed && since==0 && engine.CurrentState==MacroState.PostCatch)Queue();
        };
        input.AfterMove=(_,_)=>{if(boundary=="target" && engine.CurrentState==MacroState.Casting && engine.TotalCatches==1)Queue();};
        input.AfterDown=(_,_)=>{
            if(engine.CurrentState!=MacroState.Casting)return;
            casts++;reelingSince=null;
            if(boundary is "down" or "down-focus" && casts==2)
            {Queue();if(boundary=="down-focus")desktop.Focused=false;}
        };
        input.AfterUp=()=>{
            if(boundary is "release" or "release-focus" && engine.CurrentState==MacroState.Casting && casts==2)
            {Queue();if(boundary=="release-focus")desktop.Focused=false;}
        };
        clock.Tick=()=>{if(clock.Timestamp>25000){expired=true;engine.Stop();}};
        engine.Start();
        try{await engine.Completion.WaitAsync(TimeSpan.FromSeconds(30));}
        finally{engine.Stop();}
        Assert.True(queued,"The stop boundary was not reached");
        Assert.False(expired);
        if(boundary.EndsWith("-focus"))Assert.True(desktop.Activations>0,"The interrupted cycle must reacquire gameplay");
        int expected=boundary=="initial-capture"?0:castAlreadyStarted?2:1;
        Assert.Equal(expected,casts);
        Assert.Equal(expected,engine.TotalCatches);
        Assert.Equal(expected,engine.CurrentStreak);
        Assert.Equal(0,engine.TotalFails);Assert.Equal(0,engine.UnknownCatches);
        Assert.False(engine.IsRunning);Assert.False(engine.IsStopQueued);Assert.False(input.Held);
        Assert.Empty(input.HeldKeys);Assert.Null(engine.PauseReason);
    }
}
