using System;
using System.Collections.Generic;
using System.IO;
using System.Security;

namespace CLV_CivilTools.Gis
{
    /// <summary>
    /// Setup resources and exact source-code/profile selection. Resolving a path
    /// does not open, parse or modify the shared import profile.
    /// </summary>
    internal static class GisNewDrawingResources
    {
        internal const string TemplatePath = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Drawing Templates\Blank (2026).dwt";
        internal const string ProfileFolder = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\SDF to SHP";
        internal const string LvfCoordinateSystem = "NV83.NCRS-LVF";
        internal const string LvhefCoordinateSystem = "NV83.NCRS-LVHEF";
        internal const string LvfProfileFileName = "UFLS-IMPORT-NV83.NCRS-LVF.ipf";
        internal const string LvhefProfileFileName = "UFLS-IMPORT-NV83.NCRS.LVHEF.ipf";

        internal static bool IsSupportedCoordinateSystem(string? coordinateSystem)
            => coordinateSystem == LvfCoordinateSystem || coordinateSystem == LvhefCoordinateSystem;

        /// <summary>
        /// Checks a new DWG destination without creating or changing anything.
        /// This is a filesystem snapshot, not an atomic reservation: the caller
        /// must revalidate immediately before saving.
        /// </summary>
        internal static bool TryValidateNewDrawingPath(string? requested, IEnumerable<string> protectedPaths,
            out string fullPath, out string detail)
        {
            fullPath = string.Empty;
            detail = string.Empty;
            try
            {
                if (string.IsNullOrWhiteSpace(requested) || !Path.IsPathFullyQualified(requested))
                {
                    detail = "Choose a fully qualified path and filename for the new drawing.";
                    return false;
                }

                string candidate = Path.GetFullPath(requested);
                string fileName = Path.GetFileName(candidate);
                if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    detail = "Choose a valid filename for the new drawing.";
                    return false;
                }
                string extension = Path.GetExtension(candidate);
                if (extension.Length == 0)
                    candidate += ".dwg";
                else if (!string.Equals(extension, ".dwg", StringComparison.OrdinalIgnoreCase))
                {
                    detail = "The new drawing filename must use the .dwg extension.";
                    return false;
                }

                if (protectedPaths == null)
                {
                    detail = "The source, template and open drawing paths could not be checked.";
                    return false;
                }
                foreach (string protectedPath in protectedPaths)
                {
                    // Unsaved document display names (for example Drawing1.dwg)
                    // are not filesystem paths and must not use the process CWD.
                    if (string.IsNullOrWhiteSpace(protectedPath) || !Path.IsPathRooted(protectedPath))
                        continue;
                    if (string.Equals(candidate, Path.GetFullPath(protectedPath), StringComparison.OrdinalIgnoreCase))
                    {
                        detail = "Choose a new path different from the source, template and every open drawing.";
                        return false;
                    }
                }

                string? parent = Path.GetDirectoryName(candidate);
                if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
                {
                    detail = "The destination folder does not exist or is unavailable. Choose an existing folder.";
                    return false;
                }
                if (File.Exists(candidate) || Directory.Exists(candidate))
                {
                    detail = "The destination already exists. Choose a new filename; existing files and folders cannot be overwritten.";
                    return false;
                }

                fullPath = candidate;
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException ||
                ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException)
            {
                detail = "The new drawing path could not be validated: " + ex.Message;
                return false;
            }
        }

        internal static bool TryResolveProfilePath(string? exactSourceCrs, out string path, out string detail)
        {
            path = string.Empty;
            if (!IsSupportedCoordinateSystem(exactSourceCrs))
            {
                detail = $"Unsupported source coordinate system '{exactSourceCrs ?? "<missing>"}'. " +
                    $"The source drawing must have exactly {LvfCoordinateSystem} or {LvhefCoordinateSystem}; " +
                    "no default, alias or fallback is allowed.";
                return false;
            }

            // Keep Windows deployment paths exact on every platform. The period
            // before LVHEF in its profile filename is intentional.
            path = ProfileFolder + "\\" + (exactSourceCrs == LvfCoordinateSystem ? LvfProfileFileName : LvhefProfileFileName);
            detail = string.Empty;
            return true;
        }
    }
}
