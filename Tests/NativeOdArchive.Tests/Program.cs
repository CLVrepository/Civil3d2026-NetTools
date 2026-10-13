using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CLV_CivilTools.Gis;

int checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new Exception(message);
}
void Reject(Action action, string message)
{
    checks++;
    try { action(); }
    catch (Exception ex) when (ex is FormatException or ArgumentException or System.Text.Json.JsonException or InvalidOperationException) { return; }
    throw new Exception("Accepted invalid input: " + message);
}
NativeOdArchiveField Field(string name, string kind, params string[] values) => new(name, kind, values);
NativeOdArchiveRecord Record(params NativeOdArchiveField[] fields) => new("TaBlE:é😀", fields);
string R(double value) => value.ToString("R", CultureInfo.InvariantCulture);
NativeOdArchiveRecord first = Record(
    Field("cHaR", "Character", "  \t\nΩ😀:|,\"\\\0  "),
    Field("blank", "Character", ""), Field("spaces", "Character", "   "),
    Field("Integer", "Integer", int.MinValue.ToString(CultureInfo.InvariantCulture)),
    Field("real", "Real", R(1.2345678901234567)),
    Field("point", "Point", R(-0.0), R(double.Epsilon), R(double.MaxValue)));
NativeOdArchiveRecord second = new("another", new[] { Field("", "Integer", "2147483647") });
NativeOdArchiveRecord[] original = { first, second, first };
string json = NativeOdArchive.Serialize(original);
NativeOdArchiveRecord[] roundtrip = NativeOdArchive.Deserialize(json);
Check(roundtrip.Length == original.Length, "Record multiplicity");
for (int i = 0; i < original.Length; i++)
{
    Check(roundtrip[i].TableName == original[i].TableName, "Table spelling/order");
    Check(roundtrip[i].Fields.Length == original[i].Fields.Length, "Field count");
    for (int j = 0; j < original[i].Fields.Length; j++)
    {
        NativeOdArchiveField a = original[i].Fields[j], b = roundtrip[i].Fields[j];
        Check(a.Name == b.Name && a.Kind == b.Kind && a.Values.SequenceEqual(b.Values, StringComparer.Ordinal), "Exact typed field spelling/value/order");
    }
}
Check(json == NativeOdArchive.Serialize(roundtrip), "Stable exact JSON roundtrip");
// Independent native-key implementation: typed values formatted as GisImport does,
// rather than reusing the archive's string-based fingerprint or key helper.
string Token(string value) => value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
string NativeField(string name, string kind, object value)
{
    string text = value is double[] point ? string.Join(",", point.Select(R)) :
        value is double real ? R(real) : Convert.ToString(value, CultureInfo.InvariantCulture)!;
    return Token(Token(name.ToUpperInvariant()) + Token(kind) + Token(text));
}
string key1 = Token("TaBlE:é😀".ToUpperInvariant()) +
    NativeField("cHaR", "Character", "  \t\nΩ😀:|,\"\\\0  ") + NativeField("blank", "Character", "") +
    NativeField("spaces", "Character", "   ") + NativeField("Integer", "Integer", int.MinValue) +
    NativeField("real", "Real", 1.2345678901234567) + NativeField("point", "Point", new[] { -0.0, double.Epsilon, double.MaxValue });
string key2 = Token("ANOTHER") + NativeField("", "Integer", int.MaxValue);
string[] keys = { key1, key2, key1 };
Array.Sort(keys, StringComparer.Ordinal);
const string prefix = "CLV_OD_V1:SHA256:";
string expected = prefix + Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(prefix + "3:" + string.Concat(keys.Select(Token)))));
Check(NativeOdArchive.Fingerprint(roundtrip) == expected, "Independent native fingerprint");
Check(NativeOdArchive.Fingerprint(original.Reverse().ToArray()) == expected, "Record-order independent fingerprint");
Check(NativeOdArchive.Fingerprint(new[] { first, second }) != expected, "Duplicate multiplicity affects fingerprint");
Check(NativeOdArchive.Fingerprint(new[] { first with { Fields = first.Fields.Reverse().ToArray() }, second, first }) != expected, "Field order affects fingerprint");
NativeOdArchiveRecord Uppercase(NativeOdArchiveRecord record) => new(record.TableName.ToUpperInvariant(),
    record.Fields.Select(field => field with { Name = field.Name.ToUpperInvariant() }).ToArray());
