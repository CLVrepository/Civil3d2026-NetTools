using CLV_CivilTools.PointCloud;
using static CLV_CivilTools.PointCloud.VertexElevationMatcher;

int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    checks++;
}
void Reject(Action action, string name)
{
    bool rejected = false;
    try { action(); } catch (ArgumentException) { rejected = true; }
    Check(rejected, name);
}
Sample P(int key, double x, double y, double z) => new(key, "Point " + key, x, y, z);
Match[] M(XY[] vertices, Sample[] points, double tolerance = 0.001) => MatchVertices(vertices, points, tolerance);

var matched = M(new[] { new XY(885100, 1283300), new XY(885110, 1283307), new XY(885120, 1283305) },
    new[] { P(2, 885120, 1283305, 2801.77), P(0, 885100, 1283300, 2806.19), P(1, 885110, 1283307, 2803.42) });
Check(matched.All(m => m.Issue == null), "large survey coordinates match");
Check(matched.Select(m => m.Point!.Z).SequenceEqual(new[] { 2806.19, 2803.42, 2801.77 }), "vertex order, not selection order");
Check(matched.Select(m => m.Point!.Key).SequenceEqual(new[] { 0, 1, 2 }), "point identity retained");

Check(M(new[] { new XY(0, 0) }, Array.Empty<Sample>())[0].Point == null, "no points is missing, not zero elevation");
Check(M(new[] { new XY(0, 0) }, new[] { P(0, 0, 0, 0) })[0].Point!.Z == 0, "measured zero elevation accepted");
Check(M(new[] { new XY(0, 0) }, new[] { P(0, 0, 0, -15.25) })[0].Point!.Z == -15.25, "negative elevation accepted");
Check(M(new[] { new XY(0, 0) }, new[] { P(0, 0.002, 0, 100) })[0].Point == null, "no nearest fallback beyond tolerance");
Check(M(new[] { new XY(0, 0) }, new[] { P(0, 0.001, 0, 100) })[0].Point != null, "inclusive radial boundary");
Check(M(new[] { new XY(0, 0) }, new[] { P(0, 0.0008, 0.0008, 100) })[0].Point == null, "circular radius excludes square corner");
Check(M(new[] { new XY(0, 0) }, new[] { P(0, -0.0006, 0.0006, 100) })[0].Point != null, "negative XY inside circular radius");

var conflict = M(new[] { new XY(0, 0) }, new[] { P(0, 0, 0, 100), P(1, 0.0005, 0, 110) })[0];
Check(conflict.Point == null && conflict.Issue!.Contains("Conflicting"), "different nearby heights refuse even with an exact XY hit");
Check(conflict.Issue!.Contains("Point 0") && conflict.Issue.Contains("Point 1"), "conflict identifies source points");
var duplicate = M(new[] { new XY(0, 0) }, new[] { P(3, 0, 0, 100), P(1, 0, 0, 100) })[0];
Check(duplicate.Point!.Key == 1, "same-height duplicate tie is deterministic");
Check(M(new[] { new XY(0, 0) }, new[] { P(2, 0.0005, 0, 100), P(1, 0, 0, 100) })[0].Point!.Key == 1, "same-height closest point");
Check(M(new[] { new XY(0, 0) }, new[] { P(0, 0, 0, 100), P(1, 0, 0, 100 + 5e-8) })[0].Point != null, "tiny elevation roundoff agrees");
Check(M(new[] { new XY(0, 0) }, new[] { P(0, 0, 0, 0), P(1, 0, 0, 8e-8), P(2, 0, 0, 16e-8) })[0].Point == null,
    "agreement compares full elevation range, not a chain of pairwise neighbors");

var reuse = M(new[] { new XY(0, 0), new XY(0.0005, 0) }, new[] { P(0, 0, 0, 99) });
Check(reuse.All(m => m.Point == null && m.Issue!.Contains("several vertices")), "one shot cannot silently supply multiple vertices");
var mixed = M(new[] { new XY(0, 0), new XY(5, 5) }, new[] { P(0, 0, 0, 99) });
Check(mixed[0].Point != null && mixed[1].Point == null, "partial matches expose missing vertex for atomic host refusal");

foreach (double tolerance in new[] { 0.0, -1, double.NaN, double.PositiveInfinity, 1e200, 1e-200 })
    Reject(() => M(new[] { new XY(0, 0) }, new[] { P(0, 0, 0, 1) }, tolerance), "invalid numeric tolerance " + tolerance);
Reject(() => M(new[] { new XY(double.NaN, 0) }, Array.Empty<Sample>()), "invalid vertex");
Reject(() => M(new[] { new XY(0, 0) }, new[] { P(0, 0, 0, double.NaN) }), "unset elevation");
Reject(() => M(new[] { new XY(0, 0) }, new[] { P(0, double.PositiveInfinity, 0, 1) }), "non-finite point");
Reject(() => M(new[] { new XY(0, 0) }, new[] { P(0, 0, 0, 1), P(0, 1, 1, 1) }), "duplicate point identity");

var vertices = Enumerable.Range(0, 10000).Select(i => new XY(885000 + i, 1283000 + i % 7)).ToArray();
var points = vertices.Select((v, i) => P(i, v.X, v.Y, 2800 + i / 100.0)).Reverse().ToArray();
var large = M(vertices, points);
Check(large.All(m => m.Point != null), "10000-vertex trace matches");
Check(large[9999].Point!.Z == 2899.99, "large trace retains final elevation");
Console.WriteLine($"Passed {checks} vertex-elevation checks.");
