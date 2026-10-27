using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CLV_CivilTools.Shared;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.PointCloud
{
    public static partial class CloudGridCommands
    {
        [CommandMethod("CGADDPOINT", CommandFlags.Modal)]
        public static void AddSlicePoint() => Run(d =>
        {
            Review state = NeedReview(d);
            if (state.PlanView) throw new InvalidOperationException("Use CGSECTION before adding points.");
            Job job = state.Job;
            Vector3d axis = state.Columns ? new Vector3d(-job.Frame.AxisY, job.Frame.AxisX, 0) :
                new Vector3d(job.Frame.AxisX, job.Frame.AxisY, 0);
            Vector3d normal = axis.CrossProduct(Vector3d.ZAxis).GetNormal();
            using (ViewTableRecord view = d.Editor.GetCurrentView())
                if (view.PerspectiveEnabled || Math.Abs(view.ViewDirection.GetNormal().DotProduct(normal)) < 1 - 1e-8)
                    throw new InvalidOperationException("Use CGSECTION to restore the side-on slice view first.");
            using (Transaction tr = d.Database.TransactionManager.StartOpenCloseTransaction())
                if (!state.Cloud.IsValid || state.Cloud.IsErased || tr.GetObject(state.Cloud, OpenMode.ForRead) is not PointCloudEx)
                    throw new InvalidOperationException("Review cloud is unavailable. Run CGEND and restart review.");
            var mode = new PromptKeywordOptions("\nAdd using [Cloud/Visual] <Cloud>: ");
            mode.Keywords.Add("Cloud"); mode.Keywords.Add("Visual"); mode.Keywords.Default = "Cloud"; mode.AllowNone = true;
            var answer = d.Editor.GetKeywords(mode);
            if (answer.Status != PromptStatus.OK && answer.Status != PromptStatus.None) return;
            bool cloudSnap = answer.StringResult != "Visual";
            object snap = AcadApp.GetSystemVariable("OSMODE"), snap3d = AcadApp.GetSystemVariable("3DOSMODE");
            object snapZ = AcadApp.GetSystemVariable("OSNAPZ"), ortho = AcadApp.GetSystemVariable("ORTHOMODE");
            object gridSnap = AcadApp.GetSystemVariable("SNAPMODE");
            Matrix3d originalUcs = d.Editor.CurrentUserCoordinateSystem;
            int added = 0;
            try
            {
                int index = state.Rows[state.Index];
                GridXY center = state.Columns ? job.Frame.World(index * job.Frame.Spacing, (job.MinV + job.MaxV) / 2) :
                    job.Frame.World((job.MinU + job.MaxU) / 2, index * job.Frame.Spacing);
                ViewState.SetCrossSectionUcs(new Point3d(center.X, center.Y, (state.MinZ + state.MaxZ) / 2), axis);
                AcadApp.SetSystemVariable("OSMODE", 0);
                AcadApp.SetSystemVariable("3DOSMODE", cloudSnap ? 128 : 0);
                AcadApp.SetSystemVariable("OSNAPZ", 0);
                AcadApp.SetSystemVariable("ORTHOMODE", 0); AcadApp.SetSystemVariable("SNAPMODE", 0);
                d.Editor.WriteMessage(cloudSnap ?
                    "\nCloud NODE snap enabled. Wait for its snap glyph before clicking; a click without a glyph is a visual pick. Z is retained, XY is projected onto this grid line." :
                    "\nVisual mode: click the desired ground envelope in the side view. XY is constrained to this grid line.");
                while (true)
                {
                    var pick = d.Editor.GetPoint(new PromptPointOptions("\nPick additional ground/crown point (Enter finishes): ") { AllowNone = true });
                    if (pick.Status != PromptStatus.OK) break;
                    Point3d picked = pick.Value.TransformBy(d.Editor.CurrentUserCoordinateSystem);
                    if (!double.IsFinite(picked.Z)) throw new ArgumentException("Picked elevation must be finite.");
                    GridNode node = CloudGridMath.ProjectToSlice(job.Frame, state.Columns, index, new GridXY(picked.X, picked.Y));
                    using Transaction tr = d.Database.TransactionManager.StartTransaction();
                    var polygon = JobPolygon(d.Database, tr, job.Key);
                    if (!CloudGridMath.Contains(polygon, new GridXY(node.X, node.Y)))
                    { d.Editor.WriteMessage("\nProjected point is outside the job boundary; nothing added."); continue; }
                    if (Points(d.Database, tr, job.Key).Any(p => p.Tag.Status != "Excluded" &&
                        Math.Abs(p.Position.X - node.X) <= 1e-7 && Math.Abs(p.Position.Y - node.Y) <= 1e-7))
                    { d.Editor.WriteMessage("\nAn included point already occupies this XY. Use CGMOVEZ/CGSETZ instead."); continue; }
                    EnsureLayer(d.Database, tr, Layer(job, "POINTS"), 2, true);
                    Register(d.Database, tr);
                    var point = new DBPoint(new Point3d(node.X, node.Y, picked.Z)) { Layer = Layer(job, "POINTS") };
                    // Added provenance means OriginalZ is the first picked elevation, not a TIN sample.
                    WriteTag(point, new Tag(job.Key, node.Row, node.Column, picked.Z, "Adjusted", true));
                    Model(d.Database, tr, OpenMode.ForWrite).AppendEntity(point); tr.AddNewlyCreatedDBObject(point, true);
                    tr.Commit(); added++;
                    state.MinZ = Math.Min(state.MinZ, picked.Z); state.MaxZ = Math.Max(state.MaxZ, picked.Z);
                    d.Editor.Regen();
                    RefreshReviewMarkers(state);
                    d.Editor.WriteMessage("\nAdded POINT at Z " + picked.Z.ToString("F4") + " on current grid line. Earlier additions remain if you press Esc.");
                }
            }
            finally
            {
                d.Editor.CurrentUserCoordinateSystem = originalUcs;
                AcadApp.SetSystemVariable("OSMODE", snap); AcadApp.SetSystemVariable("3DOSMODE", snap3d);
                AcadApp.SetSystemVariable("OSNAPZ", snapZ); AcadApp.SetSystemVariable("ORTHOMODE", ortho);
                AcadApp.SetSystemVariable("SNAPMODE", gridSnap);
                d.Editor.WriteMessage("\nAdded " + added + " supplemental POINT(s); snap/UCS settings restored. Navigation will not resample.");
            }
        });

        private static List<GridXY> JobPolygon(Database db, Transaction tr, string key)
        {
            foreach (ObjectId id in Model(db, tr, OpenMode.ForRead))
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Polyline outline) continue;
                using ResultBuffer? data = outline.GetXDataForApplication(App);
                TypedValue[] v = data?.AsArray() ?? Array.Empty<TypedValue>();
                if (v.Length != 3 || (string?)v[1].Value != "JOB") continue;
                Job? job = JsonSerializer.Deserialize<Job>((string)v[2].Value);
                if (job?.Key != key) continue;
                return Enumerable.Range(0, outline.NumberOfVertices).Select(i =>
                { Point3d p = outline.GetPoint3dAt(i); return new GridXY(p.X, p.Y); }).ToList();
            }
            throw new InvalidOperationException("The tagged job boundary is missing.");
        }
    }
}
