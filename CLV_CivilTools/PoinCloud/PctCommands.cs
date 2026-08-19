using System;
using System.Collections.Generic;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;

// Shared helpers (SnapState, ViewState, LayerState, SelectionUtils, GeometryUtils, PointCloudUtils, PipeCatalog)
using CLV_CivilTools.Shared;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;
using AcPolyline = Autodesk.AutoCAD.DatabaseServices.Polyline;
using CogoPoint = Autodesk.Civil.DatabaseServices.CogoPoint;
using AcColor = Autodesk.AutoCAD.Colors.Color;

namespace CLV_CivilTools.PointCloud
{
    public static class PctCommands
    {
        // ------------------------------------------------------------
        // Constants / Layers / Views
        // ------------------------------------------------------------

        // View names
        private const string VIEW_NAME_SL = "CROSS-SECTION_SL";             // PCT2
        private const string VIEW_NAME_RV = "CROSS-SECTION_RV";             // PCT3
        private const string VIEW_NAME_QV = "CROSS-SECTION_QV";             // PCT4
        private const string VIEW_NAME_MH = "CROSS-SECTION_MH";             // Manhole workflow

        private const string VIEW_NAME_RW_ORBIT = "ROADWAY_ORBIT_VIEW";     // PCT18 baseline
        private const string VIEW_NAME_RW_ORBIT_CLEAR = "ROADWAY_ORBIT_VIEW_CLEAR"; // PCT18R switch-to view so we can delete baseline

        // Permanent layers
        private const string LYR_CROP = "V-PNTC-CROP";
        private const string LYR_CROS = "V-PNTC-CROS";
        private const string LYR_SAMP = "V-PNTC-SAMP";
        private const string LYR_PLNM = "V-PNTC-PLNM";

        // TEMP layers
        private const string LYR_CROS_TEMP = "V-PNTC-CROS-TEMP";
        private const string LYR_CROP_TEMP = "V-PNTC-CROP-TEMP";

        // ------------------------------------------------------------
        // PCT1 – Sample Lines
        // ------------------------------------------------------------

        [CommandMethod("PCT", "PCT1", CommandFlags.Modal)]
        public static void PCT1()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                LayerState.EnsureLayer(LYR_CROP);
                LayerState.EnsureLayer(LYR_CROS);
                LayerState.EnsureLayer(LYR_SAMP);

                var peo = new PromptEntityOptions("\nSelect centerline polyline/curve: ");
                peo.SetRejectMessage("\nMust be a curve/polyline.");
                peo.AddAllowedClass(typeof(Curve), exactMatch: false);

                var per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nPCT1: Cancelled.");
                    return;
                }

                var pInterval = new PromptDoubleOptions("\nInterval along centerline <10.0>: ")
                {
                    AllowNegative = false,
                    AllowZero = false,
                    DefaultValue = 10.0
                };
                var rInterval = ed.GetDouble(pInterval);
                if (rInterval.Status != PromptStatus.OK) return;
                double intv = rInterval.Value;

                var pHalfLen = new PromptDoubleOptions("\nHalf-length of sample line <10.0>: ")
                {
                    AllowNegative = false,
                    AllowZero = false,
                    DefaultValue = 10.0
                };
                var rHalfLen = ed.GetDouble(pHalfLen);
                if (rHalfLen.Status != PromptStatus.OK) return;
                double halfLen = rHalfLen.Value;

                var pHalfWid = new PromptDoubleOptions("\nHalf-width of rectangle <0.5>: ")
                {
                    AllowNegative = false,
                    AllowZero = false,
                    DefaultValue = 0.5
                };
                var rHalfWid = ed.GetDouble(pHalfWid);
                if (rHalfWid.Status != PromptStatus.OK) return;
                double halfWid = rHalfWid.Value;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var curve = tr.GetObject(per.ObjectId, OpenMode.ForRead) as Curve;
                    if (curve == null)
                    {
                        ed.WriteMessage("\nPCT1: Selected entity is not a curve.");
                        return;
                    }

                    if (curve is Entity entCurve)
                    {
                        entCurve.UpgradeOpen();
                        entCurve.Layer = LYR_SAMP;
                    }

                    double startParam = curve.StartParam;
                    double endParam = curve.EndParam;

                    double startDist = curve.GetDistanceAtParameter(startParam);
                    double endDist = curve.GetDistanceAtParameter(endParam);
                    double lenCL = endDist - startDist;

                    if (lenCL < 1e-6)
                    {
                        ed.WriteMessage("\nPCT1: Centerline length too small.");
                        return;
                    }

                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    int created = 0;

                    for (double dist = 0.0; dist <= lenCL + 1e-6; dist += intv)
                    {
                        double dAbs = startDist + dist;
                        double param = curve.GetParameterAtDistance(dAbs);

                        Point3d pt = curve.GetPointAtParameter(param);
                        Vector3d tanv = GeometryUtils.SafeTangentXY(curve.GetFirstDerivative(param));
                        Vector3d perpU = GeometryUtils.PerpCCW(tanv).GetNormal();

                        Point3d s1 = pt + (perpU * halfLen);
                        Point3d s2 = pt - (perpU * halfLen);

                        var ln = GeometryUtils.MakeLine(
                            new Point3d(s1.X, s1.Y, 0.0),
                            new Point3d(s2.X, s2.Y, 0.0),
                            LYR_CROS);

                        ms.AppendEntity(ln);
                        tr.AddNewlyCreatedDBObject(ln, true);

                        Point3d r1 = s1 + (tanv * halfWid);
                        Point3d r2 = s2 + (tanv * halfWid);
                        Point3d r3 = s2 - (tanv * halfWid);
                        Point3d r4 = s1 - (tanv * halfWid);

                        var rect = GeometryUtils.MakeRectFromCorners(
                            new Point3d(r1.X, r1.Y, 0.0),
                            new Point3d(r2.X, r2.Y, 0.0),
                            new Point3d(r3.X, r3.Y, 0.0),
                            new Point3d(r4.X, r4.Y, 0.0),
                            LYR_CROP);

                        ms.AppendEntity(rect);
                        tr.AddNewlyCreatedDBObject(rect, true);

                        created++;
                    }

