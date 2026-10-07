using System;
using System.Collections.Generic;
using System.Linq;

namespace CLV_CivilTools.Gis
{
    internal enum StormTerminalTrimKind { Unchanged, Trimmed, Review }

    internal sealed record StormTerminalTrimDecision(StormTerminalTrimKind Kind,
        double StartParameter, double EndParameter, string Reason);

    internal enum StormTerminalGapKind { Clear, Gap, Review }
    internal sealed record StormTerminalGapProbe(StormTerminalGapKind Kind, string Reason);
    internal sealed record StormWallConnectionContext(double SearchDistance, bool AllowStartGap, bool AllowEndGap);

    /// <summary>
    /// Immutable XY planning for one terminal clip of an open straight-segment path.
    /// Parameters are segment index plus fraction, matching an AutoCAD LWPolyline.
    /// No endpoint-distance heuristic, geometry mutation, or native host calls occur here.
    /// </summary>
    internal static class StormTerminalTrim
    {
        internal const double Tolerance = 1e-8;

        /// <summary>
        /// Tests only the finite outward continuation of an exterior terminal. A gap
        /// requires a real interior interval, not a tangent, corner or boundary overlap.
        /// This is review evidence only: it never proposes or performs an extension.
        /// </summary>
        internal static StormTerminalGapProbe ProbeTerminalGap(IReadOnlyList<StormStructureVertex> boundary,
            StormStructureVertex endpoint, StormStructureVertex neighbor, double maxDistance)
        {
            if (boundary == null) return GapReview("Box boundary is missing.");
            StormStructureVertex[] ring = boundary.ToArray();
            if (!TryValidateBoundary(ring, out string reason)) return GapReview(reason);
            if (!Finite(endpoint) || !Finite(neighbor) || !double.IsFinite(maxDistance) || maxDistance <= Tolerance)
                return GapReview("Gap probe requires finite terminal points and a positive finite distance.");
            if (Locate(ring, endpoint) != Location.Outside)
                return GapReview("Gap probe requires a strictly exterior terminal.");
            double segmentLength = Distance(endpoint, neighbor);
            if (!double.IsFinite(segmentLength) || segmentLength <= Tolerance)
                return GapReview("Gap probe requires a nondegenerate terminal segment.");
            StormStructureVertex delta = Subtract(endpoint, neighbor);
            var direction = new StormStructureVertex(delta.X / segmentLength, delta.Y / segmentLength);
            var limit = new StormStructureVertex(endpoint.X + direction.X * maxDistance, endpoint.Y + direction.Y * maxDistance);
            if (!Finite(limit) || !double.IsFinite(Distance(endpoint, limit)) || Distance(endpoint, limit) <= Tolerance)
                return GapReview("Bounded gap probe cannot be represented by finite nondegenerate geometry.");

            var positions = new List<Position> { new(0, 0), new(maxDistance, 1) };
            for (int i = 0; i < ring.Length; i++)
            {
                StormStructureVertex a = ring[i], b = ring[(i + 1) % ring.Length];
                Hit hit = Intersect(endpoint, limit, a, b);
                if (!hit.Exists) continue;
                positions.Add(new Position(hit.Fraction * maxDistance, hit.Fraction));
                if (hit.Overlap)
                {
                    // Split at BOTH ends of a collinear overlap. Its midpoint alone
                    // could hide an adjacent inside interval in a concave footprint.
                    double first = Math.Clamp(Dot(Subtract(a, endpoint), direction), 0, maxDistance);
                    double last = Math.Clamp(Dot(Subtract(b, endpoint), direction), 0, maxDistance);
                    positions.Add(new Position(first, first / maxDistance));
                    positions.Add(new Position(last, last / maxDistance));
                }
            }
            List<Position> breaks = Deduplicate(positions);
            for (int i = 1; i < breaks.Count; i++)
            {
                double distance = (breaks[i - 1].Distance + breaks[i].Distance) * 0.5;
                var midpoint = new StormStructureVertex(endpoint.X + direction.X * distance, endpoint.Y + direction.Y * distance);
                if (Locate(ring, midpoint) == Location.Inside)
                    return new StormTerminalGapProbe(StormTerminalGapKind.Gap,
                        "Outward terminal continuation enters the box before the search limit; review the unextended connection.");
            }
            return new StormTerminalGapProbe(StormTerminalGapKind.Clear,
                "No positive-length interior interval within the search limit; boundary-only contacts are clear.");
        }

        internal static bool ContainsEndpoint(IReadOnlyList<StormStructureVertex> boundary, StormStructureVertex endpoint)
            => Finite(endpoint) && TryValidateBoundary(boundary, out _) && Locate(boundary, endpoint) != Location.Outside;

        private static StormTerminalGapProbe GapReview(string reason) => new(StormTerminalGapKind.Review, reason);

