using System;
using System.IO;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Gis
{
    /// <summary>
    /// Basic map-import pipe offset helper.
    /// Uses the ADE Object Data AutoLISP helper so it can read Map OD tables
    /// the same way CLV-GIS-OD-INSPECT does in Civil 3D / Map workflows.
    /// </summary>
    public static class GisPipeOdOffsetCommands
    {
        private const string HelperCommandName = "CLV-GIS-PIPE-OFFSET-OD-LSP";
        private const string HelperAllCommandName = "CLV-GIS-PIPE-OFFSET-OD-ALL-LSP";
        private const string NetworkHelperPath = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\CLV_GIS_PIPE_OD_OFFSET.lsp";

        [CommandMethod("CLV-GIS-PIPE-OFFSET-OD", CommandFlags.Modal)]
        public static void OffsetPipesFromObjectData()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;

            try
            {
                if (!EnsureHelperLoaded(doc, ed, out string helperPath))
                {
                    ed.WriteMessage($"\nCLV-GIS-PIPE-OFFSET-OD: unable to load helper {NetworkHelperPath}");
                    return;
                }

                ed.WriteMessage($"\nCLV-GIS-PIPE-OFFSET-OD: using helper {helperPath} then queueing strong cleanup.");
                doc.SendStringToExecute(HelperCommandName + " CLV-GIS-CLEAN-DWG ", true, false, false);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-PIPE-OFFSET-OD error: {ex.Message}");
            }
        }

        [CommandMethod("CLV-GIS-PIPE-OFFSET-OD-ALL", CommandFlags.Modal)]
        public static void OffsetAllPipesFromObjectData()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;

            try
            {
                if (!EnsureHelperLoaded(doc, ed, out string helperPath))
                {
                    ed.WriteMessage($"\nCLV-GIS-PIPE-OFFSET-OD-ALL: unable to load helper {NetworkHelperPath}");
                    return;
                }

                ed.WriteMessage($"\nCLV-GIS-PIPE-OFFSET-OD-ALL: using helper {helperPath} then queueing strong cleanup.");
                doc.SendStringToExecute(HelperAllCommandName + " CLV-GIS-CLEAN-DWG ", true, false, false);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-PIPE-OFFSET-OD-ALL error: {ex.Message}");
            }
        }

        private static bool EnsureHelperLoaded(Document? doc, Editor ed, out string helperPath)
        {
            helperPath = NetworkHelperPath;
            if (doc == null || string.IsNullOrWhiteSpace(helperPath) || !File.Exists(helperPath))
                return false;

            try
            {
                string escapedPath = helperPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
                doc.SendStringToExecute($"(progn (vl-load-com) (load \"{escapedPath}\") (princ)) ", true, false, false);
                return true;
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-PIPE-OFFSET-OD helper load failed: {ex.Message}");
                return false;
            }
        }
    }
}
