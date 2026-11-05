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
var connections = Sample().Manholes[0].Connections;
var partial = SewerInfoModel.PlanAssignments(connections, new int?[] { 2 });
Assert(partial.Assignments.Length == 1 && partial.Assignments[0].ConnectionIndex == 2 && partial.PendingConnections.SequenceEqual(new[] { 0, 1 }), "one existing outgoing pipe applies despite missing incoming branches");
var missingStub = SewerInfoModel.PlanAssignments(new[] { connections[0], connections[2] }, new int?[] { 1 });
Assert(missingStub.Assignments.Length == 1 && missingStub.PendingConnections.SequenceEqual(new[] { 0 }), "reported two-plan/one-native case applies outgoing and leaves incoming stub pending");
var emptyNetwork = SewerInfoModel.PlanAssignments(connections, Array.Empty<int?>());
Assert(emptyNetwork.Assignments.Length == 0 && emptyNetwork.PendingConnections.Length == 3, "structure without pipes can retain all plan connections pending");
var leaveExtra = SewerInfoModel.PlanAssignments(connections, new int?[] { 0, 1, 2, -1 });
Assert(leaveExtra.Assignments.Length == 3 && leaveExtra.PendingConnections.Length == 0 && leaveExtra.UnchangedPipes.SequenceEqual(new[] { 3 }), "explicitly unchanged extra native pipe");
Reject(() => SewerInfoModel.PlanAssignments(connections, new int?[] { null }), "mapping must be explicit");
Reject(() => SewerInfoModel.PlanAssignments(connections, new int?[] { 2, 2 }), "duplicate plan connection mapping");
Reject(() => SewerInfoModel.PlanAssignments(connections, new int?[] { 3 }), "out of range mapping");
Reject(() => SewerInfoModel.PlanAssignments(connections, new int?[] { -2 }), "invalid negative mapping");
var missingInvert = new[] { new SewerConnection { ConnectionId = "unknown", InvertElevation = null } };
Reject(() => SewerInfoModel.PlanAssignments(missingInvert, new int?[] { 0 }), "unknown invert cannot be assigned");
Assert(SewerInfoModel.PlanAssignments(missingInvert, new int?[] { -1 }).PendingConnections.Length == 1, "unknown invert may remain pending without blocking name/rim");
Assert(SewerInfoModel.PlanAssignments(Array.Empty<SewerConnection>(), new int?[] { -1 }).UnchangedPipes.Length == 1, "no plan connections still permits explicit unchanged native pipe");
var autoMh1Connections = new[]
{
    new SewerConnection { ConnectionId = "in", Flow = "in", Direction = "S", InvertElevation = 100.2 },
    new SewerConnection { ConnectionId = "out", Flow = "out", Direction = "N", InvertElevation = 100 }
};
var autoMh1 = SewerInfoModel.MatchAutomatically(autoMh1Connections, new[] { new SewerInfoModel.NativeConnection(true, 0, 20) });
Assert(autoMh1.Plan.Assignments.Single().ConnectionIndex == 1 && autoMh1.Plan.PendingConnections.SequenceEqual(new[] { 0 }), "automatic MH1 missing incoming stub, existing outgoing START north");
var autoTwo = SewerInfoModel.MatchAutomatically(autoMh1Connections, new[] { new SewerInfoModel.NativeConnection(false, 0, -20), new SewerInfoModel.NativeConnection(true, 0, 20) });
Assert(autoTwo.Plan.Assignments.Select(x => x.ConnectionIndex).SequenceEqual(new[] { 0, 1 }), "END incoming and START outgoing without assignment UI");
var branches = new[]
{
    new SewerConnection { ConnectionId = "nw", Flow = "in", Direction = "NW", InvertElevation = 101 },
    new SewerConnection { ConnectionId = "sw", Flow = "in", Direction = "South-West", InvertElevation = 102 },
    new SewerConnection { ConnectionId = "out", Flow = "out", Direction = "N.E.", InvertElevation = 100 }
};
var autoBranches = SewerInfoModel.MatchAutomatically(branches, new[]
{ new SewerInfoModel.NativeConnection(false, -10, 10), new SewerInfoModel.NativeConnection(false, -10, -10), new SewerInfoModel.NativeConnection(true, 10, 10) });
Assert(autoBranches.Plan.Assignments.Select(x => x.ConnectionIndex).SequenceEqual(new[] { 0, 1, 2 }), "multiple incoming branches and direction aliases");
var uniqueRole = SewerInfoModel.MatchAutomatically(new[] { new SewerConnection { Flow = "out", InvertElevation = 0 } }, new[] { new SewerInfoModel.NativeConnection(true, 9, 2) });
Assert(uniqueRole.Plan.Assignments.Length == 1, "unique role with missing plan direction and zero invert");
var conflictDirection = SewerInfoModel.MatchAutomatically(autoMh1Connections, new[] { new SewerInfoModel.NativeConnection(true, 0, -10) });
Assert(conflictDirection.Plan.Assignments.Length == 0 && conflictDirection.Plan.UnchangedPipes.Length == 1, "known opposing direction does not guess");
var duplicateNative = SewerInfoModel.MatchAutomatically(autoMh1Connections, new[] { new SewerInfoModel.NativeConnection(true, 0, 10), new SewerInfoModel.NativeConnection(true, 1, 10) });
Assert(duplicateNative.Plan.Assignments.Length == 0 && duplicateNative.Plan.UnchangedPipes.Length == 2, "two native pipes claiming one plan record remain unresolved");
var duplicatePlans = SewerInfoModel.MatchAutomatically(new[] { branches[0], SewerInfoModel.Copy(branches[0]) }, new[] { new SewerInfoModel.NativeConnection(false, -10, 10) });
Assert(duplicatePlans.Plan.Assignments.Length == 0, "two plan records with same role/direction remain unresolved");
var unknownAutoInvert = SewerInfoModel.MatchAutomatically(new[] { new SewerConnection { Flow = "out", Direction = "N" } }, new[] { new SewerInfoModel.NativeConnection(true, 0, 10) });
Assert(unknownAutoInvert.Plan.Assignments.Length == 0 && unknownAutoInvert.Plan.PendingConnections.Length == 1, "unknown automatic invert remains pending");
var emptyAuto = SewerInfoModel.MatchAutomatically(autoMh1Connections, Array.Empty<SewerInfoModel.NativeConnection>());
Assert(emptyAuto.Plan.PendingConnections.Length == 2, "zero native pipes retains all plan data");
foreach (var (direction,dx,dy) in new[] { ("N",0d,1d),("NE",1d,1d),("E",1d,0d),("SE",1d,-1d),("S",0d,-1d),("SW",-1d,-1d),("W",-1d,0d),("NW",-1d,1d) })
    Assert(SewerInfoModel.MatchAutomatically(new[] { new SewerConnection { Flow = "out", Direction = direction, InvertElevation = 1 } }, new[] { new SewerInfoModel.NativeConnection(true,dx,dy) }).Plan.Assignments.Length == 1, "WCS compass " + direction);
