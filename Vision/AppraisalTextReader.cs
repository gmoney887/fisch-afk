using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using OpenCvSharp;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace FischMacroCS.Vision;

internal sealed class AppraisalTextReader
{
    private readonly OcrEngine _engine = OcrEngine.TryCreateFromLanguage(new Language("en"))
        ?? throw new InvalidOperationException("Install English optical character recognition in Windows language settings to read appraisal results.");

    internal sealed record Word(string Text, Rect Bounds);
    internal Word[][] Read(Mat region, double scale, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        scale = Math.Min(scale, OcrEngine.MaxImageDimension / (double)Math.Max(region.Width, region.Height));
        using var enlarged = new Mat();
        Cv2.Resize(region, enlarged, default, scale, scale, InterpolationFlags.Cubic);
        using var pixels = new Mat();
        Cv2.CvtColor(enlarged, pixels, ColorConversionCodes.BGR2BGRA);
        var bytes = new byte[checked(pixels.Width * pixels.Height * 4)];
        Marshal.Copy(pixels.Data, bytes, 0, bytes.Length);
        using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(bytes.AsBuffer(), BitmapPixelFormat.Bgra8,
            pixels.Width, pixels.Height, BitmapAlphaMode.Ignore);
        var result = _engine.RecognizeAsync(bitmap).AsTask(cancellation).GetAwaiter().GetResult();
        return result.Lines.Select(line => line.Words.Select(word =>
        {
            var b = word.BoundingRect;
            return new Word(word.Text, new Rect((int)(b.X / scale), (int)(b.Y / scale),
                (int)Math.Ceiling(b.Width / scale), (int)Math.Ceiling(b.Height / scale)));
        }).ToArray()).ToArray();
    }
}
