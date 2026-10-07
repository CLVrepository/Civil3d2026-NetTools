using System;
using System.Collections.Generic;

// Test-only ABI facade. Never reference/copy this assembly into the Civil plugin.
// It deliberately allows a WKT parse to snap to a dictionary definition, proving
// that the production structural guard cannot be bypassed by such native behavior.
namespace GisCoordinateSystemTestSupport
{
    public sealed record FakeDefinition(string Code, string Wkt)
    {
        public bool Valid { get; init; } = true;
        public bool Usable { get; init; } = true;
        public bool Geodetic { get; init; } = true;
        public int Type { get; init; } = 3;
        public int UnitCode { get; init; } = 9003;
        public double UnitScale { get; init; } = 1200.0 / 3937.0;
        public int ProjectionCode { get; init; } = 3;
        public short Quadrant { get; init; } = 1;
        public int ParameterCount { get; init; } = 1;
        public string Datum { get; init; } = "NAD83";
        public string Ellipsoid { get; init; } = "GRS1980";
        public double Parameter { get; init; } = -114.96666666666667;
        public double OffsetX { get; init; } = 984250;
        public double OffsetY { get; init; } = 1312333.3333;
        public double ScaleReduction { get; init; } = 1.000135;
        public double MapScale { get; init; } = 1;
        public double OriginLatitude { get; init; } = 36.25;
        public double OriginLongitude { get; init; }
    }

    public static class FakeMapGuide
    {
        public const string Lvf = "NV83.NCRS-LVF";
        public const string Lvhef = "NV83.NCRS-LVHEF";
        public static Dictionary<string, FakeDefinition> Definitions { get; } = new(StringComparer.Ordinal);
        public static Dictionary<string, FakeDefinition> ParsedDefinitions { get; } = new(StringComparer.Ordinal);
        public static List<string> Calls { get; } = new();
        public static List<(object First, object Second)> Comparisons { get; } = new();
        public static List<(string Operation, int Id, string Kind)> Lifetime { get; } = new();
        public static int CachedCatalogReferences { get; private set; } = 1;
        private static int nextId;
        public static int LiveObjects { get; private set; }
        public static string? ThrowOn { get; set; }
        public static bool ComparatorResult { get; set; } = true;
        public static Func<object, object, bool>? ComparatorOverride { get; set; }
        public static bool CompareDatumParameters { get; set; } = true;
        public static bool WktValid { get; set; } = true;
        public static bool FailDispose { get; set; }
        public static string? FailDisposeKind { get; set; }
        public static bool ReturnNullCatalog { get; set; }
        public static bool ReturnNullComparator { get; set; }
        public static bool ReturnNullParsed { get; set; }
        public static bool ReuseManagedDefinition { get; set; }
        public static bool ReturnNullDefinition { get; set; }

