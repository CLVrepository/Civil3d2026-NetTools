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
