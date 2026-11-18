using System;
using System.Collections.Generic;
using System.Globalization;

namespace CLV_CivilTools.Ufls
{
    // Drawing-unit geometry only; Civil host supplies native dimensions in feet.
    internal static class PipeStakeMath
    {
        internal readonly record struct XY(double X, double Y);
        internal sealed record Projection(XY Foot, double Fraction, double Along, double Length, double Offset, bool Clamped);
        private const double TwoPi = 2 * Math.PI;
        private static void Valid(XY p)
        { if (!double.IsFinite(p.X) || !double.IsFinite(p.Y)) throw new ArgumentException("Non-finite plan coordinate."); }
        private static double Distance(XY a, XY b) => Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y));
        private static double PositiveAngle(double angle) => (angle % TwoPi + TwoPi) % TwoPi;
        internal static Projection Line(XY start, XY end, XY pick)
        {
            Valid(start); Valid(end); Valid(pick);
            double dx=end.X-start.X, dy=end.Y-start.Y, length=Distance(start,end);
            if (!double.IsFinite(length) || length <= 1e-8) throw new ArgumentException("Pipe has zero/invalid plan length.");
            double raw=((pick.X-start.X)*dx+(pick.Y-start.Y)*dy)/(length*length);
            double t=Math.Clamp(raw,0,1);
            XY foot=new(start.X+t*dx,start.Y+t*dy);
            return new(foot,t,length*t,length,Distance(pick,foot),raw < -1e-10 || raw > 1+1e-10);
        }
        internal static Projection Arc(XY start, XY end, XY center, bool clockwise, double sweep, XY pick)
        {
            Valid(start); Valid(end); Valid(center); Valid(pick);
            double radius=Distance(start,center), endRadius=Distance(end,center);
            if (!double.IsFinite(radius) || radius <= 1e-8 || !double.IsFinite(sweep) || sweep <= 1e-10 || sweep >= TwoPi-1e-10)
                throw new ArgumentException("Invalid/closed pipe plan arc.");
            double tolerance=Math.Max(1e-6,radius*1e-8), sign=clockwise ? -1 : 1;
            double theta=Math.Atan2(start.Y-center.Y,start.X-center.X);
            XY expected=new(center.X+radius*Math.Cos(theta+sign*sweep),center.Y+radius*Math.Sin(theta+sign*sweep));
            if (Math.Abs(radius-endRadius)>tolerance || Distance(expected,end)>tolerance)
                throw new ArgumentException("Native pipe arc orientation/endpoints do not agree.");
            if (Distance(pick,center)<=tolerance) throw new ArgumentException("Point is at the pipe arc center; perpendicular station is ambiguous.");
            double angle=PositiveAngle(sign*(Math.Atan2(pick.Y-center.Y,pick.X-center.X)-theta));
            if (TwoPi-angle < 1e-10) angle=0;
            if (angle <= sweep+1e-10)
            {
                angle=Math.Clamp(angle,0,sweep);
                XY foot=new(center.X+radius*Math.Cos(theta+sign*angle),center.Y+radius*Math.Sin(theta+sign*angle));
                return new(foot,angle/sweep,angle*radius,sweep*radius,Distance(pick,foot),false);
            }
            double a=Distance(pick,start),b=Distance(pick,end);
            if (Math.Abs(a-b)<=tolerance) throw new ArgumentException("Point has two equally near pipe ends; choose a different pipe/location.");
            bool first=a<b;
            return new(first?start:end,first?0:1,first?0:sweep*radius,sweep*radius,first?a:b,true);
        }
        internal static (double Invert,double? Top) Elevations(double startZ,double endZ,double insideHeight,double? wall,double fraction)
        {
            if (!double.IsFinite(startZ) || !double.IsFinite(endZ) || !double.IsFinite(insideHeight) || insideHeight<=0
                || !double.IsFinite(fraction) || fraction<0 || fraction>1)
                throw new ArgumentException("Invalid native pipe elevation/inside height.");
            double invert=startZ+(endZ-startZ)*fraction-insideHeight/2;
            double? top=wall.HasValue && double.IsFinite(wall.Value) && wall.Value>0 ? invert+insideHeight+wall.Value : null;
            return(invert,top);
        }
        internal sealed record LabelValues(string Point,string Pipe,string Size,string Material,double Invert,double? Top,double? Wall,
            double Offset,double Along,bool Clamped,string? Alignment,string? Station,string? StationWarning);
        internal static string Escape(string text) => text.Replace("\\","\\\\").Replace("{","\\{").Replace("}","\\}").Replace("\r"," ").Replace("\n"," ");
        internal static string N(double n,string format="0.000") => n.ToString(format,CultureInfo.InvariantCulture);
        internal static string Label(LabelValues v)
        {
            var lines=new List<string> { "CHECK: "+Escape(v.Point),"PIPE: "+Escape(v.Pipe),"SIZE: "+Escape(v.Size),"MATERIAL: "+Escape(v.Material) };
            if (v.Alignment!=null) lines.Add("PIPE STA ["+Escape(v.Alignment)+"]: "+Escape(v.Station ?? "UNAVAILABLE"));
            else lines.Add("PIPE DIST: "+N(v.Along,"0.00")+" ft from start");
            lines.Add((v.Clamped?"DIST TO PIPE END: ":"OFFSET FROM PIPE CL: ")+N(v.Offset,"0.00")+" ft");
            lines.Add("INVERT: "+N(v.Invert)+" ft");
            lines.Add("TOP (OUTSIDE): "+(v.Top.HasValue?N(v.Top.Value)+" ft":"UNKNOWN - wall/slab unavailable"));
            lines.Add("WALL/SLAB: "+(v.Wall.HasValue?N(v.Wall.Value*12)+" in":"UNKNOWN"));
            if(v.Clamped) lines.Add("{\\C1;WARNING: BEYOND PIPE LIMITS - NEAREST END VALUES}");
            if(v.StationWarning!=null) lines.Add("{\\C1;"+Escape(v.StationWarning)+"}");
            return string.Join("\\P",lines);
        }
    }
}