        static FakeMapGuide() => Reset();
        public static void Reset()
        {
            if (LiveObjects != 0 || CachedCatalogReferences != 1) throw new InvalidOperationException("A previous verifier leaked native wrappers or changed the shared catalog owner.");
            Lifetime.Clear();
            nextId = 0;
            Calls.Clear();
            Comparisons.Clear();
            Definitions.Clear();
            ParsedDefinitions.Clear();
            ThrowOn = null;
            ComparatorResult = true;
            ComparatorOverride = null;
            CompareDatumParameters = true;
            WktValid = true;
            FailDispose = false;
            FailDisposeKind = null;
            ReturnNullCatalog = false;
            ReturnNullComparator = false;
            ReturnNullParsed = false;
            ReuseManagedDefinition = false;
            ReturnNullDefinition = false;
            foreach (string code in new[] { Lvf, Lvhef })
            {
                string scale = code == Lvhef ? "1.000135000000" : "1.000000000000";
                string wkt = "PROJCS[\"" + code + "\",GEOGCS[\"LL83\",DATUM[\"NAD83\",SPHEROID[\"GRS1980\",6378137.000,298.25722210]]," +
                    "PRIMEM[\"Greenwich\",0],UNIT[\"Degree\",0.017453292519943295]],PROJECTION[\"Transverse_Mercator\"]," +
                    "PARAMETER[\"false_easting\",984250.0000],PARAMETER[\"false_northing\",1312333.3333]," +
                    "PARAMETER[\"scale_factor\"," + scale + "],PARAMETER[\"central_meridian\",-114.96666666666667]," +
                    "PARAMETER[\"latitude_of_origin\",36.25000000000000],UNIT[\"Foot_US\",0.30480060960122]]";
                var definition = new FakeDefinition(code, wkt) { ScaleReduction = code == Lvhef ? 1.000135 : 1 };
                Definitions.Add(code, definition);
                ParsedDefinitions.Add(wkt, definition);
            }
        }
        public static string Wkt(string code) => Definitions[code].Wkt;
        public static void RegisterWkt(string wkt, string code) => ParsedDefinitions[wkt.Trim()] = Definitions[code];
        public static void Hit(string method)
        {
            Calls.Add(method);
            if (ThrowOn == method) throw new InvalidOperationException("Injected Map failure: " + method);
        }
        internal static int Open(string kind)
        {
            Hit(kind + ".ctor");
            LiveObjects++;
            if (kind == "MgCoordinateSystemCatalog") CachedCatalogReferences++;
            int id = ++nextId;
            Lifetime.Add(("open", id, kind));
            return id;
        }
        internal static void Close(string kind, int id)
        {
            LiveObjects--;
            if (kind == "MgCoordinateSystemCatalog") CachedCatalogReferences--;
            Calls.Add(kind + ".Dispose");
            Lifetime.Add(("dispose", id, kind));
            if (FailDispose || FailDisposeKind == kind) throw new InvalidOperationException("Injected dispose failure.");
        }
    }
}

namespace OSGeo.MapGuide
{
    using GisCoordinateSystemTestSupport;

