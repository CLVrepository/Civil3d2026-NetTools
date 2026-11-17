using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CLV_CivilTools.Shared;

namespace CLV_CivilTools.Ufls.StormInfo
{
    // Host-independent rules. Display names are never a source of pipe dimensions.
    internal static class StormInfoPartRules
    {
        // An absolute rounding allowance, not nearest-size selection.
        internal const double PipeDimensionToleranceInches = 0.01;

        internal static string? Family(StormConnection c)
        {
            string text = string.Join(" ", c.MaterialCode, c.Material, c.PipeSpecification, c.MaterialSourceLabel).ToUpperInvariant();
            bool c900 = Regex.IsMatch(text, @"\bC[\s-]*900\b"), rcp = Regex.IsMatch(text, @"\bRCP\b");
            bool pvc = Regex.IsMatch(text, @"\bPVC\b|\bSDR[\s-]*35\b|\bD[\s-]*3034\b");
            bool rcb = Regex.IsMatch(text, @"\bRCB\b");
            if (rcb && (rcp || c900 || pvc)) throw new ArgumentException("Conflicting RCB and circular pipe materials.");
            if (rcb) return "CLV_RCB";
            if (rcp && (c900 || pvc)) throw new ArgumentException("Conflicting pipe materials: " + text);
            if (c900 && Regex.IsMatch(text, @"\bSDR[\s-]*35\b|\bD[\s-]*3034\b")) throw new ArgumentException("Conflicting C900 and SDR35 specifications: " + text);
            if (c900) return "CLV_C900"; // C900 is PVC; physical material PVC is compatible.
            if (rcp) return "CLV_RCP";
            if (pvc) return "CLV_PVC";
            if (!string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Unrecognized pipe material/specification: " + text.Trim());
            return null;
        }

        internal static double? Inches(double value, string? units)
        {
            if (!double.IsFinite(value) || value <= 0) return null;
            double? scale = (units ?? "").Trim().ToLowerInvariant() switch
            {
                "in" or "in." or "inch" or "inches" or "\"" or "″" => 1,
                "ft" or "ft." or "foot" or "feet" or "'" or "′" => 12,
                "mm" or "millimeter" or "millimeters" or "millimetre" or "millimetres" => 1 / 25.4,
                "cm" or "centimeter" or "centimeters" or "centimetre" or "centimetres" => 1 / 2.54,
                "m" or "meter" or "meters" or "metre" or "metres" => 1 / 0.0254,
                _ => null
            };
            return scale * value;
        }

        private static PipeSize[] Catalog(string family) => family.ToUpperInvariant() switch
        {
            "CLV_PVC" => PipeCatalog.PvcSizes,
            "CLV_C900" => PipeCatalog.C900Sizes,
            "CLV_RCP" => PipeCatalog.RcpSizes,
            _ => Array.Empty<PipeSize>()
        };

        internal static bool SamePipeDimension(double a, double b) =>
            double.IsFinite(a) && double.IsFinite(b) && a > 0 && b > 0 &&
            Math.Abs(a - b) <= PipeDimensionToleranceInches;

        // Civil 3D has no standard nominal-diameter PartContextType. Supported CLV
        // catalogs may model a nominal bore, or the physical ID in PipeCatalog.
        // Enumerate BOTH numeric interpretations and require one nominal result.
        // Neither the part's name nor a nearby arbitrary diameter can authorize a swap.
        internal static double? PipeNominal(string family, double? innerDiameterInches)
        {
            if (!innerDiameterInches.HasValue) return null;
            int[] exact = Catalog(family)
                .Where(s => SamePipeDimension(s.InnerDiameterInches, innerDiameterInches.Value) ||
                            SamePipeDimension(s.NominalInches, innerDiameterInches.Value))
                .Select(s => s.NominalInches).Distinct().ToArray();
            return exact.Length == 1 ? exact[0] : null;
        }

        internal static string PipeDimensionBasis(string family, double innerDiameterInches, double nominal)
        {
            bool modeledNominal = SamePipeDimension(innerDiameterInches, nominal);
            bool catalogId = Catalog(family).Any(s => s.NominalInches == nominal &&
                SamePipeDimension(s.InnerDiameterInches, innerDiameterInches));
            return modeledNominal && catalogId ? "numeric nominal/ID" :
                modeledNominal ? "numeric nominal-bore definition" : "numeric CLV catalog ID";
        }

        // Physical PVC alone cannot distinguish C900 from SDR-35. The configured
        // exact CLV family remains the specification key; Material is a consistency
        // check, never a reason to choose the first PVC family in a parts list.
        internal static string? MaterialProblem(string family, string? material)
        {
            if (string.IsNullOrWhiteSpace(material)) return null; // optional catalog parameter
            string value = material.Trim().ToUpperInvariant();
            value = Regex.Replace(value, @"\bPOLYVINYL\s+CHLORIDE\b", "PVC");
            value = Regex.Replace(value, @"\bREINFORCED\s+CONCRETE(?:\s+PIPE)?\b", "RCP");
            if (value == "CONCRETE") value = "RCP";
            if (Regex.IsMatch(value, @"\bHDPE\b|\bSTEEL\b|\bIRON\b|\bCOPPER\b"))
                return "Material parameter '" + material + "' is incompatible with " + family;
            string? actual;
            try { actual = Family(new StormConnection { Material = value }); }
            catch (ArgumentException) { return "unrecognized Material parameter '" + material + "'"; }
            bool genericPvc = actual == "CLV_PVC" && !Regex.IsMatch(value, @"\bSDR[\s-]*35\b|\bD[\s-]*3034\b");
            bool compatible = string.Equals(family, actual, StringComparison.OrdinalIgnoreCase) ||
                (family.Equals("CLV_C900", StringComparison.OrdinalIgnoreCase) && genericPvc);
            return compatible ? null : "Material parameter '" + material + "' conflicts with " + family;
        }

        // Candidate IDs have already passed numeric and Material checks.
        internal static int SelectPartCandidate<T>(IReadOnlyList<T> compatible, T current)
        {
            int[] existing = Enumerable.Range(0, compatible.Count)
                .Where(i => EqualityComparer<T>.Default.Equals(compatible[i], current)).ToArray();
            if (existing.Length == 1) return existing[0];
            if (existing.Length > 1) return -1;
            return compatible.Count == 1 ? 0 : -1;
        }

        internal static double Centerline(double invert, double height)
        {
            if (!double.IsFinite(invert) || !double.IsFinite(height) || height <= 0) throw new ArgumentException("Invalid invert/inside height.");
            return invert + height / 2;
        }

        internal static void CheckOtherEnd(StormConnection requested, StormConnection other)
        {
            if (requested.Shape != other.Shape) throw new ArgumentException("Opposite-end records specify different pipe shapes.");
            if (requested.Shape == "rectangular"
                && (!SamePipeDimension(requested.WidthInches ?? 0, other.WidthInches ?? 0)
                    || !SamePipeDimension(requested.HeightInches ?? 0, other.HeightInches ?? 0)))
                throw new ArgumentException("Opposite-end records specify different box dimensions.");
            if (requested.DiameterInches.HasValue && other.DiameterInches.HasValue && Math.Abs(requested.DiameterInches.Value - other.DiameterInches.Value) > 0.0001)
                throw new ArgumentException("Opposite-end plan record specifies a different nominal diameter for the same pipe. Correct the catalog and reapply that end first.");
            string? a = Family(requested), b = Family(other);
            if (a != null && b != null && a != b)
                throw new ArgumentException("Opposite-end plan record specifies a different material family for the same pipe. Correct the catalog and reapply that end first.");
        }
    }
}
