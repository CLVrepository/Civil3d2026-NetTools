using System;
using System.Globalization;
using System.Linq;
using CLV_CivilTools.Survey;

public static class PointInverseTests
{
    public static void Main() => Console.WriteLine(Run());
    public static string Run()
    {
        int passed = 0;
        void Check(string name, bool condition)
        {
            if (!condition) throw new InvalidOperationException("FAIL: " + name);
            passed++;
        }
        void Near(string name, double? actual, double expected, double tolerance = 1e-9)
            => Check(name, actual.HasValue && Math.Abs(actual.Value - expected) <= tolerance);
        InversePoint P(double north, double east, double? z = 0, uint number = 1)
            => new(number, "CONTROL", north, east, z);
        var a = P(10000, 20000, 2100);
        var b = P(10030, 20040, 2101, 2);
        var result = new PointInverseResult(a, b);
        Near("3-4-5 horizontal", result.Horizontal, 50);
        Near("3D uses elevation difference", result.Spatial, Math.Sqrt(2501));
        Near("grade uses horizontal denominator", result.GradePercent, 2);
        Near("northing signed", result.DeltaNorthing, 30);
        Near("easting signed", result.DeltaEasting, 40);
        Near("elevation signed", result.DeltaElevation, 1);
        Check("survey bearing", result.Bearing == "N 53°07′48.37″ E");
        var reversed = new PointInverseResult(b, a);
        Near("reverse keeps horizontal", reversed.Horizontal, result.Horizontal);
        Near("reverse keeps spatial", reversed.Spatial, result.Spatial!.Value);
        Near("reverse grade", reversed.GradePercent, -2);
        Near("reverse elevation", reversed.DeltaElevation, -1);
        Check("reverse bearing", reversed.Bearing == "S 53°07′48.37″ W");
        var shots = new PointInverseResult(P(1000000, 500000, 2000), P(1000000.003, 500000.004, 1999.998, 2));
        Near("near-coincident record/survey offset", shots.Horizontal, 0.005, 1e-9);
        Near("near-coincident negative Z", shots.DeltaElevation, -0.002, 1e-9);
        var veryClose = new PointInverseResult(P(1000000, 500000), P(1000000.000001, 500000, 0, 2));
        Check("no tolerance collapses distinct shots", veryClose.Horizontal > 0 && veryClose.Azimuth.HasValue);
        var vertical = new PointInverseResult(P(1, 2, 5), P(1, 2, 8, 2));
        Near("vertical horizontal zero", vertical.Horizontal, 0);
        Near("vertical spatial", vertical.Spatial, 3);
        Check("vertical bearing undefined", !vertical.Azimuth.HasValue && vertical.Bearing.Contains("N/A"));
        Check("vertical grade undefined", !vertical.GradePercent.HasValue);
        var coincident = new PointInverseResult(P(1, 2, 5), P(1, 2, 5, 2));
        Near("coincident 3D zero", coincident.Spatial, 0);
        Check("coincident grade undefined", !coincident.GradePercent.HasValue);
        foreach (var pair in new[] { (P(0, 0, null), P(3, 4, 1, 2)), (P(0, 0, 1), P(3, 4, null, 2)) })
        {
            var missing = new PointInverseResult(pair.Item1, pair.Item2);
            Near("missing elevation keeps horizontal", missing.Horizontal, 5);
            Check("missing elevation never becomes zero", !missing.Spatial.HasValue && !missing.DeltaElevation.HasValue && !missing.GradePercent.HasValue);
        }
        foreach (var test in new[] { (1.0, 0.0, 0.0), (0.0, 1.0, 90.0), (-1.0, 0.0, 180.0), (0.0, -1.0, 270.0),
            (1.0, 1.0, 45.0), (-1.0, 1.0, 135.0), (-1.0, -1.0, 225.0), (1.0, -1.0, 315.0) })
            Near("cardinal/quadrant azimuth " + test.Item3, new PointInverseResult(P(0, 0), P(test.Item1, test.Item2, 0, 2)).Azimuth, test.Item3);
        Check("DMS carries seconds/minutes", PointInverseResult.Dms(12.999999999, false) == "13°00′00.00″");
        Check("DMS azimuth wraps north", PointInverseResult.Dms(359.999999999, true) == "000°00′00.00″");
        Check("display negative zero suppressed", PointInverseResult.Number(-0.00001, true) == "0.000");
        foreach (var bad in new[] { P(double.NaN, 0), P(0, double.PositiveInfinity), P(0, 0, double.NaN) })
        {
            bool stopped = false;
            try { _ = new PointInverseResult(bad, b); } catch (ArgumentException) { stopped = true; }
            Check("nonfinite coordinate rejected", stopped);
        }
        var timestamp = new DateTimeOffset(2026, 10, 26, 12, 0, 0, TimeSpan.FromHours(-7));
        var rows = result.Rows("INSUNITS: USSurveyFeet", timestamp);
        Check("snapshot timestamp includes zone", rows.Any(r => r.Label.Contains("2026-10-26 12:00:00 -07:00")));
        Check("units preserved without conversion", rows.Any(r => r.Label.Contains("USSurveyFeet")));
        Check("basis explicit", rows.Any(r => r.Label.Contains("no ground/grid reduction")));
        Check("snapshot not live", rows.Any(r => r.Label.Contains("does not update")));
        Check("description text cannot break clipboard rows", PointInverseResult.Clean("A\tB\r\nC") == "A B  C");
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Check("report formatting consistent", PointInverseResult.Number(1.25) == "1.250");
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }
        return passed + "/" + passed + " point inverse checks passed.";
    }
}
