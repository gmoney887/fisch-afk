using System.IO;
using System.IO.Compression;
using System.Text.Json;
using FischMacroCS.Core;
using OpenCvSharp;

namespace FischMacroCS.Tests;

public class RecordingReplayTests : IDisposable
{
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
        var label = new FrameLabel("Reeling", "Unknown", "needle", 5, 5, 2, 5);
        replay.SaveLabel(frame, label); Assert.Equal(label, replay.LoadLabel(frame));
    }

    [Fact]
    public void SelectionAndMaskMatchExportAndRetryUsesFrozenZip()
    {
        string session = Path.Combine(_root, "session_test"); Directory.CreateDirectory(session);
        File.WriteAllText(Path.Combine(session, "summary.txt"), "Session Outcome: Unknown");
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
