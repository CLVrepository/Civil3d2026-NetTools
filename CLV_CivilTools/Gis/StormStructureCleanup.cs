using System;
using System.Collections.Generic;
using System.Linq;

namespace CLV_CivilTools.Gis
{
    internal enum StormCleanupSourceState { LiveImportedPoint, ArchivedCompletion }
    internal enum StormMarkerOdState { Empty, Present, Unreadable }

    internal sealed record StormCleanupSource(StormStructureSource Source, StormCleanupSourceState State, bool VerifiedNow,
        IReadOnlyList<string>? ArchivedMarkerIds = null);
    internal sealed record StormCleanupMarker(string Id, string EffectiveName, string Layer, double X, double Y, StormMarkerOdState OdState);
    internal sealed record StormCleanupMarkerMatch(string SourceId, string MarkerId);
    internal sealed record StormCleanupPlan(IReadOnlyList<string> SourceIdsToArchiveAndErase,
        IReadOnlyList<StormCleanupMarkerMatch> MarkerMatches, IReadOnlyList<StormStructureIssue> Issues)
    {
        public IReadOnlyList<string> VerifiedOwnerIds { get; init; } = Array.Empty<string>();
    }

    /// <summary>
    /// Pure cleanup eligibility only. VerifiedNow means the host freshly verified all
    /// owned outputs and source/archive identity; a saved completion flag is insufficient.
    /// The host must archive and read back a live source before erasing it, and revalidate
    /// entity type, exact marker identity/layer and empty OD in the applying transaction.
    /// </summary>
    internal static class StormStructureCleanup
    {
        public const double MarkerTolerance = StormStructureMatching.MatchTolerance;

