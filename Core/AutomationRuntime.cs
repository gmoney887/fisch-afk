using System.Collections.Concurrent;
using System.Diagnostics;
using OpenCvSharp;

namespace FischMacroCS.Core;

public interface IClock
{
    long Timestamp { get; }
    double ElapsedMilliseconds(long since);
    void Delay(int milliseconds, CancellationToken cancellation);
}

public sealed class MonotonicClock : IClock
{
    public long Timestamp => Stopwatch.GetTimestamp();
    public double ElapsedMilliseconds(long since) => Stopwatch.GetElapsedTime(since).TotalMilliseconds;
    public void Delay(int milliseconds, CancellationToken cancellation)
    {
        if (cancellation.WaitHandle.WaitOne(Math.Max(0, milliseconds))) cancellation.ThrowIfCancellationRequested();
    }
}

public interface IFrameSource : IDisposable
{
    Mat? CaptureClientRegion(IntPtr window, int x, int y, int width, int height);
}

public enum ActionOutcome { Unknown, ConfirmedSuccess, ConfirmedFailure }
public sealed record Observation(long FrameId, long Timestamp, Rect Viewport, Rect Region,
    string Detector, double Confidence, string Evidence);

/// <summary>Absence of a success banner is not evidence of failure.</summary>
public sealed class CatchOutcomeTracker
{
    private bool _success;
    private bool _failure;
    public bool IsFinalized { get; private set; }
    public ActionOutcome Outcome { get; private set; }
    public void Observe(bool success, bool failure = false)
    {
        if (IsFinalized) return;
        _success |= success;
        _failure |= failure;
    }
    public ActionOutcome? FinalizeOnce(bool windowExpired)
    {
        if (IsFinalized || !windowExpired) return null;
        IsFinalized = true;
        return Outcome = _success == _failure ? ActionOutcome.Unknown :
            _success ? ActionOutcome.ConfirmedSuccess : ActionOutcome.ConfirmedFailure;
    }
}

/// <summary>A recovery incident ends only on verified progress, not on a completed retry.</summary>
public sealed class RecoveryBudget
{
    public int Attempts { get; private set; }
    public bool TryBegin() { if (Attempts >= 3) return false; Attempts++; return true; }
    public void ConfirmProgress() => Attempts = 0;
}

/// <summary>One owner for all workflows. Nested calls stay on the owner thread.</summary>
public sealed class AutomationCoordinator : IDisposable
{
    private readonly BlockingCollection<Action> _requests = new(32);
    private readonly Thread _thread;
    private readonly object _lifetime = new();
    private bool _disposed;
    private int _generation;
    private int _executingGeneration;
    public void ThrowIfCancelled()
    {
        if (IsOwner && _executingGeneration != Volatile.Read(ref _generation))
            throw new OperationCanceledException("Workflow was stopped.");
    }
    public void CancelPending() => Interlocked.Increment(ref _generation);
    public bool IsOwner => Thread.CurrentThread == _thread;
    public AutomationCoordinator()
    {
        _thread = new Thread(() =>
        {
            try { foreach (var request in _requests.GetConsumingEnumerable()) request(); }
            finally { _requests.Dispose(); }
        })
            { IsBackground = true, Name = "Fisch workflow coordinator" };
        _thread.Start();
    }
    public Task<T> Enqueue<T>(Func<T> operation)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        int generation = Volatile.Read(ref _generation);
        void Run()
        {
            if (generation != Volatile.Read(ref _generation)) { result.TrySetCanceled(); return; }
            int previous = _executingGeneration;
            _executingGeneration = generation;
            try { result.TrySetResult(operation()); }
            catch (OperationCanceledException) { result.TrySetCanceled(); }
            catch (Exception ex) { result.TrySetException(ex); }
            finally { _executingGeneration = previous; }
        }
        lock (_lifetime)
        {
            if (_disposed) result.TrySetException(new ObjectDisposedException(nameof(AutomationCoordinator)));
            else if (!IsOwner && !_requests.TryAdd(Run)) result.TrySetException(new InvalidOperationException("Workflow queue is full."));
        }
        if (IsOwner && !result.Task.IsCompleted) Run();
        return result.Task;
    }
    public void DrainPending()
    {
        if (!IsOwner) throw new InvalidOperationException("Only the coordinator may drain requests.");
        // Bound work per boundary so repeated hotkeys cannot starve fishing.
        for (int i = 0; i < 32 && _requests.TryTake(out var request); i++) request();
    }
    public void Dispose()
    {
        lock (_lifetime)
        {
            if (_disposed) return;
            _disposed = true;
            CancelPending();
            _requests.CompleteAdding();
        }
        // The consumer owns queue disposal; disposing from its own operation is supported.
        if (!IsOwner) _thread.Join();
    }
}
