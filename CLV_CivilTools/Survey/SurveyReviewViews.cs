using System;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CLV_CivilTools.Shared;

namespace CLV_CivilTools.Survey
{
    public static class SurveyReviewViews
    {
        private const string ReviewLayoutName = "CLV CLOSURE REVIEW";

        [CommandMethod("SURVEY-CLOSURE-REVIEW", CommandFlags.Modal)]
        [CommandMethod("SURVEYCLOSUREREVIEW", CommandFlags.Modal)]
        public static void CreateReviewViewports()
        {
            Document? doc = AcAp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                using DocumentLock docLock = doc.LockDocument();

                LayerStandards.EnsureSurveyMapClosureLayers(db, ed);

                ViewTableRecord sourceView = ed.GetCurrentView();

                LayoutManager layoutManager = LayoutManager.Current;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    if (!LayoutExists(db, tr, ReviewLayoutName))
                        layoutManager.CreateLayout(ReviewLayoutName);
                    tr.Commit();
                }

                layoutManager.CurrentLayout = ReviewLayoutName;
                db.TileMode = false;

                using Transaction tr2 = db.TransactionManager.StartTransaction();

                Layout layout = (Layout)tr2.GetObject(layoutManager.GetLayoutId(ReviewLayoutName), OpenMode.ForRead);
                BlockTableRecord paperSpace = (BlockTableRecord)tr2.GetObject(layout.BlockTableRecordId, OpenMode.ForWrite);

                EraseExistingFloatingViewports(tr2, paperSpace);

                LayerTable layerTable = (LayerTable)tr2.GetObject(db.LayerTableId, OpenMode.ForRead);
                ObjectId origLayerId = layerTable[LayerStandards.SurveyMapOriginalLayerName];
                ObjectId adjLayerId = layerTable[LayerStandards.SurveyMapAdjustedLayerName];

                Viewport leftViewport = CreateFloatingViewport(
                    paperSpace,
                    tr2,
                    center: new Point3d(5.25, 4.0, 0.0),
                    width: 10.0,
                    height: 7.5,
                    sourceView);
                FreezeSingleLayerInViewport(leftViewport, adjLayerId);

                Viewport rightViewport = CreateFloatingViewport(
                    paperSpace,
                    tr2,
                    center: new Point3d(15.75, 4.0, 0.0),
                    width: 10.0,
                    height: 7.5,
                    sourceView);
                FreezeSingleLayerInViewport(rightViewport, origLayerId);

                tr2.Commit();

