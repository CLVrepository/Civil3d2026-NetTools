using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcPolyline = Autodesk.AutoCAD.DatabaseServices.Polyline;
using CompletionRecord = CLV_CivilTools.Gis.StormPipeCompletionRecord<Autodesk.AutoCAD.DatabaseServices.ObjectId>;
using WallRecord = CLV_CivilTools.Gis.StormPipeWallRecord<Autodesk.AutoCAD.DatabaseServices.ObjectId>;
using static CLV_CivilTools.Gis.StormPipePreparationRules;

namespace CLV_CivilTools.Gis
{
    /// <summary>
    /// Exact source-owned pipe results within one caller-owned transaction. A successful
    /// trim must precede CompleteAfterTrim; the caller commits only after all dependent
    /// structure cleanup succeeds. Review results must cause the caller to abort.
    /// </summary>
    internal sealed class StormPipePreparationBatch
    {
        private readonly Database _database;
        private readonly Transaction _transaction;
        private readonly IReadOnlyList<GisStormPipePreparation.PreparedPipe> _pipes;
        private bool _completed;

        internal StormPipePreparationBatch(Database database, Transaction transaction,
            IEnumerable<GisStormPipePreparation.PreparedPipe> pipes, IEnumerable<string> reviews)
        {
            _database = database;
            _transaction = transaction;
            _pipes = Array.AsReadOnly(pipes.ToArray());
            Reviews = Array.AsReadOnly(reviews.ToArray());
            WallIds = Array.AsReadOnly(_pipes.SelectMany(pipe => pipe.PositiveIds.Concat(pipe.NegativeIds)).ToArray());
            NewPipeCount = _pipes.Count(pipe => pipe.PreviousCompletion == null);
            ReusedPipeCount = _pipes.Count(pipe => pipe.PreviousCompletion != null);
            SingleLineCount = _pipes.Count(pipe => pipe.IsSingleLine);
        }

        internal IReadOnlyList<ObjectId> WallIds { get; }
        internal IReadOnlyList<string> Reviews { get; }
        internal bool Success => Reviews.Count == 0;
        internal int NewPipeCount { get; }
        internal int ReusedPipeCount { get; }
        internal int SingleLineCount { get; }

        internal IReadOnlyDictionary<ObjectId, StormWallConnectionContext> BuildWallConnectionContexts(
            Database db, Transaction tr, IReadOnlyList<StormStructureSource> structureSources,
            IReadOnlyList<IReadOnlyList<StormStructureVertex>>? physicalFootprints = null)
        {
            if (!ReferenceEquals(db, _database) || !ReferenceEquals(tr, _transaction) || !Success || _completed)
                throw new InvalidOperationException("Wall connection contexts require this uncompleted pipe batch and its original transaction.");
            return GisStormPipePreparation.BuildWallConnectionContexts(db, tr, _pipes, structureSources, physicalFootprints);
        }

        internal void CompleteAfterTrim(Database db, Transaction tr)
        {
            if (!ReferenceEquals(db, _database) || !ReferenceEquals(tr, _transaction))
                throw new InvalidOperationException("Pipe completion must use the same drawing and transaction as preparation.");
            if (!Success || _completed)
                throw new InvalidOperationException("Only an uncompleted successful pipe batch can be completed.");
            GisStormPipePreparation.CompleteAfterTrim(db, tr, _pipes);
            _completed = true;
        }
    }

    /// <summary>
    /// Managed storm ALL stage. Native offset geometry is staged before any pipe write;
    /// no command queue, LISP, proximity ownership, nested transaction, or commit is used.
    /// All exceptions after apply begins must abort the caller's complete transaction.
    /// </summary>
    internal static class GisStormPipePreparation
    {
        internal const string WallLayer = "C-STRM-PIPE-E";
        internal const string CenterlineLayer = "C-STRM-PIPE-CNTR-E";
        private const string CompletionKey = "CLV_STORM_PIPE_PREP_V1";
        private const string GeometryPrefix = "CLV_PIPE_GEOM_V1:SHA256:";

