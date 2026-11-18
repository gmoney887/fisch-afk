using OpenCvSharp;

namespace FischMacroCS.Vision;

internal static class HammyshorkReelDetector
{
    internal readonly record struct Observation(Rect? Body, Rect Marker);

    internal static Observation? Find(Mat crop, int offsetY, double height)
    {
        if (FindSlashedReel(crop, offsetY, height) is { } slashed) return slashed;
        int top = Math.Max(0, (int)(height * .79) - offsetY);
        int bottom = Math.Min(crop.Height, (int)(height * .92) - offsetY);
        if (bottom <= top) return null;
        using var track = crop.RowRange(top, bottom);
        var reading = FindStemAndBody(track, height) ?? FindBite(track, offsetY + top, height);
        if (reading is not { } found) return null;
        return new Observation(found.Body is Rect body ? body with { Y = body.Y + top } : null,
            found.Marker with { Y = found.Marker.Y + top });
    }

    private static Observation? FindSlashedReel(Mat crop, int offsetY, double height)
    {
        // Empyrean's red strike crosses the fish and can split both the slate
        // stem and the teeth. Search for the fish icon above that crossing.
        int middle = (int)Math.Round(height * .852) - offsetY;
        int headTop = (int)(height * .746) - offsetY, headBottom = (int)(height * .819) - offsetY;
        int radius = (int)Math.Round(height * .026);
        if (height < 200 || headTop < 0 || headBottom <= headTop || middle + (int)Math.Ceiling(height * .043) >= crop.Height) return null;
        using var row = crop.Row(middle);
        using var red = new Mat();
        Cv2.InRange(row, new Scalar(0, 0, 120), new Scalar(120, 115, 255), red);
        var strike = Cv2.BoundingRect(red);
        if (strike.Width < 2 || strike.Width > height * .26 || !HasAquaRail(crop, middle, height)) return null;
        int margin = (int)Math.Ceiling(height * .10);
        int left = Math.Max(0, strike.Left - margin), right = Math.Min(crop.Width, strike.Right + margin);
        using var headBand = new Mat(crop, new Rect(left, headTop, right - left, headBottom - headTop));
        using var blue = new Mat();
        Cv2.InRange(headBand, new Scalar(248, 226, 180), new Scalar(255, 240, 194), blue);
        var head = Cv2.BoundingRect(blue);
        if (head.Width < height * .032 || head.Width > height * .064 ||
            head.Height < height * .030 || head.Height > height * .068 ||
            Cv2.CountNonZero(blue) < head.Width * head.Height * .13) return null;
        head.X += left;
        head.Y += headTop;

        // Surviving stem pixels above and below the slash still share columns.
        // The upward-pointing nose is a fixed distance to the right of the
        // stem, even when the bite hides the tail and nearly all of the stem.
        int stemTop = middle - (int)(height * .043), stemHeight = (int)(height * .083);
        using var underHead = new Mat(crop, new Rect(head.X, stemTop, head.Width, stemHeight));
        using var slate = new Mat();
        Cv2.InRange(underHead, new Scalar(85, 69, 61), new Scalar(97, 81, 73), slate);
        using var columns = new Mat();
        Cv2.Reduce(slate, columns, ReduceDimension.Row, ReduceTypes.Sum, MatType.CV_32S);
        using var visibleStem = new Mat();
        Cv2.InRange(columns, Scalar.All(height * .008 * 255), Scalar.All(stemHeight * 255), visibleStem);
        var stem = Cv2.BoundingRect(visibleStem);
        int fishX;
        int noseSize = Math.Max(2, (int)Math.Round(height * .006));
        using var nose = new Mat(blue, new Rect(head.Right - left - noseSize, head.Y - headTop, noseSize, noseSize));
        if (head.Top + offsetY <= height * .758 && Cv2.CountNonZero(nose) >= noseSize)
            fishX = head.Right - (int)Math.Round(height * .029);
        else if (stem.Width >= height * .004 && stem.Width <= height * .016)
            fishX = head.X + stem.X + stem.Width / 2;
        else return null;

        // The strike can divide the bar into several contours. Its rounded
        // caps still cross the middle of the aqua rail, and paired horizontal
        // sections of the rim survive above and below the bite.
        int half = Math.Min(crop.Width / 2, (int)Math.Ceiling(height * .510));
        if (half < 1) return null;
        int railLeft = crop.Width / 2 - half;
        using var rail = new Mat(crop, new Rect(railLeft, middle - radius - 2, half * 2, radius * 2 + 5));
        using var rim = new Mat();
        Cv2.InRange(rail, new Scalar(66, 66, 66), new Scalar(74, 74, 74), rim);
        int center = radius + 2, band = Math.Max(2, (int)Math.Round(height * .004));
        using var caps = rim.RowRange(center - band, center + band + 1);
        var span = Cv2.BoundingRect(caps);
        if (span.Width < height * .07 || span.Width > height) return null;
        // A stray gray pixel in the rail cannot extend a cap. Look for a
        // stroke along the outer edge over most rows of the center band.
        int edgeWidth = Math.Max(2, (int)Math.Ceiling(height * .004));
        using var leftCap = caps.ColRange(span.Left, span.Left + edgeWidth);
        using var rightCap = caps.ColRange(span.Right - edgeWidth, span.Right);
        using var edgeRows = new Mat();
        bool HasCap(Mat edge)
        {
            Cv2.Reduce(edge, edgeRows, ReduceDimension.Column, ReduceTypes.Max, -1);
            return Cv2.CountNonZero(edgeRows) >= edge.Rows * .65;
        }
        if (!HasCap(leftCap) || !HasCap(rightCap)) return null;
        using var upper = new Mat(rim, new Rect(span.X, 0, span.Width, band + 3));
        using var lower = new Mat(rim, new Rect(span.X, rim.Height - band - 3, span.Width, band + 3));
        using var upperColumns = new Mat();
        using var lowerColumns = new Mat();
        Cv2.Reduce(upper, upperColumns, ReduceDimension.Row, ReduceTypes.Max, -1);
        Cv2.Reduce(lower, lowerColumns, ReduceDimension.Row, ReduceTypes.Max, -1);
        Cv2.BitwiseAnd(upperColumns, lowerColumns, upperColumns);
        if (Cv2.CountNonZero(upperColumns) < span.Width * .50) return null;
        return new Observation(new Rect(railLeft + span.X, middle - radius, span.Width, radius * 2),
            new Rect(fishX - 1, head.Y, 2, stemTop + stemHeight - head.Y));
    }

