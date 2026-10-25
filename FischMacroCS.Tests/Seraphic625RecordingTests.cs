using System.Diagnostics;
using System.IO;
using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;
using Xunit.Abstractions;

namespace FischMacroCS.Tests;
public class Seraphic625RecordingTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(150,917,920,1165,2276,1720)]
    [InlineData(158,686,825,1032,2144,1655)]
    [InlineData(315,917,920,1087,2199,1689)]
    public void OriginalSlowFramesKeepReviewedGeometryAndBoundedRepeatedCost(int frame,int x,int y,int left,int right,int fish)
    {
        using var pixels=Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,"Fixtures","seraphic_625",$"frame_{frame:D8}.png"));
        var vision=new VisionProcessor();
        for(int repeat=0;repeat<4;repeat++)
        {
            var watch=Stopwatch.StartNew();
            using var r=vision.ProcessTrack(pixels,x,y,1369/1080.0,MinigameTheme.AutoCalibrate,false);
            watch.Stop();
            output.WriteLine($"Original625 frame{frame}, repeat{repeat}: {watch.Elapsed.TotalMilliseconds:F2}ms, {r.BarLeft}..{r.BarRight}, target{r.FishX}");
            Assert.True(r.HasLiveReel); if (frame == 150) Assert.True(r.ReelProgressFound);
            Assert.InRange(r.BarLeft,left-4,left+4);Assert.InRange(r.BarRight,right-4,right+4);
            Assert.InRange(r.BarWidth,right-left-5,right-left+5);Assert.InRange(r.FishX,fish-3,fish+3);
            Assert.InRange(watch.Elapsed.TotalMilliseconds,0,250);
        }
    }
}
