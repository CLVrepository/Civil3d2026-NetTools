using System;
using System.Collections.Generic;
using System.Linq;

namespace CLV_CivilTools.PointCloud
{
    /// <summary>Matches measured points in WCS XY; never interpolates Z.</summary>
    internal static class VertexElevationMatcher
    {
        internal readonly record struct XY(double X, double Y);
        internal sealed record Sample(int Key, string Label, double X, double Y, double Z);
        internal sealed record Match(int Vertex, Sample? Point, string? Issue);
        internal const double ElevationAgreement = 1e-7;

        internal static Match[] MatchVertices(IReadOnlyList<XY> vertices, IReadOnlyList<Sample> points, double tolerance)
        {
            if (!double.IsFinite(tolerance) || tolerance <= 0 || !double.IsFinite(tolerance * tolerance)
                || tolerance * tolerance == 0)
                throw new ArgumentException("XY tolerance must be finite, positive and within numeric range.");
            if (vertices.Any(v => !double.IsFinite(v.X) || !double.IsFinite(v.Y))
                || points.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(p.Z)))
                throw new ArgumentException("Vertex and point coordinates must be finite.");
            if (points.Select(p => p.Key).Distinct().Count() != points.Count)
                throw new ArgumentException("Point identities must be unique.");

            // Sort once and inspect only the X range around each vertex.
            Sample[] sorted = points.OrderBy(p => p.X).ThenBy(p => p.Key).ToArray();
            var result = new Match[vertices.Count];
            double radiusSquared = tolerance * tolerance;
            for (int i = 0; i < vertices.Count; i++)
            {
                XY v = vertices[i];
                int low = 0, high = sorted.Length;
                while (low < high)
                {
                    int middle = low + (high - low) / 2;
                    if (sorted[middle].X < v.X - tolerance) low = middle + 1;
                    else high = middle;
                }
                var candidates = new List<Sample>();
                Sample? closest = null;
                double best = double.PositiveInfinity;
                for (int p = low; p < sorted.Length && sorted[p].X <= v.X + tolerance; p++)
                {
                    Sample sample = sorted[p];
                    double dx = sample.X - v.X, dy = sample.Y - v.Y;
                    double distanceSquared = dx * dx + dy * dy;
                    if (distanceSquared > radiusSquared) continue;
                    candidates.Add(sample);
                    if (distanceSquared < best || (distanceSquared == best && sample.Key < closest!.Key))
                    {
                        best = distanceSquared; closest = sample;
                    }
                }
                // A shot moved exactly to this vertex outranks nearby points on adjacent traces.
                var exact = candidates.Where(p => p.X == v.X && p.Y == v.Y).ToList();
                if (exact.Count != 0)
                {
                    candidates = exact;
                    closest = exact.OrderBy(p => p.Key).First();
                }
                if (candidates.Count == 0)
                    result[i] = new Match(i, null, "No eligible point at this vertex or within fallback XY tolerance.");
                else if (candidates.Max(p => p.Z) - candidates.Min(p => p.Z) > ElevationAgreement)
                    result[i] = new Match(i, null, "Conflicting elevations: "
                        + string.Join(", ", candidates.Take(6).Select(p => p.Label))
                        + (candidates.Count > 6 ? ", ..." : ""));
                else
                    result[i] = new Match(i, closest, null);
            }

            // Do not silently make two distinct vertices share one measured shot.
            foreach (var reused in result.Where(m => m.Point != null).GroupBy(m => m.Point!.Key).Where(g => g.Count() > 1))
            {
                string issue = "The same point would supply several vertices. Reduce tolerance or provide separate points.";
                foreach (Match match in reused) result[match.Vertex] = new Match(match.Vertex, null, issue);
            }
            return result;
        }
    }
}

