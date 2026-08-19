using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using CLV_CivilTools.Shared;

using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Autodesk.Civil.DatabaseServices;
using CogoPoint = Autodesk.Civil.DatabaseServices.CogoPoint;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace CLV_CivilTools
{
    public class UflsTopOfPipeCommand
    {
        private const string LAYER_PIPE_OUTER = "V-SURV-PIPE-OUTR";
        private const string LAYER_STRUCT_INNER = "V-SURV-STRC-INNR-2D~~";
        private const string LAYER_PICK_MARKER = "V-TEMP-PIPEPICK";
        private const string LAYER_GRADE_BREAK_REDLINE = "V-SURV-RDLN";

        private const double GradeBreakWarnChangePercent = 0.5;
        private const double GradeBreakRedlineBoxSizeFeet = 4.0;

        // ------------------------------------------------------------
        // UFLS1 – Top of Pipe
        //  * Select 2+ CogoPoints in order (upstream -> downstream)
        //  * 3D best-fit line
        //  * Direction aligned with pick order
        //  * Extended to nearest inner-wall footprint:
        //      - polylines in Modelspace on V-SURV-STRC-INNR-2D~~
        //      - polylines inside first-level blocks on that layer
        //      - circles on that layer (modelspace + blocks)
        //  * If upstream/downstream extreme pick lies INSIDE a
        //    structure interval (based on intersection order),
        //    do NOT extend into that structure on that side.
        //  * Creates 3D polyline on V-SURV-PIPE-OUTR
        // ------------------------------------------------------------
        [CommandMethod("UFLS", "UFLS1", CommandFlags.Modal)]
        public static void Ufls1_TopOfPipe() => RunUfls1TopOfPipe(debug: false);

        [CommandMethod("UFLS", "UFLS1DBG", CommandFlags.Modal)]
        public static void Ufls1_TopOfPipe_Debug() => RunUfls1TopOfPipe(debug: true);

        private static void RunUfls1TopOfPipe(bool debug)
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var db = doc.Database;
            var ed = doc.Editor;

            var markerIds = new List<ObjectId>();

            try
            {
                // Ensure temp marker layer exists (yellow)
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayer(db, tr, LAYER_PICK_MARKER, 2);
                    tr.Commit();
                }

                // -------------------------------------------
                // 1) Collect 2+ COGO points with numbered markers
                // -------------------------------------------
                var pts = GetPipePointsFromCogo(ed, db, markerIds);
                if (pts.Count < 2)
                {
                    ed.WriteMessage("\nUFLS1: Need at least 2 COGO points. Command cancelled.");
                    RemoveMarkers(db, markerIds);
                    return;
                }

                // -------------------------------------------
                // 2) Best-fit line
                // -------------------------------------------
                // Civil 3D's Best Fit Line from COGO points is a plan-view (XY)
                // fit. UFLS1 now matches that behavior for the 2D alignment,
                // then separately fits elevation against station along that
                // alignment. The previous full 3D PCA could shift the plan
                // location when the selected shots had elevation spread.
                var fit = ComputeTopOfPipeBestFit(pts);
                var centroid = fit.Centroid;
                var dir = fit.Direction;

                // Align station direction with pick order.  The elevation fit is
                // station-based, so when the station direction is reversed the
                // fitted slope must be reversed too.  Otherwise the XY line is
                // drawn in the picked direction but the start/end invert
                // elevations are swapped whenever the PCA direction naturally
                // points opposite the user's picks.
                double tFirst = Dot(VectorFrom(pts[0], centroid), dir);
                double tLast = Dot(VectorFrom(pts[pts.Count - 1], centroid), dir);
                if (tLast < tFirst)
                {
                    dir = dir.Negate();
                    fit = new TopOfPipeFit(
                        centroid,
                        dir,
                        fit.ElevationIntercept,
                        -fit.ElevationSlope);
                }

                var tList = pts
                    .Select(p => Dot(VectorFrom(p, centroid), dir))
                    .ToList();

                double tMinPick = tList.Min();
                double tMaxPick = tList.Max();

                // -------------------------------------------
                // 2b) Review noticeable grade breaks before creating the pipe.
                //     This is intentionally a warning/review step only; when no
                //     break is found, UFLS1 continues exactly as it did before.
                // -------------------------------------------
                var gradeBreakReport = AnalyzeTopOfPipeGradeBreaks(pts, centroid, dir, fit);
                if (gradeBreakReport.HasGradeBreak)
                {
                    int redlineCount = CreateGradeBreakRedlineBoxes(db, gradeBreakReport);
                    ZoomToPickedPointExtents(ed, pts);

                    var reviewResult = TopOfPipeGradeBreakReviewForm.ShowReview(gradeBreakReport, redlineCount);

                    if (reviewResult != System.Windows.Forms.DialogResult.OK)
                    {
                        ed.WriteMessage("\nUFLS1: Grade-break review cancelled. Redline boxes remain on V-SURV-RDLN for review. No top-of-pipe polyline created.");
                        RemoveMarkers(db, markerIds);
                        return;
                    }
                }

                // -------------------------------------------
                // 3) Collect structure inner-wall geometry
                //    from modelspace *and* first-level blocks:
                //    - polyline segments
                //    - circles (manholes, etc.)
                // -------------------------------------------
                List<Point2d> boundaryIntersectionPoints;
                List<Point2d> openIntersectionPoints;
                List<(Point2d P0, Point2d P1)> openStructureSegments;
                List<(Point2d P0, Point2d P1)> closedBoundarySegments;
                List<List<Point2d>> structureLoops;
                List<(Point2d Center, double Radius)> structureCircles;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    openStructureSegments = new List<(Point2d P0, Point2d P1)>();
                    closedBoundarySegments = new List<(Point2d P0, Point2d P1)>();
                    structureLoops = new List<List<Point2d>>();
                    structureCircles = new List<(Point2d Center, double Radius)>();

                    CollectInnerStructureGeometry(
                        tr,
                        db,
                        openStructureSegments,
                        closedBoundarySegments,
                        structureLoops,
                        structureCircles);


                    var boundarySegPts =
                        FindIntersectionPointsFromSegments(closedBoundarySegments, centroid, dir);
                    var openSegPts =
                        FindIntersectionPointsFromSegments(openStructureSegments, centroid, dir);
                    var circlePts =
                        FindIntersectionPointsFromCircles(structureCircles, centroid, dir);

                    boundaryIntersectionPoints = DeduplicateIntersectionPoints(boundarySegPts
                        .Concat(circlePts)
                        .ToList());
                    openIntersectionPoints = DeduplicateIntersectionPoints(openSegPts);

                    if (debug)
                    {
                        ed.WriteMessage(
                            "\nUFLS1DBG: collected {0} open segments, {1} closed loops, {2} circles.",
                            openStructureSegments.Count,
                            structureLoops.Count,
                            structureCircles.Count);
                        ed.WriteMessage(
                            "\nUFLS1DBG: boundary intersections = {0}, open-segment intersections = {1}.",
                            boundaryIntersectionPoints.Count,
                            openIntersectionPoints.Count);
                    }

                    tr.Commit();
                }

                // -------------------------------------------
                // 3b) Build the final pipe on the fitted alignment, not as
                //     a direct connection between the first and last picked
                //     COGO points.  The picked end shots only define the
                //     station limits.  The actual start/end geometry is
                //     projected onto the Civil-style plan-view best-fit line
                //     and then extended along that same fitted line to the
                //     nearest inner wall on each side.
                // -------------------------------------------
                Point3d fittedA = PointOnFittedPipe(centroid, dir, fit, tMinPick);
                Point3d fittedB = PointOnFittedPipe(centroid, dir, fit, tMaxPick);

                Point3d pStart = fittedA;
                Point3d pEnd = fittedB;

                var dir2 = new Vector2d(dir.X, dir.Y);
                bool firstIsA = Distance2dSquared(fittedA, pts[0]) <= Distance2dSquared(fittedB, pts[0]);
                bool startParamHit = false;
                bool endParamHit = false;
                double startParamDistance = 0.0;
                double endParamDistance = 0.0;
                bool startRayHit = false;
                bool endRayHit = false;
                double startRayDistance = 0.0;
                double endRayDistance = 0.0;

                if (dir2.Length >= 1e-9)
                {
                    var dir2Unit = dir2.GetNormal();
                    var startBase2d = new Point2d(fittedA.X, fittedA.Y);
                    var endBase2d = new Point2d(fittedB.X, fittedB.Y);

                    if (TryFindNearestIntersectionAlongRayPriority(
                        closedBoundarySegments,
                        openStructureSegments,
                        structureCircles,
                        startBase2d,
                        dir2Unit.Negate(),
                        out startRayDistance))
                    {
                        startRayHit = true;
                        double tStart = tMinPick - startRayDistance;
                        pStart = PointOnFittedPipe(centroid, dir, fit, tStart);
                    }
                    else
                    {
                        var before = boundaryIntersectionPoints
                            .Concat(openIntersectionPoints)
                            .Select(pt => Dot(VectorFrom(new Point3d(pt.X, pt.Y, centroid.Z), centroid), dir))
                            .Where(t => t < tMinPick)
                            .ToList();
                        if (before.Count > 0)
                        {
                            startParamHit = true;
                            var tBest = before.Max();
                            startParamDistance = tMinPick - tBest;
                            pStart = PointOnFittedPipe(centroid, dir, fit, tBest);
                        }
                    }

                    if (TryFindNearestIntersectionAlongRayPriority(
                        closedBoundarySegments,
                        openStructureSegments,
                        structureCircles,
                        endBase2d,
                        dir2Unit,
                        out endRayDistance))
                    {
                        endRayHit = true;
                        double tEnd = tMaxPick + endRayDistance;
                        pEnd = PointOnFittedPipe(centroid, dir, fit, tEnd);
                    }
                    else
                    {
                        var after = boundaryIntersectionPoints
                            .Concat(openIntersectionPoints)
                            .Select(pt => Dot(VectorFrom(new Point3d(pt.X, pt.Y, centroid.Z), centroid), dir))
                            .Where(t => t > tMaxPick)
                            .ToList();
                        if (after.Count > 0)
                        {
                            endParamHit = true;
                            var tBest = after.Min();
                            endParamDistance = tBest - tMaxPick;
                            pEnd = PointOnFittedPipe(centroid, dir, fit, tBest);
                        }
                    }
                }
                else
                {
                    pStart = PointOnFittedPipe(centroid, dir, fit, tMinPick);
                    pEnd = PointOnFittedPipe(centroid, dir, fit, tMaxPick);
                }

                if (debug)
                {
                    ed.WriteMessage(
                        "\nUFLS1DBG: tMinPick={0:F4}, tMaxPick={1:F4}, firstIsA={2}, startParamHit={3}, endParamHit={4}.",
                        tMinPick,
                        tMaxPick,
                        firstIsA,
                        startParamHit,
                        endParamHit);
                    ed.WriteMessage(
                        "\nUFLS1DBG: startParamDistance={0:F4}, endParamDistance={1:F4}.",
                        startParamDistance,
                        endParamDistance);
                    ed.WriteMessage(
                        "\nUFLS1DBG: centroid=({0:F4}, {1:F4}, {2:F4}), dir=({3:F6}, {4:F6}, {5:F6}).",
                        centroid.X,
                        centroid.Y,
                        centroid.Z,
                        dir.X,
                        dir.Y,
                        dir.Z);
                    ed.WriteMessage(
                        "\nUFLS1DBG: fittedA=({0:F4}, {1:F4}, {2:F4}), fittedB=({3:F4}, {4:F4}, {5:F4}).",
                        fittedA.X,
                        fittedA.Y,
                        fittedA.Z,
                        fittedB.X,
                        fittedB.Y,
                        fittedB.Z);
                    ed.WriteMessage(
                        "\nUFLS1DBG: fitted/default start=({0:F4}, {1:F4}, {2:F4}), end=({3:F4}, {4:F4}, {5:F4}).",
                        pStart.X,
                        pStart.Y,
                        pStart.Z,
                        pEnd.X,
                        pEnd.Y,
                        pEnd.Z);
                }
                if (debug)
                {
                    ed.WriteMessage(
                        "\nUFLS1DBG: ray startHit={0}, startDistance={1:F4}, endHit={2}, endDistance={3:F4}.",
                        startRayHit,
                        startRayDistance,
                        endRayHit,
                        endRayDistance);
                    ed.WriteMessage(
                        "\nUFLS1DBG: final start=({0:F4}, {1:F4}, {2:F4}), end=({3:F4}, {4:F4}, {5:F4}).",
                        pStart.X,
                        pStart.Y,
                        pStart.Z,
                        pEnd.X,
                        pEnd.Y,
                        pEnd.Z);
                }

                // -------------------------------------------
                // 4) Compute 3D start/end points
                // -------------------------------------------
                // -------------------------------------------
                // 5) Create 3D polyline on V-SURV-PIPE-OUTR
                // -------------------------------------------
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayer(db, tr, LAYER_PIPE_OUTER, 6); // magenta-ish

                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(
                        bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    var pts3d = new Point3dCollection { pStart, pEnd };
                    var poly3d = new Polyline3d(Poly3dType.SimplePoly, pts3d, false)
                    {
                        Layer = LAYER_PIPE_OUTER
                    };

                    ms.AppendEntity(poly3d);
                    tr.AddNewlyCreatedDBObject(poly3d, true);

                    tr.Commit();
                }

                ed.WriteMessage(
                    "\nUFLS1: 3D top-of-pipe polyline created on layer {0}.",
                    LAYER_PIPE_OUTER);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUFLS1 error: {ex.Message}");
            }
            finally
            {
                RemoveMarkers(db, markerIds);
            }
        }

