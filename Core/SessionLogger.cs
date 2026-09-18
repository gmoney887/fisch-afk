using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;

namespace FischMacroCS.Core;

/// <summary>
/// High-frequency, thread-safe session event journal for Fisch AFK Pro.
/// Records millisecond-precision state transitions, computer vision detections,
/// hardware inputs, and recovery diagnostics to 'macro_events.log'.
/// </summary>
public class SessionLogger : IDisposable
{
    private static SessionLogger? _instance;
    private static readonly object _lock = new();

    public static SessionLogger Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_lock)
                {
                    _instance ??= new SessionLogger();
                }
            }
            return _instance;
        }
    }

    private readonly string _logFilePath;
    private readonly object _fileLock = new();
    private StreamWriter? _writer;
    private readonly long _maxFileSizeBytes = 10 * 1024 * 1024; // 10 MB limit
    private readonly ConcurrentQueue<string> _recentEntries = new();
    private const int MaxRecentEntries = 100;

    public event Action<string>? OnLog;
    public string LogFilePath => _logFilePath;

    public SessionLogger(string? customPath = null)
    {
        _logFilePath = customPath ?? AppDataPaths.FilePath("macro_events.log");
        InitializeWriter();
    }

    private void InitializeWriter()
    {
        lock (_fileLock)
        {
            try
            {
                // Check for rollover
                if (File.Exists(_logFilePath))
                {
                    var fi = new FileInfo(_logFilePath);
                    if (fi.Length > _maxFileSizeBytes)
                    {
                        string oldPath = _logFilePath + ".old";
                        if (File.Exists(oldPath)) File.Delete(oldPath);
                        File.Move(_logFilePath, oldPath);
                    }
                }

                var fs = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(fs, Encoding.UTF8) { AutoFlush = true };
                _writer.WriteLine($"=== Session Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} (PID: {Environment.ProcessId}) ===");
            }
            catch
            {
                _writer = null;
            }
        }
    }

    public void Log(string category, string message)
    {
        string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        string entry = $"[{timestamp}] [{category.PadRight(7)}] {message}";

        // Enqueue to recent in-memory buffer
        _recentEntries.Enqueue(entry);
        while (_recentEntries.Count > MaxRecentEntries && _recentEntries.TryDequeue(out _)) { }

        // Write to log file
        lock (_fileLock)
        {
            try
            {
                _writer?.WriteLine(entry);
            }
            catch { }
        }

        try
        {
            OnLog?.Invoke(entry);
        }
        catch { }
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
        lock (_fileLock)
        {
            try { _writer?.Flush(); } catch { }
        }
    }

    public void Dispose()
    {
        lock (_fileLock)
        {
            try
            {
                _writer?.WriteLine($"=== Session Closed at {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ===\n");
                _writer?.Flush();
                _writer?.Dispose();
                _writer = null;
            }
            catch { }
        }
    }
}
