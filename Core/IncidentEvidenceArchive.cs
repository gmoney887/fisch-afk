using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FischMacroCS.Core;

public sealed record IncidentArchiveLimits(long ArchiveBytes = 512L * 1024 * 1024,
    long IncidentBytes = 32L * 1024 * 1024, int Incidents = 64,
    int BeforeSeconds = 5, int AfterSeconds = 3, int EventBytes = 1024 * 1024);

/// <summary>Freezes short failure clips on the recorder writer, outside routine session retention.</summary>
public sealed class IncidentEvidenceArchive : IDisposable
{
    private const long MetadataReserve = 512 * 1024;
    private sealed record Row(long Timestamp, string Kind, JsonElement Data, string Line, string? File, long DroppedBefore);
    private static readonly object StorageGate = new();
    private readonly string _session, _root;
    private readonly JsonElement _source;
    private readonly long _frequency;
    private readonly IncidentArchiveLimits _limits;
    private readonly Action<string>? _error;
    private readonly LinkedList<Row> _before = new();
    private int _beforeBytes;
    private long _beforePressureUntil = long.MinValue;
    private Clip? _clip;
    public string? LastError { get; private set; }
    public string DirectoryPath => _root;

    public IncidentEvidenceArchive(string session, JsonElement source, long frequency,
        IncidentArchiveLimits? limits = null, Action<string>? error = null)
    {
        _session = Path.GetFullPath(session);
        _root = Path.Combine(Path.GetDirectoryName(_session)!, "incident-archive");
        _source = source.Clone(); _frequency = frequency; _limits = limits ?? new(); _error = error;
        if (frequency <= 0 || _limits.IncidentBytes < 1024 * 1024 || _limits.ArchiveBytes < _limits.IncidentBytes
            || _limits.Incidents < 1 || _limits.Incidents > 128 || _limits.BeforeSeconds is < 0 or > 30 || _limits.AfterSeconds is < 0 or > 15
            || _limits.EventBytes < 1024) throw new ArgumentOutOfRangeException(nameof(limits));
    }

    public void Observe(long timestamp, string kind, JsonElement data, string line, string? file = null, long droppedBefore = 0)
    {
        try
        {
            if (_clip != null && timestamp > _clip.Until) Finish("Complete");
            TrimBefore(timestamp);
            var row = new Row(timestamp, kind, data.Clone(), line, file, droppedBefore);
            bool incident = IsIncident(kind, data);
            if (_clip == null && incident)
            {
                lock (StorageGate)
                {
                    Directory.CreateDirectory(_root);
                    var directories = Directory.GetDirectories(_root, "incident_*");
                    long reserved = 0;
                    foreach (string directory in directories)
                    {
                        string reservation = Path.Combine(directory, "reservation.json");
                        if (File.Exists(reservation))
                        {
                            using var pending = JsonDocument.Parse(File.ReadAllText(reservation));
                            reserved += Math.Max(0, pending.RootElement.GetProperty("Bytes").GetInt64() - FlightRecorder.StoredBytes(directory));
                        }
                    }
                    if (directories.Length >= _limits.Incidents ||
                        RecordingStorageBudget.IncidentBytes(_root) + reserved + _limits.IncidentBytes + 256 * 1024 > _limits.ArchiveBytes)
                    {
                        Report("Incident archive is full; existing clips were preserved. Review/export clips in " + _root);
                        return;
                    }
                    _clip = new Clip(_root, _session, _source, timestamp, _frequency, _limits, timestamp <= _beforePressureUntil);
                    LastError = null;
                }
                _clip.Trigger(row);
                // Preserve events in time order, but admit the frames nearest the failure first.
                foreach (var prior in _before) _clip.Event(prior);
                foreach (var prior in _before.Reverse()) _clip.Frame(prior, before: true);
                _clip.Save("Capturing");
                lock (StorageGate) WriteCatalog(_root);
            }
            else if (incident) _clip?.Trigger(row);
            _clip?.Event(row);
            _clip?.Frame(row, before: false);
            _clip?.Checkpoint(timestamp);
            _before.AddLast(row);
            _beforeBytes += Encoding.UTF8.GetByteCount(line);
            TrimBefore(timestamp);
        }
        catch (Exception ex)
        {
            Report("Incident preservation incomplete: " + ex.Message);
            try { Finish("Error"); } catch { _clip = null; }
        }
    }

