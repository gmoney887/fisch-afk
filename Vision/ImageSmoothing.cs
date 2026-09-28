using OpenCvSharp;

namespace FischMacroCS.Vision;

internal static class ImageSmoothing
{
    // 3x3 binomial Gaussian with reflect-101 borders, separate storage even in place.
    // Avoids the native GaussianBlur access violation observed on this Windows runtime.
    public static unsafe void Apply(Mat source, Mat destination)
    {
        if (source.Empty()) { source.CopyTo(destination); return; }
        if (source.Depth() != MatType.CV_8U) throw new ArgumentException("Expected byte pixels.", nameof(source));
        using var result = new Mat(source.Rows, source.Cols, source.Type());
        int w = source.Cols, h = source.Rows, channels = source.Channels();
        for (int y = 0; y < h; y++)
        {
            byte* above = (byte*)source.Ptr(y == 0 ? Math.Min(1, h - 1) : y - 1);
            byte* row = (byte*)source.Ptr(y);
            byte* below = (byte*)source.Ptr(y == h - 1 ? Math.Max(0, h - 2) : y + 1);
            byte* dst = (byte*)result.Ptr(y);
            for (int x = 0; x < w; x++)
            for (int c = 0; c < channels; c++)
            {
                int l = (x == 0 ? Math.Min(1, w - 1) : x - 1) * channels + c;
                int r = (x == w - 1 ? Math.Max(0, w - 2) : x + 1) * channels + c;
                int p = x * channels + c;
                dst[p] = (byte)((above[l] + 2 * above[p] + above[r] +
                    2 * row[l] + 4 * row[p] + 2 * row[r] + below[l] + 2 * below[p] + below[r] + 8) / 16);
            }
        }
        GC.KeepAlive(source);
        result.CopyTo(destination);
    }
}
