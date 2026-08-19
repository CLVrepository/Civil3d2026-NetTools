using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CLV_CivilTools.Shared;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDocument = Autodesk.AutoCAD.ApplicationServices.Document;
using AcPolyline = Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace CLV_CivilTools.Gis
{
    /// <summary>
    /// Batch automation for storm structures.
    /// Pass 1: find/convert all supported storm structure blocks (drop inlets + circular storm manholes)
    ///         and queue OD copy from matching Structures points.
    /// Pass 2: find/convert remaining junction structures from the remaining Structures points.
    /// </summary>
    public static class GisStormStructureAuto
    {
        private const string OdHelperPath = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\CLV_GIS_OD_HELPERS.lsp";

        private const string StructuresPointLayer = "Structures";
        private const string TargetInnerLayer = "C-STRM-STRC-INNR";
        private const string TargetOuterLayer = "C-STRM-STRC-E";

        private static readonly string[] SourceInnerLayerTokens =
        {
            "V-SURV-STRC-INNER-2D",
            "V-SURV-STRC-INNR-2D"
        };

        private static readonly string[] SourceOuterLayerTokens =
        {
            "V-SURV-OUTR-2D",
            "V-SURV-STRC-OUTR-2D",
            "V-SURV-STRC-OUTER-2D"
        };

        private static readonly string[] SupportedDiNames =
        {
            "TYPE_A-USD_411",
            "TYPE_A_MOD-USD_411.1",
            "TYPE_C-USD_413",
            "TYPE_CM-USD_422",
            "TYPE_CM2-USD_412.1",
            "TYPE_D-USD_414",
            "TYPE_DM2-USD_412.1"
        };

        private static readonly string[] SupportedCircularStormManholeNames =
        {
            "UFLS-GIS-MH-CIRCULAR"
        };

        private const double ExactPointTolerance = 0.10;
        private const double TightPointTolerance = 0.50;
        private const double LoosePointTolerance = 1.50;
        private const double JsSearchRadius = 25.0;
        private const double JsExtentsTolerance = 1.0;

        [CommandMethod("CLV-GIS-STRM-AUTO", CommandFlags.Modal)]
        public static void RunStormStructureAuto()
        {
            AcDocument? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                LayerStandards.EnsureGisLayers(db, ed);

                List<ObjectId> stormStructureBlockIds = new List<ObjectId>();
                int diFound = 0;
                int circularMhFound = 0;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    if (tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) is BlockTableRecord space)
                    {
                        foreach (ObjectId id in space)
                        {
                            if (tr.GetObject(id, OpenMode.ForRead, false) is not BlockReference br)
                                continue;

                            string blockKind = GetSupportedStormStructureBlockKind(br, tr);
                            if (string.IsNullOrWhiteSpace(blockKind))
                                continue;

                            stormStructureBlockIds.Add(id);
                            if (string.Equals(blockKind, "DI", StringComparison.OrdinalIgnoreCase))
                                diFound++;
                            else if (string.Equals(blockKind, "CIRCULAR-MH", StringComparison.OrdinalIgnoreCase))
                                circularMhFound++;
                        }
                    }
                    tr.Commit();
                }

                int blockProcessed = 0;
                int blockFailed = 0;
                int blockPointsMatched = 0;
                int blockOuterEntities = 0;
                int blockOdQueued = 0;
                HashSet<ObjectId> consumedPointIds = new HashSet<ObjectId>();

                foreach (ObjectId blockId in stormStructureBlockIds)
                {
                    if (TryProcessStormStructureBlock(blockId, consumedPointIds, out DropInletBatchResult diResult))
                    {
                        blockProcessed++;
                        if (!diResult.SourcePointId.IsNull)
                            blockPointsMatched++;
                        blockOuterEntities += diResult.OuterHandleCount;
                        if (diResult.OdQueued)
                            blockOdQueued++;
                    }
                    else
                    {
                        blockFailed++;
                    }
                }

                List<ObjectId> remainingStructurePointIds = new List<ObjectId>();
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    if (tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) is BlockTableRecord space)
                    {
                        foreach (ObjectId id in space)
                        {
                            if (consumedPointIds.Contains(id))
                                continue;

                            if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity ent || ent.IsErased)
                                continue;

                            if (!string.Equals(ent.Layer, StructuresPointLayer, StringComparison.OrdinalIgnoreCase))
                                continue;

                            if (TryGetEntityPoint(ent).HasValue)
                                remainingStructurePointIds.Add(id);
                        }
                    }
                    tr.Commit();
                }

                int jsFound = remainingStructurePointIds.Count;
                int jsProcessed = 0;
                int jsFailed = 0;
                int jsOdQueued = 0;

                foreach (ObjectId pointId in remainingStructurePointIds)
                {
                    if (TryProcessJunctionStructure(pointId, out JunctionBatchResult jsResult))
                    {
                        jsProcessed++;
                        if (jsResult.OdQueued)
                            jsOdQueued++;
                    }
                    else
                    {
                        jsFailed++;
                    }
                }

                int xDataCleaned = GisXDataCleanup.CleanCurrentDrawingEntityXData(doc, preserveClvCache: true);
                ed.WriteMessage(
                    $"\nCLV-GIS-STRM-AUTO complete. diFound={diFound}, circularMhFound={circularMhFound}, stormBlocksProcessed={blockProcessed}, stormBlocksFailed={blockFailed}, stormBlockPointsMatched={blockPointsMatched}, stormBlockOuterEntities={blockOuterEntities}, stormBlockOdQueued={blockOdQueued}, remainingPointsForJs={jsFound}, jsProcessed={jsProcessed}, jsFailed={jsFailed}, jsOdQueued={jsOdQueued}, xDataCleaned={xDataCleaned}."
                );
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-STRM-AUTO failed: {ex.Message}");
            }
        }

        private static bool TryProcessStormStructureBlock(ObjectId blockId, HashSet<ObjectId> consumedPointIds, out DropInletBatchResult result)
        {
            result = new DropInletBatchResult();

            AcDocument? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return false;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    if (tr.GetObject(blockId, OpenMode.ForWrite, false) is not BlockReference br || br.IsErased)
                        return false;

                    Point3d blockCenter = GetPreferredBlockCenter(br, tr);
                    EnsureLayer(db, tr, TargetInnerLayer);
                    EnsureLayer(db, tr, TargetOuterLayer);

                    if (tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) is not BlockTableRecord space)
                        return false;

                    List<ObjectId> createdIds = new List<ObjectId>();
                    int createdCount = 0;
                    int erasedCount = 0;
                    int innerMoved = 0;
                    int outerMoved = 0;

                    ExplodeRecursive(br, space, tr, createdIds, ref createdCount);
                    br.Erase(true);

                    foreach (ObjectId id in createdIds)
                    {
                        if (tr.GetObject(id, OpenMode.ForWrite, false) is not Entity ent || ent.IsErased)
                            continue;

                        if (ShouldEraseExplodedEntity(ent))
                        {
                            ent.Erase(true);
                            erasedCount++;
                            continue;
                        }

                        string layerName = ent.Layer ?? string.Empty;
                        if (MatchesAnyToken(layerName, SourceInnerLayerTokens))
                        {
                            ent.Layer = TargetInnerLayer;
                            innerMoved++;
                            continue;
                        }

                        if (MatchesAnyToken(layerName, SourceOuterLayerTokens))
                        {
                            ent.Layer = TargetOuterLayer;
                            outerMoved++;
                        }
                    }

                    ObjectId sourcePointId = FindStructurePointAtCenter(tr, db, blockCenter, out double matchTolerance, out double sourcePointDistance);
                    List<string> outerHandles = GetEligibleOuterDestinationHandles(tr, createdIds, blockCenter);

                    result = new DropInletBatchResult
                    {
                        SourcePointId = sourcePointId,
                        SourcePointHandle = GetHandleString(tr, sourcePointId),
                        OuterHandleCount = outerHandles.Count,
                        CreatedCount = createdCount,
                        ErasedCount = erasedCount,
                        InnerMoved = innerMoved,
                        OuterMoved = outerMoved,
                        MatchTolerance = matchTolerance,
                        SourcePointDistance = sourcePointDistance,
                        OuterHandles = outerHandles
                    };

                    tr.Commit();
                }

                if (!result.SourcePointId.IsNull)
                    consumedPointIds.Add(result.SourcePointId);

                if (!string.IsNullOrWhiteSpace(result.SourcePointHandle) && result.OuterHandles.Count > 0)
                    result.OdQueued = QueueCopyObjectDataViaLisp(result.SourcePointHandle, result.OuterHandles, ed);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryProcessJunctionStructure(ObjectId pointId, out JunctionBatchResult result)
        {
            result = new JunctionBatchResult();

            AcDocument? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return false;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    if (tr.GetObject(pointId, OpenMode.ForRead, false) is not Entity pointEnt || pointEnt.IsErased)
                        return false;

                    if (!string.Equals(pointEnt.Layer, StructuresPointLayer, StringComparison.OrdinalIgnoreCase))
                        return false;

                    Point3d? maybePoint = TryGetEntityPoint(pointEnt);
                    if (!maybePoint.HasValue)
                        return false;

                    Point3d center = maybePoint.Value;
                    string sourcePointHandle = pointEnt.Handle.ToString();

                    EnsureLayer(db, tr, TargetInnerLayer);
                    EnsureLayer(db, tr, TargetOuterLayer);

                    List<PolylineCandidate> candidates = FindClosedPolylineCandidates(tr, db, center, JsSearchRadius);
                    PolylineCandidate? outer = ChooseOuterCandidate(candidates, center);
                    PolylineCandidate? inner = ChooseInnerCandidate(candidates, outer, center);

                    string outerHandle = string.Empty;
                    string innerHandle = string.Empty;
                    if (outer != null && tr.GetObject(outer.Id, OpenMode.ForWrite, false) is Entity outerEnt)
                    {
                        outerEnt.Layer = TargetOuterLayer;
                        outerHandle = outerEnt.Handle.ToString();
                    }

                    if (inner != null && tr.GetObject(inner.Id, OpenMode.ForWrite, false) is Entity innerEnt)
                    {
                        innerEnt.Layer = TargetInnerLayer;
                        innerHandle = innerEnt.Handle.ToString();
                    }

                    result = new JunctionBatchResult
                    {
                        SourcePointHandle = sourcePointHandle,
                        OuterHandle = outerHandle,
                        InnerHandle = innerHandle,
                        CandidateCount = candidates.Count
                    };

                    tr.Commit();
                }

                if (!string.IsNullOrWhiteSpace(result.SourcePointHandle) && !string.IsNullOrWhiteSpace(result.OuterHandle))
                    result.OdQueued = QueueCopyObjectDataViaLisp(result.SourcePointHandle, result.OuterHandle, ed);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string GetSupportedStormStructureBlockKind(BlockReference br, Transaction tr)
        {
            string name = GetEffectiveBlockName(br, tr);
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;

            if (SupportedDiNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                return "DI";

            if (SupportedCircularStormManholeNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                return "CIRCULAR-MH";

            if (name.IndexOf("MH-CIRCULAR", StringComparison.OrdinalIgnoreCase) >= 0)
                return "CIRCULAR-MH";

            if (name.IndexOf("MANHOLE", StringComparison.OrdinalIgnoreCase) >= 0 &&
                name.IndexOf("CIRC", StringComparison.OrdinalIgnoreCase) >= 0)
                return "CIRCULAR-MH";

            return string.Empty;
        }

        private static string GetEffectiveBlockName(BlockReference br, Transaction tr)
        {
            try
            {
                if (br.IsDynamicBlock && !br.DynamicBlockTableRecord.IsNull)
                {
                    if (tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead, false) is BlockTableRecord dynBtr)
                        return dynBtr.Name ?? string.Empty;
                }

                if (tr.GetObject(br.BlockTableRecord, OpenMode.ForRead, false) is BlockTableRecord btr)
                    return btr.Name ?? string.Empty;
            }
            catch
            {
                // ignore
            }

            return string.Empty;
        }

        private static string GetHandleString(Transaction tr, ObjectId id)
        {
            if (id.IsNull || !id.IsValid)
                return string.Empty;

            if (tr.GetObject(id, OpenMode.ForRead, false) is DBObject dbo)
                return dbo.Handle.ToString();

            return string.Empty;
        }

        private static Point3d GetPreferredBlockCenter(BlockReference br, Transaction tr)
        {
            try
            {
                foreach (ObjectId attId in br.AttributeCollection)
                {
                    if (tr.GetObject(attId, OpenMode.ForRead, false) is AttributeReference att &&
                        string.Equals(att.Tag, "DI_CENTER", StringComparison.OrdinalIgnoreCase))
                    {
                        return att.Position;
                    }
                }
            }
            catch
            {
            }

            try
            {
                return br.Position;
            }
            catch
            {
                try
                {
                    Extents3d ext = br.GeometricExtents;
                    return new Point3d((ext.MinPoint.X + ext.MaxPoint.X) * 0.5, (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5, (ext.MinPoint.Z + ext.MaxPoint.Z) * 0.5);
                }
                catch
                {
                    return Point3d.Origin;
                }
            }
        }

        private static void ExplodeRecursive(Entity sourceEnt, BlockTableRecord space, Transaction tr, List<ObjectId> createdIds, ref int createdCount)
        {
            if (sourceEnt is BlockReference nestedBr)
            {
                DBObjectCollection exploded = new DBObjectCollection();
                nestedBr.Explode(exploded);

                foreach (DBObject dbo in exploded)
                {
                    if (dbo is not Entity childEnt)
                    {
                        dbo.Dispose();
                        continue;
                    }

                    if (childEnt is BlockReference childBr)
                    {
                        ExplodeRecursive(childBr, space, tr, createdIds, ref createdCount);
                        childBr.Dispose();
                        continue;
                    }

                    space.AppendEntity(childEnt);
                    tr.AddNewlyCreatedDBObject(childEnt, true);
                    createdIds.Add(childEnt.ObjectId);
                    createdCount++;
                }

                return;
            }

            Entity clone = (Entity)sourceEnt.Clone();
            space.AppendEntity(clone);
            tr.AddNewlyCreatedDBObject(clone, true);
            createdIds.Add(clone.ObjectId);
            createdCount++;
        }

        private static bool ShouldEraseExplodedEntity(Entity ent)
        {
            if (ent is AttributeReference || ent is DBText || ent is MText)
                return true;

            string layerName = ent.Layer ?? string.Empty;
            if (layerName.IndexOf("CURB", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (string.Equals(layerName, "C-DETL-MARK", StringComparison.OrdinalIgnoreCase))
                return true;

            if (ent is Circle circle && circle.Radius <= 0.20)
                return true;

            if (ent is BlockReference)
                return true;

            return false;
        }

        private static bool MatchesAnyToken(string layerName, IEnumerable<string> tokens)
        {
            foreach (string token in tokens)
            {
                if (layerName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static void EnsureLayer(Database db, Transaction tr, string layerName)
        {
            Editor? ed = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            if (ed != null && LayerStandards.TryEnsureManagedGisLayer(db, tr, ed, layerName))
                return;

            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(layerName))
                return;

            lt.UpgradeOpen();
            LayerTableRecord ltr = new LayerTableRecord { Name = layerName };
            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }

        private static ObjectId FindStructurePointAtCenter(Transaction tr, Database db, Point3d center, out double matchTolerance, out double sourcePointDistance)
        {
            matchTolerance = 0.0;
            sourcePointDistance = -1.0;

            ObjectId bestId = FindStructurePointAtCenterCore(tr, db, center, ExactPointTolerance, out sourcePointDistance);
            if (!bestId.IsNull)
            {
                matchTolerance = ExactPointTolerance;
                return bestId;
            }

            bestId = FindStructurePointAtCenterCore(tr, db, center, TightPointTolerance, out sourcePointDistance);
            if (!bestId.IsNull)
            {
                matchTolerance = TightPointTolerance;
                return bestId;
            }

            bestId = FindStructurePointAtCenterCore(tr, db, center, LoosePointTolerance, out sourcePointDistance);
            if (!bestId.IsNull)
                matchTolerance = LoosePointTolerance;

            return bestId;
        }

        private static ObjectId FindStructurePointAtCenterCore(Transaction tr, Database db, Point3d center, double tolerance, out double sourcePointDistance)
        {
            sourcePointDistance = -1.0;
            if (tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) is not BlockTableRecord space)
                return ObjectId.Null;

            ObjectId bestId = ObjectId.Null;
            double bestDistance = double.MaxValue;

            foreach (ObjectId id in space)
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity ent || ent.IsErased)
                    continue;

                if (!string.Equals(ent.Layer, StructuresPointLayer, StringComparison.OrdinalIgnoreCase))
                    continue;

                Point3d? maybePoint = TryGetEntityPoint(ent);
                if (!maybePoint.HasValue)
                    continue;

                double dist = Distance2d(center, maybePoint.Value);
                if (dist > tolerance)
                    continue;

                if (dist < bestDistance)
                {
                    bestDistance = dist;
                    bestId = id;
                }
            }

            if (!bestId.IsNull)
                sourcePointDistance = bestDistance;

            return bestId;
        }

        private static Point3d? TryGetEntityPoint(Entity ent)
        {
            if (ent is DBPoint dbPoint)
                return dbPoint.Position;

            if (ent is BlockReference br)
                return br.Position;

            return null;
        }

        private static List<string> GetEligibleOuterDestinationHandles(Transaction tr, List<ObjectId> createdIds, Point3d blockCenter)
        {
            List<(string Handle, double Score)> handles = new List<(string, double)>();

            foreach (ObjectId id in createdIds)
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity ent || ent.IsErased)
                    continue;

                if (!string.Equals(ent.Layer, TargetOuterLayer, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!CouldBeOuterDestination(ent, blockCenter))
                    continue;

                handles.Add((ent.Handle.ToString(), ScoreOuterDestination(ent, blockCenter)));
            }

            return handles
                .OrderByDescending(x => x.Score)
                .Select(x => x.Handle)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool CouldBeOuterDestination(Entity ent, Point3d blockCenter)
        {
            if (ent is Circle circle)
                return Distance2d(blockCenter, circle.Center) <= Math.Max(circle.Radius, 1.5);

            if (ent is AcPolyline pl)
            {
                if (!pl.Closed)
                    return false;

                try
                {
                    if (pl.Area <= 0.0)
                        return false;

                    return IsPointInsideClosedPolyline(pl, blockCenter);
                }
                catch
                {
                    return false;
                }
            }

            if (ent is Ellipse ellipse)
            {
                try
                {
                    Point3d c = ellipse.Center;
                    double major = ellipse.MajorAxis.Length;
                    double minor = major * ellipse.RadiusRatio;
                    double tol = Math.Max(Math.Max(major, minor), 1.5);
                    return Distance2d(blockCenter, c) <= tol;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private static double ScoreOuterDestination(Entity ent, Point3d blockCenter)
        {
            Point3d candidateCenter = GetEntityCenter(ent);
            double distPenalty = Distance2d(blockCenter, candidateCenter);

            if (ent is Circle circle)
                return (Math.PI * circle.Radius * circle.Radius * 1000.0) - distPenalty;

            if (ent is AcPolyline pl)
            {
                try
                {
                    return (Math.Abs(pl.Area) * 1000.0) - distPenalty;
                }
                catch
                {
                    return -distPenalty;
                }
            }

            if (ent is Ellipse ellipse)
            {
                try
                {
                    double major = ellipse.MajorAxis.Length;
                    double minor = major * ellipse.RadiusRatio;
                    return (Math.PI * major * minor * 1000.0) - distPenalty;
                }
                catch
                {
                    return -distPenalty;
                }
            }

            return -distPenalty;
        }

        private static Point3d GetEntityCenter(Entity ent)
        {
            try
            {
                Extents3d ext = ent.GeometricExtents;
                return new Point3d(
                    (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                    (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5,
                    (ext.MinPoint.Z + ext.MaxPoint.Z) * 0.5);
            }
            catch
            {
                if (ent is BlockReference br)
                    return br.Position;

                if (ent is Circle circle)
                    return circle.Center;

                if (ent is Ellipse ellipse)
                    return ellipse.Center;

                return Point3d.Origin;
            }
        }

        private static List<PolylineCandidate> FindClosedPolylineCandidates(Transaction tr, Database db, Point3d center, double maxRadius)
        {
            List<PolylineCandidate> results = new List<PolylineCandidate>();
            if (tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) is not BlockTableRecord space)
                return results;

            foreach (ObjectId id in space)
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is not AcPolyline pl || pl.IsErased || !pl.Closed)
                    continue;

                if (pl.Area <= 0.0)
                    continue;

                if (!CouldBelongToStructure(pl))
                    continue;

                Point3d plCenter = GetPolylineCenter(pl);
                if (Distance2d(center, plCenter) > maxRadius)
                    continue;

                Extents3d ext;
                try
                {
                    ext = pl.GeometricExtents;
                }
                catch
                {
                    continue;
                }

                if (!PointWithinExtents(center, ext, JsExtentsTolerance))
                    continue;

                bool contains = false;
                try
                {
                    contains = IsPointInsideClosedPolyline(pl, center);
                }
                catch
                {
                    contains = false;
                }

                if (!contains)
                    continue;

                results.Add(new PolylineCandidate(id, pl.Handle.ToString(), Math.Abs(pl.Area), plCenter, ext));
            }

            return results;
        }

        private static bool CouldBelongToStructure(AcPolyline pl)
        {
            string layer = pl.Layer ?? string.Empty;
            if (string.Equals(layer, StructuresPointLayer, StringComparison.OrdinalIgnoreCase))
                return false;

            if (layer.IndexOf("CURB", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            if (string.Equals(layer, "C-DETL-MARK", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private static PolylineCandidate? ChooseOuterCandidate(List<PolylineCandidate> candidates, Point3d center)
        {
            if (candidates.Count == 0)
                return null;

            return candidates.OrderByDescending(c => c.Area).ThenBy(c => Distance2d(center, c.Center)).FirstOrDefault();
        }

        private static PolylineCandidate? ChooseInnerCandidate(List<PolylineCandidate> candidates, PolylineCandidate? outer, Point3d center)
        {
            if (outer == null)
                return null;

            return candidates
                .Where(c => c.Id != outer.Id && c.Area < outer.Area && ExtentsContainedWithin(c.Extents, outer.Extents, 0.5))
                .OrderByDescending(c => c.Area)
                .ThenBy(c => Distance2d(center, c.Center))
                .FirstOrDefault();
        }

        private static Point3d GetPolylineCenter(AcPolyline pl)
        {
            Extents3d ext = pl.GeometricExtents;
            return new Point3d((ext.MinPoint.X + ext.MaxPoint.X) * 0.5, (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5, 0.0);
        }

        private static bool PointWithinExtents(Point3d point, Extents3d ext, double tol)
        {
            return point.X >= ext.MinPoint.X - tol &&
                   point.X <= ext.MaxPoint.X + tol &&
                   point.Y >= ext.MinPoint.Y - tol &&
                   point.Y <= ext.MaxPoint.Y + tol;
        }

        private static bool ExtentsContainedWithin(Extents3d inner, Extents3d outer, double tol)
        {
            return inner.MinPoint.X >= outer.MinPoint.X - tol &&
                   inner.MaxPoint.X <= outer.MaxPoint.X + tol &&
                   inner.MinPoint.Y >= outer.MinPoint.Y - tol &&
                   inner.MaxPoint.Y <= outer.MaxPoint.Y + tol;
        }

        private static bool IsPointInsideClosedPolyline(AcPolyline pl, Point3d point)
        {
            List<Point2d> vertices = new List<Point2d>();
            for (int i = 0; i < pl.NumberOfVertices; i++)
                vertices.Add(pl.GetPoint2dAt(i));

            if (vertices.Count < 3)
                return false;

            bool inside = false;
            double x = point.X;
            double y = point.Y;
            int j = vertices.Count - 1;

            for (int i = 0; i < vertices.Count; i++)
            {
                double xi = vertices[i].X;
                double yi = vertices[i].Y;
                double xj = vertices[j].X;
                double yj = vertices[j].Y;

                bool intersect = ((yi > y) != (yj > y)) &&
                                 (x < ((xj - xi) * (y - yi) / ((yj - yi) == 0.0 ? 1e-12 : (yj - yi)) + xi));
                if (intersect)
                    inside = !inside;

                j = i;
            }

            return inside;
        }

        private static double Distance2d(Point3d a, Point3d b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        private static bool QueueCopyObjectDataViaLisp(string sourceHandle, string destHandle, Editor ed)
        {
            if (string.IsNullOrWhiteSpace(sourceHandle) || string.IsNullOrWhiteSpace(destHandle) || !File.Exists(OdHelperPath))
                return false;

            try
            {
                AcDocument? doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null)
                    return false;

                string escapedPath = OdHelperPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
                string escapedSource = sourceHandle.Replace("\"", "\\\"");
                string escapedDest = destHandle.Replace("\"", "\\\"");
                string expr = $"(progn (vl-load-com) (load \"{escapedPath}\") (CLV-GIS-OD-COPY-HANDLES \"{escapedSource}\" \"{escapedDest}\") (princ)) ";
                doc.SendStringToExecute(expr, true, false, false);
                return true;
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-STRM-AUTO OD helper queue failed: {ex.Message}");
                return false;
            }
        }

        private static bool QueueCopyObjectDataViaLisp(string sourceHandle, IEnumerable<string> destHandles, Editor ed)
        {
            List<string> cleanHandles = destHandles
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (string.IsNullOrWhiteSpace(sourceHandle) || cleanHandles.Count == 0 || !File.Exists(OdHelperPath))
                return false;

            try
            {
                AcDocument? doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null)
                    return false;

                string escapedPath = OdHelperPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
                string escapedSource = sourceHandle.Replace("\"", "\\\"");
                string copies = string.Join(" ", cleanHandles.Select(h =>
                {
                    string escapedDest = h.Replace("\"", "\\\"");
                    return $"(CLV-GIS-OD-COPY-HANDLES \"{escapedSource}\" \"{escapedDest}\")";
                }));

                string expr = $"(progn (vl-load-com) (load \"{escapedPath}\") {copies} (princ)) ";
                doc.SendStringToExecute(expr, true, false, false);
                return true;
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-STRM-AUTO OD helper queue failed: {ex.Message}");
                return false;
            }
        }

        private sealed class PolylineCandidate
        {
            public PolylineCandidate(ObjectId id, string handle, double area, Point3d center, Extents3d extents)
            {
                Id = id;
                Handle = handle;
                Area = area;
                Center = center;
                Extents = extents;
            }

            public ObjectId Id { get; }
            public string Handle { get; }
            public double Area { get; }
            public Point3d Center { get; }
            public Extents3d Extents { get; }
        }

        private sealed class DropInletBatchResult
        {
            public ObjectId SourcePointId { get; set; } = ObjectId.Null;
            public string SourcePointHandle { get; set; } = string.Empty;
            public List<string> OuterHandles { get; set; } = new List<string>();
            public int OuterHandleCount { get; set; }
            public int CreatedCount { get; set; }
            public int ErasedCount { get; set; }
            public int InnerMoved { get; set; }
            public int OuterMoved { get; set; }
            public double MatchTolerance { get; set; }
            public double SourcePointDistance { get; set; } = -1.0;
            public bool OdQueued { get; set; }
        }

        private sealed class JunctionBatchResult
        {
            public string SourcePointHandle { get; set; } = string.Empty;
            public string OuterHandle { get; set; } = string.Empty;
            public string InnerHandle { get; set; } = string.Empty;
            public int CandidateCount { get; set; }
            public bool OdQueued { get; set; }
        }
    }
}
