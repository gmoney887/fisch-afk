using FischMacroCS.Core;
using OpenCvSharp;
namespace FischMacroCS.Tests;

public class WorkflowEvidenceTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void MovingOrStaleTargetNeverAuthorizesClick(bool onlyMoveAtAction)
    {
        var clock=new Clock(); int reads=0, clicks=0;
        var vision=new Vision(target =>
        {
            if(target.Name=="after") return (false,default,0);
            reads++;
            int x=onlyMoveAtAction ? (reads<=2 ? 100 : 800) : (reads%2==0 ? 800 : 100);
            return (true,new Point(x,100),1);
        });
        var workflow=new VerifiedWorkflow(clock,vision,()=>new Mat(1080,1920,MatType.CV_8UC3,Scalar.Black),ms=>clock.Delay(ms,default));
        var result=workflow.Run([new("Move",new("before",0,0,.1),new("after",0,0,.1),_=>clicks++,TimeoutMs:400)],default);
        Assert.Equal(ActionOutcome.Unknown,result.Outcome); Assert.Equal(0,clicks);
    }
    [Fact]
    public void StopDuringCooldownRemainsImmediateAndNeverFakesProgress()
    {
        var clock=new Clock(); using var stop=new CancellationTokenSource(); bool resumed=false;
        Assert.ThrowsAny<OperationCanceledException>(()=>FishingRetryWait.Wait(clock,()=>{resumed=true;return true;},()=>{},()=>
        { if(clock.Timestamp>=1000) stop.Cancel(); },stop.Token,minimumDelayMs:60000));
        Assert.False(resumed); Assert.Equal(1000,clock.Timestamp);
    }
    private sealed class Vision(Func<WorkflowTarget,(bool,Point,double)> find):IWorkflowVision
    { public (bool Found,Point Center,double Confidence) Find(Mat image,WorkflowTarget target)=>find(target); }
    private sealed class Clock:IClock
    {
        public long Timestamp{get;private set;}
        public double ElapsedMilliseconds(long since)=>Timestamp-since;
        public void Delay(int ms,CancellationToken ct){ct.ThrowIfCancellationRequested();Timestamp+=ms;}
    }
}
