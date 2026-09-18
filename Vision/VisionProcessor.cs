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

public class ToolToggleResult
{
    public bool ToggleDetected { get; set; }
    public double DeltaRatio { get; set; }
    public int ChangedPixels { get; set; }
    public int TotalPixels { get; set; }
    public Rect SlotBounds { get; set; }
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

        int searchW = trackSearchX2 - trackSearchX1 + 1;
        int searchH = searchY2 - searchY1;
        if (searchW > 0 && searchH > 0 && trackSearchX1 >= 0 && trackSearchX1 + searchW <= cropW && searchY1 >= 0 && searchY1 + searchH <= cropH)
        {
            Rect needleSearchRoi = new Rect(trackSearchX1, searchY1, searchW, searchH);
            using var searchZone = new Mat(crop, needleSearchRoi);
            using var needleMask = new Mat();

            if (theme == MinigameTheme.Feline || theme == MinigameTheme.Golden)
            {
                // Pink / Golden themes use a stark white vertical needle line (R, G, B >= 200)
                Cv2.InRange(searchZone, new Scalar(200, 200, 200), new Scalar(255, 255, 255), needleMask);
            }
            else if (theme == MinigameTheme.Trident)
            {
                // Trident (Green) uses a stark black vertical needle line (R, G, B <= 50)
                Cv2.InRange(searchZone, new Scalar(0, 0, 0), new Scalar(50, 50, 50), needleMask);
            }
            else
            {
                // Default Slate Blue: B in [70, 125], G in [50, 110], R in [40, 100], and B >= R+10 & B >= G+5
                using var broadMask = new Mat();
                Cv2.InRange(searchZone, new Scalar(70, 50, 40), new Scalar(125, 110, 100), broadMask);

                // Channel dominance via native OpenCV matrix subtraction
                Mat[] channels = Cv2.Split(searchZone);
                using (var bChan = channels[0])
                using (var gChan = channels[1])
                using (var rChan = channels[2])
                using (var diffR = new Mat())
                using (var diffG = new Mat())
                using (var condR = new Mat())
                using (var condG = new Mat())
                using (var condCombined = new Mat())
                {
                    Cv2.Subtract(bChan, rChan, diffR);
                    Cv2.Subtract(bChan, gChan, diffG);
                    Cv2.Compare(diffR, 10, condR, CmpTypes.GE);
                    Cv2.Compare(diffG, 5, condG, CmpTypes.GE);
                    Cv2.BitwiseAnd(condR, condG, condCombined);
                    Cv2.BitwiseAnd(broadMask, condCombined, needleMask);
                }
                foreach (var ch in channels) ch.Dispose();
            }

            // Native OpenCV SIMD column reduction: sums matching pixels per column
            using var colScores = new Mat();
            Cv2.Reduce(needleMask, colScores, ReduceDimension.Row, ReduceTypes.Sum, MatType.CV_32S);

            int nCols = colScores.Cols;
            for (int i = 0; i < nCols; i++)
            {
                colHist[trackSearchX1 + i] = colScores.At<int>(0, i) / 255;
            }
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
            // Eliminates black bars, outer margins, hotbar/tooltips (lower 34%), and topbar (top 16%)
            int maxHalfW = (int)Math.Round(cropH * 0.75);
            int searchX1 = Math.Max(0, (cropW / 2) - maxHalfW);
            int searchW = Math.Min(cropW - searchX1, maxHalfW * 2);
            int searchY1 = (int)Math.Round(cropH * 0.18);
            int searchH = Math.Max(100, (int)Math.Round(cropH * 0.48)); // Range: 18% -> 66% viewport height
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

                    if (coarseVal >= 0.42)
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

                            if (refVal > bestConfidence && refVal >= 0.58)
                            {
                                bestConfidence = refVal;
                                bestRect = new Rect(roiX + refLoc.X, roiY + refLoc.Y, fullTmplW, fullTmplH);
                                bestCenter = new Point(absOffsetX + roiX + refLoc.X + (fullTmplW / 2),
                                                       absOffsetY + roiY + refLoc.Y + (fullTmplH / 2));

                                // If nominal scale achieved high confidence, break early for fast <30ms reaction
                                if (refVal >= 0.72)
                                    break;
                            }
                        }
                    }
                }
            }

            if (bestRect.HasValue && bestConfidence >= 0.58)
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
            using var greenMask = new Mat();
            Cv2.InRange(hsv, new Scalar(40, 60, 60), new Scalar(85, 255, 255), greenMask);

            Cv2.FindContours(greenMask, out Point[][] greenContours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

            double scale = fullClientH > 0 ? (fullClientH / 1080.0) : (roiH / 540.0);
            scale = Math.Clamp(scale, 0.35, 3.5);

            int minCapW = Math.Max(6, (int)Math.Round(6 * scale));
            int maxCapW = Math.Max(40, (int)Math.Round(65 * scale));
            int minCapH = Math.Max(2, (int)Math.Round(2 * scale));
            int maxCapH = Math.Max(20, (int)Math.Round(35 * scale));

            Rect? bestCap = null;
            Rect bestWhiteContour = default;
            double bestScore = -1.0;
            int bestTotalBarH = 0;
            int bestScanX = 0;
            int bestScanW = 0;

            foreach (var c in greenContours)
            {
                Rect r = Cv2.BoundingRect(c);
                if (r.Width >= minCapW && r.Width <= maxCapW && r.Height >= minCapH && r.Height <= maxCapH && 
                    r.Width >= r.Height * 0.45 && (r.Width * r.Height) >= 20)
                {
                    int capCenterX = r.X + r.Width / 2;
                    int capBottomY = r.Y + r.Height;
                    int expectedBarH = (int)Math.Round(r.Width * 20.0);
                    int scanW = Math.Max(4, Math.Min(r.Width, 14));
                    int scanX = Math.Clamp(capCenterX - scanW / 2, 0, roiW - scanW);
                    int scanH = Math.Min(roiH - capBottomY, (int)Math.Round(expectedBarH * 1.85));
                    if (scanH < 20) continue;

                    using var colMat = new Mat(roi, new Rect(scanX, capBottomY, scanW, scanH));
                    using var gray = new Mat();
                    Cv2.CvtColor(colMat, gray, ColorConversionCodes.BGR2GRAY);
                    using var whiteMask = new Mat();
                    Cv2.Threshold(gray, whiteMask, 195, 255, ThresholdTypes.Binary);

                    int kernelH = Math.Max(5, (int)Math.Round(15 * scale));
                    using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(1, kernelH));
                    using var closedMask = new Mat();
                    Cv2.MorphologyEx(whiteMask, closedMask, MorphTypes.Close, kernel);

                    Cv2.FindContours(closedMask, out Point[][] wContours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
                    foreach (var wc in wContours)
                    {
                        Rect wr = Cv2.BoundingRect(wc);
                        int minRunH = Math.Max(5, (int)Math.Round(6 * scale));
                        int whiteBottom = wr.Y + wr.Height;

                        bool isCapContact = wr.Y <= Math.Max(2, (int)Math.Round(3 * scale)) && wr.Height >= Math.Max(50, (int)Math.Round(60 * scale));

                        // Solid vertical white column under green cap indicates active cast power bar
                        if (wr.Height >= minRunH && (wr.Height >= 30 || whiteBottom >= (int)(expectedBarH * 0.40) || isCapContact))
                        {
                            int totalBarH = isCapContact
                                ? wr.Height
                                : Math.Max(whiteBottom, (int)Math.Round(r.Width * 14.0));
                            double aspectMatch = isCapContact
                                ? 1.0
                                : (1.0 - Math.Min(1.0, Math.Abs(whiteBottom - expectedBarH) / (double)expectedBarH));
                            double score = (wr.Height * 3.0) + (aspectMatch * 100.0) + (isCapContact ? 100.0 : 0.0);

                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestCap = r;
                                bestWhiteContour = new Rect(scanX + wr.X, capBottomY + wr.Y, wr.Width, wr.Height);
                                bestTotalBarH = totalBarH;
                                bestScanX = scanX;
                                bestScanW = scanW;
                            }
                        }
                    }
                }
            }

            int minFillH = Math.Max(10, (int)Math.Round(12 * scale));
            int fillH = bestWhiteContour.Height;

            if (bestCap.HasValue && bestScore >= 200.0 && fillH >= minFillH)
            {
                var cap = bestCap.Value;
                int localTargetY = cap.Y + (cap.Height / 2);
                int localTargetX = cap.X + (cap.Width / 2);

                res.Found = true;
                res.GreenY = roiOffsetY + localTargetY;
                res.WhiteTop = roiOffsetY + bestWhiteContour.Y;
                res.WhiteBottom = roiOffsetY + bestWhiteContour.Y + bestWhiteContour.Height;
                res.FillPercent = Math.Clamp((double)fillH * 100.0 / bestTotalBarH, 0.0, 100.0);
                res.BarBounds = new Rect(roiOffsetX + bestScanX, roiOffsetY + cap.Y + cap.Height, bestScanW, bestTotalBarH);

                if (generateDebug)
                {
                    Mat dbg = roi.Clone();
                    // Draw Target Line & Cap
                    Cv2.Rectangle(dbg, new Rect(cap.X, cap.Y, cap.Width, cap.Height), Scalar.FromRgb(0, 255, 128), 2);
                    Cv2.Line(dbg, new Point(localTargetX - 35, localTargetY), new Point(localTargetX + 35, localTargetY), Scalar.FromRgb(0, 255, 128), 3);
                    Cv2.PutText(dbg, "TARGET 100%", new Point(localTargetX + 40, localTargetY + 4), HersheyFonts.HersheySimplex, 0.45, Scalar.FromRgb(0, 255, 128), 1);

                    // Draw Bar Container outline
                    Cv2.Rectangle(dbg, new Rect(bestScanX, cap.Y + cap.Height, bestScanW, bestTotalBarH), Scalar.FromRgb(80, 80, 80), 1);

                    // Draw White Fill Level line
                    int whiteY = bestWhiteContour.Y;
                    Cv2.Line(dbg, new Point(localTargetX - 25, whiteY), new Point(localTargetX + 25, whiteY), Scalar.FromRgb(0, 229, 255), 2);

                    // Power percentage badge
                    Scalar badgeColor = res.FillPercent >= 95.0 ? Scalar.FromRgb(0, 230, 118) : Scalar.FromRgb(0, 229, 255);
                    string text = $"⚡ CAST POWER: {res.FillPercent:F0}% {(res.FillPercent >= 95.0 ? "[PERFECT!]" : "")}";
                    Cv2.PutText(dbg, text, new Point(10, 25), HersheyFonts.HersheySimplex, 0.55, badgeColor, 2);
                    res.AnnotatedFrame = dbg;
                }
            }
            else
            {
                res.Found = false;
                res.FillPercent = 0.0;
                if (generateDebug)
                {
                    Mat dbg = roi.Clone();
                    Cv2.PutText(dbg, "SEARCHING FOR CAST POWER BAR...", new Point(10, 25),
                        HersheyFonts.HersheySimplex, 0.45, Scalar.FromRgb(148, 163, 184), 1);
                    res.AnnotatedFrame = dbg;
                }
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

            using var hsvRoi = new Mat();
            if (roi.Channels() == 4)
            {
                using var bgrRoi = new Mat();
                Cv2.CvtColor(roi, bgrRoi, ColorConversionCodes.BGRA2BGR);
                Cv2.CvtColor(bgrRoi, hsvRoi, ColorConversionCodes.BGR2HSV);
            }
            else
            {
                Cv2.CvtColor(roi, hsvRoi, ColorConversionCodes.BGR2HSV);
            }

            if (targetType == UIColorType.GreenButton)
            {
                // Green button in HSV: H in [35, 85], S >= 60, V >= 60
                Cv2.InRange(hsvRoi, new Scalar(35, 60, 60), new Scalar(85, 255, 255), mask);
            }
            else if (targetType == UIColorType.RedCloseButton)
            {
                // Red close button in HSV (wraps around 0/180): H in [0, 10] or [170, 180]
                using var mask1 = new Mat();
                using var mask2 = new Mat();
                Cv2.InRange(hsvRoi, new Scalar(0, 70, 70), new Scalar(10, 255, 255), mask1);
                Cv2.InRange(hsvRoi, new Scalar(170, 70, 70), new Scalar(180, 255, 255), mask2);
                Cv2.BitwiseOr(mask1, mask2, mask);
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

    public RodDetectionResult DetectRodEquipped(Mat frame, int slotNum = 1, bool generateDebug = false, int fullViewportHeight = 0)
    {
        var result = new RodDetectionResult();
        if (frame == null || frame.Empty()) return result;

        slotNum = Math.Clamp(slotNum, 1, 9);
        int w = frame.Width;
        int h = frame.Height;
        int midX = w / 2;

        // In Roblox CoreGui, UI elements scale strictly with viewport height (GuiService).
        // If frame is an ROI crop of the bottom region (w/h >= 4.5), calculate full viewport height.
        int vpH = fullViewportHeight > 0 
            ? fullViewportHeight 
            : ((w / (double)h >= 4.5) ? (int)Math.Round(h * 4.0) : h);

        // Center-Anchored Height-Scaled Roblox Hotbar Geometry (Immune to scenery, sand, and aspect ratio)
        int nominalTotalW = Math.Max(50, (int)Math.Round(vpH * 0.435));
        double nominalSlotW = nominalTotalW / 9.0;
        int nominalHotbarH = Math.Max(16, (int)Math.Round(vpH * 0.0584));
        int bottomMargin = Math.Max(2, (int)Math.Round(vpH * 0.007));

        int defaultBottom = h - bottomMargin;
        int defaultTop = defaultBottom - nominalHotbarH;

        int hotbarTop = defaultTop;
        int hotbarBottom = defaultBottom;

        // Visual hotbar container discovery using OpenCV native InRange + Row/Col projections
        // Try to visually locate or fine-tune vertical hotbar bounds near screen center
        int scanTop = Math.Max(0, defaultTop - (int)(vpH * 0.025));
        int scanBottom = Math.Min(h - 2, defaultBottom + (int)(vpH * 0.015));
        int sliceRadius = Math.Max(8, (int)Math.Round(vpH * 0.018));
        int stripW = (sliceRadius * 2) + 1;
        int stripH = scanBottom - scanTop + 1;

        if (stripW > 0 && stripH > 0 && (midX - sliceRadius) >= 0 && (midX + sliceRadius) < w)
        {
            Rect vStripRoi = new Rect(midX - sliceRadius, scanTop, stripW, stripH);
            using var vStrip = new Mat(frame, vStripRoi);
            using var vDark = new Mat();
            // Hotbar container background is dark (R,G,B < 80)
            Cv2.InRange(vStrip, new Scalar(0, 0, 0), new Scalar(80, 80, 80), vDark);

            using var rowAvg = new Mat();
            Cv2.Reduce(vDark, rowAvg, ReduceDimension.Column, ReduceTypes.Avg, MatType.CV_32F);

            int detectedTop = -1;
            int detectedBottom = -1;
            int rowCount = rowAvg.Rows;
            for (int i = 0; i < rowCount; i++)
            {
                float val = rowAvg.At<float>(i, 0); // 0 to 255
                if (val >= 165) // 65% dark threshold
                {
                    int y = scanTop + i;
                    if (detectedTop == -1) detectedTop = y;
                    detectedBottom = y;
                }
            }

            int minHotbarH = Math.Max(12, (int)Math.Round(vpH * 0.030));
            if (detectedTop != -1 && (detectedBottom - detectedTop) >= minHotbarH)
            {
                hotbarTop = detectedTop;
                hotbarBottom = detectedBottom;
            }
        }

        // Horizontal Container Bounds:
        // Roblox CoreGui hotbar is STRICTLY HORIZONTALLY CENTERED around midX (clientW / 2).
        // Check if visual contrast scan finds a valid symmetric container, otherwise use center-anchored geometry.
        int topMargin = Math.Max(2, (int)Math.Round((hotbarBottom - hotbarTop) * 0.12));
        int scanY = Math.Clamp(hotbarTop + topMargin, hotbarTop, hotbarBottom);

        int searchRadiusX = (int)Math.Round(vpH * 0.32);
        int searchMinX = Math.Max(0, midX - searchRadiusX);
        int searchMaxX = Math.Min(w - 1, midX + searchRadiusX);
        int searchW = searchMaxX - searchMinX + 1;
        int hStripHeight = Math.Max(1, (int)Math.Round((hotbarBottom - hotbarTop) * 0.3));

        int detectedLeft = -1;
        int detectedRight = -1;

        if (searchW > 0 && scanY + hStripHeight <= h)
        {
            Rect hStripRoi = new Rect(searchMinX, scanY, searchW, hStripHeight);
            using var hStrip = new Mat(frame, hStripRoi);
            using var hDark = new Mat();
            Cv2.InRange(hStrip, new Scalar(0, 0, 0), new Scalar(80, 80, 80), hDark);

            using var colScores = new Mat();
            Cv2.Reduce(hDark, colScores, ReduceDimension.Row, ReduceTypes.Avg, MatType.CV_32F);

            int colCount = colScores.Cols;
            for (int i = 0; i < colCount; i++)
            {
                float val = colScores.At<float>(0, i);
                if (val >= 128)
                {
                    int x = searchMinX + i;
                    if (detectedLeft == -1) detectedLeft = x;
                    detectedRight = x;
                }
            }
        }

        int visualW = (detectedLeft >= 0 && detectedRight > detectedLeft) ? (detectedRight - detectedLeft + 1) : 0;
        int visualCenter = (detectedLeft + detectedRight) / 2;

        // Roblox CoreGui hotbar is strictly horizontally centered around midX (clientW / 2).
        // A visual container is accepted only if symmetric around midX within a tight margin.
        bool visualValid = visualW >= (int)(nominalTotalW * 0.80) &&
                           visualW <= (int)(nominalTotalW * 1.25) &&
                           Math.Abs(visualCenter - midX) <= Math.Max(2, (int)(vpH * 0.008));

        int totalW = visualValid ? visualW : nominalTotalW;
        int hotbarLeft = midX - (totalW / 2);
        int hotbarRight = hotbarLeft + totalW - 1;

        Rect hotbarRect = new Rect(hotbarLeft, hotbarTop, totalW, hotbarBottom - hotbarTop + 1);
        double slotW = totalW / 9.0;
        int sLeft = hotbarLeft + (int)Math.Round((slotNum - 1) * slotW);
        int sRight = hotbarLeft + (int)Math.Round(slotNum * slotW);
        Rect slotRect = new Rect(sLeft, hotbarTop, Math.Max(1, sRight - sLeft), hotbarRect.Height);
        Point center = new Point((sLeft + sRight) / 2, (hotbarTop + hotbarBottom) / 2);

        // Native OpenCV Computer Vision: Sample interior of the slot using HSV InRange
        // Inset by 12% to strictly avoid borders and background water
        int insetX = Math.Max(2, (int)Math.Round(slotRect.Width * 0.12));
        int insetY = Math.Max(2, (int)Math.Round(slotRect.Height * 0.12));
        int sampleX = Math.Clamp(slotRect.X + insetX, 0, w - 1);
        int sampleY = Math.Clamp(slotRect.Y + insetY, 0, h - 1);
        int sampleW = Math.Clamp(slotRect.Width - (2 * insetX), 1, w - sampleX);
        int sampleH = Math.Clamp(slotRect.Height - (2 * insetY), 1, h - sampleY);
        Rect sampleRoi = new Rect(sampleX, sampleY, sampleW, sampleH);

        int totalSampleArea = Math.Max(1, sampleRoi.Width * sampleRoi.Height);
        int activePx = 0;

        using (var slotInterior = new Mat(frame, sampleRoi))
        using (var hsv = new Mat())
        using (var maskCyan = new Mat())
        {
            if (slotInterior.Channels() == 4)
            {
                using var bgrInterior = new Mat();
                Cv2.CvtColor(slotInterior, bgrInterior, ColorConversionCodes.BGRA2BGR);
                Cv2.CvtColor(bgrInterior, hsv, ColorConversionCodes.BGR2HSV);
            }
            else
            {
                Cv2.CvtColor(slotInterior, hsv, ColorConversionCodes.BGR2HSV);
            }

            // Active cyan/blue selection glow (Roblox CoreGui selection highlight: H: 90-130, S: 120-255, V: 140-255)
            Cv2.InRange(hsv, new Scalar(90, 120, 140), new Scalar(130, 255, 255), maskCyan);

            // Native population count via OpenCV SIMD
            activePx = Cv2.CountNonZero(maskCyan);
        }

        double activeDensity = (double)activePx / totalSampleArea;
        int minActivePxFloor = Math.Max(4, (int)Math.Round(6 * (vpH / 1080.0)));
        bool isCyanEquipped = activeDensity >= 0.045 && activePx >= minActivePxFloor;

        // Method 2: Roblox Desktop PC White Outline Border
        // When an item is equipped on PC, Roblox draws a solid 1px white border (RGB ~255,255,255) around the slot square.
        bool isWhiteBorderEquipped = false;
        int borderPad = Math.Max(2, (int)Math.Round(vpH * 0.003));
        int slotSquareH = Math.Min(slotRect.Width, slotRect.Height);
        int squareTop = slotRect.Bottom - slotSquareH;
        int borderX = Math.Clamp(slotRect.X - borderPad, 0, w - 1);
        int borderY = Math.Clamp(squareTop - borderPad, 0, h - 1);
        int borderW = Math.Clamp(slotRect.Width + (2 * borderPad), 1, w - borderX);
        int borderH = Math.Clamp(slotSquareH + (2 * borderPad), 1, h - borderY);

        if (borderW > 10 && borderH > 10)
        {
            using var slotBoxMat = new Mat(frame, new Rect(borderX, borderY, borderW, borderH));
            using var slotBoxBgr = new Mat();
            if (slotBoxMat.Channels() == 4)
                Cv2.CvtColor(slotBoxMat, slotBoxBgr, ColorConversionCodes.BGRA2BGR);
            else
                slotBoxMat.CopyTo(slotBoxBgr);

            using var slotBoxHsv = new Mat();
            Cv2.CvtColor(slotBoxBgr, slotBoxHsv, ColorConversionCodes.BGR2HSV);

            using var maskWhite = new Mat();
            Cv2.InRange(slotBoxHsv, new Scalar(0, 0, 215), new Scalar(180, 40, 255), maskWhite);

            Cv2.FindContours(maskWhite, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
            foreach (var cnt in contours)
            {
                Rect r = Cv2.BoundingRect(cnt);
                if (r.Width >= (slotRect.Width * 0.72) && r.Height >= (slotSquareH * 0.72))
                {
                    isWhiteBorderEquipped = true;
                    break;
                }
            }
        }

        bool isEquipped = isCyanEquipped || isWhiteBorderEquipped;

        result.HotbarFound = true;
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

    /// <summary>
    /// Evaluates visual tool toggle state transitions between two frames using OpenCV temporal differential analysis (Cv2.AbsDiff).
    /// Highly resilient against any background scenery (sand, snow, water, transparent CoreGui).
    /// </summary>
    public ToolToggleResult DetectToolToggleDiff(Mat beforeFrame, Mat afterFrame, Rect targetSlotRoi, double minDeltaRatio = 0.03, bool generateDebug = false)
    {
        var result = new ToolToggleResult
        {
            SlotBounds = targetSlotRoi
        };

        if (beforeFrame == null || beforeFrame.Empty() || afterFrame == null || afterFrame.Empty())
            return result;

        int w = beforeFrame.Width;
        int h = beforeFrame.Height;

        int x = Math.Clamp(targetSlotRoi.X, 0, w - 1);
        int y = Math.Clamp(targetSlotRoi.Y, 0, h - 1);
        int slotW = Math.Clamp(targetSlotRoi.Width, 1, w - x);
        int slotH = Math.Clamp(targetSlotRoi.Height, 1, h - y);
        Rect clampedRoi = new Rect(x, y, slotW, slotH);

        result.SlotBounds = clampedRoi;
        result.TotalPixels = clampedRoi.Width * clampedRoi.Height;

        try
        {
            using var slotBefore = new Mat(beforeFrame, clampedRoi);
            using var slotAfter = new Mat(afterFrame, clampedRoi);

            // 1. Calculate absolute pixel difference across all channels
            using var diff = new Mat();
            Cv2.Absdiff(slotBefore, slotAfter, diff);

            // 2. Convert difference to grayscale
            using var diffGray = new Mat();
            if (diff.Channels() == 3)
                Cv2.CvtColor(diff, diffGray, ColorConversionCodes.BGR2GRAY);
            else if (diff.Channels() == 4)
                Cv2.CvtColor(diff, diffGray, ColorConversionCodes.BGRA2GRAY);
            else
                diff.CopyTo(diffGray);

            // 3. Threshold to ignore minor camera / sensor noise (diff intensity > 20)
            using var diffThresh = new Mat();
            Cv2.Threshold(diffGray, diffThresh, 20, 255, ThresholdTypes.Binary);

            // 4. Count changed pixels with native SIMD CountNonZero
            int changedPx = Cv2.CountNonZero(diffThresh);
            double deltaRatio = (double)changedPx / Math.Max(1, result.TotalPixels);

            result.ChangedPixels = changedPx;
            result.DeltaRatio = deltaRatio;
            result.ToggleDetected = deltaRatio >= minDeltaRatio;

            if (generateDebug)
            {
                Mat annotated = afterFrame.Clone();
                Scalar boxColor = result.ToggleDetected ? Scalar.FromRgb(0, 230, 118) : Scalar.FromRgb(255, 82, 82);
                Cv2.Rectangle(annotated, clampedRoi, boxColor, 2);

                string tag = result.ToggleDetected 
                    ? $"TOOL TOGGLE CONFIRMED ({deltaRatio * 100:F1}%)" 
                    : $"NO TOGGLE ({deltaRatio * 100:F1}%)";
                int textY = Math.Max(20, clampedRoi.Y - 6);
                int textX = Math.Max(5, clampedRoi.X - 50);
                Cv2.PutText(annotated, tag, new Point(textX, textY), HersheyFonts.HersheySimplex, 0.45, boxColor, 1);

                result.AnnotatedFrame = annotated;
            }
        }
        catch
        {
            // Graceful fallback
        }

        return result;
    }
}