        internal static StormPipePreparationBatch Prepare(Database db, Transaction tr)
        {
            RequireContext(db, tr);
            ObjectId modelId = ModelSpaceId(db, tr);
            var model = (BlockTableRecord)tr.GetObject(modelId, OpenMode.ForRead);
            var reviews = new List<string>();
            var pipes = new List<PreparedPipe>();
            var transients = new HashSet<DBObject>(ReferenceEqualityComparer.Instance);
            var claimedWalls = new HashSet<ObjectId>();

            try
            {
                // Enumerate a snapshot: appended offsets must never become fresh sources.
                foreach (ObjectId id in model.Cast<ObjectId>().ToArray())
                {
                    try
                    {
                        if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity entity) continue;
                        CompletionRecord? completion = ReadCompletion(tr, entity);
                        if (completion != null)
                        {
                            PreparedPipe reused = ValidateCompletion(db, tr, modelId, entity, completion);
                            foreach (ObjectId wallId in reused.PositiveIds.Concat(reused.NegativeIds))
                                if (!claimedWalls.Add(wallId))
                                    throw new InvalidOperationException("A recorded wall is claimed by more than one pipe source.");
                            pipes.Add(reused);
                            continue;
                        }
                        if (LayerEquals(entity.Layer, CenterlineLayer))
                            throw new InvalidOperationException("Untracked processed storm centerline requires review; existing walls cannot be inferred or offset again.");
                        if (LayerEquals(entity.Layer, WallLayer) || IsStructureLayer(entity.Layer)) continue;
                        if (entity is not Curve curve) continue;

                        var state = GisImportCommands.InspectPipeInsideDiameter(id, out double diameter,
                            out string odFingerprint, out IReadOnlyList<string> tables, out string detail);
                        // An invalid/read-failed snapshot may have incomplete table names;
                        // it cannot prove that a named sewer source has no storm OD.
                        if (state == GisImportCommands.PipeInsideDiameterState.ReadFailed ||
                            state == GisImportCommands.PipeInsideDiameterState.Invalid)
                            throw new InvalidOperationException("InsideDiameter/native utility evidence was not verified: " + detail);
                        StormPipeUtilityKind utility = ClassifyUtility(entity.Layer, tables);
                        if (utility == StormPipeUtilityKind.Review)
                            throw new InvalidOperationException("Conflicting or incomplete storm/sewer utility evidence requires review; no utility was chosen.");
                        if (utility == StormPipeUtilityKind.ExcludedSewer) continue;
                        if (state == GisImportCommands.PipeInsideDiameterState.Absent && !IsKnownSourceLayer(entity.Layer)) continue;
                        if (state != GisImportCommands.PipeInsideDiameterState.Valid)
                            throw new InvalidOperationException("InsideDiameter was not verified: " + detail);
                        RequireUnlockedLayer(tr, entity.LayerId);
                        string geometry = GeometryFingerprint(curve);
                        pipes.Add(new PreparedPipe(id, diameter, odFingerprint, geometry, entity.Layer, null));
                    }
                    catch (System.Exception ex)
                    {
                        reviews.Add($"Pipe source {id.Handle}: {ex.Message}");
                    }
                }

                // Existing target layers are never unlocked, restyled, or silently repaired.
                string[] targetLayers = pipes.Count == 0 ? Array.Empty<string>() :
                    pipes.Any(pipe => !pipe.IsSingleLine) ? new[] { WallLayer, CenterlineLayer } : new[] { WallLayer };
                foreach (string name in targetLayers)
                {
                    try { RequireUnlockedLayerIfPresent(db, tr, name); }
                    catch (System.Exception ex) { reviews.Add(ex.Message); }
                }
                if (reviews.Count > 0) return ReviewBatch(db, tr, reviews);

                // Both signs are required and validated while all native outputs are still
                // transient. Source and output geometry stays untouched if any preflight fails.
                foreach (PreparedPipe pipe in pipes.Where(pipe => pipe.PreviousCompletion == null && !pipe.IsSingleLine))
                {
                    try
                    {
                        var curve = (Curve)tr.GetObject(pipe.SourceId, OpenMode.ForRead);
                        StageSide(curve, pipe.DiameterFeet / 2.0, pipe.PositiveCurves, transients);
                        StageSide(curve, -pipe.DiameterFeet / 2.0, pipe.NegativeCurves, transients);
                        if (!TryValidateOwnership(curve, pipe.DiameterFeet, pipe.PositiveCurves, pipe.NegativeCurves, out string ownershipDetail))
                            throw new InvalidOperationException(ownershipDetail);
                        string[] geometries = pipe.PositiveCurves.Concat(pipe.NegativeCurves).Select(GeometryFingerprint).ToArray();
                        if (geometries.Distinct(StringComparer.Ordinal).Count() != geometries.Length ||
                            geometries.Contains(pipe.SourceGeometry, StringComparer.Ordinal))
                            throw new InvalidOperationException("Native offset produced duplicate or unchanged geometry.");
                        RequireSourceUnchanged(db, tr, modelId, pipe, pipe.OriginalLayer);
                    }
                    catch (System.Exception ex) { reviews.Add($"Pipe source {pipe.SourceId.Handle}: native offset preflight failed: {ex.Message}"); }
                }
                if (reviews.Count > 0) return ReviewBatch(db, tr, reviews);

                // Recheck the complete source batch before the first database write.
                foreach (PreparedPipe pipe in pipes)
                    RequireSourceUnchanged(db, tr, modelId, pipe,
                        pipe.PreviousCompletion == null ? pipe.OriginalLayer : ExpectedSourceLayer(pipe.DiameterFeet));

                if (pipes.Any(pipe => pipe.PreviousCompletion == null))
                {
                    EnsureLayer(db, tr, WallLayer, 60, "HIDDEN2");
                    if (pipes.Any(pipe => pipe.PreviousCompletion == null && !pipe.IsSingleLine))
                        EnsureLayer(db, tr, CenterlineLayer, 9, "CENTER2");
                }
                foreach (PreparedPipe pipe in pipes.Where(pipe => pipe.PreviousCompletion == null))
                {
                    AppendSide(db, tr, model, pipe, pipe.PositiveCurves, pipe.PositiveIds);
                    AppendSide(db, tr, model, pipe, pipe.NegativeCurves, pipe.NegativeIds);
                    VerifyWalls(db, tr, modelId, pipe);
                    var source = (Entity)tr.GetObject(pipe.SourceId, OpenMode.ForWrite);
                    source.Layer = ExpectedSourceLayer(pipe.DiameterFeet);
                    RequireSourceUnchanged(db, tr, modelId, pipe, ExpectedSourceLayer(pipe.DiameterFeet));
                    source.RecordGraphicsModified(true);
                }
                return new StormPipePreparationBatch(db, tr, pipes, Array.Empty<string>());
            }
            finally
            {
                // Once appended, the transaction owns the DBObject. Dispose every unused
                // transient even if the opposite side's native offset throws.
                foreach (DBObject transient in transients)
                    if (transient.ObjectId.IsNull) transient.Dispose();
            }
        }

