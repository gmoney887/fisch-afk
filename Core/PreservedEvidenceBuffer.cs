namespace FischMacroCS.Core;

/// <summary>Evicts routine samples before failure evidence within one fixed storage budget.</summary>
public sealed class PreservedEvidenceBuffer(long byteLimit = 128L * 1024 * 1024, int countLimit = 5000)
{
    private sealed record Frame(string File, long Timestamp, long Bytes, bool Failure);
    private readonly LinkedList<Frame> _frames = new();
    public long Bytes { get; private set; }
    public int Count => _frames.Count;

    public void Add(string file, long timestamp, long bytes, bool failure)
    {
        _frames.AddLast(new Frame(file, timestamp, bytes, failure));
        Bytes += bytes;
    }

    public void PromoteSince(long timestamp)
    {
        for (var node = _frames.Last; node != null && node.Value.Timestamp >= timestamp; node = node.Previous)
            node.Value = node.Value with { Failure = true };
    }

    public IEnumerable<string> Trim()
    {
        while (Bytes > byteLimit || Count > countLimit)
        {
            var victim = _frames.First;
            while (victim != null && victim.Value.Failure) victim = victim.Next;
            victim ??= _frames.First;
            if (victim == null) yield break;
            Bytes -= victim.Value.Bytes;
            string file = victim.Value.File;
            _frames.Remove(victim);
            yield return file;
        }
    }
}
