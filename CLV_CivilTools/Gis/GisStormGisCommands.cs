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
                if (!GisStormStructureAuto.RunVerified(doc))
                {
                    ed.WriteMessage("\nCLV-GIS-STORM-GIS: review the structure results before continuing. Pipe offset and verified cleanup were not run; imported Structures points are retained.");
                    return;
                }
                ed.WriteMessage("\nCLV-GIS-STORM-GIS: structure OD verified. Starting managed pipe offset, outer-wall trim and verified point/marker cleanup.");
                GisStormStructureAuto.CleanupVerifiedSources(doc, preparePipes: true);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-STORM-GIS failed: {ex.Message}");
            }
        }
    }
}
