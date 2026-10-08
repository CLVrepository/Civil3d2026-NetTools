using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using AcDocument = Autodesk.AutoCAD.ApplicationServices.Document;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using static CLV_CivilTools.Gis.GisNewDrawingAppearance;

namespace CLV_CivilTools.Gis
{
    /// <summary>
    /// Read-only source capture and native, same-coordinate cloning for the session
    /// new-drawing command. No explode, source edits, coordinate transformation,
    /// inferred OD repair, SaveAs or destination cleanup occurs here. The caller
    /// MUST discard the unsaved destination if any stage throws, including readback.
    /// </summary>
    internal static class GisNewDrawingClone
    {
        // Match complete layer names, optionally followed by the survey ~~ suffix.
        // Do not use substring matching: xref prefixes and unrelated layers are out.
        private static readonly HashSet<string> StructuralLayers = new(StringComparer.OrdinalIgnoreCase)
        {
            "V-SURV-STRC-INNR-2D", "V-SURV-STRC-INNER-2D",
            "V-SURV-OUTR-2D", "V-SURV-STRC-OUTR-2D", "V-SURV-STRC-OUTER-2D"
        };
        // Kept identical to GisStormStructureAuto's supported effective names.
        private static readonly HashSet<string> StructureBlocks = new(StringComparer.OrdinalIgnoreCase)
        {
            "TYPE_A-USD_411", "TYPE_A_MOD-USD_411.1", "TYPE_C-USD_413",
            "TYPE_CM-USD_422", "TYPE_CM2-USD_412.1", "TYPE_D-USD_414",
            "TYPE_DM2-USD_412.1", "UFLS-GIS-MH-CIRCULAR"
        };
        private static readonly HashSet<string> MarkerBlocks = new(StringComparer.OrdinalIgnoreCase)
        { "UFLS_DI_MARK", "UFLS_MH_MARK" };

        internal sealed class Snapshot
        {
            internal Snapshot(Database database, ObjectId modelSpaceId, IEnumerable<EntityState> entities,
                IEnumerable<DefinitionState> definitions, IEnumerable<ResourceState> resources, IEnumerable<EntityState> dependencies)
            {
                Database = database;
                ModelSpaceId = modelSpaceId;
                Entities = Array.AsReadOnly(entities.ToArray());
                Definitions = Array.AsReadOnly(definitions.ToArray());
                Resources = Array.AsReadOnly(resources.ToArray());
                DependencyEntities = Array.AsReadOnly(dependencies.ToArray());
            }
            internal Database Database { get; }
            internal ObjectId ModelSpaceId { get; }
            internal IReadOnlyList<EntityState> Entities { get; }
            internal IReadOnlyList<DefinitionState> Definitions { get; }
            internal IReadOnlyList<ResourceState> Resources { get; }
            internal IReadOnlyList<EntityState> DependencyEntities { get; }
            internal int SelectedCount => Entities.Count;
        }

        internal sealed class Result
        {
            internal Result(IDictionary<ObjectId, ObjectId> ids)
                => Ids = new ReadOnlyDictionary<ObjectId, ObjectId>(new Dictionary<ObjectId, ObjectId>(ids));
            internal IReadOnlyDictionary<ObjectId, ObjectId> Ids { get; }
            internal int CopiedCount => Ids.Count;
        }

        // Only immutable CLR values/ObjectIds survive a transaction. No borrowed
        // DBObject, AttributeReference, Map Records or MapValue wrapper is retained.
        internal sealed record EntityState(ObjectId Id, string Handle, string Kind, string Geometry,
            GisImportCommands.ObjectDataFingerprintState OdState, string OdFingerprint, BlockReferenceState? Reference);
        internal sealed record BlockReferenceState(ObjectId BlockTableRecord, ObjectId DynamicBlockTableRecord, bool IsDynamicBlock);
        internal sealed record DefinitionState(ObjectId Id, string Handle, string Name, bool IsAnonymous, IReadOnlyList<ObjectId> Children);
        internal sealed record ResourceState(ObjectId Id, string Handle, string Kind, string Name, IReadOnlyList<ResourceProperty> Properties);
        private sealed record DefinitionClone(DefinitionState Source, ObjectId DestinationId, IReadOnlyList<ObjectId> Children);

        internal static Snapshot Capture(AcDocument source)
        {
            ArgumentNullException.ThrowIfNull(source);
            using var context = new ActiveContext(source);
            using (source.LockDocument())
            using (Transaction tr = source.Database.TransactionManager.StartTransaction())
            {
                ObjectId modelSpace = ModelSpace(source.Database, tr);
                var selected = new List<EntityState>();
                var definitions = new Dictionary<ObjectId, DefinitionState>();
                var resources = new Dictionary<ObjectId, ResourceState>();
                var dependencies = new Dictionary<ObjectId, EntityState>();
                foreach (ObjectId id in (BlockTableRecord)tr.GetObject(modelSpace, OpenMode.ForRead))
                {
                    if (id.IsErased) continue;
                    if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity entity) continue;
                    string kind = SelectKind(entity, tr);
                    if (kind.Length == 0) continue;
                    try
                    {
                        RejectCompletionLinks(entity, tr);
                        selected.Add(ReadEntity(entity, kind, tr));
                        CollectDependencies(entity, tr, definitions, resources, dependencies, new HashSet<ObjectId>(), 0);
                    }
                    catch (System.Exception ex)
                    {
                        throw Failure($"Source handle {id.Handle}: capture of {kind} failed", ex);
                    }
                }
                // Read-only transaction; deliberately do not upgrade/open for write.
                tr.Commit();
                return new Snapshot(source.Database, modelSpace, selected, definitions.Values, resources.Values, dependencies.Values);
            }
        }

        // Reuse the established method-specific appearance/geometry readers for
        // durable sewer ownership checks without changing the clone workflow.
        internal static string ReadPrimitiveSignature(Entity entity, Transaction transaction)
        {
            if (entity is not Line && entity is not Circle)
                throw new InvalidOperationException("Primitive signature requires a LINE or CIRCLE.");
            return GeometrySignature(entity, transaction);
        }

