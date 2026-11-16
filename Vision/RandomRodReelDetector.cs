using System.IO;
using OpenCvSharp;

namespace FischMacroCS.Vision;

internal static class RandomRodReelDetector
{
    internal readonly record struct Observation(Rect Body, Rect Marker);

    private static readonly Lazy<Mat> RightArrow = new(() =>
    {
        using var stream = typeof(RandomRodReelDetector).Assembly
            .GetManifestResourceStream("FischMacroCS.Assets.ReelShapes.random_arrow.png")!;
        using var bytes = new MemoryStream();
        stream.CopyTo(bytes);
        return Cv2.ImDecode(bytes.ToArray(), ImreadModes.Grayscale);
    });

    internal static Observation? Find(Mat crop, int offsetY, double height)
    {
        int top = Math.Max(0, (int)(height * .80) - offsetY);
        int bottom = Math.Min(crop.Height, (int)(height * .94) - offsetY);
        if (bottom <= top || height < 200) return null;
        using var band = crop.RowRange(top, bottom);
        using var hsv = new Mat();
        Cv2.CvtColor(band, hsv, ColorConversionCodes.BGR2HSV);
        using var black = new Mat();
        Cv2.InRange(hsv, Scalar.All(0), new Scalar(179, 255, 4), black);

        // Opening isolates the tall fish marker even when it crosses a black arrow.
        using var stem = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(1, (int)(height * .057) | 1));
        using var vertical = new Mat();
        Cv2.MorphologyEx(black, vertical, MorphTypes.Open, stem);
        Cv2.FindContours(vertical, out Point[][] stems, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var markers = stems.Select(Cv2.BoundingRect).Where(r =>
            r.Height >= height * .071 && r.Height <= height * .091 &&
            r.Width >= height * .005 && r.Width <= height * .017).ToArray();
        if (markers.Length != 1) return null;
        var marker = markers[0];

        // The white body becomes a translucent red when the fish escapes.
        using var mask = new Mat();
        using var red = new Mat();
        Cv2.InRange(hsv, new Scalar(0, 0, 240), new Scalar(179, 22, 255), mask);
        Cv2.InRange(hsv, new Scalar(0, 45, 45), new Scalar(13, 230, 175), red);
        Cv2.BitwiseOr(mask, red, mask);
        Cv2.InRange(hsv, new Scalar(168, 45, 45), new Scalar(179, 230, 175), red);
        Cv2.BitwiseOr(mask, red, mask);

        // Join only across the measured marker. Closing the whole rail would
        // attach the red end-stop triangle when the body reaches the left edge.
        int margin = (int)Math.Ceiling(height * .035);
        using var crossing = mask.ColRange(Math.Max(0, marker.Left - margin), Math.Min(mask.Width, marker.Right + margin));
        using var bridge = Cv2.GetStructuringElement(MorphShapes.Rect, new Size((int)(height * .018) | 1, 1));
        Cv2.MorphologyEx(crossing, crossing, MorphTypes.Close, bridge, borderType: BorderTypes.Isolated);
        Cv2.FindContours(mask, out Point[][] bodies, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        foreach (var contour in bodies)
        {
            var body = Cv2.BoundingRect(contour);
            if (body.Width < height * .07 || body.Width > height * 1.03 ||
                body.Height < height * .042 || body.Height > height * .085 ||
                Math.Abs(body.Y + body.Height / 2.0 - marker.Y - marker.Height / 2.0) > height * .023 ||
                Cv2.ContourArea(contour) < body.Width * body.Height * .74) continue;
            if (HasOutwardArrow(black, body))
                return new(body with { Y = body.Y + top }, marker with { Y = marker.Y + top });
        }
        return null;
    }

    private static bool HasOutwardArrow(Mat black, Rect body)
    {
        using var interior = new Mat(black, body);
        Cv2.FindContours(interior, out Point[][] contours, out _, RetrievalModes.List, ContourApproximationModes.ApproxSimple);
        foreach (var contour in contours)
        {
            var r = Cv2.BoundingRect(contour);
            if (r.Height < body.Height * .31 || r.Height > body.Height * .78 ||
                r.Width < r.Height * .85 || r.Width > r.Height * 1.18 ||
                Math.Abs(r.Y + r.Height / 2.0 - body.Height / 2.0) > body.Height * .16 ||
                Math.Min(r.Left, body.Width - r.Right) > body.Height * 1.1) continue;
            using var glyph = new Mat(interior, r);
            using var normalized = new Mat();
            Cv2.Resize(glyph, normalized, RightArrow.Value.Size(), 0, 0, InterpolationFlags.Linear);
            if (r.X + r.Width / 2 < body.Width / 2) Cv2.Flip(normalized, normalized, FlipMode.Y);
            using var score = new Mat();
            Cv2.MatchTemplate(normalized, RightArrow.Value, score, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(score, out _, out double confidence);
            if (confidence >= .72) return true;
        }
        return false;
    }
}
