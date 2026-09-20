using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace FischMacroCS.Core;

/// <summary>Bounded background logging; gameplay never waits for a file write.</summary>
public class SessionLogger : IDisposable
{
    private static readonly Lazy<SessionLogger> Shared = new(() => new SessionLogger());
    public static SessionLogger Instance => Shared.Value;
    private sealed record Entry(string? Text, TaskCompletionSource? Barrier = null);
    private readonly BlockingCollection<Entry> _queue = new(2048);
    private readonly ConcurrentQueue<string> _recentEntries = new();
    private readonly object _gate = new();
    private readonly Task _worker;
    private readonly string _logFilePath;
    private readonly long _maxBytes;
    private long _dropped;
    private bool _disposed;
    private int _writerThread;
    public long DroppedEntries => Interlocked.Read(ref _dropped);
    public string? LastError { get; private set; }
    public event Action<string>? OnLog;
    public string LogFilePath => _logFilePath;

    public SessionLogger(string? customPath = null, long maxFileSizeBytes = 10 * 1024 * 1024)
    {
        _logFilePath = customPath ?? AppDataPaths.FilePath("macro_events.log");
        _maxBytes = Math.Max(256, maxFileSizeBytes);
        _worker = Task.Factory.StartNew(WriteLoop, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public void Log(string category, string message)
    {
        string entry = $"[{DateTime.Now:HH:mm:ss.fff}] [{category.PadRight(7)}] {message}";
        _recentEntries.Enqueue(entry);
        while (_recentEntries.Count > 100 && _recentEntries.TryDequeue(out _)) { }
        lock (_gate)
        {
            if (_disposed) return;
            // Reserve queue capacity for state changes and faults, rather than input chatter.
            if ((_queue.Count >= 1792 && category is "INPUT" or "VISION") || !_queue.TryAdd(new(entry)))
                Interlocked.Increment(ref _dropped);
        }
    }

    private void WriteLoop()
    {
        _writerThread = Environment.CurrentManagedThreadId;
        StreamWriter? writer = null;
        long bytes = 0;
        long lastFlush = Environment.TickCount64;
        try
        {
            void Open()
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_logFilePath))!);
                bytes = File.Exists(_logFilePath) ? new FileInfo(_logFilePath).Length : 0;
                if (bytes >= _maxBytes)
                {
                    File.Move(_logFilePath, _logFilePath + ".old", true);
                    bytes = 0;
                }
                writer = new StreamWriter(new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false));
            }
            while (!_queue.IsCompleted)
            {
                if (!_queue.TryTake(out var entry, 200))
                { try { writer?.Flush(); } catch (Exception ex) { LastError = ex.Message; } continue; }
                try
                {
                    if (entry.Text is string text)
                    {
                        int size = Encoding.UTF8.GetByteCount(text) + Environment.NewLine.Length;
                        if (size > _maxBytes) { Interlocked.Increment(ref _dropped); continue; }
                        if (writer == null) Open();
                        if (bytes + size > _maxBytes)
                        {
                            writer!.Dispose(); writer = null;
                            File.Move(_logFilePath, _logFilePath + ".old", true);
                            Open();
                        }
                        writer!.WriteLine(text); bytes += size;
                        try { OnLog?.Invoke(text); } catch { }
                    }
                    if (entry.Barrier != null || Environment.TickCount64 - lastFlush >= 200) { writer?.Flush(); lastFlush = Environment.TickCount64; }
                }
                catch (Exception ex) { LastError = ex.Message; Interlocked.Increment(ref _dropped); }
                finally { entry.Barrier?.TrySetResult(); }
            }
        }
        finally { try { writer?.Dispose(); } catch (Exception ex) { LastError = ex.Message; } }
    }
    public void LogState(MacroState from, MacroState to, string reason = "")
    {
        string msg = $"{from} -> {to}";
        if (!string.IsNullOrEmpty(reason))
        {
            msg += $" (Reason: {reason})";
        }
        Log("STATE", msg);
    }

    public void LogInput(string action, int screenX, int screenY, int clientX = -1, int clientY = -1)
    {
        string msg = (clientX >= 0 && clientY >= 0)
            ? $"{action} | Screen=({screenX}, {screenY}) | Client=({clientX}, {clientY})"
            : $"{action} | Screen=({screenX}, {screenY})";
        Log("INPUT", msg);
    }

    public void LogVision(string feature, string details)
    {
        Log("VISION", $"{feature}: {details}");
    }

    public void LogSafety(string warning)
    {
        Log("SAFETY", warning);
    }

    public void LogWarning(string message)
    {
        Log("WARN", message);
    }

    public void LogError(string message, Exception? ex = null)
    {
        string text = ex != null ? $"{message} | Exception: {ex.GetType().Name}: {ex.Message}" : message;
        Log("ERROR", text);
    }

    public string[] GetRecentLogs()
    {
        return _recentEntries.ToArray();
    }

    public void Flush()
    {
        if (Environment.CurrentManagedThreadId == _writerThread) return;
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        while (true)
        {
            lock (_gate)
            {
                if (_disposed) return;
                if (_queue.TryAdd(new Entry(null, barrier))) break;
            }
            // Explicit drain is for report/shutdown boundaries, never the control loop.
            Thread.Yield();
        }
        barrier.Task.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _queue.CompleteAdding();
        }
        if (Environment.CurrentManagedThreadId != _writerThread) _worker.GetAwaiter().GetResult();
    }
}
