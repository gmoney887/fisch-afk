using System;
using OpenCvSharp;
using FischMacroCS.Core;

namespace FischMacroCS.Vision;

public class DetectionResult
{
    public bool BarFound { get; set; }
    public int BarLeft { get; set; }
    public int BarRight { get; set; }
    public double BarCenter { get; set; }
    public int BarWidth { get; set; }

    public bool FishFound { get; set; }
    public int FishX { get; set; }
    public double Error => FishFound && BarFound ? (FishX - BarCenter) : 0;

    public Mat? AnnotatedFrame { get; set; }
}

public class ShakeDetectionResult
{
    public bool Found { get; set; }
    public Point Center { get; set; }
    public Rect BoundingBox { get; set; }
    public double ContrastScore { get; set; }
    public Mat? AnnotatedFrame { get; set; }
}

public class CastBarResult
{
    public bool Found { get; set; }
    public double FillPercent { get; set; }
    public int GreenY { get; set; }
    public int WhiteTop { get; set; }
    public int WhiteBottom { get; set; }
    public Rect BarBounds { get; set; }
    public Mat? AnnotatedFrame { get; set; }
}

public class RodDetectionResult
{
    public bool IsEquipped { get; set; }
    public bool HotbarFound { get; set; }
    public Rect HotbarBounds { get; set; }
    public Rect SlotBounds { get; set; }
    public Point SlotCenter { get; set; }
    public int ActivePixels { get; set; }
    public double ActiveDensity { get; set; }
    public Mat? AnnotatedFrame { get; set; }
}

