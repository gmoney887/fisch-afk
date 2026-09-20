using OpenCvSharp;

namespace FischMacroCS.Core;

public sealed record WorkflowTarget(string Name, double XFromCenterInHeights, double YInHeights, double RadiusInHeights);
public sealed record WorkflowStep(string Name, WorkflowTarget Prerequisite, WorkflowTarget Expected,
    Action<Point> Act, int TimeoutMs = 3000, int Attempts = 1, bool ExpectedPresent = true);
public sealed record WorkflowResult(ActionOutcome Outcome, string Evidence);

public interface IWorkflowVision
{
    (bool Found, Point Center, double Confidence) Find(Mat frame, WorkflowTarget target);
}

/// <summary>Only versioned, developer-reviewed templates are trusted as workflow evidence.</summary>
public sealed class TemplateWorkflowVision : IWorkflowVision, IDisposable
{
    private readonly string _directory;
    private readonly Dictionary<string, Mat> _templates = new();
    private readonly Dictionary<string, Mat> _scaled = new();
    private int _height;
    public TemplateWorkflowVision(string directory) => _directory = directory;
    public string[] MissingTemplates(IEnumerable<string> names) => names.Distinct()
        .Where(name => !System.IO.File.Exists(System.IO.Path.Combine(_directory, name + ".png")))
        .ToArray();
    public (bool Found, Point Center, double Confidence) Find(Mat frame, WorkflowTarget target)
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
        // Templates are labeled at reference height 1080 and resized to physical viewport height.
        if (!_scaled.TryGetValue(target.Name, out var scaled))
        {
            scaled = new Mat();
            Cv2.Resize(template, scaled, new Size(Math.Max(1, (int)(template.Width * frame.Height / 1080.0)),
                Math.Max(1, (int)(template.Height * frame.Height / 1080.0))));
            _scaled[target.Name] = scaled;
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
        using var score = new Mat();
        Cv2.MatchTemplate(roi, scaled, score, TemplateMatchModes.SqDiffNormed);
        Cv2.MinMaxLoc(score, out double minimum, out _, out Point location, out _);
        double confidence = 1 - minimum;
        return (confidence >= 0.96, new Point(search.X + location.X + scaled.Width / 2,
            search.Y + location.Y + scaled.Height / 2), confidence);
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
