using System;
using System.Globalization;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Ufls
{
    /// <summary>
    /// Diagnostic command for inspecting structured data stored on PIPE TOP CHECK MText objects.
    /// </summary>
    public static class PipeTopCheckDataCommand
    {
        [CommandMethod("UFLS-PIPE-TOP-DATA")]
        public static void ShowPipeTopCheckData()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptEntityOptions peo = new PromptEntityOptions("\nSelect PIPE TOP CHECK label: ");
            peo.AllowNone = false;
            peo.SetRejectMessage("\nSelected object is not an MText object.");
            peo.AddAllowedClass(typeof(MText), exactMatch: true);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
                return;

            try
            {
                using Transaction tr = db.TransactionManager.StartTransaction();
                MText label = (MText)tr.GetObject(per.ObjectId, OpenMode.ForRead, false);

                if (!PipeTopCheckData.TryRead(label, tr, out PipeTopCheckData.Snapshot snapshot))
                {
                    ed.WriteMessage("\nSelected MText does not contain CLV PIPE TOP CHECK metadata.");
                    return;
                }

                ed.WriteMessage("\n\nPIPE TOP CHECK DATA");
                ed.WriteMessage("\n-------------------");
                ed.WriteMessage($"\nLabel Handle: {per.ObjectId.Handle}");
                ed.WriteMessage($"\nID:           {snapshot.Id:D}");
                ed.WriteMessage($"\nPoint Number: {snapshot.PointNumber}");
                ed.WriteMessage($"\nCOGO Point:   {FormatObjectId(snapshot.CogoPointId)}");
                ed.WriteMessage($"\nPipe:         {snapshot.PipeName}");
                ed.WriteMessage($"\nPipe Handle:  {snapshot.PipeHandle}");
                ed.WriteMessage($"\nCheck X/Y/Z:  {FormatPoint(snapshot.CheckPointLocation)}");
                ed.WriteMessage($"\nLabel X/Y/Z:  {FormatPoint(snapshot.LabelLocation)}");
                ed.WriteMessage($"\nPLAN - TOP:   {FormatElevation(snapshot.PlanTopElevation)}");
                ed.WriteMessage($"\nSURV - TOP:   {FormatElevation(snapshot.SurveyTopElevation)}");
                ed.WriteMessage($"\nDIFF:         {FormatSigned(snapshot.Difference)}");
                ed.WriteMessage($"\nStation:      {FormatNumber(snapshot.Station)}");
                ed.WriteMessage($"\nOffset:       {FormatNumber(snapshot.Offset)}");
                ed.WriteMessage($"\nDisplay Mode: {snapshot.Mode}");
                ed.WriteMessage($"\nExhibit ID:   {snapshot.ExhibitId}");
                ed.WriteMessage($"\nCreated UTC:  {snapshot.CreatedUtc:O}");
                ed.WriteMessage("\n");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUFLS-PIPE-TOP-DATA error: {ex.Message}");
            }
        }

        private static string FormatObjectId(ObjectId id)
            => id.IsNull ? "<null>" : id.Handle.ToString();

        private static string FormatPoint(Autodesk.AutoCAD.Geometry.Point3d point)
            => $"{point.X:0.000}, {point.Y:0.000}, {point.Z:0.000}";

        private static string FormatElevation(double value)
            => double.IsNaN(value)
                ? "<not available>"
                : value.ToString("0.000", CultureInfo.InvariantCulture);

        private static string FormatSigned(double value)
            => double.IsNaN(value)
                ? "<not available>"
                : value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);

        private static string FormatNumber(double value)
            => double.IsNaN(value)
                ? "<not available>"
                : value.ToString("0.000", CultureInfo.InvariantCulture);
    }
}
