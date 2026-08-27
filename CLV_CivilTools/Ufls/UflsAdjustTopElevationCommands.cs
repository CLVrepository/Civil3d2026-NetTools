using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilCogoPoint = Autodesk.Civil.DatabaseServices.CogoPoint;

namespace CLV_CivilTools.Ufls
{
    /// <summary>
    /// Adjusts a storm junction structure's rim elevation to the elevation
    /// of a selected Civil 3D COGO point.
    /// </summary>
    public static class UflsAdjustTopElevationCommands
    {
        [CommandMethod("UFLS-ADJ-TOP-ELEV", CommandFlags.Modal)]
        public static void AdjustTopElevation()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    PromptEntityOptions structureOptions = new PromptEntityOptions(
                        "\nSelect STORM JUNCTION STRUCTURE: ");
                    structureOptions.SetRejectMessage(
                        "\nSelect a Civil 3D structure.");
                    structureOptions.AddAllowedClass(typeof(Structure), exactMatch: true);

                    PromptEntityResult structureResult = ed.GetEntity(structureOptions);
                    if (structureResult.Status != PromptStatus.OK)
                        return;

                    Structure structure = (Structure)tr.GetObject(
                        structureResult.ObjectId,
                        OpenMode.ForWrite);

                    PromptEntityOptions pointOptions = new PromptEntityOptions(
                        "\nSelect AEC/COGO point for top elevation: ");
                    pointOptions.SetRejectMessage(
                        "\nSelect a Civil 3D COGO point.");
                    pointOptions.AddAllowedClass(typeof(CivilCogoPoint), exactMatch: true);

                    PromptEntityResult pointResult = ed.GetEntity(pointOptions);
                    if (pointResult.Status != PromptStatus.OK)
                        return;

                    CivilCogoPoint point = (CivilCogoPoint)tr.GetObject(
                        pointResult.ObjectId,
                        OpenMode.ForRead);

                    double oldRimElevation = structure.RimElevation;
                    double newRimElevation = point.Elevation;

                    structure.AutomaticRimSurfaceAdjustment = false;
                    structure.RimElevation = newRimElevation;
                    structure.RecordGraphicsModified(true);

                    tr.Commit();

                    ed.WriteMessage(
                        $"\nADJUST TOP ELEV: Structure '{structure.Name}' rim elevation changed " +
                        $"from {oldRimElevation:0.##} to {newRimElevation:0.##}." );
                    ed.WriteMessage(
                        $"\nADJUST TOP ELEV: Point '{point.PointNumber}' elevation = {newRimElevation:0.##}." );
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nADJUST TOP ELEV error: {ex.Message}");
            }
        }
    }
}
