using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace CLV_CivilTools.Gis
{
    internal sealed class SewerManholeCircle
    {
        internal SewerManholeCircle(ObjectId sourceEntityId, Point3d center, Vector3d normal, double radius, string originalLayer)
        { SourceEntityId = sourceEntityId; Center = center; Normal = normal; Radius = radius; OriginalLayer = originalLayer; }
        internal ObjectId SourceEntityId { get; }
        internal Point3d Center { get; }
        internal Vector3d Normal { get; }
        internal double Radius { get; }
        internal string OriginalLayer { get; }
    }

    internal sealed class SewerManholePlan
    {
        internal SewerManholePlan(ObjectId sourceId, string name, string partSizeName, Point3d sourcePosition,
            string sourceOdFingerprint, bool isNullEnd, ObjectId blockId, ObjectId markerId,
            SewerManholeCircle? outer, IEnumerable<SewerManholeCircle> inner, string blockStateSignature,
            string markerStateSignature = "")
        {
            SourceId = sourceId; Name = name; PartSizeName = partSizeName; SourcePosition = sourcePosition;
            SourceOdFingerprint = sourceOdFingerprint; IsNullEnd = isNullEnd; BlockId = blockId; MarkerId = markerId;
            Outer = outer; Inner = Array.AsReadOnly(inner.ToArray()); BlockStateSignature = blockStateSignature;
            MarkerStateSignature = markerStateSignature;
        }
        internal ObjectId SourceId { get; }
        internal string Name { get; }
        internal string PartSizeName { get; }
        internal Point3d SourcePosition { get; }
        internal string SourceOdFingerprint { get; }
        internal bool IsNullEnd { get; }
        internal ObjectId BlockId { get; }
        internal ObjectId MarkerId { get; }
        internal SewerManholeCircle? Outer { get; }
        internal IReadOnlyList<SewerManholeCircle> Inner { get; }
        internal string BlockStateSignature { get; }
        internal string MarkerStateSignature { get; }
    }

    internal sealed class SewerManholeBatch
    {
        internal SewerManholeBatch(IEnumerable<SewerManholePlan> structures, IEnumerable<string> reviews)
        {
            Structures = Array.AsReadOnly(structures.ToArray());
            Reviews = Array.AsReadOnly(reviews.Distinct(StringComparer.Ordinal).ToArray());
        }
        internal IReadOnlyList<SewerManholePlan> Structures { get; }
        internal IReadOnlyList<string> Reviews { get; }
    }

    /// <summary>
    /// Read-only native planning. The caller holds the active document lock and owns
    /// OD transfer/readback, transaction rollback, output ownership and source retirement.
    /// ObjectIds identify this drawing only; no live Entity escapes the transaction.
    /// </summary>
    internal static class GisSewerManholePlanner
    {
        private const string SourceLayer = "Structures";
        private const string ManholeBlockName = "UFLS-GIS-MH-CIRCULAR";
        private const string MarkerBlockName = "UFLS_MH_MARK";
        private const string MarkerLayer = "V-SURV-CHCK";
        private const double GeometryTolerance = 1e-8;
        private const double TransformTolerance = 1e-9;

        private static readonly Regex SewerToken = new Regex(@"(?<![A-Z0-9])(?:SSMH|SSWR|SEWER)(?=$|[^A-Z])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex StormToken = new Regex(@"(?<![A-Z0-9])(?:SDDI|SDMH|SDWR|STRM|STORM|SD)(?=$|[^A-Z])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex OuterLayer = new Regex(@"^V-SURV-(?:STRC-)?(?:OUTR|OUTER)(?:-2D)?(?:~~.*)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex InnerLayer = new Regex(@"^V-SURV-STRC-(?:INNR|INNER)(?:-2D)?(?:~~.*)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static SewerManholeBatch Plan(Database db, Transaction tr)
        {
            ArgumentNullException.ThrowIfNull(db);
            ArgumentNullException.ThrowIfNull(tr);
            var reviews = new List<string>();
            var plans = new List<SewerManholePlan>();
            var sources = new List<Source>();
            var blocks = new List<BlockReference>();
            var markers = new List<BlockReference>();
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            if (!string.Equals(space.Name, BlockTableRecord.ModelSpace, StringComparison.OrdinalIgnoreCase))
                return new SewerManholeBatch(plans, new[] { "Sewer manhole planning requires model space." });

            bool unlocatableCompetitor = false;
            foreach (ObjectId id in space.Cast<ObjectId>().OrderBy(Handle, StringComparer.Ordinal))
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity entity || entity.IsErased) continue;
                if (string.Equals(entity.Layer, SourceLayer, StringComparison.OrdinalIgnoreCase))
                {
                    if (entity.GetType() != typeof(DBPoint))
                    {
                        reviews.Add($"Structures entity {Handle(id)} is {entity.GetType().Name}, not a supported DBPoint; retained for review.");
                        unlocatableCompetitor = true;
                    }
                    else
                    {
                        var source = ReadSource((DBPoint)entity);
                        sources.Add(source); // Unreadable/storm/unknown sources still compete geometrically.
                        if (!Finite(source.Position)) unlocatableCompetitor = true;
                        if (source.Problem.Length > 0) reviews.Add(SourceReview(source, source.Problem));
                    }
                }

                if (entity is not BlockReference block) continue;
                try
                {
                    string name = EffectiveName(block, tr);
                    if (KnownName(name, ManholeBlockName)) blocks.Add(block);
                    if (KnownName(name, MarkerBlockName)) markers.Add(block);
                }
                catch (System.Exception ex)
                {
                    // An unreadable reference cannot be silently removed from ownership
                    // competition and thereby make a nearby source appear unique.
                    reviews.Add($"Block {Handle(id)} could not be identified: {ex.Message}; retained for review.");
                    unlocatableCompetitor = true;
                }
            }

            foreach (BlockReference block in blocks.Concat(markers))
                if (!Finite(block.Position))
                {
                    reviews.Add($"Block {Handle(block.ObjectId)} has nonfinite insertion coordinates; retained for review.");
                    unlocatableCompetitor = true;
                }

            var repeatedNames = new HashSet<string>(sources.Where(s => s.Name.Trim().Length > 0)
                .GroupBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1)
                .Select(g => g.Key), StringComparer.OrdinalIgnoreCase);
            var captured = new Dictionary<ObjectId, CapturedBlock>();
            var blockProblems = new Dictionary<ObjectId, string>();
            foreach (BlockReference block in blocks)
            {
                try { captured.Add(block.ObjectId, Capture(tr, block)); }
                catch (System.Exception ex) { blockProblems.Add(block.ObjectId, ex.Message); }
            }

            bool Competes(Source source, BlockReference block) => NearXY(source.Position, block.Position) ||
                captured.TryGetValue(block.ObjectId, out CapturedBlock? geometry) && NearXY(source.Position, geometry.Outer.Center);

            foreach (Source source in sources)
            {
                BlockReference[] nearby = blocks.Where(block => Competes(source, block)).ToArray();
                if (source.Kind == SourceKind.Ambiguous)
                {
                    if (nearby.Length > 0 || ManholeHint(source.PartSizeName))
                        reviews.Add(SourceReview(source, "utility identity is ambiguous; a nearby manhole block does not establish sewer ownership"));
                    continue;
                }
                if (source.Kind == SourceKind.Other || source.Problem.Length > 0) continue;
                if (repeatedNames.Contains(source.Name.Trim()))
                {
                    reviews.Add(SourceReview(source, "duplicate Structures identity; every duplicate is retained"));
                    continue;
                }
                if (source.Kind == SourceKind.NullEnd)
                {
                    // Exact established null/STUB convention only. A null endpoint never
                    // consumes a block/marker and stays at its original XYZ coordinate.
                    plans.Add(new SewerManholePlan(source.Id, source.Name, source.PartSizeName, source.Position,
                        source.Fingerprint, true, ObjectId.Null, ObjectId.Null, null,
                        Array.Empty<SewerManholeCircle>(), string.Empty));
                    continue;
                }
                if (unlocatableCompetitor)
                {
                    reviews.Add(SourceReview(source, "an unlocatable Structures/reference competitor prevents a safe ownership decision"));
                    continue;
                }
                if (nearby.Length != 1)
                {
                    reviews.Add(SourceReview(source, nearby.Length == 0
                        ? "no known manhole block insertion is within 0.10 XY drawing units"
                        : $"{nearby.Length} manhole blocks compete within 0.10 XY drawing units"));
                    continue;
                }
                BlockReference selected = nearby[0];
                Source[] claimants = sources.Where(s => Competes(s, selected)).ToArray();
                if (claimants.Length != 1 || claimants[0].Id != source.Id)
                {
                    reviews.Add(SourceReview(source, $"manhole block {Handle(selected.ObjectId)} has competing Structures points; none is selected"));
                    continue;
                }

                try
                {
                    if (!captured.TryGetValue(selected.ObjectId, out CapturedBlock? geometry))
                        throw new InvalidOperationException(blockProblems[selected.ObjectId]);
                    if (!NearXY(geometry.Outer.Center, selected.Position) || !NearXY(geometry.Outer.Center, source.Position))
                        throw new InvalidOperationException("the visible outer circle is not centered at the matched block insertion and source point within 0.10 XY drawing units");
                    if (!SameElevation(geometry.Outer.Center, selected.Position) || !SameElevation(geometry.Outer.Center, source.Position))
                        throw new InvalidOperationException("the imported point, block and outer circle have different elevations; no flattening or source retirement is permitted");
                    if (!GisImportCommands.CanReplaceOutlineObjectData(source.Id, selected.ObjectId, out string detail))
                        throw new InvalidOperationException(detail);
                    ObjectId markerId = FindOwnedMarker(tr, source, selected, sources, markers, reviews);
                    plans.Add(new SewerManholePlan(source.Id, source.Name, source.PartSizeName, source.Position,
                        source.Fingerprint, false, selected.ObjectId, markerId, geometry.Outer,
                        geometry.Inner, geometry.Signature, markerId.IsNull ? string.Empty :
                            CaptureMarkerState(tr, (BlockReference)tr.GetObject(markerId, OpenMode.ForRead))));
                }
                catch (System.Exception ex)
                {
                    reviews.Add(SourceReview(source, $"block {Handle(selected.ObjectId)}: {ex.Message}"));
                }
            }
            return new SewerManholeBatch(plans, reviews);
        }

        /// <summary>Re-read the evaluated block, all visible geometry and instance state before applying a plan.</summary>
        internal static string CaptureBlockState(Transaction tr, BlockReference block) => Capture(tr, block).Signature;

        internal static bool IsSewerIdentity(string name, string part)
        {
            if (SewerToken.IsMatch(name) || SewerManholePart(part)) return true;
            return !StormToken.IsMatch(name) && !StormToken.IsMatch(part) &&
                StormStructureMatching.Classify(name, part) == StormStructureRole.NullPipeEnd;
        }

        internal static string CaptureMarkerState(Transaction tr, BlockReference marker)
        {
            if (marker.GetType() != typeof(BlockReference) || !marker.Visible ||
                !string.Equals(EffectiveName(marker, tr), MarkerBlockName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(marker.Layer, MarkerLayer, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("marker name, layer, visibility or entity type changed");
            RequireLocalDefinition(marker, tr);
            using ResultBuffer? xdata = marker.XData;
            if (marker.AttributeCollection.Count > 0 || !marker.ExtensionDictionary.IsNull || (xdata?.AsArray().Length ?? 0) > 0)
                throw new InvalidOperationException("marker carries attributes, XData or extension data");
            if (GisImportCommands.InspectObjectDataFingerprint(marker.ObjectId, out _, out string detail) !=
                GisImportCommands.ObjectDataFingerprintState.Empty)
                throw new InvalidOperationException("marker OD must remain empty: " + detail);
            var state = new Signature();
            AddBlockState(state, marker, tr);
            return state.Finish();
        }

        private static Source ReadSource(DBPoint point)
        {
            bool read = GisImportCommands.TryReadStructuresIdentity(point.ObjectId, out string name, out string part,
                out string identityDetail, out _);
            var source = new Source(point.ObjectId, name, part, point.Position);
            if (!Finite(point.Position)) source.Problem = "source XYZ coordinates are not finite";
            else if (!read) source.Problem = identityDetail;
            else
            {
                StormStructureRole role = StormStructureMatching.Classify(name, part);
                bool sewer = SewerToken.IsMatch(name) || SewerManholePart(part);
                bool storm = StormToken.IsMatch(name) || StormToken.IsMatch(part) ||
                    role == StormStructureRole.Access || role == StormStructureRole.DropInlet || role == StormStructureRole.JunctionBox;
                if (storm)
                {
                    source.Kind = SourceKind.Other;
                    if (sewer) source.Problem = "conflicting sewer and explicit storm identity/role evidence";
                }
                else if (role == StormStructureRole.NullPipeEnd) source.Kind = SourceKind.NullEnd;
                else if (name.Trim().EndsWith("-STUB", StringComparison.OrdinalIgnoreCase) ||
                    part.IndexOf("NULL", StringComparison.OrdinalIgnoreCase) >= 0)
                    source.Problem = "null/STUB identity does not match the exact supported non-graphic pipe-end convention";
                else if (sewer && name.Trim().Length > 0) source.Kind = SourceKind.Manhole;
                else source.Kind = SourceKind.Ambiguous;

                if (source.Kind == SourceKind.Manhole || source.Kind == SourceKind.NullEnd)
                {
                    var state = GisImportCommands.InspectObjectDataFingerprint(point.ObjectId, out string fingerprint, out string odDetail);
                    if (state != GisImportCommands.ObjectDataFingerprintState.Present) source.Problem = odDetail;
                    else source.Fingerprint = fingerprint;
                }
            }
            return source;
        }

        private static ObjectId FindOwnedMarker(Transaction tr, Source source, BlockReference block,
            IReadOnlyList<Source> sources, IReadOnlyList<BlockReference> markers, List<string> reviews)
        {
            BlockReference[] nearby = markers.Where(m => NearXY(source.Position, m.Position)).ToArray();
            if (nearby.Length == 0) return ObjectId.Null;
            if (nearby.Length != 1)
            {
                reviews.Add(SourceReview(source, "multiple UFLS_MH_MARK instances compete; markers retained"));
                return ObjectId.Null;
            }
            BlockReference marker = nearby[0];
            try
            {
                if (sources.Count(s => NearXY(s.Position, marker.Position)) != 1 || !NearXY(block.Position, marker.Position))
                    throw new InvalidOperationException("marker ownership is not mutually unique at the source/block XY anchor");
                if (!SameElevation(source.Position, marker.Position) || !SameElevation(block.Position, marker.Position))
                    throw new InvalidOperationException("marker elevation differs from its source and block");
                if (marker.GetType() != typeof(BlockReference) || !marker.Visible ||
                    !string.Equals(EffectiveName(marker, tr), MarkerBlockName, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(marker.Layer, MarkerLayer, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("marker name, layer, visibility or entity type is unsupported");
                RequireLocalDefinition(marker, tr);
                var state = GisImportCommands.InspectObjectDataFingerprint(marker.ObjectId, out _, out string odDetail);
                if (state != GisImportCommands.ObjectDataFingerprintState.Empty)
                    throw new InvalidOperationException("marker native OD must be verified empty: " + odDetail);
                using ResultBuffer? xdata = marker.XData;
                if (marker.AttributeCollection.Count > 0 || !marker.ExtensionDictionary.IsNull || (xdata?.AsArray().Length ?? 0) > 0)
                    throw new InvalidOperationException("marker carries attributes, XData or extension data");
                return marker.ObjectId;
            }
            catch (System.Exception ex)
            {
                reviews.Add(SourceReview(source, $"marker {Handle(marker.ObjectId)} retained: {ex.Message}"));
                return ObjectId.Null;
            }
        }

        private static CapturedBlock Capture(Transaction tr, BlockReference block)
        {
            if (block.GetType() != typeof(BlockReference) || block.IsErased ||
                !string.Equals(EffectiveName(block, tr), ManholeBlockName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("the source is not the supported local manhole block");
            if (!block.Visible) throw new InvalidOperationException("the manhole block is not visible");
            var outer = new List<SewerManholeCircle>();
            var inner = new List<SewerManholeCircle>();
            var signature = new Signature();
            signature.Add("sewer-manhole-plan-v1");
            StormStructureVisibility.VisitVisible(new GeometryNode(block, Matrix3d.Identity),
                node => !node.Entity.IsErased && node.Entity.Visible,
                node =>
                {
                    if (node.Entity is not BlockReference nested) return null;
                    if (nested.GetType() != typeof(BlockReference))
                        throw new InvalidOperationException("a visible nested reference has an unsupported derived entity type");
                    BlockTableRecord definition = RequireLocalDefinition(nested, tr);
                    RequireSupportedInstanceData(nested, tr);
                    if (nested.ObjectId != block.ObjectId &&
                        GisImportCommands.InspectObjectDataFingerprint(nested.ObjectId, out _, out string nestedDetail) !=
                            GisImportCommands.ObjectDataFingerprintState.Empty)
                        throw new InvalidOperationException($"nested block {Handle(nested.ObjectId)} carries unverified native OD: {nestedDetail}");
                    AddBlockState(signature, nested, tr);
                    Matrix3d transform = node.ToWorld * nested.BlockTransform;
                    RequireUniformTransform(transform);
                    var children = new List<GeometryNode>();
                    // Always inspect the evaluated BTR, never DynamicBlockTableRecord.
                    foreach (ObjectId id in definition)
                        if (tr.GetObject(id, OpenMode.ForRead, false) is Entity child)
                            children.Add(new GeometryNode(child, transform));
                    return children;
                },
                node =>
                {
                    Entity entity = node.Entity;
                    RequireEmptyLeafData(entity);
                    bool isOuter = OuterLayer.IsMatch(entity.Layer ?? string.Empty);
                    bool isInner = InnerLayer.IsMatch(entity.Layer ?? string.Empty);
                    if (!isOuter && !isInner && IsAnnotation(entity)) return;
                    if ((!isOuter && !isInner) || entity.GetType() != typeof(Circle))
                        throw new InvalidOperationException($"unsupported visible {entity.GetType().Name} on layer '{entity.Layer}'");
                    SewerManholeCircle circle = ReadCircle((Circle)entity, node.ToWorld);
                    signature.Add(Handle(entity.ObjectId)); signature.Add(entity.Layer);
                    signature.Add(circle.Center); signature.Add(circle.Normal); signature.Add(circle.Radius);
                    signature.Add(GisNewDrawingClone.ReadPrimitiveSignature(entity, tr));
                    (isOuter ? outer : inner).Add(circle);
                });
            if (outer.Count != 1)
                throw new InvalidOperationException($"expected exactly one visible circle on a known outer layer; found {outer.Count}");
            if (inner.Count == 0)
                throw new InvalidOperationException("no corresponding visible circle on a known inner layer");
            SewerManholeCircle boundary = outer[0];
            foreach (SewerManholeCircle circle in inner)
                if (circle.Center.DistanceTo(boundary.Center) > GeometryTolerance ||
                    Math.Abs(Math.Abs(circle.Normal.DotProduct(boundary.Normal)) - 1.0) > TransformTolerance ||
                    circle.Radius >= boundary.Radius - GeometryTolerance)
                    throw new InvalidOperationException("inner and outer circles have mismatched centers, elevations, planes or radii");
            for (int i = 0; i < inner.Count; i++)
                for (int j = i + 1; j < inner.Count; j++)
                    if (Math.Abs(inner[i].Radius - inner[j].Radius) <= GeometryTolerance)
                        throw new InvalidOperationException("duplicate visible inner circles require review");
            return new CapturedBlock(boundary, inner, signature.Finish());
        }

        private static SewerManholeCircle ReadCircle(Circle circle, Matrix3d transform)
        {
            if (circle.Color.ColorMethod == Autodesk.AutoCAD.Colors.ColorMethod.ByBlock ||
                string.Equals(circle.Linetype, "ByBlock", StringComparison.OrdinalIgnoreCase) ||
                circle.LineWeight == LineWeight.ByBlock || circle.Transparency.IsByBlock)
                throw new InvalidOperationException("circle appearance inherits ByBlock properties; retain the block until its intended standalone appearance is resolved");
            if (!Finite(circle.Center) || !Finite(circle.Normal) || !double.IsFinite(circle.Radius) || circle.Radius <= 0 ||
                !double.IsFinite(circle.Thickness) || circle.Thickness != 0 || circle.Normal.Length <= 0)
                throw new InvalidOperationException("circle has invalid XYZ, normal, radius or nonzero thickness");
            Vector3d normal = circle.Normal.GetNormal();
            Vector3d localU = normal.GetPerpendicularVector().GetNormal();
            Vector3d u = localU.TransformBy(transform);
            Vector3d v = normal.CrossProduct(localU).TransformBy(transform);
            double scale = u.Length;
            if (!Finite(u) || !Finite(v) || !double.IsFinite(scale) || scale <= 0 ||
                Math.Abs(scale - v.Length) > TransformTolerance * Math.Max(scale, v.Length) ||
                Math.Abs(u.DotProduct(v)) > TransformTolerance * scale * v.Length)
                throw new InvalidOperationException("circle transform is nonuniform or noncircular");
            Vector3d worldNormal = u.GetNormal().CrossProduct(v.GetNormal()).GetNormal();
            Point3d center = circle.Center.TransformBy(transform);
            double radius = circle.Radius * scale;
            if (!Finite(center) || !Finite(worldNormal) || !double.IsFinite(radius) || radius <= 0 ||
                Math.Abs(worldNormal.X) > TransformTolerance || Math.Abs(worldNormal.Y) > TransformTolerance ||
                Math.Abs(Math.Abs(worldNormal.Z) - 1.0) > TransformTolerance)
                throw new InvalidOperationException("transformed circle is invalid or is not in a world-XY plane");
            // Preserve the computed XYZ and normal, including nonzero elevation. No flattening.
            return new SewerManholeCircle(circle.ObjectId, center, worldNormal, radius, circle.Layer);
        }

        private static void RequireEmptyLeafData(Entity entity)
        {
            using ResultBuffer? xdata = entity.XData;
            if ((xdata?.AsArray().Length ?? 0) != 0 || !entity.ExtensionDictionary.IsNull)
                throw new InvalidOperationException($"visible block entity {Handle(entity.ObjectId)} carries XData or extension data requiring review");
            if (GisImportCommands.InspectObjectDataFingerprint(entity.ObjectId, out _, out string detail) !=
                GisImportCommands.ObjectDataFingerprintState.Empty)
                throw new InvalidOperationException($"visible block entity {Handle(entity.ObjectId)} carries unverified native OD: {detail}");
        }

        private static void RequireUniformTransform(Matrix3d transform)
        {
            if (transform.ToArray().Any(value => !double.IsFinite(value)))
                throw new InvalidOperationException("block transform contains nonfinite values");
            Vector3d x = Vector3d.XAxis.TransformBy(transform);
            Vector3d y = Vector3d.YAxis.TransformBy(transform);
            Vector3d z = Vector3d.ZAxis.TransformBy(transform);
            double scale = x.Length;
            if (!Finite(x) || !Finite(y) || !Finite(z) || !double.IsFinite(scale) || scale <= 0 ||
                Math.Abs(scale - y.Length) > TransformTolerance * Math.Max(scale, y.Length) ||
                Math.Abs(scale - z.Length) > TransformTolerance * Math.Max(scale, z.Length) ||
                Math.Abs(x.GetNormal().DotProduct(y.GetNormal())) > TransformTolerance ||
                Math.Abs(x.GetNormal().DotProduct(z.GetNormal())) > TransformTolerance ||
                Math.Abs(y.GetNormal().DotProduct(z.GetNormal())) > TransformTolerance)
                throw new InvalidOperationException("block transform has zero/nonuniform scale or shear");
        }

        private static BlockTableRecord RequireLocalDefinition(BlockReference block, Transaction tr)
        {
            var definition = (BlockTableRecord)tr.GetObject(block.BlockTableRecord, OpenMode.ForRead);
            if (definition.IsFromExternalReference || definition.IsFromOverlayReference || definition.IsLayout)
                throw new InvalidOperationException("external or layout block definitions require review");
            if (block.IsDynamicBlock)
            {
                var authoring = (BlockTableRecord)tr.GetObject(block.DynamicBlockTableRecord, OpenMode.ForRead);
                if (authoring.IsFromExternalReference || authoring.IsFromOverlayReference || authoring.IsLayout)
                    throw new InvalidOperationException("external or layout dynamic authoring definitions require review");
            }
            return definition;
        }

        private static void RequireSupportedInstanceData(BlockReference block, Transaction tr)
        {
            if (block.AttributeCollection.Count > 0)
                throw new InvalidOperationException($"block {Handle(block.ObjectId)} has instance attributes that cannot be discarded");
            if (!block.ExtensionDictionary.IsNull)
            {
                var dictionary = (DBDictionary)tr.GetObject(block.ExtensionDictionary, OpenMode.ForRead);
                foreach (DBDictionaryEntry entry in dictionary)
                {
                    // A normal dynamic INSERT owns this native representation container.
                    // Its semantics are read through the public dynamic-block APIs, not
                    // by editing the evaluation graph or converting the source to static.
                    if (!block.IsDynamicBlock || !string.Equals(entry.Key, "AcDbBlockRepresentation", StringComparison.Ordinal) ||
                        tr.GetObject(entry.Value, OpenMode.ForRead) is not DBDictionary representation ||
                        !representation.Contains("AcDbRepData") ||
                        !string.Equals(representation.GetAt("AcDbRepData").ObjectClass.Name, "AcDbBlockRepresentationData", StringComparison.Ordinal))
                        throw new InvalidOperationException($"block {Handle(block.ObjectId)} has unverified extension data '{entry.Key}'");
                    foreach (DBDictionaryEntry item in representation)
                        if (!string.Equals(item.Key, "AcDbRepData", StringComparison.Ordinal) &&
                            !string.Equals(item.Key, "AppDataCache", StringComparison.Ordinal))
                            throw new InvalidOperationException($"block {Handle(block.ObjectId)} has unknown dynamic representation entry '{item.Key}'");
                    if (representation.Contains("AppDataCache"))
                    {
                        if (tr.GetObject(representation.GetAt("AppDataCache"), OpenMode.ForRead) is not DBDictionary cache)
                            throw new InvalidOperationException("dynamic AppDataCache has an unsupported native type");
                        foreach (DBDictionaryEntry item in cache)
                            if (!string.Equals(item.Key, "ACAD_ENHANCEDBLOCKDATA", StringComparison.Ordinal) &&
                                !string.Equals(item.Key, "ACAD_ENHANCEDBLOCKHDATA", StringComparison.Ordinal) &&
                                !string.Equals(item.Key, "ACAD_ENHANCEDBLOCKHISTORY", StringComparison.Ordinal))
                                throw new InvalidOperationException($"block {Handle(block.ObjectId)} has unknown dynamic cache entry '{item.Key}'");
                    }
                }
            }
            using ResultBuffer? data = block.XData;
            TypedValue[] values = data?.AsArray() ?? Array.Empty<TypedValue>();
            if (values.Length > 0 && values[0].TypeCode != 1001)
                throw new InvalidOperationException($"block {Handle(block.ObjectId)} has malformed/unowned XData");
            foreach (TypedValue value in values)
            {
                // This is the existing AutoCAD dynamic representation tag, not user
                // data. Every other XData application remains a review requirement.
                if (value.TypeCode != 1001) continue;
                if (!block.IsDynamicBlock || !string.Equals(Convert.ToString(value.Value, CultureInfo.InvariantCulture),
                    "AcDbBlockRepETag", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"block {Handle(block.ObjectId)} has unverified XData application '{value.Value}'");
            }
        }

        private static void AddBlockState(Signature signature, BlockReference block, Transaction tr)
        {
            signature.Add(Handle(block.ObjectId)); signature.Add(EffectiveName(block, tr)); signature.Add(block.Layer);
            signature.Add(Handle(block.BlockTableRecord)); signature.Add(block.IsDynamicBlock);
            signature.Add(block.Position); signature.Add(block.Normal); signature.Add(block.Rotation);
            foreach (double element in block.BlockTransform.ToArray()) signature.Add(element);
            if (block.IsDynamicBlock)
            {
                signature.Add(Handle(block.DynamicBlockTableRecord));
                var properties = new List<string>();
                foreach (DynamicBlockReferenceProperty property in block.DynamicBlockReferencePropertyCollection)
                {
                    var state = new Signature(); state.Add(property.PropertyName); state.Add(property.UnitsType);
                    state.Add(property.ReadOnly); state.Add(property.Value); properties.Add(state.Finish());
                }
                foreach (string property in properties.OrderBy(p => p, StringComparer.Ordinal)) signature.Add(property);
            }
            if (!block.ExtensionDictionary.IsNull)
                AddDynamicMetadataState(signature, block.ExtensionDictionary, tr, 0);
            var odState = GisImportCommands.InspectObjectDataFingerprint(block.ObjectId, out string fingerprint, out string detail);
            if (odState == GisImportCommands.ObjectDataFingerprintState.ReadFailed) throw new InvalidOperationException(detail);
            signature.Add(odState); signature.Add(fingerprint);
            using ResultBuffer? xdata = block.XData;
            foreach (TypedValue value in xdata?.AsArray() ?? Array.Empty<TypedValue>())
            { signature.Add(value.TypeCode); signature.Add(value.Value); }
        }

        private static void AddDynamicMetadataState(Signature signature, ObjectId id, Transaction tr, int depth)
        {
            if (depth > 16) throw new InvalidOperationException("dynamic metadata nesting requires review");
            DBObject value = tr.GetObject(id, OpenMode.ForRead);
            signature.Add(Handle(id)); signature.Add(id.ObjectClass.Name);
            if (value is DBDictionary dictionary)
            {
                foreach (DBDictionaryEntry entry in dictionary.Cast<DBDictionaryEntry>().OrderBy(e => e.Key, StringComparer.Ordinal))
                {
                    signature.Add(entry.Key);
                    AddDynamicMetadataState(signature, entry.Value, tr, depth + 1);
                }
            }
            else if (value is Xrecord record)
            {
                using ResultBuffer? data = record.Data;
                foreach (TypedValue item in data?.AsArray() ?? Array.Empty<TypedValue>())
                { signature.Add(item.TypeCode); signature.Add(item.Value); }
            }
            else if (!string.Equals(id.ObjectClass.Name, "AcDbBlockRepresentationData", StringComparison.Ordinal))
                throw new InvalidOperationException($"unsupported dynamic metadata type '{id.ObjectClass.Name}'");
        }

        private static bool IsAnnotation(Entity entity) => entity is AttributeDefinition ||
            entity is DBText && entity is not AttributeReference || entity is MText ||
            entity is Dimension || entity is Leader || entity is MLeader;
        private static bool SewerManholePart(string part) =>
            part.IndexOf("DCSWCS", StringComparison.OrdinalIgnoreCase) >= 0 ||
            part.IndexOf("BARREL", StringComparison.OrdinalIgnoreCase) >= 0 && part.IndexOf("FRAME", StringComparison.OrdinalIgnoreCase) >= 0;
        private static bool ManholeHint(string part) => SewerManholePart(part) || part.IndexOf("MANHOLE", StringComparison.OrdinalIgnoreCase) >= 0;
        private static string EffectiveName(BlockReference block, Transaction tr) =>
            ((BlockTableRecord)tr.GetObject(block.IsDynamicBlock ? block.DynamicBlockTableRecord : block.BlockTableRecord, OpenMode.ForRead)).Name;
        private static bool KnownName(string value, string name) => string.Equals(value, name, StringComparison.OrdinalIgnoreCase) ||
            value.EndsWith("|" + name, StringComparison.OrdinalIgnoreCase);
        private static bool NearXY(Point3d a, Point3d b)
        {
            if (!Finite(a) || !Finite(b)) return false;
            double dx = a.X - b.X, dy = a.Y - b.Y;
            return dx * dx + dy * dy <= StormStructureMatching.MatchTolerance * StormStructureMatching.MatchTolerance;
        }
        private static bool Finite(Point3d value) => double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);
        private static bool SameElevation(Point3d a, Point3d b) => Math.Abs(a.Z - b.Z) <= GeometryTolerance;
        private static bool Finite(Vector3d value) => double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);
        private static string Handle(ObjectId id) => id.IsNull ? "<null>" : id.Handle.ToString();
        private static string SourceReview(Source source, string message) => $"Structures point {Handle(source.Id)} ('{source.Name}'): {message}; retained for review.";

        private enum SourceKind { Ambiguous, Other, Manhole, NullEnd }
        private sealed class Source
        {
            internal Source(ObjectId id, string name, string partSizeName, Point3d position)
            { Id = id; Name = name; PartSizeName = partSizeName; Position = position; }
            internal ObjectId Id { get; }
            internal string Name { get; }
            internal string PartSizeName { get; }
            internal Point3d Position { get; }
            internal SourceKind Kind { get; set; }
            internal string Fingerprint { get; set; } = string.Empty;
            internal string Problem { get; set; } = string.Empty;
        }
        private sealed record GeometryNode(Entity Entity, Matrix3d ToWorld);
        private sealed record CapturedBlock(SewerManholeCircle Outer, IReadOnlyList<SewerManholeCircle> Inner, string Signature);
        private sealed class Signature
        {
            private readonly StringBuilder text = new StringBuilder();
            internal void Add(object? value)
            {
                if (value is Point3d point) { Add(point.X); Add(point.Y); Add(point.Z); return; }
                if (value is Vector3d vector) { Add(vector.X); Add(vector.Y); Add(vector.Z); return; }
                string token = value is double number ? number.ToString("R", CultureInfo.InvariantCulture) :
                    Convert.ToString(value, CultureInfo.InvariantCulture) ?? "<null>";
                text.Append(token.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(token);
            }
            internal string Finish() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
        }
    }
}
