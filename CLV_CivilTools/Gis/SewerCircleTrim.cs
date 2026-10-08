using System;

namespace CLV_CivilTools.Gis
{
    internal readonly record struct SewerPoint2(double X, double Y);
    internal sealed record SewerCircle(string Id, double X, double Y, double Radius);
    internal enum SewerClipKind { Unchanged, Trimmed, Review }
    internal sealed record SewerClipDecision(SewerClipKind Kind,
        double StartParameter, double EndParameter, string Reason);

    /// <summary>
    /// Pure XY clipping of one straight LINE against its verified terminal outer
    /// circles. Returned parameters are fractions of the original LINE, in [0, 1].
    /// The caller supplies the actual OUTER circles and has already proved terminal
    /// ownership. A null circle means an explicitly verified open/null terminal.
    /// This planner never infers connectivity, extends a line or polygonizes a circle.
    /// </summary>
    internal static class SewerCircleTrim
    {
        // Absolute drawing-unit distance, never a relative/normalized parameter
        // tolerance. Subtract survey coordinates before evaluating circle geometry.
        internal const double Tolerance = 1e-8;

        internal static SewerClipDecision Plan(SewerPoint2 start, SewerPoint2 end,
            SewerCircle? startBoundary, SewerCircle? endBoundary)
        {
            if (!Finite(start.X, start.Y) || !Finite(end.X, end.Y))
                return Review("LINE endpoints must be finite.");
            double dx = end.X - start.X, dy = end.Y - start.Y;
            double length = Hypot(dx, dy);
            if (!double.IsFinite(length) || length <= Tolerance)
                return Review("LINE is degenerate, too short or not representable.");
            if (!ValidCircle(startBoundary) || !ValidCircle(endBoundary))
                return Review("Each supplied outer circle requires an identity, finite center and positive finite radius.");

            if (startBoundary != null && endBoundary != null)
            {
                if (string.Equals(startBoundary.Id.Trim(), endBoundary.Id.Trim(), StringComparison.OrdinalIgnoreCase))
                    return Review("Both terminals claim the same outer-circle identity.");
                double separation = Hypot(endBoundary.X - startBoundary.X, endBoundary.Y - startBoundary.Y);
                double radii = startBoundary.Radius + endBoundary.Radius;
                if (!double.IsFinite(separation) || !double.IsFinite(radii) || separation - radii <= Tolerance)
                    return Review("Connected outer circles overlap, touch or have an ambiguous separation.");
            }

            double ux = dx / length, uy = dy / length;
            if (!TryTerminal(start, ux, uy, length, startBoundary, out double startCut, out string reason))
                return Review("Start: " + reason);
            if (!TryTerminal(end, -ux, -uy, length, endBoundary, out double endCut, out reason))
                return Review("End: " + reason);

            double retainedLength = length - startCut - endCut;
            if (!double.IsFinite(retainedLength) || retainedLength <= Tolerance)
                return Review("Terminal clips consume the LINE or leave an ambiguous short interval.");
            double first = startCut / length, endFraction = endCut / length;
            double last = 1.0 - endFraction;
            if (!Finite(first, last) || first < 0 || last > 1 || first >= last ||
                // Require both orientations to represent the clip: otherwise a
                // direction reversal could silently round a small terminal cut off.
                (startCut > 0 && (first == 0 || 1.0 - first == 1.0)) ||
                (endCut > 0 && (endFraction == 0 || last == 1)))
                return Review("The retained LINE interval cannot be represented without extending or losing a clip.");

            SewerPoint2 a = At(start, end, dx, dy, first);
            SewerPoint2 b = At(start, end, dx, dy, last);
            if (!Finite(a.X, a.Y) || !Finite(b.X, b.Y) || Hypot(b.X - a.X, b.Y - a.Y) <= Tolerance)
                return Review("The retained LINE endpoints cannot be represented reliably.");
            if (!OnBoundary(a, startBoundary) || !OnBoundary(b, endBoundary))
                return Review("A computed endpoint cannot meet its outer circle within the absolute drawing-unit tolerance.");
            if (!OutsideInterior(a, b, startBoundary) || !OutsideInterior(a, b, endBoundary))
                return Review("The retained LINE crosses or reenters a connected outer-circle interior.");

            return new SewerClipDecision(startCut == 0 && endCut == 0 ? SewerClipKind.Unchanged : SewerClipKind.Trimmed,
                first, last, startCut == 0 && endCut == 0
                    ? "Verified connected endpoints are already on their outer circles; permitted open endpoints are preserved."
                    : "Exact circular crossings trim the connected terminals; permitted open endpoints are preserved.");
        }

