using FischMacroCS.Core;
using FischMacroCS.Vision;
using OpenCvSharp;
namespace FischMacroCS.Tests;
public class RecordedHotbarTests
{
    private static Mat Frame(int number) => Cv2.ImRead(System.IO.Path.Combine(AppContext.BaseDirectory,"Fixtures",$"hotbar_stall_{number}.png"));
    [Theory]
    [InlineData(24276, true)] [InlineData(24277, false)]
    public void SelectedAndUnselectedHotbarBothHaveVerifiedGeometry(int number,bool equipped)
    {
        using var frame=Frame(number);
        var found=new VisionProcessor().DetectRodEquipped(frame,1,false,1369);
        Assert.True(found.GeometryConfirmed);
        Assert.Equal(equipped,found.IsEquipped);
        Assert.InRange(found.SlotCenter.X,1430,1455);
    }
    [Fact]
    public void RecordedUnequipCompletesWithoutLosingHotbar()
    {
        int presses=0;
        RodResetGuard.Unequip(()=>presses++,()=>
        {
            using var frame=Frame(presses==0?24276:24277);
            var found=new VisionProcessor().DetectRodEquipped(frame,1,false,1369);
            return(found.IsEquipped,found.GeometryConfirmed);
        },_=>{},default);
        Assert.Equal(1,presses);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void BlankOrIsolatedDimSquareIsNotAHotbar(bool square)
    {
        using var frame=new Mat(342,3440,MatType.CV_8UC3,Scalar.Black);
        if(square) Cv2.Rectangle(frame,new Rect(1686,271,68,68),Scalar.All(100),1);
        Assert.False(new VisionProcessor().DetectRodEquipped(frame,1,false,1369).GeometryConfirmed);
    }
}
