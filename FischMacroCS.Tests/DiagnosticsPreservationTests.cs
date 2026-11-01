using FischMacroCS.Core;
using OpenCvSharp;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace FischMacroCS.Tests;

public sealed class DiagnosticsPreservationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fisch-diagnostics-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    [Fact]
    public void DecisionJournalKeepsObservedGeometrySeparateFromControlEstimates()
    {
        using var recorder = new FlightRecorder(_root);
        recorder.StartSession(64, 64);
        string session = recorder.CurrentSessionDirectory!;
        using var observed = new TelemetryData
        {
            State = MacroState.Reeling, BarWidth = 200, BarLeft = 1000, BarRight = 1200,
            BarCenter = 1150, FishX = 1130, Error = -20,
            ObservedBarCenter = 1100, ObservedFishX = 1120,
            ControlBarCenter = 1150, ControlFishX = 1130, ControlBarVelocity = 35,
            ObservationAgeMs = 12.5, RodPullAccel = 550,
            CaptureLatencyMs=7,DetectionLatencyMs=4,FamilyProbeLatencyMs=0,DetectorPath="AGY",
            ObservationTimestampTicks=1234,DecisionTimestampTicks=1300
        };
        recorder.RecordTick(observed, true, null);
        using var missing = new TelemetryData
        {
            State = MacroState.Reeling, BarWidth = 200,
            ControlBarCenter = 1160, ControlFishX = 1140
        };
        recorder.RecordTick(missing, false, null);
        recorder.Dispose();

        var rows = Rows(session).Where(r => r.GetProperty("Kind").GetString() == "decision").ToArray();
        Assert.Equal(2, rows.Length);
        var first = rows[0].GetProperty("Data");
        Assert.Equal(1100, first.GetProperty("ObservedBarCenter").GetDouble());
        Assert.Equal(1120, first.GetProperty("ObservedFishX").GetInt32());
        Assert.Equal(1150, first.GetProperty("ControlBarCenter").GetDouble());
        Assert.Equal(1130, first.GetProperty("ControlFishX").GetDouble());
        Assert.Equal(20, first.GetProperty("ObservedCenterError").GetDouble());
        Assert.Equal(0.1, first.GetProperty("ObservedCenterErrorFraction").GetDouble(), 8);
        Assert.Equal(-20, first.GetProperty("Error").GetDouble());
        Assert.Equal(12.5, first.GetProperty("ObservationAgeMs").GetDouble());
        Assert.Equal(7,first.GetProperty("CaptureLatencyMs").GetDouble());
        Assert.Equal(4,first.GetProperty("DetectionLatencyMs").GetDouble());
        Assert.Equal(1234,first.GetProperty("ObservationTimestampTicks").GetInt64());
        Assert.Equal(1300,first.GetProperty("DecisionTimestampTicks").GetInt64());
        Assert.True(first.GetProperty("MouseDown").GetBoolean());
        var second = rows[1].GetProperty("Data");
        Assert.Equal(JsonValueKind.Null, second.GetProperty("ObservedBarCenter").ValueKind);
        Assert.Equal(JsonValueKind.Null, second.GetProperty("ObservedCenterError").ValueKind);
        Assert.Equal(JsonValueKind.Null, second.GetProperty("ObservedCenterErrorFraction").ValueKind);
        Assert.Equal(1160, second.GetProperty("ControlBarCenter").GetDouble());
        Assert.False(second.GetProperty("MouseDown").GetBoolean());
        Assert.Null(recorder.LastError);
    }

    [Fact]
    public void UserStopWhileReelingPreservesOriginalEvidenceWithoutCallingItACatchFailure()
    {
        using var recorder = new FlightRecorder(_root);
        recorder.StartSession(64, 64);
        string session = recorder.CurrentSessionDirectory!;
        using var pixels = new Mat(32, 64, MatType.CV_8UC3, Scalar.All(95));
        recorder.RecordFrame(pixels, new Rect(8, 24, 64, 32), new Rect(0, 0, 96, 80), 1.5);
        recorder.RecordEvent("stop-request", new { Source = "user", State = "Reeling" });
        recorder.Dispose();

        string clip = Assert.Single(Directory.GetDirectories(recorder.IncidentArchiveDirectory, "incident_*"));
        var frame = Assert.Single(Directory.GetFiles(clip, "*.png"));
        Assert.Equal(SHA256.HashData(File.ReadAllBytes(Path.Combine(session, Path.GetFileName(frame)))),
            SHA256.HashData(File.ReadAllBytes(frame)));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(clip, "incident.json")));
        var root = manifest.RootElement;
        Assert.Equal("Unreviewed", root.GetProperty("Status").GetString());
        Assert.Equal("SessionEnded", root.GetProperty("CaptureState").GetString());
        Assert.True(root.GetProperty("Incomplete").GetBoolean());
        Assert.Equal(Path.GetFileName(session), root.GetProperty("SourceSession").GetString());
        Assert.Equal("stop-request", root.GetProperty("Triggers")[0].GetProperty("Kind").GetString());
        Assert.Contains(Rows(clip), r => r.GetProperty("Kind").GetString() == "stop-request");
        Assert.DoesNotContain(Rows(clip), r => r.GetProperty("Kind").GetString() == "outcome");
        Assert.Null(recorder.LastError);
        Assert.Null(recorder.IncidentArchiveWarning);
    }

    [Theory]
    [InlineData("Luring")]
    [InlineData("Stopped")]
    public void StopOutsideReelingDoesNotCreateAnIncident(string state)
    {
        Assert.False(IncidentEvidenceArchive.IsIncident("stop-request", JsonSerializer.SerializeToElement(new { State = state })));
    }

    [Fact]
    public void UnwritableIncidentArchiveLeavesTheRealRollingWriterHealthy()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Path.Combine(_root, "incident-archive"), "preserve this existing file");
        using var recorder = new FlightRecorder(_root);
        recorder.StartSession(64, 64);
        recorder.RecordEvent("outcome", new { Outcome = "Unknown" });
        Assert.True(SpinWait.SpinUntil(() => recorder.IncidentArchiveWarning is not null, TimeSpan.FromSeconds(5)));
        Assert.True(recorder.IsRecording);
        Assert.Null(recorder.LastError);
        using var pixels = new Mat(64, 64, MatType.CV_8UC3, Scalar.All(80));
        recorder.RecordFrame(pixels, new Rect(0, 0, 64, 64), new Rect(0, 0, 64, 64), 1);
        recorder.RecordEvent("writer-still-active", new { });
        string session = recorder.CurrentSessionDirectory!;
        recorder.Dispose();
        Assert.Single(Directory.GetFiles(session, "*.png"));
        Assert.Contains(Rows(session), r => r.GetProperty("Kind").GetString() == "writer-still-active");
        Assert.Equal("preserve this existing file", File.ReadAllText(Path.Combine(_root, "incident-archive")));
        Assert.Null(recorder.LastError);
        Assert.NotNull(recorder.IncidentArchiveWarning);
    }

    private static JsonElement[] Rows(string directory) => File.ReadAllLines(Path.Combine(directory, "events.jsonl"))
        .Select(line => JsonSerializer.Deserialize<JsonElement>(line)).ToArray();
}
