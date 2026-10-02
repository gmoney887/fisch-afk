using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;
using System.IO;

namespace FischMacroCS.Tests;

public class ObservedGeometryTests
{
    [Fact]
    public void DuplicateOrReversedLabelsDoNotAuthorizeAClick()
    {
        using var reference=Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,"Assets","Workflows","aquarium-navigation-1009.png"));
        using var frame=new Mat(70,400,MatType.CV_8UC3,Scalar.Black);
        using(var first=new Mat(frame,new Rect(20,20,reference.Width,reference.Height))) reference.CopyTo(first);
        using(var second=new Mat(frame,new Rect(230,20,reference.Width,reference.Height))) reference.CopyTo(second);
        var duplicated=ObservedGlyphMatcher.Find(frame,reference,ObservedGlyphMatcher.Ink.Blue,.94);
        Assert.False(duplicated.Found); Assert.True(duplicated.Ambiguous);
        frame.SetTo(Scalar.Black);
        using(var target=new Mat(frame,new Rect(20,20,reference.Width,reference.Height))) Cv2.Flip(reference,target,FlipMode.Y);
        Assert.False(ObservedGlyphMatcher.Find(frame,reference,ObservedGlyphMatcher.Ink.Blue,.94).Found);
    }
    public static IEnumerable<object[]> Layouts()
    {
        foreach (int height in new[] { 720, 1080, 1440, 2160 })
        foreach (double aspect in new[] { 4.0/3, 16.0/9, 32.0/9 })
        foreach (double uiScale in new[] { .8, 1.0, 1.4, 2.0 })
            yield return new object[] { height, (int)(height*aspect), uiScale };
    }

    [Theory]
    [MemberData(nameof(Layouts))]
    public void NavigationSizeIsIndependentOfViewport(int height, int width, double uiScale)
    {
        using var screenshot = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,"Fixtures","aquarium_navigation_1920.png"));
        using var label = new Mat(screenshot,new Rect(1015,23,100,18));
        using var resized = new Mat();
        Cv2.Resize(label,resized,new Size((int)(label.Width*uiScale),(int)(label.Height*uiScale)));
        using var frame = new Mat(height,width,MatType.CV_8UC3,Scalar.Black);
        var center = new Point(width/2+(int)(height*.077),(int)(height*.035));
        var area = new Rect(center.X-resized.Width/2,center.Y-resized.Height/2,resized.Width,resized.Height);
        using (var roi=new Mat(frame,area)) resized.CopyTo(roi);
        using var vision = new TemplateWorkflowVision(Path.Combine(AppContext.BaseDirectory,"Assets","Workflows"));
        var match = vision.Find(frame,AquariumWorkflow.Navigation);
        Assert.True(match.Found,$"{width}x{height}, UI {uiScale}: {match.Confidence}");
        Assert.True(area.Contains(match.Center),$"Matched outside observed control: {match.Center}");
    }

    [Theory]
    [InlineData(.8)] [InlineData(1.0)] [InlineData(1.4)] [InlineData(2.0)]
    public void MeasuredGlyphMatcherRecognizesIndependentScale(double scale)
    {
        using var reference=Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,"Assets","Workflows","aquarium-navigation-1009.png"));
        using var screenshot=Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,"Fixtures","aquarium_navigation_1920.png"));
        using var strip=new Mat(screenshot,new Rect(790,0,340,65));
        using var resized=new Mat(); Cv2.Resize(strip,resized,new Size((int)(strip.Width*scale),(int)(strip.Height*scale)));
        var match=ObservedGlyphMatcher.Find(resized,reference,ObservedGlyphMatcher.Ink.Blue,.94);
        Assert.True(match.Found,$"UI scale {scale}: {match.Confidence}");
        Assert.True(match.Bounds.X>resized.Width/2);
        Cv2.Rectangle(resized,match.Bounds,Scalar.Black,-1);
        Assert.False(ObservedGlyphMatcher.Find(resized,reference,ObservedGlyphMatcher.Ink.Blue,.94).Found);
    }
}
