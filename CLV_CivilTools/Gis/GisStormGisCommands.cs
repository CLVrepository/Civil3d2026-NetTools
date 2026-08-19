using Autodesk.AutoCAD.Runtime;
using CLV_CivilTools.Shared;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Gis
{
    public static class GisStormGisCommands
    {
        [CommandMethod("CLV-GIS-STORM-GIS", CommandFlags.Modal)]
        public static void RunStormGis()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            var ed = doc.Editor;

            try
            {
                LayerStandards.EnsureGisLayers(doc.Database, ed);
                ed.WriteMessage("\nCLV-GIS-STORM-GIS: storm/sewer GIS layer standards synced. Queueing STORM STRUCTURES AUTO then PIPE OD OFFSET ALL, then strong cleanup.");
                doc.SendStringToExecute("CLV-GIS-STRM-AUTO CLV-GIS-PIPE-OFFSET-OD-ALL CLV-GIS-CLEAN-DWG ", true, false, false);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-STORM-GIS failed: {ex.Message}");
            }
        }
    }
}
