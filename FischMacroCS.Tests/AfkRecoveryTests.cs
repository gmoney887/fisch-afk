using FischMacroCS.Core;
using FischMacroCS.Native;
using System.Runtime.InteropServices;

namespace FischMacroCS.Tests;

public class AfkRecoveryTests
{
    private sealed class Clock : IClock
    {
        public long Timestamp { get; set; }
        public double ElapsedMilliseconds(long since) => Timestamp - since;
        public void Delay(int milliseconds, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); Timestamp += milliseconds; }
    }

    [Fact]
    public void UpdateFocusLossAndLoadingOnlyFinishWhenGameplayReturns()
    {
        var clock = new Clock(); var recovery = new AfkRecovery(clock);
        bool focused = false; int activations = 0, observations = 0;
        FishingRetryWait.Wait(clock, () => recovery.Poll(() => focused,
            () => { if (++activations == 3) focused = true; },
            () => ++observations < 150 ? RecoveryView.Loading : RecoveryView.Gameplay,
            () => Assert.Fail("Loading must not click anything"), CancellationToken.None),
            () => { }, () => { }, CancellationToken.None);
        Assert.Equal(3, activations); Assert.Equal(150, observations);
        Assert.True(clock.Timestamp >= 30000);
        Assert.Equal("Gameplay verified", recovery.Status);
    }

    [Fact]
    public void FocusAttemptsAreThrottledAndNeverObserveAnUnfocusedWindow()
    {
        var clock = new Clock(); var recovery = new AfkRecovery(clock); int attempts = 0;
        bool Poll() => recovery.Poll(() => false, () => attempts++,
            () => throw new Exception("Wrong window"), () => Assert.Fail(), CancellationToken.None);
        Assert.False(Poll()); clock.Timestamp = 999; Assert.False(Poll()); Assert.Equal(1, attempts);
        clock.Timestamp = 1000; Assert.False(Poll()); Assert.Equal(2, attempts);
        recovery.Reset(); Assert.False(Poll()); Assert.Equal(3, attempts);
    }

    [Theory]
    [InlineData("activate")]
    [InlineData("observe")]
    [InlineData("reconnect")]
    public void TransientInterruptionInsideRecoveryKeepsTheRequestAlive(string stage)
    {
        var recovery = new AfkRecovery(new Clock());
        void Fail() => throw new GameplayInterruptedException("Focus changed");
        Assert.False(recovery.Poll(() => stage != "activate", Fail,
            () => { if (stage == "observe") Fail(); return RecoveryView.Reconnect; }, Fail, CancellationToken.None));
        Assert.Contains("retrying", recovery.Status);
        Assert.True(recovery.Poll(() => true, () => { }, () => RecoveryView.Gameplay, () => { }, CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StopBeforeOrDuringObservationPreventsAnyReconnect(bool during)
    {
        var recovery = new AfkRecovery(new Clock()); using var stop = new CancellationTokenSource();
        if (!during) stop.Cancel();
        Assert.Throws<OperationCanceledException>(() => recovery.Poll(() => true, () => Assert.Fail(),
            () => { stop.Cancel(); return RecoveryView.Reconnect; }, () => Assert.Fail(), stop.Token));
    }

    [Fact]
    public void ReconnectPolicyThrottlesAttemptsAndStillRequiresGameplay()
    {
        // Policy contract only: production has no validated Reconnect visual detector yet.
        var clock = new Clock(); var recovery = new AfkRecovery(clock); int clicks = 0;
        bool Poll(RecoveryView view) => recovery.Poll(() => true, () => { }, () => view, () => clicks++, CancellationToken.None);
        Assert.False(Poll(RecoveryView.Unknown)); Assert.Equal(0, clicks);
        Assert.False(Poll(RecoveryView.Reconnect)); clock.Timestamp = 9999;
        Assert.False(Poll(RecoveryView.Reconnect)); Assert.Equal(1, clicks);
        clock.Timestamp = 10000; Assert.False(Poll(RecoveryView.Reconnect)); Assert.Equal(2, clicks);
        Assert.False(Poll(RecoveryView.Loading)); Assert.True(Poll(RecoveryView.Gameplay));
    }

    [Fact]
    public void FocusLossAfterDetectionPreventsReconnect()
    {
        var recovery = new AfkRecovery(new Clock()); bool focused = true;
        Assert.False(recovery.Poll(() => focused, () => { },
            () => { focused = false; return RecoveryView.Reconnect; }, () => Assert.Fail(), CancellationToken.None));
    }

    [Fact]
    public void HeartbeatIsImmediateThenBoundedAndDisabledMeansNoInput()
    {
        var clock = new Clock(); var heartbeat = new AntiIdleHeartbeat(clock); int down = 0, up = 0;
        bool Send(bool enabled = true) => heartbeat.TrySend(enabled, () => down++, () => up++, CancellationToken.None, 8);
        Assert.False(Send(false)); Assert.True(Send()); Assert.False(Send());
        clock.Timestamp = 119999; Assert.False(Send()); clock.Timestamp++; Assert.True(Send());
        Assert.Equal(2, down); Assert.Equal(down, up); Assert.Equal(2, heartbeat.Completed);
        heartbeat.Reset(); Assert.Equal(0, heartbeat.Completed); Assert.True(Send());
    }

    [Fact]
    public void FailedOrCancelledHeartbeatReleasesInputWithoutRecordingSuccess()
    {
        var heartbeat = new AntiIdleHeartbeat(new Clock()); int releases = 0;
        Assert.Throws<GameplayInterruptedException>(() => heartbeat.TrySend(true,
            () => throw new GameplayInterruptedException("Focus lost"), () => releases++, CancellationToken.None));
        using var stop = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => heartbeat.TrySend(true, stop.Cancel, () => releases++, stop.Token));
        Assert.Equal(2, releases); Assert.Equal(0, heartbeat.Completed);
        Assert.True(heartbeat.TrySend(true, () => { }, () => releases++, CancellationToken.None));
    }

    [Fact]
    public void KeyboardSendInputHasNativeUnionSizeAndCorrectKeyUpFlag()
    {
        Assert.Equal(IntPtr.Size == 8 ? 40 : 28, Marshal.SizeOf<Win32.KEYBOARDINPUT>());
        var input = Win32.KeyboardInput(0x7E, Win32.KEYEVENTF_KEYUP);
        Assert.Equal(1u, input.type); Assert.Equal(0x7E, input.data.keyboard.wVk);
        Assert.Equal(Win32.KEYEVENTF_KEYUP, input.data.keyboard.dwFlags);
    }
}
