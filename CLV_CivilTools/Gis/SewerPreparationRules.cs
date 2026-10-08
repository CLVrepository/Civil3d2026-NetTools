using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CLV_CivilTools.Gis
{
    internal enum SewerPipeSizeKind { Invalid, SingleLine, CenterAndWalls }
    internal enum SewerPipeUtilityKind { Candidate, ExcludedStorm, Review }

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
        {
            if (string.IsNullOrWhiteSpace(layer) || tables == null || identityValues == null)
                return SewerPipeUtilityKind.Review;

            bool storm = IsKnownSourceLayer(layer) && StormToken.IsMatch(layer);
            bool sewer = SewerToken.IsMatch(layer);
            foreach (string table in tables)
            {
                if (string.IsNullOrWhiteSpace(table)) return SewerPipeUtilityKind.Review;
                // Never interpret a table such as OTHER_SD_Pipes as SD_Pipes.
                storm |= string.Equals(table, "SD_Pipes", StringComparison.OrdinalIgnoreCase);
                sewer |= string.Equals(table, "SS_Pipes", StringComparison.OrdinalIgnoreCase) || SewerToken.IsMatch(table);
            }
            foreach (string identity in identityValues)
            {
                if (identity == null) return SewerPipeUtilityKind.Review;
                storm |= StormToken.IsMatch(identity);
                sewer |= SewerToken.IsMatch(identity);
            }
            return storm && sewer ? SewerPipeUtilityKind.Review :
                storm ? SewerPipeUtilityKind.ExcludedStorm : SewerPipeUtilityKind.Candidate;
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
