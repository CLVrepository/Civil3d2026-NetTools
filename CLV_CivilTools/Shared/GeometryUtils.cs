using System;
using System.Collections.Generic;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;
using AcPolyline = Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace CLV_CivilTools.Shared
{
    /// <summary>
    /// Geometry helper methods shared across PCT tools.
    /// Extracted from CommonUtils (geometry-related parts).
    /// </summary>
    internal static class GeometryUtils
    {
        // ------------------------------------------------------------
        // Basic vector helpers
        // ------------------------------------------------------------

        /// <summary>
        /// Returns a normalized XY-only tangent vector based on the given vector.
        /// If the XY length is too small, X-axis is used.
        /// </summary>
        public static Vector3d SafeTangentXY(Vector3d v)
        {
            var t = new Vector3d(v.X, v.Y, 0.0);
            if (t.Length < 1e-9) t = Vector3d.XAxis;
            return t.GetNormal();
        }

        /// <summary>
        /// Returns a 90° counter-clockwise perpendicular in the XY plane.
        /// </summary>
        public static Vector3d PerpCCW(Vector3d v)
        {
            return new Vector3d(-v.Y, v.X, 0.0);
        }

        // ------------------------------------------------------------
        // Circle helpers
        // ------------------------------------------------------------

        /// <summary>
        /// Fit a circle (in current UCS XY) through 3 points.
        /// Returns center (with average Z) and radius.
        /// </summary>
        public static bool TryFitCircle2DFrom3Points(
            Point3d p1,
            Point3d p2,
            Point3d p3,
            out Point3d center,
            out double radius)
        {
            center = Point3d.Origin;
            radius = 0.0;

            // Work in 2D (UCS XY)
            var a = new Point2d(p1.X, p1.Y);
            var b = new Point2d(p2.X, p2.Y);
            var c = new Point2d(p3.X, p3.Y);

            double A = b.X - a.X;
            double B = b.Y - a.Y;
            double C = c.X - a.X;
            double D = c.Y - a.Y;

            double E = A * (a.X + b.X) + B * (a.Y + b.Y);
            double F = C * (a.X + c.X) + D * (a.Y + c.Y);
            double G = 2.0 * (A * (c.Y - b.Y) - B * (c.X - b.X));

            if (Math.Abs(G) < 1e-12)
                return false; // points are colinear or numerically unstable

            double cx = (D * E - B * F) / G;
            double cy = (A * F - C * E) / G;

            radius = Math.Sqrt((cx - a.X) * (cx - a.X) + (cy - a.Y) * (cy - a.Y));
            double zAvg = (p1.Z + p2.Z + p3.Z) / 3.0;

            center = new Point3d(cx, cy, zAvg);
            return radius > 1e-9;
        }

        // ------------------------------------------------------------
        // Basic entity creation helpers
        // ------------------------------------------------------------

        public static Line MakeLine(Point3d a, Point3d b, string layer)
        {
            var ln = new Line(a, b);
            ln.SetDatabaseDefaults();
            ln.Layer = layer;
            return ln;
        }

        /// <summary>
        /// Creates a closed LWPOLYLINE rectangle given four corner points,
        /// in order, on the specified layer.
        /// </summary>
        public static AcPolyline MakeRectFromCorners(
            Point3d r1,
            Point3d r2,
            Point3d r3,
            Point3d r4,
            string layer)
        {
            var pl = new AcPolyline();
            pl.SetDatabaseDefaults();
            pl.Layer = layer;

            pl.AddVertexAt(0, new Point2d(r1.X, r1.Y), 0, 0, 0);
            pl.AddVertexAt(1, new Point2d(r2.X, r2.Y), 0, 0, 0);
            pl.AddVertexAt(2, new Point2d(r3.X, r3.Y), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(r4.X, r4.Y), 0, 0, 0);
            pl.Closed = true;

            return pl;
        }

        // ------------------------------------------------------------
        // Rectangle helpers
        // ------------------------------------------------------------

        /// <summary>
        /// Attempts to compute the long and short side lengths of a rectangular polyline.
        /// Returns false if it cannot be determined.
        /// </summary>
        public static bool TryGetRectangleDims(
            AcPolyline pl,
            out double longSide,
            out double shortSide)
        {
            longSide = 0.0;
            shortSide = 0.0;

            int n = pl.NumberOfVertices;
            if (n < 2) return false;

            double max = 0.0;
            double min = double.MaxValue;

            int segCount = pl.Closed ? n : (n - 1);

            for (int i = 0; i < segCount; i++)
            {
                int j = (i == n - 1) ? 0 : i + 1;
                Point3d a = pl.GetPoint3dAt(i);
                Point3d b = pl.GetPoint3dAt(j);

                double d = a.DistanceTo(b);
                if (d < 1e-8) continue;

                if (d > max) max = d;
                if (d < min) min = d;
            }

            if (max < 1e-8 || min == double.MaxValue) return false;

            longSide = max;
            shortSide = min;
            return true;
        }

        /// <summary>
        /// Given a rectangle entity (LWPOLYLINE), tries to extract a direction
        /// vector along its first edge, the first vertex, and the long/short side.
        /// </summary>
        public static bool TryGetRectangleDirection(
            AcEntity rectEnt,
            out Vector3d dir,
            out Point3d p1,
            out double longSide,
            out double shortSide)
        {
            dir = Vector3d.XAxis;
            p1 = Point3d.Origin;
            longSide = 0.0;
            shortSide = 0.0;

            if (rectEnt is AcPolyline pl && pl.NumberOfVertices >= 2)
            {
                p1 = pl.GetPoint3dAt(0);
                Point3d p2 = pl.GetPoint3dAt(1);
                dir = p2 - p1;

                if (!TryGetRectangleDims(pl, out longSide, out shortSide))
                    return false;

                return dir.Length > 1e-9;
            }

            return false;
        }

        // ------------------------------------------------------------
        // Polyline projection helpers (for PCT5/PCT6)
        // ------------------------------------------------------------

        /// <summary>
        /// Project a point (XY) onto a 2D polyline and return the nearest point and segment index.
        /// Ignores bulges (treats each segment as straight).
        /// </summary>
        public static bool TryProjectPointToPolylineXY(
            AcPolyline pl,
            Point3d pt,
            out Point3d projected,
            out int segIndex)
        {
            projected = Point3d.Origin;
            segIndex = -1;

            double bestDist2 = double.MaxValue;
            bool found = false;

            int n = pl.NumberOfVertices;
            if (n < 2) return false;

            int segCount = pl.Closed ? n : n - 1;

            for (int i = 0; i < segCount; i++)
            {
                int j = (i == n - 1) ? 0 : i + 1;

                Point3d a = pl.GetPoint3dAt(i);
                Point3d b = pl.GetPoint3dAt(j);

                // Work in XY
                var a2 = new Point2d(a.X, a.Y);
                var b2 = new Point2d(b.X, b.Y);
                var p2 = new Point2d(pt.X, pt.Y);

                Vector2d ab = b2 - a2;
                double len2 = ab.DotProduct(ab);
                if (len2 < 1e-12) continue;

                double t = ((p2 - a2).DotProduct(ab)) / len2;
                if (t < 0.0) t = 0.0;
                if (t > 1.0) t = 1.0;

                var proj2 = a2 + (ab * t);
                double d2 = (p2 - proj2).DotProduct(p2 - proj2);

                if (d2 < bestDist2)
                {
                    bestDist2 = d2;
                    projected = new Point3d(proj2.X, proj2.Y, pt.Z);
                    segIndex = i;
                    found = true;
                }
            }

            return found;
        }
    }
}