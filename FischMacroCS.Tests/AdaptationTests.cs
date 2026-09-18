using FischMacroCS.Core;

namespace FischMacroCS.Tests;

public class AdaptationTests
{
    [Fact]
    public void PersistedValueCannotMoveTheSafetyEnvelope()
    {
        var estimator = new BoundedRodEstimator(1, 1.25);
        for (int i = 0; i < 500; i++) estimator.Observe(2, .02, true, true);
        Assert.Equal(1.25, estimator.Pull);
        Assert.False(estimator.CanPersist);
        for (int i = 0; i < 3; i++) estimator.ConfirmCatch();
        Assert.True(estimator.CanPersist);
    }

    [Fact]
    public void UnreliableMeasurementsFallBackToValidatedValue()
    {
        var estimator = new BoundedRodEstimator(1, .9);
        for (int i = 0; i < 50; i++) estimator.Observe(1.5, .02, true, true);
        for (int i = 0; i < 6; i++) Assert.False(estimator.Observe(1, double.NaN, true, true));
        Assert.Equal(.9, estimator.Pull);
        Assert.Equal(0, estimator.Samples);
        Assert.False(estimator.CanPersist);
        Assert.False(estimator.Observe(1, .02, true, false));
        Assert.False(estimator.Observe(1, .02, false, true));
    }
}
