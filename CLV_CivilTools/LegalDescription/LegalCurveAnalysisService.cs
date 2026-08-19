using System;
using System.Collections.Generic;

namespace CLV_CivilTools.LegalDescription
{
    internal static class LegalCurveAnalysisService
    {
        private const double TangencyToleranceRadians = Math.PI / (180.0 * 60.0); // 1 minute

        internal static void Analyze(LegalDescriptionSession session)
        {
            AnalyzeGroup(session.TieCourses, isClosed: false);
            AnalyzeGroup(session.Courses, isClosed: IsClosed(session.Courses));
        }

        private static bool IsClosed(IReadOnlyList<LegalCourse> courses)
        {
            if (courses.Count < 2)
                return false;
            LegalCourse first = courses[0];
            LegalCourse last = courses[^1];
            double dx = last.EndX - first.StartX;
            double dy = last.EndY - first.StartY;
            return Math.Sqrt(dx * dx + dy * dy) <= LegalGeometryService.EndpointTolerance;
        }

        private static void AnalyzeGroup(IReadOnlyList<LegalCourse> courses, bool isClosed)
        {
            for (int i = 0; i < courses.Count; i++)
            {
                LegalCourse course = courses[i];
                if (!string.Equals(course.EntityType, "ARC", StringComparison.OrdinalIgnoreCase))
                    continue;

                LegalCourse? previous = i > 0 ? courses[i - 1] : isClosed && courses.Count > 1 ? courses[^1] : null;
                LegalCourse? next = i + 1 < courses.Count ? courses[i + 1] : isClosed && courses.Count > 1 ? courses[0] : null;

                double startTangent = TangentAzimuth(course, atStart: true);
                double endTangent = TangentAzimuth(course, atStart: false);
                course.TangentAtStart = previous != null && ConnectionsAreTangent(previous, course, atSharedStartOfOutgoing: true);
                course.TangentAtEnd = next != null && ConnectionsAreTangent(course, next, atSharedStartOfOutgoing: false);

                // Legal radial bearings are measured from the curve center outward to the curve point.
                course.RadialBearingAtStart = RadialAzimuth(course, atStart: true);
                course.RadialBearingAtEnd = RadialAzimuth(course, atStart: false);
                course.ChordBearing = NormalizeAzimuth(Math.Atan2(course.EndX - course.StartX, course.EndY - course.StartY));
                double dx = course.EndX - course.StartX;
                double dy = course.EndY - course.StartY;
                course.ChordLength = Math.Sqrt(dx * dx + dy * dy);
                course.Concavity = CardinalDirection(Math.Atan2(course.CenterX - MidX(course), course.CenterY - MidY(course)));

                course.CurveInClassification = ClassifyConnection(previous, course, course.TangentAtStart);
                course.CurveOutClassification = ClassifyConnection(course, next, course.TangentAtEnd);
                // Preserve the original field for saved-session compatibility and incoming curve wording.
                course.CurveClassification = course.CurveInClassification;
            }
        }


        private static bool ConnectionsAreTangent(LegalCourse incoming, LegalCourse outgoing, bool atSharedStartOfOutgoing)
        {
            bool incomingArc = string.Equals(incoming.EntityType, "ARC", StringComparison.OrdinalIgnoreCase);
            bool outgoingArc = string.Equals(outgoing.EntityType, "ARC", StringComparison.OrdinalIgnoreCase);

            // Arc-to-arc tangency is most reliably determined from the radial lines at the
            // shared point. Compound curves have coincident radial directions; reverse
            // curves have opposite radial directions. Both are tangent relationships.
            if (incomingArc && outgoingArc)
            {
                double incomingRadial = RadialAzimuth(incoming, atStart: false);
                double outgoingRadial = RadialAzimuth(outgoing, atStart: true);
                double radialDifference = AngleDifference(incomingRadial, outgoingRadial);
                double collinearityError = Math.Min(radialDifference, Math.Abs(Math.PI - radialDifference));
                return collinearityError <= TangencyToleranceRadians;
            }

            // For line/arc and arc/line connections, compare the tangent lines as
            // undirected axes. This remains stable when the traverse is reversed.
            double incomingDirection = EndDirection(incoming);
            double outgoingDirection = StartDirection(outgoing);
            double directionDifference = AngleDifference(incomingDirection, outgoingDirection);
            double lineCollinearityError = Math.Min(directionDifference, Math.Abs(Math.PI - directionDifference));
            return lineCollinearityError <= TangencyToleranceRadians;
        }

