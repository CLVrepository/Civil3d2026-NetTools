using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CLV_CivilTools.Gis
{
    internal enum StormStructureRole
    {
        Unknown,
        DropInlet,
        Access,
        JunctionBox,
        NullPipeEnd
    }

    internal readonly record struct StormStructureVertex(double X, double Y);
    internal sealed record StormStructureSource(string Id, string Name, string PartSizeName, double X, double Y);
    internal sealed record StormStructureTarget(string Id, StormStructureRole Role, double X, double Y,
        IReadOnlyList<StormStructureVertex>? Footprint = null, bool IsExistingOutline = false);
    internal sealed record StormStructureMatch(string SourceId, string TargetId, StormStructureRole Role);
    internal sealed record StormStructureIssue(string Code, IReadOnlyList<string> SourceIds, IReadOnlyList<string> TargetIds, string Message);
    internal sealed record StormStructureCompletion(string SourceId, IReadOnlyList<string> OutputIds);
    internal sealed record StormStructureCompletionPlan(IReadOnlyList<StormStructureCompletion> Accepted,
        IReadOnlyList<string> ConflictedSourceIds, IReadOnlyList<StormStructureIssue> Issues);
    internal sealed record StormStructureMatchResult(IReadOnlyList<StormStructureMatch> Matches, IReadOnlyList<StormStructureIssue> Issues)
    {
        public IReadOnlyList<string> PreservedPipeEndSourceIds { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// Pure, conservative planning only: this class never changes drawing geometry or OD.
    /// Caller-supplied identifiers must distinguish entities in the current import snapshot.
    /// Matching is role-aware and mutual one-to-one. DI/access and center-only targets
    /// require 0.10 XY proximity. A supplied junction footprint or explicitly flagged
    /// existing DI/box outline instead requires polygon containment and the legacy
    /// 25-unit center bound. An unbound existing outline participates once in the shared
    /// DI/box candidate graph. No nearest-neighbor tie breaking is performed.
    /// </summary>
    internal static class StormStructureMatching
    {
        public const double MatchTolerance = 0.10;
        public const double JunctionSearchRadius = 25.0;
        private const double FootprintBoundaryTolerance = 1e-8;

        private static readonly Regex DropInletToken = new Regex(
            @"(?<![A-Z0-9])(?:SDDI(?=$|[^A-Z])|TYPE[_\s]+(?:A(?:[_\s]+MOD)?|CM2|CM|C|DM2|D)(?![A-Z0-9]))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex AccessToken = new Regex(
            @"(?<![A-Z0-9])ACCESS\s+STRUCTURE(?![A-Z0-9])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex JunctionNameToken = new Regex(
            @"(?:^|[-_\s])JS(?:$|[-_\s])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static StormStructureRole Classify(string? name, string? partSizeName)
        {
            string cleanName = Clean(name);
            string cleanPart = Clean(partSizeName);
            if (cleanName.Length == 0 || BaseName(cleanName).Length == 0)
                return StormStructureRole.Unknown;

            bool box = cleanName.EndsWith("-JS", StringComparison.OrdinalIgnoreCase);
            bool di = DropInletToken.IsMatch(cleanName) || DropInletToken.IsMatch(cleanPart);
            bool access = AccessToken.IsMatch(cleanPart);

            if (string.Equals(cleanPart, "UFLS-Null Structure", StringComparison.OrdinalIgnoreCase))
            {
                // This exact OD convention identifies a non-graphic pipe end. Do
                // not generalize it to other null parts or unknown/missing OD.
                bool stub = cleanName.EndsWith("-STUB", StringComparison.OrdinalIgnoreCase) &&
                    cleanName.Substring(0, cleanName.Length - 5).Trim().Length > 0;
                return stub && !di && !box && !JunctionNameToken.IsMatch(cleanName) &&
                    !access && !AccessToken.IsMatch(cleanName)
                    ? StormStructureRole.NullPipeEnd
                    : StormStructureRole.Unknown;
            }

            // -JS is the authoritative box naming convention even when its catalog
            // description contains ACCESS STRUCTURE. DI evidence is a conflict.
            if (di && (box || access))
                return StormStructureRole.Unknown;
            if (box)
                return StormStructureRole.JunctionBox;
            if (di)
                return StormStructureRole.DropInlet;
            if (access)
                return StormStructureRole.Access;
            return StormStructureRole.Unknown;
        }

        public static string BaseName(string? name)
        {
            string value = Clean(name);
            return value.EndsWith("-JS", StringComparison.OrdinalIgnoreCase)
                ? value.Substring(0, value.Length - 3).Trim()
                : value;
        }

        /// <summary>
        /// Validate every completion's full output ownership before the host removes
        /// any ordinary target. Accepted output order is preserved: index zero is the
        /// logical primary, but ALL outputs belong to that one completed source.
        /// Invalid claims still participate in overlap detection to fail closed.
        /// </summary>
        public static StormStructureCompletionPlan PlanCompletions(IEnumerable<StormStructureCompletion> completions)
        {
            ArgumentNullException.ThrowIfNull(completions);
            StormStructureCompletion[] claims = completions
                .OrderBy(c => Clean(c.SourceId), StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => Clean(c.SourceId), StringComparer.Ordinal)
                .ThenBy(c => string.Join("\u001f", c.OutputIds ?? Array.Empty<string>()), StringComparer.Ordinal)
                .ToArray();
            var blocked = new HashSet<int>();
            var issues = new List<StormStructureIssue>();

            for (int i = 0; i < claims.Length; i++)
            {
                StormStructureCompletion claim = claims[i];
                if (Clean(claim.SourceId).Length == 0 || claim.OutputIds == null ||
                    claim.OutputIds.Count == 0 || claim.OutputIds.Any(id => Clean(id).Length == 0))
                {
                    blocked.Add(i);
                    issues.Add(new StormStructureIssue("InvalidCompletion", new[] { claim.SourceId },
                        claim.OutputIds?.ToArray() ?? Array.Empty<string>(),
                        "Completion requires a source ID and one or more nonblank output IDs; no outputs were reserved."));
                }
                if (claim.OutputIds == null)
                    continue;
                string[] repeated = claim.OutputIds.Where(id => Clean(id).Length > 0)
                    .GroupBy(Clean, StringComparer.OrdinalIgnoreCase)
                    .Where(g => g.Count() > 1).Select(g => g.Key)
                    .OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
                if (repeated.Length > 0)
                {
                    blocked.Add(i);
                    issues.Add(new StormStructureIssue("DuplicateCompletionOutput", new[] { claim.SourceId }, repeated,
                        "One completion lists an output more than once; the entire claim requires review."));
                }
            }

            foreach (var group in Enumerable.Range(0, claims.Length)
                .GroupBy(i => Clean(claims[i].SourceId), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Key.Length > 0 && g.Count() > 1))
            {
                int[] members = group.ToArray();
                blocked.UnionWith(members);
                issues.Add(new StormStructureIssue("DuplicateCompletionSource",
                    members.Select(i => claims[i].SourceId).ToArray(),
                    members.SelectMany(i => claims[i].OutputIds ?? Array.Empty<string>()).ToArray(),
                    "A source has multiple completion claims; none of its outputs were reserved."));
            }

            var outputClaims = Enumerable.Range(0, claims.Length)
                .SelectMany(i => (claims[i].OutputIds ?? Array.Empty<string>())
                    .Where(id => Clean(id).Length > 0).Select(id => (OutputId: Clean(id), ClaimIndex: i)))
                .GroupBy(entry => entry.OutputId, StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
            foreach (var group in outputClaims)
            {
                int[] members = group.Select(entry => entry.ClaimIndex).Distinct().ToArray();
                if (members.Select(i => Clean(claims[i].SourceId)).Distinct(StringComparer.OrdinalIgnoreCase).Count() <= 1)
                    continue;
                blocked.UnionWith(members);
                issues.Add(new StormStructureIssue("CompletionOwnershipConflict",
                    members.Select(i => claims[i].SourceId).ToArray(), new[] { group.Key },
                    "An output is claimed by multiple sources; every overlapping claim requires review."));
            }

            StormStructureCompletion[] accepted = Enumerable.Range(0, claims.Length)
                .Where(i => !blocked.Contains(i))
                .Select(i => new StormStructureCompletion(claims[i].SourceId, claims[i].OutputIds.ToArray())).ToArray();
            string[] conflictedSourceIds = blocked.Select(i => claims[i].SourceId)
                .Where(id => Clean(id).Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray();
            return new StormStructureCompletionPlan(accepted, conflictedSourceIds, issues.ToArray());
        }

        public static StormStructureMatchResult Match(
            IEnumerable<StormStructureSource> sources,
            IEnumerable<StormStructureTarget> targets)
        {
            ArgumentNullException.ThrowIfNull(sources);
            ArgumentNullException.ThrowIfNull(targets);
            // Stable output order makes import enumeration irrelevant to the result.
            StormStructureSource[] sourceArray = sources
                .OrderBy(s => Clean(s.Id), StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => Clean(s.Name), StringComparer.OrdinalIgnoreCase).ToArray();
            StormStructureTarget[] targetArray = targets
                .OrderBy(t => Clean(t.Id), StringComparer.OrdinalIgnoreCase).ToArray();
            StormStructureRole[] roles = sourceArray.Select(s => Classify(s.Name, s.PartSizeName)).ToArray();
            var blockedSources = new HashSet<int>();
            var blockedTargets = new HashSet<int>();
            var validFootprints = new bool[targetArray.Length];
            var issues = new List<StormStructureIssue>();
            var matches = new List<StormStructureMatch>();

            void Issue(string code, IEnumerable<int> sourceIndices, IEnumerable<int> targetIndices, string message)
            {
                issues.Add(new StormStructureIssue(code,
                    sourceIndices.Select(i => sourceArray[i].Id).ToArray(),
                    targetIndices.Select(i => targetArray[i].Id).ToArray(), message));
            }

            for (int i = 0; i < sourceArray.Length; i++)
            {
                StormStructureSource source = sourceArray[i];
                if (Clean(source.Id).Length == 0 || Clean(source.Name).Length == 0 || !Finite(source.X, source.Y))
                {
                    blockedSources.Add(i);
                    Issue("InvalidSource", new[] { i }, Array.Empty<int>(), "Source requires an ID, a name, and finite XY coordinates.");
                }
                else if (roles[i] == StormStructureRole.Unknown)
                {
                    blockedSources.Add(i);
                    Issue("UnknownRole", new[] { i }, Array.Empty<int>(), "Missing or conflicting structure-role evidence; review the source OD.");
                }
            }

            for (int i = 0; i < targetArray.Length; i++)
            {
                StormStructureTarget target = targetArray[i];
                bool validRole = target.IsExistingOutline
                    ? target.Role == StormStructureRole.Unknown || target.Role == StormStructureRole.DropInlet || target.Role == StormStructureRole.JunctionBox
                    : target.Role >= StormStructureRole.DropInlet && target.Role <= StormStructureRole.JunctionBox;
                if (Clean(target.Id).Length == 0 || !Finite(target.X, target.Y) ||
                    !validRole)
                {
                    blockedTargets.Add(i);
                    Issue("InvalidTarget", Array.Empty<int>(), new[] { i }, "Target requires an ID, finite XY and a supported role; only explicitly flagged existing DI/box outlines may be unbound.");
                }
                if (target.IsExistingOutline || (target.Role == StormStructureRole.JunctionBox && target.Footprint != null))
                {
                    validFootprints[i] = target.Footprint != null && IsValidFootprint(target.Footprint);
                    if (!validFootprints[i])
                    {
                        blockedTargets.Add(i);
                        Issue("InvalidFootprint", Array.Empty<int>(), new[] { i }, "Outline requires a footprint with at least three finite vertices and nonzero finite area.");
                    }
                }
            }

            foreach (var group in Enumerable.Range(0, sourceArray.Length)
                .GroupBy(i => Clean(sourceArray[i].Id), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Key.Length > 0 && g.Count() > 1))
            {
                int[] members = group.ToArray();
                blockedSources.UnionWith(members);
                Issue("DuplicateSourceId", members, Array.Empty<int>(), "A source entity ID occurs more than once.");
            }

            foreach (var group in Enumerable.Range(0, sourceArray.Length)
                .GroupBy(i => Clean(sourceArray[i].Name), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Key.Length > 0 && g.Count() > 1))
            {
                int[] members = group.ToArray();
                blockedSources.UnionWith(members);
                Issue("DuplicateIdentity", members, Array.Empty<int>(), "A structure name occurs more than once; do not resolve duplicate imports by distance.");
            }

            foreach (var group in Enumerable.Range(0, targetArray.Length)
                .GroupBy(i => Clean(targetArray[i].Id), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Key.Length > 0 && g.Count() > 1))
            {
                int[] members = group.ToArray();
                blockedTargets.UnionWith(members);
                Issue("DuplicateTargetId", Array.Empty<int>(), members, "A destination entity ID occurs more than once.");
            }

            foreach (var group in Enumerable.Range(0, sourceArray.Length)
                .Where(i => roles[i] == StormStructureRole.Access || roles[i] == StormStructureRole.JunctionBox)
                .GroupBy(i => BaseName(sourceArray[i].Name), StringComparer.OrdinalIgnoreCase))
            {
                int[] access = group.Where(i => roles[i] == StormStructureRole.Access).ToArray();
                int[] boxes = group.Where(i => roles[i] == StormStructureRole.JunctionBox).ToArray();
                int[] members = group.ToArray();
                if (access.Length > 1 || boxes.Length > 1)
                {
                    blockedSources.UnionWith(members);
                    Issue("AmbiguousPair", members, Array.Empty<int>(), "A base name has multiple access or box records.");
                    continue;
                }

                // Standalone access and standalone boxes are valid. Their association
                // is by unique exact base name, not shared XY: an access structure can
                // be physically eccentric to its box. Each source still has to match
                // its OWN role-specific geometry using the bounded checks below.
                if (access.Length != 1 || boxes.Length != 1)
                    continue;
                if (members.Any(blockedSources.Contains))
                {
                    blockedSources.UnionWith(members);
                    Issue("InvalidPair", members, Array.Empty<int>(), "One member of the named access/box pair is invalid or duplicated.");
                }
            }

            var targetIndicesBySource = Enumerable.Range(0, sourceArray.Length).Select(_ => new List<int>()).ToArray();
            var sourceIndicesByTarget = Enumerable.Range(0, targetArray.Length).Select(_ => new List<int>()).ToArray();
            for (int s = 0; s < sourceArray.Length; s++)
            {
                if (roles[s] == StormStructureRole.Unknown || roles[s] == StormStructureRole.NullPipeEnd ||
                    !Finite(sourceArray[s].X, sourceArray[s].Y))
                    continue;
                for (int t = 0; t < targetArray.Length; t++)
                {
                    StormStructureSource source = sourceArray[s];
                    StormStructureTarget target = targetArray[t];
                    if (!IsRoleCompatible(roles[s], target) || !IsGeometryCandidate(source, target, validFootprints[t]))
                        continue;
                    // Even a duplicate/blocked record competes for geometry. Removing
                    // it here could incorrectly make another source appear unique.
                    targetIndicesBySource[s].Add(t);
                    sourceIndicesByTarget[t].Add(s);
                }
            }

            for (int s = 0; s < sourceArray.Length; s++)
            {
                if (blockedSources.Contains(s) || roles[s] == StormStructureRole.NullPipeEnd)
                    continue;
                List<int> candidates = targetIndicesBySource[s];
                if (candidates.Count == 0)
                    Issue("UnmatchedSource", new[] { s }, Array.Empty<int>(),
                        roles[s] == StormStructureRole.JunctionBox
                            ? "No containing junction footprint within 25 drawing units and no center-only target within 0.10."
                            : roles[s] == StormStructureRole.DropInlet
                                ? "No DI center within 0.10 drawing units or containing compatible existing outline within 25."
                                : "No same-role destination center within 0.10 drawing units.");
                else if (candidates.Count > 1)
                    Issue("AmbiguousSource", new[] { s }, candidates, "Multiple compatible destinations satisfy the geometry checks; no nearest-target fallback is allowed.");
                else
                {
                    int t = candidates[0];
                    if (blockedTargets.Contains(t))
                        Issue("BlockedTarget", new[] { s }, new[] { t }, "The only candidate destination is invalid or duplicated.");
                    else if (sourceIndicesByTarget[t].Count == 1)
                        matches.Add(new StormStructureMatch(sourceArray[s].Id, targetArray[t].Id, roles[s]));
                }
            }

            for (int t = 0; t < targetArray.Length; t++)
            {
                if (blockedTargets.Contains(t))
                    continue;
                List<int> candidates = sourceIndicesByTarget[t];
                if (candidates.Count == 0)
                    Issue("UnmatchedTarget", Array.Empty<int>(), new[] { t },
                        validFootprints[t]
                            ? "No compatible source lies inside/on this footprint within 25 drawing units of its center."
                            : "No same-role source center within 0.10 drawing units.");
                else if (candidates.Count > 1)
                    Issue("AmbiguousTarget", candidates, new[] { t }, "Multiple compatible sources compete for this destination; none is accepted.");
                else if (blockedSources.Contains(candidates[0]))
                    Issue("BlockedSource", candidates, new[] { t }, "The only candidate source requires review.");
            }

            return new StormStructureMatchResult(matches.ToArray(), issues.ToArray())
            {
                // Exemption is applied only after ordinary identity, duplicate and
                // coordinate validation; malformed pipe ends still require review.
                PreservedPipeEndSourceIds = Enumerable.Range(0, sourceArray.Length)
                    .Where(i => roles[i] == StormStructureRole.NullPipeEnd && !blockedSources.Contains(i))
                    .Select(i => sourceArray[i].Id).ToArray()
            };
        }

        private static string Clean(string? value) => (value ?? string.Empty).Trim();
        private static bool Finite(double x, double y) => double.IsFinite(x) && double.IsFinite(y);

        // Geometry-only containment for host inner-outline/completion checks. Callers
        // must separately enforce JunctionSearchRadius when matching a source.
        public static bool ContainsFootprint(IReadOnlyList<StormStructureVertex> footprint, double x, double y)
        {
            ArgumentNullException.ThrowIfNull(footprint);
            return Finite(x, y) && IsValidFootprint(footprint) && IsInsideOrOnFootprint(x, y, footprint);
        }

        public static bool ContainsOutline(IReadOnlyList<StormStructureVertex> outer, IReadOnlyList<StormStructureVertex> inner)
        {
            ArgumentNullException.ThrowIfNull(outer);
            ArgumentNullException.ThrowIfNull(inner);
            if (!IsValidFootprint(outer) || !IsValidFootprint(inner) ||
                inner.Any(v => !IsInsideOrOnFootprint(v.X, v.Y, outer)))
                return false;

            for (int i = 0; i < inner.Count; i++)
            {
                StormStructureVertex a = inner[i];
                StormStructureVertex b = inner[(i + 1) % inner.Count];
                double rx = b.X - a.X, ry = b.Y - a.Y;
                double lengthSquared = rx * rx + ry * ry;
                if (lengthSquared == 0.0)
                    continue;
                if (!double.IsFinite(lengthSquared))
                    return false;
                var cuts = new List<double> { 0.0, 1.0 };
                for (int j = 0; j < outer.Count; j++)
                {
                    StormStructureVertex c = outer[j];
                    StormStructureVertex d = outer[(j + 1) % outer.Count];
                    double sx = d.X - c.X, sy = d.Y - c.Y;
                    double qx = c.X - a.X, qy = c.Y - a.Y;
                    double denominator = rx * sy - ry * sx;
                    if (denominator != 0.0)
                    {
                        double t = (qx * sy - qy * sx) / denominator;
                        double u = (qx * ry - qy * rx) / denominator;
                        if (double.IsFinite(t) && double.IsFinite(u) && t >= 0.0 && t <= 1.0 && u >= 0.0 && u <= 1.0)
                            cuts.Add(t);
                    }
                    else if (Math.Abs(qx * ry - qy * rx) <= FootprintBoundaryTolerance * Math.Sqrt(lengthSquared))
                    {
                        // Collinear boundary overlaps also delimit intervals. The
                        // midpoint checks below accept actual boundary segments.
                        double tc = (qx * rx + qy * ry) / lengthSquared;
                        double td = ((d.X - a.X) * rx + (d.Y - a.Y) * ry) / lengthSquared;
                        if (tc > 0.0 && tc < 1.0) cuts.Add(tc);
                        if (td > 0.0 && td < 1.0) cuts.Add(td);
                    }
                }
                double[] orderedCuts = cuts.Distinct().OrderBy(t => t).ToArray();
                for (int j = 1; j < orderedCuts.Length; j++)
                {
                    double midpoint = (orderedCuts[j - 1] + orderedCuts[j]) * 0.5;
                    if (!IsInsideOrOnFootprint(a.X + midpoint * rx, a.Y + midpoint * ry, outer))
                        return false;
                }
            }
            // Identical boundaries are contained. The host separately enforces that
            // an inner-wall outline has smaller area than its outer-wall outline.
            return true;
        }

        private static bool IsRoleCompatible(StormStructureRole sourceRole, StormStructureTarget target)
        {
            if (target.IsExistingOutline)
            {
                // Unknown is an explicitly unbound physical outline, not an unknown
                // source. A single target ID must compete across both source roles.
                return (sourceRole == StormStructureRole.DropInlet || sourceRole == StormStructureRole.JunctionBox) &&
                    (target.Role == StormStructureRole.Unknown || target.Role == sourceRole);
            }
            return sourceRole == target.Role;
        }

        private static bool IsGeometryCandidate(StormStructureSource source, StormStructureTarget target, bool validFootprint)
        {
            if (target.IsExistingOutline || (target.Role == StormStructureRole.JunctionBox && target.Footprint != null))
            {
                return validFootprint && target.Footprint != null &&
                    WithinRadius(source.X, source.Y, target.X, target.Y, JunctionSearchRadius) &&
                    IsInsideOrOnFootprint(source.X, source.Y, target.Footprint);
            }
            // An ordinary DI block or access target stays centered. A footprint
            // alone cannot opt a DI target into existing-outline containment.
            return WithinRadius(source.X, source.Y, target.X, target.Y, MatchTolerance);
        }

        private static bool IsValidFootprint(IReadOnlyList<StormStructureVertex> vertices)
        {
            if (vertices.Count < 3 || vertices.Any(v => !Finite(v.X, v.Y)))
                return false;
            // Translate before computing signed area to avoid cancellation at survey
            // coordinates far from the origin. Input is the caller's closed polygon.
            StormStructureVertex origin = vertices[0];
            double twiceArea = 0.0;
            for (int i = 1; i + 1 < vertices.Count; i++)
            {
                double ax = vertices[i].X - origin.X;
                double ay = vertices[i].Y - origin.Y;
                double bx = vertices[i + 1].X - origin.X;
                double by = vertices[i + 1].Y - origin.Y;
                twiceArea += ax * by - ay * bx;
            }
            return double.IsFinite(twiceArea) && Math.Abs(twiceArea) > 0.0;
        }

        private static bool IsInsideOrOnFootprint(double x, double y, IReadOnlyList<StormStructureVertex> vertices)
        {
            bool inside = false;
            for (int i = 0, j = vertices.Count - 1; i < vertices.Count; j = i++)
            {
                StormStructureVertex a = vertices[j];
                StormStructureVertex b = vertices[i];
                double dx = b.X - a.X;
                double dy = b.Y - a.Y;
                double px = x - a.X;
                double py = y - a.Y;
                double lengthSquared = dx * dx + dy * dy;
                if (lengthSquared == 0.0)
                {
                    if (WithinRadius(x, y, a.X, a.Y, FootprintBoundaryTolerance))
                        return true;
                    continue;
                }
                double length = Math.Sqrt(lengthSquared);
                double cross = dx * py - dy * px;
                double projection = px * dx + py * dy;
                // Only numerical boundary slack, never the 0.10 center tolerance.
                if (Math.Abs(cross) <= FootprintBoundaryTolerance * length &&
                    projection >= -FootprintBoundaryTolerance * length &&
                    projection <= lengthSquared + FootprintBoundaryTolerance * length)
                    return true;
                if ((a.Y > y) != (b.Y > y) && px < dx * py / dy)
                    inside = !inside;
            }
            return inside;
        }

        private static bool WithinRadius(double x1, double y1, double x2, double y2, double radius)
        {
            if (!Finite(x1, y1) || !Finite(x2, y2))
                return false;
            double dx = Math.Abs(x1 - x2);
            double dy = Math.Abs(y1 - y2);
            return dx <= radius && dy <= radius && dx * dx + dy * dy <= radius * radius;
        }
    }
}
