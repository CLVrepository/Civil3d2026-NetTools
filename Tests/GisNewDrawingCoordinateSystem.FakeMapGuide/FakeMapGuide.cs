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
        public static int LiveObjects { get; private set; }
        public static string? ThrowOn { get; set; }
        public static bool ComparatorResult { get; set; } = true;
        public static Func<object, object, bool>? ComparatorOverride { get; set; }
        public static bool CompareDatumParameters { get; set; } = true;
        public static bool WktValid { get; set; } = true;
        public static bool FailDispose { get; set; }
        public static bool ReturnNullDefinition { get; set; }

        static FakeMapGuide() => Reset();
        public static void Reset()
        {
            if (LiveObjects != 0) throw new InvalidOperationException("A previous verifier leaked native wrappers.");
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
        internal static void Open(string kind) { Hit(kind + ".ctor"); LiveObjects++; }
        internal static void Close(string kind)
        {
            LiveObjects--;
            Calls.Add(kind + ".Dispose");
            if (FailDispose) throw new InvalidOperationException("Injected dispose failure.");
        }
    }
}

namespace OSGeo.MapGuide
{
    using GisCoordinateSystemTestSupport;

    public abstract class MgGuardDisposable : IDisposable
    {
        private readonly string kind;
        private bool disposed;
        protected MgGuardDisposable(string kind) { this.kind = kind; FakeMapGuide.Open(kind); }
        public void Dispose()
        {
            if (disposed) throw new InvalidOperationException("Double disposal.");
            disposed = true;
            FakeMapGuide.Close(kind);
        }
    }
    public sealed class MgCoordinateSystemFactory : MgGuardDisposable
    {
        public MgCoordinateSystemFactory() : base(nameof(MgCoordinateSystemFactory)) { }
        public MgCoordinateSystemCatalog GetCatalog() { FakeMapGuide.Hit(nameof(GetCatalog)); return new(); }
        public MgCoordinateSystem? CreateFromCode(string code)
        {
            FakeMapGuide.Hit(nameof(CreateFromCode) + ":" + code);
            if (FakeMapGuide.ReturnNullDefinition) return null;
            return new(FakeMapGuide.Definitions[code]);
        }
        public bool IsValid(string wkt) { FakeMapGuide.Hit("Factory.IsValid"); return FakeMapGuide.WktValid; }
        public MgCoordinateSystem Create(string wkt)
        {
            FakeMapGuide.Hit(nameof(Create));
            return new(FakeMapGuide.ParsedDefinitions[wkt]);
        }
    }
    public sealed class MgCoordinateSystemCatalog : MgGuardDisposable
    {
        public MgCoordinateSystemCatalog() : base(nameof(MgCoordinateSystemCatalog)) { }
        public MgCoordinateSystemMathComparator GetMathComparator() { FakeMapGuide.Hit(nameof(GetMathComparator)); return new(); }
    }
    public sealed class MgCoordinateSystemMathComparator : MgGuardDisposable
    {
        public MgCoordinateSystemMathComparator() : base(nameof(MgCoordinateSystemMathComparator)) { }
        public bool GetCompareInternalDatumOldParameters() { FakeMapGuide.Hit(nameof(GetCompareInternalDatumOldParameters)); return FakeMapGuide.CompareDatumParameters; }
        public bool SameCoordinateSystem(MgCoordinateSystem first, MgCoordinateSystem second)
        {
            FakeMapGuide.Hit(nameof(SameCoordinateSystem));
            FakeMapGuide.Comparisons.Add((first, second));
            return FakeMapGuide.ComparatorOverride?.Invoke(first, second) ?? FakeMapGuide.ComparatorResult;
        }
    }
    public sealed class MgCoordinateSystem : MgGuardDisposable
    {
        private readonly FakeDefinition definition;
        public MgCoordinateSystem(FakeDefinition definition) : base(nameof(MgCoordinateSystem)) { this.definition = definition; }
        private T Read<T>(string method, T value) { FakeMapGuide.Hit(method); return value; }
        public string GetCsCode() => Read(nameof(GetCsCode), definition.Code);
        public bool IsValid() => Read(nameof(IsValid), definition.Valid);
        public bool IsUsable(MgCoordinateSystemCatalog catalog) => Read(nameof(IsUsable), definition.Usable);
        public bool IsGeodetic() => Read(nameof(IsGeodetic), definition.Geodetic);
        public new int GetType() => Read(nameof(GetType), definition.Type);
        public int GetUnitCode() => Read(nameof(GetUnitCode), definition.UnitCode);
        public double GetUnitScale() => Read(nameof(GetUnitScale), definition.UnitScale);
        public int GetProjectionCode() => Read(nameof(GetProjectionCode), definition.ProjectionCode);
        public int GetProjectionParameterCount() => Read(nameof(GetProjectionParameterCount), definition.ParameterCount);
        public double GetProjectionParameter(int index)
        {
            if (index < 1 || index > definition.ParameterCount) throw new ArgumentOutOfRangeException(nameof(index));
            return Read(nameof(GetProjectionParameter) + ":" + index, definition.Parameter);
        }
        public short GetQuadrant() => Read(nameof(GetQuadrant), definition.Quadrant);
        public string GetDatum() => Read(nameof(GetDatum), definition.Datum);
        public string GetEllipsoid() => Read(nameof(GetEllipsoid), definition.Ellipsoid);
        public double GetOffsetX() => Read(nameof(GetOffsetX), definition.OffsetX);
        public double GetOffsetY() => Read(nameof(GetOffsetY), definition.OffsetY);
        public double GetScaleReduction() => Read(nameof(GetScaleReduction), definition.ScaleReduction);
        public double GetMapScale() => Read(nameof(GetMapScale), definition.MapScale);
        public double GetOriginLongitude() => Read(nameof(GetOriginLongitude), definition.OriginLongitude);
        public double GetOriginLatitude() => Read(nameof(GetOriginLatitude), definition.OriginLatitude);
        public override string ToString() => Read(nameof(ToString), definition.Wkt);
    }
}
