using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CLV_CivilTools.Gis;

int passed = 0;
void Check(string name, bool condition)
{
    if (!condition) throw new Exception(name);
    passed++;
    Console.WriteLine("PASS " + name);
}
void Reject(string name, Action action)
{
    try { action(); }
    catch (Exception error) when (error is FormatException or JsonException or ArgumentException)
    { Check(name, true); return; }
    throw new Exception("Accepted invalid input: " + name);
}

const string legacyJson = """
{"Version":1,"Points":[{"Handle":"A1","Name":"STUB-1","Part":"STUB","X":1.25,"Y":-2.5,"Z":3,"SourceShape":"POINT","OdFingerprint":"ABCDEF","NativeOd":"opaque-native","Pipes":[{"Handle":"B2","Name":"PIPE-1","AtStart":true,"X":1.25,"Y":-2.5,"Z":3}]}]}
""";
var pipe = new StubPipeLink("B2", "PIPE-1", true, 1.25, -2.5, 3);
var source = new StubArchiveEntry("A1", "STUB-1", "STUB", 1.25, -2.5, 3,
    "POINT", "ABCDEF", "opaque-native", new[] { pipe });
var nullEnd = new OpenEndArchiveEntry("NullStub", "OpenNullEnd", source);
Check("Legacy record property order and JSON bytes unchanged", JsonSerializer.Serialize(new StubArchiveData(1, new[] { source })) == legacyJson);
OpenEndArchiveState legacy = SewerOpenEndArchive.Read(legacyJson);
Check("Legacy maps kind and role", legacy.Version == 1 && legacy.Points.Single().Kind == "NullStub" && legacy.Points[0].SourceRole == "OpenNullEnd");
Check("Read retains exact legacy JSON", legacy.Json == legacyJson);
Check("Legacy digest matches fixed pre-upgrade byte fixture", SewerOpenEndArchive.Digest(legacy) == "EF8D4BE7AD1DA354B26B6C133C8CAFB926D2B1BCB221EDBA64142CEBCD1483E8");
Check("Creating legacy archive preserves its bytes", SewerOpenEndArchive.Create(new[] { nullEnd }, false).Json == legacyJson);

// Opaque typed OD payload: whitespace, Unicode, escaping, zero bytes and line endings survive.
string native = "{\"version\":1,\"records\":[{\"tableName\":\"Sewer Ω\",\"fields\":[{\"Name\":\"text\",\"Values\":[\"a\\b\"]}]}]}\r\n\t\0";
var detailed = source with { NativeOd = native, Pipes = new[] { pipe, pipe with { Handle = "C3", AtStart = false, X = -0.125, Y = 200, Z = 0 } } };
var legacyDetailed = SewerOpenEndArchive.Create(new[] { nullEnd with { Source = detailed } }, false);
var connection = new OpenEndArchiveEntry("Connection", "OpenConnectionEnd", source with { Handle = "D4", Name = "CONN-1", Part = "CONNECTION" });
var upgraded = SewerOpenEndArchive.Create(legacyDetailed.Points.Append(connection).ToArray(), true);
Check("Upgrade writes version 2 and both kinds", upgraded.Version == 2 && upgraded.Points.Length == 2 && upgraded.Points[1].Kind == "Connection" && upgraded.Points[1].SourceRole == "OpenConnectionEnd");
Check("Upgrade retains every legacy source property and pipe", JsonSerializer.Serialize(upgraded.Points[0].Source) == JsonSerializer.Serialize(legacyDetailed.Points[0].Source));
Check("Upgrade retains native OD UTF8 payload bytes", Encoding.UTF8.GetBytes(upgraded.Points[0].Source.NativeOd).SequenceEqual(Encoding.UTF8.GetBytes(native)));
Check("Version 2 JSON uses declared property order", upgraded.Json.StartsWith("{\"Version\":2,\"Points\":[{\"Kind\":\"NullStub\",\"SourceRole\":\"OpenNullEnd\",\"Source\":", StringComparison.Ordinal));
Check("Version 2 canonical read roundtrip", SewerOpenEndArchive.Read(upgraded.Json).Json == upgraded.Json);
Check("Version 2 digest hashes exact JSON", SewerOpenEndArchive.Digest(upgraded) == Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(upgraded.Json))));
Check("Empty legacy archive", SewerOpenEndArchive.Create(Array.Empty<OpenEndArchiveEntry>(), false).Json == "{\"Version\":1,\"Points\":[]}");
Check("Explicit upgrade stays V2 even without connections", SewerOpenEndArchive.Create(legacy.Points, true).Version == 2);
Check("Empty upgraded archive", SewerOpenEndArchive.Create(Array.Empty<OpenEndArchiveEntry>(), true).Version == 2);