public class VisionProcessor
{
    public DetectionResult ProcessTrack(Mat crop, int absOffsetX, int absOffsetY, double scaleFactor, MinigameTheme theme = MinigameTheme.Default, bool generateDebug = true)
    {
        DetectionResult result = new DetectionResult();
        if (crop == null || crop.Empty()) return result;

        if (theme == MinigameTheme.AutoCalibrate)
        {
            Scalar meanColor = Cv2.Mean(crop);
            double b = meanColor.Val0;
            double g = meanColor.Val1;
            double r = meanColor.Val2;

            if (r > g + 20 && r > b + 20)
            {
                theme = MinigameTheme.Feline; // Pink
            }
            else if (g > r + 10 && g > b + 10)
            {
                theme = MinigameTheme.Trident; // Green
            }
            else if (r > 100 && g > 100 && b < 80)
            {
                theme = MinigameTheme.Golden; // Yellow
            }
            else
            {
                theme = MinigameTheme.Default;
            }
        }

        try
        {
            int cropH = crop.Height;
            int cropW = crop.Width;

        // 1. Split BGR channels and apply Theme-Specific Masks
        Mat[] bgr = Cv2.Split(crop);
        using var b = bgr[0];
        using var g = bgr[1];
        using var r = bgr[2];

        using var barMask = new Mat();

        if (theme == MinigameTheme.Feline)
        {
            // Feline (Pink): High R and B, lower G.
            using var maskR = new Mat();
            Cv2.Threshold(r, maskR, 200, 255, ThresholdTypes.Binary);
            using var maskB = new Mat();
            Cv2.Threshold(b, maskB, 180, 255, ThresholdTypes.Binary);
            Cv2.BitwiseAnd(maskR, maskB, barMask);
        }
        else if (theme == MinigameTheme.Trident)
        {
            // Trident (Green): High G, low R, low B
            using var maskG = new Mat();
            Cv2.Threshold(g, maskG, 200, 255, ThresholdTypes.Binary);
            using var maskR = new Mat();
            Cv2.Threshold(r, maskR, 120, 255, ThresholdTypes.BinaryInv);
            using var maskB = new Mat();
            Cv2.Threshold(b, maskB, 120, 255, ThresholdTypes.BinaryInv);
            using var tempG = new Mat();
            Cv2.BitwiseAnd(maskG, maskR, tempG);
            Cv2.BitwiseAnd(tempG, maskB, barMask);
        }
        else if (theme == MinigameTheme.Golden)
        {
            // Golden: High R, High G, low B
            using var maskR = new Mat();
            Cv2.Threshold(r, maskR, 200, 255, ThresholdTypes.Binary);
            using var maskG = new Mat();
            Cv2.Threshold(g, maskG, 180, 255, ThresholdTypes.Binary);
            using var maskB = new Mat();
            Cv2.Threshold(b, maskB, 120, 255, ThresholdTypes.BinaryInv);
            using var tempY = new Mat();
            Cv2.BitwiseAnd(maskR, maskG, tempY);
            Cv2.BitwiseAnd(tempY, maskB, barMask);
        }
        else
        {
            // DEFAULT (Pure White Catch Bar)
            // Removes false-positive tension red trigger on red wooden dock planks
            using var maskB = new Mat();
            Cv2.Threshold(b, maskB, 200, 255, ThresholdTypes.Binary);
            using var maskG = new Mat();
            Cv2.Threshold(g, maskG, 200, 255, ThresholdTypes.Binary);
            using var maskR = new Mat();
            Cv2.Threshold(r, maskR, 200, 255, ThresholdTypes.Binary);

            using var maskBG = new Mat();
            Cv2.BitwiseAnd(maskB, maskG, maskBG);
            Cv2.BitwiseAnd(maskBG, maskR, barMask);
        }

        // Morphological CLOSE with horizontal structuring element:
        // Bridges the vertical fish needle cutting through the catch bar, merging it into one complete bar
        int closeKernelW = (int)Math.Max(25, Math.Round(35 * scaleFactor));
        int closeKernelH = (int)Math.Max(3, Math.Round(5 * scaleFactor));
        using var closeKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(closeKernelW, closeKernelH));
        using var bridgedBarMask = new Mat();
        Cv2.MorphologyEx(barMask, bridgedBarMask, MorphTypes.Close, closeKernel);

        // 2. Find Catch Bar Contours
        Cv2.FindContours(bridgedBarMask, out Point[][] contours, out HierarchyIndex[] _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

        Rect? bestBar = null;
        double maxArea = 0;
        int minBarH = (int)Math.Max(20, Math.Round(26 * scaleFactor));
        int maxBarH = (int)Math.Max(120, Math.Round(140 * scaleFactor));
        int minBarW = (int)Math.Max(25, Math.Round(35 * scaleFactor));
        int maxBarW = (int)(cropW * 0.70);

        foreach (var cnt in contours)
        {
            Rect rect = Cv2.BoundingRect(cnt);
            // Height and width bounds isolate minigame bar
            if (rect.Height >= minBarH && rect.Height <= maxBarH && rect.Width >= minBarW && rect.Width <= maxBarW)
            {
                double bboxArea = rect.Width * rect.Height;
                double cntArea = Cv2.ContourArea(cnt);
                double solidity = bboxArea > 0 ? (cntArea / bboxArea) : 0;

                // Solid rectangular catch bar has solidity >= 0.70.
                // Floating text notifications (e.g. catch banner) and splashes have solidity < 0.55.
                if (solidity < 0.60)
                    continue;

                if (bboxArea > maxArea)
                {
                    maxArea = bboxArea;
                    bestBar = rect;
                }
            }
        }

        Rect localBar = default;
        if (bestBar.HasValue)
        {
            localBar = bestBar.Value;
            result.BarFound = true;
            result.BarLeft = absOffsetX + localBar.Left;
            result.BarRight = absOffsetX + localBar.Right;
            result.BarCenter = (result.BarLeft + result.BarRight) / 2.0;
            result.BarWidth = localBar.Width;
        }

        // 3. Fish Needle Search:
        // Needle has Slate Blue silhouette (B channel dominant over R and G, ~70-88px tall)
        int minNeedleHeight = (int)Math.Max(15, Math.Round(25 * scaleFactor));

        int searchY1 = bestBar.HasValue 
            ? Math.Max(0, localBar.Top - (int)Math.Round(15 * scaleFactor)) 
            : (int)(cropH * 0.35);
        int searchY2 = bestBar.HasValue 
            ? Math.Min(cropH, localBar.Bottom + (int)Math.Round(15 * scaleFactor)) 
            : (int)(cropH * 0.95);

        // Constrain needle search to the physical minigame track zone
        // Excludes outer screen margins, borders, and distant ocean water
        int trackSearchX1 = bestBar.HasValue 
            ? Math.Max(25, localBar.Left - (int)(localBar.Width * 1.7)) 
            : 30;
        int trackSearchX2 = bestBar.HasValue 
            ? Math.Min(cropW - 25, localBar.Right + (int)(localBar.Width * 1.7)) 
            : cropW - 30;

        int[] colHist = new int[cropW];

        for (int nx = trackSearchX1; nx <= trackSearchX2; nx++)
        {
            int colMatches = 0;
            for (int ny = searchY1; ny < searchY2; ny++)
            {
                Vec3b pixel = crop.Get<Vec3b>(ny, nx);
                byte pb = pixel.Item0, pg = pixel.Item1, pr = pixel.Item2;

                bool isNeedleColor = false;

                if (theme == MinigameTheme.Feline || theme == MinigameTheme.Golden)
                {
                    // Pink / Golden themes use a stark white vertical needle line
                    isNeedleColor = (pr >= 200 && pg >= 200 && pb >= 200);
                }
                else if (theme == MinigameTheme.Trident)
                {
                    // Trident (Green) uses a stark black vertical needle line
                    isNeedleColor = (pr <= 50 && pg <= 50 && pb <= 50);
                }
                else
                {
                    // Default Slate Blue: B > R and B > G
                    isNeedleColor = (pb >= 70 && pb <= 125) &&
                                    (pg >= 50 && pg <= 110) &&
                                    (pr >= 40 && pr <= 100) &&
                                    (pb >= pr + 10) && (pb >= pg + 5);
                }

                if (isNeedleColor)
                    colMatches++;
            }
            colHist[nx] = colMatches;
        }

        // Peak Prominence & Narrow Width Isolation:
        // A true fish needle in Fisch is a sharp vertical silhouette (8px to 25px wide).
        // Background ocean water and dock planks form wide, continuous fields (>40px wide).
        // By evaluating peak prominence relative to baseline shoulders (scaled with scaleFactor) and contiguity,
        // we completely eliminate false detections on ocean water across all resolutions.
        int bestNeedleX = -1;
        double bestNeedleScore = 0;

        int shoulderDist = Math.Max(15, (int)Math.Round(25 * scaleFactor));
        int maxNeedleWidth = Math.Max(20, (int)Math.Round(32 * scaleFactor));

        for (int nx = trackSearchX1 + 5; nx <= trackSearchX2 - 5; nx++)
        {
            if (colHist[nx] >= minNeedleHeight)
            {
                int baseline = Math.Max(colHist[Math.Max(0, nx - shoulderDist)], colHist[Math.Min(cropW - 1, nx + shoulderDist)]);
                int prominence = colHist[nx] - baseline;

                // Check contiguous width of matches around nx
                int leftEdge = nx;
                while (leftEdge > trackSearchX1 && colHist[leftEdge] >= (minNeedleHeight / 2)) leftEdge--;
                int rightEdge = nx;
                while (rightEdge < trackSearchX2 && colHist[rightEdge] >= (minNeedleHeight / 2)) rightEdge++;
                int peakWidth = rightEdge - leftEdge - 1;

                // Needle must be narrow (<= maxNeedleWidth) and have significant localized prominence
                if (peakWidth <= maxNeedleWidth && prominence >= Math.Max(10, (int)(minNeedleHeight * 0.35)))
                {
                    double score = (prominence * 1.6) + colHist[nx];
                    if (score > bestNeedleScore)
                    {
                        bestNeedleScore = score;
                        bestNeedleX = nx;
                    }
                }
            }
        }

        // Fallback: If no prominent peak was found (e.g. low-contrast lighting), check highest narrow cluster
        if (bestNeedleX < 0)
        {
            int fallbackMax = 0;
            for (int nx = trackSearchX1 + 10; nx <= trackSearchX2 - 10; nx++)
            {
                if (colHist[nx] > fallbackMax && colHist[nx] >= minNeedleHeight)
                {
                    int leftScan = nx; while (leftScan > trackSearchX1 && colHist[leftScan] >= (minNeedleHeight / 2)) leftScan--;
                    int rightScan = nx; while (rightScan < trackSearchX2 && colHist[rightScan] >= (minNeedleHeight / 2)) rightScan++;
                    if ((rightScan - leftScan - 1) <= maxNeedleWidth)
                    {
                        fallbackMax = colHist[nx];
                        bestNeedleX = nx;
                    }
                }
            }
        }

        if (bestNeedleX >= 0)
        {
            // Sub-pixel centroid around the peak needle column (scaled radius window)
            int winRadius = Math.Max(4, (int)Math.Round(6 * scaleFactor));
            int winStart = Math.Max(0, bestNeedleX - winRadius);
            int winEnd = Math.Min(cropW - 1, bestNeedleX + winRadius);
            long weightedSum = 0;
            int totalWeight = 0;

            for (int wx = winStart; wx <= winEnd; wx++)
            {
                int w = colHist[wx];
                if (w >= (minNeedleHeight / 2))
                {
                    weightedSum += (long)wx * w;
                    totalWeight += w;
                }
            }

            double subpixelX = totalWeight > 0 ? (double)weightedSum / totalWeight : bestNeedleX;
            result.FishFound = true;
            result.FishX = absOffsetX + (int)Math.Round(subpixelX);
        }

        // 4. Annotated Debug Frame (for live preview and flight recorder)
        if (generateDebug)
        {
            Mat debug = crop.Clone();

            if (result.BarFound)
            {
                // Green box over catch bar
                Cv2.Rectangle(debug, localBar, Scalar.FromRgb(0, 230, 118), 2);

                // Orange line at bar center
                int localCenter = (int)((localBar.Left + localBar.Right) / 2.0);
                Cv2.Line(debug, new Point(localCenter, 0), new Point(localCenter, cropH), Scalar.FromRgb(255, 152, 0), 2);
            }

            if (result.FishFound)
            {
                // Cyan vertical line on fish needle
                int localFishX = result.FishX - absOffsetX;
                if (localFishX >= 0 && localFishX < cropW)
                {
                    Cv2.Line(debug, new Point(localFishX, 0), new Point(localFishX, cropH), Scalar.FromRgb(0, 229, 255), 2);
                    Cv2.Circle(debug, new Point(localFishX, cropH / 2), 4, Scalar.FromRgb(0, 229, 255), -1);
                }
            }

            result.AnnotatedFrame = debug;
        }

        foreach (var m in bgr) m.Dispose();

            return result;
        }
        catch
        {
            return result;
        }
    }

