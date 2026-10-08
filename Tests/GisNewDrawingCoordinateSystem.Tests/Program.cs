using CLV_CivilTools.Gis;

int passed = 0;
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { throw new Exception("FAILED " + name, ex); }
}
void Check(bool value, string detail) { if (!value) throw new Exception(detail); }
void Reject(Action action, string detail)
{
    Exception? failure = null;
    try { action(); } catch (Exception ex) { failure = ex; }
    Check(failure != null && failure.ToString().Contains(detail, StringComparison.Ordinal), "Expected rejection: " + detail);
}
Test("exact deployed template and profile folder", () =>
{
    Check(GisNewDrawingResources.TemplatePath == @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Drawing Templates\Blank (2026).dwt",
        "The deployed blank template UNC path must remain exact.");
    Check(GisNewDrawingResources.ProfileFolder == @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\SDF to SHP",
        "The deployed profile folder UNC path must remain exact.");
});
foreach (var (source, fileName, expectedPath) in new[]
{
    ("NV83.NCRS-LVF", "UFLS-IMPORT-NV83.NCRS-LVF.ipf",
        @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\SDF to SHP\UFLS-IMPORT-NV83.NCRS-LVF.ipf"),
    ("NV83.NCRS-LVHEF", "UFLS-IMPORT-NV83.NCRS.LVHEF.ipf",
        @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\SDF to SHP\UFLS-IMPORT-NV83.NCRS.LVHEF.ipf")
})
    Test("exact source code selects deployed profile path " + source, () =>
    {
        Check(GisNewDrawingResources.IsSupportedCoordinateSystem(source), "The exact assigned code must be supported.");
        Check(GisNewDrawingResources.TryResolveProfilePath(source, out string path, out string detail), detail);
        Check(path == expectedPath, "Profile selection must retain the exact Windows UNC path and filename punctuation.");
        Check(detail == string.Empty, "Successful selection must have no failure detail.");
        string actualName = source == "NV83.NCRS-LVF"
            ? GisNewDrawingResources.LvfProfileFileName : GisNewDrawingResources.LvhefProfileFileName;
        Check(actualName == fileName, "The deployed profile filename must remain exact.");
    });
string[] codes = { GisNewDrawingResources.LvfCoordinateSystem, GisNewDrawingResources.LvhefCoordinateSystem };
foreach (string source in codes)
{
    Test("supported source drawing code " + source, () => GisNewDrawingCoordinateSystem.RequireSourceCode(source));
    Test("one assignment and ordered readback " + source, () =>
    {
        string? stored = null;
        var events = new List<string>();
        GisNewDrawingCoordinateSystem.AssignSameCode(source,
            code => { events.Add("set:" + code); stored = code; },
            () => { events.Add("get"); return stored; }, "New drawing");
        Check(events.SequenceEqual(new[] { "set:" + source, "get" }), "Assign once, then read exactly once.");
    });
    foreach (string? originalLabel in new string?[] { null, "", "unrecognized label", "PROJCS[malformed]", codes.Single(code => code != source) })
        Test("source assignment replaces destination template code for " + source + ": " + (originalLabel ?? "<null>"), () =>
        {
            string? stored = originalLabel;
            int writes = 0;
            GisNewDrawingCoordinateSystem.AssignSameCode(source,
                code => { writes++; stored = code; }, () => stored, "Destination drawing CRS");
            Check(writes == 1 && stored == source, "Source assignment must replace any previous destination code.");
        });
    Test("read-only assignment verification " + source, () =>
        GisNewDrawingCoordinateSystem.VerifySameCode(source, source, "Destination"));
    Test("opposite drawing code remains a failure " + source, () =>
        Reject(() => GisNewDrawingCoordinateSystem.VerifySameCode(source, codes.Single(code => code != source), "Destination"), "did not retain"));
}
foreach (string? invalid in new string?[]
{
    null, "", " ", "LVF", "LVHEF", "NV83.NCRS", "nv83.ncrs-lvf", "nv83.ncrs-lvhef",
    " NV83.NCRS-LVF", "NV83.NCRS-LVF ", " NV83.NCRS-LVHEF", "NV83.NCRS-LVHEF ",
    "NV83.NCRS.LVF", "NV83.NCRS.LVHEF", "NV83.NCRS-LVF\n", "EPSG:3421"
})
{
    Test("unsupported source selects no profile: " + (invalid ?? "<null>"), () =>
    {
        Check(!GisNewDrawingResources.IsSupportedCoordinateSystem(invalid), "Aliases, case changes and whitespace must be rejected.");
        Check(!GisNewDrawingResources.TryResolveProfilePath(invalid, out string path, out string detail),
            "Unsupported source must not select a profile.");
        Check(path == string.Empty, "Unsupported source must not leave a fallback path.");
        Check(detail.Contains("no default, alias or fallback", StringComparison.Ordinal),
            "The refusal must explain that no fallback is allowed.");
    });
    Test("missing/unsupported source has no fallback: " + (invalid ?? "<null>"), () =>
        Reject(() => GisNewDrawingCoordinateSystem.RequireSourceCode(invalid), "No default was assigned"));
    Test("invalid source stops before write/read: " + (invalid ?? "<null>"), () =>
    {
        int calls = 0;
        Reject(() => GisNewDrawingCoordinateSystem.AssignSameCode(invalid!, _ => calls++, () => { calls++; return codes[0]; }, "Destination"), "No default was assigned");
        Check(calls == 0, "Invalid source must not mutate a drawing setting.");
    });
}
foreach (string? actual in new string?[] { null, "", "PROJCS[\"NV83.NCRS-LVF\"]", "NV83.NCRS.LVF" })
    Test("failed native code readback stops: " + (actual ?? "<null>"), () =>
        Reject(() => GisNewDrawingCoordinateSystem.VerifySameCode(codes[0], actual, "Native setting"), "did not retain"));
Test("native setter exception is propagated without readback", () =>
{
    int reads = 0;
    Reject(() => GisNewDrawingCoordinateSystem.AssignSameCode(codes[0], _ => throw new InvalidOperationException("setter failed"),
        () => { reads++; return codes[0]; }, "Native setting"), "setter failed");
    Check(reads == 0, "Readback must not hide a failed assignment.");
});
Test("native reader exception is propagated after one assignment", () =>
{
    int writes = 0;
    Reject(() => GisNewDrawingCoordinateSystem.AssignSameCode(codes[0], _ => writes++,
        () => throw new InvalidOperationException("reader failed"), "Native setting"), "reader failed");
    Check(writes == 1, "A read failure must not repeat assignment.");
});
Test("ignored native setter is detected", () =>
    Reject(() => GisNewDrawingCoordinateSystem.AssignSameCode(codes[0], _ => { }, () => codes[1], "Native setting"), "did not retain"));
Test("policy and test runner have no MapGuide dependency", () =>
{
    Check(!typeof(GisNewDrawingCoordinateSystem).Assembly.GetReferencedAssemblies().Any(assembly => assembly.Name?.StartsWith("OSGeo.MapGuide", StringComparison.Ordinal) == true),
        "The source-authoritative policy must not reference a MapGuide runtime or fake.");
});
Console.WriteLine($"All {passed} drawing-setup coordinate/resource checks passed. Native drawing setup acceptance remains required.");
