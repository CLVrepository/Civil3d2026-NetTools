using CLV_CivilTools.Gis;

int passed = 0;
int failed = 0;

Run("Generic utility evidence remains a candidate for later native validation", () =>
{
    Equal(SewerPipeUtilityKind.Candidate, SewerPreparationRules.ClassifyUtility("Pipes", Array.Empty<string>(), Array.Empty<string>()));
    Equal(SewerPipeUtilityKind.Candidate, SewerPreparationRules.ClassifyUtility("Pipes", new[] { "Pipes", "AssetInfo" }, new[] { "P-1", "N-1", "N-2" }));
});
Run("Explicit storm layer is excluded before requiring Pipes schema or diameter", () =>
{
    foreach (string layer in new[] { "GIS-STRM-PIPE-E", "v-surv-storm-pipe", "C-STRM-PIPE-CNTR-E", "GIS-SD_PIPE-E" })
        Equal(SewerPipeUtilityKind.ExcludedStorm, SewerPreparationRules.ClassifyUtility(layer, Array.Empty<string>(), Array.Empty<string>()));
    // Blank native Character values do not invent a sewer-schema requirement for storm.
    Equal(SewerPipeUtilityKind.ExcludedStorm, SewerPreparationRules.ClassifyUtility("GIS-STRM-PIPE-E", new[] { "AssetInfo" }, new[] { "", " " }));
});
Run("Exact storm table excludes generic geometry without any Pipes record", () =>
{
    Equal(SewerPipeUtilityKind.ExcludedStorm, SewerPreparationRules.ClassifyUtility("Pipes", new[] { "sd_pipes" }, Array.Empty<string>()));
    Equal(SewerPipeUtilityKind.Candidate, SewerPreparationRules.ClassifyUtility("Pipes", new[] { "OTHER_SD_Pipes", "SD_Pipes_old", "StormAssets" }, Array.Empty<string>()));
});
Run("Every verified identity field can supply bounded storm evidence", () =>
{
    foreach (string identity in new[] { "STRM-1", "STORM pipe 12", "sd_pipe-17", "SD-12", "STORM12" })
    for (int field = 0; field < 3; field++)
    {
        string[] values = { "P-1", "N-1", "N-2" };
        values[field] = identity;
        Equal(SewerPipeUtilityKind.ExcludedStorm, SewerPreparationRules.ClassifyUtility("Pipes", new[] { "AssetInfo" }, values));
    }
});
Run("Sewer tokens and the exact sewer table remain candidates", () =>
{
    foreach (string identity in new[] { "SSWR-1", "SEWR pipe", "SEWER-2", "SSMH114", "SANITARY-4" })
    {
        Equal(SewerPipeUtilityKind.Candidate, SewerPreparationRules.ClassifyUtility("Pipes", new[] { "Pipes" }, new[] { identity }));
        Equal(SewerPipeUtilityKind.Review, SewerPreparationRules.ClassifyUtility("Pipes", new[] { "SD_Pipes" }, new[] { identity }));
    }
    Equal(SewerPipeUtilityKind.Candidate, SewerPreparationRules.ClassifyUtility("GIS-SSWR-PIPE-E", new[] { "ss_pipes" }, Array.Empty<string>()));
});
Run("Conflicting layer table and terminal identities require review", () =>
{
    Equal(SewerPipeUtilityKind.Review, SewerPreparationRules.ClassifyUtility("GIS-STRM-PIPE-E", new[] { "SS_Pipes" }, Array.Empty<string>()));
    Equal(SewerPipeUtilityKind.Review, SewerPreparationRules.ClassifyUtility("GIS-SSWR-PIPE-E", new[] { "SD_Pipes" }, Array.Empty<string>()));
    Equal(SewerPipeUtilityKind.Review, SewerPreparationRules.ClassifyUtility("Pipes", new[] { "SD_Pipes", "SS_Pipes" }, Array.Empty<string>()));
    Equal(SewerPipeUtilityKind.Review, SewerPreparationRules.ClassifyUtility("Pipes", new[] { "SD_Pipes", "Sanitary" }, Array.Empty<string>()));
    Equal(SewerPipeUtilityKind.Review, SewerPreparationRules.ClassifyUtility("Pipes", new[] { "Pipes" }, new[] { "P-1", "SSMH-1", "SD_END-2" }));
    Equal(SewerPipeUtilityKind.Review, SewerPreparationRules.ClassifyUtility("GIS-STRM-PIPE-E", Array.Empty<string>(), new[] { "SSMH114" }));
});
Run("Utility matching does not infer tokens from arbitrary substrings", () =>
{
    foreach (string identity in new[] { "Stormer", "Bostorm", "Stormwater", "STORM12ER", "STRMANN", "OSD_PIPE", "SSMITH", "SSMH114A", "SEWERSON", "UNSANITARY", "éSTORM", "STORMé" })
        Equal(SewerPipeUtilityKind.Candidate, SewerPreparationRules.ClassifyUtility("Pipes", new[] { "Pipes" }, new[] { identity }));
    foreach (string layer in new[] { "STORM", "NOTGIS-STORM-PIPE", "GIS-STORM-STRUCTURE", "GIS-STORM-PIPELINE", "GIS-STORMER-PIPE" })
        Equal(SewerPipeUtilityKind.Candidate, SewerPreparationRules.ClassifyUtility(layer, Array.Empty<string>(), Array.Empty<string>()));
    Equal(SewerPipeUtilityKind.ExcludedStorm, SewerPreparationRules.ClassifyUtility("Pipes", new[] { "SD_Pipes" }, new[] { "SEWERSON", "UNSANITARY" }));
});
Run("Missing and invalid utility evidence fails closed even beside storm evidence", () =>
{
    foreach (string? layer in new string?[] { null, "", " " })
        Equal(SewerPipeUtilityKind.Review, SewerPreparationRules.ClassifyUtility(layer!, Array.Empty<string>(), Array.Empty<string>()));
    Equal(SewerPipeUtilityKind.Review, SewerPreparationRules.ClassifyUtility("GIS-STRM-PIPE-E", null!, Array.Empty<string>()));
    Equal(SewerPipeUtilityKind.Review, SewerPreparationRules.ClassifyUtility("GIS-STRM-PIPE-E", new[] { "SD_Pipes" }, null!));
    foreach (string? table in new string?[] { null, "", " " })
        Equal(SewerPipeUtilityKind.Review, SewerPreparationRules.ClassifyUtility("GIS-STRM-PIPE-E", new[] { "SD_Pipes", table! }, Array.Empty<string>()));
    Equal(SewerPipeUtilityKind.Review, SewerPreparationRules.ClassifyUtility("GIS-STRM-PIPE-E", new[] { "SD_Pipes" }, new[] { "STORM-1", null! }));
});
Run("Utility conflicts inspect all records and ignore evidence ordering and culture", () =>
{
    var savedCulture = System.Globalization.CultureInfo.CurrentCulture;
    try
    {
        System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
        foreach (string[] tables in new[] { new[] { "sd_pipes", "AssetInfo" }, new[] { "AssetInfo", "sd_pipes" } })
        foreach (string[] identities in new[] { new[] { "storm-1", "sanitary-2" }, new[] { "sanitary-2", "storm-1" } })
            Equal(SewerPipeUtilityKind.Review, SewerPreparationRules.ClassifyUtility("Pipes", tables, identities));
    }
    finally { System.Globalization.CultureInfo.CurrentCulture = savedCulture; }
});

