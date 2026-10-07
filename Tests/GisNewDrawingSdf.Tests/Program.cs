using System.Collections.Generic;
using System.Security.Cryptography;
using CLV_CivilTools.Gis;

int passed = 0, failed = 0;
string folder = Path.Combine(Path.GetTempPath(), "clv-sdf-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
string path = Path.Combine(folder, "misleading-NV83.NCRS-LVHEF.sdf");
try
{
    Run("Correct native CRS overrides misleading filename; all rows/scalars preserved", () =>
    {
        var snapshot = Read();
        Equal(GisNewDrawingProfile.LvfCoordinateSystem, snapshot.CoordinateSystem);
        Equal(2, snapshot.PipeCount); Equal(2, snapshot.StructureCount);
        Equal("UFLS-Null Structure", snapshot.Structures[1].PartSizeName);
        Equal("Pipe 1", snapshot.Pipes[0].Name);
        Equal(9, snapshot.Pipes[0].Scalars.Count);
        Equal(2.0, snapshot.Pipes[0].Scalars["InsideDiameter"]);
        Equal(32.75, snapshot.Pipes[0].Scalars["Length"]);
        Equal(0.01, snapshot.Pipes[0].Scalars["Slope"]);
        Equal(2050.125, snapshot.Pipes[0].Scalars["StartInvert"]);
        Equal(2049.875, snapshot.Pipes[0].Scalars["EndInvert"]);
        Equal("Structure 1", snapshot.Pipes[0].Scalars["StructureStart"]);
        Equal("Stub 1", snapshot.Pipes[0].Scalars["StructureEnd"]);
        Equal(2, snapshot.Structures[0].Scalars.Count);
        Equal("PROJCS[\"actual test CRS\"]", snapshot.SpatialContexts.Single().CoordinateSystemWkt);
        True(FakeState.Events.Contains("Open.ReadOnly=TRUE"));
    });
    Run("Provider must expose explicit ReadOnly before any Open", () => { FakeState.HasReadOnly = false; Reject("ReadOnly"); Equal(0, FakeState.OpenCount); });
    Run("Provider must retain ReadOnly=TRUE before any Open", () => { FakeState.HonorsReadOnly = false; Reject("ReadOnly=TRUE"); Equal(0, FakeState.OpenCount); });
    Run("Provider must expose File before any Open", () => { FakeState.HasFile = false; Reject("File"); Equal(0, FakeState.OpenCount); });
    Run("Pending provider connection is rejected", () => { FakeState.OpenSucceeds = false; Reject("ConnectionState_Open"); });
    foreach (string crs in new[] { GisNewDrawingProfile.LvhefCoordinateSystem, "nv83.ncrs-lvf", "NV83.NCRS.LVF", "", "EPSG:26911" })
    {
        string actual = crs;
        Run("Native CRS mismatch/alias/unknown rejected: " + actual, () =>
        {
            FakeState.Contexts[0] = ("Default", actual, "PROJCS[\"NV83.NCRS-LVF\"]");
            Reject("does not exactly match"); Equal(0, FakeState.FeatureReadCount);
        });
    }
    Run("LVHEF succeeds only with exact LVHEF profile", () =>
    {
        FakeState.Contexts[0] = ("Default", GisNewDrawingProfile.LvhefCoordinateSystem, "PROJCS[\"LVHEF\"]");
        var snapshot = Read(GisNewDrawingProfile.LvhefCoordinateSystem);
        Equal(GisNewDrawingProfile.LvhefCoordinateSystem, snapshot.CoordinateSystem);
    });
    Run("Missing spatial contexts fail closed", () => { FakeState.Contexts.Clear(); Reject("no spatial context"); });
    Run("Duplicate spatial-context names fail closed", () => { FakeState.Contexts.Add(FakeState.Contexts[0]); Reject("duplicate spatial-context"); });
    Run("Missing WKT fails even when native code matches", () => { FakeState.Contexts[0] = ("Default", GisNewDrawingProfile.LvfCoordinateSystem, ""); Reject("no coordinate-system WKT"); });
    Run("Each class must resolve its actual spatial-context association", () => { Structures().Association = "missing"; Reject("unresolved/ambiguous"); });
    Run("Blank association accepted only with one real spatial context", () => { Pipes().Association = Structures().Association = ""; Read(); });
    Run("Blank association with multiple contexts is ambiguous", () =>
    {
        Pipes().Association = "";
        FakeState.Contexts.Add(("Other", GisNewDrawingProfile.LvfCoordinateSystem, "PROJCS[\"other\"]"));
        Reject("unresolved/ambiguous");
    });
    Run("Duplicate Name rows are retained with distinct scalars and geometry", () =>
    {
        Pipes().Rows[1]["Name"] = "Pipe 1"; Pipes().Rows[1]["Length"] = 77.0;
        var snapshot = Read(); Equal(2, snapshot.PipeCount);
        Equal(snapshot.Pipes[0].Name, snapshot.Pipes[1].Name);
        Equal(77.0, snapshot.Pipes[1].Scalars["Length"]);
        True(snapshot.Pipes[0].GeometrySha256 != snapshot.Pipes[1].GeometrySha256);
    });
    Run("Missing required source class fails with no partial snapshot", () => { FakeState.Classes.Remove(GisNewDrawingProfile.StructuresInputClass); Reject("KeyNotFoundException"); });
    Run("Different returned class identity fails", () => { Pipes().ReturnedName = "Other:Pipes"; Reject("different class definition"); });
    Run("Missing mapped field fails before reading features", () => { Pipes().MissingProperty = "Length"; Reject("Missing mapped property"); Equal(0, FakeState.FeatureReadCount); });
    Run("Unsupported scalar type is not converted lossily", () => { Pipes().UnsupportedTypeProperty = "Length"; Reject("unsupported FDO scalar type"); });
    Run("Null mapped scalars are preserved", () =>
    {
        Pipes().Rows[0]["Slope"] = null; Structures().Rows[0]["Name"] = null;
        var snapshot = Read(); Equal<object?>(null, snapshot.Pipes[0].Scalars["Slope"]);
        Equal<object?>(null, snapshot.Structures[0].Scalars["Name"]); Equal("", snapshot.Structures[0].Name);
    });
    Run("Nonfinite scalar rejected", () => { Pipes().Rows[0]["Length"] = double.NaN; Reject("nonfinite scalar"); });
    Run("Null feature geometry rejected", () => { Pipes().Rows[0]["Geometry"] = null; Reject("null geometry"); });
    Run("Geometry snapshots survive native buffer reuse and cannot be changed through getter", () =>
    {
        var snapshot = Read(); byte[] expected = { 1, 2, 3, 4 };
        True(expected.SequenceEqual(snapshot.Pipes[0].GeometryBytes));
        Equal(Convert.ToHexString(SHA256.HashData(expected)), snapshot.Pipes[0].GeometrySha256);
        byte[] exposed = snapshot.Pipes[0].GeometryBytes; exposed[0] = 200;
        True(expected.SequenceEqual(snapshot.Pipes[0].GeometryBytes));
        var scalars = (IDictionary<string, object?>)snapshot.Pipes[0].Scalars;
        Throws<NotSupportedException>(() => scalars["Name"] = "changed");
    });
    Run("Native Point/LineString coordinates captured with absent Z preserved", () =>
    {
        var snapshot = Read(); Equal("LineString", snapshot.Pipes[0].GeometryType);
        Equal(2, snapshot.Pipes[0].Coordinates.Count);
        Equal(new GisNewDrawingSdfCoordinate(1.5, 2.5, null), snapshot.Pipes[0].Coordinates[0]);
        Equal(new GisNewDrawingSdfCoordinate(3.5, 4.5, null), snapshot.Pipes[0].Coordinates[1]);
        Equal("Point", snapshot.Structures[0].GeometryType);
        Equal(new GisNewDrawingSdfCoordinate(9.5, 10.5, null), snapshot.Structures[0].Coordinates.Single());
        var coordinates = (IList<GisNewDrawingSdfCoordinate>)snapshot.Pipes[0].Coordinates;
        Throws<NotSupportedException>(() => coordinates[0] = new(0, 0, 0));
    });
    Run("Native XYZ coordinates preserve Z", () =>
    {
        var geometry = FakeState.Geometries["01020304"]; geometry.Dimensionality = 1;
        geometry.Coordinates = new[] { (1.5, 2.5, (double?)30), (3.5, 4.5, (double?)31) };
        var snapshot = Read(); Equal<double?>(30, snapshot.Pipes[0].Coordinates[0].Z);
        Equal<double?>(31, snapshot.Pipes[0].Coordinates[1].Z);
    });
    foreach (string kind in new[] { "GeometryType_CurveString", "GeometryType_MultiLineString", "GeometryType_Polygon" })
    {
        string type = kind;
        Run("Unsupported geometry is never flattened: " + type, () => { FakeState.Geometries["01020304"].Type = type; Reject("unsupported native geometry"); });
    }
    Run("Wrong per-class native geometry rejected", () => { FakeState.Geometries["01020304"].Type = "GeometryType_Point"; Reject("expected LineString"); });
    Run("M ordinate is rejected rather than discarded", () => { FakeState.Geometries["01020304"].Dimensionality = 2; Reject("unsupported ordinate dimensionality"); });
    Run("Position dimensionality must match geometry", () => { FakeState.Geometries["01020304"].PositionDimensionality = 1; Reject("dimensionalities disagree"); });
    Run("Nonfinite geometry coordinate rejected", () =>
    {
        FakeState.Geometries["01020304"].Coordinates[0] = (double.NaN, 2, null);
        Reject("nonfinite coordinates");
    });
    Run("Too-short LineString rejected", () =>
    {
        FakeState.Geometries["01020304"].Coordinates = new[] { (1.0, 2.0, (double?)null) };
        Reject("vertex count");
    });
    Run("Native geometry decoder failure disposes all wrappers", () => { FakeState.FailGeometryDecode = true; Reject("Injected native geometry decoder failure"); });
    Run("Source scalar mutation after preflight does not alter snapshot", () =>
    {
        var snapshot = Read(); Pipes().Rows[0]["Name"] = "changed";
        Equal("Pipe 1", snapshot.Pipes[0].Name);
    });
    Run("Unchanged file verifies length timestamp and SHA256", () =>
    {
        var snapshot = Read(); Equal(new FileInfo(path).Length, snapshot.FileLength);
        Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))), snapshot.FileSha256);
        True(GisNewDrawingSdf.TryVerifyUnchanged(snapshot, out string detail), detail);
    });
    Run("Same length and restored timestamp cannot hide changed file bytes", () =>
    {
        var snapshot = Read(); byte[] changed = File.ReadAllBytes(path); changed[0] ^= 0x01;
        File.WriteAllBytes(path, changed); File.SetLastWriteTimeUtc(path, snapshot.LastWriteTimeUtc);
        True(!GisNewDrawingSdf.TryVerifyUnchanged(snapshot, out string detail)); True(detail.Contains("differs"));
    });
    Run("Missing file invalidates snapshot", () =>
    {
        var snapshot = Read(); File.Delete(path);
        True(!GisNewDrawingSdf.TryVerifyUnchanged(snapshot, out string detail)); True(detail.Contains("verification failed"));
    });
    Run("Spatial reader exception disposes all acquired resources", () => { FakeState.FailSpatialRead = true; Reject("Injected spatial reader failure"); });
    Run("Feature reader exception closes/disposes all acquired resources", () => { FakeState.FailFeatureRead = true; Reject("Injected feature reader failure"); });
    Run("Reader Close exception still disposes reader command and connection", () => { FakeState.FailReaderClose = true; Reject("fully closed/disposed"); });
    Run("Reader Dispose exception still releases command and connection", () => { FakeState.FailReaderDispose = true; Reject("fully closed/disposed"); });
}
finally { Directory.Delete(folder, recursive: true); }
Console.WriteLine($"{passed} passed; {failed} failed. Fake FDO only; no native SDF/Autodesk runtime tested.");
return failed == 0 ? 0 : 1;