                    tr.Commit();
                    ed.WriteMessage($"\nPCT1: Created {created} sample lines + rectangles. (Centerline moved to {LYR_SAMP})");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPCT1 error: {ex.Message}");
            }
        }

        // ------------------------------------------------------------
        // PCT2 – Sample Line Crop (save view CROSS-SECTION_SL)
        // ------------------------------------------------------------

        [CommandMethod("PCT", "PCT2", CommandFlags.Modal)]
        public static void PCT2()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                ViewState.SaveView(VIEW_NAME_SL);

                var pso = new PromptSelectionOptions
                {
                    MessageForAdding = $"\nSelect crop boundary polyline on layer {LYR_CROP}: "
                };

                PromptSelectionResult selRes = ed.GetSelection(pso);
                if (selRes.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nPCT2: Nothing selected.");
                    return;
                }

                ObjectId rectId = ObjectId.Null;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in selRes.Value.GetObjectIds())
                    {
                        var obj = tr.GetObject(id, OpenMode.ForRead) as AcEntity;
                        if (obj == null) continue;

                        string lay = (obj.Layer ?? "").ToUpperInvariant();
                        if (lay == LYR_CROP.ToUpperInvariant())
                        {
                            rectId = id;
                            break;
                        }
                    }
                    tr.Commit();
                }

                if (rectId.IsNull)
                {
                    ed.WriteMessage($"\nPCT2: No polyline found on layer {LYR_CROP} in selection.");
                    return;
                }

                if (!PointCloudUtils.TryGetPolylineVerticesWcsPlan(rectId, db, out var polyPtsPlan))
                {
                    ed.WriteMessage("\nPCT2: Crop boundary must be a polyline with 3+ vertices.");
                    return;
                }

                ObjectId cloudId = PointCloudUtils.FindSinglePointCloudInModelSpace(db);
                if (cloudId.IsNull)
                {
                    cloudId = PointCloudUtils.PromptForPointCloud(ed, db, "\nMultiple/no point clouds detected — pick point cloud to crop: ");
                    if (cloudId.IsNull)
                    {
                        ed.WriteMessage("\nPCT2: No point cloud selected.");
                        return;
                    }
                }

                PointCloudUtils.UncropPointCloudById(ed, cloudId);
                PointCloudUtils.CropPointCloudPolygonById(ed, cloudId, polyPtsPlan);

                ed.WriteMessage($"\nPCT2: View saved as {VIEW_NAME_SL}, cloud polygon-cropped to {LYR_CROP} boundary.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPCT2 error: {ex.Message}");
            }
        }

        // ------------------------------------------------------------
        // PCT2R – Sample Line Uncrop (restore CROSS-SECTION_SL)
        // ------------------------------------------------------------

        [CommandMethod("PCT", "PCT2R", CommandFlags.Modal)]
        public static void PCT2R()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                if (!ViewState.RestoreView(VIEW_NAME_SL))
                {
                    ed.WriteMessage($"\nPCT2R: View {VIEW_NAME_SL} not found (PCT2 not run this session).");
                }

                ObjectId cloudId = PointCloudUtils.FindSinglePointCloudInModelSpace(db);
                if (cloudId.IsNull)
                {
                    cloudId = PointCloudUtils.PromptForPointCloud(ed, db, "\nPick cloud to UN-CROP: ");
                    if (cloudId.IsNull)
                    {
                        ed.WriteMessage("\nPCT2R: Cancelled.");
                        return;
                    }
                }

                PointCloudUtils.UncropPointCloudById(ed, cloudId);
                ed.WriteMessage("\nPCT2R: Cloud uncropped, view restored if available.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPCT2R error: {ex.Message}");
            }
        }

        // ------------------------------------------------------------
        // PCT3 – Rotate View (save CROSS-SECTION_RV)
        // ------------------------------------------------------------

        [CommandMethod("PCT", "PCT3", CommandFlags.Modal)]
        public static void PCT3()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                using (var snap = SnapState.Capture())
                {
                    snap.Set(osnapZ: 0);

                    ViewState.SaveView(VIEW_NAME_RV);

                    LayerState.SetLayerOff(LYR_PLNM, true);

                    var pso = new PromptSelectionOptions
                    {
                        MessageForAdding = "\nWindow select strip objects (cloud, rectangle, cross line, COGO points): "
                    };

                    PromptSelectionResult selRes = ed.GetSelection(pso);
                    if (selRes.Status != PromptStatus.OK)
                    {
                        ed.WriteMessage("\nPCT3: Nothing selected.");
                        return;
                    }

                    ObjectId cloudId = ObjectId.Null;
                    ObjectId rectId = ObjectId.Null;
                    ObjectId crossId = ObjectId.Null;
                    var cogoIds = new List<ObjectId>();

                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        foreach (ObjectId id in selRes.Value.GetObjectIds())
                        {
                            var obj = tr.GetObject(id, OpenMode.ForRead);

                            if (obj is CogoPoint)
                            {
                                cogoIds.Add(id);
                                continue;
                            }

                            if (obj is AcEntity ent)
                            {
                                string lay = (ent.Layer ?? "");

                                if (cloudId.IsNull && PointCloudUtils.IsPointCloudEntity(ent))
                                    cloudId = id;

                                if (rectId.IsNull && LayerState.IsLayerOneOf(lay, LYR_CROP, LYR_CROP_TEMP))
                                    rectId = id;

                                if (crossId.IsNull && LayerState.IsLayerOneOf(lay, LYR_CROS, LYR_CROS_TEMP))
                                    crossId = id;
                            }
                        }

                        tr.Commit();
                    }

                    if (cloudId.IsNull)
                    {
                        var peo = new PromptEntityOptions("\nPoint cloud not captured by window — pick point cloud: ");
                        var per = ed.GetEntity(peo);
                        if (per.Status != PromptStatus.OK)
                        {
                            ed.WriteMessage("\nPCT3: No point cloud selected.");
                            return;
                        }

                        using (var tr = db.TransactionManager.StartTransaction())
                        {
                            var ent = tr.GetObject(per.ObjectId, OpenMode.ForRead) as AcEntity;
                            if (ent == null || !PointCloudUtils.IsPointCloudEntity(ent))
                            {
                                ed.WriteMessage("\nPCT3: Selected object is not a point cloud.");
                                return;
                            }
                            cloudId = per.ObjectId;
                            tr.Commit();
                        }
                    }

                    if (rectId.IsNull)
                    {
                        ed.WriteMessage($"\nPCT3: Rectangle not found. Select a boundary on {LYR_CROP} or {LYR_CROP_TEMP}.");
                        return;
                    }

                    var iso = new List<ObjectId> { cloudId, rectId };
                    if (!crossId.IsNull) iso.Add(crossId);
                    iso.AddRange(cogoIds);

                    SelectionUtils.IsolateSelection(ed, iso.ToArray());

                    var ppr = ed.GetPoint("\nPick elevation reference point (snap to cloud node or any Z ref): ");
                    if (ppr.Status != PromptStatus.OK)
                    {
                        ed.WriteMessage("\nPCT3: No elevation reference selected.");
                        return;
                    }

                    Point3d elevPick = ppr.Value;
                    double zElev = elevPick.Z;

                    Vector3d dir;
                    Point3d p1;
                    double longSide, shortSide;

                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var rectEnt = (AcEntity)tr.GetObject(rectId, OpenMode.ForRead);

                        if (!GeometryUtils.TryGetRectangleDirection(rectEnt, out dir, out p1, out longSide, out shortSide))
                        {
                            ed.WriteMessage("\nPCT3: Rectangle must be a Polyline rectangle (preferably Closed).");
                            return;
                        }

                        tr.Commit();
                    }

                    var origin = new Point3d(p1.X, p1.Y, zElev);
                    ViewState.SetCrossSectionUcs(origin, dir);
                    ViewState.PlanCurrentUcs(ed);

                    LayerState.SetLayerOff(LYR_SAMP, true);
                    LayerState.SetLayerOff(LYR_CROP, true);
                    LayerState.SetLayerOff(LYR_CROP_TEMP, true);

                    bool zoomOk = false;

                    try
                    {
                        if (!cloudId.IsNull)
                        {
                            var ss = SelectionSet.FromObjectIds(new[] { cloudId });
                            ed.Command("._ZOOM", "_Object", ss, "");
                            zoomOk = true;
                        }
                    }
                    catch { zoomOk = false; }

                    if (!zoomOk)
                    {
                        ViewState.ZoomCenterByRect(elevPick, longSide, shortSide);
                    }

                    try
                    {
                        ed.Command("._UCS", "", "");
                    }
                    catch (System.Exception exUcs)
                    {
                        ed.WriteMessage($"\nPCT3 warning: Failed to reset UCS: {exUcs.Message}");
                    }

                    ed.WriteMessage(
                        $"\nPCT3: Cross-section view set (view saved {VIEW_NAME_RV}). " +
                        $"Center Z={zElev:0.###}.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPCT3 error: {ex.Message}");
            }
        }

        // ------------------------------------------------------------
        // PCT3R – Reset View (restore CROSS-SECTION_RV)
        // ------------------------------------------------------------

        [CommandMethod("PCT", "PCT3R", CommandFlags.Modal)]
        public static void PCT3R()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            try
            {
                if (!ViewState.RestoreView(VIEW_NAME_RV))
                {
                    ed.WriteMessage($"\nPCT3R: Saved view {VIEW_NAME_RV} not found. Run PCT3 first.");
                    return;
                }

                SelectionUtils.Unisolate(ed);

                LayerState.SetLayerOff(LYR_SAMP, false);
                LayerState.SetLayerOff(LYR_CROP, false);
                LayerState.SetLayerOff(LYR_CROP_TEMP, false);
                LayerState.SetLayerOff(LYR_PLNM, false);

                ViewState.SetUcsWorld(ed);

                ed.WriteMessage("\nPCT3R: View restored, objects unisolated, layers restored, UCS reset.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPCT3R error: {ex.Message}");
            }
        }

        // ------------------------------------------------------------
        // PCT4 – Quick Section (two-point TEMP crop)
        // ------------------------------------------------------------

        [CommandMethod("PCT", "PCT4", CommandFlags.Modal)]
        public static void PCT4()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                LayerState.EnsureLayer(LYR_CROS_TEMP);
                LayerState.EnsureLayer(LYR_CROP_TEMP);

                ViewState.SaveView(VIEW_NAME_QV);

                var pWidth = new PromptDoubleOptions("\nPCT4: Full section width <1.0>: ")
                {
                    AllowNegative = false,
                    AllowZero = false,
                    DefaultValue = 1.0
                };
                var rWidth = ed.GetDouble(pWidth);
                if (rWidth.Status != PromptStatus.OK) return;

                double fullWidth = rWidth.Value;
                if (fullWidth < 1e-6) fullWidth = 1.0;
                double halfWid = fullWidth * 0.5;

                var p1r = ed.GetPoint("\nPCT4: Pick first point for cross-section line: ");
                if (p1r.Status != PromptStatus.OK) return;

                var p2opts = new PromptPointOptions("\nPCT4: Pick second point: ")
                {
                    BasePoint = p1r.Value,
                    UseBasePoint = true
                };
                var p2r = ed.GetPoint(p2opts);
                if (p2r.Status != PromptStatus.OK) return;

                Point3d p1 = new Point3d(p1r.Value.X, p1r.Value.Y, 0.0);
                Point3d p2 = new Point3d(p2r.Value.X, p2r.Value.Y, 0.0);

                Vector3d u = p2 - p1;
                if (u.Length < 1e-6)
                {
                    ed.WriteMessage("\nPCT4: Points are too close together.");
                    return;
                }

                Vector3d crossDir = GeometryUtils.SafeTangentXY(u);
                Vector3d tanDir = new Vector3d(crossDir.Y, -crossDir.X, 0.0).GetNormal();

                Point3d r1 = p1 + (tanDir * halfWid);
                Point3d r2 = p2 + (tanDir * halfWid);
                Point3d r3 = p2 - (tanDir * halfWid);
                Point3d r4 = p1 - (tanDir * halfWid);

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    var ln = GeometryUtils.MakeLine(p1, p2, LYR_CROS_TEMP);
                    ms.AppendEntity(ln);
                    tr.AddNewlyCreatedDBObject(ln, true);

                    var rect = GeometryUtils.MakeRectFromCorners(r1, r2, r3, r4, LYR_CROP_TEMP);
                    ms.AppendEntity(rect);
                    tr.AddNewlyCreatedDBObject(rect, true);

                    tr.Commit();
                }

                ObjectId cloudId = PointCloudUtils.FindSinglePointCloudInModelSpace(db);
                if (cloudId.IsNull)
                {
                    cloudId = PointCloudUtils.PromptForPointCloud(ed, db, "\nMultiple/no point clouds detected — pick point cloud to crop: ");
                    if (cloudId.IsNull)
                    {
                        ed.WriteMessage("\nPCT4: No point cloud selected.");
                        return;
                    }
                }

                PointCloudUtils.UncropPointCloudById(ed, cloudId);

                var polyPtsPlan = new List<Point3d>
                {
                    new Point3d(r1.X, r1.Y, 0.0),
                    new Point3d(r2.X, r2.Y, 0.0),
                    new Point3d(r3.X, r3.Y, 0.0),
                    new Point3d(r4.X, r4.Y, 0.0)
                };

                PointCloudUtils.CropPointCloudPolygonById(ed, cloudId, polyPtsPlan);

                ed.WriteMessage($"\nPCT4: TEMP line+boundary created, cloud cropped, view saved as {VIEW_NAME_QV}. Run PCT3 or PCT4R as needed.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPCT4 error: {ex.Message}");
            }
        }

        // ------------------------------------------------------------
        // PCT4R – Quick Section Reset
        // ------------------------------------------------------------

        [CommandMethod("PCT", "PCT4R", CommandFlags.Modal)]
        public static void PCT4R()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                SelectionUtils.Unisolate(ed);

                if (!ViewState.RestoreView(VIEW_NAME_QV))
                {
                    ViewState.SetUcsWorld(ed);
                    ViewState.PlanWorld(ed);
                }

                LayerState.SetLayerOff(LYR_PLNM, false);
                LayerState.SetLayerOff(LYR_SAMP, false);
                LayerState.SetLayerOff(LYR_CROP, false);
                LayerState.SetLayerOff(LYR_CROS, false);
                LayerState.SetLayerOff(LYR_CROP_TEMP, false);
                LayerState.SetLayerOff(LYR_CROS_TEMP, false);

                ViewState.SetUcsWorld(ed);

                ObjectId cloudId = PointCloudUtils.FindSinglePointCloudInModelSpace(db);
                if (cloudId.IsNull)
                {
                    cloudId = PointCloudUtils.PromptForPointCloud(ed, db, "\nMultiple/no point clouds detected — pick point cloud to UN-CROP: ");
                }

                if (!cloudId.IsNull)
                    PointCloudUtils.UncropPointCloudById(ed, cloudId);

                LayerState.DeleteEntitiesOnLayer(db, LYR_CROS_TEMP);
                LayerState.DeleteEntitiesOnLayer(db, LYR_CROP_TEMP);

                ed.WriteMessage("\nPCT4R: Quick view reset, cloud uncropped, TEMP geometry removed.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPCT4R error: {ex.Message}");
            }
        }

        // ------------------------------------------------------------
        // PCT5 – Align COGO points to section line (XY) keeping Z
        // ------------------------------------------------------------

        [CommandMethod("PCT", "PCT5", CommandFlags.Modal)]
        public static void PCT5()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                var peo = new PromptEntityOptions("\nPCT5: Select cross-section line (LINE or Polyline): ");
                peo.SetRejectMessage("\nMust be LINE or Polyline.");
                peo.AddAllowedClass(typeof(Line), false);
                peo.AddAllowedClass(typeof(AcPolyline), false);

                var per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nPCT5: Cancelled.");
                    return;
                }

                ObjectId lineId = per.ObjectId;

                var pso = new PromptSelectionOptions
                {
                    MessageForAdding = "\nSelect COGO points to align to section line: "
                };
                pso.RejectObjectsOnLockedLayers = true;

                var filter = new SelectionFilter(new[]
                {
                    new TypedValue((int)DxfCode.Start, "AECC_COGO_POINT")
                });

                var psr = ed.GetSelection(pso, filter);
                if (psr.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nPCT5: No COGO points selected.");
                    return;
                }

                int moved = 0;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    if (tr.GetObject(lineId, OpenMode.ForRead) is not Curve curve)
                    {
                        ed.WriteMessage("\nPCT5: Selected object is not a curve.");
                        return;
                    }

                    foreach (ObjectId id in psr.Value.GetObjectIds())
                    {
                        if (tr.GetObject(id, OpenMode.ForWrite, false) is not CogoPoint cp)
                            continue;

                        Point3d loc = cp.Location;
                        Point3d closest = curve.GetClosestPointTo(
                            new Point3d(loc.X, loc.Y, curve.StartPoint.Z),
                            extend: false);

                        cp.Easting = closest.X;
                        cp.Northing = closest.Y;

                        moved++;
                    }

                    tr.Commit();
                }

                ed.WriteMessage($"\nPCT5: Aligned {moved} COGO points to section line (XY only).");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPCT5 error: {ex.Message}");
            }
        }

        // ------------------------------------------------------------
        // PCT6 – Snap COGO points to nearest polyline + add vertex
        // ------------------------------------------------------------

        [CommandMethod("PCT", "PCT6", CommandFlags.Modal)]
        public static void PCT6()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                var psoPl = new PromptSelectionOptions
                {
                    MessageForAdding = "\nPCT6: Select 2D polylines to snap to (curb, walk, etc.): "
                };
                psoPl.RejectObjectsOnLockedLayers = true;

                var plFilter = new SelectionFilter(new[]
                {
                    new TypedValue((int)DxfCode.Start, "LWPOLYLINE")
                });

                var plRes = ed.GetSelection(psoPl, plFilter);
                if (plRes.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nPCT6: No polylines selected.");
                    return;
                }

                var psoPt = new PromptSelectionOptions
                {
                    MessageForAdding = "\nSelect COGO points to snap to polylines: "
                };
                psoPt.RejectObjectsOnLockedLayers = true;

                var ptFilter = new SelectionFilter(new[]
                {
                    new TypedValue((int)DxfCode.Start, "AECC_COGO_POINT")
                });

                var ptRes = ed.GetSelection(psoPt, ptFilter);
                if (ptRes.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nPCT6: No COGO points selected.");
                    return;
                }

                int moved = 0;
                int addedVerts = 0;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var polylines = new List<AcPolyline>();
                    foreach (ObjectId id in plRes.Value.GetObjectIds())
                    {
                        if (tr.GetObject(id, OpenMode.ForWrite, false) is AcPolyline pl)
                            polylines.Add(pl);
                    }

                    if (polylines.Count == 0)
                    {
                        ed.WriteMessage("\nPCT6: No editable polylines found.");
                        return;
                    }

                    foreach (ObjectId cpId in ptRes.Value.GetObjectIds())
                    {
                        if (tr.GetObject(cpId, OpenMode.ForWrite, false) is not CogoPoint cp)
                            continue;

                        Point3d loc = cp.Location;
                        Point3d bestProj = Point3d.Origin;
                        int bestSegIndex = -1;
                        AcPolyline? bestPl = null;
                        double bestDist2 = double.MaxValue;

                        foreach (var pl in polylines)
                        {
                            if (!GeometryUtils.TryProjectPointToPolylineXY(pl, loc, out var proj, out int segIdx))
                                continue;

                            double d2 = (proj.X - loc.X) * (proj.X - loc.X)
                                      + (proj.Y - loc.Y) * (proj.Y - loc.Y);

                            if (d2 < bestDist2)
                            {
                                bestDist2 = d2;
                                bestProj = proj;
                                bestSegIndex = segIdx;
                                bestPl = pl;
                            }
                        }

                        if (bestPl is null || bestSegIndex < 0)
                            continue;

                        cp.Easting = bestProj.X;
                        cp.Northing = bestProj.Y;
                        moved++;

                        bool needVertex = true;
                        double tol2 = 0.0001;

                        int vCount = bestPl.NumberOfVertices;
                        for (int i = 0; i < vCount; i++)
                        {
                            Point3d v = bestPl.GetPoint3dAt(i);
                            double d2v = (v.X - bestProj.X) * (v.X - bestProj.X)
                                       + (v.Y - bestProj.Y) * (v.Y - bestProj.Y);
                            if (d2v < tol2)
                            {
                                needVertex = false;
                                break;
                            }
                        }

                        if (needVertex)
                        {
                            var p2d = new Point2d(bestProj.X, bestProj.Y);
                            int insertIndex = bestSegIndex + 1;
                            if (insertIndex > bestPl.NumberOfVertices) insertIndex = bestPl.NumberOfVertices;

                            bestPl.AddVertexAt(insertIndex, p2d, 0, 0, 0);
                            addedVerts++;
                        }
                    }

                    tr.Commit();
                }

                ed.WriteMessage($"\nPCT6: Moved {moved} COGO points to nearest polylines and added {addedVerts} new vertices.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPCT6 error: {ex.Message}");
            }
        }

        // ------------------------------------------------------------
        // PCT7 – Visual style: WIREFRAME (clouds visible)
        // ------------------------------------------------------------

        [CommandMethod("PCT", "PCT7", CommandFlags.Modal)]
        public static void PCT7()
        {
            var ed = AcadApp.DocumentManager.MdiActiveDocument.Editor;
            try { ed.Command("._VSCURRENT", "WIREFRAME"); }
            catch (System.Exception ex) { ed.WriteMessage($"\nPCT7 error: {ex.Message}"); }
        }

        // ------------------------------------------------------------
        // PCT8 – Visual style: 2DWIREFRAME (clouds off)
        // ------------------------------------------------------------

        [CommandMethod("PCT", "PCT8", CommandFlags.Modal)]
        public static void PCT8()
        {
            var ed = AcadApp.DocumentManager.MdiActiveDocument.Editor;
            try { ed.Command("._VSCURRENT", "2DWIREFRAME"); }
            catch (System.Exception ex) { ed.WriteMessage($"\nPCT8 error: {ex.Message}"); }
        }

        // ------------------------------------------------------------
        // PCT9 – Move COGO points to nearest vertex
        // ------------------------------------------------------------

        [CommandMethod("PCT", "PCT9", CommandFlags.Modal)]
        public static void PCT9()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                var peo = new PromptEntityOptions("\nSelect target polyline: ");
                peo.SetRejectMessage("\nMust be a polyline.");
                peo.AddAllowedClass(typeof(AcPolyline), exactMatch: false);

                var per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nPCT9: Cancelled.");
                    return;
                }

                var pso = new PromptSelectionOptions { MessageForAdding = "\nSelect COGO points to move: " };
                var psr = ed.GetSelection(pso);
                if (psr.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nPCT9: No points selected.");
                    return;
                }

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var plEnt = tr.GetObject(per.ObjectId, OpenMode.ForRead) as AcPolyline;
                    if (plEnt == null)
                    {
                        ed.WriteMessage("\nPCT9: Selected entity is not a polyline.");
                        return;
                    }

                    int n = plEnt.NumberOfVertices;
                    if (n < 1)
                    {
                        ed.WriteMessage("\nPCT9: Polyline has no vertices.");
                        return;
                    }

                    var verts = new List<Point3d>(n);
                    for (int i = 0; i < n; i++) verts.Add(plEnt.GetPoint3dAt(i));

                    int moved = 0;

                    foreach (ObjectId id in psr.Value.GetObjectIds())
                    {
                        var cp = tr.GetObject(id, OpenMode.ForWrite) as CogoPoint;
                        if (cp == null) continue;

                        Point3d loc = cp.Location;
                        double bestDist2 = double.MaxValue;
                        Point3d bestVert = loc;

                        foreach (var v in verts)
                        {
                            double dx = v.X - loc.X;
                            double dy = v.Y - loc.Y;
                            double d2 = dx * dx + dy * dy;
                            if (d2 < bestDist2)
                            {
                                bestDist2 = d2;
                                bestVert = v;
                            }
                        }

                        cp.Easting = bestVert.X;
                        cp.Northing = bestVert.Y;
                        moved++;
                    }

                    tr.Commit();
                    ed.WriteMessage($"\nPCT9: Moved {moved} COGO point(s) to nearest vertex.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPCT9 error: {ex.Message}");
            }
        }

        // ------------------------------------------------------------
        // PCT10 – Polyline Vertex Markers + optional perp lines
        // ------------------------------------------------------------

        [CommandMethod("PCT", "PCT10", CommandFlags.Modal)]
        public static void PCT10()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            const string MarkerLayer = "V-CONS-LINE-TEMP";

            try
            {
                var pRad = new PromptDoubleOptions("\nPCT10: Marker radius <0.05>: ")
                {
                    AllowNegative = false,
                    AllowZero = false,
                    DefaultValue = 0.05
                };
                var rRad = ed.GetDouble(pRad);
                if (rRad.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nPCT10: Cancelled.");
                    return;
                }
                double radius = rRad.Value;

                bool addLines = false;
                double lineLength = 0.0;

                var pLines = new PromptKeywordOptions(
                    "\nPCT10: Also draw perpendicular line at each vertex? [Yes/No] <No>: ")
                { AllowNone = true };
                pLines.Keywords.Add("Yes");
                pLines.Keywords.Add("No");
                pLines.Keywords.Default = "No";

                var rLines = ed.GetKeywords(pLines);
                if (rLines.Status == PromptStatus.OK &&
                    string.Equals(rLines.StringResult, "Yes", StringComparison.OrdinalIgnoreCase))
                {
                    addLines = true;

                    var pLen = new PromptDoubleOptions("\nPCT10: Perpendicular line length <0.50>: ")
                    {
                        AllowNegative = false,
                        AllowZero = false,
                        DefaultValue = 0.50
                    };
                    var rLen = ed.GetDouble(pLen);
                    if (rLen.Status != PromptStatus.OK)
                    {
                        ed.WriteMessage("\nPCT10: Cancelled.");
                        return;
                    }
                    lineLength = rLen.Value;
                }

                var peo = new PromptEntityOptions("\nPCT10: Select polyline for vertex markers: ");
                peo.SetRejectMessage("\nMust be a polyline.");
                peo.AddAllowedClass(typeof(AcPolyline), exactMatch: false);

                var per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nPCT10: Cancelled.");
                    return;
                }

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var plEnt = tr.GetObject(per.ObjectId, OpenMode.ForRead) as AcPolyline;
                    if (plEnt == null)
                    {
                        ed.WriteMessage("\nPCT10: Selected entity is not a polyline.");
                        return;
                    }

                    LayerState.EnsureLayer(MarkerLayer);

                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    int n = plEnt.NumberOfVertices;
                    if (n < 1)
                    {
                        ed.WriteMessage("\nPCT10: Polyline has no vertices.");
                        return;
                    }

                    int circlesCreated = 0;
                    int linesCreated = 0;
                    double halfLen = lineLength * 0.5;

                    Vector3d GetVertexTangentHelper(int i)
                    {
                        int last = n - 1;

                        if (!plEnt.Closed)
                        {
                            if (i == 0)
                            {
                                var p0 = plEnt.GetPoint3dAt(0);
                                var p1 = plEnt.GetPoint3dAt(1);
                                return (p1 - p0);
                            }
                            if (i == last)
                            {
                                var pPrev = plEnt.GetPoint3dAt(last - 1);
                                var pCur = plEnt.GetPoint3dAt(last);
                                return (pCur - pPrev);
                            }
                            else
                            {
                                var pPrev = plEnt.GetPoint3dAt(i - 1);
                                var pNext = plEnt.GetPoint3dAt(i + 1);
                                return (pNext - pPrev);
                            }
                        }
                        else
                        {
                            int prev = (i == 0) ? last : i - 1;
                            int next = (i == last) ? 0 : i + 1;

                            var pPrev = plEnt.GetPoint3dAt(prev);
                            var pNext = plEnt.GetPoint3dAt(next);
                            return (pNext - pPrev);
                        }
                    }

                    for (int i = 0; i < n; i++)
                    {
                        Point3d v = plEnt.GetPoint3dAt(i);

                        var c = new Circle(v, Vector3d.ZAxis, radius) { Layer = MarkerLayer };
                        ms.AppendEntity(c);
                        tr.AddNewlyCreatedDBObject(c, true);
                        circlesCreated++;

                        if (addLines)
                        {
                            Vector3d tan = GetVertexTangentHelper(i);
                            if (tan.Length > 1e-8)
                            {
                                tan = new Vector3d(tan.X, tan.Y, 0.0).GetNormal();
                                Vector3d perp = new Vector3d(-tan.Y, tan.X, 0.0).GetNormal();

                                Point3d p1 = v + perp * halfLen;
                                Point3d p2 = v - perp * halfLen;

                                var ln = new Line(p1, p2) { Layer = MarkerLayer };
                                ms.AppendEntity(ln);
                                tr.AddNewlyCreatedDBObject(ln, true);
                                linesCreated++;
                            }
                        }
                    }

                    tr.Commit();

                    if (addLines)
                        ed.WriteMessage($"\nPCT10: Created {circlesCreated} marker(s) and {linesCreated} perp line(s) on {MarkerLayer}.");
                    else
                        ed.WriteMessage($"\nPCT10: Created {circlesCreated} marker(s) on {MarkerLayer}.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPCT10 error: {ex.Message}");
            }
        }

        // ------------------------------------------------------------
        // PCT11 – Attach Point Cloud (IP LISP replacement)
        // ------------------------------------------------------------

        [CommandMethod("PCT", "PCT11", CommandFlags.Modal)]
        public static void PCT11()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            short? originalInsUnits = null;

            try
            {
                try
                {
                    var val = AcadApp.GetSystemVariable("INSUNITS");
                    if (val is short s) originalInsUnits = s;
                    else if (val is int i) originalInsUnits = (short)i;
                }
                catch { }

                try { AcadApp.SetSystemVariable("INSUNITS", 21); }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nPCT11: Failed to set INSUNITS: {ex.Message}");
                    return;
                }

                var pfo = new PromptOpenFileOptions("\nPCT11: Select point cloud file (RCP/RCS):")
                {
                    Filter = "Point Cloud (*.rcp;*.rcs)|*.rcp;*.rcs|All Files (*.*)|*.*"
                };

                var res = ed.GetFileNameForOpen(pfo);
                if (res.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nPCT11: Cancelled.");
                    return;
                }

                string fileName = res.StringResult;
                if (string.IsNullOrWhiteSpace(fileName))
                {
                    ed.WriteMessage("\nPCT11: No file selected.");
                    return;
                }

                ed.Command("_.POINTCLOUDATTACH", fileName);

                ed.WriteMessage("\nPCT11: Point cloud attached.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPCT11 error: {ex.Message}");
            }
            finally
            {
                if (originalInsUnits.HasValue)
                {
                    try { AcadApp.SetSystemVariable("INSUNITS", originalInsUnits.Value); }
                    catch { }
                }
            }
        }

        // ------------------------------------------------------------
        // PCT11I – Stylize Point Clouds to INTENSITY (previous selection)
        // ------------------------------------------------------------

        [CommandMethod("PCT", "PCT11I", CommandFlags.Modal)]
        public static void PCT11I()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                var ids = CollectPointCloudIds_Helper_PCT11I(db);
                if (ids.Count == 0)
                {
                    ed.WriteMessage("\nPCT11I: No point clouds found.");
                    return;
                }

                ed.SetImpliedSelection(ids.ToArray());
                doc.SendStringToExecute("_.POINTCLOUDSTYLIZE P I ", true, false, false);

                ed.WriteMessage($"\nPCT11I: Sent POINTCLOUDSTYLIZE using Previous selection ({ids.Count} cloud(s)).");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPCT11I error: {ex.Message}");
            }

            static List<ObjectId> CollectPointCloudIds_Helper_PCT11I(Database db)
            {
                var results = new List<ObjectId>();

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    void Scan(ObjectId spaceId)
                    {
                        var btr = (BlockTableRecord)tr.GetObject(spaceId, OpenMode.ForRead);
                        foreach (ObjectId id in btr)
                        {
                            if (!id.IsValid) continue;

                            var cls = id.ObjectClass;
                            string dxf = cls?.DxfName ?? "";
                            string clsName = cls?.Name ?? "";

                            bool looksLikePointCloud =
                                (!string.IsNullOrEmpty(dxf) && dxf.IndexOf("POINTCLOUD", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                (!string.IsNullOrEmpty(clsName) && clsName.IndexOf("PointCloud", StringComparison.OrdinalIgnoreCase) >= 0);

                            if (looksLikePointCloud)
                                results.Add(id);
                        }
                    }

                    Scan(db.CurrentSpaceId);

                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var msId = bt[BlockTableRecord.ModelSpace];
                    if (msId != db.CurrentSpaceId)
                        Scan(msId);

                    tr.Commit();
                }

                var set = new HashSet<ObjectId>(results);
                return new List<ObjectId>(set);
            }
        }

        // ------------------------------------------------------------
        // PCT12 – Pipe Locator (auto circle fit + UFLS block + top COGO)
        // ------------------------------------------------------------

        [CommandMethod("PCT", "PCT12", CommandFlags.Modal)]
        public static void PCT12()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            const string TempLayer = "V-CONS-LINE-TEMP";

            try
            {
                // 1) Material pick
                var matOpts = new PromptKeywordOptions("\nPCT12: Select pipe material")
                {
                    AllowNone = false
                };
                matOpts.Keywords.Add("PVC");
                matOpts.Keywords.Add("C900");
                matOpts.Keywords.Add("RCP");
                matOpts.Keywords.Default = "PVC";

                var matRes = ed.GetKeywords(matOpts);
                if (matRes.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nPCT12: Cancelled.");
                    return;
                }

                PipeMaterial material = matRes.StringResult switch
                {
                    "PVC" => PipeMaterial.Pvc,
                    "C900" => PipeMaterial.C900,
                    "RCP" => PipeMaterial.Rcp,
                    _ => PipeMaterial.Pvc
                };

                string blockName = material switch
                {
                    PipeMaterial.Pvc => "UFLS-PVC",
                    PipeMaterial.C900 => "UFLS-C900",
                    PipeMaterial.Rcp => "UFLS-RCP",
                    _ => "UFLS-PVC"
                };

                using (var snap = SnapState.Capture())
                {
                    // OSNAPZ=0 and 3D object snaps for true interior picks
                    snap.Set(osnapZ: 0, osMode3d: 128);

                    var p1r = ed.GetPoint("\nPCT12: Pick 1st point on inside of pipe: ");
                    if (p1r.Status != PromptStatus.OK) return;

                    var p2r = ed.GetPoint("\nPCT12: Pick 2nd point on inside of pipe: ");
                    if (p2r.Status != PromptStatus.OK) return;

                    var p3r = ed.GetPoint("\nPCT12: Pick 3rd point on inside of pipe: ");
                    if (p3r.Status != PromptStatus.OK) return;

                    // GetPoint() returns coordinates in the CURRENT UCS.
                    // Fit the circle in UCS XY – same behavior as manual CIRCLE 3P.
                    Point3d centerUcs;
                    double radius;

                    if (!TryFitCircle2DFrom3Points(
                            p1r.Value, p2r.Value, p3r.Value,
                            out centerUcs, out radius))
                    {
                        ed.WriteMessage("\nPCT12: Points are colinear or invalid for circle fit.");
                        return;
                    }

                    // Drawing units are feet -> convert radius to inches
                    double measuredIdInches = 2.0 * radius * 12.0;

                    PipeSize size = PipeCatalog.FindClosest(material, measuredIdInches);
                    string visibilityName = PipeCatalog.GetVisibilityName(size);
                    double outerRadiusFeet = PipeCatalog.GetOuterRadiusFeet(size);

                    // ------------------------------------------------------------------
                    // UCS → WCS: CurrentUserCoordinateSystem maps UCS coords to WCS.
                    // ------------------------------------------------------------------
                    Matrix3d ucsMatrix = ed.CurrentUserCoordinateSystem;

                    // Center in WCS
                    Point3d centerWcs = centerUcs.TransformBy(ucsMatrix);

                    // UCS Z-axis expressed in WCS (so circle lies in section plane)
                    Vector3d ucsZinWcs = Vector3d.ZAxis
                        .TransformBy(ucsMatrix)
                        .GetNormal();

                    // "Top of pipe" is +Y in current UCS, then transformed to WCS
                    Point3d topUcs = new Point3d(
                        centerUcs.X,
                        centerUcs.Y + outerRadiusFeet,
                        centerUcs.Z);

                    Point3d topWcs = topUcs.TransformBy(ucsMatrix);
                    // ------------------------------------------------------------------

                    LayerState.EnsureLayer(TempLayer);

                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                        if (!bt.Has(blockName))
                        {
                            ed.WriteMessage($"\nPCT12: Block \"{blockName}\" not found in drawing.");
                            tr.Commit();
                            return;
                        }

                        var ms = (BlockTableRecord)tr.GetObject(
                            bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                        // Temp circle in correct (rotated) plane, in WCS
                        var tempCircle = new Circle(centerWcs, ucsZinWcs, radius)
                        {
                            Layer = TempLayer
                        };
                        ms.AppendEntity(tempCircle);
                        tr.AddNewlyCreatedDBObject(tempCircle, true);

                        // Insert dynamic block at center *in UCS*, then transform to WCS.
                        // This gives the block the same orientation as the rotated UCS,
                        // matching the temp circle / pipe section.
                        var defBtr = (BlockTableRecord)tr.GetObject(bt[blockName], OpenMode.ForRead);

                        var br = new BlockReference(centerUcs, defBtr.ObjectId)
                        {
                            // optional: set to same layer as temp circle or leave current
                            Layer = TempLayer
                        };
                        br.SetDatabaseDefaults();
                        ms.AppendEntity(br);
                        tr.AddNewlyCreatedDBObject(br, true);

                        // Apply UCS → WCS transform to the block reference
                        br.TransformBy(ucsMatrix);

                        // Set visibility state
                        if (br.IsDynamicBlock)
                        {
                            foreach (DynamicBlockReferenceProperty prop in br.DynamicBlockReferencePropertyCollection)
                            {
                                if (prop.ReadOnly) continue;
                                if (!prop.PropertyName.Equals("Visibility1", StringComparison.OrdinalIgnoreCase))
                                    continue;

                                foreach (var allowed in prop.GetAllowedValues())
                                {
                                    string name = Convert.ToString(allowed) ?? string.Empty;
                                    if (name.Equals(visibilityName, StringComparison.OrdinalIgnoreCase))
                                    {
                                        prop.Value = allowed;
                                        break;
                                    }
                                }
                            }
                        }

                        // Top-of-pipe COGO point (approx outside top), in WCS
                        try
                        {
                            CivilDocument civDoc = CivilApplication.ActiveDocument;
                            var cogoPts = civDoc.CogoPoints;

                            // location, desc, useDescriptionKey, matchOnParams, useNextPointNumSetting
                            ObjectId ptId = cogoPts.Add(topWcs, "UFLS-TOP", false, false, true);

                            // Apply your custom point style + label style
                            var cp = (CogoPoint)tr.GetObject(ptId, OpenMode.ForWrite);

                            try
                            {
                                // Point style – name must exist in this drawing
                                cp.StyleId = civDoc.Styles.PointStyles["R26_General Marker-3D"];
                            }
                            catch
                            {
                                // Style not found – leave default
                            }

                            try
                            {
                                // Point label style – via PointLabelStyles.LabelStyles collection
                                var lblColl = civDoc
                                    .Styles
                                    .LabelStyles
                                    .PointLabelStyles
                                    .LabelStyles;

                                cp.LabelStyleId = lblColl["R26-General 3D"];
                            }
                            catch
                            {
                                // Label style not found – leave default
                            }
                        }
                        catch (System.Exception exPt)
                        {
                            ed.WriteMessage($"\nPCT12: Warning – failed to create or style COGO point: {exPt.Message}");
                        }

                        tr.Commit();
                    }

                    ed.WriteMessage(
                        $"\nPCT12: Material={material}, measured ID≈{measuredIdInches:0.##}\" " +
                        $"=> size {size.NominalInches}\" ({visibilityName}). " +
                        "Temp circle, pipe block, and top point created.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPCT12 error: {ex.Message}");
            }
        }


        // ------------------------------------------------------------
        // PCT13 – Point Cloud UCS (PC_UCS replacement)
        // ------------------------------------------------------------

        [CommandMethod("PCT", "PCT13", CommandFlags.Modal)]
        public static void PCT13()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;

            try
            {
                using (var snap = SnapState.Capture())
                {
                    snap.Set(osMode3d: 128);

                    var ppr = ed.GetPoint("\nPCT13: Pick UCS origin (snap to point cloud / pipe, etc.): ");
                    if (ppr.Status != PromptStatus.OK)
                    {
                        ed.WriteMessage("\nPCT13: Cancelled.");
                        return;
                    }

                    Point3d origin = ppr.Value;

                    ViewTableRecord vtr = ed.GetCurrentView();
                    Vector3d z = vtr.ViewDirection;
                    if (z.Length < 1e-9) z = Vector3d.ZAxis;
                    z = z.GetNormal();

                    Vector3d worldX = Vector3d.XAxis;
                    Vector3d x = worldX - (worldX.DotProduct(z) * z);
                    if (x.Length < 1e-9)
                    {
                        Vector3d worldY = Vector3d.YAxis;
                        x = worldY - (worldY.DotProduct(z) * z);
                    }
                    if (x.Length < 1e-9)
                        x = z.GetPerpendicularVector();

                    x = x.GetNormal();
                    Vector3d y = z.CrossProduct(x).GetNormal();

                    Matrix3d ucs = Matrix3d.AlignCoordinateSystem(
                        Point3d.Origin,
                        Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                        origin, x, y, z);

                    ed.CurrentUserCoordinateSystem = ucs;

                    ed.WriteMessage(
                        $"\nPCT13: UCS set to origin at {origin.X:0.###}, {origin.Y:0.###}, {origin.Z:0.###}.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPCT13 error: {ex.Message}");
            }
        }

        // ------------------------------------------------------------
        // PCT14 – Manhole / Local Crop + Isolate
        // ------------------------------------------------------------

        [CommandMethod("PCT", "PCT14", CommandFlags.Modal)]
        public static void PCT14()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                ViewState.SaveView(VIEW_NAME_MH);

                var points = new List<Point3d>();

                var p1Res = ed.GetPoint("\nPCT14: Pick first corner of crop polygon: ");
                if (p1Res.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nPCT14: Cancelled.");
                    return;
                }
                points.Add(p1Res.Value);

                while (true)
                {
                    var pOpts = new PromptPointOptions("\nPCT14: Pick next corner or [Close]: ")
                    {
                        BasePoint = points[^1],
                        UseBasePoint = true
                    };

                    pOpts.Keywords.Add("Close");
                    pOpts.Keywords.Default = "Close";

                    var pRes = ed.GetPoint(pOpts);

                    if (pRes.Status == PromptStatus.Keyword &&
                        string.Equals(pRes.StringResult, "Close", StringComparison.OrdinalIgnoreCase))
                    {
                        if (points.Count < 3)
                        {
                            ed.WriteMessage("\nPCT14: Need at least 3 points to close a polygon.");
                            return;
                        }
                        break;
                    }

                    if (pRes.Status == PromptStatus.OK)
                    {
                        points.Add(pRes.Value);
                        continue;
                    }

                    if (points.Count < 3)
                    {
                        ed.WriteMessage("\nPCT14: Cancelled (fewer than 3 points).");
                        return;
                    }

                    break;
                }

                ObjectId plId;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    LayerState.EnsureLayer(LYR_CROP_TEMP);

                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    var pl = new AcPolyline();
                    pl.SetDatabaseDefaults();
                    pl.Layer = LYR_CROP_TEMP;

                    for (int i = 0; i < points.Count; i++)
                    {
                        var p = points[i];
                        pl.AddVertexAt(i, new Point2d(p.X, p.Y), 0.0, 0.0, 0.0);
                    }

                    pl.Closed = true;

                    plId = ms.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);

                    tr.Commit();
                }

                if (!PointCloudUtils.TryGetPolylineVerticesWcsPlan(plId, db, out var polyPtsPlan))
                {
                    ed.WriteMessage("\nPCT14: Created crop boundary is not valid (need 3+ vertices).");
                    return;
                }

                ObjectId cloudId = PointCloudUtils.FindSinglePointCloudInModelSpace(db);
                if (cloudId.IsNull)
                {
                    cloudId = PointCloudUtils.PromptForPointCloud(ed, db, "\nPCT14: Pick point cloud to crop: ");
                    if (cloudId.IsNull)
                    {
                        ed.WriteMessage("\nPCT14: No point cloud selected.");
                        return;
                    }
                }

                PointCloudUtils.UncropPointCloudById(ed, cloudId);
                PointCloudUtils.CropPointCloudPolygonById(ed, cloudId, polyPtsPlan);

                var idsToKeep = new List<ObjectId> { cloudId, plId };

                try
                {
                    var polyColl = new Point3dCollection();
                    foreach (var p in polyPtsPlan)
                        polyColl.Add(new Point3d(p.X, p.Y, 0.0));

                    PromptSelectionResult selInside = ed.SelectCrossingPolygon(polyColl, null);

                    if (selInside.Status == PromptStatus.OK && selInside.Value != null)
                    {
                        foreach (ObjectId id in selInside.Value.GetObjectIds())
                        {
                            if (!idsToKeep.Contains(id))
                                idsToKeep.Add(id);
                        }
                    }
                }
                catch (System.Exception exSel)
                {
                    ed.WriteMessage($"\nPCT14: Warning – polygon selection failed: {exSel.Message}");
                }

                SelectionUtils.IsolateSelection(ed, idsToKeep.ToArray());

                ed.WriteMessage($"\nPCT14: Polygon on {LYR_CROP_TEMP}, cloud cropped, inside entities kept via isolate.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPCT14 error: {ex.Message}");
            }
        }

        // ------------------------------------------------------------
        // PCT15 – Manhole 3D Orbit Center
        // ------------------------------------------------------------

        [CommandMethod("PCT", "PCT15", CommandFlags.Modal)]
        public static void PCT15()
        {
            var ed = AcadApp.DocumentManager.MdiActiveDocument.Editor;

            try
            {
                using (var snap = SnapState.Capture())
                {
                    snap.Set(osMode3d: 128);
                    ed.Command("._3DORBITCTR");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPCT15 error: {ex.Message}");
            }
        }

        // ------------------------------------------------------------
        // PCT16 – Reset Manhole View
        // ------------------------------------------------------------

        [CommandMethod("PCT", "PCT16", CommandFlags.Modal)]
        public static void PCT16()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                if (!ViewState.RestoreView(VIEW_NAME_MH))
                {
                    ed.WriteMessage($"\nPCT16: Saved view {VIEW_NAME_MH} not found. Using World plan.");
                    ViewState.SetUcsWorld(ed);
                    ViewState.PlanWorld(ed);
                }
                else
                {
                    ViewState.SetUcsWorld(ed);
                }

                ObjectId cloudId = PointCloudUtils.FindSinglePointCloudInModelSpace(db);
                if (cloudId.IsNull)
                {
                    cloudId = PointCloudUtils.PromptForPointCloud(ed, db, "\nPCT16: Pick point cloud to UN-CROP: ");
                    if (cloudId.IsNull)
                    {
                        ed.WriteMessage("\nPCT16: No point cloud selected.");
                        return;
                    }
                }

                PointCloudUtils.UncropPointCloudById(ed, cloudId);

                LayerState.DeleteEntitiesOnLayer(db, LYR_CROP_TEMP);

                SelectionUtils.Unisolate(ed);

                ed.WriteMessage("\nPCT16: Manhole reset complete (view/UCS restored, cloud uncropped, temp crop removed, unisolated).");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPCT16 error: {ex.Message}");
            }
        }
        // ------------------------------------------------------------
        // PCT17 – Move COGO points to base polyline + add vertices
        //          and propagate matching vertices to adjacent polylines
        // ------------------------------------------------------------

        [CommandMethod("PCT", "PCT17", CommandFlags.Modal)]
        public static void PCT17()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                // 1) Pick BASE polyline (typically back-of-curb)
                var peBase = new PromptEntityOptions("\nPCT17: Select BASE polyline (e.g., back-of-curb): ");
                peBase.SetRejectMessage("\nMust be a 2D polyline.");
                peBase.AddAllowedClass(typeof(AcPolyline), false);

                var baseRes = ed.GetEntity(peBase);
                if (baseRes.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nPCT17: Cancelled.");
                    return;
                }

                ObjectId basePlId = baseRes.ObjectId;

                // 2) Pick COGO points
                var psoPts = new PromptSelectionOptions
                {
                    MessageForAdding = "\nSelect COGO points to move to BASE polyline: ",
                    RejectObjectsOnLockedLayers = true
                };

                var ptFilter = new SelectionFilter(new[]
                {
                    new TypedValue((int)DxfCode.Start, "AECC_COGO_POINT")
                });

                var ptsRes = ed.GetSelection(psoPts, ptFilter);
                if (ptsRes.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nPCT17: No COGO points selected.");
                    return;
                }

                // 3) Pick adjacent polylines
                var psoAdj = new PromptSelectionOptions
                {
                    MessageForAdding = "\nSelect ADJACENT polylines (lip, flowline, back-of-walk, etc.): ",
                    RejectObjectsOnLockedLayers = true
                };

                var plFilter = new SelectionFilter(new[]
                {
                    new TypedValue((int)DxfCode.Start, "LWPOLYLINE")
                });

                var adjRes = ed.GetSelection(psoAdj, plFilter);
                if (adjRes.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nPCT17: No adjacent polylines selected.");
                    return;
                }

                int movedPoints = 0;
                int baseVertsAdded = 0;
                int adjVertsAdded = 0;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    // Open base polyline for write
                    var basePl = tr.GetObject(basePlId, OpenMode.ForWrite, false) as AcPolyline;
                    if (basePl == null)
                    {
                        ed.WriteMessage("\nPCT17: BASE selection is not a polyline.");
                        return;
                    }

                    // Open adjacent polylines for write (skip the base itself)
                    var adjPlines = new System.Collections.Generic.List<AcPolyline>();
                    foreach (ObjectId adjId in adjRes.Value.GetObjectIds())
                    {
                        if (adjId == basePlId)
                            continue;

                        if (tr.GetObject(adjId, OpenMode.ForWrite, false) is AcPolyline pl)
                        {
                            adjPlines.Add(pl);
                        }
                    }

                    if (adjPlines.Count == 0)
                    {
                        ed.WriteMessage("\nPCT17: No valid adjacent polylines found.");
                        return;
                    }

                    // Helper to test if a vertex already exists near a point
                    bool HasNearbyVertex(AcPolyline pl, Point3d pt, double tolSquared)
                    {
                        int vCount = pl.NumberOfVertices;
                        for (int i = 0; i < vCount; i++)
                        {
                            Point3d v = pl.GetPoint3dAt(i);
                            double dx = v.X - pt.X;
                            double dy = v.Y - pt.Y;
                            if ((dx * dx + dy * dy) < tolSquared)
                                return true;
                        }
                        return false;
                    }

                    double tol2 = 0.0001; // ~0.01' tolerance

                    foreach (ObjectId cpId in ptsRes.Value.GetObjectIds())
                    {
                        if (tr.GetObject(cpId, OpenMode.ForWrite, false) is not CogoPoint cp)
                            continue;

                        Point3d loc = cp.Location;

                        // Project point to BASE polyline
                        if (!GeometryUtils.TryProjectPointToPolylineXY(
                                basePl, loc, out Point3d projBase, out int baseSeg))
                        {
                            continue;
                        }

                        // Move COGO point onto base polyline (XY only)
                        cp.Easting = projBase.X;
                        cp.Northing = projBase.Y;
                        movedPoints++;

                        // Add vertex to BASE polyline if needed
                        if (!HasNearbyVertex(basePl, projBase, tol2))
                        {
                            int insertIndex = baseSeg + 1;
                            if (insertIndex > basePl.NumberOfVertices)
                                insertIndex = basePl.NumberOfVertices;

                            basePl.AddVertexAt(
                                insertIndex,
                                new Point2d(projBase.X, projBase.Y),
                                0.0, 0.0, 0.0);

                            baseVertsAdded++;
                        }

                        // For each adjacent polyline, project this same station and add vertex
                        foreach (var adjPl in adjPlines)
                        {
                            if (!GeometryUtils.TryProjectPointToPolylineXY(
                                    adjPl, projBase, out Point3d projAdj, out int adjSeg))
                            {
                                continue;
                            }

                            if (HasNearbyVertex(adjPl, projAdj, tol2))
                                continue;

                            int adjInsert = adjSeg + 1;
                            if (adjInsert > adjPl.NumberOfVertices)
                                adjInsert = adjPl.NumberOfVertices;

                            adjPl.AddVertexAt(
                                adjInsert,
                                new Point2d(projAdj.X, projAdj.Y),
                                0.0, 0.0, 0.0);

                            adjVertsAdded++;
                        }
                    }

                    tr.Commit();
                }

                ed.WriteMessage(
                    $"\nPCT17: Moved {movedPoints} COGO point(s) to BASE polyline, " +
                    $"added {baseVertsAdded} vertex(ices) on BASE and {adjVertsAdded} on ADJACENT polylines.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPCT17 error: {ex.Message}");
            }
        }
        // ------------------------------------------------------------
        // PCT17V – Propagate vertices from BASE polyline
        //          to ADJACENT polylines (no COGO points)
        // ------------------------------------------------------------

        [CommandMethod("PCT", "PCT17V", CommandFlags.Modal)]
        public static void PCT17V()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                // 1) Pick BASE polyline (source of vertex stations)
                var peBase = new PromptEntityOptions(
                    "\nPCT17V: Select BASE polyline (source with desired vertices): ");
                peBase.SetRejectMessage("\nMust be a 2D polyline.");
                peBase.AddAllowedClass(typeof(AcPolyline), exactMatch: false);

                var baseRes = ed.GetEntity(peBase);
                if (baseRes.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nPCT17V: Cancelled.");
                    return;
                }

                ObjectId basePlId = baseRes.ObjectId;

                // 2) Pick ADJACENT polylines to receive matching vertices
                var psoAdj = new PromptSelectionOptions
                {
                    MessageForAdding = "\nPCT17V: Select ADJACENT polylines (lip, flowline, walk, etc.): ",
                    RejectObjectsOnLockedLayers = true
                };

                var plFilter = new SelectionFilter(new[]
                {
            new TypedValue((int)DxfCode.Start, "LWPOLYLINE")
        });

                var adjRes = ed.GetSelection(psoAdj, plFilter);
                if (adjRes.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nPCT17V: No adjacent polylines selected.");
                    return;
                }

                int adjVertsAdded = 0;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    // Open BASE for read
                    var basePl = tr.GetObject(basePlId, OpenMode.ForRead, false) as AcPolyline;
                    if (basePl == null)
                    {
                        ed.WriteMessage("\nPCT17V: BASE selection is not a polyline.");
                        return;
                    }

                    // Collect BASE vertices in XY
                    var baseVerts = new List<Point3d>();
                    int baseCount = basePl.NumberOfVertices;
                    for (int i = 0; i < baseCount; i++)
                    {
                        baseVerts.Add(basePl.GetPoint3dAt(i));
                    }

                    if (baseVerts.Count == 0)
                    {
                        ed.WriteMessage("\nPCT17V: BASE polyline has no vertices.");
                        return;
                    }

                    // Open ADJACENT polylines for write (skip BASE itself if in set)
                    var adjPlines = new List<AcPolyline>();
                    foreach (ObjectId adjId in adjRes.Value.GetObjectIds())
                    {
                        if (adjId == basePlId)
                            continue;

                        if (tr.GetObject(adjId, OpenMode.ForWrite, false) is AcPolyline pl)
                        {
                            adjPlines.Add(pl);
                        }
                    }

                    if (adjPlines.Count == 0)
                    {
                        ed.WriteMessage("\nPCT17V: No valid adjacent polylines found.");
                        return;
                    }

                    // Helper: does this polyline already have a vertex near pt?
                    bool HasNearbyVertex(AcPolyline pl, Point3d pt, double tolSquared)
                    {
                        int vCount = pl.NumberOfVertices;
                        for (int i = 0; i < vCount; i++)
                        {
                            Point3d v = pl.GetPoint3dAt(i);
                            double dx = v.X - pt.X;
                            double dy = v.Y - pt.Y;
                            if ((dx * dx + dy * dy) < tolSquared)
                                return true;
                        }
                        return false;
                    }

                    double tol2 = 0.0001; // ~0.01' tolerance

                    // For each BASE vertex, project onto each ADJACENT polyline and add vertex
                    foreach (Point3d baseVert in baseVerts)
                    {
                        foreach (var adjPl in adjPlines)
                        {
                            if (!GeometryUtils.TryProjectPointToPolylineXY(
                                    adjPl, baseVert, out Point3d projAdj, out int adjSeg))
                            {
                                continue;
                            }

                            if (HasNearbyVertex(adjPl, projAdj, tol2))
                                continue;

                            int insertIndex = adjSeg + 1;
                            if (insertIndex > adjPl.NumberOfVertices)
                                insertIndex = adjPl.NumberOfVertices;

                            adjPl.AddVertexAt(
                                insertIndex,
                                new Point2d(projAdj.X, projAdj.Y),
                                0.0, 0.0, 0.0);

                            adjVertsAdded++;
                        }
                    }

                    tr.Commit();
                }

                ed.WriteMessage(
                    $"\nPCT17V: Added {adjVertsAdded} vertex(ices) to ADJACENT polylines based on BASE polyline vertices.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPCT17V error: {ex.Message}");
            }
        }
        // ============================================================
        // PCT18 / PCT18R – Roadway 3D orbit saved state (view + layers)
        //   FIXES:
        //   - Switch current view to ROADWAY_ORBIT_VIEW_CLEAR before deleting baseline
        //   - Avoid eInvalidLayer by switching CLAYER to "0" and skipping invalid ops
        // ============================================================

        private sealed class RoadwayOrbitState
        {
            public bool Saved;
            public string? SavedCurrentLayer;
            public Dictionary<string, (bool IsOff, bool IsFrozen)>? LayerState;
        }

        private static readonly Dictionary<string, RoadwayOrbitState> _rwOrbitStates =
            new Dictionary<string, RoadwayOrbitState>(StringComparer.OrdinalIgnoreCase);

        private static string GetDbKey(Database db) => db.FingerprintGuid.ToString();

        private static RoadwayOrbitState GetRwState(Database db)
        {
            string key = GetDbKey(db);
            if (!_rwOrbitStates.TryGetValue(key, out var state))
            {
                state = new RoadwayOrbitState();
                _rwOrbitStates[key] = state;
            }
            return state;
        }

        private static void CaptureLayerState(Database db, RoadwayOrbitState state)
        {
            var dict = new Dictionary<string, (bool IsOff, bool IsFrozen)>(StringComparer.OrdinalIgnoreCase);

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

                foreach (ObjectId id in lt)
                {
                    var rec = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    dict[rec.Name] = (rec.IsOff, rec.IsFrozen);
                }

                tr.Commit();
            }

            state.LayerState = dict;

            try { state.SavedCurrentLayer = AcadApp.GetSystemVariable("CLAYER") as string; }
            catch { state.SavedCurrentLayer = null; }
        }

        private static void EnsureCurrentLayerSafe(Database db, Editor ed)
        {
            // If "0" doesn't exist, create it (rare, but safe).
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                    if (!lt.Has("0"))
                    {
                        lt.UpgradeOpen();
                        var ltr = new LayerTableRecord { Name = "0" };
                        lt.Add(ltr);
                        tr.AddNewlyCreatedDBObject(ltr, true);
                    }
                    tr.Commit();
                }

                AcadApp.SetSystemVariable("CLAYER", "0");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPCT18R warning: Could not set current layer to 0: {ex.Message}");
            }
        }

        private static void RestoreLayerState(Database db, Editor ed, RoadwayOrbitState state)
        {
            if (state.LayerState == null) return;

            EnsureCurrentLayerSafe(db, ed);

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

                foreach (var kvp in state.LayerState)
                {
                    if (!lt.Has(kvp.Key)) continue;

                    try
                    {
                        var rec = (LayerTableRecord)tr.GetObject(lt[kvp.Key], OpenMode.ForWrite);

                        // Defensive: some layers can throw when off/frozen depending on state/xrefs/locked/current
                        try { rec.IsOff = kvp.Value.IsOff; } catch { /* ignore */ }
                        try { rec.IsFrozen = kvp.Value.IsFrozen; } catch { /* ignore */ }
                    }
                    catch
                    {
                        // skip any layer that errors out (avoids eInvalidLayer chain failures)
                    }
                }

                tr.Commit();
            }

            // Try to restore original current layer (if it still exists).
            if (!string.IsNullOrWhiteSpace(state.SavedCurrentLayer))
            {
                try
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                        if (lt.Has(state.SavedCurrentLayer))
                        {
                            tr.Commit();
                            AcadApp.SetSystemVariable("CLAYER", state.SavedCurrentLayer);
                        }
                        else
                        {
                            tr.Commit();
                        }
                    }
                }
                catch { }
            }
        }

        private static void SaveNamedViewOverwrite(Editor ed, string viewName)
        {
            var db = ed.Document.Database;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var vt = (ViewTable)tr.GetObject(db.ViewTableId, OpenMode.ForRead);

                if (vt.Has(viewName))
                {
                    vt.UpgradeOpen();
                    var existingId = vt[viewName];
                    var existing = (ViewTableRecord)tr.GetObject(existingId, OpenMode.ForWrite);
                    existing.Erase();
                }

                var cur = ed.GetCurrentView();

                var vtr = new ViewTableRecord
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

                vt.UpgradeOpen();
                vt.Add(vtr);
                tr.AddNewlyCreatedDBObject(vtr, true);

                tr.Commit();
            }
        }

        private static bool DeleteNamedViewSafe(Editor ed, string viewName, out string? failReason)
        {
            failReason = null;
            var db = ed.Document.Database;

            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var vt = (ViewTable)tr.GetObject(db.ViewTableId, OpenMode.ForRead);
                    if (!vt.Has(viewName))
                        return true;

                    vt.UpgradeOpen();

                    var id = vt[viewName];
                    var vtr = (ViewTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                    vtr.Erase();

                    tr.Commit();
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                failReason = ex.Message;
                return false;
            }
        }

        [CommandMethod("PCT", "PCT18", CommandFlags.Modal)]
        public static void PCT18()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                var state = GetRwState(db);

                if (!state.Saved)
                {
                    SaveNamedViewOverwrite(ed, VIEW_NAME_RW_ORBIT);
                    CaptureLayerState(db, state);
                    state.Saved = true;

                    ed.WriteMessage(
                        $"\nPCT18: Baseline saved as {VIEW_NAME_RW_ORBIT} (view + layer state). " +
                        "Subsequent PCT18 runs will NOT overwrite until PCT18R is used.");
                }

                using (var snap = SnapState.Capture())
                {
                    snap.Set(osMode3d: 128);
                    ed.Command("._3DORBITCTR");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPCT18 error: {ex.Message}");
            }
        }

        [CommandMethod("PCT", "PCT18R", CommandFlags.Modal)]
        public static void PCT18R()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                var state = GetRwState(db);

                if (!state.Saved)
                {
                    ed.WriteMessage("\nPCT18R: No baseline saved yet. Run PCT18 first.");
                    return;
                }

                // 1) Restore the baseline view
                bool restored = ViewState.RestoreView(VIEW_NAME_RW_ORBIT);
                if (!restored)
                {
                    ed.WriteMessage($"\nPCT18R: Saved view {VIEW_NAME_RW_ORBIT} not found in this drawing.");
                }

                // 2) Restore layer state (robust: avoids eInvalidLayer)
                RestoreLayerState(db, ed, state);

                // 3) Reset UCS
                ViewState.SetUcsWorld(ed);

                // 4) Create/overwrite CLEAR view from the now-restored current view,
                //    then switch to CLEAR so baseline view is NOT the current view.
                SaveNamedViewOverwrite(ed, VIEW_NAME_RW_ORBIT_CLEAR);
                ViewState.RestoreView(VIEW_NAME_RW_ORBIT_CLEAR);

                // 5) Now delete baseline view safely
                if (!DeleteNamedViewSafe(ed, VIEW_NAME_RW_ORBIT, out var reason))
                {
                    ed.WriteMessage($"\nPCT18R: Could not delete {VIEW_NAME_RW_ORBIT}: {reason}");
                }
                else
                {
                    ed.WriteMessage($"\nPCT18R: Deleted named view {VIEW_NAME_RW_ORBIT}.");
                }

                // 6) Clear baseline so next PCT18 captures a NEW one.
                state.Saved = false;
                state.LayerState = null;
                state.SavedCurrentLayer = null;

                ed.WriteMessage($"\nPCT18R: Restored baseline, switched to {VIEW_NAME_RW_ORBIT_CLEAR}, baseline cleared.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPCT18R error: {ex.Message}");
            }
        }

        // ------------------------------------------------------------
        // PCT19 – General Marker (PC_MARKER replacement)
        // ------------------------------------------------------------

        [CommandMethod("PCT", "PCT19", CommandFlags.Modal)]
        public static void PCT19()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            const string MarkerLayerName = "V-SURV-MRKR";
            const short MarkerColorAci = 141;
            const int OsMode2D = 16384;
            const int OsMode3D = 128;

            string? oldLayer = null;

            try
            {
                try { oldLayer = AcadApp.GetSystemVariable("CLAYER") as string; } catch { }

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

                    if (!lt.Has(MarkerLayerName))
                    {
                        lt.UpgradeOpen();

                        var ltr = new LayerTableRecord
                        {
                            Name = MarkerLayerName,
                            Color = AcColor.FromColorIndex(ColorMethod.ByAci, MarkerColorAci)
                        };

                        lt.Add(ltr);
                        tr.AddNewlyCreatedDBObject(ltr, true);
                    }
                    else
                    {
                        var ltr = (LayerTableRecord)tr.GetObject(lt[MarkerLayerName], OpenMode.ForWrite);
                        ltr.Color = AcColor.FromColorIndex(ColorMethod.ByAci, MarkerColorAci);
                    }

                    tr.Commit();
                }

                try { AcadApp.SetSystemVariable("CLAYER", MarkerLayerName); }
                catch (System.Exception exLayer)
                {
                    ed.WriteMessage($"\nPCT19 warning: Failed to set CLAYER: {exLayer.Message}");
                }

                using (var snap = SnapState.Capture())
                {
                    snap.Set(osMode3d: OsMode3D, osnapZ: 0);
                    AcadApp.SetSystemVariable("OSMODE", OsMode2D);

                    var pr = ed.GetPoint("\nPCT19: Pick marker location: ");
                    if (pr.Status != PromptStatus.OK)
                    {
                        ed.WriteMessage("\nPCT19: Cancelled.");
                        return;
                    }

                    Point3d insPt = pr.Value;

                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                        if (bt.Has("MARKER"))
                        {
                            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                            var br = new BlockReference(insPt, bt["MARKER"])
                            {
                                Layer = MarkerLayerName,
                                ScaleFactors = new Scale3d(1.0),
                                Rotation = 0.0
                            };
                            br.SetDatabaseDefaults();

                            ms.AppendEntity(br);
                            tr.AddNewlyCreatedDBObject(br, true);

                            tr.Commit();
                        }
                        else
                        {
                            tr.Commit();
                            ed.Command("._-INSERT", "MARKER", insPt, 1.0, 0.0);
                        }
                    }

                    ed.WriteMessage("\nPCT19: General marker placed on V-SURV-MRKR.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPCT19 error: {ex.Message}");
            }
            finally
            {
                if (!string.IsNullOrEmpty(oldLayer))
                {
                    try { AcadApp.SetSystemVariable("CLAYER", oldLayer); } catch { }
                }
            }
        }

        // ------------------------------------------------------------
        // PCT20 – Insert vertices on polylines at line/polyline crossings
        //          - Select BASE polylines to modify
        //          - Select CROSSING lines/polylines
        //          - For every intersection, insert a vertex on the BASE polyline
        // ------------------------------------------------------------

        [CommandMethod("PCT", "PCT20", CommandFlags.Modal)]
        public static void PCT20()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                // 1) Select BASE polylines (the ones we will modify)
                var psoBase = new PromptSelectionOptions
                {
                    MessageForAdding = "\nPCT20: Select BASE polylines to add vertices to: ",
                    RejectObjectsOnLockedLayers = true
                };

                var baseFilter = new SelectionFilter(new[]
                {
            new TypedValue((int)DxfCode.Start, "LWPOLYLINE")
        });

                var baseRes = ed.GetSelection(psoBase, baseFilter);
                if (baseRes.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nPCT20: No BASE polylines selected.");
                    return;
                }

                // 2) Select crossing geometry (lines and/or polylines)
                var psoCut = new PromptSelectionOptions
                {
                    MessageForAdding =
                        "\nPCT20: Select crossing LINE / POLYLINE objects (these define where vertices are added): ",
                    RejectObjectsOnLockedLayers = true
                };

                var cutRes = ed.GetSelection(psoCut);
                if (cutRes.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\nPCT20: No crossing objects selected.");
                    return;
                }

                int totalVerticesAdded = 0;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    // Open BASE polylines for write
                    var basePlines = new List<AcPolyline>();
                    foreach (ObjectId id in baseRes.Value.GetObjectIds())
                    {
                        if (tr.GetObject(id, OpenMode.ForWrite, false) is AcPolyline pl)
                            basePlines.Add(pl);
                    }

                    if (basePlines.Count == 0)
                    {
                        ed.WriteMessage("\nPCT20: No editable BASE polylines found.");
                        return;
                    }

                    // Open crossing objects as Curves (Line or Polyline)
                    var cutterCurves = new List<Curve>();
                    foreach (ObjectId id in cutRes.Value.GetObjectIds())
                    {
                        var obj = tr.GetObject(id, OpenMode.ForRead, false);
                        if (obj is Line ln)
                            cutterCurves.Add(ln);
                        else if (obj is AcPolyline plCut)
                            cutterCurves.Add(plCut);
                    }

                    if (cutterCurves.Count == 0)
                    {
                        ed.WriteMessage("\nPCT20: No valid crossing LINE / POLYLINE objects found.");
                        return;
                    }

                    // Helper: does this polyline already have a vertex near pt?
                    bool HasNearbyVertex(AcPolyline pl, Point3d pt, double tolSquared)
                    {
                        int vCount = pl.NumberOfVertices;
                        for (int i = 0; i < vCount; i++)
                        {
                            Point3d v = pl.GetPoint3dAt(i);
                            double dx = v.X - pt.X;
                            double dy = v.Y - pt.Y;
                            if ((dx * dx + dy * dy) < tolSquared)
                                return true;
                        }
                        return false;
                    }

                    double tol2 = 0.0001; // ~0.01' tolerance in XY

                    // For each BASE polyline, find intersections with each cutter curve
                    foreach (var basePl in basePlines)
                    {
                        var baseCurve = (Curve)basePl;

                        foreach (var cutter in cutterCurves)
                        {
                            if (ReferenceEquals(cutter, baseCurve))
                                continue;

                            var intPts = new Point3dCollection();

                            try
                            {
                                baseCurve.IntersectWith(
                                    cutter,
                                    Intersect.OnBothOperands,
                                    intPts,
                                    IntPtr.Zero,
                                    IntPtr.Zero);
                            }
                            catch (System.Exception exInt)
                            {
                                ed.WriteMessage($"\nPCT20: Warning – intersection failure: {exInt.Message}");
                                continue;
                            }

                            if (intPts.Count == 0)
                                continue;

                            foreach (Point3d ip in intPts)
                            {
                                Point3d ipXY = new Point3d(ip.X, ip.Y, 0.0);

                                if (!GeometryUtils.TryProjectPointToPolylineXY(
                                        basePl, ipXY, out Point3d projBase, out int segIndex))
                                {
                                    continue;
                                }

                                if (HasNearbyVertex(basePl, projBase, tol2))
                                    continue;

                                int insertIndex = segIndex + 1;
                                if (insertIndex > basePl.NumberOfVertices)
                                    insertIndex = basePl.NumberOfVertices;

                                basePl.AddVertexAt(
                                    insertIndex,
                                    new Point2d(projBase.X, projBase.Y),
                                    0.0, 0.0, 0.0);

                                totalVerticesAdded++;
                            }
                        }
                    }

                    tr.Commit();
                }

                ed.WriteMessage(
                    $"\nPCT20: Added {totalVerticesAdded} vertex(ices) at crossing locations on selected BASE polylines.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPCT20 error: {ex.Message}");
            }
        }
        // ------------------------------------------------------------
        // Local geometry helper: circle through 3 points (XY)
        // ------------------------------------------------------------
        private static bool TryFitCircle2DFrom3Points(
            Point3d p1, Point3d p2, Point3d p3,
            out Point3d center,
            out double radius)
        {
            // Work in XY plane
            double x1 = p1.X, y1 = p1.Y;
            double x2 = p2.X, y2 = p2.Y;
            double x3 = p3.X, y3 = p3.Y;

            double a1 = x2 - x1;
            double b1 = y2 - y1;
            double a2 = x3 - x1;
            double b2 = y3 - y1;

            double det = 2.0 * (a1 * b2 - a2 * b1);
            if (Math.Abs(det) < 1e-12)
            {
                center = Point3d.Origin;
                radius = 0.0;
                return false; // colinear or nearly so
            }

            double c1 = (x2 * x2 - x1 * x1) + (y2 * y2 - y1 * y1);
            double c2 = (x3 * x3 - x1 * x1) + (y3 * y3 - y1 * y1);

            double cx = (b2 * c1 - b1 * c2) / det;
            double cy = (a1 * c2 - a2 * c1) / det;

            double dx = cx - x1;
            double dy = cy - y1;

            radius = Math.Sqrt(dx * dx + dy * dy);

            center = new Point3d(cx, cy, p1.Z); // keep Z of first pick
            return radius > 1e-9;
        }

    }
}