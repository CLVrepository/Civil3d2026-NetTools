using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Gis
{
    /// <summary>
    /// Wrapper for the shared ADE/LISP Object Data transfer helper.
    /// All source/destination selection stays inside the LISP routine so
    /// Map Object Data is handled through ADE instead of managed .NET APIs.
    /// </summary>
    public static class GisObjectDataTransfer
    {
        private const string HelperCommandName = "CLV-GIS-OD-COPY";
        private const string NetworkHelperPath = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\CLV_GIS_OD_HELPERS.lsp";

        [CommandMethod("CLV-GIS-OD-XFER", CommandFlags.Modal)]
        public static void TransferObjectData()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            var ed = doc.Editor;

            try
            {
                string? helperPath = EnsureHelperLoaded(doc, ed);
                if (string.IsNullOrWhiteSpace(helperPath))
                {
                    ed.WriteMessage($"\nCLV-GIS-OD-XFER: unable to load helper {NetworkHelperPath}");
                    return;
                }

                ed.WriteMessage($"\nCLV-GIS-OD-XFER: using helper {helperPath}");
                ed.WriteMessage("\nCLV-GIS-OD-XFER: select SOURCE object, then DESTINATION object.");
                doc.SendStringToExecute(HelperCommandName + " ", true, false, false);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-OD-XFER error: {ex.Message}");
            }
        }

        private static string? EnsureHelperLoaded(Autodesk.AutoCAD.ApplicationServices.Document doc, Autodesk.AutoCAD.EditorInput.Editor ed)
        {
            string helperPath = NetworkHelperPath;
            if (!System.IO.File.Exists(helperPath))
                return null;

            try
            {
                string escapedPath = helperPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
                doc.SendStringToExecute($"(progn (vl-load-com) (load \"{escapedPath}\") (princ)) ", true, false, false);
                return helperPath;
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-OD-XFER helper load failed: {ex.Message}");
                return null;
            }
        }
    }
}
