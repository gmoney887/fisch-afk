using FischMacroCS.Core;

namespace FischMacroCS.Tests;

public class RodResetGuardTests
{
    [Fact]
    public void WaitsForTwoConsecutiveDeselectedFramesWithoutBlindRetoggling()
    {
        var frames = new Queue<bool>([true, false, true, false, false]);
        int toggles = 0, waited = 0;
        RodResetGuard.Unequip(() => toggles++, () => (frames.Dequeue(), true), ms => waited += ms, default);
        Assert.Equal(1, toggles);
        Assert.Equal(500, waited);
        Assert.Empty(frames);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingHotbarOrIgnoredToggleCannotAuthorizeReequip(bool visible)
    {
        int toggles = 0;
        Assert.Throws<GameplayInterruptedException>(() => RodResetGuard.Unequip(
            () => toggles++, () => (visible, visible), _ => { }, default));
        Assert.Equal(1, toggles);
    }

    [Fact]
    public void StopDuringUnequipPreventsFurtherObservationOrInput()
    {
        using var stop = new CancellationTokenSource();
        int toggles = 0;
        Assert.ThrowsAny<OperationCanceledException>(() => RodResetGuard.Unequip(
            () => toggles++, () => throw new Exception("Must stop before observing"), _ => stop.Cancel(), stop.Token));
        Assert.Equal(1, toggles);
    }
}
