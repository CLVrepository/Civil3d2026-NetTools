using System;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.PointCloud
{
    public static class CloudGridDiagnostics
    {
        [CommandMethod("CGCLOUDDIAG", CommandFlags.Modal)]
        public static void ReportDisplay()
        {
            Document? d = AcadApp.DocumentManager.MdiActiveDocument;
            if (d == null) return;
            Editor ed = d.Editor;
            ed.WriteMessage("\n=== CLOUD DISPLAY DIAGNOSTIC R1 (read-only) ===");
            try
            {
                if (!d.Database.TileMode) throw new InvalidOperationException("Use the Model tab.");
                using ViewTableRecord view = ed.GetCurrentView();
                ed.WriteMessage("\nEditor view: front=" + view.FrontClipEnabled + ", back=" + view.BackClipEnabled +
                    ", atEye=" + view.FrontClipAtEye + ", frontZ=" + view.FrontClipDistance + ", backZ=" + view.BackClipDistance +
                    ", perspective=" + view.PerspectiveEnabled + ", target=" + view.Target + ", direction=" + view.ViewDirection +
                    ", width=" + view.Width + ", height=" + view.Height);
                ed.WriteMessage("\nVIEWMODE=" + AcadApp.GetSystemVariable("VIEWMODE") + ", CVPORT=" + AcadApp.GetSystemVariable("CVPORT"));
                using Transaction tr = d.Database.TransactionManager.StartOpenCloseTransaction();
                var viewport = (ViewportTableRecord)tr.GetObject(d.Database.CurrentViewportTableRecordId, OpenMode.ForRead);
                ed.WriteMessage("\nStored viewport: front=" + viewport.FrontClipEnabled + ", back=" + viewport.BackClipEnabled +
                    ", atEye=" + viewport.FrontClipAtEye + ", frontZ=" + viewport.FrontClipDistance + ", backZ=" + viewport.BackClipDistance);
                var manager = (SectionManager)tr.GetObject(d.Database.SectionManagerId, OpenMode.ForRead);
                ed.WriteMessage("\nSection manager: count=" + manager.NumSections + ", liveId=" + manager.LiveSection);
                var table = (BlockTable)tr.GetObject(d.Database.BlockTableId, OpenMode.ForRead);
                var model = (BlockTableRecord)tr.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                int clouds = 0, sections = 0;
                foreach (ObjectId id in model)
                {
                    DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                    if (obj is Section section)
                    {
                        sections++;
                        ed.WriteMessage("\nSECTION handle=" + section.Handle + ", live=" + section.IsLiveSectionEnabled +
                            ", visible=" + section.Visible + ", state=" + section.State + ", layer=" + section.Layer);
                    }
                    if (obj is not PointCloudEx cloud) continue;
                    clouds++;
                    ed.WriteMessage("\nCLOUD handle=" + cloud.Handle + ", visible=" + cloud.Visible + ", layer=" + cloud.Layer +
                        ", def=" + cloud.PointCloudDefExId + ", crops=" + cloud.getCroppingCount() +
                        ", showCropped=" + cloud.ShowCropped + ", inverted=" + cloud.CroppingInverted);
                    var layer = (LayerTableRecord)tr.GetObject(cloud.LayerId, OpenMode.ForRead);
                    ed.WriteMessage("\n  Layer: off=" + layer.IsOff + ", frozen=" + layer.IsFrozen + ", locked=" + layer.IsLocked);
                    using ResultBuffer? tag = cloud.GetXDataForApplication("CLV_CLOUD_GRID_V1");
                    var values = tag?.AsArray() ?? Array.Empty<TypedValue>();
                    ed.WriteMessage("\n  Review tag=" + (values.Length > 1 ? values[1].Value : "none"));
                    try { ed.WriteMessage("\n  Native bounds: min=" + cloud.NativeExtents.MinPoint + ", max=" + cloud.NativeExtents.MaxPoint +
                        "; geometric bounds: min=" + cloud.GeomExtents.MinPoint + ", max=" + cloud.GeomExtents.MaxPoint); }
                    catch (System.Exception error) { ed.WriteMessage("\n  Bounds unavailable: " + error.Message); }
                    try
                    {
                        var definition = (PointCloudDefEx)tr.GetObject(cloud.PointCloudDefExId, OpenMode.ForRead);
                        ed.WriteMessage("\n  Source=" + Path.GetFileName(definition.SourceFileName) + "; definition bounds: min=" + definition.extents.MinPoint + ", max=" + definition.extents.MaxPoint);
                    }
                    catch (System.Exception error) { ed.WriteMessage("\n  Definition unavailable: " + error.Message); }
                }
                ed.WriteMessage("\nTotals: model-space clouds=" + clouds + ", section objects=" + sections +
                    ". Nested/xref clouds are not enumerated. No display settings or objects changed.");
            }
            catch (System.Exception error) { ed.WriteMessage("\nDiagnostic: " + error.Message); }
            ed.WriteMessage("\n=== END CLOUD DISPLAY DIAGNOSTIC ===");
        }
    }
}
