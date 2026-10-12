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
foreach (var pair in new[] { (0, (object)"OTHER"), (1, (object)1), (1, (object)(short)2), (2, (object)(short)0), (2, (object)2), (3, (object)42) })
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
var changed = Tag(); changed[2] = new V(1071, 2);
string changedReason = SewerRepresentationTag.SourceReviewReason(changed, "F3F", true, true, false, false, true);
Check("Wrong tag value identifies predicate without blaming empty OD", changedReason.Contains("XData[2] must be Int32 value 0") && !changedReason.Contains("OD") && !changedReason.Contains("resolve"));
Check("Extension diagnostic identifies extension", SewerRepresentationTag.SourceReviewReason(Tag(), "F3F", true, true, true, true, true).Contains("extension dictionary"));
Check("OD failure has its own predicate", SewerRepresentationTag.SourceReviewReason(Tag(), "F3F", true, true, true, false, false).Contains("native OD"));
Check("Unresolved exact tag identifies resolution", SewerRepresentationTag.SourceReviewReason(Tag(), "F3F", true, true, false, false, true).Contains("did not resolve"));
Check("Observed F40 value 1 accepted without assigning role semantics", SewerRepresentationTag.AcceptSource(Tag("F40", 1), "F40", true, true, true, false, true));
Check("Value 1 clone keeps actual source value", SewerRepresentationTag.AcceptClone(Tag("F40", 1), "F40", "A01", 1, false));
Check("Value 1 clone remapped self keeps actual value", SewerRepresentationTag.AcceptClone(Tag("A01", 1), "F40", "A01", 1, false));
Check("Changed clone value 0 to 1 rejected", !SewerRepresentationTag.AcceptClone(Tag("F3F", 1), "F3F", "A01", 0, false));
Check("Changed clone value 1 to 0 rejected", !SewerRepresentationTag.AcceptClone(Tag("F40", 0), "F40", "A01", 1, false));
Check("Unknown tag negative rejected", !Source(Tag("F3F", -1)));
Check("Unknown tag value 2 rejected", !Source(Tag("F3F", 2)));
Check("Value 1 with unknown source rejected", !SewerRepresentationTag.AcceptClone(Tag("F40", 1), "F40", "A01", null, false));
Check("Value 1 still rejects non-dynamic context", !Source(Tag("F3F", 1), dynamic: false));
Check("Value 1 still rejects native OD", !Source(Tag("F3F", 1), od: false));
Check("Diagnostic reports actual tag value", SewerRepresentationTag.Describe(changed).Contains("1071:Int32=2"));
Check("Diagnostic escapes control text", !SewerRepresentationTag.Describe(new[] { new V(1001, "OTHER\r\n") }).Contains('\n'));
Check("Value 1 still rejects extension dictionary", !Source(Tag("F3F", 1), extension: true));
Check("Value 1 still rejects wrong owner", !Source(Tag("F3F", 1), owner: false));
Check("Value 1 still rejects unresolved self", !Source(Tag("F3F", 1), resolves: false));
Check("Value 1 transient zero still rejected", !SewerRepresentationTag.AcceptClone(Tag("0", 1), "F40", "0", 1, false));
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
