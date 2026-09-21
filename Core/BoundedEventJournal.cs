using System.IO;
using System.Text;

namespace FischMacroCS.Core;

/// <summary>Four bounded journal segments retain recent decisions during long sessions.</summary>
public sealed class BoundedEventJournal : IDisposable
{
    private readonly string _directory;
    private readonly long _segmentBytes;
    private readonly string _name;
    private readonly long[] _lines = new long[4];
    private StreamWriter _writer;
    private long _bytes;
    public long ExpiredEntries { get; private set; }
    public BoundedEventJournal(string directory, long segmentBytes = 4L * 1024 * 1024, string name = "events")
    {
        if (segmentBytes <= 0) throw new ArgumentOutOfRangeException(nameof(segmentBytes));
        if (string.IsNullOrEmpty(name) || name.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-'))
            throw new ArgumentException("Journal name must be a simple file stem", nameof(name));
        _directory = directory; _segmentBytes = segmentBytes; _name = name;
        _writer = Open();
    }
    private string PathFor(int index) => Path.Combine(_directory, index == 0 ? $"{_name}.jsonl" : $"{_name}.{index}.jsonl");
    private StreamWriter Open() => new(PathFor(0), false, new UTF8Encoding(false));
    public void WriteLine(string line)
    {
        long size = Encoding.UTF8.GetByteCount(line) + 2;
        if (size > _segmentBytes) { ExpiredEntries++; return; }
        if (_bytes + size > _segmentBytes)
        {
            _writer.Dispose();
            ExpiredEntries += _lines[3];
            File.Delete(PathFor(3));
            for (int i = 2; i >= 0; i--)
            {
                if (File.Exists(PathFor(i))) File.Move(PathFor(i), PathFor(i + 1));
                _lines[i + 1] = _lines[i];
            }
            _lines[0] = 0; _bytes = 0; _writer = Open();
        }
        _writer.WriteLine(line); _bytes += size; _lines[0]++;
    }
    public void Flush() => _writer.Flush();
    public void Dispose() => _writer.Dispose();
}
