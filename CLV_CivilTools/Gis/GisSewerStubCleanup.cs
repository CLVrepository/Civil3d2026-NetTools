using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        private const string ConnectionLinkedArchiveVersion = "CLV_SEWER_PREP_V4_S10_OPEN_ENDS";
        private const string StubLinkedArchiveVersion = "CLV_SEWER_PREP_V3_S9_STUBS";
        private const string StubArchiveKey = "CLV_SEWER_STUB_ARCHIVE_V1";
        private sealed record StubCandidate(Output Source, OpenEndArchiveEntry Saved, string Metadata);

        private static BatchArchive CleanupArchivedOpenEndPoints(Document document, Transaction tr,
            BlockTableRecord model, BatchArchive archive, out int erasedStubs, out int erasedConnections)
        {
            Database db = document.Database;
            VerifyArchive(db, tr, model.ObjectId, archive);
            OpenEndArchiveState prior = ReadStubArchive(db, tr);
            var candidates = new List<StubCandidate>();
            foreach (Output source in archive.Outputs.Where(item => item.Role == "OpenNullEnd" || item.Role == "OpenConnectionEnd"))
            {
                if (!TryCaptureStub(tr, model.ObjectId, archive, source, out StubCandidate? candidate, out string reason))
                {
                    document.Editor.WriteMessage("\nSEWER OPEN-END POINT RETAINED {0} ({1}): {2}", source.Id.Handle, source.Name, reason);
                    continue;
                }
                if (prior.Points.Any(item => SameName(item.Source.Handle, source.Id.Handle.ToString()) || SameName(item.Source.Name, source.Name)))
                    throw new InvalidOperationException("A live open-end point conflicts with an already archived source; cleanup requires review.");
                candidates.Add(candidate!);
            }
            erasedStubs = 0; erasedConnections = 0;
            if (candidates.Count == 0) return archive;
            var removing = candidates.Select(item => item.Source.Id).ToHashSet();
            var saved = SewerOpenEndArchive.Create(prior.Points.Concat(candidates.Select(item => item.Saved)).ToArray(),
                prior.Version == 2 || candidates.Any(item => item.Source.Role == "OpenConnectionEnd"));
            var updated = archive with { Outputs = Array.AsReadOnly(archive.Outputs.Where(item => !removing.Contains(item.Id)).ToArray()), StubArchiveDigest = SewerOpenEndArchive.Digest(saved), ConnectionArchive = saved.Version == 2 };
            SewerPointCleanup.Apply(candidates,
                item => { RequireUnlocked(tr, item.Source.Id); tr.GetObject(item.Source.Id, OpenMode.ForWrite, false); },
                () =>
                {
                    VerifyArchive(db, tr, model.ObjectId, archive);
                    foreach (StubCandidate item in candidates)
                        if (!TryCaptureStub(tr, model.ObjectId, archive, item.Source, out StubCandidate? current, out string reason) ||
                            current!.Metadata != item.Metadata || JsonSerializer.Serialize(current.Saved) != JsonSerializer.Serialize(item.Saved))
                            throw new InvalidOperationException($"Open-end point {item.Source.Id.Handle} changed before cleanup: {reason}");
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
            erasedStubs = candidates.Count(item => item.Source.Role == "OpenNullEnd");
            erasedConnections = candidates.Count(item => item.Source.Role == "OpenConnectionEnd");
            return updated;
        }

        private static bool TryCaptureStub(Transaction tr, ObjectId model, BatchArchive archive, Output source,
            out StubCandidate? candidate, out string reason)
        {
            candidate = null;
            reason = "No unique archive-owned, unchanged Structures POINT.";
            if ((source.Role != "OpenNullEnd" && source.Role != "OpenConnectionEnd") || source.OdState != GisImportCommands.ObjectDataFingerprintState.Present ||
                archive.Outputs.Count(item => SameName(item.Name, source.Name) &&
                    (item.Role == "OpenNullEnd" || item.Role == "OpenConnectionEnd" || item.Role == "RetainedSourcePoint")) != 1 ||
                archive.Structures.Any(item => SameName(item.Name, source.Name)) ||
                tr.GetObject(source.Id, OpenMode.ForRead, false) is not DBPoint point || point.GetType() != typeof(DBPoint) ||
                point.OwnerId != model || point.IsErased || !SameName(point.Layer, "Structures") ||
                Shape(tr, point) != source.Shape || !Finite(point.Position)) return false;
            if (!GisImportCommands.TryReadSewerStructuresIdentity(source.Id, out string name, out string part, out string detail))
                throw new InvalidOperationException("Open-end identity read failed: " + detail);
            string kind = source.Role == "OpenConnectionEnd" ? "Connection" : "NullStub";
            reason = "Identity does not match the exact archived open-end role.";
            bool exactIdentity = kind == "Connection" ? SewerPreparationRules.IsExplicitSewerConnectionName(name) :
                !SewerPreparationRules.IsExplicitSewerConnectionName(name) && StormStructureMatching.Classify(name, part) == StormStructureRole.NullPipeEnd;
            if (!SameName(name, source.Name) || !exactIdentity) return false;
            var metadata = ReadCleanupMetadata(tr, point);
            reason = "Additional or unsupported point metadata is preserved. " + DescribeCleanupMetadata(metadata);
            if (SewerPointCleanup.Classify(metadata) == SewerPointMetadataKind.Review) return false;
            RequireOd(source.Id, source.OdState, source.OdFingerprint);
            StubPipeLink[] links = CaptureStubPipeLinks(tr, archive, name, point.Position, kind);
            reason = "No verified owned pipe endpoint names this open-end point.";
            if (!SewerPointCleanup.CanArchiveOpenEnd(source.Role, kind, true, true, links.Length, metadata)) return false;
            string payload = GisImportCommands.CaptureNativeObjectDataArchive(source.Id, source.OdFingerprint);
            var saved = new StubArchiveEntry(source.Id.Handle.ToString(), name, part, point.Position.X, point.Position.Y,
                point.Position.Z, source.Shape, source.OdFingerprint, payload, links);
            var signature = new StringBuilder();
            AppendOutputDictionaryMetadata(tr, point.ExtensionDictionary, signature, 0);
            candidate = new StubCandidate(source, new OpenEndArchiveEntry(kind, source.Role, saved), signature.ToString());
            reason = "";
            return true;
        }

        private static StubPipeLink[] CaptureStubPipeLinks(Transaction tr, BatchArchive archive, string name, Point3d anchor, string kind)
        {
            var result = new List<StubPipeLink>();
            foreach (var original in archive.OriginalPipes)
            {
                Output[] owners = archive.Outputs.Where(item => item.Id == original.Id &&
                    (item.Role == "SinglePipe" || item.Role == "PipeCenter") && SameName(item.Name, original.Name)).ToArray();
                if (owners.Length != 1 || tr.GetObject(original.Id, OpenMode.ForRead, false) is not Line line || line.GetType() != typeof(Line))
                    throw new InvalidOperationException("Open-end pipe ownership is missing or ambiguous.");
                if (!GisImportCommands.TryReadSewerPipeData(original.Id, out var data, out string detail, allowBlankEndpoints: true) || data == null)
                    throw new InvalidOperationException("Open-end pipe native OD read failed: " + detail);
                bool first = SameName(data.StructureStart, name), last = SameName(data.StructureEnd, name);
                if (!first && !last) continue;
                if (first && last || !SameName(data.Name, original.Name))
                    throw new InvalidOperationException("Open-end pipe endpoint identity is ambiguous.");
                // The original accepted null-end contract is retained. No snapping,
                // shortening, extending or wall/centerline edits occur in cleanup.
                bool atStart = NearOpenEnd(line.StartPoint, anchor, kind), atEnd = NearOpenEnd(line.EndPoint, anchor, kind);
                if (atStart == atEnd) throw new InvalidOperationException("Open-end point does not resolve to exactly one unchanged pipe terminal.");
                Point3d terminal = atStart ? line.StartPoint : line.EndPoint;
                result.Add(new StubPipeLink(original.Id.Handle.ToString(), original.Name, atStart, terminal.X, terminal.Y, terminal.Z));
            }
            return result.OrderBy(item => item.Handle, StringComparer.Ordinal).ToArray();
        }

        private static bool NearOpenEnd(Point3d end, Point3d anchor, string kind)
            => kind == "Connection"
                ? SewerPreparationRules.ConnectionAnchorMatches(end.X, end.Y, end.Z, anchor.X, anchor.Y, anchor.Z)
                : kind == "NullStub" && Math.Sqrt(Math.Pow(end.X - anchor.X, 2) + Math.Pow(end.Y - anchor.Y, 2)) <= 0.10 && Math.Abs(end.Z - anchor.Z) <= Tolerance;
        private static bool SameName(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static OpenEndArchiveState ReadStubArchive(Database db, Transaction tr)
        {
            var root = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!root.Contains(StubArchiveKey)) return SewerOpenEndArchive.Create(Array.Empty<OpenEndArchiveEntry>(), false);
            var record = tr.GetObject(root.GetAt(StubArchiveKey), OpenMode.ForRead) as Xrecord
                ?? throw new InvalidOperationException("Stub archive has an unexpected type.");
            using ResultBuffer? data = record.Data;
            TypedValue[] values = data?.AsArray() ?? throw new InvalidOperationException("Stub archive is empty.");
            if (values.Length < 2 || values[0].TypeCode != (int)DxfCode.Text || (string)values[0].Value != StubArchiveKey ||
                values[1].TypeCode != (int)DxfCode.Int32 || (int)values[1].Value != values.Length - 2 ||
                values.Skip(2).Any(item => item.TypeCode != (int)DxfCode.Text || item.Value is not string))
                throw new InvalidOperationException("Stub archive framing is invalid.");
            string json = string.Concat(values.Skip(2).Select(item => (string)item.Value));
            return SewerOpenEndArchive.Read(json);
        }

        private static void WriteStubArchive(Database db, Transaction tr, OpenEndArchiveState expected, OpenEndArchiveState updated)
        {
            if (expected.Json != ReadStubArchive(db, tr).Json)
                throw new InvalidOperationException("Stub archive changed before write.");
            var root = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
            Xrecord record;
            if (root.Contains(StubArchiveKey)) record = (Xrecord)tr.GetObject(root.GetAt(StubArchiveKey), OpenMode.ForWrite);
            else { record = new Xrecord(); root.SetAt(StubArchiveKey, record); tr.AddNewlyCreatedDBObject(record, true); }
            var values = new List<TypedValue> { Text(StubArchiveKey) };
            AddTextChunks(values, updated.Json);
            using var data = new ResultBuffer(values.ToArray()); record.Data = data;
            if (updated.Json != ReadStubArchive(db, tr).Json)
                throw new InvalidOperationException("Complete stub archive did not read back; erasure is prohibited.");
        }

        private static bool ArchivedIdentity(NativeOdArchiveRecord record, string fieldName, string expected, bool allowMissing = false)
        {
            NativeOdArchiveField[] fields = record.Fields.Where(item => SameName(item.Name, fieldName)).ToArray();
            return SewerPointCleanup.ArchivedIdentityValueMatches(fields.Length,
                fields.Length == 1 ? fields[0].Kind : null,
                fields.Length == 1 && fields[0].Values.Length == 1 ? fields[0].Values[0] : null, expected, allowMissing);
        }

        private static void VerifyStubArchive(Database db, Transaction tr, BatchArchive archive, ISet<ObjectId>? pendingErases)
        {
            OpenEndArchiveState saved = ReadStubArchive(db, tr);
            if (!SewerPointCleanup.ValidStubArchiveLink(archive.StubArchiveDigest, saved.Points.Length, SewerOpenEndArchive.Digest(saved)) || archive.ConnectionArchive != (saved.Version == 2))
                throw new InvalidOperationException("Required complete stub archive is missing, unlinked or changed.");
            var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (OpenEndArchiveEntry entry in saved.Points)
            {
                StubArchiveEntry point = entry.Source;
                bool connection = entry.Kind == "Connection";
                if (point == null || string.IsNullOrWhiteSpace(point.Handle) || !handles.Add(point.Handle) ||
                    string.IsNullOrWhiteSpace(point.Name) || !names.Add(point.Name) || string.IsNullOrEmpty(point.SourceShape) ||
                    point.Pipes == null || point.Pipes.Length == 0 ||
                    !SewerPointCleanup.ValidOpenEndKindRole(entry.Kind, entry.SourceRole) ||
                    (connection ? !SewerPreparationRules.IsExplicitSewerConnectionName(point.Name) :
                        StormStructureMatching.Classify(point.Name, point.Part) != StormStructureRole.NullPipeEnd || SewerPreparationRules.IsExplicitSewerConnectionName(point.Name)) ||
                    NativeOdArchive.Fingerprint(NativeOdArchive.Deserialize(point.NativeOd)) != point.OdFingerprint)
                    throw new InvalidOperationException("Archived open-end point identity or complete typed OD is invalid.");
                NativeOdArchiveRecord[] native = NativeOdArchive.Deserialize(point.NativeOd);
                NativeOdArchiveRecord[] identities = native.Where(item => SameName(item.TableName, "Structures")).ToArray();
                if (identities.Length == 0 || identities.Any(item => !ArchivedIdentity(item, "Name", point.Name) ||
                    !ArchivedIdentity(item, "PartSizeName", point.Part, allowMissing: connection)))
                    throw new InvalidOperationException("Archived open-end point identity differs from its preserved native OD.");
                if (!long.TryParse(point.Handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long handle) || handle <= 0)
                    throw new InvalidOperationException("Archived open-end point source handle is invalid.");
                if (db.TryGetObjectId(new Handle(handle), out ObjectId original) && original.IsValid && !original.IsErased &&
                    (pendingErases == null || !pendingErases.Contains(original)))
                    throw new InvalidOperationException("An archived open-end point source is still live or was restored; review is required.");
                Point3d anchor = new Point3d(point.X, point.Y, point.Z);
                if (!Finite(anchor) || archive.Outputs.Any(item => SameName(item.Id.Handle.ToString(), point.Handle)) ||
                    archive.Structures.Any(item => SameName(item.Name, point.Name)) ||
                    JsonSerializer.Serialize(CaptureStubPipeLinks(tr, archive, point.Name, anchor, entry.Kind)) != JsonSerializer.Serialize(point.Pipes))
                    throw new InvalidOperationException("Archived open-end point provenance or owned pipe terminals differ.");
            }
        }
    }
}