Check(NativeOdArchive.Fingerprint(original.Select(Uppercase).ToArray()) == expected, "Names case insensitive in fingerprint only");
CultureInfo previous = CultureInfo.CurrentCulture;
try
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
    Check(NativeOdArchive.Fingerprint(roundtrip) == expected, "Culture-invariant fingerprint");
    Check(NativeOdArchive.Serialize(roundtrip) == json, "Culture-invariant snapshot");
}
finally { CultureInfo.CurrentCulture = previous; }

var invalidRecords = new NativeOdArchiveRecord[][]
{
    null!, Array.Empty<NativeOdArchiveRecord>(), new NativeOdArchiveRecord[] { null! },
    new[] { new NativeOdArchiveRecord(null!, Array.Empty<NativeOdArchiveField>()) },
    new[] { new NativeOdArchiveRecord("table", null!) }, new[] { Record(new NativeOdArchiveField[] { null! }) },
    new[] { Record(Field(null!, "Character", "x")) }, new[] { Record(Field("x", null!, "x")) },
    new[] { Record(new NativeOdArchiveField("x", "Character", null!)) },
    new[] { Record(Field("x", "Character", new string[] { null! })) },
    new[] { Record(Field("x", "Character", "\ud800")) }
};
foreach (NativeOdArchiveRecord[] bad in invalidRecords)
{
    Reject(() => NativeOdArchive.Serialize(bad), "Invalid graph serialization");
    Reject(() => NativeOdArchive.Fingerprint(bad), "Invalid graph fingerprint");
}
foreach (NativeOdArchiveField bad in new[]
{
    Field("x", "Unknown", "x"), Field("x", "character", "x"), Field("x", "Character"),
    Field("x", "Character", "a", "b"), Field("x", "Point", "1", "2"), Field("x", "Point", "1", "2", "3", "4"),
    Field("x", "Integer", "2147483648"), Field("x", "Integer", "01"), Field("x", "Integer", "+1"),
    Field("x", "Integer", "-0"), Field("x", "Integer", " 1"), Field("x", "Real", "1.0"),
    Field("x", "Real", "1,5"), Field("x", "Real", "1e0"), Field("x", "Real", "NaN"),
    Field("x", "Real", "Infinity"), Field("x", "Real", "-Infinity"), Field("x", "Real", "1E+999"),
    Field("x", "Point", "1", "NaN", "3")
})
{
    Reject(() => NativeOdArchive.Serialize(new[] { Record(bad) }), "Malformed typed value serialization");
    Reject(() => NativeOdArchive.Fingerprint(new[] { Record(bad) }), "Malformed typed value fingerprint");
    string invalidJson = System.Text.Json.JsonSerializer.Serialize(new
    {
        version = 1,
        records = new[] { new { tableName = "t", fields = new[] { new { name = bad.Name, kind = bad.Kind, values = bad.Values } } } }
    });
    Reject(() => NativeOdArchive.Deserialize(invalidJson), "Malformed typed value deserialization");
}
foreach (string bad in new[]
{
    "", "null", "[]", "{}", "{\"version\":2,\"records\":[]}", "{\"version\":1,\"records\":[]}",
    json + "null", json + " x", json[..^1] + ",}", json.Replace("\"version\":1", "\"version\":1,\"version\":1"),
    json.Replace("\"version\":1", "\"version\":1,\"extra\":true"),
    json.Replace("\"version\":1", "\"version\":1.0"), json.Replace("\"version\":1", "\"Version\":1"),
    json.Replace("\"kind\":\"Character\"", "\"kind\":\"Unknown\""),
    json.Replace("\"kind\":\"Character\"", "\"kind\":null"),
    json.Replace("\"kind\":\"Character\"", "\"kind\":\"Character\",\"kind\":\"Character\""),
    json.Replace("\"fields\":", "\"unknown\":"),
    "{\"version\":1,\"records\":[{\"tableName\":\"t\",\"fields\":[{\"name\":\"n\",\"kind\":\"Real\",\"values\":[\"NaN\"]}]}]}",
    "{\"version\":1,\"records\":[{\"tableName\":\"t\",\"fields\":[{\"name\":\"n\",\"kind\":\"Integer\",\"values\":[1]}]}]}"
}) Reject(() => NativeOdArchive.Deserialize(bad), "Malformed JSON/schema");
Reject(() => NativeOdArchive.Deserialize(null!), "Null payload");
Console.WriteLine($"PASS: {checks} NativeOdArchive checks.");
