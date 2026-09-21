using System.IO;
using FischMacroCS.Vision;
using OpenCvSharp;

namespace FischMacroCS.Tests;

public class DeathScreenDetectorTests
{
    [Theory]
    [InlineData(720)]
    [InlineData(1080)]
    [InlineData(1353)]
    [InlineData(1440)]
    public void RequiresBothDeathLogoAndTitleAtRecordedGeometry(int height)
    {
        using var source = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "death_wasted.png"));
        Assert.False(source.Empty());
        double scale = height / 1353.0;
        using var scaled = new Mat();
        Cv2.Resize(source, scaled, new Size((int)Math.Round(source.Width * scale), (int)Math.Round(source.Height * scale)));
        Assert.True(DeathScreenDetector.IsDeathScreen(scaled, height));
        using var noLogo = source.Clone();
        Cv2.Rectangle(noLogo, new Rect(275, 30, 290, 150), Scalar.Black, -1);
        using var altered = new Mat();
        Cv2.Resize(noLogo, altered, scaled.Size());
        Assert.False(DeathScreenDetector.IsDeathScreen(altered, height));
        using var noTitle = source.Clone();
        Cv2.Rectangle(noTitle, new Rect(220, 190, 410, 95), Scalar.Black, -1);
        Cv2.Resize(noTitle, altered, scaled.Size());
        Assert.False(DeathScreenDetector.IsDeathScreen(altered, height));
        using var misplaced = source.Clone();
        using var title = new Mat(source, new Rect(220, 190, 410, 95));
        Cv2.Rectangle(misplaced, new Rect(220, 190, 410, 95), Scalar.Black, -1);
        using (var target = new Mat(misplaced, new Rect(220, 340, 410, 95))) title.CopyTo(target);
        Cv2.Resize(misplaced, altered, scaled.Size());
        Assert.False(DeathScreenDetector.IsDeathScreen(altered, height));
    }

    [Theory]
    [InlineData("fisch_continue.png")]
    [InlineData("disconnect_idle_278.png")]
    [InlineData("server_update_wait.png")]
    [InlineData("reel_catch_live.png")]
    [InlineData("shake_lower_right.png")]
    public void OtherScreensAreNotDeaths(string fixture)
    {
        using var frame = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));
        Assert.False(frame.Empty());
        Assert.False(DeathScreenDetector.IsDeathScreen(frame, 1353));
    }
}
