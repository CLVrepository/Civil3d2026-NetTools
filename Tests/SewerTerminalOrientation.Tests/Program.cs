using CLV_CivilTools.Gis;

int passed = 0;
void Check(string name, bool condition) { if (!condition) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
SewerPoint3 P(double x, double y = 0, double z = 0) => new(x, y, z);
SewerTerminal Mh(string name, double x, double radius = 2, double z = 0)
    => new(name, SewerOpenEndKind.None, P(x, z: z), new SewerCircle(name, x, 0, radius), z);
SewerTerminal Open(string name, double x, SewerOpenEndKind kind = SewerOpenEndKind.Connection, double z = 0)
    => new(name, kind, P(x, z: z), null, 0);
var a = Mh("A", 0); var b = Mh("B", 10);
var start = P(1); var end = P(9);
bool legacyForward = SewerTerminalOrientation.LegacyPointMatches(start, a, 0.10) && SewerTerminalOrientation.LegacyPointMatches(end, b, 0.10);
bool legacyReverse = SewerTerminalOrientation.LegacyPointMatches(start, b, 0.10) && SewerTerminalOrientation.LegacyPointMatches(end, a, 0.10);
Check("Reproduces previous production center gate rejection", !legacyForward && !legacyReverse);
var current = SewerTerminalOrientation.Plan(start, end, a, b);
Check("OD-bound off-center endpoints have unique forward orientation", current.Unique && current.Forward);
var clip = SewerCircleTrim.Plan(new(1, 0), new(9, 0), a.Circle, b.Circle);
Check("Existing production clip produces x=2 and x=8", clip.Kind == SewerClipKind.Trimmed && Math.Abs(1 + 8 * clip.StartParameter - 2) < 1e-12 && Math.Abs(1 + 8 * clip.EndParameter - 8) < 1e-12);
Check("Reversed off-center line has unique reverse orientation", SewerTerminalOrientation.Plan(end, start, a, b) is { Unique: true, Reverse: true });
Check("Centered ordinary endpoints still work", SewerTerminalOrientation.Plan(P(0), P(10), a, b) is { Unique: true, Forward: true });
Check("Endpoints already on outer boundary work", SewerTerminalOrientation.Plan(P(2), P(8), a, b) is { Unique: true, Forward: true });
Check("Outside gap is not extended", !SewerTerminalOrientation.Plan(P(2.01), P(8), a, b).Unique);
Check("Tangent contact requires review", !SewerTerminalOrientation.Plan(P(0, 2), P(10, 2), a, b).Unique);
Check("Overlapping outer circles require review", !SewerTerminalOrientation.Plan(P(0), P(3), a, Mh("B", 3)).Unique);
Check("Same terminal identity requires review", !SewerTerminalOrientation.Plan(P(0), P(10), a, a).Unique);
Check("Physical circle Z mismatch requires review", !SewerTerminalOrientation.Plan(P(0), P(10), a with { CircleZ = 1 }, b).Unique);
Check("Imported point Z mismatch remains review", !SewerTerminalOrientation.Plan(P(0), P(10), a with { Point = P(0, z: 1) }, b).Unique);
Check("Horizontal geometry at nonzero elevation works", SewerTerminalOrientation.Plan(P(1, z: 12), P(9, z: 12), Mh("A", 0, z: 12), Mh("B", 10, z: 12)).Unique);
Check("Sloped line is not flattened", !SewerTerminalOrientation.Plan(P(0), P(10, z: 1), a, b).Unique);
Check("Nonfinite geometry requires review", !SewerTerminalOrientation.Plan(P(double.NaN), P(10), a, b).Unique);
Check("Connection preserves exact open terminal", SewerTerminalOrientation.Plan(P(1), P(10), a, Open("SSWR-CONN", 10)) is { Unique: true, Forward: true });
Check("Connection nearby point is not accepted", !SewerTerminalOrientation.Plan(P(1), P(10.01), a, Open("SSWR-CONN", 10)).Unique);
Check("Connection Z mismatch is not accepted", !SewerTerminalOrientation.Plan(P(1), P(10), a, Open("SSWR-CONN", 10, z: 1)).Unique);
Check("Null end preserves exact open terminal", SewerTerminalOrientation.Plan(P(1), P(10), a, Open("SSWR-STUB", 10, SewerOpenEndKind.NullStub)).Unique);
Check("Legacy null XY allowance is preserved without relocation", SewerTerminalOrientation.Plan(P(1), P(10.01), a, Open("SSWR-STUB", 10, SewerOpenEndKind.NullStub)).Unique && SewerCircleTrim.Plan(new(1, 0), new(10.01, 0), a.Circle, null).EndParameter == 1);
Check("Null point outside legacy XY tolerance rejected", !SewerTerminalOrientation.Plan(P(1), P(10.11), a, Open("SSWR-STUB", 10, SewerOpenEndKind.NullStub)).Unique);
Check("Null point Z mismatch still rejected", !SewerTerminalOrientation.Plan(P(1), P(10), a, Open("SSWR-STUB", 10, SewerOpenEndKind.NullStub, z: 1)).Unique);
var ambiguous = SewerTerminalOrientation.Plan(P(-0.5e-8), P(1e-8), Open("OPEN-A", 0), Open("OPEN-B", 0.5e-8));
Check("Two feasible orientations are rejected", ambiguous.Forward && ambiguous.Reverse && !ambiguous.Unique);
var outerA = Mh("A", 0, 2.4167); var outerB = Mh("B", 10, 2.4167);
Check("Observed inner-wall endpoints orient against OD-linked outer circles", SewerTerminalOrientation.Plan(P(2), P(8), outerA, outerB) is { Unique: true, Forward: true });
Check("Observed reversed inner-wall endpoints orient correctly", SewerTerminalOrientation.Plan(P(8), P(2), outerA, outerB) is { Unique: true, Reverse: true });
foreach (double offset in new[] { -0.5, 0.0, 0.5 })
{
    var decision = SewerCircleTrim.Plan(new(2, offset), new(8, offset), outerA.Circle, outerB.Circle);
    double expected = Math.Sqrt(2.4167 * 2.4167 - offset * offset);
    Check("Inner-wall center/12-inch wall clip " + offset,
        decision.Kind == SewerClipKind.Trimmed && Math.Abs(2 + 6 * decision.StartParameter - expected) < 1e-8 && Math.Abs(2 + 6 * decision.EndParameter - (10 - expected)) < 1e-8);
    var reversed = SewerCircleTrim.Plan(new(8, offset), new(2, offset), outerB.Circle, outerA.Circle);
    Check("Reversed inner-wall center/12-inch wall clip " + offset,
        reversed.Kind == SewerClipKind.Trimmed && Math.Abs(8 - 6 * reversed.StartParameter - (10 - expected)) < 1e-8 && Math.Abs(8 - 6 * reversed.EndParameter - expected) < 1e-8);
}
Check("Offset wall gap is not extended", SewerCircleTrim.Plan(new(2, 0.5), new(8, 0.5), a.Circle, b.Circle).Kind == SewerClipKind.Review);
Check("Observed both-blank lateral keeps source direction", SewerTerminalOrientation.Plan(
    new(865656.3249999999, 1287950.260000000, 0), new(865634.2809999999, 1287961.410000000, 0), null, null) is { Unique: true, Forward: true, Reverse: false });
Check("Observed kink segment remains separate and directed", SewerTerminalOrientation.Plan(
    new(865634.2809999999, 1287961.410000000, 0), new(865633.6500952667, 1287963.092239749, 0), null, null) is { Unique: true, Forward: true });
Check("Blank-end degenerate segment rejected", !SewerTerminalOrientation.Plan(P(0), P(0), null, null).Unique);
Check("Blank-end nonflat segment rejected", !SewerTerminalOrientation.Plan(P(0), P(10, z: 1), null, null).Unique);
Check("Blank-end nonfinite segment rejected", !SewerTerminalOrientation.Plan(P(0), P(double.NaN), null, null).Unique);
Check("One blank endpoint clips only named manhole", SewerTerminalOrientation.Plan(P(1), P(10), a, null) is { Unique: true, Forward: true });
Check("Reversed one-blank endpoint keeps unique named association", SewerTerminalOrientation.Plan(P(10), P(1), a, null) is { Unique: true, Reverse: true });
Check("One blank endpoint cannot extend to named circle", !SewerTerminalOrientation.Plan(P(3), P(10), a, null).Unique);
Check("One blank endpoint cannot flatten named circle", !SewerTerminalOrientation.Plan(P(1), P(10), a with { CircleZ = 1 }, null).Unique);
Check("Present empty Character endpoint accepted explicitly", SewerPreparationRules.TryNormalizePipeEndpoint(1, "Character", "", true, out var blank) && blank == "");
Check("Whitespace planning endpoint retains blank semantics", SewerPreparationRules.TryNormalizePipeEndpoint(1, "Character", "  ", true, out blank) && blank == "");
Check("Legacy strict reader still rejects empty endpoints", !SewerPreparationRules.TryNormalizePipeEndpoint(1, "Character", "", false, out _));
Check("Missing endpoint is not blank", !SewerPreparationRules.TryNormalizePipeEndpoint(0, null, null, true, out _));
Check("Duplicate endpoint is not blank", !SewerPreparationRules.TryNormalizePipeEndpoint(2, "Character", "", true, out _));
Check("Wrong endpoint type is not blank", !SewerPreparationRules.TryNormalizePipeEndpoint(1, "Real", 0.0, true, out _));
Check("Unavailable Character value is not blank", !SewerPreparationRules.TryNormalizePipeEndpoint(1, "Character", null, true, out _));
Check("Nonblank unresolved name is never normalized to blank", SewerPreparationRules.TryNormalizePipeEndpoint(1, "Character", " UNKNOWN ", true, out var named) && named == "UNKNOWN");
Check("Observed generic lateral needs batch consent", !SewerPreparationRules.HasPositiveSewerEvidence("Pipes", new[] { "Pipes" }, new[] { "Pipe - (221)", "", "" }));
Check("Named sewer main supplies positive evidence", SewerPreparationRules.HasPositiveSewerEvidence("Pipes", new[] { "Pipes" }, new[] { "Pipe - (12)", "L24-01394-SSMH-12", "L24-01394-SSMH-13" }));
Check("Sewer table supplies positive evidence", SewerPreparationRules.HasPositiveSewerEvidence("Pipes", new[] { "SS_Pipes" }, new[] { "Pipe - (221)", "", "" }));
Check("Explicit storm still excluded before consent", SewerPreparationRules.ClassifyUtility("Pipes", new[] { "SD_Pipes" }, new[] { "Pipe - (221)", "", "" }) == SewerPipeUtilityKind.ExcludedStorm);
Check("Conflicting storm and sewer cannot be approved", SewerPreparationRules.ClassifyUtility("Pipes", new[] { "SD_Pipes" }, new[] { "L24-01394-SSMH-12" }) == SewerPipeUtilityKind.Review);
Console.WriteLine($"{passed}/{passed} production-linked checks passed. Native OD, locks, appearance, prompts and transactions require host testing.");
