using OpenCvSharp;

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
    public string[] MissingTemplates() => Names.Where(n => !System.IO.File.Exists(
        System.IO.Path.Combine(directory, "crate-" + n + ".png"))).ToArray();

    public Point? Find(Mat frame, string name)
    {
        if (name is "opened" or "reward-word")
        {
            var text = Match(frame, name, name + "-current", whiteText: true)
                ?? (name == "opened" ? Match(frame, name, "opened-mutated", whiteText: true) : null)
                ?? Match(frame, name, name, whiteText: true);
            if (text.HasValue) return text;
        }
        return (name is "bag" or "search" or "opened" or "reward-word" ? Match(frame, name, name + "-current") : null)
           ?? Match(frame, name, name);
    }

    private Point? Match(Mat frame, string name, string templateName, bool whiteText = false)
    {
        string cacheKey = templateName + (whiteText ? ":white" : "");
        if (!_templates.TryGetValue(cacheKey, out var template))
        {
            string path = System.IO.Path.Combine(directory, "crate-" + templateName + ".png");
            if (!System.IO.File.Exists(path)) return null;
            using var color = Cv2.ImRead(path);
            if (color.Empty()) return null;
            template = new Mat();
            if (whiteText) Cv2.InRange(color, new Scalar(180, 180, 180), Scalar.All(255), template);
            else Cv2.CvtColor(color, template, ColorConversionCodes.BGR2GRAY);
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
        var rect = Region(frame, bounds.Item1, bounds.Item2, bounds.Item3, bounds.Item4);
        using var roi = new Mat(frame, rect); using var gray = new Mat();
        if (whiteText) Cv2.InRange(roi, new Scalar(180, 180, 180), Scalar.All(255), gray);
        else Cv2.CvtColor(roi, gray, ColorConversionCodes.BGR2GRAY);
        Cv2.GaussianBlur(gray, gray, new Size(3, 3), 0);
        double threshold = name is "one" or "search" ? .94 : .90;
        foreach (double textScale in name is "bag" or "search" or "item" or "opened" or "reward-word" ? new[] { 1.0, .7, .8, .9 } : new[] { 1.0 })
        {
            int width = Math.Max(1, (int)Math.Round(template.Width * frame.Height / 1369.0 * textScale));
            int height = Math.Max(1, (int)Math.Round(template.Height * frame.Height / 1369.0 * textScale));
            // Rasterized text can round by a pixel at a different viewport scale.
            foreach (int dw in new[] { 0, -1, 1 })
            foreach (int dh in new[] { 0, -1, 1 })
            {
                using var scaled = new Mat();
                Cv2.Resize(template, scaled, new Size(Math.Max(1, width + dw), Math.Max(1, height + dh)));
                if (gray.Width < scaled.Width || gray.Height < scaled.Height) continue;
                Cv2.GaussianBlur(scaled, scaled, new Size(3, 3), 0);
                using var scores = new Mat();
                Cv2.MatchTemplate(gray, scaled, scores, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(scores, out _, out double confidence, out _, out var point);
                if (double.IsFinite(confidence) && confidence >= threshold)
                {
                    return new Point(rect.X + point.X + scaled.Width / 2, rect.Y + point.Y + scaled.Height / 2);
                }
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

    public void Dispose() { foreach (var template in _templates.Values) template.Dispose(); }
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
        if (!Await(f => vision.Find(f, "dialog") == null && Reward(f), 8000))
            return Unknown("A new crate reward notification was not confirmed.");
        return new(ActionOutcome.ConfirmedSuccess, "Crate batch opened; new reward notification confirmed", RewardClaimed: true);
    }
}
