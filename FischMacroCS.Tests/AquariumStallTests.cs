using FischMacroCS.Core;
using OpenCvSharp;
namespace FischMacroCS.Tests;
public class AquariumStallTests
{
    [Theory]
    [InlineData(1032)] [InlineData(1080)]
    public void NavigationOnSecondPcIsRecognized(int height)
    {
        using var original = Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory,"Fixtures","aquarium_navigation_1920.png"),ImreadModes.Color);
        using var frame = new Mat(height,original.Width,MatType.CV_8UC3,Scalar.Black);
        using (var roi = new Mat(frame,new Rect(0,0,original.Width,original.Height))) original.CopyTo(roi);
        using var vision = Vision();
        var found = vision.Find(frame,AquariumWorkflow.Navigation);
        Assert.True(found.Found,$"Navigation confidence {found.Confidence}");
        Assert.InRange(found.Center.X,1015,1115);
        Assert.InRange(found.Center.Y,20,42);
        Cv2.Rectangle(frame,new Rect(1014,20,104,25),Scalar.Black,-1);
        Assert.False(vision.Find(frame,AquariumWorkflow.Navigation).Found);
    }
    private static Mat Frame() => Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory,"Fixtures","aquarium_stalled_controls.png"));
    private static TemplateWorkflowVision Vision() => new(System.IO.Path.Combine(AppContext.BaseDirectory,"Assets","Workflows"));
    [Theory]
    [InlineData(720)] [InlineData(1080)] [InlineData(1353)] [InlineData(1369)] [InlineData(2160)]
    public void WorldLabelBehindCloseDoesNotHideTheButton(int height)
    {
        using var original = Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory,"Fixtures","aquarium_red_label_overlap.png"));
        using var frame = new Mat();
        double scale = height/1369.0;
        Cv2.Resize(original,frame,new Size((int)Math.Round(original.Width*scale),height));
        using var vision = Vision();
        var close = vision.Find(frame,AquariumWorkflow.Close);
        Assert.True(close.Found, $"Close confidence {close.Confidence}");
        Assert.InRange(close.Center.X/scale,2475,2505);
        Assert.InRange(close.Center.Y/scale,150,180);
    }
    [Fact]
    public void RecordedOverlapClosesThroughDetectedButtonAndVerifiesAbsence()
    {
        using var vision = Vision(); var clock = new Clock(); int clicks = 0;
        Mat Capture() => clicks > 0 ? new Mat(1369,3440,MatType.CV_8UC3,Scalar.Black)
            : Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory,"Fixtures","aquarium_red_label_overlap.png"));
        var result = AquariumWorkflow.ClosePanel(clock,vision,Capture,ms=>clock.Now+=ms,point=>
        {
            Assert.InRange(point.X,2475,2505); Assert.InRange(point.Y,150,180); clicks++;
        },default,requireCloseAction:true);
        Assert.Equal(1,clicks);
        Assert.Equal(ActionOutcome.ConfirmedSuccess,result.Outcome);
    }
    [Fact]
    public void BackgroundWorldLabelWithoutCloseCannotAuthorizeClick()
    {
        using var frame = Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory,"Fixtures","aquarium_red_label_overlap.png"));
        Cv2.Rectangle(frame,new Rect(2475,148,30,33),Scalar.Black,-1);
        using var vision = Vision();
        Assert.False(vision.Find(frame,AquariumWorkflow.Close).Found);
    }
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
