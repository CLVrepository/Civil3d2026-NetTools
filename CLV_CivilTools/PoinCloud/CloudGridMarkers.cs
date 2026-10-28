using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.PointCloud
{
    public static partial class CloudGridCommands
    {
        [CommandMethod("CGPOINTSIZE", CommandFlags.Modal)]
        public static void ResizePointDisplay() => Run(d =>
        {
            if (review == null || !ReferenceEquals(review.Document, d))
            {
                d.Editor.Command("_.PTYPE");
                return;
            }
            double? size = Positive(d.Editor, "\nReview marker diameter as percent of screen height", review.MarkerPercent);
            if (size.HasValue)
            {
                if (size.Value > 10) throw new ArgumentException("Use 10 percent or less for review markers.");
                review.MarkerPercent = size.Value;
                d.Editor.WriteMessage("\nReview markers set to " + size.Value + "% of view height. Native POINT styles/coordinates are unchanged.");
            }
        });

        private static void StartReviewMarkers(Review state)
        {
            state.MarkerViewChanged = (_, _) => state.MarkerPending = true;
            state.MarkerIdle = (_, _) =>
            {
                if (!ReferenceEquals(AcadApp.DocumentManager.MdiActiveDocument, state.Document))
                {
                    ClearReviewMarkers(state); state.MarkerPending = true; return;
                }
                if (!state.MarkerPending || !state.Document.Editor.IsQuiescent) return;
                try
                {
                    using (state.Document.LockDocument()) RefreshReviewMarkers(state);
                }
                catch (System.Exception) { ClearReviewMarkers(state); state.MarkerPending = false; }
            };
            state.Document.ViewChanged += state.MarkerViewChanged;
            AcadApp.Idle += state.MarkerIdle;
        }

        private static void StopReviewMarkers(Review state)
        {
            if (state.MarkerViewChanged != null) state.Document.ViewChanged -= state.MarkerViewChanged;
            if (state.MarkerIdle != null) AcadApp.Idle -= state.MarkerIdle;
            state.MarkerViewChanged = null; state.MarkerIdle = null;
            ClearReviewMarkers(state);
        }

        private static void ClearReviewMarkers(Review state)
        {
            foreach (Entity marker in state.Markers)
            {
                try { TransientManager.CurrentTransientManager.EraseTransient(marker, new IntegerCollection()); }
                catch (System.Exception) { }
                marker.Dispose();
            }
            state.Markers.Clear();
        }

        private static void RefreshReviewMarkers(Review state)
        {
            ClearReviewMarkers(state);
            state.MarkerPending = false;
            if (state.PlanView || !ReferenceEquals(AcadApp.DocumentManager.MdiActiveDocument, state.Document)) return;
            var ed = state.Document.Editor;
            using ViewTableRecord view = ed.GetCurrentView();
            if (view.PerspectiveEnabled) return;
            double radius = view.Height * state.MarkerPercent / 200;
            if (!double.IsFinite(radius) || radius <= 0) return;
            ScreenProjection projection = ProjectionFor(view);
            Matrix3d toWorld = projection.WorldToView.Inverse();
            Vector3d x = Vector3d.XAxis.TransformBy(toWorld).GetNormal() * radius;
            Vector3d y = Vector3d.YAxis.TransformBy(toWorld).GetNormal() * radius;
            var viewports = new IntegerCollection(new[] { Convert.ToInt32(AcadApp.GetSystemVariable("CVPORT")) });
            using Transaction tr = state.Document.Database.TransactionManager.StartOpenCloseTransaction();
            foreach (var item in Candidates(state.Document, state))
            {
                var point = (Entity)tr.GetObject(item.Id, OpenMode.ForRead);
                Point3d center = PointPosition(point);
                // Overlay graphics face the current view; native DBPoint orientation/XYZ is untouched.
                AddReviewMarker(state, new Circle(center, view.ViewDirection.GetNormal(), radius), viewports, item.Tag);
                AddReviewMarker(state, new Line(center - x, center + x), viewports, item.Tag);
                AddReviewMarker(state, new Line(center - y, center + y), viewports, item.Tag);
            }
        }
        private static void AddReviewMarker(Review state, Entity marker, IntegerCollection viewports, Tag tag)
        {
            marker.ColorIndex = tag.Status == "Excluded" ? 1 : tag.Status == "Accepted" ? 3 : tag.Status == "Adjusted" ? 4 : 2;
            if (!TransientManager.CurrentTransientManager.AddTransient(marker, TransientDrawingMode.DirectShortTerm, 128, viewports))
            {
                marker.Dispose();
                throw new InvalidOperationException("Civil 3D could not draw the temporary review markers.");
            }
            state.Markers.Add(marker);
        }
    }
}