        internal static StormTerminalTrimDecision Plan(IReadOnlyList<StormStructureVertex> boundary,
            IReadOnlyList<StormStructureVertex> path)
        {
            if (boundary == null || path == null) return Review("Boundary or path is missing.");
            StormStructureVertex[] ring = boundary.ToArray();
            StormStructureVertex[] points = path.ToArray();
            if (!TryValidateBoundary(ring, out string reason)) return Review(reason);
            if (!TryValidatePath(points, out reason)) return Review(reason);

            var positions = new List<Position> { new Position(0, 0) };
            var hits = new List<Position>();
            double total = 0;
            for (int i = 0; i < points.Length - 1; i++)
            {
                double length = Distance(points[i], points[i + 1]);
                for (int j = 0; j < ring.Length; j++)
                {
                    Hit hit = Intersect(points[i], points[i + 1], ring[j], ring[(j + 1) % ring.Length]);
                    if (hit.Overlap) return Review("Pipe wall overlaps the box boundary.");
                    if (hit.Exists) hits.Add(new Position(total + hit.Fraction * length, i + hit.Fraction));
                }
                total += length;
                positions.Add(new Position(total, i + 1));
            }
            if (!double.IsFinite(total)) return Review("Pipe wall length is not finite.");

            // Use physical distance for duplicate corner/vertex hits, never rounded parameters.
            var distinctHits = Deduplicate(hits);
            positions.AddRange(distinctHits);
            List<Position> breaks = Deduplicate(positions);
            var intervals = new List<Location>();
            for (int i = 1; i < breaks.Count; i++)
            {
                double midParameter = (breaks[i - 1].Parameter + breaks[i].Parameter) * 0.5;
                Location location = Locate(ring, At(points, midParameter));
                if (location == Location.Boundary)
                    return Review("A pipe interval lies on or too close to the box boundary.");
                intervals.Add(location);
            }

            if (intervals.All(location => location == Location.Outside))
                return new StormTerminalTrimDecision(StormTerminalTrimKind.Unchanged, 0, points.Length - 1,
                    "No interior pipe interval; tangent or already-trimmed contacts are unchanged.");

            Location start = Locate(ring, points[0]);
            Location end = Locate(ring, points[points.Length - 1]);
            bool removeStart = start == Location.Inside && end == Location.Outside;
            bool removeEnd = start == Location.Outside && end == Location.Inside;
            if (!removeStart && !removeEnd)
                return Review("Only one strictly inside endpoint and one strictly outside endpoint can be clipped.");
            if (distinctHits.Count != 1 || distinctHits[0].Distance <= Tolerance ||
                total - distinctHits[0].Distance <= Tolerance)
                return Review("Pipe wall has multiple or ambiguous boundary contacts.");

            int transitions = 0;
            for (int i = 1; i < intervals.Count; i++)
                if (intervals[i] != intervals[i - 1]) transitions++;
            if (transitions != 1 || intervals[0] != start || intervals[intervals.Count - 1] != end)
                return Review("Pipe intervals do not establish one proper terminal crossing.");

            double parameter = distinctHits[0].Parameter;
            return new StormTerminalTrimDecision(StormTerminalTrimKind.Trimmed,
                removeStart ? parameter : 0, removeEnd ? parameter : points.Length - 1,
                "One proper crossing separates the terminal inside interval from the retained outside path.");
        }

        internal static bool TryValidateBoundary(IReadOnlyList<StormStructureVertex> boundary, out string reason)
        {
            reason = "Box boundary must be a finite, simple, nondegenerate straight polygon.";
            if (boundary == null || boundary.Count < 3 || boundary.Any(point => !Finite(point))) return false;
            double area = 0;
            StormStructureVertex origin = boundary[0];
            for (int i = 0; i < boundary.Count; i++)
            {
                StormStructureVertex a = boundary[i], b = boundary[(i + 1) % boundary.Count];
                double length = Distance(a, b);
                if (!double.IsFinite(length) || length <= Tolerance) return false;
                area += Cross(Subtract(a, origin), Subtract(b, origin));
                for (int j = i + 1; j < boundary.Count; j++)
                {
                    bool adjacent = j == i + 1 || (i == 0 && j == boundary.Count - 1);
                    Hit hit = Intersect(a, b, boundary[j], boundary[(j + 1) % boundary.Count]);
                    if (hit.Overlap || (!adjacent && hit.Exists)) return false;
                }
            }
            if (!double.IsFinite(area) || Math.Abs(area) <= Tolerance * Tolerance) return false;
            reason = string.Empty;
            return true;
        }

