using OpenCvSharp;
using System.IO;

namespace FischMacroCS.Vision;

/// <summary>Recognizes both fixed phrases around the variable streak count.</summary>
public static class CatchFailureDetector
{
    private static readonly Lazy<Mat> Prefix = new(() => Load("catch_loss_prefix.png"));
    private static readonly Lazy<Mat> Suffix = new(() => Load("catch_loss_suffix.png"));
    private static Mat Load(string name)
    {
        var assembly = typeof(CatchFailureDetector).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith(name)))!;
        using var bytes = new MemoryStream(); stream.CopyTo(bytes);
        return Cv2.ImDecode(bytes.ToArray(), ImreadModes.Color);
    }
    private static Mat Gold(Mat image)
    {
        using var hsv = new Mat(); var mask = new Mat();
        Cv2.CvtColor(image, hsv, ColorConversionCodes.BGR2HSV);
        Cv2.InRange(hsv, new Scalar(10, 60, 30), new Scalar(40, 255, 255), mask);
        return mask;
    }
    public static bool Detect(Mat track, int viewportHeight)
    {
        if (track.Empty() || viewportHeight <= 0) return false;
        using var region = new Mat(track, new Rect(0, 0, track.Width,
            Math.Min(track.Height, Math.Max(1, (int)(viewportHeight * .11)))));
        using var mask = Gold(region);
        Point? Match(Mat source)
        {
            using var resized = new Mat();
            Cv2.Resize(source, resized, new Size(Math.Max(1, (int)Math.Round(source.Width * viewportHeight / 1369.0)),
                Math.Max(1, (int)Math.Round(source.Height * viewportHeight / 1369.0))));
            using var template = Gold(resized);
            if (template.Width > mask.Width || template.Height > mask.Height || Cv2.CountNonZero(template) < 10) return null;
            using var scores = new Mat();
            Cv2.MatchTemplate(mask, template, scores, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(scores, out _, out double confidence, out _, out var location);
            return double.IsFinite(confidence) && confidence >= .85 ? location : null;
        }
        var prefix = Match(Prefix.Value); var suffix = Match(Suffix.Value);
        double scale = viewportHeight / 1369.0;
        return prefix.HasValue && suffix.HasValue &&
            Math.Abs(prefix.Value.Y - suffix.Value.Y) <= Math.Max(2, 4 * scale) &&
            suffix.Value.X - prefix.Value.X >= 330 * scale && suffix.Value.X - prefix.Value.X <= 700 * scale;
    }
}
