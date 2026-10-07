using System;
using System.Collections.Generic;
using System.Linq;

namespace CLV_CivilTools.Gis
{
    internal enum StormPipeSizeKind { Invalid, SingleLine, TwoWall }
    internal enum StormPipeUtilityKind { Candidate, ExcludedSewer, Review }

    internal sealed record StormPipeWallRecord<T>(T Id, string Handle, string Geometry) where T : notnull;

    internal sealed record StormPipeCompletionRecord<T>(T SourceId, string SourceHandle, double DiameterFeet,
        string ObjectDataFingerprint, string SourceGeometry, string OriginalLayer,
        IReadOnlyList<StormPipeWallRecord<T>> Positive, IReadOnlyList<StormPipeWallRecord<T>> Negative) where T : notnull;

    /// <summary>
    /// Host-independent decisions shared by managed pipe preparation and its tests.
    /// Native geometry, live ObjectIds, and OD must still be read and verified by the host.
    /// An equivalent completion record alone does not authorize reuse or cleanup.
    /// </summary>
    internal static class StormPipePreparationRules
    {
        internal static StormPipeSizeKind ClassifyDiameter(double diameterFeet)
            => !double.IsFinite(diameterFeet) || diameterFeet <= 0 ? StormPipeSizeKind.Invalid :
                diameterFeet < 1.0 ? StormPipeSizeKind.SingleLine : StormPipeSizeKind.TwoWall;

        internal static bool TryValidateOwnership<T>(T sourceId, double diameterFeet,
            IReadOnlyList<T> positiveIds, IReadOnlyList<T> negativeIds, out string reason) where T : notnull
        {
            StormPipeSizeKind kind = ClassifyDiameter(diameterFeet);
            if (kind == StormPipeSizeKind.Invalid)
            {
                reason = "InsideDiameter must be positive and finite, in feet.";
                return false;
            }
            if (sourceId is null || positiveIds == null || negativeIds == null)
            {
                reason = "Pipe source or side ownership is missing.";
                return false;
            }
            T[] ids = positiveIds.Concat(negativeIds).ToArray();
            if (kind == StormPipeSizeKind.SingleLine)
            {
                reason = ids.Length == 0 ? string.Empty : "A below-12-inch single-line source cannot claim offset walls.";
                return ids.Length == 0;
            }
            if (positiveIds.Count == 0 || negativeIds.Count == 0)
            {
                reason = "A two-wall pipe requires a nonempty output on both native offset sides.";
                return false;
            }
            if (ids.Any(id => id is null) || ids.Distinct().Count() != ids.Length || ids.Contains(sourceId))
            {
                reason = "Every offset output must be distinct from every other output and the retained source.";
                return false;
            }
            reason = string.Empty;
            return true;
        }

        internal static bool SameCompletion<T>(StormPipeCompletionRecord<T>? a, StormPipeCompletionRecord<T>? b) where T : notnull
            => a == null || b == null ? a == null && b == null :
                EqualityComparer<T>.Default.Equals(a.SourceId, b.SourceId) && a.SourceHandle == b.SourceHandle &&
                a.DiameterFeet == b.DiameterFeet && a.ObjectDataFingerprint == b.ObjectDataFingerprint &&
                a.SourceGeometry == b.SourceGeometry && a.OriginalLayer == b.OriginalLayer &&
                a.Positive.SequenceEqual(b.Positive) && a.Negative.SequenceEqual(b.Negative);

