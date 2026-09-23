using OpenCvSharp;

namespace FischMacroCS.Vision;

public enum FishingViewStatus { Learning, Stable, Uncertain, Changed }

/// <summary>
/// Detects persistent loss of a stationary fishing view, not world coordinates or swimming.
/// Camera rotation is also a reason to ask the user to check the position. The central
/// avatar/effects, top menus, quest panel, reel and hotbar are outside the landmark tiles.
/// </summary>
public sealed class FishingViewGuard : IDisposable
{
    private readonly List<(Rect Region, Mat Image)> _landmarks = new();
    private Size _viewport;
    private int _stable, _changed;
    public bool IsArmed => _stable >= 3 && _landmarks.Count >= 3;

    public FishingViewStatus Observe(Mat frame)
    {
        if (frame.Empty() || frame.Height < 200) return FishingViewStatus.Learning;
        if (_landmarks.Count > 0 && _viewport != frame.Size())
            return IsArmed ? FishingViewStatus.Changed : ResetAndLearn(frame);
        using var gray = new Mat(); using var small = new Mat(); using var edges = new Mat();
        Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
        double scale = Math.Min(1, 540.0 / frame.Height);
        Cv2.Resize(gray, small, new Size(), scale, scale, InterpolationFlags.Area);
        Cv2.GaussianBlur(small, small, new Size(3, 3), 0);
        Cv2.Canny(small, edges, 45, 100);
        if (_landmarks.Count == 0)
        {
            _viewport = frame.Size();
            int h = edges.Height;
            foreach (double y in new[] { .52, .68 })
            foreach (double x in new[] { -.38, .38 })
            {
                var region = new Rect((int)(edges.Width / 2.0 + (x - .09) * h), (int)((y - .06) * h),
                    (int)(h * .18), (int)(h * .12));
                if (region.X < 0 || region.Right > edges.Width || region.Bottom > edges.Height) continue;
                using var tile = new Mat(edges, region);
                double density = Cv2.CountNonZero(tile) / (double)(region.Width * region.Height);
                using var grayTile = new Mat(small, region);
                Cv2.MeanStdDev(grayTile, out _, out Scalar deviation);
                if (density < .005 || density > .45 || deviation.Val0 < 6) continue;
                _landmarks.Add((region, grayTile.Clone()));
            }
            if (_landmarks.Count < 3) { Reset(); return FishingViewStatus.Learning; }
            _stable = 1;
            return FishingViewStatus.Learning;
        }
        int Match(double zoom)
        {
            int matches = 0;
            foreach (var (region, reference) in _landmarks)
            {
                // Fishing animations change the camera FOV around the screen center.
                // All landmarks must agree on one zoom; do not learn a moved scene.
                int x = (int)Math.Round(small.Width / 2.0 + (region.X - small.Width / 2.0) * zoom);
                int y = (int)Math.Round(small.Height / 2.0 + (region.Y - small.Height / 2.0) * zoom);
                int width = Math.Max(2, (int)Math.Round(region.Width * zoom));
                int height = Math.Max(2, (int)Math.Round(region.Height * zoom));
                int pad = Math.Max(1, (int)(small.Height * .012));
                int left = Math.Max(0, x - pad), top = Math.Max(0, y - pad);
                var search = new Rect(left, top, Math.Min(small.Width, x + width + pad) - left,
                    Math.Min(small.Height, y + height + pad) - top);
                if (search.Width < width || search.Height < height) continue;
                using var scaled = new Mat();
                Cv2.Resize(reference, scaled, new Size(width, height), 0, 0, InterpolationFlags.Area);
                using var current = new Mat(small, search); using var scores = new Mat();
                Cv2.MatchTemplate(current, scaled, scores, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(scores, out _, out double correlation);
                if (double.IsFinite(correlation) && correlation >= .70) matches++;
            }
            return matches;
        }
        bool unchanged = Match(1) >= 3;
        if (!unchanged)
            foreach (double zoom in new[] { .975, 1.025, .95, 1.05, .925, 1.075, .90, 1.10, .875, 1.125, .85, 1.15, .825, 1.175, .80, 1.20, .775, 1.225, .75, 1.25 })
                if (Match(zoom) >= 3) { unchanged = true; break; }
        if (!unchanged)
        {
            if (!IsArmed) return ResetAndLearn(frame);
            return ++_changed >= 3 ? FishingViewStatus.Changed : FishingViewStatus.Uncertain;
        }
        _changed = 0;
        _stable++;
        return IsArmed ? FishingViewStatus.Stable : FishingViewStatus.Learning;
    }

    private FishingViewStatus ResetAndLearn(Mat frame) { Reset(); return Observe(frame); }
    public void Reset()
    {
        foreach (var landmark in _landmarks) landmark.Image.Dispose();
        _landmarks.Clear(); _stable = _changed = 0;
    }
    public void Dispose() => Reset();
}
