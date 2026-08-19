using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace CLV_CivilTools.PdfViewer
{
    public enum PdfSheetCategory
    {
        Plans,
        Profiles,
        Tables,
        Details,
        Notes
    }

    public sealed class PdfViewerDrawingState
    {
        public int Version { get; set; } = 3;
        public string PdfPath { get; set; } = string.Empty;
        public string RelativePdfPath { get; set; } = string.Empty;
        public List<PdfSheetMapping> Sheets { get; set; } = new();
    }

    public sealed class PdfSheetMapping
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public PdfSheetCategory Category { get; set; } = PdfSheetCategory.Plans;
        public int PageIndex { get; set; }
        public int Priority { get; set; }
        public bool IsPinned { get; set; }
        public bool HasModelMapping { get; set; }
        public PdfPoint PdfPoint1 { get; set; } = new();
        public PdfPoint PdfPoint2 { get; set; } = new();
        public DrawingPoint DrawingPoint1 { get; set; } = new();
        public DrawingPoint DrawingPoint2 { get; set; } = new();
        public DrawingBounds Coverage { get; set; } = new();
        public List<CoverageVertex> CoveragePolygon { get; set; } = new();
        public bool HasCustomCoverage { get; set; }
        public double PdfPageHeight { get; set; }
        public bool HasSavedPdfView { get; set; }
        public PdfViewBounds SavedPdfView { get; set; } = new();

        public bool ContainsCoverage(double x, double y)
        {
            if (CoveragePolygon.Count >= 3)
                return CoverageGeometry.Contains(CoveragePolygon, x, y);

            return Coverage.Contains(x, y);
        }

        public double GetCoverageArea()
        {
            if (CoveragePolygon.Count >= 3)
                return CoverageGeometry.Area(CoveragePolygon);

            double width = Math.Abs(Coverage.MaxX - Coverage.MinX);
            double height = Math.Abs(Coverage.MaxY - Coverage.MinY);
            double area = width * height;
            return area > 1e-9 ? area : double.MaxValue;
        }
    }

    public sealed class PdfPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    public sealed class PdfViewBounds
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }

        public RectangleF ToRectangleF() => new((float)X, (float)Y, (float)Width, (float)Height);

        public static PdfViewBounds FromRectangleF(RectangleF value) => new()
        {
            X = value.X,
            Y = value.Y,
            Width = value.Width,
            Height = value.Height
        };
    }

    public sealed class DrawingPoint
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    public sealed class CoverageVertex
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Bulge { get; set; }
    }

    public sealed class DrawingBounds
    {
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }

        public bool Contains(double x, double y) =>
            x >= Math.Min(MinX, MaxX) && x <= Math.Max(MinX, MaxX) &&
            y >= Math.Min(MinY, MaxY) && y <= Math.Max(MinY, MaxY);
    }

    internal static class CoverageGeometry
    {
        public static List<CoverageVertex> Rectangle(DrawingBounds bounds) => new()
        {
            new CoverageVertex { X = bounds.MinX, Y = bounds.MinY },
            new CoverageVertex { X = bounds.MaxX, Y = bounds.MinY },
            new CoverageVertex { X = bounds.MaxX, Y = bounds.MaxY },
            new CoverageVertex { X = bounds.MinX, Y = bounds.MaxY }
        };

        public static DrawingBounds GetBounds(IReadOnlyList<CoverageVertex> vertices)
        {
            List<DrawingPoint> points = Tessellate(vertices);
            if (points.Count == 0)
                return new DrawingBounds();

            return new DrawingBounds
            {
                MinX = points.Min(p => p.X),
                MinY = points.Min(p => p.Y),
                MaxX = points.Max(p => p.X),
                MaxY = points.Max(p => p.Y)
            };
        }

        public static bool Contains(IReadOnlyList<CoverageVertex> vertices, double x, double y)
        {
            List<DrawingPoint> points = Tessellate(vertices);
            if (points.Count < 3)
                return false;

            bool inside = false;
            for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
            {
                DrawingPoint pi = points[i];
                DrawingPoint pj = points[j];

                if (PointOnSegment(x, y, pj.X, pj.Y, pi.X, pi.Y))
                    return true;

                bool intersects = ((pi.Y > y) != (pj.Y > y)) &&
                    (x < ((pj.X - pi.X) * (y - pi.Y) / ((pj.Y - pi.Y) + 1e-30)) + pi.X);
                if (intersects)
                    inside = !inside;
            }

            return inside;
        }

        public static double Area(IReadOnlyList<CoverageVertex> vertices)
        {
            List<DrawingPoint> points = Tessellate(vertices);
            if (points.Count < 3)
                return double.MaxValue;

            double twiceArea = 0;
            for (int i = 0; i < points.Count; i++)
            {
                DrawingPoint a = points[i];
                DrawingPoint b = points[(i + 1) % points.Count];
                twiceArea += (a.X * b.Y) - (b.X * a.Y);
            }

            double area = Math.Abs(twiceArea) / 2.0;
            return area > 1e-9 ? area : double.MaxValue;
        }

        public static List<DrawingPoint> Tessellate(IReadOnlyList<CoverageVertex> vertices)
        {
            List<DrawingPoint> points = new();
            if (vertices.Count < 2)
                return points;

            for (int i = 0; i < vertices.Count; i++)
            {
                CoverageVertex start = vertices[i];
                CoverageVertex end = vertices[(i + 1) % vertices.Count];
                points.Add(new DrawingPoint { X = start.X, Y = start.Y });

                if (Math.Abs(start.Bulge) < 1e-10)
                    continue;

                double dx = end.X - start.X;
                double dy = end.Y - start.Y;
                double chord = Math.Sqrt((dx * dx) + (dy * dy));
                if (chord < 1e-9)
                    continue;

                double theta = 4.0 * Math.Atan(start.Bulge);
                double midpointX = (start.X + end.X) / 2.0;
                double midpointY = (start.Y + end.Y) / 2.0;
                double centerOffset = chord * (1.0 - (start.Bulge * start.Bulge)) / (4.0 * start.Bulge);
                double centerX = midpointX + ((-dy / chord) * centerOffset);
                double centerY = midpointY + ((dx / chord) * centerOffset);
                double radius = Math.Sqrt(
                    ((start.X - centerX) * (start.X - centerX)) +
                    ((start.Y - centerY) * (start.Y - centerY)));
                double startAngle = Math.Atan2(start.Y - centerY, start.X - centerX);
                int segments = Math.Max(2, (int)Math.Ceiling(Math.Abs(theta) / (Math.PI / 18.0)));

                for (int segment = 1; segment < segments; segment++)
                {
                    double angle = startAngle + (theta * segment / segments);
                    points.Add(new DrawingPoint
                    {
                        X = centerX + (radius * Math.Cos(angle)),
                        Y = centerY + (radius * Math.Sin(angle))
                    });
                }
            }

            return points;
        }

        private static bool PointOnSegment(
            double x,
            double y,
            double x1,
            double y1,
            double x2,
            double y2)
        {
            const double tolerance = 1e-7;
            double cross = ((x - x1) * (y2 - y1)) - ((y - y1) * (x2 - x1));
            if (Math.Abs(cross) > tolerance)
                return false;

            double dot = ((x - x1) * (x2 - x1)) + ((y - y1) * (y2 - y1));
            if (dot < -tolerance)
                return false;

            double squaredLength = ((x2 - x1) * (x2 - x1)) + ((y2 - y1) * (y2 - y1));
            return dot <= squaredLength + tolerance;
        }
    }

    internal readonly struct SimilarityTransform2D
    {
        private readonly double _scale;
        private readonly double _cos;
        private readonly double _sin;
        private readonly double _tx;
        private readonly double _ty;

        private SimilarityTransform2D(double scale, double angle, double tx, double ty)
        {
            _scale = scale;
            _cos = Math.Cos(angle);
            _sin = Math.Sin(angle);
            _tx = tx;
            _ty = ty;
        }

        public static SimilarityTransform2D Create(PdfSheetMapping mapping)
        {
            double px = mapping.PdfPoint2.X - mapping.PdfPoint1.X;
            double py = mapping.PdfPoint2.Y - mapping.PdfPoint1.Y;
            double dx = mapping.DrawingPoint2.X - mapping.DrawingPoint1.X;
            double dy = mapping.DrawingPoint2.Y - mapping.DrawingPoint1.Y;

            double pdfLength = Math.Sqrt((px * px) + (py * py));
            double drawingLength = Math.Sqrt((dx * dx) + (dy * dy));
            if (pdfLength < 1e-9 || drawingLength < 1e-9)
                throw new InvalidOperationException("Calibration points must be different.");

            double scale = drawingLength / pdfLength;
            double angle = Math.Atan2(dy, dx) - Math.Atan2(py, px);
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            double transformedX = scale * ((mapping.PdfPoint1.X * cos) - (mapping.PdfPoint1.Y * sin));
            double transformedY = scale * ((mapping.PdfPoint1.X * sin) + (mapping.PdfPoint1.Y * cos));

            return new SimilarityTransform2D(
                scale,
                angle,
                mapping.DrawingPoint1.X - transformedX,
                mapping.DrawingPoint1.Y - transformedY);
        }

        public PointF DrawingToPdf(double x, double y)
        {
            double translatedX = x - _tx;
            double translatedY = y - _ty;
            double pdfX = ((translatedX * _cos) + (translatedY * _sin)) / _scale;
            double pdfY = ((-translatedX * _sin) + (translatedY * _cos)) / _scale;
            return new PointF((float)pdfX, (float)pdfY);
        }
    }
}