    private void TrimBefore(long timestamp)
    {
        while (_before.First is { } first && (timestamp - first.Value.Timestamp > _limits.BeforeSeconds * _frequency
            || _beforeBytes > _limits.EventBytes || _before.Count > 4096))
        {
            if (timestamp - first.Value.Timestamp <= _limits.BeforeSeconds * _frequency)
                _beforePressureUntil = timestamp + _limits.BeforeSeconds * _frequency;
            _beforeBytes -= Encoding.UTF8.GetByteCount(first.Value.Line);
            _before.RemoveFirst();
        }
    }

    public static bool IsIncident(string kind, JsonElement data)
    {
        // A user often stops a trial after poor centering even when the game
        // reports a successful catch. Preserve evidence, without calling it a loss.
        if (kind == "stop-request" && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("State", out var state) && state.ValueKind == JsonValueKind.String
            && state.GetString() == "Reeling") return true;
        if (kind is "catch-failure-evidence" or "recovery-attempt" or "death-detected"
            or "missing-needle-recovery" or "reel-input-retry" or "pause-outcome" or "diagnostics-requested") return true;
        return kind == "outcome" && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("Outcome", out var outcome) && outcome.ValueKind == JsonValueKind.String
            && outcome.GetString() is "ConfirmedFailure" or "Unknown";
    }

    private void Report(string message) { LastError = message; _error?.Invoke(message); }
    private void Finish(string state)
    {
        var clip = _clip;
        if (clip == null) return;
        _clip = null;
        lock (StorageGate) { clip.Finish(state); WriteCatalog(_root); }
    }
    public void Dispose() { try { Finish("SessionEnded"); } catch (Exception ex) { Report("Incident preservation incomplete: " + ex.Message); } }

