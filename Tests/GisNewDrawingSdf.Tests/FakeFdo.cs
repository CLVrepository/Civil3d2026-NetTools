// Deliberately tiny test-only FDO facade. These are not Autodesk binaries and
// the synthetic geometry/file bytes are not an SDF implementation.
using CLV_CivilTools.Gis;
using OSGeo.FDO.Commands;

namespace OSGeo.FDO.ClientServices
{
    public static class FeatureAccessManager
    {
        public static FakeConnectionManager GetConnectionManager() => new();
    }
}
namespace OSGeo.FDO.Commands
{
    public enum CommandType { CommandType_GetSpatialContexts, CommandType_Select }
}

public static class FakeState
{
    public static readonly List<FakeDisposable> Objects = new();
    public static readonly List<string> Events = new();
    public static readonly Dictionary<string, FakeClassData> Classes = new(StringComparer.Ordinal);
    public static readonly Dictionary<string, FakeGeometryData> Geometries = new(StringComparer.Ordinal);
    public static bool FailGeometryDecode;
    public static List<(string Name, string Crs, string Wkt)> Contexts = new();
    public static bool HasReadOnly = true;
    public static bool HonorsReadOnly = true;
    public static bool HasFile = true;
    public static bool OpenSucceeds = true;
    public static bool FailSpatialRead;
    public static bool FailFeatureRead;
    public static bool FailReaderClose;
    public static bool FailReaderDispose;
    public static int OpenCount;
    public static int FeatureReadCount;

