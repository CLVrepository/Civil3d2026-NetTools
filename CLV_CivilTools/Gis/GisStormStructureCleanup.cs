using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDocument = Autodesk.AutoCAD.ApplicationServices.Document;

namespace CLV_CivilTools.Gis
{
    public static partial class GisStormStructureAuto
    {
        [CommandMethod("CLV-GIS-STORM-CLEAN-VERIFIED", CommandFlags.Modal)]
        public static void CleanVerifiedStormSources()
        {
            AcDocument? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc != null) CleanupVerifiedSources(doc);
        }

        // Standalone cleanup is an independent read-only proof followed by erasure.
        // ALL also prepares and trims pipes in the same applying transaction.
        internal static bool CleanupVerifiedSources(AcDocument doc, bool preparePipes = false)
        {
            var db = doc.Database;
            var ed = doc.Editor;
            var sources = new Dictionary<string, SourceInfo>(StringComparer.OrdinalIgnoreCase);
            var sourcePlans = new List<StormCleanupSource>();
            var completions = new List<StormStructureCompletion>();
            var markers = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
            var markerPlans = new List<StormCleanupMarker>();
            var fingerprints = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var anchors = new Dictionary<string, Point3d>(StringComparer.OrdinalIgnoreCase);
            var notes = new List<string>();
            bool cleanupCommitted = false;
            string managedPipeSummary = string.Empty;
            try
            {
                using (doc.LockDocument())
                {
                    // All source/output geometry is already committed before this command.
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                        if (!string.Equals(space.Name, BlockTableRecord.ModelSpace, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Verified storm cleanup requires model space");
                        foreach (ObjectId id in space)
                        {
                            if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity entity || entity.IsErased) continue;
                            string handle = entity.Handle.ToString();
                            if (entity is DBPoint point && string.Equals(point.Layer, StructuresPointLayer, StringComparison.OrdinalIgnoreCase))
                            {
                                bool identity = GisImportCommands.TryReadStructuresIdentity(id, out string name, out string part,
                                    out string identityDetail, out bool nativeFailure);
                                if (nativeFailure) throw new InvalidOperationException(identityDetail);
                                var source = new SourceInfo(id, new StormStructureSource(handle, name, part, point.Position.X, point.Position.Y))
                                { Outputs = ReadUnverifiedOutputClaims(tr, point) };
                                sources.Add(handle, source);
                                bool verified = false;
                                if (identity && TryReadCompletion(tr, point, name, part, out List<ObjectId> outputs, out string completionDetail))
                                {
                                    source.Outputs = outputs;
                                    verified = GisImportCommands.TryVerifyTransferredObjectData(id, outputs, out string fingerprint, out string odDetail);
                                    if (verified)
                                    {
                                        fingerprints.Add(handle, fingerprint);
                                        anchors.Add(handle, ReadPointCompletionAnchor(tr, point));
                                    }
                                    else notes.Add($"point {handle}: {odDetail}; retained");
                                }
                                sourcePlans.Add(new StormCleanupSource(source.Data, StormCleanupSourceState.LiveImportedPoint, verified));
                                if (source.Outputs.Count > 0)
                                    completions.Add(new StormStructureCompletion(handle, source.Outputs.Select(ClaimHandle).ToArray()));
                            }
                            else if (string.Equals(entity.Layer, StructuresPointLayer, StringComparison.OrdinalIgnoreCase))
                            {
                                // An unsupported import could still own a nearby marker.
                                // Do not drop it from the competition and infer uniqueness.
                                throw new InvalidOperationException($"Structures entity {handle} is {entity.GetType().Name}, not a supported DBPoint; cleanup requires review");
                            }
                            else if (entity is BlockReference marker && IsKnownPrepMarker(GetEffectiveBlockName(marker, tr)))
                            {
                                markers.Add(handle, id);
                                var state = InspectMarkerData(marker, out string detail);
                                if (state == StormMarkerOdState.Unreadable)
                                    notes.Add($"marker {handle}: {detail}; retained");
                                markerPlans.Add(new StormCleanupMarker(handle, GetEffectiveBlockName(marker, tr), marker.Layer,
                                    marker.Position.X, marker.Position.Y, state));
                            }
                        }
                        foreach (StormPrepArchiveRecord archived in GisStormPrepArchive.ReadAll(db, tr))
                        {
                            ValidateArchivedOutputs(tr, archived);
                            if (sources.ContainsKey(archived.SourceHandle))
                                throw new InvalidOperationException($"Source {archived.SourceHandle} exists both live and in cleanup history; retained for review.");
                            var source = new SourceInfo(ObjectId.Null, new StormStructureSource(archived.SourceHandle,
                                archived.Name, archived.PartSizeName, archived.Anchor.X, archived.Anchor.Y))
                            { Outputs = archived.OutputIds.ToList(), Archive = archived };
                            sources.Add(source.Data.Id, source);
                            sourcePlans.Add(new StormCleanupSource(source.Data, StormCleanupSourceState.ArchivedCompletion,
                                true, archived.MarkerHandles));
                            completions.Add(new StormStructureCompletion(source.Data.Id, source.Outputs.Select(ClaimHandle).ToArray()));
                        }
                        tr.Commit();
                    }

                    StormCleanupPlan plan = StormStructureCleanup.Plan(sourcePlans, completions, markerPlans);
                    notes.AddRange(plan.Issues.Select(issue => DescribeIssue(issue, sources)));
                    if (preparePipes && notes.Count > 0)
                        throw new InvalidOperationException("Structure/marker cleanup requires review before managed pipe preparation: " + string.Join(" | ", notes));
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        if (preparePipes)
                        {
                            // The managed offset, wall trim and source cleanup share this
                            // one transaction. No queued helper is a completion signal.
                            var pipes = GisStormPipePreparation.Prepare(db, tr);
                            if (pipes.Reviews.Count > 0)
                                throw new InvalidOperationException("Pipe preparation requires review: " + string.Join(" | ", pipes.Reviews));
                            var boundaries = new List<ObjectId>();
                            foreach (string ownerId in plan.VerifiedOwnerIds)
                            {
                                SourceInfo owner = sources[ownerId];
                                StormStructureRole role = StormStructureMatching.Classify(owner.Data.Name, owner.Data.PartSizeName);
                                if (role != StormStructureRole.DropInlet && role != StormStructureRole.JunctionBox) continue;
                                foreach (ObjectId id in owner.Outputs)
                                {
                                    if (tr.GetObject(id, OpenMode.ForRead, false) is Autodesk.AutoCAD.DatabaseServices.Polyline outline &&
                                        outline.Visible && IsStructureOutline(outline))
                                        boundaries.Add(id);
                                }
                            }
                            var physicalFootprints = boundaries.Distinct().Select(id => GetStructureFootprint(
                                (Autodesk.AutoCAD.DatabaseServices.Polyline)tr.GetObject(id, OpenMode.ForRead))).ToArray();
                            var connections = pipes.BuildWallConnectionContexts(db, tr,
                                sourcePlans.Select(source => source.Source).ToArray(), physicalFootprints);
                            StormTrimBatchResult trim = GisTrimInsideBoundaryCommands.TrimVerifiedStormBoxWalls(db, tr,
                                boundaries.Distinct().ToArray(), pipes.WallIds, connections);
                            if (!trim.Success)
                                throw new InvalidOperationException("Pipe-wall trim requires review: " + string.Join(" | ", trim.ReviewItems));
                            pipes.CompleteAfterTrim(db, tr);
                            managedPipeSummary = $"\nManaged storm pipes: new={pipes.NewPipeCount}, already verified={pipes.ReusedPipeCount}, single-line={pipes.SingleLineCount}, owned wall curves={pipes.WallIds.Count}, walls trimmed={trim.TrimmedCount}, unchanged={trim.UnchangedCount}. Pipe offsets, trim and source cleanup committed together.";
                        }
                        foreach (string sourceId in plan.SourceIdsToArchiveAndErase)
                        {
                            SourceInfo source = sources[sourceId];
                            if (source.Archive != null || source.Id.IsNull)
                                throw new InvalidOperationException("Archived ownership is not a deletable imported point.");
                            var point = tr.GetObject(source.Id, OpenMode.ForRead, false) as DBPoint;
                            if (point == null || point.IsErased || !string.Equals(point.Layer, StructuresPointLayer, StringComparison.OrdinalIgnoreCase) ||
                                Distance2d(point.Position, new Point3d(source.Data.X, source.Data.Y, 0.0)) > StormStructureMatching.MatchTolerance ||
                                !TryReadCompletion(tr, point, source.Data.Name, source.Data.PartSizeName, out List<ObjectId> outputs, out _) ||
                                !outputs.SequenceEqual(source.Outputs))
                                throw new InvalidOperationException($"Point {sourceId} or its ownership changed; cleanup rolled back.");
                            if (!GisImportCommands.TryVerifyTransferredObjectData(source.Id, source.Outputs, out string fingerprint, out string detail) ||
                                !string.Equals(fingerprint, fingerprints[sourceId], StringComparison.Ordinal))
                                throw new InvalidOperationException($"Point {sourceId} output OD is no longer verified: {detail}; cleanup rolled back.");
                            string[] ownedMarkers = plan.MarkerMatches.Where(m => m.SourceId.Equals(sourceId, StringComparison.OrdinalIgnoreCase))
                                .Select(m => m.MarkerId).ToArray();
                            GisStormPrepArchive.WriteAndVerify(db, tr, new StormPrepArchiveRecord(sourceId, source.Data.Name,
                                source.Data.PartSizeName, StormStructureMatching.Classify(source.Data.Name, source.Data.PartSizeName),
                                anchors[sourceId], fingerprint, source.Outputs, ownedMarkers));
                        }
                        // Acquire every write lock and finish native reads before erasure.
                        // A protected marker/layer must not fail only after points are gone.
                        var pointsToErase = plan.SourceIdsToArchiveAndErase
                            .Select(sourceId => (DBPoint)tr.GetObject(sources[sourceId].Id, OpenMode.ForWrite)).ToList();
                        var markersToErase = new List<BlockReference>();
                        foreach (StormCleanupMarkerMatch match in plan.MarkerMatches)
                        {
                            SourceInfo owner = sources[match.SourceId];
                            if (owner.Archive != null) ValidateArchivedOutputs(tr, owner.Archive);
                            var marker = tr.GetObject(markers[match.MarkerId], OpenMode.ForWrite, false) as BlockReference;
                            string expectedName = StormStructureMatching.Classify(owner.Data.Name, owner.Data.PartSizeName) == StormStructureRole.Access
                                ? "UFLS_MH_MARK" : "UFLS_DI_MARK";
                            if (marker == null || marker.IsErased ||
                                !string.Equals(GetEffectiveBlockName(marker, tr), expectedName, StringComparison.OrdinalIgnoreCase) ||
                                !string.Equals(marker.Layer, "V-SURV-CHCK", StringComparison.OrdinalIgnoreCase) ||
                                Distance2d(marker.Position, new Point3d(owner.Data.X, owner.Data.Y, 0.0)) > StormStructureMatching.MatchTolerance ||
                                InspectMarkerData(marker, out _) != StormMarkerOdState.Empty ||
                                (owner.Archive != null && !owner.Archive.MarkerHandles.Contains(match.MarkerId, StringComparer.OrdinalIgnoreCase)))
                                throw new InvalidOperationException($"Marker {match.MarkerId} no longer has verified ownership/data; cleanup rolled back.");
                            markersToErase.Add(marker);
                        }
                        // Archive/readback and all validation precede every source erase.
                        foreach (DBPoint point in pointsToErase) point.Erase(true);
                        foreach (BlockReference marker in markersToErase) marker.Erase(true);
                        tr.TransactionManager.QueueForGraphicsFlush();
                        tr.Commit();
                        cleanupCommitted = true;
                    }
                    if (managedPipeSummary.Length > 0) ed.WriteMessage(managedPipeSummary);
                    ed.WriteMessage($"\nCLV-GIS-STORM-CLEAN-VERIFIED: removed points={plan.SourceIdsToArchiveAndErase.Count}, owned markers={plan.MarkerMatches.Count}, retained live points={sources.Values.Count(s => s.Archive == null) - plan.SourceIdsToArchiveAndErase.Count}. Output ownership is archived in this drawing; null/unresolved/unrelated entities are retained. Review items={notes.Count}.");
                    foreach (string note in notes.Distinct(StringComparer.Ordinal)) ed.WriteMessage("\n  REVIEW: " + note);
                    try { ed.Regen(); ed.UpdateScreen(); }
                    catch (System.Exception ex) { ed.WriteMessage("\nDISPLAY WARNING: cleanup committed, but display refresh failed: " + ex.Message); }
                    return notes.Count == 0;
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nCLV-GIS-STORM-CLEAN-VERIFIED stopped: " + ex.Message +
                    (cleanupCommitted ? ". Cleanup had already committed; review the drawing result." : ". Cleanup transaction rolled back; no broad erasure was run."));
                return false;
            }
        }

        private static bool IsKnownPrepMarker(string name) =>
            string.Equals(name, "UFLS_DI_MARK", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "UFLS_MH_MARK", StringComparison.OrdinalIgnoreCase);

        private static StormMarkerOdState InspectMarkerData(BlockReference marker, out string detail)
        {
            var state = GisImportCommands.InspectObjectDataFingerprint(marker.ObjectId, out _, out detail);
            if (state == GisImportCommands.ObjectDataFingerprintState.ReadFailed) return StormMarkerOdState.Unreadable;
            if (state == GisImportCommands.ObjectDataFingerprintState.Present) return StormMarkerOdState.Present;
            using ResultBuffer? data = marker.XData;
            if (!marker.ExtensionDictionary.IsNull || marker.AttributeCollection.Count > 0 || (data?.AsArray().Length ?? 0) > 0)
            {
                detail = "Marker has additional instance data; retained for review.";
                return StormMarkerOdState.Present;
            }
            return StormMarkerOdState.Empty;
        }

        private static List<ObjectId> ReadUnverifiedOutputClaims(Transaction tr, DBPoint point)
        {
            if (point.ExtensionDictionary.IsNull) return new List<ObjectId>();
            var dictionary = (DBDictionary)tr.GetObject(point.ExtensionDictionary, OpenMode.ForRead);
            if (!dictionary.Contains(CompletionKey)) return new List<ObjectId>();
            var record = (Xrecord)tr.GetObject(dictionary.GetAt(CompletionKey), OpenMode.ForRead);
            using ResultBuffer? data = record.Data;
            // Even a corrupt/unverified claim competes for ownership; it cannot make
            // another source look unique merely because it was filtered out first.
            return (data?.AsArray() ?? Array.Empty<TypedValue>()).Where(v => v.Value is ObjectId).Select(v => (ObjectId)v.Value).ToList();
        }

        private static Point3d ReadPointCompletionAnchor(Transaction tr, DBPoint point)
        {
            var dictionary = (DBDictionary)tr.GetObject(point.ExtensionDictionary, OpenMode.ForRead);
            var record = (Xrecord)tr.GetObject(dictionary.GetAt(CompletionKey), OpenMode.ForRead);
            using ResultBuffer data = record.Data;
            TypedValue[] values = data.AsArray();
            return new Point3d((double)values[3].Value, (double)values[4].Value, point.Position.Z);
        }

        private static string ClaimHandle(ObjectId id)
        {
            try { return id.IsNull ? "INVALID" : id.Handle.ToString(); }
            catch { return "INVALID"; }
        }
    }
}
