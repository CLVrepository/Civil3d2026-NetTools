using System;
using System.Collections.Generic;
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
    /// Plan role-specific one-to-one matches before changing geometry.
    /// Copy and read back native OD before retiring source geometry; retain imported points.
    /// </summary>
    public static class GisStormStructureAuto
    {
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

        private const string CompletionKey = "CLV_STORM_PREP_V1";

        [CommandMethod("CLV-GIS-STRM-AUTO", CommandFlags.Modal)]
        public static void RunStormStructureAuto()
        {
            AcDocument? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc != null)
                RunVerified(doc);
        }

        // ALL calls this synchronously, so a failed preparation cannot leave cleanup queued.
        internal static bool RunVerified(AcDocument doc)
        {
            Editor ed = doc.Editor;
            Database db = doc.Database;
            var sources = new Dictionary<string, SourceInfo>(StringComparer.OrdinalIgnoreCase);
            var targets = new Dictionary<string, TargetInfo>(StringComparer.OrdinalIgnoreCase);
            var reviews = new List<string>();
            var invalidCompletions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int converted = 0;
            int alreadyVerified = 0;
            var preservedPipeEndNotices = new List<string>();
            try
            {
                using (doc.LockDocument())
                {
                    // Snapshot every source and destination before any block is exploded.
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
                        foreach (ObjectId id in space)
                        {
                            if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity ent || ent.IsErased)
                                continue;
                            string key = ent.Handle.ToString();
                            if (ent is DBPoint point && string.Equals(ent.Layer, StructuresPointLayer, StringComparison.OrdinalIgnoreCase))
                            {
                                bool read = GisImportCommands.TryReadStructuresIdentity(id, out string name, out string part, out string detail, out bool nativeReadFailure);
                                if (nativeReadFailure)
                                    throw new InvalidOperationException($"Read-only OD preflight stopped at point {key}: {detail}");
                                if (!read) { name = string.Empty; part = string.Empty; }
                                var source = new SourceInfo(id, new StormStructureSource(key, name, part, point.Position.X, point.Position.Y));
                                sources.Add(key, source);
                                if (!read)
                                    reviews.Add($"point {key}: {detail}");
                                if (TryReadCompletion(tr, point, name, part, out List<ObjectId> outputs, out string completionDetail))
                                {
                                    source.Outputs = outputs;
                                }
                                else if (!string.IsNullOrEmpty(completionDetail))
                                {
                                    invalidCompletions.Add(key);
                                    reviews.Add($"point {key} ({name}): {completionDetail}; original point retained");
                                }
                                continue;
                            }
                            if (string.Equals(ent.Layer, StructuresPointLayer, StringComparison.OrdinalIgnoreCase))
                            {
                                reviews.Add($"source {key}: unsupported imported entity type {ent.GetType().Name}; only DBPoint sources are converted, entity retained");
                                continue;
                            }
                            if (ent is BlockReference br)
                            {
                                string kind = GetSupportedStormStructureBlockKind(br, tr);
                                if (string.IsNullOrEmpty(kind))
                                    continue;
                                Point3d center = GetPreferredBlockCenter(br, tr);
                                var role = kind == "DI" ? StormStructureRole.DropInlet : StormStructureRole.Access;
                                targets.Add(key, new TargetInfo(id, new StormStructureTarget(key, role, center.X, center.Y), false));
                            }
                            else if (ent is AcPolyline pl && IsStructureOutline(pl) && IsOuterLayer(pl.Layer))
                            {
                                // Only genuinely OD-empty survey outlines are unbound.
                                // Any existing native identity binds their role; conflicts stay review-only.
                                var odState = GisImportCommands.InspectOutlineObjectData(id, out string existingName,
                                    out string existingPart, out string existingDetail);
                                if (odState == GisImportCommands.OutlineObjectDataState.ReadFailed)
                                    throw new InvalidOperationException($"Read-only OD preflight stopped at outline {key}: {existingDetail}");
                                if (odState == GisImportCommands.OutlineObjectDataState.Review)
                                {
                                    reviews.Add($"outline {key}: {existingDetail}");
                                    continue;
                                }
                                StormStructureRole outlineRole = odState == GisImportCommands.OutlineObjectDataState.Identified
                                    ? StormStructureMatching.Classify(existingName, existingPart) : StormStructureRole.Unknown;
                                if (odState == GisImportCommands.OutlineObjectDataState.Identified &&
                                    outlineRole != StormStructureRole.DropInlet && outlineRole != StormStructureRole.JunctionBox)
                                {
                                    reviews.Add($"outline {key}: existing OD role requires review, Name='{existingName}', PartSizeName='{existingPart}'; retained unchanged");
                                    continue;
                                }
                                if (odState == GisImportCommands.OutlineObjectDataState.Empty &&
                                    string.Equals(pl.Layer, TargetOuterLayer, StringComparison.OrdinalIgnoreCase))
                                {
                                    reviews.Add($"outline {key}: final-layer outline has no OD; retained for review rather than reassigned");
                                    continue;
                                }
                                Point3d center = GetEntityCenter(pl);
                                targets.Add(key, new TargetInfo(id, new StormStructureTarget(key, outlineRole, center.X, center.Y, GetStructureFootprint(pl), IsExistingOutline: true), false));
                            }
                        }
                        // Every persisted output belongs to one logical completed structure.
                        // Check all primary/secondary claims before hiding any ordinary candidate.
                        StormStructureCompletionPlan completions = StormStructureMatching.PlanCompletions(
                            sources.Values.Where(source => source.Outputs.Count > 0).Select(source =>
                                new StormStructureCompletion(source.Data.Id, source.Outputs.Select(id =>
                                    ((Entity)tr.GetObject(id, OpenMode.ForRead)).Handle.ToString()).ToArray())));
                        foreach (string sourceId in completions.ConflictedSourceIds)
                            invalidCompletions.Add(sourceId);
                        reviews.AddRange(completions.Issues.Select(issue => DescribeIssue(issue, sources)));
                        foreach (StormStructureCompletion completion in completions.Accepted)
                        {
                            SourceInfo source = sources[completion.SourceId];
                            // Secondary DI outer footprints must not reappear as fresh targets.
                            foreach (string outputHandle in completion.OutputIds)
                                targets.Remove(outputHandle);
                            ObjectId outerId = source.Outputs[0];
                            var outer = (Entity)tr.GetObject(outerId, OpenMode.ForRead);
                            string key = outer.Handle.ToString();
                            var role = StormStructureMatching.Classify(source.Data.Name, source.Data.PartSizeName);
                            Point3d center = role == StormStructureRole.JunctionBox ? GetEntityCenter(outer)
                                : new Point3d(source.Data.X, source.Data.Y, 0.0);
                            IReadOnlyList<StormStructureVertex>? footprint = role == StormStructureRole.JunctionBox && outer is AcPolyline box
                                ? GetStructureFootprint(box) : null;
                            targets[key] = new TargetInfo(outerId, new StormStructureTarget(key, role, center.X, center.Y, footprint), true)
                            { CompletedSource = source.Data.Id };
                        }
                        tr.Commit();
                    }

                    StormStructureMatchResult plan = StormStructureMatching.Match(
                        sources.Values.Select(s => s.Data), targets.Values.Select(t => t.Data));
                    reviews.AddRange(plan.Issues.Select(issue => DescribeIssue(issue, sources)));
                    foreach (string sourceId in plan.PreservedPipeEndSourceIds)
                    {
                        if (!invalidCompletions.Contains(sourceId))
                            preservedPipeEndNotices.Add(DescribeSource(sources[sourceId]));
                    }
                    LayerStandards.EnsureGisLayers(db, ed);
                    var reserved = new HashSet<ObjectId>();
                    // Resolve existing DI/box outlines while access/DI blocks remain intact.
                    foreach (StormStructureMatch match in plan.Matches.OrderBy(m => targets[m.TargetId].Data.IsExistingOutline ? 0 : 1))
                    {
                        SourceInfo source = sources[match.SourceId];
                        TargetInfo target = targets[match.TargetId];
                        if (invalidCompletions.Contains(source.Data.Id))
                            continue;
                        if (source.Outputs.Count > 0 && (!target.IsCompleted || target.CompletedSource != source.Data.Id))
                        {
                            reviews.Add($"point {source.Data.Id}: existing completion cannot be reassigned; review its linked output");
                            continue;
                        }
                        if (target.IsCompleted && target.CompletedSource != source.Data.Id)
                        {
                            reviews.Add($"point {source.Data.Id}: destination belongs to another completed source");
                            continue;
                        }
                        if (target.IsCompleted)
                        {
                            bool verified = true;
                            foreach (ObjectId output in source.Outputs)
                            {
                                var status = GisImportCommands.CopyObjectDataVerified(source.Id, output, out string detail);
                                if (!IsVerified(status))
                                {
                                    verified = false;
                                    reviews.Add($"point {source.Data.Id} ({source.Data.Name}): existing output {status}: {detail}");
                                }
                            }
                            if (verified)
                                alreadyVerified++;
                            continue;
                        }
                        if (TryConvert(doc, source, target, reserved, out string failure))
                            converted++;
                        else
                            reviews.Add($"point {source.Data.Id} ({source.Data.Name}) -> {target.Data.Id}: {failure}");
                    }
                }
                if (converted > 0 || alreadyVerified > 0)
                {
                    try { ed.Regen(); }
                    catch (System.Exception ex) { ed.WriteMessage($"\nStorm GIS display refresh failed: {ex.Message}. Run REGEN to refresh the view."); }
                }
                ed.WriteMessage($"\nCLV-GIS-STRM-AUTO revision 2026.10.06-R3: converted/OD verified={converted}, already verified={alreadyVerified}, review items={reviews.Count}, intentionally retained null pipe ends={preservedPipeEndNotices.Count}. All {sources.Count} Structures source points retained; no broad cleanup run. Planned targets: total={targets.Count}, existing outlines={targets.Values.Count(t => t.Data.IsExistingOutline)}.");
                foreach (string preserved in preservedPipeEndNotices)
                    ed.WriteMessage("\n  PRESERVED NULL PIPE END (no geometry expected): " + preserved);
                foreach (string review in reviews.Distinct(StringComparer.Ordinal))
                    ed.WriteMessage("\n  REVIEW: " + review);
                return reviews.Count == 0 && sources.Count > 0;
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-STRM-AUTO revision 2026.10.06-R3 stopped: {ex.Message}. Unverified source geometry and all imported source points are retained. No downstream cleanup was queued.");
                return false;
            }
        }

        private static bool TryConvert(AcDocument doc, SourceInfo source, TargetInfo target,
            HashSet<ObjectId> reserved, out string detail)
        {
            detail = string.Empty;
            try
            {
                Database db = doc.Database;
                using Transaction tr = db.TransactionManager.StartTransaction();
                var original = tr.GetObject(target.Id, OpenMode.ForRead, false) as Entity;
                if (original == null || original.IsErased || reserved.Contains(target.Id))
                {
                    detail = "destination unavailable or already reserved";
                    return false;
                }
                EnsureLayer(db, tr, TargetOuterLayer);
                EnsureLayer(db, tr, TargetInnerLayer);
                var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
                var created = new List<ObjectId>();
                var outputs = new List<ObjectId>();
                ObjectId innerId = ObjectId.Null;
                var center = new Point3d(source.Data.X, source.Data.Y, 0.0);
                if (target.Data.IsExistingOutline)
                {
                    if (original is not AcPolyline outer || !IsStructureOutline(outer))
                    {
                        detail = "expected a closed straight existing structure outline";
                        return false;
                    }
                    if (!GisImportCommands.CanReplaceOutlineObjectData(source.Id, target.Id, out detail))
                        return false;
                    // Ambiguous nested linework is reviewed instead of choosing by enumeration order.
                    var inners = new List<ObjectId>();
                    foreach (ObjectId id in space)
                    {
                        if (id == target.Id || reserved.Contains(id)) continue;
                        if (tr.GetObject(id, OpenMode.ForRead, false) is AcPolyline pl && !pl.IsErased &&
                            IsStructureOutline(pl) && IsInnerLayer(pl.Layer) &&
                            Math.Abs(pl.Area) < Math.Abs(outer.Area) && IsInsideStructureOutline(outer, pl))
                            inners.Add(id);
                    }
                    if (inners.Count > 1)
                    {
                        detail = "multiple nested inner structure outlines; review required";
                        return false;
                    }
                    if (inners.Count == 1) innerId = inners[0];
                    var clone = (Entity)outer.Clone();
                    clone.Layer = TargetOuterLayer;
                    space.AppendEntity(clone);
                    tr.AddNewlyCreatedDBObject(clone, true);
                    created.Add(clone.ObjectId);
                    outputs.Add(clone.ObjectId);
                }
                else
                {
                    if (original is not BlockReference block)
                    {
                        detail = "expected supported source block";
                        return false;
                    }
                    int count = 0;
                    ExplodeRecursive(block, space, tr, created, ref count);
                    foreach (ObjectId id in created)
                    {
                        if (tr.GetObject(id, OpenMode.ForWrite, false) is not Entity ent || ent.IsErased) continue;
                        if (ShouldEraseExplodedEntity(ent)) { ent.Erase(true); continue; }
                        if (MatchesAnyToken(ent.Layer, SourceInnerLayerTokens)) ent.Layer = TargetInnerLayer;
                        else if (MatchesAnyToken(ent.Layer, SourceOuterLayerTokens)) ent.Layer = TargetOuterLayer;
                        if (string.Equals(ent.Layer, TargetOuterLayer, StringComparison.OrdinalIgnoreCase) &&
                            IsRoleOutline(ent, target.Data.Role) &&
                            OutlineMatchesAnchor(ent, target.Data.Role, center))
                            outputs.Add(id);
                    }
                    if (outputs.Count == 0 || (target.Data.Role == StormStructureRole.Access && outputs.Count != 1))
                    {
                        detail = $"expected verified role-specific outer outline(s), one for access; found {outputs.Count}";
                        return false;
                    }
                }
                // This is synchronous native OD copy + full readback, never 'helper queued'.
                foreach (ObjectId output in outputs)
                {
                    var status = GisImportCommands.CopyObjectDataVerified(source.Id, output, out detail);
                    if (!IsVerified(status))
                    {
                        detail = $"OD {status}: {detail}; conversion rolled back";
                        return false;
                    }
                }
                // Only after successful readback may original block/outer outline be retired.
                original.UpgradeOpen();
                original.Erase(true);
                if (!innerId.IsNull)
                    ((Entity)tr.GetObject(innerId, OpenMode.ForWrite)).Layer = TargetInnerLayer;
                WriteCompletion(tr, source, outputs);
                tr.Commit();
                reserved.Add(target.Id);
                if (!innerId.IsNull) reserved.Add(innerId);
                return true;
            }
            catch (System.Exception ex)
            {
                detail = ex.Message + "; conversion rolled back";
                return false;
            }
        }

        private static bool IsVerified(GisImportCommands.ObjectDataCopyStatus status) =>
            status == GisImportCommands.ObjectDataCopyStatus.CopiedVerified ||
            status == GisImportCommands.ObjectDataCopyStatus.AlreadyEquivalent;

        private static void WriteCompletion(Transaction tr, SourceInfo source, List<ObjectId> outputs)
        {
            var point = (Entity)tr.GetObject(source.Id, OpenMode.ForWrite);
            if (point.ExtensionDictionary.IsNull) point.CreateExtensionDictionary();
            var dictionary = (DBDictionary)tr.GetObject(point.ExtensionDictionary, OpenMode.ForWrite);
            var record = new Xrecord();
            dictionary.SetAt(CompletionKey, record);
            tr.AddNewlyCreatedDBObject(record, true);
            var data = new List<TypedValue>
            {
                new TypedValue((int)DxfCode.Text, "1"),
                new TypedValue((int)DxfCode.Text, source.Data.Name),
                new TypedValue((int)DxfCode.Text, source.Data.PartSizeName),
                new TypedValue((int)DxfCode.Real, source.Data.X),
                new TypedValue((int)DxfCode.Real, source.Data.Y)
            };
            data.AddRange(outputs.Select(id => new TypedValue((int)DxfCode.SoftPointerId, id)));
            record.Data = new ResultBuffer(data.ToArray());
        }

        private static bool TryReadCompletion(Transaction tr, Entity source, string name, string part,
            out List<ObjectId> outputs, out string detail)
        {
            outputs = new List<ObjectId>();
            detail = string.Empty;
            if (source.ExtensionDictionary.IsNull) return false;
            var dictionary = (DBDictionary)tr.GetObject(source.ExtensionDictionary, OpenMode.ForRead);
            if (!dictionary.Contains(CompletionKey)) return false;
            bool nativeReadFailure = false;
            try
            {
                var record = (Xrecord)tr.GetObject(dictionary.GetAt(CompletionKey), OpenMode.ForRead);
                using ResultBuffer? buffer = record.Data;
                TypedValue[] values = buffer?.AsArray() ?? Array.Empty<TypedValue>();
                if (values.Length < 6 || (string)values[0].Value != "1" ||
                    !string.Equals(values[1].Value as string, name, StringComparison.Ordinal) ||
                    !string.Equals(values[2].Value as string, part, StringComparison.Ordinal))
                    throw new InvalidOperationException("completion identity changed or unreadable");
                if (source is not DBPoint point || values[3].Value is not double x || values[4].Value is not double y ||
                    Distance2d(point.Position, new Point3d(x, y, 0.0)) > StormStructureMatching.MatchTolerance)
                    throw new InvalidOperationException("completed source point moved; review its linked output");
                var anchor = new Point3d(x, y, 0.0);
                foreach (TypedValue value in values.Skip(5))
                {
                    if (value.Value is not ObjectId id || id.IsNull || !id.IsValid || id.IsErased ||
                        tr.GetObject(id, OpenMode.ForRead, false) is not Entity ent || ent.IsErased ||
                        !string.Equals(ent.Layer, TargetOuterLayer, StringComparison.OrdinalIgnoreCase) ||
                        !IsRoleOutline(ent, StormStructureMatching.Classify(name, part)) ||
                        !OutlineMatchesAnchor(ent, StormStructureMatching.Classify(name, part), anchor))
                        throw new InvalidOperationException("completed output is missing or changed");
                    bool outputRead = GisImportCommands.TryReadStructuresIdentity(id, out string outputName,
                        out string outputPart, out string readDetail, out nativeReadFailure);
                    if (nativeReadFailure)
                        throw new InvalidOperationException($"Read-only OD preflight stopped at completed output {ent.Handle}: {readDetail}");
                    if (!outputRead || !string.Equals(outputName, name, StringComparison.Ordinal) || !string.Equals(outputPart, part, StringComparison.Ordinal))
                        throw new InvalidOperationException("completed output OD changed: " + readDetail);
                    outputs.Add(id);
                }
                return true;
            }
            catch (System.Exception ex) when (!nativeReadFailure)
            {
                outputs.Clear();
                detail = ex.Message;
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

        private static bool IsOuterLayer(string layer) =>
            MatchesAnyToken(layer, SourceOuterLayerTokens) || string.Equals(layer, TargetOuterLayer, StringComparison.OrdinalIgnoreCase);

        private static bool IsInnerLayer(string layer) =>
            MatchesAnyToken(layer, SourceInnerLayerTokens) || string.Equals(layer, TargetInnerLayer, StringComparison.OrdinalIgnoreCase);

        private static bool IsStructureOutline(AcPolyline pl)
        {
            // Surveyed structure walls are not necessarily perfectly orthogonal.
            // Keep the trusted layer/role checks and require closed, straight, nonzero geometry.
            if (!pl.Closed || pl.NumberOfVertices < 3 || Math.Abs(pl.Area) <= 1e-8) return false;
            for (int i = 0; i < pl.NumberOfVertices; i++)
                if (Math.Abs(pl.GetBulgeAt(i)) > 1e-8) return false;
            return true;
        }

        private static IReadOnlyList<StormStructureVertex> GetStructureFootprint(AcPolyline pl)
        {
            var vertices = new List<StormStructureVertex>();
            for (int i = 0; i < pl.NumberOfVertices; i++)
            {
                Point3d point = pl.GetPoint3dAt(i);
                vertices.Add(new StormStructureVertex(point.X, point.Y));
            }
            return vertices;
        }

        private static bool IsInsideStructureOutline(AcPolyline outer, AcPolyline inner)
            => StormStructureMatching.ContainsOutline(GetStructureFootprint(outer), GetStructureFootprint(inner));

        private static bool IsRoleOutline(Entity ent, StormStructureRole role)
        {
            if (role == StormStructureRole.Access)
            {
                if (ent is Circle circle) return circle.Radius > 0.20;
                // Existing circular blocks can explode to a closed bulged polyline.
                if (ent is AcPolyline round && round.Closed && round.NumberOfVertices >= 2)
                {
                    for (int i = 0; i < round.NumberOfVertices; i++)
                        if (Math.Abs(round.GetBulgeAt(i)) <= 1e-8) return false;
                    return Math.Abs(round.Area) > 0.1;
                }
                return false;
            }
            if (role == StormStructureRole.JunctionBox) return ent is AcPolyline box && IsStructureOutline(box);
            if (role == StormStructureRole.DropInlet) return ent is AcPolyline inlet && inlet.Closed && Math.Abs(inlet.Area) > 1e-8;
            return false;
        }

        private static bool OutlineMatchesAnchor(Entity ent, StormStructureRole role, Point3d anchor)
        {
            if (role == StormStructureRole.JunctionBox)
                return ent is AcPolyline box &&
                    Distance2d(GetEntityCenter(box), anchor) <= StormStructureMatching.JunctionSearchRadius &&
                    StormStructureMatching.ContainsFootprint(GetStructureFootprint(box), anchor.X, anchor.Y);
            if (role != StormStructureRole.DropInlet)
                return Distance2d(GetEntityCenter(ent), anchor) <= StormStructureMatching.MatchTolerance;
            if (ent is not AcPolyline pl || !pl.Closed) return false;
            // DI_CENTER is the design anchor; an asymmetric inlet need not have a
            // bounding-box center at that anchor. Retain the established footprint test.
            if (Distance2d(pl.GetClosestPointTo(anchor, false), anchor) <= StormStructureMatching.MatchTolerance) return true;
            bool inside = false;
            for (int i = 0, j = pl.NumberOfVertices - 1; i < pl.NumberOfVertices; j = i++)
            {
                Point2d a = pl.GetPoint2dAt(i);
                Point2d b = pl.GetPoint2dAt(j);
                if ((a.Y > anchor.Y) != (b.Y > anchor.Y) &&
                    anchor.X < (b.X - a.X) * (anchor.Y - a.Y) / (b.Y - a.Y) + a.X)
                    inside = !inside;
            }
            return inside;
        }

        private static Point3d GetEntityCenter(Entity ent)
        {
            if (ent is Circle circle) return circle.Center;
            Extents3d ext = ent.GeometricExtents;
            return new Point3d((ext.MinPoint.X + ext.MaxPoint.X) * 0.5, (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5, 0.0);
        }

        private static double Distance2d(Point3d a, Point3d b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static string DescribeIssue(StormStructureIssue issue, IReadOnlyDictionary<string, SourceInfo> sources)
            => $"{issue.Code}: sources=[{string.Join("; ", issue.SourceIds.Select(id => sources.TryGetValue(id, out SourceInfo? source) ? DescribeSource(source) : id))}], targets=[{string.Join(",", issue.TargetIds)}]: {issue.Message}";

        private static string DescribeSource(SourceInfo source)
            => FormattableString.Invariant($"{source.Data.Id} Name='{source.Data.Name}' PartSizeName='{source.Data.PartSizeName}' XY=({source.Data.X:R},{source.Data.Y:R})");

        private sealed class SourceInfo
        {
            internal SourceInfo(ObjectId id, StormStructureSource data) { Id = id; Data = data; }
            internal ObjectId Id { get; }
            internal StormStructureSource Data { get; }
            internal List<ObjectId> Outputs { get; set; } = new List<ObjectId>();
        }

        private sealed class TargetInfo
        {
            internal TargetInfo(ObjectId id, StormStructureTarget data, bool completed) { Id = id; Data = data; IsCompleted = completed; }
            internal ObjectId Id { get; }
            internal StormStructureTarget Data { get; }
            internal bool IsCompleted { get; }
            internal string? CompletedSource { get; set; }
        }
    }
}
