using FischMacroCS.Core;
using OpenCvSharp;
using System.IO;

namespace FischMacroCS.Tests;
public partial class FishingEngineReplayTests
{
    private sealed class PausedReadback(IFrameSource source):IFrameSource
    {
        public volatile bool PauseNext;
        public readonly TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Allow=new(false);
        public Mat? CaptureClientRegion(IntPtr window,int x,int y,int width,int height)
        {
            if(PauseNext&&height<400){PauseNext=false;Entered.TrySetResult();
                if(!Allow.Wait(TimeSpan.FromSeconds(3)))throw new TimeoutException("Test did not return paused capture");}
            return source.CaptureClientRegion(window,x,y,width,height);
        }
        public void Dispose(){Allow.Set();source.Dispose();Allow.Dispose();}
    }

    [Theory]
    [InlineData("stop")] [InlineData("focus")] [InlineData("resize")]
    public async Task CenteringReleasesBeforePendingNativeCaptureReturns(string interruption)
    {
        var clock=new Clock();var desktop=new Desktop();var input=new Input(clock);
        using var pixels=Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,"Fixtures","oath_red_45.png"));
        using var reel=new Mat();double ratio=Desktop.Height/1369.0;
        Cv2.Resize(pixels,reel,new Size((int)Math.Round(pixels.Width*ratio),(int)Math.Round(pixels.Height*ratio)));
        using var frames=new Frames(()=>{
            var full=new Mat(Desktop.Height,Desktop.Width,MatType.CV_8UC3,Scalar.All(20));
            using var destination=new Mat(full,new Rect((full.Width-reel.Width)/2,(int)Math.Round(1013*ratio),reel.Width,reel.Height));
            reel.CopyTo(destination);return full;
        },clock);
        using var pending=new PausedReadback(frames);
        using var engine=new FishingEngine(new Settings {EnableRecording=false,EnableAntiAfk=false,
            EnableAutoClaimAquarium=false,EnableWatchdogRecovery=false},pending,frames,clock:clock,desktop:desktop,hardware:input);
        int fresh=0;
        var released=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        input.AfterUp=()=>{if(pending.Entered.Task.IsCompleted)released.TrySetResult();};
        engine.OnTelemetry+=t=>{if(t.State==MacroState.Reeling&&t.BarWidth>0&&++fresh==4)pending.PauseNext=true;t.Dispose();};
        engine.Start();
        try
        {
            await pending.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(input.Held,"Pause after four fresh rightward commands.");
            if(interruption=="stop")engine.Stop();
            else if(interruption=="focus"){desktop.Focused=false;desktop.DenyNextActivations=int.MaxValue;}
            else desktop.ClientWidth+=80;
            await released.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(input.Held);
            Assert.False(pending.Allow.IsSet,"Input must release before native capture is unblocked.");
        }
        finally {engine.Stop();pending.Allow.Set();await engine.Completion.WaitAsync(TimeSpan.FromSeconds(5));}
        Assert.False(input.Held);Assert.Empty(input.HeldKeys);
    }
}

