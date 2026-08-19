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
    /// Explodes a UFLS drop-inlet block to GIS-style structure linework.
    /// User selects the block only. The routine explodes it, deletes curb/marker/text remnants,
    /// remaps inner/outer linework layers, then copies GIS Object Data from the aligned Structures point
    /// to all eligible outer structure entities created from that block.
    /// </summary>
    public static class GisDropInletExplodeToGis
    {
        private const string OdHelperPath = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\CLV_GIS_OD_HELPERS.lsp";

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

        private const string TargetInnerLayer = "C-STRM-STRC-INNR";
        private const string TargetOuterLayer = "C-STRM-STRC-E";
        private const string StructuresPointLayer = "Structures";

        private const double ExactPointTolerance = 0.10;
        private const double TightPointTolerance = 0.50;
        private const double LoosePointTolerance = 1.50;

        [CommandMethod("CLV-GIS-DI-EXPLODE", CommandFlags.Modal)]
        public static void ExplodeDropInletToGis()
        {
            AcDocument? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                LayerStandards.EnsureGisLayers(db, ed);
                TryFocusDrawingView();

                PromptEntityOptions peo = new PromptEntityOptions("\nSELECT DROP INLET BLOCK: ");
                peo.SetRejectMessage("\nSelect a drop inlet block reference.");
                peo.AddAllowedClass(typeof(BlockReference), exactMatch: false);

                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                    return;

                string sourcePointHandle = string.Empty;
                string sampleOuterHandle = string.Empty;
                List<string> outerHandles = new List<string>();
                int createdCount = 0;
                int erasedCount = 0;
                int erasedTextCount = 0;
                int erasedCurbCount = 0;
                int erasedMarkerCount = 0;
                int erasedTinyCircleCount = 0;
                int innerCount = 0;
                int outerCount = 0;
                double matchTolerance = 0.0;
                double sourcePointDistance = -1.0;
                Point3d blockCenter = Point3d.Origin;

                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    if (tr.GetObject(per.ObjectId, OpenMode.ForWrite, false) is not BlockReference br)
                    {
                        ed.WriteMessage("\nCLV-GIS-DI-EXPLODE: selected object was not a block reference.");
                        return;
                    }

                    blockCenter = GetPreferredBlockCenter(br, tr);

                    EnsureLayer(db, tr, TargetInnerLayer);
                    EnsureLayer(db, tr, TargetOuterLayer);

                    if (tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite) is not BlockTableRecord space)
                    {
                        ed.WriteMessage("\nCLV-GIS-DI-EXPLODE: unable to open current space.");
                        return;
                    }

                    List<ObjectId> createdIds = new List<ObjectId>();
                    ExplodeRecursive(br, space, tr, createdIds, ref createdCount);
                    br.Erase(true);

                    foreach (ObjectId id in createdIds)
                    {
                        if (tr.GetObject(id, OpenMode.ForWrite, false) is not Entity ent || ent.IsErased)
                            continue;

                        if (ShouldEraseExplodedEntity(ent, out string eraseReason))
                        {
                            ent.Erase(true);
                            erasedCount++;
                            switch (eraseReason)
                            {
                                case "text": erasedTextCount++; break;
                                case "curb": erasedCurbCount++; break;
                                case "marker": erasedMarkerCount++; break;
                                case "tiny-circle": erasedTinyCircleCount++; break;
                            }
                            continue;
                        }

                        string layerName = ent.Layer ?? string.Empty;
                        if (MatchesAnyToken(layerName, SourceInnerLayerTokens))
                        {
                            ent.Layer = TargetInnerLayer;
                            innerCount++;
                            continue;
                        }

                        if (MatchesAnyToken(layerName, SourceOuterLayerTokens))
                        {
                            ent.Layer = TargetOuterLayer;
                            outerCount++;
                        }
                    }

                    ObjectId sourcePointId = FindStructurePointAtCenter(tr, db, blockCenter, out matchTolerance, out sourcePointDistance);
                    if (!sourcePointId.IsNull && sourcePointId.IsValid)
                    {
                        DBObject sourceObj = tr.GetObject(sourcePointId, OpenMode.ForRead, false);
                        sourcePointHandle = sourceObj.Handle.ToString();
                    }

                    outerHandles = GetEligibleOuterDestinationHandles(tr, createdIds, blockCenter);
                    if (outerHandles.Count > 0)
                        sampleOuterHandle = outerHandles[0];

                    tr.Commit();
                }

                bool odQueued = false;
                if (!string.IsNullOrWhiteSpace(sourcePointHandle) && outerHandles.Count > 0)
                {
                    odQueued = QueueCopyObjectDataViaLisp(sourcePointHandle, outerHandles, ed);
                }

                string plotMode = db.PlotStyleMode ? "CTB" : "STB";
                string innerLayerState = ForceAndDescribeManagedLayer(doc, TargetInnerLayer);
                string outerLayerState = ForceAndDescribeManagedLayer(doc, TargetOuterLayer);
                int xDataCleaned = GisXDataCleanup.CleanCurrentDrawingEntityXData(doc, preserveClvCache: true);
                string distanceText = sourcePointDistance < 0.0 ? "n/a" : sourcePointDistance.ToString("0.###");
                int keptCount = createdCount - erasedCount;
                ed.WriteMessage(
                    $"\nCLV-GIS-DI-EXPLODE complete. createdRaw={createdCount}, erased={erasedCount}, kept={keptCount}, innerMoved={innerCount}, outerMoved={outerCount}, eraseBreakdown[text={erasedTextCount}, curb={erasedCurbCount}, marker={erasedMarkerCount}, tinyCircle={erasedTinyCircleCount}], matchTolerance={matchTolerance:0.###}, sourcePointDistance={distanceText}, sourcePoint={(string.IsNullOrWhiteSpace(sourcePointHandle) ? "not found" : sourcePointHandle)}, outerCountEligible={outerHandles.Count}, outer={(string.IsNullOrWhiteSpace(sampleOuterHandle) ? "not found" : sampleOuterHandle)}, odCopyQueued={(odQueued ? "yes" : "no")}, plotMode={plotMode}, innerLayerState={innerLayerState}, outerLayerState={outerLayerState}, xDataCleaned={xDataCleaned}."
                );
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-DI-EXPLODE failed: {ex.Message}");
            }
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
                // fall through
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
                    return new Point3d(
                        (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                        (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5,
                        (ext.MinPoint.Z + ext.MaxPoint.Z) * 0.5);
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

        private static bool ShouldEraseExplodedEntity(Entity ent, out string reason)
        {
            if (ent is AttributeReference || ent is DBText || ent is MText)
            {
                reason = "text";
                return true;
            }

            string layerName = ent.Layer ?? string.Empty;
            if (layerName.IndexOf("CURB", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                reason = "curb";
                return true;
            }

            if (string.Equals(layerName, "C-DETL-MARK", StringComparison.OrdinalIgnoreCase))
            {
                reason = "marker";
                return true;
            }

            if (ent is Circle circle && circle.Radius <= 0.10)
            {
                reason = "tiny-circle";
                return true;
            }

            reason = string.Empty;
            return false;
        }


        private static string ForceAndDescribeManagedLayer(AcDocument doc, string layerName)
        {
            try
            {
                using (doc.LockDocument())
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    Editor ed = doc.Editor;
                    LayerStandards.TryEnsureManagedGisLayer(doc.Database, tr, ed, layerName);

                    LayerTable lt = (LayerTable)tr.GetObject(doc.Database.LayerTableId, OpenMode.ForRead);
                    if (!lt.Has(layerName))
                    {
                        tr.Commit();
                        return "missing";
                    }

                    LayerTableRecord ltr = (LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForRead);
                    string plotStyleName = GetPlotStyleNameSafe(tr, ltr);
                    string linetypeName = GetLinetypeNameSafe(tr, ltr);
                    short aci = (short)ltr.Color.ColorIndex;
                    tr.Commit();
                    return $"{layerName}[aci={aci},lt={linetypeName},ps={plotStyleName}]";
                }
            }
            catch (System.Exception ex)
            {
                return $"{layerName}[verify-failed:{ex.Message}]";
            }
        }

        private static string GetPlotStyleNameSafe(Transaction tr, LayerTableRecord ltr)
        {
            try
            {
                return string.IsNullOrWhiteSpace(ltr.PlotStyleName) ? "unavailable" : ltr.PlotStyleName;
            }
            catch
            {
                return "unavailable";
            }
        }

        private static string GetLinetypeNameSafe(Transaction tr, LayerTableRecord ltr)
        {
            try
            {
                if (ltr.LinetypeObjectId.IsNull)
                    return "null";

                if (tr.GetObject(ltr.LinetypeObjectId, OpenMode.ForRead, false) is LinetypeTableRecord ltrType)
                    return ltrType.Name ?? "unnamed";
            }
            catch
            {
            }

            return "unavailable";
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

        private static bool MatchesAnyToken(string layerName, IEnumerable<string> tokens)
        {
            foreach (string token in tokens)
            {
                if (layerName.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static ObjectId FindStructurePointAtCenter(Transaction tr, Database db, Point3d target, out double usedTolerance, out double usedDistance)
        {
            usedTolerance = 0.0;
            usedDistance = -1.0;

            ObjectId id = FindBestStructurePoint(tr, db, target, ExactPointTolerance, out usedDistance);
            if (!id.IsNull)
            {
                usedTolerance = ExactPointTolerance;
                return id;
            }

            id = FindBestStructurePoint(tr, db, target, TightPointTolerance, out usedDistance);
            if (!id.IsNull)
            {
                usedTolerance = TightPointTolerance;
                return id;
            }

            id = FindBestStructurePoint(tr, db, target, LoosePointTolerance, out usedDistance);
            if (!id.IsNull)
                usedTolerance = LoosePointTolerance;

            return id;
        }

        private static ObjectId FindBestStructurePoint(Transaction tr, Database db, Point3d target, double tolerance, out double bestDistance)
        {
            bestDistance = double.MaxValue;
            ObjectId bestId = ObjectId.Null;

            if (tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) is not BlockTableRecord space)
                return ObjectId.Null;

            foreach (ObjectId id in space)
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity ent || ent.IsErased)
                    continue;

                if (!string.Equals(ent.Layer, StructuresPointLayer, StringComparison.OrdinalIgnoreCase))
                    continue;

                Point3d? candidatePoint = TryGetEntityPoint(ent);
                if (!candidatePoint.HasValue)
                    continue;

                double dist = Distance2d(target, candidatePoint.Value);
                if (dist <= tolerance && dist < bestDistance)
                {
                    bestDistance = dist;
                    bestId = id;
                }
            }

            if (bestId.IsNull)
                bestDistance = -1.0;

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

        private static List<string> GetEligibleOuterDestinationHandles(Transaction tr, List<ObjectId> createdIds, Point3d center)
        {
            List<(string Handle, double Score)> closedPolylines = new List<(string, double)>();
            List<(string Handle, double Score)> fallbackCurves = new List<(string, double)>();

            foreach (ObjectId id in createdIds)
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity ent || ent.IsErased)
                    continue;

                if (!string.Equals(ent.Layer, TargetOuterLayer, StringComparison.OrdinalIgnoreCase))
                    continue;

                double score = ScoreOuterCandidate(ent, center);
                if (ent is AcPolyline pl && pl.Closed)
                {
                    closedPolylines.Add((ent.Handle.ToString(), score));
                    continue;
                }

                if (ent is Curve)
                    fallbackCurves.Add((ent.Handle.ToString(), score));
            }

            if (closedPolylines.Count > 0)
            {
                return closedPolylines
                    .OrderByDescending(x => x.Score)
                    .Select(x => x.Handle)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return fallbackCurves
                .OrderByDescending(x => x.Score)
                .Select(x => x.Handle)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static double ScoreOuterCandidate(Entity ent, Point3d center)
        {
            Point3d candidateCenter = GetEntityCenter(ent);
            double distPenalty = Distance2d(center, candidateCenter);

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

                return Point3d.Origin;
            }
        }

        private static double Distance2d(Point3d a, Point3d b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        private static void TryFocusDrawingView()
        {
            try
            {
                Type? utilsType = Type.GetType("Autodesk.AutoCAD.Internal.Utils, AcCoreMgd", throwOnError: false)
                    ?? Type.GetType("Autodesk.AutoCAD.Internal.Utils, AcMgd", throwOnError: false);

                var method = utilsType?.GetMethod("SetFocusToDwgView", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                method?.Invoke(null, null);
            }
            catch
            {
                // best effort only
            }
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
                ed.WriteMessage($"\nCLV-GIS-DI-EXPLODE OD helper queue failed: {ex.Message}");
                return false;
            }
        }
    }
}
