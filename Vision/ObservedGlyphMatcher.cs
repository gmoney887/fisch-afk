using OpenCvSharp;

namespace FischMacroCS.Vision;

/// <summary>Recognize reviewed glyph identity at a size measured from the current frame.</summary>
public static class ObservedGlyphMatcher
{
    public enum Ink { Blue, Red, Balance, Claim, White }
    public readonly record struct Match(bool Found, Rect Bounds, double Confidence, bool Ambiguous = false);

    public static Mat Mask(Mat image, Ink ink)
    {
        using var hsv = new Mat();
        using var bgr = new Mat();
        if (image.Channels()==1) Cv2.CvtColor(image,bgr,ColorConversionCodes.GRAY2BGR);
        else if (image.Channels()==4) Cv2.CvtColor(image,bgr,ColorConversionCodes.BGRA2BGR);
        else image.CopyTo(bgr);
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
        var mask = new Mat();
        switch (ink)
        {
            case Ink.White:
                Cv2.InRange(hsv,new Scalar(0,0,180),new Scalar(179,65,255),mask); break;
            case Ink.Blue:
                Cv2.InRange(hsv, new Scalar(85, 45, 95), new Scalar(125, 255, 255), mask); break;
            case Ink.Red:
                using (var second = new Mat())
                {
                    Cv2.InRange(hsv, new Scalar(0, 140, 220), new Scalar(10, 255, 255), mask);
                    Cv2.InRange(hsv, new Scalar(170, 140, 220), new Scalar(180, 255, 255), second);
                    Cv2.BitwiseOr(mask, second, mask);
                }
                break;
            case Ink.Claim:
                Cv2.InRange(hsv, new Scalar(20, 40, 110), new Scalar(90, 255, 255), mask); break;
            default:
                using (var second = new Mat())
                {
                    Cv2.InRange(hsv, new Scalar(10, 45, 120), new Scalar(40, 255, 255), mask);
                    Cv2.InRange(hsv, new Scalar(80, 45, 120), new Scalar(110, 255, 255), second);
                    Cv2.BitwiseOr(mask, second, mask);
                }
                break;
        }
        return mask;
    }

    public static Match Find(Mat region, Mat reference, Ink ink, double minimumConfidence)
    {
        using var scene = Mask(region, ink);
        using var template = Mask(reference, ink);
        if (Cv2.CountNonZero(template) < 5) return default;
        var referenceBounds = Cv2.BoundingRect(template);
        using var referenceInk = new Mat(template, referenceBounds);
        Cv2.FindContours(scene, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        // Component heights supply observed scale. No list of screen resolutions or DPI assumptions.
        var heights = contours.Select(Cv2.BoundingRect).Where(r => r.Height >= 3 && r.Height <= region.Height / 2)
            .Select(r => r.Height).Distinct().Order().Take(32).ToArray();
        var candidates = new HashSet<Rect>();
        foreach (int height in heights)
        {
            using var joined = new Mat();
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(Math.Max(1, height), 1));
            Cv2.MorphologyEx(scene, joined, MorphTypes.Close, kernel);
            Cv2.FindContours(joined, out Point[][] words, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            foreach (var word in words)
            {
                var rect = Cv2.BoundingRect(word);
                if (rect.Left == 0 || rect.Top == 0 || rect.Right >= scene.Width || rect.Bottom >= scene.Height) continue;
                using var component = new Mat(scene, rect);
                if (Cv2.CountNonZero(component) < 5) continue;
                var tight = Cv2.BoundingRect(component);
                candidates.Add(new Rect(rect.X + tight.X, rect.Y + tight.Y, tight.Width, tight.Height));
            }
        }
        Match best = default;
        var verified = new List<Rect>();
        foreach (var rect in candidates.Take(256))
        {
            double ratio = (double)rect.Width / rect.Height / ((double)referenceBounds.Width / referenceBounds.Height);
            if (ratio < .80 || ratio > 1.20) continue;
            using var glyph = new Mat(scene, rect);
            using var normalized = new Mat(); using var expected = new Mat(); using var score = new Mat();
            // Normalize both shapes; smoothing tolerates rasterization differences without changing identity.
            var size = new Size(128, Math.Max(16, (int)Math.Round(128.0 * referenceBounds.Height / referenceBounds.Width)));
            Cv2.Resize(glyph, normalized, size, 0, 0, InterpolationFlags.Area);
            Cv2.Resize(referenceInk, expected, size, 0, 0, InterpolationFlags.Area);
            ImageSmoothing.Apply(normalized, normalized); ImageSmoothing.Apply(expected, expected);
            Cv2.MatchTemplate(normalized, expected, score, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(score, out _, out double confidence);
            // Rasterization moves thin strokes by a pixel. Compare bidirectional
            // stroke coverage as well as correlation, with tolerance relative to glyph height.
            Cv2.Threshold(normalized,normalized,100,255,ThresholdTypes.Binary);
            Cv2.Threshold(expected,expected,100,255,ThresholdTypes.Binary);
            using var expandedActual = new Mat(); using var expandedExpected = new Mat(); using var overlap = new Mat();
            int tolerance = Math.Max(1,(int)Math.Round(size.Height*.08));
            using var toleranceKernel = Cv2.GetStructuringElement(MorphShapes.Ellipse,new Size(2*tolerance+1,2*tolerance+1));
            Cv2.Dilate(normalized,expandedActual,toleranceKernel); Cv2.Dilate(expected,expandedExpected,toleranceKernel);
            Cv2.BitwiseAnd(normalized,expandedExpected,overlap);
            double precision = Cv2.CountNonZero(overlap)/(double)Math.Max(1,Cv2.CountNonZero(normalized));
            Cv2.BitwiseAnd(expected,expandedActual,overlap);
            double recall = Cv2.CountNonZero(overlap)/(double)Math.Max(1,Cv2.CountNonZero(expected));
            confidence = Math.Max(confidence,Math.Min(precision,recall));
            if (double.IsFinite(confidence) && confidence >= minimumConfidence) verified.Add(rect);
            if (double.IsFinite(confidence) && confidence > best.Confidence)
                best = new(confidence >= minimumConfidence, rect, confidence);
        }
        // Two separate controls with the same apparent identity are ambiguous.
        if (best.Found && verified.Any(r => (r & best.Bounds).Width <= 0 || (r & best.Bounds).Height <= 0))
            return new(false,default,best.Confidence,true);
        return best;
    }
}
