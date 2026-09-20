namespace FischMacroCS.Core;

/// <summary>Only physical up-to-down edges dispatch commands; repeats and injected keys do not.</summary>
public sealed class KeyEdgeTracker
{
    private readonly HashSet<int> _held = new();
    public bool Observe(int key, bool down, bool injected = false)
    {
        if (injected) return false;
        if (!down) { _held.Remove(key); return false; }
        return _held.Add(key);
    }
    public void Reset() => _held.Clear();
}
