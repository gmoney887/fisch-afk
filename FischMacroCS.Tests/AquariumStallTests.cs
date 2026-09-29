using FischMacroCS.Core;
using OpenCvSharp;
namespace FischMacroCS.Tests;
public class AquariumStallTests
{
    private static Mat Frame() => Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory,"Fixtures","aquarium_stalled_controls.png"));
    private static TemplateWorkflowVision Vision() => new(System.IO.Path.Combine(AppContext.BaseDirectory,"Assets","Workflows"));
    [Theory]
    [InlineData(720)] [InlineData(1080)] [InlineData(1353)] [InlineData(2160)]
    public void RecordedPanelControlsRemainRecognizable(int height)
    {
        using var original = Frame(); using var frame = new Mat();
        Cv2.Resize(original, frame, new Size((int)Math.Round(original.Width * height / 1353.0), height));
        using var vision = Vision();
        Assert.True(vision.Find(frame, AquariumWorkflow.Claim).Found);
        Assert.True(vision.Find(frame, AquariumWorkflow.Close).Found);
        Assert.False(vision.Find(frame, AquariumWorkflow.EmptyBalance).Found);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CloseRetriesMissedClickButIsBounded(bool permanentlyStuck)
    {
        using var vision = Vision(); var clock = new Clock(); int clicks = 0;
        Mat Capture() => !permanentlyStuck && clicks >= 2 ? new Mat(1353,3424,MatType.CV_8UC3,Scalar.Black) : Frame();
        var result = AquariumWorkflow.ClosePanel(clock,vision,Capture,ms=>clock.Now+=ms,_=>clicks++,default,requireCloseAction:true);
        Assert.Equal(permanentlyStuck ? 3 : 2, clicks);
        Assert.Equal(permanentlyStuck ? ActionOutcome.Unknown : ActionOutcome.ConfirmedSuccess,result.Outcome);
    }
    [Fact]
    public void RedSceneryAloneIsNotACloseSymbol()
    {
        using var frame = new Mat(1353,3424,MatType.CV_8UC3,Scalar.Black);
        Cv2.Rectangle(frame,new Rect(2400,100,130,125),new Scalar(0,0,255),-1);
        using var vision = Vision(); Assert.False(vision.Find(frame,AquariumWorkflow.Close).Found);
    }
    private class Clock : IClock
    {
        public long Now; public long Timestamp => Now;
        public double ElapsedMilliseconds(long since) => Now-since;
        public void Delay(int ms,CancellationToken cancellation) { cancellation.ThrowIfCancellationRequested(); Now+=ms; }
    }
}
