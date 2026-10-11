namespace FischMacroCS.Core;

/// <summary>Coalesces the hook and WM_HOTKEY copies of one physical press.</summary>
public sealed class HotkeyDispatchGate
{
    private readonly Dictionary<int, (uint Time, bool Hook)> _last = new();

    public bool TryDispatch(int key, uint time, bool fromHook)
    {
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
