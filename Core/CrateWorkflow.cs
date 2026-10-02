using OpenCvSharp;
using FischMacroCS.Vision;

namespace FischMacroCS.Core;

public interface ICrateVision
{
    string[] MissingTemplates();
    Point? Find(Mat frame, string name);
    bool Selected(Mat frame, Point item);
    bool Empty(Mat frame);
    bool QuantityPresent(Mat frame);
}

/// <summary>Text and panel geometry from the September 18 manual crate demonstration.</summary>
public sealed class CrateVision(string directory) : ICrateVision, IDisposable
{
    public static readonly string[] Names = ["bag", "search", "item", "dialog", "one", "yes", "opened", "reward-word"];
    private readonly Dictionary<string, Mat> _templates = new();
    private readonly Dictionary<(string Key, int W, int H), Mat> _prepared = new();
    private readonly Dictionary<string, (int W, int H)> _lastSize = new();
    private int _viewportHeight;
    private Mat? _notificationFrame;
    private Mat? _normalizationSource, _normalized;
    private Point? _openedPoint, _cratePoint;
    public string[] MissingTemplates() => Names.Where(n => !System.IO.File.Exists(
        System.IO.Path.Combine(directory, "crate-" + n + ".png"))).ToArray();

    public Point? Find(Mat frame, string name)
    {
        if (frame.Height <= 1369) return FindAtCaptureScale(frame, name);
        if (!ReferenceEquals(frame, _normalizationSource))
        {
            _normalized?.Dispose();
            _normalized = new Mat();
            _normalizationSource = frame;
            Cv2.Resize(frame, _normalized, new Size((int)Math.Round(frame.Width * 1369.0 / frame.Height), 1369));
        }
        var point = FindAtCaptureScale(_normalized!, name);
        return point.HasValue ? new Point((int)Math.Round(point.Value.X * frame.Width / (double)_normalized!.Width),
            (int)Math.Round(point.Value.Y * frame.Height / 1369.0)) : null;
    }

    private Point? FindAtCaptureScale(Mat frame, string name)
    {
        if (name is not ("opened" or "reward-word")) return FindUncached(frame, name);
        if (!ReferenceEquals(_notificationFrame, frame))
        {
            _notificationFrame = frame;
            _openedPoint = _cratePoint = null;
            Rect Row(Point point, int left, int right)
            {
                int top = Math.Max(0, point.Y - (int)(frame.Height * .012));
                return new Rect(left, top, Math.Max(1, right - left),
                    Math.Min(frame.Height - top, Math.Max(1, (int)(frame.Height * .024))));
            }
            var crate = FindUncached(frame, "reward-word");
            Point? opened = crate.HasValue ? FindUncached(frame, "opened", Row(crate.Value,
                Math.Max(0, crate.Value.X - (int)(frame.Height * .65)), crate.Value.X)) : null;
            if (!opened.HasValue && crate.HasValue)
            {
                // A suffix candidate can be another line of the reward list.
                // Fall back to the prefix, then constrain the suffix to its row.
                double edge = frame.Width / (2.0 * frame.Height);
                opened = FindUncached(frame, "opened", Region(frame, edge - .65, .20, edge - .004, .82));
                if (opened.HasValue) crate = FindUncached(frame, "reward-word",
                    Row(opened.Value, opened.Value.X, frame.Width));
            }
            if (opened.HasValue && crate.HasValue && crate.Value.X > opened.Value.X &&
                Math.Abs(crate.Value.Y - opened.Value.Y) < frame.Height * .012)
            { _openedPoint = opened; _cratePoint = crate; }
        }
        return name == "opened" ? _openedPoint : _cratePoint;
    }

    private Point? FindUncached(Mat frame, string name, Rect? notificationRow = null)
    {
        Point? Match(Mat image, string target, string template, bool whiteText = false) =>
            MatchTemplate(image, target, template, whiteText, notificationRow);
        // Dense stacks shrink/overlap labels and vary their background. Match
        // the white Crate lettering independently of the colored modifiers.
        if (name is "item" or "search")
        {
            var dense = Match(frame, name, name + "-dense", whiteText: true)
                ?? (name == "search" ? Match(frame, name, name + "-dense") : null);
            if (dense.HasValue) return dense;
        }
        if (name == "item")
        {
            var text = Match(frame, name, name, whiteText: true);
            if (text.HasValue) return text;
        }
        if (name is "opened" or "reward-word")
        {
            var text = Match(frame, name, name + "-current", whiteText: true)
                ?? Match(frame, name, name + "-compact", whiteText: true)
                ?? (name == "opened" ? Match(frame, name, "opened-mutated", whiteText: true) : null)
                ?? Match(frame, name, name, whiteText: true);
            if (text.HasValue) return text;
        }
        return (name is "bag" or "search" or "opened" or "reward-word" ? Match(frame, name, name + "-current") : null)
           ?? (name is "opened" or "reward-word" ? Match(frame, name, name + "-compact") : null)
           ?? Match(frame, name, name);
    }

