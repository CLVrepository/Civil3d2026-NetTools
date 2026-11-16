using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace CLV_CivilTools.Gis
{
    internal static class AerialLayerIdentity
    {
        internal static bool IsAerialGroup(string? name, string? legend) =>
            string.Equals(name?.Trim(), "Aerial Imagery", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(legend?.Trim(), "Aerial Imagery", StringComparison.OrdinalIgnoreCase);

        internal static bool IsAerial(params string?[] identities) => identities.Any(IsNearmap);

        private static bool IsNearmap(string? identity)
        {
            if (string.IsNullOrWhiteSpace(identity)) return false;
            string text = Uri.UnescapeDataString(identity).Trim();
            if (Regex.IsMatch(text, @"(?<![A-Za-z])Near[ _-]*map(?![A-Za-z])", RegexOptions.IgnoreCase)) return true;
            // Historic .layer files use '(2026 - January) LasVegas'. Do not match
            // unrelated names merely because they contain LasVegas, e.g. parcels/roads.
            string leaf = text.Replace('\\', '/').Split('/').Last();
            leaf = Regex.Replace(leaf, @"\.(?:LayerDefinition|FeatureSource|layer)$", "", RegexOptions.IgnoreCase);
            return Regex.IsMatch(leaf, @"^(?:\(\d{4}\s*-\s*[A-Za-z]+\)\s*)?Las[ _-]*Vegas(?:_\d+)?$", RegexOptions.IgnoreCase);
        }
    }
}
