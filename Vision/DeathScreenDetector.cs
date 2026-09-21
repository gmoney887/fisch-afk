using System.IO;
using OpenCvSharp;

namespace FischMacroCS.Vision;

/// <summary>Recognizes the reviewed Fisch death logo and WASTED title together. Never supplies an action target.</summary>
public static class DeathScreenDetector
{
    private static readonly Lazy<Mat> Reference = new(() =>
    {
        using var stream = typeof(DeathScreenDetector).Assembly.GetManifestResourceStream("FischMacroCS.Assets.death_wasted.png")
            ?? throw new InvalidOperationException("Missing death screen identity resource");
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return Cv2.ImDecode(bytes.ToArray(), ImreadModes.Grayscale);
    });

    public static bool IsDeathScreen(Mat region, int viewportHeight)
    {
        if (region.Empty() || viewportHeight < 200) return false;
        using var gray = new Mat();
        if (region.Channels() == 4) Cv2.CvtColor(region, gray, ColorConversionCodes.BGRA2GRAY);
        else if (region.Channels() == 3) Cv2.CvtColor(region, gray, ColorConversionCodes.BGR2GRAY);
        else region.CopyTo(gray);
        double scale = viewportHeight / 1353.0;
        using var logoSource = new Mat(Reference.Value, new Rect(275, 30, 290, 150));
        using var titleSource = new Mat(Reference.Value, new Rect(220, 190, 410, 95));
        using var logo = new Mat(); using var title = new Mat();
        Cv2.Resize(logoSource, logo, new Size(Math.Max(1, (int)Math.Round(290 * scale)), Math.Max(1, (int)Math.Round(150 * scale))));
        Cv2.Resize(titleSource, title, new Size(Math.Max(1, (int)Math.Round(410 * scale)), Math.Max(1, (int)Math.Round(95 * scale))));
        if (gray.Width < logo.Width || gray.Height < logo.Height) return false;
        Cv2.GaussianBlur(gray, gray, new Size(3, 3), .8);
        Cv2.GaussianBlur(logo, logo, new Size(3, 3), .8);
        Cv2.GaussianBlur(title, title, new Size(3, 3), .8);
        using var scores = new Mat();
        Cv2.MatchTemplate(gray, logo, scores, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(scores, out _, out double logoScore, out _, out Point at);
        if (logoScore < .90) return false;
        int tolerance = Math.Max(2, (int)Math.Ceiling(5 * scale));
        var search = new Rect(at.X - (int)Math.Round(55 * scale) - tolerance,
            at.Y + (int)Math.Round(160 * scale) - tolerance,
            title.Width + 2 * tolerance, title.Height + 2 * tolerance) & new Rect(0, 0, gray.Width, gray.Height);
        if (search.Width < title.Width || search.Height < title.Height) return false;
        using var titleRegion = new Mat(gray, search);
        Cv2.MatchTemplate(titleRegion, title, scores, TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(scores, out _, out double titleScore, out _, out _);
        return titleScore >= .90;
    }
}
