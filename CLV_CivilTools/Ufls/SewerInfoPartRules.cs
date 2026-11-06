using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using CLV_CivilTools.Shared;

namespace CLV_CivilTools.Ufls.SewerInfo
{
    // Host-independent rules: nominal plan sizes are not modeled inside diameters.
    internal static class SewerInfoPartRules
    {
        internal static string? Family(SewerConnection c)
        {
            string text = string.Join(" ", c.MaterialCode, c.Material, c.PipeSpecification, c.MaterialSourceLabel).ToUpperInvariant();
            bool c900 = Regex.IsMatch(text, @"\bC[\s-]*900\b"), rcp = Regex.IsMatch(text, @"\bRCP\b");
            bool pvc = Regex.IsMatch(text, @"\bPVC\b|\bSDR[\s-]*35\b|\bD[\s-]*3034\b");
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
                "in" or "inch" or "inches" or "\"" => 1,
                "ft" or "foot" or "feet" or "'" => 12,
                "mm" or "millimeter" or "millimeters" => 1 / 25.4,
                "cm" or "centimeter" or "centimeters" => 1 / 2.54,
                "m" or "meter" or "meters" => 1 / 0.0254,
                _ => null
            };
            return scale * value;
        }

        // Only explicit unit-bearing dimensions. Never take a year, wall thickness or frame size
        // as the nominal pipe diameter; multiple distinct dimensions remain unresolved.
        internal static double? LabeledInches(string label)
        {
            double[] values = Regex.Matches(label, "(?<![\\d.])(\\d+(?:\\.\\d+)?)\\s*(\"|INCHES\\b|INCH\\b|IN\\b|FT\\b|FEET\\b|MM\\b)", RegexOptions.IgnoreCase)
                .Cast<Match>().Select(m => Inches(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m.Groups[2].Value))
                .Where(v => v.HasValue).Select(v => v!.Value).Distinct().ToArray();
            return values.Length == 1 ? values[0] : null;
        }

        internal static double? PipeNominal(string family, string label, double? actualIdInches)
        {
            double? labeled = LabeledInches(label);
            if (labeled.HasValue) return labeled;
            if (!actualIdInches.HasValue) return null;
            PipeSize[] sizes = family.ToUpperInvariant() switch
            {
                "CLV_PVC" => PipeCatalog.PvcSizes,
                "CLV_C900" => PipeCatalog.C900Sizes,
                "CLV_RCP" => PipeCatalog.RcpSizes,
                _ => Array.Empty<PipeSize>()
            };
            PipeSize[] exact = sizes.Where(s => Math.Abs(s.InnerDiameterInches - actualIdInches.Value) < 0.0001).ToArray();
            return exact.Length == 1 ? exact[0].NominalInches : null;
        }

        internal static double Centerline(double invert, double height)
        {
            if (!double.IsFinite(invert) || !double.IsFinite(height) || height <= 0) throw new ArgumentException("Invalid invert/inside height.");
            return invert + height / 2;
        }

        internal static void CheckOtherEnd(SewerConnection requested, SewerConnection other)
        {
            if (requested.DiameterInches.HasValue && other.DiameterInches.HasValue && Math.Abs(requested.DiameterInches.Value - other.DiameterInches.Value) > 0.0001)
                throw new ArgumentException("Opposite-end plan record specifies a different nominal diameter for the same pipe. Correct the catalog and reapply that end first.");
            string? a = Family(requested), b = Family(other);
            if (a != null && b != null && a != b)
                throw new ArgumentException("Opposite-end plan record specifies a different material family for the same pipe. Correct the catalog and reapply that end first.");
        }
    }
}
