using FischMacroCS.Vision;
using OpenCvSharp;
using System.IO;

namespace FischMacroCS.Tests;

public class DisconnectDetectorTests
{
    [Theory]
    [InlineData(720)]
    [InlineData(1080)]
    [InlineData(1353)]
    [InlineData(1440)]
    public void ReviewedDialogRequiresTitleAndReconnectInTheSameGeometry(int height)
    {
        using var dialog = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "disconnect_idle_278.png"));
        Assert.False(dialog.Empty());
        double scale = height / 1353.0;
        using var scaled = new Mat();
        Cv2.Resize(dialog, scaled, new Size((int)Math.Round(dialog.Width * scale), (int)Math.Round(dialog.Height * scale)));
        using var roi = new Mat((int)(height * .4), (int)(height * .62), MatType.CV_8UC3, Scalar.All(15));
        int x = (roi.Width - scaled.Width) / 2, y = (roi.Height - scaled.Height) / 2;
        using (var target = new Mat(roi, new Rect(x,y,scaled.Width,scaled.Height))) scaled.CopyTo(target);
        Assert.True(DisconnectDetector.TryFindReconnect(roi, height, out var point));
        Assert.InRange(point.X, x + (int)(285 * scale), x + (int)(301 * scale));
        Assert.InRange(point.Y, y + (int)(204 * scale), y + (int)(220 * scale));

        using var noButton = roi.Clone();
        Cv2.Rectangle(noButton, new Rect(x, y+(int)(185*scale), scaled.Width, scaled.Height-(int)(185*scale)), Scalar.All(58), -1);
        Assert.False(DisconnectDetector.TryFindReconnect(noButton, height, out _));
        using var noTitle = roi.Clone();
        Cv2.Rectangle(noTitle, new Rect(x, y, scaled.Width, (int)(50*scale)), Scalar.All(58), -1);
        Assert.False(DisconnectDetector.TryFindReconnect(noTitle, height, out _));

        using var leaveOnly = dialog.Clone();
        using (var leave = new Mat(dialog, new Rect(21,194,175,36)))
        using (var rightAction = new Mat(leaveOnly, new Rect(206,194,175,36))) leave.CopyTo(rightAction);
        using var scaledLeave = new Mat();
        Cv2.Resize(leaveOnly, scaledLeave, scaled.Size());
        Assert.False(DisconnectDetector.TryFindReconnect(scaledLeave, height, out _));
    }

    [Theory]
    [InlineData("server_update_wait.png")]
    [InlineData("death_wasted.png")]
    [InlineData("reel_catch_live.png")]
    [InlineData("companion_bonus_only.png")]
    [InlineData("reel_live_false_exit.png")]
    public void OtherReviewedScreensDoNotOfferReconnect(string fixture)
    {
        using var frame = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        Assert.False(frame.Empty());
        Assert.False(DisconnectDetector.TryFindReconnect(frame, 1353, out _));
    }
}
