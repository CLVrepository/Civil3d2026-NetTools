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

string pathTestRoot = Path.Combine(Path.GetTempPath(), "GisNewDrawingPath-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(pathTestRoot);
try
{
    void CheckPathRejected(string? requested, IEnumerable<string> protectedPaths, string expectedDetail)
    {
        string path = "must be cleared";
        Check(!GisNewDrawingResources.TryValidateNewDrawingPath(requested, protectedPaths, out path, out string detail),
            "An unsafe or unavailable destination must be rejected.");
        Check(path == string.Empty, "Rejected validation must clear the output path.");
        Check(detail.Contains(expectedDetail, StringComparison.Ordinal), "Expected refusal detail: " + expectedDetail + "; got: " + detail);
    }

    foreach (string name in new[] { "New drawing.dwg", "New drawing.DWG", "New drawing" })
        Test("new filename is accepted without filesystem writes: " + name, () =>
        {
            string requested = Path.Combine(pathTestRoot, name);
            string expected = Path.HasExtension(requested) ? requested : requested + ".dwg";
            Check(GisNewDrawingResources.TryValidateNewDrawingPath(requested, Array.Empty<string>(), out string path, out string detail), detail);
            Check(path == expected, "Keep an explicit DWG extension or append .dwg when absent.");
            Check(detail == string.Empty, "A valid new destination must have no failure detail.");
            Check(!File.Exists(path) && !Directory.Exists(path), "Validation must not reserve or create its target.");
        });

    foreach (string? requested in new string?[] { null, "", " ", "\t", "Drawing1.dwg", Path.Combine("relative", "drawing.dwg") })
        Test("blank or relative path is rejected: " + (requested ?? "<null>"), () =>
            CheckPathRejected(requested, Array.Empty<string>(), "fully qualified"));

    foreach (string name in new[] { "drawing.dwt", "drawing.dxf", "drawing.dwg.bak" })
        Test("non-DWG extension is rejected: " + name, () =>
            CheckPathRejected(Path.Combine(pathTestRoot, name), Array.Empty<string>(), ".dwg extension"));

    Test("missing parent is rejected without creating a folder", () =>
    {
        string parent = Path.Combine(pathTestRoot, "missing-parent");
        CheckPathRejected(Path.Combine(parent, "new.dwg"), Array.Empty<string>(), "does not exist or is unavailable");
        Check(!Directory.Exists(parent), "Validation must not create the missing destination folder.");
    });
    Test("a file cannot serve as the parent folder", () =>
    {
        string parent = Path.Combine(pathTestRoot, "parent-is-file");
        File.WriteAllText(parent, "preserve parent file");
        CheckPathRejected(Path.Combine(parent, "new.dwg"), Array.Empty<string>(), "does not exist or is unavailable");
        Check(File.ReadAllText(parent) == "preserve parent file", "Parent files must remain unchanged.");
    });
    Test("existing drawing is rejected and preserved", () =>
    {
        string existing = Path.Combine(pathTestRoot, "existing.dwg");
        File.WriteAllText(existing, "preserve existing drawing");
        CheckPathRejected(existing, Array.Empty<string>(), "already exists");
        CheckPathRejected(Path.Combine(pathTestRoot, "existing"), Array.Empty<string>(), "already exists");
        Check(File.ReadAllText(existing) == "preserve existing drawing", "Validation must not alter existing drawing bytes.");
    });
    Test("existing directory is rejected and preserved", () =>
    {
        string existing = Path.Combine(pathTestRoot, "existing-folder.dwg");
        Directory.CreateDirectory(existing);
        CheckPathRejected(existing, Array.Empty<string>(), "already exists");
        Check(Directory.Exists(existing) && !Directory.EnumerateFileSystemEntries(existing).Any(),
            "Validation must not change the existing directory.");
    });
    Test("revalidation rejects a destination created since the first check", () =>
    {
        string requested = Path.Combine(pathTestRoot, "created-after-validation.dwg");
        Check(GisNewDrawingResources.TryValidateNewDrawingPath(requested, Array.Empty<string>(), out string path, out string detail), detail);
        File.WriteAllText(requested, "another writer created this drawing");
        Check(!GisNewDrawingResources.TryValidateNewDrawingPath(requested, Array.Empty<string>(), out path, out detail),
            "An earlier successful check must not authorize a now-existing target.");
        Check(path == string.Empty && detail.Contains("already exists", StringComparison.Ordinal),
            "Revalidation must clear the previously valid path and explain the collision.");
        Check(File.ReadAllText(requested) == "another writer created this drawing", "Revalidation must preserve the intervening file.");
    });

    string nested = Path.Combine(pathTestRoot, "nested");
    Directory.CreateDirectory(nested);
    Test("dot segments normalize to one new destination", () =>
    {
        string requested = Path.Combine(nested, "..", ".", "canonical.dwg");
        Check(GisNewDrawingResources.TryValidateNewDrawingPath(requested, Array.Empty<string>(), out string path, out string detail), detail);
        Check(path == Path.Combine(pathTestRoot, "canonical.dwg"), "Returned path must be canonical and fully qualified.");
        Check(!File.Exists(path), "Canonicalization must not create a file.");
    });
    Test("dot segments cannot alias an existing drawing", () =>
        CheckPathRejected(Path.Combine(nested, "..", "existing.dwg"), Array.Empty<string>(), "already exists"));

    foreach (string name in new[] { "source.dwg", "template.dwg", "open-drawing.dwg" })
        Test("protected canonical path is rejected: " + name, () =>
        {
            // These files deliberately do not exist: protection must work
            // independently of the target-existence check.
            string protectedPath = Path.Combine(pathTestRoot, name);
            CheckPathRejected(Path.Combine(nested, "..", name), new[] { protectedPath }, "source, template and every open drawing");
            CheckPathRejected(protectedPath, new[] { Path.Combine(nested, "..", name) }, "source, template and every open drawing");
        });
    Test("protected path case aliases are rejected on every platform", () =>
        CheckPathRejected(Path.Combine(pathTestRoot, "Protected.dwg"),
            new[] { Path.Combine(pathTestRoot, "PROTECTED.DWG") }, "source, template and every open drawing"));
    Test("implicit extension cannot alias a protected drawing", () =>
        CheckPathRejected(Path.Combine(pathTestRoot, "protected-no-extension"),
            new[] { Path.Combine(pathTestRoot, "protected-no-extension.dwg") }, "source, template and every open drawing"));
    Test("unsaved document display names do not protect unrelated full paths", () =>
    {
        string requested = Path.Combine(pathTestRoot, "Drawing1.dwg");
        Check(GisNewDrawingResources.TryValidateNewDrawingPath(requested,
            new[] { "Drawing1.dwg", "", " " }, out string path, out string detail), detail);
        Check(path == requested, "A non-rooted display name must not be resolved against the current directory.");
    });
    Test("invalid path exception becomes a refusal with no output path", () =>
        CheckPathRejected(Path.Combine(pathTestRoot, "invalid\0.dwg"), Array.Empty<string>(), "could not be validated"));
    Test("invalid protected path cannot bypass protection", () =>
        CheckPathRejected(Path.Combine(pathTestRoot, "safe.dwg"),
            new[] { Path.Combine(pathTestRoot, "invalid\0.dwg") }, "could not be validated"));
    Test("missing protected paths fail closed", () =>
        CheckPathRejected(Path.Combine(pathTestRoot, "safe.dwg"), null!, "could not be checked"));
}
finally
{
    Directory.Delete(pathTestRoot, recursive: true);
}
Console.WriteLine($"All {passed} drawing-setup coordinate/resource checks passed. Native drawing setup acceptance remains required.");
