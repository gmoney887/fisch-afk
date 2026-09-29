using FischMacroCS.Vision;
using OpenCvSharp;

namespace FischMacroCS.Core;

public sealed record WorkflowTarget(string Name, double XFromCenterInHeights, double YInHeights, double RadiusInHeights,
    int ReferenceHeight = 1080, double MinimumConfidence = .96, bool Smooth = false,
    string? AlternateTemplate = null, int AlternateReferenceHeight = 0, bool BlueText = false, bool SearchNearbyScales = false, bool RewardBalanceText = false, bool RedGlyph = false);
public sealed record WorkflowStep(string Name, WorkflowTarget Prerequisite, WorkflowTarget Expected,
    Action<Point> Act, int TimeoutMs = 3000, int Attempts = 1, bool ExpectedPresent = true);
public sealed record WorkflowResult(ActionOutcome Outcome, string Evidence, bool RewardClaimed = false, bool RetryableWithoutRecovery = false);

public interface IWorkflowVision
{
    (bool Found, Point Center, double Confidence) Find(Mat frame, WorkflowTarget target);
}

/// <summary>Only versioned, developer-reviewed templates are trusted as workflow evidence.</summary>
public sealed class TemplateWorkflowVision : IWorkflowVision, IDisposable
{
    private readonly string _directory;
    private readonly Dictionary<string, Mat> _templates = new();
    private readonly Dictionary<(string Name, int ReferenceHeight, bool Smooth, double Scale), Mat> _scaled = new();
    private int _height;
    public TemplateWorkflowVision(string directory) => _directory = directory;
    public string[] MissingTemplates(IEnumerable<string> names) => names.Distinct()
        .Where(name => !System.IO.File.Exists(System.IO.Path.Combine(_directory, name + ".png")))
        .ToArray();
    public (bool Found, Point Center, double Confidence) Find(Mat frame, WorkflowTarget target)
    {
        var primary = FindAtScales(frame, target);
        if (primary.Found || target.AlternateTemplate == null || target.AlternateReferenceHeight <= 0) return primary;
        var alternate = FindAtScales(frame, target with { Name = target.AlternateTemplate, ReferenceHeight = target.AlternateReferenceHeight });
        return alternate.Confidence > primary.Confidence ? alternate : primary;
    }
    private (bool Found, Point Center, double Confidence) FindAtScales(Mat frame, WorkflowTarget target)
    {
        var best = FindSingle(frame, target, 1);
        if (best.Found || !target.SearchNearbyScales) return best;
        foreach (double scale in new[] { .99, 1.01, .98, 1.02 })
        {
            var candidate = FindSingle(frame, target, scale);
            if (candidate.Confidence > best.Confidence) best = candidate;
            if (best.Found) break;
        }
        return best;
    }
    private (bool Found, Point Center, double Confidence) FindSingle(Mat frame, WorkflowTarget target, double scale)
    {
        string path = System.IO.Path.Combine(_directory, target.Name + ".png");
        if (!System.IO.File.Exists(path)) return (false, default, 0);
        if (!_templates.TryGetValue(target.Name, out var template))
            _templates[target.Name] = template = Cv2.ImRead(path);
        if (template.Empty()) return (false, default, 0);
        if (_height != frame.Height)
        {
            foreach (var cached in _scaled.Values) cached.Dispose();
            _scaled.Clear(); _height = frame.Height;
        }
        // Preserve the reviewed capture's reference height to avoid resampling small UI text twice.
        var scaleKey = (target.Name, target.ReferenceHeight, target.Smooth, scale);
        if (!_scaled.TryGetValue(scaleKey, out var scaled))
        {
            scaled = new Mat();
            Cv2.Resize(template, scaled, new Size(Math.Max(1, (int)Math.Round(template.Width * frame.Height / (double)target.ReferenceHeight * scale)),
                Math.Max(1, (int)Math.Round(template.Height * frame.Height / (double)target.ReferenceHeight * scale))));
            if (target.Smooth) ImageSmoothing.Apply(scaled, scaled);
            _scaled[scaleKey] = scaled;
        }
        int radius = (int)Math.Ceiling(target.RadiusInHeights * frame.Height);
        int x = frame.Width / 2 + (int)(target.XFromCenterInHeights * frame.Height);
        int y = (int)(target.YInHeights * frame.Height);
        var search = new Rect(Math.Max(0, x - radius), Math.Max(0, y - radius), 1, 1);
        search.Width = Math.Min(frame.Width, x + radius) - search.X;
        search.Height = Math.Min(frame.Height, y + radius) - search.Y;
        if (search.Width < scaled.Width || search.Height < scaled.Height || Cv2.Mean(scaled).Val0 == 0)
            return (false, default, 0);
        using var roi = new Mat(frame, search);
        using var smoothed = new Mat();
        if (target.Smooth) ImageSmoothing.Apply(roi, smoothed);
        using var score = new Mat();
        double confidence;
        Point location;
        Cv2.MatchTemplate(target.Smooth ? smoothed : roi, scaled, score, TemplateMatchModes.SqDiffNormed);
        Cv2.MinMaxLoc(score, out double minimum, out _, out location, out _);
        confidence = 1 - minimum;
        if (target.RedGlyph)
        {
            using var sceneRed = RedGlyphs(roi);
            using var templateRed = RedGlyphs(scaled);
            if (Cv2.CountNonZero(templateRed) < 5) return (false, default, 0);
            Cv2.MatchTemplate(sceneRed, templateRed, score, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(score, out _, out confidence, out _, out location);
            if (!double.IsFinite(confidence)) return (false, default, 0);
        }
        if (target.RewardBalanceText)
        {
            // Aquarium scenery and reward animations show through the panel.
            // Compare the gold cash and cyan XP glyphs, excluding that background.
            using var sceneGlyphs = BalanceGlyphs(target.Smooth ? smoothed : roi);
            using var referenceGlyphs = BalanceGlyphs(scaled);
            if (Cv2.CountNonZero(referenceGlyphs) < 5) return (false, default, 0);
            Cv2.MatchTemplate(sceneGlyphs, referenceGlyphs, score, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(score, out _, out confidence, out _, out location);
        }
        if (target.BlueText && confidence < target.MinimumConfidence)
        {
            using var gray = new Mat(); using var templateGray = new Mat();
            using var hsv = new Mat(); using var templateHsv = new Mat();
            Cv2.CvtColor(roi, hsv, ColorConversionCodes.BGR2HSV);
            Cv2.CvtColor(scaled, templateHsv, ColorConversionCodes.BGR2HSV);
            Cv2.InRange(hsv, new Scalar(85, 45, 95), new Scalar(125, 255, 255), gray);
            Cv2.InRange(templateHsv, new Scalar(85, 45, 95), new Scalar(125, 255, 255), templateGray);
            if (Cv2.CountNonZero(templateGray) < 5) return (false, default, 0);
            ImageSmoothing.Apply(gray, gray);
            ImageSmoothing.Apply(templateGray, templateGray);
            Cv2.MatchTemplate(gray, templateGray, score, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(score, out _, out confidence, out _, out location);
        }
        if (target.BlueText && confidence < target.MinimumConfidence)
        {
            // Translucent navigation can sit over scenery with the same hue as its
            // text. Local brightness contrast isolates glyphs without masking in the
            // entire blue background or reducing the recognition threshold.
            using var sceneHsv = new Mat(); using var glyphHsv = new Mat();
            using var sceneValue = new Mat(); using var glyphValue = new Mat();
            Cv2.CvtColor(target.Smooth ? smoothed : roi, sceneHsv, ColorConversionCodes.BGR2HSV);
            Cv2.CvtColor(scaled, glyphHsv, ColorConversionCodes.BGR2HSV);
            Cv2.ExtractChannel(sceneHsv, sceneValue, 2);
            Cv2.ExtractChannel(glyphHsv, glyphValue, 2);
            int kernelSize = Math.Max(3, (scaled.Height / 4) | 1);
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(kernelSize, kernelSize));
            Cv2.MorphologyEx(sceneValue, sceneValue, MorphTypes.TopHat, kernel);
            Cv2.MorphologyEx(glyphValue, glyphValue, MorphTypes.TopHat, kernel);
            Cv2.MatchTemplate(sceneValue, glyphValue, score, TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(score, out _, out double shapeConfidence, out _, out Point shapeLocation);
            if (double.IsFinite(shapeConfidence) && shapeConfidence > confidence)
            {
                using var candidate = new Mat(sceneHsv, new Rect(shapeLocation.X, shapeLocation.Y, scaled.Width, scaled.Height));
                using var blue = new Mat();
                Cv2.InRange(candidate, new Scalar(85, 45, 95), new Scalar(125, 255, 255), blue);
                if (Cv2.CountNonZero(blue) >= 5) { confidence = shapeConfidence; location = shapeLocation; }
            }
        }
        return (confidence >= target.MinimumConfidence, new Point(search.X + location.X + scaled.Width / 2,
            search.Y + location.Y + scaled.Height / 2), confidence);
    }
    private static Mat RedGlyphs(Mat image)
    {
        using var hsv = new Mat(); using var low = new Mat(); using var high = new Mat();
        Cv2.CvtColor(image, hsv, ColorConversionCodes.BGR2HSV);
        Cv2.InRange(hsv, new Scalar(0, 140, 100), new Scalar(10, 255, 255), low);
        Cv2.InRange(hsv, new Scalar(170, 140, 100), new Scalar(180, 255, 255), high);
        var mask = new Mat(); Cv2.BitwiseOr(low, high, mask);
        ImageSmoothing.Apply(mask, mask);
        return mask;
    }
    private static Mat BalanceGlyphs(Mat image)
    {
        using var hsv = new Mat();
        using var cash = new Mat();
        using var xp = new Mat();
        Cv2.CvtColor(image, hsv, ColorConversionCodes.BGR2HSV);
        Cv2.InRange(hsv, new Scalar(12, 45, 150), new Scalar(35, 255, 255), cash);
        Cv2.InRange(hsv, new Scalar(80, 45, 150), new Scalar(100, 255, 255), xp);
        var glyphs = new Mat();
        Cv2.BitwiseOr(cash, xp, glyphs);
        ImageSmoothing.Apply(glyphs, glyphs);
        return glyphs;
    }
    public void Dispose()
    {
        foreach (var mat in _scaled.Values) mat.Dispose();
        foreach (var mat in _templates.Values) mat.Dispose();
        _scaled.Clear(); _templates.Clear();
    }
}

public sealed class VerifiedWorkflow(IClock clock, IWorkflowVision vision, Func<Mat?> capture,
    Action<int> delay, Action<string>? evidence = null)
{
    private (bool Found, Point Center) Await(WorkflowTarget target, int timeoutMs, CancellationToken cancellation, bool expectedPresent = true)
    {
        long start = clock.Timestamp;
        int matches = 0;
        do
        {
            cancellation.ThrowIfCancellationRequested();
            using var frame = capture();
            if (frame == null || frame.Empty()) throw new GameplayInterruptedException("Invalid workflow capture.");
            var detected = vision.Find(frame, target);
            evidence?.Invoke($"{target.Name}: confidence={detected.Confidence:F3}; found={detected.Found}");
            matches = detected.Found == expectedPresent ? matches + 1 : 0;
            if (matches >= 2) return (true, detected.Center);
            delay(100);
        } while (clock.ElapsedMilliseconds(start) < timeoutMs);
        return (false, default);
    }
    public WorkflowResult Run(IEnumerable<WorkflowStep> steps, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var sequence = steps.ToArray();
        if (vision is TemplateWorkflowVision templates)
        {
            var missing = templates.MissingTemplates(sequence.SelectMany(step =>
                new[] { step.Prerequisite.Name, step.Expected.Name }));
            if (missing.Length > 0)
                return new(ActionOutcome.Unknown,
                    "Automation unavailable: reviewed visual templates are missing (" + string.Join(", ", missing) + ").");
        }
        foreach (var step in sequence)
        {
            bool verified = false;
            for (int attempt = 0; attempt < Math.Clamp(step.Attempts, 1, 3); attempt++)
            {
                var prerequisite = Await(step.Prerequisite, step.TimeoutMs, cancellation);
                if (!prerequisite.Found) return new(ActionOutcome.Unknown, $"{step.Name}: prerequisite not detected");
                // Reject stale success evidence before acting.
                using (var before = capture())
                {
                    if (before == null || before.Empty()) throw new GameplayInterruptedException("Invalid prerequisite capture.");
                    var latest = vision.Find(before, step.Prerequisite);
                    if (!latest.Found) return new(ActionOutcome.Unknown, $"{step.Name}: prerequisite disappeared before action");
                    prerequisite = (true, latest.Center);
                    if (vision.Find(before, step.Expected).Found == step.ExpectedPresent)
                        return new(ActionOutcome.Unknown, $"{step.Name}: result already present before action");
                }
                cancellation.ThrowIfCancellationRequested();
                step.Act(prerequisite.Center);
                if (Await(step.Expected, step.TimeoutMs, cancellation, step.ExpectedPresent).Found) { verified = true; break; }
            }
            if (!verified) return new(ActionOutcome.Unknown, $"{step.Name}: expected result not detected");
        }
        return new(ActionOutcome.ConfirmedSuccess, "All visual results confirmed");
    }
}
