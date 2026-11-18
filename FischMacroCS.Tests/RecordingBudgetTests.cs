using System.IO;
using FischMacroCS.Core;

namespace FischMacroCS.Tests;

public sealed class RecordingBudgetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fisch-budget-" + Guid.NewGuid().ToString("N"));
    private void Payload(string relative, long bytes)
    {
        string path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        stream.SetLength(bytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AllowedRecordingStorageCanExportSmallSessionAndIncidentReports(bool incident)
    {
        Payload("session_bulk/payload.bin", 600L * 1024 * 1024);
        Payload("incident-archive/incident_bulk/payload.bin", 450L * 1024 * 1024);
        string selected = Path.Combine(_root, incident ? "incident-archive/incident_small" : "session_small");
        Directory.CreateDirectory(selected);
        File.WriteAllText(Path.Combine(selected, "summary.txt"), "small report");
        using (var recorder = new FlightRecorder(_root))
        {
            recorder.StartSession(100, 80);
            Assert.True(recorder.IsRecording, recorder.LastError);
        }
        var result = RecordingSubmissionService.CreateDiagnosticBundle(selected);
        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(File.Exists(result.ZipPath));
    }

    [Fact]
    public void IncidentExportsStillConsumeTheSharedReportBudget()
    {
        Payload("incident-archive/exports/old.zip", 1024L * 1024 * 1024);
        string selected = Path.Combine(_root, "session_small");
        Directory.CreateDirectory(selected);
        File.WriteAllText(Path.Combine(selected, "summary.txt"), "small report");
        var result = RecordingSubmissionService.CreateDiagnosticBundle(selected);
        Assert.False(result.Success);
        Assert.Contains("budget", result.ErrorMessage);
        using var recorder = new FlightRecorder(_root);
        recorder.StartSession(100, 80);
        Assert.False(recorder.IsRecording);
        Assert.Contains("storage is full", recorder.LastError);
    }

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
