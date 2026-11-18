using OpenCvSharp;
using System.IO;

namespace FischMacroCS.Vision;

/// <summary>Recognizes a reviewed Roblox disconnect title AND its matching recovery action.
/// Input is the central recovery ROI; returned coordinates are relative to that ROI.</summary>
public static class DisconnectDetector
{
    private static readonly Lazy<Mat> Title = new(() => Load("disconnected_title.png"));
    private static readonly Lazy<Mat> Button = new(() => Load("reconnect_button.png"));
    private static readonly Lazy<Mat> FailedTitle = new(() => Load("connection_failed_title.png"));
    private static readonly Lazy<Mat> RetryButton = new(() => Load("retry_button.png"));

    private static Mat Load(string name)
    {
        using var stream = typeof(DisconnectDetector).Assembly.GetManifestResourceStream("FischMacroCS.Assets." + name)
            ?? throw new InvalidOperationException("Missing disconnect identity resource: " + name);
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return Cv2.ImDecode(bytes.ToArray(), ImreadModes.Grayscale);
    }

    public static bool TryFindReconnect(Mat region, int viewportHeight, out Point center)
        => TryFindReconnect(region, viewportHeight, out center, out _);

    public static bool TryFindReconnect(Mat region, int viewportHeight, out Point center, out string action)
    {
        center = default;
        action = "";
        if (region.Empty() || viewportHeight < 200) return false;
        using var gray = new Mat();
        if (region.Channels() == 4) Cv2.CvtColor(region, gray, ColorConversionCodes.BGRA2GRAY);
        else if (region.Channels() == 3) Cv2.CvtColor(region, gray, ColorConversionCodes.BGR2GRAY);
        else region.CopyTo(gray);
        ImageSmoothing.Apply(gray, gray);
        if (TryFindAction(gray, viewportHeight / 1353.0, Title.Value, Button.Value, 120, 189, out center))
        { action = "Reconnect"; return true; }
        // Error 279 uses a different title and Retry, not the idle-278 Reconnect dialog.
        // Keep each title/action pair bound to its independently reviewed geometry.
        if (TryFindAction(gray, viewportHeight / 1369.0, FailedTitle.Value, RetryButton.Value, 129, 190, out center))
        { action = "Retry"; return true; }
        return false;
    }

    private static bool TryFindAction(Mat gray, double scale, Mat titleSource, Mat buttonSource,
        int offsetX, int offsetY, out Point center)
    {
        center = default;
        using var title = new Mat(); using var button = new Mat();
        Cv2.Resize(titleSource, title, new Size(Math.Max(1, (int)Math.Round(titleSource.Width * scale)), Math.Max(1, (int)Math.Round(titleSource.Height * scale))));
        Cv2.Resize(buttonSource, button, new Size(Math.Max(1, (int)Math.Round(buttonSource.Width * scale)), Math.Max(1, (int)Math.Round(buttonSource.Height * scale))));
        // The crop origin and whole-frame resize can differ by a subpixel.
        // Smooth both sides equally so antialiasing phase does not change identity.
        ImageSmoothing.Apply(title, title);
        ImageSmoothing.Apply(button, button);
        if (gray.Width < title.Width || gray.Height < title.Height) return false;
        using var scores = new Mat();
        Cv2.MatchTemplate(gray, title, scores, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(scores, out _, out double titleScore, out _, out Point titleAt);
        if (titleScore < .88) return false;
        // Both labels must share the reviewed dialog geometry. A similar white
        // button elsewhere on screen cannot supply the second identity signal.
        int tolerance = Math.Max(2, (int)Math.Ceiling(5 * scale));
        int bx = titleAt.X + (int)Math.Round(offsetX * scale);
        int by = titleAt.Y + (int)Math.Round(offsetY * scale);
        var search = new Rect(bx - tolerance, by - tolerance, button.Width + 2 * tolerance, button.Height + 2 * tolerance)
            & new Rect(0, 0, gray.Width, gray.Height);
        if (search.Width < button.Width || search.Height < button.Height) return false;
        using var buttonRegion = new Mat(gray, search);
        Cv2.MatchTemplate(buttonRegion, button, scores, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(scores, out _, out double buttonScore, out _, out Point buttonAt);
        if (buttonScore < .88) return false;
        center = new Point(search.X + buttonAt.X + button.Width / 2, search.Y + buttonAt.Y + button.Height / 2);
        return true;
    }
}