    public static void Reset()
    {
        Objects.Clear(); Events.Clear(); Classes.Clear(); Geometries.Clear();
        FailGeometryDecode = false;
        HasReadOnly = HonorsReadOnly = HasFile = OpenSucceeds = true;
        FailSpatialRead = FailFeatureRead = FailReaderClose = FailReaderDispose = false;
        OpenCount = FeatureReadCount = 0;
        Contexts = new() { ("Default", GisNewDrawingProfile.LvfCoordinateSystem, "PROJCS[\"actual test CRS\"]") };
        var pipes = new FakeClassData(GisNewDrawingProfile.PipesInputClass);
        pipes.Rows.Add(Pipe("Pipe 1", 1)); pipes.Rows.Add(Pipe("Pipe 2", 5));
        var structures = new FakeClassData(GisNewDrawingProfile.StructuresInputClass);
        structures.Rows.Add(new() { ["Name"] = "Structure 1", ["PartSizeName"] = "UFLS-Access Structure", ["Geometry"] = new byte[] { 9, 10, 11, 12 } });
        structures.Rows.Add(new() { ["Name"] = "Stub 1", ["PartSizeName"] = "UFLS-Null Structure", ["Geometry"] = new byte[] { 13, 14, 15, 16 } });
        Classes.Add(pipes.Name, pipes); Classes.Add(structures.Name, structures);
        // Synthetic test keys, not FGF decoding. The native decoder is represented
        // by explicit geometry objects so the production accessor path is tested.
        Geometries.Add("01020304", new("GeometryType_LineString", (1.5, 2.5, null), (3.5, 4.5, null)));
        Geometries.Add("05060708", new("GeometryType_LineString", (5.5, 6.5, null), (7.5, 8.5, null)));
        Geometries.Add("090A0B0C", new("GeometryType_Point", (9.5, 10.5, null)));
        Geometries.Add("0D0E0F10", new("GeometryType_Point", (13.5, 14.5, null)));
    }
    private static Dictionary<string, object?> Pipe(string name, byte first) => new()
    {
        ["Name"] = name, ["PartSizeName"] = "24 inch Pipe", ["InsideDiameter"] = 2.0,
        ["Length"] = 32.75, ["Slope"] = 0.01, ["StartInvert"] = 2050.125,
        ["EndInvert"] = 2049.875, ["StructureStart"] = "Structure 1", ["StructureEnd"] = "Stub 1",
        ["Geometry"] = new byte[] { first, (byte)(first + 1), (byte)(first + 2), (byte)(first + 3) }
    };
}
public sealed class FakeClassData
{
    public FakeClassData(string name) { Name = name; }
    public string Name { get; }
    public string Association { get; set; } = "Default";
    public string? ReturnedName { get; set; }
    public string? MissingProperty { get; set; }
    public string? UnsupportedTypeProperty { get; set; }
    public List<Dictionary<string, object?>> Rows { get; } = new();
}
public abstract class FakeDisposable : IDisposable
{
    protected FakeDisposable() { FakeState.Objects.Add(this); }
    public int DisposeCount { get; private set; }
    public virtual void Dispose() { DisposeCount++; FakeState.Events.Add(GetType().Name + ".Dispose"); }
}
public sealed class FakeConnectionManager : FakeDisposable
{
    public FakeConnection CreateConnection(string provider)
    {
        if (provider != "OSGeo.SDF") throw new InvalidOperationException("Unexpected provider.");
        return new();
    }
}
public sealed class FakeConnection : FakeDisposable
{
    private readonly Dictionary<string, string> settings = new();
    public FakeConnectionInfo ConnectionInfo => new(settings);
    public string Open()
    {
        FakeState.OpenCount++;
        if (settings.GetValueOrDefault("ReadOnly") != "TRUE" || !settings.ContainsKey("File"))
            throw new InvalidOperationException("Opened without explicit File/ReadOnly=TRUE.");
        FakeState.Events.Add("Open.ReadOnly=TRUE");
        return FakeState.OpenSucceeds ? "ConnectionState_Open" : "ConnectionState_Pending";
    }
    public FakeDisposable CreateCommand(CommandType kind) => kind switch
    {
        CommandType.CommandType_GetSpatialContexts => new FakeSpatialCommand(),
        CommandType.CommandType_Select => new FakeSelectCommand(),
        _ => throw new InvalidOperationException("Unexpected command; writes are prohibited.")
    };
    public int CloseCount { get; private set; }
    public void Close() { CloseCount++; FakeState.Events.Add("Connection.Close"); }
}
public sealed class FakeConnectionInfo : FakeDisposable
{
    private readonly Dictionary<string, string> settings;
    public FakeConnectionInfo(Dictionary<string, string> settings) { this.settings = settings; }
    public FakeConnectionProperties ConnectionProperties => new(settings);
}
public sealed class FakeConnectionProperties : FakeDisposable
{
    private readonly Dictionary<string, string> settings;
    public FakeConnectionProperties(Dictionary<string, string> settings) { this.settings = settings; }
    public string[] PropertyNames => new[] { FakeState.HasFile ? "File" : "OtherFile", FakeState.HasReadOnly ? "ReadOnly" : "OtherReadOnly" };
    public void SetProperty(string name, string value) { settings[name] = name == "ReadOnly" && !FakeState.HonorsReadOnly ? "FALSE" : value; }
    public string GetProperty(string name) => settings[name];
}
public sealed class FakeSpatialCommand : FakeDisposable
{
    public bool ActiveOnly { get; set; } = true;
    public FakeSpatialReader Execute()
    {
        if (ActiveOnly) throw new InvalidOperationException("Did not request all spatial contexts.");
        return new();
    }
}
public sealed class FakeSpatialReader : FakeDisposable
{
    private int index = -1;
    public bool ReadNext()
    {
        if (FakeState.FailSpatialRead) throw new IOException("Injected spatial reader failure.");
        return ++index < FakeState.Contexts.Count;
    }
    public string GetName() => FakeState.Contexts[index].Name;
    public string GetCoordinateSystem() => FakeState.Contexts[index].Crs;
    public string GetCoordinateSystemWkt() => FakeState.Contexts[index].Wkt;
}
public sealed class FakeSelectCommand : FakeDisposable
{
    private string className = string.Empty;
    public void SetFeatureClassName(string value) { className = value; }
    public FakeFeatureReader Execute() => new(FakeState.Classes[className]);
}
public sealed class FakeFeatureReader : FakeDisposable
{
    private readonly FakeClassData data;
    private readonly byte[] buffer = new byte[4];
    private int index = -1;
    public FakeFeatureReader(FakeClassData data) { this.data = data; }
    public FakeClassDefinition GetClassDefinition() => new(data);
    public bool ReadNext()
    {
        FakeState.FeatureReadCount++;
        if (FakeState.FailFeatureRead) throw new IOException("Injected feature reader failure.");
        Array.Clear(buffer);
        if (++index >= data.Rows.Count) return false;
        if (data.Rows[index]["Geometry"] is byte[] geometry) geometry.CopyTo(buffer, 0);
        return true;
    }
    public bool IsNull(string name) => data.Rows[index][name] == null;
    public string GetString(string name) => (string)data.Rows[index][name]!;
    public double GetDouble(string name) => (double)data.Rows[index][name]!;
    public byte[] GetGeometry(string name) => name == "Geometry" ? buffer : throw new InvalidOperationException("Unexpected geometry.");
    public int CloseCount { get; private set; }
    public void Close()
    {
        CloseCount++; Array.Clear(buffer); FakeState.Events.Add("Reader.Close");
        if (FakeState.FailReaderClose) throw new IOException("Injected reader Close failure.");
    }
    public override void Dispose()
    {
        base.Dispose();
        if (FakeState.FailReaderDispose) throw new IOException("Injected reader Dispose failure.");
    }
}
public sealed class FakeClassDefinition : FakeDisposable
{
    private readonly FakeClassData data;
    public FakeClassDefinition(FakeClassData data) { this.data = data; }
    public string QualifiedName => data.ReturnedName ?? data.Name;
    public FakeGeometryProperty GeometryProperty => new(data.Association);
    public FakeProperties Properties => new(data);
}
public sealed class FakeGeometryProperty : FakeDisposable
{
    public FakeGeometryProperty(string association) { SpatialContextAssociation = association; }
    public string Name => "Geometry";
    public string SpatialContextAssociation { get; }
}
public sealed class FakeProperties : FakeDisposable
{
    private readonly FakeClassData data;
    public FakeProperties(FakeClassData data) { this.data = data; }
    public FakeDataProperty this[string name] => name == data.MissingProperty
        ? throw new KeyNotFoundException("Missing mapped property " + name)
        : new(name, name == data.UnsupportedTypeProperty);
}
public sealed class FakeDataProperty : FakeDisposable
{
    public FakeDataProperty(string name, bool unsupported)
    {
        DataType = unsupported ? "DataType_Decimal" : name is "Name" or "PartSizeName" or "StructureStart" or "StructureEnd"
            ? "DataType_String" : "DataType_Double";
    }
    public string PropertyType => "PropertyType_DataProperty";
    public string DataType { get; }
}