    private static Mat? _shakeTemplateMat;
    private static readonly object _templateLock = new();

    private static Mat? GetShakeTemplate()
    {
        if (_shakeTemplateMat == null || _shakeTemplateMat.IsDisposed)
        {
            lock (_templateLock)
            {
                if (_shakeTemplateMat == null || _shakeTemplateMat.IsDisposed)
                {
                    try
                    {
                        string basePath = AppDomain.CurrentDomain.BaseDirectory;
                        string[] candidatePaths = {
                            System.IO.Path.Combine(basePath, "Assets", "shake_template.png"),
                            System.IO.Path.Combine(basePath, "shake_template.png"),
                            @"c:\Users\garre\OneDrive\Documents\AutoHotkey\FischMacroCS\Assets\shake_template.png"
                        };

                        foreach (var path in candidatePaths)
                        {
                            if (System.IO.File.Exists(path))
                            {
                                var loaded = Cv2.ImRead(path, ImreadModes.Color);
                                if (loaded != null && !loaded.Empty())
                                {
                                    _shakeTemplateMat = loaded;
                                    break;
                                }
                            }
                        }

                        if (_shakeTemplateMat == null || _shakeTemplateMat.Empty())
                        {
                            var asm = System.Reflection.Assembly.GetExecutingAssembly();
                            using var stream = asm.GetManifestResourceStream("FischMacroCS.Assets.shake_template.png");
                            if (stream != null)
                            {
                                using var ms = new System.IO.MemoryStream();
                                stream.CopyTo(ms);
                                _shakeTemplateMat = Cv2.ImDecode(ms.ToArray(), ImreadModes.Color);
                            }
                        }
                    }
                    catch { }
                }
            }
        }
        return _shakeTemplateMat;
    }

