using System.Text.Json;
using CLV_CivilTools.Ufls.SewerInfo;

internal static class IdentifierRegression
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string label) { checks++; if (!value) throw new Exception(label); }
        void Reject(Action action, string label)
        {
            checks++;
            try { action(); } catch (ArgumentException) { return; } catch (JsonException) { return; }
            throw new Exception("Expected identifier rejection: " + label);
        }
        SewerCatalog Catalog(params string[] ids) => new()
        {
            ProjectNumber = "L00-00001",
            Manholes = ids.Select(id => new SewerManhole
            {
                Number = id, Name = SewerInfoModel.Name("L00-00001", id), SourceId = "plan-" + id,
                RimElevation = 100, Connections = new() { new SewerConnection
                    { ConnectionId = "connection-" + id, Flow = "out", InvertElevation = 95,
                        ConnectedManholeName = "L00-00001-SSMH-07B", DiameterInches = 8, MaterialCode = "C900" } }
            }).ToList()
        };
        foreach (var example in new[] { ("6", "6", "06"), ("06", "6", "06"), ("6A", "6A", "06A"),
            (" 06a ", "6A", "06A"), ("12B", "12B", "12B"), ("6ab", "6AB", "06AB"),
            ("a", "A", "A"), ("MH", "MH", "MH"), ("999999Z", "999999Z", "999999Z") })
        {
            SewerManholeNumber value = SewerManholeNumber.Parse(example.Item1);
            Check(value.ToString() == example.Item2, "canonical identifier " + example.Item1);
            Check(SewerInfoModel.Name("L00-00001", value) == "L00-00001-SSMH-" + example.Item3, "padded name " + example.Item1);
            Check(SewerInfoModel.Copy(value) == value, "identifier JSON round trip " + example.Item1);
        }
        foreach (string? bad in new string?[] { null, "", " ", "0", "00A", "-1", "+1", "1.5", "1e2",
            "6 A", "6-A", "6/A", "6_A", "A6", "1000000", "1000000B", "999999999999999999999999999999", "6ABCDEFGHI", "é", new string('0', 65) + "1" })
            Reject(() => SewerManholeNumber.Parse(bad), "invalid text " + bad);
        Check((SewerManholeNumber)"06a" == (SewerManholeNumber)"6A", "normalized equality");
        Check(new HashSet<SewerManholeNumber> { "06a", "6A", "6a" }.Count == 1, "normalized hash");
        Check((SewerManholeNumber)"6A" != (SewerManholeNumber)"6B" && (SewerManholeNumber)"6A" != 6, "distinct suffix identity");
        Check(new SewerManholeNumber[] { "10A", "6B", "10", "6", "6A", "2", "A" }.OrderBy(x => x).Select(x => x.ToString())
            .SequenceEqual(new[] { "2", "6", "6A", "6B", "10", "10A", "A" }), "natural order preserves numeric behavior");
        Check(System.Collections.Comparer.Default.Compare((SewerManholeNumber)"6A", (SewerManholeNumber)"10") < 0, "boxed grid numeric sort");
        Reject(() => SewerInfoModel.Name("L00-00001", default(SewerManholeNumber)), "default identifier name");
        Reject(() => SewerInfoModel.Serialize(default(SewerManholeNumber)), "default identifier serialization");
        Reject(() => { var x = Catalog("6"); x.Manholes[0].Number = default; SewerInfoModel.Validate(x); }, "default identifier persistence");
        Check(SewerInfoModel.Serialize((SewerManholeNumber)6) == "6", "legacy integer write");
        Check(SewerInfoModel.Serialize((SewerManholeNumber)"06") == "6", "numeric string emits old JSON number");
        Check(SewerInfoModel.Serialize((SewerManholeNumber)"6a") == "\"6A\"", "alphanumeric JSON string write");
        foreach (string token in new[] { "null", "true", "{}", "[]", "0", "-6", "6.5", "6.0", "6e0", "2147483648", "\"0A\"" })
            Reject(() => JsonSerializer.Deserialize<SewerManholeNumber>(token, SewerInfoModel.Json), "invalid JSON " + token);
        SewerCatalog mixed = Catalog("6", "6A", "6B", "10", "A");
        string json = SewerInfoModel.Serialize(mixed);
        SewerCatalog reread = SewerInfoModel.Parse(json);
        Check(SewerInfoModel.Serialize(reread) == json, "mixed catalog export/reimport and stored snapshot round trip");
        Check(reread.Manholes[1].Name == "L00-00001-SSMH-06A", "letter suffix survives catalog snapshot");
        Check(SewerInfoModel.Attributes(reread, reread.Manholes[1])["MH_NAME"] == "L00-00001-SSMH-06A", "block attribute letter suffix");
        Check(reread.Manholes[1].Connections[0].ConnectedManholeName == "L00-00001-SSMH-07B", "connected reference preserved");
        Check(reread.Manholes.Single(m => m.Number == (SewerManholeNumber)" 06a ").Name.EndsWith("-06A"), "command lookup canonical form");
        Check(reread.Manholes[0].Number == 6 && reread.Manholes[3].Number == 10, "legacy numeric JSON import");
        Reject(() => SewerInfoModel.Parse(json.Replace("\"number\": 6,", "\"missingNumber\": 6,")), "missing identifier");
        Reject(() => SewerInfoModel.Validate(Catalog("6A", "06a")), "canonical duplicate IDs");
        Reject(() => { var x = Catalog("6A"); x.Manholes[0].Name = "L00-00001-SSMH-6A"; SewerInfoModel.Validate(x); }, "un-padded alphanumeric name");
        Reject(() => { var x = Catalog("6A"); x.Manholes[0].Type = "SDMH"; SewerInfoModel.Validate(x); }, "storm scope remains excluded");
        var incoming = Catalog("06a"); incoming.Manholes[0].RimElevation = 101;
        var merged = SewerInfoModel.Merge(mixed, incoming);
        Check(merged.Manholes.Count == 5 && merged.Manholes.Single(m => m.Number == (SewerManholeNumber)"6A").RimElevation == 101, "merge normalized same ID");
        Check(merged.Manholes.Single(m => m.Number == 6).RimElevation == 100 && merged.Manholes.Single(m => m.Number == (SewerManholeNumber)"6B").RimElevation == 100, "merge preserves numeric neighbor and other suffix");
        Check(SewerInfoModel.Serialize(mixed) == json, "merge leaves source and placed snapshots unchanged");
        Console.WriteLine($"PASS: {checks} alphanumeric identifier checks (input, naming, ordering, duplicate identity, legacy JSON, snapshots, attributes and merge).");
    }
}
