using FischMacroCS.Core;

namespace FischMacroCS.Tests;

public class PreservedEvidenceBufferTests
{
    [Fact]
    public void RoutineRecoveryFloodCannotEvictFailureEvidence()
    {
        var buffer = new PreservedEvidenceBuffer(100, 5);
        buffer.Add("failed-catch", 0, 60, true);
        for (int i = 1; i <= 1000; i++)
        {
            buffer.Add("routine-" + i, i, 20, false);
            Assert.DoesNotContain("failed-catch", buffer.Trim());
            Assert.InRange(buffer.Bytes, 0, 100);
            Assert.InRange(buffer.Count, 0, 5);
        }
    }

    [Fact]
    public void FailurePromotesPrecedingRoutineFramesAndStillHonorsHardLimits()
    {
        var buffer = new PreservedEvidenceBuffer(100, 3);
        buffer.Add("older-failure", 0, 20, true);
        buffer.Add("before-failure", 10, 20, false);
        buffer.PromoteSince(10);
        buffer.Add("after-failure", 11, 20, true);
        buffer.Add("routine", 12, 20, false);
        Assert.Equal(new[] { "routine" }, buffer.Trim());
        buffer.Add("new-failure", 13, 20, true);
        Assert.Equal(new[] { "older-failure" }, buffer.Trim());
        buffer.Add("oversized", 14, 200, true);
        Assert.Equal(new[] { "before-failure", "after-failure", "new-failure", "oversized" }, buffer.Trim());
        Assert.Equal(0, buffer.Bytes);
        Assert.Equal(0, buffer.Count);
    }
}