        private static string ClassifyConnection(LegalCourse? incoming, LegalCourse? outgoing, bool tangent)
        {
            if (incoming == null || outgoing == null)
                return "N/A";
            if (!tangent)
                return "NON-TANGENT";
            if (string.Equals(incoming.EntityType, "ARC", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(outgoing.EntityType, "ARC", StringComparison.OrdinalIgnoreCase))
            {
                double incomingRadial = RadialAzimuth(incoming, atStart: false);
                double outgoingRadial = RadialAzimuth(outgoing, atStart: true);
                double radialDifference = AngleDifference(incomingRadial, outgoingRadial);

                // Same radial direction means both centers lie on the same side of the
                // shared point (compound curve). Opposite radial directions mean the
                // centers lie on opposite sides (reverse curve / PRC).
                return radialDifference <= Math.PI / 2.0 ? "COMPOUND" : "REVERSE";
            }
            return "TANGENT";
        }

        private static double MidX(LegalCourse course) => (course.StartX + course.EndX) / 2.0;
        private static double MidY(LegalCourse course) => (course.StartY + course.EndY) / 2.0;

        private static double StartDirection(LegalCourse course)
        {
            return string.Equals(course.EntityType, "ARC", StringComparison.OrdinalIgnoreCase)
                ? TangentAzimuth(course, atStart: true)
                : NormalizeAzimuth(Math.Atan2(course.EndX - course.StartX, course.EndY - course.StartY));
        }

        private static double EndDirection(LegalCourse course)
        {
            return string.Equals(course.EntityType, "ARC", StringComparison.OrdinalIgnoreCase)
                ? TangentAzimuth(course, atStart: false)
                : NormalizeAzimuth(Math.Atan2(course.EndX - course.StartX, course.EndY - course.StartY));
        }

        private static double TangentAzimuth(LegalCourse course, bool atStart)
        {
            double px = atStart ? course.StartX : course.EndX;
            double py = atStart ? course.StartY : course.EndY;
            double rx = px - course.CenterX;
            double ry = py - course.CenterY;
            double vx = course.CurveRight ? ry : -ry;
            double vy = course.CurveRight ? -rx : rx;
            return NormalizeAzimuth(Math.Atan2(vx, vy));
        }

        private static double RadialAzimuth(LegalCourse course, bool atStart)
        {
            double px = atStart ? course.StartX : course.EndX;
            double py = atStart ? course.StartY : course.EndY;
            return NormalizeAzimuth(Math.Atan2(px - course.CenterX, py - course.CenterY));
        }

        private static double AngleDifference(double a, double b)
        {
            double difference = Math.Abs(NormalizeAzimuth(a) - NormalizeAzimuth(b));
            return difference > Math.PI ? Math.PI * 2.0 - difference : difference;
        }

        private static double NormalizeAzimuth(double value)
        {
            while (value < 0.0) value += Math.PI * 2.0;
            while (value >= Math.PI * 2.0) value -= Math.PI * 2.0;
            return value;
        }

        private static string CardinalDirection(double azimuth)
        {
            double degrees = NormalizeAzimuth(azimuth) * 180.0 / Math.PI;
            string[] names = { "NORTHERLY", "NORTHEASTERLY", "EASTERLY", "SOUTHEASTERLY", "SOUTHERLY", "SOUTHWESTERLY", "WESTERLY", "NORTHWESTERLY" };
            int index = (int)Math.Floor((degrees + 22.5) / 45.0) % 8;
            return names[index];
        }
    }
}
