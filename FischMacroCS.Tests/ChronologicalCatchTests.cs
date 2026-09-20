using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;

namespace FischMacroCS.Tests;

public class ChronologicalCatchTests
{
    [Fact]
    public void RecordedReelExitAndCatchAnimationPreserveDetectionAcrossConsecutiveFrames()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "catch_sequence");
        using var sequence = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "sequence.json")));
        var vision = new VisionProcessor(); // Preserve temporal detector state across this real sequence.
        long previousTimestamp = 0;
        int previousId = 466, consecutiveBanners = 0;
        bool confirmed = false;
        foreach (var entry in sequence.RootElement.EnumerateArray())
        {
            int id = entry.GetProperty("FrameId").GetInt32();
            Assert.Equal(previousId + 1, id);
            previousId = id;
            long timestamp = entry.GetProperty("Timestamp").GetInt64();
            Assert.True(timestamp > previousTimestamp);
            previousTimestamp = timestamp;
            string path = Path.Combine(directory, entry.GetProperty("File").GetString()!);
            Assert.Equal(entry.GetProperty("SHA256").GetString(), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
            using var frame = Cv2.ImRead(path);
            Assert.False(frame.Empty());
            var region = entry.GetProperty("Region");
            int height = entry.GetProperty("Viewport").GetProperty("Height").GetInt32();
            Assert.Equal(region.GetProperty("Width").GetInt32(), frame.Width);
            Assert.Equal(region.GetProperty("Height").GetInt32(), frame.Height);
            using var detection = vision.ProcessTrack(frame, region.GetProperty("X").GetInt32(),
                region.GetProperty("Y").GetInt32(), height / 1080.0, MinigameTheme.AutoCalibrate, false);
            bool banner = vision.DetectCatchNotification(frame, height);
            string label = entry.GetProperty("Label").GetString()!;
            if (label == "ActiveReel")
            {
                Assert.True(detection.HasLiveReel, $"Frame {id}: real bar/fish/progress must remain active.");
                Assert.True(detection.BarFound);
                Assert.False(banner);
            }
            else if (label == "ReelExitAnimation") Assert.False(banner);
            else
            {
                Assert.False(detection.HasLiveReel, $"Frame {id}: player reward is not an active reel.");
                // The earliest translucent banner may be below confidence; settled frames must match.
                if (id >= 474) Assert.True(banner, $"Frame {id}: settled player catch must match.");
            }
            consecutiveBanners = banner ? consecutiveBanners + 1 : 0;
            confirmed |= consecutiveBanners >= 2;
            if (label != "PlayerCatch") Assert.False(confirmed);
        }
        Assert.Equal(475, previousId);
        Assert.True(confirmed, "Two separate recorded player banners must confirm the transition.");
    }
}