foreach (string json in new[]
{
    "", "null", "[]", "{}", "{", "{\"Version\":1}",
    "{\"Version\":3,\"Points\":[]}", "{\"Version\":0,\"Points\":[]}",
    "{\"Version\":\"1\",\"Points\":[]}", "{\"Version\":1.0,\"Points\":[]}",
    "{\"Version\":1,\"Points\":null}", "{\"Version\":2,\"Points\":null}",
    "{\"Version\":1,\"Points\":[null]}", "{\"Version\":2,\"Points\":[null]}",
    legacyJson + " ", " " + legacyJson, legacyJson.Replace("\"Version\":1", "\"Version\":1,\"Version\":1"),
    legacyJson.Replace("\"Version\":1", "\"Version\":1,\"Unknown\":true"),
    legacyJson.Replace("\"Handle\":\"A1\"", "\"Handle\":\"A1\",\"Handle\":\"A1\""),
    legacyJson.Replace("\"Handle\":\"A1\"", "\"Handle\":\"A1\",\"Extra\":0"),
    legacyJson.Replace("\"Handle\":\"B2\"", "\"Handle\":\"B2\",\"Extra\":0"),
    legacyJson.Replace("\"Handle\":\"B2\"", "\"Handle\":\"B2\",\"Handle\":\"B2\""),
    legacyJson.Replace("STUB-1", "STUB\\u002D1"),
    "{\"Points\":[],\"Version\":1}", "{\"Version\":1,\"Points\":[],}",
    upgraded.Json.Replace("\"Version\":2", "\"Version\":2,\"Extra\":0"),
    upgraded.Json.Replace("\"Kind\":\"NullStub\"", "\"Kind\":\"NullStub\",\"Kind\":\"NullStub\""),
    upgraded.Json.Replace("\"Kind\":\"NullStub\"", "\"Kind\":\"NullStub\",\"Extra\":0"),
    upgraded.Json.Replace("\"Kind\":\"NullStub\"", "\"Kind\":\"Connection\""),
    upgraded.Json.Replace("\"SourceRole\":\"OpenConnectionEnd\"", "\"SourceRole\":\"OpenNullEnd\"")
}) Reject("Malformed, unsupported or noncanonical JSON " + passed, () => SewerOpenEndArchive.Read(json));

foreach (var point in new[]
{
    nullEnd with { Kind = "Unknown" }, nullEnd with { Kind = "nullstub" },
    nullEnd with { SourceRole = "Unknown" }, nullEnd with { SourceRole = "OpenConnectionEnd" },
    connection with { SourceRole = "OpenNullEnd" }, nullEnd with { Source = null! },
    nullEnd with { Source = source with { Pipes = null! } },
    nullEnd with { Source = source with { Pipes = new StubPipeLink[] { null! } } },
    null!
})
{
    Reject("Create rejects invalid kind, role, source or pipe " + passed, () => SewerOpenEndArchive.Create(new[] { point }, true));
    Reject("Read V2 rejects invalid kind, role, source or pipe " + passed,
        () => SewerOpenEndArchive.Read(JsonSerializer.Serialize(new OpenEndArchiveData(2, new[] { point }))));
}
foreach (var invalidSource in new[] { null!, source with { Pipes = null! }, source with { Pipes = new StubPipeLink[] { null! } } })
    Reject("Read V1 rejects null source/pipe " + passed,
        () => SewerOpenEndArchive.Read(JsonSerializer.Serialize(new StubArchiveData(1, new[] { invalidSource }))));
Reject("Create rejects null points", () => SewerOpenEndArchive.Create(null!, false));
Reject("Read rejects null JSON", () => SewerOpenEndArchive.Read(null!));
Reject("No connection in version 1", () => SewerOpenEndArchive.Create(new[] { connection }, false));
Reject("No mixed connection in version 1", () => SewerOpenEndArchive.Create(new[] { nullEnd, connection }, false));
Reject("Version 1 does not accept version 2 shape", () => SewerOpenEndArchive.Read(JsonSerializer.Serialize(new OpenEndArchiveData(1, new[] { nullEnd }))));
Reject("Version 2 does not accept version 1 shape", () => SewerOpenEndArchive.Read(JsonSerializer.Serialize(new StubArchiveData(2, new[] { source }))));
Console.WriteLine($"All {passed} open-end archive checks passed.");