        internal static Result CloneAndVerify(AcDocument source, AcDocument destination, Snapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(destination);
            ArgumentNullException.ThrowIfNull(snapshot);
            if (source == destination || source.Database == destination.Database || snapshot.Database != source.Database)
                throw new InvalidOperationException("Clone requires the captured source and a different destination drawing.");
            using var originalContext = new ActiveContext(source);
            using (source.LockDocument())
            using (destination.LockDocument())
            {
                VerifySource(source, snapshot);
                ObjectId destinationModelSpace;
                using (Transaction sourceTr = source.Database.TransactionManager.StartTransaction())
                using (Transaction targetTr = destination.Database.TransactionManager.StartTransaction())
                {
                    destinationModelSpace = ModelSpace(destination.Database, targetTr);
                    RequireEmptyModelSpace(destinationModelSpace, targetTr);
                    PreflightDependencies(snapshot, sourceTr, destination.Database, targetTr);
                    targetTr.Commit();
                    sourceTr.Commit();
                }

                var copies = new Dictionary<ObjectId, ObjectId>();
                var dependencyCopies = new Dictionary<ObjectId, ObjectId>();
                var definitionCopies = new Dictionary<ObjectId, DefinitionClone>();
                if (snapshot.SelectedCount > 0)
                {
                    using var mapping = new IdMapping();
                    try
                    {
                        // Autodesk defines Ignore as reuse of destination duplicate
                        // records. Named block collisions were rejected; anonymous
                        // definitions must map to new records with exact reference
                        // targets, even if native generated names differ. Only
                        // compatible layers/text styles and built-in ByLayer/ByBlock/
                        // Continuous linetypes may be reused.
                        // Never use Replace (changes template) or MangleName (layers).
                        source.Database.WblockCloneObjects(
                            new ObjectIdCollection(snapshot.Entities.Select(entity => entity.Id).ToArray()),
                            destinationModelSpace, mapping, DuplicateRecordCloning.Ignore, false);
                        VerifyMapping(snapshot, destination.Database, destinationModelSpace, mapping, copies, dependencyCopies, definitionCopies);
                    }
                    catch (System.Exception ex)
                    {
                        throw Failure("Native clone/IdMapping validation failed; source handles " +
                            string.Join(", ", snapshot.Entities.Select(entity => entity.Handle)), ex);
                    }
                }

                // Source is still read-only and locked. Recheck against the immutable
                // capture before switching Map projects for destination OD readback.
                VerifySource(source, snapshot);
                using (var destinationContext = new ActiveContext(destination))
                using (Transaction tr = destination.Database.TransactionManager.StartTransaction())
                {
                    var actualIds = new HashSet<ObjectId>();
                    foreach (ObjectId id in (BlockTableRecord)tr.GetObject(destinationModelSpace, OpenMode.ForRead))
                        if (!id.IsErased) actualIds.Add(id);
                    if (!actualIds.SetEquals(copies.Values))
                        throw new InvalidOperationException("Destination ModelSpace differs from the selected clone set. Unexpected handles: " +
                            string.Join(", ", actualIds.Except(copies.Values).Select(id => id.Handle.ToString())));
                    foreach (DefinitionClone definition in definitionCopies.Values)
                    {
                        var actual = (BlockTableRecord)tr.GetObject(definition.DestinationId, OpenMode.ForRead);
                        RequireDefinitionIdentity(definition.Source, actual);
                        if (!definition.Children.SequenceEqual(actual.Cast<ObjectId>()))
                            throw new InvalidOperationException($"Source block '{definition.Source.Name}', handle {definition.Source.Handle} -> " +
                                $"destination handle {definition.DestinationId.Handle}: child topology/order changed after destination activation.");
                    }
                    VerifyDestinationResources(snapshot, destination.Database, tr);
                    foreach (EntityState expected in snapshot.Entities)
                    {
                        ObjectId destinationId = copies[expected.Id];
                        try
                        {
                            var entity = tr.GetObject(destinationId, OpenMode.ForRead, false) as Entity
                                ?? throw new InvalidOperationException("Mapped destination is not an Entity.");
                            EntityState actual = ReadDestinationEntity(entity, expected, tr, definitionCopies);
                            RequireSame(expected, actual, $"Source handle {expected.Handle} -> destination handle {destinationId.Handle}");
                        }
                        catch (System.Exception ex)
                        {
                            throw Failure($"Source handle {expected.Handle} -> destination handle {destinationId.Handle}: clone readback failed", ex);
                        }
                    }
                    foreach (EntityState expected in snapshot.DependencyEntities)
                    {
                        ObjectId id = dependencyCopies[expected.Id];
                        try
                        {
                            EntityState actual = ReadDestinationEntity((Entity)tr.GetObject(id, OpenMode.ForRead), expected, tr, definitionCopies);
                            RequireSame(expected, actual, $"Source dependency handle {expected.Handle} -> destination handle {id.Handle}");
                        }
                        catch (System.Exception ex)
                        {
                            throw Failure($"Source dependency handle {expected.Handle} -> destination handle {id.Handle}: geometry/OD readback failed", ex);
                        }
                    }
                    tr.Commit();
                }
                return new Result(copies);
            }
        }

