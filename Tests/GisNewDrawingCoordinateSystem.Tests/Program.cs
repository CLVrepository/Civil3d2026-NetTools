using System;
using System.Linq;
using CLV_CivilTools.Gis;
using GisCoordinateSystemTestSupport;

int passed = 0;
void Test(string name, Action action)
{
    FakeMapGuide.Reset();
    try
    {
        action();
        if (FakeMapGuide.LiveObjects != 0 || FakeMapGuide.CachedCatalogReferences != 1)
            throw new Exception("Native-wrapper leak or changed shared catalog owner.");
        if (FakeMapGuide.Calls.Any(call => call.StartsWith("Set", StringComparison.Ordinal) || call.Contains("Transform")))
            throw new Exception("An unauthorized native mutation/transform was called.");
        passed++;
        Console.WriteLine("PASS " + name);
    }
    catch (Exception ex) { throw new Exception("FAILED " + name, ex); }
}
void Check(bool value, string detail) { if (!value) throw new Exception(detail); }
void Reject(Action action, string? contains = null)
{
    Exception? failure = null;
    try { action(); } catch (Exception ex) { failure = ex; }
    Check(failure != null, "Unexpected accepted CRS.");
    if (contains != null) Check(failure!.ToString().Contains(contains, StringComparison.OrdinalIgnoreCase),
        "Missing diagnostic '" + contains + "': " + failure);
    Check(FakeMapGuide.LiveObjects == 0, "Native-wrapper leak after rejection.");
}
GisNewDrawingCoordinateSystemVerification Verify(string? name, string? wkt, string code = FakeMapGuide.Lvhef)
    => GisNewDrawingCoordinateSystem.Verify(code, name, wkt);
string Wkt() => FakeMapGuide.Wkt(FakeMapGuide.Lvhef);
void VerifyEquivalent(string candidate)
{
    FakeMapGuide.RegisterWkt(candidate, FakeMapGuide.Lvhef);
    var result = Verify(candidate, candidate);
    Check(result.ResolvedCode == FakeMapGuide.Lvhef && result.ExpectedCode == FakeMapGuide.Lvhef, "Wrong proven code.");
    Check(FakeMapGuide.Calls.Count(call => call == "SameCoordinateSystem") == 4, "Both native comparison directions required per field.");
}
void Hostile(string candidate, string reason)
{
    // The fake parser explicitly returns the expected dictionary definition even
    // for this hostile input. Structural verification must reject BEFORE parsing.
    FakeMapGuide.RegisterWkt(candidate, FakeMapGuide.Lvhef);
    Reject(() => Verify(candidate, candidate), reason);
    Check(!FakeMapGuide.Calls.Contains("Create"), "Hostile definition reached the snapping native parser.");
}

Test("HEF raw native WKT in both FDO fields", () => VerifyEquivalent(Wkt()));
Test("LVF dictionary and WKT", () => Verify(FakeMapGuide.Lvf, FakeMapGuide.Wkt(FakeMapGuide.Lvf), FakeMapGuide.Lvf));
Test("short code plus WKT", () => Verify(FakeMapGuide.Lvhef, Wkt()));
Test("code-only readback is authoritative", () => Verify(FakeMapGuide.Lvhef, string.Empty));
Test("WKT-only first field readback", () => Verify(Wkt(), string.Empty));
Test("missing first field with complete WKT", () => Verify(null, Wkt()));
Test("title alone is ignored after proof", () => VerifyEquivalent(Wkt().Replace("\"NV83.NCRS-LVHEF\"", "\"different display title\"")));
Test("escaped top display title", () => VerifyEquivalent(Wkt().Replace("\"NV83.NCRS-LVHEF\"", "\"a \"\"quoted\"\" title\"")));
Test("harmless whitespace", () => VerifyEquivalent(" \r\n" + Wkt().Replace(",", ", \t\n") + "  "));
Test("equivalent exact numeric spelling", () => VerifyEquivalent(Wkt().Replace("984250.0000", "+9.8425e5")
    .Replace("1312333.3333", "13123333333e-4").Replace("1.000135000000", "1000135e-6")
    .Replace("36.25000000000000", ".3625E+2").Replace("\"Greenwich\",0", "\"Greenwich\",-0.000")));
