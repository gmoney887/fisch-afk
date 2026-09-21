using System.IO;
using System.IO.Compression;
using System.Text.Json;
using FischMacroCS.Core;
using OpenCvSharp;

namespace FischMacroCS.Tests;

public class RecordingReplayTests : IDisposable
{
    [Theory]
    [InlineData("catch_stacked_rewards.png", true)]
    [InlineData("companion_bonus_only.png", false)]
    public void ReplayCatchPredictionUsesRecordedViewportRatherThanCropHeight(string fixture, bool expected)
    {
        Directory.CreateDirectory(_root);
        string file = "frame.png";
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture), Path.Combine(_root, file));
        using var image = Cv2.ImRead(Path.Combine(_root, file));
        Assert.False(image.Empty());
        var frame = new ReplayFrame(file, 1, 0, new Rect(383, 1001, image.Width, image.Height), new Rect(0, 0, 2254, 1353));
        var prediction = new ReplaySession(_root).Predict(frame);
        Assert.Equal(expected, prediction.Catch);
    }

    private sealed class BlockedEvidence : IDisposable
    {
        internal readonly ManualResetEventSlim Entered = new();
        internal readonly ManualResetEventSlim Release = new();
        public string Value
        {
            get
            {
                Entered.Set();
                if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("Test writer was not released");
                return "writer resumed";
            }
        }
        public void Dispose() { Release.Set(); Entered.Dispose(); Release.Dispose(); }
    }

    [Fact]
    public async Task SaturatedQueueDoesNotBlockProducersOrLoseCompletionAndCanRestart()
    {
        using var recorder = new FlightRecorder(_root);
        using var evidence = new BlockedEvidence();
        recorder.StartSession(100, 80);
        string first = recorder.CurrentSessionDirectory!;
        recorder.RecordEvent("blocked-writer", evidence);
        try
        {
            Assert.True(evidence.Entered.Wait(TimeSpan.FromSeconds(5)));
            // Writer cannot consume until Release: this proves bounded, nonblocking production.
            await Task.Run(() =>
            {
                for (int i = 0; i < 4096; i++) recorder.RecordEvent("pressure", new { Sequence = i });
                recorder.StopSession("Stopped under pressure", new { TotalCatches = 7, UnknownCatches = 2 });
            }).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(recorder.IsRecording);
            Assert.False(File.Exists(Path.Combine(first, "completed.json")));
        }
        finally { evidence.Release.Set(); }
        // Restart before final disposal, allowing the old writer to drain concurrently.
        recorder.StartSession(100, 80);
        string second = recorder.CurrentSessionDirectory!;
        using (var frame = new Mat(80, 100, MatType.CV_8UC3, Scalar.White))
            recorder.RecordFrame(frame, new Rect(0, 0, 100, 80), new Rect(0, 0, 100, 80), 1);
        recorder.StopSession("Next run");
        await Task.Run(recorder.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(recorder.LastError);
        using var completed = JsonDocument.Parse(File.ReadAllText(Path.Combine(first, "completed.json")));
        Assert.Equal(4096 - 24, completed.RootElement.GetProperty("DroppedEntries").GetInt64());
        Assert.Equal("Stopped under pressure", completed.RootElement.GetProperty("Outcome").GetString());
        Assert.Equal(7, completed.RootElement.GetProperty("Statistics").GetProperty("TotalCatches").GetInt32());
        Assert.Equal(2, completed.RootElement.GetProperty("Statistics").GetProperty("UnknownCatches").GetInt32());

        Assert.NotEqual(first, second);
        Assert.Single(new ReplaySession(second).Frames);
        using var restarted = JsonDocument.Parse(File.ReadAllText(Path.Combine(second, "completed.json")));
        Assert.Equal(0, restarted.RootElement.GetProperty("DroppedEntries").GetInt64());
    }

    private sealed class Clock : IClock
    {
        public long Timestamp { get; set; }
        public double ElapsedMilliseconds(long since) => Timestamp - since;
        public void Delay(int milliseconds, CancellationToken cancellation) => Timestamp += milliseconds;
    }
    private sealed class Source(Func<Mat?> capture) : IFrameSource
    {
        public Mat? CaptureClientRegion(IntPtr window, int x, int y, int width, int height) => capture();
        public void Dispose() { }
    }
    [Fact]
    public void StaleCaptureCannotReachDetectionAndItsBufferIsReleased()
    {
        var clock = new Clock(); var raw = new Mat(10, 10, MatType.CV_8UC3);
        using var recorder = new FlightRecorder(_root);
        using var source = new RecordingFrameSource(new Source(() => { clock.Timestamp += 251; return raw; }), recorder,
            () => new Rect(0, 0, 10, 10), clock);
        Assert.Throws<GameplayInterruptedException>(() => source.CaptureClientRegion(default, 0, 0, 10, 10));
        Assert.True(raw.IsDisposed);
    }
    private readonly string _root = Path.Combine(Path.GetTempPath(), "fisch-replay-" + Guid.NewGuid().ToString("N"));
    public RecordingReplayTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Fact]
    public void JournalRotationKeepsRecentEventsAndReportsExpiredEntries()
    {
        using (var journal = new BoundedEventJournal(_root, 64))
        {
            for (int i = 0; i < 100; i++) journal.WriteLine($"{{\"FrameId\":{i}}}");
            journal.Flush();
            Assert.True(journal.ExpiredEntries > 0);
        }
        var files = Directory.GetFiles(_root, "events*.jsonl");
        Assert.Equal(4, files.Length);
        Assert.All(files, file => Assert.True(new FileInfo(file).Length <= 64));
        Assert.Contains("99", File.ReadAllText(Path.Combine(_root, "events.jsonl")));
    }

    [Fact]
    public void RawCaptureAndFinalStatisticsSurviveSourceDisposalAndReplay()
    {
        string session;
        using (var recorder = new FlightRecorder(_root))
        {
            recorder.StartSession(100, 80, new { Mode = "test" }); session = recorder.CurrentSessionDirectory!;
            using (var raw = new Mat(20, 100, MatType.CV_8UC3, new Scalar(10, 20, 30)))
                recorder.RecordFrame(raw, new Rect(0, 50, 100, 20), new Rect(0, 0, 100, 80), 2);
            recorder.RecordEvent("outcome", new { Outcome = ActionOutcome.Unknown });
            recorder.RecordEvent("input", new { Action = "KeyDown:49" });
            recorder.RecordEvent("input", new { Action = "KeyUp:49" });
            recorder.StopSession("test completed", new { TotalCatches = 7, UnknownCatches = 2 });
        }
        var replay = new ReplaySession(session);
        var frame = Assert.Single(replay.Frames);
        Assert.NotEqual("unknown", replay.PcId); Assert.True(frame.Seconds >= 0);
        Assert.Equal(50, frame.Region.Y);
        using var saved = Cv2.ImRead(Path.Combine(session, frame.File));
        Assert.Equal(new Vec3b(10, 20, 30), saved.At<Vec3b>(0, 0));
        using var completed = JsonDocument.Parse(File.ReadAllText(Path.Combine(session, "completed.json")));
        Assert.Equal(7, completed.RootElement.GetProperty("Statistics").GetProperty("TotalCatches").GetInt32());
        Assert.Equal(0, completed.RootElement.GetProperty("IncidentJournalEntriesExpired").GetInt64());
        string incidentText = File.ReadAllText(Path.Combine(session, "incidents.jsonl"));
        Assert.Contains("KeyDown:49", incidentText);
        Assert.Contains("KeyUp:49", incidentText);
        Assert.Contains("Unknown", incidentText);
        Assert.Contains("completed", incidentText);
        var label = new FrameLabel("Reeling", "Unknown", "needle", 5, 5, 2, 5);
        replay.SaveLabel(frame, label); Assert.Equal(label, replay.LoadLabel(frame));
    }

    [Fact]
    public void SelectionAndMaskMatchExportAndRetryUsesFrozenZip()
    {
        string session = Path.Combine(_root, "session_test"); Directory.CreateDirectory(session);
        File.WriteAllText(Path.Combine(session, "summary.txt"), "Session Outcome: Unknown");
        for (int i = 0; i < 4; i++)
            File.WriteAllText(Path.Combine(session, i == 0 ? "incidents.jsonl" : $"incidents.{i}.jsonl"),
                $"{{\"Kind\":\"recovery-context\",\"Sequence\":{i}}}");
        using (var raw = new Mat(10, 10, MatType.CV_8UC3, Scalar.White))
        {
            Cv2.ImWrite(Path.Combine(session, "frame_00000001.png"), raw);
            Cv2.ImWrite(Path.Combine(session, "frame_00000002.png"), raw);
        }
        var media = new ReportMediaOptions(["frame_00000001.png"], 0, 0, 5, 5);
        var package = RecordingSubmissionService.CreateDiagnosticBundle(session, media: media);
        Assert.True(package.Success, package.ErrorMessage);
        byte[] original = File.ReadAllBytes(package.ZipPath);
        using (var zip = ZipFile.OpenRead(package.ZipPath))
        {
            for (int i = 0; i < 4; i++)
            {
                string name = i == 0 ? "incidents.jsonl" : $"incidents.{i}.jsonl";
                using var reader = new StreamReader(Assert.IsType<ZipArchiveEntry>(zip.GetEntry(name)).Open());
                Assert.Contains($"\"Sequence\":{i}", reader.ReadToEnd());
            }
            Assert.Null(zip.GetEntry("frame_00000002.png"));
            using var memory = new MemoryStream(); zip.GetEntry("frame_00000001.png")!.Open().CopyTo(memory);
            using var image = Cv2.ImDecode(memory.ToArray(), ImreadModes.Color);
            Assert.Equal(new Vec3b(0, 0, 0), image.At<Vec3b>(0, 0));
            Assert.Equal(new Vec3b(255, 255, 255), image.At<Vec3b>(9, 9));
        }
        File.WriteAllText(Path.Combine(session, "summary.txt"), "Changed later");
        var retry = RecordingSubmissionService.CreateDiagnosticBundle(session, media: media);
        Assert.Equal(package.ZipPath, retry.ZipPath);
        Assert.Equal(original, File.ReadAllBytes(retry.ZipPath));
    }
}