        private static bool TryTerminal(SewerPoint2 endpoint, double ux, double uy, double length,
            SewerCircle? boundary, out double cut, out string reason)
        {
            cut = 0;
            reason = string.Empty;
            if (boundary == null) return true;

            double x = endpoint.X - boundary.X, y = endpoint.Y - boundary.Y;
            double radius = boundary.Radius, distance = Hypot(x, y);
            if (!double.IsFinite(distance))
                return Fail("The endpoint-to-circle displacement is not finite.", out reason);
            double signedDistance = distance - radius;
            if (signedDistance > Tolerance)
                return Fail("Endpoint is outside its assigned outer circle; a gap or unrelated through-crossing requires review, never extension.", out reason);

            // With unit direction u and q = endpoint - center, the circle equation
            // is s^2 + 2(q.u)s + |q|^2 - R^2 = 0. Its positive exit root is
            // -q.u + sqrt(R^2 - cross(q,u)^2). Factoring the radical avoids
            // subtracting squared survey coordinates and reduces tangent loss.
            double projection = x * ux + y * uy;
            double perpendicular = Math.Abs(x * uy - y * ux);
            if (!Finite(projection, perpendicular) || radius - perpendicular <= Tolerance)
                return Fail("The LINE has only a tangent, misses the circle or has an ambiguous near-tangent contact.", out reason);
            double ratio = perpendicular / radius;
            double halfChord = radius * Math.Sqrt((1.0 - ratio) * (1.0 + ratio));
            if (!double.IsFinite(halfChord))
                return Fail("The circular crossing cannot be represented reliably.", out reason);

            if (Math.Abs(signedDistance) <= Tolerance)
            {
                // Keep an already-on-circle endpoint exactly where it is. Heading
                // inward would introduce a second, unrelated boundary crossing.
                if (projection <= 0)
                    return Fail("An already-on-circle endpoint heads into the circle or has a tangent-only contact.", out reason);
                return true;
            }

            // The alternative form avoids cancellation when an inside endpoint is
            // close to its exit and points outward. This is the same quadratic root.
            cut = projection > 0
                ? (radius - distance) * ((radius + distance) / (halfChord + projection))
                : halfChord - projection;
            if (!double.IsFinite(cut) || cut <= 0 || cut >= length || length - cut <= Tolerance)
                return Fail("No proper interior-to-exterior crossing leaves a positive usable LINE interval.", out reason);
            return true;
        }

        private static bool ValidCircle(SewerCircle? circle)
            => circle == null || (!string.IsNullOrWhiteSpace(circle.Id) && Finite(circle.X, circle.Y) &&
                double.IsFinite(circle.Radius) && circle.Radius > 0);

        private static bool OnBoundary(SewerPoint2 endpoint, SewerCircle? circle)
            => circle == null || Math.Abs(Hypot(endpoint.X - circle.X, endpoint.Y - circle.Y) - circle.Radius) <= Tolerance;

        private static bool OutsideInterior(SewerPoint2 start, SewerPoint2 end, SewerCircle? circle)
        {
            if (circle == null) return true;
            double dx = end.X - start.X, dy = end.Y - start.Y, length = Hypot(dx, dy);
            double x = start.X - circle.X, y = start.Y - circle.Y;
            if (!double.IsFinite(length) || length <= Tolerance || !Finite(x, y)) return false;
            double ux = dx / length, uy = dy / length;
            double nearest = Math.Clamp(-(x * ux + y * uy), 0, length);
            double distance = Hypot(x + ux * nearest, y + uy * nearest);
            return double.IsFinite(distance) && distance - circle.Radius >= -Tolerance;
        }

        private static SewerPoint2 At(SewerPoint2 start, SewerPoint2 end, double dx, double dy, double parameter)
            => parameter == 0 ? start : parameter == 1 ? end : new SewerPoint2(start.X + dx * parameter, start.Y + dy * parameter);

        private static double Hypot(double x, double y)
        {
            double largest = Math.Max(Math.Abs(x), Math.Abs(y));
            if (!double.IsFinite(largest) || largest == 0) return largest;
            double smallest = Math.Min(Math.Abs(x), Math.Abs(y)) / largest;
            return largest * Math.Sqrt(1.0 + smallest * smallest);
        }

        private static bool Finite(double x, double y) => double.IsFinite(x) && double.IsFinite(y);
        private static bool Fail(string message, out string reason) { reason = message; return false; }
        // Review never exposes a partial trim as if it were actionable.
        private static SewerClipDecision Review(string reason) => new(SewerClipKind.Review, 0, 1, reason);
    }
}