    public ShakeDetectionResult DetectShakeIcon(Mat crop, int absOffsetX, int absOffsetY, double scaleFactor = 1.0, bool generateDebug = true)
    {
        var result = new ShakeDetectionResult();
        if (crop == null || crop.Empty()) return result;

        int cropW = crop.Width;
        int cropH = crop.Height;

        try
        {
            Rect? bestRect = null;
            Point bestCenter = default;
            double bestConfidence = 0;

            // Active interactive search zone: center-anchored height-scaled to cover active gameplay area
            // Eliminates black bars and outer margins on 21:9 and 32:9 Super Ultrawide screens
            int maxHalfW = (int)Math.Round(cropH * 0.85);
            int searchX1 = Math.Max(0, (cropW / 2) - maxHalfW);
            int searchW = Math.Min(cropW - searchX1, maxHalfW * 2);
            int searchY1 = (int)Math.Round(cropH * 0.08);
            int searchH = Math.Max(100, (int)Math.Round(cropH * 0.82));
            using var searchZone = new Mat(crop, new Rect(searchX1, searchY1, searchW, searchH));

            // =========================================================================
            // PASS 1: Fast Hierarchical Normalized Cross-Correlation Template Matching
            // Stage 1A: 2x downsampled coarse global sweep (~20ms)
            // Stage 1B: Full-resolution refined sub-pixel verification on candidate (~1ms)
            // =========================================================================
            Mat? template = GetShakeTemplate();
            if (template != null && !template.Empty())
            {
                int tmplW = template.Width;
                int tmplH = template.Height;

                int halfW = Math.Max(50, searchW / 2);
                int halfH = Math.Max(50, searchH / 2);
                using var halfZone = new Mat();
                Cv2.Resize(searchZone, halfZone, new Size(halfW, halfH), 0, 0, InterpolationFlags.Linear);

                // Calibrated multi-scale sweep relative to 1369p base resolution
                double baseScale = cropH / 1369.0;
                double[] testScales = { baseScale * 1.0, baseScale * 0.88, baseScale * 1.14 };
                foreach (double s in testScales)
                {
                    int coarseW = (int)Math.Round((tmplW * s) / 2.0);
                    int coarseH = (int)Math.Round((tmplH * s) / 2.0);

                    if (coarseW <= 8 || coarseH <= 4 || coarseW >= halfW || coarseH >= halfH)
                        continue;

                    using var halfTmpl = new Mat();
                    Cv2.Resize(template, halfTmpl, new Size(coarseW, coarseH), 0, 0, InterpolationFlags.Linear);

                    using var coarseRes = new Mat();
                    Cv2.MatchTemplate(halfZone, halfTmpl, coarseRes, TemplateMatchModes.CCoeffNormed);
                    Cv2.MinMaxLoc(coarseRes, out _, out double coarseVal, out _, out Point coarseLoc);

                    if (coarseVal >= 0.38)
                    {
                        // Stage 1B: Refine with full-resolution patch around candidate
                        int candX = searchX1 + (coarseLoc.X * 2);
                        int candY = searchY1 + (coarseLoc.Y * 2);

                        int fullTmplW = (int)Math.Round(tmplW * s);
                        int fullTmplH = (int)Math.Round(tmplH * s);

                        int roiPadX = Math.Max(25, (int)(fullTmplW * 0.35));
                        int roiPadY = Math.Max(18, (int)(fullTmplH * 0.40));

                        int roiX = Math.Clamp(candX - roiPadX, 0, Math.Max(0, cropW - fullTmplW - (roiPadX * 2)));
                        int roiY = Math.Clamp(candY - roiPadY, 0, Math.Max(0, cropH - fullTmplH - (roiPadY * 2)));
                        int roiW = Math.Min(cropW - roiX, fullTmplW + (roiPadX * 2));
                        int roiH = Math.Min(cropH - roiY, fullTmplH + (roiPadY * 2));

                        if (roiW > fullTmplW && roiH > fullTmplH)
                        {
                            using var roi = new Mat(crop, new Rect(roiX, roiY, roiW, roiH));
                            using var fullScaledTmpl = new Mat();
                            Cv2.Resize(template, fullScaledTmpl, new Size(fullTmplW, fullTmplH));

                            using var refineRes = new Mat();
                            Cv2.MatchTemplate(roi, fullScaledTmpl, refineRes, TemplateMatchModes.CCoeffNormed);
                            Cv2.MinMaxLoc(refineRes, out _, out double refVal, out _, out Point refLoc);

                            if (refVal > bestConfidence && refVal >= 0.45)
                            {
                                bestConfidence = refVal;
                                bestRect = new Rect(roiX + refLoc.X, roiY + refLoc.Y, fullTmplW, fullTmplH);
                                bestCenter = new Point(absOffsetX + roiX + refLoc.X + (fullTmplW / 2),
                                                       absOffsetY + roiY + refLoc.Y + (fullTmplH / 2));

                                // If nominal scale achieved high confidence, break early for fast <30ms reaction
                                if (refVal >= 0.70)
                                    break;
                            }
                        }
                    }
                }
            }

            // =========================================================================
            // PASS 2: Morphological Connected-Component Detector (Fallback)
            // Groups "S-H-A-K-E" into cohesive text block and validates dark button disc
            // =========================================================================
            if (bestConfidence < 0.65)
            {
                using var gray = new Mat();
                Cv2.CvtColor(searchZone, gray, ColorConversionCodes.BGR2GRAY);

                using var hsv = new Mat();
                Cv2.CvtColor(searchZone, hsv, ColorConversionCodes.BGR2HSV);

                using var whiteMask = new Mat();
                Cv2.InRange(hsv, new Scalar(0, 0, 215), new Scalar(180, 45, 255), whiteMask);

                int kw = Math.Max(16, (int)(22 * scaleFactor));
                int kh = Math.Max(4, (int)(6 * scaleFactor));
                using var closeKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(kw, kh));
                using var closed = new Mat();
                Cv2.MorphologyEx(whiteMask, closed, MorphTypes.Close, closeKernel);

                Cv2.FindContours(closed, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                foreach (var cnt in contours)
                {
                    Rect rect = Cv2.BoundingRect(cnt);
                    double aspect = (double)rect.Width / Math.Max(1, rect.Height);

                    // Dimension filters calibrated for "SHAKE" text block
                    if (rect.Width < (30 * scaleFactor) || rect.Width > (220 * scaleFactor) ||
                        rect.Height < (10 * scaleFactor) || rect.Height > (60 * scaleFactor) ||
                        aspect < 1.8 || aspect > 6.0)
                        continue;

                    // Verify dark button disc behind and around the text
                    int midX = Math.Clamp(rect.X + (rect.Width / 2), 0, searchW - 1);
                    int aboveY = Math.Max(0, rect.Y - (int)(10 * scaleFactor));
                    int belowY = Math.Min(searchH - 1, rect.Y + rect.Height + (int)(10 * scaleFactor));

                    byte lumaAbove = gray.Get<byte>(aboveY, midX);
                    byte lumaBelow = gray.Get<byte>(belowY, midX);
                    double avgBgLuma = (lumaAbove + lumaBelow) / 2.0;

                    // True Fisch shake button has dark background (avgBgLuma < 100)
                    if (avgBgLuma >= 100)
                        continue;

                    double score = (1.0 - (avgBgLuma / 100.0)) * 0.85;
                    if (score > bestConfidence)
                    {
                        bestConfidence = score;
                        bestRect = new Rect(searchX1 + rect.X, searchY1 + rect.Y, rect.Width, rect.Height);
                        bestCenter = new Point(absOffsetX + searchX1 + rect.X + (rect.Width / 2),
                                               absOffsetY + searchY1 + rect.Y + (rect.Height / 2));
                    }
                }
            }

            if (bestRect.HasValue && bestConfidence >= 0.50)
            {
                result.Found = true;
                result.Center = bestCenter;
                result.BoundingBox = bestRect.Value;
                result.ContrastScore = bestConfidence;
            }

            if (generateDebug)
            {
                Mat debug = crop.Clone();

                // Draw subtle active search zone frame
                Cv2.Rectangle(debug, new Rect(2, 2, cropW - 4, cropH - 4), Scalar.FromRgb(30, 41, 59), 1);

                if (result.Found && bestRect.HasValue)
                {
                    int localCx = result.Center.X - absOffsetX;
                    int localCy = result.Center.Y - absOffsetY;
                    int radius = Math.Max(bestRect.Value.Width, bestRect.Value.Height);

                    // Draw neon emerald targeting circle around the shake button
                    Cv2.Circle(debug, new Point(localCx, localCy), radius / 2 + 10, Scalar.FromRgb(0, 230, 118), 2);

                    // Draw neon targeting box on the text glyphs
                    Cv2.Rectangle(debug, bestRect.Value, Scalar.FromRgb(0, 255, 128), 1);

                    // Draw golden crosshair at center of button
                    Cv2.DrawMarker(debug, new Point(localCx, localCy), Scalar.FromRgb(255, 215, 0), MarkerTypes.Cross, 22, 2);

                    // Draw label
                    int badgeY = Math.Max(20, bestRect.Value.Y - 8);
                    Cv2.PutText(debug, $"SHAKE [{result.Center.X}, {result.Center.Y}] ({result.ContrastScore * 100:F0}%)",
                        new Point(Math.Max(4, bestRect.Value.X - 10), badgeY),
                        HersheyFonts.HersheySimplex, 0.45, Scalar.FromRgb(0, 255, 128), 1);
                }
                else
                {
                    Cv2.PutText(debug, "WATCHING FOR SHAKE BUTTON...", new Point(14, 24),
                        HersheyFonts.HersheySimplex, 0.42, Scalar.FromRgb(148, 163, 184), 1);
                }
                result.AnnotatedFrame = debug;
            }

            return result;
        }
        catch
        {
            return result;
        }
    }

