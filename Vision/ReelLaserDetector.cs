using FischMacroCS.Core;
using OpenCvSharp;

namespace FischMacroCS.Vision;

/// <summary>The Hammerhead warning tints a vertical strip across the entire reel capture.</summary>
public static class ReelLaserDetector
{
    public static unsafe ReelLaserZone? Find(Mat pixels, int offsetX, int offsetY, double viewportHeight)
    {
        if (pixels.Empty() || pixels.Type() != MatType.CV_8UC3 || viewportHeight <= 0) return null;
        int top = Math.Max(0, (int)(viewportHeight * .74) - offsetY);
        int bottom = Math.Min(pixels.Height, (int)(viewportHeight * .94) - offsetY);
        if (bottom - top < viewportHeight * .12) return null;
        int radius = Math.Max(1, (int)Math.Round(viewportHeight / 1080 * 2));
        int step = Math.Max(1, (int)Math.Round(viewportHeight / 1080 * 4));
        int width = pixels.Width;
        byte* data = (byte*)pixels.Data;
        long stride = pixels.Step();
        var rises = new List<int>();
        var falls = new List<int>();
        for (int x = radius; x < width - radius; x++)
        {
            int rising = 0, falling = 0, count = 0;
            for (int y = top; y < bottom; y += step)
            {
                byte* row = data + y * stride;
                byte* a = row + (x - radius) * 3;
                byte* b = row + (x + radius) * 3;
                int red = b[2] - a[2], green = b[1] - a[1], blue = b[0] - a[0];
                if (red > 8 && red > green * 1.5 + 4 && red > blue * 1.5 + 4) rising++;
                if (-red > 8 && -red > -green * 1.5 + 4 && -red > -blue * 1.5 + 4) falling++;
                count++;
            }
            // Paired, tall red edges survive the flashing fill. A red control
            // bar, diagonal radar stripes and rod slashes do not span these rows.
            if (rising >= count * .65) rises.Add(x);
            if (falling >= count * .65) falls.Add(x);
        }
        foreach (int left in Centers(rises))
            foreach (int right in Centers(falls))
                if (right - left >= viewportHeight * .18 && right - left <= viewportHeight * .48)
                    return new ReelLaserZone(offsetX + left, offsetX + right);
        return null;
    }

    private static IEnumerable<int> Centers(List<int> edges)
    {
        for (int i = 0; i < edges.Count; i++)
        {
            int start = edges[i], end = start;
            while (i + 1 < edges.Count && edges[i + 1] <= end + 1) end = edges[++i];
            yield return (start + end + 1) / 2;
        }
    }
}
