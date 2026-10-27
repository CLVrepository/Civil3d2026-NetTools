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

        var rowAdd = CloudGridMath.ProjectToSlice(frame, false, 2, new GridXY(0.7, 2.03));
        Check("supplement stays on row", Math.Abs(rowAdd.Y - 2) < 1e-10 && rowAdd.Row == 2);
        Check("supplement retains along-line station", Math.Abs(rowAdd.X - 0.7) < 1e-10);
        Check("off-intersection has no fake column", rowAdd.Column == CloudGridMath.OffGrid);
        var colAdd = CloudGridMath.ProjectToSlice(frame, true, -2, new GridXY(-1.96, 0.8));
        Check("negative column projection", Math.Abs(colAdd.X + 2) < 1e-10 && colAdd.Column == -2);
        Check("off-intersection has no fake row", colAdd.Row == CloudGridMath.OffGrid && Math.Abs(colAdd.Y - 0.8) < 1e-10);
        var crossingAdd = CloudGridMath.ProjectToSlice(frame, false, 2, new GridXY(3, 2.04));
        Check("intersection belongs to both directions", crossingAdd.Row == 2 && crossingAdd.Column == 3);
        var rotatedFrame = new CloudGridFrame(800000, 26000000, Math.Cos(0.63), Math.Sin(0.63), 5);
        var rotatedPick = rotatedFrame.World(7.4, 15.04);
        var rotatedAdd = CloudGridMath.ProjectToSlice(rotatedFrame, false, 3, rotatedPick);
        var rotatedLocal = rotatedFrame.Local(new GridXY(rotatedAdd.X, rotatedAdd.Y));
        Check("rotated large-coordinate row exact", Math.Abs(rotatedLocal.Y - 15) < 1e-7);
        Check("rotated added station retained", Math.Abs(rotatedLocal.X - 7.4) < 1e-7 && rotatedAdd.Column == CloudGridMath.OffGrid);
        Reject("invalid supplemental pick", () => CloudGridMath.ProjectToSlice(frame, false, 2, new GridXY(double.NaN, 0)));
        Reject("reserved non-grid slice", () => CloudGridMath.ProjectToSlice(frame, false, CloudGridMath.OffGrid, new GridXY(1, 0)));
        return count + "/" + count + " cloud grid geometry checks passed.";
    }
}
