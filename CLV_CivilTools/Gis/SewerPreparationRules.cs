using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CLV_CivilTools.Gis
{
    internal enum SewerPipeSizeKind { Invalid, SingleLine, CenterAndWalls }
    internal enum SewerPipeUtilityKind { Candidate, ExcludedStorm, Review }
    internal enum SewerOpenEndKind { None, NullStub, Connection }

    internal static class SewerPreparationRules
    {
        // A utility word or numbered utility identifier must occupy its own token.
        // In particular, surnames such as Stormer and unrelated embedded text are
        // not utility evidence. SD_ / SD- are established identifier prefixes;
        // table names use the separate exact-name checks below.
        private static readonly Regex StormToken = new Regex(
            @"(?<![\p{L}\p{N}])(?:(?:STRM|STORM)[0-9]*(?![\p{L}\p{N}])|SD(?=[_-][\p{L}\p{N}]))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex SewerToken = new Regex(
            @"(?<![\p{L}\p{N}])(?:SSWR|SEWR|SEWER|SSMH|SANITARY)[0-9]*(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex PipeToken = new Regex(@"(?<![\p{L}\p{N}])PIPES?(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex CenterToken = new Regex(@"(?<![\p{L}\p{N}])CNTR(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>
        /// Classifies complete, already-read native utility evidence before requiring
        /// the sewer Pipes schema or diameter. identityValues contains all Character
        /// Name, StructureStart and StructureEnd values from every attached record.
        /// Empty collections and blank Character values convey no utility evidence;
        /// unavailable collections, null entries and malformed table names fail closed.
        /// Candidate still requires verified Pipes data and sewer terminal ownership.
        /// </summary>
        internal static SewerPipeUtilityKind ClassifyUtility(string layer, IReadOnlyList<string> tables,
            IReadOnlyList<string> identityValues)
            => ClassifyUtility(layer, tables, identityValues, out _);

        internal static SewerPipeUtilityKind ClassifyUtility(string layer, IReadOnlyList<string> tables,
            IReadOnlyList<string> identityValues, out string detail)
        {
            if (string.IsNullOrWhiteSpace(layer))
            {
                detail = "The source layer is blank or unavailable.";
                return SewerPipeUtilityKind.Review;
            }
            if (tables == null || identityValues == null)
            {
                detail = "Native table or identity evidence is unavailable.";
                return SewerPipeUtilityKind.Review;
            }

            bool storm = IsKnownSourceLayer(layer) && StormToken.IsMatch(layer);
            bool sewer = SewerToken.IsMatch(layer);
            var stormEvidence = new List<string>();
            var sewerEvidence = new List<string>();
            if (storm) stormEvidence.Add("layer=" + DescribeEvidenceValue(layer));
            if (sewer) sewerEvidence.Add("layer=" + DescribeEvidenceValue(layer));
            for (int index = 0; index < tables.Count; index++)
            {
                string table = tables[index];
                if (string.IsNullOrWhiteSpace(table))
                {
                    detail = $"Native table name at index {index} is blank or unavailable.";
                    return SewerPipeUtilityKind.Review;
                }
                // Never interpret a table such as OTHER_SD_Pipes as SD_Pipes.
                bool tableStorm = string.Equals(table, "SD_Pipes", StringComparison.OrdinalIgnoreCase);
                bool tableSewer = string.Equals(table, "SS_Pipes", StringComparison.OrdinalIgnoreCase) || SewerToken.IsMatch(table);
                storm |= tableStorm; sewer |= tableSewer;
                if (tableStorm) stormEvidence.Add($"table[{index}]=" + DescribeEvidenceValue(table));
                if (tableSewer) sewerEvidence.Add($"table[{index}]=" + DescribeEvidenceValue(table));
            }
            for (int index = 0; index < identityValues.Count; index++)
            {
                string identity = identityValues[index];
                if (identity == null)
                {
                    detail = $"Native Character identity at index {index} is unavailable.";
                    return SewerPipeUtilityKind.Review;
                }
                bool identityStorm = StormToken.IsMatch(identity), identitySewer = SewerToken.IsMatch(identity);
                storm |= identityStorm; sewer |= identitySewer;
                if (identityStorm) stormEvidence.Add($"identity[{index}]=" + DescribeEvidenceValue(identity));
                if (identitySewer) sewerEvidence.Add($"identity[{index}]=" + DescribeEvidenceValue(identity));
            }
            detail = storm && sewer
                ? "Conflicting utility evidence. Storm: " + string.Join("; ", stormEvidence) +
                    ". Sewer: " + string.Join("; ", sewerEvidence) + "."
                : storm ? "Explicit storm evidence: " + string.Join("; ", stormEvidence) + "."
                : "No conflicting storm evidence; sewer schema and terminal ownership still require validation.";
            return storm && sewer ? SewerPipeUtilityKind.Review :
                storm ? SewerPipeUtilityKind.ExcludedStorm : SewerPipeUtilityKind.Candidate;
        }

        internal static string DescribeEvidenceValue(string? value) => value == null ? "<unavailable>" :
            "'" + value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t").Replace("'", "\\'") + "'";

        // User-defined tie-in convention, independent of any physical structure
        // type. Identity, coordinates and native OD are still proved by the host.
        internal static bool IsExplicitSewerConnectionName(string? name)
        {
            string value = name?.Trim() ?? string.Empty;
            return value.Length > 5 && value.EndsWith("-CONN", StringComparison.OrdinalIgnoreCase) &&
                value.Substring(0, value.Length - 5).Trim().Length > 0 &&
                SewerToken.IsMatch(value) && !StormToken.IsMatch(value);
        }

        internal static bool MayOmitStructurePartSizeName(bool sewerConnectionReader, string? name)
            => sewerConnectionReader && IsExplicitSewerConnectionName(name);

        internal static bool ConnectionAnchorMatches(double pipeX, double pipeY, double pipeZ,
            double pointX, double pointY, double pointZ)
        {
            const double tolerance = 1e-8;
            if (!double.IsFinite(pipeX) || !double.IsFinite(pipeY) || !double.IsFinite(pipeZ) ||
                !double.IsFinite(pointX) || !double.IsFinite(pointY) || !double.IsFinite(pointZ)) return false;
            double x = pipeX - pointX, y = pipeY - pointY, z = pipeZ - pointZ;
            return Math.Abs(x) <= tolerance && Math.Abs(y) <= tolerance && Math.Abs(z) <= tolerance &&
                x * x + y * y <= tolerance * tolerance;
        }

        private static bool IsKnownSourceLayer(string layer)
            => PipeToken.IsMatch(layer) &&
                (layer.StartsWith("GIS-", StringComparison.OrdinalIgnoreCase) ||
                 layer.StartsWith("V-SURV-", StringComparison.OrdinalIgnoreCase) ||
                 (layer.StartsWith("C-", StringComparison.OrdinalIgnoreCase) && CenterToken.IsMatch(layer)));

        /// <summary>
        /// Autodesk INSUNITS values: 2 = Feet; 21 = US Survey Feet.
        /// Unitless or other drawing units cannot interpret native OD feet safely.
        /// The host supplies the current database value before any geometry writes.
        /// </summary>
        internal static bool IsFeetInsertionUnits(int units) => units == 2 || units == 21;

        /// <summary>
        /// InsideDiameter is in drawing feet. Preserve one source LINE below
        /// 12 inches; at/above 12 inches retain the centerline and both walls.
        /// Every retained LINE is independently clipped to the MH outer circle.
        /// </summary>
        internal static SewerPipeSizeKind ClassifyDiameter(double diameterFeet)
            => !double.IsFinite(diameterFeet) || diameterFeet <= 0 ? SewerPipeSizeKind.Invalid :
                diameterFeet < 1.0 ? SewerPipeSizeKind.SingleLine : SewerPipeSizeKind.CenterAndWalls;
    }
}
