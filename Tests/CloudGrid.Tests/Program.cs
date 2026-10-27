using System;
using System.Linq;
using CLV_CivilTools.PointCloud;
public static class CloudGridTests
{
    public static void Main() => Console.WriteLine(Run());
    public static string Run()
    {
        int count = 0;
        void Check(string name, bool value) { if (!value) throw new Exception("FAIL " + name); count++; }
        void Reject(string name, Action work) { bool rejected = false; try { work(); } catch (ArgumentException) { rejected = true; } Check(name, rejected); }
        var frame = new CloudGridFrame(0, 0, 1, 0, 1);
        var square = new[] { new GridXY(0, 0), new GridXY(2, 0), new GridXY(2, 2), new GridXY(0, 2) };
        var nodes = CloudGridMath.Plan(frame, square);
        Check("3x3 including boundary", nodes.Count == 9);
        Check("unique grid identities", nodes.Select(n => (n.Row, n.Column)).Distinct().Count() == 9);
        Check("on edge included", CloudGridMath.Contains(square, new GridXY(2, 1)));
        Check("outside excluded", !CloudGridMath.Contains(square, new GridXY(2.01, 1)));
        Check("inside included", CloudGridMath.Contains(square, new GridXY(1, 1)));
        Check("clockwise polygon same coverage", CloudGridMath.Plan(frame, square.Reverse().ToArray()).Count == 9);
        var rotated = new CloudGridFrame(1000000, 500000, 0, 1, 1);
        var rotatedBoundary = square.Select(p => rotated.World(p.X, p.Y)).ToArray();
        var rn = CloudGridMath.Plan(rotated, rotatedBoundary);
        Check("rotated translated count", rn.Count == 9);
        Check("rotated exact node", rn.Any(n => n.Row == 1 && n.Column == 2 && n.X == 999999 && n.Y == 500002));
        GridXY back = rotated.Local(rotated.World(-2, 3));
        Check("frame roundtrip", Math.Abs(back.X + 2) < 1e-10 && Math.Abs(back.Y - 3) < 1e-10);
        Check("negative indices supported", CloudGridMath.Plan(frame, square.Select(p => new GridXY(p.X - 2, p.Y - 2)).ToArray()).Any(n => n.Row == -2 && n.Column == -2));
        var concave = new[] { new GridXY(0, 0), new GridXY(4, 0), new GridXY(4, 1), new GridXY(1, 1), new GridXY(1, 4), new GridXY(0, 4) };
        var cn = CloudGridMath.Plan(frame, concave);
        Check("concave notch skipped", !cn.Any(n => n.X == 2 && n.Y == 2));
        Check("concave arms kept", cn.Any(n => n.X == 0 && n.Y == 3) && cn.Any(n => n.X == 3 && n.Y == 0));
        var rowStrip = CloudGridMath.Strip(frame, false, 1, 0, 10, 0, 10, 0.5);
        Check("six-inch total thickness", Math.Abs(rowStrip[2].Y - rowStrip[0].Y - 0.5) < 1e-12);
        Check("three inches each side", rowStrip[0].Y == 0.75 && rowStrip[2].Y == 1.25);
        var colStrip = CloudGridMath.Strip(frame, true, 2, 0, 10, 0, 10, 0.5);
        Check("column slice", colStrip[0].X == 1.75 && colStrip[1].X == 2.25);
        var rs = CloudGridMath.Strip(rotated, false, 1, 0, 10, 0, 10, 0.5).Select(rotated.Local).ToArray();
        Check("rotated strip width", Math.Abs(rs[2].Y - rs[0].Y - 0.5) < 1e-8);
        Reject("zero spacing", () => CloudGridMath.Plan(frame with { Spacing = 0 }, square));
        Reject("NaN spacing", () => CloudGridMath.Plan(frame with { Spacing = double.NaN }, square));
        Reject("nonunit axis", () => CloudGridMath.Plan(frame with { AxisX = 2 }, square));
        Reject("zero thickness", () => CloudGridMath.Strip(frame, false, 1, 0, 1, 0, 1, 0));
        Reject("candidate cap", () => CloudGridMath.Plan(frame with { Spacing = 0.001 }, square));
        Reject("self crossing", () => CloudGridMath.Plan(frame, new[] { new GridXY(0, 0), new GridXY(2, 2), new GridXY(0, 2), new GridXY(2, 0) }));
        Reject("zero length edge", () => CloudGridMath.Plan(frame, new[] { new GridXY(0, 0), new GridXY(0, 0), new GridXY(1, 1) }));
        Reject("collinear boundary", () => CloudGridMath.Plan(frame, new[] { new GridXY(0, 0), new GridXY(1, 0), new GridXY(2, 0) }));
        Check("empty small polygon between grid nodes", CloudGridMath.Plan(frame, new[] { new GridXY(0.1, 0.1), new GridXY(0.2, 0.1), new GridXY(0.2, 0.2), new GridXY(0.1, 0.2) }).Count == 0);
        return count + "/" + count + " cloud grid geometry checks passed.";
    }
}