        internal static void CompleteAfterTrim(Database db, Transaction tr, IReadOnlyList<PreparedPipe> pipes)
        {
            RequireContext(db, tr);
            ObjectId modelId = ModelSpaceId(db, tr);
            var records = new List<(PreparedPipe Pipe, CompletionRecord Record)>();
            var claimed = new HashSet<ObjectId>();
            foreach (PreparedPipe pipe in pipes)
            {
                RequireSourceUnchanged(db, tr, modelId, pipe, ExpectedSourceLayer(pipe.DiameterFeet));
                var source = (Entity)tr.GetObject(pipe.SourceId, OpenMode.ForRead);
                CompletionRecord? current = ReadCompletion(tr, source);
                if (!SameCompletion(current, pipe.PreviousCompletion))
                    throw new InvalidOperationException($"Pipe {pipe.SourceId.Handle} completion changed during preparation; abort the transaction.");
                VerifyWalls(db, tr, modelId, pipe);
                var positive = CaptureWalls(pipe.PositiveIds);
                var negative = CaptureWalls(pipe.NegativeIds);
                records.Add((pipe, new CompletionRecord(pipe.SourceId, pipe.SourceId.Handle.ToString(),
                    pipe.DiameterFeet, pipe.ObjectDataFingerprint, pipe.SourceGeometry, pipe.OriginalLayer, positive, negative)));
            }

            // Capture all final geometry and validate ownership before writing any record.
            foreach (var item in records)
            {
                var source = (Entity)tr.GetObject(item.Pipe.SourceId, OpenMode.ForRead);
                // An unchanged validated rerun does not rewrite its completion dictionary.
                if (!SameCompletion(item.Pipe.PreviousCompletion, item.Record))
                {
                    if (!source.IsWriteEnabled) source.UpgradeOpen();
                    WriteCompletion(tr, source, item.Record);
                }
                if (!SameCompletion(ReadCompletion(tr, source), item.Record))
                    throw new InvalidOperationException($"Pipe {source.Handle} completion readback differs; abort the transaction.");
            }
            foreach (var item in records)
            {
                var source = (Entity)tr.GetObject(item.Pipe.SourceId, OpenMode.ForRead);
                CompletionRecord? readback = ReadCompletion(tr, source);
                if (!SameCompletion(readback, item.Record) || readback == null)
                    throw new InvalidOperationException($"Pipe {source.Handle} completion changed during final verification.");
                ValidateCompletion(db, tr, modelId, source, readback);
            }

            WallRecord[] CaptureWalls(IEnumerable<ObjectId> ids)
            {
                return ids.Select(id =>
                {
                    if (!claimed.Add(id)) throw new InvalidOperationException("A pipe wall has multiple completion owners.");
                    Entity wall = RequireEntity(db, tr, modelId, id, WallLayer);
                    return new WallRecord(id, id.Handle.ToString(), GeometryFingerprint(wall));
                }).ToArray();
            }
        }