        public static StormCleanupPlan Plan(IEnumerable<StormCleanupSource> sources,
            IEnumerable<StormStructureCompletion> completions, IEnumerable<StormCleanupMarker> markers)
        {
            ArgumentNullException.ThrowIfNull(sources);
            ArgumentNullException.ThrowIfNull(completions);
            ArgumentNullException.ThrowIfNull(markers);

            // Validate the full claim set first. Filtering out unverified/malformed
            // owners first would hide collisions and make another claim look safe.
            StormStructureCompletionPlan completionPlan = StormStructureMatching.PlanCompletions(completions);
            StormCleanupSource[] sourceArray = sources
                .OrderBy(s => Clean(s.Source.Id), StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => Clean(s.Source.Id), StringComparer.Ordinal)
                .ThenBy(s => Clean(s.Source.Name), StringComparer.OrdinalIgnoreCase).ToArray();
            StormCleanupMarker[] markerArray = markers
                .OrderBy(m => Clean(m.Id), StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => Clean(m.Id), StringComparer.Ordinal)
                .ThenBy(m => Clean(m.EffectiveName), StringComparer.OrdinalIgnoreCase).ToArray();
            StormStructureRole[] roles = sourceArray.Select(s => StormStructureMatching.Classify(s.Source.Name, s.Source.PartSizeName)).ToArray();
            StormStructureRole[] markerRoles = markerArray.Select(MarkerRole).ToArray();
            var issues = new List<StormStructureIssue>(completionPlan.Issues);
            var blockedSources = new HashSet<int>();
            var blockedMarkers = new HashSet<int>();
            var ambiguousMarkers = new HashSet<int>();
            var ambiguousSources = new HashSet<int>();
            var acceptedOwners = completionPlan.Accepted.ToDictionary(c => Clean(c.SourceId), StringComparer.OrdinalIgnoreCase);
            var conflictedOwners = completionPlan.ConflictedSourceIds.Select(Clean).ToHashSet(StringComparer.OrdinalIgnoreCase);

            void Issue(string code, IEnumerable<int> sourceIndices, IEnumerable<int> markerIndices, string message)
                => issues.Add(new StormStructureIssue(code,
                    sourceIndices.Select(i => sourceArray[i].Source.Id).ToArray(),
                    markerIndices.Select(i => markerArray[i].Id).ToArray(), message));

            for (int s = 0; s < sourceArray.Length; s++)
            {
                StormCleanupSource owner = sourceArray[s];
                StormStructureSource source = owner.Source;
                if (Clean(source.Id).Length == 0 || Clean(source.Name).Length == 0 || !Finite(source.X, source.Y) ||
                    (owner.State != StormCleanupSourceState.LiveImportedPoint && owner.State != StormCleanupSourceState.ArchivedCompletion))
                {
                    blockedSources.Add(s);
                    Issue("InvalidCleanupSource", new[] { s }, Array.Empty<int>(), "Source identity, coordinates or lifecycle state is invalid; retain it.");
                }
                if (conflictedOwners.Contains(Clean(source.Id)))
                    blockedSources.Add(s);
                if (owner.State == StormCleanupSourceState.ArchivedCompletion && owner.ArchivedMarkerIds != null &&
                    (owner.ArchivedMarkerIds.Any(id => Clean(id).Length == 0) ||
                     owner.ArchivedMarkerIds.Select(Clean).Distinct(StringComparer.OrdinalIgnoreCase).Count() != owner.ArchivedMarkerIds.Count))
                {
                    blockedSources.Add(s);
                    Issue("InvalidArchivedMarkerIds", new[] { s }, Array.Empty<int>(), "Archived marker IDs are malformed or duplicated; retain markers for review.");
                }
                if (roles[s] == StormStructureRole.Unknown)
                {
                    blockedSources.Add(s);
                    Issue("UnknownCleanupRole", new[] { s }, Array.Empty<int>(), "Unknown/conflicting source role remains unresolved and may compete for nearby markers.");
                }
                else if (roles[s] == StormStructureRole.NullPipeEnd)
                {
                    // Explicit non-graphic STUBs are intentionally retained and never
                    // require or claim DI/access markers.
                    blockedSources.Add(s);
                }
                else if (!owner.VerifiedNow)
                {
                    blockedSources.Add(s);
                    Issue("UnverifiedCleanupSource", new[] { s }, Array.Empty<int>(), "Current output/identity verification is required before cleanup.");
                }
                else if (!acceptedOwners.ContainsKey(Clean(source.Id)))
                {
                    blockedSources.Add(s);
                    Issue("MissingVerifiedCompletion", new[] { s }, Array.Empty<int>(), "No accepted full output-ownership claim exists for this source.");
                }
            }

            foreach (var group in Enumerable.Range(0, sourceArray.Length)
                .GroupBy(s => Clean(sourceArray[s].Source.Id), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Key.Length > 0 && g.Count() > 1))
            {
                int[] members = group.ToArray();
                blockedSources.UnionWith(members);
                Issue("DuplicateCleanupSourceId", members, Array.Empty<int>(), "Duplicate live/archive source IDs must be resolved before cleanup.");
            }
            foreach (var group in Enumerable.Range(0, sourceArray.Length)
                .GroupBy(s => Clean(sourceArray[s].Source.Name), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Key.Length > 0 && g.Count() > 1))
            {
                int[] members = group.ToArray();
                blockedSources.UnionWith(members);
                Issue("DuplicateCleanupIdentity", members, Array.Empty<int>(), "Duplicate live/archive structure names must be resolved before cleanup.");
            }

            var knownSourceIds = sourceArray.Select(s => Clean(s.Source.Id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (StormStructureCompletion completion in completionPlan.Accepted)
            {
                if (!knownSourceIds.Contains(Clean(completion.SourceId)))
                    issues.Add(new StormStructureIssue("MissingCompletionSource", new[] { completion.SourceId }, completion.OutputIds,
                        "Ownership claim has no corresponding live/archive source identity; reserve no cleanup action for it."));
            }

            for (int m = 0; m < markerArray.Length; m++)
            {
                StormCleanupMarker marker = markerArray[m];
                if (Clean(marker.Id).Length == 0 || !Finite(marker.X, marker.Y) ||
                    markerRoles[m] == StormStructureRole.Unknown || !IsMarkerLayer(marker.Layer))
                {
                    blockedMarkers.Add(m);
                    Issue("IneligibleCleanupMarker", Array.Empty<int>(), new[] { m }, "Marker requires an exact known name/layer, an ID and finite coordinates; retain it.");
                }
                if (marker.OdState != StormMarkerOdState.Empty)
                {
                    blockedMarkers.Add(m);
                    Issue("MarkerObjectDataProtected", Array.Empty<int>(), new[] { m }, "Marker OD is present, unreadable or unknown; it must not be erased.");
                }
            }
            foreach (var group in Enumerable.Range(0, markerArray.Length)
                .GroupBy(m => Clean(markerArray[m].Id), StringComparer.OrdinalIgnoreCase)
                .Where(g => g.Key.Length > 0 && g.Count() > 1))
            {
                int[] members = group.ToArray();
                blockedMarkers.UnionWith(members);
                ambiguousMarkers.UnionWith(members);
                Issue("DuplicateCleanupMarkerId", Array.Empty<int>(), members, "Duplicate marker IDs leave ownership ambiguous; retain the affected cohort.");
            }

            var markersBySource = sourceArray.Select(_ => new List<int>()).ToArray();
            var sourcesByMarker = markerArray.Select(_ => new List<int>()).ToArray();
            for (int s = 0; s < sourceArray.Length; s++)
            {
                StormStructureSource source = sourceArray[s].Source;
                if (!Finite(source.X, source.Y) || roles[s] == StormStructureRole.JunctionBox || roles[s] == StormStructureRole.NullPipeEnd)
                    continue;
                for (int m = 0; m < markerArray.Length; m++)
                {
                    StormCleanupMarker marker = markerArray[m];
                    if (markerRoles[m] == StormStructureRole.Unknown || !IsMarkerLayer(marker.Layer) ||
                        !Finite(marker.X, marker.Y) ||
                        (roles[s] != StormStructureRole.Unknown && roles[s] != markerRoles[m]) ||
                        !WithinTolerance(source.X, source.Y, marker.X, marker.Y))
                        continue;
                    // Unverified, duplicate and unknown owners remain competitors.
                    // Recognized protected markers also compete but can never be erased.
                    markersBySource[s].Add(m);
                    sourcesByMarker[m].Add(s);
                }
            }

            for (int s = 0; s < sourceArray.Length; s++)
            {
                if (markersBySource[s].Count > 1)
                {
                    ambiguousSources.Add(s);
                    ambiguousMarkers.UnionWith(markersBySource[s]);
                    Issue("AmbiguousCleanupMarkers", new[] { s }, markersBySource[s], "Multiple recognized markers compete for one owner; retain the source and markers.");
                }
            }
            for (int m = 0; m < markerArray.Length; m++)
            {
                if (sourcesByMarker[m].Count > 1)
                {
                    ambiguousMarkers.Add(m);
                    ambiguousSources.UnionWith(sourcesByMarker[m]);
                    Issue("AmbiguousCleanupOwner", sourcesByMarker[m], new[] { m }, "Multiple possible source owners compete for this marker; retain the affected cohort.");
                }
            }
            // Duplicate marker IDs or an ambiguous neighbor must not make a source
            // appear resolved merely because that marker cannot be deleted.
            foreach (int m in ambiguousMarkers)
                ambiguousSources.UnionWith(sourcesByMarker[m]);

            var matches = new List<StormCleanupMarkerMatch>();
            for (int s = 0; s < sourceArray.Length; s++)
            {
                if (blockedSources.Contains(s) || ambiguousSources.Contains(s) || markersBySource[s].Count != 1)
                    continue;
                int m = markersBySource[s][0];
                if (blockedMarkers.Contains(m) || ambiguousMarkers.Contains(m) || sourcesByMarker[m].Count != 1)
                    continue;
                if (sourceArray[s].State == StormCleanupSourceState.ArchivedCompletion &&
                    (sourceArray[s].ArchivedMarkerIds == null ||
                     !sourceArray[s].ArchivedMarkerIds!.Any(id => string.Equals(Clean(id), Clean(markerArray[m].Id), StringComparison.OrdinalIgnoreCase))))
                {
                    Issue("UnrecordedArchivedMarker", new[] { s }, new[] { m }, "This marker ID was not recorded in the verified archive; proximity alone cannot authorize its removal.");
                    continue;
                }
                matches.Add(new StormCleanupMarkerMatch(sourceArray[s].Source.Id, markerArray[m].Id));
            }
            int[] verifiedOwners = Enumerable.Range(0, sourceArray.Length)
                .Where(s => !blockedSources.Contains(s) && !ambiguousSources.Contains(s) && sourceArray[s].VerifiedNow &&
                    acceptedOwners.ContainsKey(Clean(sourceArray[s].Source.Id))).ToArray();
            string[] sourceIds = verifiedOwners
                .Where(s => sourceArray[s].State == StormCleanupSourceState.LiveImportedPoint)
                .Select(s => sourceArray[s].Source.Id).ToArray();
            return new StormCleanupPlan(sourceIds, matches.ToArray(), issues.ToArray())
            {
                VerifiedOwnerIds = verifiedOwners.Select(s => sourceArray[s].Source.Id).ToArray()
            };
        }

        private static string Clean(string? value) => (value ?? string.Empty).Trim();
        private static bool Finite(double x, double y) => double.IsFinite(x) && double.IsFinite(y);
        private static bool IsMarkerLayer(string? layer) => string.Equals(Clean(layer), "V-SURV-CHCK", StringComparison.OrdinalIgnoreCase);
        private static StormStructureRole MarkerRole(StormCleanupMarker marker)
        {
            string name = Clean(marker.EffectiveName);
            if (string.Equals(name, "UFLS_DI_MARK", StringComparison.OrdinalIgnoreCase)) return StormStructureRole.DropInlet;
            if (string.Equals(name, "UFLS_MH_MARK", StringComparison.OrdinalIgnoreCase)) return StormStructureRole.Access;
            return StormStructureRole.Unknown;
        }
        private static bool WithinTolerance(double x1, double y1, double x2, double y2)
        {
            double dx = Math.Abs(x1 - x2), dy = Math.Abs(y1 - y2);
            return dx <= MarkerTolerance && dy <= MarkerTolerance && dx * dx + dy * dy <= MarkerTolerance * MarkerTolerance;
        }
    }
}
