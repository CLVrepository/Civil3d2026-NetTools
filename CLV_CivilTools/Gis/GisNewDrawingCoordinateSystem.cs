using System;
using System.IO;

namespace CLV_CivilTools.Gis
{
    /// <summary>
    /// The original drawing's assigned code is authoritative for this workflow.
    /// This policy sets/reads codes only; it does not load MapGuide, inspect SDF
    /// spatial contexts, resolve WKT, transform coordinates or change dictionaries.
    /// </summary>
    internal static class GisNewDrawingCoordinateSystem
    {
        internal static void RequireSourceCode(string? sourceCode)
        {
            if (!GisNewDrawingProfile.IsSupportedCoordinateSystem(sourceCode))
                throw new InvalidDataException($"The source drawing must have an assigned coordinate system of " +
                    $"{GisNewDrawingProfile.LvfCoordinateSystem} or {GisNewDrawingProfile.LvhefCoordinateSystem}; " +
                    $"read '{sourceCode ?? "<missing>"}'. No default was assigned.");
        }

        internal static void VerifySameCode(string sourceCode, string? actualCode, string setting)
        {
            RequireSourceCode(sourceCode);
            if (!string.Equals(sourceCode, actualCode, StringComparison.Ordinal))
                throw new InvalidDataException($"{setting} did not retain source drawing coordinate system '{sourceCode}'; " +
                    $"read '{actualCode ?? "<missing>"}'. Import was not accepted.");
        }

        internal static void AssignSameCode(string sourceCode, Action<string> assign, Func<string?> read, string setting)
        {
            RequireSourceCode(sourceCode);
            ArgumentNullException.ThrowIfNull(assign);
            ArgumentNullException.ThrowIfNull(read);
            // One explicit setting followed by readback. The SDF's CRS labels are
            // intentionally not an input to this source-authoritative policy.
            assign(sourceCode);
            VerifySameCode(sourceCode, read(), setting);
        }
    }
}
