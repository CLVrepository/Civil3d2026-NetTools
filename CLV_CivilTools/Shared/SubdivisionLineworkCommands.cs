using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcadColor = Autodesk.AutoCAD.Colors.Color;
using ColorMethod = Autodesk.AutoCAD.Colors.ColorMethod;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace CLV_CivilTools.Shared
{
    /// <summary>
    /// Early subdivision-linework drafting helpers. These tools are intentionally
    /// conservative: they create reviewable CAD geometry from selected centerlines
    /// and do not modify source centerlines.
    /// </summary>
    public class SubdivisionLineworkCommands
    {
        private const string RoadCenterlineLayer = "V-MAPL-ROAD-CL";
        private const string RoadEdgeLayer = "V-MAPL-ROAD-ROW";
        private const string LotLayer = "V-MAPL-LOT";
        private const string QaLayer = "V-MAPL-QA";

        private static double _typicalRoadWidth = 50.0;
        private static double _culdesacRadius = 50.0;
        private static double _culdesacTieInRadius = 25.0;
        private static double _curbReturnRadius = 25.0;
        private static double _lotOffsetDistance = 50.0;

        [CommandMethod("CLV_SUBDIV_SITE_SETTINGS")]
        [CommandMethod("SUBDIV-SITE-SETTINGS")]
        [CommandMethod("SUBDIVISION-SITE-SETTINGS")]
        public void OpenSubdivisionSiteSettings()
        {
            using SiteSettingsForm form = new SiteSettingsForm(_typicalRoadWidth, _culdesacRadius, _culdesacTieInRadius, _curbReturnRadius)
            {
                Text = "Subdivision Site Settings"
            };
            if (form.ShowDialog() != DialogResult.OK) return;

            _typicalRoadWidth = form.TypicalRoadWidth;
            _culdesacRadius = form.CuldesacRadius;
            _culdesacTieInRadius = form.CuldesacTieInRadius;
            _curbReturnRadius = form.CurbReturnRadius;

            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            doc?.Editor.WriteMessage($"\nSubdivision defaults saved for this CAD session: road width={_typicalRoadWidth:0.##}, cul-de-sac radius={_culdesacRadius:0.##}, tie-in radius={_culdesacTieInRadius:0.##}, curb return radius={_curbReturnRadius:0.##}.");
        }

        [CommandMethod("CLV_SUBDIV_ROADS")]
        [CommandMethod("SUBDIV-ROADS")]
        [CommandMethod("SUBDIVISION-ROADS")]
        [CommandMethod("CLV_SUBDIV_ROAD_EDGES")]
        [CommandMethod("SUBDIV-ROAD-EDGES")]
        [CommandMethod("SUBDIVISION-ROAD-EDGES")]
        public void CreateRoadEdgesFromCenterlines()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptSelectionOptions pso = new PromptSelectionOptions
            {
                MessageForAdding = $"\nSelect road centerline objects to offset using site width {_typicalRoadWidth:0.##}: "
            };
            PromptSelectionResult psr = ed.GetSelection(pso);
            if (psr.Status != PromptStatus.OK) return;

            double halfWidth = _typicalRoadWidth / 2.0;
            if (halfWidth <= 0.0)
            {
                ed.WriteMessage("\nTypical road width must be greater than zero. Run SITE SETTINGS first.");
                return;
            }

            int edgeCount = 0;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EnsureLayer(db, tr, RoadEdgeLayer, 83, "M");
                EnsureLayer(db, tr, RoadCenterlineLayer, 11, "M");
                EnsureLayer(db, tr, QaLayer, 1, "M");

                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                foreach (SelectedObject so in psr.Value)
                {
                    if (so == null) continue;
                    DBObject obj = tr.GetObject(so.ObjectId, OpenMode.ForRead);
                    if (obj is not Curve curve)
                    {
                        continue;
                    }

                    edgeCount += AppendOffsets(ms, tr, curve, halfWidth, RoadEdgeLayer);
                    edgeCount += AppendOffsets(ms, tr, curve, -halfWidth, RoadEdgeLayer);
                }

                tr.Commit();
            }

            ed.WriteMessage($"\nROADS complete. Created {edgeCount} road-edge object(s) on {RoadEdgeLayer} using {halfWidth:0.##} half-width. Cul-de-sacs are handled by the separate CUL-DE-SAC command.");
        }

        [CommandMethod("CLV_SUBDIV_CULDESAC")]
        [CommandMethod("SUBDIV-CUL-DE-SAC")]
        [CommandMethod("SUBDIV-CULDESAC")]
        [CommandMethod("SUBDIVISION-CUL-DE-SAC")]
        public void CreateCuldesacFromEndpoint()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            using CuldesacOptionsForm form = new CuldesacOptionsForm(_culdesacRadius, _culdesacTieInRadius, _typicalRoadWidth)
            {
                Text = "Cul-de-sac Settings"
            };
            if (form.ShowDialog() != DialogResult.OK) return;

            _culdesacRadius = form.CuldesacRadius;
            _culdesacTieInRadius = form.TieInRadius;
            _typicalRoadWidth = form.RoadWidth;

            PromptEntityOptions peo = new PromptEntityOptions("\nSelect cul-de-sac road centerline: ");
            peo.SetRejectMessage("\nSelect a road centerline curve (line, arc, or polyline).");
            peo.AddAllowedClass(typeof(Curve), exactMatch: false);
            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            PromptPointOptions ppo = new PromptPointOptions("\nPick the cul-de-sac endpoint / bulb center on the selected centerline: ");
            PromptPointResult ppr = ed.GetPoint(ppo);
            if (ppr.Status != PromptStatus.OK) return;

            int created = 0;
            int cleaned = 0;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EnsureLayer(db, tr, RoadEdgeLayer, 83, "M");
                EnsureLayer(db, tr, QaLayer, 1, "M");

                Curve centerline = (Curve)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                Point3d bulbCenter = ppr.Value;
                Vector2d stemDirection = GetStemDirection(centerline, bulbCenter);

                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // Use a cul-de-sac-specific edge finder.  The general nearest-edge search
                // can accidentally pick linework that was already cleaned at a nearby
                // intersection or a previous bulb, especially when this bulb uses a larger
                // radius.  For a cul-de-sac we want exactly one road edge on each side of
                // the selected centerline, parallel to that centerline and offset by the
                // current half-width near the picked bulb center.
                List<Curve> edges = FindCuldesacRoadEdges(ms, tr, centerline, bulbCenter, _typicalRoadWidth / 2.0, _culdesacRadius, _culdesacTieInRadius);

                if (edges.Count < 2)
                {
                    Circle bulb = new Circle(bulbCenter, Vector3d.ZAxis, _culdesacRadius)
                    {
                        Layer = RoadEdgeLayer
                    };
                    ms.AppendEntity(bulb);
                    tr.AddNewlyCreatedDBObject(bulb, true);
                    created++;
                    ed.WriteMessage("\nCould not automatically identify both road-edge lines. Created full bulb circle only.");
                }
                else
                {
                    List<Point3d> circleTiePoints = new List<Point3d>();
                    foreach (Curve edge in edges)
                    {
                        Line? filletBaseLine = edge as Line ?? CreateTangentWorkLine(edge, bulbCenter, _culdesacRadius + _culdesacTieInRadius + _typicalRoadWidth);
                        if (filletBaseLine != null && TryCreateLineCircleFillet(filletBaseLine, bulbCenter, _culdesacRadius, _culdesacTieInRadius, stemDirection, out Arc? fillet, out Point3d lineTangent, out Point3d circleTangent))
                        {
                            TrimCurveEndNearestPoint(edge, bulbCenter, lineTangent);
                            fillet.Layer = RoadEdgeLayer;
                            ms.AppendEntity(fillet);
                            tr.AddNewlyCreatedDBObject(fillet, true);
                            circleTiePoints.Add(circleTangent);
                            created++;
                            cleaned++;
                        }
                        else if (edge is Line lineEdge && TrimOrExtendLineToCircle(lineEdge, bulbCenter, _culdesacRadius))
                        {
                            circleTiePoints.Add(lineEdge.StartPoint.DistanceTo(bulbCenter) < lineEdge.EndPoint.DistanceTo(bulbCenter) ? lineEdge.StartPoint : lineEdge.EndPoint);
                            cleaned++;
                        }
                    }

                    if (circleTiePoints.Count >= 2)
                    {
                        Arc bulbArc = CreateBulbArc(bulbCenter, _culdesacRadius, circleTiePoints[0], circleTiePoints[1], stemDirection);
                        bulbArc.Layer = RoadEdgeLayer;
                        ms.AppendEntity(bulbArc);
                        tr.AddNewlyCreatedDBObject(bulbArc, true);
                        created++;
                    }
                    else
                    {
                        Circle bulb = new Circle(bulbCenter, Vector3d.ZAxis, _culdesacRadius) { Layer = RoadEdgeLayer };
                        ms.AppendEntity(bulb);
                        tr.AddNewlyCreatedDBObject(bulb, true);
                        created++;
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage($"\nCUL-DE-SAC complete. Created/cleaned {created} bulb/fillet object(s) and trimmed {cleaned} automatically detected road-edge line(s). No road-edge selection prompt was required.");
        }

        [CommandMethod("CLV_SUBDIV_INTERSECTION")]
        [CommandMethod("SUBDIV-INTERSECTION")]
        [CommandMethod("SUBDIVISION-INTERSECTION")]
        [CommandMethod("CLV_SUBDIV_INTERSECTION_RETURNS")]
        [CommandMethod("SUBDIV-INTERSECTION-RETURNS")]
        [CommandMethod("SUBDIVISION-INTERSECTION-RETURNS")]
        public void CreateIntersectionReturnsFromTwoCenterlines()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptEntityOptions peo1 = new PromptEntityOptions("\nSelect MAIN road centerline: ");
            peo1.SetRejectMessage("\nSelect a MAIN road centerline curve (line, arc, or polyline).");
            peo1.AddAllowedClass(typeof(Curve), exactMatch: false);
            PromptEntityResult per1 = ed.GetEntity(peo1);
            if (per1.Status != PromptStatus.OK) return;

            PromptEntityOptions peo2 = new PromptEntityOptions("\nSelect INTERSECTING road centerline near the side that joins the MAIN road: ");
            peo2.SetRejectMessage("\nSelect an INTERSECTING road centerline curve (line, arc, or polyline).");
            peo2.AddAllowedClass(typeof(Curve), exactMatch: false);
            PromptEntityResult per2 = ed.GetEntity(peo2);
            if (per2.Status != PromptStatus.OK) return;

            double halfWidth = _typicalRoadWidth / 2.0;
            double returnRadius = _curbReturnRadius;
            if (halfWidth <= 0.0 || returnRadius <= 0.0)
            {
                ed.WriteMessage("\nTypical road width and curb return radius must be greater than zero. Run SITE SETTINGS first.");
                return;
            }

            int created = 0;
            int cleaned = 0;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EnsureLayer(db, tr, RoadEdgeLayer, 83, "M");
                EnsureLayer(db, tr, QaLayer, 1, "M");

                Curve mainCl = (Curve)tr.GetObject(per1.ObjectId, OpenMode.ForRead);
                Curve sideCl = (Curve)tr.GetObject(per2.ObjectId, OpenMode.ForRead);
                if (!TryCurveIntersection2d(mainCl, sideCl, out Point2d centerIntersection))
                {
                    ed.WriteMessage("\nSelected centerlines do not intersect in 2D. No returns created.");
                    return;
                }

                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                Vector2d mainDir = GetCurveTangent2d(mainCl, ToPoint3d(centerIntersection));
                Vector2d sideDirRaw = GetCurveTangent2d(sideCl, ToPoint3d(centerIntersection));
                Point2d pick2 = new Point2d(per2.PickedPoint.X, per2.PickedPoint.Y);
                Vector2d fromIntersectionToPick = pick2 - centerIntersection;
                Vector2d sideDir = fromIntersectionToPick.DotProduct(sideDirRaw) >= 0.0 ? sideDirRaw : sideDirRaw.Negate();
                if (fromIntersectionToPick.Length < 1e-6)
                {
                    Point2d sA = new Point2d(sideCl.StartPoint.X, sideCl.StartPoint.Y);
                    Point2d sB = new Point2d(sideCl.EndPoint.X, sideCl.EndPoint.Y);
                    sideDir = sA.GetDistanceTo(centerIntersection) >= sB.GetDistanceTo(centerIntersection) ? (sA - centerIntersection).GetNormal() : (sB - centerIntersection).GetNormal();
                }

                Vector2d mainNormal = new Vector2d(-mainDir.Y, mainDir.X);
                Vector2d sideNormal = new Vector2d(-sideDir.Y, sideDir.X);

                // The user selected MAIN road first and INTERSECTING road second.
                // Build the T-intersection from the centerline frame rather than from
                // whatever partial edge segments happen to be closest. This matches the
                // manual workflow:
                //   1) identify the MAIN edge on the side the side-road joins,
                //   2) trim an opening in that MAIN edge between the two return tangencies,
                //   3) trim each side-road edge to its return tangency,
                //   4) create two tangent/tangent return arcs.
                double mainJoinSide = Math.Sign(sideDir.DotProduct(mainNormal));
                if (Math.Abs(mainJoinSide) < 1e-8) mainJoinSide = 1.0;

                List<Point3d> mainTangentPoints = new List<Point3d>();
                List<(Curve? Edge, Point3d Tangent)> sideTrimTargets = new List<(Curve? Edge, Point3d Tangent)>();

                Curve? mainEdge = FindRoadEdgeForCenterlineSide(ms, tr, mainCl, centerIntersection, mainJoinSide, halfWidth, halfWidth + returnRadius + 250.0);

                if (mainEdge == null)
                {
                    ed.WriteMessage("\nCould not automatically identify the MAIN road edge on the side where the intersecting road joins. No returns created.");
                    return;
                }

                foreach (double sideSign in new[] { 1.0, -1.0 })
                {
                    Curve? sideEdge = FindRoadEdgeForCenterlineSide(ms, tr, sideCl, centerIntersection, sideSign, halfWidth, halfWidth + returnRadius + 250.0);
                    if (sideEdge == null) continue;

                    // Determine the ACTUAL corner from the generated road-edge curves.
                    // For curved centerlines the road edges may be arcs, so a theoretical
                    // offset-corner based only on centerline tangents can land in the wrong
                    // quadrant. Use the intersection of the selected main edge and side edge
                    // (extended if needed), then use the local tangent of each actual edge
                    // at that corner. This handles straight/straight and simple arc/line cases.
                    Point2d theoreticalCorner = centerIntersection
                        .Add(mainNormal * (mainJoinSide * halfWidth))
                        .Add(sideNormal * (sideSign * halfWidth));

                    if (!TryCurveCurveIntersection2d(mainEdge, sideEdge, theoreticalCorner, out Point2d corner))
                    {
                        corner = theoreticalCorner;
                    }

                    Vector2d mainTan = GetCurveTangent2d(mainEdge, ToPoint3d(corner));
                    Vector2d sideTan = GetCurveTangent2d(sideEdge, ToPoint3d(corner));
                    if (mainTan.Length < 1e-8) mainTan = mainDir;
                    if (sideTan.Length < 1e-8) sideTan = sideDir;

                    // Along the main edge, move away from the centerline crossing toward
                    // the actual corner side. Along the side-road edge, move away from the
                    // main road into the intersecting road.
                    Vector2d mainAway = mainTan.DotProduct(corner - centerIntersection) >= 0.0 ? mainTan.GetNormal() : mainTan.Negate().GetNormal();
                    Vector2d sideAway = sideTan.DotProduct(sideDir) >= 0.0 ? sideTan.GetNormal() : sideTan.Negate().GetNormal();

                    if (TryCreateCornerFilletArc(corner, mainAway, sideAway, returnRadius, out Arc? arc, out Point2d mainTangent, out Point2d sideTangent))
                    {
                        arc.Layer = RoadEdgeLayer;
                        ms.AppendEntity(arc);
                        tr.AddNewlyCreatedDBObject(arc, true);
                        created++;
                        mainTangentPoints.Add(ToPoint3d(mainTangent));
                        sideTrimTargets.Add((sideEdge, ToPoint3d(sideTangent)));
                    }
                }

                if (mainTangentPoints.Count >= 2)
                {
                    if (CleanRoadEdgeCurveAtIntersection(mainEdge, mainTangentPoints, ms, tr)) cleaned++;
                }
                else
                {
                    ed.WriteMessage("\nCould not calculate both return tangent points. MAIN road edge was not trimmed.");
                }

                Point3d center3d = ToPoint3d(centerIntersection);
                foreach ((Curve? edge, Point3d tangent) in sideTrimTargets)
                {
                    if (edge == null) continue;
                    TrimCurveEndNearestPoint(edge, center3d, tangent);
                    cleaned++;
                }

                tr.Commit();
            }

            ed.WriteMessage($"\nINTERSECTION complete. Created {created} return arc(s). Trimmed the MAIN road edge opening first, then trimmed the intersecting road edges to the return tangent points using half-width {halfWidth:0.##} and return radius {returnRadius:0.##}.");
        }

        [CommandMethod("CLV_SUBDIV_LOT_LINES")]
        [CommandMethod("SUBDIV-LOT-LINES")]
        [CommandMethod("SUBDIVISION-LOT-LINES")]
        [CommandMethod("CLV_SUBDIV_LOT_OFFSET")]
        [CommandMethod("SUBDIV-LOT-OFFSET")]
        [CommandMethod("SUBDIVISION-LOT-OFFSET")]
        public void CreateLotLinesByRepeatedOffset()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptEntityOptions peo = new PromptEntityOptions("\nSelect source lot line to offset repeatedly: ");
            peo.SetRejectMessage("\nSelect a line, arc, or polyline.");
            peo.AddAllowedClass(typeof(Curve), exactMatch: false);
            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            PromptPointResult stopPick = ed.GetPoint("\nPick furthest point / stop location for repeated lot lines: ");
            if (stopPick.Status != PromptStatus.OK) return;

            PromptDoubleOptions pdo = new PromptDoubleOptions("\nTypical lot spacing / offset distance: ")
            {
                DefaultValue = _lotOffsetDistance,
                UseDefaultValue = true,
                AllowNegative = false,
                AllowZero = false
            };
            PromptDoubleResult pdr = ed.GetDouble(pdo);
            if (pdr.Status != PromptStatus.OK) return;
            _lotOffsetDistance = pdr.Value;

            PromptSelectionOptions trimOptions = new PromptSelectionOptions
            {
                MessageForAdding = "\nSelect optional trim/extend boundary lot lines near both ends, or press Enter: "
            };
            PromptSelectionResult trimSelection = ed.GetSelection(trimOptions);
            ObjectId[] trimIds = trimSelection.Status == PromptStatus.OK ? trimSelection.Value.GetObjectIds() : Array.Empty<ObjectId>();

            int created = 0;
            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EnsureLayer(db, tr, LotLayer, 220, "M");
                EnsureLayer(db, tr, QaLayer, 1, "M");

                Curve source = (Curve)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                double maxDistance = EstimateOffsetDistanceToPoint(source, stopPick.Value);
                if (maxDistance < _lotOffsetDistance * 0.50)
                {
                    ed.WriteMessage("\nStop point is too close to the source lot line for the current spacing.");
                    return;
                }

                int lineCount = Math.Max(1, (int)Math.Floor((maxDistance + 1e-6) / _lotOffsetDistance));
                if (lineCount > 500)
                {
                    ed.WriteMessage("\nToo many lot lines would be created. Pick a closer stop point or use a larger spacing.");
                    return;
                }

                double sideSign = DetermineOffsetSideSign(source, stopPick.Value, _lotOffsetDistance);
                if (Math.Abs(sideSign) < 0.5)
                {
                    ed.WriteMessage("\nCould not determine offset side from the stop point.");
                    return;
                }

                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                for (int i = 1; i <= lineCount; i++)
                {
                    double offset = sideSign * _lotOffsetDistance * i;
                    DBObjectCollection offsets;
                    try
                    {
                        offsets = source.GetOffsetCurves(offset);
                    }
                    catch
                    {
                        ed.WriteMessage($"\nOffset failed at distance {Math.Abs(offset):0.##}.");
                        continue;
                    }

                    foreach (DBObject dbo in offsets)
                    {
                        if (dbo is Entity ent)
                        {
                            if (ent is Line offsetLine && trimIds.Length > 0)
                            {
                                TrimOrExtendLineToNearestIntersections(offsetLine, trimIds, tr);
                            }

                            ent.Layer = LotLayer;
                            ms.AppendEntity(ent);
                            tr.AddNewlyCreatedDBObject(ent, true);
                            created++;
                        }
                        else
                        {
                            dbo.Dispose();
                        }
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage($"\nLOT LINES complete. Created {created} lot line object(s) at {_lotOffsetDistance:0.##} spacing toward the selected stop point.");
        }

        private static double EstimateOffsetDistanceToPoint(Curve source, Point3d stopPoint)
        {
            if (source is Line line)
            {
                Vector2d dir = ToVector2d(line.StartPoint, line.EndPoint);
                if (dir.Length < 1e-8) return 0.0;
                dir = dir.GetNormal();
                Point2d a = new Point2d(line.StartPoint.X, line.StartPoint.Y);
                Point2d p = new Point2d(stopPoint.X, stopPoint.Y);
                Vector2d v = p - a;
                return Math.Abs((v.X * dir.Y) - (v.Y * dir.X));
            }

            try
            {
                Point3d closest = source.GetClosestPointTo(stopPoint, extend: false);
                return closest.DistanceTo(stopPoint);
            }
            catch
            {
                return 0.0;
            }
        }

        private static double DetermineOffsetSideSign(Curve source, Point3d stopPoint, double testDistance)
        {
            if (testDistance <= 0.0) return 0.0;

            DBObjectCollection plus;
            DBObjectCollection minus;
            try
            {
                plus = source.GetOffsetCurves(testDistance);
                minus = source.GetOffsetCurves(-testDistance);
            }
            catch
            {
                return 0.0;
            }

            double plusDistance = ClosestDistanceToOffsetCollection(plus, stopPoint);
            double minusDistance = ClosestDistanceToOffsetCollection(minus, stopPoint);

            foreach (DBObject dbo in plus) dbo.Dispose();
            foreach (DBObject dbo in minus) dbo.Dispose();

            if (double.IsInfinity(plusDistance) && double.IsInfinity(minusDistance)) return 0.0;
            return plusDistance <= minusDistance ? 1.0 : -1.0;
        }

        private static double ClosestDistanceToOffsetCollection(DBObjectCollection offsets, Point3d point)
        {
            double best = double.PositiveInfinity;
            foreach (DBObject dbo in offsets)
            {
                if (dbo is Curve curve)
                {
                    try
                    {
                        Point3d closest = curve.GetClosestPointTo(point, extend: false);
                        best = Math.Min(best, closest.DistanceTo(point));
                    }
                    catch
                    {
                        // Ignore invalid offset piece and continue checking remaining objects.
                    }
                }
            }
            return best;
        }

        private static int AppendOffsets(BlockTableRecord ms, Transaction tr, Curve curve, double offsetDistance, string layerName)
        {
            int count = 0;
            DBObjectCollection offsets;
            try
            {
                offsets = curve.GetOffsetCurves(offsetDistance);
            }
            catch
            {
                return 0;
            }

            foreach (DBObject dbo in offsets)
            {
                if (dbo is Entity ent)
                {
                    // AutoCAD may return offset road edges as lightweight polylines when the
                    // selected centerline is an arc/polyline or contains mixed geometry.  The
                    // downstream subdivision cleanup tools work best when road-edge geometry is
                    // individual line/arc segments, because each segment can be trimmed or filleted
                    // independently.  Explode offset polylines here so INTERSECTION and CUL-DE-SAC
                    // can work against real road-edge pieces instead of one long polyline that
                    // cannot be cleanly split by the current first-pass cleanup logic.
                    if (ent is Curve && ent is not Line && ent is not Arc)
                    {
                        DBObjectCollection pieces = new DBObjectCollection();
                        bool exploded = false;
                        try
                        {
                            ent.Explode(pieces);
                            exploded = pieces.Count > 0;
                        }
                        catch
                        {
                            exploded = false;
                        }

                        if (exploded)
                        {
                            foreach (DBObject piece in pieces)
                            {
                                if (piece is Entity pieceEnt)
                                {
                                    pieceEnt.Layer = layerName;
                                    ms.AppendEntity(pieceEnt);
                                    tr.AddNewlyCreatedDBObject(pieceEnt, true);
                                    count++;
                                }
                                else
                                {
                                    piece.Dispose();
                                }
                            }

                            ent.Dispose();
                            continue;
                        }
                    }

                    ent.Layer = layerName;
                    ms.AppendEntity(ent);
                    tr.AddNewlyCreatedDBObject(ent, true);
                    count++;
                }
                else
                {
                    dbo.Dispose();
                }
            }

            return count;
        }

        private static Entity? ChooseOffsetNearestPoint(DBObjectCollection plus, DBObjectCollection minus, Point3d pick)
        {
            List<Entity> entities = new List<Entity>();
            foreach (DBObject dbo in plus) if (dbo is Entity e) entities.Add(e);
            foreach (DBObject dbo in minus) if (dbo is Entity e) entities.Add(e);

            Entity? best = null;
            double bestDistance = double.MaxValue;
            foreach (Entity entity in entities)
            {
                if (entity is Curve curve)
                {
                    Point3d p = curve.GetClosestPointTo(pick, extend: false);
                    double d = p.DistanceTo(pick);
                    if (d < bestDistance)
                    {
                        best = entity;
                        bestDistance = d;
                    }
                }
            }

            return best;
        }

        private static void DisposeUnchosen(DBObjectCollection collection, Entity? chosen)
        {
            foreach (DBObject dbo in collection)
            {
                if (!ReferenceEquals(dbo, chosen))
                {
                    dbo.Dispose();
                }
            }
        }

        private static Vector2d GetStemDirection(Curve centerline, Point3d pickedEndpoint)
        {
            Point3d closest = centerline.GetClosestPointTo(pickedEndpoint, extend: false);
            Vector2d tangent = GetCurveTangent2d(centerline, closest);
            double dStart = centerline.StartPoint.DistanceTo(pickedEndpoint);
            double dEnd = centerline.EndPoint.DistanceTo(pickedEndpoint);
            if (dEnd <= dStart) tangent = tangent.Negate();
            return tangent.Length < 1e-8 ? Vector2d.YAxis.Negate() : tangent.GetNormal();
        }

        private static List<Curve> FindCuldesacRoadEdges(BlockTableRecord ms, Transaction tr, Curve centerline, Point3d bulbCenter, double halfWidth, double bulbRadius, double tieInRadius)
        {
            List<Curve> chosen = new List<Curve>();
            if (halfWidth <= 0.0) return chosen;

            Point3d clClosest = centerline.GetClosestPointTo(bulbCenter, extend: false);
            Vector2d clDir = GetCurveTangent2d(centerline, clClosest);
            if (clDir.Length < 1e-8) return chosen;
            Vector2d clNormal = new Vector2d(-clDir.Y, clDir.X);
            Point2d clPoint = new Point2d(clClosest.X, clClosest.Y);
            Point2d bulb = new Point2d(bulbCenter.X, bulbCenter.Y);
            Vector2d stemDir = GetStemDirection(centerline, bulbCenter);

            double offsetTolerance = Math.Max(1.0, Math.Abs(halfWidth) * 0.25);
            double segmentReach = Math.Max(halfWidth + tieInRadius + 20.0, bulbRadius + tieInRadius + halfWidth + 20.0);

            Curve? bestLeft = null;
            Curve? bestRight = null;
            double bestLeftScore = double.MaxValue;
            double bestRightScore = double.MaxValue;

            foreach (ObjectId id in ms)
            {
                if (!id.IsValid || id.IsErased) continue;
                if (tr.GetObject(id, OpenMode.ForWrite, false) is not Curve edge) continue;
                if (edge is not Entity ent) continue;
                if (!string.Equals(ent.Layer, RoadEdgeLayer, StringComparison.OrdinalIgnoreCase)) continue;

                Point3d edgeClosest = edge.GetClosestPointTo(bulbCenter, extend: false);
                Vector2d edgeDir = GetCurveTangent2d(edge, edgeClosest);
                if (edgeDir.Length < 1e-8) continue;
                if (Math.Abs(edgeDir.GetNormal().DotProduct(clDir)) < 0.92) continue;

                Point2d p = new Point2d(edgeClosest.X, edgeClosest.Y);
                double signedOffset = (p - clPoint).DotProduct(clNormal);
                double absOffsetError = Math.Abs(Math.Abs(signedOffset) - halfWidth);
                if (absOffsetError > offsetTolerance) continue;

                double closestSegmentDistance = edgeClosest.DistanceTo(bulbCenter);
                double closestEndpointDistance = Math.Min(edge.StartPoint.DistanceTo(bulbCenter), edge.EndPoint.DistanceTo(bulbCenter));
                if (closestSegmentDistance > segmentReach && closestEndpointDistance > segmentReach) continue;

                Point2d nearEndpoint = edge.StartPoint.DistanceTo(bulbCenter) <= edge.EndPoint.DistanceTo(bulbCenter)
                    ? new Point2d(edge.StartPoint.X, edge.StartPoint.Y)
                    : new Point2d(edge.EndPoint.X, edge.EndPoint.Y);
                double alongStem = (nearEndpoint - bulb).DotProduct(stemDir);
                double behindPenalty = alongStem < -5.0 ? Math.Abs(alongStem) * 100.0 : 0.0;
                double score = closestEndpointDistance + (closestSegmentDistance * 0.25) + (absOffsetError * 20.0) + behindPenalty;

                if (signedOffset >= 0.0)
                {
                    if (score < bestLeftScore)
                    {
                        bestLeft = edge;
                        bestLeftScore = score;
                    }
                }
                else
                {
                    if (score < bestRightScore)
                    {
                        bestRight = edge;
                        bestRightScore = score;
                    }
                }
            }

            if (bestLeft != null) chosen.Add(bestLeft);
            if (bestRight != null && !ReferenceEquals(bestLeft, bestRight)) chosen.Add(bestRight);
            return chosen;
        }

        private static List<ObjectId> FindRoadEdgesForCenterline(BlockTableRecord ms, Transaction tr, Line centerline, Point3d nearPoint, double halfWidth, double searchRadius)
        {
            List<ObjectId> candidates = new List<ObjectId>();
            Vector2d clDir = ToVector2d(centerline.StartPoint, centerline.EndPoint).GetNormal();
            Point2d clPoint = new Point2d(centerline.StartPoint.X, centerline.StartPoint.Y);
            Point3d near = nearPoint;
            double tolerance = Math.Max(5.0, Math.Abs(halfWidth) * 0.50);

            foreach (ObjectId id in ms)
            {
                if (!id.IsValid || id.IsErased) continue;
                if (tr.GetObject(id, OpenMode.ForRead) is not Line line) continue;
                if (!string.Equals(line.Layer, RoadEdgeLayer, StringComparison.OrdinalIgnoreCase)) continue;
                Point3d closest = line.GetClosestPointTo(near, extend: true);
                if (closest.DistanceTo(near) > searchRadius) continue;

                Vector2d ld = ToVector2d(line.StartPoint, line.EndPoint);
                if (ld.Length < 1e-8) continue;
                double parallel = Math.Abs(ld.GetNormal().DotProduct(clDir));
                if (parallel < 0.96) continue;

                double offset = DistancePointToLine(new Point2d(line.StartPoint.X, line.StartPoint.Y), clPoint, clDir);
                if (Math.Abs(offset - halfWidth) <= tolerance || halfWidth <= 0.0)
                {
                    candidates.Add(id);
                }
            }

            return candidates
                .Distinct()
                .OrderBy(id => ((Line)tr.GetObject(id, OpenMode.ForRead)).GetClosestPointTo(near, extend: true).DistanceTo(near))
                .ToList();
        }

        private static List<ObjectId> FindIntersectionRoadEdges(BlockTableRecord ms, Transaction tr, Line mainCenterline, Line intersectingCenterline, Point2d intersection, double halfWidth, double searchRadius)
        {
            Point3d center3d = ToPoint3d(intersection);
            List<ObjectId> result = new List<ObjectId>();
            result.AddRange(FindRoadEdgesForCenterline(ms, tr, mainCenterline, center3d, halfWidth, searchRadius).Take(2));
            result.AddRange(FindRoadEdgesForCenterline(ms, tr, intersectingCenterline, center3d, halfWidth, searchRadius).Take(2));
            return result.Distinct().ToList();
        }

        private static Curve? FindRoadEdgeForCenterlineSide(BlockTableRecord ms, Transaction tr, Curve centerline, Point2d nearPoint, double sideSign, double halfWidth, double searchRadius)
        {
            Point3d near3d = ToPoint3d(nearPoint);
            Point3d clClosest = centerline.GetClosestPointTo(near3d, extend: false);
            Vector2d clDir = GetCurveTangent2d(centerline, clClosest);
            Vector2d clNormal = new Vector2d(-clDir.Y, clDir.X);
            Point2d clPoint = new Point2d(clClosest.X, clClosest.Y);
            double offsetTolerance = Math.Max(2.0, Math.Abs(halfWidth) * 0.30);

            Curve? best = null;
            double bestDistance = double.MaxValue;
            foreach (ObjectId id in ms)
            {
                if (!id.IsValid || id.IsErased) continue;
                if (tr.GetObject(id, OpenMode.ForWrite, false) is not Curve edge) continue;
                if (edge is not Entity ent) continue;
                if (!string.Equals(ent.Layer, RoadEdgeLayer, StringComparison.OrdinalIgnoreCase)) continue;

                Point3d edgeClosest = edge.GetClosestPointTo(near3d, extend: false);
                Vector2d edgeDir = GetCurveTangent2d(edge, edgeClosest);
                if (edgeDir.Length < 1e-8) continue;
                if (Math.Abs(edgeDir.GetNormal().DotProduct(clDir)) < 0.90) continue;

                Point2d ep = new Point2d(edgeClosest.X, edgeClosest.Y);
                double signedOffset = (ep - clPoint).DotProduct(clNormal);
                if (Math.Sign(signedOffset) != Math.Sign(sideSign)) continue;
                if (Math.Abs(Math.Abs(signedOffset) - halfWidth) > offsetTolerance) continue;

                double d = edgeClosest.DistanceTo(near3d);
                if (d > searchRadius) continue;
                if (d < bestDistance)
                {
                    best = edge;
                    bestDistance = d;
                }
            }

            return best;
        }

        private static bool TryCreateCornerFilletArc(Point2d corner, Vector2d away1, Vector2d away2, double radius, out Arc? arc, out Point2d tangent1, out Point2d tangent2)
        {
            arc = null;
            tangent1 = Point2d.Origin;
            tangent2 = Point2d.Origin;
            if (radius <= 0.0) return false;

            Vector2d v1 = away1.GetNormal();
            Vector2d v2 = away2.GetNormal();
            double dot = Math.Max(-0.999999, Math.Min(0.999999, v1.DotProduct(v2)));
            double theta = Math.Acos(dot);
            if (theta < 0.05 || Math.Abs(Math.PI - theta) < 0.05) return false;

            // Tangent/tangent fillet from two rays that leave the theoretical corner.
            // The prior routine picked the arc center by testing normals from the first
            // tangent point. That could choose the wrong quadrant for T-intersections,
            // leaving one corner untrimmed or creating a return on the wrong side. This
            // version uses the internal angle-bisector solution:
            //   tangent distance = R / tan(theta/2)
            //   center distance  = R / sin(theta/2)
            // where v1/v2 are already the two directions that move away from the road
            // opening. The tangent points therefore land directly on the actual edge
            // lines and the cleanup routine can split/trim the opening reliably.
            double tangentDistance = radius / Math.Tan(theta / 2.0);
            double centerDistance = radius / Math.Sin(theta / 2.0);
            if (double.IsNaN(tangentDistance) || double.IsNaN(centerDistance)) return false;
            if (tangentDistance <= 0.0 || centerDistance <= 0.0 || tangentDistance > 10000.0 || centerDistance > 10000.0) return false;

            Vector2d bisector = v1 + v2;
            if (bisector.Length < 1e-8) return false;
            bisector = bisector.GetNormal();

            Point2d t1 = corner.Add(v1 * tangentDistance);
            Point2d t2 = corner.Add(v2 * tangentDistance);
            Point2d center = corner.Add(bisector * centerDistance);

            // Safety check: both tangent points should be one radius from the center.
            if (Math.Abs(center.GetDistanceTo(t1) - radius) > 0.05) return false;
            if (Math.Abs(center.GetDistanceTo(t2) - radius) > 0.05) return false;

            tangent1 = t1;
            tangent2 = t2;

            double startAngle = Math.Atan2(t1.Y - center.Y, t1.X - center.X);
            double endAngle = Math.Atan2(t2.Y - center.Y, t2.X - center.X);
            double sweep = NormalizeRadians(endAngle - startAngle);
            Point3d c3 = new Point3d(center.X, center.Y, 0.0);
            arc = sweep <= Math.PI
                ? new Arc(c3, radius, startAngle, endAngle)
                : new Arc(c3, radius, endAngle, startAngle);
            return true;
        }


        private static bool TryCreateLineCircleFillet(Line line, Point3d circleCenter3d, double circleRadius, double filletRadius, Vector2d stemDirection, out Arc? arc, out Point3d lineTangent, out Point3d circleTangent)
        {
            arc = null;
            lineTangent = Point3d.Origin;
            circleTangent = Point3d.Origin;
            if (circleRadius <= 0.0 || filletRadius <= 0.0) return false;

            Point2d c = new Point2d(circleCenter3d.X, circleCenter3d.Y);
            Point2d a = new Point2d(line.StartPoint.X, line.StartPoint.Y);
            Point2d b = new Point2d(line.EndPoint.X, line.EndPoint.Y);
            Vector2d edgeDir = b - a;
            if (edgeDir.Length < 1e-8) return false;
            edgeDir = edgeDir.GetNormal();

            Vector2d stem = stemDirection.Length < 1e-8 ? Vector2d.YAxis.Negate() : stemDirection.GetNormal();
            Vector2d sideAxis = new Vector2d(-stem.Y, stem.X);

            // Determine whether this edge is left or right of the selected centerline.
            Point2d closestOnEdgeToCenter = a.Add(edgeDir * ((c - a).DotProduct(edgeDir)));
            double edgeSide = Math.Sign((closestOnEdgeToCenter - c).DotProduct(sideAxis));
            if (Math.Abs(edgeSide) < 1e-8)
            {
                // Fall back to the endpoint furthest from the bulb center.
                Point2d far = a.GetDistanceTo(c) >= b.GetDistanceTo(c) ? a : b;
                edgeSide = Math.Sign((far - c).DotProduct(sideAxis));
                if (Math.Abs(edgeSide) < 1e-8) edgeSide = 1.0;
            }

            // Offset the road-edge line AWAY from the centerline by the tie-in radius.
            // This gives the centerline of the tie-in fillet circle. The previous routine
            // tested both sides of the road edge and could choose the inward solution,
            // which caused the pinched/wrong-way cul-de-sac throat.
            Vector2d normalA = new Vector2d(-edgeDir.Y, edgeDir.X);
            Vector2d outward = normalA.DotProduct(sideAxis * edgeSide) >= 0.0 ? normalA : normalA.Negate();
            Point2d offsetPoint = a.Add(outward * filletRadius);

            double requiredDistance = circleRadius + filletRadius; // external tangency to bulb circle
            Vector2d f = offsetPoint - c;
            double A = edgeDir.DotProduct(edgeDir);
            double B = 2.0 * f.DotProduct(edgeDir);
            double C = f.DotProduct(f) - requiredDistance * requiredDistance;
            double disc = B * B - 4.0 * A * C;
            if (disc < -1e-8) return false;
            disc = Math.Max(0.0, disc);
            double sqrt = Math.Sqrt(disc);

            List<(Point2d Center, Point2d LineTan, Point2d CircleTan, double Score)> candidates = new List<(Point2d, Point2d, Point2d, double)>();
            foreach (double t in new[] { (-B - sqrt) / (2.0 * A), (-B + sqrt) / (2.0 * A) })
            {
                Point2d filletCenter = offsetPoint.Add(edgeDir * t);
                Point2d lt = filletCenter.Add(outward.Negate() * filletRadius);
                Vector2d bulbRadial = (filletCenter - c);
                if (bulbRadial.Length < 1e-8) continue;
                bulbRadial = bulbRadial.GetNormal();
                Point2d ct = c.Add(bulbRadial * circleRadius);

                // Pick the lower/open throat solution, but keep it on the same left/right side.
                double sameSideScore = Math.Sign((ct - c).DotProduct(sideAxis)) == Math.Sign(edgeSide) ? 1000000.0 : -1000000.0;
                double openSideScore = (ct - c).DotProduct(stem) * 1000.0;
                double tangentLineScore = (lt - c).DotProduct(stem) * 100.0;
                candidates.Add((filletCenter, lt, ct, sameSideScore + openSideScore + tangentLineScore));
            }

            if (candidates.Count == 0) return false;
            var best = candidates.OrderByDescending(x => x.Score).First();

            double startAng = Math.Atan2(best.LineTan.Y - best.Center.Y, best.LineTan.X - best.Center.X);
            double endAng = Math.Atan2(best.CircleTan.Y - best.Center.Y, best.CircleTan.X - best.Center.X);
            double sweep = NormalizeRadians(endAng - startAng);
            Point3d filletCenter3d = ToPoint3d(best.Center);
            arc = sweep <= Math.PI
                ? new Arc(filletCenter3d, filletRadius, startAng, endAng)
                : new Arc(filletCenter3d, filletRadius, endAng, startAng);
            lineTangent = ToPoint3d(best.LineTan);
            circleTangent = ToPoint3d(best.CircleTan);
            return true;
        }

        private static Arc CreateBulbArc(Point3d center, double radius, Point3d p1, Point3d p2, Vector2d stemDirection)
        {
            double a1 = Math.Atan2(p1.Y - center.Y, p1.X - center.X);
            double a2 = Math.Atan2(p2.Y - center.Y, p2.X - center.X);
            Point2d c2 = new Point2d(center.X, center.Y);
            Point2d midA = MidAnglePoint(c2, radius, a1, a2);
            double stemDot = (midA - c2).DotProduct(stemDirection.GetNormal());

            // Use the arc that goes away from the stem/opening side of the road.
            if (stemDot > 0)
            {
                return new Arc(center, radius, a2, a1);
            }
            return new Arc(center, radius, a1, a2);
        }

        private static Point2d MidAnglePoint(Point2d center, double radius, double startAngle, double endAngle)
        {
            double sweep = NormalizeRadians(endAngle - startAngle);
            double mid = startAngle + sweep / 2.0;
            return new Point2d(center.X + Math.Cos(mid) * radius, center.Y + Math.Sin(mid) * radius);
        }

        private static void TrimLineEndNearestPoint(Line line, Point3d nearPoint, Point3d newEndPoint)
        {
            if (line.StartPoint.DistanceTo(nearPoint) <= line.EndPoint.DistanceTo(nearPoint))
            {
                line.StartPoint = newEndPoint;
            }
            else
            {
                line.EndPoint = newEndPoint;
            }
        }

        private static bool TrimOrExtendLineToCircle(Line line, Point3d center3d, double radius)
        {
            if (radius <= 0.0) return false;
            Point2d c = new Point2d(center3d.X, center3d.Y);
            Point2d a = new Point2d(line.StartPoint.X, line.StartPoint.Y);
            Point2d b = new Point2d(line.EndPoint.X, line.EndPoint.Y);
            Vector2d dir = b - a;
            if (dir.Length < 1e-8) return false;
            dir = dir.GetNormal();

            if (!TryInfiniteLineCircleIntersections(a, dir, c, radius, out Point2d i1, out Point2d i2)) return false;
            Point3d p1 = ToPoint3d(i1);
            Point3d p2 = ToPoint3d(i2);
            Point3d use = p1.DistanceTo(center3d) <= p2.DistanceTo(center3d) ? p1 : p2;
            if (line.StartPoint.DistanceTo(center3d) <= line.EndPoint.DistanceTo(center3d))
            {
                line.StartPoint = use;
            }
            else
            {
                line.EndPoint = use;
            }
            return true;
        }

        private static void TrimOrExtendLineToNearestIntersections(Line line, ObjectId[] trimIds, Transaction tr)
        {
            if (trimIds.Length == 0) return;
            Point2d a = new Point2d(line.StartPoint.X, line.StartPoint.Y);
            Point2d b = new Point2d(line.EndPoint.X, line.EndPoint.Y);
            List<Point3d> intersections = new List<Point3d>();

            foreach (ObjectId id in trimIds)
            {
                if (!id.IsValid || id.IsErased) continue;
                if (tr.GetObject(id, OpenMode.ForRead, false) is not Line other) continue;
                Point2d c = new Point2d(other.StartPoint.X, other.StartPoint.Y);
                Point2d d = new Point2d(other.EndPoint.X, other.EndPoint.Y);
                if (TryLineIntersection2d(a, b, c, d, out Point2d ip))
                {
                    intersections.Add(ToPoint3d(ip));
                }
            }

            if (intersections.Count == 0) return;

            Point3d startBest = intersections.OrderBy(p => p.DistanceTo(line.StartPoint)).First();
            Point3d endBest = intersections.OrderBy(p => p.DistanceTo(line.EndPoint)).First();
            if (startBest.DistanceTo(endBest) > 1e-6)
            {
                line.StartPoint = startBest;
                line.EndPoint = endBest;
            }
        }

        private static bool TryInfiniteLineCircleIntersections(Point2d linePoint, Vector2d lineDirection, Point2d circleCenter, double radius, out Point2d i1, out Point2d i2)
        {
            i1 = Point2d.Origin;
            i2 = Point2d.Origin;
            if (radius <= 0.0 || lineDirection.Length < 1e-8) return false;
            Vector2d d = lineDirection.GetNormal();
            Vector2d f = linePoint - circleCenter;
            double b = 2.0 * f.DotProduct(d);
            double c = f.DotProduct(f) - radius * radius;
            double discriminant = b * b - 4.0 * c;
            if (discriminant < -1e-8) return false;
            if (discriminant < 0.0) discriminant = 0.0;
            double root = Math.Sqrt(discriminant);
            double t1 = (-b - root) / 2.0;
            double t2 = (-b + root) / 2.0;
            i1 = linePoint.Add(d * t1);
            i2 = linePoint.Add(d * t2);
            return true;
        }

        private static ObjectId[] CollectNearbyRoadEdgeLines(BlockTableRecord ms, Transaction tr, Point2d center, double radius)
        {
            List<ObjectId> ids = new List<ObjectId>();
            Point3d center3d = ToPoint3d(center);
            foreach (ObjectId id in ms)
            {
                if (!id.IsValid || id.IsErased) continue;
                if (tr.GetObject(id, OpenMode.ForRead) is not Line line) continue;
                if (!string.Equals(line.Layer, RoadEdgeLayer, StringComparison.OrdinalIgnoreCase)) continue;
                Point3d closest = line.GetClosestPointTo(center3d, extend: false);
                if (closest.DistanceTo(center3d) <= radius)
                {
                    ids.Add(id);
                }
            }
            return ids.ToArray();
        }

        private static bool CleanRoadEdgeLineAtIntersection(Line line, List<Point3d> tangentPoints, BlockTableRecord ms, Transaction tr)
        {
            const double pointTolerance = 0.25;
            List<(Point3d Point, double T)> pointsOnLine = new List<(Point3d Point, double T)>();
            Vector3d dir = line.EndPoint - line.StartPoint;
            if (dir.Length < 1e-8) return false;
            Vector3d unit = dir.GetNormal();
            double lineLength = dir.Length;

            foreach (Point3d pt in tangentPoints)
            {
                Point3d closest = line.GetClosestPointTo(pt, extend: true);
                if (closest.DistanceTo(pt) > pointTolerance) continue;
                double t = (pt - line.StartPoint).DotProduct(unit);
                if (t < -pointTolerance || t > lineLength + pointTolerance) continue;

                bool duplicate = pointsOnLine.Any(x => x.Point.DistanceTo(pt) <= pointTolerance);
                if (!duplicate) pointsOnLine.Add((pt, t));
            }

            if (pointsOnLine.Count == 0) return false;

            pointsOnLine = pointsOnLine.OrderBy(x => x.T).ToList();
            line.Layer = RoadEdgeLayer;

            if (pointsOnLine.Count == 1)
            {
                Point3d tp = pointsOnLine[0].Point;
                if (line.StartPoint.DistanceTo(tp) <= line.EndPoint.DistanceTo(tp))
                {
                    line.StartPoint = tp;
                }
                else
                {
                    line.EndPoint = tp;
                }
                return true;
            }

            Point3d first = pointsOnLine.First().Point;
            Point3d last = pointsOnLine.Last().Point;
            double firstT = pointsOnLine.First().T;
            double lastT = pointsOnLine.Last().T;

            // If both tangent points fall inside a long edge, replace the crossing edge
            // with two outside edge segments and erase the middle crossing portion.
            if (firstT > pointTolerance && lastT < lineLength - pointTolerance)
            {
                Point3d originalStart = line.StartPoint;
                Point3d originalEnd = line.EndPoint;

                Line before = new Line(originalStart, first) { Layer = RoadEdgeLayer };
                Line after = new Line(last, originalEnd) { Layer = RoadEdgeLayer };
                ms.AppendEntity(before);
                tr.AddNewlyCreatedDBObject(before, true);
                ms.AppendEntity(after);
                tr.AddNewlyCreatedDBObject(after, true);
                line.Erase();
                return true;
            }

            if (firstT <= pointTolerance && lastT < lineLength - pointTolerance)
            {
                line.StartPoint = last;
                return true;
            }

            if (firstT > pointTolerance && lastT >= lineLength - pointTolerance)
            {
                line.EndPoint = first;
                return true;
            }

            return false;
        }

        private static bool TryCreateFilletArc(Point2d line1Point, Vector2d dir1, Point2d line2Point, Vector2d dir2, double radius, Point2d roadCenter, out Arc? arc, out Point2d tangent1, out Point2d tangent2)
        {
            arc = null;
            tangent1 = Point2d.Origin;
            tangent2 = Point2d.Origin;
            if (radius <= 0.0) return false;
            if (!TryLineIntersection2d(line1Point, line1Point.Add(dir1), line2Point, line2Point.Add(dir2), out Point2d corner)) return false;

            Vector2d v1 = ChooseAwayDirection(corner, dir1.GetNormal(), roadCenter);
            Vector2d v2 = ChooseAwayDirection(corner, dir2.GetNormal(), roadCenter);
            double dot = Math.Max(-0.999999, Math.Min(0.999999, v1.DotProduct(v2)));
            double theta = Math.Acos(dot);
            if (theta < 0.05 || Math.Abs(Math.PI - theta) < 0.05) return false;

            double tangent = radius / Math.Tan(theta / 2.0);
            if (double.IsNaN(tangent) || tangent <= 0.0 || tangent > 10000.0) return false;

            Point2d t1 = corner.Add(v1 * tangent);
            Point2d t2 = corner.Add(v2 * tangent);
            tangent1 = t1;
            tangent2 = t2;
            Vector2d n1 = new Vector2d(-v1.Y, v1.X);
            Point2d cA = t1.Add(n1 * radius);
            Point2d cB = t1.Add(n1.Negate() * radius);
            Point2d center = DistancePointToLine(cA, t2, v2) < DistancePointToLine(cB, t2, v2) ? cA : cB;

            double startAngle = Math.Atan2(t1.Y - center.Y, t1.X - center.X);
            double endAngle = Math.Atan2(t2.Y - center.Y, t2.X - center.X);
            double sweep = NormalizeRadians(endAngle - startAngle);
            Point3d c3 = new Point3d(center.X, center.Y, 0.0);
            arc = sweep <= Math.PI
                ? new Arc(c3, radius, startAngle, endAngle)
                : new Arc(c3, radius, endAngle, startAngle);
            return true;
        }

        private static Vector2d ChooseAwayDirection(Point2d origin, Vector2d direction, Point2d awayFrom)
        {
            double dPlus = origin.Add(direction).GetDistanceTo(awayFrom);
            double dMinus = origin.Add(direction.Negate()).GetDistanceTo(awayFrom);
            return dPlus >= dMinus ? direction : direction.Negate();
        }

        private static double DistancePointToLine(Point2d point, Point2d linePoint, Vector2d lineDirection)
        {
            Vector2d v = point - linePoint;
            Vector2d d = lineDirection.GetNormal();
            return Math.Abs(v.X * d.Y - v.Y * d.X);
        }

        private static Vector2d GetCurveTangent2d(Curve curve, Point3d nearPoint)
        {
            try
            {
                Point3d p = curve.GetClosestPointTo(nearPoint, extend: false);
                double param = curve.GetParameterAtPoint(p);
                Vector3d d = curve.GetFirstDerivative(param);
                Vector2d v = new Vector2d(d.X, d.Y);
                if (v.Length > 1e-8) return v.GetNormal();
            }
            catch
            {
                // Fall through to endpoint direction.
            }

            Vector2d fallback = ToVector2d(curve.StartPoint, curve.EndPoint);
            return fallback.Length < 1e-8 ? Vector2d.XAxis : fallback.GetNormal();
        }

        private static bool TryCurveIntersection2d(Curve a, Curve b, out Point2d intersection)
        {
            intersection = Point2d.Origin;
            try
            {
                Point3dCollection points = new Point3dCollection();
                a.IntersectWith(b, Intersect.OnBothOperands, points, IntPtr.Zero, IntPtr.Zero);
                if (points.Count > 0)
                {
                    Point3d p = points[0];
                    intersection = new Point2d(p.X, p.Y);
                    return true;
                }

                points = new Point3dCollection();
                a.IntersectWith(b, Intersect.ExtendBoth, points, IntPtr.Zero, IntPtr.Zero);
                if (points.Count > 0)
                {
                    Point3d p = points[0];
                    intersection = new Point2d(p.X, p.Y);
                    return true;
                }
            }
            catch
            {
                // If the AutoCAD intersection API fails, fall back to line chord intersection.
            }

            return TryLineIntersection2d(a.StartPoint, a.EndPoint, b.StartPoint, b.EndPoint, out intersection);
        }

        private static Line? CreateTangentWorkLine(Curve curve, Point3d nearPoint, double length)
        {
            try
            {
                Point3d p = curve.GetClosestPointTo(nearPoint, extend: false);
                Vector2d t = GetCurveTangent2d(curve, p);
                if (t.Length < 1e-8) return null;
                Vector3d v = new Vector3d(t.X, t.Y, 0.0).GetNormal();
                double l = Math.Max(10.0, length);
                return new Line(p - v * l, p + v * l);
            }
            catch
            {
                return null;
            }
        }

        private static void TrimCurveEndNearestPoint(Curve curve, Point3d nearPoint, Point3d newEndPoint)
        {
            if (curve is Line line)
            {
                TrimLineEndNearestPoint(line, nearPoint, newEndPoint);
                return;
            }

            if (curve is Arc arc)
            {
                double angle = Math.Atan2(newEndPoint.Y - arc.Center.Y, newEndPoint.X - arc.Center.X);
                if (arc.StartPoint.DistanceTo(nearPoint) <= arc.EndPoint.DistanceTo(nearPoint))
                {
                    arc.StartAngle = angle;
                }
                else
                {
                    arc.EndAngle = angle;
                }
            }
        }

        private static bool CleanRoadEdgeCurveAtIntersection(Curve curve, List<Point3d> tangentPoints, BlockTableRecord ms, Transaction tr)
        {
            if (curve is Line line)
            {
                return CleanRoadEdgeLineAtIntersection(line, tangentPoints, ms, tr);
            }

            if (curve is not Arc arc || tangentPoints.Count < 2) return false;

            Point3d p1 = arc.GetClosestPointTo(tangentPoints[0], extend: true);
            Point3d p2 = arc.GetClosestPointTo(tangentPoints[1], extend: true);
            double aStart = arc.StartAngle;
            double aEnd = arc.EndAngle;
            double a1 = Math.Atan2(p1.Y - arc.Center.Y, p1.X - arc.Center.X);
            double a2 = Math.Atan2(p2.Y - arc.Center.Y, p2.X - arc.Center.X);

            double t1 = NormalizeRadians(a1 - aStart);
            double t2 = NormalizeRadians(a2 - aStart);
            double total = NormalizeRadians(aEnd - aStart);
            if (total < 1e-8) total = Math.PI * 2.0;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
                (a1, a2) = (a2, a1);
            }

            if (t1 > 0.01)
            {
                Arc first = new Arc(arc.Center, arc.Radius, aStart, a1) { Layer = arc.Layer };
                ms.AppendEntity(first);
                tr.AddNewlyCreatedDBObject(first, true);
            }
            if (t2 < total - 0.01)
            {
                Arc second = new Arc(arc.Center, arc.Radius, a2, aEnd) { Layer = arc.Layer };
                ms.AppendEntity(second);
                tr.AddNewlyCreatedDBObject(second, true);
            }
            arc.Erase();
            return true;
        }

        private static bool TryCurveCurveIntersection2d(Curve a, Curve b, Point2d nearHint, out Point2d intersection)
        {
            intersection = Point2d.Origin;
            Point3d best = Point3d.Origin;
            double bestDistance = double.MaxValue;
            bool found = false;

            void Consider(Point3dCollection points)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    Point3d p = points[i];
                    double d = new Point2d(p.X, p.Y).GetDistanceTo(nearHint);
                    if (d < bestDistance)
                    {
                        best = p;
                        bestDistance = d;
                        found = true;
                    }
                }
            }

            try
            {
                Point3dCollection points = new Point3dCollection();
                a.IntersectWith(b, Intersect.OnBothOperands, points, IntPtr.Zero, IntPtr.Zero);
                Consider(points);

                points = new Point3dCollection();
                a.IntersectWith(b, Intersect.ExtendBoth, points, IntPtr.Zero, IntPtr.Zero);
                Consider(points);
            }
            catch
            {
                // Fall back below.
            }

            if (found)
            {
                intersection = new Point2d(best.X, best.Y);
                return true;
            }

            return TryLineIntersection2d(a.StartPoint, a.EndPoint, b.StartPoint, b.EndPoint, out intersection);
        }

        private static bool TryLineIntersection2d(Point3d a1, Point3d a2, Point3d b1, Point3d b2, out Point2d intersection)
        {
            return TryLineIntersection2d(new Point2d(a1.X, a1.Y), new Point2d(a2.X, a2.Y), new Point2d(b1.X, b1.Y), new Point2d(b2.X, b2.Y), out intersection);
        }

        private static bool TryLineIntersection2d(Point2d a1, Point2d a2, Point2d b1, Point2d b2, out Point2d intersection)
        {
            double x1 = a1.X, y1 = a1.Y, x2 = a2.X, y2 = a2.Y;
            double x3 = b1.X, y3 = b1.Y, x4 = b2.X, y4 = b2.Y;
            double den = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
            if (Math.Abs(den) < 1e-10)
            {
                intersection = Point2d.Origin;
                return false;
            }

            double px = ((x1 * y2 - y1 * x2) * (x3 - x4) - (x1 - x2) * (x3 * y4 - y3 * x4)) / den;
            double py = ((x1 * y2 - y1 * x2) * (y3 - y4) - (y1 - y2) * (x3 * y4 - y3 * x4)) / den;
            intersection = new Point2d(px, py);
            return true;
        }

        private static Point3d ToPoint3d(Point2d point)
        {
            return new Point3d(point.X, point.Y, 0.0);
        }

        private static Vector2d ToVector2d(Point3d a, Point3d b)
        {
            return new Vector2d(b.X - a.X, b.Y - a.Y);
        }

        private static double NormalizeRadians(double radians)
        {
            double twoPi = Math.PI * 2.0;
            radians %= twoPi;
            return radians < 0 ? radians + twoPi : radians;
        }

        private static void EnsureLayer(Database db, Transaction tr, string layerName, short colorIndex, string preferredPlotStyle)
        {
            LayerTable layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!layerTable.Has(layerName))
            {
                layerTable.UpgradeOpen();
                LayerTableRecord layer = new LayerTableRecord
                {
                    Name = layerName,
                    Color = AcadColor.FromColorIndex(ColorMethod.ByAci, colorIndex)
                };
                TrySetPlotStyleName(layer, preferredPlotStyle);
                layerTable.Add(layer);
                tr.AddNewlyCreatedDBObject(layer, true);
            }
            else
            {
                LayerTableRecord layer = (LayerTableRecord)tr.GetObject(layerTable[layerName], OpenMode.ForWrite);
                if (layer.Color.ColorIndex == 7 || layer.Color.ColorIndex == 0)
                {
                    layer.Color = AcadColor.FromColorIndex(ColorMethod.ByAci, colorIndex);
                }
                TrySetPlotStyleName(layer, preferredPlotStyle);
            }
        }

        private static void TrySetPlotStyleName(LayerTableRecord layer, string preferredPlotStyle)
        {
            try
            {
                System.Reflection.PropertyInfo? prop = layer.GetType().GetProperty("PlotStyleName");
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(layer, preferredPlotStyle, null);
                }
            }
            catch
            {
                // Plot style setup should never block geometry creation.
            }
        }

        private sealed class SiteSettingsForm : Form
        {
            private readonly NumericUpDown _roadWidth = CreateNumber(50);
            private readonly NumericUpDown _culdesacRadius = CreateNumber(50);
            private readonly NumericUpDown _culdesacTieInRadius = CreateNumber(25);
            private readonly NumericUpDown _curbReturnRadius = CreateNumber(25);

            public SiteSettingsForm(double roadWidth, double culdesacRadius, double culdesacTieInRadius, double curbReturnRadius)
            {
                Width = 390;
                Height = 235;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterScreen;
                MaximizeBox = false;
                MinimizeBox = false;

                _roadWidth.Value = ClampDecimal(roadWidth);
                _culdesacRadius.Value = ClampDecimal(culdesacRadius);
                _culdesacTieInRadius.Value = ClampDecimal(culdesacTieInRadius);
                _curbReturnRadius.Value = ClampDecimal(curbReturnRadius);

                TableLayoutPanel panel = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 2,
                    RowCount = 5,
                    Padding = new Padding(10),
                    AutoSize = true
                };
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

                AddRow(panel, 0, "Typical road width", _roadWidth);
                AddRow(panel, 1, "Cul-de-sac radius", _culdesacRadius);
                AddRow(panel, 2, "Cul-de-sac tie-in radius", _culdesacTieInRadius);
                AddRow(panel, 3, "Curb return radius", _curbReturnRadius);
                AddButtons(panel, 4);

                Controls.Add(panel);
            }

            public double TypicalRoadWidth => (double)_roadWidth.Value;
            public double CuldesacRadius => (double)_culdesacRadius.Value;
            public double CuldesacTieInRadius => (double)_culdesacTieInRadius.Value;
            public double CurbReturnRadius => (double)_curbReturnRadius.Value;
        }

        private sealed class CuldesacOptionsForm : Form
        {
            private readonly NumericUpDown _culdesacRadius = CreateNumber(50);
            private readonly NumericUpDown _tieInRadius = CreateNumber(25);
            private readonly NumericUpDown _roadWidth = CreateNumber(50);

            public CuldesacOptionsForm(double culdesacRadius, double tieInRadius, double roadWidth)
            {
                Width = 380;
                Height = 205;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterScreen;
                MaximizeBox = false;
                MinimizeBox = false;

                _culdesacRadius.Value = ClampDecimal(culdesacRadius);
                _tieInRadius.Value = ClampDecimal(tieInRadius);
                _roadWidth.Value = ClampDecimal(roadWidth);

                TableLayoutPanel panel = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 2,
                    RowCount = 4,
                    Padding = new Padding(10),
                    AutoSize = true
                };
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
                panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));

                AddRow(panel, 0, "Cul-de-sac radius", _culdesacRadius);
                AddRow(panel, 1, "Tie-in radius", _tieInRadius);
                AddRow(panel, 2, "Road width", _roadWidth);
                AddButtons(panel, 3);

                Controls.Add(panel);
            }

            public double CuldesacRadius => (double)_culdesacRadius.Value;
            public double TieInRadius => (double)_tieInRadius.Value;
            public double RoadWidth => (double)_roadWidth.Value;
        }

        private static NumericUpDown CreateNumber(decimal value)
        {
            return new NumericUpDown
            {
                Minimum = 0,
                Maximum = 10000,
                DecimalPlaces = 2,
                Increment = 1,
                Value = value,
                Dock = DockStyle.Fill
            };
        }

        private static decimal ClampDecimal(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0m;
            return Math.Max(0m, Math.Min(10000m, (decimal)value));
        }

        private static void AddRow(TableLayoutPanel panel, int row, string label, Control control)
        {
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, row);
            panel.Controls.Add(control, 1, row);
        }

        private static void AddButtons(TableLayoutPanel panel, int row)
        {
            FlowLayoutPanel buttons = new FlowLayoutPanel { FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            Button ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 80 };
            Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80 };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            panel.Controls.Add(buttons, 0, row);
            panel.SetColumnSpan(buttons, 2);
            Form? form = panel.FindForm();
            if (form != null)
            {
                form.AcceptButton = ok;
                form.CancelButton = cancel;
            }
        }
    }
}
