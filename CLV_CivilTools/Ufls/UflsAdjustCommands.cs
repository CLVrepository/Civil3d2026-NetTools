using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcRegion = Autodesk.AutoCAD.DatabaseServices.Region;

namespace CLV_CivilTools.Ufls
{
    public static class UflsAdjustCommands
    {
        private const string LayerCheck = "V-SURV-CHCK";
        private const string MhMarkerBlock = "UFLS_MH_MARK";
        private const string StubMarkerBlock = "UFLS_STUB_MARK";
        private const string PipeCenterlineLayer = "V-SURV-PIPE-CNTR";
        private const string StormDrainInnerLayer = "V-SURV-STRC-INNR-2D~~";
        private const string DiMarkerBlock = "UFLS_DI_MARK";
        private const string SurveyBlockFolder =
            @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\Blocks\Survey";
        private const double DefaultSearchRadius = 8.0;
        private const double SinglePipeSearchRadius = 12.0;

        [CommandMethod("UFLS", "UFLS-STUB", CommandFlags.Modal)]
        public static void PlaceStubMarker()
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            PromptPointOptions ppo = new PromptPointOptions("\nPick stub locator point: ");
            PromptPointResult ppr = ed.GetPoint(ppo);
            if (ppr.Status != PromptStatus.OK)
                return;

            Point3d insPt = new Point3d(ppr.Value.X, ppr.Value.Y, 0.0);

            try
            {
                using Transaction tr = db.TransactionManager.StartTransaction();
                EnsureLayer(db, tr, LayerCheck, 1);
                EnsureStubMarkerBlock(db, tr);
                InsertMarkerBlock(db, tr, StubMarkerBlock, insPt, LayerCheck);
                tr.Commit();
                ed.WriteMessage("\nUFLS-STUB: Stub marker placed.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUFLS-STUB error: {ex.Message}");
            }
        }

        [CommandMethod("UFLS", "UFLS-ADJ-MH-AUTO", CommandFlags.Modal)]
        [CommandMethod("UFLS", "UFLS-ADJ-MH-ALL", CommandFlags.Modal)]
        public static void AdjustManholeAuto()
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                int moved = 0;
                using Transaction tr = db.TransactionManager.StartTransaction();
                List<BlockReference> markers = GetStructureMarkerBlocks(tr, db);
                foreach (BlockReference marker in markers)
                {
                    ObjectId? structureId = FindNearestStructureId(tr, db, marker.Position, DefaultSearchRadius);
                    if (!structureId.HasValue)
                        continue;

                    MoveStructureAndConnectedPipes(tr, db, structureId.Value, marker.Position);
                    moved++;
                }

