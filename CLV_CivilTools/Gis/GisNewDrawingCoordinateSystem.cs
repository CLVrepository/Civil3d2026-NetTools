using System;
using System.IO;

namespace CLV_CivilTools.Gis
{
    /// <summary>
    /// The original drawing's assigned code is authoritative for drawing setup.
    /// This policy sets/reads codes only; it does not load MapGuide, inspect SDF
    /// spatial contexts, resolve WKT, transform coordinates or change dictionaries.
    /// </summary>
    internal static class GisNewDrawingCoordinateSystem
    {
        internal static void RequireSourceCode(string? sourceCode)
        {
            if (!GisNewDrawingResources.IsSupportedCoordinateSystem(sourceCode))
                throw new InvalidDataException($"The source drawing must have an assigned coordinate system of " +
                    $"{GisNewDrawingResources.LvfCoordinateSystem} or {GisNewDrawingResources.LvhefCoordinateSystem}; " +
                    $"read '{sourceCode ?? "<missing>"}'. No default was assigned.");
        }

        internal static void VerifySameCode(string sourceCode, string? actualCode, string setting)
        {
            RequireSourceCode(sourceCode);
            if (!string.Equals(sourceCode, actualCode, StringComparison.Ordinal))
                throw new InvalidDataException($"{setting} did not retain source drawing coordinate system '{sourceCode}'; " +
                    $"read '{actualCode ?? "<missing>"}'. Drawing setup was not accepted.");
        }

        internal static void AssignSameCode(string sourceCode, Action<string> assign, Func<string?> read, string setting)
        {
            RequireSourceCode(sourceCode);
            ArgumentNullException.ThrowIfNull(assign);
            ArgumentNullException.ThrowIfNull(read);
            // One explicit destination setting followed by readback. The original
            // drawing's assigned code is the only input to this setup policy.
            assign(sourceCode);
            VerifySameCode(sourceCode, read(), setting);
        }
    }
}
