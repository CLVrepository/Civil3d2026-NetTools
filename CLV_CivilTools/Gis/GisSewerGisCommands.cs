using Autodesk.AutoCAD.Runtime;
using CLV_CivilTools.Shared;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Gis
{
    public static class GisSewerGisCommands
    {
        [CommandMethod("CLV-GIS-SSWR-GIS", CommandFlags.Modal)]
        public static void RunSewerGis()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            var ed = doc.Editor;

            try
            {
                LayerStandards.EnsureGisLayers(doc.Database, ed);
                ed.WriteMessage("\nCLV-GIS-SSWR-GIS: sewer/storm GIS layer standards synced. Queueing SEWER MANHOLE ALL then SEWER PIPE ALL, then strong cleanup.");
                doc.SendStringToExecute("CLV-GIS-SSWR-MH-ALL CLV-GIS-SSWR-PIPE-ALL CLV-GIS-CLEAN-DWG ", true, false, false);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-SSWR-GIS failed: {ex.Message}");
            }
        }
    }
}
