using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Xunit.Abstractions;

namespace FischMacroCS.Tests;

public sealed class ReelTimingInstrumentationTests(ITestOutputHelper output)
{
    [Fact]
    public void RepeatedNativePointedCropKeepsOriginalGeometryWithinLatencyGate()
    {
        using var source=Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,"Fixtures","webweaver","frame_00000021.png"));
        using var full=new Mat(1369,3440,MatType.CV_8UC3,Scalar.All(35));
        using(var target=new Mat(full,new Rect(840,800,source.Width,source.Height)))source.CopyTo(target);
        var vision=new VisionProcessor();var first=Stopwatch.StartNew();
        using(var acquired=vision.ProcessReelContext(full,MinigameTheme.AutoCalibrate))Assert.True(acquired.BarFound);
        double acquisition=first.Elapsed.TotalMilliseconds;
        var region=vision.GetReelCaptureRegion(new Rect(968,1013,1504,273),new Size(3440,1369));
        using var crop=new Mat(full,region);var times=new List<double>();
        for(int i=0;i<44;i++)
        {
            using var current=crop.Clone();using var r=vision.ProcessTrack(current,region.X,region.Y,1369/1080.0,MinigameTheme.AutoCalibrate,false);
            Assert.True(r.BarFound&&r.FishFound&&r.ReelProgressFound);
            Assert.InRange(r.BarLeft,1352,1356);Assert.InRange(r.BarRight,1805,1809);Assert.InRange(r.FishX,1609,1613);
            if(i>=4)times.Add(r.DetectionLatencyMs);
        }
        var sorted=times.Order().ToArray();double p95=sorted[(int)Math.Ceiling(sorted.Length*.95)-1];
        var report=new{Source="native Webweaver21, real timing; clones are not additional game attempts",AcquisitionMs=acquisition,
            ProcessWasCold=false,Region=new{region.X,region.Y,region.Width,region.Height},Count=times.Count,
            P50Ms=sorted[sorted.Length/2],P95Ms=p95,P99Ms=sorted[^1],SamplesMs=times};
        string path=Path.Combine(Path.GetTempPath(),"fisch-638-native-timing-"+Guid.NewGuid().ToString("N")+".json");
        File.WriteAllText(path,JsonSerializer.Serialize(report));output.WriteLine(JsonSerializer.Serialize(report));output.WriteLine("timing="+path);
        Assert.True(p95<=25,$"Repeated native pointed p95={p95:F2}ms exceeds 25ms; original coordinates remain mandatory.");
    }
}
