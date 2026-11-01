using FischMacroCS.Core;
using OpenCvSharp;
using System.IO;
using System.Text.Json;

namespace FischMacroCS.Tests;

public sealed class RecorderArchiveStatusTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fisch-recorder-status-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public void FullIncidentArchiveLeavesRollingFramesActiveAndRecoversAfterCapacityReturns()
    {
        string recordings = Path.Combine(_root, "recordings"), archive = Path.Combine(recordings, "incident-archive");
        string previous = Path.Combine(archive, "incident_previous");
        Directory.CreateDirectory(previous);
        File.WriteAllText(Path.Combine(previous, "incident.json"), "{\"CaptureState\":\"Complete\",\"Status\":\"Unreviewed\"}");
        using var recorder = new FlightRecorder(recordings, 25,
            new IncidentArchiveLimits(ArchiveBytes: 2 * 1024 * 1024, IncidentBytes: 1024 * 1024, Incidents: 1));
        recorder.StartSession(64, 64);
        recorder.RecordEvent("outcome", new { Outcome = "Unknown" });
        Assert.True(SpinWait.SpinUntil(() => recorder.IncidentArchiveWarning is not null, TimeSpan.FromSeconds(5)));
        Assert.True(recorder.IsRecording); Assert.Null(recorder.LastError);
        Assert.Contains("archive is full", recorder.IncidentArchiveWarning);
        using var pixels = new Mat(64, 64, MatType.CV_8UC3, Scalar.All(80));
        recorder.RecordFrame(pixels, new Rect(0, 0, 64, 64), new Rect(0, 0, 64, 64), 1);
        recorder.RecordEvent("after-archive-limit", new { });

        // Preserve the completed original, as an operator can do without
        // stopping fishing. A later successful clip clears only this warning.
        string preserved = Path.Combine(_root, "preserved-original");
        Directory.Move(previous, preserved);
        recorder.RecordEvent("outcome", new { Outcome = "Unknown" });
        Assert.True(SpinWait.SpinUntil(() => recorder.IncidentArchiveWarning is null
            && Directory.GetDirectories(archive, "incident_*").Length == 1, TimeSpan.FromSeconds(5)));
        Assert.True(recorder.IsRecording); Assert.Null(recorder.LastError);
        string session = recorder.CurrentSessionDirectory!;
        recorder.Dispose();

        var rows = File.ReadAllLines(Path.Combine(session, "events.jsonl"))
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
        var frame = Assert.Single(rows, row => row.GetProperty("Kind").GetString() == "frame");
        Assert.True(File.Exists(Path.Combine(session, frame.GetProperty("File").GetString()!)));
        Assert.Contains(rows, row => row.GetProperty("Kind").GetString() == "after-archive-limit");
        Assert.True(File.Exists(Path.Combine(preserved, "incident.json")));
        Assert.Null(recorder.LastError); Assert.Null(recorder.IncidentArchiveWarning);
    }
}
