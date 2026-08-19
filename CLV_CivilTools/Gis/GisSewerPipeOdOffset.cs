using System;
using System.IO;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Gis
{
    /// <summary>
    /// Sewer GIS pipe conversion helper.
    /// Uses a server-hosted AutoLISP helper because Map ADE Object Data reads have been
    /// more reliable through ADE/LISP for this workflow.
    /// </summary>
    public static class GisSewerPipeOdOffsetCommands
    {
        private const string HelperCommandName = "CLV-GIS-SSWR-PIPE-LSP";
        private const string HelperAllCommandName = "CLV-GIS-SSWR-PIPE-ALL-LSP";
        private const string ServerHelperPath = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\CLV_GIS_SSWR_PIPE_OD_OFFSET.lsp";

        [CommandMethod("CLV-GIS-SSWR-PIPE", CommandFlags.Modal)]
        public static void OffsetSewerPipeFromObjectData()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;

            try
            {
                if (!EnsureHelperLoaded(doc, ed, out string helperPath))
                {
                    ed.WriteMessage($"\nCLV-GIS-SSWR-PIPE: unable to load helper {ServerHelperPath}");
                    return;
                }

                ed.WriteMessage($"\nCLV-GIS-SSWR-PIPE: using helper {helperPath} then queueing strong cleanup.");
                doc.SendStringToExecute(HelperCommandName + " CLV-GIS-CLEAN-DWG ", true, false, false);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-SSWR-PIPE error: {ex.Message}");
            }
        }

        [CommandMethod("CLV-GIS-SSWR-PIPE-ALL", CommandFlags.Modal)]
        public static void OffsetAllSewerPipesFromObjectData()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;

            try
            {
                if (!EnsureHelperLoaded(doc, ed, out string helperPath))
                {
                    ed.WriteMessage($"\nCLV-GIS-SSWR-PIPE-ALL: unable to load helper {ServerHelperPath}");
                    return;
                }

                ed.WriteMessage($"\nCLV-GIS-SSWR-PIPE-ALL: using helper {helperPath} then queueing strong cleanup.");
                doc.SendStringToExecute(HelperAllCommandName + " CLV-GIS-CLEAN-DWG ", true, false, false);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-SSWR-PIPE-ALL error: {ex.Message}");
            }
        }

        private static bool EnsureHelperLoaded(Document? doc, Editor ed, out string helperPath)
        {
            helperPath = ServerHelperPath;
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
                ed.WriteMessage($"\nCLV-GIS-SSWR-PIPE helper load failed: {ex.Message}");
                return false;
            }
        }

    }
}
