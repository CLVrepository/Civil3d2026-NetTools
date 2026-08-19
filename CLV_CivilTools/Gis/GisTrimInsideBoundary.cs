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
    public static class GisTrimInsideBoundaryCommands
    {
        [CommandMethod("CLV-GIS-TRIM-INSIDE", CommandFlags.Modal)]
        public static void TrimInsideBoundary()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                using var docLock = doc.LockDocument();

                var peo = new PromptEntityOptions("\nSelect closed structure boundary (circle or closed polyline): ");
                peo.SetRejectMessage("\nSelect a circle or closed polyline.");
                peo.AddAllowedClass(typeof(Circle), exactMatch: false);
                peo.AddAllowedClass(typeof(AcPolyline), exactMatch: false);
                var per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                    return;

                using var tr = db.TransactionManager.StartTransaction();
                if (tr.GetObject(per.ObjectId, OpenMode.ForRead) is not Curve selectedBoundary)
                {
                    ed.WriteMessage("\nSelected object is not a valid boundary curve.");
                    return;
                }

                if (!TryCreateBoundaryInfo(selectedBoundary, out BoundaryInfo? selectedInfo) || selectedInfo == null)
                {
                    ed.WriteMessage("\nBoundary must be a circle or closed straight-segment polyline.");
                    return;
                }

                var boundaryIds = new List<ObjectId> { per.ObjectId };

                int trimmedCount = 0;
                int skippedCount = 0;

                foreach (ObjectId boundaryId in boundaryIds)
                {
                    if (!boundaryId.IsValid || boundaryId.IsErased)
                        continue;

                    if (tr.GetObject(boundaryId, OpenMode.ForRead, false) is not Curve boundaryCurve)
                        continue;

                    if (!TryCreateBoundaryInfo(boundaryCurve, out BoundaryInfo? boundaryInfo) || boundaryInfo == null)
                    {
                        skippedCount++;
                        continue;
                    }

                    var candidateIds = CollectCandidateCurveIds(tr, db, boundaryId, boundaryInfo);
                    foreach (ObjectId candidateId in candidateIds)
                    {
                        if (!candidateId.IsValid || candidateId.IsErased)
                            continue;

                        if (tr.GetObject(candidateId, OpenMode.ForWrite, false) is not Curve candidate)
                            continue;

                        TrimResult result = TrimCurveAgainstBoundary(tr, db, candidate, boundaryCurve, boundaryInfo);
                        if (result == TrimResult.Trimmed)
                            trimmedCount++;
                        else if (result == TrimResult.Skipped)
                            skippedCount++;
                    }
                }

                tr.Commit();
                ed.WriteMessage($"\nCLV-GIS-TRIM-INSIDE complete. Trimmed: {trimmedCount}. Skipped: {skippedCount}. Boundaries processed: {boundaryIds.Count}. Mode: Single boundary. Candidate layers: C-STRM-PIPE-E, C-SSWR-PIPE-E.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-TRIM-INSIDE failed: {ex.Message}");
            }
        }

        private static List<ObjectId> CollectSimilarBoundaryIds(Transaction tr, Database db, ObjectId seedId, Curve seedBoundary, BoundaryInfo seedInfo)
        {
            _ = seedInfo;
            var allMatches = new List<(ObjectId Id, Curve Curve)>();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var model = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            string layer = seedBoundary.Layer;

            foreach (ObjectId id in model)
            {
                if (id.IsErased || !id.IsValid)
                    continue;

                if (tr.GetObject(id, OpenMode.ForRead, false) is not Curve curve)
                    continue;

                if (!string.Equals(curve.Layer, layer, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (IsSupportedBoundaryCurve(curve))
                    allMatches.Add((id, curve));
            }

            var result = FilterNestedCompanionBoundaries(seedBoundary, allMatches);

            if (!result.Contains(seedId))
                result.Add(seedId);

            return result;
        }

        private static bool IsPipeWallLayer(string layerName)
        {
            return string.Equals(layerName, "C-STRM-PIPE-E", StringComparison.OrdinalIgnoreCase)
                || string.Equals(layerName, "C-SSWR-PIPE-E", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSupportedBoundaryCurve(Curve curve)
        {
            return curve is Circle
                || (curve is AcPolyline polyline && polyline.Closed);
        }

        private static List<ObjectId> FilterNestedCompanionBoundaries(Curve seedBoundary, List<(ObjectId Id, Curve Curve)> allMatches)
        {
            if (seedBoundary is not Circle seedCircle)
                return allMatches.Select(x => x.Id).ToList();

            const double centerTolerance = 0.10;
            const double radiusTolerance = 0.01;
            var circleMatches = allMatches
                .Where(x => x.Curve is Circle)
                .ToList();
            var nonCircleMatches = allMatches
                .Where(x => x.Curve is not Circle)
                .Select(x => x.Id)
                .ToList();

            bool seedHasLargerCompanion = circleMatches.Any(x =>
                x.Curve is Circle circle
                && !ReferenceEquals(circle, seedCircle)
                && circle.Center.DistanceTo(seedCircle.Center) <= centerTolerance
                && circle.Radius > seedCircle.Radius + radiusTolerance);
            bool seedHasSmallerCompanion = circleMatches.Any(x =>
                x.Curve is Circle circle
                && !ReferenceEquals(circle, seedCircle)
                && circle.Center.DistanceTo(seedCircle.Center) <= centerTolerance
                && circle.Radius < seedCircle.Radius - radiusTolerance);

            if (!seedHasLargerCompanion && !seedHasSmallerCompanion)
                return allMatches.Select(x => x.Id).ToList();

            bool keepInnerCompanions = seedHasLargerCompanion || !seedHasSmallerCompanion;
            var filtered = new List<ObjectId>();

            foreach (var match in circleMatches)
            {
                if (match.Curve is not Circle circle)
                    continue;

                bool hasLargerCompanion = circleMatches.Any(x =>
                    x.Curve is Circle other
                    && !ReferenceEquals(other, circle)
                    && other.Center.DistanceTo(circle.Center) <= centerTolerance
                    && other.Radius > circle.Radius + radiusTolerance);
                bool hasSmallerCompanion = circleMatches.Any(x =>
                    x.Curve is Circle other
                    && !ReferenceEquals(other, circle)
                    && other.Center.DistanceTo(circle.Center) <= centerTolerance
                    && other.Radius < circle.Radius - radiusTolerance);

                if (keepInnerCompanions)
                {
                    if (!hasSmallerCompanion)
                        filtered.Add(match.Id);
                }
                else if (!hasLargerCompanion)
                {
                    filtered.Add(match.Id);
                }
            }

            filtered.AddRange(nonCircleMatches);
            return filtered;
        }

        private static List<ObjectId> CollectCandidateCurveIds(Transaction tr, Database db, ObjectId boundaryId, BoundaryInfo boundaryInfo)
        {
            var result = new List<ObjectId>();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var model = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            Extents3d ext = boundaryInfo.Extents;

            foreach (ObjectId id in model)
            {
                if (id == boundaryId || id.IsErased || !id.IsValid)
                    continue;

                if (tr.GetObject(id, OpenMode.ForRead, false) is not Curve curve)
                    continue;

                if (!IsPipeWallLayer(curve.Layer))
                    continue;

                if (curve is Circle)
                    continue;

                if (curve is AcPolyline candidatePoly && candidatePoly.Closed)
                    continue;

                try
                {
                    Extents3d candidateExt = curve.GeometricExtents;
                    if (!Intersects(ext, candidateExt))
                        continue;
                }
                catch
                {
                    continue;
                }

                result.Add(id);
            }

            return result;
        }

        private static TrimResult TrimCurveAgainstBoundary(Transaction tr, Database db, Curve candidate, Curve boundaryCurve, BoundaryInfo boundaryInfo)
        {
            var intersections = new Point3dCollection();
            try
            {
                candidate.IntersectWith(boundaryCurve, Intersect.OnBothOperands, intersections, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
                return TrimResult.Skipped;
            }

            if (intersections.Count == 0)
                return TrimResult.None;

            Point3d start = candidate.StartPoint;
            Point3d end = candidate.EndPoint;
            bool startInside = boundaryInfo.Contains(start);
            bool endInside = boundaryInfo.Contains(end);

            if (!startInside && !endInside)
            {
                if (intersections.Count == 1)
                {
                    double startDist = start.DistanceTo(boundaryInfo.Center);
                    double endDist = end.DistanceTo(boundaryInfo.Center);
                    startInside = startDist < endDist;
                    endInside = !startInside;
                }
                else
                {
                    return TrimResult.Skipped;
                }
            }

            List<(double Param, Point3d Point)> paramHits = new();
            foreach (Point3d pt in intersections.Cast<Point3d>())
            {
                try
                {
                    double param = candidate.GetParameterAtPoint(candidate.GetClosestPointTo(pt, false));
                    paramHits.Add((param, pt));
                }
                catch
                {
                    // skip bad hit
                }
            }

            if (paramHits.Count == 0)
                return TrimResult.Skipped;

            paramHits = paramHits
                .OrderBy(x => x.Param)
                .GroupBy(x => Math.Round(x.Param, 8))
                .Select(g => g.First())
                .ToList();

            if (startInside)
            {
                double splitParam = paramHits.First().Param;
                return KeepOneSideAfterSplit(tr, db, candidate, splitParam, keepStartSide: false);
            }

            if (endInside)
            {
                double splitParam = paramHits.Last().Param;
                return KeepOneSideAfterSplit(tr, db, candidate, splitParam, keepStartSide: true);
            }

            return TrimResult.Skipped;
        }

        private static TrimResult KeepOneSideAfterSplit(Transaction tr, Database db, Curve candidate, double splitParam, bool keepStartSide)
        {
            double startParam = candidate.StartParam;
            double endParam = candidate.EndParam;
            const double tol = 1e-8;
            if (Math.Abs(splitParam - startParam) < tol || Math.Abs(splitParam - endParam) < tol)
                return TrimResult.None;

            Point3d originalStart = candidate.StartPoint;
            Point3d originalEnd = candidate.EndPoint;

            if (TryTrimInPlace(candidate, splitParam, keepStartSide))
                return TrimResult.Trimmed;

            DBObjectCollection pieces;
            try
            {
                var splitParams = new DoubleCollection { splitParam };
                pieces = candidate.GetSplitCurves(splitParams);
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

            Curve? kept = null;
            double best = double.MaxValue;

            foreach (DBObject dbo in pieces)
            {
                if (dbo is not Curve piece)
                    continue;

                double metric = keepStartSide
                    ? Math.Min(piece.StartPoint.DistanceTo(originalStart), piece.EndPoint.DistanceTo(originalStart))
                    : Math.Min(piece.StartPoint.DistanceTo(originalEnd), piece.EndPoint.DistanceTo(originalEnd));

                if (metric < best)
                {
                    kept = piece;
                    best = metric;
                }
            }

            if (kept == null)
            {
                DisposePieces(pieces);
                return TrimResult.Skipped;
            }

            var owner = (BlockTableRecord)tr.GetObject(candidate.OwnerId, OpenMode.ForWrite);
            foreach (DBObject dbo in pieces)
            {
                if (ReferenceEquals(dbo, kept))
                    continue;

                dbo.Dispose();
            }

            owner.AppendEntity(kept);
            tr.AddNewlyCreatedDBObject(kept, true);
            candidate.Erase();
            return TrimResult.Trimmed;
        }


        private static bool TryTrimInPlace(Curve candidate, double splitParam, bool keepStartSide)
        {
            try
            {
                Point3d splitPoint = candidate.GetPointAtParameter(splitParam);

                if (candidate is Line line)
                {
                    if (keepStartSide)
                        line.EndPoint = splitPoint;
                    else
                        line.StartPoint = splitPoint;

                    return true;
                }

                if (candidate is AcPolyline polyline)
                    return TryTrimPolylineInPlace(polyline, splitParam, keepStartSide);
            }
            catch
            {
                // Fall back to split/recreate for uncommon curve types.
            }

            return false;
        }

        private static bool TryTrimPolylineInPlace(AcPolyline original, double splitParam, bool keepStartSide)
        {
            DBObjectCollection pieces;
            try
            {
                var splitParams = new DoubleCollection { splitParam };
                pieces = original.GetSplitCurves(splitParams);
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

                    double metric = Math.Min(piece.StartPoint.DistanceTo(target), piece.EndPoint.DistanceTo(target));
                    if (metric < best)
                    {
                        kept = piece;
                        best = metric;
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
                {
                    original.AddVertexAt(i, kept.GetPoint2dAt(i), kept.GetBulgeAt(i), kept.GetStartWidthAt(i), kept.GetEndWidthAt(i));
                }

                return true;
            }
            finally
            {
                DisposePieces(pieces);
            }
        }

        private static void DisposePieces(DBObjectCollection pieces)
        {
            foreach (DBObject dbo in pieces)
                dbo.Dispose();
        }

        private static bool TryCreateBoundaryInfo(Curve boundaryCurve, out BoundaryInfo? info)
        {
            info = null;
            try
            {
                if (boundaryCurve is Circle circle)
                {
                    info = BoundaryInfo.FromCircle(circle);
                    return true;
                }

                if (boundaryCurve is AcPolyline polyline && polyline.Closed)
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

        private static bool Intersects(Extents3d a, Extents3d b)
        {
            return a.MinPoint.X <= b.MaxPoint.X
                && a.MaxPoint.X >= b.MinPoint.X
                && a.MinPoint.Y <= b.MaxPoint.Y
                && a.MaxPoint.Y >= b.MinPoint.Y;
        }

        private sealed class BoundaryInfo
        {
            private readonly Point2d[]? _polygonVertices;
            private readonly Circle? _circle;

            private BoundaryInfo(Point3d center, Extents3d extents, Circle? circle, Point2d[]? polygonVertices)
            {
                Center = center;
                Extents = extents;
                _circle = circle;
                _polygonVertices = polygonVertices;
            }

            public Point3d Center { get; }
            public Extents3d Extents { get; }

            public static BoundaryInfo FromCircle(Circle circle)
            {
                return new BoundaryInfo(circle.Center, circle.GeometricExtents, circle, null);
            }

            public static BoundaryInfo? FromPolyline(AcPolyline polyline)
            {
                if (!polyline.Closed || polyline.NumberOfVertices < 3)
                    return null;

                var points = new List<Point2d>();
                for (int i = 0; i < polyline.NumberOfVertices; i++)
                {
                    if (Math.Abs(polyline.GetBulgeAt(i)) > 1e-9)
                        return null;

                    points.Add(polyline.GetPoint2dAt(i));
                }

                double sumX = points.Sum(p => p.X);
                double sumY = points.Sum(p => p.Y);
                var center = new Point3d(sumX / points.Count, sumY / points.Count, 0.0);
                return new BoundaryInfo(center, polyline.GeometricExtents, null, points.ToArray());
            }

            public bool Contains(Point3d point)
            {
                if (_circle != null)
                    return _circle.Center.DistanceTo(point) < (_circle.Radius - 1e-6);

                if (_polygonVertices == null || _polygonVertices.Length < 3)
                    return false;

                var p = new Point2d(point.X, point.Y);
                bool inside = false;
                for (int i = 0, j = _polygonVertices.Length - 1; i < _polygonVertices.Length; j = i++)
                {
                    Point2d pi = _polygonVertices[i];
                    Point2d pj = _polygonVertices[j];
                    bool intersect = ((pi.Y > p.Y) != (pj.Y > p.Y))
                                     && (p.X < (pj.X - pi.X) * (p.Y - pi.Y) / ((pj.Y - pi.Y) == 0.0 ? 1e-12 : (pj.Y - pi.Y)) + pi.X);
                    if (intersect)
                        inside = !inside;
                }

                return inside;
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
