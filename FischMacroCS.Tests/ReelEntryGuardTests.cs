using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;
using System.IO;

namespace FischMacroCS.Tests;

public class ReelEntryGuardTests
{
    [Fact]
    public void RecordedReelDuringRecovery_IsConfirmedDespiteHiddenHotbar()
    {
        var vision = new VisionProcessor();
        int frame = 21;
        bool result = ReelEntryGuard.Confirm(() =>
        {
            using var image = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"reel_active_recovery_{frame++}.png"));
            Assert.False(image.Empty());
            return vision.ProcessTrack(image, 968, 1015, 1353 / 1080.0, MinigameTheme.Default, false);
        }, _ => { }, CancellationToken.None);
        Assert.True(result);
        Assert.Equal(23, frame);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void MissingBarOrFish_DoesNotConfirmReel(bool bar, bool fish)
    {
        Assert.False(ReelEntryGuard.Confirm(() => new DetectionResult { BarFound = bar, FishFound = fish },
            _ => throw new Exception("No second sample needed"), CancellationToken.None));
    }

    [Fact]
    public void TransientDetection_DoesNotConfirmReel()
    {
        int samples = 0;
        Assert.False(ReelEntryGuard.Confirm(() => new DetectionResult { BarFound = true, FishFound = ++samples == 1 },
            _ => { }, CancellationToken.None));
    }

    [Fact]
    public void StopBetweenSamples_PreventsConfirmation()
    {
        using var cancellation = new CancellationTokenSource();
        int samples = 0;
        Assert.Throws<OperationCanceledException>(() => ReelEntryGuard.Confirm(() =>
        {
            samples++;
            return new DetectionResult { BarFound = true, FishFound = true };
        }, _ => cancellation.Cancel(), cancellation.Token));
        Assert.Equal(1, samples);
    }
}
