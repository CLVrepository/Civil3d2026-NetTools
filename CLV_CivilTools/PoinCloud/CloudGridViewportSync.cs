using System;
using System.Globalization;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.PointCloud
{
    public static class CloudGridViewportSync
    {
        [CommandMethod("CGVIEWSYNCTEST", CommandFlags.Modal)]
        public static void TestViewportSynchronization()
        {
            var document = AcadApp.DocumentManager.MdiActiveDocument;
            if (document == null) return;
            Editor editor = document.Editor;
            if (!document.Database.TileMode)
            {
                editor.WriteMessage("\nCGVIEWSYNCTEST requires the Model tab."); return;
            }
            editor.WriteMessage("\n=== CGVIEWSYNCTEST R5-SYNC-1; native experiment, not a confirmed cloud fix ===");
            editor.WriteMessage("\nExecuting assembly: " + Assembly.GetExecutingAssembly().Location);
            editor.WriteMessage("\nThis synchronizes viewport records only; cloud/crop/source/point/surface data are untouched.");
            using ViewTableRecord before = editor.GetCurrentView();
            try
            {
                int number = Convert.ToInt32(AcadApp.GetSystemVariable("CVPORT"));
                ObjectId activeId = editor.ActiveViewportId;
                using (Transaction transaction = document.Database.TransactionManager.StartOpenCloseTransaction())
                {
                    if (number < 2 || activeId.IsNull ||
                        transaction.GetObject(activeId, OpenMode.ForRead) is not ViewportTableRecord active ||
                        active.Number != number || !string.Equals(active.Name, "*Active", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Cannot verify the active tiled viewport against CVPORT; no synchronization attempted.");
                }
                Report(editor, document.Database, "BEFORE");
                // Export the current live tiled views before any import. Importing
                // stale database records first could discard the current section.
                editor.UpdateTiledViewportsInDatabase();
                Report(editor, document.Database, "AFTER WRITE");
                using (Transaction transaction = document.Database.TransactionManager.StartOpenCloseTransaction())
                {
                    if (editor.ActiveViewportId != activeId ||
                        transaction.GetObject(editor.ActiveViewportId, OpenMode.ForRead) is not ViewportTableRecord stored ||
                        stored.Number != number || !SameCameraAndClip(before, stored))
                    {
                        editor.WriteMessage("\nActive stored viewport does not match live camera/clip after export. No database-to-display import attempted. Stop and retain this log.");
                        return;
                    }
                }
                editor.UpdateTiledViewportsFromDatabase();
                editor.Regen();
                Report(editor, document.Database, "AFTER RELOAD");
                using ViewTableRecord after = editor.GetCurrentView();
                bool retained = SameCameraAndClip(before, after);
                editor.WriteMessage("\nLive camera/clip retained=" + retained +
                    ". Inspect CLOUD versus ordinary out-of-slab geometry; matching flags alone do not prove cloud clipping.");
                if (!retained)
                {
                    editor.SetCurrentView(before);
                    editor.UpdateTiledViewportsInDatabase();
                    editor.UpdateTiledViewportsFromDatabase();
                    editor.Regen();
                    editor.WriteMessage("\nUnexpected view change: original live view reapplied and synchronized. Stop this experiment.");
                }
                editor.WriteMessage("\nAfter testing: CGEND, then CGVIEWSYNCTEST once more to synchronize the restored overview. Do not save a clipped review view.");
            }
            catch (System.Exception error)
            {
                editor.WriteMessage("\nViewport sync experiment failed: " + error.Message +
                    ". No cloud operations were attempted. Use CGEND and retain this log.");
            }
        }

        private static bool SameCameraAndClip(AbstractViewTableRecord a, AbstractViewTableRecord b) =>
            a.Target.DistanceTo(b.Target) <= 1e-7 && (a.ViewDirection.GetNormal() - b.ViewDirection.GetNormal()).Length <= 1e-8 &&
            Math.Abs(a.ViewTwist - b.ViewTwist) <= 1e-8 &&
            a.CenterPoint.GetDistanceTo(b.CenterPoint) <= 1e-7 &&
            Math.Abs(a.Width - b.Width) <= 1e-7 && Math.Abs(a.Height - b.Height) <= 1e-7 &&
            a.PerspectiveEnabled == b.PerspectiveEnabled && a.FrontClipEnabled == b.FrontClipEnabled &&
            a.BackClipEnabled == b.BackClipEnabled && a.FrontClipAtEye == b.FrontClipAtEye &&
            Math.Abs(a.FrontClipDistance - b.FrontClipDistance) <= 1e-8 &&
            Math.Abs(a.BackClipDistance - b.BackClipDistance) <= 1e-8;

        private static void Report(Editor editor, Database database, string phase)
        {
            using ViewTableRecord live = editor.GetCurrentView();
            editor.WriteMessage("\n" + phase + " live: " + Describe(live));
            using Transaction transaction = database.TransactionManager.StartOpenCloseTransaction();
            ObjectId active = editor.ActiveViewportId, current = database.CurrentViewportTableRecordId;
            editor.WriteMessage("\n" + phase + " CVPORT=" + AcadApp.GetSystemVariable("CVPORT") +
                ", ActiveViewportId=" + active + ", databaseCurrentId=" + current + ", sameId=" + (active == current));
            foreach (ObjectId id in active == current ? new[] { active } : new[] { active, current })
            {
                if (id.IsNull) continue;
                if (transaction.GetObject(id, OpenMode.ForRead) is ViewportTableRecord stored)
                    editor.WriteMessage("\n" + phase + " stored " + stored.Handle + " number=" + stored.Number + " name=" + stored.Name + ": " + Describe(stored));
                else editor.WriteMessage("\n" + phase + " viewport ID " + id + " is not a tiled viewport record.");
            }
        }

        private static string Describe(AbstractViewTableRecord view) =>
            "front=" + view.FrontClipEnabled + "/" + view.FrontClipDistance.ToString("G9", CultureInfo.InvariantCulture) +
            ", back=" + view.BackClipEnabled + "/" + view.BackClipDistance.ToString("G9", CultureInfo.InvariantCulture) +
            ", atEye=" + view.FrontClipAtEye + ", perspective=" + view.PerspectiveEnabled +
            ", target=" + view.Target + ", WCS direction=" + view.ViewDirection + ", twist=" + view.ViewTwist +
            ", width=" + view.Width + ", height=" + view.Height;
    }
}