        /// <summary>
        /// Proves one permitted open wall terminal from its already-owned source path.
        /// This is not a nearest-wall match or a general exemption near a null marker.
        /// The host must verify ownership, current native geometry and OD first.
        /// </summary>
        internal static bool IsVerifiedOpenNullTerminal(IReadOnlyList<StormStructureVertex> sourcePath,
            IReadOnlyList<StormStructureVertex> wallPath, bool wallStart, double diameterFeet,
            IReadOnlyList<StormStructureSource> structureSources,
            IReadOnlyList<IReadOnlyList<StormStructureVertex>>? physicalFootprints = null)
        {
            if (ClassifyDiameter(diameterFeet) != StormPipeSizeKind.TwoWall || structureSources == null ||
                !StormTerminalTrim.TryValidatePath(sourcePath, out _) || !StormTerminalTrim.TryValidatePath(wallPath, out _))
                return false;
            // An unlocatable possible competitor cannot establish an unopposed null end.
            if (structureSources.Any(source => source == null || !Finite(source.X, source.Y))) return false;
            StormStructureVertex wallPoint = wallStart ? wallPath[0] : wallPath[wallPath.Count - 1];
            StormStructureVertex wallNeighbor = wallStart ? wallPath[1] : wallPath[wallPath.Count - 2];
            if (!TryUnitInward(wallPoint, wallNeighbor, out double wallX, out double wallY)) return false;

            var matches = new List<StormStructureVertex>();
            CheckSourceTerminal(sourcePath[0], sourcePath[1]);
            CheckSourceTerminal(sourcePath[sourcePath.Count - 1], sourcePath[sourcePath.Count - 2]);
            if (matches.Count != 1) return false;
            StormStructureVertex anchor = matches[0];
            // Physical boxes can be off-center relative to their imported point. Their
            // verified footprint still competes even when that point is not within .10.
            if (physicalFootprints != null && physicalFootprints.Any(footprint =>
                !StormTerminalTrim.TryValidateBoundary(footprint, out _) || StormTerminalTrim.ContainsEndpoint(footprint, anchor)))
                return false;
            StormStructureSource[] near = structureSources.Where(source =>
                Within(anchor.X, anchor.Y, source.X, source.Y, StormStructureMatching.MatchTolerance)).ToArray();
            if (near.Length != 1) return false;
            StormStructureSource candidate = near[0];
            if (string.IsNullOrWhiteSpace(candidate.Id) || string.IsNullOrWhiteSpace(candidate.Name) ||
                StormStructureMatching.Classify(candidate.Name, candidate.PartSizeName) != StormStructureRole.NullPipeEnd)
                return false;
            // A repeated identity is not a valid unique null source even when its other
            // occurrence lies outside the local endpoint tolerance.
            return structureSources.Count(source => string.Equals(source.Id?.Trim(), candidate.Id.Trim(), StringComparison.OrdinalIgnoreCase)) == 1 &&
                structureSources.Count(source => string.Equals(source.Name?.Trim(), candidate.Name.Trim(), StringComparison.OrdinalIgnoreCase)) == 1;

            void CheckSourceTerminal(StormStructureVertex point, StormStructureVertex neighbor)
            {
                if (!TryUnitInward(point, neighbor, out double inwardX, out double inwardY)) return;
                double cross = inwardX * wallY - inwardY * wallX;
                double dot = inwardX * wallX + inwardY * wallY;
                if (!double.IsFinite(cross) || !double.IsFinite(dot) || Math.Abs(cross) > 1e-8 || dot <= 0) return;
                double half = diameterFeet / 2.0;
                // Native sign conventions can reverse with source/wall direction. Both
                // perpendicular signs are tested, but only this source's two terminals.
                bool positive = Within(wallPoint.X, wallPoint.Y, point.X - inwardY * half,
                    point.Y + inwardX * half, StormStructureMatching.MatchTolerance);
                bool negative = Within(wallPoint.X, wallPoint.Y, point.X + inwardY * half,
                    point.Y - inwardX * half, StormStructureMatching.MatchTolerance);
                if (positive != negative) matches.Add(point);
            }
        }

        private static bool TryUnitInward(StormStructureVertex point, StormStructureVertex neighbor, out double x, out double y)
        {
            x = neighbor.X - point.X;
            y = neighbor.Y - point.Y;
            double length = Math.Sqrt(x * x + y * y);
            if (!double.IsFinite(length) || length <= StormTerminalTrim.Tolerance) return false;
            x /= length;
            y /= length;
            return Finite(x, y);
        }

        private static bool Finite(double x, double y) => double.IsFinite(x) && double.IsFinite(y);
        private static bool Within(double ax, double ay, double bx, double by, double tolerance)
        {
            if (!Finite(ax, ay) || !Finite(bx, by)) return false;
            double x = ax - bx, y = ay - by;
            double distance = Math.Sqrt(x * x + y * y);
            return double.IsFinite(distance) && distance <= tolerance;
        }

        internal static bool IsKnownSourceLayer(string name)
            => (name.StartsWith("GIS-", StringComparison.OrdinalIgnoreCase) && name.Contains("PIPE", StringComparison.OrdinalIgnoreCase)) ||
                (name.StartsWith("V-SURV-", StringComparison.OrdinalIgnoreCase) && name.Contains("PIPE", StringComparison.OrdinalIgnoreCase)) ||
                (name.StartsWith("C-", StringComparison.OrdinalIgnoreCase) && name.Contains("PIPE-CNTR", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Uses only complete native table-name evidence. The host must reject Invalid
        /// and ReadFailed OD inspections before calling this classification gate.
        /// Generic pipe layers/tables do not establish storm utility by themselves.
        /// </summary>
        internal static StormPipeUtilityKind ClassifyUtility(string layer, IReadOnlyList<string> odTableNames)
        {
            if (layer == null || odTableNames == null || odTableNames.Any(name => string.IsNullOrWhiteSpace(name)))
                return StormPipeUtilityKind.Review;
            string[] tokens = layer.Split(new[] { '-', '_', ' ', '.', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            bool storm = (IsKnownSourceLayer(layer) && tokens.Any(token =>
                string.Equals(token, "STRM", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(token, "STORM", StringComparison.OrdinalIgnoreCase))) ||
                odTableNames.Any(name => string.Equals(name, "SD_Pipes", StringComparison.OrdinalIgnoreCase));
            bool sewer = IsSewerName(layer) || odTableNames.Any(IsSewerName);
            return storm && sewer ? StormPipeUtilityKind.Review :
                sewer ? StormPipeUtilityKind.ExcludedSewer : StormPipeUtilityKind.Candidate;
        }

        internal static bool IsSewerName(string name)
        {
            string upper = name.ToUpperInvariant();
            string[] tokens = upper.Split(new[] { '-', '_', ' ', '.', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
            return upper.Contains("SSWR", StringComparison.Ordinal) || upper.Contains("SEWER", StringComparison.Ordinal) ||
                upper.Contains("SEWR", StringComparison.Ordinal) || upper.Contains("SANITARY", StringComparison.Ordinal) ||
                tokens.Contains("SAN", StringComparer.Ordinal) || (tokens.Contains("SS", StringComparer.Ordinal) &&
                    tokens.Any(token => token == "PIPE" || token == "PIPES"));
        }
    }
}
