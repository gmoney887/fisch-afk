using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;
using System.Diagnostics;
using System.IO;
using Xunit.Abstractions;

namespace FischMacroCS.Tests;

public sealed class WebweaverRecordingTests(ITestOutputHelper output)
{
    private static Mat Load(int frame) => Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,"Fixtures","webweaver",$"frame_{frame:D8}.png"));

    [Theory]
    [InlineData(21,1354,1807,1611)]
    [InlineData(24,1159,1612,1317)]
    [InlineData(29,1481,1934,1724)]
    public void CurrentOriginalBoundsSurviveFourProductionObservations(int frame,int left,int right,int fish)
    {
        using var pixels=Load(frame);var vision=new VisionProcessor();
        for(int i=0;i<4;i++)
        {
            using var result=vision.ProcessTrack(pixels,840,800,1369/1080.0,MinigameTheme.AutoCalibrate,false);
            output.WriteLine($"{frame}/{i}: live={result.HasLiveReel},bar={result.BarLeft}..{result.BarRight},fish={result.FishX},meter={result.ReelProgressFound},ms={result.DetectionLatencyMs:F2}");
            Assert.True(result.HasLiveReel&&result.BarFound&&result.ReelProgressFound);
            Assert.InRange(result.BarLeft,left-2,left+2); Assert.InRange(result.BarRight,right-2,right+2);
            Assert.InRange(result.BarWidth,right-left-2,right-left+2); Assert.InRange(result.FishX,fish-2,fish+2);
        }
    }

    [Fact]
    public void OriginalCatchFrameDoesNotBecomeALiveReel()
    {
        using var pixels=Load(25); var vision=new VisionProcessor();
        for(int i=0;i<4;i++) { using var result=vision.ProcessTrack(pixels,840,800,1369/1080.0,MinigameTheme.AutoCalibrate,false); Assert.False(result.HasLiveReel); }
    }

    [Fact]
    public void HiddenEndRemainsMissingAndClearFrameReacquiresCurrentBounds()
    {
        using var hidden=Load(16);using var clear=Load(21);var vision=new VisionProcessor();
        for(int i=0;i<4;i++) { using var r=vision.ProcessTrack(hidden,840,800,1369/1080.0,MinigameTheme.AutoCalibrate,false);
            Assert.True(r.FishFound&&r.ReelProgressFound);Assert.False(r.BarFound);Assert.Equal(0,r.BarWidth);Assert.InRange(r.FishX,1532,1536); }
        using var current=vision.ProcessTrack(clear,840,800,1369/1080.0,MinigameTheme.AutoCalibrate,false);
        Assert.True(current.BarFound);Assert.InRange(current.BarLeft,1352,1356);Assert.InRange(current.BarRight,1805,1809);
    }

    [Theory]
    [InlineData(720,true)] [InlineData(1080,false)] [InlineData(1369,true)] [InlineData(2160,false)]
    public void CurrentGeometryUsesUiScaleAndOriginIndependentlyOfDesktop(int uiHeight,bool gray)
    {
        using var original=Load(21);using var pixels=new Mat();double scale=uiHeight/1369.0;
        Cv2.Resize(original,pixels,new Size(),scale,scale);
        if(gray){Cv2.CvtColor(pixels,pixels,ColorConversionCodes.BGR2GRAY);Cv2.CvtColor(pixels,pixels,ColorConversionCodes.GRAY2BGR);}
        else Cv2.CvtColor(pixels,pixels,ColorConversionCodes.BGR2RGB);
        int x=(int)Math.Round(840*scale)+37,y=(int)Math.Round(800*scale)-19;var vision=new VisionProcessor();
        for(int i=0;i<4;i++)
        {
            using var r=vision.ProcessTrack(pixels,x,y,4000/1080.0,MinigameTheme.AutoCalibrate,false);
            double tolerance=2*scale+1;
            Assert.True(r.BarFound&&r.FishFound&&r.ReelProgressFound,$"{uiHeight}/{i}: {r.BarLeft}..{r.BarRight},fish={r.FishX},path={r.DetectorPath}");
            Assert.InRange(r.BarLeft,x+514*scale-tolerance,x+514*scale+tolerance);
            Assert.InRange(r.BarRight,x+967*scale-tolerance,x+967*scale+tolerance);
            Assert.InRange(r.BarWidth,453*scale-tolerance,453*scale+tolerance);
            Assert.InRange(r.FishX,x+771*scale-tolerance,x+771*scale+tolerance);
        }
    }

    [Theory]
    [InlineData("meter")] [InlineData("meter-left")] [InlineData("meter-right")]
    [InlineData("upper-tip")] [InlineData("lower-tip")] [InlineData("left-end")] [InlineData("right-end")] [InlineData("body")]
    public void MissingComponentsCannotBorrowEarlierEvidence(string part)
    {
        using var clear=Load(21);using var erased=clear.Clone();
        var area=part switch {"meter"=>new Rect(490,490,790,45),"meter-left"=>new Rect(499,493,15,35),
            "meter-right"=>new Rect(1248,493,15,35),"upper-tip"=>new Rect(744,214,52,34),"lower-tip"=>new Rect(744,321,52,35),
            "left-end"=>new Rect(500,239,24,100),"right-end"=>new Rect(954,239,28,100),_=>new Rect(500,239,482,100)};
        using(var roi=new Mat(erased,area))roi.SetTo(Scalar.All(35));
        foreach(bool warm in new[]{false,true})
        {
            var vision=new VisionProcessor();
            if(warm)for(int i=0;i<4;i++){using var r=vision.ProcessTrack(clear,840,800,1369/1080.0,MinigameTheme.AutoCalibrate,false);Assert.True(r.BarFound);}
            using var missing=vision.ProcessTrack(erased,840,800,1369/1080.0,MinigameTheme.AutoCalibrate,false);
            // A missing control end may leave a genuinely observed target and
            // meter; it must never publish the erased body coordinates.
            if(part is "left-end" or "right-end" or "body")Assert.False(missing.BarFound,$"{part}/{warm}: {missing.BarLeft}..{missing.BarRight}");
            else Assert.False(missing.HasLiveReel,$"{part}/{warm}: {missing.BarLeft}..{missing.BarRight}/{missing.FishX}/{missing.DetectorPath}");
        }
    }

    [Fact]
    public void OrdinaryBarImmediatelyWinsAfterPointedLayout()
    {
        using var web=Load(21);var vision=new VisionProcessor();
        using(var r=vision.ProcessTrack(web,840,800,1369/1080.0,MinigameTheme.AutoCalibrate,false))Assert.True(r.BarFound);
        using var ordinary=Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,"Fixtures","reel_cyan_left.png"));
        for(int i=0;i<4;i++)
        {
            using var r=vision.ProcessTrack(ordinary,968,1013,1369/1080.0,MinigameTheme.AutoCalibrate,false);
            Assert.True(r.HasLiveReel&&r.BarFound);Assert.Equal("AGY",r.DetectorPath);
            if(i>0)Assert.Equal(0,r.FamilyProbeLatencyMs);
            Assert.InRange(r.BarLeft,1025,1035);Assert.InRange(r.BarRight,1789,1799);Assert.InRange(r.FishX,1441,1451);
        }
    }

    [Theory]
    [InlineData("regular_914.png")] [InlineData("boat_catch_25.png")] [InlineData("styx_6376.png")]
    public void OtherRecordedStructuresCannotBecomePointedLayout(string fixture)
    {
        using var pixels=Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,"Fixtures",fixture));
        var vision=new VisionProcessor();using var web=Load(21);
        using(var warm=vision.ProcessTrack(web,840,800,1369/1080.0,MinigameTheme.AutoCalibrate,false))Assert.True(warm.BarFound);
        for(int i=0;i<4;i++){using var result=vision.TryAcquirePointedLayout(pixels);Assert.Null(result);}
    }

    [Theory]
    [InlineData(720,false)] [InlineData(720,true)] [InlineData(1080,false)] [InlineData(1080,true)]
    [InlineData(1369,false)] [InlineData(1369,true)] [InlineData(2160,false)] [InlineData(2160,true)]
    public void ErasedPoolNeedleCannotBecomePointedSilhouette(int height,bool gray)
    {
        using var source=Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,"Fixtures","regular_914.png"));
        using(var missing=new Mat(source,new Rect(1105,80,40,140)))missing.SetTo(Scalar.Black);
        using var pixels=new Mat();Cv2.Resize(source,pixels,new Size(),height/1369.0,height/1369.0);
        if(gray){Cv2.CvtColor(pixels,pixels,ColorConversionCodes.BGR2GRAY);Cv2.CvtColor(pixels,pixels,ColorConversionCodes.GRAY2BGR);}
        var vision=new VisionProcessor();for(int i=0;i<4;i++){using var result=vision.TryAcquirePointedLayout(pixels);Assert.Null(result);}
    }

    [Fact]
    public void MeasuredCaptureRetainsCurrentMeterAndClearsOnMissingContextOrResize()
    {
        var ordinary=new Rect(968,1013,1504,273);var viewport=new Size(3440,1369);var vision=new VisionProcessor();
        Assert.Equal(ordinary,vision.GetReelCaptureRegion(ordinary,viewport));
        using var web=Load(21);using(var r=vision.ProcessTrack(web,840,800,1369/1080.0,MinigameTheme.AutoCalibrate,false))Assert.True(r.BarFound);
        var capture=vision.GetReelCaptureRegion(ordinary,viewport);
        Assert.True(capture.Contains(new Point(1346,1303))&&capture.Contains(new Point(2095,1320)));
        Assert.Equal(ordinary.X,capture.X);Assert.Equal(ordinary.Width,capture.Width);
        using var blank=new Mat(web.Size(),MatType.CV_8UC3,Scalar.All(35));
        using(var r=vision.ProcessTrack(blank,840,800,1369/1080.0,MinigameTheme.AutoCalibrate,false))Assert.False(r.HasLiveReel);
        Assert.Equal(ordinary,vision.GetReelCaptureRegion(ordinary,viewport));
        using(var r=vision.ProcessTrack(web,840,800,1369/1080.0,MinigameTheme.AutoCalibrate,false))Assert.True(r.BarFound);
        Assert.Equal(ordinary,vision.GetReelCaptureRegion(ordinary,new Size(3441,1369)));
    }
}
