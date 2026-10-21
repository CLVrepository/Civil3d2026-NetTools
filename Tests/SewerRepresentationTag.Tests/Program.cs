using CLV_CivilTools.Gis;
using V = CLV_CivilTools.Gis.SewerRepresentationTag.Value;

int passed = 0;
V[] Tag(string h = "F3F", int tagValue = 0) => new[] { new V(1001, "AcDbBlockRepETag"), new V(1070, (short)1), new V(1071, tagValue), new V(1005, h) };
void Check(string name, bool condition) { if (!condition) throw new Exception(name); Console.WriteLine("PASS " + name); passed++; }
bool Source(V[] v, bool dynamic = true, bool owner = true, bool resolves = true, bool extension = false, bool od = true)
    => SewerRepresentationTag.AcceptSource(v, "F3F", dynamic, owner, resolves, extension, od);
Check("Observed F3F payload accepted", Source(Tag()));
Check("Empty leaf remains accepted", Source(Array.Empty<V>(), dynamic: false));
Check("Static context rejected", !Source(Tag(), dynamic: false));
Check("Wrong owner rejected", !Source(Tag(), owner: false));
Check("Unresolved self rejected", !Source(Tag(), resolves: false));
Check("Extension data rejected", !Source(Tag(), extension: true));
Check("Native OD rejected", !Source(Tag(), od: false));
Check("Wrong self handle rejected", !Source(Tag("ABC")));
for (int i = 0; i < 4; i++) { var v = Tag(); v[i] = new V(v[i].Code + 1, v[i].Data); Check("Wrong code " + i, !Source(v)); }
foreach (var pair in new[] { (0, (object)"OTHER"), (1, (object)1), (1, (object)(short)2), (2, (object)(short)0), (3, (object)42) })
{ var v = Tag(); v[pair.Item1] = new V(v[pair.Item1].Code, pair.Item2); Check("Wrong value/type " + pair, !Source(v)); }
Check("Extra application rejected", !Source(Tag().Concat(new[] { new V(1001, "OTHER") }).ToArray()));
Check("Truncated payload rejected", !Source(Tag().Take(3).ToArray()));
Check("Clone retains source self", SewerRepresentationTag.AcceptClone(Tag(), "F3F", "A01", 0, false));
Check("Clone remapped real destination self", SewerRepresentationTag.AcceptClone(Tag("A01"), "F3F", "A01", 0, false));
Check("Transient zero rejected", !SewerRepresentationTag.AcceptClone(Tag("0"), "F3F", "0", 0, false));
Check("Unknown clone handle rejected", !SewerRepresentationTag.AcceptClone(Tag("ABC"), "F3F", "A01", 0, false));
Check("Empty source cannot produce tag", !SewerRepresentationTag.AcceptClone(Tag(), "F3F", "A01", null, false));
Check("Empty clone accepted", SewerRepresentationTag.AcceptClone(Array.Empty<V>(), "F3F", "", 0, false));
Check("Clone extension rejected", !SewerRepresentationTag.AcceptClone(Tag(), "F3F", "A01", 0, true));
Check("Empty source has no review reason", SewerRepresentationTag.SourceReviewReason(Array.Empty<V>(), "F3F", false, false, false, false, true) == "");
Check("Exact accepted tag has no review reason", SewerRepresentationTag.SourceReviewReason(Tag(), "F3F", true, true, true, false, true) == "");
var changed = Tag(); changed[2] = new V(1071, (short)3);
string changedReason = SewerRepresentationTag.SourceReviewReason(changed, "F3F", true, true, false, false, true);
Check("Wrong tag value identifies predicate without blaming empty OD", changedReason.Contains("XData[2] must be Int32") && !changedReason.Contains("OD") && !changedReason.Contains("resolve"));
Check("Extension diagnostic identifies extension", SewerRepresentationTag.SourceReviewReason(Tag(), "F3F", true, true, true, true, true).Contains("extension dictionary"));
Check("OD failure has its own predicate", SewerRepresentationTag.SourceReviewReason(Tag(), "F3F", true, true, true, false, false).Contains("native OD"));
Check("Unresolved exact tag identifies resolution", SewerRepresentationTag.SourceReviewReason(Tag(), "F3F", true, true, false, false, true).Contains("did not resolve"));
Check("Observed F40 value 1 accepted without assigning role semantics", SewerRepresentationTag.AcceptSource(Tag("F40", 1), "F40", true, true, true, false, true));
Check("Value 1 clone keeps actual source value", SewerRepresentationTag.AcceptClone(Tag("F40", 1), "F40", "A01", 1, false));
Check("Value 1 clone remapped self keeps actual value", SewerRepresentationTag.AcceptClone(Tag("A01", 1), "F40", "A01", 1, false));
Check("Changed clone value 0 to 1 rejected", !SewerRepresentationTag.AcceptClone(Tag("F3F", 1), "F3F", "A01", 0, false));
Check("Changed clone value 1 to 0 rejected", !SewerRepresentationTag.AcceptClone(Tag("F40", 0), "F40", "A01", 1, false));
Check("Value 1 with unknown source rejected", !SewerRepresentationTag.AcceptClone(Tag("F40", 1), "F40", "A01", null, false));
Check("Value 1 still rejects non-dynamic context", !Source(Tag("F3F", 1), dynamic: false));
Check("Value 1 still rejects native OD", !Source(Tag("F3F", 1), od: false));
Check("Diagnostic reports actual tag value", SewerRepresentationTag.Describe(changed).Contains("1071:Int16=3"));
Check("Diagnostic escapes control text", !SewerRepresentationTag.Describe(new[] { new V(1001, "OTHER\r\n") }).Contains('\n'));
Check("Value 1 still rejects extension dictionary", !Source(Tag("F3F", 1), extension: true));
Check("Value 1 still rejects wrong owner", !Source(Tag("F3F", 1), owner: false));
Check("Value 1 still rejects unresolved self", !Source(Tag("F3F", 1), resolves: false));
Check("Value 1 transient zero still rejected", !SewerRepresentationTag.AcceptClone(Tag("0", 1), "F40", "0", 1, false));
// Regression: reported F41 leaf has native Int32 value 2, not a stub role.
Check("Reported F41 tag 2 accepted", SewerRepresentationTag.AcceptSource(Tag("F41", 2), "F41", true, true, true, false, true));
Check("Tag 2 has no review reason", SewerRepresentationTag.SourceReviewReason(Tag("F41", 2), "F41", true, true, true, false, true) == "");
Check("Tag 2 clone source self accepted", SewerRepresentationTag.AcceptClone(Tag("F41", 2), "F41", "A01", 2, false));
Check("Tag 2 clone remapped self accepted", SewerRepresentationTag.AcceptClone(Tag("A01", 2), "F41", "A01", 2, false));
foreach (int other in new[] { 0, 1, 3 })
    Check("Clone cannot change tag 2 to " + other, !SewerRepresentationTag.AcceptClone(Tag("F41", other), "F41", "A01", 2, false));
