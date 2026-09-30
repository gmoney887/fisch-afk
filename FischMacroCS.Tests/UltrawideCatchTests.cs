using FischMacroCS.Vision;
using OpenCvSharp;
using System.IO;
namespace FischMacroCS.Tests;
public class UltrawideCatchTests
{
    [Theory]
    [InlineData("catch_ultrawide_3424.png")]
    [InlineData("catch_ultrawide_3424_next.png")]
    public void RecordedPelagicCodCatchIsConfirmed(string name)
    {
        using var frame = Cv2.ImRead(Path.Combine(AppContext.BaseDirectory,"Fixtures",name));
        Assert.True(new VisionProcessor().DetectCatchNotification(frame,1353));
    }
}
