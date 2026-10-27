using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.PointCloud
{
    public static class CloudGridRestoreCommands
    {
        [CommandMethod("CGFULLCLOUD", CommandFlags.Modal)]
        public static void RestoreFullCloud()
        {
            Document? d = AcadApp.DocumentManager.MdiActiveDocument;
            if (d == null) return;
            try
            {
                if (!d.Database.TileMode) throw new InvalidOperationException("Use the Model tab.");
                Editor ed = d.Editor;
                using Transaction tr = d.Database.TransactionManager.StartTransaction();
                var table = (BlockTable)tr.GetObject(d.Database.BlockTableId, OpenMode.ForRead);
                var model = (BlockTableRecord)tr.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                var clouds = new List<ObjectId>();
                foreach (ObjectId id in model)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not PointCloudEx cloud) continue;
                    using ResultBuffer? data = cloud.GetXDataForApplication("CLV_CLOUD_GRID_V1");
                    TypedValue[] values = data?.AsArray() ?? Array.Empty<TypedValue>();
                    if (values.Length == 4 && values[1].Value is string role && role == "REVIEW")
                        throw new InvalidOperationException("Run CGEND first to remove the temporary review reference, then CGFULLCLOUD.");
                    var layer = (LayerTableRecord)tr.GetObject(cloud.LayerId, OpenMode.ForRead);
                    if (cloud.Visible && !layer.IsOff && !layer.IsFrozen) clouds.Add(id);
                }
                if (clouds.Count == 0) throw new InvalidOperationException("No visible attached PointCloudEx found directly in model space. Check cloud/layer visibility or xref ownership.");
                int selected = 0;
                if (clouds.Count > 1)
                {
                    for (int i = 0; i < clouds.Count; i++)
                    {
                        var cloud = (PointCloudEx)tr.GetObject(clouds[i], OpenMode.ForRead);
                        ed.WriteMessage("\n" + (i + 1) + ": cloud handle " + cloud.Handle + ", layer " + cloud.Layer + ", crops " + cloud.getCroppingCount());
                    }
                    var pick = ed.GetInteger(new PromptIntegerOptions("\nCloud number to restore")
                    { LowerLimit = 1, UpperLimit = clouds.Count, AllowNone = false });
                    if (pick.Status != PromptStatus.OK) return;
                    selected = pick.Value - 1;
                }
                var target = (PointCloudEx)tr.GetObject(clouds[selected], OpenMode.ForRead);
                ed.WriteMessage("\nRestore cloud handle " + target.Handle + " on layer " + target.Layer +
                    ". This removes ALL crop boundaries on that cloud, including pre-review crops, and disables view front/back clipping.");
                var options = new PromptKeywordOptions("\nRestore full cloud display [Yes/No] <No>: ");
                options.Keywords.Add("Yes"); options.Keywords.Add("No");
                options.Keywords.Default = "No"; options.AllowNone = true;
                var answer = ed.GetKeywords(options);
                if (answer.Status != PromptStatus.OK || answer.StringResult != "Yes") return;
                // Direct managed API: no native cloud-selection prompt or implied selection.
                target = (PointCloudEx)tr.GetObject(clouds[selected], OpenMode.ForWrite, false, true);
                if (target.getCroppingCount() > 0 && !target.clearCropping())
                    throw new InvalidOperationException("Civil 3D refused to clear cloud crops. Check the cloud's Lock property in Properties.");
                target.CroppingInverted = false;
                target.RecordGraphicsModified(true);
                tr.Commit();
                using ViewTableRecord view = ed.GetCurrentView();
                view.FrontClipEnabled = false; view.BackClipEnabled = false;
                ed.SetCurrentView(view);
                ed.SetImpliedSelection(Array.Empty<ObjectId>());
                ed.Regen();
                ed.WriteMessage("\nCloud crop boundaries cleared and view clipping disabled. Grid points, exclusions and elevations retained. Zoom out if needed. Hidden scans/regions, limit boxes and source-file contents are unchanged.");
            }
            catch (System.Exception error)
            {
                d.Editor.WriteMessage("\nFULL CLOUD RESTORE: " + error.Message);
            }
        }
    }
}
