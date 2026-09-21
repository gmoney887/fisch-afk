namespace FischMacroCS.Core;

public enum RecoveryView { Unknown, Loading, Reconnect, Gameplay, Continue, Death }

/// <summary>One persistent recovery request; only observed gameplay completes it.</summary>
public sealed class AfkRecovery(IClock clock)
{
    private long? _lastFocus, _lastReconnect, _lastContinue;
    public string Status { get; private set; } = "Checking Roblox";
    public void Reset() { _lastFocus = _lastReconnect = _lastContinue = null; Status = "Checking Roblox"; }
    public bool Poll(Func<bool> focused, Action activate, Func<RecoveryView> observe,
        Action reconnect, CancellationToken cancellation, Action? continueLoading = null)
    {
        try { return PollCore(focused, activate, observe, reconnect, cancellation, continueLoading); }
        catch (GameplayInterruptedException ex)
        {
            cancellation.ThrowIfCancellationRequested();
            Status = "Recovery interrupted; retrying: " + ex.Message;
            return false;
        }
    }
    private bool PollCore(Func<bool> focused, Action activate, Func<RecoveryView> observe,
        Action reconnect, CancellationToken cancellation, Action? continueLoading)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!focused())
        {
            Status = "Restoring Roblox focus";
            if (_lastFocus == null || clock.ElapsedMilliseconds(_lastFocus.Value) >= 1000)
            {
                cancellation.ThrowIfCancellationRequested();
                _lastFocus = clock.Timestamp;
                activate();
            }
            if (!focused()) return false;
        }
        cancellation.ThrowIfCancellationRequested();
        var view = observe();
        cancellation.ThrowIfCancellationRequested();
        if (view == RecoveryView.Gameplay) { Status = "Gameplay verified"; return true; }
        Status = view switch
        {
            RecoveryView.Loading => "Gameplay unavailable: waiting for recognizable controls",
            RecoveryView.Reconnect => "Disconnected: retrying Reconnect",
            RecoveryView.Continue => "Continuing from Fisch loading screen",
            RecoveryView.Death => "Character died: waiting for gameplay; return to a fishing spot after respawning",
            _ => "Waiting for recognizable gameplay or reconnect controls"
        };
        if (view == RecoveryView.Reconnect && (_lastReconnect == null || clock.ElapsedMilliseconds(_lastReconnect.Value) >= 10000))
        {
            cancellation.ThrowIfCancellationRequested();
            if (!focused()) return false;
            _lastReconnect = clock.Timestamp;
            reconnect();
        }
        if (view == RecoveryView.Continue && continueLoading != null && (_lastContinue == null || clock.ElapsedMilliseconds(_lastContinue.Value) >= 2000))
        {
            cancellation.ThrowIfCancellationRequested();
            if (!focused()) return false;
            _lastContinue = clock.Timestamp;
            continueLoading();
        }
        return false;
    }
}

public sealed class AntiIdleHeartbeat(IClock clock)
{
    private long? _lastSuccess;
    public int Completed { get; private set; }
    public void Reset() { _lastSuccess = null; Completed = 0; }
    public bool TrySend(bool enabled, Action send, Action release, CancellationToken cancellation, int intervalMinutes = 2)
    {
        int intervalMs = Math.Clamp(intervalMinutes, 1, 2) * 60000;
        if (!enabled || (_lastSuccess.HasValue && clock.ElapsedMilliseconds(_lastSuccess.Value) < intervalMs)) return false;
        cancellation.ThrowIfCancellationRequested();
        try { send(); cancellation.ThrowIfCancellationRequested(); }
        finally { release(); }
        _lastSuccess = clock.Timestamp;
        Completed++;
        return true;
    }
}
