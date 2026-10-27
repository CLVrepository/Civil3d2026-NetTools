using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.PointCloud
{
    public static partial class CloudGridCommands
    {
        // Picks are resolved against generated DBPoints in screen space. Native entity picking
        // would hit the cloud first. No cloud, source layer or global selection setting is locked.
        private sealed record PickCandidate(ObjectId Id, Tag Tag, double X, double Y);
        private static Review? CurrentReview(Document d)
        {
            if (review != null && !ReferenceEquals(review.Document, d))
                throw new InvalidOperationException("Finish the review in its original drawing with CGEND first.");
            return review;
        }

        [CommandMethod("CGMOVEZ", CommandFlags.Modal)]
        public static void MoveElevation() => Run(d =>
        {
            Review? state = CurrentReview(d);
            ObjectId[] ids = PickGridPoints(d, state, true);
            if (ids.Length == 0) return;
            try
            {
                var options = new PromptDoubleOptions("\nRelative WORLD Z change in drawing units (negative lowers, e.g. -0.04)")
                { AllowNegative = true, AllowZero = false };
                var result = d.Editor.GetDouble(options);
                if (result.Status != PromptStatus.OK) return;
                if (!double.IsFinite(result.Value)) throw new ArgumentException("Z change must be finite.");
                using Transaction tr = d.Database.TransactionManager.StartTransaction();
                foreach (ObjectId id in ids)
                {
                    var point = (DBPoint)tr.GetObject(id, OpenMode.ForWrite);
                    Tag tag = state == null ? ReadTag(point) ?? throw new InvalidOperationException("Grid point metadata missing.") : RequireCurrent(state, point);
                    Point3d p = point.Position;
                    double z = p.Z + result.Value;
                    if (!double.IsFinite(z)) throw new ArgumentException("Resulting elevation must be finite.");
                    point.Position = new Point3d(p.X, p.Y, z);
                    WriteTag(point, tag with { Status = tag.Status == "Excluded" ? "Excluded" : "Adjusted" });
                }
                tr.Commit(); d.Editor.Regen();
                d.Editor.WriteMessage("\nMoved " + ids.Length + " grid POINT(s) by world Z " +
                    result.Value.ToString("G", CultureInfo.InvariantCulture) + ". XY and original sampled Z retained. Navigation will not resample.");
            }
            finally { d.Editor.SetImpliedSelection(Array.Empty<ObjectId>()); }
        });

        [CommandMethod("CGSELECT", CommandFlags.Modal)]
        public static void SelectGridPoints() => Run(d =>
        {
            ObjectId[] ids = PickGridPoints(d, CurrentReview(d), true);
            d.Editor.SetImpliedSelection(ids);
            d.Editor.WriteMessage("\nSelected " + ids.Length + " grid POINT(s). Use Properties or another command; ordinary commands retain their own UCS behavior.");
        });

        private static ObjectId PickGridPoint(Document d, Review? state)
        {
            ObjectId[] ids = PickGridPoints(d, state, false);
            d.Editor.SetImpliedSelection(Array.Empty<ObjectId>());
            return ids.Length == 0 ? ObjectId.Null : ids[0];
        }


        // Orthographic view projection uses AutoCAD geometry only. Editor.PointToScreen
        // returns System.Windows.Point, which requires WPF's WindowsBase reference.
        private sealed record ScreenProjection(Matrix3d WorldToView, double PixelsPerX, double PixelsPerY)
        {
            internal Point2d Project(Point3d world)
            {
                Point3d eye = world.TransformBy(WorldToView);
                return new Point2d(eye.X * PixelsPerX, eye.Y * PixelsPerY);
            }
        }
        private static ScreenProjection ProjectionFor(ViewTableRecord view)
        {
            if (view.PerspectiveEnabled) throw new InvalidOperationException("Use an orthographic plan or section view for point picking.");
            if (AcadApp.GetSystemVariable("SCREENSIZE") is not Point2d pixels ||
                pixels.X <= 0 || pixels.Y <= 0 || view.Width <= 0 || view.Height <= 0)
                throw new InvalidOperationException("Cannot determine the active viewport size.");
            Matrix3d toWorld = Matrix3d.PlaneToWorld(view.ViewDirection);
            toWorld = Matrix3d.Displacement(view.Target - Point3d.Origin) * toWorld;
            toWorld = Matrix3d.Rotation(-view.ViewTwist, view.ViewDirection, view.Target) * toWorld;
            return new ScreenProjection(toWorld.Inverse(), pixels.X / view.Width, pixels.Y / view.Height);
        }
        private static Point2d ProjectToScreen(Editor ed, Point3d world)
        {
            using ViewTableRecord view = ed.GetCurrentView();
            return ProjectionFor(view).Project(world);
        }

        private static List<PickCandidate> Candidates(Document d, Review? state)
        {
            using ViewTableRecord view = d.Editor.GetCurrentView();
            if (view.PerspectiveEnabled) throw new InvalidOperationException("Use an orthographic plan or section view for point picking.");
            ScreenProjection projection = ProjectionFor(view);
            Matrix3d toView = projection.WorldToView;
            using Transaction tr = d.Database.TransactionManager.StartOpenCloseTransaction();
            var candidates = new List<PickCandidate>();
            foreach (ObjectId id in Model(d.Database, tr, OpenMode.ForRead))
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not DBPoint point || ReadTag(point) is not Tag tag || !point.Visible) continue;
                if (state != null && (tag.Job != state.Job.Key ||
                    (!state.PlanView && (state.Columns ? tag.Column : tag.Row) != state.Rows[state.Index]))) continue;
                var layer = (LayerTableRecord)tr.GetObject(point.LayerId, OpenMode.ForRead);
                if (layer.IsOff || layer.IsFrozen || layer.IsLocked) continue;
                Point3d eye = point.Position.TransformBy(toView);
                if (view.FrontClipEnabled && !view.FrontClipAtEye && eye.Z > view.FrontClipDistance + 1e-8) continue;
                if (view.BackClipEnabled && eye.Z < view.BackClipDistance - 1e-8) continue;
                if (Math.Abs(eye.X - view.CenterPoint.X) > view.Width / 2 ||
                    Math.Abs(eye.Y - view.CenterPoint.Y) > view.Height / 2) continue;
                Point2d screen = projection.Project(point.Position);
                if (double.IsFinite(screen.X) && double.IsFinite(screen.Y))
                    candidates.Add(new PickCandidate(id, tag, screen.X, screen.Y));
            }
            return candidates;
        }

        private static ObjectId[] PickGridPoints(Document d, Review? state, bool multiple)
        {
            Editor ed = d.Editor;
            var chosen = new HashSet<ObjectId>();
            string? job = state?.Job.Key;
            object snap = AcadApp.GetSystemVariable("OSMODE"), snap3d = AcadApp.GetSystemVariable("3DOSMODE");
            object ortho = AcadApp.GetSystemVariable("ORTHOMODE");
            bool finished = false;
            ed.SetImpliedSelection(Array.Empty<ObjectId>());
            try
            {
                AcadApp.SetSystemVariable("OSMODE", 0); AcadApp.SetSystemVariable("3DOSMODE", 0);
                AcadApp.SetSystemVariable("ORTHOMODE", 0);
                while (true)
                {
                    var options = new PromptPointOptions(multiple ?
                        "\nClick near grid POINT to add/remove [Window] (Enter finishes): " :
                        "\nClick near generated grid POINT (cloud is ignored): ")
                    { AllowNone = true };
                    if (multiple) options.Keywords.Add("Window");
                    var pick = ed.GetPoint(options);
                    if (pick.Status == PromptStatus.None)
                    { finished = true; return chosen.ToArray(); }
                    if (pick.Status != PromptStatus.OK && pick.Status != PromptStatus.Keyword) return Array.Empty<ObjectId>();
                    List<PickCandidate> candidates = Candidates(d, state);
                    if (job != null) candidates = candidates.Where(p => p.Tag.Job == job).ToList();
                    var hits = new List<PickCandidate>();
                    if (pick.Status == PromptStatus.Keyword && pick.StringResult == "Window")
                    {
                        var first = ed.GetPoint("\nPoint-only window first corner: ");
                        if (first.Status != PromptStatus.OK) return Array.Empty<ObjectId>();
                        var last = ed.GetPoint(new PromptPointOptions("\nPoint-only window opposite corner: ")
                        { BasePoint = first.Value, UseBasePoint = true });
                        if (last.Status != PromptStatus.OK) return Array.Empty<ObjectId>();
                        candidates = Candidates(d, state);
                        if (job != null) candidates = candidates.Where(p => p.Tag.Job == job).ToList();
                        Point2d a = ProjectToScreen(ed, first.Value.TransformBy(ed.CurrentUserCoordinateSystem));
                        Point2d b = ProjectToScreen(ed, last.Value.TransformBy(ed.CurrentUserCoordinateSystem));
                        hits = candidates.Where(p => p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X) &&
                            p.Y >= Math.Min(a.Y, b.Y) && p.Y <= Math.Max(a.Y, b.Y)).ToList();
                        if (job == null && hits.Select(p => p.Tag.Job).Distinct().Take(2).Count() > 1)
                        { ed.WriteMessage("\nWindow contains multiple grid jobs. Click one point first to choose a job."); continue; }
                        foreach (var hit in hits) chosen.Add(hit.Id);
                    }
                    else if (pick.Status == PromptStatus.OK)
                    {
                        Point2d cursor = ProjectToScreen(ed, pick.Value.TransformBy(ed.CurrentUserCoordinateSystem));
                        double radius = Math.Max(6, Convert.ToDouble(AcadApp.GetSystemVariable("PICKBOX"), CultureInfo.InvariantCulture) + 3);
                        var near = candidates.Select(p => (Point: p, Distance: Math.Sqrt(Math.Pow(p.X - cursor.X, 2) + Math.Pow(p.Y - cursor.Y, 2))))
                            .Where(p => p.Distance <= radius).OrderBy(p => p.Distance).Take(2).ToArray();
                        if (near.Length > 1 && near[1].Distance - near[0].Distance < 1)
                        { ed.WriteMessage("\nTwo projected points are too close to distinguish. Zoom in or use a narrower slice."); continue; }
                        if (near.Length > 0)
                        {
                            hits.Add(near[0].Point);
                            if (!chosen.Add(near[0].Point.Id)) chosen.Remove(near[0].Point.Id);
                        }
                    }
                    if (hits.Count == 0)
                    { ed.WriteMessage("\nNo eligible visible grid POINT near that location. Click its center or use Window; check point layers and slice."); continue; }
                    job ??= hits[0].Tag.Job;
                    if (!multiple) { finished = true; return new[] { hits[0].Id }; }
                    ed.SetImpliedSelection(chosen.ToArray());
                    ed.WriteMessage("\n" + chosen.Count + " grid POINT(s) selected. Enter finishes; Esc cancels all.");
                }
            }
            finally
            {
                AcadApp.SetSystemVariable("OSMODE", snap); AcadApp.SetSystemVariable("3DOSMODE", snap3d);
                AcadApp.SetSystemVariable("ORTHOMODE", ortho);
                if (!finished) ed.SetImpliedSelection(Array.Empty<ObjectId>());
            }
        }
    }
}
