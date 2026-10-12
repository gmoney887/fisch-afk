namespace FischMacroCS.Core;

/// <summary>Coalesces the hook and WM_HOTKEY copies of one physical press.</summary>
public sealed class HotkeyDispatchGate
{
    private readonly Dictionary<int, (uint Time, bool Hook)> _last = new();
    private readonly object _gate = new();
    private readonly HashSet<(int Key, uint Time)> _injected = new();
    private readonly Queue<(int Key, uint Time)> _injectedOrder = new();

    public void RecordInjected(int key, uint time)
    {
        lock (_gate)
        {
            var edge = (key, time);
            if (!_injected.Add(edge)) return;
            _injectedOrder.Enqueue(edge);
            while (_injectedOrder.Count > 512) _injected.Remove(_injectedOrder.Dequeue());
        }
    }

    public bool TryDispatch(int key, uint time, bool fromHook)
    {
        lock (_gate)
        {
            // WM_HOTKEY does not carry the low-level injected flag. Retain the
            // original event timestamp so automated End cannot become a stop.
            if (!fromHook && _injected.Contains((key, time))) return false;
            if (_last.TryGetValue(key, out var previous))
            {
                long difference = unchecked((int)(time - previous.Time));
                if (difference == 0 || (previous.Hook != fromHook && Math.Abs(difference) <= 50))
                    return false;
            }
            _last[key] = (time, fromHook);
            return true;
        }
    }
}
