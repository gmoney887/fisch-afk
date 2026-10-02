using FischMacroCS.Core;
using OpenCvSharp;
using System.IO;

namespace FischMacroCS.Tests;

public class AquariumLayoutTests
{
    [Theory]
    [InlineData(1.0, -.15, .15)]
    [InlineData(1.5, .15, -.15)]
    public void ClaimIdentityCanBeReacquiredWhenPanelLayoutMoves(double scale, double dx, double dy)
    {
        using var template = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Assets", "Workflows", "aquarium-claim.png"));
        using var text = new Mat();
        Cv2.Resize(template, text, new Size((int)(template.Width * scale), (int)(template.Height * scale)));
        using var frame = new Mat(1009, 1920, MatType.CV_8UC3, Scalar.Black);
        var target = AquariumWorkflow.Claim;
        int x = frame.Width / 2 + (int)((target.XFromCenterInHeights + dx) * frame.Height);
        int y = (int)((target.YInHeights + dy) * frame.Height);
        var bounds = new Rect(x - text.Width / 2, y - text.Height / 2, text.Width, text.Height);
        using (var destination = new Mat(frame, bounds)) text.CopyTo(destination);
        using var vision = new TemplateWorkflowVision(Path.Combine(AppContext.BaseDirectory, "Assets", "Workflows"));
        var match = vision.Find(frame, target);
        Assert.True(match.Found);
        Assert.True(bounds.Contains(match.Center));
        frame.SetTo(Scalar.Black);
        Assert.False(vision.Find(frame, target).Found);
    }
}
