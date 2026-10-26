using System.IO;
using OpenCvSharp;

namespace FischMacroCS.Vision;

/// <summary>Pointed-marker/flared-cap structural family. All witnesses are from this image.</summary>
internal static class PointedControlDetector
{
    internal readonly record struct Observation(Rect? Body, Rect? Needle, Rect Meter, double LayoutScaleWidth, bool MeterObserved = true);
    private static readonly Lazy<Mat[]> Shapes = new(() => new[] { "pointed", "flared_left", "flared_right" }.Select(name =>
    {
        using var stream = typeof(PointedControlDetector).Assembly.GetManifestResourceStream($"FischMacroCS.Assets.ReelShapes.{name}.png")!;
        using var bytes = new MemoryStream(); stream.CopyTo(bytes);
        return Cv2.ImDecode(bytes.ToArray(), ImreadModes.Grayscale);
    }).ToArray());
    private static readonly Lazy<Mat[]> Luminance = new(() => new[] { "pointed", "flared_left", "flared_right" }.Select(name =>
    {
        using var stream = typeof(PointedControlDetector).Assembly.GetManifestResourceStream($"FischMacroCS.Assets.ReelShapes.{name}_luma.png")!;
        using var bytes = new MemoryStream(); stream.CopyTo(bytes); return Cv2.ImDecode(bytes.ToArray(), ImreadModes.Grayscale);
    }).ToArray());

    internal static Observation? Find(Mat image, Action<string>? trace = null, Rect? meterHint = null)
    {
        int imageWidth = image.Width, imageHeight = image.Height;
        if (imageWidth < 160 || imageHeight < 80) return null;
        Cv2.MeanStdDev(image,out _,out Scalar deviation);
        if(deviation.Val0==0&&deviation.Val1==0&&deviation.Val2==0)return null;
        using var gray = new Mat(); using var edges = new Mat(); using var horizontal = new Mat();
        Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
        // Most non-reel frames have no candidate meter. Reject those before
        // edge extraction; a warmed hint still goes straight to current proof.
        if(meterHint is null&&!HasPotentialMeter(gray))return null;
        Cv2.Canny(gray, edges, 5, 15);
        var edge = edges.GetUnsafeGenericIndexer<byte>(); var tones = gray.GetUnsafeGenericIndexer<byte>();
        bool Hit(int x, int y)
        {
            if (x < 1 || y < 1 || x >= imageWidth - 1 || y >= imageHeight - 1) return false;
            for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) if (edge[y + dy, x + dx] != 0) return true;
            return false;
        }
        double Shoulder(int x, int y, int width)
        {
            if (x < 1 || y < 2 || x + width >= imageWidth || y >= imageHeight - 2) return 0;
            int hits = 0;
            for (int i = 2; i < width - 2; i++)
            {
                bool ridge = false;
                for (int d = -1; d <= 1; d++)
                { int a = tones[y+d-1,x+i]-tones[y+d,x+i], b = tones[y+d+1,x+i]-tones[y+d,x+i]; ridge |= a>=3&&b>=3||a<=-3&&b<=-3; }
                if (Hit(x + i, y) || ridge) hits++;
            }
            return hits / (double)Math.Max(1, width - 4);
        }
        double Wall(int x, int y, int height)
        {
            if (x < 3 || x >= imageWidth - 3 || y < 2 || y + height >= imageHeight - 2) return 0;
            int hits = 0,minimum=255,maximum=0;
            for (int i = 2; i < height - 2; i++)
            {
                int row = y + i;
                if (!Hit(x, row)) continue;
                minimum=Math.Min(minimum,tones[row,x]);maximum=Math.Max(maximum,tones[row,x]);
                int a = tones[row, x - 3] - tones[row, x], b = tones[row, x + 3] - tones[row, x];
                if (a >= 3 && b >= 3 || a <= -3 && b <= -3) hits++;
            }
            if(maximum<minimum)return 0; // Missing current edges never mean perfect coherence.
            return hits / (double)Math.Max(1, height - 4)-(maximum-minimum)/255.0;
        }