Check("Tag 2 rejects static leaf", !Source(Tag("F3F", 2), dynamic: false));
Check("Tag 2 rejects wrong owner", !Source(Tag("F3F", 2), owner: false));
Check("Tag 2 rejects unresolved self", !Source(Tag("F3F", 2), resolves: false));
Check("Tag 2 rejects extension", !Source(Tag("F3F", 2), extension: true));
Check("Tag 2 rejects OD", !Source(Tag("F3F", 2), od: false));
Check("Tag 2 rejects wrong self", !Source(Tag("OTHER", 2)));
Check("Tag 2 clone rejects extension", !SewerRepresentationTag.AcceptClone(Tag("F41", 2), "F41", "A01", 2, true));
Check("Tag 2 clone rejects unknown source", !SewerRepresentationTag.AcceptClone(Tag("F41", 2), "F41", "A01", null, false));
Check("Tag 2 clone rejects transient zero", !SewerRepresentationTag.AcceptClone(Tag("0", 2), "F41", "0", 2, false));
// F42 regression and opaque signed Int32 policy: no role or allowed-value inference.
foreach (int nativeValue in new[] { 0, 1, 2, 3, 4, 127, 65536, int.MaxValue, -1, int.MinValue })
{
    var observed = Tag("F42", nativeValue);
    Check("Native Int32 source accepted " + nativeValue,
        SewerRepresentationTag.AcceptSource(observed, "F42", true, true, true, false, true));
    Check("Native Int32 has no review reason " + nativeValue,
        SewerRepresentationTag.SourceReviewReason(observed, "F42", true, true, true, false, true) == "");
    Check("Native Int32 clone preserves source self " + nativeValue,
        SewerRepresentationTag.AcceptClone(observed, "F42", "A01", nativeValue, false));
    Check("Native Int32 clone preserves remapped self " + nativeValue,
        SewerRepresentationTag.AcceptClone(Tag("A01", nativeValue), "F42", "A01", nativeValue, false));
    int different = nativeValue == int.MaxValue ? int.MinValue : nativeValue + 1;
    Check("Native Int32 mutation rejected " + nativeValue,
        !SewerRepresentationTag.AcceptClone(Tag("F42", different), "F42", "A01", nativeValue, false));
    Check("Native Int32 requires dynamic context " + nativeValue, !Source(Tag("F3F", nativeValue), dynamic: false));
    Check("Native Int32 requires owner " + nativeValue, !Source(Tag("F3F", nativeValue), owner: false));
    Check("Native Int32 requires resolution " + nativeValue, !Source(Tag("F3F", nativeValue), resolves: false));
    Check("Native Int32 rejects extension " + nativeValue, !Source(Tag("F3F", nativeValue), extension: true));
    Check("Native Int32 rejects OD " + nativeValue, !Source(Tag("F3F", nativeValue), od: false));
    Check("Native Int32 rejects wrong handle " + nativeValue, !Source(Tag("OTHER", nativeValue)));
    Check("Native Int32 rejects extra app " + nativeValue,
        !Source(Tag("F3F", nativeValue).Concat(new[] { new V(1001, "OTHER") }).ToArray()));
}
foreach (object wrongType in new object[] { (short)3, 3L, "3", 3.0 })
{
    var payload = Tag(); payload[2] = new V(1071, wrongType);
    Check("Opaque metadata still rejects wrong type " + wrongType.GetType().Name, !Source(payload));
}
SewerArchiveVersion.Require(SewerArchiveVersion.Current);
Check("Current S4 archive accepted", true);
foreach (string version in new[] { "CLV_SEWER_PREP_V1", "UNKNOWN" })
{
    bool stopped = false;
    try { SewerArchiveVersion.Require(version); }
    catch (InvalidOperationException error)
    {
        stopped = error.Message.Contains("untouched input copy") && !error.Message.Contains("was edited") &&
            (version != "CLV_SEWER_PREP_V1" || error.Message.Contains("S1-S3"));
    }
    Check("Archive fails closed with version-specific diagnostic " + version, stopped);
}
Console.WriteLine($"{passed}/{passed} checks passed. Native clone/removal/revalidation require host tests.");