Test("PARAMETER order", () =>
{
    string east = "PARAMETER[\"false_easting\",984250.0000]", north = "PARAMETER[\"false_northing\",1312333.3333]";
    VerifyEquivalent(Wkt().Replace(east + "," + north, north + "," + east));
});
Test("different recognized title is not code evidence", () =>
{
    string candidate = Wkt().Replace("\"NV83.NCRS-LVHEF\"", "\"NV83.NCRS-LVF\"");
    FakeMapGuide.RegisterWkt(candidate, FakeMapGuide.Lvhef);
    var result = Verify(candidate, candidate);
    Check(result.ResolvedCode == FakeMapGuide.Lvhef, "Resolved code must come from the proven expected definition.");
});
Test("LVF to HEF swap rejected", () => Reject(() => Verify(FakeMapGuide.Lvf, FakeMapGuide.Wkt(FakeMapGuide.Lvf)), "does not match"));
Test("HEF to LVF swap rejected", () => Reject(() => Verify(FakeMapGuide.Lvhef, Wkt(), FakeMapGuide.Lvf), "does not match"));
Test("LVF WKT against HEF", () => Reject(() => Verify(FakeMapGuide.Wkt(FakeMapGuide.Lvf), string.Empty), "scale_factor"));
Test("conflicting first and second definitions", () => Reject(() => Verify(Wkt(), FakeMapGuide.Wkt(FakeMapGuide.Lvf)), "GetCoordinateSystemWkt"));
Test("code cannot conceal conflicting WKT", () => Reject(() => Verify(FakeMapGuide.Lvhef, FakeMapGuide.Wkt(FakeMapGuide.Lvf)), "scale_factor"));
Test("same title changed false easting", () => Hostile(Wkt().Replace("984250.0000", "984251.0000"), "false_easting"));
Test("same title tiny offset change", () => Hostile(Wkt().Replace("984250.0000", "984250.0000000000000000000001"), "false_easting"));
Test("same title changed scale", () => Hostile(Wkt().Replace("1.000135000000", "1.000136000000"), "scale_factor"));
Test("same title tiny scale change", () => Hostile(Wkt().Replace("1.000135000000", "1.000135000001"), "scale_factor"));
Test("same title changed datum", () => Hostile(Wkt().Replace("\"NAD83\"", "\"WGS84\""), "DATUM"));
Test("same title changed ellipsoid", () => Hostile(Wkt().Replace("6378137.000", "6378138.000"), "SPHEROID"));
Test("same title changed projection", () => Hostile(Wkt().Replace("Transverse_Mercator", "Mercator"), "PROJECTION"));
Test("same title US feet to metres", () => Hostile(Wkt().Replace("\"Foot_US\",0.30480060960122", "\"Meter\",1"), "UNIT"));
Test("same title changed unit scale only", () => Hostile(Wkt().Replace("0.30480060960122", "0.3048"), "UNIT"));
Test("same title changed prime meridian", () => Hostile(Wkt().Replace("\"Greenwich\",0", "\"Greenwich\",1"), "PRIMEM"));
Test("same title changed origin", () => Hostile(Wkt().Replace("36.25000000000000", "36.26000000000000"), "latitude_of_origin"));
Test("unproved nested name alias", () => Hostile(Wkt().Replace("\"LL83\"", "\"Another name\""), "GEOGCS"));
Test("unknown extra node", () => Hostile(Wkt()[..^1] + ",EXTENSION[\"x\",\"y\"]]", "unsupported WKT1 node"));
Test("extra authority remains significant", () => Hostile(Wkt()[..^1] + ",AUTHORITY[\"EPSG\",\"9999\"]]", "definition values"));
Test("duplicate parameter", () => Hostile(Wkt()[..^1] + ",PARAMETER[\"false_easting\",984250.0000]]", "duplicate projection parameter"));
Test("missing parameter", () => Hostile(Wkt().Replace("PARAMETER[\"false_easting\",984250.0000],", ""), "definition values"));
Test("title only is insufficient", () => Hostile("PROJCS[\"NV83.NCRS-LVHEF\"]", "invalid PROJCS"));
Test("malformed WKT", () => Hostile(Wkt()[..^1], "incomplete"));
Test("trailing text rejected", () => Hostile(Wkt() + "junk", "complete projected"));
Test("unsupported LOCAL_CS", () => Hostile("LOCAL_CS[\"NV83.NCRS-LVHEF\",UNIT[\"Foot_US\",0.30480060960122]]", "complete projected"));
Test("all fields missing", () => Reject(() => Verify(null, null), "no coordinate-system"));
Test("unsupported expected code", () => Reject(() => Verify(Wkt(), Wkt(), "EPSG:4326"), "exact supported"));
Test("missing expected dictionary entry", () => { string wkt = Wkt(); FakeMapGuide.Definitions.Remove(FakeMapGuide.Lvhef); Reject(() => Verify(wkt, wkt)); });
Test("unresolved actual code", () => Reject(() => Verify("Some unknown code", Wkt())));
Test("expected dictionary code conflict", () =>
{
    FakeMapGuide.Definitions[FakeMapGuide.Lvhef] = FakeMapGuide.Definitions[FakeMapGuide.Lvhef] with { Code = FakeMapGuide.Lvf };
    Reject(() => Verify(Wkt(), Wkt()), "exact source code");
});
Test("native comparator rejection", () => { FakeMapGuide.ComparatorResult = false; Reject(() => Verify(Wkt(), Wkt()), "native mathematical"); });
Test("asymmetric native comparison rejects and reverses arguments", () =>
{
    FakeMapGuide.ComparatorOverride = (_, _) => FakeMapGuide.Comparisons.Count == 1;
    Reject(() => Verify(Wkt(), Wkt()), "native mathematical");
    Check(FakeMapGuide.Comparisons.Count == 2, "Both directions must be attempted after the forward comparison succeeds.");
    var forward = FakeMapGuide.Comparisons[0];
    var reverse = FakeMapGuide.Comparisons[1];
    Check(!ReferenceEquals(forward.First, forward.Second), "Expected and parsed wrappers must be distinct.");
    Check(ReferenceEquals(forward.First, reverse.Second) && ReferenceEquals(forward.Second, reverse.First),
        "The second native comparison must reverse the exact argument objects.");
});
Test("unusable expected dictionary definition", () =>
{
    FakeMapGuide.Definitions[FakeMapGuide.Lvhef] = FakeMapGuide.Definitions[FakeMapGuide.Lvhef] with { Usable = false };
    Reject(() => Verify(Wkt(), Wkt()), "cannot use");
});
Test("nonprojected expected dictionary definition", () =>
{
    FakeMapGuide.Definitions[FakeMapGuide.Lvhef] = FakeMapGuide.Definitions[FakeMapGuide.Lvhef] with { Type = 2 };
    Reject(() => Verify(Wkt(), Wkt()), "unsupported CRS type");
});
Test("nongeodetic expected dictionary definition", () =>
{
    FakeMapGuide.Definitions[FakeMapGuide.Lvhef] = FakeMapGuide.Definitions[FakeMapGuide.Lvhef] with { Geodetic = false };
    Reject(() => Verify(Wkt(), Wkt()), "non-geodetic");
});
Test("disabled datum comparison rejected", () => { FakeMapGuide.CompareDatumParameters = false; Reject(() => Verify(Wkt(), Wkt()), "datum transformation"); });
Test("native parser rejection", () => { FakeMapGuide.WktValid = false; Reject(() => Verify(Wkt(), Wkt()), "factory rejected"); });
foreach (var mutation in new (string Name, Func<FakeDefinition, FakeDefinition> Change)[]
{
    ("unit scale", value => value with { UnitScale = 1 }),
    ("datum", value => value with { Datum = "WGS84" }),
    ("ellipsoid", value => value with { Ellipsoid = "Other" }),
    ("projection", value => value with { ProjectionCode = 99 }),
    ("quadrant", value => value with { Quadrant = -1 }),
    ("offset", value => value with { OffsetX = 984251 }),
    ("scale", value => value with { ScaleReduction = 1.000136 }),
    ("parameter", value => value with { Parameter = -115 }),
    ("nonfinite", value => value with { OffsetY = double.NaN }),
    ("invalid", value => value with { Valid = false }),
    ("nonprojected", value => value with { Type = 1 }),
    ("nongeodetic", value => value with { Geodetic = false }),
    ("parameter bound", value => value with { ParameterCount = 25 })
})
    Test("native property mismatch: " + mutation.Name, () =>
    {
        FakeMapGuide.ParsedDefinitions[Wkt()] = mutation.Change(FakeMapGuide.Definitions[FakeMapGuide.Lvhef]);
        Reject(() => Verify(Wkt(), Wkt()));
    });
