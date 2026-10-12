using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

namespace CLV_CivilTools.Gis
{
    internal static partial class GisSewerPreparation
    {
        private sealed record PointCleanupCandidate(Output Source, Output Outer,
            StructureArchive Structure, string MetadataSnapshot);

        private static BatchArchive CleanupArchivedManholePoints(Document document, Transaction tr,
            BlockTableRecord model, BatchArchive archive, out int erased)
        {
            Database db = document.Database;
            VerifyArchive(db, tr, model.ObjectId, archive);
            var candidates = new List<PointCleanupCandidate>();
            foreach (Output source in archive.Outputs.Where(output => output.Role == "RetainedSourcePoint"))
            {
                var links = archive.Structures.Where(structure =>
                    string.Equals(structure.Handle, source.Id.Handle.ToString(), StringComparison.OrdinalIgnoreCase)).ToArray();
                if (links.Length != 1)
                { Retain(source, "Missing or ambiguous archived source handle link."); continue; }
                StructureArchive link = links[0];
                var outers = archive.Outputs.Where(output => output.Id == link.OuterId &&
                    output.Role == "ManholeOuter" && string.Equals(output.Name, link.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
                bool unique = SewerPointCleanup.UniqueLinks(links.Length, outers.Length,
                    archive.Structures.Count(item => item.OuterId == link.OuterId),
                    archive.Structures.Count(item => string.Equals(item.Name, link.Name, StringComparison.OrdinalIgnoreCase)),
                    archive.Outputs.Count(item => item.Role == "ManholeOuter" && string.Equals(item.Name, link.Name, StringComparison.OrdinalIgnoreCase)),
                    archive.Outputs.Count(item => item.Role == "RetainedSourcePoint" && string.Equals(item.Name, link.Name, StringComparison.OrdinalIgnoreCase)));
                if (!unique)
                { Retain(source, "Missing or ambiguous archived outer-circle ownership link."); continue; }
                var candidate = new PointCleanupCandidate(source, outers[0], link, "");
                if (!TryProveCleanupPoint(tr, model.ObjectId, candidate, out string snapshot, out string reason))
                { Retain(source, reason); continue; }
                candidates.Add(candidate with { MetadataSnapshot = snapshot });
            }
            erased = 0;
            if (candidates.Count == 0) return archive;
            var removing = candidates.Select(candidate => candidate.Source.Id).ToHashSet();
            var updated = archive with { Outputs = Array.AsReadOnly(archive.Outputs.Where(output => !removing.Contains(output.Id)).ToArray()) };
            SewerPointCleanup.Apply(candidates,
                candidate =>
                {
                    RequireUnlocked(tr, candidate.Source.Id);
                    tr.GetObject(candidate.Source.Id, OpenMode.ForWrite, false);
                },
                () =>
                {
                    VerifyArchive(db, tr, model.ObjectId, archive);
                    foreach (var candidate in candidates)
                        if (!TryProveCleanupPoint(tr, model.ObjectId, candidate, out string snapshot, out string reason) ||
                            snapshot != candidate.MetadataSnapshot)
                            throw new InvalidOperationException($"Point {candidate.Source.Id.Handle} changed before cleanup: {reason}; cleanup rolled back.");
                },
                () =>
                {
                    ReplaceArchive(db, tr, archive, updated);
                    BatchArchive stored = ReadArchive(db, tr) ?? throw new InvalidOperationException("Updated sewer archive is unavailable.");
                    RequireSameArchive(updated, stored);
                    VerifyArchive(db, tr, model.ObjectId, stored);
                },
                candidate => ((DBPoint)tr.GetObject(candidate.Source.Id, OpenMode.ForWrite, false)).Erase(true),
                () => VerifyArchive(db, tr, model.ObjectId, updated));
            erased = candidates.Count;
            return updated;

            void Retain(Output point, string reason)
                => document.Editor.WriteMessage("\nSEWER POINT RETAINED {0} ({1}): {2}", point.Id.Handle, point.Name, reason);
        }

        private static bool TryProveCleanupPoint(Transaction tr, ObjectId model, PointCleanupCandidate candidate,
            out string snapshot, out string reason)
        {
            snapshot = "";
            reason = "Source or outer circle is not the exact archived entity type/owner.";
            Output expected = candidate.Source;
            if (tr.GetObject(expected.Id, OpenMode.ForRead, false) is not DBPoint point || point.GetType() != typeof(DBPoint) ||
                point.OwnerId != model || point.IsErased || !string.Equals(point.Layer, "Structures", StringComparison.OrdinalIgnoreCase) ||
                tr.GetObject(candidate.Outer.Id, OpenMode.ForRead, false) is not Circle circle || circle.GetType() != typeof(Circle) ||
                circle.OwnerId != model || circle.IsErased || !string.Equals(circle.Layer, OuterLayer, StringComparison.OrdinalIgnoreCase)) return false;
            StructureArchive link = candidate.Structure;
            if (SewerPreparationRules.IsExplicitSewerConnectionName(link.Name) ||
                link.Name.EndsWith("-STUB", StringComparison.OrdinalIgnoreCase) || link.Part.IndexOf("NULL", StringComparison.OrdinalIgnoreCase) >= 0)
            { reason = "Connection/null/review points are not cleanup candidates."; return false; }
            if (!GisImportCommands.TryReadSewerStructuresIdentity(point.ObjectId, out string name, out string part, out string detail))
                throw new InvalidOperationException($"Point {point.Handle} native identity could not be verified: {detail}");
            var sourceState = GisImportCommands.InspectObjectDataFingerprint(point.ObjectId, out string sourceOd, out string sourceDetail);
            var outerState = GisImportCommands.InspectObjectDataFingerprint(circle.ObjectId, out string outerOd, out string outerDetail);
            if (sourceState == GisImportCommands.ObjectDataFingerprintState.ReadFailed || outerState == GisImportCommands.ObjectDataFingerprintState.ReadFailed)
                throw new InvalidOperationException($"Cleanup OD read failed: {sourceDetail}; {outerDetail}");
            var sourceMetadata = ReadCleanupMetadata(tr, point);
            var outerMetadata = ReadCleanupMetadata(tr, circle);
            bool identity = string.Equals(name, link.Name, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(part, link.Part, StringComparison.Ordinal) && string.Equals(expected.Name, link.Name, StringComparison.OrdinalIgnoreCase) &&
                point.Position.DistanceTo(link.Position) <= Tolerance &&
                Math.Sqrt(Math.Pow(circle.Center.X - link.Position.X, 2) + Math.Pow(circle.Center.Y - link.Position.Y, 2)) <= 0.10 &&
                Math.Abs(circle.Center.Z - link.Position.Z) <= Tolerance;
            var proof = new SewerPointCleanupProof(expected.Role, candidate.Outer.Role, true,
                Shape(tr, point) == expected.Shape, identity,
                sourceState == GisImportCommands.ObjectDataFingerprintState.Present,
                outerState == GisImportCommands.ObjectDataFingerprintState.Present,
                expected.OdFingerprint, sourceOd, candidate.Outer.OdFingerprint, outerOd, sourceMetadata, outerMetadata);
            if (!SewerPointCleanup.CanErase(proof, out reason))
            {
                reason += " Source metadata: " + DescribeCleanupMetadata(sourceMetadata);
                return false;
            }
            // Inner rings intentionally have no OD; only the exact archived outer
            // circle is the retained native-data destination for this source point.
            if (!GisImportCommands.TryVerifyTransferredObjectData(point.ObjectId, new[] { circle.ObjectId },
                out string transferred, out string transferDetail) || transferred != expected.OdFingerprint)
                throw new InvalidOperationException($"Point {point.Handle} complete OD preservation did not verify: {transferDetail}");
            var metadata = new StringBuilder();
            AppendOutputDictionaryMetadata(tr, point.ExtensionDictionary, metadata, 0);
            snapshot = Shape(tr, point) + "|" + sourceOd + "|" + metadata;
            return true;
        }

        private static SewerPointMetadata ReadCleanupMetadata(Transaction tr, Entity entity)
        {
            bool xdata = HasCleanupXData(entity);
            bool reactors = entity.GetPersistentReactorIds().Count != 0;
            if (entity.ExtensionDictionary.IsNull)
                return new SewerPointMetadata(xdata, reactors, false, true, false, Array.Empty<SewerPointMetadataEntry>());
            DBObject root = tr.GetObject(entity.ExtensionDictionary, OpenMode.ForRead, false);
            if (root is not DBDictionary dictionary || root.GetType() != typeof(DBDictionary))
                return new SewerPointMetadata(xdata, reactors, true, false, true, Array.Empty<SewerPointMetadataEntry>());
            var entries = new List<SewerPointMetadataEntry>();
            foreach (DBDictionaryEntry entry in dictionary)
            {
                DBObject child = tr.GetObject(entry.Value, OpenMode.ForRead, false);
                var childReactors = child.GetPersistentReactorIds();
                bool unexpectedReactors = childReactors.Count > 1 || childReactors.Cast<ObjectId>().Any(id => id != dictionary.ObjectId);
                entries.Add(new SewerPointMetadataEntry(entry.Key, entry.Value.ObjectClass.DxfName,
                    child.OwnerId == dictionary.ObjectId && !child.IsErased,
                    HasCleanupXData(child), !child.ExtensionDictionary.IsNull, unexpectedReactors));
            }
            return new SewerPointMetadata(xdata, reactors, true, dictionary.OwnerId == entity.ObjectId && !dictionary.IsErased,
                entries.Count != dictionary.Count || HasCleanupXData(dictionary) || !dictionary.ExtensionDictionary.IsNull || dictionary.GetPersistentReactorIds().Count != 0,
                entries.AsReadOnly());
        }

        private static bool HasCleanupXData(DBObject value)
        {
            using ResultBuffer? data = value.XData;
            return (data?.AsArray().Length ?? 0) != 0;
        }
        private static string DescribeCleanupMetadata(SewerPointMetadata metadata)
            => $"XData={metadata.HasXData}, reactors={metadata.HasUnexpectedReactors}, dictionary={metadata.HasDictionary}, " +
               $"owned={metadata.DictionaryIsExactAndOwned}, extra={metadata.DictionaryHasExtraMetadata}, " +
               string.Join(", ", metadata.Entries.Select(entry => $"{entry.Key}:{entry.DxfName}, owned={entry.Owned}, extra={entry.HasXData || entry.HasExtension || entry.HasUnexpectedReactors}"));
    }
}
