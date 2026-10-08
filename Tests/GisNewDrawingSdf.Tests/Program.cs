using System.Collections.Generic;
using System.Reflection;
using System.Security.Cryptography;
using CLV_CivilTools.Gis;

int passed = 0, failed = 0;
string folder = Path.Combine(Path.GetTempPath(), "clv-sdf-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
string path = Path.Combine(folder, "misleading-NV83.NCRS-LVHEF.sdf");
try
{
    Run("Source drawing CRS overrides misleading filename; all rows/scalars preserved", () =>
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
        True(FakeState.Events.Contains("Open.ReadOnly=TRUE"));
    });
    Run("Provider must expose explicit ReadOnly before any Open", () => { FakeState.HasReadOnly = false; Reject("ReadOnly"); Equal(0, FakeState.OpenCount); });
    Run("Provider must retain ReadOnly=TRUE before any Open", () => { FakeState.HonorsReadOnly = false; Reject("ReadOnly=TRUE"); Equal(0, FakeState.OpenCount); });
    Run("Provider must expose File before any Open", () => { FakeState.HasFile = false; Reject("File"); Equal(0, FakeState.OpenCount); });
    Run("Pending provider connection is rejected", () => { FakeState.OpenSucceeds = false; Reject("ConnectionState_Open"); });
    foreach (string source in new[] { GisNewDrawingProfile.LvfCoordinateSystem, GisNewDrawingProfile.LvhefCoordinateSystem })
    {
        string sourceCrs = source;
        Run("Source CRS is authoritative with absent SDF spatial contexts: " + sourceCrs, () =>
        {
            FakeState.Contexts.Clear();
            var snapshot = Read(sourceCrs);
            Equal(sourceCrs, snapshot.CoordinateSystem);
            Equal(2, snapshot.PipeCount); Equal(2, snapshot.StructureCount);
        });
        foreach (var metadata in new[]
        {
            (Name: "Default", Crs: "", Wkt: ""),
            (Name: "Default", Crs: "EPSG:26911", Wkt: "PROJCS[\"different coordinate system\"]"),
            (Name: "Default", Crs: "malformed[", Wkt: "not WKT"),
            (Name: "Default", Crs: GisNewDrawingProfile.LvfCoordinateSystem, Wkt: "contradictory WKT"),
            (Name: "Default", Crs: GisNewDrawingProfile.LvhefCoordinateSystem, Wkt: "contradictory WKT")
        })
        {
            var context = metadata;
            Run("SDF CRS metadata cannot block source " + sourceCrs + ": " + context.Crs + "/" + context.Wkt, () =>
            {
                FakeState.Contexts[0] = context;
                var snapshot = Read(sourceCrs);
                Equal(sourceCrs, snapshot.CoordinateSystem);
                Equal(new GisNewDrawingSdfCoordinate(1.5, 2.5, null), snapshot.Pipes[0].Coordinates[0]);
                True(new byte[] { 1, 2, 3, 4 }.SequenceEqual(snapshot.Pipes[0].GeometryBytes));
            });
        }
    }
    Run("Duplicate SDF context names do not block raw feature reads", () =>
    {
        FakeState.Contexts.Add(FakeState.Contexts[0]); Read();
    });
    Run("Unknown geometry spatial-context association is never resolved", () =>
    {
        Structures().Association = "missing"; Read();
    });
    Run("Blank geometry association with multiple contexts does not block reads", () =>
    {
        Pipes().Association = Structures().Association = "";
        FakeState.Contexts.Add(("Other", "different CRS", "malformed WKT"));
        Read();
    });
    foreach (string unsupported in new[] { "", "nv83.ncrs-lvf", "NV83.NCRS.LVF", "EPSG:26911" })
    {
        string sourceCrs = unsupported;
        Run("Unsupported source drawing CRS rejected before FDO access: " + sourceCrs, () =>
        {
            // Bypass the normal profile parser to exercise the SDF boundary guard.
            var constructor = typeof(GisNewDrawingProfile).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
            var profile = (GisNewDrawingProfile)constructor.Invoke(new object[]
            {
                sourceCrs, "test-only invalid profile", Array.Empty<GisNewDrawingProfileTable>(), Array.Empty<string>()
            });
            True(!GisNewDrawingSdf.TryRead(path, profile, out var snapshot, out string detail));
            True(snapshot == null); True(detail.Contains("exact supported CLV coordinate system"), detail);
            Equal(0, FakeState.Objects.Count); Equal(0, FakeState.OpenCount);
        });
    }
    Run("Missing source profile rejected before FDO access", () =>
    {
        True(!GisNewDrawingSdf.TryRead(path, null!, out var snapshot, out string detail));
        True(snapshot == null); True(detail.Contains("exact supported CLV coordinate system"), detail);
        Equal(0, FakeState.Objects.Count); Equal(0, FakeState.OpenCount);
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
    Run("Attached exact class definitions pass with declared schema anchor", () =>
    {
        Read();
        True(FakeState.Events.IndexOf("DescribeSchema.Civil_Schema") < FakeState.Events.IndexOf("Select.Civil_Schema:Pipes"));
        True(FakeState.Events.Contains("Select.Civil_Schema:Structures"));
    });
    Run("Detached bare names pass only after exact schema declarations", () =>
    {
        Detach(Pipes(), "Pipes"); Detach(Structures(), "Structures"); Read();
    });
    Run("Detached qualified exact names also pass", () =>
    {
        Detach(Pipes(), GisNewDrawingProfile.PipesInputClass); Read();
    });
    Run("Dependent schemas may accompany the exact declared schema", () =>
    {
        FakeState.Schemas.Insert(0, new("Unrelated", new FakeDeclaredClassData("Pipes") { QualifiedName = "Unrelated:Pipes", Schema = "Unrelated", Parent = "Unrelated" }));
        Detach(Pipes(), "Pipes"); Read();
    });
    Run("Missing exact schema cannot be inferred from a Pipes class", () =>
    {
        FakeState.Schemas[0].Name = "Other"; Detach(Pipes(), "Pipes");
        RejectBeforeSelect("exactly one schema 'Civil_Schema'");
    });
    Run("Case-folded schema name is not accepted", () =>
    {
        FakeState.Schemas[0].Name = "civil_schema"; RejectBeforeSelect("exactly one schema");
    });
    Run("Missing schema declarations fail before Select", () => { FakeState.Schemas.Clear(); RejectBeforeSelect("found 0"); });
    Run("Ambiguous schema declarations fail before Select", () =>
    {
        FakeState.Schemas.Add(new("Civil_Schema")); RejectBeforeSelect("exactly one schema");
    });
    Run("Both exact class declarations are required before either Select", () =>
    {
        FakeState.Schemas[0].Classes.RemoveAt(1); RejectBeforeSelect("exactly one class 'Civil_Schema:Structures'");
    });
    Run("Ambiguous class declarations fail before Select", () =>
    {
        FakeState.Schemas[0].Classes.Add(new("Pipes")); RejectBeforeSelect("exactly one class 'Civil_Schema:Pipes'");
    });
    Run("Case-folded declared class is not accepted", () =>
    {
        FakeState.Schemas[0].Classes[0].Name = "pipes"; RejectBeforeSelect("exactly one class 'Civil_Schema:Pipes'");
    });
    Run("Conflicting declaration QualifiedName fails with actual metadata", () =>
    {
        FakeState.Schemas[0].Classes[0].QualifiedName = "Other:Pipes"; RejectBeforeSelect("QualifiedName='Other:Pipes'");
    });
    Run("Conflicting declaration schema linkage fails", () =>
    {
        FakeState.Schemas[0].Classes[0].Schema = "Other"; RejectBeforeSelect("FeatureSchema='Other'");
    });
    Run("Conflicting declaration parent fails", () =>
    {
        FakeState.Schemas[0].Classes[0].Parent = "Other"; RejectBeforeSelect("Parent='Other'");
    });
    Run("Bare detached declaration does not establish the schema anchor", () =>
    {
        var declaration = FakeState.Schemas[0].Classes[0];
        declaration.QualifiedName = "Pipes"; declaration.Schema = declaration.Parent = null;
        RejectBeforeSelect("declared schema");
    });
    Run("Reader wrong exact Name fails despite matching QualifiedName", () =>
    {
        Pipes().ReaderName = "Structures"; Reject("Name='Structures'"); Equal(0, FakeState.FeatureReadCount);
    });
    Run("Detached class with another schema-qualified name fails", () =>
    {
        Detach(Pipes(), "Other:Pipes"); Reject("QualifiedName='Other:Pipes'");
    });
    Run("Detached class does not accept a suffix match", () =>
    {
        Detach(Pipes(), "prefixPipes"); Reject("QualifiedName='prefixPipes'");
    });
    Run("Detached class still requires exact case-sensitive Name", () =>
    {
        Detach(Pipes(), "Pipes"); Pipes().ReaderName = "pipes"; Reject("Name='pipes'");
    });
    Run("Reader linked to wrong schema fails despite matching QualifiedName", () =>
    {
        Pipes().ReaderSchema = "Other"; Reject("FeatureSchema='Other'");
    });
    Run("Reader linked to wrong parent fails despite matching QualifiedName", () =>
    {
        Pipes().ReaderParent = "Other"; Reject("Parent='Other'");
    });
    Run("Bare reader name with FeatureSchema linkage is inconsistent", () =>
    {
        Pipes().ReturnedName = "Pipes"; Pipes().ReaderParent = null; Reject("different class definition");
    });
    Run("Bare reader name with Parent linkage is inconsistent", () =>
    {
        Pipes().ReturnedName = "Pipes"; Pipes().ReaderSchema = null; Reject("different class definition");
    });
    Run("DescribeSchema failure releases all acquired wrappers", () =>
    {
        FakeState.FailDescribeSchema = true; RejectBeforeSelect("Injected DescribeSchema failure");
    });
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
        Equal(0, FakeState.SpatialContextCommandCount);
        Equal(0, FakeState.SpatialContextAssociationReadCount);
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
void RejectBeforeSelect(string expected)
{
    Reject(expected);
    True(!FakeState.Events.Any(value => value.StartsWith("Select.", StringComparison.Ordinal)), "Schema rejection must precede every Select.");
    Equal(0, FakeState.FeatureReadCount);
}
void Detach(FakeClassData data, string qualifiedName)
{
    data.ReaderSchema = data.ReaderParent = null;
    data.ReturnedName = qualifiedName;
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