        internal static IReadOnlyDictionary<ObjectId, StormWallConnectionContext> BuildWallConnectionContexts(
            Database db, Transaction tr, IReadOnlyList<PreparedPipe> pipes, IReadOnlyList<StormStructureSource> structureSources,
            IReadOnlyList<IReadOnlyList<StormStructureVertex>>? physicalFootprints = null)
        {
            RequireContext(db, tr);
            ArgumentNullException.ThrowIfNull(structureSources);
            ObjectId modelId = ModelSpaceId(db, tr);
            var result = new Dictionary<ObjectId, StormWallConnectionContext>();
            foreach (PreparedPipe pipe in pipes)
            {
                RequireSourceUnchanged(db, tr, modelId, pipe, ExpectedSourceLayer(pipe.DiameterFeet));
                VerifyWalls(db, tr, modelId, pipe);
                if (pipe.IsSingleLine) continue;
                StormStructureVertex[] sourcePath = ReadPipePath(RequireEntity(db, tr, modelId,
                    pipe.SourceId, ExpectedSourceLayer(pipe.DiameterFeet)));
                double distance = 25.0 + pipe.DiameterFeet / 2.0;
                foreach (ObjectId id in pipe.PositiveIds.Concat(pipe.NegativeIds))
                {
                    StormStructureVertex[] wallPath = ReadPipePath(RequireEntity(db, tr, modelId, id, WallLayer));
                    // Only exact output IDs from this retained source can receive an
                    // endpoint exemption. Split/intermediate terminals receive none.
                    result.Add(id, new StormWallConnectionContext(distance,
                        IsVerifiedOpenNullTerminal(sourcePath, wallPath, true, pipe.DiameterFeet, structureSources, physicalFootprints),
                        IsVerifiedOpenNullTerminal(sourcePath, wallPath, false, pipe.DiameterFeet, structureSources, physicalFootprints)));
                }
            }
            return new System.Collections.ObjectModel.ReadOnlyDictionary<ObjectId, StormWallConnectionContext>(result);
        }

        private static StormStructureVertex[] ReadPipePath(Entity entity)
        {
            GeometryFingerprint(entity); // Uses the same strict finite WCS-XY geometry gate.
            if (entity is Line line)
                return new[] { new StormStructureVertex(line.StartPoint.X, line.StartPoint.Y),
                    new StormStructureVertex(line.EndPoint.X, line.EndPoint.Y) };
            var polyline = (AcPolyline)entity;
            return Enumerable.Range(0, polyline.NumberOfVertices).Select(index =>
            {
                Point3d point = polyline.GetPoint3dAt(index);
                return new StormStructureVertex(point.X, point.Y);
            }).ToArray();
        }

        private static StormPipePreparationBatch ReviewBatch(Database db, Transaction tr, IEnumerable<string> reviews)
            => new(db, tr, Array.Empty<PreparedPipe>(), reviews);

        private static void StageSide(Curve source, double distance, List<Curve> result, HashSet<DBObject> transients)
        {
            if (!double.IsFinite(distance) || distance == 0)
                throw new InvalidOperationException("Offset distance is not finite and nonzero.");
            using DBObjectCollection native = source.GetOffsetCurves(distance);
            if (native == null || native.Count == 0)
                throw new InvalidOperationException($"Native offset at {distance.ToString("R", CultureInfo.InvariantCulture)} feet returned no curves.");
            DBObject[] objects = native.Cast<DBObject>().ToArray();
            // Register every transient first so a bad early object does not leak later ones.
            foreach (DBObject item in objects)
                if (item != null && item.ObjectId.IsNull && !ReferenceEquals(item, source)) transients.Add(item);
            foreach (DBObject item in objects)
            {
                if (item is not Curve curve || !curve.ObjectId.IsNull || curve.Database != null ||
                    ReferenceEquals(curve, source) || result.Any(existing => ReferenceEquals(existing, curve)))
                    throw new InvalidOperationException("Native offset must return distinct new transient curves.");
                GeometryFingerprint(curve); // Includes finite/simple/supported geometry validation.
                result.Add(curve);
            }
        }

