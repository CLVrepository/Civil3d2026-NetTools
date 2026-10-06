using CLV_CivilTools.Gis;

internal static class Program
{
    private static int Main()
    {
        var tests = new (string Name, Action Run)[]
        {
            ("Classifies verified DI tokens", ClassifiesDi),
            ("Requires explicit access evidence", RequiresExplicitAccess),
            ("JS suffix has box precedence", BoxPrecedence),
            ("Conflicting DI roles stay unknown", ConflictingRoles),
            ("Normalizes exact trailing JS suffix only", BaseNames),
            ("Accepts centered access and box as two assets", CenteredPair),
            ("Accepts standalone explicit access", StandaloneAccess),
            ("Accepts standalone box", StandaloneBox),
            ("Accepts ordinary DI", OrdinaryDi),
            ("Never cross-matches roles", RolePartition),
            ("Defers named offset access/box pair", OffsetPair),
            ("Allows pair at tolerance boundary", PairAtBoundary),
            ("Requires exact base-name pairing", ExactBasePairing),
            ("Rejects duplicate names case-insensitively", DuplicateNames),
            ("Blocks partner when access identity is duplicated", DuplicatePair),
            ("Blocks duplicate source IDs", DuplicateSourceIds),
            ("Blocks duplicate target IDs", DuplicateTargetIds),
            ("Does not choose a closest competing target", CompetingTargets),
            ("Does not choose a closest competing source", CompetingSources),
            ("Does not reuse a target in a multi-edge graph", MultiEdgeGraph),
            ("Blocked duplicates still compete for geometry", BlockedCompetition),
            ("Uses radial XY distance within 0.10", RadiusRules),
            ("Has no loose radius fallback", NoLooseFallback),
            ("Rejects nonfinite and missing source data", InvalidSources),
            ("Rejects unknown and invalid target roles", InvalidTargets),
            ("Returns issues for unmatched sources and targets", UnmatchedIssues),
            ("Handles empty input", EmptyInput),
            ("Does not require all paired targets to exist", IncompleteGeometry),
            ("Rejects pair when one member is invalid", InvalidPair),
            ("Planning results are order-independent", OrderIndependent),
            ("Planning is repeatable and does not mutate inputs", Repeatable),
            ("Null collections fail explicitly", NullCollections),
            ("Accepted matches remain one-to-one in generated cases", GeneratedCases)
        };
        int failed = 0;
        foreach ((string name, Action run) in tests)
        {
            try { run(); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + ex.Message); }
        }
        Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed.");
        return failed == 0 ? 0 : 1;
    }

    private static StormStructureSource Access(string id, string name = "SD-1", double x = 0, double y = 0)
        => new(id, name, "TYPE I ACCESS STRUCTURE 48 INCH BARREL 24 INCH LID", x, y);
    private static StormStructureSource Box(string id, string name = "SD-1-JS", double x = 0, double y = 0)
        => new(id, name, "Rectangular junction structure", x, y);
    private static StormStructureSource Di(string id, string name = "DI-1", double x = 0, double y = 0)
        => new(id, name, "SDDI : L=30 x W=18 Walls=6 Inch", x, y);
    private static StormStructureTarget Target(string id, StormStructureRole role, double x = 0, double y = 0)
        => new(id, role, x, y);
    private static StormStructureMatchResult Match(StormStructureSource[] sources, params StormStructureTarget[] targets)
        => StormStructureMatching.Match(sources, targets);
    private static void Check(bool condition, string message = "Assertion failed")
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Count(StormStructureMatchResult result, int count)
        => Check(result.Matches.Count == count, $"Expected {count} matches; got {result.Matches.Count}: {string.Join(", ", result.Issues.Select(i => i.Code))}");
    private static void Issue(StormStructureMatchResult result, string code)
        => Check(result.Issues.Any(i => i.Code == code), "Expected issue " + code);
    private static string Signature(StormStructureMatchResult result)
        => string.Join("|", result.Matches.Select(m => $"{m.SourceId}>{m.TargetId}:{m.Role}")) + ";" +
            string.Join("|", result.Issues.Select(i => $"{i.Code}:{string.Join(",", i.SourceIds)}:{string.Join(",", i.TargetIds)}"));

    private static void ClassifiesDi()
    {
        foreach (string text in new[] { "SDDI : L=30", "sddi-123", "TYPE_A-USD_411", "TYPE_A_MOD-USD_411.1", "TYPE_C-USD_413", "TYPE_CM-USD_422", "TYPE_CM2-USD_412.1", "TYPE_D-USD_414", "TYPE_DM2-USD_412.1", "TYPE A (USD 411)", "TYPE A MOD (USD 411.1)" })
            Check(StormStructureMatching.Classify("DI-1", text) == StormStructureRole.DropInlet, text);
        Check(StormStructureMatching.Classify("SDDI123", "") == StormStructureRole.DropInlet);
        Check(StormStructureMatching.Classify("XSDDI", "TYPE ABC") == StormStructureRole.Unknown);
    }
    private static void RequiresExplicitAccess()
    {
        Check(StormStructureMatching.Classify(" MH-1 ", "TYPE IA ACCESS STRUCTURE") == StormStructureRole.Access);
        foreach (string text in new[] { "MANHOLE", "TYPE I", "ACCESS", "ACCESS STRUCTURES", "XACCESS STRUCTURE" })
            Check(StormStructureMatching.Classify("MH-1", text) == StormStructureRole.Unknown, text);
        Check(StormStructureMatching.Classify("", "ACCESS STRUCTURE") == StormStructureRole.Unknown);
        Check(StormStructureMatching.Classify(null, null) == StormStructureRole.Unknown);
    }
    private static void BoxPrecedence()
    {
        Check(StormStructureMatching.Classify("sd-1-js", "TYPE I ACCESS STRUCTURE") == StormStructureRole.JunctionBox);
        Check(StormStructureMatching.Classify("SD-1-JS", "") == StormStructureRole.JunctionBox);
        Check(StormStructureMatching.Classify("SD-1-JS-EXTRA", "") == StormStructureRole.Unknown);
        Check(StormStructureMatching.Classify("-JS", "") == StormStructureRole.Unknown);
    }
    private static void ConflictingRoles()
    {
        Check(StormStructureMatching.Classify("SDDI-1-JS", "") == StormStructureRole.Unknown);
        Check(StormStructureMatching.Classify("SD-1-JS", "TYPE_A-USD_411") == StormStructureRole.Unknown);
        Check(StormStructureMatching.Classify("SDDI-1", "TYPE I ACCESS STRUCTURE") == StormStructureRole.Unknown);
        Check(StormStructureMatching.Classify("SD-1", "SDDI ACCESS STRUCTURE") == StormStructureRole.Unknown);
    }
    private static void BaseNames()
    {
        Check(StormStructureMatching.BaseName(" SD-1-js ") == "SD-1");
        Check(StormStructureMatching.BaseName("SD-1-JS-EXTRA") == "SD-1-JS-EXTRA");
        Check(StormStructureMatching.BaseName(null) == "");
    }
    private static void CenteredPair()
    {
        var result = Match(new[] { Access("A"), Box("B") }, Target("TA", StormStructureRole.Access), Target("TB", StormStructureRole.JunctionBox));
        Count(result, 2); Check(result.Issues.Count == 0);
        Check(result.Matches.Single(m => m.SourceId == "A").TargetId == "TA");
    }
    private static void StandaloneAccess() => Count(Match(new[] { Access("A") }, Target("T", StormStructureRole.Access)), 1);
    private static void StandaloneBox() => Count(Match(new[] { Box("B") }, Target("T", StormStructureRole.JunctionBox)), 1);
    private static void OrdinaryDi() => Count(Match(new[] { Di("D") }, Target("T", StormStructureRole.DropInlet)), 1);
    private static void RolePartition()
    {
        Count(Match(new[] { Access("A") }, Target("B", StormStructureRole.JunctionBox)), 0);
        Count(Match(new[] { Access("A"), Box("B"), Di("D") }, Target("TA", StormStructureRole.Access), Target("TB", StormStructureRole.JunctionBox), Target("TD", StormStructureRole.DropInlet)), 3);
    }
    private static void OffsetPair()
    {
        var result = Match(new[] { Access("A", x: 0.11), Box("B") }, Target("TA", StormStructureRole.Access, 0.11), Target("TB", StormStructureRole.JunctionBox));
        Count(result, 0); Issue(result, "OffsetPair");
    }
    private static void PairAtBoundary() => Count(Match(new[] { Access("A", x: 0.10), Box("B") }, Target("TA", StormStructureRole.Access, 0.10), Target("TB", StormStructureRole.JunctionBox)), 2);
    private static void ExactBasePairing()
    {
        var result = Match(new[] { Access("A", "SD-10", 50), Box("B", "SD-1-JS") }, Target("TA", StormStructureRole.Access, 50), Target("TB", StormStructureRole.JunctionBox));
        Count(result, 2); Check(!result.Issues.Any(i => i.Code == "OffsetPair"));
    }
    private static void DuplicateNames()
    {
        var result = Match(new[] { Access("A1", "SD-1"), Access("A2", " sd-1 ", 10) }, Target("T1", StormStructureRole.Access), Target("T2", StormStructureRole.Access, 10));
        Count(result, 0); Issue(result, "DuplicateIdentity");
    }
    private static void DuplicatePair()
    {
        var result = Match(new[] { Access("A1"), Access("A2"), Box("B") }, Target("TA", StormStructureRole.Access), Target("TB", StormStructureRole.JunctionBox));
        Count(result, 0); Issue(result, "AmbiguousPair");
    }
    private static void DuplicateSourceIds()
    {
        var result = Match(new[] { Access("A"), Box("a") }, Target("TA", StormStructureRole.Access), Target("TB", StormStructureRole.JunctionBox));
        Count(result, 0); Issue(result, "DuplicateSourceId");
    }
    private static void DuplicateTargetIds()
    {
        var result = Match(new[] { Access("A"), Box("B") }, Target("T", StormStructureRole.Access), Target("t", StormStructureRole.JunctionBox));
        Count(result, 0); Issue(result, "DuplicateTargetId");
    }
    private static void CompetingTargets()
    {
        var result = Match(new[] { Access("A") }, Target("T1", StormStructureRole.Access), Target("T2", StormStructureRole.Access, 0.09));
        Count(result, 0); Issue(result, "AmbiguousSource");
    }
    private static void CompetingSources()
    {
        var result = Match(new[] { Access("A1"), Access("A2", "SD-2", 0.09) }, Target("T", StormStructureRole.Access));
        Count(result, 0); Issue(result, "AmbiguousTarget");
    }
    private static void MultiEdgeGraph()
    {
        var result = Match(new[] { Access("A1"), Access("A2", "SD-2", 0.18) }, Target("T1", StormStructureRole.Access, 0.09), Target("T2", StormStructureRole.Access, -0.09));
        Count(result, 0); Issue(result, "AmbiguousSource"); Issue(result, "AmbiguousTarget");
    }
    private static void BlockedCompetition()
    {
        var result = Match(new[] { Access("A1"), Access("A2"), Access("A3", "SD-2", 0.02) }, Target("T", StormStructureRole.Access));
        Count(result, 0); Issue(result, "AmbiguousTarget");
    }
    private static void RadiusRules()
    {
        Count(Match(new[] { Access("A") }, Target("T", StormStructureRole.Access, 0.10)), 1);
        Count(Match(new[] { Access("A") }, Target("T", StormStructureRole.Access, 0.10001)), 0);
        Count(Match(new[] { Access("A") }, Target("T", StormStructureRole.Access, 0.08, 0.08)), 0);
        Count(Match(new[] { Access("A") }, Target("T", StormStructureRole.Access, 0.06, 0.079)), 1);
    }
    private static void NoLooseFallback()
    {
        foreach (double distance in new[] { 0.5, 1.5, 3.0, 25.0 })
            Count(Match(new[] { Access("A") }, Target("T", StormStructureRole.Access, distance)), 0);
    }
    private static void InvalidSources()
    {
        foreach (StormStructureSource source in new[] { Access(""), Access("A", ""), Access("A", x: double.NaN), Access("A", y: double.PositiveInfinity), new StormStructureSource("A", "SD-1", "Manhole", 0, 0) })
            Count(Match(new[] { source }, Target("T", StormStructureRole.Access)), 0);
    }
    private static void InvalidTargets()
    {
        foreach (StormStructureTarget target in new[] { Target("", StormStructureRole.Access), Target("T", StormStructureRole.Unknown), Target("T", (StormStructureRole)99), Target("T", StormStructureRole.Access, double.NaN) })
        {
            var result = Match(new[] { Access("A") }, target); Count(result, 0); Issue(result, "InvalidTarget");
        }
    }
    private static void UnmatchedIssues()
    {
        var result = Match(new[] { Access("A") }, Target("T", StormStructureRole.Access, 1));
        Count(result, 0); Issue(result, "UnmatchedSource"); Issue(result, "UnmatchedTarget");
    }
    private static void EmptyInput()
    {
        var result = Match(Array.Empty<StormStructureSource>()); Count(result, 0); Check(result.Issues.Count == 0);
    }
    private static void IncompleteGeometry()
    {
        var result = Match(new[] { Access("A"), Box("B") }, Target("TB", StormStructureRole.JunctionBox));
        Count(result, 1); Check(result.Matches[0].SourceId == "B"); Issue(result, "UnmatchedSource");
    }
    private static void InvalidPair()
    {
        var result = Match(new[] { Access("A", x: double.NaN), Box("B") }, Target("TB", StormStructureRole.JunctionBox));
        Count(result, 0); Issue(result, "InvalidPair");
    }
    private static void OrderIndependent()
    {
        var sources = new[] { Access("A"), Box("B"), Di("D", x: 10), Di("E", "DI-2", 10.02) };
        var targets = new[] { Target("TA", StormStructureRole.Access), Target("TB", StormStructureRole.JunctionBox), Target("TD", StormStructureRole.DropInlet, 10) };
        Check(Signature(Match(sources, targets)) == Signature(Match(sources.Reverse().ToArray(), targets.Reverse().ToArray())));
    }
    private static void Repeatable()
    {
        var sources = new[] { Access("A"), Box("B") };
        var targets = new[] { Target("TA", StormStructureRole.Access), Target("TB", StormStructureRole.JunctionBox) };
        string before = string.Join("|", sources.Select(s => s.ToString()));
        Check(Signature(Match(sources, targets)) == Signature(Match(sources, targets)));
        Check(before == string.Join("|", sources.Select(s => s.ToString())));
    }
    private static void NullCollections()
    {
        int thrown = 0;
        try { StormStructureMatching.Match(null!, Array.Empty<StormStructureTarget>()); } catch (ArgumentNullException) { thrown++; }
        try { StormStructureMatching.Match(Array.Empty<StormStructureSource>(), null!); } catch (ArgumentNullException) { thrown++; }
        Check(thrown == 2);
    }
    private static void GeneratedCases()
    {
        var random = new Random(1741);
        for (int run = 0; run < 50; run++)
        {
            StormStructureSource[] sources = Enumerable.Range(0, 20).Select(i => Access("S" + i, "SD-" + i, random.Next(8) * 0.04, random.Next(8) * 0.04)).ToArray();
            StormStructureTarget[] targets = Enumerable.Range(0, 20).Select(i => Target("T" + i, StormStructureRole.Access, random.Next(8) * 0.04, random.Next(8) * 0.04)).ToArray();
            var result = Match(sources, targets);
            Check(result.Matches.Select(m => m.SourceId).Distinct().Count() == result.Matches.Count);
            Check(result.Matches.Select(m => m.TargetId).Distinct().Count() == result.Matches.Count);
            Check(Signature(result) == Signature(Match(sources.Reverse().ToArray(), targets.Reverse().ToArray())));
        }
    }
}
