using OpenCvSharp;
using System.IO;

namespace FischMacroCS.Vision;

/// <summary>Recognizes a reviewed Roblox disconnect title AND its Reconnect action.
/// Input is the central recovery ROI; returned coordinates are relative to that ROI.</summary>
public static class DisconnectDetector
{
    private static readonly Lazy<Mat> Title = new(() => Load("disconnected_title.png"));
    private static readonly Lazy<Mat> Button = new(() => Load("reconnect_button.png"));

    private static Mat Load(string name)
    {
        using var stream = typeof(DisconnectDetector).Assembly.GetManifestResourceStream("FischMacroCS.Assets." + name)
            ?? throw new InvalidOperationException("Missing disconnect identity resource: " + name);
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return Cv2.ImDecode(bytes.ToArray(), ImreadModes.Grayscale);
    }

    public static bool TryFindReconnect(Mat region, int viewportHeight, out Point center)
    {
        center = default;
        if (region.Empty() || viewportHeight < 200) return false;
        using var gray = new Mat();
        if (region.Channels() == 4) Cv2.CvtColor(region, gray, ColorConversionCodes.BGRA2GRAY);
        else if (region.Channels() == 3) Cv2.CvtColor(region, gray, ColorConversionCodes.BGR2GRAY);
        else region.CopyTo(gray);
        double scale = viewportHeight / 1353.0;
        using var title = new Mat(); using var button = new Mat();
        Cv2.Resize(Title.Value, title, new Size(Math.Max(1, (int)Math.Round(146 * scale)), Math.Max(1, (int)Math.Round(28 * scale))));
        Cv2.Resize(Button.Value, button, new Size(Math.Max(1, (int)Math.Round(94 * scale)), Math.Max(1, (int)Math.Round(24 * scale))));
        // The crop origin and whole-frame resize can differ by a subpixel.
        // Smooth both sides equally so antialiasing phase does not change identity.
        Cv2.GaussianBlur(gray, gray, new Size(3, 3), .8);
        Cv2.GaussianBlur(title, title, new Size(3, 3), .8);
        Cv2.GaussianBlur(button, button, new Size(3, 3), .8);
        if (gray.Width < title.Width || gray.Height < title.Height) return false;
        using var scores = new Mat();
        Cv2.MatchTemplate(gray, title, scores, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(scores, out _, out double titleScore, out _, out Point titleAt);
        if (titleScore < .88) return false;
        // Both labels must share the reviewed dialog geometry. A similar white
        // button elsewhere on screen cannot supply the second identity signal.
        int tolerance = Math.Max(2, (int)Math.Ceiling(5 * scale));
        int bx = titleAt.X + (int)Math.Round(120 * scale);
        int by = titleAt.Y + (int)Math.Round(189 * scale);
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
