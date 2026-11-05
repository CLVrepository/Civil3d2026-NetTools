using System.Text.Json;
using CLV_CivilTools.Ufls.SewerInfo;

static void Assert(bool ok, string name) { if (!ok) throw new Exception(name); }
static void Reject(Action action, string name)
{
    try { action(); } catch (ArgumentException) { return; } catch (JsonException) { return; }
    throw new Exception("Expected rejection: " + name);
}
static SewerCatalog Sample() => new()
{
    ProjectNumber = "L00-00001",
    Extra = new() { ["verticalDatum"] = JsonSerializer.SerializeToElement(new { name = "NAVD88" }) },
    Manholes = new()
    {
        new SewerManhole
        {
            Number = 1, Name = "L00-00001-SSMH-01", RimElevation = 10, StructureDiameterInches = 48,
            StructureDiameterBasis = "base_as_labeled",
            Extra = new() { ["sourceReferences"] = JsonSerializer.SerializeToElement(new[] { new { sheet = "S1", location = "profile" } }) },
            Connections = new()
            {
                new SewerConnection { ConnectionId = "a", Flow = "in", Direction = "N", InvertElevation = 0, DiameterInches = 8, MaterialCode = "C900", PipeSpecification = "C900", MaterialSourceLabel = "C-900" },
                new SewerConnection { ConnectionId = "b", Flow = "in", Direction = "W", InvertElevation = -0.2, DiameterInches = 10 },
                new SewerConnection { ConnectionId = "c", Flow = "out", Direction = "S", InvertElevation = -0.4, DiameterInches = 15, MaterialCode = "SDR35", PipeSpecification = "SDR35" }
            }
        },
        new SewerManhole { Number = 12, Name = "L00-00001-SSMH-12", RimElevation = null, StructureDiameterInches = null }
    }
};
SewerCatalog c = Sample(); SewerInfoModel.Validate(c);
SewerCatalog round = SewerInfoModel.Parse(SewerInfoModel.Serialize(c));
Assert(round.Manholes[0].Connections.Count == 3, "multiple incoming retained");
Assert(round.Manholes[0].Connections[0].InvertElevation == 0 && round.Manholes[0].Connections[1].InvertElevation == -0.2, "zero and negative measured elevations retained");
Assert(round.Manholes[1].RimElevation == null && round.Manholes[1].StructureDiameterInches == null, "unknown not default zero");
Assert(round.Manholes[0].StructureDiameterInches == 48 && round.Manholes[0].Connections[0].DiameterInches == 8, "structure and pipe sizes separate");
Assert(round.Manholes[0].Connections[2].MaterialCode == "SDR35" && round.Manholes[0].Connections[2].Material == null, "specification does not invent material family");
Assert(round.Manholes[0].Extra!["sourceReferences"][0].GetProperty("sheet").GetString() == "S1", "source provenance round trip");
Assert(SewerInfoModel.Name(c.ProjectNumber, 100) == "L00-00001-SSMH-100", "minimum two digits without truncation");
Assert(SewerInfoModel.Code(" c-900 ") == "C900" && SewerInfoModel.Code("SDR-35") == "SDR35", "standard code normalization");
Reject(() => SewerInfoModel.Name(c.ProjectNumber, 0), "zero ID");
Reject(() => SewerInfoModel.Name("bad/project", 1), "bad project");
Reject(() => SewerInfoModel.Parse("{\"projectNumber\":\"L00-00001\",\"ProjectNumber\":\"L00-00002\"}"), "duplicate JSON case keys");
Reject(() => { var x = Sample(); x.Manholes.Add(SewerInfoModel.Copy(x.Manholes[0])); SewerInfoModel.Validate(x); }, "duplicate number");
Reject(() => { var x = Sample(); x.Manholes[0].Name = "L00-00001-SSMH-1"; SewerInfoModel.Validate(x); }, "missing zero pad");
Reject(() => { var x = Sample(); x.Manholes[0].StructureDiameterInches = 0; SewerInfoModel.Validate(x); }, "zero structure diameter");
Reject(() => { var x = Sample(); x.Manholes[0].Connections[0].DiameterInches = -8; SewerInfoModel.Validate(x); }, "negative pipe diameter");
Reject(() => { var x = Sample(); x.Manholes[0].RimElevation = double.NaN; SewerInfoModel.Validate(x); }, "nonfinite rim");
Reject(() => { var x = Sample(); x.Manholes[0].Connections[0].Flow = "unknown"; SewerInfoModel.Validate(x); }, "bad flow");
Reject(() => { var x = Sample(); x.Manholes[0].Connections[0].InvertElevation = 10; SewerInfoModel.Validate(x); }, "invert at rim");
Reject(() => { var x = Sample(); x.Units.Elevations = "meters"; SewerInfoModel.Validate(x); }, "units mismatch");
Reject(() => { var x = Sample(); x.System = "storm"; SewerInfoModel.Validate(x); }, "storm excluded");
Reject(() => { var x = Sample(); x.SchemaVersion = "future"; SewerInfoModel.Validate(x); }, "unknown schema");
foreach (string version in new[] { "clv.sewer-manhole-catalog/0.1-draft", "clv.sewer-manhole-catalog/0.2-draft" })
{ var x = Sample(); x.SchemaVersion = version; Assert(SewerInfoModel.Parse(SewerInfoModel.Serialize(x)).Manholes.Count == 2, "draft compatibility"); }
SewerCatalog imported = Sample(); imported.Manholes.RemoveAt(1); imported.Manholes[0].RimElevation = 11;
string before = SewerInfoModel.Serialize(c);
SewerCatalog merged = SewerInfoModel.Merge(c, imported);
Assert(merged.Manholes.Count == 2 && merged.Manholes.Single(m => m.Number == 1).RimElevation == 11, "merge replaces matching and retains manual absent");
Assert(SewerInfoModel.Serialize(c) == before, "merge does not mutate existing or placed snapshot");
imported.Manholes[0].RimElevation = 12;
Assert(merged.Manholes.Single(m => m.Number == 1).RimElevation == 11, "merge deep copy");
Reject(() => { var x = Sample(); x.Extra!["verticalDatum"] = JsonSerializer.SerializeToElement(new { name = "NGVD29" }); SewerInfoModel.Merge(c, x); }, "datum mismatch");
Reject(() => { var x = Sample(); x.ProjectNumber = "L00-00002"; foreach (var m in x.Manholes) m.Name = SewerInfoModel.Name(x.ProjectNumber, m.Number); SewerInfoModel.Merge(c, x); }, "project mismatch");
var attrs = SewerInfoModel.Attributes(c, c.Manholes[0]);
Assert(attrs.Count == 7 && attrs["STRUCT_SIZE"].Contains("48 in") && attrs["CONN_03"].Contains("15 in"), "block rows and separate size labels");
Assert(SewerInfoModel.Attributes(c, c.Manholes[1])["RIM"].Contains("UNKNOWN"), "unknown block label");
Assert(attrs["STRUCT_SIZE"].Contains("BASE") && !attrs["STRUCT_SIZE"].Contains("base_as_labeled"), "human readable size basis");
Assert(SewerInfoModel.PipeLabel(new SewerConnection { Material = "PVC", PipeSpecification = "SDR35" }).Contains("PVC / SDR35"), "physical material and specification both displayed");
Console.WriteLine("PASS: sewer catalog naming, schema, provenance, dimensions/materials, unknowns, validation, merge and block labels.");
foreach (string path in args)
{
    SewerCatalog input = SewerInfoModel.Parse(File.ReadAllText(path));
    SewerCatalog reread = SewerInfoModel.Parse(SewerInfoModel.Serialize(input));
    Assert(reread.Manholes.Count == input.Manholes.Count, "supplied catalog round trip");
    Console.WriteLine($"PASS supplied catalog: {input.Manholes.Count} records, {input.Manholes.Sum(m => m.Connections.Count)} connections.");
}