        internal static bool TryValidatePath(IReadOnlyList<StormStructureVertex> path, out string reason)
        {
            reason = "Pipe wall must be a finite, simple, nondegenerate open straight path.";
            if (path == null || path.Count < 2 || path.Any(point => !Finite(point))) return false;
            for (int i = 0; i < path.Count - 1; i++)
            {
                double length = Distance(path[i], path[i + 1]);
                if (!double.IsFinite(length) || length <= Tolerance) return false;
                for (int j = i + 1; j < path.Count - 1; j++)
                {
                    Hit hit = Intersect(path[i], path[i + 1], path[j], path[j + 1]);
                    if (hit.Overlap || (j != i + 1 && hit.Exists)) return false;
                }
            }
            reason = string.Empty;
            return true;
        }

        private static StormTerminalTrimDecision Review(string reason)
            => new StormTerminalTrimDecision(StormTerminalTrimKind.Review, 0, 0, reason);

        private static List<Position> Deduplicate(IEnumerable<Position> source)
        {
            var result = new List<Position>();
            foreach (Position position in source.OrderBy(value => value.Distance))
                if (result.Count == 0 || position.Distance - result[result.Count - 1].Distance > Tolerance)
                    result.Add(position);
            return result;
        }

        private static StormStructureVertex At(IReadOnlyList<StormStructureVertex> path, double parameter)
        {
            int segment = Math.Min((int)Math.Floor(parameter), path.Count - 2);
            double fraction = parameter - segment;
            return new StormStructureVertex(path[segment].X + (path[segment + 1].X - path[segment].X) * fraction,
                path[segment].Y + (path[segment + 1].Y - path[segment].Y) * fraction);
        }

        private static Location Locate(IReadOnlyList<StormStructureVertex> ring, StormStructureVertex point)
        {
            bool inside = false;
            for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
            {
                StormStructureVertex a = ring[j], b = ring[i];
                StormStructureVertex delta = Subtract(b, a);
                double length = Distance(a, b);
                var unit = new StormStructureVertex(delta.X / length, delta.Y / length);
                StormStructureVertex relative = Subtract(point, a);
                double along = Dot(relative, unit);
                if (along >= -Tolerance && along <= length + Tolerance && Math.Abs(Cross(relative, unit)) <= Tolerance)
                    return Location.Boundary;
                if ((a.Y > point.Y) != (b.Y > point.Y) &&
                    point.X - a.X < (b.X - a.X) * ((point.Y - a.Y) / (b.Y - a.Y))) inside = !inside;
            }
            return inside ? Location.Inside : Location.Outside;
        }

        private static Hit Intersect(StormStructureVertex a, StormStructureVertex b,
            StormStructureVertex c, StormStructureVertex d)
        {
            double firstLength = Distance(a, b), secondLength = Distance(c, d);
            if (!double.IsFinite(firstLength) || !double.IsFinite(secondLength) ||
                firstLength <= Tolerance || secondLength <= Tolerance) return default;
            StormStructureVertex first = Subtract(b, a), second = Subtract(d, c), offset = Subtract(c, a);
            first = new StormStructureVertex(first.X / firstLength, first.Y / firstLength);
            second = new StormStructureVertex(second.X / secondLength, second.Y / secondLength);
            double denominator = Cross(first, second);
            if (Math.Abs(denominator) <= 1e-12)
            {
                if (Math.Abs(Cross(offset, first)) > Tolerance) return default;
                double p = Dot(offset, first), q = Dot(Subtract(d, a), first);
                double low = Math.Max(0, Math.Min(p, q)), high = Math.Min(firstLength, Math.Max(p, q));
                if (high < low - Tolerance) return default;
                return new Hit(true, high - low > Tolerance, Math.Clamp((low + high) * 0.5 / firstLength, 0, 1));
            }
            double firstDistance = Cross(offset, second) / denominator;
            double secondDistance = Cross(offset, first) / denominator;
            if (firstDistance < -Tolerance || firstDistance > firstLength + Tolerance ||
                secondDistance < -Tolerance || secondDistance > secondLength + Tolerance) return default;
            return new Hit(true, false, Math.Clamp(firstDistance / firstLength, 0, 1));
        }

        private static bool Finite(StormStructureVertex point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
        private static StormStructureVertex Subtract(StormStructureVertex a, StormStructureVertex b) => new(a.X - b.X, a.Y - b.Y);
        private static double Distance(StormStructureVertex a, StormStructureVertex b)
        {
            double x = a.X - b.X, y = a.Y - b.Y;
            return Math.Sqrt(x * x + y * y);
        }
        private static double Dot(StormStructureVertex a, StormStructureVertex b) => a.X * b.X + a.Y * b.Y;
        private static double Cross(StormStructureVertex a, StormStructureVertex b) => a.X * b.Y - a.Y * b.X;
        private enum Location { Outside, Inside, Boundary }
        private readonly record struct Position(double Distance, double Parameter);
        private readonly record struct Hit(bool Exists, bool Overlap, double Fraction);
    }
}