        private static void AppendSide(Database db, Transaction tr, BlockTableRecord model,
            PreparedPipe pipe, IEnumerable<Curve> curves, List<ObjectId> ids)
        {
            foreach (Curve curve in curves)
            {
                string stagedGeometry = GeometryFingerprint(curve);
                if (!curve.ObjectId.IsNull || curve.Database != null)
                    throw new InvalidOperationException("Offset was unexpectedly database-resident before append.");
                if (!model.IsWriteEnabled) model.UpgradeOpen();
                curve.Layer = WallLayer;
                ObjectId id = model.AppendEntity(curve);
                tr.AddNewlyCreatedDBObject(curve, true);
                if (id.IsNull || id == pipe.SourceId || !id.IsValid || id.IsErased || id.Database != db ||
                    curve.OwnerId != model.ObjectId || !LayerEquals(curve.Layer, WallLayer) ||
                    GeometryFingerprint(curve) != stagedGeometry)
                    throw new InvalidOperationException("Native wall append/readback did not retain the exact staged geometry.");
                ids.Add(id);
                var status = GisImportCommands.CopyObjectDataVerified(pipe.SourceId, id, out string detail);
                if (status != GisImportCommands.ObjectDataCopyStatus.CopiedVerified &&
                    status != GisImportCommands.ObjectDataCopyStatus.AlreadyEquivalent)
                    throw new InvalidOperationException($"Pipe {pipe.SourceId.Handle}, wall {id.Handle}: native OD transfer failed ({status}): {detail}");
                if (!curve.IsWriteEnabled) curve.UpgradeOpen();
                curve.RecordGraphicsModified(true);
            }
        }

        private static PreparedPipe ValidateCompletion(Database db, Transaction tr, ObjectId modelId,
            Entity source, CompletionRecord record)
        {
            if (record.SourceId != source.ObjectId || record.SourceHandle != source.Handle.ToString())
                throw new InvalidOperationException("Completion source identity differs; copied or damaged ownership requires review.");
            var state = GisImportCommands.InspectPipeInsideDiameter(source.ObjectId, out double diameter,
                out string fingerprint, out IReadOnlyList<string> tables, out string detail);
            if (state != GisImportCommands.PipeInsideDiameterState.Valid || diameter != record.DiameterFeet ||
                fingerprint != record.ObjectDataFingerprint || ClassifyUtility(record.OriginalLayer, tables) != StormPipeUtilityKind.Candidate)
                throw new InvalidOperationException("Recorded storm diameter/utility does not match current native OD: " + detail);
            var pipe = new PreparedPipe(source.ObjectId, record.DiameterFeet, record.ObjectDataFingerprint,
                record.SourceGeometry, record.OriginalLayer, record);
            pipe.PositiveIds.AddRange(record.Positive.Select(wall => wall.Id));
            pipe.NegativeIds.AddRange(record.Negative.Select(wall => wall.Id));
            RequireSourceUnchanged(db, tr, modelId, pipe, ExpectedSourceLayer(pipe.DiameterFeet));
            RequireUnlockedLayer(tr, source.LayerId);
            VerifyWalls(db, tr, modelId, pipe);
            foreach (WallRecord expected in record.Positive.Concat(record.Negative))
            {
                Entity wall = RequireEntity(db, tr, modelId, expected.Id, WallLayer);
                RequireUnlockedLayer(tr, wall.LayerId);
                if (expected.Handle != wall.Handle.ToString() || expected.Geometry != GeometryFingerprint(wall))
                    throw new InvalidOperationException($"Recorded wall {expected.Handle} geometry/identity changed; rerun requires review.");
            }
            return pipe;
        }

        private static void VerifyWalls(Database db, Transaction tr, ObjectId modelId, PreparedPipe pipe)
        {
            ObjectId[] ids = pipe.PositiveIds.Concat(pipe.NegativeIds).ToArray();
            if (!TryValidateOwnership(pipe.SourceId, pipe.DiameterFeet, pipe.PositiveIds, pipe.NegativeIds, out string reason))
                throw new InvalidOperationException(reason);
            if (pipe.IsSingleLine) return;
            foreach (ObjectId id in ids)
                GeometryFingerprint(RequireEntity(db, tr, modelId, id, WallLayer));
            if (!GisImportCommands.TryVerifyTransferredObjectData(pipe.SourceId, ids, out string fingerprint, out string detail) ||
                !string.Equals(fingerprint, pipe.ObjectDataFingerprint, StringComparison.Ordinal))
                throw new InvalidOperationException($"Pipe {pipe.SourceId.Handle} wall OD retention failed: {detail}");
        }

