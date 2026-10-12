using System;

namespace CLV_CivilTools.Gis
{
    internal readonly record struct SewerPoint3(double X, double Y, double Z);
    internal sealed record SewerTerminal(string Name, SewerOpenEndKind Kind,
        SewerPoint3 Point, SewerCircle? Circle, double CircleZ);
    internal sealed record SewerOrientationDecision(bool Forward, bool Reverse,
        string ForwardReason, string ReverseReason)
    {
        internal bool Unique => Forward != Reverse;
    }

    // OD identity supplies the only two possible associations. Actual verified
    // circular crossings prove orientation; no radius/proximity ownership lookup.
    internal static class SewerTerminalOrientation
    {
        internal static SewerOrientationDecision Plan(SewerPoint3 start, SewerPoint3 end,
            SewerTerminal first, SewerTerminal last)
        {
            bool forward = Try(start, end, first, last, out string forwardReason);
            bool reverse = Try(start, end, last, first, out string reverseReason);
            return new SewerOrientationDecision(forward, reverse, forwardReason, reverseReason);
        }

        private static bool Try(SewerPoint3 start, SewerPoint3 end, SewerTerminal first,
            SewerTerminal last, out string reason)
        {
            reason = "";
            if (!Finite(start) || !Finite(end) || start.Z != end.Z)
            { reason = "LINE coordinates are nonfinite or have different endpoint elevations."; return false; }
            if (string.Equals(first.Name, last.Name, StringComparison.OrdinalIgnoreCase))
            { reason = "Both OD terminals claim the same structure identity."; return false; }
            if (!Terminal(start, first, out reason)) { reason = "Start: " + reason; return false; }
            if (!Terminal(end, last, out reason)) { reason = "End: " + reason; return false; }
            var clip = SewerCircleTrim.Plan(new SewerPoint2(start.X, start.Y), new SewerPoint2(end.X, end.Y),
                first.Kind == SewerOpenEndKind.None ? first.Circle : null,
                last.Kind == SewerOpenEndKind.None ? last.Circle : null);
            reason = clip.Reason;
            return clip.Kind != SewerClipKind.Review;
        }

        private static bool Terminal(SewerPoint3 endpoint, SewerTerminal terminal, out string reason)
        {
            reason = "";
            if (string.IsNullOrWhiteSpace(terminal.Name) || !Finite(terminal.Point))
            { reason = "Terminal identity or imported point is invalid."; return false; }
            if (terminal.Kind == SewerOpenEndKind.NullStub)
            {
                bool matches = LegacyPointMatches(endpoint, terminal, 0.10) &&
                    Math.Abs(endpoint.Z - terminal.Point.Z) <= SewerCircleTrim.Tolerance;
                if (!matches) reason = "Explicit null endpoint does not satisfy the existing 0.10 XY / 1e-8 Z point rule; no relocation is inferred.";
                return matches;
            }
            if (terminal.Kind == SewerOpenEndKind.Connection)
            {
                bool matches = SewerPreparationRules.ConnectionAnchorMatches(endpoint.X, endpoint.Y, endpoint.Z,
                    terminal.Point.X, terminal.Point.Y, terminal.Point.Z);
                if (!matches) reason = "Explicit open endpoint must coincide with its OD-bound point in XYZ; no relocation is inferred.";
                return matches;
            }
            if (terminal.Kind != SewerOpenEndKind.None || terminal.Circle == null ||
                !string.Equals(terminal.Circle.Id, terminal.Name, StringComparison.OrdinalIgnoreCase))
            { reason = "Manhole requires its own verified outer circle."; return false; }
            // Keep the existing source-point and physical-circle elevation checks.
            // Any planar-policy change requires separate actual-coordinate evidence.
            if (!double.IsFinite(terminal.CircleZ) ||
                Math.Abs(endpoint.Z - terminal.Point.Z) > SewerCircleTrim.Tolerance ||
                Math.Abs(endpoint.Z - terminal.CircleZ) > SewerCircleTrim.Tolerance)
            { reason = "Pipe, imported structure point and outer-circle elevations differ; no flattening is inferred."; return false; }
            return true;
        }

        // Read-only comparison with the previous production wrapper gate.
        internal static bool LegacyPointMatches(SewerPoint3 endpoint, SewerTerminal terminal, double xyTolerance)
            => terminal.Kind == SewerOpenEndKind.Connection
                ? SewerPreparationRules.ConnectionAnchorMatches(endpoint.X, endpoint.Y, endpoint.Z,
                    terminal.Point.X, terminal.Point.Y, terminal.Point.Z)
                : Math.Sqrt((endpoint.X - terminal.Point.X) * (endpoint.X - terminal.Point.X) +
                    (endpoint.Y - terminal.Point.Y) * (endpoint.Y - terminal.Point.Y)) <= xyTolerance;

        private static bool Finite(SewerPoint3 point)
            => double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z);
    }
}
