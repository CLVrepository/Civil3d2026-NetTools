using System;
using System.Collections.Generic;
using System.Linq;

namespace CLV_CivilTools.PointCloud
{
    public readonly record struct GridXY(double X, double Y);
    public readonly record struct GridNode(int Column, int Row, double X, double Y);
    public sealed record CloudGridFrame(double OriginX, double OriginY, double AxisX, double AxisY, double Spacing)
    {
        public GridXY Local(GridXY p) => new((p.X - OriginX) * AxisX + (p.Y - OriginY) * AxisY,
            -(p.X - OriginX) * AxisY + (p.Y - OriginY) * AxisX);
        public GridXY World(double u, double v) => new(OriginX + u * AxisX - v * AxisY, OriginY + u * AxisY + v * AxisX);
        public void Validate()
        {
            if (new[] { OriginX, OriginY, AxisX, AxisY, Spacing }.Any(v => !double.IsFinite(v)) ||
                Spacing <= 0 || Math.Abs(AxisX * AxisX + AxisY * AxisY - 1) > 1e-8)
                throw new ArgumentException("Grid frame requires finite coordinates, positive spacing and a unit XY axis.");
        }
    }

    public static class CloudGridMath
    {
        public const int CandidateLimit = 10000;
        public static List<GridNode> Plan(CloudGridFrame frame, IReadOnlyList<GridXY> boundary)
        {
            frame.Validate();
            if (boundary.Count < 3 || boundary.Count > 1000 || boundary.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y)))
                throw new ArgumentException("Use a finite, straight-sided boundary with 3 to 1000 vertices.");
            var poly = boundary.Select(frame.Local).ToArray();
            double area = 0;
            for (int i = 0; i < poly.Length; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Length];
                if (Length(a, b) < 1e-8) throw new ArgumentException("Boundary contains a zero-length edge.");
                area += a.X * b.Y - b.X * a.Y;
                for (int j = i + 1; j < poly.Length; j++)
                {
                    if (j == i + 1 || i == 0 && j == poly.Length - 1) continue;
                    if (Crosses(a, b, poly[j], poly[(j + 1) % poly.Length]))
                        throw new ArgumentException("Boundary intersects or touches itself.");
                }
            }
            if (!double.IsFinite(area) || Math.Abs(area) < 1e-10) throw new ArgumentException("Boundary has no usable area.");
            int c0 = checked((int)Math.Ceiling((poly.Min(p => p.X) - 1e-8) / frame.Spacing));
            int c1 = checked((int)Math.Floor((poly.Max(p => p.X) + 1e-8) / frame.Spacing));
            int r0 = checked((int)Math.Ceiling((poly.Min(p => p.Y) - 1e-8) / frame.Spacing));
            int r1 = checked((int)Math.Floor((poly.Max(p => p.Y) + 1e-8) / frame.Spacing));
            long columns = (long)c1 - c0 + 1, rows = (long)r1 - r0 + 1;
            if (columns <= 0 || rows <= 0) return new List<GridNode>();
            if (columns > CandidateLimit || rows > CandidateLimit || columns * rows > CandidateLimit)
                throw new ArgumentException("Starter limit is 10,000 bounding-grid candidates. Use a smaller test area or larger spacing.");
            var result = new List<GridNode>();
            for (long r = r0; r <= r1; r++)
                for (long c = c0; c <= c1; c++)
                {
                    double u = c * frame.Spacing, v = r * frame.Spacing;
                    if (!Contains(poly, new GridXY(u, v))) continue;
                    GridXY world = frame.World(u, v);
                    result.Add(new GridNode((int)c, (int)r, world.X, world.Y));
                }
            return result;
        }

        public static GridXY[] Strip(CloudGridFrame frame, bool columns, int index,
            double minU, double maxU, double minV, double maxV, double thickness)
        {
            frame.Validate();
            if (!double.IsFinite(thickness) || thickness <= 0) throw new ArgumentException("Slice thickness must be positive.");
            double value = index * frame.Spacing, half = thickness / 2;
            double u0 = columns ? value - half : minU - half, u1 = columns ? value + half : maxU + half;
            double v0 = columns ? minV - half : value - half, v1 = columns ? maxV + half : value + half;
            return new[] { frame.World(u0, v0), frame.World(u1, v0), frame.World(u1, v1), frame.World(u0, v1) };
        }

        public static bool Contains(IReadOnlyList<GridXY> poly, GridXY p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                GridXY a = poly[j], b = poly[i];
                if (OnSegment(a, b, p)) return true;
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                    inside = !inside;
            }
            return inside;
        }
        private static double Length(GridXY a, GridXY b) => Math.Sqrt((b.X - a.X) * (b.X - a.X) + (b.Y - a.Y) * (b.Y - a.Y));
        private static double Cross(GridXY a, GridXY b, GridXY c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        private static bool OnSegment(GridXY a, GridXY b, GridXY p) => Math.Abs(Cross(a, b, p)) <= 1e-8 * Math.Max(1, Length(a, b)) &&
            p.X >= Math.Min(a.X, b.X) - 1e-8 && p.X <= Math.Max(a.X, b.X) + 1e-8 &&
            p.Y >= Math.Min(a.Y, b.Y) - 1e-8 && p.Y <= Math.Max(a.Y, b.Y) + 1e-8;
        private static bool Crosses(GridXY a, GridXY b, GridXY c, GridXY d) =>
            OnSegment(a, b, c) || OnSegment(a, b, d) || OnSegment(c, d, a) || OnSegment(c, d, b) ||
            Math.Sign(Cross(a, b, c)) != Math.Sign(Cross(a, b, d)) && Math.Sign(Cross(c, d, a)) != Math.Sign(Cross(c, d, b));
    }
}