                tr.Commit();
                ed.WriteMessage($"\nUFLS-ADJ-MH-AUTO: moved {moved} structure(s).");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUFLS-ADJ-MH-AUTO error: {ex.Message}");
            }
        }

        [CommandMethod("UFLS", "UFLS-ADJ-MH-SINGLE", CommandFlags.Modal)]
        public static void AdjustManholeSingle()
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                PromptEntityOptions peo = new PromptEntityOptions("\nSelect manhole structure to move: ");
                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                    return;

                using Transaction tr = db.TransactionManager.StartTransaction();
                DBObject dbo = tr.GetObject(per.ObjectId, OpenMode.ForWrite, false);
                if (!IsCivilStructure(dbo))
                    throw new InvalidOperationException("Selected object is not a Civil 3D structure.");

                Point3d stLocation = GetStructurePoint(dbo);
                BlockReference? marker = FindNearestStructureMarkerBlock(tr, db, stLocation, DefaultSearchRadius);
                if (marker == null)
                    throw new InvalidOperationException("No nearby UFLS_MH_MARK or UFLS_STUB_MARK found for the selected structure.");

                MoveStructureAndConnectedPipes(tr, db, per.ObjectId, marker.Position);
                tr.Commit();
                ed.WriteMessage("\nUFLS-ADJ-MH-SINGLE: structure moved.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUFLS-ADJ-MH-SINGLE error: {ex.Message}");
            }
        }



        [CommandMethod("UFLS", "UFLS-ADJ-SD-ALL", CommandFlags.Modal)]
        [CommandMethod("UFLS", "UFLS-ADJ-JNCT-ALL", CommandFlags.Modal)]
        public static void AdjustStormDrainAll()
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                int moved = 0;
                using Transaction tr = db.TransactionManager.StartTransaction();
                List<FootprintTarget> targets = GetStormDrainFootprintTargets(tr, db);
                if (targets.Count == 0)
                    throw new InvalidOperationException($"No closed polylines were found on layer {StormDrainInnerLayer}.");

                foreach (FootprintTarget target in targets)
                {
                    ObjectId? structureId = FindNearestStructureId(tr, db, target.Center, DefaultSearchRadius * 2.0, IsStormDrainStructure);
                    if (!structureId.HasValue)
                        continue;

                    MoveStructureAndConnectedPipes(tr, db, structureId.Value, target.Center);
                    SetStructureRotation(tr, structureId.Value, target.Rotation);
                    moved++;
                }

                tr.Commit();
                ed.WriteMessage($"\nUFLS-ADJ-JNCT-ALL: moved {moved} structure(s).");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUFLS-ADJ-JNCT-ALL error: {ex.Message}");
            }
        }

        [CommandMethod("UFLS", "UFLS-ADJ-SD-SINGLE", CommandFlags.Modal)]
        [CommandMethod("UFLS", "UFLS-ADJ-JNCT-SINGLE", CommandFlags.Modal)]
        public static void AdjustStormDrainSingle()
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                PromptEntityOptions peo = new PromptEntityOptions("\nSelect storm drain structure to move: ");
                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                    return;

                using Transaction tr = db.TransactionManager.StartTransaction();
                DBObject dbo = tr.GetObject(per.ObjectId, OpenMode.ForWrite, false);
                if (!IsCivilStructure(dbo))
                    throw new InvalidOperationException("Selected object is not a Civil 3D structure.");

                Point3d location = GetStructurePoint(dbo);
                ObjectId preferredPolylineId = PromptForStormDrainPolyline(ed, tr);
                if (!TryFindNearestStormDrainTarget(tr, db, location, DefaultSearchRadius * 2.0, preferredPolylineId, out FootprintTarget target))
                    throw new InvalidOperationException($"No nearby closed {StormDrainInnerLayer} polyline found for the selected structure.");

                MoveStructureAndConnectedPipes(tr, db, per.ObjectId, target.Center);
                SetStructureRotation(dbo, target.Rotation);
                tr.Commit();
                ed.WriteMessage("\nUFLS-ADJ-JNCT-SINGLE: structure moved.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUFLS-ADJ-JNCT-SINGLE error: {ex.Message}");
            }
        }

        [CommandMethod("UFLS", "UFLS-ADJ-DI-ALL", CommandFlags.Modal)]
        public static void AdjustDropInletAll()
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                int moved = 0;
                using Transaction tr = db.TransactionManager.StartTransaction();
                List<BlockReference> markers = GetMarkerBlocks(tr, db, DiMarkerBlock);
                foreach (BlockReference marker in markers)
                {
                    ObjectId? structureId = FindNearestStructureId(tr, db, marker.Position, DefaultSearchRadius * 2.0, IsDropInletStructure);
                    if (!structureId.HasValue)
                        continue;

                    MoveStructureAndConnectedPipes(tr, db, structureId.Value, marker.Position);
                    SetStructureRotation(tr, structureId.Value, marker.Rotation);
                    moved++;
                }

                tr.Commit();
                ed.WriteMessage($"\nUFLS-ADJ-DI-ALL: moved {moved} structure(s).");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUFLS-ADJ-DI-ALL error: {ex.Message}");
            }
        }

        [CommandMethod("UFLS", "UFLS-ADJ-DI-SINGLE", CommandFlags.Modal)]
        public static void AdjustDropInletSingle()
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                PromptEntityOptions peo = new PromptEntityOptions("\nSelect drop inlet structure to move: ");
                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                    return;

                using Transaction tr = db.TransactionManager.StartTransaction();
                DBObject dbo = tr.GetObject(per.ObjectId, OpenMode.ForWrite, false);
                if (!IsCivilStructure(dbo))
                    throw new InvalidOperationException("Selected object is not a Civil 3D structure.");

                Point3d location = GetStructurePoint(dbo);
                BlockReference? marker = FindNearestMarkerBlock(tr, db, DiMarkerBlock, location, DefaultSearchRadius * 2.0);
                if (marker == null)
                    throw new InvalidOperationException("No nearby UFLS_DI_MARK found for the selected structure.");

                MoveStructureAndConnectedPipes(tr, db, per.ObjectId, marker.Position);
                SetStructureRotation(dbo, marker.Rotation);
                tr.Commit();
                ed.WriteMessage("\nUFLS-ADJ-DI-SINGLE: structure moved.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUFLS-ADJ-DI-SINGLE error: {ex.Message}");
            }
        }

        [CommandMethod("UFLS", "UFLS-ADJ-PIPE-AUTO", CommandFlags.Modal)]
        [CommandMethod("UFLS", "UFLS-ADJ-PIPE-ALL", CommandFlags.Modal)]
        public static void AdjustPipeAuto()
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                int moved = 0;

                using Transaction tr = db.TransactionManager.StartTransaction();
                List<LineEndpointPair> centerlines = GetPipeCenterlineEndpoints(tr, db, PipeCenterlineLayer);
                if (centerlines.Count == 0)
                    throw new InvalidOperationException($"No LINE or 3D POLYLINE entities found on layer {PipeCenterlineLayer}.");

                foreach (ObjectId id in EnumerateModelSpaceEntityIds(tr, db))
                {
                    DBObject dbo = tr.GetObject(id, OpenMode.ForWrite, false);
                    if (!IsCivilPipe(dbo))
                        continue;

                    if (TryFindBestCenterlineForPipe(dbo, centerlines, SinglePipeSearchRadius, out LineEndpointPair best))
                    {
                        SetPipeToCenterline(dbo, best);
                        moved++;
                    }
                }

                tr.Commit();
                ed.WriteMessage($"\nUFLS-ADJ-PIPE-AUTO: moved {moved} pipe(s).");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUFLS-ADJ-PIPE-AUTO error: {ex.Message}");
            }
        }

        [CommandMethod("UFLS", "UFLS-ADJ-PIPE-SINGLE", CommandFlags.Modal)]
        public static void AdjustPipeSingle()
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                PromptEntityOptions peo = new PromptEntityOptions("\nSelect pipe to move: ");
                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                    return;

                using Transaction tr = db.TransactionManager.StartTransaction();
                DBObject pipeObj = tr.GetObject(per.ObjectId, OpenMode.ForWrite, false);
                if (!IsCivilPipe(pipeObj))
                    throw new InvalidOperationException("Selected object is not a Civil 3D pipe.");

                List<LineEndpointPair> centerlines = GetPipeCenterlineEndpoints(tr, db, PipeCenterlineLayer);
                if (centerlines.Count == 0)
                    throw new InvalidOperationException($"No LINE or 3D POLYLINE entities found on layer {PipeCenterlineLayer}.");

                if (!TryFindBestCenterlineForPipe(pipeObj, centerlines, SinglePipeSearchRadius, out LineEndpointPair best))
                    throw new InvalidOperationException($"No nearby {PipeCenterlineLayer} LINE or 3D POLYLINE found for the selected pipe.");

                SetPipeToCenterline(pipeObj, best);
                tr.Commit();
                ed.WriteMessage("\nUFLS-ADJ-PIPE-SINGLE: pipe moved to surveyed centerline.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUFLS-ADJ-PIPE-SINGLE error: {ex.Message}");
            }
        }

        private readonly struct FootprintTarget
        {
            public FootprintTarget(ObjectId polylineId, Point3d center, double rotation)
            {
                PolylineId = polylineId;
                Center = center;
                Rotation = rotation;
            }

            public ObjectId PolylineId { get; }
            public Point3d Center { get; }
            public double Rotation { get; }
        }

        private readonly struct LineEndpointPair
        {
            public LineEndpointPair(Point3d start, Point3d end)
            {
                Start = start;
                End = end;
            }

            public Point3d Start { get; }
            public Point3d End { get; }
        }

        private static List<LineEndpointPair> GetPipeCenterlineEndpoints(Transaction tr, Database db, string layerName)
        {
            List<LineEndpointPair> result = new List<LineEndpointPair>();
            foreach (ObjectId id in EnumerateModelSpaceEntityIds(tr, db))
            {
                DBObject dbo = tr.GetObject(id, OpenMode.ForRead, false);

                if (dbo is Line ln)
                {
                    if (string.Equals(ln.Layer, layerName, StringComparison.OrdinalIgnoreCase))
                        result.Add(new LineEndpointPair(ln.StartPoint, ln.EndPoint));

                    continue;
                }

                if (dbo is Polyline3d pl3d)
                {
                    if (!string.Equals(pl3d.Layer, layerName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (TryGetPolyline3dEndpoints(tr, pl3d, out Point3d start, out Point3d end))
                        result.Add(new LineEndpointPair(start, end));
                }
            }
            return result;
        }

        private static bool TryGetPolyline3dEndpoints(Transaction tr, Polyline3d pl3d, out Point3d start, out Point3d end)
        {
            start = Point3d.Origin;
            end = Point3d.Origin;

            bool found = false;
            foreach (ObjectId vId in pl3d)
            {
                if (tr.GetObject(vId, OpenMode.ForRead, false) is not PolylineVertex3d vtx)
                    continue;

                if (!found)
                {
                    start = vtx.Position;
                    end = vtx.Position;
                    found = true;
                }
                else
                {
                    end = vtx.Position;
                }
            }

            return found && !start.IsEqualTo(end, new Tolerance(1e-6, 1e-6));
        }


        private static ObjectId PromptForStormDrainPolyline(Editor ed, Transaction tr)
        {
            PromptKeywordOptions pko = new PromptKeywordOptions("\nStorm-drain target source [Nearest/Select] <Nearest>: ");
            pko.AllowNone = true;
            pko.Keywords.Add("Nearest");
            pko.Keywords.Add("Select");
            pko.Keywords.Default = "Nearest";

            PromptResult pkr = ed.GetKeywords(pko);
            if (pkr.Status == PromptStatus.Cancel)
                return ObjectId.Null;

            string choice = pkr.Status == PromptStatus.OK ? pkr.StringResult : "Nearest";
            if (!string.Equals(choice, "Select", StringComparison.OrdinalIgnoreCase))
                return ObjectId.Null;

            PromptEntityOptions peo = new PromptEntityOptions($"\nSelect closed {StormDrainInnerLayer} polyline: ");
            peo.SetRejectMessage($"\nSelect a closed polyline on layer {StormDrainInnerLayer}.");
            peo.AddAllowedClass(typeof(Polyline), false);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
                return ObjectId.Null;

            if (tr.GetObject(per.ObjectId, OpenMode.ForRead, false) is not Polyline pl)
                return ObjectId.Null;

            if (!pl.Closed || !string.Equals(pl.Layer, StormDrainInnerLayer, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Selected polyline must be closed and on layer {StormDrainInnerLayer}.");

            return per.ObjectId;
        }

        private static List<FootprintTarget> GetStormDrainFootprintTargets(Transaction tr, Database db)
        {
            List<FootprintTarget> result = new List<FootprintTarget>();
            foreach (ObjectId id in EnumerateModelSpaceEntityIds(tr, db))
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is not Polyline pl)
                    continue;

                if (!pl.Closed || !string.Equals(pl.Layer, StormDrainInnerLayer, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!UflsSdJunctionSizeTestCommands.TryGetBestFitFootprintSize(pl, out _, out _, out double rotation))
                    continue;

                Point3d center = GetClosedPolylineCenter(pl);
                result.Add(new FootprintTarget(id, center, rotation));
            }

            return result;
        }

        private static bool TryFindNearestStormDrainTarget(Transaction tr, Database db, Point3d target, double maxDist, out FootprintTarget best)
        {
            return TryFindNearestStormDrainTarget(tr, db, target, maxDist, ObjectId.Null, out best);
        }

        private static bool TryFindNearestStormDrainTarget(Transaction tr, Database db, Point3d target, double maxDist, ObjectId preferredPolylineId, out FootprintTarget best)
        {
            best = default;
            bool found = false;
            double bestScore = double.MaxValue;

            foreach (FootprintTarget candidate in GetStormDrainFootprintTargets(tr, db))
            {
                double centerD2 = DistanceSquared2d(candidate.Center, target);
                double boundaryD2 = centerD2;

                if (!candidate.PolylineId.IsNull &&
                    tr.GetObject(candidate.PolylineId, OpenMode.ForRead, false) is Polyline pl)
                {
                    Point3d closest = pl.GetClosestPointTo(target, false);
                    boundaryD2 = DistanceSquared2d(closest, target);

                    if (PointInsideClosedPolyline(pl, target))
                    {
                        if (preferredPolylineId == candidate.PolylineId)
                        {
                            best = candidate;
                            return true;
                        }

                        double insideScore = Math.Min(centerD2, boundaryD2);
                        if (!found || insideScore < bestScore)
                        {
                            best = candidate;
                            bestScore = insideScore;
                            found = true;
                        }

                        continue;
                    }
                }

                double score = Math.Min(centerD2, boundaryD2);
                if (preferredPolylineId == candidate.PolylineId)
                    score *= 0.25;

                if (score > maxDist * maxDist)
                    continue;

                if (!found || score < bestScore)
                {
                    best = candidate;
                    bestScore = score;
                    found = true;
                }
            }

            return found;
        }

        private static Point3d GetClosedPolylineCenter(Polyline pl)
        {
            if (TryGetRegionCentroid(pl, out Point3d centroid))
                return centroid;

            return GetPolylineCentroidFallback(pl);
        }

        private static bool TryGetRegionCentroid(Polyline pl, out Point3d centroid)
        {
            centroid = Point3d.Origin;

            try
            {
                DBObjectCollection curves = new DBObjectCollection();
                using Polyline clone = (Polyline)pl.Clone();
                curves.Add(clone);

                DBObjectCollection regions = AcRegion.CreateFromCurves(curves);
                try
                {
                    if (regions.Count == 0 || regions[0] is not AcRegion region)
                        return false;

                    using (region)
                    {
                        Point3d origin = Point3d.Origin;
                        Vector3d xAxis = Vector3d.XAxis;
                        Vector3d yAxis = Vector3d.YAxis;
                        RegionAreaProperties props = region.AreaProperties(ref origin, ref xAxis, ref yAxis);
                        centroid = new Point3d(props.Centroid.X, props.Centroid.Y, 0.0);
                        return true;
                    }
                }
                finally
                {
                    for (int i = 1; i < regions.Count; i++)
                    {
                        if (regions[i] is IDisposable disposable)
                            disposable.Dispose();
                    }
                }
            }
            catch
            {
                return false;
            }
        }

        private static Point3d GetPolylineCentroidFallback(Polyline pl)
        {
            double area2 = 0.0;
            double cx = 0.0;
            double cy = 0.0;

            int n = pl.NumberOfVertices;
            for (int i = 0; i < n; i++)
            {
                Point2d p0 = pl.GetPoint2dAt(i);
                Point2d p1 = pl.GetPoint2dAt((i + 1) % n);
                double cross = (p0.X * p1.Y) - (p1.X * p0.Y);
                area2 += cross;
                cx += (p0.X + p1.X) * cross;
                cy += (p0.Y + p1.Y) * cross;
            }

            if (Math.Abs(area2) > 1e-8)
            {
                double factor = 1.0 / (3.0 * area2);
                return new Point3d(cx * factor, cy * factor, 0.0);
            }

            Extents3d ext = pl.GeometricExtents;
            return new Point3d((ext.MinPoint.X + ext.MaxPoint.X) * 0.5, (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5, 0.0);
        }

        private static bool PointInsideClosedPolyline(Polyline pl, Point3d point)
        {
            try
            {
                Point3d closest = pl.GetClosestPointTo(point, false);
                if (DistanceSquared2d(closest, point) < 1e-8)
                    return true;
            }
            catch
            {
            }

            bool inside = false;
            int count = pl.NumberOfVertices;
            double x = point.X;
            double y = point.Y;

            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                Point2d pi = pl.GetPoint2dAt(i);
                Point2d pj = pl.GetPoint2dAt(j);

                bool intersects = ((pi.Y > y) != (pj.Y > y)) &&
                                  (x < (pj.X - pi.X) * (y - pi.Y) / ((pj.Y - pi.Y) == 0.0 ? 1e-12 : (pj.Y - pi.Y)) + pi.X);
                if (intersects)
                    inside = !inside;
            }

            return inside;
        }

        private static bool TryFindBestCenterlineForPipe(
            DBObject pipeObj,
            List<LineEndpointPair> centerlines,
            double maxDist,
            out LineEndpointPair best)
        {
            Point3d p0 = GetPipeStartPoint(pipeObj);
            Point3d p1 = GetPipeEndPoint(pipeObj);

            double bestScore = double.MaxValue;
            LineEndpointPair? bestPair = null;
            double maxDist2 = maxDist * maxDist;

            foreach (LineEndpointPair cl in centerlines)
            {
                double scoreA = DistanceSquared3d(p0, cl.Start) + DistanceSquared3d(p1, cl.End);
                double scoreB = DistanceSquared3d(p0, cl.End) + DistanceSquared3d(p1, cl.Start);

                if (scoreA <= scoreB)
                {
                    if (scoreA < bestScore)
                    {
                        bestScore = scoreA;
                        bestPair = cl;
                    }
                }
                else
                {
                    if (scoreB < bestScore)
                    {
                        bestScore = scoreB;
                        bestPair = new LineEndpointPair(cl.End, cl.Start);
                    }
                }
            }

            if (bestPair.HasValue &&
                DistanceSquared3d(p0, bestPair.Value.Start) <= maxDist2 &&
                DistanceSquared3d(p1, bestPair.Value.End) <= maxDist2)
            {
                best = bestPair.Value;
                return true;
            }

            best = default;
            return false;
        }

        private static void SetPipeToCenterline(DBObject pipeObj, LineEndpointPair endpoints)
        {
            if (TryInvokeSetStartAndEndPoints(pipeObj, endpoints.Start, endpoints.End))
                return;

            SetPipeStartPoint(pipeObj, endpoints.Start);
            SetPipeEndPoint(pipeObj, endpoints.End);
        }

        private static bool TryInvokeSetStartAndEndPoints(object pipeObj, Point3d start, Point3d end)
        {
            MethodInfo? mi = pipeObj.GetType().GetMethod(
                "SetStartAndEndPoints",
                BindingFlags.Public | BindingFlags.Instance,
                binder: null,
                types: new[] { typeof(Point3d), typeof(Point3d) },
                modifiers: null);

            if (mi == null)
                return false;

            mi.Invoke(pipeObj, new object[] { start, end });
            return true;
        }

        private static double DistanceSquared3d(Point3d a, Point3d b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            double dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        private static bool TryFindBestMarkerForPipe(
            Transaction tr,
            Database db,
            ObjectId pipeId,
            out BlockReference? bestMarker,
            out bool moveStart)
        {
            DBObject pipeObj = tr.GetObject(pipeId, OpenMode.ForRead, false);
            bestMarker = null;
            moveStart = false;
            double bestD2 = SinglePipeSearchRadius * SinglePipeSearchRadius;

            foreach (BlockReference marker in GetMarkerBlocks(tr, db, StubMarkerBlock))
            {
                EvaluatePipeEndAgainstMarker(pipeObj, true, marker, ref bestMarker, ref moveStart, ref bestD2);
                EvaluatePipeEndAgainstMarker(pipeObj, false, marker, ref bestMarker, ref moveStart, ref bestD2);
            }

            return bestMarker != null;
        }

        private static void EvaluatePipeEndAgainstMarker(
            DBObject pipeObj,
            bool isStart,
            BlockReference marker,
            ref BlockReference? bestMarker,
            ref bool moveStart,
            ref double bestD2)
        {
            if (!IsCivilPipe(pipeObj))
                return;
            if (isStart && !GetPipeStartStructureId(pipeObj).IsNull)
                return;
            if (!isStart && !GetPipeEndStructureId(pipeObj).IsNull)
                return;

            Point3d p = isStart ? GetPipeStartPoint(pipeObj) : GetPipeEndPoint(pipeObj);
            double dx = p.X - marker.Position.X;
            double dy = p.Y - marker.Position.Y;
            double d2 = dx * dx + dy * dy;
            if (d2 < bestD2)
            {
                bestD2 = d2;
                bestMarker = marker;
                moveStart = isStart;
            }
        }

        private static void MoveStructureAndConnectedPipes(Transaction tr, Database db, ObjectId structureId, Point3d newXy)
        {
            DBObject structureObj = tr.GetObject(structureId, OpenMode.ForWrite, false);
            Point3d old = GetStructurePoint(structureObj);
            Point3d movedPoint = new Point3d(newXy.X, newXy.Y, old.Z);
            SetStructurePoint(structureObj, movedPoint);

            foreach (ObjectId pipeId in GetPipeIdsForStructure(tr, db, structureId))
            {
                DBObject pipeObj = tr.GetObject(pipeId, OpenMode.ForWrite, false);
                ApplyPipeMoveForStructure(pipeObj, structureId, movedPoint);
            }
        }

        private static List<ObjectId> GetPipeIdsForStructure(Transaction tr, Database db, ObjectId structureId)
        {
            List<ObjectId> result = new List<ObjectId>();
            foreach (ObjectId id in EnumerateModelSpaceEntityIds(tr, db))
            {
                DBObject dbo = tr.GetObject(id, OpenMode.ForRead, false);
                if (!IsCivilPipe(dbo))
                    continue;

                if (GetPipeStartStructureId(dbo) == structureId || GetPipeEndStructureId(dbo) == structureId)
                    result.Add(id);
            }
            return result;
        }

        private static void ApplyPipeMoveForStructure(DBObject pipeObj, ObjectId structureId, Point3d movedStructurePoint)
        {
            if (!IsCivilPipe(pipeObj))
                throw new InvalidOperationException("Object is not a Civil 3D pipe.");

            ObjectId startStructureId = GetPipeStartStructureId(pipeObj);
            ObjectId endStructureId = GetPipeEndStructureId(pipeObj);

            Point3d start = GetPipeStartPoint(pipeObj);
            Point3d end = GetPipeEndPoint(pipeObj);
            bool changed = false;

            if (startStructureId == structureId)
            {
                start = new Point3d(movedStructurePoint.X, movedStructurePoint.Y, start.Z);
                changed = true;
            }

            if (endStructureId == structureId)
            {
                end = new Point3d(movedStructurePoint.X, movedStructurePoint.Y, end.Z);
                changed = true;
            }

            if (!changed)
                return;

            if (TryInvokeSetStartAndEndPoints(pipeObj, start, end))
                return;

            SetPipeStartPoint(pipeObj, start);
            SetPipeEndPoint(pipeObj, end);
        }

        private static bool TryFindNearestPipeEnd(
            Transaction tr,
            Database db,
            Point3d target,
            double maxDist,
            HashSet<string> touchedEnds,
            out ObjectId bestPipeId,
            out bool moveStart)
        {
            bestPipeId = ObjectId.Null;
            moveStart = false;
            double bestD2 = maxDist * maxDist;

            foreach (ObjectId id in EnumerateModelSpaceEntityIds(tr, db))
            {
                DBObject dbo = tr.GetObject(id, OpenMode.ForRead, false);
                if (!IsCivilPipe(dbo))
                    continue;

                EvaluatePipeEnd(id, dbo, true, target, touchedEnds, ref bestPipeId, ref moveStart, ref bestD2);
                EvaluatePipeEnd(id, dbo, false, target, touchedEnds, ref bestPipeId, ref moveStart, ref bestD2);
            }

            return !bestPipeId.IsNull;
        }

        private static void EvaluatePipeEnd(
            ObjectId pipeId,
            DBObject pipeObj,
            bool isStart,
            Point3d target,
            HashSet<string> touchedEnds,
            ref ObjectId bestPipeId,
            ref bool bestIsStart,
            ref double bestD2)
        {
            if (isStart && !GetPipeStartStructureId(pipeObj).IsNull)
                return;
            if (!isStart && !GetPipeEndStructureId(pipeObj).IsNull)
                return;

            string key = GetPipeEndKey(pipeId, isStart);
            if (touchedEnds.Contains(key))
                return;

            Point3d p = isStart ? GetPipeStartPoint(pipeObj) : GetPipeEndPoint(pipeObj);
            double dx = p.X - target.X;
            double dy = p.Y - target.Y;
            double d2 = dx * dx + dy * dy;
            if (d2 < bestD2)
            {
                bestD2 = d2;
                bestPipeId = pipeId;
                bestIsStart = isStart;
            }
        }

        private static string GetPipeEndKey(ObjectId pipeId, bool isStart)
            => pipeId.Handle + (isStart ? ":S" : ":E");

        private static ObjectId? FindNearestStructureId(Transaction tr, Database db, Point3d target, double maxDist)
            => FindNearestStructureId(tr, db, target, maxDist, null);

        private static ObjectId? FindNearestStructureId(
            Transaction tr,
            Database db,
            Point3d target,
            double maxDist,
            Func<DBObject, bool>? structureFilter)
        {
            ObjectId? best = null;
            double bestD2 = maxDist * maxDist;

            foreach (ObjectId id in EnumerateModelSpaceEntityIds(tr, db))
            {
                DBObject dbo = tr.GetObject(id, OpenMode.ForRead, false);
                if (!IsCivilStructure(dbo))
                    continue;
                if (structureFilter != null && !structureFilter(dbo))
                    continue;

                Point3d p = GetStructurePoint(dbo);
                double dx = p.X - target.X;
                double dy = p.Y - target.Y;
                double d2 = dx * dx + dy * dy;
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    best = id;
                }
            }

            return best;
        }


        private static BlockReference? FindNearestStructureMarkerBlock(Transaction tr, Database db, Point3d target, double maxDist)
        {
            BlockReference? best = null;
            double bestD2 = maxDist * maxDist;

            foreach (BlockReference br in GetStructureMarkerBlocks(tr, db))
            {
                double dx = br.Position.X - target.X;
                double dy = br.Position.Y - target.Y;
                double d2 = dx * dx + dy * dy;
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    best = br;
                }
            }

            return best;
        }

        private static List<BlockReference> GetStructureMarkerBlocks(Transaction tr, Database db)
        {
            List<BlockReference> result = new List<BlockReference>();
            result.AddRange(GetMarkerBlocks(tr, db, MhMarkerBlock));
            result.AddRange(GetMarkerBlocks(tr, db, StubMarkerBlock));
            return result;
        }

        private static BlockReference? FindNearestMarkerBlock(Transaction tr, Database db, string blockName, Point3d target, double maxDist)
        {
            BlockReference? best = null;
            double bestD2 = maxDist * maxDist;
            foreach (BlockReference br in GetMarkerBlocks(tr, db, blockName))
            {
                double dx = br.Position.X - target.X;
                double dy = br.Position.Y - target.Y;
                double d2 = dx * dx + dy * dy;
                if (d2 < bestD2)
                {
                    bestD2 = d2;
                    best = br;
                }
            }
            return best;
        }

        private static List<BlockReference> GetMarkerBlocks(Transaction tr, Database db, string blockName)
        {
            List<BlockReference> result = new List<BlockReference>();
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is not BlockReference br)
                    continue;

                string name = GetBlockReferenceName(tr, br);
                if (string.Equals(name, blockName, StringComparison.OrdinalIgnoreCase))
                    result.Add(br);
            }

            return result;
        }

        private static IEnumerable<ObjectId> EnumerateModelSpaceEntityIds(Transaction tr, Database db)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            return ms.Cast<ObjectId>().Where(id => id.IsValid && !id.IsErased);
        }

        private static string GetBlockReferenceName(Transaction tr, BlockReference br)
        {
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
            return btr.Name;
        }

        private static bool IsDropInletStructure(DBObject structureObj)
            => ContainsAnyKeyword(GetStructureSearchText(structureObj), "INLET", "DROP", "GRATE", "CURB");

        private static bool IsStormDrainStructure(DBObject structureObj)
        {
            string text = GetStructureSearchText(structureObj);
            if (ContainsAnyKeyword(text, "INLET", "DROP", "GRATE", "CURB"))
                return false;

            return ContainsAnyKeyword(text, "STORM", "SD", "JUNCTION", "MANHOLE", "STRUCTURE");
        }

        private static string GetStructureSearchText(DBObject structureObj)
        {
            List<string> parts = new List<string>();
            AddSearchPart(parts, TryGetStringProperty(structureObj, "PartFamilyName"));
            AddSearchPart(parts, TryGetStringProperty(structureObj, "PartSizeName"));
            AddSearchPart(parts, TryGetStringProperty(structureObj, "PartDescription"));
            AddSearchPart(parts, TryGetStringProperty(structureObj, "Description"));
            AddSearchPart(parts, TryGetStringProperty(structureObj, "Name"));
            return string.Join(" | ", parts).ToUpperInvariant();
        }

        private static void AddSearchPart(List<string> parts, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                parts.Add(value);
        }

        private static string? TryGetStringProperty(object obj, string propertyName)
        {
            PropertyInfo? pi = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null)
                return null;

            object? raw = pi.GetValue(obj);
            return raw?.ToString();
        }

        private static bool ContainsAnyKeyword(string text, params string[] keywords)
            => keywords.Any(k => text.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0);

        private static bool IsCivilPipe(object? obj)
            => HasTypeName(obj, "Autodesk.Civil.DatabaseServices.Pipe") || HasTypeName(obj, "AeccPipe");

        private static bool IsCivilStructure(object? obj)
            => HasTypeName(obj, "Autodesk.Civil.DatabaseServices.Structure") || HasTypeName(obj, "AeccStructure");

        private static bool HasTypeName(object? obj, string fullOrShortName)
        {
            Type? t = obj?.GetType();
            while (t != null)
            {
                if (string.Equals(t.FullName, fullOrShortName, StringComparison.Ordinal) ||
                    string.Equals(t.Name, fullOrShortName, StringComparison.Ordinal))
                    return true;
                t = t.BaseType;
            }
            return false;
        }

        private static double DistanceSquared2d(Point3d a, Point3d b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return (dx * dx) + (dy * dy);
        }

        private static ObjectId GetPipeStartStructureId(object pipeObj)
            => GetPropertyValue<ObjectId>(pipeObj, "StartStructureId");

        private static ObjectId GetPipeEndStructureId(object pipeObj)
            => GetPropertyValue<ObjectId>(pipeObj, "EndStructureId");

        private static Point3d GetPipeStartPoint(object pipeObj)
            => GetPropertyValue<Point3d>(pipeObj, "StartPoint");

        private static Point3d GetPipeEndPoint(object pipeObj)
            => GetPropertyValue<Point3d>(pipeObj, "EndPoint");

        private static void SetPipeStartPoint(object pipeObj, Point3d value)
            => SetPropertyValue(pipeObj, "StartPoint", value);

        private static void SetPipeEndPoint(object pipeObj, Point3d value)
            => SetPropertyValue(pipeObj, "EndPoint", value);

        private static Point3d GetStructurePoint(object structureObj)
        {
            if (TryGetPropertyValue(structureObj, "Position", out Point3d position))
                return position;

            return GetPropertyValue<Point3d>(structureObj, "Location");
        }

        private static void SetStructurePoint(object structureObj, Point3d value)
        {
            if (TrySetPropertyValue(structureObj, "Position", value))
                return;

            SetPropertyValue(structureObj, "Location", value);
        }

        private static void SetStructureRotation(Transaction tr, ObjectId structureId, double rotation)
        {
            DBObject dbo = tr.GetObject(structureId, OpenMode.ForWrite, false);
            SetStructureRotation(dbo, rotation);
        }

        private static void SetStructureRotation(object structureObj, double rotation)
        {
            if (!TrySetPropertyValue(structureObj, "Rotation", rotation))
                return;

            if (structureObj is Entity ent)
                ent.RecordGraphicsModified(true);
        }

        private static bool TryGetPropertyValue<T>(object obj, string propertyName, out T value)
        {
            PropertyInfo? pi = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (pi != null)
            {
                object? raw = pi.GetValue(obj);
                if (raw is T typed)
                {
                    value = typed;
                    return true;
                }
            }

            value = default!;
            return false;
        }

        private static bool TrySetPropertyValue(object obj, string propertyName, object value)
        {
            PropertyInfo? pi = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null || !pi.CanWrite)
                return false;

            pi.SetValue(obj, value);
            return true;
        }

        private static T GetPropertyValue<T>(object obj, string propertyName)
        {
            PropertyInfo? pi = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null)
                throw new MissingMemberException(obj.GetType().FullName, propertyName);

            object? value = pi.GetValue(obj);
            if (value is T typed)
                return typed;

            throw new InvalidCastException($"Property '{propertyName}' on '{obj.GetType().FullName}' could not be cast to '{typeof(T).FullName}'.");
        }

        private static void SetPropertyValue(object obj, string propertyName, object value)
        {
            PropertyInfo? pi = obj.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            if (pi == null)
                throw new MissingMemberException(obj.GetType().FullName, propertyName);

            pi.SetValue(obj, value);
        }

        private static void EnsureLayer(Database db, Transaction tr, string layerName, short colorIndex)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(layerName))
                return;

            lt.UpgradeOpen();
            LayerTableRecord ltr = new LayerTableRecord
            {
                Name = layerName,
                Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                    Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex)
            };
            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }

        private static void EnsureStubMarkerBlock(Database db, Transaction tr)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(StubMarkerBlock))
                return;

            string externalPath = Path.Combine(SurveyBlockFolder, StubMarkerBlock + ".dwg");
            if (File.Exists(externalPath))
            {
                using Database srcDb = new Database(false, true);
                srcDb.ReadDwgFile(externalPath, FileOpenMode.OpenForReadAndAllShare, false, string.Empty);
                ObjectIdCollection ids = new ObjectIdCollection();
                using Transaction srcTr = srcDb.TransactionManager.StartTransaction();
                BlockTable srcBt = (BlockTable)srcTr.GetObject(srcDb.BlockTableId, OpenMode.ForRead);
                if (srcBt.Has(StubMarkerBlock))
                {
                    ids.Add(srcBt[StubMarkerBlock]);
                    IdMapping map = new IdMapping();
                    db.WblockCloneObjects(ids, db.BlockTableId, map, DuplicateRecordCloning.Ignore, false);
                    srcTr.Commit();
                    return;
                }
            }

            bt.UpgradeOpen();
            BlockTableRecord btr = new BlockTableRecord { Name = StubMarkerBlock, Origin = Point3d.Origin };
            bt.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);

            Circle c = new Circle(Point3d.Origin, Vector3d.ZAxis, 1.0) { Layer = "0" };
            btr.AppendEntity(c);
            tr.AddNewlyCreatedDBObject(c, true);

            Line h = new Line(new Point3d(-1.2, 0, 0), new Point3d(1.2, 0, 0)) { Layer = "0" };
            Line v = new Line(new Point3d(0, -1.2, 0), new Point3d(0, 1.2, 0)) { Layer = "0" };
            btr.AppendEntity(h);
            tr.AddNewlyCreatedDBObject(h, true);
            btr.AppendEntity(v);
            tr.AddNewlyCreatedDBObject(v, true);
        }

        private static void InsertMarkerBlock(Database db, Transaction tr, string blockName, Point3d position, string layerName)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(blockName))
                throw new InvalidOperationException($"Block '{blockName}' is not defined in the drawing.");

            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            BlockReference br = new BlockReference(position, bt[blockName])
            {
                Layer = layerName,
                Rotation = 0.0,
                ScaleFactors = new Scale3d(1.0)
            };
            ms.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);
        }
    }
}
