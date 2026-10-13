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

string archiveDigest = new string('A', 64);
Check("Legacy V2 accepts no stub payload", SewerPointCleanup.ValidStubArchiveLink("", 0, archiveDigest));
Check("Legacy V2 rejects unlinked stub payload", !SewerPointCleanup.ValidStubArchiveLink("", 1, archiveDigest));
Check("V3 requires its complete saved payload", SewerPointCleanup.ValidStubArchiveLink(archiveDigest, 1, archiveDigest));
Check("V3 missing companion rejected", !SewerPointCleanup.ValidStubArchiveLink(archiveDigest, 0, archiveDigest));
Check("V3 altered payload rejected", !SewerPointCleanup.ValidStubArchiveLink(archiveDigest, 1, new string('B', 64)));
Check("V3 invalid digest rejected", !SewerPointCleanup.ValidStubArchiveLink("short", 1, "short"));

Check("Verified archive-owned null/STUB with native OD-only metadata accepted", SewerPointCleanup.CanArchiveStub("OpenNullEnd", true, true, 1, native));
Check("Connection points are protected", !SewerPointCleanup.CanArchiveStub("OpenConnectionEnd", true, true, 1, native));
Check("Unowned ordinary points are protected", !SewerPointCleanup.CanArchiveStub("UnrelatedPoint", true, true, 1, native));
Check("Unproved STUB convention is protected", !SewerPointCleanup.CanArchiveStub("OpenNullEnd", false, true, 1, native));
Check("Edited or ambiguous ownership is protected", !SewerPointCleanup.CanArchiveStub("OpenNullEnd", true, false, 1, native));
Check("Orphan stub without owned named pipe is protected", !SewerPointCleanup.CanArchiveStub("OpenNullEnd", true, true, 0, native));
Check("Stub XData is preserved", !SewerPointCleanup.CanArchiveStub("OpenNullEnd", true, true, 1, native with { HasXData = true }));
Check("Stub mixed extension data is preserved", !SewerPointCleanup.CanArchiveStub("OpenNullEnd", true, true, 1, native with { DictionaryHasExtraMetadata = true }));

Check("S10 verified owned connection accepted", SewerPointCleanup.CanArchiveOpenEnd("OpenConnectionEnd", "Connection", true, true, 1, native));
Check("S10 verified owned null/STUB accepted", SewerPointCleanup.CanArchiveOpenEnd("OpenNullEnd", "NullStub", true, true, 1, native));
Check("Connection cannot use stub role", !SewerPointCleanup.CanArchiveOpenEnd("OpenNullEnd", "Connection", true, true, 1, native));
Check("Stub cannot use connection role", !SewerPointCleanup.CanArchiveOpenEnd("OpenConnectionEnd", "NullStub", true, true, 1, native));
Check("Unknown open kind rejected", !SewerPointCleanup.CanArchiveOpenEnd("OpenConnectionEnd", "Other", true, true, 1, native));
Check("Connection without pipe link retained", !SewerPointCleanup.CanArchiveOpenEnd("OpenConnectionEnd", "Connection", true, true, 0, native));
Check("Connection extra metadata retained", !SewerPointCleanup.CanArchiveOpenEnd("OpenConnectionEnd", "Connection", true, true, 1, native with { HasXData = true }));
Check("Connection changed ownership retained", !SewerPointCleanup.CanArchiveOpenEnd("OpenConnectionEnd", "Connection", true, false, 1, native));
Check("Connection changed identity retained", !SewerPointCleanup.CanArchiveOpenEnd("OpenConnectionEnd", "Connection", false, true, 1, native));
Check("Reported FD9 exact connection convention", SewerPreparationRules.IsExplicitSewerConnectionName("L24-00066-SSWR-64+88-CONN"));
Check("Storm connection convention rejected", !SewerPreparationRules.IsExplicitSewerConnectionName("L24-STRM-64+88-CONN"));
Check("Reported FD9 exact anchor accepted", SewerPreparationRules.ConnectionAnchorMatches(867983.4039999999, 1295548.377, 0, 867983.4039999999, 1295548.377, 0));
Check("Connection cannot borrow stub XY allowance", !SewerPreparationRules.ConnectionAnchorMatches(0.05, 0, 0, 0, 0, 0));
Check("Connection Z mismatch rejected", !SewerPreparationRules.ConnectionAnchorMatches(0, 0, 0.0001, 0, 0, 0));
Check("Connection diagonal outside 1e-8 rejected", !SewerPreparationRules.ConnectionAnchorMatches(0.9e-8, 0.9e-8, 0, 0, 0, 0));
Check("Connection finite guard", !SewerPreparationRules.ConnectionAnchorMatches(double.NaN, 0, 0, 0, 0, 0));
Check("Connection part may remain absent", SewerPointCleanup.ArchivedIdentityValueMatches(0, null, null, "", true));
Check("Connection part may remain blank Character", SewerPointCleanup.ArchivedIdentityValueMatches(1, "Character", "  ", "", true));
Check("Present connection part retained", SewerPointCleanup.ArchivedIdentityValueMatches(1, "Character", "UFLS-Null Structure", "UFLS-Null Structure", true));
Check("Absent part cannot stand for nonblank part", !SewerPointCleanup.ArchivedIdentityValueMatches(0, null, null, "UFLS-Null Structure", true));
Check("Stub part cannot be absent", !SewerPointCleanup.ArchivedIdentityValueMatches(0, null, null, "", false));
Check("Duplicate connection part rejected", !SewerPointCleanup.ArchivedIdentityValueMatches(2, "Character", "", "", true));
Check("Wrong-type connection part rejected", !SewerPointCleanup.ArchivedIdentityValueMatches(1, "Integer", "0", "0", true));

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

