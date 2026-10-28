using System;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using CogoPoint = Autodesk.Civil.DatabaseServices.CogoPoint;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.PointCloud
{
    public static partial class CloudGridCommands
    {
        private static Review NeedPointEditSection(Document d)
        {
            Review state = NeedReview(d);
            Vector3d axis = state.Columns ? new Vector3d(-state.Job.Frame.AxisY, state.Job.Frame.AxisX, 0) :
                new Vector3d(state.Job.Frame.AxisX, state.Job.Frame.AxisY, 0);
            using ViewTableRecord view = d.Editor.GetCurrentView();
            if (state.PlanView || view.PerspectiveEnabled ||
                Math.Abs(view.ViewDirection.GetNormal().DotProduct(axis.CrossProduct(Vector3d.ZAxis).GetNormal())) < 1 - 1e-8)
                throw new InvalidOperationException("Use CGSECTION before picking an elevation or deleting a review point.");
            return state;
        }

        private static void RequireEditablePoint(Document d, Transaction tr, Review state, Entity point)
        {
            RequireCurrent(state, point);
            if (point.OwnerId != Model(d.Database, tr, OpenMode.ForRead).ObjectId || !point.Visible ||
                tr.GetObject(point.LayerId, OpenMode.ForRead) is not LayerTableRecord layer ||
                layer.IsOff || layer.IsFrozen || layer.IsLocked)
                throw new InvalidOperationException("Review point is no longer editable on a visible unlocked model-space layer.");
            if (point is CogoPoint cogo && (cogo.IsLocked || cogo.IsSurveyPoint || cogo.IsProjectPoint || !cogo.IsMovable))
                throw new InvalidOperationException("Locked, survey, project or immovable COGO points are not edited or deleted by this tool.");
        }

        private static void PickCloudElevation(Document d)
        {
            Review state = NeedPointEditSection(d);
            ObjectId id = PickGridPoint(d, state, true);
            if (id.IsNull) return;
            Point3d original;
            using (Transaction read = d.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var point = (Entity)read.GetObject(id, OpenMode.ForRead);
                RequireEditablePoint(d, read, state, point);
                original = PointPosition(point);
            }
            double? elevation = PickVerifiedCloudZ(d, state);
            if (!elevation.HasValue) return;
            if (!ReferenceEquals(AcadApp.DocumentManager.MdiActiveDocument, d))
                throw new InvalidOperationException("Review drawing is no longer active; no point changed.");
            using Transaction tr = d.Database.TransactionManager.StartTransaction();
            var target = (Entity)tr.GetObject(id, OpenMode.ForRead);
            RequireEditablePoint(d, tr, state, target);
            Point3d current = PointPosition(target);
            if (current.X != original.X || current.Y != original.Y || current.Z != original.Z)
                throw new InvalidOperationException("Point changed during picking; nothing updated. Pick again.");
            Tag tag = RequireCurrent(state, target);
            target.UpgradeOpen(); SetPointElevation(target, elevation.Value);
            Point3d updated = PointPosition(target);
            if (updated.X != original.X || updated.Y != original.Y || updated.Z != elevation.Value)
                throw new InvalidOperationException("Exact XY/elevation verification failed; point change not committed.");
            WriteTag(target, tag with { Status = tag.Status == "Excluded" ? "Excluded" : "Adjusted" });
            tr.Commit(); d.Editor.Regen();
            d.Editor.WriteMessage("\nPoint elevation set from verified review-cloud snap; exact WORLD X/Y and original sampled Z retained. Undo reverses this edit.");
        }

        private static double? PickVerifiedCloudZ(Document d, Review state)
        {
            Editor ed = d.Editor;
            string[] names = { "OSMODE", "3DOSMODE", "OSNAPZ", "ORTHOMODE", "SNAPMODE" };
            object[] saved = names.Select(AcadApp.GetSystemVariable).ToArray();
            Matrix3d ucs = ed.CurrentUserCoordinateSystem;
            Point3d? lastSnap = null;
            string evidence = "no matching point-monitor callback";
            bool monitoring = false;
            PointMonitorEventHandler monitor = (_, args) =>
            {
                // A failed event detach must not leave a callback touching a
                // document/native context after this command has finished.
                if (!monitoring) return;
                lastSnap = null;
                evidence = "no verified object snap";
                try
                {
                    // Borrowed callback context: inspect it only here; do not
                    // retain or dispose it. This handler belongs to d.Editor.
                    InputPointContext context = args.Context;
                    PointHistoryBits reject = PointHistoryBits.FromKeyboard | PointHistoryBits.NotInteractive |
                        PointHistoryBits.CyclingPoint | PointHistoryBits.PickAborted | PointHistoryBits.AppFiltered;
                    if (!context.PointComputed)
                    { evidence = "native point not computed"; return; }
                    PointHistoryBits history = context.History;
                    if ((history & PointHistoryBits.ObjectSnapped) == 0)
                    { evidence = "no native object-snap flag; history=" + history; return; }
                    if ((history & reject) != 0)
                    { evidence = "native input flags rejected: " + (history & reject); return; }
                    if ((context.ObjectSnapMask & ObjectSnapMasks.Node) == 0)
                    { evidence = "native snap was not NODE; mask=" + context.ObjectSnapMask; return; }
                    var paths = context.GetKeyPointEntities();
                    // Evidence must identify the active review cloud, not merely
                    // an entity under the cursor or a nearby point/line.
                    if (paths.Length != 1 || !paths[0].GetObjectIds().Contains(state.Cloud))
                    { evidence = "native snap did not identify the active review cloud"; return; }
                    Point3d snapped = context.ObjectSnappedPoint;
                    if (!double.IsFinite(snapped.X) || !double.IsFinite(snapped.Y) || !double.IsFinite(snapped.Z) ||
                        context.ComputedPoint.DistanceTo(snapped) > 1e-7)
                    { evidence = "invalid or filtered snap coordinates"; return; }
                    lastSnap = snapped;
                    evidence = "returned pick differed from the native snapped coordinates";
                }
                catch (System.Exception error) { lastSnap = null; evidence = "native snap evidence unavailable: " + error.Message; }
            };
            try
            {
                // AutoCAD NODE is OSMODE=8. 3DOSMODE=1 disables face snaps;
                // 128 is not a documented AutoCAD NODE mode.
                AcadApp.SetSystemVariable("OSMODE", 8); AcadApp.SetSystemVariable("3DOSMODE", 1);
                AcadApp.SetSystemVariable("OSNAPZ", 0); AcadApp.SetSystemVariable("ORTHOMODE", 0);
                AcadApp.SetSystemVariable("SNAPMODE", 0);
                monitoring = true;
                ed.PointMonitor += monitor;
                while (true)
                {
                    lastSnap = null;
                    evidence = "no matching point-monitor callback";
                    var result = ed.GetPoint(new PromptPointOptions("\nSnap to review-cloud NODE for elevation (Enter/Esc cancels): ") { AllowNone = true });
                    if (result.Status != PromptStatus.OK) return null;
                    Point3d picked = result.Value.TransformBy(ucs);
                    if (lastSnap is Point3d snapped && picked.DistanceTo(snapped) <= 1e-7)
                        return snapped.Z;
                    ed.WriteMessage("\nPoint unchanged: " + evidence + ". Wait for NODE snap and retry, or Esc to cancel.");
                }
            }
            finally
            {
                monitoring = false;
                System.Exception? restoreError = null;
                try { ed.PointMonitor -= monitor; }
                catch (System.Exception error) { restoreError = error; }
                for (int i = 0; i < names.Length; i++)
                {
                    try { AcadApp.SetSystemVariable(names[i], saved[i]); }
                    catch (System.Exception error) { restoreError ??= error; }
                }
                try { ed.SetImpliedSelection(Array.Empty<ObjectId>()); }
                catch (System.Exception error) { restoreError ??= error; }
                if (restoreError != null)
                    throw new InvalidOperationException("Snap callback/settings cleanup failed; no elevation edit applied. " + restoreError.Message);
            }
        }

        [CommandMethod("CGDELETEPOINT", CommandFlags.Modal)]
        public static void DeleteReviewPoint() => Run(d =>
        {
            Review state = NeedPointEditSection(d);
            ObjectId id = PickGridPoint(d, state, true);
            if (id.IsNull) return;
            using Transaction tr = d.Database.TransactionManager.StartTransaction();
            var point = (Entity)tr.GetObject(id, OpenMode.ForRead);
            RequireEditablePoint(d, tr, state, point);
            string identity = point is CogoPoint cogo ? "COGO " + cogo.PointNumber : "POINT " + point.Handle;
            if (point is CogoPoint) CivilDocument.GetCivilDocument(d.Database).CogoPoints.Remove(id);
            else { point.UpgradeOpen(); point.Erase(); }
            tr.Commit(); d.Editor.SetImpliedSelection(Array.Empty<ObjectId>()); d.Editor.Regen();
            d.Editor.WriteMessage("\nDeleted one tagged " + identity + ". Undo restores it. Exclude/Restore does not restore deleted points.");
        });
    }
}