    public Point? FindShakeTarget(Mat crop, int absOffsetX, int absOffsetY, double scaleFactor = 1.0)
    {
        var res = DetectShakeIcon(crop, absOffsetX, absOffsetY, scaleFactor, false);
        return res.Found ? res.Center : null;
    }

    /// <summary>
    /// Detects the Fisch casting power bar in real time.
    /// Locates the green target peak line and the rising white power fill.
    /// Computes accurate [0..100%] fill percentage for closed-loop perfect cast release.
    /// </summary>
    public CastBarResult DetectCastBar(Mat frame, MinigameTheme theme = MinigameTheme.Default, bool generateDebug = true)
    {
        var res = new CastBarResult();
        if (frame == null || frame.Empty()) return res;

        int frameW = frame.Width;
        int frameH = frame.Height;
        if (frameW < 50 || frameH < 50) return res;

        try
        {
            // Center-anchored height-scaled ROI for avatar's overhead cast bar (aspect-ratio invariant for 16:9, 16:10, 21:9, 32:9)
            int roiHalfW = (int)Math.Round(frameH * 0.28);
            int roiX = Math.Max(0, (frameW / 2) - roiHalfW);
            int roiW = Math.Min(frameW - roiX, roiHalfW * 2);
            int roiY = (int)Math.Round(frameH * 0.25);
            int roiH = (int)Math.Round(frameH * 0.50);

            using Mat roi = new Mat(frame, new Rect(roiX, roiY, roiW, roiH));
            return DetectCastBarROI(roi, frameH, roiX, roiY, theme, generateDebug);
        }
        catch
        {
            return res;
        }
    }

