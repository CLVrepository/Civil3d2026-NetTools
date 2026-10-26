using System;
using System.Collections.Generic;
using System.Globalization;

namespace CLV_CivilTools.Survey
{
    internal sealed record InversePoint(uint Number, string Description, double Northing, double Easting, double? Elevation);
    internal sealed record InverseRow(string Label, string From, string To, bool Merge = false);

    internal sealed class PointInverseResult
    {
        private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;
        internal InversePoint From { get; }
        internal InversePoint To { get; }
        internal double DeltaNorthing { get; }
        internal double DeltaEasting { get; }
        internal double? DeltaElevation { get; }
        internal double Horizontal { get; }
        internal double? Spatial { get; }
        internal double? GradePercent { get; }
        internal double? Azimuth { get; }

        internal PointInverseResult(InversePoint from, InversePoint to)
        {
            Validate(from); Validate(to);
            From = from; To = to;
            DeltaNorthing = to.Northing - from.Northing;
            DeltaEasting = to.Easting - from.Easting;
            Horizontal = Hypot(DeltaNorthing, DeltaEasting);
            if (!double.IsFinite(Horizontal)) throw new ArgumentException("Point separation is outside the supported numeric range.");
            if (from.Elevation.HasValue && to.Elevation.HasValue)
            {
                DeltaElevation = to.Elevation.Value - from.Elevation.Value;
                Spatial = Hypot(Horizontal, DeltaElevation.Value);
                if (!double.IsFinite(Spatial.Value)) throw new ArgumentException("Elevation separation is outside the supported numeric range.");
                double grade = Horizontal > 0 ? DeltaElevation.Value / Horizontal * 100.0 : double.NaN;
                GradePercent = double.IsFinite(grade) ? grade : null;
            }
            if (Horizontal > 0)
                Azimuth = (Math.Atan2(DeltaEasting, DeltaNorthing) * 180.0 / Math.PI + 360.0) % 360.0;
        }

        private static void Validate(InversePoint point)
        {
            if (!double.IsFinite(point.Northing) || !double.IsFinite(point.Easting) ||
                point.Elevation.HasValue && !double.IsFinite(point.Elevation.Value))
                throw new ArgumentException("Coordinates must be finite; an unset elevation must be omitted.");
        }

        private static double Hypot(double x, double y)
        {
            double a = Math.Max(Math.Abs(x), Math.Abs(y)), b = Math.Min(Math.Abs(x), Math.Abs(y));
            return a == 0 ? 0 : a * Math.Sqrt(1 + (b / a) * (b / a));
        }

        internal string Bearing => !Azimuth.HasValue ? "N/A (coincident XY)" :
            (DeltaNorthing >= 0 ? "N " : "S ") +
            Dms(Math.Atan2(Math.Abs(DeltaEasting), Math.Abs(DeltaNorthing)) * 180.0 / Math.PI, false) +
            (DeltaEasting >= 0 ? " E" : " W");

        internal static string Dms(double degrees, bool wrap)
        {
            long ticks = (long)Math.Round(degrees * 360000.0, MidpointRounding.AwayFromZero);
            if (wrap) ticks %= 360L * 360000;
            long d = ticks / 360000, m = ticks / 6000 % 60;
            double seconds = ticks % 6000 / 100.0;
            return d.ToString(wrap ? "000" : "00", Culture) + "°" + m.ToString("00", Culture) + "′" +
                seconds.ToString("00.00", Culture) + "″";
        }

        internal static string Number(double? value, bool signed = false)
        {
            if (!value.HasValue) return "N/A";
            double rounded = Math.Round(value.Value, 3, MidpointRounding.AwayFromZero);
            if (rounded == 0) rounded = 0;
            return rounded.ToString(signed ? "+0.000;-0.000;0.000" : "F3", Culture);
        }

        internal IReadOnlyList<InverseRow> Rows(string units, DateTimeOffset captured) => new[]
        {
            new InverseRow("POINT INVERSE — SNAPSHOT", "", "", true),
            new InverseRow("Point information", "FROM / Reference", "TO / Comparison"),
            new InverseRow("Point number", From.Number.ToString(Culture), To.Number.ToString(Culture)),
            new InverseRow("Raw description", Clean(From.Description), Clean(To.Description)),
            new InverseRow("Northing", Number(From.Northing), Number(To.Northing)),
            new InverseRow("Easting", Number(From.Easting), Number(To.Easting)),
            new InverseRow("Elevation", Number(From.Elevation), Number(To.Elevation)),
            new InverseRow("Difference: TO minus FROM", "Result", ""),
            new InverseRow("Horizontal offset / distance (2D)", Number(Horizontal), ""),
            new InverseRow("Northing difference", Number(DeltaNorthing, true), ""),
            new InverseRow("Easting difference", Number(DeltaEasting, true), ""),
            new InverseRow("Elevation difference", Number(DeltaElevation, true), ""),
            new InverseRow("Slope distance (3D)", Number(Spatial), ""),
            new InverseRow("Bearing", Bearing, ""),
            new InverseRow("Azimuth (clockwise from north)", Azimuth.HasValue ? Dms(Azimuth.Value, true) : "N/A (coincident XY)", ""),
            new InverseRow("Grade (%)", Number(GradePercent, true), ""),
            new InverseRow(units, "", "", true),
            new InverseRow("Drawing-coordinate inverse; no ground/grid reduction.", "", "", true),
            new InverseRow("Snapshot: " + captured.ToString("yyyy-MM-dd HH:mm:ss zzz", Culture), "", "", true),
            new InverseRow("Snapshot only — does not update when points change.", "", "", true)
        };

        internal static string Clean(string text) => text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
    }
}
