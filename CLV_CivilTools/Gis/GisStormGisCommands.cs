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
                    ed.WriteMessage("\nCLV-GIS-STORM-GIS: review the structure results before continuing. Pipe offset and broad cleanup were not queued; imported Structures points are retained.");
                    return;
                }
                ed.WriteMessage("\nCLV-GIS-STORM-GIS: structure OD verified. Queueing pipe offset without broad cleanup; imported Structures points remain available for review.");
                GisPipeOdOffsetCommands.QueueAllWithoutCleanup(doc);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-STORM-GIS failed: {ex.Message}");
            }
        }
    }
}

