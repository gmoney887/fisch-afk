using System.IO;
using System.Text.RegularExpressions;
using FischMacroCS.Core;
using OpenCvSharp;

namespace FischMacroCS.Vision;

internal sealed class AppraisalVision : IDisposable
{
    private readonly Func<Rect, Mat> _capture;
    private readonly int _width, _height;
    private readonly string _item, _mutation;
    private readonly CancellationToken _cancellation;
    private readonly AppraisalTextReader _reader = new();
    private readonly Dictionary<string, Mat> _templates = new();
    private readonly Dictionary<(string, int), Mat> _scaled = new();
    private readonly Dictionary<string, int> _lastWidth = new();

    internal AppraisalVision(int width, int height, string item, string mutation, Func<Rect, Mat> capture, CancellationToken cancellation)
    {
        _width = width; _height = height; _item = item; _mutation = mutation;
        _capture = capture; _cancellation = cancellation;
        foreach (string name in new[] { "again", "again-large", "held-driftwood", "held-driftwood-mutated", "poisoned", "weight" })
        {
            using var stream = typeof(AppraisalVision).Assembly.GetManifestResourceStream(
                $"FischMacroCS.Assets.Workflows.appraise-{name}.png") ?? throw new InvalidOperationException("Missing appraisal template: " + name);
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            using var color = Cv2.ImDecode(bytes.ToArray(), ImreadModes.Color);
            _templates[name] = Mask(color, name);
        }
    }

    private static Mat Mask(Mat image, string name)
    {
        using var hsv = new Mat();
        Cv2.CvtColor(image, hsv, ColorConversionCodes.BGR2HSV);
        var result = new Mat();
        if (name.StartsWith("held-driftwood", StringComparison.Ordinal))
            Cv2.InRange(hsv, new Scalar(0, 0, 114), new Scalar(180, 72, 174), result);
        else if (name == "poisoned")
            Cv2.InRange(hsv, new Scalar(125, 96, 65), new Scalar(143, 158, 178), result);
        else
            Cv2.InRange(hsv, new Scalar(0, 0, 180), new Scalar(180, 68, 255), result);
        return result;
    }