    // Mirrors installed Map 3D 2026 metadata: these wrappers have no interfaces,
    // and each derived wrapper declares an override of MgObject.Dispose().
    public abstract class MgObject { public virtual void Dispose() { } }
    public abstract class MgDisposable : MgObject { public override void Dispose() => base.Dispose(); }
    public abstract class MgGuardDisposable : MgDisposable
    {
        private readonly string kind;
        private readonly int id;
        private bool disposed;
        protected MgGuardDisposable(string kind) { this.kind = kind; id = FakeMapGuide.Open(kind); }
        public override void Dispose()
        {
            if (disposed) throw new InvalidOperationException("Double disposal.");
            disposed = true;
            FakeMapGuide.Close(kind, id);
        }
    }
    public class MgCoordinateSystemFactory : MgGuardDisposable
    {
        private MgCoordinateSystem? lastDefinition;
        public MgCoordinateSystemFactory() : base(nameof(MgCoordinateSystemFactory)) { }
        public override void Dispose() => base.Dispose();
        public virtual MgCoordinateSystemCatalog? GetCatalog() { FakeMapGuide.Hit(nameof(GetCatalog)); return FakeMapGuide.ReturnNullCatalog ? null : new(); }
        public virtual MgCoordinateSystem? CreateFromCode(string code)
        {
            FakeMapGuide.Hit(nameof(CreateFromCode) + ":" + code);
            if (FakeMapGuide.ReturnNullDefinition) return null;
            return lastDefinition = new(FakeMapGuide.Definitions[code]);
        }
        public virtual bool IsValid(string wkt) { FakeMapGuide.Hit("Factory.IsValid"); return FakeMapGuide.WktValid; }
        public virtual MgCoordinateSystem? Create(string wkt)
        {
            FakeMapGuide.Hit(nameof(Create));
            return FakeMapGuide.ReturnNullParsed ? null : FakeMapGuide.ReuseManagedDefinition ? lastDefinition : new(FakeMapGuide.ParsedDefinitions[wkt]);
        }
    }
    public class MgCoordinateSystemCatalog : MgGuardDisposable
    {
        public MgCoordinateSystemCatalog() : base(nameof(MgCoordinateSystemCatalog)) { }
        public override void Dispose() => base.Dispose();
        public virtual MgCoordinateSystemMathComparator? GetMathComparator() { FakeMapGuide.Hit(nameof(GetMathComparator)); return FakeMapGuide.ReturnNullComparator ? null : new(); }
    }
    public class MgCoordinateSystemMathComparator : MgGuardDisposable
    {
        public MgCoordinateSystemMathComparator() : base(nameof(MgCoordinateSystemMathComparator)) { }
        public override void Dispose() => base.Dispose();
        public virtual bool GetCompareInternalDatumOldParameters() { FakeMapGuide.Hit(nameof(GetCompareInternalDatumOldParameters)); return FakeMapGuide.CompareDatumParameters; }
        public virtual bool SameCoordinateSystem(MgCoordinateSystem first, MgCoordinateSystem second)
        {
            FakeMapGuide.Hit(nameof(SameCoordinateSystem));
            FakeMapGuide.Comparisons.Add((first, second));
            return FakeMapGuide.ComparatorOverride?.Invoke(first, second) ?? FakeMapGuide.ComparatorResult;
        }
    }
    public class MgCoordinateSystem : MgGuardDisposable
    {
        private readonly FakeDefinition definition;
        public object NativeIdentity => definition;
        public MgCoordinateSystem(FakeDefinition definition) : base(nameof(MgCoordinateSystem)) { this.definition = definition; }
        public override void Dispose() => base.Dispose();
        private T Read<T>(string method, T value) { FakeMapGuide.Hit(method); return value; }
        public virtual string GetCsCode() => Read(nameof(GetCsCode), definition.Code);
        public virtual bool IsValid() => Read(nameof(IsValid), definition.Valid);
        public virtual bool IsUsable(MgCoordinateSystemCatalog catalog) => Read(nameof(IsUsable), definition.Usable);
        public virtual bool IsGeodetic() => Read(nameof(IsGeodetic), definition.Geodetic);
        public new int GetType() => Read(nameof(GetType), definition.Type);
        public virtual int GetUnitCode() => Read(nameof(GetUnitCode), definition.UnitCode);
        public virtual double GetUnitScale() => Read(nameof(GetUnitScale), definition.UnitScale);
        public virtual int GetProjectionCode() => Read(nameof(GetProjectionCode), definition.ProjectionCode);
        public virtual int GetProjectionParameterCount() => Read(nameof(GetProjectionParameterCount), definition.ParameterCount);
        public virtual double GetProjectionParameter(int index)
        {
            if (index < 1 || index > definition.ParameterCount) throw new ArgumentOutOfRangeException(nameof(index));
            return Read(nameof(GetProjectionParameter) + ":" + index, definition.Parameter);
        }
        public virtual short GetQuadrant() => Read(nameof(GetQuadrant), definition.Quadrant);
        public virtual string GetDatum() => Read(nameof(GetDatum), definition.Datum);
        public virtual string GetEllipsoid() => Read(nameof(GetEllipsoid), definition.Ellipsoid);
        public virtual double GetOffsetX() => Read(nameof(GetOffsetX), definition.OffsetX);
        public virtual double GetOffsetY() => Read(nameof(GetOffsetY), definition.OffsetY);
        public virtual double GetScaleReduction() => Read(nameof(GetScaleReduction), definition.ScaleReduction);
        public virtual double GetMapScale() => Read(nameof(GetMapScale), definition.MapScale);
        public virtual double GetOriginLongitude() => Read(nameof(GetOriginLongitude), definition.OriginLongitude);
        public virtual double GetOriginLatitude() => Read(nameof(GetOriginLatitude), definition.OriginLatitude);
        public new string ToString() => Read(nameof(ToString), definition.Wkt);
    }
}