    private static Observation? FindStemAndBody(Mat track, double height)
    {
        // The fish rides a solid slate stem. Both the shark fins and the
        // direction arrows can travel independently of the fish.
        var slateLow = new Scalar(85, 69, 61);
        var slateHigh = new Scalar(97, 81, 73);
        using var stem = new Mat();
        // A stem taller than half this search band always crosses its center.
        // Most frames from other rods can be dismissed with a single scanline.
        using var centerRow = track.Row(track.Height / 2);
        Cv2.InRange(centerRow, slateLow, slateHigh, stem);
        if (Cv2.CountNonZero(stem) < height * .005) return null;
        Cv2.InRange(track, slateLow, slateHigh, stem);
        Cv2.FindContours(stem, out Point[][] stems, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var markers = stems.Select(Cv2.BoundingRect).Where(r =>
            r.Height >= height * .07 && r.Height <= height * .09 &&
            r.Width >= height * .006 && r.Width <= height * .016).ToArray();
        if (markers.Length != 1) return null;
        var marker = markers[0];

        // Confirm the exposed aqua rail alongside the body, even at a travel stop.
        int middle = marker.Y + marker.Height / 2;
        if (!HasAquaRail(track, middle, height)) return null;

        // The fill turns translucent when the fish escapes. The dark rim stays
        // opaque. Join only the gap a stem can cut through that rim.
        using var outline = new Mat();
        Cv2.InRange(track, new Scalar(66, 66, 66), new Scalar(74, 74, 74), outline);
        using var bridge = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(marker.Width * 2 + 1, 1));
        Cv2.MorphologyEx(outline, outline, MorphTypes.Close, bridge);
        Cv2.FindContours(outline, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var bodies = contours.Select(Cv2.BoundingRect).Where(r =>
            r.Width >= height * .055 && r.Width <= height && r.Width > r.Height * 1.5 &&
            r.Height >= height * .045 && r.Height <= height * .065 &&
            Math.Abs(r.Y + r.Height / 2 - middle) <= height * .009).ToArray();
        if (bodies.Length != 1) return null;
        return new Observation(bodies[0], marker);
    }

    private static bool HasAquaRail(Mat track, int middle, double height)
    {
        using var railRow = track.Row(middle);
        using var aqua = new Mat();
        Cv2.InRange(railRow, new Scalar(225, 216, 150), new Scalar(249, 242, 203), aqua);
        return Cv2.CountNonZero(aqua) >= height * .06;
    }

    private static Observation? FindBite(Mat track, int offsetY, double height)
    {
        int middle = (int)Math.Round(height * .851) - offsetY;
        if (middle <= 0 || middle >= track.Height || !HasAquaRail(track, middle, height)) return null;

        // The opposing rows of teeth travel with the fish even when its stem
        // is fully covered. Their broad bases taper towards the rail center.
        using var teeth = new Mat();
        using var hsv = new Mat();
        Cv2.CvtColor(track, hsv, ColorConversionCodes.BGR2HSV);
        Cv2.InRange(hsv, new Scalar(0, 0, 201), new Scalar(179, 6, 225), teeth);
        using var bridge = Cv2.GetStructuringElement(MorphShapes.Rect,
            new Size(Math.Max(3, (int)Math.Round(height * .008)), 3));
        using var joined = new Mat();
        Cv2.MorphologyEx(teeth, joined, MorphTypes.Close, bridge);
        Rect? Jaw(int start, int end, bool upper)
        {
            using var half = joined.RowRange(start, end);
            Cv2.FindContours(half, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            Rect? match = null;
            foreach (var contour in contours)
            {
                var r = Cv2.BoundingRect(contour);
                if (r.Width < height * .180 || r.Width > height * .205 ||
                    r.Height < height * .030 || r.Height > height * .065) continue;
                r.Y += start;
                int third = r.Height / 3;
                using var basePixels = new Mat(teeth, new Rect(r.X, upper ? r.Top : r.Bottom - third, r.Width, third));
                using var tips = new Mat(teeth, new Rect(r.X, upper ? r.Bottom - third : r.Top, r.Width, third));
                if (Cv2.CountNonZero(basePixels) - Cv2.CountNonZero(tips) < r.Width * third * .15) continue;
                if (match.HasValue) return null;
                match = r;
            }
            return match;
        }
        if (Jaw(0, middle, true) is not Rect upper || Jaw(middle, track.Height, false) is not Rect lower ||
            Math.Abs(upper.Left - lower.Left) > height * .006 || Math.Abs(upper.Right - lower.Right) > height * .006 ||
            Math.Abs((upper.Bottom + lower.Top) / 2 - middle) > height * .012) return null;
        var jaws = upper | lower;

        // Mask the bite before measuring the rim. If it covers a cap, only
        // report the fish; a single exposed flank cannot establish bar width.
        using var rim = new Mat();
        Cv2.InRange(track, new Scalar(66, 66, 66), new Scalar(74, 74, 74), rim);
        // At smaller scales the one-pixel curved cap can break at its midpoint.
        int capGap = Math.Max(3, (int)Math.Round(height * .008) | 1);
        using var joinRim = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, capGap));
        Cv2.MorphologyEx(rim, rim, MorphTypes.Close, joinRim);
        int padding = Math.Max(2, (int)Math.Ceiling(height * .003));
        int gapLeft = Math.Max(0, jaws.Left - padding), gapRight = Math.Min(track.Width, jaws.Right + padding);
        using (var covered = rim.ColRange(gapLeft, gapRight)) covered.SetTo(Scalar.Black);
        Cv2.FindContours(rim, out Point[][] rims, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        Rect? left = null, right = null;
        foreach (var contour in rims)
        {
            var r = Cv2.BoundingRect(contour);
            if (r.Height < height * .045 || r.Height > height * .065 ||
                Math.Abs(r.Y + r.Height / 2 - middle) > height * .009) continue;
            if (r.Right == gapLeft) left = r;
            else if (r.Left == gapRight) right = r;
            else if (r.Width > r.Height * 1.5 && r.Width <= height && Cv2.ContourArea(contour) > r.Width * r.Height * .75)
                return new Observation(r, jaws);
        }
        Rect? body = left is Rect l && right is Rect rgt && Math.Abs(l.Top - rgt.Top) <= height * .003 &&
            rgt.Right - l.Left <= height ? l | rgt : null;
        return new Observation(body, jaws);
    }
}