                ed.Regen();
                ed.WriteMessage(
                    "\nSURVEY-CLOSURE-REVIEW:" +
                    "\nCreated paper-space review layout with two floating viewports." +
                    $"\n  Left viewport: {LayerStandards.SurveyMapOriginalLayerName} only" +
                    $"\n  Right viewport: {LayerStandards.SurveyMapAdjustedLayerName} only" +
                    "\nDouble-click inside either viewport to pan/zoom, then run SURVEY-SYNC-REVIEW to match the other side.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nSURVEY-CLOSURE-REVIEW failed: {ex.Message}");
            }
        }

        [CommandMethod("SURVEY-SYNC-REVIEW", CommandFlags.Modal)]
        [CommandMethod("SURVEYSYNCREVIEW", CommandFlags.Modal)]
        public static void SyncReviewView()
        {
            Document? doc = AcAp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                using DocumentLock docLock = doc.LockDocument();

                if (db.TileMode)
                {
                    ed.WriteMessage("\nSURVEY-SYNC-REVIEW: Use the CLV CLOSURE REVIEW layout created by SURVEY-CLOSURE-REVIEW.");
                    return;
                }

                short activeViewportNumber = Convert.ToInt16(AcAp.GetSystemVariable("CVPORT"));
                if (activeViewportNumber <= 1)
                {
                    ed.WriteMessage("\nSURVEY-SYNC-REVIEW: Double-click inside the source review viewport first, then run the sync command.");
                    return;
                }

                ViewTableRecord sourceView = ed.GetCurrentView();
                int matchedCount = 0;

                using Transaction tr = db.TransactionManager.StartTransaction();
                LayoutManager layoutManager = LayoutManager.Current;
                Layout layout = (Layout)tr.GetObject(layoutManager.GetLayoutId(layoutManager.CurrentLayout), OpenMode.ForRead);
                BlockTableRecord paperSpace = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);

                foreach (ObjectId id in paperSpace)
                {
                    if (!id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(Viewport))))
                        continue;

                    Viewport viewport = (Viewport)tr.GetObject(id, OpenMode.ForWrite);
                    if (viewport.Number <= 1 || viewport.Number == activeViewportNumber)
                        continue;

                    viewport.ViewCenter = sourceView.CenterPoint;
                    viewport.ViewHeight = sourceView.Height;
                    viewport.ViewTarget = sourceView.Target;
                    viewport.ViewDirection = sourceView.ViewDirection;
                    viewport.TwistAngle = sourceView.ViewTwist;
                    viewport.CustomScale = GetActiveViewportScaleOrDefault(db, activeViewportNumber, tr, sourceView.Height, viewport.Height);
                    matchedCount++;
                }

                tr.Commit();
                ed.Regen();

                if (matchedCount == 0)
                    ed.WriteMessage("\nSURVEY-SYNC-REVIEW: Could not find another review viewport to sync.");
                else
                    ed.WriteMessage($"\nSURVEY-SYNC-REVIEW: Synced {matchedCount} other viewport to the active viewport view.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nSURVEY-SYNC-REVIEW failed: {ex.Message}");
            }
        }

        private static double GetActiveViewportScaleOrDefault(Database db, short activeViewportNumber, Transaction tr, double activeViewHeight, double paperViewportHeight)
        {
            if (paperViewportHeight <= 0.0 || activeViewHeight <= 0.0)
                return 1.0;

            LayoutManager layoutManager = LayoutManager.Current;
            Layout layout = (Layout)tr.GetObject(layoutManager.GetLayoutId(layoutManager.CurrentLayout), OpenMode.ForRead);
            BlockTableRecord paperSpace = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);

            foreach (ObjectId id in paperSpace)
            {
                if (!id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(Viewport))))
                    continue;

                Viewport viewport = (Viewport)tr.GetObject(id, OpenMode.ForRead);
                if (viewport.Number == activeViewportNumber && viewport.CustomScale > 0.0)
                    return viewport.CustomScale;
            }

            return paperViewportHeight / activeViewHeight;
        }

        private static void FreezeSingleLayerInViewport(Viewport viewport, ObjectId layerId)
        {
            var layerIds = new ObjectIdCollection();
            layerIds.Add(layerId);
            viewport.FreezeLayersInViewport(layerIds.GetEnumerator());
        }

        private static bool LayoutExists(Database db, Transaction tr, string layoutName)
        {
            DBDictionary layouts = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
            return layouts.Contains(layoutName);
        }

        private static void EraseExistingFloatingViewports(Transaction tr, BlockTableRecord paperSpace)
        {
            foreach (ObjectId id in paperSpace)
            {
                if (!id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(Viewport))))
                    continue;

                Viewport viewport = (Viewport)tr.GetObject(id, OpenMode.ForWrite);
                if (viewport.Number > 1 || viewport.Number == 0)
                    viewport.Erase();
            }
        }

        private static Viewport CreateFloatingViewport(
            BlockTableRecord paperSpace,
            Transaction tr,
            Point3d center,
            double width,
            double height,
            ViewTableRecord sourceView)
        {
            Viewport viewport = new Viewport
            {
                CenterPoint = center,
                Width = width,
                Height = height,
                ViewCenter = sourceView.CenterPoint,
                ViewHeight = sourceView.Height,
                ViewTarget = sourceView.Target,
                ViewDirection = sourceView.ViewDirection,
                TwistAngle = sourceView.ViewTwist,
                Locked = false
            };

            paperSpace.AppendEntity(viewport);
            tr.AddNewlyCreatedDBObject(viewport, true);
            viewport.On = true;
            return viewport;
        }
    }
}
