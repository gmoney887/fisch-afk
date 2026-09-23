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
                if (density < .025 || density > .45) continue;
                _landmarks.Add((region, tile.Clone()));
            }
            if (_landmarks.Count < 3) { Reset(); return FishingViewStatus.Learning; }
            _stable = 1;
            return FishingViewStatus.Learning;
        }
        int missing = 0;
        foreach (var (region, reference) in _landmarks)
        {
            int pad = Math.Max(1, (int)(edges.Height * .012));
            int left = Math.Max(0, region.Left - pad), top = Math.Max(0, region.Top - pad);
            var search = new Rect(left, top, Math.Min(edges.Width, region.Right + pad) - left,
                Math.Min(edges.Height, region.Bottom + pad) - top);
            using var current = new Mat(edges, search); using var scores = new Mat();
            Cv2.MatchTemplate(current, reference, scores, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(scores, out _, out double correlation);
            if (!double.IsFinite(correlation) || correlation < .45) missing++;
        }
        if (missing >= 3)
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
