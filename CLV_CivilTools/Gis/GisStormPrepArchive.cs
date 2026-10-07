using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace CLV_CivilTools.Gis
{
    internal sealed record StormPrepArchiveRecord(string SourceHandle, string Name, string PartSizeName,
        StormStructureRole Role, Point3d Anchor, string ObjectDataFingerprint, IReadOnlyList<ObjectId> OutputIds, IReadOnlyList<string> MarkerHandles);

    /// <summary>
    /// Drawing-local ownership after a verified imported point is erased. Unlike an
    /// output extension dictionary, this survives deletion of any output so a rerun
    /// can report missing geometry. It is not exported as cache-entity metadata.
    /// </summary>
    internal static class GisStormPrepArchive
    {
        private const string DictionaryKey = "CLV_STORM_PREP_ARCHIVE_V1";

        internal static IReadOnlyList<StormPrepArchiveRecord> ReadAll(Database db, Transaction tr)
        {
            var root = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!root.Contains(DictionaryKey)) return Array.Empty<StormPrepArchiveRecord>();
            if (tr.GetObject(root.GetAt(DictionaryKey), OpenMode.ForRead) is not DBDictionary archive)
                throw new InvalidOperationException("Storm preparation archive is not a dictionary; cleanup/history requires review.");
            var result = new List<StormPrepArchiveRecord>();
            foreach (DBDictionaryEntry entry in archive)
            {
                if (tr.GetObject(entry.Value, OpenMode.ForRead) is not Xrecord record)
                    throw new InvalidOperationException($"Storm preparation archive {entry.Key} is not an ownership record.");
                StormPrepArchiveRecord item = ReadRecord(record);
                if (!string.Equals(entry.Key, item.SourceHandle, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Storm preparation archive {entry.Key} has a different source identity.");
                result.Add(item);
            }
            return result;
        }

        internal static void WriteAndVerify(Database db, Transaction tr, StormPrepArchiveRecord item)
        {
            var root = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            DBDictionary archive;
            if (root.Contains(DictionaryKey))
                archive = tr.GetObject(root.GetAt(DictionaryKey), OpenMode.ForWrite) as DBDictionary
                    ?? throw new InvalidOperationException("Storm preparation archive is not a dictionary.");
            else
            {
                root.UpgradeOpen();
                archive = new DBDictionary();
                root.SetAt(DictionaryKey, archive);
                tr.AddNewlyCreatedDBObject(archive, true);
            }
            if (archive.Contains(item.SourceHandle))
                throw new InvalidOperationException($"Source {item.SourceHandle} already has archived ownership; retained for review.");
            var record = new Xrecord();
            archive.SetAt(item.SourceHandle, record);
            tr.AddNewlyCreatedDBObject(record, true);
            // The first five values retain the point-owned completion's version,
            // identity and XY layout. Additional metadata belongs to this archive only.
            var values = new List<TypedValue>
            {
                new TypedValue((int)DxfCode.Text, "1"),
                new TypedValue((int)DxfCode.Text, item.Name),
                new TypedValue((int)DxfCode.Text, item.PartSizeName),
                new TypedValue((int)DxfCode.Real, item.Anchor.X),
                new TypedValue((int)DxfCode.Real, item.Anchor.Y),
                new TypedValue((int)DxfCode.Real, item.Anchor.Z),
                new TypedValue((int)DxfCode.Text, item.SourceHandle),
                new TypedValue((int)DxfCode.Text, item.Role.ToString()),
                new TypedValue((int)DxfCode.Text, item.ObjectDataFingerprint),
                new TypedValue((int)DxfCode.Text, string.Join(",", item.MarkerHandles))
            };
            values.AddRange(item.OutputIds.Select(id => new TypedValue((int)DxfCode.SoftPointerId, id)));
            using (var data = new ResultBuffer(values.ToArray())) record.Data = data;
            StormPrepArchiveRecord read = ReadRecord(record);
            if (read.SourceHandle != item.SourceHandle || read.Name != item.Name ||
                read.PartSizeName != item.PartSizeName || read.Role != item.Role ||
                read.Anchor != item.Anchor || read.ObjectDataFingerprint != item.ObjectDataFingerprint ||
                !read.OutputIds.SequenceEqual(item.OutputIds) || !read.MarkerHandles.SequenceEqual(item.MarkerHandles))
                throw new InvalidOperationException($"Source {item.SourceHandle} archive readback failed; cleanup rolled back.");
        }

        private static StormPrepArchiveRecord ReadRecord(Xrecord record)
        {
            using ResultBuffer? buffer = record.Data;
            TypedValue[] values = buffer?.AsArray() ?? Array.Empty<TypedValue>();
            if (values.Length < 11 || Text(0) != "1")
                throw new InvalidOperationException("Storm preparation archive has an unsupported or incomplete record.");
            string name = Text(1), part = Text(2), handle = Text(6), fingerprint = Text(8);
            var anchor = new Point3d(Real(3), Real(4), Real(5));
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(part) ||
                string.IsNullOrWhiteSpace(handle) || !handle.All(Uri.IsHexDigit) ||
                !Enum.TryParse(Text(7), out StormStructureRole role) ||
                role != StormStructureMatching.Classify(name, part) ||
                (role != StormStructureRole.DropInlet && role != StormStructureRole.Access && role != StormStructureRole.JunctionBox) ||
                !StormObjectDataFingerprint.IsValid(fingerprint))
                throw new InvalidOperationException("Storm preparation archive identity or OD fingerprint requires review.");
            string[] markers = Text(9).Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (markers.Any(marker => !marker.All(Uri.IsHexDigit)) || markers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != markers.Length)
                throw new InvalidOperationException($"Archived source {handle} has invalid marker ownership.");
            var outputs = new List<ObjectId>();
            foreach (TypedValue value in values.Skip(10))
            {
                if (value.TypeCode != (int)DxfCode.SoftPointerId || value.Value is not ObjectId id)
                    throw new InvalidOperationException($"Archived source {handle} has an invalid output link.");
                outputs.Add(id); // Keep missing/erased links for the host's explicit review.
            }
            return new StormPrepArchiveRecord(handle, name, part, role, anchor, fingerprint, outputs, markers);

            string Text(int index)
            {
                if (index >= values.Length || values[index].TypeCode != (int)DxfCode.Text || values[index].Value is not string text)
                    throw new InvalidOperationException("Storm preparation archive text field is unreadable.");
                return text;
            }
            double Real(int index)
            {
                if (index >= values.Length || values[index].TypeCode != (int)DxfCode.Real ||
                    values[index].Value is not double number || !double.IsFinite(number))
                    throw new InvalidOperationException("Storm preparation archive coordinate is unreadable.");
                return number;
            }
        }
    }
}
