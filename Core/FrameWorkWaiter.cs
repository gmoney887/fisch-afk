using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace FischMacroCS.Core;

/// <summary>Wait for native work while preserving the coordinator's short input checks.</summary>
internal sealed class FrameWorkWaiter : IDisposable
{
    private readonly TimerWaitHandle? _timer;

    internal FrameWorkWaiter()
    {
        // Unlike a short WaitOne timeout, this timer explicitly requests the
        // Windows10 1803+ high-resolution expiration facility. Older systems
        // retain the existing bounded poll; no process timer policy is changed.
        try
        {
            var handle = CreateWaitableTimerExW(IntPtr.Zero, null, 2, 0x00100002);
            if (handle.IsInvalid) { handle.Dispose(); return; }
            _timer = new TimerWaitHandle(handle);
        }
        catch (EntryPointNotFoundException) { }
        catch (DllNotFoundException) { }
    }

    internal bool WaitForCompletion(WaitHandle completion, int milliseconds)
    {
        if (completion.WaitOne(0)) return true;
        long due = -Math.Max(1, milliseconds) * 10000L;
        if (_timer is null || !SetWaitableTimer(_timer.SafeWaitHandle, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
            return completion.WaitOne(milliseconds);
        // No callback or extra input thread. A timer tick returns to the same
        // coordinator's existing focus/cancellation/lease checks and pulse.
        return WaitHandle.WaitAny([completion, _timer]) == 0;
    }

    public void Dispose() => _timer?.Dispose();

    private sealed class TimerWaitHandle : WaitHandle
    {
        internal TimerWaitHandle(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeWaitHandle CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long due, int period,
        IntPtr completion, IntPtr argument, [MarshalAs(UnmanagedType.Bool)] bool resume);
}