    private static void WriteCatalog(string root)
    {
        var entries = Directory.GetDirectories(root, "incident_*").Order(StringComparer.Ordinal).Select(directory =>
        {
            string manifest = Path.Combine(directory, "incident.json");
            using var document = JsonDocument.Parse(File.Exists(manifest) ? File.ReadAllText(manifest) :
                "{\"CaptureState\":\"Incomplete\",\"SourceSession\":null,\"BuildVersion\":null,\"TriggerTimestamp\":0}");
            var data = document.RootElement;
            return new
            {
                Id = Path.GetFileName(directory),
                Status = "Unreviewed",
                CaptureState = data.GetProperty("CaptureState").GetString(),
                Incomplete = !data.TryGetProperty("Incomplete", out var incomplete) || incomplete.GetBoolean(),
                SourceSession = data.GetProperty("SourceSession").GetString(),
                BuildVersion = data.GetProperty("BuildVersion").GetString(),
                TriggerTimestamp = data.GetProperty("TriggerTimestamp").GetInt64(),
                Manifest = Path.GetFileName(directory) + "/incident.json"
            };
        }).ToArray();
        string temporary = Path.Combine(root, "catalog.json.tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(new { SchemaVersion = 1, Incidents = entries }));
        File.Move(temporary, Path.Combine(root, "catalog.json"), true);
    }

    private sealed class Clip
    {
        private readonly string _directory, _session;
        private readonly long _at, _frequency;
        private readonly IncidentArchiveLimits _limits;
        private readonly JsonElement _source;
        private readonly List<object> _triggers = new(), _frames = new();
        private readonly HashSet<string> _files = new(StringComparer.Ordinal);
        private readonly List<string> _events = new();
        private readonly List<JsonElement> _frameRows = new();
        private long _bytes, _lastRoi = long.MinValue, _lastCheckpoint, _lastTimestamp;
        private long _firstTimestamp = long.MaxValue, _firstDrops = -1, _lastDrops;
        private readonly bool _beforeTruncated;
        private bool _contextBefore, _contextAfter, _postFrame;
        private int _eventBytes, _droppedEvents, _missingFrames, _droppedFrames, _droppedTriggers;
        public long Until => _at + _limits.AfterSeconds * _frequency;
        public Clip(string root, string session, JsonElement source, long at, long frequency, IncidentArchiveLimits limits, bool beforeTruncated)
        {
            _session = session; _source = source; _at = at; _frequency = frequency; _limits = limits; _beforeTruncated = beforeTruncated;
            _directory = Path.Combine(root, $"incident_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}");
            string metadata = JsonSerializer.Serialize(source);
            if (Encoding.UTF8.GetByteCount(metadata) > 32768) throw new IOException("Source manifest exceeds the incident metadata limit.");
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Path.Combine(_directory, "reservation.json"), JsonSerializer.Serialize(new { Bytes = limits.IncidentBytes }));
            File.WriteAllText(Path.Combine(_directory, "manifest.json"), metadata);
            _bytes = Encoding.UTF8.GetByteCount(metadata);
        }
        public void Trigger(Row row)
        {
            if (_triggers.Count >= 32) { _droppedTriggers++; return; }
            bool truncated = Encoding.UTF8.GetByteCount(row.Data.GetRawText()) > 2048;
            _triggers.Add(new { row.Timestamp, row.Kind, Data = truncated ? (JsonElement?)null : row.Data, DataTruncated = truncated });
        }
        public void Event(Row row)
        {
            _lastTimestamp = Math.Max(_lastTimestamp, row.Timestamp);
            _firstTimestamp = Math.Min(_firstTimestamp, row.Timestamp);
            if (_firstDrops < 0) _firstDrops = row.DroppedBefore;
            _lastDrops = Math.Max(_lastDrops, row.DroppedBefore);
            int bytes = Encoding.UTF8.GetByteCount(row.Line) + 1;
            if (_events.Count >= 4096 || _eventBytes + bytes > _limits.EventBytes
                || _bytes + _eventBytes + bytes > _limits.IncidentBytes - MetadataReserve) { _droppedEvents++; return; }
            _events.Add(row.Line); _eventBytes += bytes;
        }
        public void Frame(Row row, bool before)
        {
            if (row.File == null || _files.Contains(row.File)) return;
            if (Path.GetFileName(row.File) != row.File || Path.IsPathRooted(row.File)
                || row.File.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) throw new IOException("Invalid incident frame path.");
            if (Encoding.UTF8.GetByteCount(row.Data.GetRawText()) > 2048) { _droppedFrames++; return; }
            bool context = row.Data.ValueKind == JsonValueKind.Object && row.Data.TryGetProperty("Context", out var c) && c.GetBoolean();
            if (context ? before ? _contextBefore : _contextAfter : _lastRoi != long.MinValue && Math.Abs(row.Timestamp - _lastRoi) < _frequency / 4) return;
            string source = Path.Combine(_session, row.File);
            if (!File.Exists(source)) { _missingFrames++; return; }
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new IOException("Linked incident frame paths are not allowed.");
            long length = new FileInfo(source).Length;
            long limit = before ? _limits.IncidentBytes * 2 / 3 : _limits.IncidentBytes;
            if (_frames.Count >= 48 || _bytes + _eventBytes + length > limit - MetadataReserve) { _droppedFrames++; return; }
            using var input = File.OpenRead(source);
            string sha = Convert.ToHexString(SHA256.HashData(input));
            input.Position = 0;
            using (var output = new FileStream(Path.Combine(_directory, row.File), FileMode.CreateNew, FileAccess.Write)) input.CopyTo(output);
            _bytes += length; _files.Add(row.File);
            if (row.Timestamp > _at) _postFrame = true;
            if (context) { if (before) _contextBefore = true; else _contextAfter = true; } else _lastRoi = row.Timestamp;
            using var entry = JsonDocument.Parse(row.Line);
            _frameRows.Add(entry.RootElement.Clone());
            _frames.Add(new { row.Timestamp, File = row.File, SHA256 = sha, Bytes = length, row.Data });
        }
        public void Save(string state)
        {
            lock (StorageGate)
            {
                string? build = _source.TryGetProperty("BuildVersion", out var value) ? value.GetString() : null;
                if (build?.Length > 512) build = build[..512];
                var manifest = new
                {
                    SchemaVersion = 1,
                    IncidentId = Path.GetFileName(_directory),
                    Status = "Unreviewed",
                    CaptureState = state,
                    SourceSession = Path.GetFileName(_session),
                    BuildVersion = build,
                    TriggerTimestamp = _at,
                    TimestampFrequency = _frequency,
                    _limits.BeforeSeconds,
                    _limits.AfterSeconds,
                    FrameSamplingIntervalMs = 250,
                    Triggers = _triggers,
                    Frames = _frames,
                    CapturedThroughTimestamp = _lastTimestamp,
                    RequestedThroughTimestamp = Until,
                    FirstSampleTimestamp = _firstTimestamp,
                    HasPostFrames = _postFrame,
                    BeforeWindowTruncated = _beforeTruncated,
                    SourceDroppedEntriesSinceFirstSample = _lastDrops - Math.Max(0, _firstDrops),
                    EventCount = _events.Count,
                    DroppedEvents = _droppedEvents,
                    DroppedFrames = _droppedFrames,
                    MissingFrames = _missingFrames,
                    DroppedTriggers = _droppedTriggers,
                    Incomplete = state != "Complete" || _lastTimestamp < Until - _frequency / 2
                        || _frames.Count == 0 || (_limits.AfterSeconds > 0 && !_postFrame)
                        || _firstTimestamp > _at - _limits.BeforeSeconds * _frequency + _frequency / 2 || _beforeTruncated || _lastDrops > _firstDrops
                        || _droppedEvents > 0 || _droppedFrames > 0 || _missingFrames > 0 || _droppedTriggers > 0,
                    Privacy = "Private gameplay evidence. Review before sharing; this is not a passing regression."
                };
                string temporary = Path.Combine(_directory, "incident.json.tmp");
                File.WriteAllText(temporary, JsonSerializer.Serialize(manifest));
                File.Move(temporary, Path.Combine(_directory, "incident.json"), true);
                File.WriteAllLines(Path.Combine(_directory, "events.jsonl"), _events);
                File.WriteAllText(Path.Combine(_directory, "frame-index.json"), JsonSerializer.Serialize(_frameRows.OrderBy(r => r.GetProperty("Timestamp").GetInt64())));
            }
        }
        public void Checkpoint(long timestamp)
        {
            if (timestamp - _lastCheckpoint < _frequency / 2) return;
            Save("Capturing"); _lastCheckpoint = timestamp;
        }
        public void Finish(string state)
        {
            Save(state);
            File.WriteAllText(Path.Combine(_directory, "first-incident.json"), JsonSerializer.Serialize(new
            {
                Timestamp = _at,
                TimestampFrequency = _frequency,
                _limits.BeforeSeconds,
                _limits.AfterSeconds
            }));
            File.WriteAllText(Path.Combine(_directory, "completed.json"), JsonSerializer.Serialize(new
            {
                Kind = "incident-completed",
                Outcome = "UnreviewedIncident",
                CaptureState = state
            }));
            File.WriteAllText(Path.Combine(_directory, "summary.txt"),
                $"Session Outcome: UnreviewedIncident\nSource Session: {Path.GetFileName(_session)}\nCapture: {state}\n" +
                $"Preserved frames: {_frames.Count}; missing: {_missingFrames}; budget-limited: {_droppedFrames}\n" +
                "Private evidence for review. No successful regression or catch is implied.\n");
            File.Delete(Path.Combine(_directory, "reservation.json"));
        }
    }
}
