using OpenCvSharp;

namespace FischMacroCS.Core;

public static class FishingPressTarget
{
    public static Point? Find(int width, int height, Func<Point, bool> uncovered)
    {
        int margin = Math.Max(1, (int)(height * .02));
        foreach (double y in new[] { .42, .30, .24 })
            foreach (double offset in new[] { .28, -.28, .45, -.45, .65, -.65 })
            {
                var point = new Point(width / 2 + (int)(height * offset), (int)(height * y));
                if (point.X >= margin && point.X < width - margin && uncovered(point)) return point;
            }
        return null;
    }
}
