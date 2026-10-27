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
        private static void ReadSection(Editor ed, string label, Action read)
        {
            try { read(); }
            catch (System.Exception error) { ed.WriteMessage("\n" + label + " unavailable: " + error.Message + " (continuing)"); }
        }

        [CommandMethod("CGCLOUDDIAG", CommandFlags.Modal)]
        public static void ReportDisplay()
        {
            Document? d = AcadApp.DocumentManager.MdiActiveDocument;
            if (d == null) return;
            Editor ed = d.Editor;
            ed.WriteMessage("\n=== CLOUD DISPLAY DIAGNOSTIC R2 (read-only) ===");
            try
            {
                if (!d.Database.TileMode) throw new InvalidOperationException("Use the Model tab.");
                ReadSection(ed, "Editor view", () =>
                {
                    using ViewTableRecord view = ed.GetCurrentView();
                    ed.WriteMessage("\nEditor view: front=" + view.FrontClipEnabled + ", back=" + view.BackClipEnabled +
                        ", atEye=" + view.FrontClipAtEye + ", frontZ=" + view.FrontClipDistance + ", backZ=" + view.BackClipDistance +
                        ", perspective=" + view.PerspectiveEnabled + ", target=" + view.Target + ", direction=" + view.ViewDirection +
                        ", width=" + view.Width + ", height=" + view.Height);
                });
                ReadSection(ed, "View variables", () => ed.WriteMessage("\nVIEWMODE=" + AcadApp.GetSystemVariable("VIEWMODE") +
                    ", CVPORT=" + AcadApp.GetSystemVariable("CVPORT")));
                using Transaction tr = d.Database.TransactionManager.StartOpenCloseTransaction();
                ReadSection(ed, "Stored viewport", () =>
                {
                    ObjectId id = d.Database.CurrentViewportTableRecordId;
                    if (id.IsNull) { ed.WriteMessage("\nStored viewport: no object ID."); return; }
                    var viewport = (ViewportTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    ed.WriteMessage("\nStored viewport: front=" + viewport.FrontClipEnabled + ", back=" + viewport.BackClipEnabled +
                        ", atEye=" + viewport.FrontClipAtEye + ", frontZ=" + viewport.FrontClipDistance + ", backZ=" + viewport.BackClipDistance);
                });
                ReadSection(ed, "Section manager", () =>
                {
                    ObjectId id = d.Database.SectionManagerId;
                    if (id.IsNull) { ed.WriteMessage("\nSection manager: no object ID; continuing with model-space scan."); return; }
                    var manager = (SectionManager)tr.GetObject(id, OpenMode.ForRead);
                    ed.WriteMessage("\nSection manager: count=" + manager.NumSections +
                        ", liveId=" + (manager.LiveSection.IsNull ? "none" : manager.LiveSection.Handle.ToString()));
                });
                var table = (BlockTable)tr.GetObject(d.Database.BlockTableId, OpenMode.ForRead);
                var model = (BlockTableRecord)tr.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                int clouds = 0, sections = 0;
                foreach (ObjectId id in model)
                {
                    if (id.IsNull || id.IsErased) continue;
                    ReadSection(ed, "Model object " + id.Handle, () =>
                    {
                        DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                        if (obj is Section section)
                        {
                            sections++;
                            ed.WriteMessage("\nSECTION handle=" + section.Handle + ", live=" + section.IsLiveSectionEnabled +
                                ", visible=" + section.Visible + ", state=" + section.State + ", layer=" + section.Layer);
                        }
                        if (obj is not PointCloudEx cloud) return;
                        clouds++;
                        ed.WriteMessage("\nCLOUD handle=" + cloud.Handle + ", visible=" + cloud.Visible + ", layer=" + cloud.Layer +
                            ", location=" + cloud.Location + ", rotation=" + cloud.Rotation + ", scale=" + cloud.Scale);
                        ReadSection(ed, "  Cloud crop flags", () => ed.WriteMessage("\n  Crops=" + cloud.getCroppingCount() +
                            ", showCropped=" + cloud.ShowCropped + ", inverted=" + cloud.CroppingInverted));
                        ReadSection(ed, "  Cloud layer", () =>
                        {
                            var layer = (LayerTableRecord)tr.GetObject(cloud.LayerId, OpenMode.ForRead);
                            ed.WriteMessage("\n  Layer: off=" + layer.IsOff + ", frozen=" + layer.IsFrozen + ", locked=" + layer.IsLocked);
                        });
                        ReadSection(ed, "  Review tag", () =>
                        {
                            using ResultBuffer? tag = cloud.GetXDataForApplication("CLV_CLOUD_GRID_V1");
                            var values = tag?.AsArray() ?? Array.Empty<TypedValue>();
                            ed.WriteMessage("\n  Review tag=" + (values.Length > 1 ? values[1].Value : "none"));
                        });
                        ReadSection(ed, "  Cloud bounds", () => ed.WriteMessage("\n  Native bounds: min=" + cloud.NativeExtents.MinPoint +
                            ", max=" + cloud.NativeExtents.MaxPoint + "; geometric bounds: min=" + cloud.GeomExtents.MinPoint +
                            ", max=" + cloud.GeomExtents.MaxPoint));
                        ReadSection(ed, "  Elevation display", () => ed.WriteMessage("\n  Stylization=" + cloud.Stylization +
                            ", elevationRange=" + cloud.MinMaxElevation + ", elevationOutOfRange=" + cloud.ElevationOutOfRangeBehavior));
                        ReadSection(ed, "  Cloud definition", () =>
                        {
                            ObjectId definitionId = cloud.PointCloudDefExId;
                            if (definitionId.IsNull) { ed.WriteMessage("\n  Definition: no object ID."); return; }
                            var definition = (PointCloudDefEx)tr.GetObject(definitionId, OpenMode.ForRead);
                            ed.WriteMessage("\n  Definition handle=" + definition.Handle + ", source=" + Path.GetFileName(definition.SourceFileName) + ", active=" + Path.GetFileName(definition.ActiveFileName) +
                                "; bounds: min=" + definition.extents.MinPoint + ", max=" + definition.extents.MaxPoint);
                        });
                    });
                }
                ed.WriteMessage("\nTotals: model-space clouds=" + clouds + ", section objects=" + sections +
                    ". Nested/xref clouds are not enumerated. No display settings or objects changed.");
            }
            catch (System.Exception error) { ed.WriteMessage("\nDiagnostic could not finish model scan: " + error.Message); }
            ed.WriteMessage("\n=== END CLOUD DISPLAY DIAGNOSTIC R2 ===");
        }
    }
}
