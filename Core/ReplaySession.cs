using System.IO;
using System.Text.Json;
using FischMacroCS.Vision;
using OpenCvSharp;

namespace FischMacroCS.Core;

public sealed record ReplayFrame(string File, long FrameId, double Seconds, Rect Region, Rect Viewport);
public sealed record FrameLabel(string State, string Outcome, string Target, int X, int Y, int Width, int Height);
public sealed record ReplayPrediction(string File, bool Bar, bool Fish, int FishX, bool Catch);

public sealed class ReplaySession
{
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true };
    public string DirectoryPath { get; }
    public string SessionId { get; }
    public string PcId { get; } = "unknown";
    public List<ReplayFrame> Frames { get; } = new();
    public ReplaySession(string directory)
    {
        DirectoryPath = Path.GetFullPath(directory);
        SessionId = Path.GetFileName(DirectoryPath);
        var manifestPath = Path.Combine(directory, "manifest.json");
        long start = 0, frequency = 1;
        if (File.Exists(manifestPath))
        {
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = manifest.RootElement;
            if (root.TryGetProperty("PcId", out var pc)) PcId = pc.GetString() ?? "unknown";
            if (root.TryGetProperty("MonotonicStart", out var tick)) start = tick.GetInt64();
            if (root.TryGetProperty("TimestampFrequency", out var freq)) frequency = Math.Max(1, freq.GetInt64());
        }
        var journal = Path.Combine(directory, "events.jsonl");
        string indexPath = Path.Combine(directory, "frame-index.json");
        if (!File.Exists(journal) && !File.Exists(indexPath)) return;
        IEnumerable<string> rows;
        if (File.Exists(indexPath))
        {
            using var index = JsonDocument.Parse(File.ReadAllText(indexPath));
            rows = index.RootElement.EnumerateArray().Select(row => row.GetRawText()).ToArray();
        }
        else rows = Directory.GetFiles(directory, "events*.jsonl").OrderByDescending(Path.GetFileName)
            .SelectMany(File.ReadLines);
        foreach (string line in rows)
        {
            try
            {
                using var document = JsonDocument.Parse(line);
                var e = document.RootElement;
                if (!e.TryGetProperty("Kind", out var kind) || kind.GetString() != "frame") continue;
                string? file = e.GetProperty("File").GetString();
                if (file == null || Path.GetFileName(file) != file || !File.Exists(Path.Combine(directory, file))) continue;
                var data = e.GetProperty("Data");
                Frames.Add(new(file, e.GetProperty("FrameId").GetInt64(),
                    (e.GetProperty("Timestamp").GetInt64() - start) / (double)frequency,
                    data.GetProperty("Region").Deserialize<Rect>(Json), data.GetProperty("Viewport").Deserialize<Rect>(Json)));
            }
            catch (JsonException) { /* An interrupted final journal line is not a frame. */ }
        }
        Frames.Sort((left, right) => left.FrameId.CompareTo(right.FrameId));
    }
    public FrameLabel? LoadLabel(ReplayFrame frame)
    {
        string path = Path.Combine(DirectoryPath, frame.File + ".label.json");
        return File.Exists(path) ? JsonSerializer.Deserialize<FrameLabel>(File.ReadAllText(path)) : null;
    }
    public void SaveLabel(ReplayFrame frame, FrameLabel label) =>
        File.WriteAllText(Path.Combine(DirectoryPath, frame.File + ".label.json"), JsonSerializer.Serialize(label, Json));

    public ReplayPrediction Predict(ReplayFrame frame)
    {
        using var raw = Cv2.ImRead(Path.Combine(DirectoryPath, frame.File));
        var vision = new VisionProcessor();
        bool context = frame.Region.Width == frame.Viewport.Width && frame.Region.Height == frame.Viewport.Height;
        var detected = context ? new DetectionResult() : vision.ProcessTrack(raw, frame.Region.X, frame.Region.Y,
            frame.Viewport.Height / 1080.0, generateDebug: false);
        return new(frame.File, detected.BarFound, detected.FishFound, detected.FishX, vision.DetectCatchNotification(raw));
    }
    public void ExportFixture(ReplayFrame frame, FrameLabel label, string destination)
    {
        // PC/session provenance travels with every fixture. Never split neighboring frames independently.
        Directory.CreateDirectory(destination);
        string stem = SessionId + "_" + Path.GetFileNameWithoutExtension(frame.File);
        File.Copy(Path.Combine(DirectoryPath, frame.File), Path.Combine(destination, stem + ".png"), true);
        File.WriteAllText(Path.Combine(destination, stem + ".json"), JsonSerializer.Serialize(new
        {
            SchemaVersion = 1, SessionId, PcId, PartitionGroup = PcId == "unknown" ? SessionId : PcId,
            Frame = frame, Label = label, Prediction = Predict(frame),
            Limitation = "Perception evidence only; alternative input outcomes require live trials."
        }, Json));
    }
}
