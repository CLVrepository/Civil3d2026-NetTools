using CLV_CivilTools.Gis;
using V = CLV_CivilTools.Gis.SewerRepresentationTag.Value;

int passed = 0;
V[] Tag(string h = "F3F") => new[] { new V(1001, "AcDbBlockRepETag"), new V(1070, (short)1), new V(1071, 0), new V(1005, h) };
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
foreach (var pair in new[] { (0, (object)"OTHER"), (1, (object)1), (1, (object)(short)2), (2, (object)(short)0), (2, (object)1), (3, (object)42) })
{ var v = Tag(); v[pair.Item1] = new V(v[pair.Item1].Code, pair.Item2); Check("Wrong value/type " + pair, !Source(v)); }
Check("Extra application rejected", !Source(Tag().Concat(new[] { new V(1001, "OTHER") }).ToArray()));
Check("Truncated payload rejected", !Source(Tag().Take(3).ToArray()));
Check("Clone retains source self", SewerRepresentationTag.AcceptClone(Tag(), "F3F", "A01", true, false));
Check("Clone remapped real destination self", SewerRepresentationTag.AcceptClone(Tag("A01"), "F3F", "A01", true, false));
Check("Transient zero rejected", !SewerRepresentationTag.AcceptClone(Tag("0"), "F3F", "0", true, false));
Check("Unknown clone handle rejected", !SewerRepresentationTag.AcceptClone(Tag("ABC"), "F3F", "A01", true, false));
Check("Empty source cannot produce tag", !SewerRepresentationTag.AcceptClone(Tag(), "F3F", "A01", false, false));
Check("Empty clone accepted", SewerRepresentationTag.AcceptClone(Array.Empty<V>(), "F3F", "", true, false));
Check("Clone extension rejected", !SewerRepresentationTag.AcceptClone(Tag(), "F3F", "A01", true, true));
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
