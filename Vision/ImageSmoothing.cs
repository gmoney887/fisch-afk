using OpenCvSharp;

namespace FischMacroCS.Vision;

internal static class ImageSmoothing
{
    // Native separable-equivalent 3x3 binomial filter. Separate output keeps in-place calls safe.
    // Filter2D avoids both the affected GaussianBlur path and managed per-pixel loops.
    public static void Apply(Mat source, Mat destination)
    {
        if (source.Empty()) { source.CopyTo(destination); return; }
        if (source.Depth() != MatType.CV_8U) throw new ArgumentException("Expected byte pixels.", nameof(source));
        using var kernel = Mat.FromArray(new float[,] { {1f/16,2f/16,1f/16}, {2f/16,4f/16,2f/16}, {1f/16,2f/16,1f/16} });
        using var result = new Mat();
        Cv2.Filter2D(source,result,-1,kernel,borderType:BorderTypes.Reflect101);
        result.CopyTo(destination);
    }
}
