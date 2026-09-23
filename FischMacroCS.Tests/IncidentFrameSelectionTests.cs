using System.IO;
using System.Text.Json;
using FischMacroCS.Core;
using OpenCvSharp;

namespace FischMacroCS.Tests;

public class IncidentFrameSelectionTests
{
    [Fact]
    public void RecorderExportsFirstIncidentContextAndDoesNotReplaceItsCause()
    {
        string root = Path.Combine(Path.GetTempPath(), "fisch-incident-" + Guid.NewGuid().ToString("N"));
        try
        {
            string session;
            using (var recorder = new FlightRecorder(root))
            {
                recorder.StartSession(100, 80);
                session = recorder.CurrentSessionDirectory!;
                using var frame = new Mat(80, 100, MatType.CV_8UC3, Scalar.All(30));
                var viewport = new Rect(0, 0, 100, 80);
                recorder.RecordFrame(frame, viewport, viewport, 1);
                recorder.RecordEvent("position-suspected", new { Outcome = "Unknown" });
                recorder.RecordEvent("recovery-attempt", new { Outcome = "Unknown" });
                recorder.StopSession();
            }
            using var marker = JsonDocument.Parse(File.ReadAllText(Path.Combine(session, "first-incident.json")));
            Assert.Equal("position-suspected", marker.RootElement.GetProperty("Kind").GetString());
            var selected = IncidentFrameSelection.Read(session);
            Assert.Single(selected);
            Assert.True(File.Exists(Path.Combine(session, selected[0])));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public void OlderSessionWithoutMarkerHasNoAutomaticImageSelection()
    {
        Assert.Empty(IncidentFrameSelection.Read(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));
    }
}
