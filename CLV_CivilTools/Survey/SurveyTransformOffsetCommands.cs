using System;
using System.Collections.Generic;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Survey
{
    public static class SurveyTransformOffsetCommands
    {
        private const string TemporaryConstructionLayerName = "V-CONS-LINE-TEMP";

        [CommandMethod("SURVEY-TRANSFORM-CONTROL", CommandFlags.Modal | CommandFlags.UsePickSet)]
        [CommandMethod("TRANSFORMCONTROL", CommandFlags.Modal | CommandFlags.UsePickSet)]
        public static void TransformToControl()
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                PromptSelectionResult selection = ed.SelectImplied();
                if (selection.Status != PromptStatus.OK || selection.Value.Count == 0)
                {
                    selection = ed.GetSelection(new PromptSelectionOptions
                    {
                        MessageForAdding = "\nSelect object, block, xref, or grouped objects to transform: "
                    });
                }

                if (selection.Status != PromptStatus.OK || selection.Value.Count == 0)
                    return;

                if (!TryGetNestedCircleCenter(ed, db, "\nSelect SOURCE control circle: ", out Point3d sourcePoint))
                    return;

                if (!TryGetNestedLineDirection(ed, db, "\nSelect SOURCE rotation line: ", out Vector3d sourceDirection))
                    return;

                if (!TryGetNestedCircleCenter(ed, db, "\nSelect DESTINATION control circle: ", out Point3d destinationPoint))
                    return;

                if (!TryGetNestedLineDirection(ed, db, "\nSelect DESTINATION rotation line (drawing or xref): ", out Vector3d destinationDirection))
                    return;

                double sourceAngle = Math.Atan2(sourceDirection.Y, sourceDirection.X);
                double destinationAngle = Math.Atan2(destinationDirection.Y, destinationDirection.X);
                double rotation = NormalizeAngle(destinationAngle - sourceAngle);

                Matrix3d displacement = Matrix3d.Displacement(destinationPoint - sourcePoint);
                Matrix3d rotationMatrix = Matrix3d.Rotation(rotation, Vector3d.ZAxis, destinationPoint);

                using DocumentLock docLock = doc.LockDocument();
                using Transaction tr = db.TransactionManager.StartTransaction();

                List<Entity> transformedEntities = new List<Entity>();
                Dictionary<Entity, Autodesk.AutoCAD.Colors.Transparency> originalTransparency =
                    new Dictionary<Entity, Autodesk.AutoCAD.Colors.Transparency>();

                foreach (SelectedObject selected in selection.Value)
                {
                    if (selected == null || selected.ObjectId.IsNull)
                        continue;

                    if (tr.GetObject(selected.ObjectId, OpenMode.ForWrite, false) is not Entity entity)
                        continue;

                    originalTransparency[entity] = entity.Transparency;
                    entity.TransformBy(displacement);
                    entity.TransformBy(rotationMatrix);
                    entity.Transparency = new Autodesk.AutoCAD.Colors.Transparency(110);
                    transformedEntities.Add(entity);
                }

                if (transformedEntities.Count == 0)
                {
                    ed.WriteMessage("\nTRANSFORM TO CONTROL: no transformable objects were selected.");
                    return;
                }

                FlushPreview(db, ed);
                ed.WriteMessage(
                    $"\nTransform preview shown faded at destination. Rotation = {RadiansToDegrees(rotation):0.####} degrees.");

                bool flipped = false;
                while (true)
                {
                    PromptKeywordOptions previewOptions = new PromptKeywordOptions(
                        "\nPress Enter to accept, or choose [Flip/Cancel] <Accept>: ",
                        "Flip Cancel")
                    {
                        AllowNone = true
                    };

                    PromptResult previewResult = ed.GetKeywords(previewOptions);
                    if (previewResult.Status == PromptStatus.Cancel ||
                        (previewResult.Status == PromptStatus.OK &&
                         string.Equals(previewResult.StringResult, "Cancel", StringComparison.OrdinalIgnoreCase)))
                    {
                        ed.WriteMessage("\nTRANSFORM TO CONTROL canceled; original geometry restored.");
                        return;
                    }

                    if (previewResult.Status == PromptStatus.None)
                        break;

                    if (previewResult.Status == PromptStatus.OK &&
                        string.Equals(previewResult.StringResult, "Flip", StringComparison.OrdinalIgnoreCase))
                    {
                        Matrix3d flipMatrix = Matrix3d.Rotation(Math.PI, Vector3d.ZAxis, destinationPoint);
                        foreach (Entity entity in transformedEntities)
                            entity.TransformBy(flipMatrix);

                        flipped = !flipped;
                        FlushPreview(db, ed);
                        ed.WriteMessage(flipped
                            ? "\n180-degree flipped preview shown. Press Enter to accept or choose Flip again to return."
                            : "\nOriginal-direction preview restored. Press Enter to accept or choose Flip again.");
                    }
                }

                foreach (Entity entity in transformedEntities)
                    entity.Transparency = originalTransparency[entity];

                tr.Commit();
                ed.Regen();

                double finalRotation = NormalizeAngle(rotation + (flipped ? Math.PI : 0.0));
                ed.WriteMessage(
                    $"\nTRANSFORM TO CONTROL: transformed {transformedEntities.Count} object(s); " +
                    $"move = {sourcePoint.DistanceTo(destinationPoint):0.###}, rotation = {RadiansToDegrees(finalRotation):0.####} degrees.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nTRANSFORM TO CONTROL error: {ex.Message}");
            }
            finally
            {
                ed.SetImpliedSelection(Array.Empty<ObjectId>());
                ed.Regen();
            }
        }

        [CommandMethod("SURVEY-OFFSET-TEMP", CommandFlags.Modal)]
        [CommandMethod("OFFSETTEMP", CommandFlags.Modal)]
        public static void OffsetToTemporaryConstructionLayer()
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                PromptDoubleOptions distanceOptions = new PromptDoubleOptions("\nSpecify offset distance: ")
                {
                    AllowNegative = false,
                    AllowZero = false,
                    DefaultValue = Math.Abs(Convert.ToDouble(AcApp.GetSystemVariable("OFFSETDIST"))),
                    UseDefaultValue = true
                };
                PromptDoubleResult distanceResult = ed.GetDouble(distanceOptions);
                if (distanceResult.Status != PromptStatus.OK)
                    return;

                double distance = distanceResult.Value;
                AcApp.SetSystemVariable("OFFSETDIST", distance);

                int totalCreated = 0;
                while (true)
                {
                    PromptEntityOptions entityOptions = new PromptEntityOptions(
                        "\nSelect object to offset or press Enter to finish: ")
                    {
                        AllowNone = true
                    };
                    entityOptions.SetRejectMessage("\nSelect a curve that supports offsetting.");
                    entityOptions.AddAllowedClass(typeof(Curve), false);

                    PromptEntityResult entityResult = ed.GetEntity(entityOptions);
                    if (entityResult.Status == PromptStatus.None)
                        break;
                    if (entityResult.Status != PromptStatus.OK)
                        return;

                    PromptPointOptions sideOptions = new PromptPointOptions("\nSpecify point on side to offset: ");
                    PromptPointResult sideResult = ed.GetPoint(sideOptions);
                    if (sideResult.Status != PromptStatus.OK)
                        return;

                    using DocumentLock docLock = doc.LockDocument();
                    using Transaction tr = db.TransactionManager.StartTransaction();

                    EnsureTemporaryConstructionLayer(db, tr);

                    Curve source = (Curve)tr.GetObject(entityResult.ObjectId, OpenMode.ForRead);
                    DBObjectCollection offsets = ChooseOffsetSide(source, distance, sideResult.Value);
                    if (offsets.Count == 0)
                    {
                        ed.WriteMessage("\nThe selected object did not produce offset geometry.");
                        continue;
                    }

                    BlockTableRecord owner = (BlockTableRecord)tr.GetObject(source.OwnerId, OpenMode.ForWrite);
                    int created = 0;
                    foreach (DBObject obj in offsets)
                    {
                        if (obj is not Entity entity)
                        {
                            obj.Dispose();
                            continue;
                        }

                        entity.Layer = TemporaryConstructionLayerName;
                        owner.AppendEntity(entity);
                        tr.AddNewlyCreatedDBObject(entity, true);
                        created++;
                    }

                    tr.Commit();
                    totalCreated += created;
                    ed.WriteMessage(
                        $"\nOFFSET TEMP: created {created} object(s) on {TemporaryConstructionLayerName}. Select another object or press Enter to finish.");
                }

                ed.WriteMessage($"\nOFFSET TEMP complete: created {totalCreated} object(s) on {TemporaryConstructionLayerName}.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nOFFSET TEMP error: {ex.Message}");
            }
        }

        private static DBObjectCollection ChooseOffsetSide(Curve source, double distance, Point3d sidePoint)
        {
            DBObjectCollection positive = source.GetOffsetCurves(distance);
            DBObjectCollection negative = source.GetOffsetCurves(-distance);

            double positiveDistance = MinimumDistanceToCurves(positive, sidePoint);
            double negativeDistance = MinimumDistanceToCurves(negative, sidePoint);

            DBObjectCollection keep = positiveDistance <= negativeDistance ? positive : negative;
            DBObjectCollection discard = ReferenceEquals(keep, positive) ? negative : positive;
            foreach (DBObject obj in discard)
                obj.Dispose();

            return keep;
        }

        private static double MinimumDistanceToCurves(DBObjectCollection curves, Point3d point)
        {
            double minimum = double.MaxValue;
            foreach (DBObject obj in curves)
            {
                if (obj is not Curve curve)
                    continue;

                try
                {
                    Point3d closest = curve.GetClosestPointTo(point, false);
                    minimum = Math.Min(minimum, closest.DistanceTo(point));
                }
                catch
                {
                    // Ignore an individual unusable result and evaluate the remainder.
                }
            }

            return minimum;
        }

        private static void EnsureTemporaryConstructionLayer(Database db, Transaction tr)
        {
            LayerTable layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (layerTable.Has(TemporaryConstructionLayerName))
                return;

            layerTable.UpgradeOpen();
            LayerTableRecord layer = new LayerTableRecord
            {
                Name = TemporaryConstructionLayerName,
                IsPlottable = true,
                Description = "Temporary construction linework"
            };
            layerTable.Add(layer);
            tr.AddNewlyCreatedDBObject(layer, true);
        }

        private static bool TryGetNestedCircleCenter(Editor ed, Database db, string message, out Point3d center)
        {
            center = Point3d.Origin;
            PromptNestedEntityResult result = ed.GetNestedEntity(message);
            if (result.Status != PromptStatus.OK)
                return false;

            using Transaction tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(result.ObjectId, OpenMode.ForRead) is not Circle circle)
            {
                ed.WriteMessage("\nThe selected control object must be a circle.");
                return false;
            }

            center = circle.Center.TransformBy(GetNestedTransform(tr, result.GetContainers()));
            tr.Commit();
            return true;
        }

        private static bool TryGetNestedLineDirection(Editor ed, Database db, string message, out Vector3d direction)
        {
            direction = Vector3d.XAxis;
            PromptNestedEntityResult result = ed.GetNestedEntity(message);
            if (result.Status != PromptStatus.OK)
                return false;

            using Transaction tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(result.ObjectId, OpenMode.ForRead) is not Line line)
            {
                ed.WriteMessage("\nThe selected rotation object must be a line.");
                return false;
            }

            Matrix3d transform = GetNestedTransform(tr, result.GetContainers());
            Point3d start = line.StartPoint.TransformBy(transform);
            Point3d end = line.EndPoint.TransformBy(transform);
            direction = end - start;
            if (direction.Length < Tolerance.Global.EqualPoint)
            {
                ed.WriteMessage("\nThe selected rotation line has no usable length.");
                return false;
            }

            direction = direction.GetNormal();
            tr.Commit();
            return true;
        }

        private static void FlushPreview(Database db, Editor ed)
        {
            db.TransactionManager.QueueForGraphicsFlush();
            ed.UpdateScreen();
        }

        private static Matrix3d GetNestedTransform(Transaction tr, ObjectId[] containers)
        {
            Matrix3d transform = Matrix3d.Identity;
            for (int i = containers.Length - 1; i >= 0; i--)
            {
                if (tr.GetObject(containers[i], OpenMode.ForRead) is BlockReference blockReference)
                    transform = transform.PreMultiplyBy(blockReference.BlockTransform);
            }

            return transform;
        }

        private static double NormalizeAngle(double angle)
        {
            while (angle > Math.PI)
                angle -= Math.PI * 2.0;
            while (angle <= -Math.PI)
                angle += Math.PI * 2.0;
            return angle;
        }

        private static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;
    }
}
