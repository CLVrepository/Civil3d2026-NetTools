using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcPolyline = Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace CLV_CivilTools.Gis
{
    /// <summary>
    /// Trims GIS pipe wall linework back to GIS structure outer walls after GIS PREP - ALL.
    /// Automatic mode uses closed structure outer wall geometry on C-STRM-STRC-E / C-SSWR-STRC-E.
    /// If automatic mode makes no edits, the command falls back to one selected trim edge and trims every
    /// GIS pipe wall crossing that edge by removing the shorter side past the edge.
    /// </summary>
    public static class GisTrimPipesCommands
    {
        private const string StormStructureOuterLayer = "C-STRM-STRC-E";
        private const string SewerStructureOuterLayer = "C-SSWR-STRC-E";
        private const string StormPipeWallLayer = "C-STRM-PIPE-E";
        private const string SewerPipeWallLayer = "C-SSWR-PIPE-E";
        private const double ExtentsPadding = 0.05;
        private const double EndpointSnapToBoundaryMaxDistance = 8.0;
        private const double DuplicateParamTolerance = 1e-7;

        [CommandMethod("CLV-GIS-TRIM-PIPES", CommandFlags.Modal)]
        public static void TrimPipes()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                using DocumentLock docLock = doc.LockDocument();

                int automaticTrimmed = RunAutomaticTrim(db, ed, out int boundaryCount, out int pipeCount);
                if (automaticTrimmed > 0)
                {
                    ed.WriteMessage($"\nCLV-GIS-TRIM-PIPES complete. Automatic trims={automaticTrimmed}, boundaries={boundaryCount}, pipe candidates={pipeCount}.");
                    return;
                }

                ed.WriteMessage($"\nCLV-GIS-TRIM-PIPES: automatic pass made no trims. Boundaries={boundaryCount}, pipe candidates={pipeCount}.");
                ed.WriteMessage("\nSelect one structure outer wall edge to trim all crossing GIS pipe wall lines, or press ESC to cancel.");

                int manualTrimmed = RunManualTrim(db, ed);
                if (manualTrimmed > 0)
                    ed.WriteMessage($"\nCLV-GIS-TRIM-PIPES complete. Manual trims={manualTrimmed}.");
                else
                    ed.WriteMessage("\nCLV-GIS-TRIM-PIPES complete. No pipe wall lines were trimmed.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-TRIM-PIPES failed: {ex.Message}");
            }
        }

        private static int RunAutomaticTrim(Database db, Editor ed, out int boundaryCount, out int pipeCount)
        {
            boundaryCount = 0;
            pipeCount = 0;
            int trimmed = 0;

            using Transaction tr = db.TransactionManager.StartTransaction();
            List<BoundaryCandidate> boundaries = CollectAutomaticBoundaries(tr, db);
            List<OpenEdgeCandidate> openEdges = CollectAutomaticOpenEdges(tr, db);
            boundaryCount = boundaries.Count + openEdges.Count;

            if (boundaries.Count == 0 && openEdges.Count == 0)
            {
                tr.Commit();
                return 0;
            }

            List<ObjectId> pipeIds = CollectPipeWallIds(tr, db, onlyLayers: null);
            pipeCount = pipeIds.Count;

            foreach (BoundaryCandidate boundary in boundaries)
            {
                if (!boundary.Id.IsValid || boundary.Id.IsErased)
                    continue;

                if (tr.GetObject(boundary.Id, OpenMode.ForRead, false) is not Curve boundaryCurve)
                    continue;

                string pipeLayer = GetMatchingPipeLayer(boundaryCurve.Layer);
                if (string.IsNullOrWhiteSpace(pipeLayer))
                    continue;

                foreach (ObjectId pipeId in pipeIds)
                {
                    if (!pipeId.IsValid || pipeId.IsErased)
                        continue;

                    if (tr.GetObject(pipeId, OpenMode.ForWrite, false) is not Curve pipeCurve || pipeCurve.IsErased)
                        continue;

                    if (!IsLayer(pipeCurve, pipeLayer))
                        continue;

                    if (!ExtentsIntersect(boundary.Extents, TryGetExtents(pipeCurve), EndpointSnapToBoundaryMaxDistance))
                        continue;

                    TrimResult result = TrimPipeToClosedBoundary(tr, pipeCurve, boundaryCurve, boundary.Info);
                    if (result == TrimResult.Trimmed)
                        trimmed++;
                }
            }

            foreach (OpenEdgeCandidate openEdge in openEdges)
            {
                if (!openEdge.Id.IsValid || openEdge.Id.IsErased)
                    continue;

                if (tr.GetObject(openEdge.Id, OpenMode.ForRead, false) is not Curve trimEdge)
                    continue;

                string pipeLayer = GetMatchingPipeLayer(trimEdge.Layer);
                if (string.IsNullOrWhiteSpace(pipeLayer))
                    continue;

                foreach (ObjectId pipeId in pipeIds)
                {
                    if (!pipeId.IsValid || pipeId.IsErased)
                        continue;

                    if (tr.GetObject(pipeId, OpenMode.ForWrite, false) is not Curve pipeCurve || pipeCurve.IsErased)
                        continue;

                    if (!IsLayer(pipeCurve, pipeLayer))
                        continue;

                    if (!ExtentsIntersect(openEdge.Extents, TryGetExtents(pipeCurve), EndpointSnapToBoundaryMaxDistance))
                        continue;

                    TrimResult result = TrimPipeToOpenEdge(tr, pipeCurve, trimEdge);
                    if (result == TrimResult.Trimmed)
                        trimmed++;
                }
            }

            tr.Commit();
            return trimmed;
        }

        private static int RunManualTrim(Database db, Editor ed)
        {
            PromptEntityOptions peo = new PromptEntityOptions("\nSelect trim edge or closed structure outer wall: ");
            peo.SetRejectMessage("\nSelect a line, arc, circle, ellipse, or polyline.");
            peo.AddAllowedClass(typeof(Line), exactMatch: false);
            peo.AddAllowedClass(typeof(Arc), exactMatch: false);
            peo.AddAllowedClass(typeof(Circle), exactMatch: false);
            peo.AddAllowedClass(typeof(Ellipse), exactMatch: false);
            peo.AddAllowedClass(typeof(AcPolyline), exactMatch: false);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
                return 0;

            int trimmed = 0;
            using Transaction tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(per.ObjectId, OpenMode.ForRead, false) is not Curve trimEdge)
                return 0;

            BoundaryInfo? boundaryInfo = TryCreateBoundaryInfo(trimEdge, out BoundaryInfo? info) ? info : null;
            Extents3d? edgeExtents = TryGetExtents(trimEdge);
            List<ObjectId> pipeIds = CollectPipeWallIds(tr, db, onlyLayers: null);

            foreach (ObjectId pipeId in pipeIds)
            {
                if (!pipeId.IsValid || pipeId.IsErased || pipeId == per.ObjectId)
                    continue;

                if (tr.GetObject(pipeId, OpenMode.ForWrite, false) is not Curve pipeCurve || pipeCurve.IsErased)
                    continue;

                if (!ExtentsIntersect(edgeExtents, TryGetExtents(pipeCurve), EndpointSnapToBoundaryMaxDistance))
                    continue;

                TrimResult result = boundaryInfo != null
                    ? TrimPipeToClosedBoundary(tr, pipeCurve, trimEdge, boundaryInfo)
                    : TrimPipeToOpenEdge(tr, pipeCurve, trimEdge);

                if (result == TrimResult.Trimmed)
                    trimmed++;
            }

            tr.Commit();
            return trimmed;
        }

        private static List<BoundaryCandidate> CollectAutomaticBoundaries(Transaction tr, Database db)
        {
            List<BoundaryCandidate> result = new List<BoundaryCandidate>();
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                if (!id.IsValid || id.IsErased)
                    continue;

                if (tr.GetObject(id, OpenMode.ForRead, false) is not Curve curve || curve.IsErased)
                    continue;

                if (!IsStructureOuterLayer(curve.Layer))
                    continue;

                if (!TryCreateBoundaryInfo(curve, out BoundaryInfo? info) || info == null)
                    continue;

                result.Add(new BoundaryCandidate(id, info.Extents, info));
            }

            return result;
        }


        private static List<OpenEdgeCandidate> CollectAutomaticOpenEdges(Transaction tr, Database db)
        {
            List<OpenEdgeCandidate> result = new List<OpenEdgeCandidate>();
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                if (!id.IsValid || id.IsErased)
                    continue;

                if (tr.GetObject(id, OpenMode.ForRead, false) is not Curve curve || curve.IsErased)
                    continue;

                if (!IsStructureOuterLayer(curve.Layer))
                    continue;

                if (TryCreateBoundaryInfo(curve, out BoundaryInfo? _))
                    continue;

                if (curve is Line || curve is Arc || curve is Ellipse || curve is AcPolyline)
                {
                    Extents3d? ext = TryGetExtents(curve);
                    if (ext.HasValue)
                        result.Add(new OpenEdgeCandidate(id, ext.Value));
                }
            }

            return result;
        }

        private static List<ObjectId> CollectPipeWallIds(Transaction tr, Database db, HashSet<string>? onlyLayers)
        {
            List<ObjectId> result = new List<ObjectId>();
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                if (!id.IsValid || id.IsErased)
                    continue;

                if (tr.GetObject(id, OpenMode.ForRead, false) is not Curve curve || curve.IsErased)
                    continue;

                if (!IsPipeWallLayer(curve.Layer))
                    continue;

                if (onlyLayers != null && !onlyLayers.Contains(curve.Layer))
                    continue;

                if (curve is AcPolyline pl && pl.Closed)
                    continue;

                result.Add(id);
            }

            return result;
        }

        private static TrimResult TrimPipeToClosedBoundary(Transaction tr, Curve pipeCurve, Curve boundaryCurve, BoundaryInfo boundaryInfo)
        {
            List<ParamHit> hits = GetIntersectionParams(pipeCurve, boundaryCurve);
            if (hits.Count == 0)
                return SnapNearestEndpointToExtendedBoundary(pipeCurve, boundaryCurve);

            Point3d start = pipeCurve.StartPoint;
            Point3d end = pipeCurve.EndPoint;
            bool startInside = boundaryInfo.Contains(start);
            bool endInside = boundaryInfo.Contains(end);

            if (hits.Count == 1)
            {
                double splitParam = hits[0].Param;
                if (startInside && !endInside)
                    return KeepOneSideAfterSplit(tr, pipeCurve, splitParam, keepStartSide: false);

                if (endInside && !startInside)
                    return KeepOneSideAfterSplit(tr, pipeCurve, splitParam, keepStartSide: true);

                return TrimShortestSideAtParam(tr, pipeCurve, splitParam);
            }

            return KeepOutsidePiecesAfterSplit(tr, pipeCurve, hits.Select(x => x.Param).ToList(), boundaryInfo);
        }

        private static TrimResult TrimPipeToOpenEdge(Transaction tr, Curve pipeCurve, Curve trimEdge)
        {
            List<ParamHit> hits = GetIntersectionParams(pipeCurve, trimEdge);
            if (hits.Count == 0)
                return SnapNearestEndpointToExtendedBoundary(pipeCurve, trimEdge);

            if (hits.Count == 1)
                return TrimShortestSideAtParam(tr, pipeCurve, hits[0].Param);

            // For an open manually selected trim edge, each pipe wall should normally cross once.
            // If more than one crossing is found, trim at the crossing nearest to either endpoint,
            // which matches the "shortest past the wall" cleanup behavior.
            ParamHit nearestEndHit = hits
                .OrderBy(h => Math.Min(SafeDistanceAtParam(pipeCurve, h.Param), SafeTotalLength(pipeCurve) - SafeDistanceAtParam(pipeCurve, h.Param)))
                .First();

            return TrimShortestSideAtParam(tr, pipeCurve, nearestEndHit.Param);
        }


        private static TrimResult SnapNearestEndpointToExtendedBoundary(Curve pipeCurve, Curve trimCurve)
        {
            List<Point3d> hits = GetIntersectionPoints(pipeCurve, trimCurve, Intersect.ExtendThis);
            if (hits.Count == 0)
                return TrimResult.None;

            Point3d start = pipeCurve.StartPoint;
            Point3d end = pipeCurve.EndPoint;

            EndpointHit? best = null;
            foreach (Point3d hit in hits)
            {
                double startDistance = Distance2d(start, hit);
                double endDistance = Distance2d(end, hit);
                bool useStart = startDistance <= endDistance;
                double distance = useStart ? startDistance : endDistance;

                if (distance <= DuplicateParamTolerance || distance > EndpointSnapToBoundaryMaxDistance)
                    continue;

                if (best == null || distance < best.Distance)
                    best = new EndpointHit(hit, useStart, distance);
            }

            if (best == null)
                return TrimResult.None;

            return TryMoveEndpointInPlace(pipeCurve, best.Point, best.UseStartPoint)
                ? TrimResult.Trimmed
                : TrimResult.Skipped;
        }

        private static bool TryMoveEndpointInPlace(Curve pipeCurve, Point3d point, bool moveStartPoint)
        {
            try
            {
                if (pipeCurve is Line line)
                {
                    if (moveStartPoint)
                        line.StartPoint = point;
                    else
                        line.EndPoint = point;

                    return true;
                }

                if (pipeCurve is AcPolyline polyline && polyline.NumberOfVertices >= 2)
                {
                    int index = moveStartPoint ? 0 : polyline.NumberOfVertices - 1;
                    polyline.SetPointAt(index, new Point2d(point.X, point.Y));
                    return true;
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        private static List<Point3d> GetIntersectionPoints(Curve pipeCurve, Curve trimCurve, Intersect intersectType)
        {
            Point3dCollection intersections = new Point3dCollection();
            try
            {
                pipeCurve.IntersectWith(trimCurve, intersectType, intersections, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
                return new List<Point3d>();
            }

            List<Point3d> points = new List<Point3d>();
            foreach (Point3d pt in intersections.Cast<Point3d>())
            {
                bool duplicate = points.Any(existing => Distance2d(existing, pt) <= DuplicateParamTolerance);
                if (!duplicate)
                    points.Add(pt);
            }

            return points;
        }

        private static List<ParamHit> GetIntersectionParams(Curve pipeCurve, Curve trimCurve)
        {
            Point3dCollection intersections = new Point3dCollection();
            try
            {
                pipeCurve.IntersectWith(trimCurve, Intersect.OnBothOperands, intersections, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
                return new List<ParamHit>();
            }

            List<ParamHit> hits = new List<ParamHit>();
            foreach (Point3d pt in intersections.Cast<Point3d>())
            {
                try
                {
                    Point3d onPipe = pipeCurve.GetClosestPointTo(pt, false);
                    double param = pipeCurve.GetParameterAtPoint(onPipe);
                    if (IsAtCurveEnd(pipeCurve, param))
                        continue;

                    hits.Add(new ParamHit(param, onPipe));
                }
                catch
                {
                    // Skip invalid intersection points.
                }
            }

            return hits
                .OrderBy(h => h.Param)
                .GroupBy(h => Math.Round(h.Param / DuplicateParamTolerance) * DuplicateParamTolerance)
                .Select(g => g.First())
                .ToList();
        }

        private static TrimResult TrimShortestSideAtParam(Transaction tr, Curve pipeCurve, double splitParam)
        {
            double startLength = SafeDistanceAtParam(pipeCurve, splitParam);
            double endLength = SafeTotalLength(pipeCurve) - startLength;

            if (double.IsNaN(startLength) || double.IsNaN(endLength))
                return TrimResult.Skipped;

            bool keepStartSide = startLength >= endLength;
            return KeepOneSideAfterSplit(tr, pipeCurve, splitParam, keepStartSide);
        }

        private static TrimResult KeepOneSideAfterSplit(Transaction tr, Curve pipeCurve, double splitParam, bool keepStartSide)
        {
            if (IsAtCurveEnd(pipeCurve, splitParam))
                return TrimResult.None;

            if (TryTrimEndpointInPlace(pipeCurve, splitParam, keepStartSide))
                return TrimResult.Trimmed;

            DBObjectCollection pieces;
            try
            {
                pieces = pipeCurve.GetSplitCurves(new DoubleCollection { splitParam });
            }
            catch
            {
                return TrimResult.Skipped;
            }

            if (pieces.Count < 2)
            {
                DisposePieces(pieces);
                return TrimResult.Skipped;
            }

            Point3d target = keepStartSide ? pipeCurve.StartPoint : pipeCurve.EndPoint;
            Curve? kept = null;
            double best = double.MaxValue;
            foreach (DBObject dbo in pieces)
            {
                if (dbo is not Curve piece)
                    continue;

                double score = Math.Min(piece.StartPoint.DistanceTo(target), piece.EndPoint.DistanceTo(target));
                if (score < best)
                {
                    kept = piece;
                    best = score;
                }
            }

            if (kept == null)
            {
                DisposePieces(pieces);
                return TrimResult.Skipped;
            }

            BlockTableRecord owner = (BlockTableRecord)tr.GetObject(pipeCurve.OwnerId, OpenMode.ForWrite);
            foreach (DBObject dbo in pieces)
            {
                if (ReferenceEquals(dbo, kept))
                    continue;

                dbo.Dispose();
            }

            owner.AppendEntity(kept);
            tr.AddNewlyCreatedDBObject(kept, true);
            pipeCurve.Erase(true);
            return TrimResult.Trimmed;
        }

        private static TrimResult KeepOutsidePiecesAfterSplit(Transaction tr, Curve pipeCurve, List<double> splitParams, BoundaryInfo boundaryInfo)
        {
            List<double> validParams = splitParams
                .Where(p => !IsAtCurveEnd(pipeCurve, p))
                .DistinctBy(p => Math.Round(p / DuplicateParamTolerance) * DuplicateParamTolerance)
                .OrderBy(p => p)
                .ToList();

            if (validParams.Count == 0)
                return TrimResult.None;

            DBObjectCollection pieces;
            try
            {
                DoubleCollection dc = new DoubleCollection();
                foreach (double p in validParams)
                    dc.Add(p);

                pieces = pipeCurve.GetSplitCurves(dc);
            }
            catch
            {
                return TrimResult.Skipped;
            }

            try
            {
                List<Curve> keepers = new List<Curve>();
                foreach (DBObject dbo in pieces)
                {
                    if (dbo is not Curve piece)
                    {
                        dbo.Dispose();
                        continue;
                    }

                    Point3d sample = GetMidPoint(piece);
                    if (!boundaryInfo.Contains(sample))
                        keepers.Add(piece);
                }

                if (keepers.Count == 0 || keepers.Count == pieces.Count)
                {
                    DisposePieces(pieces);
                    return TrimResult.Skipped;
                }

                BlockTableRecord owner = (BlockTableRecord)tr.GetObject(pipeCurve.OwnerId, OpenMode.ForWrite);
                foreach (DBObject dbo in pieces)
                {
                    if (dbo is Curve piece && keepers.Contains(piece))
                    {
                        owner.AppendEntity(piece);
                        tr.AddNewlyCreatedDBObject(piece, true);
                    }
                    else
                    {
                        dbo.Dispose();
                    }
                }

                pipeCurve.Erase(true);
                return TrimResult.Trimmed;
            }
            catch
            {
                foreach (DBObject dbo in pieces)
                    dbo.Dispose();

                return TrimResult.Skipped;
            }
        }

        private static bool TryTrimEndpointInPlace(Curve pipeCurve, double splitParam, bool keepStartSide)
        {
            try
            {
                Point3d splitPoint = pipeCurve.GetPointAtParameter(splitParam);
                if (pipeCurve is Line line)
                {
                    if (keepStartSide)
                        line.EndPoint = splitPoint;
                    else
                        line.StartPoint = splitPoint;

                    return true;
                }

                if (pipeCurve is AcPolyline polyline)
                    return TryTrimPolylineEndpointInPlace(polyline, splitParam, keepStartSide);
            }
            catch
            {
                // Fall back to split/recreate.
            }

            return false;
        }

        private static bool TryTrimPolylineEndpointInPlace(AcPolyline original, double splitParam, bool keepStartSide)
        {
            DBObjectCollection pieces;
            try
            {
                pieces = original.GetSplitCurves(new DoubleCollection { splitParam });
            }
            catch
            {
                return false;
            }

            try
            {
                if (pieces.Count < 2)
                    return false;

                Point3d target = keepStartSide ? original.StartPoint : original.EndPoint;
                AcPolyline? kept = null;
                double best = double.MaxValue;

                foreach (DBObject dbo in pieces)
                {
                    if (dbo is not AcPolyline piece)
                        continue;

                    double score = Math.Min(piece.StartPoint.DistanceTo(target), piece.EndPoint.DistanceTo(target));
                    if (score < best)
                    {
                        kept = piece;
                        best = score;
                    }
                }

                if (kept == null || kept.NumberOfVertices < 2)
                    return false;

                original.Closed = false;
                while (original.NumberOfVertices > 0)
                    original.RemoveVertexAt(original.NumberOfVertices - 1);

                original.Elevation = kept.Elevation;
                original.Normal = kept.Normal;
                for (int i = 0; i < kept.NumberOfVertices; i++)
                    original.AddVertexAt(i, kept.GetPoint2dAt(i), kept.GetBulgeAt(i), kept.GetStartWidthAt(i), kept.GetEndWidthAt(i));

                return true;
            }
            finally
            {
                foreach (DBObject dbo in pieces)
                    dbo.Dispose();
            }
        }

        private static void DisposePieces(DBObjectCollection pieces)
        {
            foreach (DBObject dbo in pieces)
                dbo.Dispose();
        }

        private static Point3d GetMidPoint(Curve curve)
        {
            try
            {
                double startDist = curve.GetDistanceAtParameter(curve.StartParam);
                double endDist = curve.GetDistanceAtParameter(curve.EndParam);
                return curve.GetPointAtDist((startDist + endDist) * 0.5);
            }
            catch
            {
                return new Point3d(
                    (curve.StartPoint.X + curve.EndPoint.X) * 0.5,
                    (curve.StartPoint.Y + curve.EndPoint.Y) * 0.5,
                    (curve.StartPoint.Z + curve.EndPoint.Z) * 0.5);
            }
        }

        private static double SafeTotalLength(Curve curve)
        {
            try
            {
                return curve.GetDistanceAtParameter(curve.EndParam) - curve.GetDistanceAtParameter(curve.StartParam);
            }
            catch
            {
                return curve.StartPoint.DistanceTo(curve.EndPoint);
            }
        }

        private static double SafeDistanceAtParam(Curve curve, double param)
        {
            try
            {
                return curve.GetDistanceAtParameter(param) - curve.GetDistanceAtParameter(curve.StartParam);
            }
            catch
            {
                try
                {
                    Point3d p = curve.GetPointAtParameter(param);
                    return curve.StartPoint.DistanceTo(p);
                }
                catch
                {
                    return double.NaN;
                }
            }
        }

        private static double Distance2d(Point3d a, Point3d b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool IsAtCurveEnd(Curve curve, double param)
        {
            return Math.Abs(param - curve.StartParam) <= DuplicateParamTolerance
                || Math.Abs(param - curve.EndParam) <= DuplicateParamTolerance;
        }

        private static bool TryCreateBoundaryInfo(Curve curve, out BoundaryInfo? info)
        {
            info = null;
            try
            {
                if (curve is Circle circle)
                {
                    info = BoundaryInfo.FromCircle(circle);
                    return true;
                }

                if (curve is AcPolyline polyline && polyline.Closed)
                {
                    info = BoundaryInfo.FromPolyline(polyline);
                    return info != null;
                }
            }
            catch
            {
                info = null;
            }

            return false;
        }

        private static Extents3d? TryGetExtents(Entity ent)
        {
            try
            {
                return ent.GeometricExtents;
            }
            catch
            {
                return null;
            }
        }

        private static bool ExtentsIntersect(Extents3d? a, Extents3d? b, double padding)
        {
            if (!a.HasValue || !b.HasValue)
                return true;

            Extents3d ea = a.Value;
            Extents3d eb = b.Value;
            return ea.MinPoint.X - padding <= eb.MaxPoint.X
                && ea.MaxPoint.X + padding >= eb.MinPoint.X
                && ea.MinPoint.Y - padding <= eb.MaxPoint.Y
                && ea.MaxPoint.Y + padding >= eb.MinPoint.Y;
        }

        private static string GetMatchingPipeLayer(string structureLayer)
        {
            if (string.Equals(structureLayer, StormStructureOuterLayer, StringComparison.OrdinalIgnoreCase))
                return StormPipeWallLayer;

            if (string.Equals(structureLayer, SewerStructureOuterLayer, StringComparison.OrdinalIgnoreCase))
                return SewerPipeWallLayer;

            return string.Empty;
        }

        private static bool IsStructureOuterLayer(string layer)
        {
            return string.Equals(layer, StormStructureOuterLayer, StringComparison.OrdinalIgnoreCase)
                || string.Equals(layer, SewerStructureOuterLayer, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPipeWallLayer(string layer)
        {
            return string.Equals(layer, StormPipeWallLayer, StringComparison.OrdinalIgnoreCase)
                || string.Equals(layer, SewerPipeWallLayer, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLayer(Entity ent, string layer)
        {
            return string.Equals(ent.Layer, layer, StringComparison.OrdinalIgnoreCase);
        }

        private sealed record BoundaryCandidate(ObjectId Id, Extents3d Extents, BoundaryInfo Info);
        private sealed record OpenEdgeCandidate(ObjectId Id, Extents3d Extents);
        private sealed record ParamHit(double Param, Point3d Point);
        private sealed record EndpointHit(Point3d Point, bool UseStartPoint, double Distance);

        private sealed class BoundaryInfo
        {
            private readonly Point2d[]? _polygonVertices;
            private readonly Point3d _circleCenter;
            private readonly double _circleRadius;
            private readonly bool _isCircle;

            private BoundaryInfo(Point3d center, Extents3d extents, bool isCircle, double circleRadius, Point2d[]? polygonVertices)
            {
                Center = center;
                Extents = extents;
                _isCircle = isCircle;
                _circleCenter = center;
                _circleRadius = circleRadius;
                _polygonVertices = polygonVertices;
            }

            public Point3d Center { get; }
            public Extents3d Extents { get; }

            public static BoundaryInfo FromCircle(Circle circle)
            {
                return new BoundaryInfo(circle.Center, circle.GeometricExtents, true, circle.Radius, null);
            }

            public static BoundaryInfo? FromPolyline(AcPolyline polyline)
            {
                if (!polyline.Closed || polyline.NumberOfVertices < 3)
                    return null;

                List<Point2d> points = new List<Point2d>();
                for (int i = 0; i < polyline.NumberOfVertices; i++)
                {
                    if (Math.Abs(polyline.GetBulgeAt(i)) > 1e-9)
                        return null;

                    points.Add(polyline.GetPoint2dAt(i));
                }

                double sumX = points.Sum(p => p.X);
                double sumY = points.Sum(p => p.Y);
                Point3d center = new Point3d(sumX / points.Count, sumY / points.Count, 0.0);
                return new BoundaryInfo(center, polyline.GeometricExtents, false, 0.0, points.ToArray());
            }

            public bool Contains(Point3d point)
            {
                if (_isCircle)
                    return Distance2d(_circleCenter, point) < _circleRadius - 1e-6;

                if (_polygonVertices == null || _polygonVertices.Length < 3)
                    return false;

                Point2d p = new Point2d(point.X, point.Y);
                bool inside = false;
                for (int i = 0, j = _polygonVertices.Length - 1; i < _polygonVertices.Length; j = i++)
                {
                    Point2d pi = _polygonVertices[i];
                    Point2d pj = _polygonVertices[j];
                    double denom = Math.Abs(pj.Y - pi.Y) < 1e-12 ? 1e-12 : pj.Y - pi.Y;
                    bool intersects = ((pi.Y > p.Y) != (pj.Y > p.Y))
                        && (p.X < (pj.X - pi.X) * (p.Y - pi.Y) / denom + pi.X);
                    if (intersects)
                        inside = !inside;
                }

                return inside;
            }

            private static double Distance2d(Point3d a, Point3d b)
            {
                double dx = a.X - b.X;
                double dy = a.Y - b.Y;
                return Math.Sqrt(dx * dx + dy * dy);
            }
        }

        private enum TrimResult
        {
            None,
            Trimmed,
            Skipped
        }
    }
}