foreach (string method in new[] { "GetCatalog", "GetMathComparator", "GetProjectionParameter:1", "Factory.IsValid", "Create", "SameCoordinateSystem" })
    Test("cleanup after native failure: " + method, () => { FakeMapGuide.ThrowOn = method; Reject(() => Verify(Wkt(), Wkt()), "Injected Map failure"); });
Test("cleanup failure prevents success", () => { FakeMapGuide.FailDispose = true; Reject(() => Verify(Wkt(), Wkt()), "fully disposed"); });
Test("null native definition", () => { FakeMapGuide.ReturnNullDefinition = true; Reject(() => Verify(Wkt(), Wkt()), "returned null"); });
Test("WKT length bound", () => Reject(() => Verify(new string('x', 32769), Wkt()), "oversized"));
Test("NUL definition rejected", () => Reject(() => Verify(Wkt() + "\0", Wkt()), "invalid CRS"));
Test("large exponent rejected", () => Hostile(Wkt().Replace("984250.0000", "1e9999"), "exponent exceeds"));
Test("NaN token rejected", () => Hostile(Wkt().Replace("984250.0000", "NaN"), "expected"));
foreach (string malformed in new[] { "1e", "1e+", "1e-" })
    Test("malformed exponent rejected: " + malformed, () => Hostile(Wkt().Replace("984250.0000", malformed), "missing numeric exponent"));