        private static void VerifySource(AcDocument source, Snapshot snapshot)
        {
            if (AcadApp.DocumentManager.MdiActiveDocument != source || HostApplicationServices.WorkingDatabase != source.Database)
                throw new InvalidOperationException("Source verification requires matching active and working databases.");
            using Transaction tr = source.Database.TransactionManager.StartTransaction();
            var selectedNow = new HashSet<ObjectId>();
            foreach (ObjectId id in (BlockTableRecord)tr.GetObject(snapshot.ModelSpaceId, OpenMode.ForRead))
            {
                if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead, false) is Entity entity && SelectKind(entity, tr).Length > 0)
                    selectedNow.Add(id);
            }
            if (!selectedNow.SetEquals(snapshot.Entities.Select(entity => entity.Id)))
                throw new InvalidOperationException("Source selection changed since capture. Added/removed handles: " +
                    string.Join(", ", selectedNow.SymmetricDifference(snapshot.Entities.Select(entity => entity.Id)).Select(id => id.Handle.ToString())));
            foreach (EntityState expected in snapshot.Entities)
            {
                try
                {
                    var entity = tr.GetObject(expected.Id, OpenMode.ForRead, false) as Entity
                        ?? throw new InvalidOperationException("Source no longer exists as an Entity.");
                    if (entity.OwnerId != snapshot.ModelSpaceId) throw new InvalidOperationException("Source owner changed.");
                    RejectCompletionLinks(entity, tr);
                    RequireSameSource(expected, ReadEntity(entity, expected.Kind, tr), "Source handle " + expected.Handle);
                }
                catch (System.Exception ex) { throw Failure("Source handle " + expected.Handle + ": immutable capture no longer matches", ex); }
            }
            foreach (DefinitionState definition in snapshot.Definitions)
            {
                var current = (BlockTableRecord)tr.GetObject(definition.Id, OpenMode.ForRead, false);
                if (current.Name != definition.Name || current.IsAnonymous != definition.IsAnonymous ||
                    current.IsFromExternalReference || current.IsFromOverlayReference || current.IsLayout ||
                    !definition.Children.SequenceEqual(current.Cast<ObjectId>()))
                    throw new InvalidOperationException($"Source block '{definition.Name}', handle {definition.Handle}: dependency definition changed since capture.");
            }
            foreach (EntityState expected in snapshot.DependencyEntities)
            {
                try
                {
                    var current = (Entity)tr.GetObject(expected.Id, OpenMode.ForRead, false);
                    RequireSameSource(expected, ReadEntity(current, expected.Kind, tr), "Source dependency handle " + expected.Handle);
                }
                catch (System.Exception ex) { throw Failure("Source dependency handle " + expected.Handle + ": capture changed", ex); }
            }
            foreach (ResourceState resource in snapshot.Resources)
            {
                ResourceState current = ReadResource((SymbolTableRecord)tr.GetObject(resource.Id, OpenMode.ForRead), tr);
                IReadOnlyList<string> differences = FindResourceDifferences(resource.Properties, current.Properties, strictSource: true);
                if (differences.Count != 0)
                    throw new InvalidOperationException($"Source {resource.Kind} '{resource.Name}', handle {resource.Handle}: resource changed since capture. " +
                        ResourceDifferences(differences));
            }
            tr.Commit();
        }

        private static void RequireSameSource(EntityState expected, EntityState actual, string context)
        {
            if (expected.Reference != actual.Reference)
                throw new InvalidOperationException(context + ": source block definition targets or dynamic status changed since capture.");
            RequireSame(expected, actual, context);
        }

        private static void RequireSame(EntityState expected, EntityState actual, string context)
        {
            if (expected.Geometry != actual.Geometry)
                throw new InvalidOperationException(context + ": geometry, layer, block transform, dynamic property or attribute differs; no coordinate/Z change is permitted.");
            if (expected.OdState != actual.OdState || expected.OdFingerprint != actual.OdFingerprint)
                throw new InvalidOperationException(context + ": complete typed native Object Data differs or is missing. Native OD tables/records are not assumed to survive WblockCloneObjects.");
        }

        private static EntityState ReadEntity(Entity entity, string kind, Transaction tr, string? verifiedAnonymousName = null)
        {
            string geometry = ReadAt($"Entity handle {entity.Handle}: geometry snapshot", () => GeometrySignature(entity, tr, verifiedAnonymousName));
            var state = GisImportCommands.InspectObjectDataFingerprint(entity.ObjectId, out string fingerprint, out string detail);
            if (state == GisImportCommands.ObjectDataFingerprintState.ReadFailed)
                throw new InvalidOperationException($"Handle {entity.Handle}: native OD cannot be verified: {detail}");
            return new EntityState(entity.ObjectId, entity.Handle.ToString(), kind, geometry, state, fingerprint, ReadReference(entity));
        }

        private static BlockReferenceState? ReadReference(Entity entity)
            => entity is BlockReference block ? new BlockReferenceState(block.BlockTableRecord,
                block.IsDynamicBlock ? block.DynamicBlockTableRecord : ObjectId.Null, block.IsDynamicBlock) : null;

        private static EntityState ReadDestinationEntity(Entity entity, EntityState expected, Transaction tr,
            IReadOnlyDictionary<ObjectId, DefinitionClone> definitionCopies)
        {
            string? verifiedAnonymousName = VerifyReferenceTargets(expected, entity, tr, definitionCopies);
            return ReadEntity(entity, expected.Kind, tr, verifiedAnonymousName);
        }

        private static string? VerifyReferenceTargets(EntityState expected, Entity entity, Transaction tr,
            IReadOnlyDictionary<ObjectId, DefinitionClone> definitionCopies)
        {
            BlockReferenceState? actual = ReadReference(entity);
            if (expected.Reference == null && actual == null) return null;
            string context = $"Source handle {expected.Handle} -> destination handle {entity.Handle}";
            if (expected.Reference == null || actual == null || expected.Reference.IsDynamicBlock != actual.IsDynamicBlock)
                throw new InvalidOperationException(context + ": block reference type/dynamic status differs.");
            DefinitionClone evaluated = RequireMappedDefinition(expected.Reference.BlockTableRecord, actual.BlockTableRecord,
                "BlockTableRecord", context, tr, definitionCopies);
            DefinitionClone effective = expected.Reference.IsDynamicBlock
                ? RequireMappedDefinition(expected.Reference.DynamicBlockTableRecord, actual.DynamicBlockTableRecord,
                    "DynamicBlockTableRecord", context, tr, definitionCopies)
                : evaluated;
            // Never use a shared anonymous sentinel or infer identity from a name.
            // Only this exact mapped target may use its own captured source name.
            // Source capture/readback never calls this destination-only path.
            return effective.Source.IsAnonymous ? effective.Source.Name : null;
        }

        private static DefinitionClone RequireMappedDefinition(ObjectId sourceId, ObjectId destinationId, string target,
            string context, Transaction tr, IReadOnlyDictionary<ObjectId, DefinitionClone> definitionCopies)
        {
            if (!definitionCopies.TryGetValue(sourceId, out DefinitionClone? mapped) || mapped.DestinationId != destinationId)
                throw new InvalidOperationException(context + $": {target} does not point to the exact cloned source definition {sourceId.Handle}.");
            RequireDefinitionIdentity(mapped.Source, (BlockTableRecord)tr.GetObject(destinationId, OpenMode.ForRead));
            return mapped;
        }

        private static void RequireDefinitionIdentity(DefinitionState expected, BlockTableRecord actual)
        {
            if (actual.IsAnonymous != expected.IsAnonymous || !expected.IsAnonymous && actual.Name != expected.Name ||
                actual.IsFromExternalReference || actual.IsFromOverlayReference || actual.IsLayout)
                throw new InvalidOperationException($"Source block '{expected.Name}', handle {expected.Handle} -> destination handle {actual.Handle}: definition identity differs; only verified anonymous generated names may change.");
        }

        private static string SelectKind(Entity entity, Transaction tr)
        {
            // Exact managed types exclude Civil proxy/derived network objects.
            if (entity.GetType() == typeof(BlockReference))
            {
                var reference = (BlockReference)entity;
                var definition = (BlockTableRecord)tr.GetObject(reference.BlockTableRecord, OpenMode.ForRead);
                if (definition.IsFromExternalReference || definition.IsFromOverlayReference) return string.Empty;
                string name = EffectiveName(reference, tr);
                if (StructureBlocks.Contains(name)) return "structure block";
                if (MarkerBlocks.Contains(name)) return "marker";
                return string.Empty;
            }
            if (!IsStructuralLayer(entity.Layer) || entity is not Curve) return string.Empty;
            if (entity.GetType().Namespace != typeof(Line).Namespace) return string.Empty;
            if (!IsSupportedCurve(entity))
                throw new InvalidOperationException($"Source handle {entity.Handle}, layer '{entity.Layer}': unsupported structural curve {entity.GetType().Name}; nothing was cloned.");
            return "curve";
        }

        private static bool IsStructuralLayer(string name)
            => StructuralLayers.Any(layer => string.Equals(name, layer, StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith(layer + "~~", StringComparison.OrdinalIgnoreCase));

        private static bool IsSupportedCurve(Entity entity)
            => new[] { typeof(Line), typeof(Arc), typeof(Circle), typeof(Ellipse), typeof(Polyline),
                typeof(Polyline2d), typeof(Polyline3d), typeof(Spline) }.Contains(entity.GetType());

        private static string EffectiveName(BlockReference reference, Transaction tr)
            => ((BlockTableRecord)tr.GetObject(reference.IsDynamicBlock ? reference.DynamicBlockTableRecord :
                reference.BlockTableRecord, OpenMode.ForRead)).Name;

        private static ObjectId ModelSpace(Database database, Transaction tr)
            => ((BlockTable)tr.GetObject(database.BlockTableId, OpenMode.ForRead))[BlockTableRecord.ModelSpace];

        private static void RequireEmptyModelSpace(ObjectId modelSpace, Transaction tr)
        {
            foreach (ObjectId id in (BlockTableRecord)tr.GetObject(modelSpace, OpenMode.ForRead))
                if (!id.IsErased) throw new InvalidOperationException($"Destination template ModelSpace is not empty (handle {id.Handle}); no clone was attempted.");
        }

        private static void RejectCompletionLinks(Entity entity, Transaction tr)
        {
            if (entity.ExtensionDictionary.IsNull) return;
            var dictionary = (DBDictionary)tr.GetObject(entity.ExtensionDictionary, OpenMode.ForRead);
            foreach (DBDictionaryEntry entry in dictionary)
                if (entry.Key.StartsWith("CLV_STORM_PREP", StringComparison.OrdinalIgnoreCase) ||
                    entry.Key.StartsWith("CLV_STORM_PIPE_PREP", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Handle {entity.Handle}: '{entry.Key}' contains drawing-local completion/ownership links; it cannot be carried into a fresh GIS drawing.");
        }

        private static void CollectDependencies(Entity entity, Transaction tr,
            Dictionary<ObjectId, DefinitionState> definitions, Dictionary<ObjectId, ResourceState> resources,
            Dictionary<ObjectId, EntityState> dependencies, HashSet<ObjectId> path, int depth)
        {
            if (depth > 64) throw new InvalidOperationException($"Handle {entity.Handle}: block dependency nesting exceeds 64.");
            if (entity.GetType().Namespace != typeof(Entity).Namespace || entity is ProxyEntity ||
                entity is RasterImage && entity is not Wipeout || entity is UnderlayReference)
                throw new InvalidOperationException($"Handle {entity.Handle}: unsupported Civil/proxy/external dependency {entity.GetType().FullName}.");
            RejectCompletionLinks(entity, tr);
            if (!dependencies.ContainsKey(entity.ObjectId))
                dependencies.Add(entity.ObjectId, ReadEntity(entity, "block/curve dependency", tr));
            RequireBuiltInMaterial(entity.MaterialId, entity.Handle.ToString(), tr);
            AddResource(entity.LayerId, tr, resources);
            AddResource(entity.LinetypeId, tr, resources);
            if (entity is DBText text) AddResource(text.TextStyleId, tr, resources);
            if (entity is MText mtext) AddResource(mtext.TextStyleId, tr, resources);
            // Dimensions/mleaders/styles can reach unrelated anonymous blocks and
            // Civil objects. Fail closed rather than discover them after mutation.
            if (entity is Dimension || entity is MLeader || entity is Table)
                throw new InvalidOperationException($"Handle {entity.Handle}: {entity.GetType().Name} inside a selected block needs dependency review.");
            if (entity is not BlockReference block) return;
            foreach (ObjectId attributeId in block.AttributeCollection)
                CollectDependencies((Entity)tr.GetObject(attributeId, OpenMode.ForRead), tr, definitions, resources, dependencies, path, depth + 1);
            var blockIds = new List<ObjectId> { block.BlockTableRecord };
            if (block.IsDynamicBlock) blockIds.Add(block.DynamicBlockTableRecord);
            foreach (ObjectId blockId in blockIds.Distinct())
            {
                if (path.Contains(blockId)) throw new InvalidOperationException($"Handle {entity.Handle}: recursive block dependency {blockId.Handle}.");
                if (definitions.ContainsKey(blockId)) continue;
                var definition = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
                if (definition.IsFromExternalReference || definition.IsFromOverlayReference || definition.IsLayout)
                    throw new InvalidOperationException($"Handle {entity.Handle}: external/layout dependency '{definition.Name}', handle {definition.Handle}, cannot be cloned.");
                ObjectId[] children = definition.Cast<ObjectId>().ToArray();
                definitions.Add(blockId, new DefinitionState(blockId, definition.Handle.ToString(), definition.Name,
                    definition.IsAnonymous, Array.AsReadOnly(children)));
                path.Add(blockId);
                foreach (ObjectId child in children)
                    if (!child.IsErased && tr.GetObject(child, OpenMode.ForRead, false) is Entity nested)
                        CollectDependencies(nested, tr, definitions, resources, dependencies, path, depth + 1);
                path.Remove(blockId);
            }
        }

        private static void RequireBuiltInMaterial(ObjectId id, string ownerHandle, Transaction tr)
        {
            if (id.IsNull) return;
            var material = tr.GetObject(id, OpenMode.ForRead) as Material
                ?? throw new InvalidOperationException($"Handle {ownerHandle}: material dependency {id.Handle} is not readable.");
            if (!new[] { "Global", "ByLayer", "ByBlock" }.Contains(material.Name, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Handle {ownerHandle}: custom material '{material.Name}', handle {id.Handle}, needs collision review before cloning.");
        }

        private static void AddResource(ObjectId id, Transaction tr, Dictionary<ObjectId, ResourceState> resources)
        {
            if (id.IsNull || resources.ContainsKey(id)) return;
            var record = (SymbolTableRecord)tr.GetObject(id, OpenMode.ForRead);
            resources.Add(id, ReadResource(record, tr));
            if (record is LayerTableRecord layer) AddResource(layer.LinetypeObjectId, tr, resources);
            if (record is LinetypeTableRecord linetype)
                for (int i = 0; i < linetype.NumDashes; i++) AddResource(linetype.ShapeStyleAt(i), tr, resources);
        }

        private static ResourceState ReadResource(SymbolTableRecord record, Transaction tr)
            => ReadAt($"Resource handle {record.Handle} ({record.GetType().Name}): snapshot", () => ReadResourceCore(record, tr));

        private static ResourceState ReadResourceCore(SymbolTableRecord record, Transaction tr)
        {
            string context = $"Resource handle {record.Handle} ({record.GetType().Name})";
            var properties = new List<ResourceProperty>();
            void Add(string name, object? value, ResourcePropertyRole role = ResourcePropertyRole.Value)
            {
                var key = new Key();
                key.Add(value);
                string display = value switch
                {
                    null => "<null>",
                    double number => number.ToString("R", CultureInfo.InvariantCulture),
                    bool boolean => boolean ? "true" : "false",
                    _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "<null>"
                };
                properties.Add(new ResourceProperty(name, key.ToString(), display, role));
            }
            void ColorProperties(string prefix, ColorValue value)
            {
                Add(prefix + ".Mode", value.Mode); Add(prefix + ".Aci", value.Aci);
                Add(prefix + ".Red", value.Red); Add(prefix + ".Green", value.Green); Add(prefix + ".Blue", value.Blue);
                Add(prefix + ".HasBookName", value.HasBookName); Add(prefix + ".BookName", value.BookName);
                Add(prefix + ".HasColorName", value.HasColorName); Add(prefix + ".ColorName", value.ColorName);
            }
            Add("RecordType", record.GetType().Name);
            Add("Name", record.Name, ResourcePropertyRole.SymbolName);
            string kind;
            if (record is LayerTableRecord layer)
            {
                kind = "layer";
                ColorProperties("Color", ReadNativeColor(ReadAt(context + ".Color", () => layer.Color), context + ".Color"));
                Add("IsOff", layer.IsOff); Add("IsFrozen", layer.IsFrozen); Add("IsLocked", layer.IsLocked);
                Add("IsPlottable", layer.IsPlottable); Add("LineWeight", layer.LineWeight);
                TransparencyValue transparency = ReadNativeTransparency(ReadAt(context + ".Transparency", () => layer.Transparency), context + ".Transparency");
                Add("Transparency.Mode", transparency.Mode); Add("Transparency.Alpha", transparency.Alpha);
                Add("PlotStyleName", ReadAt(context + ".PlotStyleName", () => layer.PlotStyleName), ResourcePropertyRole.SymbolName);
                Add("ViewportVisibilityDefault", layer.ViewportVisibilityDefault);
                Add("LinetypeName", ((LinetypeTableRecord)tr.GetObject(layer.LinetypeObjectId, OpenMode.ForRead)).Name, ResourcePropertyRole.SymbolName);
                // Keep the existing viewport/material safety boundaries. A matching
                // built-in name never bypasses the captured appearance properties.
                Add("HasOverrides", layer.HasOverrides);
                RequireBuiltInMaterial(layer.MaterialId, layer.Handle.ToString(), tr);
                Add("MaterialName", layer.MaterialId.IsNull ? "" : ((Material)tr.GetObject(layer.MaterialId, OpenMode.ForRead)).Name,
                    ResourcePropertyRole.SymbolName);
            }
            else if (record is TextStyleTableRecord style)
            {
                kind = "text style";
                Add("FileName", style.FileName); Add("BigFontFileName", style.BigFontFileName);
                // These are text-generation/mirroring flags, not DXF group-70
                // referenced/bookkeeping bits. Keep their full rendering value.
                Add("FlagBits", style.FlagBits);
                Add("IsShapeFile", style.IsShapeFile); Add("IsVertical", style.IsVertical); Add("ObliquingAngle", style.ObliquingAngle);
                Add("TextSize", style.TextSize); Add("XScale", style.XScale);
                var font = style.Font;
                Add("Font.TypeFace", font.TypeFace); Add("Font.Bold", font.Bold); Add("Font.Italic", font.Italic);
                Add("Font.CharacterSet", font.CharacterSet); Add("Font.PitchAndFamily", font.PitchAndFamily);
                Add("Annotative", style.Annotative);
            }
            else if (record is LinetypeTableRecord line)
            {
                kind = "linetype";
                // Description text belongs to the strict source snapshot but does
                // not alter rendering when an equivalent destination record is reused.
                Add("Comments", line.Comments, ResourcePropertyRole.Description);
                Add("PatternLength", line.PatternLength); Add("NumDashes", line.NumDashes); Add("IsScaledToFit", line.IsScaledToFit);
                for (int i = 0; i < line.NumDashes; i++)
                {
                    int index = i;
                    string prefix = $"Dash[{index}]";
                    string elementContext = context + " " + prefix;
                    Add(prefix + ".Length", ReadAt(elementContext + ".DashLengthAt", () => line.DashLengthAt(index)));
                    ObjectId styleId = ReadAt(elementContext + ".ShapeStyleAt", () => line.ShapeStyleAt(index));
                    int shapeNumber = ReadAt(elementContext + ".ShapeNumberAt", () => line.ShapeNumberAt(index));
                    LinetypeElement element = ReadAt(elementContext + ": classify", () =>
                        ReadLinetypeElement(!styleId.IsNull, shapeNumber,
                            () => ReadAt(elementContext + ".TextAt", () => line.TextAt(index))));
                    Add(prefix + ".Kind", element.Kind); Add(prefix + ".ShapeNumber", element.ShapeNumber); Add(prefix + ".Text", element.Text);
                    if (element.Kind != LinetypeElementKind.PlainDash)
                    {
                        Add(prefix + ".Offset", ReadAt(elementContext + ".ShapeOffsetAt", () => line.ShapeOffsetAt(index)));
                        Add(prefix + ".Rotation", ReadAt(elementContext + ".ShapeRotationAt", () => line.ShapeRotationAt(index)));
                        Add(prefix + ".Scale", ReadAt(elementContext + ".ShapeScaleAt", () => line.ShapeScaleAt(index)));
                        Add(prefix + ".IsUcsOriented", ReadAt(elementContext + ".ShapeIsUcsOrientedAt", () => line.ShapeIsUcsOrientedAt(index)));
                        Add(prefix + ".IsUpright", ReadAt(elementContext + ".ShapeIsUprightAt", () => line.ShapeIsUprightAt(index)));
                        Add(prefix + ".StyleName", ReadAt(elementContext + ".ShapeStyle.Name", () =>
                            ((TextStyleTableRecord)tr.GetObject(styleId, OpenMode.ForRead)).Name), ResourcePropertyRole.SymbolName);
                    }
                }
            }
            else throw new InvalidOperationException($"Handle {record.Handle}: unsupported symbol dependency {record.GetType().Name}.");
            return new ResourceState(record.ObjectId, record.Handle.ToString(), kind, record.Name, Array.AsReadOnly(properties.ToArray()));
        }

        private static string ResourceDifferences(IReadOnlyList<string> differences)
            => string.Join("; ", differences.Take(8)) + (differences.Count > 8 ? $"; {differences.Count - 8} additional difference(s)." : ".");

        private static void PreflightDependencies(Snapshot snapshot, Transaction sourceTr, Database destination, Transaction targetTr)
        {
            var blocks = (BlockTable)targetTr.GetObject(destination.BlockTableId, OpenMode.ForRead);
            foreach (DefinitionState definition in snapshot.Definitions)
                if (!definition.IsAnonymous && blocks.Has(definition.Name))
                    throw new InvalidOperationException($"Block definition collision '{definition.Name}': source handle {definition.Handle}, template handle {blocks[definition.Name].Handle}. Definition equivalence is unproven; no Ignore/Replace fallback is allowed.");
            foreach (ResourceState resource in snapshot.Resources)
            {
                ObjectId tableId = resource.Kind == "layer" ? destination.LayerTableId :
                    resource.Kind == "text style" ? destination.TextStyleTableId : destination.LinetypeTableId;
                var table = (SymbolTable)targetTr.GetObject(tableId, OpenMode.ForRead);
                if (!table.Has(resource.Name)) continue;
                ObjectId existingId = table[resource.Name];
                ResourceState existing = ReadResource((SymbolTableRecord)targetTr.GetObject(existingId, OpenMode.ForRead), targetTr);
                bool builtInLinetype = new[] { "ByLayer", "ByBlock", "Continuous" }.Contains(resource.Name, StringComparer.OrdinalIgnoreCase);
                bool unsafeLayer = resource.Kind == "layer" &&
                    (((LayerTableRecord)sourceTr.GetObject(resource.Id, OpenMode.ForRead)).HasOverrides ||
                     ((LayerTableRecord)targetTr.GetObject(existingId, OpenMode.ForRead)).HasOverrides);
                var differences = FindResourceDifferences(resource.Properties, existing.Properties, strictSource: false).ToList();
                if (unsafeLayer) differences.Add("Viewport overrides cannot be proven equivalent for reuse");
                if (resource.Kind == "linetype" && !builtInLinetype)
                    differences.Add("Reuse of non-built-in linetypes is unsupported even when captured fields match");
                if (differences.Count != 0)
                    throw new InvalidOperationException($"{resource.Kind} collision '{resource.Name}': source handle {resource.Handle}, template handle {existingId.Handle}. " +
                        ResourceDifferences(differences) + " No clone was attempted.");
            }
        }

        private static void VerifyDestinationResources(Snapshot snapshot, Database destination, Transaction tr)
        {
            foreach (ResourceState expected in snapshot.Resources)
            {
                ObjectId tableId = expected.Kind == "layer" ? destination.LayerTableId :
                    expected.Kind == "text style" ? destination.TextStyleTableId : destination.LinetypeTableId;
                var table = (SymbolTable)tr.GetObject(tableId, OpenMode.ForRead);
                if (!table.Has(expected.Name))
                    throw new InvalidOperationException($"Source {expected.Kind} '{expected.Name}', handle {expected.Handle}: resource missing from destination.");
                ObjectId id = table[expected.Name];
                ResourceState actual = ReadResource((SymbolTableRecord)tr.GetObject(id, OpenMode.ForRead), tr);
                IReadOnlyList<string> differences = FindResourceDifferences(expected.Properties, actual.Properties, strictSource: false);
                if (differences.Count != 0)
                    throw new InvalidOperationException($"Source {expected.Kind} '{expected.Name}', handle {expected.Handle} -> destination handle {id.Handle}: resource appearance differs. " +
                        ResourceDifferences(differences));
            }
        }

        private static void VerifyMapping(Snapshot snapshot, Database destination, ObjectId modelSpace,
            IdMapping mapping, Dictionary<ObjectId, ObjectId> copies, Dictionary<ObjectId, ObjectId> dependencyCopies,
            Dictionary<ObjectId, DefinitionClone> definitionCopies)
        {
            var pairs = new Dictionary<ObjectId, IdPair>();
            foreach (IdPair pair in mapping) pairs.Add(pair.Key, pair);
            using Transaction sourceTr = snapshot.Database.TransactionManager.StartTransaction();
            using Transaction targetTr = destination.TransactionManager.StartTransaction();
            foreach (EntityState source in snapshot.Entities)
            {
                if (!pairs.TryGetValue(source.Id, out IdPair pair) || !pair.IsCloned || pair.Value.IsNull ||
                    !pair.Value.IsValid || pair.Value.IsErased || pair.Value.Database != destination)
                    throw new InvalidOperationException($"Source handle {source.Handle}: missing/invalid/not-cloned top-level IdMapping entry.");
                if (targetTr.GetObject(pair.Value, OpenMode.ForRead) is not Entity entity || entity.OwnerId != modelSpace)
                    throw new InvalidOperationException($"Source handle {source.Handle} -> destination handle {pair.Value.Handle}: clone is not owned by destination ModelSpace.");
                if (copies.Values.Contains(pair.Value))
                    throw new InvalidOperationException($"Source handle {source.Handle}: multiple selected objects map to destination handle {pair.Value.Handle}.");
                copies.Add(source.Id, pair.Value);
            }
            foreach (EntityState dependency in snapshot.DependencyEntities)
            {
                if (!pairs.TryGetValue(dependency.Id, out IdPair pair) || !pair.IsCloned || pair.Value.IsNull ||
                    !pair.Value.IsValid || pair.Value.IsErased || pair.Value.Database != destination)
                    throw new InvalidOperationException($"Source dependency handle {dependency.Handle}: missing/invalid/not-cloned IdMapping entry.");
                dependencyCopies.Add(dependency.Id, pair.Value);
            }
            var mappedDefinitionIds = new HashSet<ObjectId>();
            foreach (DefinitionState definition in snapshot.Definitions)
            {
                if (!pairs.TryGetValue(definition.Id, out IdPair pair) || !pair.IsCloned || pair.Value.IsNull ||
                    !pair.Value.IsValid || pair.Value.IsErased || pair.Value.Database != destination)
                    throw new InvalidOperationException($"Source block '{definition.Name}', handle {definition.Handle}: definition was not cloned as a new record.");
                if (!mappedDefinitionIds.Add(pair.Value))
                    throw new InvalidOperationException($"Source block '{definition.Name}', handle {definition.Handle}: multiple definitions map to destination handle {pair.Value.Handle}.");
                if (targetTr.GetObject(pair.Value, OpenMode.ForRead) is not BlockTableRecord actual || actual.OwnerId != destination.BlockTableId)
                    throw new InvalidOperationException($"Source block '{definition.Name}', handle {definition.Handle}: mapped definition is not owned by the destination BlockTable.");
                RequireDefinitionIdentity(definition, actual);
                var expectedChildren = new List<ObjectId>();
                foreach (ObjectId child in definition.Children)
                {
                    if (!pairs.TryGetValue(child, out IdPair childPair) || !childPair.IsCloned)
                        throw new InvalidOperationException($"Source block '{definition.Name}', handle {definition.Handle}, child handle {child.Handle}: missing cloned dependency.");
                    expectedChildren.Add(childPair.Value);
                }
                if (!expectedChildren.SequenceEqual(actual.Cast<ObjectId>()))
                    throw new InvalidOperationException($"Source block '{definition.Name}', handle {definition.Handle} -> destination handle {pair.Value.Handle}: child geometry topology/order changed.");
                definitionCopies.Add(definition.Id, new DefinitionClone(definition, pair.Value, Array.AsReadOnly(expectedChildren.ToArray())));
            }
            foreach (EntityState expected in snapshot.Entities.Concat(snapshot.DependencyEntities).DistinctBy(entity => entity.Id))
            {
                ObjectId id = pairs[expected.Id].Value;
                VerifyReferenceTargets(expected, (Entity)targetTr.GetObject(id, OpenMode.ForRead), targetTr, definitionCopies);
            }
            foreach (IdPair pair in pairs.Values.Where(pair => pair.IsCloned))
            {
                DBObject original = sourceTr.GetObject(pair.Key, OpenMode.ForRead, false);
                if ((original.GetType().Namespace ?? "").StartsWith("Autodesk.Civil", StringComparison.Ordinal) || original is ProxyEntity)
                    throw new InvalidOperationException($"Source handle {pair.Key.Handle} -> destination handle {pair.Value.Handle}: unexpected Civil/proxy dependency was cloned.");
                if (original is Entity && original.OwnerId == snapshot.ModelSpaceId && !copies.ContainsKey(pair.Key))
                    throw new InvalidOperationException($"Source handle {pair.Key.Handle} -> destination handle {pair.Value.Handle}: unselected ModelSpace entity was pulled in by a dependency.");
            }
            targetTr.Commit(); sourceTr.Commit();
        }

        private static string GeometrySignature(Entity entity, Transaction tr, string? verifiedAnonymousName = null)
        {
            if (entity is Curve && !IsSupportedCurve(entity) ||
                entity is BlockReference && entity.GetType() != typeof(BlockReference))
                throw new InvalidOperationException($"Handle {entity.Handle}: derived curve/block type {entity.GetType().FullName} needs its own complete geometry verifier.");
            var key = new Key();
            string context = $"Entity handle {entity.Handle} ({entity.GetType().Name})";
            key.Add(entity.GetType().FullName); key.AddSymbolName(entity.Layer);
            AddColor(key, ReadAt(context + ".Color", () => entity.Color), context + ".Color");
            key.AddSymbolName(entity.Linetype); key.Add(entity.LinetypeScale);
            key.Add(entity.LineWeight); key.Add(entity.Visible);
            AddTransparency(key, ReadAt(context + ".Transparency", () => entity.Transparency), context + ".Transparency");
            switch (entity)
            {
                case Line line:
                    key.Add(line.StartPoint); key.Add(line.EndPoint); key.Add(line.Normal); key.Add(line.Thickness); break;
                case Arc arc:
                    key.Add(arc.Center); key.Add(arc.Radius); key.Add(arc.StartAngle); key.Add(arc.EndAngle);
                    key.Add(arc.Normal); key.Add(arc.Thickness); break;
                case Circle circle:
                    key.Add(circle.Center); key.Add(circle.Radius); key.Add(circle.Normal); key.Add(circle.Thickness); break;
                case Ellipse ellipse:
                    key.Add(ellipse.Center); key.Add(ellipse.Normal); key.Add(ellipse.MajorAxis); key.Add(ellipse.MinorAxis);
                    key.Add(ellipse.RadiusRatio); key.Add(ellipse.StartParam); key.Add(ellipse.EndParam); break;
                case Polyline polyline:
                    key.Add(polyline.Closed); key.Add(polyline.Elevation); key.Add(polyline.Normal);
                    key.Add(polyline.Thickness); key.Add(polyline.Plinegen); key.Add(polyline.NumberOfVertices);
                    for (int i = 0; i < polyline.NumberOfVertices; i++)
                    {
                        key.Add(polyline.GetPoint3dAt(i)); key.Add(polyline.GetBulgeAt(i));
                        key.Add(polyline.GetStartWidthAt(i)); key.Add(polyline.GetEndWidthAt(i));
                    }
                    break;
                case Polyline2d polyline:
                    key.Add(polyline.Closed); key.Add(polyline.Elevation); key.Add(polyline.Normal);
                    key.Add(polyline.Thickness); key.Add(polyline.PolyType); key.Add(polyline.LinetypeGenerationOn);
                    key.Add(polyline.DefaultStartWidth); key.Add(polyline.DefaultEndWidth);
                    foreach (ObjectId vertexId in polyline)
                    {
                        var vertex = (Vertex2d)tr.GetObject(vertexId, OpenMode.ForRead);
                        key.Add(vertex.Position); key.Add(vertex.Bulge); key.Add(vertex.StartWidth); key.Add(vertex.EndWidth);
                        key.Add(vertex.VertexType); key.Add(vertex.TangentUsed); key.Add(vertex.Tangent);
                    }
                    break;
                case Polyline3d polyline:
                    key.Add(polyline.Closed); key.Add(polyline.PolyType);
                    foreach (ObjectId vertexId in polyline)
                    {
                        var vertex = (PolylineVertex3d)tr.GetObject(vertexId, OpenMode.ForRead);
                        key.Add(vertex.Position); key.Add(vertex.VertexType);
                    }
                    break;
                case Spline spline:
                    var data = spline.NurbsData;
                    key.Add(data.Degree); key.Add(data.Rational); key.Add(data.Closed); key.Add(data.Periodic);
                    key.Add(data.KnotTolerance); key.Add(data.ControlPointTolerance);
                    foreach (Point3d point in data.GetControlPoints()) key.Add(point);
                    key.Add("knots"); foreach (double knot in data.GetKnots()) key.Add(knot);
                    key.Add("weights"); foreach (double weight in data.GetWeights()) key.Add(weight);
                    key.Add(spline.HasFitData);
                    if (spline.HasFitData)
                    {
                        key.Add(spline.FitTolerance); key.Add(spline.NumFitPoints);
                        for (int i = 0; i < spline.NumFitPoints; i++) key.Add(spline.GetFitPointAt(i));
                        key.Add(spline.StartFitTangent); key.Add(spline.EndFitTangent);
                    }
                    break;
                case DBPoint point:
                    key.Add(point.Position); key.Add(point.Normal); key.Add(point.Thickness); key.Add(point.EcsRotation); break;
                case DBText text:
                    AddText(key, text, tr);
                    if (text is AttributeReference attributeReference)
                    {
                        key.Add(attributeReference.Tag); key.Add(attributeReference.Invisible);
                        key.Add(attributeReference.IsMTextAttribute);
                        if (attributeReference.IsMTextAttribute)
                        {
                            using MText attributeText = attributeReference.MTextAttribute;
                            AddMText(key, attributeText, tr);
                        }
                    }
                    if (text is AttributeDefinition attributeDefinition)
                    {
                        key.Add(attributeDefinition.Tag); key.Add(attributeDefinition.Prompt);
                        key.Add(attributeDefinition.Constant); key.Add(attributeDefinition.Preset);
                        key.Add(attributeDefinition.Verifiable); key.Add(attributeDefinition.Invisible);
                        key.Add(attributeDefinition.FieldLength); key.Add(attributeDefinition.LockPositionInBlock);
                        key.Add(attributeDefinition.IsMTextAttributeDefinition);
                        if (attributeDefinition.IsMTextAttributeDefinition)
                        {
                            using MText attributeText = attributeDefinition.MTextAttributeDefinition;
                            AddMText(key, attributeText, tr);
                        }
                    }
                    break;
                case MText text: AddMText(key, text, tr); break;
                case Solid solid:
                    for (short i = 0; i < 4; i++) key.Add(solid.GetPointAt(i));
                    key.Add(solid.Normal); key.Add(solid.Thickness); break;
                case Trace trace:
                    for (short i = 0; i < 4; i++) key.Add(trace.GetPointAt(i));
                    key.Add(trace.Normal); key.Add(trace.Thickness); break;
                case BlockReference block:
                    key.Add(verifiedAnonymousName ?? EffectiveName(block, tr)); key.Add(block.IsDynamicBlock);
                    foreach (double element in block.BlockTransform.ToArray()) key.Add(element);
                    key.Add(block.Position); key.Add(block.Normal); key.Add(block.Rotation);
                    key.Add(block.ScaleFactors.X); key.Add(block.ScaleFactors.Y); key.Add(block.ScaleFactors.Z);
                    if (block.IsDynamicBlock)
                    {
                        var properties = new List<string>();
                        foreach (DynamicBlockReferenceProperty property in block.DynamicBlockReferencePropertyCollection)
                        {
                            var value = new Key(); value.Add(property.PropertyName); value.Add(property.UnitsType);
                            value.Add(property.ReadOnly); value.Add(property.Value);
                            properties.Add(value.ToString());
                        }
                        foreach (string property in properties.OrderBy(value => value, StringComparer.Ordinal)) key.Add(property);
                    }
                    key.Add("attributes");
                    var attributes = new List<string>();
                    foreach (ObjectId attributeId in block.AttributeCollection)
                    {
                        var attribute = (AttributeReference)tr.GetObject(attributeId, OpenMode.ForRead);
                        var value = new Key();
                        value.Add(attribute.Tag); value.Add(attribute.TextString); value.Add(attribute.Position);
                        value.Add(attribute.AlignmentPoint); value.Add(attribute.Normal); value.Add(attribute.Rotation);
                        value.Add(attribute.Height); value.Add(attribute.WidthFactor); value.Add(attribute.Oblique);
                        value.Add(attribute.HorizontalMode); value.Add(attribute.VerticalMode); value.Add(attribute.Invisible);
                        value.Add(attribute.IsMirroredInX); value.Add(attribute.IsMirroredInY); value.Add(attribute.IsMTextAttribute);
                        value.AddSymbolName(attribute.Layer);
                        AddColor(value, ReadAt($"Attribute handle {attribute.Handle}.Color", () => attribute.Color),
                            $"Attribute handle {attribute.Handle}.Color");
                        value.AddSymbolName(((TextStyleTableRecord)tr.GetObject(attribute.TextStyleId, OpenMode.ForRead)).Name);
                        if (attribute.IsMTextAttribute)
                        {
                            using MText text = attribute.MTextAttribute;
                            value.Add(text.Contents); value.Add(text.Location); value.Add(text.Direction);
                            value.Add(text.Width); value.Add(text.TextHeight); value.Add(text.Attachment);
                        }
                        attributes.Add(value.ToString());
                    }
                    foreach (string attribute in attributes.OrderBy(value => value, StringComparer.Ordinal)) key.Add(attribute);
                    break;
                default: throw new InvalidOperationException($"Handle {entity.Handle}: no exact geometry verifier for {entity.GetType().FullName}.");
            }
            return key.ToString();
        }

        private static void AddText(Key key, DBText text, Transaction tr)
        {
            key.Add(text.TextString); key.Add(text.Position); key.Add(text.AlignmentPoint);
            key.Add(text.Normal); key.Add(text.Rotation); key.Add(text.Height); key.Add(text.WidthFactor);
            key.Add(text.Oblique); key.Add(text.Thickness); key.Add(text.HorizontalMode); key.Add(text.VerticalMode);
            key.Add(text.IsMirroredInX); key.Add(text.IsMirroredInY);
            key.AddSymbolName(((TextStyleTableRecord)tr.GetObject(text.TextStyleId, OpenMode.ForRead)).Name);
        }

        private static void AddMText(Key key, MText text, Transaction tr)
        {
            key.Add(text.Contents); key.Add(text.Location); key.Add(text.Direction); key.Add(text.Normal);
            key.Add(text.TextHeight); key.Add(text.Width); key.Add(text.Height); key.Add(text.FlowDirection);
            key.Add(text.Attachment); key.Add(text.LineSpacingFactor); key.Add(text.LineSpacingStyle);
            key.Add(text.ColumnType);
            if (text.ColumnType != ColumnType.NoColumns)
                throw new InvalidOperationException($"MText handle {text.Handle}: columned text needs a complete per-column geometry verifier before cloning.");
            key.Add(text.BackgroundFill); key.Add(text.UseBackgroundColor); key.Add(text.BackgroundScaleFactor);
            AddColor(key, ReadAt($"MText handle {text.Handle}.BackgroundFillColor", () => text.BackgroundFillColor),
                $"MText handle {text.Handle}.BackgroundFillColor");
            AddTransparency(key, ReadAt($"MText handle {text.Handle}.BackgroundTransparency", () => text.BackgroundTransparency),
                $"MText handle {text.Handle}.BackgroundTransparency");
            key.Add(text.ShowBorders);
            key.AddSymbolName(((TextStyleTableRecord)tr.GetObject(text.TextStyleId, OpenMode.ForRead)).Name);
        }

        private static ColorValue ReadNativeColor(Autodesk.AutoCAD.Colors.Color color, string context)
        {
            Autodesk.AutoCAD.Colors.ColorMethod method = ReadAt(context + ".ColorMethod", () => color.ColorMethod);
            ColorMode mode = method switch
            {
                Autodesk.AutoCAD.Colors.ColorMethod.ByLayer => ColorMode.ByLayer,
                Autodesk.AutoCAD.Colors.ColorMethod.ByBlock => ColorMode.ByBlock,
                Autodesk.AutoCAD.Colors.ColorMethod.ByAci => ColorMode.Aci,
                Autodesk.AutoCAD.Colors.ColorMethod.ByColor => ColorMode.Rgb,
                Autodesk.AutoCAD.Colors.ColorMethod.Foreground => ColorMode.Foreground,
                _ => throw new InvalidOperationException(context + ": unsupported native color method " + method)
            };
            ColorValue value = ReadColor(mode,
                () => ReadAt(context + ".ColorIndex", () => (int)color.ColorIndex),
                () => (ReadAt(context + ".Red", () => color.Red), ReadAt(context + ".Green", () => color.Green),
                    ReadAt(context + ".Blue", () => color.Blue)),
                () => ReadAt(context + ".HasBookName", () => color.HasBookName),
                () => ReadAt(context + ".BookName", () => color.BookName),
                () => ReadAt(context + ".HasColorName", () => color.HasColorName),
                () => ReadAt(context + ".ColorName", () => color.ColorName));
            return value;
        }

        private static void AddColor(Key key, Autodesk.AutoCAD.Colors.Color color, string context)
        {
            ColorValue value = ReadNativeColor(color, context);
            key.Add(value.Mode); key.Add(value.Aci); key.Add(value.Red); key.Add(value.Green); key.Add(value.Blue);
            key.Add(value.HasBookName); key.Add(value.BookName); key.Add(value.HasColorName); key.Add(value.ColorName);
        }

        private static TransparencyValue ReadNativeTransparency(Autodesk.AutoCAD.Colors.Transparency transparency, string context)
        {
            TransparencyMode mode = ReadAt(context + ": method", () => GetTransparencyMode(
                ReadAt(context + ".IsInvalid", () => transparency.IsInvalid),
                ReadAt(context + ".IsByLayer", () => transparency.IsByLayer),
                ReadAt(context + ".IsByBlock", () => transparency.IsByBlock),
                ReadAt(context + ".IsByAlpha", () => transparency.IsByAlpha)));
            return ReadTransparency(mode, () => ReadAt(context + ".Alpha", () => transparency.Alpha));
        }

        private static void AddTransparency(Key key, Autodesk.AutoCAD.Colors.Transparency transparency, string context)
        {
            TransparencyValue value = ReadNativeTransparency(transparency, context);
            key.Add(value.Mode); key.Add(value.Alpha);
        }

        // Length-prefixed, type-tagged exact round-trip values avoid collisions and
        // culture-dependent formatting. Unknown dynamic value types stop the clone.
        private sealed class Key
        {
            private readonly StringBuilder value = new();
            // Native symbol tables are case-insensitive. Only reference-name
            // tokens use this; literal text/font/color payloads remain unchanged.
            // Resource snapshots keep exact source names for immutability checks.
            internal void AddSymbolName(string name) => Add(SymbolNameKey(name));
            internal void Add(object? item)
            {
                string token;
                switch (item)
                {
                    case null: token = "null"; break;
                    case double number:
                        if (!double.IsFinite(number)) throw new InvalidOperationException("Nonfinite geometry/dynamic value.");
                        token = "double:" + number.ToString("R", CultureInfo.InvariantCulture); break;
                    case Point3d point: Add("Point3d"); Add(point.X); Add(point.Y); Add(point.Z); return;
                    case Vector3d vector: Add("Vector3d"); Add(vector.X); Add(vector.Y); Add(vector.Z); return;
                    case Point2d point: Add("Point2d"); Add(point.X); Add(point.Y); return;
                    case Vector2d vector: Add("Vector2d"); Add(vector.X); Add(vector.Y); return;
                    case string text: token = "text:" + text; break;
                    case bool boolean: token = "bool:" + (boolean ? "1" : "0"); break;
                    case Enum enumeration: token = enumeration.GetType().FullName + ":" + enumeration; break;
                    case byte or sbyte or short or ushort or int or uint or long or ulong:
                        token = item.GetType().Name + ":" + Convert.ToString(item, CultureInfo.InvariantCulture); break;
                    default: throw new InvalidOperationException("Unsupported immutable snapshot value type: " + item.GetType().FullName);
                }
                value.Append(token.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(token);
            }
            public override string ToString() => value.ToString();
        }

        private sealed class ActiveContext : IDisposable
        {
            private readonly AcDocument? previousDocument;
            private readonly Database previousDatabase;
            internal ActiveContext(AcDocument document)
            {
                previousDocument = AcadApp.DocumentManager.MdiActiveDocument;
                previousDatabase = HostApplicationServices.WorkingDatabase;
                try
                {
                    AcadApp.DocumentManager.MdiActiveDocument = document;
                    HostApplicationServices.WorkingDatabase = document.Database;
                    if (AcadApp.DocumentManager.MdiActiveDocument != document || HostApplicationServices.WorkingDatabase != document.Database)
                        throw new InvalidOperationException("Requested drawing could not be activated for native OD verification.");
                }
                catch { Restore(); throw; }
            }
            public void Dispose() => Restore();
            private void Restore()
            {
                if (previousDocument != null && AcadApp.DocumentManager.MdiActiveDocument != previousDocument)
                    AcadApp.DocumentManager.MdiActiveDocument = previousDocument;
                HostApplicationServices.WorkingDatabase = previousDatabase;
            }
        }

        private static InvalidOperationException Failure(string context, System.Exception exception)
            => new(context + ": " + exception.Message, exception);

        private static IEnumerable<ObjectId> SymmetricDifference(this HashSet<ObjectId> first, IEnumerable<ObjectId> second)
        {
            var difference = new HashSet<ObjectId>(first);
            difference.SymmetricExceptWith(second);
            return difference;
        }
    }
}
