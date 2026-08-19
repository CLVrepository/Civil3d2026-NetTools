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
    /// Sewer manhole conversion helpers.
    /// Single command lets the user select a source Structures point.
    /// All command loops all Structures points and runs the same conversion.
    ///
    /// Rules:
    /// - Source OD comes from the selected/imported Structures point.
    /// - Nearby block linework is exploded first when found.
    /// - Inner/outer structure linework is migrated to sewer GIS layers.
    /// - OD is copied to the outer structure object(s).
    /// </summary>
    public static class GisSewerManhole
    {
        private const string OdHelperPath = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\CLV_GIS_OD_HELPERS.lsp";

        private const string StructuresPointLayer = "Structures";
        private const string TargetInnerLayer = "C-SSWR-STRC-INNR";
        private const string TargetOuterLayer = "C-SSWR-STRC-E";

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

        private const double MaxSearchRadius = 25.0;
        private const double BlockCenterTolerance = 3.0;

        [CommandMethod("CLV-GIS-SSWR-MH", CommandFlags.Modal)]
        public static void ConvertSingleManhole()
        {
            AcDocument? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                LayerStandards.EnsureGisLayers(db, ed);

                PromptEntityOptions peo = new PromptEntityOptions("\nSELECT SEWER MANHOLE POINT: ");
                peo.SetRejectMessage("\nSelect the imported GIS structure point.");
                peo.AddAllowedClass(typeof(DBPoint), exactMatch: false);
                peo.AddAllowedClass(typeof(BlockReference), exactMatch: false);

                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                    return;

                string sourcePointHandle = string.Empty;
                ManholeConversionResult? result = null;

                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    if (tr.GetObject(per.ObjectId, OpenMode.ForRead, false) is not Entity pointEnt || pointEnt.IsErased)
                    {
                        ed.WriteMessage("\nCLV-GIS-SSWR-MH: unable to open selected point.");
                        return;
                    }

                    if (!string.Equals(pointEnt.Layer, StructuresPointLayer, StringComparison.OrdinalIgnoreCase))
                    {
                        ed.WriteMessage($"\nCLV-GIS-SSWR-MH: selected entity is on layer '{pointEnt.Layer}', expected '{StructuresPointLayer}'.");
                        return;
                    }

                    Point3d? maybePoint = TryGetEntityPoint(pointEnt);
                    if (!maybePoint.HasValue)
                    {
                        ed.WriteMessage("\nCLV-GIS-SSWR-MH: selected entity does not provide a point location.");
                        return;
                    }

                    sourcePointHandle = pointEnt.Handle.ToString();
                    result = ConvertAtPoint(tr, db, maybePoint.Value, sourcePointHandle);
                    tr.Commit();
                }

                if (result != null)
                {
                    bool odQueued = QueueCopyObjectDataViaLisp(sourcePointHandle, result.OuterHandles, ed);
                    int xDataCleaned = GisXDataCleanup.CleanCurrentDrawingEntityXData(doc, preserveClvCache: true);
                    ed.WriteMessage(
                        $"\nCLV-GIS-SSWR-MH complete. explodedBlock={(result.ExplodedBlock ? "yes" : "no")}, created={result.CreatedCount}, erased={result.ErasedCount}, innerMoved={result.InnerMoved}, outerMoved={result.OuterMoved}, outerTargets={result.OuterHandles.Count}, odCopyQueued={(odQueued ? "yes" : "no")}, xDataCleaned={xDataCleaned}."
                    );
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-SSWR-MH failed: {ex.Message}");
            }
        }

        [CommandMethod("CLV-GIS-SSWR-MH-ALL", CommandFlags.Modal)]
        public static void ConvertAllManholes()
        {
            AcDocument? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                LayerStandards.EnsureGisLayers(db, ed);

                int processed = 0;
                int converted = 0;
                int totalCreated = 0;
                int totalErased = 0;
                int totalInnerMoved = 0;
                int totalOuterMoved = 0;
                int odQueuedCount = 0;

                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayer(db, tr, TargetInnerLayer);
                    EnsureLayer(db, tr, TargetOuterLayer);

                    List<ObjectId> pointIds = GetStructurePointIds(tr, db);
                    processed = pointIds.Count;

                    foreach (ObjectId pointId in pointIds)
                    {
                        if (tr.GetObject(pointId, OpenMode.ForRead, false) is not Entity pointEnt || pointEnt.IsErased)
                            continue;

                        Point3d? maybePoint = TryGetEntityPoint(pointEnt);
                        if (!maybePoint.HasValue)
                            continue;

                        string sourcePointHandle = pointEnt.Handle.ToString();
                        ManholeConversionResult result = ConvertAtPoint(tr, db, maybePoint.Value, sourcePointHandle);

                        if (result.InnerMoved > 0 || result.OuterMoved > 0 || result.OuterHandles.Count > 0)
                            converted++;

                        totalCreated += result.CreatedCount;
                        totalErased += result.ErasedCount;
                        totalInnerMoved += result.InnerMoved;
                        totalOuterMoved += result.OuterMoved;

                        if (QueueCopyObjectDataViaLisp(sourcePointHandle, result.OuterHandles, ed))
                            odQueuedCount++;
                    }

                    tr.Commit();
                }

                int xDataCleaned = GisXDataCleanup.CleanCurrentDrawingEntityXData(doc, preserveClvCache: true);
                ed.WriteMessage(
                    $"\nCLV-GIS-SSWR-MH-ALL complete. points={processed}, converted={converted}, created={totalCreated}, erased={totalErased}, innerMoved={totalInnerMoved}, outerMoved={totalOuterMoved}, odQueued={odQueuedCount}, xDataCleaned={xDataCleaned}."
                );
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-SSWR-MH-ALL failed: {ex.Message}");
            }
        }

        private static ManholeConversionResult ConvertAtPoint(Transaction tr, Database db, Point3d center, string sourcePointHandle)
        {
            ManholeConversionResult result = new ManholeConversionResult();
            EnsureLayer(db, tr, TargetInnerLayer);
            EnsureLayer(db, tr, TargetOuterLayer);

            List<ObjectId> createdIds = new List<ObjectId>();

            if (TryFindContainingBlock(tr, db, center, out ObjectId blockId) &&
                tr.GetObject(blockId, OpenMode.ForWrite, false) is BlockReference br &&
                !br.IsErased)
            {
                result.ExplodedBlock = true;
                BlockTableRecord space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                int createdCount = result.CreatedCount;
                ExplodeRecursive(br, space, tr, createdIds, ref createdCount);
                result.CreatedCount = createdCount;
                br.Erase();
                result.ErasedCount++;
            }

            IEnumerable<ObjectId> candidateIds = createdIds.Count > 0
                ? createdIds
                : FindNearbyStructureEntities(tr, db, center, MaxSearchRadius);

            foreach (ObjectId id in candidateIds)
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity ent || ent.IsErased)
                    continue;

                if (ShouldEraseExplodedEntity(ent))
                {
                    ent.UpgradeOpen();
                    ent.Erase();
                    result.ErasedCount++;
                    continue;
                }

                string layerName = ent.Layer ?? string.Empty;
                string? targetLayer = ResolveTargetLayer(layerName, ent, center);
                if (string.IsNullOrWhiteSpace(targetLayer))
                    continue;

                ent.UpgradeOpen();
                ent.Layer = targetLayer;

                if (string.Equals(targetLayer, TargetInnerLayer, StringComparison.OrdinalIgnoreCase))
                    result.InnerMoved++;
                else if (string.Equals(targetLayer, TargetOuterLayer, StringComparison.OrdinalIgnoreCase))
                    result.OuterMoved++;
            }

            result.OuterHandles = GetEligibleOuterDestinationHandles(tr, candidateIds.Distinct(), center);
            return result;
        }

        private static List<ObjectId> GetStructurePointIds(Transaction tr, Database db)
        {
            List<ObjectId> ids = new List<ObjectId>();
            if (tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) is not BlockTableRecord space)
                return ids;

            foreach (ObjectId id in space)
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity ent || ent.IsErased)
                    continue;

                if (!string.Equals(ent.Layer, StructuresPointLayer, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (TryGetEntityPoint(ent).HasValue)
                    ids.Add(id);
            }

            return ids;
        }

        private static string? ResolveTargetLayer(string layerName, Entity ent, Point3d center)
        {
            if (MatchesAnyToken(layerName, SourceInnerLayerTokens))
                return TargetInnerLayer;

            if (MatchesAnyToken(layerName, SourceOuterLayerTokens))
                return TargetOuterLayer;

            if (ent is Circle circle)
            {
                Point3d circleCenter = circle.Center;
                double dist = Distance2d(center, circleCenter);
                if (dist > MaxSearchRadius)
                    return null;

                return circle.Radius <= 4.0 ? TargetInnerLayer : TargetOuterLayer;
            }

            if (ent is AcPolyline pl && pl.Closed)
            {
                try
                {
                    if (IsPointInsideClosedPolyline(pl, center))
                    {
                        double area = Math.Abs(pl.Area);
                        return area <= 150.0 ? TargetInnerLayer : TargetOuterLayer;
                    }
                }
                catch
                {
                    // ignore geometry issue
                }
            }

            return null;
        }

        private static IEnumerable<ObjectId> FindNearbyStructureEntities(Transaction tr, Database db, Point3d center, double radius)
        {
            List<ObjectId> ids = new List<ObjectId>();
            if (tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) is not BlockTableRecord space)
                return ids;

            foreach (ObjectId id in space)
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity ent || ent.IsErased)
                    continue;

                if (ent is not Curve)
                    continue;

                if (!CouldBelongToStructure(ent))
                    continue;

                Point3d entCenter = GetEntityCenter(ent);
                if (Distance2d(center, entCenter) > radius)
                    continue;

                ids.Add(id);
            }

            return ids;
        }

        private static bool CouldBelongToStructure(Entity ent)
        {
            string layer = ent.Layer ?? string.Empty;
            if (string.Equals(layer, StructuresPointLayer, StringComparison.OrdinalIgnoreCase))
                return false;

            if (layer.IndexOf("CURB", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            if (string.Equals(layer, "C-DETL-MARK", StringComparison.OrdinalIgnoreCase))
                return false;

            return ent is Circle || ent is AcPolyline || ent is Line || ent is Arc || ent is Ellipse;
        }

        private static bool TryFindContainingBlock(Transaction tr, Database db, Point3d center, out ObjectId blockId)
        {
            blockId = ObjectId.Null;
            double bestDist = double.MaxValue;

            if (tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) is not BlockTableRecord space)
                return false;

            foreach (ObjectId id in space)
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is not BlockReference br || br.IsErased)
                    continue;

                if (string.Equals(br.Layer, StructuresPointLayer, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    Extents3d ext = br.GeometricExtents;
                    if (!PointWithinExtents(center, ext, 1.0))
                        continue;

                    Point3d c = GetEntityCenter(br);
                    double dist = Distance2d(center, c);
                    if (dist < bestDist && dist <= BlockCenterTolerance)
                    {
                        bestDist = dist;
                        blockId = id;
                    }
                }
                catch
                {
                    // ignore bad block extents
                }
            }

            return !blockId.IsNull;
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

            if (ent is Circle circle && circle.Radius <= 0.10)
                return true;

            return false;
        }

        private static List<string> GetEligibleOuterDestinationHandles(Transaction tr, IEnumerable<ObjectId> ids, Point3d center)
        {
            List<(string Handle, double Score)> outer = new List<(string, double)>();

            foreach (ObjectId id in ids)
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity ent || ent.IsErased)
                    continue;

                if (!string.Equals(ent.Layer, TargetOuterLayer, StringComparison.OrdinalIgnoreCase))
                    continue;

                double score = ScoreOuterCandidate(ent, center);
                outer.Add((ent.Handle.ToString(), score));
            }

            return outer
                .OrderByDescending(x => x.Score)
                .Select(x => x.Handle)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static double ScoreOuterCandidate(Entity ent, Point3d center)
        {
            Point3d candidateCenter = GetEntityCenter(ent);
            double distPenalty = Distance2d(center, candidateCenter);

            if (ent is Circle circle)
                return (Math.PI * circle.Radius * circle.Radius * 1000.0) - distPenalty;

            if (ent is AcPolyline pl)
            {
                double area = 0.0;
                try
                {
                    if (pl.Closed)
                        area = Math.Abs(pl.Area);
                }
                catch
                {
                    area = 0.0;
                }

                return (area * 1000.0) - distPenalty;
            }

            if (ent is Curve curve)
            {
                double length = 0.0;
                try
                {
                    length = curve.GetDistanceAtParameter(curve.EndParam) - curve.GetDistanceAtParameter(curve.StartParam);
                }
                catch
                {
                    length = 0.0;
                }

                return length - distPenalty;
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

                return Point3d.Origin;
            }
        }

        private static bool MatchesAnyToken(string layerName, IEnumerable<string> tokens)
        {
            foreach (string token in tokens)
            {
                if (layerName.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static Point3d? TryGetEntityPoint(Entity ent)
        {
            if (ent is DBPoint dbPoint)
                return dbPoint.Position;

            if (ent is BlockReference br)
                return br.Position;

            return null;
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

        private static bool PointWithinExtents(Point3d point, Extents3d ext, double tol)
        {
            return point.X >= ext.MinPoint.X - tol &&
                   point.X <= ext.MaxPoint.X + tol &&
                   point.Y >= ext.MinPoint.Y - tol &&
                   point.Y <= ext.MaxPoint.Y + tol;
        }

        private static bool IsPointInsideClosedPolyline(AcPolyline pl, Point3d point)
        {
            var vertices = new List<Point2d>();
            int count = pl.NumberOfVertices;
            for (int i = 0; i < count; i++)
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

        private static bool QueueCopyObjectDataViaLisp(string sourceHandle, IEnumerable<string> destHandles, Editor ed)
        {
            if (string.IsNullOrWhiteSpace(sourceHandle) || !File.Exists(OdHelperPath))
                return false;

            List<string> cleanHandles = destHandles
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (cleanHandles.Count == 0)
                return false;

            try
            {
                AcDocument? doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null)
                    return false;

                string escapedPath = OdHelperPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
                string escapedSource = sourceHandle.Replace("\"", "\\\"");
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("(progn (vl-load-com) ");
                sb.Append("(load \"").Append(escapedPath).Append("\") ");

                foreach (string destHandle in cleanHandles)
                {
                    string escapedDest = destHandle.Replace("\"", "\\\"");
                    sb.Append("(CLV-GIS-OD-COPY-HANDLES \"").Append(escapedSource).Append("\" \"").Append(escapedDest).Append("\") ");
                }

                sb.Append("(princ)) ");
                doc.SendStringToExecute(sb.ToString(), true, false, false);
                return true;
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-SSWR-MH OD helper queue failed: {ex.Message}");
                return false;
            }
        }

        private sealed class ManholeConversionResult
        {
            public bool ExplodedBlock { get; set; }
            public int CreatedCount { get; set; }
            public int ErasedCount { get; set; }
            public int InnerMoved { get; set; }
            public int OuterMoved { get; set; }
            public List<string> OuterHandles { get; set; } = new List<string>();
        }
    }
}