var wrapNorth = SewerInfoModel.MatchAutomatically(autoMh1Connections, new[] { new SewerInfoModel.NativeConnection(true,-0.001,1) });
Assert(wrapNorth.Plan.Assignments.Length == 1, "north bearing wraps around 360");
var boundary = SewerInfoModel.MatchAutomatically(autoMh1Connections, new[] { new SewerInfoModel.NativeConnection(true, Math.Sin(Math.PI/8), Math.Cos(Math.PI/8)) });
Assert(boundary.Plan.Assignments.Length == 0, "compass sector boundary not guessed");
Console.WriteLine("PASS: automatic START/END roles, WCS compass matching, missing branches, aliases, ambiguity and pending unknowns.");
var ccwStart = SewerInfoModel.CurveOutwardTangent(1, 0, false, true);
var ccwEnd = SewerInfoModel.CurveOutwardTangent(0, 1, false, false);
var cwStart = SewerInfoModel.CurveOutwardTangent(0, 1, true, true);
var cwEnd = SewerInfoModel.CurveOutwardTangent(1, 0, true, false);
Assert(ccwStart.Dx == 0 && ccwStart.Dy == 1, "CCW start tangent points into arc north");
Assert(ccwEnd.Dx == 1 && ccwEnd.Dy == 0, "CCW end outward tangent reverses forward derivative east");
Assert(cwStart.Dx == 1 && cwStart.Dy == 0, "CW start tangent east");
Assert(cwEnd.Dx == 0 && cwEnd.Dy == 1, "CW end outward tangent north");
var tangentOptions = new[]
{
    new SewerConnection { Flow = "out", Direction = "N", InvertElevation = 100 },
    new SewerConnection { Flow = "out", Direction = "NW", InvertElevation = 101 }
};
Assert(SewerInfoModel.MatchAutomatically(tangentOptions, new[] { new SewerInfoModel.NativeConnection(true, ccwStart.Dx, ccwStart.Dy) }).Plan.Assignments.Single().ConnectionIndex == 0, "curved-pipe tangent selects N instead of NW chord");
Assert(SewerInfoModel.MatchAutomatically(tangentOptions, new[] { new SewerInfoModel.NativeConnection(true, -1, 1) }).Plan.Assignments.Single().ConnectionIndex == 1, "control chord would select different plan connection");
var scaledTangent = SewerInfoModel.CurveOutwardTangent(1000, 0, false, true);
Assert(SewerInfoModel.MatchAutomatically(tangentOptions, new[] { new SewerInfoModel.NativeConnection(true, scaledTangent.Dx, scaledTangent.Dy) }).Plan.Assignments.Single().ConnectionIndex == 0, "tangent direction independent of radius");
Reject(() => SewerInfoModel.CurveOutwardTangent(0, 0, false, true), "zero radius vector");
Reject(() => SewerInfoModel.CurveOutwardTangent(double.NaN, 1, false, true), "invalid radius vector");
Console.WriteLine("PASS: clockwise/counterclockwise endpoint tangents and curved-pipe automatic direction matching.");
Console.WriteLine("PASS: sewer catalog naming, schema, provenance, dimensions/materials, unknowns, validation, merge and block labels.");
foreach (string path in args)
{
    SewerCatalog input = SewerInfoModel.Parse(File.ReadAllText(path));
    SewerCatalog reread = SewerInfoModel.Parse(SewerInfoModel.Serialize(input));
    Assert(reread.Manholes.Count == input.Manholes.Count, "supplied catalog round trip");
    Console.WriteLine($"PASS supplied catalog: {input.Manholes.Count} records, {input.Manholes.Sum(m => m.Connections.Count)} connections.");
}