    private Point? MatchTemplate(Mat frame, string name, string templateName, bool whiteText = false, Rect? searchRegion = null)
    {
        if (_viewportHeight != frame.Height)
        {
            foreach (var image in _prepared.Values) image.Dispose();
            _prepared.Clear(); _lastSize.Clear(); _viewportHeight = frame.Height;
        }
        string cacheKey = templateName + (whiteText ? ":white" : "");
        if (!_templates.TryGetValue(cacheKey, out var template))
        {
            string path = System.IO.Path.Combine(directory, "crate-" + templateName + ".png");
            if (!System.IO.File.Exists(path)) return null;
            using var color = Cv2.ImRead(path);
            if (color.Empty()) return null;
            template = new Mat();
            if (whiteText) color.CopyTo(template);
            else Cv2.CvtColor(color, template, ColorConversionCodes.BGR2GRAY);
            // Colored labels may contain no white pixels. A constant template
            // produces meaningless perfect correlation on a blank region.
            Cv2.MeanStdDev(template, out _, out var deviation);
            if (deviation.Val0 < 1) { template.Dispose(); return null; }
            _templates[cacheKey] = template;
        }
        // Horizontal values are relative to viewport center, in viewport heights.
        var bounds = name switch
        {
            "bag" => (-.245, .62, -.115, .78),
            "search" => (-.015, .69, .200, .80),
            "item" => (-.228, .732, .228, .945),
            "dialog" => (-.120, .375, .120, .423),
            "one" => (-.018, .566, .018, .590),
            "yes" => (-.210, .546, -.080, .610),
            // Notification is anchored to the right edge, not the screen center.
            _ => (frame.Width / (2.0 * frame.Height) - .260, .20,
                  frame.Width / (2.0 * frame.Height) - .004, .82)
        };
        var rect = searchRegion ?? Region(frame, bounds.Item1, bounds.Item2, bounds.Item3, bounds.Item4);
        using var roi = new Mat(frame, rect); using var gray = new Mat();
        if (whiteText)
        {
            // Exact filter identity distinguishes "crate" from "crates"; retain its stricter comparison.
            var observed = name == "search" ? default : ObservedGlyphMatcher.Find(roi,template,ObservedGlyphMatcher.Ink.White,.96);
            if (observed.Found)
            {
                using var glyph = new Mat(roi,observed.Bounds);
                var snapped = new VisionProcessor().DynamicUISnapWithStatus(glyph,glyph.Width/2,glyph.Height/2,
                    VisionProcessor.UIColorType.WhiteText,Math.Max(glyph.Width,glyph.Height));
                if (snapped.Found) return new Point(rect.X+observed.Bounds.X+snapped.Pt.X,rect.Y+observed.Bounds.Y+snapped.Pt.Y);
            }
            using var ink = ObservedGlyphMatcher.Mask(roi,ObservedGlyphMatcher.Ink.White);
            ink.CopyTo(gray);
        }
        else Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);
        using var smoothed = Smooth(gray);
        double threshold = name is "one" or "search" ? .94 : .90;
        IEnumerable<(int W, int H)> Sizes()
        {
            if (_lastSize.TryGetValue(cacheKey, out var last)) yield return last;
            foreach (double textScale in name is "bag" or "search" or "item" or "opened" or "reward-word" ? new[] { 1.0, .7, .8, .9 } : new[] { 1.0 })
            {
            double referenceHeight = templateName.EndsWith("-dense", StringComparison.Ordinal) ? 1353.0 : 1369.0;
            int width = Math.Max(1, (int)Math.Round(template.Width * frame.Height / referenceHeight * textScale));
            int height = Math.Max(1, (int)Math.Round(template.Height * frame.Height / referenceHeight * textScale));
            foreach (int dw in new[] { 0, -1, 1 })
            foreach (int dh in new[] { 0, -1, 1 })
                yield return (Math.Max(1, width + dw), Math.Max(1, height + dh));
            }
        }
        foreach (var size in Sizes().Distinct())
            {
                var dimensions = (cacheKey, size.W, size.H);
                if (!_prepared.TryGetValue(dimensions, out var scaled))
                {
                    using var resized = new Mat();
                    Cv2.Resize(template, resized, new Size(dimensions.Item2, dimensions.Item3));
                    using var textMask = new Mat();
                    if (whiteText) { using var ink = ObservedGlyphMatcher.Mask(resized,ObservedGlyphMatcher.Ink.White); ink.CopyTo(textMask); }
                    else resized.CopyTo(textMask);
                    Cv2.MeanStdDev(textMask, out _, out var maskDeviation);
                    if (maskDeviation.Val0 < 1) continue;
                    _prepared[dimensions] = scaled = Smooth(textMask);
                }
                if (gray.Width < scaled.Width || gray.Height < scaled.Height) continue;
                using var scores = new Mat();
                Cv2.MatchTemplate(smoothed, scaled, scores, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(scores, out _, out double confidence, out _, out var point);
                if (double.IsFinite(confidence) && confidence >= threshold)
                {
                    _lastSize[cacheKey] = size;
                    return new Point(rect.X + point.X + scaled.Width / 2, rect.Y + point.Y + scaled.Height / 2);
                }
            }
        return null;
    }