Run("Feet and US Survey Feet are the only permitted insertion units", () =>
{
    Check(SewerPreparationRules.IsFeetInsertionUnits(2));
    Check(SewerPreparationRules.IsFeetInsertionUnits(21));
    // Includes unitless (0), inches (1), millimeters (4), meters (6), and
    // values outside the two explicitly supported foot-unit identifiers.
    for (int units = -1; units <= 25; units++)
        if (units != 2 && units != 21) Check(!SewerPreparationRules.IsFeetInsertionUnits(units));
    Check(!SewerPreparationRules.IsFeetInsertionUnits(int.MinValue));
    Check(!SewerPreparationRules.IsFeetInsertionUnits(int.MaxValue));
});

Run("8-inch diameter retains one source LINE", () => Equal(SewerPipeSizeKind.SingleLine, SewerPreparationRules.ClassifyDiameter(8.0 / 12.0)));
Run("Native OD 0.666667 feet remains an 8-inch single-line source", () => Equal(SewerPipeSizeKind.SingleLine, SewerPreparationRules.ClassifyDiameter(0.666667)));
Run("Exactly 12 inches retains centerline and both walls", () => Equal(SewerPipeSizeKind.CenterAndWalls, SewerPreparationRules.ClassifyDiameter(1.0)));
Run("Above 12 inches retains centerline and both walls", () => Equal(SewerPipeSizeKind.CenterAndWalls, SewerPreparationRules.ClassifyDiameter(18.0 / 12.0)));
Run("Diameter threshold has no rounding band", () =>
{
    Equal(SewerPipeSizeKind.SingleLine, SewerPreparationRules.ClassifyDiameter(Math.BitDecrement(1.0)));
    Equal(SewerPipeSizeKind.CenterAndWalls, SewerPreparationRules.ClassifyDiameter(Math.BitIncrement(1.0)));
    Equal(SewerPipeSizeKind.SingleLine, SewerPreparationRules.ClassifyDiameter(double.Epsilon));
    Equal(SewerPipeSizeKind.CenterAndWalls, SewerPreparationRules.ClassifyDiameter(double.MaxValue));
});
Run("Invalid diameters fail closed", () =>
{
    foreach (double value in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        Equal(SewerPipeSizeKind.Invalid, SewerPreparationRules.ClassifyDiameter(value));
});

Run("Both radial terminals trim to different outer radii", () =>
    AssertPlan(P(0, 0), P(10, 0), C("A", 0, 0, 2), C("B", 10, 0, 3), SewerClipKind.Trimmed, 0.2, 0.7));
Run("A start circle and permitted open end clip independently", () =>
    AssertPlan(P(0, 0), P(10, 0), C("A", 0, 0, 2), null, SewerClipKind.Trimmed, 0.2, 1));
Run("A permitted open start and end circle clip independently", () =>
    AssertPlan(P(0, 0), P(10, 0), null, C("B", 10, 0, 3), SewerClipKind.Trimmed, 0, 0.7));
Run("Two explicitly permitted open terminals are unchanged", () =>
    AssertPlan(P(0, 0), P(10, 0), null, null, SewerClipKind.Unchanged, 0, 1));
Run("Both offset walls use exact nonradial circle intersections", () =>
{
    foreach (double offset in new[] { -1.0, 1.0 })
        AssertPlan(P(0, offset), P(10, offset), C("A", 0, 0, 2), C("B", 10, 0, 3),
            SewerClipKind.Trimmed, Math.Sqrt(3) / 10, 1 - Math.Sqrt(8) / 10);
});
Run("Wall circle intersections are not a radial-radius inset", () =>
{
    SewerClipDecision decision = AssertPlan(P(0, 1.5), P(10, 1.5), C("A", 0, 0, 2.5), C("B", 10, 0, 2.5),
        SewerClipKind.Trimmed, 0.2, 0.8);
    Check(decision.StartParameter != 0.25 && decision.EndParameter != 0.75);
});
Run("Small source center and both large walls all clip to supplied OUTER circle", () =>
{
    foreach (double diameter in new[] { 8.0 / 12.0, 1.0, 1.5 })
    {
        double[] offsets = SewerPreparationRules.ClassifyDiameter(diameter) == SewerPipeSizeKind.SingleLine
            ? new[] { 0.0 } : new[] { 0.0, -diameter / 2, diameter / 2 };
        foreach (double offset in offsets)
            AssertPlan(P(0, offset), P(12, offset), C("OUTER-A", 0, 0, 3), C("OUTER-B", 12, 0, 3),
                SewerClipKind.Trimmed, Math.Sqrt(9 - offset * offset) / 12, 1 - Math.Sqrt(9 - offset * offset) / 12);
    }
});
Run("Only supplied outer radius is used when inner radius differs", () =>
{
    const double innerRadius = 2;
    SewerCircle outer = C("OUTER", 0, 0, 2.5);
    SewerClipDecision decision = AssertPlan(P(0, 0), P(10, 0), outer, null, SewerClipKind.Trimmed, 0.25, 1);
    Check(decision.StartParameter * 10 > innerRadius);
});
Run("Inside off-center endpoint pointing away has exact exit", () =>
    AssertPlan(P(1, 0.5), P(11, 0.5), C("A", 0, 0, 2), null,
        SewerClipKind.Trimmed, (Math.Sqrt(3.75) - 1) / 10, 1));
Run("Inside endpoint pointing across center uses positive exit root", () =>
    AssertPlan(P(1, 0.5), P(-9, 0.5), C("A", 0, 0, 2), null,
        SewerClipKind.Trimmed, (Math.Sqrt(3.75) + 1) / 10, 1));
Run("Near-boundary inside endpoint avoids cancellation", () =>
{
    double x = 2 - 1e-6;
    AssertPlan(P(x, 0), P(12, 0), C("A", 0, 0, 2), null,
        SewerClipKind.Trimmed, (2 - x) / (12 - x), 1, 1e-14);
});
Run("Already-trimmed outward endpoints remain exactly unchanged", () =>
    AssertPlan(P(2, 0), P(7, 0), C("A", 0, 0, 2), C("B", 10, 0, 3), SewerClipKind.Unchanged, 0, 1));
Run("One already-trimmed end does not suppress other end trim", () =>
    AssertPlan(P(2, 0), P(10, 0), C("A", 0, 0, 2), C("B", 10, 0, 3), SewerClipKind.Trimmed, 0, 5.0 / 8));
Run("Boundary tolerance is an absolute drawing-unit distance", () =>
{
    foreach (double displacement in new[] { -0.25, 0.25 })
        AssertPlan(P(2 + displacement * SewerCircleTrim.Tolerance, 0), P(100000, 0), C("A", 0, 0, 2), null,
            SewerClipKind.Unchanged, 0, 1);
    AssertReview(P(2 + 100 * SewerCircleTrim.Tolerance, 0), P(100000, 0), C("A", 0, 0, 2), null);
});
Run("Reversed LINE swaps normalized clip parameters", () =>
{
    SewerPoint2 a = P(0, 1), b = P(10, 1);
    SewerCircle ca = C("A", 0, 0, 2), cb = C("B", 10, 0, 3);
    SewerClipDecision forward = SewerCircleTrim.Plan(a, b, ca, cb), reverse = SewerCircleTrim.Plan(b, a, cb, ca);
    Equal(SewerClipKind.Trimmed, forward.Kind);
    Equal(forward.Kind, reverse.Kind);
    Near(1 - forward.EndParameter, reverse.StartParameter);
    Near(1 - forward.StartParameter, reverse.EndParameter);
    AssertSafe(a, b, ca, cb, forward);
    AssertSafe(b, a, cb, ca, reverse);
});
Run("Open end also survives reversal", () =>
    AssertPlan(P(10, 1), P(0, 1), null, C("A", 0, 0, 2), SewerClipKind.Trimmed, 0, 1 - Math.Sqrt(3) / 10));
Run("Survey translation preserves exact circular clipping", () =>
{
    const double x = 867285.6256335936, y = 1295711.003371928;
    AssertPlan(P(x, y + 1), P(x + 10, y + 1), C("A", x, y, 2), C("B", x + 10, y, 3),
        SewerClipKind.Trimmed, Math.Sqrt(3) / 10, 1 - Math.Sqrt(8) / 10);
});
Run("Rotations translations and both wall sides preserve crossings", () =>
{
    foreach (double angle in new[] { 0.0, 0.2, 0.9, 1.5707963267948966, 2.4, 3.7, 5.8 })
    foreach (double offset in new[] { -0.75, 0.0, 0.75 })
    foreach (double translation in new[] { 0.0, 867285.6256335936 })
    {
        double ux = Math.Cos(angle), uy = Math.Sin(angle), x = translation, y = translation * 1.5;
        SewerCircle ca = C("A", x, y, 2), cb = C("B", x + 30 * ux, y + 30 * uy, 3);
        SewerPoint2 a = P(x - uy * offset, y + ux * offset), b = P(cb.X - uy * offset, cb.Y + ux * offset);
        SewerClipDecision forward = AssertPlan(a, b, ca, cb, SewerClipKind.Trimmed,
            Math.Sqrt(4 - offset * offset) / 30, 1 - Math.Sqrt(9 - offset * offset) / 30, 1e-10);
        SewerClipDecision reverse = SewerCircleTrim.Plan(b, a, cb, ca);
        Equal(forward.Kind, reverse.Kind);
        Near(1 - forward.EndParameter, reverse.StartParameter, 1e-10);
        Near(1 - forward.StartParameter, reverse.EndParameter, 1e-10);
        AssertSafe(b, a, cb, ca, reverse);
        SewerPoint2 clippedA = At(a, b, forward.StartParameter), clippedB = At(a, b, forward.EndParameter);
        AssertPlan(clippedA, clippedB, ca, cb, SewerClipKind.Unchanged, 0, 1);
    }
});
Run("Exact tangent endpoint is review-only", () =>
    AssertReview(P(0, 2), P(10, 2), C("A", 0, 0, 2), null));
Run("Exterior tangent contact cannot establish terminal ownership", () =>
    AssertReview(P(-5, 2), P(5, 2), C("A", 0, 0, 2), null));
Run("Near-tangent wall contact is ambiguous", () =>
    AssertReview(P(0, 2 - 0.25 * SewerCircleTrim.Tolerance), P(10, 2 - 0.25 * SewerCircleTrim.Tolerance), C("A", 0, 0, 2), null));
Run("Outside endpoint beyond circle cannot be extended", () =>
    AssertReview(P(3, 0), P(10, 0), C("A", 0, 0, 2), null));
Run("Outside wall that misses circle is review-only", () =>
    AssertReview(P(0, 3), P(10, 3), C("A", 0, 0, 2), null));
Run("Outside-to-outside through crossing is unrelated to terminal clip", () =>
    AssertReview(P(-5, 0), P(5, 0), C("A", 0, 0, 2), null));
Run("Outside-to-inside cannot silently discard disconnected terminal", () =>
    AssertReview(P(-5, 0), P(0, 0), C("A", 0, 0, 2), null));
Run("Already-on-circle heading inward would reenter interior", () =>
    AssertReview(P(-2, 0), P(10, 0), C("A", 0, 0, 2), null));
Run("End on wrong side of its circle would retain an interior crossing", () =>
    AssertReview(P(0, 0), P(12, 0), null, C("B", 10, 0, 2)));
Run("Two points inside one terminal circle leave no valid retained interval", () =>
    AssertReview(P(0, 0), P(1, 0), C("A", 0, 0, 2), null));
Run("Exit at other endpoint consumes entire source", () =>
    AssertReview(P(0, 0), P(2, 0), C("A", 0, 0, 2), null));
Run("Exit leaving sub-tolerance remainder is review-only", () =>
    AssertReview(P(0, 0), P(2 + 0.25 * SewerCircleTrim.Tolerance, 0), C("A", 0, 0, 2), null));
Run("Overlapping connected circles are review-only", () =>
    AssertReview(P(0, 0), P(3, 0), C("A", 0, 0, 2), C("B", 3, 0, 2)));
Run("Touching connected circles are review-only", () =>
    AssertReview(P(0, 0), P(5, 0), C("A", 0, 0, 2), C("B", 5, 0, 3)));
Run("Overlapping circles remain review-only even off-axis gap appears", () =>
    AssertReview(P(0, 1.9), P(3.9, 1.9), C("A", 0, 0, 2), C("B", 3.9, 0, 2)));
Run("Near-touching ambiguous circles are review-only", () =>
    AssertReview(P(0, 0), P(4 + 0.25 * SewerCircleTrim.Tolerance, 0), C("A", 0, 0, 2), C("B", 4 + 0.25 * SewerCircleTrim.Tolerance, 0, 2)));
Run("Duplicate identities cannot establish two distinct manholes", () =>
    AssertReview(P(0, 0), P(10, 0), C(" mh ", 0, 0, 2), C("MH", 10, 0, 2)));
Run("Coincident circles with different IDs are still ambiguous", () =>
    AssertReview(P(0, 0), P(10, 0), C("A", 0, 0, 2), C("B", 0, 0, 2)));
Run("Swapped terminal associations cannot be guessed back into place", () =>
    AssertReview(P(0, 0), P(10, 0), C("B", 10, 0, 2), C("A", 0, 0, 2)));
Run("Nonfinite or nonpositive circle radii require review", () =>
{
    foreach (double radius in new[] { 0.0, -1.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
    {
        AssertReview(P(0, 0), P(10, 0), C("A", 0, 0, radius), null);
        AssertReview(P(0, 0), P(10, 0), null, C("B", 10, 0, radius));
    }
});
Run("Unresolved tiny circle cannot be confused with already-trimmed endpoint", () =>
    AssertReview(P(0, 0), P(10, 0), C("A", 0, 0, double.Epsilon), null));
Run("Nonfinite circle centers require review", () =>
{
    foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
    {
        AssertReview(P(0, 0), P(10, 0), C("A", value, 0, 2), null);
        AssertReview(P(0, 0), P(10, 0), null, C("B", 10, value, 2));
    }
});
Run("Missing circle identity cannot masquerade as permitted null", () =>
{
    foreach (string id in new[] { "", " ", null! })
        AssertReview(P(0, 0), P(10, 0), C(id, 0, 0, 2), null);
});
Run("Nonfinite LINE endpoints require review even with two open ends", () =>
{
    foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
    {
        AssertReview(P(value, 0), P(10, 0), null, null);
        AssertReview(P(0, 0), P(10, value), null, null);
    }
});
Run("Degenerate and unresolvably short LINEs require review", () =>
{
    AssertReview(P(0, 0), P(0, 0), null, null);
    AssertReview(P(0, 0), P(SewerCircleTrim.Tolerance, 0), null, null);
});
Run("Overflow geometry fails closed without nonfinite clip parameters", () =>
{
    AssertReview(P(-double.MaxValue, 0), P(double.MaxValue, 0), null, null);
    AssertReview(P(0, 0), P(10, 0), C("A", -double.MaxValue, 0, 2), C("B", double.MaxValue, 0, 2));
});
Run("Unrepresentable circle endpoint fails absolute-tolerance validation", () =>
    AssertReview(P(1e12, 1e12), P(1e12 + 10, 1e12), C("A", 1e12, 1e12, 2.123456789), null));
Run("Sub-resolution normalized clip cannot silently become an open end", () =>
    AssertReview(P(-1e20, 0), P(0, 0), null, C("B", 0, 0, 2)));
Run("Planning is repeatable and leaves input records unchanged", () =>
{
    SewerPoint2 a = P(0, 1), b = P(10, 1);
    SewerCircle ca = C("A", 0, 0, 2), cb = C("B", 10, 0, 3);
    SewerCircle copyA = ca with { }, copyB = cb with { };
    SewerClipDecision first = SewerCircleTrim.Plan(a, b, ca, cb);
    for (int i = 0; i < 10; i++) Equal(first, SewerCircleTrim.Plan(a, b, ca, cb));
    Equal(copyA, ca); Equal(copyB, cb); Equal(P(0, 1), a); Equal(P(10, 1), b);
});

Console.WriteLine($"Sewer GIS preparation: {passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;

void Run(string name, Action action)
{
    try { action(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + error.Message); }
}

static SewerPoint2 P(double x, double y) => new(x, y);
static SewerCircle C(string id, double x, double y, double radius) => new(id, x, y, radius);
static void Check(bool condition) { if (!condition) throw new InvalidOperationException("Condition was false."); }
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}; got {actual}.");
}
static void Near(double expected, double actual, double tolerance = 1e-12)
{
    if (!double.IsFinite(actual) || Math.Abs(expected - actual) > tolerance)
        throw new InvalidOperationException($"Expected {expected:R} within {tolerance:R}; got {actual:R}.");
}
static SewerPoint2 At(SewerPoint2 start, SewerPoint2 end, double parameter)
    => parameter == 0 ? start : parameter == 1 ? end : P(start.X + (end.X - start.X) * parameter, start.Y + (end.Y - start.Y) * parameter);
static SewerClipDecision AssertPlan(SewerPoint2 start, SewerPoint2 end, SewerCircle? a, SewerCircle? b,
    SewerClipKind kind, double first, double last, double tolerance = 1e-12)
{
    SewerClipDecision decision = SewerCircleTrim.Plan(start, end, a, b);
    if (decision.Kind != kind) throw new InvalidOperationException($"Expected {kind}; got {decision.Kind}: {decision.Reason}");
    Near(first, decision.StartParameter, tolerance); Near(last, decision.EndParameter, tolerance);
    Check(!string.IsNullOrWhiteSpace(decision.Reason));
    AssertSafe(start, end, a, b, decision);
    return decision;
}
static void AssertReview(SewerPoint2 start, SewerPoint2 end, SewerCircle? a, SewerCircle? b)
{
    SewerClipDecision forward = SewerCircleTrim.Plan(start, end, a, b);
    Equal(SewerClipKind.Review, forward.Kind);
    Near(0, forward.StartParameter); Near(1, forward.EndParameter);
    Check(!string.IsNullOrWhiteSpace(forward.Reason));
    SewerClipDecision reverse = SewerCircleTrim.Plan(end, start, b, a);
    Equal(SewerClipKind.Review, reverse.Kind);
    Near(0, reverse.StartParameter); Near(1, reverse.EndParameter);
}
static void AssertSafe(SewerPoint2 start, SewerPoint2 end, SewerCircle? a, SewerCircle? b, SewerClipDecision decision)
{
    Check(decision.Kind != SewerClipKind.Review);
    Check(decision.StartParameter >= 0 && decision.StartParameter < decision.EndParameter && decision.EndParameter <= 1);
    if (a == null) Near(0, decision.StartParameter);
    else OnCircle(At(start, end, decision.StartParameter), a);
    if (b == null) Near(1, decision.EndParameter);
    else OnCircle(At(start, end, decision.EndParameter), b);
    // Independently sample the entire retained interval, including both ends.
    // Expected crossing values above use analytic examples, not planner internals.
    for (int i = 0; i <= 32; i++)
    {
        SewerPoint2 point = At(start, end, decision.StartParameter + (decision.EndParameter - decision.StartParameter) * i / 32);
        foreach (SewerCircle? circle in new[] { a, b })
            if (circle != null) Check(Distance(point, circle) >= circle.Radius - SewerCircleTrim.Tolerance);
    }
}
static double Distance(SewerPoint2 point, SewerCircle circle)
    => Math.Sqrt((point.X - circle.X) * (point.X - circle.X) + (point.Y - circle.Y) * (point.Y - circle.Y));
static void OnCircle(SewerPoint2 point, SewerCircle circle)
    => Near(circle.Radius, Distance(point, circle), SewerCircleTrim.Tolerance);
