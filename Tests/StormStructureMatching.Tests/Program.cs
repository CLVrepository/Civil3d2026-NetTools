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
            ("Preserves verified UFLS null STUB pipe end", VerifiedNullPipeEnd),
            ("Null pipe-end convention is case and trim insensitive", NullPipeEndCaseAndTrim),
            ("Other null or incomplete OD is not exempt", NullPipeEndRequiresExactConvention),
            ("Contradictory null pipe-end roles stay unknown", ConflictingNullPipeEnd),
            ("Null pipe ends retain duplicate ID validation", DuplicateNullPipeEndIds),
            ("Null pipe ends retain duplicate name validation", DuplicateNullPipeEndNames),
            ("Preserved null pipe ends never consume geometry", NullPipeEndDoesNotMatch),
            ("Null pipe-end destinations are invalid", NullPipeEndTargetInvalid),
            ("Malformed null pipe ends still require review", InvalidNullPipeEnds),
            ("Verified NDOT source remains classified by SDDI name", VerifiedNdotSource),
            ("Result constructor remains compatible", MatchResultCompatibility),
            ("Normalizes exact trailing JS suffix only", BaseNames),
            ("Accepts centered access and box as two assets", CenteredPair),
            ("Accepts standalone explicit access", StandaloneAccess),
            ("Accepts standalone box", StandaloneBox),
            ("Accepts ordinary DI", OrdinaryDi),
            ("Never cross-matches roles", RolePartition),
            ("Accepts eccentric named pair at independent centers", EccentricPair),
            ("Eccentric pair retains strict own-target tolerance", EccentricPairOwnTolerance),
            ("Eccentric pair cannot cross-match role geometry", EccentricPairRolePartition),
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
            ("Matches skew junction quadrilateral", SkewJunctionFootprint),
            ("Matches junction source away from footprint center", OffCenterJunctionSource),
            ("Junction boundary has numerical slack only", JunctionBoundary),
            ("Rejects nearby sources outside junction footprint", OutsideJunctionFootprint),
            ("Junction footprint cannot consume two sources", CompetingJunctionSources),
            ("Overlapping junction footprints stay ambiguous", OverlappingJunctionFootprints),
            ("Junction footprint retains 25-unit search radius", JunctionSearchRadius),
            ("Rejects invalid junction footprints", InvalidJunctionFootprints),
            ("Footprints never loosen DI or access tolerance", OtherRolesIgnoreFootprint),
            ("Junction without footprint keeps 0.10 tolerance", CenterOnlyJunctionTarget),
            ("Supports concave footprints and either winding", ConcaveFootprint),
            ("Containment remains stable at survey coordinates", SurveyCoordinateFootprint),
            ("Containment helper rejects invalid query points", InvalidContainmentQuery),
            ("Contains nested skew and offset inner outlines", NestedInnerOutlines),
            ("Rejects partly outside inner outline", PartlyOutsideInnerOutline),
            ("Rejects inner edge crossing concave exterior", CrossingConcaveInnerOutline),
            ("Contains identical outline for caller area check", IdenticalInnerOutline),
            ("Outline containment rejects invalid polygons", InvalidOutlineContainment),
            ("Existing shared outline converts NDOT source as DI", SharedOutlineNdot),
            ("Existing shared outline accepts junction source", SharedOutlineBox),
            ("DI and box compete for one physical shared outline", SharedOutlineCrossRoleCompetition),
            ("Existing bound outlines respect OD role", BoundExistingOutlineRoles),
            ("Shared outlines never accept access or null sources", SharedOutlineRejectsOtherSources),
            ("Access and null cannot be existing-outline target roles", InvalidExistingOutlineRoles),
            ("Overlapping shared outlines remain ambiguous", OverlappingSharedOutlines),
            ("Existing outlines require a valid footprint", InvalidExistingOutlineFootprints),
            ("Existing DI outline uses containment and 25-unit bound", ExistingOutlineBounds),
            ("Unknown target without explicit outline flag remains invalid", UnflaggedUnknownTarget),
            ("Duplicate physical outline IDs remain invalid", DuplicateSharedOutlineIds),
            ("DI block and existing outline share one ambiguity graph", BlockAndOutlineCompetition),
            ("Completed multi-output source reserves every output", CompletedMultiOutputClaim),
            ("Secondary output overlap conflicts both completions", SecondaryCompletionOverlap),
            ("Disjoint valid completion claims remain accepted", DisjointCompletionClaims),
            ("Repeated output within completion invalidates whole claim", DuplicateCompletionOutput),
            ("Repeated source completion claims are rejected", DuplicateCompletionSource),
            ("Completion ownership collisions ignore ID case", CompletionOwnershipCase),
            ("Completion planning is order-independent", CompletionOrderIndependent),
            ("Malformed completion claims reserve nothing", InvalidCompletionClaims),
            ("Invalid claims still participate in ownership conflicts", InvalidCompletionOverlap),
            ("Empty completion planning is valid", EmptyCompletions),
            ("Accepted completion outputs are snapshots", CompletionOutputSnapshot),
            ("Visibility manifest visits only the selected manhole pair", VisibilityManholeManifest),
            ("Hidden ancestor suppresses all descendants", VisibilityHiddenParent),
            ("Hidden root is never expanded", VisibilityHiddenRoot),
            ("All hidden children produce no leaves", VisibilityAllHidden),
            ("Empty container is not a geometry leaf", VisibilityEmptyContainer),
            ("Visibility callback failures propagate unchanged", VisibilityCallbackFailures),
            ("Visibility traversal bounds visible nesting", VisibilityDepthBound),
            ("Visible traversal preserves depth-first entity order", VisibilityOrdering),
            ("Null traversal callbacks fail before traversal", VisibilityNullCallbacks),
            ("Null child collection identifies a leaf", VisibilityLeaf),
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
    private static StormStructureSource NullPipeEnd(string id = "D5C4A", string name = "L24-00066-STRM-63+75-STUB", double x = 0, double y = 0)
        => new(id, name, "UFLS-Null Structure", x, y);
    private static StormStructureTarget Target(string id, StormStructureRole role, double x = 0, double y = 0)
        => new(id, role, x, y);
    private static StormStructureVertex[] Rectangle(double left = -5, double bottom = -5, double right = 5, double top = 5)
        => new[] { new StormStructureVertex(left, bottom), new StormStructureVertex(right, bottom), new StormStructureVertex(right, top), new StormStructureVertex(left, top) };
    private static StormStructureTarget BoxTarget(string id, IReadOnlyList<StormStructureVertex> footprint, double x = 0, double y = 0)
        => new(id, StormStructureRole.JunctionBox, x, y, footprint);
    private static StormStructureTarget ExistingOutline(string id, IReadOnlyList<StormStructureVertex>? footprint,
        StormStructureRole role = StormStructureRole.Unknown, double x = 0, double y = 0)
        => new(id, role, x, y, footprint, IsExistingOutline: true);
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
            string.Join("|", result.Issues.Select(i => $"{i.Code}:{string.Join(",", i.SourceIds)}:{string.Join(",", i.TargetIds)}")) + ";" +
            string.Join(",", result.PreservedPipeEndSourceIds);
    private static string CompletionSignature(StormStructureCompletionPlan plan)
        => string.Join("|", plan.Accepted.Select(c => c.SourceId + ":" + string.Join(",", c.OutputIds))) + ";" +
            string.Join(",", plan.ConflictedSourceIds) + ";" +
            string.Join("|", plan.Issues.Select(i => $"{i.Code}:{string.Join(",", i.SourceIds)}:{string.Join(",", i.TargetIds)}"));

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
    private static void VerifiedNullPipeEnd()
    {
        StormStructureSource source = NullPipeEnd();
        Check(StormStructureMatching.Classify(source.Name, source.PartSizeName) == StormStructureRole.NullPipeEnd);
        var result = Match(new[] { source });
        Count(result, 0); Check(result.Issues.Count == 0);
        Check(result.PreservedPipeEndSourceIds.SequenceEqual(new[] { "D5C4A" }));
    }
    private static void NullPipeEndCaseAndTrim()
    {
        var source = new StormStructureSource("D5C4A", " l24-00066-strm-63+75-stub ", " ufls-null structure ", 0, 0);
        Check(StormStructureMatching.Classify(source.Name, source.PartSizeName) == StormStructureRole.NullPipeEnd);
        var result = Match(new[] { source });
        Check(result.Issues.Count == 0 && result.PreservedPipeEndSourceIds.Count == 1);
    }
    private static void NullPipeEndRequiresExactConvention()
    {
        foreach ((string name, string part) in new[]
        {
            ("L24-00066-STRM-63+75", "UFLS-Null Structure"),
            ("L24-00066-STRM-63+75-STUB", ""),
            ("L24-00066-STRM-63+75-STUB", "Null Structure"),
            ("L24-00066-STRM-63+75-STUB", "UFLS-Null Structure extra"),
            ("L24-00066-STRM-63+75-STUB", "UFLS-Null  Structure"),
            ("L24-00066-STRM-63+75STUB", "UFLS-Null Structure"),
            ("-STUB", "UFLS-Null Structure"),
            ("", "UFLS-Null Structure")
        })
        {
            Check(StormStructureMatching.Classify(name, part) == StormStructureRole.Unknown);
            var result = Match(new[] { new StormStructureSource("N", name, part, 0, 0) });
            Check(result.PreservedPipeEndSourceIds.Count == 0 && result.Issues.Count > 0);
        }
    }
    private static void ConflictingNullPipeEnd()
    {
        foreach (string name in new[] { "SDDI-1-STUB", "A-JS-STUB", "A-STUB-JS", "A-ACCESS STRUCTURE-STUB", "TYPE_A-STUB" })
        {
            Check(StormStructureMatching.Classify(name, "UFLS-Null Structure") == StormStructureRole.Unknown, name);
            var result = Match(new[] { NullPipeEnd(name: name) });
            Check(result.PreservedPipeEndSourceIds.Count == 0); Issue(result, "UnknownRole");
        }
    }
    private static void DuplicateNullPipeEndIds()
    {
        var result = Match(new[] { NullPipeEnd("N"), NullPipeEnd("n", "SD-2-STUB") });
        Check(result.PreservedPipeEndSourceIds.Count == 0); Issue(result, "DuplicateSourceId");
        result = Match(new[] { NullPipeEnd("N"), Access("n") }, Target("T", StormStructureRole.Access));
        Count(result, 0); Check(result.PreservedPipeEndSourceIds.Count == 0); Issue(result, "DuplicateSourceId");
    }
    private static void DuplicateNullPipeEndNames()
    {
        var result = Match(new[] { NullPipeEnd("N1"), NullPipeEnd("N2", " l24-00066-strm-63+75-stub ") });
        Check(result.PreservedPipeEndSourceIds.Count == 0); Issue(result, "DuplicateIdentity");
        result = Match(new[] { NullPipeEnd("N1"), Access("A", "L24-00066-STRM-63+75-STUB") }, Target("T", StormStructureRole.Access));
        Count(result, 0); Check(result.PreservedPipeEndSourceIds.Count == 0); Issue(result, "DuplicateIdentity");
    }
    private static void NullPipeEndDoesNotMatch()
    {
        var sources = new[] { NullPipeEnd(), Access("A"), Box("B"), Di("D") };
        var targets = new[] { Target("TA", StormStructureRole.Access), Target("TB", StormStructureRole.JunctionBox), Target("TD", StormStructureRole.DropInlet) };
        var result = Match(sources, targets);
        Count(result, 3); Check(result.Issues.Count == 0);
        Check(result.PreservedPipeEndSourceIds.SequenceEqual(new[] { "D5C4A" }));
        Check(result.Matches.All(m => m.SourceId != "D5C4A" && m.Role != StormStructureRole.NullPipeEnd));
        Check(Signature(result) == Signature(Match(sources.Reverse().ToArray(), targets.Reverse().ToArray())));
        var onlyNull = Match(new[] { NullPipeEnd() }, targets);
        Count(onlyNull, 0); Check(!onlyNull.Issues.Any(i => i.SourceIds.Contains("D5C4A")));
        Check(onlyNull.Issues.Count(i => i.Code == "UnmatchedTarget") == 3);
    }
    private static void NullPipeEndTargetInvalid()
    {
        var result = Match(new[] { NullPipeEnd() }, Target("T", StormStructureRole.NullPipeEnd));
        Count(result, 0); Issue(result, "InvalidTarget"); Check(result.PreservedPipeEndSourceIds.Count == 1);
    }
    private static void InvalidNullPipeEnds()
    {
        foreach (StormStructureSource source in new[] { NullPipeEnd(id: ""), NullPipeEnd(name: ""), NullPipeEnd(x: double.NaN), NullPipeEnd(y: double.PositiveInfinity) })
        {
            var result = Match(new[] { source });
            Count(result, 0); Check(result.PreservedPipeEndSourceIds.Count == 0); Issue(result, "InvalidSource");
        }
    }
    private static void VerifiedNdotSource()
        => Check(StormStructureMatching.Classify("L24-00066-SDDI-06", "4.00 ' X 4.00 ' NDOT TYPE 2") == StormStructureRole.DropInlet);
    private static void MatchResultCompatibility()
        => Check(new StormStructureMatchResult(Array.Empty<StormStructureMatch>(), Array.Empty<StormStructureIssue>()).PreservedPipeEndSourceIds.Count == 0);
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
    private static void EccentricPair()
    {
        foreach ((double x, double y) in new[] { (0.11, 0.0), (2.0, 1.0), (1000.0, -750.0) })
        {
            var result = Match(new[] { Access("A", "sd-1", x, y), Box("B", "SD-1-JS") },
                Target("TA", StormStructureRole.Access, x, y), Target("TB", StormStructureRole.JunctionBox));
            Count(result, 2); Check(result.Issues.Count == 0);
            Check(result.Matches.Single(m => m.SourceId == "A").TargetId == "TA");
            Check(result.Matches.Single(m => m.SourceId == "B").TargetId == "TB");
        }
    }
    private static void EccentricPairOwnTolerance()
    {
        var sources = new[] { Access("A", x: 5.0), Box("B") };
        Count(Match(sources, Target("TA", StormStructureRole.Access, 5.10), Target("TB", StormStructureRole.JunctionBox, 0.10)), 2);
        var result = Match(sources, Target("TA", StormStructureRole.Access, 5.10001), Target("TB", StormStructureRole.JunctionBox));
        Count(result, 1); Check(result.Matches[0].SourceId == "B"); Issue(result, "UnmatchedSource");
    }
    private static void EccentricPairRolePartition()
    {
        var result = Match(new[] { Access("A", x: 5.0), Box("B") },
            Target("TA", StormStructureRole.Access), Target("TB", StormStructureRole.JunctionBox, 5.0));
        Count(result, 0); Issue(result, "UnmatchedSource"); Issue(result, "UnmatchedTarget");
    }
    private static void ExactBasePairing()
    {
        var result = Match(new[] { Access("A", "SD-10", 50), Box("B", "SD-1-JS") }, Target("TA", StormStructureRole.Access, 50), Target("TB", StormStructureRole.JunctionBox));
        Count(result, 2); Check(result.Issues.Count == 0);
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
    private static void SkewJunctionFootprint()
    {
        var footprint = new[] { new StormStructureVertex(-4, -2), new StormStructureVertex(4.001, -2), new StormStructureVertex(4, 2), new StormStructureVertex(-4, 2.002) };
        Count(Match(new[] { Box("B", x: 3, y: 1) }, BoxTarget("TB", footprint, 0.0005, 0.001)), 1);
    }
    private static void OffCenterJunctionSource()
    {
        var result = Match(new[] { Box("B", x: 3, y: 1) }, BoxTarget("TB", Rectangle()));
        Count(result, 1); Check(result.Issues.Count == 0);
    }
    private static void JunctionBoundary()
    {
        foreach ((double x, double y) in new[] { (5.0, 0.0), (5.0, 5.0), (5.0 + 5e-9, 0.0) })
        {
            Check(StormStructureMatching.ContainsFootprint(Rectangle(), x, y));
            Count(Match(new[] { Box("B", x: x, y: y) }, BoxTarget("TB", Rectangle())), 1);
        }
        Check(!StormStructureMatching.ContainsFootprint(Rectangle(), 5 + 1e-6, 0));
        Count(Match(new[] { Box("B", x: 5 + 1e-6) }, BoxTarget("TB", Rectangle())), 0);
    }
    private static void OutsideJunctionFootprint()
    {
        Count(Match(new[] { Box("B", x: 5.05) }, BoxTarget("TB", Rectangle())), 0);
        // A supplied footprint must not fall back to the 0.10 center test outside it.
        Count(Match(new[] { Box("B", x: 0.03) }, BoxTarget("TB", Rectangle(-0.02, -0.02, 0.02, 0.02))), 0);
    }
    private static void CompetingJunctionSources()
    {
        var result = Match(new[] { Box("B1", "SD-1-JS", -2), Box("B2", "SD-2-JS", 2) }, BoxTarget("TB", Rectangle()));
        Count(result, 0); Issue(result, "AmbiguousTarget");
    }
    private static void OverlappingJunctionFootprints()
    {
        var result = Match(new[] { Box("B") }, BoxTarget("T1", Rectangle()), BoxTarget("T2", Rectangle(-3, -5, 7, 5), 2));
        Count(result, 0); Issue(result, "AmbiguousSource");
    }
    private static void JunctionSearchRadius()
    {
        StormStructureTarget target = BoxTarget("TB", Rectangle(-40, -40, 40, 40));
        Count(Match(new[] { Box("B", x: 25) }, target), 1);
        Count(Match(new[] { Box("B", x: 15, y: 20) }, target), 1);
        Count(Match(new[] { Box("B", x: 25.000001) }, target), 0);
        Count(Match(new[] { Box("B", x: 20, y: 20) }, target), 0);
    }
    private static void InvalidJunctionFootprints()
    {
        StormStructureVertex[][] invalid =
        {
            Array.Empty<StormStructureVertex>(),
            new[] { new StormStructureVertex(0, 0), new StormStructureVertex(1, 1) },
            new[] { new StormStructureVertex(0, 0), new StormStructureVertex(1, 1), new StormStructureVertex(2, 2) },
            new[] { new StormStructureVertex(0, 0), new StormStructureVertex(double.NaN, 1), new StormStructureVertex(1, 0) },
            new[] { new StormStructureVertex(0, 0), new StormStructureVertex(1, double.PositiveInfinity), new StormStructureVertex(1, 0) }
        };
        foreach (StormStructureVertex[] footprint in invalid)
        {
            var result = Match(new[] { Box("B") }, BoxTarget("TB", footprint));
            Count(result, 0); Issue(result, "InvalidFootprint");
            Check(!StormStructureMatching.ContainsFootprint(footprint, 0, 0));
        }
        var repeatedClosingVertex = Rectangle().Concat(new[] { new StormStructureVertex(-5, -5) }).ToArray();
        Count(Match(new[] { Box("B") }, BoxTarget("TB", repeatedClosingVertex)), 1);
    }
    private static void OtherRolesIgnoreFootprint()
    {
        foreach (StormStructureRole role in new[] { StormStructureRole.Access, StormStructureRole.DropInlet })
        {
            StormStructureSource source = role == StormStructureRole.Access ? Access("S", x: 1) : Di("S", x: 1);
            Count(Match(new[] { source }, new StormStructureTarget("T", role, 0, 0, Rectangle())), 0);
            Count(Match(new[] { source }, new StormStructureTarget("T", role, 1, 0, Rectangle())), 1);
            // Non-junction footprints are irrelevant, including an empty supplied list.
            Count(Match(new[] { source }, new StormStructureTarget("T", role, 1, 0, Array.Empty<StormStructureVertex>())), 1);
        }
    }
    private static void CenterOnlyJunctionTarget()
    {
        Count(Match(new[] { Box("B", x: 1) }, Target("TB", StormStructureRole.JunctionBox)), 0);
        Count(Match(new[] { Box("B", x: 0.1) }, Target("TB", StormStructureRole.JunctionBox)), 1);
    }
    private static void ConcaveFootprint()
    {
        var footprint = new[] { new StormStructureVertex(0, 0), new StormStructureVertex(4, 0), new StormStructureVertex(4, 1), new StormStructureVertex(1, 1), new StormStructureVertex(1, 4), new StormStructureVertex(0, 4) };
        foreach (StormStructureVertex[] ring in new[] { footprint, footprint.Reverse().ToArray() })
        {
            Count(Match(new[] { Box("B", x: 0.5, y: 3) }, BoxTarget("TB", ring, 2, 2)), 1);
            Count(Match(new[] { Box("B", x: 3, y: 3) }, BoxTarget("TB", ring, 2, 2)), 0);
        }
    }
    private static void SurveyCoordinateFootprint()
    {
        const double x = 800000, y = 20000000;
        StormStructureVertex[] footprint = Rectangle().Select(v => new StormStructureVertex(v.X + x, v.Y + y)).ToArray();
        Count(Match(new[] { Box("B", x: x + 3, y: y + 1) }, BoxTarget("TB", footprint, x, y)), 1);
        Check(StormStructureMatching.ContainsFootprint(footprint, x + 5, y));
        Check(!StormStructureMatching.ContainsFootprint(footprint, x + 5.001, y));
    }
    private static void InvalidContainmentQuery()
    {
        Check(!StormStructureMatching.ContainsFootprint(Rectangle(), double.NaN, 0));
        Check(!StormStructureMatching.ContainsFootprint(Rectangle(), 0, double.PositiveInfinity));
    }
    private static void NestedInnerOutlines()
    {
        var outer = new[] { new StormStructureVertex(-5, -5), new StormStructureVertex(5, -5), new StormStructureVertex(5.001, 5), new StormStructureVertex(-5.002, 5) };
        var skewInner = new[] { new StormStructureVertex(-2, -2), new StormStructureVertex(2.001, -2), new StormStructureVertex(2, 2), new StormStructureVertex(-2, 2.001) };
        Check(StormStructureMatching.ContainsOutline(outer, skewInner));
        Check(StormStructureMatching.ContainsOutline(outer, Rectangle(1, 1, 4, 4)));
        Check(StormStructureMatching.ContainsOutline(outer.Reverse().ToArray(), skewInner.Reverse().ToArray()));
    }
    private static void PartlyOutsideInnerOutline()
        => Check(!StormStructureMatching.ContainsOutline(Rectangle(), Rectangle(4, 4, 6, 6)));
    private static void CrossingConcaveInnerOutline()
    {
        var outer = new[] { new StormStructureVertex(0, 0), new StormStructureVertex(6, 0), new StormStructureVertex(6, 6), new StormStructureVertex(4, 6), new StormStructureVertex(4, 2), new StormStructureVertex(2, 2), new StormStructureVertex(2, 6), new StormStructureVertex(0, 6) };
        var crossing = new[] { new StormStructureVertex(1, 5), new StormStructureVertex(5, 5), new StormStructureVertex(3, 1) };
        Check(crossing.All(v => StormStructureMatching.ContainsFootprint(outer, v.X, v.Y)));
        Check(!StormStructureMatching.ContainsOutline(outer, crossing));
        // This edge leaves the polygon through boundary vertices, rather than two
        // proper edge-interior crossings; checking only proper crossings misses it.
        var vertexCrossing = new[] { new StormStructureVertex(2, 6), new StormStructureVertex(4, 6), new StormStructureVertex(3, 1) };
        Check(vertexCrossing.All(v => StormStructureMatching.ContainsFootprint(outer, v.X, v.Y)));
        Check(!StormStructureMatching.ContainsOutline(outer, vertexCrossing));
        Check(StormStructureMatching.ContainsOutline(outer, new[] { new StormStructureVertex(2, 2), new StormStructureVertex(4, 2), new StormStructureVertex(3, 1) }));
    }
    private static void IdenticalInnerOutline()
        => Check(StormStructureMatching.ContainsOutline(Rectangle(), Rectangle()));
    private static void InvalidOutlineContainment()
    {
        Check(!StormStructureMatching.ContainsOutline(Rectangle(), Array.Empty<StormStructureVertex>()));
        Check(!StormStructureMatching.ContainsOutline(Array.Empty<StormStructureVertex>(), Rectangle()));
        Check(!StormStructureMatching.ContainsOutline(Rectangle(), new[] { new StormStructureVertex(0, 0), new StormStructureVertex(1, 1), new StormStructureVertex(2, 2) }));
    }
    private static void SharedOutlineNdot()
    {
        var source = new StormStructureSource("D5C1D", "L24-00066-SDDI-06", "4.00 ' X 4.00 ' NDOT TYPE 2", 1, 0.5);
        var result = Match(new[] { source }, ExistingOutline("T", Rectangle(-2, -2, 2, 2)));
        Count(result, 1); Check(result.Issues.Count == 0);
        Check(result.Matches[0].Role == StormStructureRole.DropInlet);
    }
    private static void SharedOutlineBox()
    {
        var result = Match(new[] { Box("B", x: 3) }, ExistingOutline("T", Rectangle()));
        Count(result, 1); Check(result.Matches[0].Role == StormStructureRole.JunctionBox);
    }
    private static void SharedOutlineCrossRoleCompetition()
    {
        var result = Match(new[] { Di("D"), Box("B") }, ExistingOutline("T", Rectangle()));
        Count(result, 0); Issue(result, "AmbiguousTarget");
        StormStructureIssue issue = result.Issues.Single(i => i.Code == "AmbiguousTarget");
        Check(issue.SourceIds.Contains("D") && issue.SourceIds.Contains("B"));
    }
    private static void BoundExistingOutlineRoles()
    {
        var diTarget = ExistingOutline("DI", Rectangle(), StormStructureRole.DropInlet);
        var boxTarget = ExistingOutline("BOX", Rectangle(), StormStructureRole.JunctionBox);
        Count(Match(new[] { Di("D", x: 2) }, diTarget), 1);
        Count(Match(new[] { Box("B", x: 2) }, boxTarget), 1);
        Count(Match(new[] { Box("B") }, diTarget), 0);
        Count(Match(new[] { Di("D") }, boxTarget), 0);
    }
    private static void SharedOutlineRejectsOtherSources()
    {
        Count(Match(new[] { Access("A") }, ExistingOutline("T", Rectangle())), 0);
        var result = Match(new[] { NullPipeEnd() }, ExistingOutline("T", Rectangle()));
        Count(result, 0); Check(result.PreservedPipeEndSourceIds.Count == 1);
        result = Match(new[] { new StormStructureSource("U", "UNKNOWN-1", "", 0, 0) }, ExistingOutline("T", Rectangle()));
        Count(result, 0); Issue(result, "UnknownRole");
    }
    private static void InvalidExistingOutlineRoles()
    {
        foreach (StormStructureRole role in new[] { StormStructureRole.Access, StormStructureRole.NullPipeEnd, (StormStructureRole)99 })
        {
            var result = Match(new[] { Access("A"), NullPipeEnd() }, ExistingOutline("T", Rectangle(), role));
            Count(result, 0); Issue(result, "InvalidTarget");
        }
    }
    private static void OverlappingSharedOutlines()
    {
        var result = Match(new[] { Di("D") }, ExistingOutline("T1", Rectangle()), ExistingOutline("T2", Rectangle(-3, -5, 7, 5), x: 2));
        Count(result, 0); Issue(result, "AmbiguousSource");
    }
    private static void InvalidExistingOutlineFootprints()
    {
        foreach (IReadOnlyList<StormStructureVertex>? footprint in new IReadOnlyList<StormStructureVertex>?[]
        {
            null, Array.Empty<StormStructureVertex>(),
            new[] { new StormStructureVertex(0, 0), new StormStructureVertex(1, 1), new StormStructureVertex(2, 2) },
            new[] { new StormStructureVertex(0, 0), new StormStructureVertex(double.NaN, 1), new StormStructureVertex(1, 0) }
        })
        {
            var result = Match(new[] { Di("D") }, ExistingOutline("T", footprint));
            Count(result, 0); Issue(result, "InvalidFootprint");
        }
    }
    private static void ExistingOutlineBounds()
    {
        Count(Match(new[] { Di("D", x: 3) }, ExistingOutline("T", Rectangle())), 1);
        Count(Match(new[] { Di("D", x: 5.05) }, ExistingOutline("T", Rectangle())), 0);
        Count(Match(new[] { Di("D", x: 25) }, ExistingOutline("T", Rectangle(-40, -40, 40, 40))), 1);
        Count(Match(new[] { Di("D", x: 25.00001) }, ExistingOutline("T", Rectangle(-40, -40, 40, 40))), 0);
        // A supplied footprint alone still does not loosen ordinary DI block matching.
        Count(Match(new[] { Di("D", x: 3) }, new StormStructureTarget("BLOCK", StormStructureRole.DropInlet, 0, 0, Rectangle())), 0);
    }
    private static void UnflaggedUnknownTarget()
    {
        var result = Match(new[] { Di("D") }, new StormStructureTarget("T", StormStructureRole.Unknown, 0, 0, Rectangle()));
        Count(result, 0); Issue(result, "InvalidTarget");
    }
    private static void DuplicateSharedOutlineIds()
    {
        var result = Match(new[] { Di("D"), Box("B") },
            ExistingOutline("T", Rectangle(), StormStructureRole.DropInlet),
            ExistingOutline("t", Rectangle(), StormStructureRole.JunctionBox));
        Count(result, 0); Issue(result, "DuplicateTargetId");
    }
    private static void BlockAndOutlineCompetition()
    {
        var result = Match(new[] { Di("D") }, Target("BLOCK", StormStructureRole.DropInlet), ExistingOutline("OUTLINE", Rectangle()));
        Count(result, 0); Issue(result, "AmbiguousSource");
    }
    private static void CompletedMultiOutputClaim()
    {
        var plan = StormStructureMatching.PlanCompletions(new[] { new StormStructureCompletion("DI", new[] { "PRIMARY", "SECONDARY" }) });
        Check(plan.Accepted.Count == 1 && plan.ConflictedSourceIds.Count == 0 && plan.Issues.Count == 0);
        Check(plan.Accepted[0].OutputIds.SequenceEqual(new[] { "PRIMARY", "SECONDARY" }));
        Check(plan.Accepted.SelectMany(c => c.OutputIds).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 2);
    }
    private static void SecondaryCompletionOverlap()
    {
        var plan = StormStructureMatching.PlanCompletions(new[]
        {
            new StormStructureCompletion("DI1", new[] { "PRIMARY1", "SECONDARY" }),
            new StormStructureCompletion("DI2", new[] { "PRIMARY2", "SECONDARY" })
        });
        Check(plan.Accepted.Count == 0 && plan.ConflictedSourceIds.SequenceEqual(new[] { "DI1", "DI2" }));
        Check(plan.Issues.Any(i => i.Code == "CompletionOwnershipConflict" && i.TargetIds.Contains("SECONDARY")));
    }
    private static void DisjointCompletionClaims()
    {
        var plan = StormStructureMatching.PlanCompletions(new[]
        {
            new StormStructureCompletion("A", new[] { "A1", "A2" }),
            new StormStructureCompletion("B", new[] { "B1" })
        });
        Check(plan.Accepted.Count == 2 && plan.Issues.Count == 0);
        plan = StormStructureMatching.PlanCompletions(new[]
        {
            new StormStructureCompletion("A", new[] { "A1", "SHARED" }),
            new StormStructureCompletion("B", new[] { "B1", "SHARED" }),
            new StormStructureCompletion("C", new[] { "C1", "C2" })
        });
        Check(plan.Accepted.Count == 1 && plan.Accepted[0].SourceId == "C");
    }
    private static void DuplicateCompletionOutput()
    {
        var plan = StormStructureMatching.PlanCompletions(new[] { new StormStructureCompletion("DI", new[] { "ONE", "one" }) });
        Check(plan.Accepted.Count == 0 && plan.ConflictedSourceIds.SequenceEqual(new[] { "DI" }));
        Check(plan.Issues.Any(i => i.Code == "DuplicateCompletionOutput"));
    }
    private static void DuplicateCompletionSource()
    {
        var plan = StormStructureMatching.PlanCompletions(new[]
        {
            new StormStructureCompletion("DI", new[] { "ONE" }),
            new StormStructureCompletion("di", new[] { "TWO" })
        });
        Check(plan.Accepted.Count == 0 && plan.ConflictedSourceIds.Count == 1);
        Check(plan.Issues.Any(i => i.Code == "DuplicateCompletionSource"));
    }
    private static void CompletionOwnershipCase()
    {
        var plan = StormStructureMatching.PlanCompletions(new[]
        {
            new StormStructureCompletion("A", new[] { "One", "Two" }),
            new StormStructureCompletion("B", new[] { "two" })
        });
        Check(plan.Accepted.Count == 0 && plan.ConflictedSourceIds.Count == 2);
        Check(plan.Issues.Any(i => i.Code == "CompletionOwnershipConflict"));
    }
    private static void CompletionOrderIndependent()
    {
        var claims = new[]
        {
            new StormStructureCompletion("D", new[] { "D_PRIMARY", "D_SECONDARY" }),
            new StormStructureCompletion("A", new[] { "A_PRIMARY", "COMMON" }),
            new StormStructureCompletion("B", new[] { "B_PRIMARY", "common" }),
            new StormStructureCompletion("C", new[] { "REPEATED", "repeated" })
        };
        Check(CompletionSignature(StormStructureMatching.PlanCompletions(claims)) ==
            CompletionSignature(StormStructureMatching.PlanCompletions(claims.Reverse())));
    }
    private static void InvalidCompletionClaims()
    {
        foreach (StormStructureCompletion claim in new[]
        {
            new StormStructureCompletion("", new[] { "OUTPUT" }),
            new StormStructureCompletion("A", Array.Empty<string>()),
            new StormStructureCompletion("A", new[] { "OUTPUT", " " }),
            new StormStructureCompletion("A", null!)
        })
        {
            var plan = StormStructureMatching.PlanCompletions(new[] { claim });
            Check(plan.Accepted.Count == 0 && plan.Issues.Any(i => i.Code == "InvalidCompletion"));
            if (!string.IsNullOrWhiteSpace(claim.SourceId))
                Check(plan.ConflictedSourceIds.Contains(claim.SourceId));
        }
    }
    private static void InvalidCompletionOverlap()
    {
        var plan = StormStructureMatching.PlanCompletions(new[]
        {
            new StormStructureCompletion("A", new[] { "OUTPUT", "" }),
            new StormStructureCompletion("B", new[] { "OUTPUT" })
        });
        Check(plan.Accepted.Count == 0 && plan.ConflictedSourceIds.Count == 2);
        Check(plan.Issues.Any(i => i.Code == "InvalidCompletion"));
        Check(plan.Issues.Any(i => i.Code == "CompletionOwnershipConflict"));
    }
    private static void EmptyCompletions()
    {
        var plan = StormStructureMatching.PlanCompletions(Array.Empty<StormStructureCompletion>());
        Check(plan.Accepted.Count == 0 && plan.ConflictedSourceIds.Count == 0 && plan.Issues.Count == 0);
    }
    private static void CompletionOutputSnapshot()
    {
        var outputs = new[] { "PRIMARY", "SECONDARY" };
        var plan = StormStructureMatching.PlanCompletions(new[] { new StormStructureCompletion("A", outputs) });
        outputs[1] = "CHANGED";
        Check(plan.Accepted[0].OutputIds.SequenceEqual(new[] { "PRIMARY", "SECONDARY" }));
    }

    private sealed record VisibilityNode(string Name, bool Visible,
        IReadOnlyList<VisibilityNode>? Children = null, double? Radius = null);

    private static List<VisibilityNode> VisibleLeaves(VisibilityNode root)
    {
        var leaves = new List<VisibilityNode>();
        StormStructureVisibility.VisitVisible(root, n => n.Visible, n => n.Children, leaves.Add);
        return leaves;
    }

    private static void VisibilityManholeManifest()
    {
        // Read-only D5DE6 / evaluated *U481 evidence: Visibility1 = 60 MANHOLE.
        var root = new VisibilityNode("*U481", true, new[]
        {
            new VisibilityNode("48-inner", false, Radius: 2.0),
            new VisibilityNode("48-outer", false, Radius: 2.4167),
            new VisibilityNode("60-inner", true, Radius: 2.5),
            new VisibilityNode("60-outer", true, Radius: 3.0),
            new VisibilityNode("72-inner", false, Radius: 3.0),
            new VisibilityNode("72-outer", false, Radius: 3.5833)
        });
        List<VisibilityNode> leaves = VisibleLeaves(root);
        Check(leaves.Select(n => n.Name).SequenceEqual(new[] { "60-inner", "60-outer" }));
        Check(leaves.Select(n => n.Radius).SequenceEqual(new double?[] { 2.5, 3.0 }));

        // These 48/72 cases model changing the visible-state flags using the same
        // measured radii. Only the 60-inch state above was observed in the native
        // D5DE6 probe; these variations are traversal tests, not native-state proof.
        foreach ((int state, double innerRadius, double outerRadius) in new[]
        {
            (48, 2.0, 2.4167),
            (72, 3.0, 3.5833)
        })
        {
            VisibilityNode modeled = root with
            {
                Children = root.Children!.Select(n => n with
                {
                    Visible = n.Name.StartsWith(state + "-", StringComparison.Ordinal)
                }).ToArray()
            };
            leaves = VisibleLeaves(modeled);
            Check(leaves.Select(n => n.Name).SequenceEqual(new[] { state + "-inner", state + "-outer" }));
            Check(leaves.Select(n => n.Radius).SequenceEqual(new double?[] { innerRadius, outerRadius }));
        }
    }
    private static void VisibilityHiddenParent()
    {
        var hidden = new VisibilityNode("hidden-parent", false, new[] { new VisibilityNode("visible-child", true) });
        var root = new VisibilityNode("root", true, new[] { hidden, new VisibilityNode("sibling", true) });
        var checkedNames = new List<string>();
        var leaves = new List<string>();
        StormStructureVisibility.VisitVisible(root,
            n => { checkedNames.Add(n.Name); return n.Visible; },
            n => n.Name == "hidden-parent" ? throw new InvalidOperationException("Hidden parent was expanded") : n.Children,
            n => leaves.Add(n.Name));
        Check(!checkedNames.Contains("visible-child"));
        Check(leaves.SequenceEqual(new[] { "sibling" }));
    }
    private static void VisibilityHiddenRoot()
    {
        var root = new VisibilityNode("hidden", false, new[] { new VisibilityNode("child", true) });
        int leafCount = 0;
        StormStructureVisibility.VisitVisible(root, n => n.Visible,
            _ => throw new InvalidOperationException("Hidden root was expanded"), _ => leafCount++);
        Check(leafCount == 0);
    }
    private static void VisibilityAllHidden()
    {
        var root = new VisibilityNode("root", true, new[]
        {
            new VisibilityNode("A", false), new VisibilityNode("B", false), new VisibilityNode("C", false)
        });
        Check(VisibleLeaves(root).Count == 0);
    }
    private static void VisibilityEmptyContainer()
        => Check(VisibleLeaves(new VisibilityNode("empty", true, Array.Empty<VisibilityNode>())).Count == 0);
    private static void VisibilityCallbackFailures()
    {
        var root = new VisibilityNode("leaf", true);
        for (int stage = 0; stage < 3; stage++)
        {
            var expected = new InvalidOperationException("Callback " + stage);
            Exception? caught = null;
            try
            {
                StormStructureVisibility.VisitVisible(root,
                    n => stage == 0 ? throw expected : n.Visible,
                    n => stage == 1 ? throw expected : n.Children,
                    _ => { if (stage == 2) throw expected; });
            }
            catch (Exception ex) { caught = ex; }
            Check(ReferenceEquals(caught, expected), "Traversal swallowed or replaced callback failure");
        }
    }
    private static void VisibilityDepthBound()
    {
        VisibilityNode root = new VisibilityNode("leaf", true);
        for (int i = 1; i < StormStructureVisibility.MaximumDepth; i++)
            root = new VisibilityNode("level-" + i, true, new[] { root });
        Check(VisibleLeaves(root).Count == 1);
        root = new VisibilityNode("one-too-deep", true, new[] { root });
        bool threw = false;
        try { VisibleLeaves(root); }
        catch (InvalidOperationException) { threw = true; }
        Check(threw, "Excessive nesting was not bounded");
        threw = false;
        try { StormStructureVisibility.VisitVisible(0, _ => true, n => new[] { n }, _ => { }); }
        catch (InvalidOperationException) { threw = true; }
        Check(threw, "A visible cycle was not bounded");
    }
    private static void VisibilityOrdering()
    {
        var root = new VisibilityNode("root", true, new[]
        {
            new VisibilityNode("A", true),
            new VisibilityNode("branch", true, new[] { new VisibilityNode("B1", true), new VisibilityNode("B2", true) }),
            new VisibilityNode("C", true)
        });
        Check(VisibleLeaves(root).Select(n => n.Name).SequenceEqual(new[] { "A", "B1", "B2", "C" }));
    }
    private static void VisibilityNullCallbacks()
    {
        var root = new VisibilityNode("leaf", true);
        int callbacks = 0;
        Func<VisibilityNode, bool> visible = _ => { callbacks++; return true; };
        Func<VisibilityNode, IReadOnlyList<VisibilityNode>?> children = _ => { callbacks++; return null; };
        Action<VisibilityNode> visit = _ => callbacks++;
        int thrown = 0;
        try { StormStructureVisibility.VisitVisible(root, null!, children, visit); }
        catch (ArgumentNullException ex) { Check(ex.ParamName == "isVisible"); thrown++; }
        try { StormStructureVisibility.VisitVisible(root, visible, null!, visit); }
        catch (ArgumentNullException ex) { Check(ex.ParamName == "getChildren"); thrown++; }
        try { StormStructureVisibility.VisitVisible(root, visible, children, null!); }
        catch (ArgumentNullException ex) { Check(ex.ParamName == "visitLeaf"); thrown++; }
        Check(thrown == 3 && callbacks == 0);
    }
    private static void VisibilityLeaf()
        => Check(VisibleLeaves(new VisibilityNode("leaf", true)).Single().Name == "leaf");
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