        private static void RequireSourceUnchanged(Database db, Transaction tr, ObjectId modelId, PreparedPipe pipe, string layer)
        {
            Entity source = RequireEntity(db, tr, modelId, pipe.SourceId, layer);
            if (GeometryFingerprint(source) != pipe.SourceGeometry)
                throw new InvalidOperationException($"Pipe {pipe.SourceId.Handle} source geometry changed; abort the transaction.");
            var state = GisImportCommands.InspectObjectDataFingerprint(pipe.SourceId, out string current, out string detail);
            if (state != GisImportCommands.ObjectDataFingerprintState.Present || current != pipe.ObjectDataFingerprint)
                throw new InvalidOperationException($"Pipe {pipe.SourceId.Handle} complete typed source OD changed or is unreadable: {detail}");
        }

        private static string GeometryFingerprint(Entity entity)
        {
            var canonical = new StringBuilder(GeometryPrefix);
            StormStructureVertex[] points;
            if (entity is Line line)
            {
                if (line.StartPoint.Z != line.EndPoint.Z || !IsWorldXyNormal(line.Normal))
                    throw new InvalidOperationException("Only horizontal world-XY pipe lines are supported.");
                canonical.Append("LINE|");
                AppendPoint(line.StartPoint); AppendPoint(line.EndPoint);
                AppendVector(line.Normal); AppendReal(line.Thickness);
                points = new[] { new StormStructureVertex(line.StartPoint.X, line.StartPoint.Y),
                    new StormStructureVertex(line.EndPoint.X, line.EndPoint.Y) };
            }
            else if (entity is AcPolyline polyline && !polyline.Closed && IsWorldXyNormal(polyline.Normal))
            {
                canonical.Append("LWPOLYLINE|").Append(polyline.NumberOfVertices.ToString(CultureInfo.InvariantCulture)).Append('|');
                AppendReal(polyline.Elevation); AppendVector(polyline.Normal); AppendReal(polyline.Thickness);
                points = new StormStructureVertex[polyline.NumberOfVertices];
                for (int i = 0; i < polyline.NumberOfVertices; i++)
                {
                    if (polyline.GetBulgeAt(i) != 0 || polyline.GetStartWidthAt(i) < 0 || polyline.GetEndWidthAt(i) < 0)
                        throw new InvalidOperationException("Pipe lightweight polylines must have straight segments and valid widths.");
                    Point3d point = polyline.GetPoint3dAt(i);
                    AppendPoint(point); AppendReal(polyline.GetBulgeAt(i));
                    AppendReal(polyline.GetStartWidthAt(i)); AppendReal(polyline.GetEndWidthAt(i));
                    points[i] = new StormStructureVertex(point.X, point.Y);
                }
            }
            else throw new InvalidOperationException("Curved, closed, 3D, or other complex pipe geometry requires review; only LINE and open straight world-XY LWPOLYLINE are supported.");
            if (!StormTerminalTrim.TryValidatePath(points, out string reason)) throw new InvalidOperationException(reason);
            return GeometryPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));

            void AppendPoint(Point3d point) { AppendReal(point.X); AppendReal(point.Y); AppendReal(point.Z); }
            void AppendVector(Vector3d vector) { AppendReal(vector.X); AppendReal(vector.Y); AppendReal(vector.Z); }
            void AppendReal(double value)
            {
                if (!double.IsFinite(value)) throw new InvalidOperationException("Pipe geometry contains a non-finite coordinate or dimension.");
                canonical.Append(value.ToString("R", CultureInfo.InvariantCulture)).Append('|');
            }
        }

        private static CompletionRecord? ReadCompletion(Transaction tr, Entity source)
        {
            if (source.ExtensionDictionary.IsNull) return null;
            var dictionary = (DBDictionary)tr.GetObject(source.ExtensionDictionary, OpenMode.ForRead);
            if (!dictionary.Contains(CompletionKey)) return null;
            if (tr.GetObject(dictionary.GetAt(CompletionKey), OpenMode.ForRead) is not Xrecord record)
                throw new InvalidOperationException("Pipe completion is not an Xrecord.");
            using ResultBuffer? buffer = record.Data;
            TypedValue[] values = buffer?.AsArray() ?? Array.Empty<TypedValue>();
            if (values.Length < 9 || Text(0) != "1")
                throw new InvalidOperationException("Pipe completion version or fields are unreadable.");
            ObjectId sourceId = Id(1);
            string handle = Text(2), od = Text(4), geometry = Text(5), originalLayer = Text(6);
            if (values[3].TypeCode != (int)DxfCode.Real || values[3].Value is not double diameter ||
                ClassifyDiameter(diameter) == StormPipeSizeKind.Invalid || !ValidHandle(handle) ||
                !StormObjectDataFingerprint.IsValid(od) || !ValidGeometryFingerprint(geometry) || string.IsNullOrWhiteSpace(originalLayer))
                throw new InvalidOperationException("Pipe completion diameter, source identity, or fingerprint is invalid.");
            int positiveCount = Count(7), negativeCount = Count(8);
            if ((long)values.Length != 9L + 3L * ((long)positiveCount + negativeCount) ||
                (ClassifyDiameter(diameter) == StormPipeSizeKind.SingleLine ? positiveCount != 0 || negativeCount != 0 : positiveCount == 0 || negativeCount == 0))
                throw new InvalidOperationException("Pipe completion has invalid side counts.");
            WallRecord[] positive = Walls(9, positiveCount);
            WallRecord[] negative = Walls(9 + 3 * positiveCount, negativeCount);
            return new CompletionRecord(sourceId, handle, diameter, od, geometry, originalLayer, positive, negative);

            WallRecord[] Walls(int offset, int count)
            {
                var result = new WallRecord[count];
                for (int i = 0; i < count; i++)
                {
                    ObjectId id = Id(offset + 3 * i);
                    string wallHandle = Text(offset + 3 * i + 1), wallGeometry = Text(offset + 3 * i + 2);
                    if (!ValidHandle(wallHandle) || !ValidGeometryFingerprint(wallGeometry))
                        throw new InvalidOperationException("Pipe wall completion identity or geometry fingerprint is invalid.");
                    result[i] = new WallRecord(id, wallHandle, wallGeometry);
                }
                return result;
            }
            string Text(int index)
            {
                if (values[index].TypeCode != (int)DxfCode.Text || values[index].Value is not string text)
                    throw new InvalidOperationException("Pipe completion text field is unreadable.");
                return text;
            }
            ObjectId Id(int index)
            {
                if (values[index].TypeCode != (int)DxfCode.SoftPointerId || values[index].Value is not ObjectId id)
                    throw new InvalidOperationException("Pipe completion object link is unreadable.");
                return id;
            }
            int Count(int index)
            {
                if (values[index].TypeCode != (int)DxfCode.Int32 || values[index].Value is not int count || count < 0)
                    throw new InvalidOperationException("Pipe completion side count is unreadable.");
                return count;
            }
        }

        private static void WriteCompletion(Transaction tr, Entity source, CompletionRecord item)
        {
            if (source.ExtensionDictionary.IsNull) source.CreateExtensionDictionary();
            var dictionary = (DBDictionary)tr.GetObject(source.ExtensionDictionary, OpenMode.ForWrite);
            Xrecord record;
            if (dictionary.Contains(CompletionKey))
                record = tr.GetObject(dictionary.GetAt(CompletionKey), OpenMode.ForWrite) as Xrecord
                    ?? throw new InvalidOperationException("Existing pipe completion is not an Xrecord.");
            else
            {
                record = new Xrecord();
                dictionary.SetAt(CompletionKey, record);
                tr.AddNewlyCreatedDBObject(record, true);
            }
            var values = new List<TypedValue>
            {
                new((int)DxfCode.Text, "1"), new((int)DxfCode.SoftPointerId, item.SourceId),
                new((int)DxfCode.Text, item.SourceHandle), new((int)DxfCode.Real, item.DiameterFeet),
                new((int)DxfCode.Text, item.ObjectDataFingerprint), new((int)DxfCode.Text, item.SourceGeometry),
                new((int)DxfCode.Text, item.OriginalLayer), new((int)DxfCode.Int32, item.Positive.Count),
                new((int)DxfCode.Int32, item.Negative.Count)
            };
            foreach (WallRecord wall in item.Positive.Concat(item.Negative))
            {
                values.Add(new TypedValue((int)DxfCode.SoftPointerId, wall.Id));
                values.Add(new TypedValue((int)DxfCode.Text, wall.Handle));
                values.Add(new TypedValue((int)DxfCode.Text, wall.Geometry));
            }
            using var data = new ResultBuffer(values.ToArray());
            record.Data = data;
        }

        private static bool ValidGeometryFingerprint(string value)
            => value.StartsWith(GeometryPrefix, StringComparison.Ordinal) && value.Length == GeometryPrefix.Length + 64 &&
                value.Skip(GeometryPrefix.Length).All(character => (character >= '0' && character <= '9') || (character >= 'A' && character <= 'F'));
        private static bool ValidHandle(string value) => value.Length > 0 && value.All(Uri.IsHexDigit);
        private static bool IsWorldXyNormal(Vector3d normal) => normal.X == 0 && normal.Y == 0 && normal.Z == 1;
        private static string ExpectedSourceLayer(double diameter) => ClassifyDiameter(diameter) == StormPipeSizeKind.SingleLine ? WallLayer : CenterlineLayer;
        private static bool LayerEquals(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        private static bool IsStructureLayer(string name) => name.Contains("STRC", StringComparison.OrdinalIgnoreCase);
        private static void RequireContext(Database db, Transaction tr)
        {
            if (db == null || tr == null) throw new ArgumentNullException(db == null ? nameof(db) : nameof(tr));
            if (AcadApp.DocumentManager.MdiActiveDocument?.Database != db || tr.TransactionManager != db.TransactionManager ||
                db.TransactionManager.TopTransaction != tr)
                throw new InvalidOperationException("Storm pipe preparation requires the active drawing and the caller's current transaction/document lock.");
        }

        private static ObjectId ModelSpaceId(Database db, Transaction tr)
            => ((BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead))[BlockTableRecord.ModelSpace];

        private static Entity RequireEntity(Database db, Transaction tr, ObjectId modelId, ObjectId id, string layer)
        {
            if (id.IsNull || !id.IsValid || id.IsErased || id.Database != db ||
                tr.GetObject(id, OpenMode.ForRead, false) is not Entity entity || entity.IsErased ||
                entity.OwnerId != modelId || !LayerEquals(entity.Layer, layer))
                throw new InvalidOperationException($"Required entity is not live model-space geometry on {layer}.");
            return entity;
        }

        private static void RequireUnlockedLayer(Transaction tr, ObjectId layerId)
        {
            var layer = (LayerTableRecord)tr.GetObject(layerId, OpenMode.ForRead);
            if (layer.IsLocked) throw new InvalidOperationException($"Layer {layer.Name} is locked; review before storm preparation.");
        }

        private static void RequireUnlockedLayerIfPresent(Database db, Transaction tr, string name)
        {
            var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (layers.Has(name)) RequireUnlockedLayer(tr, layers[name]);
        }

        private static void EnsureLayer(Database db, Transaction tr, string name, short colorIndex, string linetype)
        {
            var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (layers.Has(name)) return;
            layers.UpgradeOpen();
            var layer = new LayerTableRecord { Name = name,
                Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex) };
            var linetypes = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (linetypes.Has(linetype)) layer.LinetypeObjectId = linetypes[linetype];
            layers.Add(layer);
            tr.AddNewlyCreatedDBObject(layer, true);
        }

        internal sealed class PreparedPipe
        {
            internal PreparedPipe(ObjectId sourceId, double diameterFeet, string fingerprint,
                string sourceGeometry, string originalLayer, CompletionRecord? previousCompletion)
            {
                if (ClassifyDiameter(diameterFeet) == StormPipeSizeKind.Invalid)
                    throw new InvalidOperationException("Prepared pipe diameter must be positive and finite.");
                SourceId = sourceId; DiameterFeet = diameterFeet; ObjectDataFingerprint = fingerprint;
                SourceGeometry = sourceGeometry; OriginalLayer = originalLayer; PreviousCompletion = previousCompletion;
            }
            internal ObjectId SourceId { get; }
            internal double DiameterFeet { get; }
            internal bool IsSingleLine => ClassifyDiameter(DiameterFeet) == StormPipeSizeKind.SingleLine;
            internal string ObjectDataFingerprint { get; }
            internal string SourceGeometry { get; }
            internal string OriginalLayer { get; }
            internal CompletionRecord? PreviousCompletion { get; }
            internal List<Curve> PositiveCurves { get; } = new();
            internal List<Curve> NegativeCurves { get; } = new();
            internal List<ObjectId> PositiveIds { get; } = new();
            internal List<ObjectId> NegativeIds { get; } = new();
        }

    }
}