void Run(string name, Action test)
{
    FakeState.Reset(); File.WriteAllBytes(path, new byte[] { 41, 42, 43, 44, 45, 46 });
    try
    {
        test();
        True(FakeState.Objects.All(value => value.DisposeCount == 1), "Every acquired native wrapper must be disposed exactly once.");
        True(FakeState.Objects.OfType<FakeConnection>().All(value => value.CloseCount == 1), "Every acquired connection must be closed.");
        True(FakeState.Objects.OfType<FakeFeatureReader>().All(value => value.CloseCount == 1), "Every acquired feature reader must be closed.");
        passed++; Console.WriteLine("PASS " + name);
    }
    catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + ex); }
}
GisNewDrawingSdfSnapshot Read(string crs = GisNewDrawingProfile.LvfCoordinateSystem)
{
    var profile = Profile(crs);
    True(GisNewDrawingSdf.TryRead(path, profile, out var snapshot, out string detail), detail);
    return snapshot ?? throw new Exception("Successful read returned no snapshot.");
}
void Reject(string expected)
{
    True(!GisNewDrawingSdf.TryRead(path, Profile(GisNewDrawingProfile.LvfCoordinateSystem), out var snapshot, out string detail), "Expected failure.");
    True(snapshot == null, "Failure returned a partial snapshot.");
    True(detail.Contains(expected, StringComparison.OrdinalIgnoreCase), "Unexpected failure: " + detail);
}
GisNewDrawingProfile Profile(string crs)
{
    string filename = crs == GisNewDrawingProfile.LvfCoordinateSystem ? GisNewDrawingProfile.LvfProfileFileName : GisNewDrawingProfile.LvhefProfileFileName;
    True(GisNewDrawingProfile.TryLoad(Path.Combine(AppContext.BaseDirectory, "Fixtures", filename), crs, out var profile, out string detail), detail);
    return profile!;
}
FakeClassData Pipes() => FakeState.Classes[GisNewDrawingProfile.PipesInputClass];
FakeClassData Structures() => FakeState.Classes[GisNewDrawingProfile.StructuresInputClass];
static void True(bool value, string detail = "Assertion failed.") { if (!value) throw new Exception(detail); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}."); }
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
