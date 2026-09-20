using OpenCvSharp;
using System.IO;

namespace FischMacroCS.Vision;

/// <summary>Requires the Fisch loading logo and complete continue prompt together.</summary>
public static class ContinueScreenDetector
{
    private static readonly Lazy<Mat> Logo = new(() => Load("fisch_loading_logo.png"));
    private static readonly Lazy<Mat> Prompt = new(() => Load("fisch_continue_prompt.png"));
    private static Mat Load(string name)
    {
        using var stream = typeof(ContinueScreenDetector).Assembly.GetManifestResourceStream("FischMacroCS.Assets." + name)
            ?? throw new InvalidOperationException("Missing continue identity: " + name);
        using var bytes = new MemoryStream(); stream.CopyTo(bytes);
        using var color = Cv2.ImDecode(bytes.ToArray(), ImreadModes.Color);
        return WhiteMask(color);
    }

    private static Mat WhiteMask(Mat source)
    {
        using var bgr = new Mat(); using var hsv = new Mat();
        if (source.Channels() == 4) Cv2.CvtColor(source, bgr, ColorConversionCodes.BGRA2BGR);
        else source.CopyTo(bgr);
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
        var result = new Mat();
        Cv2.InRange(hsv, new Scalar(0,0,190), new Scalar(180,65,255), result);
        return result;
    }

    public static bool IsContinueScreen(Mat region, int viewportHeight)
        => TryFindPrompt(region, viewportHeight, out _);

    public static bool TryFindPrompt(Mat region, int viewportHeight, out Rect bounds)
    {
        bounds = default;
        if (region.Empty() || viewportHeight < 200 || region.Channels() is not (3 or 4)) return false;
        double scale = viewportHeight / 1353.0;
        using var mask = WhiteMask(region);
        using var logo = new Mat(); using var prompt = new Mat(); using var scores = new Mat();
        Cv2.Resize(Logo.Value, logo, new Size((int)Math.Round(408*scale), (int)Math.Round(242*scale)));
        Cv2.Resize(Prompt.Value, prompt, new Size((int)Math.Round(310*scale), (int)Math.Round(36*scale)));
        Cv2.GaussianBlur(mask,mask,new Size(3,3),.8);
        Cv2.GaussianBlur(logo,logo,new Size(3,3),.8);
        Cv2.GaussianBlur(prompt,prompt,new Size(3,3),.8);
        if (mask.Width < logo.Width || mask.Height < logo.Height) return false;
        Cv2.MatchTemplate(mask,logo,scores,TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(scores,out _,out double score,out _,out Point at);
        if (score < .88) return false;
        int tolerance = Math.Max(3,(int)Math.Ceiling(5*scale));
        var search = new Rect(at.X+(int)Math.Round(47*scale)-tolerance, at.Y+(int)Math.Round(281*scale)-tolerance,
            prompt.Width+2*tolerance,prompt.Height+2*tolerance) & new Rect(0,0,mask.Width,mask.Height);
        if (search.Width < prompt.Width || search.Height < prompt.Height) return false;
        using var promptRegion = new Mat(mask,search);
        Cv2.MatchTemplate(promptRegion,prompt,scores,TemplateMatchModes.CCoeffNormed);
        Cv2.MinMaxLoc(scores,out _,out score,out _,out var promptAt);
        if (score < .88) return false;
        bounds = new Rect(search.X + promptAt.X, search.Y + promptAt.Y, prompt.Width, prompt.Height);
        return true;
    }
}