// ------------------------------------------------------------
        // UFLS5 – Trim Top of Pipe to Inner Structure (no extend)
        //
        //  * User selects existing UFLS1 top-of-pipe 3D polyline
        //    near the side to trim.
        //  * Determine which end is closer to the pick.
        //  * Work in XY from the START vertex:
        //        s = distance from start in XY, 0 <= s <= segLen.
        //  * Find all intersections with inner-wall segments & circles.
        //      - For start-side trim: pick smallest s > 0.
        //      - For end-side trim:   pick largest  s < segLen.
        //  * Convert selected s to 3D point along the pipe
        //    and rebuild the 3D polyline.
        // ------------------------------------------------------------
        [CommandMethod("UFLS", "UFLS5", CommandFlags.Modal)]
        public static void Ufls5_TrimTopOfPipe()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                var peo = new PromptEntityOptions(
                    "\nSelect UFLS1 top-of-pipe 3D polyline to trim: ");
                peo.SetRejectMessage("\nOnly 3D polylines are allowed.");
                peo.AddAllowedClass(typeof(Polyline3d), exactMatch: false);

                var per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                    return;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var poly = tr.GetObject(per.ObjectId, OpenMode.ForWrite) as Polyline3d;
                    if (poly == null)
                    {
                        ed.WriteMessage("\nUFLS5: Selected entity is not a Polyline3d.");
                        return;
                    }

                    // ---------------------------------------
                    // Get first and last vertex points
                    // ---------------------------------------
                    ObjectId firstVertId = ObjectId.Null;
                    ObjectId lastVertId = ObjectId.Null;

                    foreach (ObjectId vId in poly)
                    {
                        if (firstVertId == ObjectId.Null)
                            firstVertId = vId;
                        lastVertId = vId;
                    }

                    if (firstVertId == ObjectId.Null || lastVertId == ObjectId.Null)
                    {
                        ed.WriteMessage("\nUFLS5: 3D polyline has no vertices.");
                        return;
                    }

                    var vStart = (PolylineVertex3d)tr.GetObject(firstVertId, OpenMode.ForRead);
                    var vEnd = (PolylineVertex3d)tr.GetObject(lastVertId, OpenMode.ForRead);

                    var pStart = vStart.Position;
                    var pEnd = vEnd.Position;

                    // Decide which side to trim based on pick XY distance
                    var pickPt = per.PickedPoint;
                    var pick2D = new Point2d(pickPt.X, pickPt.Y);
                    var start2D = new Point2d(pStart.X, pStart.Y);
                    var end2D = new Point2d(pEnd.X, pEnd.Y);

                    bool trimAtStart =
                        pick2D.GetDistanceTo(start2D) <= pick2D.GetDistanceTo(end2D);

                    // XY direction from start to end
                    var dir2 = end2D - start2D;
                    double segLen = dir2.Length;
                    if (segLen < 1e-9)
                    {
                        ed.WriteMessage("\nUFLS5: 3D polyline has zero XY length.");
                        return;
                    }

                    // ---------------------------------------
                    // Collect structure geometry (segments + circles)
                    // ---------------------------------------
                    var openSegments = new List<(Point2d P0, Point2d P1)>();
                    var closedBoundarySegments = new List<(Point2d P0, Point2d P1)>();
                    var allCircles = new List<(Point2d Center, double Radius)>();
                    var allLoops = new List<List<Point2d>>();
                    CollectInnerStructureGeometry(tr, db, openSegments, closedBoundarySegments, allLoops, allCircles);
                    var allSegments = new List<(Point2d P0, Point2d P1)>();
                    allSegments.AddRange(closedBoundarySegments);
                    allSegments.AddRange(openSegments);

                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(
                        bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    const double tol = 1e-6;

                    // Distances from START in XY to each intersection
                    var segDistances =
                        FindSegmentIntersectionDistancesFromStart(allSegments, pStart, pEnd);
                    var circleDistances =
                        FindCircleIntersectionDistancesFromStart(allCircles, pStart, pEnd);

                    var allDistances = segDistances
                        .Concat(circleDistances)
                        .Where(s => s >= -tol && s <= segLen + tol)
                        .ToList();

                    if (allDistances.Count == 0)
                    {
                        ed.WriteMessage("\nUFLS5: No inner-structure intersection along this pipe segment.");
                        return;
                    }

                    double sBest;

                    if (trimAtStart)
                    {
                        // Trim the start side: use the first intersection after the start
                        var startSide = allDistances
                            .Where(s => s > tol && s <= segLen + tol)
                            .ToList();

                        if (startSide.Count == 0)
                        {
                            ed.WriteMessage("\nUFLS5: No intersection found between start and end for start-side trim.");
                            return;
                        }

                        sBest = startSide.Min();
                    }
                    else
                    {
                        // Trim the end side: use the last intersection before the end
                        var endSide = allDistances
                            .Where(s => s >= -tol && s < segLen - tol)
                            .ToList();

                        if (endSide.Count == 0)
                        {
                            ed.WriteMessage("\nUFLS5: No intersection found between start and end for end-side trim.");
                            return;
                        }

                        sBest = endSide.Max();
                    }

                    // Convert distance sBest into 3D point along the pipe
                    var dir3D = pEnd - pStart;
                    double ratio = sBest / segLen;
                    var newPoint3D = pStart + dir3D.MultiplyBy(ratio);

                    Point3d newStart, newEnd;

                    if (trimAtStart)
                    {
                        newStart = newPoint3D;
                        newEnd = pEnd;        // keep original end
                    }
                    else
                    {
                        newStart = pStart;      // keep original start
                        newEnd = newPoint3D;
                    }

                    // ---------------------------------------
                    // Recreate 3D polyline with trimmed ends
                    // ---------------------------------------
                    ms.UpgradeOpen();

                    // Save original properties
                    var origLayer = poly.Layer;
                    var origLinetypeId = poly.LinetypeId;
                    var origColor = poly.Color;
                    var origLineWeight = poly.LineWeight;
                    var origLtScale = poly.LinetypeScale;
                    var origTransparency = poly.Transparency;

                    var newPts = new Point3dCollection { newStart, newEnd };
                    var newPoly = new Polyline3d(Poly3dType.SimplePoly, newPts, false)
                    {
                        Layer = origLayer,
                        LinetypeId = origLinetypeId,
                        Color = origColor,
                        LineWeight = origLineWeight,
                        LinetypeScale = origLtScale,
                        Transparency = origTransparency
                    };

                    ms.AppendEntity(newPoly);
                    tr.AddNewlyCreatedDBObject(newPoly, true);

                    // Remove original pipe
                    poly.Erase();

                    tr.Commit();
                    ed.WriteMessage("\nUFLS5: Top-of-pipe trimmed to nearest inner structure on that side.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUFLS5 error: {ex.Message}");
            }
        }

        // ------------------------------------------------------------
        // 1) COGO selection + numbered markers
        // ------------------------------------------------------------
        private static List<Point3d> GetPipePointsFromCogo(
            Editor ed,
            Database db,
            List<ObjectId> markerIds)
        {
            const double autoPanMaxHalfWidthFeet = 8.0;
            const double autoPanMinHalfWidthFeet = 3.0;
            const double clusterAlongFeet = 12.0;
            const double minAlongFeet = 0.50;

            var pts = new List<Point3d>();
            var selectedPointIds = new HashSet<ObjectId>();
            bool first = true;
            bool zoomedAhead = false;
            double? autoPanViewWidth = null;
            double? autoPanViewHeight = null;

            while (true)
            {
                if (pts.Count >= 2)
                {
                    double dynamicHalfWidthFeet = ComputeDynamicAutoPanHalfWidth(
                        ed,
                        pts,
                        autoPanMinHalfWidthFeet,
                        autoPanMaxHalfWidthFeet);

                    zoomedAhead = TryAutoPanToNextTopOfPipeGroup(
                        ed,
                        db,
                        pts,
                        selectedPointIds,
                        markerIds,
                        dynamicHalfWidthFeet,
                        clusterAlongFeet,
                        minAlongFeet,
                        autoPanViewWidth,
                        autoPanViewHeight);
                }
                else
                {
                    zoomedAhead = false;
                }

                var peo = new PromptEntityOptions(
                    first
                        ? "\nSelect COGO point for top-of-pipe (2+). Enter to finish: "
                        : "\nSelect next COGO point. Enter to finish: ");

                peo.SetRejectMessage("\nOnly COGO points are allowed.");
                peo.AddAllowedClass(typeof(CogoPoint), exactMatch: false);

                var per = ed.GetEntity(peo);

                if (per.Status != PromptStatus.OK)
                    break;

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var cp = (CogoPoint)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                    var loc = cp.Location;

                    if (!autoPanViewWidth.HasValue || !autoPanViewHeight.HasValue)
                    {
                        var curView = ed.GetCurrentView();
                        autoPanViewWidth = curView.Width;
                        autoPanViewHeight = curView.Height;
                    }

                    pts.Add(loc);
                    selectedPointIds.Add(per.ObjectId);

                    // Create big numbered text marker at this location
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(
                        bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    var view = ed.GetCurrentView();
                    double textHeight = Math.Max(view.Height * 0.03, 0.5);

                    var txt = new DBText
                    {
                        Position = loc,
                        Height = textHeight,
                        TextString = pts.Count.ToString(),
                        Layer = LAYER_PICK_MARKER
                    };

                    txt.Justify = AttachmentPoint.MiddleCenter;
                    txt.AlignmentPoint = loc;

                    ms.AppendEntity(txt);
                    tr.AddNewlyCreatedDBObject(txt, true);
                    markerIds.Add(txt.ObjectId);

                    tr.Commit();
                }

                first = false;
            }

            return pts;
        }

        private static bool TryAutoPanToNextTopOfPipeGroup(
            Editor ed,
            Database db,
            List<Point3d> pickedPoints,
            HashSet<ObjectId> selectedPointIds,
            List<ObjectId> markerIds,
            double searchHalfWidthFeet,
            double clusterAlongFeet,
            double minAlongFeet,
            double? targetViewWidth,
            double? targetViewHeight)
        {
            if (pickedPoints == null || pickedPoints.Count < 2)
                return false;

            var prev = pickedPoints[pickedPoints.Count - 2];
            var last = pickedPoints[pickedPoints.Count - 1];

            var dir2 = new Vector2d(last.X - prev.X, last.Y - prev.Y);
            if (dir2.Length < 1e-9)
                return false;

            dir2 = dir2.GetNormal();

            var candidates = new List<(ObjectId Id, Point3d Pt, double Along, double Offset)>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    if (!id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(CogoPoint))))
                        continue;

                    if (selectedPointIds.Contains(id))
                        continue;

                    var cp = tr.GetObject(id, OpenMode.ForRead) as CogoPoint;
                    if (cp == null)
                        continue;

                    var pt = cp.Location;
                    var vec = new Vector2d(pt.X - last.X, pt.Y - last.Y);

                    double along = vec.DotProduct(dir2);
                    if (along <= minAlongFeet)
                        continue;

                    double offset = Math.Abs((dir2.X * vec.Y) - (dir2.Y * vec.X));
                    if (offset > searchHalfWidthFeet)
                        continue;

                    candidates.Add((id, pt, along, offset));
                }

                tr.Commit();
            }

            if (candidates.Count == 0)
                return false;

            candidates.Sort((a, b) => a.Along.CompareTo(b.Along));

            double firstAlong = candidates[0].Along;
            double maxAlong = firstAlong + Math.Max(clusterAlongFeet, searchHalfWidthFeet * 2.0);

            var group = candidates
                .Where(c => c.Along <= maxAlong)
                .ToList();

            if (group.Count == 0)
                return false;

            CreateOrReplaceAutoPanGuideLine(db, markerIds, last, dir2, group.Max(g => g.Along) + 5.0);
            ZoomToTopOfPipeGroup(ed, last, dir2, group, searchHalfWidthFeet, targetViewWidth, targetViewHeight);
            return true;
        }

        private static void CreateOrReplaceAutoPanGuideLine(
            Database db,
            List<ObjectId> markerIds,
            Point3d startPoint,
            Vector2d dir2,
            double forwardLengthFeet)
        {
            if (markerIds == null)
                return;

            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayer(db, tr, LAYER_PICK_MARKER, 2);

                    for (int i = markerIds.Count - 1; i >= 0; i--)
                    {
                        var id = markerIds[i];
                        if (!id.IsValid || id.IsErased)
                            continue;

                        var ent = tr.GetObject(id, OpenMode.ForRead, false) as AcEntity;
                        if (ent is Line line && string.Equals(line.Layer, LAYER_PICK_MARKER, StringComparison.OrdinalIgnoreCase))
                        {
                            ent.UpgradeOpen();
                            ent.Erase();
                            markerIds.RemoveAt(i);
                        }
                    }

                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    var endPoint = new Point3d(
                        startPoint.X + (dir2.X * forwardLengthFeet),
                        startPoint.Y + (dir2.Y * forwardLengthFeet),
                        startPoint.Z);

                    var guide = new Line(startPoint, endPoint)
                    {
                        Layer = LAYER_PICK_MARKER
                    };

                    ms.AppendEntity(guide);
                    tr.AddNewlyCreatedDBObject(guide, true);
                    markerIds.Add(guide.ObjectId);

                    tr.Commit();
                }
            }
            catch
            {
                // ignore guide-line creation issues
            }
        }

        private static void ZoomToTopOfPipeGroup(
            Editor ed,
            Point3d startPoint,
            Vector2d dir2,
            List<(ObjectId Id, Point3d Pt, double Along, double Offset)> group,
            double searchHalfWidthFeet,
            double? targetViewWidth,
            double? targetViewHeight)
        {
            if (group == null || group.Count == 0)
                return;

            double firstAlong = group.Min(g => g.Along);
            double lastAlong = group.Max(g => g.Along);
            double centerAlong = (firstAlong + lastAlong) * 0.5;

            var center = new Point3d(
                startPoint.X + (dir2.X * centerAlong),
                startPoint.Y + (dir2.Y * centerAlong),
                startPoint.Z);

            if (targetViewWidth.HasValue && targetViewHeight.HasValue &&
                targetViewWidth.Value > 1e-6 && targetViewHeight.Value > 1e-6)
            {
                ZoomCenterByFixedView(ed, center, targetViewWidth.Value, targetViewHeight.Value);
                return;
            }

            double longSide = Math.Max(12.0, (lastAlong - firstAlong) + 10.0);
            double shortSide = Math.Max(10.0, (searchHalfWidthFeet * 2.0) + 6.0);

            ViewState.ZoomCenterByRect(center, longSide, shortSide);
        }

        private static double ComputeDynamicAutoPanHalfWidth(
            Editor ed,
            List<Point3d> pickedPoints,
            double minHalfWidthFeet,
            double maxHalfWidthFeet)
        {
            double dynamicHalfWidth = 5.0;

            try
            {
                var view = ed.GetCurrentView();
                dynamicHalfWidth = Math.Max(dynamicHalfWidth, view.Height * 0.18);
            }
            catch
            {
                // Keep fallback.
            }

            if (pickedPoints != null && pickedPoints.Count >= 2)
            {
                var prev = pickedPoints[pickedPoints.Count - 2];
                var last = pickedPoints[pickedPoints.Count - 1];
                double segLen = prev.DistanceTo(last);

                dynamicHalfWidth = Math.Max(dynamicHalfWidth, segLen * 0.20);

                if (pickedPoints.Count >= 3)
                {
                    var prev2 = pickedPoints[pickedPoints.Count - 3];
                    double segLen2 = prev2.DistanceTo(prev);
                    double avgSeg = (segLen + segLen2) * 0.5;
                    dynamicHalfWidth = Math.Max(dynamicHalfWidth, avgSeg * 0.18);
                }
            }

            if (dynamicHalfWidth < minHalfWidthFeet)
                dynamicHalfWidth = minHalfWidthFeet;
            if (dynamicHalfWidth > maxHalfWidthFeet)
                dynamicHalfWidth = maxHalfWidthFeet;

            return dynamicHalfWidth;
        }

        private static void ZoomCenterByFixedView(
            Editor ed,
            Point3d centerWcs,
            double viewWidth,
            double viewHeight)
        {
            if (viewWidth <= 1e-6 || viewHeight <= 1e-6)
                return;

            Matrix3d wcsToUcs = ed.CurrentUserCoordinateSystem.Inverse();
            Point3d cUcs = centerWcs.TransformBy(wcsToUcs);

            ViewTableRecord view = ed.GetCurrentView();
            view.CenterPoint = new Point2d(cUcs.X, cUcs.Y);
            view.Width = viewWidth;
            view.Height = viewHeight;
            ed.SetCurrentView(view);
        }

        // ------------------------------------------------------------
        // 2) Centroid & principal direction (PCA)
        // ------------------------------------------------------------
        private static Point3d ComputeCentroid(List<Point3d> pts)
        {
            double sx = 0, sy = 0, sz = 0;
            foreach (var p in pts)
            {
                sx += p.X;
                sy += p.Y;
                sz += p.Z;
            }

            int n = pts.Count;
            return new Point3d(sx / n, sy / n, sz / n);
        }

        private sealed record TopOfPipeFit(
            Point3d Centroid,
            Vector3d Direction,
            double ElevationIntercept,
            double ElevationSlope);

        private static TopOfPipeFit ComputeTopOfPipeBestFit(List<Point3d> pts)
        {
            if (pts == null || pts.Count == 0)
                return new TopOfPipeFit(Point3d.Origin, new Vector3d(1, 0, 0), 0.0, 0.0);

            var centroid = ComputeCentroid(pts);
            Vector2d dir2 = PrincipalDirection2d(pts, centroid);
            var dirForStation = new Vector3d(dir2.X, dir2.Y, 0.0);

            double meanStation = 0.0;
            double meanZ = 0.0;
            var stations = new List<double>(pts.Count);

            foreach (var p in pts)
            {
                double station = Dot(VectorFrom(p, centroid), dirForStation);
                stations.Add(station);
                meanStation += station;
                meanZ += p.Z;
            }

            meanStation /= pts.Count;
            meanZ /= pts.Count;

            double numerator = 0.0;
            double denominator = 0.0;
            for (int i = 0; i < pts.Count; i++)
            {
                double ds = stations[i] - meanStation;
                numerator += ds * (pts[i].Z - meanZ);
                denominator += ds * ds;
            }

            double slope = Math.Abs(denominator) > 1e-12 ? numerator / denominator : 0.0;
            double intercept = meanZ - slope * meanStation;

            // Keep the UFLS1 direction plan-view only so structure intersections
            // and stationing match Civil 3D's Best Fit Line in XY. Elevation is
            // applied separately from the station/elevation regression.
            var dir = new Vector3d(dir2.X, dir2.Y, 0.0).GetNormal();
            return new TopOfPipeFit(centroid, dir, intercept, slope);
        }

        private static Vector2d PrincipalDirection2d(List<Point3d> pts, Point3d centroid)
        {
            double sxx = 0.0;
            double sxy = 0.0;
            double syy = 0.0;

            foreach (var p in pts)
            {
                double dx = p.X - centroid.X;
                double dy = p.Y - centroid.Y;
                sxx += dx * dx;
                sxy += dx * dy;
                syy += dy * dy;
            }

            if (Math.Abs(sxx) < 1e-12 && Math.Abs(syy) < 1e-12)
                return new Vector2d(1.0, 0.0);

            double angle = 0.5 * Math.Atan2(2.0 * sxy, sxx - syy);
            var dir = new Vector2d(Math.Cos(angle), Math.Sin(angle));
            return dir.Length > 1e-12 ? dir.GetNormal() : new Vector2d(1.0, 0.0);
        }

        private static Point3d PointOnFittedPipe(Point3d centroid, Vector3d dir, TopOfPipeFit fit, double t)
        {
            var xyDir = new Vector2d(dir.X, dir.Y);
            if (xyDir.Length < 1e-12)
            {
                var p = centroid + dir.MultiplyBy(t);
                return new Point3d(p.X, p.Y, fit.ElevationIntercept + fit.ElevationSlope * t);
            }

            xyDir = xyDir.GetNormal();
            double x = centroid.X + xyDir.X * t;
            double y = centroid.Y + xyDir.Y * t;
            double z = fit.ElevationIntercept + fit.ElevationSlope * t;
            return new Point3d(x, y, z);
        }

        private static Vector3d VectorFrom(Point3d p, Point3d origin)
        {
            return new Vector3d(
                p.X - origin.X,
                p.Y - origin.Y,
                p.Z - origin.Z);
        }

        private static double Dot(Vector3d a, Vector3d b)
        {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }


        // ------------------------------------------------------------
        // 2b) Grade-break review helpers
        // ------------------------------------------------------------
        private sealed record TopOfPipeSegmentSlope(
            int SegmentNumber,
            int FromPickNumber,
            int ToPickNumber,
            double LengthFeet,
            double DeltaElevationFeet,
            double SlopePercent);

        private sealed record TopOfPipeGradeBreakCandidate(
            int BreakPickNumber,
            int BeforeSegmentNumber,
            int AfterSegmentNumber,
            Point3d BreakPoint,
            double SlopeBeforePercent,
            double SlopeAfterPercent,
            double SlopeChangePercent,
            bool IsNoticeable);

        private sealed record TopOfPipeGradeBreakReport(
            List<TopOfPipeSegmentSlope> Segments,
            List<TopOfPipeGradeBreakCandidate> Candidates,
            TopOfPipeFit OverallFit)
        {
            public bool HasGradeBreak => Candidates.Any(c => c.IsNoticeable);

            public IEnumerable<TopOfPipeGradeBreakCandidate> NoticeableCandidates => Candidates
                .Where(c => c.IsNoticeable)
                .OrderBy(c => c.BreakPickNumber);

            public TopOfPipeGradeBreakCandidate? BestBreak => Candidates
                .Where(c => c.IsNoticeable)
                .OrderByDescending(c => c.SlopeChangePercent)
                .FirstOrDefault();

        }

        private static TopOfPipeGradeBreakReport AnalyzeTopOfPipeGradeBreaks(
            List<Point3d> pts,
            Point3d centroid,
            Vector3d dir,
            TopOfPipeFit fit)
        {
            var segments = new List<TopOfPipeSegmentSlope>();
            var candidates = new List<TopOfPipeGradeBreakCandidate>();

            if (pts == null || pts.Count < 3)
                return new TopOfPipeGradeBreakReport(segments, candidates, fit);

            for (int i = 0; i < pts.Count - 1; i++)
            {
                var a = pts[i];
                var b = pts[i + 1];

                double dx = b.X - a.X;
                double dy = b.Y - a.Y;
                double length = Math.Sqrt((dx * dx) + (dy * dy));
                double dz = b.Z - a.Z;
                double slopePercent = length > 1e-9 ? (dz / length) * 100.0 : 0.0;

                segments.Add(new TopOfPipeSegmentSlope(
                    i + 1,
                    i + 1,
                    i + 2,
                    length,
                    dz,
                    slopePercent));
            }

            for (int i = 0; i < segments.Count - 1; i++)
            {
                var before = segments[i];
                var after = segments[i + 1];
                double slopeChange = Math.Abs(after.SlopePercent - before.SlopePercent);
                bool noticeable = slopeChange >= GradeBreakWarnChangePercent;

                candidates.Add(new TopOfPipeGradeBreakCandidate(
                    BreakPickNumber: i + 2,
                    BeforeSegmentNumber: before.SegmentNumber,
                    AfterSegmentNumber: after.SegmentNumber,
                    BreakPoint: pts[i + 1],
                    SlopeBeforePercent: before.SlopePercent,
                    SlopeAfterPercent: after.SlopePercent,
                    SlopeChangePercent: slopeChange,
                    IsNoticeable: noticeable));
            }

            return new TopOfPipeGradeBreakReport(segments, candidates, fit);
        }

        private static int CreateGradeBreakRedlineBoxes(Database db, TopOfPipeGradeBreakReport report)
        {
            if (report == null || !report.HasGradeBreak)
                return 0;

            int created = 0;

            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayer(db, tr, LAYER_GRADE_BREAK_REDLINE, 1);

                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    double half = GradeBreakRedlineBoxSizeFeet * 0.5;

                    foreach (var candidate in report.NoticeableCandidates)
                    {
                        Point3d p = candidate.BreakPoint;

                        var box = new Polyline();
                        box.SetDatabaseDefaults();
                        box.Layer = LAYER_GRADE_BREAK_REDLINE;
                        box.AddVertexAt(0, new Point2d(p.X - half, p.Y - half), 0.0, 0.0, 0.0);
                        box.AddVertexAt(1, new Point2d(p.X + half, p.Y - half), 0.0, 0.0, 0.0);
                        box.AddVertexAt(2, new Point2d(p.X + half, p.Y + half), 0.0, 0.0, 0.0);
                        box.AddVertexAt(3, new Point2d(p.X - half, p.Y + half), 0.0, 0.0, 0.0);
                        box.Closed = true;

                        ms.AppendEntity(box);
                        tr.AddNewlyCreatedDBObject(box, true);
                        created++;

                        var label = new DBText
                        {
                            Position = new Point3d(p.X + half + 0.5, p.Y + half + 0.5, p.Z),
                            Height = Math.Max(GradeBreakRedlineBoxSizeFeet * 0.35, 1.0),
                            TextString = $"GB {candidate.BreakPickNumber}",
                            Layer = LAYER_GRADE_BREAK_REDLINE
                        };

                        ms.AppendEntity(label);
                        tr.AddNewlyCreatedDBObject(label, true);
                    }

                    tr.Commit();
                }
            }
            catch
            {
                return created;
            }

            return created;
        }

        private static void ZoomToPickedPointExtents(Editor ed, List<Point3d> pts)
        {
            if (pts == null || pts.Count == 0)
                return;

            try
            {
                double minX = pts.Min(p => p.X);
                double maxX = pts.Max(p => p.X);
                double minY = pts.Min(p => p.Y);
                double maxY = pts.Max(p => p.Y);

                double width = Math.Max(maxX - minX, GradeBreakRedlineBoxSizeFeet * 4.0);
                double height = Math.Max(maxY - minY, GradeBreakRedlineBoxSizeFeet * 4.0);

                var center = new Point3d(
                    (minX + maxX) * 0.5,
                    (minY + maxY) * 0.5,
                    pts.Average(p => p.Z));

                ViewState.ZoomCenterByRect(center, width, height);
            }
            catch
            {
                // Do not let review zoom issues stop the command.
            }
        }

        private sealed class TopOfPipeGradeBreakReviewForm : System.Windows.Forms.Form
        {
            private TopOfPipeGradeBreakReviewForm(
                TopOfPipeGradeBreakReport report,
                int redlineCount)
            {

                Text = "TOP OF PIPE - GRADE BREAK REVIEW";
                Width = 760;
                Height = 560;
                MinimizeBox = false;
                MaximizeBox = false;
                FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
                StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;

                var header = new System.Windows.Forms.Label
                {
                    Left = 12,
                    Top = 12,
                    Width = 720,
                    Height = 48,
                    Text = $"Grade change(s) exceeded {FormatSlopePercent(GradeBreakWarnChangePercent)}. The picked point extents were zoomed and {redlineCount} redline box(es) were placed on V-SURV-RDLN. Review the flagged points or create the pipe anyway.",
                    AutoSize = false
                };

                var text = new System.Windows.Forms.TextBox
                {
                    Left = 12,
                    Top = 66,
                    Width = 720,
                    Height = 390,
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = System.Windows.Forms.ScrollBars.Both,
                    WordWrap = false,
                    Text = BuildReportText(report)
                };

                var continueButton = new System.Windows.Forms.Button
                {
                    Text = "CREATE PIPE ANYWAY",
                    Left = 440,
                    Top = 468,
                    Width = 130,
                    Height = 30,
                    DialogResult = System.Windows.Forms.DialogResult.OK
                };

                var cancelButton = new System.Windows.Forms.Button
                {
                    Text = "CANCEL / REVIEW POINTS",
                    Left = 590,
                    Top = 468,
                    Width = 142,
                    Height = 30,
                    DialogResult = System.Windows.Forms.DialogResult.Cancel
                };

                Controls.Add(header);
                Controls.Add(text);
                Controls.Add(continueButton);
                Controls.Add(cancelButton);

                AcceptButton = continueButton;
                CancelButton = cancelButton;
            }

            public static System.Windows.Forms.DialogResult ShowReview(
                TopOfPipeGradeBreakReport report,
                int redlineCount)
            {
                using var form = new TopOfPipeGradeBreakReviewForm(report, redlineCount);
                return form.ShowDialog();
            }

            private static string BuildReportText(TopOfPipeGradeBreakReport report)
            {
                var lines = new List<string>();
                var flagged = report.NoticeableCandidates.ToList();

                lines.Add("TOP OF PIPE GRADE BREAK REVIEW");
                lines.Add("------------------------------------------------------------");
                lines.Add($"Threshold: grade change >= {FormatSlopePercent(GradeBreakWarnChangePercent)}");
                lines.Add($"Flagged pick point(s): {string.Join(", ", flagged.Select(c => c.BreakPickNumber.ToString(CultureInfo.InvariantCulture)))}");
                lines.Add(string.Empty);
                lines.Add("GRADE CHANGES EXCEEDING THRESHOLD");
                lines.Add("Pick   Slope In   Slope Out   Change    Segments");
                lines.Add("----   --------   ---------   -------   --------");

                foreach (var candidate in flagged)
                {
                    lines.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0,4}   {1,8}   {2,9}   {3,7}   {4}->{5}",
                        candidate.BreakPickNumber,
                        FormatSlopePercent(candidate.SlopeBeforePercent),
                        FormatSlopePercent(candidate.SlopeAfterPercent),
                        FormatSlopePercent(candidate.SlopeChangePercent),
                        candidate.BeforeSegmentNumber,
                        candidate.AfterSegmentNumber));
                }

                lines.Add(string.Empty);
                lines.Add("SEGMENT SLOPES FROM PICKED POINT ORDER");
                lines.Add("Seg  Picks      Length(ft)   Delta Z(ft)   Slope");
                lines.Add("---  ---------  ----------   -----------   -------");

                foreach (var segment in report.Segments)
                {
                    lines.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0,3}  {1,2}->{2,-2}      {3,8:0.00}   {4,11:0.000}   {5,7}",
                        segment.SegmentNumber,
                        segment.FromPickNumber,
                        segment.ToPickNumber,
                        segment.LengthFeet,
                        segment.DeltaElevationFeet,
                        FormatSlopePercent(segment.SlopePercent)));
                }

                return string.Join(Environment.NewLine, lines);
            }
        }

        private static string FormatSlopePercent(double slopePercent)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.00}%", slopePercent);
        }

        // ------------------------------------------------------------
        // 3) Structure inner-wall segments / circles / intersections
        // ------------------------------------------------------------
        private static string NormalizeLayerName(string? layerName)
        {
            if (string.IsNullOrWhiteSpace(layerName))
                return string.Empty;

            string value = layerName.Trim();
            int pipeIndex = value.LastIndexOf('|');
            if (pipeIndex >= 0 && pipeIndex < value.Length - 1)
                value = value.Substring(pipeIndex + 1);

            return value.Trim();
        }

        private static bool IsLayerMatch(string? entityLayer, string? inheritedLayer)
        {
            string normalizedEntityLayer = NormalizeLayerName(entityLayer);
            return !string.IsNullOrWhiteSpace(normalizedEntityLayer) &&
                   normalizedEntityLayer.Equals(LAYER_STRUCT_INNER, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsInnerStructurePolyline(Polyline pl, string? inheritedLayer)
        {
            return IsLayerMatch(pl.Layer, inheritedLayer);
        }

        private static bool IsInnerStructureCircle(Circle c, string? inheritedLayer)
        {
            return IsLayerMatch(c.Layer, inheritedLayer);
        }

        private static bool IsInnerStructureLine(Line ln, string? inheritedLayer)
        {
            return IsLayerMatch(ln.Layer, inheritedLayer);
        }

        private static bool IsInnerStructureArc(Arc arc, string? inheritedLayer)
        {
            return IsLayerMatch(arc.Layer, inheritedLayer);
        }

        private static bool IsInnerStructureXline(Xline xline, string? inheritedLayer)
        {
            return IsLayerMatch(xline.Layer, inheritedLayer);
        }

        private static bool IsInnerStructureRay(Ray ray, string? inheritedLayer)
        {
            return IsLayerMatch(ray.Layer, inheritedLayer);
        }

        private static void CollectInnerStructureGeometry(
            Transaction tr,
            Database db,
            List<(Point2d P0, Point2d P1)> openSegments,
            List<(Point2d P0, Point2d P1)> closedBoundarySegments,
            List<List<Point2d>> closedLoops,
            List<(Point2d Center, double Radius)> circles)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(
                bt[BlockTableRecord.ModelSpace],
                OpenMode.ForRead);

            var visitedBlockDefs = new HashSet<ObjectId>();
            var rawOpenSegments = new List<(Point2d P0, Point2d P1)>();
            var rawClosedLoops = new List<List<Point2d>>();
            var rawCircles = new List<(Point2d Center, double Radius)>();

            foreach (ObjectId id in ms)
            {
                CollectInnerStructureGeometryFromEntity(
                    tr,
                    id,
                    Matrix3d.Identity,
                    inheritedLayer: null,
                    rawOpenSegments,
                    rawClosedLoops,
                    rawCircles,
                    visitedBlockDefs);
            }

            var loopKeep = Enumerable.Repeat(true, rawClosedLoops.Count).ToArray();
            var circleKeep = Enumerable.Repeat(true, rawCircles.Count).ToArray();
            const double containmentTol = 1e-4;

            for (int i = 0; i < rawClosedLoops.Count; i++)
            {
                for (int j = 0; j < rawClosedLoops.Count; j++)
                {
                    if (i == j || !loopKeep[i])
                        continue;

                    if (LoopInsideLoop(rawClosedLoops[i], rawClosedLoops[j], containmentTol))
                        loopKeep[i] = false;
                }

                for (int j = 0; j < rawCircles.Count; j++)
                {
                    if (!loopKeep[i])
                        continue;

                    if (LoopInsideCircle(rawClosedLoops[i], rawCircles[j], containmentTol))
                        loopKeep[i] = false;
                }
            }

            for (int i = 0; i < rawCircles.Count; i++)
            {
                for (int j = 0; j < rawCircles.Count; j++)
                {
                    if (i == j || !circleKeep[i])
                        continue;

                    if (CircleInsideCircle(rawCircles[i], rawCircles[j], containmentTol))
                        circleKeep[i] = false;
                }

                for (int j = 0; j < rawClosedLoops.Count; j++)
                {
                    if (!circleKeep[i])
                        continue;

                    if (CircleInsideLoop(rawCircles[i], rawClosedLoops[j], containmentTol))
                        circleKeep[i] = false;
                }
            }

            for (int i = 0; i < rawClosedLoops.Count; i++)
            {
                if (!loopKeep[i])
                    continue;

                var loop = rawClosedLoops[i];
                if (loop.Count < 3)
                    continue;

                closedLoops.Add(loop);
                AddLoopSegments(loop, closedBoundarySegments);
            }

            for (int i = 0; i < rawCircles.Count; i++)
            {
                if (circleKeep[i])
                    circles.Add(rawCircles[i]);
            }

            foreach (var segment in rawOpenSegments)
            {
                // Keep open LINE/LWPOLYLINE/ARC-derived stop geometry even when its midpoint falls
                // inside a retained loop/circle. The midpoint-inside filter was discarding valid
                // user-drawn test lines/polylines that crossed the pipe farther from the picked end,
                // which made straight UFLS1 appear to only work when those objects were placed very
                // close to the endpoint. Let the actual ray/segment intersection decide validity.
                openSegments.Add(segment);
            }
        }

        private static void CollectInnerStructureGeometryFromEntity(
            Transaction tr,
            ObjectId entityId,
            Matrix3d xform,
            string? inheritedLayer,
            List<(Point2d P0, Point2d P1)> segments,
            List<List<Point2d>> closedLoops,
            List<(Point2d Center, double Radius)> circles,
            HashSet<ObjectId> visitedBlockDefs)
        {
            var className = entityId.ObjectClass.DxfName.ToUpperInvariant();

            if (className == "LWPOLYLINE")
            {
                var pl = (Polyline)tr.GetObject(entityId, OpenMode.ForRead);
                if (IsInnerStructurePolyline(pl, inheritedLayer))
                    AddPolylineGeometry(pl, xform, segments, closedLoops);

                return;
            }

            if (className == "CIRCLE")
            {
                var c = (Circle)tr.GetObject(entityId, OpenMode.ForRead);
                if (IsInnerStructureCircle(c, inheritedLayer))
                    AddCircleFootprint(c, xform, circles);

                return;
            }

            if (className == "LINE")
            {
                var ln = (Line)tr.GetObject(entityId, OpenMode.ForRead);
                if (IsInnerStructureLine(ln, inheritedLayer))
                    AddLineGeometry(ln, xform, segments);

                return;
            }

            if (className == "ARC")
            {
                var arc = (Arc)tr.GetObject(entityId, OpenMode.ForRead);
                if (IsInnerStructureArc(arc, inheritedLayer))
                    AddArcGeometry(arc, xform, segments);

                return;
            }

            if (className == "XLINE")
            {
                var xline = (Xline)tr.GetObject(entityId, OpenMode.ForRead);
                if (IsInnerStructureXline(xline, inheritedLayer))
                    AddXlineGeometry(xline, xform, segments);

                return;
            }

            if (className == "RAY")
            {
                var ray = (Ray)tr.GetObject(entityId, OpenMode.ForRead);
                if (IsInnerStructureRay(ray, inheritedLayer))
                    AddRayGeometry(ray, xform, segments);

                return;
            }

            if (className != "INSERT")
                return;

            var br = (BlockReference)tr.GetObject(entityId, OpenMode.ForRead);
            if (br.BlockTableRecord.IsNull)
                return;

            ObjectId blockDefId = br.BlockTableRecord;
            if (!visitedBlockDefs.Add(blockDefId))
                return;

            try
            {
                var btr = (BlockTableRecord)tr.GetObject(blockDefId, OpenMode.ForRead);
                var nextXform = br.BlockTransform * xform;
                string? nextInheritedLayer = string.IsNullOrWhiteSpace(br.Layer) ? inheritedLayer : br.Layer;

                foreach (ObjectId childId in btr)
                {
                    CollectInnerStructureGeometryFromEntity(
                        tr,
                        childId,
                        nextXform,
                        nextInheritedLayer,
                        segments,
                        closedLoops,
                        circles,
                        visitedBlockDefs);
                }
            }
            finally
            {
                visitedBlockDefs.Remove(blockDefId);
            }
        }

        /// <summary>
        /// Converts a polyline into world-space 2D geometry using xform.
        /// Bulged segments are tessellated so circular manholes drawn as
        /// arc-polylines still intersect correctly.
        /// Closed polylines are stored as loops and filtered later so
        /// interior block decoration does not become a false pipe stop.
        /// </summary>
        private static void AddPolylineGeometry(
            Polyline pl,
            Matrix3d xform,
            List<(Point2d P0, Point2d P1)> segments,
            List<List<Point2d>> closedLoops)
        {
            int n = pl.NumberOfVertices;
            if (n < 2)
                return;

            var loopPts = new List<Point2d>();
            int segCount = pl.Closed ? n : n - 1;

            for (int i = 0; i < segCount; i++)
            {
                var p0w3 = pl.GetPoint3dAt(i).TransformBy(xform);
                var p1w3 = pl.GetPoint3dAt((i + 1) % n).TransformBy(xform);

                var p0w = new Point2d(p0w3.X, p0w3.Y);
                var p1w = new Point2d(p1w3.X, p1w3.Y);

                if (pl.Closed && loopPts.Count == 0)
                    loopPts.Add(p0w);

                double bulge = pl.GetBulgeAt(i);
                if (Math.Abs(bulge) < 1e-9)
                {
                    if (pl.Closed)
                    {
                        loopPts.Add(p1w);
                    }
                    else
                    {
                        AddSegmentIfValid(segments, p0w, p1w);
                    }

                    continue;
                }

                var arcPts = TessellateBulgeArc(p0w, p1w, bulge);
                if (pl.Closed)
                {
                    for (int j = 1; j < arcPts.Count; j++)
                        loopPts.Add(arcPts[j]);
                }
                else
                {
                    for (int j = 1; j < arcPts.Count; j++)
                        AddSegmentIfValid(segments, arcPts[j - 1], arcPts[j]);
                }
            }

            if (pl.Closed && loopPts.Count >= 3)
            {
                if (loopPts[0].GetDistanceTo(loopPts[loopPts.Count - 1]) <= 1e-6)
                    loopPts.RemoveAt(loopPts.Count - 1);

                if (loopPts.Count >= 3)
                    closedLoops.Add(loopPts);
            }
        }


        private static void AddLineGeometry(
            Line ln,
            Matrix3d xform,
            List<(Point2d P0, Point2d P1)> segments)
        {
            var p0w3 = ln.StartPoint.TransformBy(xform);
            var p1w3 = ln.EndPoint.TransformBy(xform);
            var p0w = new Point2d(p0w3.X, p0w3.Y);
            var p1w = new Point2d(p1w3.X, p1w3.Y);
            AddSegmentIfValid(segments, p0w, p1w);
        }

        private static void AddArcGeometry(
            Arc arc,
            Matrix3d xform,
            List<(Point2d P0, Point2d P1)> segments)
        {
            var centerW3 = arc.Center.TransformBy(xform);
            var centerW = new Point2d(centerW3.X, centerW3.Y);

            var cs = xform.CoordinateSystem3d;
            double scale = cs.Xaxis.Length;
            if (double.IsNaN(scale) || double.IsInfinity(scale) || scale < 1e-9)
                scale = 1.0;

            double radius = arc.Radius * scale;
            if (double.IsNaN(radius) || double.IsInfinity(radius) || radius < 1e-9)
                return;

            double startAngle = arc.StartAngle;
            double sweep = NormalizePositiveAngle(arc.EndAngle - arc.StartAngle);
            if (sweep < 1e-9)
                return;

            int subdivisions = Math.Max(8, (int)Math.Ceiling(radius * sweep / 0.50));
            var prev = new Point2d(
                centerW.X + Math.Cos(startAngle) * radius,
                centerW.Y + Math.Sin(startAngle) * radius);

            for (int i = 1; i <= subdivisions; i++)
            {
                double t = (double)i / subdivisions;
                double ang = startAngle + sweep * t;
                var next = new Point2d(
                    centerW.X + Math.Cos(ang) * radius,
                    centerW.Y + Math.Sin(ang) * radius);
                AddSegmentIfValid(segments, prev, next);
                prev = next;
            }
        }


        private static bool TryGetLinearEntityDirection2d(object entity, Matrix3d xform, out Point2d basePointW, out Vector2d dirUnit)
        {
            basePointW = default;
            dirUnit = default;

            var entityType = entity.GetType();
            var baseProp = entityType.GetProperty("BasePoint");
            if (baseProp == null || baseProp.GetValue(entity) is not Point3d basePoint)
                return false;

            var basePointW3 = basePoint.TransformBy(xform);
            basePointW = new Point2d(basePointW3.X, basePointW3.Y);

            var secondProp = entityType.GetProperty("SecondPoint");
            if (secondProp != null && secondProp.GetValue(entity) is Point3d secondPoint)
            {
                var secondPointW3 = secondPoint.TransformBy(xform);
                var secondPointW = new Point2d(secondPointW3.X, secondPointW3.Y);
                var dirFromSecond = secondPointW - basePointW;
                if (dirFromSecond.Length > 1e-9)
                {
                    dirUnit = dirFromSecond.GetNormal();
                    return true;
                }
            }

            foreach (string propName in new[] { "UnitDir", "UnitDirVector", "Direction" })
            {
                var dirProp = entityType.GetProperty(propName);
                if (dirProp != null && dirProp.GetValue(entity) is Vector3d dir3)
                {
                    var dir2 = new Vector2d(dir3.X, dir3.Y);
                    if (dir2.Length > 1e-9)
                    {
                        dirUnit = dir2.GetNormal();
                        return true;
                    }
                }
            }

            return false;
        }

        private static void AddXlineGeometry(
            Xline xline,
            Matrix3d xform,
            List<(Point2d P0, Point2d P1)> segments)
        {
            if (!TryGetLinearEntityDirection2d(xline, xform, out Point2d basePointW, out Vector2d dirUnit))
                return;

            const double halfLength = 10000.0;
            var p0 = new Point2d(basePointW.X - dirUnit.X * halfLength, basePointW.Y - dirUnit.Y * halfLength);
            var p1 = new Point2d(basePointW.X + dirUnit.X * halfLength, basePointW.Y + dirUnit.Y * halfLength);
            AddSegmentIfValid(segments, p0, p1);
        }

        private static void AddRayGeometry(
            Ray ray,
            Matrix3d xform,
            List<(Point2d P0, Point2d P1)> segments)
        {
            if (!TryGetLinearEntityDirection2d(ray, xform, out Point2d basePointW, out Vector2d dirUnit))
                return;

            const double length = 10000.0;
            var p1 = new Point2d(basePointW.X + dirUnit.X * length, basePointW.Y + dirUnit.Y * length);
            AddSegmentIfValid(segments, basePointW, p1);
        }

        private static void AddLoopSegments(
            List<Point2d> loop,
            List<(Point2d P0, Point2d P1)> segments)
        {
            if (loop == null || loop.Count < 3)
                return;

            for (int i = 0; i < loop.Count; i++)
                AddSegmentIfValid(segments, loop[i], loop[(i + 1) % loop.Count]);
        }

        private static bool LoopInsideLoop(List<Point2d> inner, List<Point2d> outer, double tol)
        {
            if (inner == null || outer == null || inner.Count < 3 || outer.Count < 3)
                return false;

            foreach (var pt in inner)
            {
                if (!PointInPolygon(pt, outer))
                    return false;
            }

            return true;
        }

        private static bool LoopInsideCircle(List<Point2d> loop, (Point2d Center, double Radius) circle, double tol)
        {
            if (loop == null || loop.Count < 3)
                return false;

            double limit = Math.Max(0.0, circle.Radius - tol);
            foreach (var pt in loop)
            {
                if (pt.GetDistanceTo(circle.Center) > limit)
                    return false;
            }

            return true;
        }

        private static bool CircleInsideCircle(
            (Point2d Center, double Radius) inner,
            (Point2d Center, double Radius) outer,
            double tol)
        {
            return inner.Center.GetDistanceTo(outer.Center) + inner.Radius <= outer.Radius - tol;
        }

        private static bool CircleInsideLoop(
            (Point2d Center, double Radius) circle,
            List<Point2d> loop,
            double tol)
        {
            if (loop == null || loop.Count < 3)
                return false;

            if (!PointInPolygon(circle.Center, loop))
                return false;

            double minDist = double.MaxValue;
            for (int i = 0; i < loop.Count; i++)
            {
                double d = DistancePointToSegment(circle.Center, loop[i], loop[(i + 1) % loop.Count]);
                if (d < minDist)
                    minDist = d;
            }

            return minDist >= circle.Radius + tol;
        }

        private static double DistancePointToSegment(Point2d pt, Point2d a, Point2d b)
        {
            var ab = b - a;
            double ab2 = ab.DotProduct(ab);
            if (ab2 < 1e-12)
                return pt.GetDistanceTo(a);

            var ap = pt - a;
            double t = ap.DotProduct(ab) / ab2;
            t = Math.Max(0.0, Math.Min(1.0, t));

            var closest = new Point2d(a.X + ab.X * t, a.Y + ab.Y * t);
            return pt.GetDistanceTo(closest);
        }
        private static void AddSegmentIfValid(
            List<(Point2d P0, Point2d P1)> segments,
            Point2d p0,
            Point2d p1)
        {
            if (p0.GetDistanceTo(p1) > 1e-9)
                segments.Add((p0, p1));
        }

        private static List<Point2d> TessellateBulgeArc(Point2d p0, Point2d p1, double bulge)
        {
            var pts = new List<Point2d> { p0 };

            double chord = p0.GetDistanceTo(p1);
            if (chord < 1e-9 || Math.Abs(bulge) < 1e-9)
            {
                pts.Add(p1);
                return pts;
            }

            double theta = 4.0 * Math.Atan(bulge);
            double absTheta = Math.Abs(theta);
            if (absTheta < 1e-9)
            {
                pts.Add(p1);
                return pts;
            }

            double radius = chord / (2.0 * Math.Sin(absTheta / 2.0));
            if (double.IsNaN(radius) || double.IsInfinity(radius) || radius < 1e-9)
            {
                pts.Add(p1);
                return pts;
            }

            var chordVec = p1 - p0;
            var chordUnit = chordVec / chord;
            var leftNormal = new Vector2d(-chordUnit.Y, chordUnit.X);
            double centerOffset = chord * (1.0 - bulge * bulge) / (4.0 * bulge);
            var mid = new Point2d((p0.X + p1.X) * 0.5, (p0.Y + p1.Y) * 0.5);
            var center = new Point2d(
                mid.X + leftNormal.X * centerOffset,
                mid.Y + leftNormal.Y * centerOffset);

            double startAngle = Math.Atan2(p0.Y - center.Y, p0.X - center.X);
            int subdivisions = Math.Max(8, (int)Math.Ceiling(radius * absTheta / 0.50));

            for (int i = 1; i < subdivisions; i++)
            {
                double t = (double)i / subdivisions;
                double ang = startAngle + theta * t;
                pts.Add(new Point2d(
                    center.X + Math.Cos(ang) * radius,
                    center.Y + Math.Sin(ang) * radius));
            }

            pts.Add(p1);
            return pts;
        }

        /// <summary>
        /// Adds a circle footprint (center, radius) in world XY using xform.
        /// Assumes uniform block scaling (uses X-axis length).
        /// </summary>
        private static void AddCircleFootprint(
            Circle c,
            Matrix3d xform,
            List<(Point2d Center, double Radius)> circles)
        {
            // Transform center to WCS
            var centerW = c.Center.TransformBy(xform);

            // Approximate uniform scale factor from transform
            var cs = xform.CoordinateSystem3d;
            double scale = cs.Xaxis.Length; // assume uniform scaling

            double radiusWorld = c.Radius * scale;

            circles.Add((new Point2d(centerW.X, centerW.Y), radiusWorld));
        }

        // ---------- UFLS1 intersection helpers (2D intersection points on fitted line) ----------

        private static List<Point2d> FindIntersectionPointsFromSegments(
            List<(Point2d P0, Point2d P1)> segments,
            Point3d centroid,
            Vector3d dir3D)
        {
            var results = new List<Point2d>();

            if (segments == null || segments.Count == 0)
                return results;

            var basePt = new Point2d(centroid.X, centroid.Y);
            var d2 = new Vector2d(dir3D.X, dir3D.Y);

            if (d2.Length < 1e-9)
                return results;

            foreach (var seg in segments)
            {
                if (TryIntersectSegment(basePt, d2, seg.P0, seg.P1, out double tLine))
                {
                    results.Add(new Point2d(
                        basePt.X + d2.X * tLine,
                        basePt.Y + d2.Y * tLine));
                }
            }

            return results;
        }

        private static List<Point2d> FindIntersectionPointsFromCircles(
            List<(Point2d Center, double Radius)> circles,
            Point3d centroid,
            Vector3d dir3D)
        {
            var results = new List<Point2d>();

            if (circles == null || circles.Count == 0)
                return results;

            var basePt = new Point2d(centroid.X, centroid.Y);
            var d2 = new Vector2d(dir3D.X, dir3D.Y);

            double a = d2.DotProduct(d2);
            if (a < 1e-12)
                return results;

            foreach (var circle in circles)
            {
                var center = circle.Center;
                double r = circle.Radius;
                Vector2d f = basePt - center;
                double b = 2.0 * d2.DotProduct(f);
                double c = f.DotProduct(f) - r * r;
                double disc = b * b - 4.0 * a * c;
                if (disc < 0.0)
                    continue;

                double sqrtDisc = Math.Sqrt(disc);
                double t1 = (-b - sqrtDisc) / (2.0 * a);
                double t2 = (-b + sqrtDisc) / (2.0 * a);
                results.Add(new Point2d(basePt.X + d2.X * t1, basePt.Y + d2.Y * t1));
                if (Math.Abs(t2 - t1) > 1e-9)
                    results.Add(new Point2d(basePt.X + d2.X * t2, basePt.Y + d2.Y * t2));
            }

            return results;
        }

        private static List<Point2d> DeduplicateIntersectionPoints(List<Point2d> points)
        {
            var results = new List<Point2d>();
            if (points == null)
                return results;

            const double tol2 = 1e-8;
            foreach (var pt in points)
            {
                bool dup = false;
                foreach (var kept in results)
                {
                    double dx = pt.X - kept.X;
                    double dy = pt.Y - kept.Y;
                    if (dx * dx + dy * dy <= tol2)
                    {
                        dup = true;
                        break;
                    }
                }

                if (!dup)
                    results.Add(pt);
            }

            return results;
        }

        private static bool TryFindNearestIntersectionPoint(
            List<Point2d> intersectionPoints,
            Point2d basePt,
            out double distance,
            out Point2d hitPoint)
        {
            distance = 0.0;
            hitPoint = Point2d.Origin;

            if (intersectionPoints == null || intersectionPoints.Count == 0)
                return false;

            bool found = false;
            double bestDistance = double.MaxValue;
            Point2d bestPoint = Point2d.Origin;

            foreach (var candidate in intersectionPoints)
            {
                double candidateDistance = basePt.GetDistanceTo(candidate);
                if (candidateDistance <= 1e-6)
                    continue;

                if (candidateDistance < bestDistance)
                {
                    bestDistance = candidateDistance;
                    bestPoint = candidate;
                    found = true;
                }
            }

            if (!found)
                return false;

            distance = bestDistance;
            hitPoint = bestPoint;
            return true;
        }

        private static bool TryFindNearestIntersectionPointInDirection(
            List<Point2d> intersectionPoints,
            Point2d basePt,
            Vector2d preferredDir,
            out double distance,
            out Point2d hitPoint)
        {
            distance = 0.0;
            hitPoint = Point2d.Origin;

            if (intersectionPoints == null || intersectionPoints.Count == 0)
                return false;

            if (preferredDir.Length < 1e-9)
                return TryFindNearestIntersectionPoint(intersectionPoints, basePt, out distance, out hitPoint);

            var dirUnit = preferredDir.GetNormal();
            bool found = false;
            double bestForwardDistance = double.MaxValue;
            Point2d bestPoint = Point2d.Origin;

            foreach (var candidate in intersectionPoints)
            {
                var toCandidate = candidate - basePt;
                double candidateDistance = toCandidate.Length;
                if (candidateDistance <= 1e-6)
                    continue;

                double forward = toCandidate.DotProduct(dirUnit);
                if (forward <= 1e-6)
                    continue;

                if (forward < bestForwardDistance)
                {
                    bestForwardDistance = forward;
                    bestPoint = candidate;
                    found = true;
                }
            }

            if (!found)
                return false;

            distance = basePt.GetDistanceTo(bestPoint);
            hitPoint = bestPoint;
            return true;
        }

        private static List<Point2d> ExcludeNearbyIntersectionPoints(
            List<Point2d> intersectionPoints,
            Point2d excludedPoint,
            double tolerance = 1e-4)
        {
            if (intersectionPoints == null || intersectionPoints.Count == 0)
                return new List<Point2d>();

            double tol2 = tolerance * tolerance;
            return intersectionPoints
                .Where(pt => pt.GetDistanceTo(excludedPoint) * pt.GetDistanceTo(excludedPoint) > tol2)
                .ToList();
        }

        private static double Distance2dSquared(Point3d a, Point3d b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        private static bool PointInsideAnyStructure(
            Point2d testPt,
            List<List<Point2d>> closedLoops,
            List<(Point2d Center, double Radius)> circles)
        {
            if (closedLoops != null)
            {
                foreach (var loop in closedLoops)
                {
                    if (PointInPolygon(testPt, loop))
                        return true;
                }
            }

            if (circles != null)
            {
                foreach (var circle in circles)
                {
                    if (testPt.GetDistanceTo(circle.Center) < circle.Radius - 1e-6)
                        return true;
                }
            }

            return false;
        }

        private static bool PointInPolygon(Point2d testPt, List<Point2d> polygon)
        {
            if (polygon == null || polygon.Count < 3)
                return false;

            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var pi = polygon[i];
                var pj = polygon[j];

                bool intersects =
                    ((pi.Y > testPt.Y) != (pj.Y > testPt.Y)) &&
                    (testPt.X < (pj.X - pi.X) * (testPt.Y - pi.Y) / ((pj.Y - pi.Y) + 1e-12) + pi.X);

                if (intersects)
                    inside = !inside;
            }

            return inside;
        }

        private static bool TryIntersectSegment(
            Point2d lineBasePt,
            Vector2d lineDir,
            Point2d segP0,
            Point2d segP1,
            out double lineParam)
        {
            lineParam = 0.0;

            if (lineDir.Length < 1e-12)
                return false;

            var segDir = segP1 - segP0;
            if (segDir.Length < 1e-12)
                return false;

            double denom = Cross2d(lineDir, segDir);
            if (Math.Abs(denom) < 1e-12)
                return false;

            var delta = segP0 - lineBasePt;
            double t = Cross2d(delta, segDir) / denom;
            double u = Cross2d(delta, lineDir) / denom;

            if (u < -1e-9 || u > 1.0 + 1e-9)
                return false;

            lineParam = t;
            return true;
        }

        private static double Cross2d(Vector2d a, Vector2d b)
        {
            return (a.X * b.Y) - (a.Y * b.X);
        }


        private static bool TryFindNearestIntersectionAlongRayPriority(
            List<(Point2d P0, Point2d P1)> boundarySegments,
            List<(Point2d P0, Point2d P1)> openSegments,
            List<(Point2d Center, double Radius)> circles,
            Point2d basePt,
            Vector2d dirUnit,
            out double distance)
        {
            distance = 0.0;

            var segmentHits = new List<double>();
            foreach (var seg in boundarySegments)
            {
                if (TryIntersectSegment(basePt, dirUnit, seg.P0, seg.P1, out double s) && s > 1e-6)
                    segmentHits.Add(s);
            }

            foreach (var seg in openSegments)
            {
                if (TryIntersectSegment(basePt, dirUnit, seg.P0, seg.P1, out double s) && s > 1e-6)
                    segmentHits.Add(s);
            }

            if (segmentHits.Count > 0)
            {
                distance = segmentHits.Min();
                return true;
            }

            return TryFindNearestIntersectionAlongRay(circles, basePt, dirUnit, out distance);
        }

        private static bool TryFindNearestIntersectionAlongRay(
            List<(Point2d P0, Point2d P1)> segments,
            List<(Point2d Center, double Radius)> circles,
            Point2d basePt,
            Vector2d dirUnit,
            out double distance)
        {
            return TryFindNearestIntersectionAlongRayPriority(segments, new List<(Point2d P0, Point2d P1)>(), circles, basePt, dirUnit, out distance);
        }

        private static bool TryFindNearestIntersectionAlongRay(
            List<(Point2d Center, double Radius)> circles,
            Point2d basePt,
            Vector2d dirUnit,
            out double distance)
        {
            distance = 0.0;

            var circleHits = new List<double>();
            double a = dirUnit.DotProduct(dirUnit);
            if (a > 1e-12)
            {
                foreach (var circle in circles)
                {
                    Vector2d f = basePt - circle.Center;
                    double b = 2.0 * dirUnit.DotProduct(f);
                    double c = f.DotProduct(f) - circle.Radius * circle.Radius;
                    double disc = b * b - 4.0 * a * c;
                    if (disc < 0.0)
                        continue;

                    double sqrtDisc = Math.Sqrt(disc);
                    double s1 = (-b - sqrtDisc) / (2.0 * a);
                    double s2 = (-b + sqrtDisc) / (2.0 * a);

                    if (s1 > 1e-6)
                        circleHits.Add(s1);
                    if (s2 > 1e-6)
                        circleHits.Add(s2);
                }
            }

            if (circleHits.Count == 0)
                return false;

            distance = circleHits.Min();
            return true;
        }

private static double NormalizePositiveAngle(double angle)
        {
            double twoPi = Math.PI * 2.0;
            while (angle < 0.0) angle += twoPi;
            while (angle >= twoPi) angle -= twoPi;
            return angle;
        }

        private static double ComputeArcFitScore(
            List<Point3d> pts,
            Point2d center,
            double radius,
            double startAngle,
            double sweep)
        {
            if (Math.Abs(sweep) < 1e-9)
                return double.MaxValue;

            double total = 0.0;
            for (int i = 0; i < pts.Count; i++)
            {
                var p = new Point2d(pts[i].X, pts[i].Y);
                double angle = Math.Atan2(p.Y - center.Y, p.X - center.X);
                double t = ParameterAlongSweep(startAngle, sweep, angle);
                t = Math.Max(0.0, Math.Min(1.0, t));
                double projAngle = startAngle + sweep * t;
                var proj = new Point2d(center.X + Math.Cos(projAngle) * radius, center.Y + Math.Sin(projAngle) * radius);
                double d = p.GetDistanceTo(proj);
                total += d * d;
            }

            return total;
        }

        private static double ParameterAlongSweep(double startAngle, double sweep, double angle)
        {
            if (sweep >= 0.0)
            {
                double delta = NormalizePositiveAngle(angle - startAngle);
                return delta / sweep;
            }

            double deltaCw = NormalizePositiveAngle(startAngle - angle);
            return deltaCw / (-sweep);
        }

        private static bool IsExtremeInsideStructureInterval(
            double tPick,
            List<double> intersectionParams,
            double tol = 1e-9)
        {
            if (intersectionParams == null || intersectionParams.Count < 2)
                return false;

            var sorted = intersectionParams
                .Distinct()
                .OrderBy(t => t)
                .ToList();

            // each pair [t0,t1], [t2,t3], ... is an inside interval
            for (int i = 0; i + 1 < sorted.Count; i += 2)
            {
                double t0 = sorted[i];
                double t1 = sorted[i + 1];

                if (tPick > t0 + tol && tPick < t1 - tol)
                    return true;
            }

            return false;
        }

        // ---------- UFLS5 intersection helpers (distances from START) ----------

        private static List<double> FindSegmentIntersectionDistancesFromStart(
            List<(Point2d P0, Point2d P1)> segments,
            Point3d pStart,
            Point3d pEnd)
        {
            var results = new List<double>();
            if (segments == null || segments.Count == 0)
                return results;

            var basePt = new Point2d(pStart.X, pStart.Y);
            var rawDir = new Vector2d(pEnd.X - pStart.X, pEnd.Y - pStart.Y);
            double len = rawDir.Length;
            if (len < 1e-9)
                return results;

            var dirUnit = rawDir / len;

            foreach (var seg in segments)
            {
                if (TryIntersectSegment(basePt, dirUnit, seg.P0, seg.P1, out double s))
                    results.Add(s); // distance from start in XY
            }

            return results;
        }

        private static List<double> FindCircleIntersectionDistancesFromStart(
            List<(Point2d Center, double Radius)> circles,
            Point3d pStart,
            Point3d pEnd)
        {
            var results = new List<double>();
            if (circles == null || circles.Count == 0)
                return results;

            var basePt = new Point2d(pStart.X, pStart.Y);
            var rawDir = new Vector2d(pEnd.X - pStart.X, pEnd.Y - pStart.Y);
            double len = rawDir.Length;
            if (len < 1e-9)
                return results;

            var dirUnit = rawDir / len; // |dirUnit| = 1

            foreach (var circle in circles)
            {
                var center = circle.Center;
                double r = circle.Radius;

                Vector2d f = basePt - center;

                double a = 1.0; // dirUnit is normalized
                double b = 2.0 * dirUnit.DotProduct(f);
                double c = f.DotProduct(f) - r * r;

                double disc = b * b - 4.0 * a * c;
                if (disc < 0.0)
                    continue;

                double sqrtDisc = Math.Sqrt(disc);
                double s1 = (-b - sqrtDisc) / (2.0 * a);
                double s2 = (-b + sqrtDisc) / (2.0 * a);

                results.Add(s1);
                if (Math.Abs(s2 - s1) > 1e-9)
                    results.Add(s2);
            }

            return results;
        }

        // ------------------------------------------------------------
        // 4) Layer + cleanup helpers
        // ------------------------------------------------------------
        private static void EnsureLayer(Database db, Transaction tr, string name, short colorIndex)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            if (!lt.Has(name))
            {
                lt.UpgradeOpen();
                var ltr = new LayerTableRecord
                {
                    Name = name,
                    Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                        Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex)
                };
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
        }

        private static void RemoveMarkers(Database db, List<ObjectId> markerIds)
        {
            if (markerIds == null || markerIds.Count == 0)
                return;

            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    foreach (var id in markerIds)
                    {
                        if (!id.IsErased && id.IsValid)
                        {
                            var ent = tr.GetObject(id, OpenMode.ForWrite, false) as AcEntity;
                            ent?.Erase();
                        }
                    }
                    tr.Commit();
                }
            }
            catch
            {
                // ignore cleanup errors
            }
        }
    }
}