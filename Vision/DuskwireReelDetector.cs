using OpenCvSharp;

namespace FischMacroCS.Vision;

/// <summary>Duskwire's opaque narrow body and white capsule are measured independently.</summary>
internal static class DuskwireReelDetector
{
    internal readonly record struct Observation(Rect Body,Rect Needle);

    internal static Observation? Find(Mat crop,int offsetY,double height,bool shifted=false)
    {
        int top=Math.Max(0,(int)(height*(shifted?.74:.80))-offsetY), bottom=Math.Min(crop.Height,(int)(height*(shifted?.98:.915))-offsetY);
        if(bottom<=top)return null;
        using var band=new Mat(crop,new Rect(0,top,crop.Width,bottom-top));
        using var hsv=new Mat();using var white=new Mat();
        Cv2.CvtColor(band,hsv,ColorConversionCodes.BGR2HSV);
        Cv2.InRange(hsv,new Scalar(0,0,220),new Scalar(179,45,255),white);
        using var markerStem=Cv2.GetStructuringElement(MorphShapes.Rect,new Size(1,Math.Max(3,(int)(height*.065))));
        Cv2.MorphologyEx(white,white,MorphTypes.Open,markerStem);
        Cv2.FindContours(white,out Point[][] contours,out _,RetrievalModes.External,ContourApproximationModes.ApproxSimple);
        var needles=contours.Select(Cv2.BoundingRect).Where(r=>
            r.Width>=height*.006 && r.Width<=height*.018 && r.Height>=height*.07 && r.Height<=height*.095 &&
            Math.Abs(offsetY+top+r.Y+r.Height/2.0-height*.853)<height*(shifted?.075:.012)).ToArray();
        if(needles.Length!=1)return null;
        // The body changes between tinted dark and neutral gradients. Its upper
        // and lower strips flank the musical note and extend beyond the rail.
        // Locate both from this frame's marker, including the entry animation.
        using var dark=new Mat();using var red=new Mat();using var black=new Mat();
        Cv2.InRange(hsv,new Scalar(0,0,0),new Scalar(179,20,90),dark);
        Cv2.InRange(hsv,new Scalar(0,0,0),new Scalar(90,255,40),red);
        Cv2.InRange(hsv,new Scalar(0,0,0),new Scalar(179,255,5),black);
        Cv2.BitwiseOr(dark,red,dark);Cv2.BitwiseOr(dark,black,dark);
        var needle=needles[0];
        int upper=needle.Y+(int)(needle.Height*.25),lower=needle.Bottom-(int)(needle.Height*.25);
        int thickness=Math.Max(1,(int)(height*.0015));
        if(upper<0 || lower+2>=band.Height)return null;
        using var upperStrip=new Mat(dark,new Rect(0,upper,band.Width,thickness));
        using var lowerStrip=new Mat(dark,new Rect(0,lower,band.Width,thickness));
        using var upperColumns=new Mat();using var lowerColumns=new Mat();
        Cv2.Reduce(upperStrip,upperColumns,ReduceDimension.Row,ReduceTypes.Min,-1);
        Cv2.Reduce(lowerStrip,lowerColumns,ReduceDimension.Row,ReduceTypes.Min,-1);
        Cv2.BitwiseAnd(upperColumns,lowerColumns,upperColumns);
        using var flankingColumns=upperColumns.Clone();
        // Entry can highlight the outline while leaving the interior unchanged.
        // Prefer the stronger outline comparison; retry inside only if needed.
        foreach(bool highlighted in new[]{false,true})
        {
            flankingColumns.CopyTo(upperColumns);
            // The opaque gradient stays the same vertically. Pool stripes and
            // particles behind the translucent rail do not. White glyph pixels
            // are allowed within the body, but cannot supply its flanking strips.
            using var upperColor=new Mat(band,new Rect(0,highlighted?needle.Y+(int)(needle.Height*.32):upper,band.Width,thickness));
            using var difference=new Mat();using var flat=new Mat();using var flatColumns=new Mat();
            if(!highlighted)
            {
                using var lowerColor=new Mat(band,new Rect(0,lower,band.Width,thickness));
                Cv2.Absdiff(upperColor,lowerColor,difference);
                Cv2.InRange(difference,new Scalar(0,0,0),new Scalar(3,3,3),flat);
                Cv2.Reduce(flat,flatColumns,ReduceDimension.Row,ReduceTypes.Min,-1);
                Cv2.BitwiseAnd(upperColumns,flatColumns,upperColumns);
            }
            foreach(double fraction in highlighted?new[]{.40,.60,.72}:new[]{.40,.60})
            {
                using var middle=new Mat(band,new Rect(0,needle.Y+(int)(needle.Height*fraction),band.Width,thickness));
                using var glyph=new Mat();
                Cv2.InRange(middle,new Scalar(64,64,64),new Scalar(255,255,255),glyph);
                Cv2.Absdiff(upperColor,middle,difference);
                Cv2.InRange(difference,new Scalar(0,0,0),new Scalar(3,3,3),flat);
                Cv2.BitwiseOr(flat,glyph,flat);
                Cv2.Reduce(flat,flatColumns,ReduceDimension.Row,ReduceTypes.Min,-1);
                Cv2.BitwiseAnd(upperColumns,flatColumns,upperColumns);
            }
            using var bridge=Cv2.GetStructuringElement(MorphShapes.Rect,new Size(Math.Max(3,(int)(height*(highlighted?.018:.030))),1));
            using var joined=new Mat();
            Cv2.MorphologyEx(upperColumns,joined,MorphTypes.Close,bridge);
            Cv2.FindContours(joined,out Point[][] bodies,out _,RetrievalModes.External,ContourApproximationModes.ApproxSimple);
            var candidates=bodies.Select(Cv2.BoundingRect).Where(r=>
            {
                if(r.Width<height*.035 || r.Width>height*.20 || r.Left<=0 || r.Right>=crop.Width)return false;
                using var evidence=new Mat(upperColumns,r);
                return Cv2.CountNonZero(evidence)/(double)r.Width>=(highlighted?.65:.70);
            }).ToArray();
            if(candidates.Length!=1)continue;
            var best=candidates[0];
            return new Observation(new Rect(best.X,upper+top,best.Width,lower-upper+2),
                new Rect(needles[0].X,needles[0].Y+top,needles[0].Width,needles[0].Height));
        }
        return null;
    }
}
