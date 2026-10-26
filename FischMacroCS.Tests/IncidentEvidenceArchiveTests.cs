using FischMacroCS.Core;
using OpenCvSharp;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace FischMacroCS.Tests;

public sealed class IncidentEvidenceArchiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fisch-incident-" + Guid.NewGuid().ToString("N"));
    private string Session => Path.Combine(_root, "session_source");
    private string Archive => Path.Combine(_root, "incident-archive");
    public IncidentEvidenceArchiveTests() => Directory.CreateDirectory(Session);
    public void Dispose() => Directory.Delete(_root, true);
    private IncidentEvidenceArchive Create(IncidentArchiveLimits? limits = null) => new(Session,
        JsonSerializer.SerializeToElement(new { BuildVersion = "test-build", Width = 3440, Height = 1369 }), 1000, limits);
    private void Frame(IncidentEvidenceArchive archive, long at, string file, int bytes = 100)
    {
        File.WriteAllBytes(Path.Combine(Session, file), new byte[bytes]);
        var data = JsonSerializer.SerializeToElement(new
        {
            Context = false,
            Region = new { X = 955, Y = 919, Width = 1526, Height = 384 },
            Viewport = new { Width = 3440, Height = 1369 },
            CaptureMs = 12
        });
        string line = JsonSerializer.Serialize(new { FrameId = at, Timestamp = at, Kind = "frame", Data = data, File = file, DroppedBefore = 0 });
        archive.Observe(at, "frame", data, line, file);
    }
    private static void Event(IncidentEvidenceArchive archive, long at, string kind, string outcome = "Unknown")
    {
        var data = JsonSerializer.SerializeToElement(new { Outcome = outcome, Reason = "test" });
        archive.Observe(at, kind, data, JsonSerializer.Serialize(new { FrameId = at, Timestamp = at, Kind = kind, Data = data, File = (string?)null }));
    }
    private string Clip() => Assert.Single(Directory.GetDirectories(Archive, "incident_*"));

    [Fact]
    public void FreezesBoundedBeforeAndAfterWithIdentityCoordinatesInputsAndHashes()
    {
        using (var archive = Create())
        {
            Frame(archive, 0, "old.png"); Frame(archive, 1000, "before.png");
            Event(archive, 5900, "input"); Frame(archive, 6000, "near.png");
            Event(archive, 6000, "outcome", "ConfirmedFailure");
            Frame(archive, 6500, "after.png"); Frame(archive, 8800, "late.png");
            Frame(archive, 9300, "outside.png");
        }
        string clip = Clip();
        Assert.False(File.Exists(Path.Combine(clip, "old.png")));
        Assert.False(File.Exists(Path.Combine(clip, "outside.png")));
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(clip, "incident.json")));
        var root = manifest.RootElement;
        Assert.Equal("Unreviewed", root.GetProperty("Status").GetString());
        Assert.Equal("Complete", root.GetProperty("CaptureState").GetString());
        Assert.Equal("test-build", root.GetProperty("BuildVersion").GetString());
        Assert.False(root.GetProperty("Incomplete").GetBoolean());
        var frames = root.GetProperty("Frames").EnumerateArray().ToArray();
        Assert.Equal(4, frames.Length);
        foreach (var row in frames)
        {
            string path = Path.Combine(clip, row.GetProperty("File").GetString()!);
            Assert.Equal(row.GetProperty("SHA256").GetString(), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
            Assert.Equal(955, row.GetProperty("Data").GetProperty("Region").GetProperty("X").GetInt32());
        }
        Assert.Contains("input", File.ReadAllText(Path.Combine(clip, "events.jsonl")));
        Assert.True(File.Exists(Path.Combine(Archive, "catalog.json")));
    }

    [Theory]
    [InlineData("outcome", "ConfirmedSuccess", false)]
    [InlineData("outcome", "Unknown", true)]
    [InlineData("outcome", "ConfirmedFailure", true)]
    [InlineData("decision", "Unknown", false)]
    [InlineData("position-suspected", "Unknown", false)]
    [InlineData("catch-failure-evidence", "Unknown", true)]
    [InlineData("recovery-attempt", "Unknown", true)]
    [InlineData("death-detected", "Unknown", true)]
    [InlineData("diagnostics-requested", "Unknown", true)]
    public void OnlyExplicitFailureEvidenceCreatesAnIncident(string kind, string outcome, bool expected)
    {
        using (var archive = Create()) Event(archive, 1000, kind, outcome);
        Assert.Equal(expected, Directory.Exists(Archive));
    }

    [Fact]
    public void MergesRelatedTriggersAndFlushesAnInterruptedWindowAsIncomplete()
    {
        using (var archive = Create())
        {
            Frame(archive, 1000, "before.png"); Event(archive, 1100, "catch-failure-evidence");
            Event(archive, 1200, "outcome", "ConfirmedFailure"); Event(archive, 1300, "recovery-attempt");
        }
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Clip(), "incident.json")));
        Assert.Equal(3, manifest.RootElement.GetProperty("Triggers").GetArrayLength());
        Assert.Equal("SessionEnded", manifest.RootElement.GetProperty("CaptureState").GetString());
        Assert.True(manifest.RootElement.GetProperty("Incomplete").GetBoolean());
    }

    [Fact]
    public void FullArchivePreservesExistingClipsAndRejectsNewAdmissionExplicitly()
    {
        var limits = new IncidentArchiveLimits(4 * 1024 * 1024, 1024 * 1024, 1);
        using (var first = Create(limits)) { Frame(first, 0, "first.png"); Event(first, 100, "outcome"); }
        string path = Path.Combine(Clip(), "first.png"); byte[] original = File.ReadAllBytes(path);
        using var second = Create(limits);
        Event(second, 200, "recovery-attempt");
        Assert.Contains("full", second.LastError);
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Single(Directory.GetDirectories(Archive, "incident_*"));
    }

    [Fact]
    public void OversizedFramesAreAccountedForWithoutExceedingTheBudget()
    {
        var limits = new IncidentArchiveLimits(4 * 1024 * 1024, 1024 * 1024);
        using (var archive = Create(limits))
        {
            Frame(archive, 0, "small.png"); Frame(archive, 1000, "large.png", 800 * 1024);
            Event(archive, 1100, "outcome"); Frame(archive, 1300, "post.png");
        }
        string clip = Clip();
        Assert.True(File.Exists(Path.Combine(clip, "small.png")));
        Assert.False(File.Exists(Path.Combine(clip, "large.png")));
        Assert.InRange(FlightRecorder.StoredBytes(clip), 1, limits.IncidentBytes);
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(clip, "incident.json")));
        Assert.True(manifest.RootElement.GetProperty("DroppedFrames").GetInt32() > 0);
        Assert.True(manifest.RootElement.GetProperty("Incomplete").GetBoolean());
    }

    [Fact]
    public void FailureToCreateTheArchiveDoesNotEscapeIntoTheRecorder()
    {
        File.WriteAllText(Archive, "blocked");
        using var archive = Create(); Event(archive, 1000, "outcome");
        Assert.Contains("incomplete", archive.LastError);
    }

    [Fact]
    public void CaptureGapAndDroppedSourceEventsRemainVisibleInTheManifest()
    {
        using (var archive = Create())
        {
            Frame(archive, 0, "before.png"); Event(archive, 1000, "outcome");
            var data = JsonSerializer.SerializeToElement(new { Reason = "dropped" });
            archive.Observe(1500, "decision", data, "{\"Timestamp\":1500,\"Kind\":\"decision\"}", droppedBefore: 3);
            Event(archive, 10000, "decision");
        }
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Clip(), "incident.json")));
        Assert.Equal(3, manifest.RootElement.GetProperty("SourceDroppedEntriesSinceFirstSample").GetInt64());
        Assert.True(manifest.RootElement.GetProperty("Incomplete").GetBoolean());
        Assert.True(manifest.RootElement.GetProperty("CapturedThroughTimestamp").GetInt64() <
            manifest.RootElement.GetProperty("RequestedThroughTimestamp").GetInt64());
    }

    [Fact]
    public void FramePathsCannotEscapeTheSourceSession()
    {
        string outside = Path.Combine(_root, "outside.png"); File.WriteAllText(outside, "private");
        using (var archive = Create())
        {
            var data = JsonSerializer.SerializeToElement(new { Context = false });
            archive.Observe(0, "frame", data, "{}", "../outside.png");
            Event(archive, 1000, "outcome");
            Assert.Contains("Invalid incident frame path", archive.LastError);
        }
        Assert.Equal("private", File.ReadAllText(outside));
        Assert.Empty(Directory.GetFiles(Clip(), "*.png"));
    }

    [Fact]
    public void ConcurrentSessionsCannotOverbookTheArchive()
    {
        var limits = new IncidentArchiveLimits(4 * 1024 * 1024, 1024 * 1024, 1);
        using var first = Create(limits); using var second = Create(limits);
        Parallel.Invoke(() => Event(first, 1000, "outcome"), () => Event(second, 1000, "outcome"));
        Assert.Single(Directory.GetDirectories(Archive, "incident_*"));
        Assert.True(first.LastError != null || second.LastError != null);
    }

    [Fact]
    public void RoutineSessionRetentionCannotDeleteThePreservedIncident()
    {
        string original;
        using (var recorder = new FlightRecorder(_root, 1))
        {
            recorder.StartSession(100, 80); original = recorder.CurrentSessionDirectory!;
            using var frame = new Mat(80, 100, MatType.CV_8UC3, Scalar.White);
            recorder.RecordFrame(frame, new Rect(0, 0, 100, 80), new Rect(0, 0, 100, 80), 1);
            recorder.RecordEvent("outcome", new { Outcome = "ConfirmedFailure" });
        }
        string clip = Clip();
        using (var recorder = new FlightRecorder(_root, 1)) recorder.StartSession(100, 80);
        using (var recorder = new FlightRecorder(_root, 1)) recorder.StartSession(100, 80);
        Assert.False(Directory.Exists(original));
        Assert.True(File.Exists(Path.Combine(clip, "incident.json")));
        Assert.Single(Directory.GetFiles(clip, "*.png"));
        Assert.Single(new ReplaySession(clip).Frames);
        Assert.Single(IncidentFrameSelection.Read(clip));
        Assert.Contains(Archive, AppDataPaths.RecordingRoots(_root));
        var package = RecordingSubmissionService.CreateDiagnosticBundle(clip);
        Assert.True(package.Success, package.ErrorMessage);
        using var zip = System.IO.Compression.ZipFile.OpenRead(package.ZipPath);
        Assert.NotNull(zip.GetEntry("incident.json"));
        Assert.Equal("UnreviewedIncident", package.Outcome);
    }
}
