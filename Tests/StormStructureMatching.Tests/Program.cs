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
            ("Cleanup matches native DI and MH anchor fixtures", CleanupNativeAnchors),
            ("Cleanup separates coincident DI access box and null sources", CleanupCoincidentRoles),
            ("Unverified known-role source blocks marker ownership", CleanupUnverifiedCompetitor),
            ("Unknown-role source blocks marker ownership", CleanupUnknownCompetitor),
            ("Cleanup respects full completion ownership conflicts", CleanupOwnershipConflict),
            ("Malformed completion claims remain ownership competitors", CleanupMalformedClaimConflict),
            ("Cleanup rejects duplicate source and marker IDs", CleanupDuplicateIds),
            ("Live and archived duplicate names remain unresolved", CleanupDuplicateArchivedName),
            ("Ambiguous marker cohort remains intact", CleanupAmbiguousMarkers),
            ("Cleanup never erases wrong marker names or layers", CleanupMarkerIdentity),
            ("Cleanup preserves markers with OD or unreadable OD", CleanupProtectedMarkerOd),
            ("Cleanup marker tolerance remains strict", CleanupTolerance),
            ("Archived owner can clean only a recorded marker", CleanupArchivedRecordedMarker),
            ("Archived owner cannot clean a newly inserted marker", CleanupArchivedNewMarker),
            ("Archived owner still competes with live source", CleanupArchivedCompetitor),
            ("Invalid archived marker ID lists require review", CleanupInvalidArchiveMarkerIds),
            ("Source cleanup requires live verified completion geometry", CleanupMissingVerification),
            ("Cleanup preserves malformed sources and markers", CleanupMalformedInputs),
            ("Cleanup can archive a verified source without a marker", CleanupSourceWithoutMarker),
            ("Cleanup planning is deterministic and idempotent", CleanupOrderAndRerun),
            ("Verified owner IDs include live and archived completions", CleanupVerifiedOwners),
            ("Verified owner IDs exclude unresolved and ambiguous owners", CleanupExcludedOwners),
            ("Cleanup result constructor remains compatible", CleanupPlanCompatibility),
            ("Terminal trim handles both line directions", TerminalTrimDirections),
            ("Terminal trim handles skew box geometry", TerminalTrimSkew),
            ("Terminal trim deduplicates a proper corner crossing", TerminalTrimCorner),
            ("Pure corner tangency leaves pipe unchanged", TerminalTrimTangency),
            ("Outside and boundary-terminal pipes stay unchanged", TerminalTrimAlreadyOutside),
            ("Boundary-overlapping pipe requires review", TerminalTrimOverlap),
            ("Through and inside-only pipes require review", TerminalTrimThroughAndInside),
            ("Multiple terminal crossings require review", TerminalTrimMultipleCrossings),
            ("Terminal trim preserves multi-segment parameters", TerminalTrimMultiSegment),
            ("Boundary-to-inside pipe requires review", TerminalTrimBoundaryEndpoint),
            ("Terminal trim handles survey coordinates", TerminalTrimSurveyCoordinates),
            ("Terminal trim leaves inputs unchanged", TerminalTrimReadOnlyInputs),
            ("Terminal trim rerun is a no-op", TerminalTrimRerun),
            ("Terminal trim rejects invalid boundaries", TerminalTrimInvalidBoundaries),
            ("Terminal trim rejects invalid open paths", TerminalTrimInvalidPaths),
            ("Terminal trim rejects missing inputs", TerminalTrimMissingInputs),
            ("Terminal trim is independent of boundary winding", TerminalTrimBoundaryWinding),
            ("Terminal trim rejects an extra tangent contact", TerminalTrimExtraContact),
            ("Outward gap probe detects true interior ahead", TerminalGapAhead),
            ("Outward gap probe ignores a box behind the endpoint", TerminalGapBehind),
            ("Outward gap probe ignores a parallel miss", TerminalGapParallel),
            ("Outward gap probe distinguishes tangent and entering corners", TerminalGapCorners),
            ("Boundary-only outward overlap is clear", TerminalGapEdgeOnly),
            ("Gap probe detects interior following concave edge overlap", TerminalGapAfterOverlap),
            ("Gap probe requires a strictly exterior endpoint", TerminalGapExteriorEndpoint),
            ("Gap probe requires interior before the distance bound", TerminalGapDistanceBound),
            ("Gap probe detects concave multiple interior intervals", TerminalGapConcave),
            ("Gap search distance is independent of neighbor length", TerminalGapNormalizedDirection),
            ("Gap probe rejects malformed vectors and limits", TerminalGapInvalidInputs),
            ("Gap probe remains stable at survey coordinates", TerminalGapSurveyCoordinates),
            ("Gap probe preserves its input boundary", TerminalGapReadOnly),
            ("Endpoint containment includes boundary and excludes invalid data", TerminalGapContainsEndpoint),
            ("Pipe size rule uses exact 12-inch threshold in feet", PipeDiameterThreshold),
            ("Pipe size rule rejects invalid numeric diameters", PipeInvalidDiameters),
            ("Single-line pipe ownership must have no offset walls", PipeSingleLineOwnership),
            ("Two-wall pipe ownership requires both distinct sides", PipeTwoWallOwnership),
            ("Pipe walls cannot share IDs or reuse source ID", PipeDuplicateOwnership),
            ("Pipe ownership rejects missing source or sides", PipeMissingOwnership),
            ("Pipe layer hints stay distinct from native OD proof", PipeSourceLayerHints),
            ("Pipe utility rules identify sewer exclusions", PipeSewerNames),
            ("Sewer-only pipe evidence is excluded", PipeUtilitySewerOnly),
            ("Mixed storm and sewer pipe evidence requires review", PipeUtilityConflicts),
            ("Storm and generic utility candidates preserve OD eligibility", PipeUtilityCandidates),
            ("Storm utility evidence uses exact documented tokens", PipeUtilityExactEvidence),
            ("Incomplete utility metadata requires review", PipeUtilityMalformed),
            ("Utility classification is culture and order invariant", PipeUtilityCultureAndOrder),
            ("Equivalent pipe completion records support no-op readback", PipeCompletionNoOp),
            ("Changed pipe source metadata invalidates completion equality", PipeCompletionSourceChanges),
            ("Changed pipe wall metadata invalidates completion equality", PipeCompletionWallChanges),
            ("Pipe completion comparison preserves side and output order", PipeCompletionSideOrder),
            ("Pipe completion comparison handles absent records", PipeCompletionNulls),
            ("Null-terminal exemption binds both wall sides and source ends", OpenNullSidesAndTerminals),
            ("Null-terminal correspondence survives path reversal", OpenNullReversedPaths),
            ("Null-terminal exemption retains size and radial tolerances", OpenNullThresholdAndRadius),
            ("Null-terminal exemption rejects wrong inward tangents", OpenNullWrongTangent),
            ("Null-terminal exemption rejects wrong offset width", OpenNullWrongWidth),
            ("Null-terminal exemption rejects middle or split endpoints", OpenNullMiddleSplit),
            ("Null-terminal exemption rejects ambiguous original endpoints", OpenNullAmbiguousTerminals),
            ("Physical or unknown nearby anchors veto null exemption", OpenNullCompetingAnchors),
            ("Global duplicate null identities veto exemption", OpenNullDuplicateIdentities),
            ("Null-terminal exemption rejects invalid path geometry", OpenNullInvalidGeometry),
            ("Unlocatable structure anchors veto null exemption", OpenNullUnlocatableAnchors),
            ("Null-terminal exemption requires exact verified null convention", OpenNullStrictConvention),
            ("Null-terminal correspondence preserves survey data", OpenNullSurveyAndReadOnly),
            ("Physical containing footprint vetoes open null terminal", OpenNullPhysicalInside),
            ("Physical boundary contact vetoes open null terminal", OpenNullPhysicalBoundary),
            ("Outside physical footprints retain valid null proof", OpenNullPhysicalOutside),
            ("Invalid physical footprints veto null exemption", OpenNullPhysicalInvalid),
            ("Fingerprint golden vectors match canonical format", FingerprintGoldenVectors),
            ("Fingerprint ignores record order but preserves multiplicity", FingerprintMultiset),
            ("Fingerprint does not mutate or normalize keys", FingerprintExactKeys),
            ("Fingerprint format is culture invariant", FingerprintCulture),
            ("Fingerprint safely delimits Unicode and record lengths", FingerprintFraming),
            ("Fingerprint rejects nulls and malformed Unicode", FingerprintInvalidInputs),
            ("Fingerprint syntax validation rejects invalid versions", FingerprintValidation),
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

    private static StormCleanupSource CleanupOwner(StormStructureSource source, bool verified = true,
        StormCleanupSourceState state = StormCleanupSourceState.LiveImportedPoint, IReadOnlyList<string>? archivedMarkers = null)
        => new(source, state, verified, archivedMarkers);
    private static StormCleanupMarker Marker(string id, string name = "UFLS_DI_MARK", double x = 0, double y = 0,
        string layer = "V-SURV-CHCK", StormMarkerOdState od = StormMarkerOdState.Empty)
        => new(id, name, layer, x, y, od);
    private static StormCleanupPlan Cleanup(StormCleanupSource[] sources, StormCleanupMarker[] markers,
        IEnumerable<StormStructureCompletion>? completions = null)
        => StormStructureCleanup.Plan(sources, completions ?? sources
            .Where(s => StormStructureMatching.Classify(s.Source.Name, s.Source.PartSizeName) is not (StormStructureRole.Unknown or StormStructureRole.NullPipeEnd))
            .Select(s => new StormStructureCompletion(s.Source.Id, new[] { "OUT-" + s.Source.Id })), markers);
    private static string CleanupSignature(StormCleanupPlan plan)
        => string.Join(",", plan.SourceIdsToArchiveAndErase) + ";" +
            string.Join(",", plan.MarkerMatches.Select(m => m.SourceId + ">" + m.MarkerId)) + ";" +
            string.Join("|", plan.Issues.Select(i => $"{i.Code}:{string.Join(",", i.SourceIds)}:{string.Join(",", i.TargetIds)}")) + ";" +
            string.Join(",", plan.VerifiedOwnerIds);
    private static void CleanupNativeAnchors()
    {
        // Native evidence supplies these handles/coordinates; helper role descriptions
        // are synthetic test metadata, not a claim about the fixture's actual OD text.
        var sources = new[]
        {
            CleanupOwner(Di("D5C17", x: 867280.7594204966, y: 1295717.498120421)),
            CleanupOwner(Access("D5C1A", x: 867285.6256335936, y: 1295711.003371928))
        };
        var plan = Cleanup(sources, new[]
        {
            Marker("D5DE5", x: 867280.767, y: 1295717.502),
            Marker("D5DED", "UFLS_MH_MARK", 867285.6256335936, 1295711.003371928)
        });
        Check(plan.SourceIdsToArchiveAndErase.Count == 2 && plan.MarkerMatches.Count == 2 && plan.Issues.Count == 0);
        Check(plan.MarkerMatches.Single(m => m.SourceId == "D5C17").MarkerId == "D5DE5");
    }
    private static void CleanupCoincidentRoles()
    {
        var plan = Cleanup(new[] { CleanupOwner(Di("D")), CleanupOwner(Access("A")), CleanupOwner(Box("B")), CleanupOwner(NullPipeEnd()) },
            new[] { Marker("DI"), Marker("MH", "UFLS_MH_MARK") });
        Check(plan.SourceIdsToArchiveAndErase.Count == 3 && !plan.SourceIdsToArchiveAndErase.Contains("D5C4A"));
        Check(plan.MarkerMatches.Count == 2 && plan.MarkerMatches.Single(m => m.MarkerId == "DI").SourceId == "D");
        Check(plan.MarkerMatches.Single(m => m.MarkerId == "MH").SourceId == "A");
    }
    private static void CleanupUnverifiedCompetitor()
    {
        var plan = Cleanup(new[] { CleanupOwner(Di("D1")), CleanupOwner(Di("D2", "DI-2", 0.03), false) }, new[] { Marker("M") });
        Check(plan.SourceIdsToArchiveAndErase.Count == 0 && plan.MarkerMatches.Count == 0);
        Check(plan.Issues.Any(i => i.Code == "AmbiguousCleanupOwner"));
    }
    private static void CleanupUnknownCompetitor()
    {
        var unknown = new StormStructureSource("U", "UNRESOLVED", "Unknown family", 0.03, 0);
        var plan = Cleanup(new[] { CleanupOwner(Di("D")), CleanupOwner(unknown, false) }, new[] { Marker("M") });
        Check(plan.SourceIdsToArchiveAndErase.Count == 0 && plan.MarkerMatches.Count == 0);
        Check(plan.Issues.Any(i => i.Code == "UnknownCleanupRole") && plan.Issues.Any(i => i.Code == "AmbiguousCleanupOwner"));
    }
    private static void CleanupOwnershipConflict()
    {
        var plan = Cleanup(new[] { CleanupOwner(Di("D1")), CleanupOwner(Di("D2", "DI-2", 10), false) }, new[] { Marker("M") },
            new[] { new StormStructureCompletion("D1", new[] { "PRIMARY", "SHARED" }), new StormStructureCompletion("D2", new[] { "SHARED" }) });
        Check(plan.SourceIdsToArchiveAndErase.Count == 0 && plan.MarkerMatches.Count == 0);
        Check(plan.Issues.Any(i => i.Code == "CompletionOwnershipConflict"));
    }
    private static void CleanupMalformedClaimConflict()
    {
        var plan = Cleanup(new[] { CleanupOwner(Di("D1")), CleanupOwner(Di("D2", "DI-2", 10), false) }, new[] { Marker("M") },
            new[] { new StormStructureCompletion("D1", new[] { "SHARED" }), new StormStructureCompletion("D2", new[] { "SHARED", "" }) });
        Check(plan.SourceIdsToArchiveAndErase.Count == 0 && plan.MarkerMatches.Count == 0);
        Check(plan.Issues.Any(i => i.Code == "InvalidCompletion"));
    }
    private static void CleanupDuplicateIds()
    {
        var plan = Cleanup(new[] { CleanupOwner(Di("D")), CleanupOwner(Di("d", "DI-2")) }, new[] { Marker("M") });
        Check(plan.SourceIdsToArchiveAndErase.Count == 0 && plan.MarkerMatches.Count == 0);
        Check(plan.Issues.Any(i => i.Code == "DuplicateCleanupSourceId"));
        plan = Cleanup(new[] { CleanupOwner(Di("D")) }, new[] { Marker("M"), Marker("m") });
        Check(plan.SourceIdsToArchiveAndErase.Count == 0 && plan.MarkerMatches.Count == 0);
        Check(plan.Issues.Any(i => i.Code == "DuplicateCleanupMarkerId"));
    }
    private static void CleanupDuplicateArchivedName()
    {
        var plan = Cleanup(new[]
        {
            CleanupOwner(Di("LIVE", "DI-1")),
            CleanupOwner(Di("OLD", " di-1 "), state: StormCleanupSourceState.ArchivedCompletion, archivedMarkers: new[] { "M" })
        }, new[] { Marker("M") });
        Check(plan.SourceIdsToArchiveAndErase.Count == 0 && plan.MarkerMatches.Count == 0);
        Check(plan.Issues.Any(i => i.Code == "DuplicateCleanupIdentity"));
    }
    private static void CleanupAmbiguousMarkers()
    {
        var plan = Cleanup(new[] { CleanupOwner(Di("D")) }, new[] { Marker("M1"), Marker("M2", x: 0.04) });
        Check(plan.SourceIdsToArchiveAndErase.Count == 0 && plan.MarkerMatches.Count == 0);
        Check(plan.Issues.Any(i => i.Code == "AmbiguousCleanupMarkers"));
    }
    private static void CleanupMarkerIdentity()
    {
        foreach (StormCleanupMarker marker in new[]
        {
            Marker("M", "UFLS_DI_MARK_COPY"), Marker("M", "UNKNOWN"),
            Marker("M", layer: "V-SURV-CHCK~~"), Marker("M", layer: "OTHER")
        })
        {
            var plan = Cleanup(new[] { CleanupOwner(Di("D")) }, new[] { marker });
            Check(plan.MarkerMatches.Count == 0 && plan.Issues.Any(i => i.Code == "IneligibleCleanupMarker"));
        }
        Check(Cleanup(new[] { CleanupOwner(Di("D")) }, new[] { Marker("M", "ufls_di_mark", layer: "v-surv-chck") }).MarkerMatches.Count == 1);
    }
    private static void CleanupProtectedMarkerOd()
    {
        foreach (StormMarkerOdState od in new[] { StormMarkerOdState.Present, StormMarkerOdState.Unreadable, (StormMarkerOdState)99 })
        {
            var plan = Cleanup(new[] { CleanupOwner(Di("D")) }, new[] { Marker("M", od: od) });
            Check(plan.MarkerMatches.Count == 0 && plan.Issues.Any(i => i.Code == "MarkerObjectDataProtected"));
        }
        var duplicateCandidate = Cleanup(new[] { CleanupOwner(Di("D")) }, new[] { Marker("M1"), Marker("M2", od: StormMarkerOdState.Present) });
        Check(duplicateCandidate.SourceIdsToArchiveAndErase.Count == 0 && duplicateCandidate.MarkerMatches.Count == 0);
    }
    private static void CleanupTolerance()
    {
        Check(Cleanup(new[] { CleanupOwner(Di("D")) }, new[] { Marker("M", x: 0.10) }).MarkerMatches.Count == 1);
        Check(Cleanup(new[] { CleanupOwner(Di("D")) }, new[] { Marker("M", x: 0.10001) }).MarkerMatches.Count == 0);
        Check(Cleanup(new[] { CleanupOwner(Di("D")) }, new[] { Marker("M", x: 0.08, y: 0.08) }).MarkerMatches.Count == 0);
    }
    private static void CleanupArchivedRecordedMarker()
    {
        var plan = Cleanup(new[] { CleanupOwner(Di("OLD"), state: StormCleanupSourceState.ArchivedCompletion, archivedMarkers: new[] { "m" }) }, new[] { Marker("M") });
        Check(plan.SourceIdsToArchiveAndErase.Count == 0 && plan.MarkerMatches.Count == 1);
    }
    private static void CleanupArchivedNewMarker()
    {
        foreach (IReadOnlyList<string>? recorded in new IReadOnlyList<string>?[] { null, Array.Empty<string>(), new[] { "ORIGINAL" } })
        {
            var plan = Cleanup(new[] { CleanupOwner(Di("OLD"), state: StormCleanupSourceState.ArchivedCompletion, archivedMarkers: recorded) }, new[] { Marker("NEW") });
            Check(plan.SourceIdsToArchiveAndErase.Count == 0 && plan.MarkerMatches.Count == 0);
            Check(plan.Issues.Any(i => i.Code == "UnrecordedArchivedMarker"));
        }
    }
    private static void CleanupArchivedCompetitor()
    {
        var plan = Cleanup(new[]
        {
            CleanupOwner(Di("LIVE", "DI-NEW")),
            CleanupOwner(Di("OLD", "DI-OLD"), state: StormCleanupSourceState.ArchivedCompletion, archivedMarkers: Array.Empty<string>())
        }, new[] { Marker("NEW") });
        Check(plan.SourceIdsToArchiveAndErase.Count == 0 && plan.MarkerMatches.Count == 0);
    }
    private static void CleanupInvalidArchiveMarkerIds()
    {
        foreach (string[] recorded in new[] { new[] { "M", "m" }, new[] { "M", "" } })
        {
            var plan = Cleanup(new[] { CleanupOwner(Di("OLD"), state: StormCleanupSourceState.ArchivedCompletion, archivedMarkers: recorded) }, new[] { Marker("M") });
            Check(plan.MarkerMatches.Count == 0 && plan.Issues.Any(i => i.Code == "InvalidArchivedMarkerIds"));
        }
    }
    private static void CleanupMissingVerification()
    {
        var owner = CleanupOwner(Di("D"));
        var plan = Cleanup(new[] { owner }, new[] { Marker("M") }, Array.Empty<StormStructureCompletion>());
        Check(plan.SourceIdsToArchiveAndErase.Count == 0 && plan.MarkerMatches.Count == 0);
        plan = Cleanup(new[] { owner }, new[] { Marker("M") }, new[] { new StormStructureCompletion("D", Array.Empty<string>()) });
        Check(plan.SourceIdsToArchiveAndErase.Count == 0 && plan.MarkerMatches.Count == 0);
        plan = Cleanup(new[] { CleanupOwner(Di("D"), false, StormCleanupSourceState.ArchivedCompletion, new[] { "M" }) }, new[] { Marker("M") });
        Check(plan.SourceIdsToArchiveAndErase.Count == 0 && plan.MarkerMatches.Count == 0);
        plan = Cleanup(new[] { CleanupOwner(NullPipeEnd()) }, Array.Empty<StormCleanupMarker>());
        Check(plan.SourceIdsToArchiveAndErase.Count == 0 && plan.MarkerMatches.Count == 0 && plan.Issues.Count == 0);
    }
    private static void CleanupMalformedInputs()
    {
        foreach (StormCleanupSource owner in new[] { CleanupOwner(Di("")), CleanupOwner(Di("D", x: double.NaN)), CleanupOwner(Di("D"), state: (StormCleanupSourceState)99) })
        {
            var plan = Cleanup(new[] { owner }, new[] { Marker("M") });
            Check(plan.SourceIdsToArchiveAndErase.Count == 0 && plan.MarkerMatches.Count == 0);
        }
        foreach (StormCleanupMarker marker in new[] { Marker(""), Marker("M", x: double.NaN), Marker("M", y: double.PositiveInfinity) })
            Check(Cleanup(new[] { CleanupOwner(Di("D")) }, new[] { marker }).MarkerMatches.Count == 0);
    }
    private static void CleanupSourceWithoutMarker()
    {
        var plan = Cleanup(new[] { CleanupOwner(Di("D")), CleanupOwner(Box("B")) }, Array.Empty<StormCleanupMarker>());
        Check(plan.SourceIdsToArchiveAndErase.Count == 2 && plan.MarkerMatches.Count == 0 && plan.Issues.Count == 0);
    }
    private static void CleanupOrderAndRerun()
    {
        var sources = new[] { CleanupOwner(Di("D")), CleanupOwner(Access("A", x: 10)) };
        var markers = new[] { Marker("MD"), Marker("MA", "UFLS_MH_MARK", 10) };
        Check(CleanupSignature(Cleanup(sources, markers)) == CleanupSignature(Cleanup(sources.Reverse().ToArray(), markers.Reverse().ToArray())));
        var archived = new[]
        {
            CleanupOwner(Di("D"), state: StormCleanupSourceState.ArchivedCompletion, archivedMarkers: new[] { "MD" }),
            CleanupOwner(Access("A", x: 10), state: StormCleanupSourceState.ArchivedCompletion, archivedMarkers: new[] { "MA" })
        };
        var rerun = Cleanup(archived, Array.Empty<StormCleanupMarker>());
        Check(rerun.SourceIdsToArchiveAndErase.Count == 0 && rerun.MarkerMatches.Count == 0 && rerun.Issues.Count == 0);
    }
    private static void CleanupVerifiedOwners()
    {
        var sources = new[]
        {
            CleanupOwner(Di("LIVE")),
            CleanupOwner(Box("ARCHIVED", "SD-2-JS", 10), state: StormCleanupSourceState.ArchivedCompletion)
        };
        var plan = Cleanup(sources, Array.Empty<StormCleanupMarker>());
        Check(plan.VerifiedOwnerIds.SequenceEqual(new[] { "ARCHIVED", "LIVE" }));
        Check(plan.SourceIdsToArchiveAndErase.SequenceEqual(new[] { "LIVE" }));
        Check(plan.MarkerMatches.Count == 0 && plan.Issues.Count == 0);
    }
    private static void CleanupExcludedOwners()
    {
        var cases = new[]
        {
            Cleanup(new[] { CleanupOwner(Di("A")), CleanupOwner(Di("B", " di-1 "), state: StormCleanupSourceState.ArchivedCompletion) }, Array.Empty<StormCleanupMarker>()),
            Cleanup(new[] { CleanupOwner(Di("A"), false) }, Array.Empty<StormCleanupMarker>()),
            Cleanup(new[] { CleanupOwner(new StormStructureSource("U", "UNKNOWN", "", 0, 0)) }, Array.Empty<StormCleanupMarker>()),
            Cleanup(new[] { CleanupOwner(NullPipeEnd()) }, Array.Empty<StormCleanupMarker>()),
            Cleanup(new[] { CleanupOwner(Di("A")) }, new[] { Marker("M1"), Marker("M2") }),
            Cleanup(new[] { CleanupOwner(Di("A")) }, Array.Empty<StormCleanupMarker>(), Array.Empty<StormStructureCompletion>()),
            Cleanup(new[] { CleanupOwner(Di("A")), CleanupOwner(Di("B", "DI-2", 10)) }, Array.Empty<StormCleanupMarker>(),
                new[] { new StormStructureCompletion("A", new[] { "SHARED" }), new StormStructureCompletion("B", new[] { "SHARED" }) })
        };
        foreach (StormCleanupPlan plan in cases)
            Check(plan.VerifiedOwnerIds.Count == 0 && plan.SourceIdsToArchiveAndErase.Count == 0);
    }
    private static void CleanupPlanCompatibility()
        => Check(new StormCleanupPlan(Array.Empty<string>(), Array.Empty<StormCleanupMarkerMatch>(), Array.Empty<StormStructureIssue>()).VerifiedOwnerIds.Count == 0);

    private static StormStructureVertex[] TrimSquare() => Rectangle(-1, -1, 1, 1);
    private static StormStructureVertex[] TrimPath(params (double X, double Y)[] points)
        => points.Select(p => new StormStructureVertex(p.X, p.Y)).ToArray();
    private static void TrimDecision(StormTerminalTrimDecision decision, StormTerminalTrimKind kind, double start = 0, double end = 0)
    {
        Check(decision.Kind == kind, $"Expected {kind}; got {decision.Kind}: {decision.Reason}");
        Check(Math.Abs(decision.StartParameter - start) <= 1e-8 && Math.Abs(decision.EndParameter - end) <= 1e-8,
            $"Expected retained parameters {start}..{end}; got {decision.StartParameter}..{decision.EndParameter}");
        Check(!string.IsNullOrWhiteSpace(decision.Reason));
    }
    private static void TerminalTrimDirections()
    {
        TrimDecision(StormTerminalTrim.Plan(TrimSquare(), TrimPath((0, 0), (2, 0))), StormTerminalTrimKind.Trimmed, 0.5, 1);
        TrimDecision(StormTerminalTrim.Plan(TrimSquare(), TrimPath((2, 0), (0, 0))), StormTerminalTrimKind.Trimmed, 0, 0.5);
    }
    private static void TerminalTrimSkew()
    {
        var boundary = TrimPath((0, 0), (4, 0.001), (4.2, 2), (0.1, 2.001));
        double crossingX = 4 + 0.2 * ((1 - 0.001) / (2 - 0.001));
        double expected = (crossingX - 2) / 4;
        TrimDecision(StormTerminalTrim.Plan(boundary, TrimPath((2, 1), (6, 1))), StormTerminalTrimKind.Trimmed, expected, 1);
        TrimDecision(StormTerminalTrim.Plan(boundary, TrimPath((6, 1), (2, 1))), StormTerminalTrimKind.Trimmed, 0, 1 - expected);
    }
    private static void TerminalTrimCorner()
        => TrimDecision(StormTerminalTrim.Plan(TrimSquare(), TrimPath((0, 0), (2, 2))), StormTerminalTrimKind.Trimmed, 0.5, 1);
    private static void TerminalTrimTangency()
        => TrimDecision(StormTerminalTrim.Plan(TrimSquare(), TrimPath((0, 2), (2, 0))), StormTerminalTrimKind.Unchanged, 0, 1);
    private static void TerminalTrimAlreadyOutside()
    {
        foreach (StormStructureVertex[] path in new[]
        {
            TrimPath((2, 2), (3, 3)), TrimPath((1, 0), (2, 0)), TrimPath((2, 0), (1, 0)),
            TrimPath((1, 1), (2, 2))
        })
            TrimDecision(StormTerminalTrim.Plan(TrimSquare(), path), StormTerminalTrimKind.Unchanged, 0, 1);
    }
    private static void TerminalTrimOverlap()
        => TrimDecision(StormTerminalTrim.Plan(TrimSquare(), TrimPath((-2, 1), (2, 1))), StormTerminalTrimKind.Review);
    private static void TerminalTrimThroughAndInside()
    {
        TrimDecision(StormTerminalTrim.Plan(TrimSquare(), TrimPath((-2, 0), (2, 0))), StormTerminalTrimKind.Review);
        TrimDecision(StormTerminalTrim.Plan(TrimSquare(), TrimPath((0, 0), (0.5, 0))), StormTerminalTrimKind.Review);
    }
    private static void TerminalTrimMultipleCrossings()
    {
        var uShape = TrimPath((0, 0), (6, 0), (6, 6), (4, 6), (4, 2), (2, 2), (2, 6), (0, 6));
        TrimDecision(StormTerminalTrim.Plan(uShape, TrimPath((1, 5), (7, 5))), StormTerminalTrimKind.Review);
    }
    private static void TerminalTrimMultiSegment()
    {
        TrimDecision(StormTerminalTrim.Plan(TrimSquare(), TrimPath((0, 0), (0.5, 0), (2, 0))), StormTerminalTrimKind.Trimmed, 1 + 1.0 / 3, 2);
        TrimDecision(StormTerminalTrim.Plan(TrimSquare(), TrimPath((2, 0), (0.5, 0), (0, 0))), StormTerminalTrimKind.Trimmed, 0, 2.0 / 3);
        TrimDecision(StormTerminalTrim.Plan(TrimSquare(), TrimPath((0, 0), (2, 0), (2, 3))), StormTerminalTrimKind.Trimmed, 0.5, 2);
        TrimDecision(StormTerminalTrim.Plan(TrimSquare(), TrimPath((0, 0), (1, 0), (2, 0))), StormTerminalTrimKind.Trimmed, 1, 2);
    }
    private static void TerminalTrimBoundaryEndpoint()
    {
        TrimDecision(StormTerminalTrim.Plan(TrimSquare(), TrimPath((1, 0), (0, 0))), StormTerminalTrimKind.Review);
        TrimDecision(StormTerminalTrim.Plan(TrimSquare(), TrimPath((0, 0), (1, 0))), StormTerminalTrimKind.Review);
    }
    private static void TerminalTrimSurveyCoordinates()
    {
        const double x = 867285.6256335936, y = 1295711.003371928;
        var boundary = TrimSquare().Select(p => new StormStructureVertex(p.X + x, p.Y + y)).ToArray();
        TrimDecision(StormTerminalTrim.Plan(boundary, TrimPath((x, y), (x + 2, y))), StormTerminalTrimKind.Trimmed, 0.5, 1);
    }
    private static void TerminalTrimReadOnlyInputs()
    {
        var boundary = TrimSquare().ToList();
        var path = TrimPath((0, 0), (2, 0)).ToList();
        var boundaryBefore = boundary.ToArray();
        var pathBefore = path.ToArray();
        StormTerminalTrimDecision decision = StormTerminalTrim.Plan(boundary, path);
        Check(boundary.SequenceEqual(boundaryBefore) && path.SequenceEqual(pathBefore));
        boundary[0] = new StormStructureVertex(-100, -100);
        path[0] = new StormStructureVertex(100, 100);
        TrimDecision(decision, StormTerminalTrimKind.Trimmed, 0.5, 1);
    }
    private static void TerminalTrimRerun()
    {
        var original = TrimPath((0, 0), (2, 0), (2, 3));
        var first = StormTerminalTrim.Plan(TrimSquare(), original);
        var again = StormTerminalTrim.Plan(TrimSquare(), original);
        Check(first == again);
        TrimDecision(first, StormTerminalTrimKind.Trimmed, 0.5, 2);
        TrimDecision(StormTerminalTrim.Plan(TrimSquare(), TrimPath((1, 0), (2, 0), (2, 3))), StormTerminalTrimKind.Unchanged, 0, 2);
    }
    private static void TerminalTrimInvalidBoundaries()
    {
        foreach (StormStructureVertex[] boundary in new[]
        {
            Array.Empty<StormStructureVertex>(), TrimPath((0, 0), (1, 0)),
            TrimPath((0, 0), (1, 1), (2, 2)), TrimPath((-1, -1), (1, 1), (-1, 1), (1, -1)),
            TrimPath((-1, -1), (1, -1), (double.NaN, 1), (-1, 1)),
            TrimPath((-1, -1), (1, -1), (1, 1), (-1, 1), (-1, -1))
        })
            TrimDecision(StormTerminalTrim.Plan(boundary, TrimPath((0, 0), (2, 0))), StormTerminalTrimKind.Review);
    }
    private static void TerminalTrimInvalidPaths()
    {
        foreach (StormStructureVertex[] path in new[]
        {
            Array.Empty<StormStructureVertex>(), TrimPath((0, 0)), TrimPath((0, 0), (0, 0)),
            TrimPath((0, 0), (1e-9, 0)), TrimPath((0, 0), (double.PositiveInfinity, 0)),
            TrimPath((0, 0), (2, 0), (1.5, 0), (3, 0)),
            TrimPath((0, 0), (2, 2), (0, 2), (2, 0)),
            TrimPath((0, 0), (2, 0), (2, 2), (0, 0))
        })
            TrimDecision(StormTerminalTrim.Plan(TrimSquare(), path), StormTerminalTrimKind.Review);
    }
    private static void TerminalTrimMissingInputs()
    {
        TrimDecision(StormTerminalTrim.Plan(null!, TrimPath((0, 0), (2, 0))), StormTerminalTrimKind.Review);
        TrimDecision(StormTerminalTrim.Plan(TrimSquare(), null!), StormTerminalTrimKind.Review);
    }
    private static void TerminalTrimBoundaryWinding()
    {
        var path = TrimPath((0, 0), (2, 0));
        Check(StormTerminalTrim.Plan(TrimSquare(), path) == StormTerminalTrim.Plan(TrimSquare().Reverse().ToArray(), path));
    }
    private static void TerminalTrimExtraContact()
        => TrimDecision(StormTerminalTrim.Plan(TrimSquare(), TrimPath((0, 0), (2, 0), (3, 2), (1, 1), (0, 2))), StormTerminalTrimKind.Review);

    private static void GapDecision(StormTerminalGapProbe decision, StormTerminalGapKind kind)
    {
        Check(decision.Kind == kind, $"Expected {kind}; got {decision.Kind}: {decision.Reason}");
        Check(!string.IsNullOrWhiteSpace(decision.Reason));
    }
    private static void TerminalGapAhead()
    {
        var endpoint = new StormStructureVertex(-2, 0);
        var neighbor = new StormStructureVertex(-3, 0);
        GapDecision(StormTerminalTrim.ProbeTerminalGap(TrimSquare(), endpoint, neighbor, 2), StormTerminalGapKind.Gap);
        GapDecision(StormTerminalTrim.ProbeTerminalGap(TrimSquare(), endpoint, neighbor, 5), StormTerminalGapKind.Gap);
    }
    private static void TerminalGapBehind()
        => GapDecision(StormTerminalTrim.ProbeTerminalGap(TrimSquare(), new(-2, 0), new(-1.5, 0), 5), StormTerminalGapKind.Clear);
    private static void TerminalGapParallel()
        => GapDecision(StormTerminalTrim.ProbeTerminalGap(TrimSquare(), new(-2, 2), new(-3, 2), 5), StormTerminalGapKind.Clear);
    private static void TerminalGapCorners()
    {
        GapDecision(StormTerminalTrim.ProbeTerminalGap(TrimSquare(), new(-2, 0), new(-3, -1), 5), StormTerminalGapKind.Clear);
        GapDecision(StormTerminalTrim.ProbeTerminalGap(TrimSquare(), new(-2, -2), new(-3, -3), 5), StormTerminalGapKind.Gap);
    }
    private static void TerminalGapEdgeOnly()
        => GapDecision(StormTerminalTrim.ProbeTerminalGap(TrimSquare(), new(-2, 1), new(-3, 1), 5), StormTerminalGapKind.Clear);
    private static void TerminalGapAfterOverlap()
    {
        var boundary = TrimPath((0, 0), (6, 0), (6, 3), (3, 3), (3, 1), (0, 1));
        GapDecision(StormTerminalTrim.ProbeTerminalGap(boundary, new(-1, 1), new(-2, 1), 3), StormTerminalGapKind.Clear);
        GapDecision(StormTerminalTrim.ProbeTerminalGap(boundary, new(-1, 1), new(-2, 1), 8), StormTerminalGapKind.Gap);
        var forwardOverlap = TrimPath((0, 0), (1, 0), (1, -1), (3, -1), (3, 1), (0, 1));
        GapDecision(StormTerminalTrim.ProbeTerminalGap(forwardOverlap, new(-1, 0), new(-2, 0), 5), StormTerminalGapKind.Gap);
    }
    private static void TerminalGapExteriorEndpoint()
    {
        GapDecision(StormTerminalTrim.ProbeTerminalGap(TrimSquare(), new(0, 0), new(-1, 0), 5), StormTerminalGapKind.Review);
        GapDecision(StormTerminalTrim.ProbeTerminalGap(TrimSquare(), new(-1, 0), new(-2, 0), 5), StormTerminalGapKind.Review);
    }
    private static void TerminalGapDistanceBound()
    {
        GapDecision(StormTerminalTrim.ProbeTerminalGap(TrimSquare(), new(-2, 0), new(-3, 0), 0.9999), StormTerminalGapKind.Clear);
        GapDecision(StormTerminalTrim.ProbeTerminalGap(TrimSquare(), new(-2, 0), new(-3, 0), 1.0), StormTerminalGapKind.Clear);
        GapDecision(StormTerminalTrim.ProbeTerminalGap(TrimSquare(), new(-2, 0), new(-3, 0), 1.0001), StormTerminalGapKind.Gap);
    }
    private static void TerminalGapConcave()
    {
        var boundary = TrimPath((0, 0), (6, 0), (6, 6), (4, 6), (4, 2), (2, 2), (2, 6), (0, 6));
        GapDecision(StormTerminalTrim.ProbeTerminalGap(boundary, new(-1, 5), new(-2, 5), 8), StormTerminalGapKind.Gap);
    }
    private static void TerminalGapNormalizedDirection()
    {
        GapDecision(StormTerminalTrim.ProbeTerminalGap(TrimSquare(), new(-2, 0), new(-1002, 0), 1.5), StormTerminalGapKind.Gap);
        GapDecision(StormTerminalTrim.ProbeTerminalGap(TrimSquare(), new(-2, 0), new(-2.01, 0), 1.5), StormTerminalGapKind.Gap);
    }
    private static void TerminalGapInvalidInputs()
    {
        foreach (double limit in new[] { 0.0, -1.0, 1e-9, double.NaN, double.PositiveInfinity })
            GapDecision(StormTerminalTrim.ProbeTerminalGap(TrimSquare(), new(-2, 0), new(-3, 0), limit), StormTerminalGapKind.Review);
        GapDecision(StormTerminalTrim.ProbeTerminalGap(TrimSquare(), new(-2, 0), new(-2, 0), 5), StormTerminalGapKind.Review);
        GapDecision(StormTerminalTrim.ProbeTerminalGap(TrimSquare(), new(double.NaN, 0), new(-3, 0), 5), StormTerminalGapKind.Review);
        GapDecision(StormTerminalTrim.ProbeTerminalGap(TrimSquare(), new(-2, 0), new(double.PositiveInfinity, 0), 5), StormTerminalGapKind.Review);
        GapDecision(StormTerminalTrim.ProbeTerminalGap(null!, new(-2, 0), new(-3, 0), 5), StormTerminalGapKind.Review);
        GapDecision(StormTerminalTrim.ProbeTerminalGap(TrimPath((0, 0), (1, 1), (2, 2)), new(-2, 0), new(-3, 0), 5), StormTerminalGapKind.Review);
    }
    private static void TerminalGapSurveyCoordinates()
    {
        const double x = 867285.6256335936, y = 1295711.003371928;
        var boundary = TrimSquare().Select(p => new StormStructureVertex(p.X + x, p.Y + y)).ToArray();
        GapDecision(StormTerminalTrim.ProbeTerminalGap(boundary, new(x - 2, y), new(x - 3, y), 2), StormTerminalGapKind.Gap);
    }
    private static void TerminalGapReadOnly()
    {
        var boundary = TrimSquare().ToList();
        var before = boundary.ToArray();
        var decision = StormTerminalTrim.ProbeTerminalGap(boundary, new(-2, 0), new(-3, 0), 2);
        Check(boundary.SequenceEqual(before));
        boundary[0] = new StormStructureVertex(-100, -100);
        GapDecision(decision, StormTerminalGapKind.Gap);
    }
    private static void TerminalGapContainsEndpoint()
    {
        Check(StormTerminalTrim.ContainsEndpoint(TrimSquare(), new(0, 0)));
        Check(StormTerminalTrim.ContainsEndpoint(TrimSquare(), new(1, 0)));
        Check(!StormTerminalTrim.ContainsEndpoint(TrimSquare(), new(2, 0)));
        Check(!StormTerminalTrim.ContainsEndpoint(TrimSquare(), new(double.NaN, 0)));
        Check(!StormTerminalTrim.ContainsEndpoint(null!, new(0, 0)));
    }

    private static void PipeDiameterThreshold()
    {
        Check(StormPipePreparationRules.ClassifyDiameter(11.0 / 12.0) == StormPipeSizeKind.SingleLine);
        Check(StormPipePreparationRules.ClassifyDiameter(Math.BitDecrement(1.0)) == StormPipeSizeKind.SingleLine);
        Check(StormPipePreparationRules.ClassifyDiameter(1.0) == StormPipeSizeKind.TwoWall);
        Check(StormPipePreparationRules.ClassifyDiameter(Math.BitIncrement(1.0)) == StormPipeSizeKind.TwoWall);
        Check(StormPipePreparationRules.ClassifyDiameter(3.0) == StormPipeSizeKind.TwoWall);
    }
    private static void PipeInvalidDiameters()
    {
        foreach (double diameter in new[] { 0.0, -0.0, -1.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            Check(StormPipePreparationRules.ClassifyDiameter(diameter) == StormPipeSizeKind.Invalid);
            Check(!StormPipePreparationRules.TryValidateOwnership(100, diameter, new[] { 1 }, new[] { 2 }, out string reason));
            Check(!string.IsNullOrWhiteSpace(reason));
        }
    }
    private static void PipeSingleLineOwnership()
    {
        Check(StormPipePreparationRules.TryValidateOwnership(100, 0.5, Array.Empty<int>(), Array.Empty<int>(), out string reason));
        Check(reason.Length == 0);
        Check(!StormPipePreparationRules.TryValidateOwnership(100, 0.5, new[] { 1 }, Array.Empty<int>(), out _));
        Check(!StormPipePreparationRules.TryValidateOwnership(100, 0.5, Array.Empty<int>(), new[] { 2 }, out _));
        Check(!StormPipePreparationRules.TryValidateOwnership(100, 0.5, new[] { 1 }, new[] { 2 }, out _));
    }
    private static void PipeTwoWallOwnership()
    {
        Check(StormPipePreparationRules.TryValidateOwnership(100, 1.0, new[] { 1 }, new[] { 2 }, out string reason));
        Check(reason.Length == 0);
        Check(StormPipePreparationRules.TryValidateOwnership(100, 3.0, new[] { 1, 2 }, new[] { 3, 4 }, out _));
        Check(!StormPipePreparationRules.TryValidateOwnership(100, 1.0, Array.Empty<int>(), new[] { 2 }, out _));
        Check(!StormPipePreparationRules.TryValidateOwnership(100, 1.0, new[] { 1 }, Array.Empty<int>(), out _));
        Check(!StormPipePreparationRules.TryValidateOwnership(100, 1.0, Array.Empty<int>(), Array.Empty<int>(), out _));
    }
    private static void PipeDuplicateOwnership()
    {
        foreach ((int[] positive, int[] negative) in new[]
        {
            (new[] { 1, 1 }, new[] { 2 }),
            (new[] { 1 }, new[] { 2, 2 }),
            (new[] { 1 }, new[] { 1 }),
            (new[] { 100 }, new[] { 2 }),
            (new[] { 1 }, new[] { 100 })
        })
        {
            Check(!StormPipePreparationRules.TryValidateOwnership(100, 1.0, positive, negative, out string reason));
            Check(!string.IsNullOrWhiteSpace(reason));
        }
    }
    private static void PipeMissingOwnership()
    {
        Check(!StormPipePreparationRules.TryValidateOwnership<string>(null!, 1.0, new[] { "P" }, new[] { "N" }, out _));
        Check(!StormPipePreparationRules.TryValidateOwnership(100, 1.0, null!, new[] { 2 }, out _));
        Check(!StormPipePreparationRules.TryValidateOwnership(100, 1.0, new[] { 1 }, null!, out _));
        Check(!StormPipePreparationRules.TryValidateOwnership("S", 1.0, new[] { "P", null! }, new[] { "N" }, out _));
        Check(!StormPipePreparationRules.TryValidateOwnership("S", 1.0, new[] { "P" }, new[] { null!, "N" }, out _));
    }
    private static void PipeSourceLayerHints()
    {
        foreach (string layer in new[] { "GIS-PIPE", "GIS-STRM-PIPE", "V-SURV-PIPE-2D", "C-STRM-PIPE-CNTR-E", "c-strm-pipe-cntr-e" })
            Check(StormPipePreparationRules.IsKnownSourceLayer(layer), layer);
        foreach (string layer in new[] { "Pipes", "0", "C-STRM-PIPE-E", "V-SURV-STRC-OUTR-2D", "GIS-STRC" })
            Check(!StormPipePreparationRules.IsKnownSourceLayer(layer), layer);
        // A generic imported layer is not a known-layer hint; valid native diameter
        // OD is separate evidence used by the host. Conversely a sewer centerline
        // can have a pipe-layer hint but must still fail the separate utility gate.
        Check(StormPipePreparationRules.IsKnownSourceLayer("C-SSWR-PIPE-CNTR-E"));
        Check(StormPipePreparationRules.IsSewerName("C-SSWR-PIPE-CNTR-E"));
    }
    private static void PipeSewerNames()
    {
        foreach (string name in new[] { "C-SSWR-PIPE-E", "GIS-SEWER-PIPE", "V-SURV-SEWR-PIPE", "SANITARY PIPE", "GIS-SAN-PIPE", "SS PIPE", "ss_pipes", "SS.PIPE" })
            Check(StormPipePreparationRules.IsSewerName(name), name);
        foreach (string name in new[] { "C-STRM-PIPE-E", "Pipes", "ACCESS STRUCTURE", "PASS PIPE", "SD-1", "SS" })
            Check(!StormPipePreparationRules.IsSewerName(name), name);
    }
    private static void PipeUtilitySewerOnly()
    {
        Check(StormPipePreparationRules.ClassifyUtility("Pipes", new[] { "SS_Pipes" }) == StormPipeUtilityKind.ExcludedSewer);
        Check(StormPipePreparationRules.ClassifyUtility("C-SSWR-PIPE-CNTR-E", Array.Empty<string>()) == StormPipeUtilityKind.ExcludedSewer);
        Check(StormPipePreparationRules.ClassifyUtility("GIS-PIPE", new[] { "Sanitary" }) == StormPipeUtilityKind.ExcludedSewer);
    }
    private static void PipeUtilityConflicts()
    {
        Check(StormPipePreparationRules.ClassifyUtility("GIS-STRM-PIPE-E", new[] { "SS_Pipes" }) == StormPipeUtilityKind.Review);
        Check(StormPipePreparationRules.ClassifyUtility("V-SURV-STORM-PIPE", new[] { "SS_Pipes" }) == StormPipeUtilityKind.Review);
        Check(StormPipePreparationRules.ClassifyUtility("Pipes", new[] { "SD_Pipes", "SS_Pipes" }) == StormPipeUtilityKind.Review);
        Check(StormPipePreparationRules.ClassifyUtility("C-SSWR-PIPE-CNTR-E", new[] { "SD_Pipes" }) == StormPipeUtilityKind.Review);
        Check(StormPipePreparationRules.ClassifyUtility("GIS-STRM-SEWER-PIPE", Array.Empty<string>()) == StormPipeUtilityKind.Review);
    }
    private static void PipeUtilityCandidates()
    {
        Check(StormPipePreparationRules.ClassifyUtility("GIS-STRM-PIPE", new[] { "SD_Pipes" }) == StormPipeUtilityKind.Candidate);
        Check(StormPipePreparationRules.ClassifyUtility("Pipes", new[] { "SD_Pipes" }) == StormPipeUtilityKind.Candidate);
        Check(StormPipePreparationRules.ClassifyUtility("Pipes", new[] { "Pipes" }) == StormPipeUtilityKind.Candidate);
        Check(StormPipePreparationRules.ClassifyUtility("Pipes", Array.Empty<string>()) == StormPipeUtilityKind.Candidate);
        // Candidate only means utility evidence does not exclude it; valid native
        // diameter, full OD, source geometry and ownership still gate preparation.
    }
    private static void PipeUtilityExactEvidence()
    {
        Check(StormPipePreparationRules.ClassifyUtility("gis-strm-pipe-e", new[] { "ss_pipes" }) == StormPipeUtilityKind.Review);
        Check(StormPipePreparationRules.ClassifyUtility("Pipes", new[] { "sd_pipes", "ss_pipes" }) == StormPipeUtilityKind.Review);
        Check(StormPipePreparationRules.ClassifyUtility("GIS-SD-PIPE", new[] { "SS_Pipes" }) == StormPipeUtilityKind.ExcludedSewer);
        Check(StormPipePreparationRules.ClassifyUtility("Pipes", new[] { "OTHER_SD_Pipes", "SS_Pipes" }) == StormPipeUtilityKind.ExcludedSewer);
        Check(StormPipePreparationRules.ClassifyUtility("GIS-STORMWATER-PIPE", new[] { "SS_Pipes" }) == StormPipeUtilityKind.ExcludedSewer);
        Check(StormPipePreparationRules.ClassifyUtility("GIS-STORM-STRUCTURE", new[] { "SS_Pipes" }) == StormPipeUtilityKind.ExcludedSewer);
    }
    private static void PipeUtilityMalformed()
    {
        Check(StormPipePreparationRules.ClassifyUtility(null!, Array.Empty<string>()) == StormPipeUtilityKind.Review);
        Check(StormPipePreparationRules.ClassifyUtility("Pipes", null!) == StormPipeUtilityKind.Review);
        foreach (string? name in new string?[] { null, "", " " })
            Check(StormPipePreparationRules.ClassifyUtility("Pipes", new[] { "SS_Pipes", name! }) == StormPipeUtilityKind.Review);
    }
    private static void PipeUtilityCultureAndOrder()
    {
        System.Globalization.CultureInfo original = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "en-US", "tr-TR", "ar-SA" })
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);
                Check(StormPipePreparationRules.ClassifyUtility("Pipes", new[] { "sd_pipes", "ss_pipes" }) == StormPipeUtilityKind.Review);
                Check(StormPipePreparationRules.ClassifyUtility("Pipes", new[] { "ss_pipes", "sd_pipes", "ss_pipes" }) == StormPipeUtilityKind.Review);
            }
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = original; }
    }
    private static StormPipeCompletionRecord<int> PipeCompletion()
        => new(100, "64", 1.5, StormObjectDataFingerprint.Compute(new[] { "DIAMETER-RECORD" }),
            "SOURCE-GEOMETRY", "GIS-STRM-PIPE",
            new[] { new StormPipeWallRecord<int>(201, "C9", "POSITIVE-1"), new StormPipeWallRecord<int>(202, "CA", "POSITIVE-2") },
            new[] { new StormPipeWallRecord<int>(301, "12D", "NEGATIVE-1"), new StormPipeWallRecord<int>(302, "12E", "NEGATIVE-2") });
    private static void PipeCompletionNoOp()
    {
        var original = PipeCompletion();
        var readback = original with
        {
            Positive = original.Positive.Select(w => w with { }).ToArray(),
            Negative = original.Negative.Select(w => w with { }).ToArray()
        };
        Check(StormPipePreparationRules.SameCompletion(original, original));
        Check(StormPipePreparationRules.SameCompletion(original, readback));
        Check(StormPipePreparationRules.SameCompletion(readback, original));
    }
    private static void PipeCompletionSourceChanges()
    {
        var original = PipeCompletion();
        foreach (var changed in new[]
        {
            original with { SourceId = 101 }, original with { SourceHandle = "65" },
            original with { DiameterFeet = Math.BitIncrement(original.DiameterFeet) },
            original with { ObjectDataFingerprint = StormObjectDataFingerprint.Compute(new[] { "CHANGED-OD" }) },
            original with { SourceGeometry = "MOVED-SOURCE" }, original with { OriginalLayer = "OTHER-LAYER" }
        })
            Check(!StormPipePreparationRules.SameCompletion(original, changed));
    }
    private static void PipeCompletionWallChanges()
    {
        var original = PipeCompletion();
        foreach (var wall in new[]
        {
            original.Positive[0] with { Id = 999 }, original.Positive[0] with { Handle = "OTHER" },
            original.Positive[0] with { Geometry = "TRIMMED-WALL" }
        })
            Check(!StormPipePreparationRules.SameCompletion(original, original with { Positive = new[] { wall, original.Positive[1] } }));
        Check(!StormPipePreparationRules.SameCompletion(original, original with
        {
            Negative = new[] { original.Negative[0] with { Geometry = "CHANGED-NEGATIVE" }, original.Negative[1] }
        }));
    }
    private static void PipeCompletionSideOrder()
    {
        var original = PipeCompletion();
        foreach (var changed in new[]
        {
            original with { Positive = original.Negative, Negative = original.Positive },
            original with { Positive = original.Positive.Reverse().ToArray() },
            original with { Negative = original.Negative.Reverse().ToArray() },
            original with { Positive = original.Positive.Take(1).ToArray() },
            original with { Negative = original.Negative.Concat(new[] { new StormPipeWallRecord<int>(999, "3E7", "EXTRA") }).ToArray() }
        })
            Check(!StormPipePreparationRules.SameCompletion(original, changed));
    }
    private static void PipeCompletionNulls()
    {
        Check(StormPipePreparationRules.SameCompletion<int>(null, null));
        Check(!StormPipePreparationRules.SameCompletion(PipeCompletion(), null));
        Check(!StormPipePreparationRules.SameCompletion(null, PipeCompletion()));
    }
    private static bool OpenNull(IReadOnlyList<StormStructureVertex> wall, bool start = true, double diameter = 2,
        IReadOnlyList<StormStructureSource>? anchors = null, IReadOnlyList<StormStructureVertex>? source = null,
        IReadOnlyList<IReadOnlyList<StormStructureVertex>>? physicalFootprints = null)
        => StormPipePreparationRules.IsVerifiedOpenNullTerminal(source ?? TrimPath((0, 0), (10, 0)), wall, start, diameter,
            anchors ?? new[] { NullPipeEnd() }, physicalFootprints);
    private static void OpenNullSidesAndTerminals()
    {
        foreach (double side in new[] { -1.0, 1.0 })
        {
            var wall = TrimPath((0, side), (10, side));
            Check(OpenNull(wall));
            Check(!OpenNull(wall, start: false));
            Check(OpenNull(wall, start: false, anchors: new[] { NullPipeEnd(x: 10) }));
        }
    }
    private static void OpenNullReversedPaths()
    {
        Check(OpenNull(TrimPath((10, 1), (0, 1)), start: false));
        Check(OpenNull(TrimPath((10, 1), (0, 1)), anchors: new[] { NullPipeEnd(x: 10) }));
        Check(OpenNull(TrimPath((0, 1), (10, 1)), source: TrimPath((10, 0), (0, 0))));
    }
    private static void OpenNullThresholdAndRadius()
    {
        Check(OpenNull(TrimPath((0, 0.5), (10, 0.5)), diameter: 1));
        Check(!OpenNull(TrimPath((0, 0.4995), (10, 0.4995)), diameter: 0.999));
        foreach (double diameter in new[] { 0, -1, double.NaN, double.PositiveInfinity })
            Check(!OpenNull(TrimPath((0, 1), (10, 1)), diameter: diameter));
        Check(OpenNull(TrimPath((0.1, 1), (10.1, 1))));
        Check(!OpenNull(TrimPath((0.10001, 1), (10.10001, 1))));
        Check(!OpenNull(TrimPath((0.08, 1.08), (10.08, 1.08))));
        Check(OpenNull(TrimPath((0, 1), (10, 1)), anchors: new[] { NullPipeEnd(x: 0.1) }));
        Check(!OpenNull(TrimPath((0, 1), (10, 1)), anchors: new[] { NullPipeEnd(x: 0.10001) }));
    }
    private static void OpenNullWrongTangent()
    {
        Check(!OpenNull(TrimPath((0, 1), (-10, 1))));
        Check(!OpenNull(TrimPath((0, 1), (10, 1.01))));
        Check(!OpenNull(TrimPath((0, 1), (0, 10))));
        Check(OpenNull(TrimPath((0, 1), (10, 1 + 1e-9))));
    }
    private static void OpenNullWrongWidth()
    {
        Check(!OpenNull(TrimPath((0, 1.2), (10, 1.2))));
        Check(!OpenNull(TrimPath((0, 0), (10, 0))));
        Check(!OpenNull(TrimPath((0, -1.2), (10, -1.2))));
    }
    private static void OpenNullMiddleSplit()
    {
        Check(!OpenNull(TrimPath((5, 1), (10, 1)), anchors: new[] { NullPipeEnd(x: 5) }));
        Check(!OpenNull(TrimPath((0, 1), (5, 1)), start: false, anchors: new[] { NullPipeEnd(x: 5) }));
        Check(!OpenNull(TrimPath((4, 1), (6, 1)), anchors: new[] { NullPipeEnd(x: 4) }));
    }
    private static void OpenNullAmbiguousTerminals()
    {
        var source = TrimPath((0, 0), (2, 0), (2, 2), (-1, 2), (-1, 1), (1, 1), (1, 0.05), (0, 0.05));
        Check(StormTerminalTrim.TryValidatePath(source, out _));
        Check(!OpenNull(TrimPath((0, 1.025), (3, 1.025)), source: source));
    }
    private static void OpenNullCompetingAnchors()
    {
        foreach (StormStructureSource other in new[]
        {
            Box("BOX", x: 0.03), Access("MH", x: 0.03), Di("DI", x: 0.03),
            new StormStructureSource("UNKNOWN", "UNKNOWN", "", 0.03, 0),
            NullPipeEnd("SECOND", "OTHER-STUB", 0.03)
        })
            Check(!OpenNull(TrimPath((0, 1), (10, 1)), anchors: new[] { NullPipeEnd(), other }));
        Check(OpenNull(TrimPath((0, 1), (10, 1)), anchors: new[] { NullPipeEnd(), Box("FAR", x: 100) }));
    }
    private static void OpenNullDuplicateIdentities()
    {
        Check(!OpenNull(TrimPath((0, 1), (10, 1)), anchors: new[] { NullPipeEnd(), NullPipeEnd(" d5c4a ", "OTHER-STUB", 100) }));
        Check(!OpenNull(TrimPath((0, 1), (10, 1)), anchors: new[] { NullPipeEnd(), NullPipeEnd("OTHER", " l24-00066-strm-63+75-stub ", 100) }));
    }
    private static void OpenNullInvalidGeometry()
    {
        foreach (StormStructureVertex[] path in new[]
        {
            Array.Empty<StormStructureVertex>(), TrimPath((0, 1)), TrimPath((0, 1), (0, 1)),
            TrimPath((0, 1), (double.NaN, 1)), TrimPath((0, 1), (2, 3), (0, 3), (2, 1))
        })
            Check(!OpenNull(path));
        Check(!OpenNull(TrimPath((0, 1), (10, 1)), source: TrimPath((0, 0), (0, 0))));
        Check(!StormPipePreparationRules.IsVerifiedOpenNullTerminal(null!, TrimPath((0, 1), (10, 1)), true, 2, new[] { NullPipeEnd() }));
        Check(!StormPipePreparationRules.IsVerifiedOpenNullTerminal(TrimPath((0, 0), (10, 0)), null!, true, 2, new[] { NullPipeEnd() }));
    }
    private static void OpenNullUnlocatableAnchors()
    {
        Check(!OpenNull(TrimPath((0, 1), (10, 1)), anchors: new[] { NullPipeEnd(), new StormStructureSource("U", "UNKNOWN", "", double.NaN, 100) }));
        Check(!OpenNull(TrimPath((0, 1), (10, 1)), anchors: new[] { NullPipeEnd(), null! }));
        Check(!StormPipePreparationRules.IsVerifiedOpenNullTerminal(TrimPath((0, 0), (10, 0)), TrimPath((0, 1), (10, 1)), true, 2, null!));
    }
    private static void OpenNullStrictConvention()
    {
        foreach (StormStructureSource source in new[]
        {
            NullPipeEnd(id: ""), NullPipeEnd(name: ""), NullPipeEnd(name: "NOT-A-STUB-SUFFIX"),
            new StormStructureSource("N", "SD-STUB", "Null Structure", 0, 0),
            new StormStructureSource("N", "SDDI-1-STUB", "UFLS-Null Structure", 0, 0), Box("B")
        })
            Check(!OpenNull(TrimPath((0, 1), (10, 1)), anchors: new[] { source }));
        Check(!OpenNull(TrimPath((0, 1), (10, 1)), anchors: Array.Empty<StormStructureSource>()));
    }
    private static void OpenNullSurveyAndReadOnly()
    {
        const double x = 867285.6256335936, y = 1295711.003371928;
        var source = TrimPath((x, y), (x + 10, y));
        var wall = TrimPath((x, y + 1), (x + 10, y + 1));
        var anchors = new[] { NullPipeEnd(x: x, y: y) };
        var beforeSource = source.ToArray(); var beforeWall = wall.ToArray(); var beforeAnchors = anchors.ToArray();
        Check(OpenNull(wall, source: source, anchors: anchors));
        Check(source.SequenceEqual(beforeSource) && wall.SequenceEqual(beforeWall) && anchors.SequenceEqual(beforeAnchors));
    }
    private static void OpenNullPhysicalInside()
    {
        var wall = TrimPath((0, 1), (10, 1));
        var anchors = new[] { NullPipeEnd(), Box("OFF-CENTER-BOX", x: 3, y: 3) };
        Check(OpenNull(wall, anchors: anchors));
        Check(!OpenNull(wall, anchors: anchors, physicalFootprints: new[] { Rectangle(-1, -1, 5, 5) }));
        // The gate uses the actual original source terminal, even if the wall
        // terminal itself lies outside the physical footprint.
        Check(!OpenNull(wall, physicalFootprints: new[] { Rectangle(-0.25, -0.25, 0.25, 0.25) }));
    }
    private static void OpenNullPhysicalBoundary()
        => Check(!OpenNull(TrimPath((0, 1), (10, 1)), physicalFootprints: new[] { Rectangle(0, -2, 3, 2) }));
    private static void OpenNullPhysicalOutside()
    {
        Check(OpenNull(TrimPath((0, 1), (10, 1)), physicalFootprints: new[] { Rectangle(20, 20, 25, 25) }));
        Check(OpenNull(TrimPath((0, 1), (10, 1)), physicalFootprints: Array.Empty<IReadOnlyList<StormStructureVertex>>()));
    }
    private static void OpenNullPhysicalInvalid()
    {
        foreach (IReadOnlyList<StormStructureVertex> footprint in new IReadOnlyList<StormStructureVertex>[]
        {
            null!, Array.Empty<StormStructureVertex>(), TrimPath((0, 0), (1, 1), (2, 2)),
            TrimPath((0, 0), (1, 0), (double.NaN, 1))
        })
            Check(!OpenNull(TrimPath((0, 1), (10, 1)), physicalFootprints: new[] { footprint }));
    }

    private static void FingerprintGoldenVectors()
    {
        Check(StormObjectDataFingerprint.Compute(new[] { "a", "bc" }) == "CLV_OD_V1:SHA256:27B872C5465894EC391744714C3492136C5A28C40F38EC7DC82B7BFAAC7082AC");
        Check(StormObjectDataFingerprint.Compute(Array.Empty<string>()) == "CLV_OD_V1:SHA256:EF6E7D7BEEC445D20E064DA7767E7982E3E94FD231C7ED7C95D7BF8B14134579");
    }
    private static void FingerprintMultiset()
    {
        Check(StormObjectDataFingerprint.Compute(new[] { "b", "a" }) == StormObjectDataFingerprint.Compute(new[] { "a", "b" }));
        Check(StormObjectDataFingerprint.Compute(new[] { "a", "a" }) != StormObjectDataFingerprint.Compute(new[] { "a" }));
        Check(StormObjectDataFingerprint.Compute(Array.Empty<string>()) != StormObjectDataFingerprint.Compute(new[] { "" }));
    }
    private static void FingerprintExactKeys()
    {
        var keys = new[] { "b", "a" };
        StormObjectDataFingerprint.Compute(keys);
        Check(keys.SequenceEqual(new[] { "b", "a" }));
        Check(StormObjectDataFingerprint.Compute(new[] { "a" }) != StormObjectDataFingerprint.Compute(new[] { " a " }));
        Check(StormObjectDataFingerprint.Compute(new[] { "a" }) != StormObjectDataFingerprint.Compute(new[] { "A" }));
    }
    private static void FingerprintCulture()
    {
        System.Globalization.CultureInfo original = System.Globalization.CultureInfo.CurrentCulture;
        var keys = new[] { "I", "i", "İ", "ı", "typed:Real:1.25" };
        string expected = StormObjectDataFingerprint.Compute(keys);
        try
        {
            foreach (string name in new[] { "en-US", "tr-TR", "ar-SA" })
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(name);
                Check(StormObjectDataFingerprint.Compute(keys) == expected);
            }
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = original; }
    }
    private static void FingerprintFraming()
    {
        Check(StormObjectDataFingerprint.Compute(new[] { "ab", "c" }) != StormObjectDataFingerprint.Compute(new[] { "a", "bc" }));
        Check(StormObjectDataFingerprint.Compute(new[] { "a:b", "c" }) != StormObjectDataFingerprint.Compute(new[] { "a", "b:c" }));
        Check(StormObjectDataFingerprint.IsValid(StormObjectDataFingerprint.Compute(new[] { "管", "é", "\U0001F600" })));
        Check(StormObjectDataFingerprint.Compute(new[] { "é" }) != StormObjectDataFingerprint.Compute(new[] { "e\u0301" }));
    }
    private static void FingerprintInvalidInputs()
    {
        int thrown = 0;
        try { StormObjectDataFingerprint.Compute(null!); } catch (ArgumentNullException) { thrown++; }
        try { StormObjectDataFingerprint.Compute(new[] { "a", null! }); } catch (ArgumentException) { thrown++; }
        try { StormObjectDataFingerprint.Compute(new[] { "\uD800" }); } catch (System.Text.EncoderFallbackException) { thrown++; }
        try { StormObjectDataFingerprint.Compute(new[] { "\uDC00" }); } catch (System.Text.EncoderFallbackException) { thrown++; }
        Check(thrown == 4);
    }
    private static void FingerprintValidation()
    {
        string valid = StormObjectDataFingerprint.Compute(new[] { "a" });
        Check(StormObjectDataFingerprint.IsValid(valid));
        foreach (string? invalid in new string?[] { null, "", valid + " ", " " + valid, valid[..^1], valid + "A", valid.ToLowerInvariant(), valid.Replace("V1", "V2"), StormObjectDataFingerprint.Prefix + new string('G', 64) })
            Check(!StormObjectDataFingerprint.IsValid(invalid));
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