Test("control character in quoted text rejected", () => Hostile(Wkt().Replace("\"NAD83\"", "\"NAD\u000183\""), "control character"));
foreach (Type wrapper in new[] { typeof(OSGeo.MapGuide.MgCoordinateSystemFactory), typeof(OSGeo.MapGuide.MgCoordinateSystemCatalog),
    typeof(OSGeo.MapGuide.MgCoordinateSystemMathComparator), typeof(OSGeo.MapGuide.MgCoordinateSystem) })
    Test("installed wrapper disposal contract: " + wrapper.Name, () =>
    {
        Check(wrapper.GetInterfaces().Length == 0, "Installed CRS wrappers implement no interfaces, including IDisposable.");
        var dispose = wrapper.GetMethod("Dispose", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
        Check(dispose != null && dispose.ReturnType == typeof(void) && dispose.IsVirtual && !dispose.IsFinal &&
            dispose.GetBaseDefinition().DeclaringType == typeof(OSGeo.MapGuide.MgObject),
            "Each installed wrapper declares public virtual non-final void Dispose(), overriding MgObject.");
        Check(wrapper.BaseType == typeof(OSGeo.MapGuide.MgGuardDisposable) &&
            wrapper.BaseType?.BaseType == typeof(OSGeo.MapGuide.MgDisposable) &&
            wrapper.BaseType?.BaseType?.BaseType == typeof(OSGeo.MapGuide.MgObject), "Installed base hierarchy mismatch.");
        Verify(Wkt(), Wkt());
    });
void CheckReverseDisposal()
{
    int[] opened = FakeMapGuide.Lifetime.Where(item => item.Operation == "open").Select(item => item.Id).ToArray();
    int[] disposed = FakeMapGuide.Lifetime.Where(item => item.Operation == "dispose").Select(item => item.Id).ToArray();
    Check(disposed.SequenceEqual(opened.Reverse()), "Every acquired wrapper must release exactly once in reverse order.");
}
Test("public void Dispose handles both WKT fields in reverse ownership order", () =>
{
    Verify(Wkt(), Wkt());
    Check(FakeMapGuide.Lifetime.Count(item => item.Operation == "open") == 6, "Expected six acquired wrappers.");
    CheckReverseDisposal();
});
Test("public void Dispose handles code-only named wrapper", () =>
{
    Verify(FakeMapGuide.Lvhef, string.Empty);
    Check(FakeMapGuide.Lifetime.Count(item => item.Operation == "open") == 5, "Expected named dictionary wrapper.");
    CheckReverseDisposal();
});
Test("same managed wrapper is disposed only once", () =>
{
    FakeMapGuide.ReuseManagedDefinition = true;
    Verify(Wkt(), Wkt());
    Check(FakeMapGuide.Lifetime.Count(item => item.Operation == "open") == 4, "Expected a reused managed CRS wrapper.");
    CheckReverseDisposal();
});
Test("distinct managed wrappers sharing a native definition each release", () =>
{
    Verify(Wkt(), Wkt());
    var pair = FakeMapGuide.Comparisons[0];
    Check(!ReferenceEquals(pair.First, pair.Second), "Test requires distinct managed wrappers.");
    Check(ReferenceEquals(((OSGeo.MapGuide.MgCoordinateSystem)pair.First).NativeIdentity,
        ((OSGeo.MapGuide.MgCoordinateSystem)pair.Second).NativeIdentity), "Test requires shared native definition identity.");
    CheckReverseDisposal();
});
Test("repeated verification preserves cached native catalog owner", () =>
{
    Verify(Wkt(), Wkt());
    Check(FakeMapGuide.CachedCatalogReferences == 1, "Catalog wrapper disposal must preserve the native static owner.");
    Verify(Wkt(), Wkt());
    Check(FakeMapGuide.CachedCatalogReferences == 1 && FakeMapGuide.LiveObjects == 0, "Repeated verification changed catalog lifetime.");
});
foreach (string kind in new[] { "MgCoordinateSystemFactory", "MgCoordinateSystemCatalog", "MgCoordinateSystemMathComparator", "MgCoordinateSystem" })
    Test("one wrapper disposal failure drains all acquisitions: " + kind, () =>
    {
        FakeMapGuide.FailDisposeKind = kind;
        Reject(() => Verify(Wkt(), Wkt()), "fully disposed");
        CheckReverseDisposal();
    });
Test("null catalog closes the already created factory", () =>
{
    FakeMapGuide.ReturnNullCatalog = true;
    Reject(() => Verify(Wkt(), Wkt()), "GetCatalog returned null");
    CheckReverseDisposal();
});
Test("null comparator closes earlier acquisitions", () =>
{
    FakeMapGuide.ReturnNullComparator = true;
    Reject(() => Verify(Wkt(), Wkt()), "GetMathComparator returned null");
    CheckReverseDisposal();
});
Test("null parsed CRS closes earlier acquisitions", () =>
{
    FakeMapGuide.ReturnNullParsed = true;
    Reject(() => Verify(Wkt(), Wkt()), "Create returned null");
    CheckReverseDisposal();
});
Console.WriteLine($"All {passed} CRS verification checks passed. Fake API only; Civil 3D native acceptance remains required.");
