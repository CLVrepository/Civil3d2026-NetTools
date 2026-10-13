using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace CLV_CivilTools.Gis
{
    internal sealed record NativeOdArchiveField(string Name, string Kind, string[] Values);
    internal sealed record NativeOdArchiveRecord(string TableName, NativeOdArchiveField[] Fields);

    /// <summary>Versioned lossless typed OD snapshots. Parsing is fail-closed; snapshots do not authorize cleanup.</summary>
    internal static class NativeOdArchive
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static string Serialize(NativeOdArchiveRecord[] records)
        {
            Validate(records);
            return JsonSerializer.Serialize(new
            {
                version = 1,
                records = records.Select(record => new
                {
                    tableName = record.TableName,
                    fields = record.Fields.Select(field => new { name = field.Name, kind = field.Kind, values = field.Values })
                })
            });
        }

        public static NativeOdArchiveRecord[] Deserialize(string payload)
        {
            ArgumentNullException.ThrowIfNull(payload);
            CheckText(payload);
            using JsonDocument document = JsonDocument.Parse(payload, new JsonDocumentOptions
            {
                AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 32
            });
            JsonElement root = document.RootElement;
            ObjectShape(root, "version", "records");
            JsonElement version = root.GetProperty("version");
            if (version.ValueKind != JsonValueKind.Number || version.GetRawText() != "1")
                throw new FormatException("Unsupported OD archive version.");
            JsonElement entries = root.GetProperty("records");
            ArrayShape(entries);
            NativeOdArchiveRecord[] records = entries.EnumerateArray().Select(record =>
            {
                ObjectShape(record, "tableName", "fields");
                JsonElement fields = record.GetProperty("fields");
                ArrayShape(fields);
                return new NativeOdArchiveRecord(Text(record.GetProperty("tableName")), fields.EnumerateArray().Select(field =>
                {
                    ObjectShape(field, "name", "kind", "values");
                    JsonElement values = field.GetProperty("values");
                    ArrayShape(values);
                    return new NativeOdArchiveField(Text(field.GetProperty("name")), Text(field.GetProperty("kind")),
                        values.EnumerateArray().Select(Text).ToArray());
                }).ToArray());
            }).ToArray();
            Validate(records);
            return records;
        }

        public static string Fingerprint(NativeOdArchiveRecord[] records)
        {
            Validate(records);
            return StormObjectDataFingerprint.Compute(records.Select(record => Token(record.TableName.ToUpperInvariant()) +
                string.Concat(record.Fields.Select(field => Token(Token(field.Name.ToUpperInvariant()) + Token(field.Kind) +
                    Token(field.Kind == "Point" ? string.Join(",", field.Values) : field.Values[0]))))));
        }

        private static void Validate(NativeOdArchiveRecord[] records)
        {
            ArgumentNullException.ThrowIfNull(records);
            if (records.Length == 0) throw new FormatException("An OD archive must contain at least one record.");
            foreach (NativeOdArchiveRecord record in records)
            {
                if (record == null || record.Fields == null) throw new FormatException("Null OD record or fields.");
                CheckText(record.TableName);
                foreach (NativeOdArchiveField field in record.Fields)
                {
                    if (field == null || field.Values == null) throw new FormatException("Null OD field or values.");
                    CheckText(field.Name);
                    CheckText(field.Kind);
                    int count = field.Kind switch { "Character" or "Integer" or "Real" => 1, "Point" => 3, _ => throw new FormatException("Unsupported OD field kind.") };
                    if (field.Values.Length != count) throw new FormatException("Invalid OD value count.");
                    foreach (string value in field.Values)
                    {
                        CheckText(value);
                        if (field.Kind == "Integer" && (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int integer) ||
                            value != integer.ToString(CultureInfo.InvariantCulture)))
                            throw new FormatException("OD integer is not canonical Int32 text.");
                        if ((field.Kind == "Real" || field.Kind == "Point") &&
                            (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double real) || !double.IsFinite(real) ||
                             value != real.ToString("R", CultureInfo.InvariantCulture)))
                            throw new FormatException("OD real is not canonical finite double text.");
                    }
                }
            }
        }

        private static string Token(string value) => value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
        private static void CheckText(string value)
        {
            if (value == null) throw new FormatException("Null OD text.");
            _ = StrictUtf8.GetByteCount(value);
        }
        private static string Text(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.String) throw new FormatException("OD text must be a JSON string.");
            string text = value.GetString()!;
            CheckText(text);
            return text;
        }
        private static void ArrayShape(JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Array) throw new FormatException("Expected OD array.");
        }
        private static void ObjectShape(JsonElement value, params string[] expected)
        {
            if (value.ValueKind != JsonValueKind.Object) throw new FormatException("Expected OD object.");
            string[] names = value.EnumerateObject().Select(property => property.Name).ToArray();
            if (names.Length != expected.Length || names.Distinct(StringComparer.Ordinal).Count() != names.Length ||
                names.Any(name => !expected.Contains(name, StringComparer.Ordinal)))
                throw new FormatException("Unknown, duplicate or missing OD property.");
        }
    }
}
