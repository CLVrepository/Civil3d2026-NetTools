using System;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Shared
{
    /// <summary>
    /// View, UCS, and zoom related helpers.
    /// Replaces the view/UCS parts of CommonUtils.
    /// </summary>
    internal static class ViewState
    {
        // ------------------------------------------------------------
        // Named view save/restore
        // ------------------------------------------------------------

        public static void SaveView(string viewName)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var vt = (ViewTable)tr.GetObject(db.ViewTableId, OpenMode.ForRead);

                if (vt.Has(viewName))
                {
                    vt.UpgradeOpen();
                    ObjectId oldId = vt[viewName];
                    var oldRec = (ViewTableRecord)tr.GetObject(oldId, OpenMode.ForWrite);
                    oldRec.Erase(true);
                }

                ViewTableRecord cur = ed.GetCurrentView();

                vt.UpgradeOpen();
                var newRec = new ViewTableRecord
                {
                    Name = viewName,
                    CenterPoint = cur.CenterPoint,
                    Height = cur.Height,
                    Width = cur.Width,
                    Target = cur.Target,
                    ViewDirection = cur.ViewDirection,
                    ViewTwist = cur.ViewTwist,
                    LensLength = cur.LensLength,
                    PerspectiveEnabled = cur.PerspectiveEnabled,
                    FrontClipEnabled = cur.FrontClipEnabled,
                    FrontClipDistance = cur.FrontClipDistance,
                    BackClipEnabled = cur.BackClipEnabled,
                    BackClipDistance = cur.BackClipDistance
                };

                vt.Add(newRec);
                tr.AddNewlyCreatedDBObject(newRec, true);
                tr.Commit();
            }
        }

        public static bool RestoreView(string viewName)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var vt = (ViewTable)tr.GetObject(db.ViewTableId, OpenMode.ForRead);
                if (!vt.Has(viewName))
                {
                    tr.Commit();
                    return false;
                }

                var saved = (ViewTableRecord)tr.GetObject(vt[viewName], OpenMode.ForRead);

                ViewTableRecord cur = ed.GetCurrentView();
                cur.CenterPoint = saved.CenterPoint;
                cur.Height = saved.Height;
                cur.Width = saved.Width;
                cur.Target = saved.Target;
                cur.ViewDirection = saved.ViewDirection;
                cur.ViewTwist = saved.ViewTwist;

                cur.LensLength = saved.LensLength;
                cur.PerspectiveEnabled = saved.PerspectiveEnabled;
                cur.FrontClipEnabled = saved.FrontClipEnabled;
                cur.FrontClipDistance = saved.FrontClipDistance;
                cur.BackClipEnabled = saved.BackClipEnabled;
                cur.BackClipDistance = saved.BackClipDistance;

                ed.SetCurrentView(cur);

                tr.Commit();
                return true;
            }
        }

        // ------------------------------------------------------------
        // UCS + PLAN helpers
        // ------------------------------------------------------------

        public static void SetUcsWorld(Editor ed)
        {
            ed.CurrentUserCoordinateSystem = Matrix3d.Identity;
        }

        public static void PlanCurrentUcs(Editor ed)
        {
            ed.Command("_.PLAN", "_C");
        }

        public static void PlanWorld(Editor ed)
        {
            ed.Command("_.PLAN", "_W");
        }

        /// <summary>
        /// Sets a cross-section UCS where:
        ///   X = given direction (flattened to XY),
        ///   Y = world Z,
        ///   Z = X × Y.
        /// </summary>
        public static void SetCrossSectionUcs(Point3d origin, Vector3d xAxisDir)
        {
            var ed = AcadApp.DocumentManager.MdiActiveDocument.Editor;

            Vector3d x = new Vector3d(xAxisDir.X, xAxisDir.Y, 0.0);
            if (x.Length < 1e-9) x = Vector3d.XAxis;
            x = x.GetNormal();

            Vector3d y = Vector3d.ZAxis;

            Vector3d z = x.CrossProduct(y);
            if (z.Length < 1e-9) z = Vector3d.YAxis;
            z = z.GetNormal();

            y = z.CrossProduct(x).GetNormal();

            Matrix3d ucs =
                Matrix3d.AlignCoordinateSystem(
                    Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                    origin, x, y, z
                );

            ed.CurrentUserCoordinateSystem = ucs;
        }

        // ------------------------------------------------------------
        // Zoom helpers
        // ------------------------------------------------------------

        /// <summary>
        /// Zoom to a rectangle defined by its long and short side,
        /// centered at the given WCS point, with padding and aspect handling.
        /// </summary>
        public static void ZoomCenterByRect(Point3d centerWcs, double rectLongSide, double rectShortSide)
        {
            var ed = AcadApp.DocumentManager.MdiActiveDocument.Editor;

            Matrix3d wcsToUcs = ed.CurrentUserCoordinateSystem.Inverse();
            Point3d cUcs = centerWcs.TransformBy(wcsToUcs);

            ViewTableRecord view = ed.GetCurrentView();

            double aspect = (view.Height > 1e-9) ? (view.Width / view.Height) : 1.5;
            if (aspect < 1e-6) aspect = 1.5;

            double pad = Math.Max(rectShortSide * 1.0, rectLongSide * 0.05);

            double width = Math.Max(5.0, rectLongSide + 2.0 * pad);

            double heightFromAspect = width / aspect;
            double heightMin = Math.Max(5.0, rectShortSide + 2.0 * pad);
            double height = Math.Max(heightFromAspect, heightMin);

            view.CenterPoint = new Point2d(cUcs.X, cUcs.Y);
            view.Width = width;
            view.Height = height;

            ed.SetCurrentView(view);
        }
    }
}