    /// <summary>
    /// High-speed Cast Bar detection directly on a pre-cropped or isolated ROI frame.
    /// Eliminates full-window BitBlt latency (~40-60ms down to ~1-2ms), enabling 150+ FPS real-time power tracking.
    /// </summary>
    public CastBarResult DetectCastBarROI(Mat roi, int fullClientH = 0, int roiOffsetX = 0, int roiOffsetY = 0, MinigameTheme theme = MinigameTheme.Default, bool generateDebug = true)
    {
        var res = new CastBarResult();
        if (roi == null || roi.Empty()) return res;

        int roiW = roi.Width;
        int roiH = roi.Height;
        if (roiW < 10 || roiH < 50) return res;

        try
        {
            using Mat hsv = new Mat();
            Cv2.CvtColor(roi, hsv, ColorConversionCodes.BGR2HSV);

            // Cast bar cap is ALWAYS green in Fisch, regardless of the rod/reeling theme
            using var mask = new Mat();
            Cv2.InRange(hsv, new Scalar(35, 60, 60), new Scalar(85, 255, 255), mask); 

            Cv2.FindContours(mask, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            double scale = fullClientH > 0 ? (fullClientH / 1080.0) : (roiH / 540.0);
            scale = Math.Clamp(scale, 0.35, 3.5);

            int minCapW = Math.Max(3, (int)Math.Round(4 * scale));
            int maxCapW = Math.Max(40, (int)Math.Round(65 * scale));
            int minCapH = Math.Max(2, (int)Math.Round(2 * scale));
            int maxCapH = Math.Max(20, (int)Math.Round(35 * scale));
            int minRoomUnder = Math.Max(30, (int)Math.Round(80 * scale));

            Rect? bestCapRect = null;
            var validCaps = new List<Rect>();
            foreach (var c in contours)
            {
                Rect rBox = Cv2.BoundingRect(c);
                // Cap is a small horizontal-ish block
                // Billboard GUI scales with camera zoom! On 4k zoomed out, it can be as small as 4-5px.
                if (rBox.Width >= minCapW && rBox.Width <= maxCapW && rBox.Height >= minCapH && rBox.Height <= maxCapH)
                {
                    // Verify sufficient vertical room underneath for the tall bar
                    int checkY = rBox.Y + rBox.Height + 5;
                    if (checkY + minRoomUnder < roiH)
                    {
                        validCaps.Add(rBox);
                    }
                }
            }
            if (validCaps.Count > 0)
            {
                // Select top-most cap candidate
                validCaps.Sort((a, b) => a.Y.CompareTo(b.Y));
                bestCapRect = validCaps[0];
            }

            if (!bestCapRect.HasValue)
            {
                if (generateDebug)
                {
                    Mat dbg = roi.Clone();
                    Cv2.PutText(dbg, "SEARCHING FOR CAST POWER BAR...", new Point(10, 25),
                        HersheyFonts.HersheySimplex, 0.45, Scalar.FromRgb(148, 163, 184), 1);
                    res.AnnotatedFrame = dbg;
                }
                return res;
            }

            var rCap = bestCapRect.Value;

            // Target Y is the vertical center of the cap block
            int localTargetY = rCap.Y + (rCap.Height / 2);
            int localTargetX = rCap.X + (rCap.Width / 2);
            res.GreenY = roiOffsetY + localTargetY;

            // Bar column extends downwards from beneath the green cap (~25% of full window height, ~340px)
            int expectedBarLen = fullClientH > 0 ? (int)(fullClientH * 0.25) : (int)(roiH * (0.25 / 0.45));
            int searchYStart = rCap.Y + rCap.Height;
            int barTopY = rCap.Y + rCap.Height; // Approximate highest point the white fill can reach (just under the cap)
            int barBottomY = roiH - 1;
            int barHeight = barBottomY - barTopY;
            if (barHeight <= Math.Max(25, (int)Math.Round(50 * scale))) return res;

            int capCenterX = rCap.X + rCap.Width / 2;
            int scanW = Math.Max(4, rCap.Width - 4);
            int scanX = Math.Clamp(capCenterX - scanW / 2, 0, roiW - scanW);

            using Mat barCol = new Mat(roi, new Rect(scanX, barTopY, scanW, barHeight));
            using Mat gray = new Mat();
            Cv2.CvtColor(barCol, gray, ColorConversionCodes.BGR2GRAY);
            using Mat whiteMask = new Mat();
            Cv2.Threshold(gray, whiteMask, 200, 255, ThresholdTypes.Binary);

            // Largest White Run Algorithm:
            // Finds the tallest continuous vertical block of white pixels.
            // Floating text (e.g. "Tunaaaaa") is only ~12-15px tall.
            // Glowing bubble lens flare is separated or short.
            // The true cast bar fill is the largest continuous solid white column.
            int bestRunStart = -1;
            int bestRunLength = 0;
            int currentRunStart = -1;
            int currentRunLength = 0;

            for (int y = 0; y < barHeight; y++)
            {
                int whiteCount = 0;
                for (int x = 0; x < scanW; x++)
                {
                    if (whiteMask.At<byte>(y, x) > 0) whiteCount++;
                }

                if (whiteCount >= Math.Max(1, scanW / 2))
                {
                    if (currentRunStart < 0) currentRunStart = y;
                    currentRunLength++;
                }
                else
                {
                    if (currentRunLength > bestRunLength)
                    {
                        bestRunLength = currentRunLength;
                        bestRunStart = currentRunStart;
                    }
                    currentRunStart = -1;
                    currentRunLength = 0;
                }
            }

            if (currentRunLength > bestRunLength)
            {
                bestRunLength = currentRunLength;
                bestRunStart = currentRunStart;
            }

            int minRunLength = Math.Max(3, (int)Math.Round(6 * scale));
            int firstWhiteRow = (bestRunLength >= minRunLength) ? bestRunStart : -1;

            if (firstWhiteRow >= 0)
            {
                res.Found = true;
                res.WhiteTop = roiOffsetY + barTopY + firstWhiteRow;
                res.WhiteBottom = roiOffsetY + barBottomY;
                int fillHeight = barHeight - firstWhiteRow;
                res.FillPercent = Math.Clamp(fillHeight * 100.0 / barHeight, 0.0, 100.0);
            }
            else
            {
                res.Found = false;
                res.FillPercent = 0.0;
            }

            res.BarBounds = new Rect(roiOffsetX + scanX, roiOffsetY + barTopY, scanW, barHeight);

            if (generateDebug)
            {
                Mat dbg = roi.Clone();
                if (res.Found)
                {
                    int drawTargetY = localTargetY;
                    int drawTargetX = localTargetX;

                    // Draw Target Line & Cap
                    Cv2.Rectangle(dbg, new Rect(rCap.X, rCap.Y, rCap.Width, rCap.Height), Scalar.FromRgb(0, 255, 128), 2);
                    Cv2.Line(dbg, new Point(drawTargetX - 35, drawTargetY), new Point(drawTargetX + 35, drawTargetY), Scalar.FromRgb(0, 255, 128), 3);
                    Cv2.PutText(dbg, "TARGET 100%", new Point(drawTargetX + 40, drawTargetY + 4), HersheyFonts.HersheySimplex, 0.45, Scalar.FromRgb(0, 255, 128), 1);

                    int whiteY = barTopY + firstWhiteRow;
                    Cv2.Line(dbg, new Point(drawTargetX - 25, whiteY), new Point(drawTargetX + 25, whiteY), Scalar.FromRgb(0, 229, 255), 2);

                    // Power percentage badge
                    Scalar badgeColor = res.FillPercent >= 95.0 ? Scalar.FromRgb(0, 230, 118) : Scalar.FromRgb(0, 229, 255);
                    string text = $"⚡ CAST POWER: {res.FillPercent:F0}% {(res.FillPercent >= 95.0 ? "[PERFECT!]" : "")}";
                    Cv2.PutText(dbg, text, new Point(10, 25), HersheyFonts.HersheySimplex, 0.55, badgeColor, 2);
                }
                else
                {
                    Cv2.PutText(dbg, "SEARCHING FOR CAST POWER BAR...", new Point(10, 25),
                        HersheyFonts.HersheySimplex, 0.45, Scalar.FromRgb(148, 163, 184), 1);
                }
                res.AnnotatedFrame = dbg;
            }

            return res;
        }
        catch
        {
            return res;
        }
    }

    public enum UIColorType
    {
        GreenButton,
        RedCloseButton
    }

    public (bool Found, Point Pt) DynamicUISnapWithStatus(Mat fullFrame, int expectedX, int expectedY, UIColorType targetType, int searchRadius = 0)
    {
        try
        {
            int effectiveRadius = searchRadius > 0 
                ? searchRadius 
                : Math.Max(25, (int)Math.Round(fullFrame.Height * 0.070));

            int roiX = Math.Max(0, expectedX - effectiveRadius);
            int roiY = Math.Max(0, expectedY - effectiveRadius);
            int roiW = Math.Min(fullFrame.Width - roiX, effectiveRadius * 2);
            int roiH = Math.Min(fullFrame.Height - roiY, effectiveRadius * 2);

            if (roiW <= 0 || roiH <= 0) return (false, new Point(expectedX, expectedY));

            using Mat roi = new Mat(fullFrame, new Rect(roiX, roiY, roiW, roiH));
            
            using Mat mask = new Mat();
            mask.Create(roi.Size(), MatType.CV_8UC1);
            mask.SetTo(Scalar.All(0));

            int roiRows = roi.Rows;
            int roiCols = roi.Cols;

            if (targetType == UIColorType.GreenButton)
            {
                for (int r = 0; r < roiRows; r++)
                {
                    for (int c = 0; c < roiCols; c++)
                    {
                        Vec3b bgr = roi.At<Vec3b>(r, c);
                        if (bgr.Item1 > 160 && bgr.Item1 > bgr.Item2 + 25 && bgr.Item1 > bgr.Item0 + 25)
                            mask.Set<byte>(r, c, 255);
                    }
                }
            }
            else if (targetType == UIColorType.RedCloseButton)
            {
                for (int r = 0; r < roiRows; r++)
                {
                    for (int c = 0; c < roiCols; c++)
                    {
                        Vec3b bgr = roi.At<Vec3b>(r, c);
                        if (bgr.Item2 > 150 && bgr.Item1 < 85 && bgr.Item0 < 85)
                            mask.Set<byte>(r, c, 255);
                    }
                }
            }

            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3));
            using var cleanMask = new Mat();
            Cv2.MorphologyEx(mask, cleanMask, MorphTypes.Open, kernel);

            Cv2.FindContours(cleanMask, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            
            Rect? bestRect = null;
            double maxArea = 0;
            // Area threshold scales with viewport height relative to 1080p base
            double minArea = Math.Max(10.0, 20.0 * Math.Pow(fullFrame.Height / 1080.0, 2));

            foreach (var cnt in contours)
            {
                double area = Cv2.ContourArea(cnt);
                if (area >= minArea && area > maxArea)
                {
                    maxArea = area;
                    bestRect = Cv2.BoundingRect(cnt);
                }
            }

            if (bestRect.HasValue)
            {
                int localCx = bestRect.Value.X + (bestRect.Value.Width / 2);
                int localCy = bestRect.Value.Y + (bestRect.Value.Height / 2);
                return (true, new Point(roiX + localCx, roiY + localCy));
            }
        }
        catch { }

        return (false, new Point(expectedX, expectedY));
    }