namespace OSGeo.FDO.Geometry
{
    public sealed class FgfGeometryFactory : FakeDisposable
    {
        public FakeGeometry CreateGeometryFromFgf(byte[] bytes)
        {
            if (FakeState.FailGeometryDecode) throw new IOException("Injected native geometry decoder failure.");
            return new(FakeState.Geometries[Convert.ToHexString(bytes)]);
        }
    }
}
public sealed class FakeGeometryData
{
    public FakeGeometryData(string type, params (double X, double Y, double? Z)[] coordinates)
    {
        Type = type; Coordinates = coordinates;
    }
    public string Type { get; set; }
    public int Dimensionality { get; set; }
    public int? PositionDimensionality { get; set; }
    public (double X, double Y, double? Z)[] Coordinates { get; set; }
}
public sealed class FakeGeometry : FakeDisposable
{
    private readonly FakeGeometryData data;
    public FakeGeometry(FakeGeometryData data) { this.data = data; }
    public string DerivedType => data.Type;
    public int Dimensionality => data.Dimensionality;
    public FakePosition Position => new(data, 0);
    public FakePositions Positions => new(data);
}
public sealed class FakePositions : FakeDisposable
{
    private readonly FakeGeometryData data;
    public FakePositions(FakeGeometryData data) { this.data = data; }
    public int Count => data.Coordinates.Length;
    public FakePosition this[int index] => new(data, index);
}
public sealed class FakePosition : FakeDisposable
{
    private readonly FakeGeometryData data;
    private readonly int index;
    public FakePosition(FakeGeometryData data, int index) { this.data = data; this.index = index; }
    public int Dimensionality => data.PositionDimensionality ?? data.Dimensionality;
    public double X => data.Coordinates[index].X;
    public double Y => data.Coordinates[index].Y;
    public double Z => data.Coordinates[index].Z ?? throw new InvalidOperationException("Must not read Z for XY geometry.");
}
