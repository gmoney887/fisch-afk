using FischMacroCS.Vision;
using OpenCvSharp;
using System.IO;

namespace FischMacroCS.Tests;

public class ContinueScreenDetectorTests
{
    [Theory]
    [InlineData(720)]
    [InlineData(1080)]
    [InlineData(1353)]
    [InlineData(1440)]
    public void LogoAndPromptAreBothRequired(int height)
    {
        using var source = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,"Fixtures","fisch_continue.png"));
        Assert.False(source.Empty());
        using var scaled = new Mat();
        double scale = height/1353.0;
        Cv2.Resize(source,scaled,new Size((int)Math.Round(source.Width*scale),(int)Math.Round(source.Height*scale)));
        Assert.True(ContinueScreenDetector.IsContinueScreen(scaled,height));
        Assert.True(ContinueScreenDetector.TryFindPrompt(scaled,height,out var bounds));
        Assert.InRange(bounds.X, (int)(47*scale)-2, (int)(47*scale)+2);
        Assert.InRange(bounds.Y, (int)(281*scale)-2, (int)(281*scale)+2);
        using var prompt = new Mat(scaled,bounds);
        var vision = new VisionProcessor();
        var snapped = vision.DynamicUISnapWithStatus(prompt,prompt.Width/2,prompt.Height/2,
            VisionProcessor.UIColorType.WhiteText,Math.Max(12,(int)(height*.02)));
        Assert.True(snapped.Found);
        Assert.True(new Rect(0,0,prompt.Width,prompt.Height).Contains(snapped.Pt));
        using var absent = scaled.Clone();
        Cv2.Rectangle(absent,new Rect(0,(int)(270*scale),absent.Width,absent.Height-(int)(270*scale)),Scalar.Black,-1);
        Assert.False(ContinueScreenDetector.IsContinueScreen(absent,height));
        using var noLogo = scaled.Clone();
        Cv2.Rectangle(noLogo,new Rect(0,0,noLogo.Width,(int)(250*scale)),Scalar.Black,-1);
        Assert.False(ContinueScreenDetector.IsContinueScreen(noLogo,height));
    }

    [Theory]
    [InlineData("disconnect_idle_278.png")]
    [InlineData("server_update_wait.png")]
    [InlineData("death_wasted.png")]
    [InlineData("reel_catch_live.png")]
    [InlineData("companion_bonus_only.png")]
    public void OtherScreensDoNotRequestContinue(string name)
    {
        using var frame = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,"Fixtures",name));
        Assert.False(frame.Empty());
        Assert.False(ContinueScreenDetector.IsContinueScreen(frame,1353));
    }

    [Fact]
    public void TextSnapDoesNotInventTargetWhenLetteringIsAbsent()
    {
        using var background = new Mat(36,310,MatType.CV_8UC3,new Scalar(30,20,10));
        var vision = new VisionProcessor();
        Assert.False(vision.DynamicUISnapWithStatus(background,155,18,
            VisionProcessor.UIColorType.WhiteText,27).Found);
    }
}
