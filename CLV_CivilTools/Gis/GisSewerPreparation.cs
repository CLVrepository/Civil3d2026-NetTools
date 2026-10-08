using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CLV_CivilTools.Shared;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Gis
{
    /// <summary>
    /// Synchronous sewer-only preparation. Planning precedes all geometry writes;
    /// structure materialization, pipe clipping, OD proof and owned-source cleanup
    /// share one transaction. Legacy standalone and Storm commands are unchanged.
    /// </summary>
    internal static class GisSewerPreparation
    {
        private const string ArchiveKey = "CLV_SEWER_PREP_V1";
        private const string PipeLayer = "C-SSWR-PIPE-E";
        private const string CenterLayer = "C-SSWR-PIPE-CNTR-E";
        private const string OuterLayer = "C-SSWR-STRC-E";
        private const string InnerLayer = "C-SSWR-STRC-INNR";
        private const double Tolerance = 1e-8;

        private sealed record Segment(Point3d Start, Point3d End);
        private sealed record PipePlan(ObjectId Id, GisImportCommands.SewerPipeData Data,
            string OriginalSignature, Segment Center, Segment? Positive, Segment? Negative);
        private sealed record Output(ObjectId Id, string Role, string Name, string Shape,
            GisImportCommands.ObjectDataFingerprintState OdState, string OdFingerprint);
        private sealed record StructureArchive(string Handle, string Name, string Part, Point3d Position,
            string BlockHandle, string MarkerHandle, ObjectId OuterId, string OriginalBlockState);
        private sealed record BatchArchive(IReadOnlyList<Output> Outputs,
            IReadOnlyList<(ObjectId Id, string Name, string Geometry)> OriginalPipes,
            IReadOnlyList<StructureArchive> Structures);

        internal static bool Run(Document document)
        {
            if (document != AcadApp.DocumentManager.MdiActiveDocument)
                throw new InvalidOperationException("Sewer preparation requires the active drawing.");
            int structures = 0, pipes = 0, walls = 0, singleLines = 0, markers = 0;
            var retainedPoints = new HashSet<ObjectId>();
            bool committed = false;
            try
            {
                using (document.LockDocument())
                using (Transaction transaction = document.Database.TransactionManager.StartTransaction())
                {
                    Database db = document.Database;
                    if (!SewerPreparationRules.IsFeetInsertionUnits((int)db.Insunits))
                        throw new InvalidOperationException($"Sewer InsideDiameter is in feet, but drawing insertion units are {db.Insunits}. Confirm Feet or US Survey Feet before preparation; no unit conversion was applied.");
                    var model = (BlockTableRecord)transaction.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                    if (!string.Equals(model.Name, BlockTableRecord.ModelSpace, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Sewer preparation requires model space.");

                    BatchArchive? prior = ReadArchive(db, transaction);
                    if (prior != null)
                    {
                        VerifyArchive(db, transaction, model.ObjectId, prior);
                        RequireNoNewInputs(model, transaction, prior.Outputs.Select(output => output.Id).ToHashSet());
                        document.Editor.WriteMessage($"\nCLV-GIS-SSWR-GIS: existing managed sewer results verified ({prior.Outputs.Count} owned entities). No duplicate offsets or cleanup were performed.");
                        transaction.Commit();
                        return true;
                    }

                    document.Editor.WriteMessage("\nCLV-GIS-SSWR-GIS 2026.10.08-S1: checking sewer OD, evaluated manhole circles and pipe connections...");
                    SewerManholeBatch manholes = GisSewerManholePlanner.Plan(db, transaction);
                    var reviews = new List<string>(manholes.Reviews);
                    IReadOnlyList<PipePlan> plannedPipes = PlanPipes(db, transaction, model, manholes.Structures, reviews);
                    foreach (SewerManholePlan manhole in manholes.Structures.Where(item => !item.IsNullEnd))
                    {
                        try
                        {
                            RequireUnlocked(transaction, manhole.SourceId);
                            RequireUnlocked(transaction, manhole.BlockId);
                            if (!manhole.MarkerId.IsNull) RequireUnlocked(transaction, manhole.MarkerId);
                            var point = (Entity)transaction.GetObject(manhole.SourceId, OpenMode.ForRead);
                            using ResultBuffer? extra = point.XData;
                            if ((extra?.AsArray().Length ?? 0) != 0 || !point.ExtensionDictionary.IsNull)
                                retainedPoints.Add(manhole.SourceId);
                        }
                        catch (System.Exception error) { reviews.Add($"Structure {manhole.Name}, handle {manhole.SourceId.Handle}: {error.Message}"); }
                    }
                    RequireNoUntrackedOutputs(model, transaction, reviews);
                    if (reviews.Count > 0)
                    {
                        foreach (string review in reviews.Distinct()) document.Editor.WriteMessage("\nSEWER REVIEW: {0}", review);
                        document.Editor.WriteMessage("\nCLV-GIS-SSWR-GIS: no sewer geometry or source cleanup was applied.");
                        return false;
                    }
                    if (plannedPipes.Count == 0 && manholes.Structures.All(item => item.IsNullEnd))
                    {
                        document.Editor.WriteMessage("\nCLV-GIS-SSWR-GIS: no supported imported sewer structures or pipes were found.");
                        return true;
                    }
                    document.Editor.WriteMessage("\nSEWER: preflight passed for {0} manholes and {1} straight pipes; preparing geometry and verifying OD...",
                        manholes.Structures.Count(item => !item.IsNullEnd), plannedPipes.Count);

                    foreach ((string name, string linetype) in new[]
                    {
                        (PipeLayer, "HIDDEN2"), (CenterLayer, "CENTER2"),
                        (OuterLayer, "HIDDEN3"), (InnerLayer, "HIDDEN4")
                    }) EnsureTargetLayer(db, transaction, document, name, linetype);

                    var outputIds = new List<(ObjectId Id, string Role, string Name)>();
                    var structureArchive = new List<StructureArchive>();
                    foreach (SewerManholePlan manhole in manholes.Structures)
                    {
                        if (manhole.IsNullEnd)
                        {
                            outputIds.Add((manhole.SourceId, "OpenNullEnd", manhole.Name));
                            continue;
                        }
                        RequireSourceManholeUnchanged(transaction, manhole);
                        Circle outer = AppendCircle(model, transaction, manhole.Outer!, OuterLayer);
                        CopyOd(manhole.SourceId, outer.ObjectId, manhole.SourceOdFingerprint);
                        outputIds.Add((outer.ObjectId, "ManholeOuter", manhole.Name));
                        if (retainedPoints.Contains(manhole.SourceId))
                            outputIds.Add((manhole.SourceId, "RetainedSourcePoint", manhole.Name));
                        foreach (SewerManholeCircle inner in manhole.Inner)
                        {
                            Circle circle = AppendCircle(model, transaction, inner, InnerLayer);
                            outputIds.Add((circle.ObjectId, "ManholeInner", manhole.Name));
                        }
                        structureArchive.Add(new StructureArchive(manhole.SourceId.Handle.ToString(), manhole.Name,
                            manhole.PartSizeName, manhole.SourcePosition, manhole.BlockId.Handle.ToString(),
                            manhole.MarkerId.IsNull ? string.Empty : manhole.MarkerId.Handle.ToString(),
                            outer.ObjectId, manhole.BlockStateSignature));
                    }

                    foreach (PipePlan pipe in plannedPipes)
                    {
                        var source = (Line)transaction.GetObject(pipe.Id, OpenMode.ForRead);
                        if (GisNewDrawingClone.ReadPrimitiveSignature(source, transaction) != pipe.OriginalSignature)
                            throw new InvalidOperationException($"Pipe {pipe.Id.Handle} changed after preflight.");
                        RequireOd(pipe.Id, GisImportCommands.ObjectDataFingerprintState.Present, pipe.Data.ObjectDataFingerprint);
                        if (!source.IsWriteEnabled) source.UpgradeOpen();
                        source.StartPoint = pipe.Center.Start;
                        source.EndPoint = pipe.Center.End;
                        source.Layer = pipe.Positive == null ? PipeLayer : CenterLayer;
                        VerifySegment(source, pipe.Center);
                        RequireOd(pipe.Id, GisImportCommands.ObjectDataFingerprintState.Present, pipe.Data.ObjectDataFingerprint);
                        outputIds.Add((pipe.Id, pipe.Positive == null ? "SinglePipe" : "PipeCenter", pipe.Data.Name));
                        if (pipe.Positive != null)
                        {
                            Line positive = AppendWall(model, transaction, source, pipe.Positive);
                            Line negative = AppendWall(model, transaction, source, pipe.Negative!);
                            CopyOd(source.ObjectId, positive.ObjectId, pipe.Data.ObjectDataFingerprint);
                            CopyOd(source.ObjectId, negative.ObjectId, pipe.Data.ObjectDataFingerprint);
                            outputIds.Add((positive.ObjectId, "PositiveWall", pipe.Data.Name));
                            outputIds.Add((negative.ObjectId, "NegativeWall", pipe.Data.Name));
                            walls += 2;
                        }
                        else singleLines++;
                        source.RecordGraphicsModified(true);
                    }

                    // All native data transfers are read back before any source
                    // block, point or owned marker is retired.
                    foreach (SewerManholePlan manhole in manholes.Structures.Where(item => !item.IsNullEnd))
                    {
                        RequireSourceManholeUnchanged(transaction, manhole);
                        ObjectId outer = structureArchive.Single(item => item.Handle == manhole.SourceId.Handle.ToString()).OuterId;
                        RequireOd(outer, GisImportCommands.ObjectDataFingerprintState.Present, manhole.SourceOdFingerprint);
                    }
                    var outputs = outputIds.Select(item => CaptureOutput(transaction, item.Id, item.Role, item.Name)).ToArray();
                    var archive = new BatchArchive(Array.AsReadOnly(outputs), Array.AsReadOnly(plannedPipes.Select(pipe =>
                        (pipe.Id, pipe.Data.Name, pipe.OriginalSignature)).ToArray()), Array.AsReadOnly(structureArchive.ToArray()));
                    WriteArchive(db, transaction, archive);
                    BatchArchive stored = ReadArchive(db, transaction)
                        ?? throw new InvalidOperationException("Sewer ownership archive did not read back.");
                    RequireSameArchive(archive, stored);
                    VerifyArchive(db, transaction, model.ObjectId, stored);

                    foreach (SewerManholePlan manhole in manholes.Structures.Where(item => !item.IsNullEnd))
                    {
                        ((Entity)transaction.GetObject(manhole.BlockId, OpenMode.ForWrite)).Erase(true);
                        if (!retainedPoints.Contains(manhole.SourceId))
                            ((Entity)transaction.GetObject(manhole.SourceId, OpenMode.ForWrite)).Erase(true);
                        if (!manhole.MarkerId.IsNull)
                        {
                            ((Entity)transaction.GetObject(manhole.MarkerId, OpenMode.ForWrite)).Erase(true);
                            markers++;
                        }
                        structures++;
                    }
                    foreach (var output in outputIds)
                        if (output.Role != "OpenNullEnd" && output.Role != "RetainedSourcePoint")
                            ((Entity)transaction.GetObject(output.Id, OpenMode.ForWrite)).RecordGraphicsModified(true);
                    VerifyArchive(db, transaction, model.ObjectId, stored);
                    transaction.TransactionManager.QueueForGraphicsFlush();
                    transaction.Commit();
                    committed = true;
                    pipes = plannedPipes.Count;
                }
                try { document.Editor.Regen(); document.Editor.UpdateScreen(); }
                catch (System.Exception error) { document.Editor.WriteMessage("\nSEWER DISPLAY WARNING: {0}", error.Message); }
                document.Editor.WriteMessage($"\nCLV-GIS-SSWR-GIS: verified manholes={structures}, pipes={pipes}, under-12-inch single lines={singleLines}, offset walls={walls}, owned markers removed={markers}. All sewer pipe linework was clipped to the verified outer circles. Object Data and ownership were read back; no broad cleanup or LISP command was queued.");
                if (retainedPoints.Count > 0)
                    document.Editor.WriteMessage("\nRetained imported points with additional XData/extension data: {0}. Their complete native OD is also on the new outer circles; other instance data was left on the points.",
                        string.Join(", ", retainedPoints.Select(id => id.Handle.ToString())));
                return true;
            }
            catch (System.Exception error)
            {
                document.Editor.WriteMessage(committed
                    ? "\nCLV-GIS-SSWR-GIS: geometry/OD was committed, but a later display/reporting operation failed: {0}"
                    : "\nCLV-GIS-SSWR-GIS stopped; the managed transaction was not committed: {0}", error.ToString());
                return false;
            }
        }

        private static IReadOnlyList<PipePlan> PlanPipes(Database db, Transaction tr, BlockTableRecord model,
            IReadOnlyList<SewerManholePlan> structures, List<string> reviews)
        {
            var result = new List<PipePlan>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var duplicateStructures = structures.GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() != 1).ToArray();
            if (duplicateStructures.Length > 0)
            {
                reviews.Add("Imported sewer structure names are not unique.");
                return result;
            }
            var byName = structures.ToDictionary(item => item.Name, StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId id in model.Cast<ObjectId>().ToArray())
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity entity || entity.IsErased || !IsRawPipeLayer(entity.Layer)) continue;
                try
                {
                    if (IsExcludedStormPipe(id, entity.Layer)) continue;
                    if (!GisImportCommands.TryReadSewerPipeData(id, out var data, out string detail) || data == null)
                        throw new InvalidOperationException(detail);
                    if (!names.Add(data.Name)) throw new InvalidOperationException("Duplicate sewer pipe Name.");
                    if (entity.GetType() != typeof(Line))
                        throw new InvalidOperationException("Only straight LINE input is supported by this sewer stage; curves/polylines are retained without conversion.");
                    var line = (Line)entity;
                    RequirePlanarLine(line);
                    RequireUnlocked(tr, id);
                    if (!byName.TryGetValue(data.StructureStart, out SewerManholePlan? first) ||
                        !byName.TryGetValue(data.StructureEnd, out SewerManholePlan? last))
                        throw new InvalidOperationException("StructureStart/StructureEnd does not resolve to a unique verified sewer structure or explicit null end.");
                    bool forward = NearXY(line.StartPoint, first.SourcePosition) && NearXY(line.EndPoint, last.SourcePosition);
                    bool reverse = NearXY(line.StartPoint, last.SourcePosition) && NearXY(line.EndPoint, first.SourcePosition);
                    if (forward == reverse) throw new InvalidOperationException("Pipe endpoints do not establish one unambiguous connection orientation to their OD structures.");
                    SewerManholePlan start = forward ? first : last, end = forward ? last : first;
                    if (Math.Abs(start.SourcePosition.Z - line.StartPoint.Z) > Tolerance ||
                        Math.Abs(end.SourcePosition.Z - line.EndPoint.Z) > Tolerance)
                        throw new InvalidOperationException("Pipe and imported endpoint points have different elevations; no flattening was applied.");
                    SewerCircle? startCircle = Boundary(start, line.StartPoint.Z), endCircle = Boundary(end, line.StartPoint.Z);
                    Segment center = Clip(line.StartPoint, line.EndPoint, startCircle, endCircle);
                    Segment? positive = null, negative = null;
                    SewerPipeSizeKind size = SewerPreparationRules.ClassifyDiameter(data.DiameterFeet);
                    if (size == SewerPipeSizeKind.Invalid) throw new InvalidOperationException("InsideDiameter must be positive finite feet.");
                    if (size == SewerPipeSizeKind.CenterAndWalls)
                    {
                        Vector3d direction = line.EndPoint - line.StartPoint;
                        double length = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
                        if (!double.IsFinite(length) || length <= Tolerance)
                            throw new InvalidOperationException("The pipe direction cannot safely represent a wall offset.");
                        var shift = new Vector3d(-direction.Y / length, direction.X / length, 0) * (data.DiameterFeet / 2.0);
                        if (!double.IsFinite(shift.X) || !double.IsFinite(shift.Y) || !double.IsFinite(shift.Length) || shift.Length <= Tolerance)
                            throw new InvalidOperationException("The pipe wall offset cannot be represented safely.");
                        positive = Clip(Offset(line.StartPoint, shift), Offset(line.EndPoint, shift), startCircle, endCircle);
                        negative = Clip(Offset(line.StartPoint, -shift), Offset(line.EndPoint, -shift), startCircle, endCircle);
                    }
                    result.Add(new PipePlan(id, data, GisNewDrawingClone.ReadPrimitiveSignature(line, tr), center, positive, negative));
                }
                catch (System.Exception error) { reviews.Add($"Pipe handle {id.Handle}: {error.Message}"); }
            }
            return Array.AsReadOnly(result.ToArray());
        }

        private static bool IsRawPipeLayer(string layer) => string.Equals(layer, "Pipes", StringComparison.OrdinalIgnoreCase) ||
            ((layer.StartsWith("GIS-", StringComparison.OrdinalIgnoreCase) || layer.StartsWith("V-SURV-", StringComparison.OrdinalIgnoreCase)) &&
                layer.Contains("PIPE", StringComparison.OrdinalIgnoreCase));
        private static bool IsExcludedStormPipe(ObjectId id, string layer)
        {
            if (!GisImportCommands.TryReadPipeUtilityEvidence(id, out var tables, out var names, out string detail))
                throw new InvalidOperationException(detail);
            SewerPipeUtilityKind kind = SewerPreparationRules.ClassifyUtility(layer, tables, names);
            if (kind == SewerPipeUtilityKind.Review)
                throw new InvalidOperationException("Invalid or conflicting storm/sewer utility evidence; no utility was inferred.");
            return kind == SewerPipeUtilityKind.ExcludedStorm;
        }
        private static bool NearXY(Point3d a, Point3d b)
            => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)) <= StormStructureMatching.MatchTolerance;
        private static Point3d Offset(Point3d point, Vector3d shift)
        {
            Point3d result = point + shift;
            double error = (result - point - shift).Length;
            if (!Finite(result) || !double.IsFinite(error) || error > Tolerance)
                throw new InvalidOperationException("Survey coordinates cannot represent the required wall offset within tolerance.");
            return result;
        }
        private static SewerCircle? Boundary(SewerManholePlan structure, double elevation)
        {
            if (structure.IsNullEnd) return null;
            SewerManholeCircle circle = structure.Outer ?? throw new InvalidOperationException("A connected manhole has no verified outer circle.");
            if (Math.Abs(circle.Center.Z - elevation) > Tolerance)
                throw new InvalidOperationException($"Pipe and {structure.Name} outer circle are at different elevations; no flattening was applied.");
            return new SewerCircle(structure.Name, circle.Center.X, circle.Center.Y, circle.Radius);
        }
        private static Segment Clip(Point3d start, Point3d end, SewerCircle? startCircle, SewerCircle? endCircle)
        {
            SewerClipDecision decision = SewerCircleTrim.Plan(new SewerPoint2(start.X, start.Y), new SewerPoint2(end.X, end.Y), startCircle, endCircle);
            if (decision.Kind == SewerClipKind.Review) throw new InvalidOperationException(decision.Reason);
            Vector3d delta = end - start;
            return new Segment(decision.StartParameter == 0 ? start : start + delta * decision.StartParameter,
                decision.EndParameter == 1 ? end : start + delta * decision.EndParameter);
        }
        private static void RequirePlanarLine(Line line)
        {
            if (!Finite(line.StartPoint) || !Finite(line.EndPoint) || line.StartPoint.DistanceTo(line.EndPoint) <= Tolerance ||
                line.StartPoint.Z != line.EndPoint.Z || line.Normal != Vector3d.ZAxis || line.Thickness != 0)
                throw new InvalidOperationException("Sewer LINE must have finite horizontal world-XY geometry and zero thickness. Unsupported/nonplanar inputs are retained; no flattening is performed.");
        }
        private static bool Finite(Point3d point) => double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z);
        private static void VerifySegment(Line line, Segment segment)
        {
            if (line.StartPoint != segment.Start || line.EndPoint != segment.End)
                throw new InvalidOperationException("Clipped LINE did not retain the planned endpoints.");
        }
        private static void RequireUnlocked(Transaction tr, ObjectId id)
        {
            var entity = (Entity)tr.GetObject(id, OpenMode.ForRead, false);
            if (((LayerTableRecord)tr.GetObject(entity.LayerId, OpenMode.ForRead)).IsLocked)
                throw new InvalidOperationException($"Layer '{entity.Layer}' is locked; it was not overridden.");
        }
        private static void EnsureTargetLayer(Database db, Transaction tr, Document doc, string name, string linetype)
        {
            var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (table.Has(name) && ((LayerTableRecord)tr.GetObject(table[name], OpenMode.ForRead)).IsLocked)
                throw new InvalidOperationException($"Target layer '{name}' is locked.");
            if (!LayerStandards.TryEnsureManagedGisLayer(db, tr, doc.Editor, name))
                throw new InvalidOperationException("Missing managed sewer layer standard: " + name);
            var layer = (LayerTableRecord)tr.GetObject(table[name], OpenMode.ForRead);
            string actual = ((LinetypeTableRecord)tr.GetObject(layer.LinetypeObjectId, OpenMode.ForRead)).Name;
            if (!string.Equals(actual, linetype, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Layer {name} requires {linetype}; no Continuous fallback was accepted.");
        }
        private static Circle AppendCircle(BlockTableRecord model, Transaction tr, SewerManholeCircle plan, string layer)
        {
            var source = (Circle)tr.GetObject(plan.SourceEntityId, OpenMode.ForRead);
            var circle = (Circle)source.Clone();
            try
            {
                circle.Center = plan.Center; circle.Normal = plan.Normal; circle.Radius = plan.Radius; circle.Layer = layer;
                if (!model.IsWriteEnabled) model.UpgradeOpen();
                model.AppendEntity(circle); tr.AddNewlyCreatedDBObject(circle, true);
                if (circle.Center != plan.Center || circle.Normal != plan.Normal || circle.Radius != plan.Radius ||
                    !string.Equals(circle.Layer, layer, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Materialized CIRCLE differs from the evaluated visible geometry.");
                return circle;
            }
            catch { if (circle.ObjectId.IsNull) circle.Dispose(); throw; }
        }
        private static Line AppendWall(BlockTableRecord model, Transaction tr, Line source, Segment plan)
        {
            var wall = new Line(plan.Start, plan.End);
            try
            {
                wall.SetPropertiesFrom(source); wall.LineWeight = source.LineWeight;
                wall.Transparency = source.Transparency; wall.Layer = PipeLayer;
                if (!model.IsWriteEnabled) model.UpgradeOpen();
                model.AppendEntity(wall); tr.AddNewlyCreatedDBObject(wall, true);
                VerifySegment(wall, plan);
                return wall;
            }
            catch { if (wall.ObjectId.IsNull) wall.Dispose(); throw; }
        }
        private static void CopyOd(ObjectId source, ObjectId destination, string expectedFingerprint)
        {
            var status = GisImportCommands.CopyObjectDataVerified(source, destination, out string detail);
            if (status != GisImportCommands.ObjectDataCopyStatus.CopiedVerified && status != GisImportCommands.ObjectDataCopyStatus.AlreadyEquivalent)
                throw new InvalidOperationException($"OD transfer {source.Handle}->{destination.Handle}: {status}: {detail}");
            RequireOd(destination, GisImportCommands.ObjectDataFingerprintState.Present, expectedFingerprint);
        }
        private static void RequireOd(ObjectId id, GisImportCommands.ObjectDataFingerprintState expected, string fingerprint)
        {
            var state = GisImportCommands.InspectObjectDataFingerprint(id, out string actual, out string detail);
            if (state != expected || (expected == GisImportCommands.ObjectDataFingerprintState.Present && actual != fingerprint))
                throw new InvalidOperationException($"Entity {id.Handle}: complete typed OD differs: {detail}");
        }
        private static void RequireSourceManholeUnchanged(Transaction tr, SewerManholePlan plan)
        {
            var point = tr.GetObject(plan.SourceId, OpenMode.ForRead, false) as DBPoint
                ?? throw new InvalidOperationException("The imported Structures point is unavailable.");
            if (point.Position != plan.SourcePosition || !string.Equals(point.Layer, "Structures", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The imported Structures point changed after planning.");
            RequireOd(plan.SourceId, GisImportCommands.ObjectDataFingerprintState.Present, plan.SourceOdFingerprint);
            var block = tr.GetObject(plan.BlockId, OpenMode.ForRead, false) as BlockReference
                ?? throw new InvalidOperationException("The planned manhole block is unavailable.");
            if (GisSewerManholePlanner.CaptureBlockState(tr, block) != plan.BlockStateSignature)
                throw new InvalidOperationException("The evaluated manhole block changed after planning.");
            if (!plan.MarkerId.IsNull)
            {
                var marker = tr.GetObject(plan.MarkerId, OpenMode.ForRead, false) as BlockReference
                    ?? throw new InvalidOperationException("The planned marker is unavailable.");
                if (GisSewerManholePlanner.CaptureMarkerState(tr, marker) != plan.MarkerStateSignature)
                    throw new InvalidOperationException("The marker changed after planning.");
                RequireOd(marker.ObjectId, GisImportCommands.ObjectDataFingerprintState.Empty, string.Empty);
            }
        }
        private static string Shape(Transaction tr, Entity entity)
        {
            string text = entity is DBPoint point
                ? FormattableString.Invariant($"POINT|{point.Position.X:R}|{point.Position.Y:R}|{point.Position.Z:R}|{point.Layer}|{point.Visible}")
                : GisNewDrawingClone.ReadPrimitiveSignature(entity, tr);
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        }
        private static Output CaptureOutput(Transaction tr, ObjectId id, string role, string name)
        {
            var entity = (Entity)tr.GetObject(id, OpenMode.ForRead, false);
            var state = GisImportCommands.InspectObjectDataFingerprint(id, out string fingerprint, out string detail);
            if (state == GisImportCommands.ObjectDataFingerprintState.ReadFailed) throw new InvalidOperationException(detail);
            return new Output(id, role, name, Shape(tr, entity), state, fingerprint);
        }
        private static void RequireNoUntrackedOutputs(BlockTableRecord model, Transaction tr, List<string> reviews)
        {
            foreach (ObjectId id in model)
                if (tr.GetObject(id, OpenMode.ForRead, false) is Entity entity && !entity.IsErased &&
                    new[] { PipeLayer, CenterLayer, OuterLayer, InnerLayer }.Contains(entity.Layer, StringComparer.OrdinalIgnoreCase))
                    reviews.Add($"Untracked existing sewer output {id.Handle} on {entity.Layer}; existing results cannot be inferred or processed again.");
        }
        private static void RequireNoNewInputs(BlockTableRecord model, Transaction tr, HashSet<ObjectId> owned)
        {
            foreach (ObjectId id in model)
            {
                if (owned.Contains(id) || tr.GetObject(id, OpenMode.ForRead, false) is not Entity entity || entity.IsErased) continue;
                bool newInput = false;
                if (IsRawPipeLayer(entity.Layer))
                {
                    if (IsExcludedStormPipe(id, entity.Layer)) continue;
                    if (!GisImportCommands.TryReadSewerPipeData(id, out var data, out string detail) || data == null)
                        throw new InvalidOperationException($"Untracked pipe {id.Handle}: {detail}");
                    newInput = true;
                }
                else if (string.Equals(entity.Layer, "Structures", StringComparison.OrdinalIgnoreCase))
                {
                    if (!GisImportCommands.TryReadStructuresIdentity(id, out string name, out string part, out string detail))
                        throw new InvalidOperationException($"Untracked Structures entity {id.Handle}: {detail}");
                    newInput = GisSewerManholePlanner.IsSewerIdentity(name, part);
                }
                if (newInput)
                    throw new InvalidOperationException($"New or untracked imported input {id.Handle} is present beside prepared sewer results; review before combining batches.");
                if (new[] { PipeLayer, CenterLayer, OuterLayer, InnerLayer }.Contains(entity.Layer, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Untracked sewer output {id.Handle} is present; no proximity ownership was inferred.");
            }
        }

        private static void WriteArchive(Database db, Transaction tr, BatchArchive archive)
        {
            var root = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
            if (root.Contains(ArchiveKey)) throw new InvalidOperationException("A sewer ownership archive already exists.");
            var values = new List<TypedValue> { Text(ArchiveKey), Count(archive.Outputs.Count) };
            foreach (Output output in archive.Outputs)
            {
                values.Add(new TypedValue((int)DxfCode.SoftPointerId, output.Id));
                values.Add(Text(output.Role)); values.Add(Text(output.Name)); values.Add(Text(output.Shape));
                values.Add(Text(output.OdState.ToString())); values.Add(Text(output.OdFingerprint));
            }
            values.Add(Count(archive.OriginalPipes.Count));
            foreach (var pipe in archive.OriginalPipes)
            {
                values.Add(new TypedValue((int)DxfCode.SoftPointerId, pipe.Id)); values.Add(Text(pipe.Name));
                AddTextChunks(values, pipe.Geometry);
            }
            values.Add(Count(archive.Structures.Count));
            foreach (StructureArchive structure in archive.Structures)
            {
                values.Add(Text(structure.Handle)); values.Add(Text(structure.Name)); values.Add(Text(structure.Part));
                values.Add(new TypedValue((int)DxfCode.XCoordinate, structure.Position));
                values.Add(Text(structure.BlockHandle)); values.Add(Text(structure.MarkerHandle));
                values.Add(new TypedValue((int)DxfCode.SoftPointerId, structure.OuterId));
                AddTextChunks(values, structure.OriginalBlockState);
            }
            var record = new Xrecord(); root.SetAt(ArchiveKey, record); tr.AddNewlyCreatedDBObject(record, true);
            using var data = new ResultBuffer(values.ToArray()); record.Data = data;
        }
        private static BatchArchive? ReadArchive(Database db, Transaction tr)
        {
            var root = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!root.Contains(ArchiveKey)) return null;
            var record = tr.GetObject(root.GetAt(ArchiveKey), OpenMode.ForRead) as Xrecord
                ?? throw new InvalidOperationException("Sewer archive has an unexpected type.");
            using ResultBuffer? data = record.Data;
            TypedValue[] values = data?.AsArray() ?? throw new InvalidOperationException("Sewer archive is empty.");
            int index = 0;
            object Take(DxfCode code)
            {
                if (index >= values.Length || values[index].TypeCode != (int)code)
                    throw new InvalidOperationException("Sewer archive is malformed.");
                return values[index++].Value;
            }
            string ReadText() => Take(DxfCode.Text) as string ?? throw new InvalidOperationException("Sewer archive text is invalid.");
            int ReadCount()
            {
                int number = Convert.ToInt32(Take(DxfCode.Int32), CultureInfo.InvariantCulture);
                return number >= 0 && number <= 100000 ? number : throw new InvalidOperationException("Sewer archive count is invalid.");
            }
            string ReadChunks()
            {
                int count = ReadCount(); var result = new StringBuilder();
                for (int part = 0; part < count; part++) result.Append(ReadText());
                return result.ToString();
            }
            ObjectId ReadId() => Take(DxfCode.SoftPointerId) is ObjectId id ? id : throw new InvalidOperationException("Sewer archive reference is invalid.");
            if (ReadText() != ArchiveKey) throw new InvalidOperationException("Sewer archive version is unsupported.");
            var outputs = new List<Output>();
            for (int remaining = ReadCount(); remaining > 0; remaining--)
            {
                ObjectId id = ReadId(); string role = ReadText(), name = ReadText(), shape = ReadText(), state = ReadText(), fingerprint = ReadText();
                if (!Enum.TryParse(state, out GisImportCommands.ObjectDataFingerprintState parsed) ||
                    (parsed != GisImportCommands.ObjectDataFingerprintState.Empty && parsed != GisImportCommands.ObjectDataFingerprintState.Present))
                    throw new InvalidOperationException("Sewer archive OD state is invalid.");
                outputs.Add(new Output(id, role, name, shape, parsed, fingerprint));
            }
            var pipes = new List<(ObjectId Id, string Name, string Geometry)>();
            for (int remaining = ReadCount(); remaining > 0; remaining--) pipes.Add((ReadId(), ReadText(), ReadChunks()));
            var structures = new List<StructureArchive>();
            for (int remaining = ReadCount(); remaining > 0; remaining--)
            {
                string handle = ReadText(), name = ReadText(), part = ReadText();
                Point3d point = Take(DxfCode.XCoordinate) is Point3d value ? value : throw new InvalidOperationException("Sewer archive position is invalid.");
                string block = ReadText(), marker = ReadText(); ObjectId outer = ReadId();
                structures.Add(new StructureArchive(handle, name, part, point, block, marker, outer, ReadChunks()));
            }
            if (index != values.Length || outputs.Count == 0) throw new InvalidOperationException("Sewer archive has invalid trailing or empty data.");
            return new BatchArchive(outputs.AsReadOnly(), pipes.AsReadOnly(), structures.AsReadOnly());
        }
        private static void VerifyArchive(Database db, Transaction tr, ObjectId model, BatchArchive archive)
        {
            var ids = new HashSet<ObjectId>();
            foreach (Output expected in archive.Outputs)
            {
                if (!ids.Add(expected.Id) || expected.Id.IsNull || !expected.Id.IsValid || expected.Id.IsErased || expected.Id.Database != db ||
                    tr.GetObject(expected.Id, OpenMode.ForRead, false) is not Entity entity || entity.OwnerId != model)
                    throw new InvalidOperationException("Sewer archive has missing, duplicated or foreign output references.");
                if (Shape(tr, entity) != expected.Shape) throw new InvalidOperationException($"Owned sewer output {expected.Id.Handle} was edited; no duplicate or repair was attempted.");
                RequireOd(expected.Id, expected.OdState, expected.OdFingerprint);
            }
            foreach (var pipe in archive.OriginalPipes)
                if (!ids.Contains(pipe.Id) || string.IsNullOrEmpty(pipe.Name) || string.IsNullOrEmpty(pipe.Geometry))
                    throw new InvalidOperationException("Sewer archive original pipe provenance is incomplete.");
            foreach (StructureArchive structure in archive.Structures)
                if (!ids.Contains(structure.OuterId) || !Finite(structure.Position) || string.IsNullOrEmpty(structure.Name) ||
                    string.IsNullOrEmpty(structure.OriginalBlockState))
                    throw new InvalidOperationException("Sewer archive structure provenance is incomplete.");
        }
        private static void RequireSameArchive(BatchArchive expected, BatchArchive actual)
        {
            if (!expected.Outputs.SequenceEqual(actual.Outputs) ||
                !expected.OriginalPipes.SequenceEqual(actual.OriginalPipes) ||
                !expected.Structures.SequenceEqual(actual.Structures))
                throw new InvalidOperationException("Sewer ownership/provenance archive did not read back exactly; no source cleanup is permitted.");
        }
        private static TypedValue Text(string value) => new((int)DxfCode.Text, value);
        private static TypedValue Count(int value) => new((int)DxfCode.Int32, value);
        private static void AddTextChunks(List<TypedValue> values, string text)
        {
            int count = (text.Length + 199) / 200; values.Add(Count(count));
            for (int index = 0; index < text.Length; index += 200) values.Add(Text(text.Substring(index, Math.Min(200, text.Length - index))));
        }
    }
}