    public Point DynamicUISnap(Mat fullFrame, int expectedX, int expectedY, UIColorType targetType, int searchRadius = 0)
    {
        return DynamicUISnapWithStatus(fullFrame, expectedX, expectedY, targetType, searchRadius).Pt;
    }

    public RodDetectionResult DetectRodEquipped(Mat frame, int slotNum = 1, bool generateDebug = false)
    {
        var result = new RodDetectionResult();
        if (frame == null || frame.Empty()) return result;

        slotNum = Math.Clamp(slotNum, 1, 9);
        int w = frame.Width;
        int h = frame.Height;
        int midX = w / 2;

        // Scan bottom of frame to find hotbar vertical bounds
        int scanTop = (h >= 200) ? (int)(h * 0.75) : 0;
        int scanBottom = h - 2;

        int hotbarTop = -1;
        int hotbarBottom = -1;

        // Find the top border of the hotbar by finding where a central slice is dark container
        // Height-scaled slice radius & sampling step to guarantee resolution invariance
        int sliceRadius = Math.Max(8, (int)Math.Round(h * 0.018));
        int step = Math.Max(2, (int)Math.Round(h * 0.004));

        for (int y = scanTop; y <= scanBottom; y++)
        {
            int totalSliceSamples = 0;
            int darkAtMid = 0;
            for (int dx = -sliceRadius; dx <= sliceRadius; dx += step)
            {
                totalSliceSamples++;
                var p = frame.At<Vec3b>(y, midX + dx);
                if (p.Item0 < 80 && p.Item1 < 80 && p.Item2 < 80) darkAtMid++;
            }
            if (totalSliceSamples > 0 && ((double)darkAtMid / totalSliceSamples) >= 0.65)
            {
                if (hotbarTop == -1) hotbarTop = y;
                hotbarBottom = y;
            }
        }

        int minHotbarH = Math.Max(12, (int)Math.Round(h * 0.030));
        if (hotbarTop == -1 || (hotbarBottom - hotbarTop) < minHotbarH)
        {
            int fallbackH = (int)Math.Max(24, Math.Round(h * 0.056));
            int bottomMargin = Math.Max(2, (int)Math.Round(h * 0.007));
            hotbarBottom = h - bottomMargin;
            hotbarTop = hotbarBottom - fallbackH;
        }

        // Scan row just inside top margin where no item text or icons appear
        int topMargin = Math.Max(2, (int)Math.Round((hotbarBottom - hotbarTop) * 0.12));
        int scanY = Math.Clamp(hotbarTop + topMargin, hotbarTop, hotbarBottom);
        int hotbarLeft = -1;
        int hotbarRight = -1;

        // Center-Anchored Height-Scaled search span: never uses horizontal width percentages
        int searchMinX = Math.Max(0, midX - (int)(h * 0.60));
        int searchMaxX = Math.Min(w - 1, midX + (int)(h * 0.60));

        for (int x = searchMinX; x <= searchMaxX; x++)
        {
            var p = frame.At<Vec3b>(scanY, x);
            bool isDark = (p.Item0 < 80 && p.Item1 < 80 && p.Item2 < 80);
            if (isDark && hotbarLeft == -1) hotbarLeft = x;
            if (isDark) hotbarRight = x;
        }

        int totalW = (hotbarLeft >= 0 && hotbarRight > hotbarLeft) ? (hotbarRight - hotbarLeft + 1) : 0;
        int minHotbarW = Math.Max(50, (int)Math.Round(h * 0.20));
        bool found = totalW >= minHotbarW;

        if (!found)
        {
            int estSlotW = (int)Math.Max(20, Math.Round(h * 0.056));
            totalW = estSlotW * 9;
            hotbarLeft = midX - (totalW / 2);
            hotbarRight = hotbarLeft + totalW - 1;
        }

        Rect hotbarRect = new Rect(hotbarLeft, hotbarTop, totalW, hotbarBottom - hotbarTop + 1);
        double slotW = totalW / 9.0;
        int sLeft = hotbarLeft + (int)Math.Round((slotNum - 1) * slotW);
        int sRight = hotbarLeft + (int)Math.Round(slotNum * slotW);
        Rect slotRect = new Rect(sLeft, hotbarTop, Math.Max(1, sRight - sLeft), hotbarRect.Height);
        Point center = new Point((sLeft + sRight) / 2, (hotbarTop + hotbarBottom) / 2);

        // Sample interior of the slot (inset by 15% to strictly avoid borders and background water)
        int insetX = Math.Max(2, (int)Math.Round(slotRect.Width * 0.15));
        int insetY = Math.Max(2, (int)Math.Round(slotRect.Height * 0.15));
        int sampleX1 = slotRect.X + insetX;
        int sampleX2 = slotRect.Right - insetX;
        int sampleY1 = slotRect.Y + insetY;
        int sampleY2 = slotRect.Bottom - insetY;

        int totalSampleArea = Math.Max(1, (sampleX2 - sampleX1 + 1) * (sampleY2 - sampleY1 + 1));
        int activePx = 0;
        for (int y = sampleY1; y <= sampleY2; y++)
        {
            for (int x = sampleX1; x <= sampleX2; x++)
            {
                var p = frame.At<Vec3b>(y, x);
                // Active cyan/blue selection
                bool isBlue = (p.Item0 > 130 && p.Item0 > p.Item2 + 25 && p.Item0 > p.Item1 + 15);
                // Active white selection border/glow
                bool isWhite = (p.Item0 > 185 && p.Item1 > 185 && p.Item2 > 185);
                if (isBlue || isWhite) activePx++;
            }
        }

        // Density-based detection: active pixels must cover at least 4.5% of the sampled interior
        // with a scaled noise floor (for tiny window scales)
        double activeDensity = (double)activePx / totalSampleArea;
        int minActivePxFloor = Math.Max(4, (int)Math.Round(6 * (h / 1080.0)));
        bool isEquipped = activeDensity >= 0.045 && activePx >= minActivePxFloor;

        result.HotbarFound = found;
        result.HotbarBounds = hotbarRect;
        result.SlotBounds = slotRect;
        result.SlotCenter = center;
        result.ActivePixels = activePx;
        result.ActiveDensity = activeDensity;
        result.IsEquipped = isEquipped;

        if (generateDebug)
        {
            Mat annotated = frame.Clone();
            // Draw hotbar container
            Cv2.Rectangle(annotated, hotbarRect, Scalar.FromRgb(40, 50, 70), 1);

            // Draw slot box: Green if equipped, Red if unequipped
            Scalar slotColor = isEquipped ? Scalar.FromRgb(0, 230, 118) : Scalar.FromRgb(255, 82, 82);
            Cv2.Rectangle(annotated, slotRect, slotColor, 2);

            // Draw status banner over slot
            string statusTag = isEquipped 
                ? $"ROD #{slotNum}: EQUIPPED ({activeDensity * 100:F0}%)" 
                : $"ROD #{slotNum}: UNEQUIPPED";
            int textY = Math.Max(20, hotbarTop - 8);
            int textX = Math.Max(5, center.X - 65);
            Cv2.PutText(annotated, statusTag, new Point(textX, textY), HersheyFonts.HersheySimplex, 0.50, slotColor, 2);

            result.AnnotatedFrame = annotated;
        }

        return result;
    }
}

