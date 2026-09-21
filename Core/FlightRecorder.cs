using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using OpenCvSharp;

namespace FischMacroCS.Core;

/// <summary>Bounded, asynchronous raw evidence recording. Mat ownership transfers only after cloning.</summary>
public sealed class FlightRecorder : IDisposable
{
    private sealed record Entry(long Id, long Timestamp, string Kind, object Data, Mat? Frame = null, long DroppedBefore = 0);
    private readonly object _gate = new();
    private readonly string _baseDir;
    private BlockingCollection<Entry>? _queue;
    private Task? _writer;
    private long _nextId, _dropped, _lastFrame, _lastContext;
    private long _queuedBytes;
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true, Converters = { new JsonStringEnumConverter() } };
    private const long QueueByteLimit = 48L * 1024 * 1024;
    private string? _sessionDirectory;
    private readonly List<Task> _writers = new();
    private int _maxRecordings = 25;
    public bool IsRecording { get { lock (_gate) return _queue != null; } }
    public string RecordingsDirectory => _baseDir;
    public string? CurrentSessionDirectory => _sessionDirectory;
    public string? LastError { get; private set; }
    public int MaxRecordingsToKeep { get => _maxRecordings; set => _maxRecordings = Math.Max(1, value); }
    public FlightRecorder(string? customDir = null, int maxRecordings = 25)
    {
        _baseDir = customDir ?? AppDataPaths.FilePath("recordings");
        MaxRecordingsToKeep = maxRecordings;
    }
    public void StartSession(int frameWidth, int frameHeight, object? metadata = null)
    {
        StopSession();
        // Reserve the active session's worst-case frame/journal footprint before accepting it.
        EnforceRetentionPolicy(224L * 1024 * 1024);
        if (Directory.Exists(_baseDir) && StoredBytes(_baseDir) > 800L * 1024 * 1024)
        {
            LastError = "Recording storage is full. Export or remove old reports before recording again.";
            return;
        }
        lock (_gate)
        {
            _sessionDirectory = Path.Combine(_baseDir, $"session_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}");
            var queue = new BlockingCollection<Entry>(24);
            _queue = queue; _nextId = _dropped = _lastFrame = _lastContext = 0;
            LastError = null;
            string directory = _sessionDirectory;
            long started = Stopwatch.GetTimestamp();
            var manifest = new { SchemaVersion = 3, SessionId = Path.GetFileName(directory),
                PcId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.MachineName)))[..16],
                AppVersion = typeof(FlightRecorder).Assembly.GetName().Version?.ToString(),
                DetectorVersion = "2", StartedUtc = DateTime.UtcNow, MonotonicStart = started,
                TimestampFrequency = Stopwatch.Frequency, Width = frameWidth, Height = frameHeight,
                Metadata = JsonSerializer.SerializeToElement(metadata, Json), Privacy = "Raw gameplay may contain player names and chat. Review before sharing." };
            _writer = Task.Run(() => WriteSession(queue, directory, manifest, started));
            _writers.RemoveAll(t => t.IsCompleted);
            _writers.Add(_writer);
        }
    }
    public void RecordFrame(Mat frame, Rect region, Rect viewport, double captureMs)
    {
        lock (_gate)
        {
            if (_queue == null) return;
            long now = Stopwatch.GetTimestamp();
            bool context = region.Width == viewport.Width && region.Height == viewport.Height;
            long previous = context ? _lastContext : _lastFrame;
            if (previous != 0 && Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds < (context ? 1000 : 100)) return;
            if (context) _lastContext = now; else _lastFrame = now;
            long id = ++_nextId;
            long bytes = frame.Total() * frame.ElemSize();
            if (_queue.Count >= 24 || Interlocked.Read(ref _queuedBytes) + bytes > QueueByteLimit) { _dropped++; return; }
            var clone = frame.Clone();
            Interlocked.Add(ref _queuedBytes, bytes);
            if (!_queue.TryAdd(new Entry(id, now, "frame", new { Region = region, Viewport = viewport, CaptureMs = captureMs, Context = context }, clone, _dropped)))
            { clone.Dispose(); Interlocked.Add(ref _queuedBytes, -bytes); _dropped++; }
        }
    }
    public void RecordEvent(string kind, object data)
    {
        lock (_gate)
        {
            if (_queue == null) return;
            if (!_queue.TryAdd(new Entry(_nextId, Stopwatch.GetTimestamp(), kind, data, DroppedBefore: _dropped))) _dropped++;
        }
    }
    public void RecordTick(TelemetryData t, bool isMouseDown, Mat? rawFrame)
    {
        RecordEvent("decision", new { t.State, t.Action, MouseDown = isMouseDown, t.BarLeft, t.BarRight,
            t.FishX, t.BarVelocity, t.FishVelocity, t.LoopLatencyMs, t.VisionLatencyMs,
            t.TotalCatches, t.TotalFails, t.SessionUptimeSeconds });
    }
    public void StopSession(string outcome = "Unknown", object? statistics = null)
    {
        lock (_gate)
        {
            if (_queue == null) return;
            // Completion metadata is delivered separately, so queue saturation cannot drop the outcome.
            var queue = _queue;
            _queue = null;
            _completion[queue] = (outcome, _dropped, statistics, Stopwatch.GetTimestamp());
            queue.CompleteAdding();
        }
    }
    private readonly ConcurrentDictionary<BlockingCollection<Entry>, (string Outcome, long Dropped, object? Statistics, long End)> _completion = new();
    private void WriteSession(BlockingCollection<Entry> queue, string directory, object manifest, long start)
    {
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(manifest, Json));
            using var journal = new SessionEventJournal(directory);
            var frameIndex = new Dictionary<string, object>(StringComparer.Ordinal);
            int frames = 0;
            var rolling = new Queue<(string File, long Timestamp, long Bytes)>();
            var preserved = new PreservedEvidenceBuffer();
            long rollingBytes = 0;
            long preserveUntil = 0;
            long failureUntil = 0;
            int successes = 0;
            foreach (var entry in queue.GetConsumingEnumerable())
            {
                var data = JsonSerializer.SerializeToElement(entry.Data, Json);
                if (entry.Kind.Contains("outcome", StringComparison.Ordinal) || entry.Kind is "recovery-context" or "death-detected")
                {
                    bool success = data.GetRawText().Contains("ConfirmedSuccess", StringComparison.Ordinal);
                    if (!success || ++successes % 10 == 0)
                    {
                        bool failure = !success && entry.Kind != "recovery-context";
                        preserveUntil = entry.Timestamp + 5 * Stopwatch.Frequency;
                        if (failure)
                        {
                            failureUntil = preserveUntil;
                            // Preceding frames may already be in the routine sample pool.
                            preserved.PromoteSince(entry.Timestamp - 15 * Stopwatch.Frequency);
                        }
                        while (rolling.TryDequeue(out var buffered))
                        { preserved.Add(buffered.File, buffered.Timestamp, buffered.Bytes, failure); rollingBytes -= buffered.Bytes; }
                    }
                }
                using (entry.Frame)
                {
                    if (entry.Frame != null) Interlocked.Add(ref _queuedBytes, -(entry.Frame.Total() * entry.Frame.ElemSize()));
                    string? filename = entry.Frame == null ? null : $"frame_{entry.Id:D8}.png";
                    if (filename != null)
                    {
                        string path = Path.Combine(directory, filename);
                        Cv2.ImWrite(path, entry.Frame!); frames++;
                        long bytes = new FileInfo(path).Length;
                        if (entry.Timestamp <= preserveUntil) preserved.Add(path, entry.Timestamp, bytes, entry.Timestamp <= failureUntil);
                        else { rolling.Enqueue((path, entry.Timestamp, bytes)); rollingBytes += bytes; }
                        while (rolling.TryPeek(out var old) && (entry.Timestamp - old.Timestamp > 15 * Stopwatch.Frequency || rolling.Count > 165 || rollingBytes > 64L * 1024 * 1024))
                        {
                            rolling.Dequeue(); File.Delete(old.File); rollingBytes -= old.Bytes;
                            frameIndex.Remove(Path.GetFileName(old.File));
                        }
                    }
                    // Promotion events can fill the pool even if no later frame arrives.
                    foreach (string preservedOld in preserved.Trim())
                    {
                        File.Delete(preservedOld);
                        frameIndex.Remove(Path.GetFileName(preservedOld));
                    }
                    var row = new { FrameId = entry.Id, entry.Timestamp, entry.Kind, Data = data, File = filename, entry.DroppedBefore };
                    if (filename != null && File.Exists(Path.Combine(directory, filename))) frameIndex[filename] = row;
                    journal.WriteLine(entry.Kind, data, JsonSerializer.Serialize(row, Json));
                }
            }
            _completion.TryRemove(queue, out var completion);
            // The final marker itself may rotate a segment. Snapshot expiry counts afterward.
            var marker = new { Kind = "completed", completion.Outcome, completion.Statistics, DroppedEntries = completion.Dropped };
            journal.WriteLine("completed", default, JsonSerializer.Serialize(marker, Json));
            var completed = new { marker.Kind, marker.Outcome, marker.Statistics, marker.DroppedEntries,
                JournalEntriesExpired = journal.ExpiredEntries, IncidentJournalEntriesExpired = journal.IncidentExpiredEntries };
            journal.Flush();
            journal.Dispose();
            File.WriteAllText(Path.Combine(directory, "frame-index.json"), JsonSerializer.Serialize(frameIndex.Values, Json));
            File.WriteAllText(Path.Combine(directory, "completed.json"), JsonSerializer.Serialize(completed, Json));
            File.WriteAllText(Path.Combine(directory, "summary.txt"), FormattableString.Invariant(
                $"Session Outcome: {completion.Outcome}\nDuration: {Stopwatch.GetElapsedTime(start, completion.End).TotalSeconds:F2} seconds\nTotal Recorded Frames: {frames}\nDropped Entries: {completion.Dropped}\nJournal Entries Expired: {journal.ExpiredEntries}\nIncident Journal Entries Expired: {journal.IncidentExpiredEntries}\nStatistics: {JsonSerializer.Serialize(completion.Statistics, Json)}\n"));
            EnforceRetentionPolicy();
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            foreach (var entry in queue.GetConsumingEnumerable())
            {
                if (entry.Frame != null) Interlocked.Add(ref _queuedBytes, -(entry.Frame.Total() * entry.Frame.ElemSize()));
                entry.Frame?.Dispose();
            }
            _completion.TryRemove(queue, out _);
        }
        finally { queue.Dispose(); }
    }
    public static long StoredBytes(string root) => Directory.Exists(root)
        ? Directory.EnumerateFiles(root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
            .Sum(f => new FileInfo(f).Length) : 0;

    public void EnforceRetentionPolicy(long reserveBytes = 0)
    {
        try
        {
            if (!Directory.Exists(_baseDir)) return;
            var sessions = Directory.GetDirectories(_baseDir, "session_*")
                .Where(d => File.Exists(Path.Combine(d, "summary.txt")))
                .Select(d => new { Path = d, Date = Directory.GetCreationTimeUtc(d),
                    Size = Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length),
                    Marked = File.Exists(Path.Combine(d, "preserve.marker")) })
                .OrderBy(d => d.Marked).ThenBy(d => d.Date).ToList();
            // Include active sessions and frozen report exports in the same storage budget.
            long bytes = StoredBytes(_baseDir);
            int count = sessions.Count;
            foreach (var session in sessions)
            {
                if (bytes + reserveBytes <= 1024L * 1024 * 1024 && count <= _maxRecordings &&
                    (session.Marked || session.Date >= DateTime.UtcNow.AddDays(-7))) continue;
                Directory.Delete(session.Path, true); bytes -= session.Size; count--;
            }
        }
        catch (Exception ex) { LastError = ex.Message; }
    }
    public void Dispose() { StopSession("Stopped"); Task.WaitAll(_writers.ToArray()); }
}

public sealed class RecordingFrameSource(IFrameSource inner, FlightRecorder recorder, Func<Rect> viewport,
    IClock? clock = null, Action? validate = null) : IFrameSource
{
    private readonly IClock _clock = clock ?? new MonotonicClock();
    public Mat? CaptureClientRegion(IntPtr window, int x, int y, int width, int height)
    {
        validate?.Invoke();
        long start = _clock.Timestamp;
        var frame = inner.CaptureClientRegion(window, x, y, width, height);
        try
        {
            validate?.Invoke();
            double captureMs = _clock.ElapsedMilliseconds(start);
            if (frame == null || frame.Empty() || captureMs > 250)
                throw new GameplayInterruptedException("Invalid or stale capture. Resume explicitly when the game is visible.");
            recorder.RecordFrame(frame, new Rect(x, y, width, height), viewport(), captureMs);
            return frame;
        }
        catch { frame?.Dispose(); throw; }
    }
    public void Dispose() => inner.Dispose();
}
