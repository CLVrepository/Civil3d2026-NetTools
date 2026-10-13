using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace CLV_CivilTools.Gis
{
    internal static partial class GisSewerPreparation
    {
        // Separate, additive archive: existing V2 batch archives stay readable.
        // Full typed OD and original coordinates survive removal of the live POINT.
        private const string StubLinkedArchiveVersion = "CLV_SEWER_PREP_V3_S9_STUBS";
        private const string StubArchiveKey = "CLV_SEWER_STUB_ARCHIVE_V1";
        internal sealed record StubPipeLink(string Handle, string Name, bool AtStart, double X, double Y, double Z);
        internal sealed record StubArchiveEntry(string Handle, string Name, string Part, double X, double Y, double Z,
            string SourceShape, string OdFingerprint, string NativeOd, StubPipeLink[] Pipes);
        internal sealed record StubArchiveData(int Version, StubArchiveEntry[] Points);
        private sealed record StubCandidate(Output Source, StubArchiveEntry Saved, string Metadata);

        private static BatchArchive CleanupArchivedStubPoints(Document document, Transaction tr,
            BlockTableRecord model, BatchArchive archive, out int erased)
        {
            Database db = document.Database;
            VerifyArchive(db, tr, model.ObjectId, archive);
            StubArchiveData prior = ReadStubArchive(db, tr);
            var candidates = new List<StubCandidate>();
            foreach (Output source in archive.Outputs.Where(item => item.Role == "OpenNullEnd"))
            {
                if (!TryCaptureStub(tr, model.ObjectId, archive, source, out StubCandidate? candidate, out string reason))
                {
                    document.Editor.WriteMessage("\nSEWER STUB RETAINED {0} ({1}): {2}", source.Id.Handle, source.Name, reason);
                    continue;
                }
                if (prior.Points.Any(item => SameName(item.Handle, source.Id.Handle.ToString()) || SameName(item.Name, source.Name)))
                    throw new InvalidOperationException("A live stub conflicts with an already archived source; cleanup requires review.");
                candidates.Add(candidate!);
            }
            erased = 0;
            if (candidates.Count == 0) return archive;
            var removing = candidates.Select(item => item.Source.Id).ToHashSet();
            var saved = new StubArchiveData(1, prior.Points.Concat(candidates.Select(item => item.Saved)).ToArray());
            var updated = archive with { Outputs = Array.AsReadOnly(archive.Outputs.Where(item => !removing.Contains(item.Id)).ToArray()), StubArchiveDigest = StubDigest(saved) };
            SewerPointCleanup.Apply(candidates,
                item => { RequireUnlocked(tr, item.Source.Id); tr.GetObject(item.Source.Id, OpenMode.ForWrite, false); },
                () =>
                {
                    VerifyArchive(db, tr, model.ObjectId, archive);
                    foreach (StubCandidate item in candidates)
                        if (!TryCaptureStub(tr, model.ObjectId, archive, item.Source, out StubCandidate? current, out string reason) ||
                            current!.Metadata != item.Metadata || JsonSerializer.Serialize(current.Saved) != JsonSerializer.Serialize(item.Saved))
                            throw new InvalidOperationException($"Stub {item.Source.Id.Handle} changed before cleanup: {reason}");
                },
                () =>
                {
                    WriteStubArchive(db, tr, prior, saved);
                    ReplaceArchive(db, tr, archive, updated);
                    RequireSameArchive(updated, ReadArchive(db, tr) ?? throw new InvalidOperationException("Sewer archive disappeared."));
                    VerifyArchive(db, tr, model.ObjectId, updated, removing);
                },
                item => ((DBPoint)tr.GetObject(item.Source.Id, OpenMode.ForWrite, false)).Erase(true),
                () => VerifyArchive(db, tr, model.ObjectId, updated));
            erased = candidates.Count;
            return updated;
        }

        private static bool TryCaptureStub(Transaction tr, ObjectId model, BatchArchive archive, Output source,
            out StubCandidate? candidate, out string reason)
        {
            candidate = null;
            reason = "No unique archive-owned, unchanged Structures POINT.";
            if (source.Role != "OpenNullEnd" || source.OdState != GisImportCommands.ObjectDataFingerprintState.Present ||
                archive.Outputs.Count(item => SameName(item.Name, source.Name) &&
                    (item.Role == "OpenNullEnd" || item.Role == "OpenConnectionEnd" || item.Role == "RetainedSourcePoint")) != 1 ||
                archive.Structures.Any(item => SameName(item.Name, source.Name)) ||
                tr.GetObject(source.Id, OpenMode.ForRead, false) is not DBPoint point || point.GetType() != typeof(DBPoint) ||
                point.OwnerId != model || point.IsErased || !SameName(point.Layer, "Structures") ||
                Shape(tr, point) != source.Shape || !Finite(point.Position)) return false;
            if (!GisImportCommands.TryReadSewerStructuresIdentity(source.Id, out string name, out string part, out string detail))
                throw new InvalidOperationException("Stub identity read failed: " + detail);
            reason = "Identity is not the exact supported null/STUB convention.";
            if (!SameName(name, source.Name) || SewerPreparationRules.IsExplicitSewerConnectionName(name) ||
                StormStructureMatching.Classify(name, part) != StormStructureRole.NullPipeEnd) return false;
            var metadata = ReadCleanupMetadata(tr, point);
            reason = "Additional or unsupported point metadata is preserved. " + DescribeCleanupMetadata(metadata);
            if (SewerPointCleanup.Classify(metadata) == SewerPointMetadataKind.Review) return false;
            RequireOd(source.Id, source.OdState, source.OdFingerprint);
            StubPipeLink[] links = CaptureStubPipeLinks(tr, archive, name, point.Position);
            reason = "No verified owned pipe endpoint names this stub.";
            if (!SewerPointCleanup.CanArchiveStub(source.Role, true, true, links.Length, metadata)) return false;
            string payload = GisImportCommands.CaptureNativeObjectDataArchive(source.Id, source.OdFingerprint);
            var saved = new StubArchiveEntry(source.Id.Handle.ToString(), name, part, point.Position.X, point.Position.Y,
                point.Position.Z, source.Shape, source.OdFingerprint, payload, links);
            var signature = new StringBuilder();
            AppendOutputDictionaryMetadata(tr, point.ExtensionDictionary, signature, 0);
            candidate = new StubCandidate(source, saved, signature.ToString());
            reason = "";
            return true;
        }

        private static StubPipeLink[] CaptureStubPipeLinks(Transaction tr, BatchArchive archive, string name, Point3d anchor)
        {
            var result = new List<StubPipeLink>();
            foreach (var original in archive.OriginalPipes)
            {
                Output[] owners = archive.Outputs.Where(item => item.Id == original.Id &&
                    (item.Role == "SinglePipe" || item.Role == "PipeCenter") && SameName(item.Name, original.Name)).ToArray();
                if (owners.Length != 1 || tr.GetObject(original.Id, OpenMode.ForRead, false) is not Line line || line.GetType() != typeof(Line))
                    throw new InvalidOperationException("Stub pipe ownership is missing or ambiguous.");
                if (!GisImportCommands.TryReadSewerPipeData(original.Id, out var data, out string detail, allowBlankEndpoints: true) || data == null)
                    throw new InvalidOperationException("Stub pipe native OD read failed: " + detail);
                bool first = SameName(data.StructureStart, name), last = SameName(data.StructureEnd, name);
                if (!first && !last) continue;
                if (first && last || !SameName(data.Name, original.Name))
                    throw new InvalidOperationException("Stub pipe endpoint identity is ambiguous.");
                // The original accepted null-end contract is retained. No snapping,
                // shortening, extending or wall/centerline edits occur in cleanup.
                bool atStart = NearStub(line.StartPoint, anchor), atEnd = NearStub(line.EndPoint, anchor);
                if (atStart == atEnd) throw new InvalidOperationException("Stub does not resolve to exactly one unchanged pipe terminal.");
                Point3d terminal = atStart ? line.StartPoint : line.EndPoint;
                result.Add(new StubPipeLink(original.Id.Handle.ToString(), original.Name, atStart, terminal.X, terminal.Y, terminal.Z));
            }
            return result.OrderBy(item => item.Handle, StringComparer.Ordinal).ToArray();
        }

        private static bool NearStub(Point3d end, Point3d anchor)
            => Math.Sqrt(Math.Pow(end.X - anchor.X, 2) + Math.Pow(end.Y - anchor.Y, 2)) <= 0.10 && Math.Abs(end.Z - anchor.Z) <= Tolerance;
        private static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static StubArchiveData ReadStubArchive(Database db, Transaction tr)
        {
            var root = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!root.Contains(StubArchiveKey)) return new StubArchiveData(1, Array.Empty<StubArchiveEntry>());
            var record = tr.GetObject(root.GetAt(StubArchiveKey), OpenMode.ForRead) as Xrecord
                ?? throw new InvalidOperationException("Stub archive has an unexpected type.");
            using ResultBuffer? data = record.Data;
            TypedValue[] values = data?.AsArray() ?? throw new InvalidOperationException("Stub archive is empty.");
            if (values.Length < 2 || values[0].TypeCode != (int)DxfCode.Text || (string)values[0].Value != StubArchiveKey ||
                values[1].TypeCode != (int)DxfCode.Int32 || (int)values[1].Value != values.Length - 2 ||
                values.Skip(2).Any(item => item.TypeCode != (int)DxfCode.Text || item.Value is not string))
                throw new InvalidOperationException("Stub archive framing is invalid.");
            string json = string.Concat(values.Skip(2).Select(item => (string)item.Value));
            StubArchiveData parsed = JsonSerializer.Deserialize<StubArchiveData>(json)
                ?? throw new InvalidOperationException("Stub archive is unreadable.");
            if (parsed.Version != 1 || parsed.Points == null || json != JsonSerializer.Serialize(parsed)) throw new InvalidOperationException("Unsupported stub archive version.");
            return parsed;
        }

        private static void WriteStubArchive(Database db, Transaction tr, StubArchiveData expected, StubArchiveData updated)
        {
            if (JsonSerializer.Serialize(expected) != JsonSerializer.Serialize(ReadStubArchive(db, tr)))
                throw new InvalidOperationException("Stub archive changed before write.");
            var root = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
            Xrecord record;
            if (root.Contains(StubArchiveKey)) record = (Xrecord)tr.GetObject(root.GetAt(StubArchiveKey), OpenMode.ForWrite);
            else { record = new Xrecord(); root.SetAt(StubArchiveKey, record); tr.AddNewlyCreatedDBObject(record, true); }
            var values = new List<TypedValue> { Text(StubArchiveKey) };
            AddTextChunks(values, JsonSerializer.Serialize(updated));
            using var data = new ResultBuffer(values.ToArray()); record.Data = data;
            if (JsonSerializer.Serialize(updated) != JsonSerializer.Serialize(ReadStubArchive(db, tr)))
                throw new InvalidOperationException("Complete stub archive did not read back; erasure is prohibited.");
        }

        private static bool ArchivedIdentity(NativeOdArchiveRecord record, string fieldName, string expected)
        {
            NativeOdArchiveField[] fields = record.Fields.Where(item => SameName(item.Name, fieldName)).ToArray();
            return fields.Length == 1 && fields[0].Kind == "Character" && fields[0].Values.Length == 1 &&
                string.Equals(fields[0].Values[0].Trim(), expected, StringComparison.Ordinal);
        }

        private static string StubDigest(StubArchiveData archive)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(archive))));

        private static void VerifyStubArchive(Database db, Transaction tr, BatchArchive archive, ISet<ObjectId>? pendingErases)
        {
            StubArchiveData saved = ReadStubArchive(db, tr);
            if (!SewerPointCleanup.ValidStubArchiveLink(archive.StubArchiveDigest, saved.Points.Length, StubDigest(saved)))
                throw new InvalidOperationException("Required complete stub archive is missing, unlinked or changed.");
            var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (StubArchiveEntry point in saved.Points)
            {
                if (point == null || string.IsNullOrWhiteSpace(point.Handle) || !handles.Add(point.Handle) ||
                    string.IsNullOrWhiteSpace(point.Name) || !names.Add(point.Name) || string.IsNullOrEmpty(point.SourceShape) ||
                    point.Pipes == null || point.Pipes.Length == 0 ||
                    StormStructureMatching.Classify(point.Name, point.Part) != StormStructureRole.NullPipeEnd ||
                    SewerPreparationRules.IsExplicitSewerConnectionName(point.Name) ||
                    NativeOdArchive.Fingerprint(NativeOdArchive.Deserialize(point.NativeOd)) != point.OdFingerprint)
                    throw new InvalidOperationException("Archived stub identity or complete typed OD is invalid.");
                NativeOdArchiveRecord[] native = NativeOdArchive.Deserialize(point.NativeOd);
                NativeOdArchiveRecord[] identities = native.Where(item => SameName(item.TableName, "Structures")).ToArray();
                if (identities.Length == 0 || identities.Any(item => !ArchivedIdentity(item, "Name", point.Name) ||
                    !ArchivedIdentity(item, "PartSizeName", point.Part)))
                    throw new InvalidOperationException("Archived stub identity differs from its preserved native OD.");
                if (!long.TryParse(point.Handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long handle) || handle <= 0)
                    throw new InvalidOperationException("Archived stub source handle is invalid.");
                if (db.TryGetObjectId(new Handle(handle), out ObjectId original) && original.IsValid && !original.IsErased &&
                    (pendingErases == null || !pendingErases.Contains(original)))
                    throw new InvalidOperationException("An archived stub source is still live or was restored; review is required.");
                Point3d anchor = new Point3d(point.X, point.Y, point.Z);
                if (!Finite(anchor) || archive.Outputs.Any(item => SameName(item.Id.Handle.ToString(), point.Handle)) ||
                    archive.Structures.Any(item => SameName(item.Name, point.Name)) ||
                    JsonSerializer.Serialize(CaptureStubPipeLinks(tr, archive, point.Name, anchor)) != JsonSerializer.Serialize(point.Pipes))
                    throw new InvalidOperationException("Archived stub provenance or owned pipe terminals differ.");
            }
        }
    }
}