    private static Rect Region(Mat frame, double left, double top, double right, double bottom)
    {
        int x = Math.Clamp((int)(frame.Width / 2.0 + left * frame.Height), 0, frame.Width - 1);
        int y = Math.Clamp((int)(top * frame.Height), 0, frame.Height - 1);
        return new Rect(x, y, Math.Max(1, Math.Min(frame.Width, (int)(frame.Width / 2.0 + right * frame.Height)) - x),
            Math.Max(1, Math.Min(frame.Height, (int)(bottom * frame.Height)) - y));
    }

    public bool Selected(Mat frame, Point item)
    {
        // A selected inventory cell has a bright rectangular outline. Require
        // both horizontal and vertical sides, not white item lettering.
        if (Find(frame, "bag") == null) return false;
        int size = Math.Max(1, (int)Math.Round(70 * frame.Height / 1369.0));
        int x = Math.Max(0, item.X - size), y = Math.Max(0, item.Y - size);
        var region = new Rect(x, y, Math.Min(frame.Width - x, size * 2), Math.Min(frame.Height - y, size * 2));
        using var cell = new Mat(frame, region);
        using var white = new Mat();
        Cv2.InRange(cell, new Scalar(95, 95, 95), Scalar.All(255), white);
        Cv2.FindContours(white, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        foreach (var contour in contours)
        {
            var bounds = Cv2.BoundingRect(contour);
            if (bounds.Width < size * .8 || bounds.Width > size * 1.2 ||
                bounds.Height < size * .8 || bounds.Height > size * 1.2 ||
                !bounds.Contains(new Point(item.X - x, item.Y - y))) continue;
            // A connected outline surrounds the chosen text. Its contour encloses
            // most of the cell, unlike disconnected white item lettering.
            if (Cv2.ContourArea(contour) >= bounds.Width * bounds.Height * .8) return true;
        }
        return false;
    }

    public bool Empty(Mat frame)
    {
        // A lack of item matches is NOT empty-inventory evidence. The entire
        // result grid must be a blank, dark panel with the exact filter visible.
        var header = Find(frame, "bag");
        if (header == null || Find(frame, "search") == null) return false;
        using var grid = new Mat(frame, Region(frame, -.220, header.Value.Y / (double)frame.Height + 75 / 1369.0, .222, Math.Min(.937, header.Value.Y / (double)frame.Height + 307 / 1369.0)));
        using var gray = new Mat(); Cv2.CvtColor(grid, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.MinMaxLoc(gray, out _, out double maximum);
        return maximum < 35;
    }

    public bool QuantityPresent(Mat frame)
    {
        // The game clamps the requested upper bound to the available stack.
        // Check for visible field text, not an exact quantity of one. A caret
        // alone must not authorize confirmation. This is not a count reader.
        using var field = new Mat(frame, Region(frame, -.040, .566, .040, .590));
        using var ink = new Mat();
        Cv2.InRange(field, new Scalar(130, 130, 130), Scalar.All(255), ink);
        Cv2.FindContours(ink, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        double scale = frame.Height / 1369.0;
        return contours.Any(c =>
        {
            var box = Cv2.BoundingRect(c);
            return box.Width >= Math.Max(2, 3 * scale) && box.Height >= 5 * scale &&
                box.Height <= 20 * scale && Cv2.ContourArea(c) >= 3 * scale * scale;
        });
    }

    // Exact 3x3 [1 2 1] smoothing with reflect-101 borders. Avoid the native
    // GaussianBlur path which access-violated during live inventory closure.
    // Source and destination are separate and both owned for the entire loop.
    private static unsafe Mat Smooth(Mat source)
    {
        var result = new Mat(source.Rows, source.Cols, MatType.CV_8UC1);
        int w = source.Cols, h = source.Rows;
        for (int y = 0; y < h; y++)
        {
            byte* above = (byte*)source.Ptr(y == 0 ? Math.Min(1, h - 1) : y - 1);
            byte* row = (byte*)source.Ptr(y);
            byte* below = (byte*)source.Ptr(y == h - 1 ? Math.Max(0, h - 2) : y + 1);
            byte* dst = (byte*)result.Ptr(y);
            for (int x = 0; x < w; x++)
            {
                int l = x == 0 ? Math.Min(1, w - 1) : x - 1;
                int r = x == w - 1 ? Math.Max(0, w - 2) : x + 1;
                dst[x] = (byte)((above[l] + 2 * above[x] + above[r] +
                    2 * row[l] + 4 * row[x] + 2 * row[r] + below[l] + 2 * below[x] + below[r] + 8) / 16);
            }
        }
        GC.KeepAlive(source);
        return result;
    }

    public void Dispose()
    {
        _normalized?.Dispose();
        _normalizationSource = _notificationFrame = null;
        foreach (var template in _templates.Values) template.Dispose();
        foreach (var template in _prepared.Values) template.Dispose();
        _templates.Clear(); _prepared.Clear();
    }
}

public static class CrateWorkflow
{
    public const string StackQuantityRequest = "999999";
    public static WorkflowResult Run(ICrateVision vision, Func<Mat?> capture, Action<int> delay,
        Action<Point> click, Action<char> key, Action<string> replaceText, CancellationToken cancellation,
        Action<string>? progress = null, IClock? clock = null)
    {
        cancellation.ThrowIfCancellationRequested();
        var missing = vision.MissingTemplates();
        if (missing.Length > 0) return new(ActionOutcome.Unknown, "Crate automation templates missing: " + string.Join(", ", missing));
        Mat Frame()
        {
            cancellation.ThrowIfCancellationRequested();
            var frame = capture();
            if (frame == null || frame.Empty()) { frame?.Dispose(); throw new GameplayInterruptedException("Invalid crate capture."); }
            return frame;
        }
        bool Await(Func<Mat, bool> predicate, int timeout = 4000)
        {
            long started = clock?.Timestamp ?? System.Diagnostics.Stopwatch.GetTimestamp();
            double Elapsed() => clock?.ElapsedMilliseconds(started) ?? System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            int stable = 0;
            while (Elapsed() <= timeout)
            {
                using var frame = Frame();
                stable = predicate(frame) ? stable + 1 : 0;
                if (Elapsed() > timeout) return false;
                if (stable >= 2) return true;
                delay(Math.Min(100, Math.Max(1, (int)(timeout - Elapsed()))));
            }
            return false;
        }
        WorkflowResult Unknown(string reason) => new(ActionOutcome.Unknown, reason);
        bool CloseBag()
        {
            using (var frame = Frame()) { if (vision.Find(frame, "bag") == null) return true; }
            key('g');
            return Await(f => vision.Find(f, "bag") == null);
        }
        bool Reward(Mat frame)
        {
            var opened = vision.Find(frame, "opened");
            if (!opened.HasValue) return false;
            var crate = vision.Find(frame, "reward-word");
            return crate.HasValue && crate.Value.X > opened.Value.X &&
                Math.Abs(crate.Value.Y - opened.Value.Y) < frame.Height * .012;
        }

        progress?.Invoke("Searching the inventory for crates...");
        using (var frame = Frame())
        {
            if (vision.Find(frame, "dialog") != null) return Unknown("Close the existing crate dialog before starting.");
            if (vision.Find(frame, "bag") == null) key('g');
        }
        Point? previousHeader = null;
        if (!Await(f =>
        {
            var header = vision.Find(f, "bag");
            bool settled = header.HasValue && previousHeader.HasValue &&
                Math.Abs(header.Value.X - previousHeader.Value.X) <= 2 &&
                Math.Abs(header.Value.Y - previousHeader.Value.Y) <= 2;
            previousHeader = header;
            return settled;
        }))
            return Unknown("Inventory did not open after G. No equipment tool was selected.");
        bool filterReady;
        using (var frame = Frame())
        {
            var header = vision.Find(frame, "bag");
            if (header == null) return Unknown("Inventory disappeared before focusing search.");
            var savedFilter = vision.Find(frame, "search");
            filterReady = savedFilter != null;
            if (!filterReady) click(new Point(frame.Width / 2 + (int)(frame.Height * .08), header.Value.Y + (int)(43 * frame.Height / 1369.0)));
        }
        if (!filterReady)
        {
            delay(250); // Let Roblox focus the field before replacing a saved filter.
            replaceText("crate");
        }
        if (!Await(f => vision.Find(f, "bag") != null && vision.Find(f, "search") != null))
            return Unknown("The crate search text was not confirmed.");
        Point? item = null;
        bool empty = false;
        if (!Await(f => { item = vision.Find(f, "item"); empty = item == null && vision.Empty(f); return empty || item != null; }))
            return Unknown("No recognizable crate or verified empty result grid was found.");
        if (empty) return CloseBag() ? new(ActionOutcome.ConfirmedSuccess, "Empty inventory and inventory closure visually confirmed")
            : Unknown("Empty inventory detected, but inventory closure was not confirmed.");
        using (var frame = Frame())
        {
            item = vision.Find(frame, "item");
            if (!item.HasValue) return Unknown("Crate disappeared before selection.");
            if (!vision.Selected(frame, item.Value)) click(item.Value);
        }
        if (!Await(f => vision.Selected(f, item!.Value))) return Unknown("Crate selection was not confirmed.");
        if (!CloseBag()) return Unknown("Inventory did not close.");
        // Let earlier reward notifications clear before opening another crate.
        progress?.Invoke("Waiting for the previous crate notification to clear...");
        if (!Await(f => !Reward(f), 15000)) return Unknown("Previous crate notification did not clear.");
        progress?.Invoke("Opening the selected crate stack...");
        using (var frame = Frame()) click(new Point(frame.Width / 2, (int)(frame.Height * .65)));
        if (!Await(f => vision.Find(f, "dialog") != null)) return Unknown("The selected crate did not open a confirmation dialog.");
        using (var frame = Frame())
        {
            click(new Point(frame.Width / 2, (int)(frame.Height * .576)));
            delay(250);
            replaceText(StackQuantityRequest);
            // Commit the field without confirming the purchase/open action.
            click(new Point(frame.Width / 2, (int)(frame.Height * .40)));
        }
        if (!Await(f => vision.Find(f, "dialog") != null && vision.QuantityPresent(f)))
            return Unknown("The crate quantity field is blank or unreadable after requesting the full stack.");
        using (var frame = Frame())
        {
            var yes = vision.Find(frame, "yes");
            if (yes == null || vision.Find(frame, "dialog") == null || !vision.QuantityPresent(frame) || Reward(frame))
                return Unknown("Crate confirmation changed before opening.");
            click(yes.Value);
        }
        bool Opened(Mat f) => vision.Find(f, "dialog") == null && Reward(f);
        if (!Await(Opened, 8000))
        {
            // A missed toast is uncertainty, not evidence that the crate failed.
            // Retry input only when the same actionable dialog is still visible.
            using (var frame = Frame())
            {
                if (vision.Find(frame, "dialog") != null)
                {
                    var yes = vision.Find(frame, "yes");
                    if (yes == null || !vision.QuantityPresent(frame) || Reward(frame))
                        return Unknown("Crate dialog changed during confirmation recovery.");
                    progress?.Invoke("Confirmation dialog is still open; retrying its verified Yes button...");
                    click(yes.Value);
                }
                else progress?.Invoke("Dialog closed; checking again for delayed reward evidence...");
            }
            if (!Await(Opened, 8000))
            {
                using var frame = Frame();
                bool closed = vision.Find(frame, "dialog") == null;
                return new(ActionOutcome.Unknown,
                    closed ? "Dialog closed but reward remains unconfirmed; inventory must be reacquired."
                        : "Crate dialog remained open after two verified confirmation attempts.",
                    RetryableWithoutRecovery: closed);
            }
        }
        return new(ActionOutcome.ConfirmedSuccess, "Crate batch opened; new reward notification confirmed", RewardClaimed: true);
    }
}
