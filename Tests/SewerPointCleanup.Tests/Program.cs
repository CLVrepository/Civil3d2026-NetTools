using CLV_CivilTools.Gis;

int passed = 0;
void Check(string name, bool condition) { if (!condition) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
var nativeEntry = new SewerPointMetadataEntry("*A1", "IRD_OBJ_RECORD", true, false, false, false);
var native = new SewerPointMetadata(false, false, true, true, false, new[] { nativeEntry });
var none = new SewerPointMetadata(false, false, false, true, false, Array.Empty<SewerPointMetadataEntry>());
var proof = new SewerPointCleanupProof("RetainedSourcePoint", "ManholeOuter", true, true, true,
    true, true, "full-typed-OD", "full-typed-OD", "full-typed-OD", "full-typed-OD", native, native);
bool Erase(SewerPointCleanupProof candidate) => SewerPointCleanup.CanErase(candidate, out _);
Check("Observed POINT 1322 and outer 13AC native OD-only pair eligible", Erase(proof));
Check("Metadata-free owned source remains eligible after full native OD proof", Erase(proof with { SourceMetadata = none }));
Check("No extension dictionary is not arbitrary extra metadata", SewerPointCleanup.Classify(none) == SewerPointMetadataKind.None);
foreach (var changed in new[]
{
    native with { HasXData = true }, native with { HasUnexpectedReactors = true },
    native with { DictionaryIsExactAndOwned = false }, native with { DictionaryHasExtraMetadata = true },
    native with { Entries = Array.Empty<SewerPointMetadataEntry>() },
    native with { Entries = new[] { nativeEntry, nativeEntry with { Key = "USER_DATA" } } },
    native with { Entries = new[] { nativeEntry with { Key = "*A2" } } },
    native with { Entries = new[] { nativeEntry with { DxfName = "CIrdObjRecord" } } },
    native with { Entries = new[] { nativeEntry with { DxfName = "XRECORD" } } },
    native with { Entries = new[] { nativeEntry with { DxfName = "ACAD_PROXY_OBJECT" } } },
    native with { Entries = new[] { nativeEntry with { Owned = false } } },
    native with { Entries = new[] { nativeEntry with { HasXData = true } } },
    native with { Entries = new[] { nativeEntry with { HasExtension = true } } },
    native with { Entries = new[] { nativeEntry with { HasUnexpectedReactors = true } } }
}) Check("Unknown/mixed/nested/XData/reactor metadata preserved " + passed, !Erase(proof with { SourceMetadata = changed }));
Check("Native source with unsupported destination layout retained", !Erase(proof with { OutputMetadata = none }));
foreach (var changed in new[]
{
    proof with { SourceRole = "OpenConnectionEnd" }, proof with { SourceRole = "OpenNullEnd" },
    proof with { SourceRole = "UnrelatedPoint" }, proof with { OutputRole = "ManholeInner" },
    proof with { ArchiveLinksUnique = false }, proof with { SourceUnchanged = false },
    proof with { IdentityAndAnchorMatch = false }, proof with { SourceOdPresent = false },
    proof with { OutputOdPresent = false }, proof with { ExpectedSourceOd = "" },
    proof with { ExpectedOutputOd = "" }, proof with { ActualSourceOd = "changed" },
    proof with { ActualOutputOd = "missing-field" }, proof with { ExpectedSourceOd = "stale" },
    proof with { ExpectedOutputOd = "stale" },
    proof with { ExpectedOutputOd = "different", ActualOutputOd = "different" }
}) Check("Ownership/current complete OD/source-change guard " + passed, !Erase(changed));
Check("One exact archive source-to-outer link accepted", SewerPointCleanup.UniqueLinks(1, 1, 1, 1, 1, 1));
for (int slot = 0; slot < 6; slot++)
    foreach (int count in new[] { 0, 2 })
    {
        int[] counts = { 1, 1, 1, 1, 1, 1 }; counts[slot] = count;
        Check($"Missing/ambiguous archive link slot {slot} count {count}", !SewerPointCleanup.UniqueLinks(counts[0], counts[1], counts[2], counts[3], counts[4], counts[5]));
    }

// A fake transactional store exercises the production ordering and exception
// propagation. It is not a substitute for native AutoCAD rollback/Undo testing.
foreach (string failure in new[] { "", "acquire:B", "revalidate", "readback", "erase:A", "erase:B", "final" })
{
    var live = new HashSet<string> { "A", "B", "CONN", "NULL", "UNKNOWN" };
    var outputs = new List<string> { "A", "B", "OUTER-A", "OUTER-B", "CONN", "NULL", "UNKNOWN" };
    var originalLive = live.ToHashSet(); var originalOutputs = outputs.ToArray();
    var trace = new List<string>(); bool failed = false;
    void Step(string step) { trace.Add(step); if (step == failure) throw new InvalidOperationException(step); }
    try
    {
        SewerPointCleanup.Apply(new[] { "A", "B" }, id => Step("acquire:" + id),
            () => Step("revalidate"),
            () => { outputs.RemoveAll(id => id == "A" || id == "B"); Step("readback"); },
            id => { live.Remove(id); Step("erase:" + id); },
            () => Step("final"));
    }
    catch (InvalidOperationException) { failed = true; live = originalLive; outputs = originalOutputs.ToList(); }
    if (failure.Length == 0)
    {
        Check("All writes/revalidation/archive readback precede erasure", trace.SequenceEqual(new[] { "acquire:A", "acquire:B", "revalidate", "readback", "erase:A", "erase:B", "final" }));
        Check("Only approved manhole source entries removed", live.SetEquals(new[] { "CONN", "NULL", "UNKNOWN" }) && outputs.SequenceEqual(new[] { "OUTER-A", "OUTER-B", "CONN", "NULL", "UNKNOWN" }));
        int erases = 0;
        SewerPointCleanup.Apply(Array.Empty<string>(), _ => throw new Exception("Unexpected acquire"),
            () => throw new Exception("Unexpected revalidation"), () => throw new Exception("Unexpected archive write"),
            _ => erases++, () => throw new Exception("Unexpected final write"));
        Check("Rerun with no live retained-point candidates erases nothing", erases == 0);
    }
    else
    {
        Check("Injected failure propagates to transaction owner " + failure, failed);
        Check("Transactional rollback restores points/archive " + failure, live.SetEquals(originalLive) && outputs.SequenceEqual(originalOutputs));
        if (failure is "acquire:B" or "revalidate" or "readback")
            Check("Locked/revalidation/readback failure prevents every erase " + failure, !trace.Any(step => step.StartsWith("erase:")));
    }
}
Console.WriteLine($"Sewer point cleanup: {passed} passed, 0 failed. Native OD API, locks, transaction rollback, Undo and save/reopen require host validation.");
