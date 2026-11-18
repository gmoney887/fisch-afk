using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FischMacroCS.Core;

/// <summary>Cross-process ownership held before directory creation through final writer disposal.</summary>
internal sealed class RecordingSessionOwnership : IDisposable
{
    private Mutex? _mutex;
    private RecordingSessionOwnership(Mutex mutex) => _mutex = mutex;

    public static RecordingSessionOwnership? TryAcquire(string directory)
    {
        string identity = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant();
        string name = "Local\\FischAFK-Recording-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        var mutex = new Mutex(false, name);
        try
        {
            bool acquired;
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (acquired) return new RecordingSessionOwnership(mutex);
            mutex.Dispose();
            return null;
        }
        catch { mutex.Dispose(); throw; }
    }

    public void Dispose()
    {
        var mutex = Interlocked.Exchange(ref _mutex, null);
        if (mutex is null) return;
        try { mutex.ReleaseMutex(); } finally { mutex.Dispose(); }
    }
}
