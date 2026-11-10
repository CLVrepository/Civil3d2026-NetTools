using System;
using System.Linq;
using CLV_CivilTools.Ufls.SewerInfo;

internal static class DirectionMatchRegression
{
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception("Direction regression: " + message); }
    private static SewerConnection Plan(string direction, bool atStart = true, double? invert = 3689.75) =>
        new() { ConnectionId = direction, Flow = atStart ? "out" : "in", Direction = direction,
            InvertElevation = invert, DiameterInches = 16, MaterialCode = "C900" };
    private static SewerInfoModel.NativeConnection Native(double azimuth, bool atStart = true) =>
        new(atStart, Math.Sin(azimuth * Math.PI / 180), Math.Cos(azimuth * Math.PI / 180));
    private static SewerInfoModel.AutomaticPlan Match(SewerConnection[] plans, SewerInfoModel.NativeConnection[] natives)
    {
        var result = SewerInfoModel.MatchAutomatically(plans, natives);
        Check(result.DirectionFallbackPipes.Distinct().Count() == result.DirectionFallbackPipes.Length,
            "fallback warning indices are unique");
        foreach (int i in result.DirectionFallbackPipes)
            Check(result.Plan.Assignments.Count(a => a.PipeIndex == i) == 1 && result.Evidence[i].Contains("WARNING"),
                "each fallback warning belongs to exactly one applied mapping");
        return result;
    }
    public static void Run()
    {
        var mh10 = new[] { Plan("W", false, 3689.94), Plan("NE") };
        var native10 = new[] { Native(270, false), Native(69.478) };
        var result = Match(mh10, native10);
        Check(result.Plan.Assignments.Select(a => a.ConnectionIndex).SequenceEqual(new[] { 0, 1 })
            && result.Plan.PendingConnections.Length == 0 && result.Plan.UnchangedPipes.Length == 0,
            "MH10 incoming and outgoing both assigned");
        Check(result.DirectionFallbackPipes.SequenceEqual(new[] { 1 })
            && result.Evidence[1].Contains("WARNING") && result.Evidence[1].Contains("plan NE")
            && result.Evidence[1].Contains("69.478") && result.Evidence[1].Contains("24.478")
            && result.Evidence[1].Contains("1.978"), "MH10 precise warning evidence");

        // A successful mapping carries BOTH size/material and invert to the existing apply path.
        SewerConnection outgoing = mh10[result.Plan.Assignments.Single(a => a.PipeIndex == 1).ConnectionIndex];
        Check(outgoing.InvertElevation == 3689.75 && outgoing.DiameterInches == 16
            && SewerInfoPartRules.Family(outgoing) == "CLV_C900"
            && SewerInfoPartRules.PipeNominal("CLV_C900", 16.008) == 16, "mapped outgoing target retained");
        double newHeight = 16.008 / 12, oldHeight = 8.326 / 12, oldOtherInvert = 3688.25;
        double newZ = SewerInfoPartRules.Centerline(outgoing.InvertElevation!.Value, newHeight);
        double oldOtherZ = SewerInfoPartRules.Centerline(oldOtherInvert, oldHeight);
        double newOtherZ = SewerInfoPartRules.Centerline(oldOtherZ - oldHeight / 2, newHeight);
        Check(Math.Abs(newZ - newHeight / 2 - 3689.75) < 1e-9
            && Math.Abs(newOtherZ - newHeight / 2 - oldOtherInvert) < 1e-9,
            "16-inch actual height gives selected invert and preserves opposite invert");

        var compass = new[] { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        foreach (bool atStart in new[] { true, false })
        for (int sector = 0; sector < compass.Length; sector++)
        foreach (int sign in new[] { -1, 1 })
        foreach (double delta in new[] { 0d, 22.49, 22.5, 24.478, 27.5, 27.501, 45, 90, 180 })
        {
            var match = Match(new[] { Plan(compass[sector], atStart) },
                new[] { Native(sector * 45 + sign * delta, atStart) });
            bool expected = delta <= 27.5;
            Check((match.Plan.Assignments.Length == 1) == expected,
                $"bounded match {atStart}/{compass[sector]}/{sign * delta}");
            Check((match.DirectionFallbackPipes.Length == 1) == (expected && delta >= 22.5),
                "warning only for bounded fallback");
        }

        foreach (bool reversePlans in new[] { false, true })
        foreach (bool reverseNative in new[] { false, true })
        {
            var plans = reversePlans ? mh10.Reverse().ToArray() : mh10;
            var natives = reverseNative ? native10.Reverse().ToArray() : native10;
            var match = Match(plans, natives);
            Check(match.Plan.Assignments.Length == 2 && match.DirectionFallbackPipes.Length == 1,
                "permutation preserves match counts");
            foreach (var a in match.Plan.Assignments)
                Check(plans[a.ConnectionIndex].Flow == (natives[a.PipeIndex].AtStart ? "out" : "in"),
                    "permutation preserves identities");
        }

        // Count full role sets. Missing data, other directions or already-strict matches
        // must not manufacture a unique pair for the fallback.
        foreach (var competitor in new[] { Native(70), Native(45), Native(270), Native(double.NaN) })
        {
            var match = Match(new[] { Plan("NE") },
                new[] { Native(69.478), competitor });
            Check(!match.Plan.Assignments.Any(a => a.PipeIndex == 0)
                && match.DirectionFallbackPipes.Length == 0, "extra native blocks fallback");
        }
        foreach (var competitor in new[] { Plan("NE"), Plan("E"), Plan("W"), Plan("W", true, null), Plan("") })
        {
            var match = Match(new[] { Plan("NE"), competitor }, new[] { Native(69.478) });
            Check(!match.Plan.Assignments.Any(a => a.ConnectionIndex == 0)
                && match.DirectionFallbackPipes.Length == 0, "extra plan blocks fallback");
        }
        var ambiguousBoundary = Match(new[] { Plan("NE"), Plan("E") }, new[] { Native(67.5) });
        Check(ambiguousBoundary.Plan.Assignments.Length == 0, "two plans at exact shared boundary unresolved");
        var duplicateNear = Match(new[] { Plan("NE") }, new[] { Native(69.478), Native(70) });
        Check(duplicateNear.Plan.Assignments.Length == 0, "two near-boundary native candidates unresolved");
        var unknownInvert = Match(new[] { Plan("NE", true, null) }, new[] { Native(69.478) });
        Check(unknownInvert.Plan.Assignments.Length == 0 && unknownInvert.DirectionFallbackPipes.Length == 0
            && unknownInvert.Evidence[0].Contains("invert is unknown"), "unknown invert never applied");
        Check(Match(new[] { Plan("NE", false) }, new[] { Native(69.478) })
            .Plan.Assignments.Length == 0, "opposite role never assigned");

        foreach (var missing in new[] {
            new SewerInfoModel.NativeConnection(true, 0, 0),
            new SewerInfoModel.NativeConnection(true, double.NaN, 1),
            new SewerInfoModel.NativeConnection(true, double.PositiveInfinity, 1) })
            Check(Match(new[] { Plan("NE") }, new[] { missing })
                .DirectionFallbackPipes.Length == 0, "unavailable bearing is not a bounded-angle match");
        Check(Match(new[] { Plan("unknown") }, new[] { Native(69.478) })
            .DirectionFallbackPipes.Length == 0, "unknown compass is not a bounded-angle match");

        foreach (bool clockwise in new[] { false, true })
        foreach (bool arcStart in new[] { false, true })
        foreach (bool nativeStart in new[] { false, true })
        {
            double dx = Math.Sin(69.478 * Math.PI / 180), dy = Math.Cos(69.478 * Math.PI / 180);
            double sign = (clockwise ? -1 : 1) * (arcStart ? 1 : -1);
            var tangent = SewerInfoModel.CurveOutwardTangent(dy * sign * 120, -dx * sign * 120, clockwise, arcStart);
            var match = Match(new[] { Plan("NE", nativeStart) },
                new[] { new SewerInfoModel.NativeConnection(nativeStart, tangent.Dx, tangent.Dy) });
            Check(match.Plan.Assignments.Length == 1 && match.DirectionFallbackPipes.Length == 1
                && match.Evidence[0].Contains("69.478"), "curved tangent bounded match CW/CCW and both ends");
        }
        Console.WriteLine("PASS: MH10 bounded direction fallback, both roles/all sectors/limits, permutations, competing rows, missing data, curved tangents and size/invert mapping.");
    }
}