        Observation? Read(IEnumerable<(Rect Meter,bool Observed)> proposals)
        {
        bool rejectedFamily=false;
        foreach (var candidate in proposals)
        {
            var meter=candidate.Meter;
            trace?.Invoke($"pointed meter={meter}");
            double scale = meter.Width / 749.0;
            // Measured reference spacing leaves a generous corridor around
            // both current marker tips, without scanning the unrelated header.
            var search = new Rect(Math.Max(0, meter.X - meter.Width / 2), Math.Max(0, meter.Y - (int)(meter.Width * .43)),
                Math.Min(imageWidth, meter.Width * 2), (int)(meter.Width * .26)) & new Rect(0, 0, imageWidth, imageHeight);
            if (search.Width < 1 || search.Height < 1) continue;
            Mat Template(Mat shape,int shapeIndex)
            {
                var template=new Mat();using var luminance=new Mat();using var mask=new Mat();
                var size=new Size((int)Math.Round(shape.Width*scale),(int)Math.Round(shape.Height*scale));
                Cv2.Resize(Luminance.Value[shapeIndex],luminance,size,0,0,InterpolationFlags.Linear);Cv2.Canny(luminance,template,5,15);
                Cv2.Resize(shape,mask,size,0,0,InterpolationFlags.Nearest);
                using(var dilation=Cv2.GetStructuringElement(MorphShapes.Rect,new Size(3,3)))Cv2.Dilate(mask,mask,dilation);
                Cv2.BitwiseAnd(template,mask,template);return template;
            }
            List<(Point At, double Score, bool Complete)> Matches(Mat shape, Rect roi, string name, Rect? occlusion = null)
            {
                int shapeIndex=name=="needle"?0:name=="left"?1:2;
                using var template=Template(shape,shapeIndex);
                var matches = new List<(Point, double, bool)>();
                if (template.Width > roi.Width || template.Height > roi.Height) return matches;
                using var pixels = new Mat(edges, roi); using var expanded = new Mat(); using var scores = new Mat();
                int radius=name=="needle"?1:Math.Max(1,(int)Math.Round(scale));
                using var expandKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(radius*2+1,radius*2+1));
                Cv2.Dilate(pixels, expanded, expandKernel);
                using var nominationTemplate=new Mat();
                if(name!="needle"&&radius>1)Cv2.Dilate(template,nominationTemplate,expandKernel);
                for(int pass=0;pass<(name!="needle"&&radius>1?2:1);pass++)
                {
                // Preserve strict matching first. A cap nomination may also
                // use the already measured spatial support image; acceptance
                // still uses every unchanged original regional proof. The
                // target never gains this alternative nomination.
                Cv2.MatchTemplate(pass==0?pixels:expanded, pass==0?template:nominationTemplate, scores, TemplateMatchModes.CCorrNormed);
                for (int i = 0; i < 4; i++)
                {
                    Cv2.MinMaxLoc(scores, out _, out double score, out _, out Point at);
                    trace?.Invoke($"pointed {name} score={score:F3},at={at + roi.TopLeft}");
                    if (!double.IsFinite(score) || score < .38) break;
                    var tp=template.GetUnsafeGenericIndexer<byte>(); var ep=expanded.GetUnsafeGenericIndexer<byte>();
                    int templateWidth=template.Width,templateHeight=template.Height;
                    (int[] Counts,int[] Hits,double Quality) Support(Point proposal)
                    {
                        var counts=new int[4];var hits=new int[4];
                        for(int y=0;y<templateHeight;y++)for(int x=0;x<templateWidth;x++)if(tp[y,x]!=0)
                        {
                            if(occlusion is Rect mask&&new Rect(mask.X-2,mask.Y-2,mask.Width+4,mask.Height+4).Contains(proposal+roi.TopLeft+new Point(x,y)))continue;
                            int group=name=="needle" ? y<templateHeight*.22?0:y>templateHeight*.80?1:x<templateWidth/2?2:3
                                :(y<templateHeight/2?0:2)+(x<templateWidth/2?0:1);
                            counts[group]++;if(ep[proposal.Y+y,proposal.X+x]!=0)hits[group]++;
                        }
                        double Ratio(int g)=>hits[g]/(double)Math.Max(1,counts[g]);
                        double quality=name=="needle"?Math.Min(Math.Min(Ratio(0),Ratio(1)),Math.Max(Ratio(2),Ratio(3)))
                            :Enumerable.Range(0,4).Min(Ratio);
                        return(counts,hits,quality);
                    }
                    // Correlation only nominates a raster location. Refine
                    // that one proposal within one original pixel using the
                    // unchanged independent regional proof, not background
                    // correlation. The regional acceptance threshold remains
                    // unchanged; cap distance is measured in current UI units.
                    var support=Support(at);var nominated=at;
                    for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                    {
                        var proposal=nominated+new Point(dx,dy);
                        if(proposal.X<0||proposal.Y<0||proposal.X>=scores.Width||proposal.Y>=scores.Height)continue;
                        var alternative=Support(proposal);
                        if(alternative.Quality>support.Quality){at=proposal;support=alternative;}
                    }
                    var counts=support.Counts;var hits=support.Hits;
                    trace?.Invoke($"pointed {name} region support={string.Join(',',hits)}/{string.Join(',',counts)}");
                    if(trace is not null&&name=="right"&&hits[3]<counts[3]*.9)
                    {
                        var missing=new List<Point>();
                        for(int y=templateHeight/2;y<templateHeight;y++)for(int x=templateWidth/2;x<templateWidth;x++)
                            if(tp[y,x]!=0&&ep[at.Y+y,at.X+x]==0&&!(occlusion is Rect mask&&new Rect(mask.X-2,mask.Y-2,mask.Width+4,mask.Height+4).Contains(at+roi.TopLeft+new Point(x,y))))missing.Add(at+roi.TopLeft+new Point(x,y));
                        trace($"pointed missing right corner={string.Join(';',missing)}");
                    }
                    bool supported=Enumerable.Range(0,4).All(g=>counts[g]>0&&hits[g]>=counts[g]*.90);
                    // A marker may join a CURRENT body at mid-height. This
                    // nominates only; after finding that body all exposed
                    // marker regions must independently pass the full proof.
                    if(name=="needle")supported=hits[0]>=counts[0]*.90&&hits[1]>=counts[1]*.90
                        &&Math.Max(hits[2]/(double)Math.Max(1,counts[2]),hits[3]/(double)Math.Max(1,counts[3]))>=.90;
                    if(supported||name!="needle"&&score>=.50)matches.Add((at + roi.TopLeft, score,supported));
                    var remove = new Rect(at.X - template.Width / 2, at.Y - template.Height / 2, template.Width, template.Height)
                        & new Rect(0, 0, scores.Width, scores.Height);
                    using var suppress = new Mat(scores, remove); suppress.SetTo(Scalar.All(-1));
                }
                if(matches.Any(match=>match.Item3))break;
                }
                return matches;
            }
            var needles = Matches(Shapes.Value[0], search, "needle");
            if (needles.Count == 0) needles.Add((new Point(0,0),0,false));
            foreach (var needleMatch in needles)
            {
                bool hasNeedle=needleMatch.Score>0;
                var needle = new Rect(needleMatch.At.X, needleMatch.At.Y + (int)Math.Round(5 * scale),
                    (int)Math.Round(34 * scale), (int)Math.Round(116 * scale));
                // The independent progress meter is below this family's
                // complete target. A top-clipped search can otherwise extend
                // below a scenery panel and match grain as a tiny needle/cap,
                // incorrectly suppressing ordinary control recovery.
                if(hasNeedle&&needle.Bottom>=meter.Top)continue;
                var bodySearch = hasNeedle ? new Rect(search.X, needleMatch.At.Y, search.Width, (int)Math.Round(126 * scale)) & new Rect(0, 0, imageWidth, imageHeight):search;
                var left = Matches(Shapes.Value[1], bodySearch, "left",hasNeedle?needle:null); var right = Matches(Shapes.Value[2], bodySearch, "right",hasNeedle?needle:null);
                // A CURRENT pointed target plus an independently complete,
                // vertically aligned flared cap identifies this family even
                // when its opposite control end is unavailable. Keep trying
                // every complete pair, but do not reinterpret that ambiguity
                // as an ordinary component in the expensive legacy fallback.
                // This publishes neither a bar nor a target/live observation.
                if(hasNeedle&&left.Concat(right).Any(cap=>cap.Complete
                    &&Math.Abs(cap.At.Y+Math.Round(6*scale)+Math.Round(78*scale)/2.0
                        -needle.Y-needle.Height/2.0)<=Math.Max(2,6*scale)))rejectedFamily=true;
                foreach (var l in left) foreach (var r in right)
                {
                    if ((!l.Complete&&!r.Complete)||Math.Abs(l.At.Y - r.At.Y) > 2 * scale || r.At.X - l.At.X < 150 * scale) continue;
                    var body = new Rect(l.At.X + (int)Math.Round(5 * scale), l.At.Y + (int)Math.Round(6 * scale),
                        r.At.X - l.At.X + (int)Math.Round(84 * scale), (int)Math.Round(78 * scale));
                    bool Covered(int x)=>hasNeedle&&x>=needle.Left&&x<=needle.Right&&needle.Top<=body.Top&&needle.Bottom>=body.Bottom;
                    if(!l.Complete&&!Covered(body.Left)||!r.Complete&&!Covered(body.Right))continue;
                    using var markerTemplate=Template(Shapes.Value[0],0);
                    var originalShape=markerTemplate.GetUnsafeGenericIndexer<byte>();
                    int markerWidth=markerTemplate.Width,markerHeight=markerTemplate.Height;
                    bool MarkerSupported(Point at)
                    {
                        var exposed=new int[4];var corroborated=new int[4];
                        for(int y=0;y<markerHeight;y++)for(int x=0;x<markerWidth;x++)if(originalShape[y,x]!=0)
                        {
                            var p=at+new Point(x,y);if(body.Contains(p))continue;
                            int group=y<markerHeight*.22?0:y>markerHeight*.80?1:x<markerWidth/2?2:3;
                            exposed[group]++;if(Hit(p.X,p.Y))corroborated[group]++;
                        }
                        trace?.Invoke($"pointed exposed marker support={string.Join(',',corroborated)}/{string.Join(',',exposed)}");
                        return !Enumerable.Range(0,4).Any(g=>(g<2&&exposed[g]<4)||corroborated[g]<exposed[g]*.90);
                    }
                    bool markerSupported=!hasNeedle||MarkerSupported(needleMatch.At);
                    // The ornament's nested ridges do not prove full stationary
                    // endpoints. Keep four independently exposed shoulder
                    // corridors as context and publish NO complete TrackBounds.
                    // Each corridor is one original row, never a contour bbox.
                    bool ExposedSide(bool right)
                    {
                        int length=Math.Max(8,(int)(body.Height*.50));
                        int x=right?body.Right+3:body.Left-length-3;
                        double upper=0,lower=0;
                        for(int d=(int)(body.Height*.04);d<=body.Height*.26;d++)
                        {upper=Math.Max(upper,Shoulder(x,body.Top+d,length));lower=Math.Max(lower,Shoulder(x,body.Bottom-d,length));}
                        trace?.Invoke($"pointed exposed side={right}: {upper:F3}/{lower:F3}");
                        return upper>=.85&&lower>=.85;
                    }
                    if(!ExposedSide(false)||!ExposedSide(true))continue;
                    // Current flared caps, their exposed rail corridors and
                    // a pointed-marker nomination identify an ambiguous
                    // family when final context proof fails. Search every
                    // strict candidate first, then reject; never let legacy
                    // components turn this uncertainty into live geometry.
                    if(hasNeedle&&l.Complete&&r.Complete&&(!candidate.Observed||!markerSupported))rejectedFamily=true;
                    if(!markerSupported)continue;
                    if(!candidate.Observed&&(!hasNeedle||!l.Complete||!r.Complete))continue;
                    return new(l.Complete&&r.Complete?body:null,hasNeedle?needle:null,meter,meter.Width*(1390.0/749),candidate.Observed);
                }
            }
        }
        return rejectedFamily?new(null,null,default,0,false):null;

        }
        bool CurrentMeter(Rect m)
        {
            int h=m.Height,left=m.Left,right=m.Right,top=m.Top,corner=Math.Max(8,h);
            if(left<3||right>=imageWidth-3||top<2||m.Bottom>=imageHeight-2)return false;
            if(Shoulder(left,top,m.Width)<.93||Shoulder(left,m.Bottom,m.Width)<.93
                ||Wall(left,top,h)<.75||Wall(right,top,h)<.75)return false;
            if(Shoulder(left,top,corner)<.90||Shoulder(left,m.Bottom,corner)<.90
                ||Shoulder(right-corner,top,corner)<.90||Shoulder(right-corner,m.Bottom,corner)<.90)return false;
            bool Continues(int wall,int row,int direction)
            {
                int length=Math.Max(6,(int)Math.Round(h*1.4));
                for(int dy=-1;dy<=1;dy++)
                {
                    int count=0;
                    for(int d=3;d<length+3;d++) {int x=wall+direction*d;if(x>=0&&x<imageWidth&&row+dy>=0&&row+dy<imageHeight&&edge[row+dy,x]!=0)count++;}
                    if(count>=length*.85)return true;
                }
                return false;
            }
            return !Continues(left,top,-1)&&!Continues(left,m.Bottom,-1)&&!Continues(right,top,1)&&!Continues(right,m.Bottom,1);
        }
        // Previous geometry nominates one search only. Every original meter,
        // marker and cap witness is checked in this image before returning.
        if(meterHint is Rect hint&&CurrentMeter(hint))
        {
            var warmed=Read(new[]{(Meter:hint,Observed:true)});
            if(warmed.HasValue)return warmed;
        }
        if(meterHint is not null&&!HasPotentialMeter(gray))return null;
        using var ridges = new Mat(); using var dark = new Mat(); using var light = new Mat();
        using var ridgeKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(1, 5));
        Cv2.MorphologyEx(gray, dark, MorphTypes.BlackHat, ridgeKernel); Cv2.MorphologyEx(gray, light, MorphTypes.TopHat, ridgeKernel);
        Cv2.Max(dark, light, ridges); Cv2.Threshold(ridges, ridges, 6, 255, ThresholdTypes.Binary);
        Cv2.BitwiseOr(edges, ridges, horizontal);
        // A one-pixel raster gap may split a real shoulder nomination. Closing
        // is only for proposals; every accepted corridor is rechecked against
        // the untouched original edges/contrast below.
        using(var join=Cv2.GetStructuringElement(MorphShapes.Rect,new Size(3,1)))
            Cv2.MorphologyEx(horizontal,horizontal,MorphTypes.Close,join);
        using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(31, 1));
        Cv2.MorphologyEx(horizontal, horizontal, MorphTypes.Open, kernel);
        List<Rect> Runs(Mat opened)
        {
            var pixels=opened.GetUnsafeGenericIndexer<byte>();var runs=new List<Rect>();
            for(int y=0;y<imageHeight;y++)for(int x=0;x<imageWidth;)
            {if(pixels[y,x]==0){x++;continue;}int start=x;while(x<imageWidth&&pixels[y,x]!=0)x++;if(x-start>=120)runs.Add(new Rect(start,y,x-start,1));}
            var kept=new List<Rect>();foreach(var run in runs.OrderByDescending(r=>r.Width))
                if(!kept.Any(r=>Math.Abs(r.Left-run.Left)<=2&&Math.Abs(r.Right-run.Right)<=2&&Math.Abs(r.Y-run.Y)<=2))
                {kept.Add(run);if(kept.Count==64)break;}
            return kept;
        }
        using var rawHorizontal=new Mat();Cv2.MorphologyEx(edges,rawHorizontal,MorphTypes.Open,kernel);
        var rawLines=Runs(rawHorizontal).ToArray();
        var lines = Runs(horizontal).Concat(rawLines).Distinct().Where(r => r.Width >= 120 && r.Height <= 3)
            .OrderByDescending(r => r.Width).Take(32).ToList();
        // Keep the current ridge-only shoulders separate from Canny texture.
        // Combining them first can extend a genuine meter into grain. These
        // bounded extra nominations retain every original cap/corner/body
        // proof below; they neither widen a wall search nor establish context.
        using var ridgeHorizontal=new Mat();Cv2.MorphologyEx(ridges,ridgeHorizontal,MorphTypes.Open,kernel);
        var ridgeLines=Runs(ridgeHorizontal).Take(16).ToArray();
        foreach(var ridge in ridgeLines)if(!lines.Contains(ridge))lines.Add(ridge);
        trace?.Invoke("pointed independent ridge rows="+string.Join(';',ridgeLines.Select(r=>$"{r.X},{r.Y},{r.Width}")));
        var panelHeights=new Dictionary<Rect,HashSet<int>>();
        using(var range=new Mat())using(var flat=new Mat())using(var opened=new Mat())
        using(var vertical=Cv2.GetStructuringElement(MorphShapes.Rect,new Size(1,3)))
        {
            // Group complete CURRENT flat row corridors, rather than a
            // contour bbox that can include an unrelated vertical filament.
            // Their endpoints and height are nominations only. In particular,
            // a flat erasure cannot substitute for an original outlined cap.
            Cv2.MorphologyEx(gray,range,MorphTypes.Gradient,vertical);Cv2.Compare(range,Scalar.All(3),flat,CmpTypes.LE);
            Cv2.MorphologyEx(flat,opened,MorphTypes.Open,kernel);
            var p=opened.GetUnsafeGenericIndexer<byte>();
            var groups=new List<(double Left,double Right,int Top,int Bottom,int Count)>();
            var panels=new List<(double Left,double Right,int Top,int Bottom,int Count)>();
            void KeepPanel((double Left,double Right,int Top,int Bottom,int Count) g)
            {
                if(g.Bottom-g.Top<3||g.Count<(g.Bottom-g.Top+1)*.8||g.Right-g.Left<(g.Bottom-g.Top+1)*20
                    ||g.Right-g.Left>(g.Bottom-g.Top+1)*100)return;
                panels.Add(g);panels.Sort((a,b)=>((b.Right-b.Left)*b.Count).CompareTo((a.Right-a.Left)*a.Count));
                if(panels.Count>4)panels.RemoveAt(4);
            }
            for(int y=0;y<imageHeight;y++)
            {
                // Only the preceding two rows can join this row. Keep four
                // finished nominations, rather than scan stale groups or
                // accumulate an image-sized history of texture fragments.
                for(int i=groups.Count-1;i>=0;i--)if(y-groups[i].Bottom>2){KeepPanel(groups[i]);groups.RemoveAt(i);}
                for(int x=0;x<imageWidth;)
                {
                if(p[y,x]==0){x++;continue;}int left=x;while(x<imageWidth&&p[y,x]!=0)x++;
                if(x-left<120)continue;
                double tolerance=Math.Max(2,(x-left)*.006);int found=-1;double closest=double.MaxValue;
                for(int i=0;i<groups.Count;i++)
                {
                    var g=groups[i];double delta=Math.Abs(g.Left-left)+Math.Abs(g.Right-x);
                    if(y-g.Bottom<=2&&Math.Abs(g.Left-left)<=tolerance&&Math.Abs(g.Right-x)<=tolerance&&delta<closest)
                    {found=i;closest=delta;}
                }
                if(found<0)groups.Add((left,x,y,y,1));
                else {var g=groups[found];groups[found]=((g.Left*g.Count+left)/(g.Count+1),(g.Right*g.Count+x)/(g.Count+1),g.Top,y,g.Count+1);}
                }
            }
            foreach(var g in groups)KeepPanel(g);
            foreach(var g in panels)
            {
                int left=(int)Math.Round(g.Left),width=(int)Math.Round(g.Right)-left;
                int border=Math.Max(2,(int)Math.Round(width/749.0)+1);
                trace?.Invoke($"pointed flat corridor={left},{g.Top}..{g.Bottom},width={width}");
                for(int upper=1;upper<=border;upper++)for(int lower=1;lower<=border;lower++)
                {
                    var line=new Rect(left,g.Top-upper,width,1);int h=g.Bottom+lower-line.Y;
                    if(line.Y<2||line.Y+h>=imageHeight-2||h<7)continue;
                    if(!panelHeights.TryGetValue(line,out var heights)){panelHeights[line]=heights=new();if(!lines.Contains(line))lines.Add(line);}
                    heights.Add(h);
                }
            }
        }
        var meters = new List<Rect>();
        var panelHints = new List<Rect>();
        IEnumerable<int> CurrentHeights(Rect line)
        {
            var proposed=new HashSet<int>();
            for(int height=Math.Max(7,(int)Math.Ceiling(line.Width*.021));height<=line.Width*.032;height++)
                if(proposed.Add(height))yield return height;
            if(panelHeights.TryGetValue(line,out var measured))foreach(int height in measured)
                if(proposed.Add(height))yield return height;
        }
        foreach (var line in lines)
            foreach(int h in CurrentHeights(line))
                foreach (int direction in new[] { -1, 1 })
                {
                    int top = direction < 0 ? line.Y - h : line.Y;
                    if (top < 2 || top + h >= imageHeight - 2 || Shoulder(line.X, top, line.Width) < .93
                        || Shoulder(line.X, top + h, line.Width) < .93) continue;
                    // A connected shoulder nomination can include a scenery
                    // continuation. Re-find the panel's CURRENT end planes
                    // from vertical interior coherence, independently of hue
                    // and of a moving fill's intensity. This is a proposal;
                    // original shoulders and both outlined caps are re-proved.
                    int bestStart=-1,bestLength=0,start=-1;
                    for(int x=Math.Max(3,line.Left-h*3);x<=Math.Min(imageWidth-4,line.Right+h*3);x++)
                    {
                        int lo=255,hi=0;
                        for(int y=top+2;y<top+h-1;y++){lo=Math.Min(lo,tones[y,x]);hi=Math.Max(hi,tones[y,x]);}
                        bool flat=hi-lo<=3;
                        if(flat&&start<0)start=x;
                        if(!flat&&start>=0){if(x-start>bestLength){bestStart=start;bestLength=x-start;}start=-1;}
                    }
                    if(start>=0){int length=Math.Min(imageWidth-4,line.Right+h*3)-start+1;if(length>bestLength){bestStart=start;bestLength=length;}}
                    var planes=new List<(int Left,int Right)>{(line.Left,line.Right-1)};
                    if(bestLength>=line.Width*.8&&bestStart!=line.Left)planes.Add((bestStart,bestStart+bestLength-1));
                    foreach(var plane in planes)
                    {
                    int planeWidth=plane.Right-plane.Left+1;
                    if(Shoulder(plane.Left,top,planeWidth)<.93||Shoulder(plane.Left,top+h,planeWidth)<.93)continue;
                    // Incomplete panels may propose scale for identifying the
                    // CURRENT structural family, never establish reel context.
                    // A proved family with a missing meter must not fall into
                    // legacy component interpretation as an ordinary bar.
                    var panelHint=new Rect(plane.Left,top,planeWidth-1,h);
                    if(!panelHints.Any(p=>Math.Abs(p.X-panelHint.X)<4&&Math.Abs(p.Y-panelHint.Y)<4
                        &&Math.Abs(p.Width-panelHint.Width)<4))panelHints.Add(panelHint);
                    // Moving progress fill is a step, not an independently
                    // outlined end. Require an original ridge on BOTH caps.
                    int left = Enumerable.Range(Math.Max(3, plane.Left - 2), 5).MaxBy(x => Wall(x, top, h));
                    int right = Enumerable.Range(Math.Min(imageWidth - 8, plane.Right - 2), 5).MaxBy(x => Wall(x, top, h));
                    trace?.Invoke($"pointed meter proposal={line.X},{top},{line.Width},{h}: caps={left}/{right},wall={Wall(left,top,h):F2}/{Wall(right,top,h):F2}");
                    if (Wall(left, top, h) < .75 || Wall(right, top, h) < .75) continue;
                    // Whole-width support can hide a short erased end. Each
                    // current wall must actually meet BOTH original shoulders
                    // in its own local corner corridor, before it is an end.
                    int corner=Math.Max(8,h);
                    if(Shoulder(left,top,corner)<.90||Shoulder(left,top+h,corner)<.90
                        ||Shoulder(right-corner,top,corner)<.90||Shoulder(right-corner,top+h,corner)<.90)continue;
                    bool Continues(int wall,int row,bool right)
                    {
                        int length=Math.Max(6,(int)Math.Round(h*1.4));
                        for(int dy=-1;dy<=1;dy++)
                        {
                            int supported=0;
                            for(int d=3;d<length+3;d++)
                            {int x=wall+(right?d:-d);if(x>=0&&x<imageWidth&&row+dy>=0&&row+dy<imageHeight&&edge[row+dy,x]!=0)supported++;}
                            if(supported>=length*.85)return true;
                        }
                        return false;
                    }
                    bool leftTop=Continues(left,top,false),leftBottom=Continues(left,top+h,false),
                        rightTop=Continues(right,top,true),rightBottom=Continues(right,top+h,true);
                    trace?.Invoke($"pointed meter outside continuation={leftTop}/{leftBottom}/{rightTop}/{rightBottom}");
                    if(leftTop||leftBottom||rightTop||rightBottom)continue;
                    var meter = new Rect(left, top, right - left, h);
                    if (!meters.Any(m => Math.Abs(m.X - meter.X) < 4 && Math.Abs(m.Y - meter.Y) < 4)) meters.Add(meter);
                    break; // Preserve a proved original outline before an alternative proposal.
                    }
                }

        return Read(meters.OrderByDescending(m=>m.Width).Take(4).Select(m=>(Meter:m,Observed:true))
            .Concat(panelHints.OrderByDescending(m=>m.Width).Take(4).Select(m=>(Meter:m,Observed:false))));
    }

    private static bool HasPotentialMeter(Mat gray)
    {
        // Only a proposal gate: every accepted meter still passes the original
        // wall, corner and shoulder proofs. No hue or desktop scale is used.
        using var gradient=new Mat();using var flat=new Mat();
        using var vertical=Cv2.GetStructuringElement(MorphShapes.Rect,new Size(1,3));
        using var horizontal=Cv2.GetStructuringElement(MorphShapes.Rect,new Size(31,1));
        Cv2.MorphologyEx(gray,gradient,MorphTypes.Gradient,vertical);
        Cv2.Compare(gradient,Scalar.All(3),flat,CmpTypes.LE);
        Cv2.MorphologyEx(flat,flat,MorphTypes.Open,horizontal);
        Cv2.FindContours(flat,out Point[][] contours,out _,RetrievalModes.List,ContourApproximationModes.ApproxSimple);
        return contours.Select(Cv2.BoundingRect).Any(r=>r.Width>=120&&r.Height>=3&&r.Width/(double)r.Height is >=20 and <=150);
    }
}
