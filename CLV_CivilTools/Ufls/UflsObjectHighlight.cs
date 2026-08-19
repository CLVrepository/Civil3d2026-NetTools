using System;
using System.Collections.Generic;

using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using CLV_CivilTools.Shared;

using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace CLV_CivilTools.Ufls
{
    /// <summary>
    /// UFLS object highlight overlay tools.
    ///
    /// These commands create new highlight geometry on the existing UFLS highlight
    /// layers instead of changing source mapping objects. Line/curve objects receive
    /// thin overlay polylines. Plain DBText/MText receive by-layer highlight
    /// backgrounds sent behind the source text. Civil 3D label-like objects receive
    /// tight text-component backgrounds brought to the front because label masks can
    /// otherwise hide the highlight when it is placed behind the label object.
    /// </summary>
    public static class UflsObjectHighlight
    {
        private const double HighlightWidth = 0.01;
        private const double TextHighlightPadding = 0.03;
        private const int CurveApproximationSegments = 64;

        [CommandMethod("UFLS", "UFLS-OBJECT-HIGHLIGHT-RED", CommandFlags.Modal)]
        public static void ObjectHighlightRed()
        {
            RunObjectHighlight(isRed: true);
        }

        [CommandMethod("UFLS", "UFLS-OBJECT-HIGHLIGHT-GREEN", CommandFlags.Modal)]
        public static void ObjectHighlightGreen()
        {
            RunObjectHighlight(isRed: false);
        }

        private static void RunObjectHighlight(bool isRed)
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            string layerName = isRed
                ? LayerStandards.UflsObjectHighlightRedLayerName
                : LayerStandards.UflsObjectHighlightGreenLayerName;

            PromptSelectionOptions pso = new PromptSelectionOptions
            {
                MessageForAdding = isRed
                    ? "\nSelect objects to highlight RED: "
                    : "\nSelect objects to highlight GREEN: ",
                AllowDuplicates = false
            };

            PromptSelectionResult psr = ed.GetSelection(pso);
            if (psr.Status != PromptStatus.OK)
                return;

            int createdCount = 0;
            int skippedCount = 0;

            try
            {
                using Transaction tr = db.TransactionManager.StartTransaction();

                LayerStandards.EnsureUflsObjectHighlightLayer(db, tr, ed, isRed);

                foreach (SelectedObject selected in psr.Value)
                {
                    if (selected == null || selected.ObjectId.IsNull)
                        continue;

                    try
                    {
                        int createdForSelection = TryCreateHighlightEntity(db, tr, ed, selected.ObjectId, layerName, isRed);
                        if (createdForSelection <= 0)
                        {
                            skippedCount++;
                            continue;
                        }

                        createdCount += createdForSelection;
                    }
                    catch (System.Exception itemEx)
                    {
                        skippedCount++;
                        ed.WriteMessage($"\nObject highlight: skipped one selected object: {itemEx.Message}");
                    }
                }

                tr.Commit();

                ed.WriteMessage(
                    "\n{0}: created {1} highlight overlay object{2} on layer {3}{4}.",
                    isRed ? "OBJECT HIGHLIGHT RED" : "OBJECT HIGHLIGHT GREEN",
                    createdCount,
                    createdCount == 1 ? string.Empty : "s",
                    layerName,
                    skippedCount > 0 ? $"; skipped {skippedCount}." : string.Empty);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage(
                    "\n{0} error: {1}",
                    isRed ? "OBJECT HIGHLIGHT RED" : "OBJECT HIGHLIGHT GREEN",
                    ex.Message);
            }
        }

        private static int TryCreateHighlightEntity(
            Database db,
            Transaction tr,
            Editor ed,
            ObjectId sourceId,
            string layerName,
            bool isRed)
        {
            DBObject sourceObject = tr.GetObject(sourceId, OpenMode.ForRead, false);
            if (sourceObject is not AcEntity sourceEntity || sourceEntity.IsErased)
                return 0;

            if (sourceEntity is Viewport)
                return 0;

            BlockTableRecord ownerRecord = GetWritableOwnerRecord(db, tr, sourceEntity);

            List<AcEntity> highlightEntities = CreateHighlightGeometry(sourceEntity);
            if (highlightEntities.Count == 0)
                return 0;

            ObjectIdCollection newIds = new ObjectIdCollection();
            foreach (AcEntity highlightEntity in highlightEntities)
            {
                ApplyHighlightProperties(highlightEntity, layerName, isRed);

                ownerRecord.AppendEntity(highlightEntity);
                tr.AddNewlyCreatedDBObject(highlightEntity, true);
                newIds.Add(highlightEntity.ObjectId);
            }

            if (ShouldMoveHighlightBehindSource(sourceEntity))
                MoveEntitiesToBottom(tr, ownerRecord, newIds, ed);
            else
                MoveEntitiesToTop(tr, ownerRecord, newIds, ed);

            return newIds.Count;
        }

        private static BlockTableRecord GetWritableOwnerRecord(Database db, Transaction tr, AcEntity sourceEntity)
        {
            ObjectId ownerId = sourceEntity.OwnerId;
            if (!ownerId.IsNull && tr.GetObject(ownerId, OpenMode.ForWrite, false) is BlockTableRecord sourceOwner)
                return sourceOwner;

            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            return (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
        }

        private static List<AcEntity> CreateHighlightGeometry(AcEntity sourceEntity)
        {
            if (IsTextLikeOrLabel(sourceEntity))
                return CreateAnnotationHighlightGeometry(sourceEntity);

            AcEntity? singleEntity = null;

            if (sourceEntity is Line line)
                singleEntity = CreatePolylineFromLine(line);
            else if (sourceEntity is Arc arc)
                singleEntity = CreatePolylineFromArc(arc);
            else if (sourceEntity is Circle circle)
                singleEntity = CreatePolylineFromCircle(circle);
            else if (sourceEntity is Polyline polyline)
                singleEntity = CreatePolylineFromPolyline(polyline);
            else if (sourceEntity is Polyline2d polyline2d)
                singleEntity = CreatePolylineFromPolyline2d(polyline2d);
            else if (sourceEntity is Polyline3d polyline3d)
                singleEntity = CreatePolylineFromPolyline3d(polyline3d);
            else if (sourceEntity is Curve curve)
                singleEntity = CreatePolylineFromGenericCurve(curve);
            else
                singleEntity = TryCreateExtentsSolid(sourceEntity);

            return singleEntity == null ? new List<AcEntity>() : new List<AcEntity> { singleEntity };
        }

        private static Polyline CreatePolylineFromLine(Line line)
        {
            Polyline pl = new Polyline(2);
            pl.AddVertexAt(0, new Point2d(line.StartPoint.X, line.StartPoint.Y), 0.0, HighlightWidth, HighlightWidth);
            pl.AddVertexAt(1, new Point2d(line.EndPoint.X, line.EndPoint.Y), 0.0, HighlightWidth, HighlightWidth);
            pl.Elevation = line.StartPoint.Z;
            return pl;
        }

        private static Polyline CreatePolylineFromArc(Arc arc)
        {
            Polyline pl = new Polyline(2);
            double bulge = Math.Tan(arc.TotalAngle / 4.0);
            if (arc.Normal.Z < 0.0)
                bulge = -bulge;

            pl.AddVertexAt(0, new Point2d(arc.StartPoint.X, arc.StartPoint.Y), bulge, HighlightWidth, HighlightWidth);
            pl.AddVertexAt(1, new Point2d(arc.EndPoint.X, arc.EndPoint.Y), 0.0, HighlightWidth, HighlightWidth);
            pl.Elevation = arc.StartPoint.Z;
            return pl;
        }

        private static Polyline CreatePolylineFromCircle(Circle circle)
        {
            Polyline pl = new Polyline(CurveApproximationSegments);
            double step = (Math.PI * 2.0) / CurveApproximationSegments;
            double bulge = Math.Tan(step / 4.0);
            if (circle.Normal.Z < 0.0)
                bulge = -bulge;

            for (int i = 0; i < CurveApproximationSegments; i++)
            {
                double a = i * step;
                Point2d p = new Point2d(
                    circle.Center.X + Math.Cos(a) * circle.Radius,
                    circle.Center.Y + Math.Sin(a) * circle.Radius);
                pl.AddVertexAt(i, p, bulge, HighlightWidth, HighlightWidth);
            }

            pl.Closed = true;
            pl.Elevation = circle.Center.Z;
            return pl;
        }

        private static Polyline CreatePolylineFromPolyline(Polyline source)
        {
            if (source.Clone() is not Polyline pl)
                return CreatePolylineFromGenericCurve(source);

            pl.ConstantWidth = HighlightWidth;
            for (int i = 0; i < pl.NumberOfVertices; i++)
            {
                pl.SetStartWidthAt(i, HighlightWidth);
                pl.SetEndWidthAt(i, HighlightWidth);
            }

            return pl;
        }

        private static Polyline CreatePolylineFromPolyline2d(Polyline2d source)
        {
            return CreatePolylineFromGenericCurve(source);
        }

        private static Polyline CreatePolylineFromPolyline3d(Polyline3d source)
        {
            return CreatePolylineFromGenericCurve(source);
        }

        private static Polyline CreatePolylineFromGenericCurve(Curve curve)
        {
            int segmentCount = Math.Max(2, CurveApproximationSegments);
            Polyline pl = new Polyline(segmentCount + 1);

            double startParam = curve.StartParam;
            double endParam = curve.EndParam;
            double span = endParam - startParam;

            if (Math.Abs(span) < 1.0e-9)
                span = 1.0;

            int lastIndex = 0;
            for (int i = 0; i <= segmentCount; i++)
            {
                double t = i / (double)segmentCount;
                Point3d p;
                try
                {
                    p = curve.GetPointAtParameter(startParam + span * t);
                }
                catch
                {
                    double dist = curve.GetDistanceAtParameter(startParam) +
                                  (curve.GetDistanceAtParameter(endParam) - curve.GetDistanceAtParameter(startParam)) * t;
                    p = curve.GetPointAtDist(dist);
                }

                if (i > 0)
                {
                    Point2d prev = pl.GetPoint2dAt(lastIndex - 1);
                    if (prev.GetDistanceTo(new Point2d(p.X, p.Y)) < 1.0e-8)
                        continue;
                }

                pl.AddVertexAt(lastIndex, new Point2d(p.X, p.Y), 0.0, HighlightWidth, HighlightWidth);
                lastIndex++;
            }

            if (lastIndex < 2)
            {
                Point3d sp = curve.StartPoint;
                Point3d ep = curve.EndPoint;
                pl = new Polyline(2);
                pl.AddVertexAt(0, new Point2d(sp.X, sp.Y), 0.0, HighlightWidth, HighlightWidth);
                pl.AddVertexAt(1, new Point2d(ep.X, ep.Y), 0.0, HighlightWidth, HighlightWidth);
            }

            try
            {
                pl.Elevation = curve.StartPoint.Z;
            }
            catch
            {
                // Leave elevation at default for curves without a usable start point.
            }

            try
            {
                if (curve.Closed)
                    pl.Closed = true;
            }
            catch
            {
                // Some curve types may not expose a usable Closed state.
            }

            return pl;
        }

        private static List<AcEntity> CreateAnnotationHighlightGeometry(AcEntity sourceEntity)
        {
            // DBText/MText can be handled directly with a tight rotated rectangle.
            // Civil 3D labels often report extents for leader/control geometry, and
            // many labels explode to one or more nested block references before the
            // visible DBText/MText is exposed. Always try to explode annotation-like
            // objects first and build one tight rotated background per real text
            // component. Only fall back to the source extents if no text can be found.
            if (sourceEntity is DBText || sourceEntity is MText)
            {
                Solid? textSolid = TryCreateExtentsSolid(sourceEntity);
                return textSolid == null ? new List<AcEntity>() : new List<AcEntity> { textSolid };
            }

            if (TryCreateExplodedTextHighlightSolids(sourceEntity, out List<AcEntity> explodedTextSolids))
                return explodedTextSolids;

            Solid? extentsSolid = TryCreateExtentsSolid(sourceEntity);
            return extentsSolid == null ? new List<AcEntity>() : new List<AcEntity> { extentsSolid };
        }

        private static bool TryCreateExplodedTextHighlightSolids(AcEntity sourceEntity, out List<AcEntity> solids)
        {
            solids = new List<AcEntity>();

            try
            {
                DBObjectCollection explodedObjects = new DBObjectCollection();
                sourceEntity.Explode(explodedObjects);
                CollectTextHighlightSolidsFromExplodedObjects(explodedObjects, solids, 0);
            }
            catch
            {
                solids.Clear();
                return false;
            }

            return solids.Count > 0;
        }

        private static void CollectTextHighlightSolidsFromExplodedObjects(
            DBObjectCollection explodedObjects,
            List<AcEntity> solids,
            int depth)
        {
            if (depth > 4)
            {
                DisposeExplodedObjects(explodedObjects);
                return;
            }

            foreach (DBObject dbObject in explodedObjects)
            {
                try
                {
                    if (dbObject is not AcEntity entity)
                        continue;

                    if (entity is DBText || entity is MText)
                    {
                        if (TryCreateTextAlignedExtentsSolid(entity, out Solid? solid) && solid != null)
                            solids.Add(solid);
                        continue;
                    }

                    if (entity is BlockReference ||
                        entity is MLeader ||
                        entity is Dimension ||
                        IsCivilLabelLike(entity))
                    {
                        DBObjectCollection nested = new DBObjectCollection();
                        try
                        {
                            entity.Explode(nested);
                            CollectTextHighlightSolidsFromExplodedObjects(nested, solids, depth + 1);
                        }
                        catch
                        {
                            // Fall through to disposal of this exploded object.
                        }
                    }
                }
                finally
                {
                    dbObject.Dispose();
                }
            }
        }

        private static void DisposeExplodedObjects(DBObjectCollection explodedObjects)
        {
            foreach (DBObject dbObject in explodedObjects)
                dbObject.Dispose();
        }

        private static Solid? TryCreateExtentsSolid(AcEntity sourceEntity)
        {
            if (TryCreateTextAlignedExtentsSolid(sourceEntity, out Solid? alignedSolid))
                return alignedSolid;

            return TryCreateAxisAlignedExtentsSolid(sourceEntity);
        }

        private static bool TryCreateTextAlignedExtentsSolid(AcEntity sourceEntity, out Solid? solid)
        {
            solid = null;

            if (!TryGetPlanTextRotationBase(sourceEntity, out double rotation, out Point3d basePoint))
                return false;

            try
            {
                using AcEntity localEntity = (AcEntity)sourceEntity.Clone();
                Matrix3d toLocal = Matrix3d.Rotation(-rotation, Vector3d.ZAxis, basePoint);
                Matrix3d fromLocal = Matrix3d.Rotation(rotation, Vector3d.ZAxis, basePoint);

                localEntity.TransformBy(toLocal);
                Extents3d ext = localEntity.GeometricExtents;

                Point3d min = ext.MinPoint;
                Point3d max = ext.MaxPoint;
                double z = min.Z;

                Point3d p1 = new Point3d(min.X - TextHighlightPadding, min.Y - TextHighlightPadding, z).TransformBy(fromLocal);
                Point3d p2 = new Point3d(max.X + TextHighlightPadding, min.Y - TextHighlightPadding, z).TransformBy(fromLocal);
                Point3d p3 = new Point3d(min.X - TextHighlightPadding, max.Y + TextHighlightPadding, z).TransformBy(fromLocal);
                Point3d p4 = new Point3d(max.X + TextHighlightPadding, max.Y + TextHighlightPadding, z).TransformBy(fromLocal);

                solid = new Solid(p1, p2, p3, p4);
                return true;
            }
            catch
            {
                solid = null;
                return false;
            }
        }

        private static bool TryGetPlanTextRotationBase(AcEntity sourceEntity, out double rotation, out Point3d basePoint)
        {
            if (sourceEntity is DBText dbText)
            {
                rotation = dbText.Rotation;
                basePoint = dbText.Position;
                return true;
            }

            if (sourceEntity is MText mText)
            {
                rotation = mText.Rotation;
                basePoint = mText.Location;
                return true;
            }

            rotation = 0.0;
            basePoint = Point3d.Origin;
            return false;
        }

        private static Solid? TryCreateAxisAlignedExtentsSolid(AcEntity sourceEntity)
        {
            try
            {
                Extents3d ext = sourceEntity.GeometricExtents;
                Point3d min = ext.MinPoint;
                Point3d max = ext.MaxPoint;
                double z = min.Z;

                Point3d p1 = new Point3d(min.X - TextHighlightPadding, min.Y - TextHighlightPadding, z);
                Point3d p2 = new Point3d(max.X + TextHighlightPadding, min.Y - TextHighlightPadding, z);
                Point3d p3 = new Point3d(min.X - TextHighlightPadding, max.Y + TextHighlightPadding, z);
                Point3d p4 = new Point3d(max.X + TextHighlightPadding, max.Y + TextHighlightPadding, z);

                return new Solid(p1, p2, p3, p4);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsTextLikeOrLabel(AcEntity sourceEntity)
        {
            return sourceEntity is DBText ||
                   sourceEntity is MText ||
                   sourceEntity is MLeader ||
                   sourceEntity is Dimension ||
                   sourceEntity is BlockReference ||
                   IsCivilLabelLike(sourceEntity);
        }

        private static bool IsCivilLabelLike(AcEntity sourceEntity)
        {
            string typeName = sourceEntity.GetType().FullName ?? string.Empty;
            return typeName.IndexOf("Label", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   typeName.IndexOf("Aecc", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ShouldMoveHighlightBehindSource(AcEntity sourceEntity)
        {
            // Direct text objects do not normally have Civil 3D label masking, so keep
            // the background behind the text. Civil 3D labels, MLeaders, dimensions,
            // and block-based annotation may include masks that hide background solids;
            // those highlight solids are intentionally brought to the front.
            return sourceEntity is DBText || sourceEntity is MText;
        }

        private static void ApplyHighlightProperties(AcEntity highlightEntity, string layerName, bool isRed)
        {
            highlightEntity.Layer = layerName;
            highlightEntity.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(ColorMethod.ByLayer, 256);
            highlightEntity.Linetype = "ByLayer";
            highlightEntity.LineWeight = LineWeight.ByLayer;
            if (highlightEntity is Polyline pl)
            {
                pl.ConstantWidth = HighlightWidth;
                for (int i = 0; i < pl.NumberOfVertices; i++)
                {
                    pl.SetStartWidthAt(i, HighlightWidth);
                    pl.SetEndWidthAt(i, HighlightWidth);
                }
            }
        }

        private static void MoveEntitiesToTop(
            Transaction tr,
            BlockTableRecord ownerRecord,
            ObjectIdCollection entityIds,
            Editor ed)
        {
            if (entityIds.Count == 0)
                return;

            try
            {
                DrawOrderTable drawOrder = (DrawOrderTable)tr.GetObject(ownerRecord.DrawOrderTableId, OpenMode.ForWrite);
                drawOrder.MoveToTop(entityIds);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nObject highlight: overlay was created, but draw-order move-to-top failed: {ex.Message}");
            }
        }

        private static void MoveEntitiesToBottom(
            Transaction tr,
            BlockTableRecord ownerRecord,
            ObjectIdCollection entityIds,
            Editor ed)
        {
            if (entityIds.Count == 0)
                return;

            try
            {
                DrawOrderTable drawOrder = (DrawOrderTable)tr.GetObject(ownerRecord.DrawOrderTableId, OpenMode.ForWrite);
                drawOrder.MoveToBottom(entityIds);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nObject highlight: overlay was created, but draw-order move-to-bottom failed: {ex.Message}");
            }
        }
    }
}