    private Rect? Match(Mat region, string name)
    {
        using var mask = Mask(region, name);
        var source = _templates[name];
        // Roblox dialogue scales with viewport size. Backpack and notice text can also retain native sizing.
        var widths = new List<int>();
        if (_lastWidth.TryGetValue(name, out int last)) widths.Add(last);
        widths.Add(source.Width);
        int referenceHeight = name is "again-large" or "held-driftwood-mutated" or "poisoned" ? 1369 : 600;
        foreach (double factor in new[] { 1.0, .98, 1.02, .95, 1.05, .90, 1.10, .85, 1.15, .80, 1.20, .75, 1.25 })
            widths.Add((int)Math.Round(source.Width * _height / referenceHeight * factor));
        Rect? best = null;
        double bestScore = .73;
        foreach (int w in widths.Distinct())
        {
            int h = (int)Math.Round(source.Height * w / (double)source.Width);
            if (w < 5 || h < 4 || w > region.Width || h > region.Height) continue;
            if (!_scaled.TryGetValue((name, w), out var template))
            {
                template = new Mat();
                Cv2.Resize(source, template, new Size(w, h));
                _scaled[(name, w)] = template;
            }
            using var scores = new Mat();
            Cv2.MatchTemplate(mask, template, scores, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(scores, out _, out double score, out _, out var location);
            if (score <= bestScore) continue;
            bestScore = score;
            best = new Rect(location, new Size(w, h));
            if (name != "weight" || score > .98) break;
        }
        if (best.HasValue) _lastWidth[name] = best.Value.Width;
        return best;
    }

    private Rect Clip(int x, int y, int width, int height) =>
        new Rect(x, y, width, height) & new Rect(0, 0, _width, _height);
    private int H(double fraction) => (int)Math.Round(_height * fraction);

    private static readonly Regex CashAmount = new(@"(?<![\d,])(?<value>\d{1,3}(?:,\d{3})+|\d+)\s*C\s*[$S]", RegexOptions.IgnoreCase);
    private static long? Amount(string text)
    {
        var match = CashAmount.Match(text);
        return match.Success && long.TryParse(match.Groups["value"].Value.Replace(",", ""), out long value) ? value : null;
    }

    internal AppraisalBalance? Balance()
    {
        var box = Clip(_width - H(.51), H(.915), H(.51), _height - H(.915));
        using var frame = _capture(box);
        using var hsv = new Mat();
        using var cash = new Mat();
        Cv2.CvtColor(frame, hsv, ColorConversionCodes.BGR2HSV);
        // The HUD's cash ink is cream (255,253,228). Keep solid strokes independent
        // of the moving game world behind the translucent HUD.
        Cv2.InRange(hsv, new Scalar(15, 10, 240), new Scalar(42, 55, 255), cash);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(Math.Max(9, H(.019)), 3));
        using var joined = new Mat();
        Cv2.MorphologyEx(cash, joined, MorphTypes.Close, kernel);
        Cv2.FindContours(joined, out Point[][] groups, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        var row = groups.Select(Cv2.BoundingRect).Where(r => r.Width >= 3 * r.Height && r.Height >= H(.009)
            && frame.Width - r.Right < H(.08)).OrderByDescending(r => r.Bottom).FirstOrDefault();
        if (row.Width < 4) return null;
        using var glyphs = new Mat(cash, row);
        int inkCount = Cv2.CountNonZero(glyphs);
        if (inkCount < 20) return null;
        using var fixedPosition = new Mat(frame.Size(), MatType.CV_8UC1, Scalar.All(0));
        using (var target = new Mat(fixedPosition, row)) glyphs.CopyTo(target);
        fixedPosition.GetArray(out byte[] pixels);
        return new AppraisalBalance(fixedPosition.Width, pixels, inkCount);
    }
    internal AppraisalQuote? Choice()
    {
        var box = Clip(_width / 2 - H(.04), H(.425), H(.98), H(.11));
        using var frame = _capture(box);
        foreach (var line in _reader.Read(frame, Math.Clamp(1800.0 / _height, 1, 3), _cancellation))
        {
            string text = string.Join(" ", line.Select(w => w.Text));
            if (!text.Contains("Can you app", StringComparison.OrdinalIgnoreCase) ||
                !text.Contains("it again", StringComparison.OrdinalIgnoreCase) || Amount(text) is not long cost || cost <= 0)
                continue;
            var word = line.FirstOrDefault(w => w.Text.Equals("you", StringComparison.OrdinalIgnoreCase));
            Rect? phrase = word?.Bounds ?? Match(frame, "again") ?? Match(frame, "again-large");
            if (phrase is not Rect r) continue;
            using var glyph = new Mat(frame, r);
            var snap = new VisionProcessor().DynamicUISnapWithStatus(glyph, r.Width / 2, r.Height / 2,
                VisionProcessor.UIColorType.WhiteText, r.Height);
            if (snap.Found) return new AppraisalQuote(new Point(box.X + r.X + snap.Pt.X, box.Y + r.Y + snap.Pt.Y), cost);
        }
        return null;
    }
    private bool Driftwood => _item.Equals("Driftwood", StringComparison.OrdinalIgnoreCase);

    internal AppraisalResult Held()
    {
        var box = Clip(_width / 2 - H(.38), H(.48), H(.76), H(.30));
        using var frame = _capture(box);
        Rect? baseName = null;
        bool matchedCrop = Driftwood && (baseName = Match(frame, "held-driftwood")).HasValue;
        if (Driftwood && !matchedCrop)
            matchedCrop = (baseName = Match(frame, "held-driftwood-mutated")).HasValue;
        if (!matchedCrop)
        {
            foreach (var line in _reader.Read(frame, 1.5, _cancellation))
            {
                if (!string.Join(" ", line.Select(w => w.Text)).Equals(_item, StringComparison.OrdinalIgnoreCase)) continue;
                var bounds = line[0].Bounds;
                foreach (var word in line.Skip(1)) bounds |= word.Bounds;
                if (Math.Abs(box.X + bounds.X + bounds.Width / 2 - _width / 2) <= bounds.Height * .5)
                    baseName = bounds;
            }
        }
        if (baseName is not Rect name || Math.Abs(box.X + name.X + name.Width / 2 - _width / 2) > name.Height * .65)
            return new(AppraisalReading.Unreadable);

        var above = new Rect(0, Math.Max(0, name.Top - H(.085)), frame.Width, Math.Min(name.Top, H(.085)));
        if (above.Height > 0)
        {
            using var modifierRow = new Mat(frame, above);
            if (Match(modifierRow, "poisoned") is Rect poison &&
                Math.Abs(poison.X + poison.Width / 2 - (name.X + name.Width / 2)) < poison.Height * .5)
                return AppraisalWorkflow.Identify("Poisoned", _item, _mutation);
        }

        // Fisch fits the name and modifiers into one billboard. With a modifier the base name
        // shrinks, while the separate Kg line keeps its scale. Measure both on the same frame.
        int weightWidth = Math.Min(name.Width, H(.18));
        var weightBox = new Rect(name.X + (name.Width - weightWidth) / 2, name.Bottom, weightWidth, name.Height * 4)
            & new Rect(0, 0, frame.Width, frame.Height);
        if (weightBox.Height < 4 || weightBox.Width < 4) return new(AppraisalReading.Unreadable);
        using var weightRegion = new Mat(frame, weightBox);
        if (Match(weightRegion, "weight") is not Rect kg) return new(AppraisalReading.Unreadable);

        double scale = kg.Width / (double)_templates["weight"].Width;
        // The Driftwood reference is a lower-half crop. OCR returns the full glyph height.
        double textScale = matchedCrop ? name.Width / (double)_templates["held-driftwood"].Width : name.Height / 31.0;
        var modifierBox = new Rect(frame.Width / 2 - (int)(135 * scale), name.Top - (int)((matchedCrop ? 33 : 26) * scale),
            (int)(270 * scale), (int)(27 * scale)) & new Rect(0, 0, frame.Width, frame.Height);
        if (modifierBox.Width < 3 || modifierBox.Height < 3) return new(AppraisalReading.Unreadable);
        using var strip = new Mat(frame, modifierBox);
        var modifier = ReadModifier(strip, scale);
        if (modifier.Reading == AppraisalReading.Target) return modifier;
        if (textScale / scale is >= .94 and <= 1.07)
            return new(AppraisalReading.Other, _item);
        if (textScale / scale < .94)
        {
            if (modifier.Reading == AppraisalReading.Other) return modifier;
            // Coloured mutation text can sit underneath the white NPC dialogue. Isolate
            // saturated ink before asking Windows to read the same label a second time.
            using var hsv = new Mat();
            using var ink = new Mat();
            using var isolated = new Mat();
            Cv2.CvtColor(strip, hsv, ColorConversionCodes.BGR2HSV);
            Cv2.InRange(hsv, new Scalar(0, 145, 58), new Scalar(180, 255, 255), ink);
            Cv2.BitwiseNot(ink, ink);
            Cv2.CvtColor(ink, isolated, ColorConversionCodes.GRAY2BGR);
            return ReadModifier(isolated, scale);
        }
        return new(AppraisalReading.Unreadable);
    }

    private AppraisalResult ReadModifier(Mat strip, double scale)
    {
        foreach (var line in _reader.Read(strip, Math.Clamp(2 / scale, 1, 3), _cancellation))
        {
            var center = (line[0].Bounds.Left + line[^1].Bounds.Right) / 2;
            if (Math.Abs(center - strip.Width / 2) > 9 * scale) continue;
            var reading = AppraisalWorkflow.Identify(string.Join(" ", line.Select(w => w.Text)), _item, _mutation);
            if (reading.Reading is AppraisalReading.Target or AppraisalReading.Other)
                return reading;
        }
        return new(AppraisalReading.Unreadable);
    }
    public void Dispose()
    {
        foreach (var mat in _templates.Values.Concat(_scaled.Values)) mat.Dispose();
    }
}
