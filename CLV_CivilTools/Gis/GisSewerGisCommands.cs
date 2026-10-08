using Autodesk.AutoCAD.Runtime;
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

            GisSewerPreparation.Run(doc);
        }
    }
}

