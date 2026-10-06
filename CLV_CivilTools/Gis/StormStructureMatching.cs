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
        JunctionBox
    }

    internal sealed record StormStructureSource(string Id, string Name, string PartSizeName, double X, double Y);
    internal sealed record StormStructureTarget(string Id, StormStructureRole Role, double X, double Y);
    internal sealed record StormStructureMatch(string SourceId, string TargetId, StormStructureRole Role);
    internal sealed record StormStructureIssue(string Code, IReadOnlyList<string> SourceIds, IReadOnlyList<string> TargetIds, string Message);
    internal sealed record StormStructureMatchResult(IReadOnlyList<StormStructureMatch> Matches, IReadOnlyList<StormStructureIssue> Issues);

    /// <summary>
    /// Pure, conservative planning only: this class never changes drawing geometry or OD.
    /// Caller-supplied identifiers must distinguish entities in the current import snapshot.
    /// Matching is role-aware and mutual one-to-one within 0.10 drawing units in XY.
    /// No nearest-neighbor tie breaking, offset fallback, or assumption that access must
    /// have a box is made. Unknown/ambiguous inputs remain available for manual review.
    /// </summary>
    internal static class StormStructureMatching
    {
        public const double MatchTolerance = 0.10;

        private static readonly Regex DropInletToken = new Regex(
            @"(?<![A-Z0-9])(?:SDDI(?=$|[^A-Z])|TYPE[_\s]+(?:A(?:[_\s]+MOD)?|CM2|CM|C|DM2|D)(?![A-Z0-9]))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex AccessToken = new Regex(
            @"(?<![A-Z0-9])ACCESS\s+STRUCTURE(?![A-Z0-9])",
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
                if (Clean(target.Id).Length == 0 || !Finite(target.X, target.Y) ||
                    target.Role < StormStructureRole.DropInlet || target.Role > StormStructureRole.JunctionBox)
                {
                    blockedTargets.Add(i);
                    Issue("InvalidTarget", Array.Empty<int>(), new[] { i }, "Target requires an ID, a known role, and finite XY coordinates.");
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

                // Standalone access and standalone boxes are valid. When both exact
                // names exist, this centered-only release must validate their pairing.
                if (access.Length != 1 || boxes.Length != 1)
                    continue;
                StormStructureSource a = sourceArray[access[0]];
                StormStructureSource b = sourceArray[boxes[0]];
                if (members.Any(blockedSources.Contains))
                {
                    blockedSources.UnionWith(members);
                    Issue("InvalidPair", members, Array.Empty<int>(), "One member of the named access/box pair is invalid or duplicated.");
                }
                else if (!WithinTolerance(a.X, a.Y, b.X, b.Y))
                {
                    blockedSources.UnionWith(members);
                    Issue("OffsetPair", members, Array.Empty<int>(), "Named access/box centers exceed 0.10 drawing units; preserve both for review.");
                }
            }

            var targetIndicesBySource = Enumerable.Range(0, sourceArray.Length).Select(_ => new List<int>()).ToArray();
            var sourceIndicesByTarget = Enumerable.Range(0, targetArray.Length).Select(_ => new List<int>()).ToArray();
            for (int s = 0; s < sourceArray.Length; s++)
            {
                if (roles[s] == StormStructureRole.Unknown || !Finite(sourceArray[s].X, sourceArray[s].Y))
                    continue;
                for (int t = 0; t < targetArray.Length; t++)
                {
                    StormStructureSource source = sourceArray[s];
                    StormStructureTarget target = targetArray[t];
                    if (roles[s] != target.Role || !WithinTolerance(source.X, source.Y, target.X, target.Y))
                        continue;
                    // Even a duplicate/blocked record competes for geometry. Removing
                    // it here could incorrectly make another source appear unique.
                    targetIndicesBySource[s].Add(t);
                    sourceIndicesByTarget[t].Add(s);
                }
            }

            for (int s = 0; s < sourceArray.Length; s++)
            {
                if (blockedSources.Contains(s))
                    continue;
                List<int> candidates = targetIndicesBySource[s];
                if (candidates.Count == 0)
                    Issue("UnmatchedSource", new[] { s }, Array.Empty<int>(), "No same-role destination center within 0.10 drawing units.");
                else if (candidates.Count > 1)
                    Issue("AmbiguousSource", new[] { s }, candidates, "Multiple same-role destinations are within tolerance; no nearest-target fallback is allowed.");
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
                    Issue("UnmatchedTarget", Array.Empty<int>(), new[] { t }, "No same-role source center within 0.10 drawing units.");
                else if (candidates.Count > 1)
                    Issue("AmbiguousTarget", candidates, new[] { t }, "Multiple same-role sources compete for this destination; none is accepted.");
                else if (blockedSources.Contains(candidates[0]))
                    Issue("BlockedSource", candidates, new[] { t }, "The only candidate source requires review.");
            }

            return new StormStructureMatchResult(matches.ToArray(), issues.ToArray());
        }

        private static string Clean(string? value) => (value ?? string.Empty).Trim();
        private static bool Finite(double x, double y) => double.IsFinite(x) && double.IsFinite(y);

        private static bool WithinTolerance(double x1, double y1, double x2, double y2)
        {
            if (!Finite(x1, y1) || !Finite(x2, y2))
                return false;
            double dx = Math.Abs(x1 - x2);
            double dy = Math.Abs(y1 - y2);
            return dx <= MatchTolerance && dy <= MatchTolerance &&
                dx * dx + dy * dy <= MatchTolerance * MatchTolerance;
        }
    }
}
