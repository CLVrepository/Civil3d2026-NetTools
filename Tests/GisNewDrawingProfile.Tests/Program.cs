using System.Collections;
using System.Security.Cryptography;
using System.Xml.Linq;
using CLV_CivilTools.Gis;

int passed = 0;
int failed = 0;
string lvfPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", GisNewDrawingProfile.LvfProfileFileName);
string lvhefPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", GisNewDrawingProfile.LvhefProfileFileName);
string lvf = File.ReadAllText(lvfPath);
string lvhef = File.ReadAllText(lvhefPath);

Run("Exact supplied LVF fixture bytes", () => Equal(
    "f9119c60aed9a1cb4a4d881c3d63578f5b3513678a2303f8d0847267b252003d", Hash(lvfPath)));
Run("Exact supplied LVHEF fixture bytes", () => Equal(
    "3333477e3af9ee3a605e6e7c02c73189f636ec840cfdfa7ff74b2e6a591b7057", Hash(lvhefPath)));

foreach ((string xml, string crs, string path) in new[]
{
    (lvf, GisNewDrawingProfile.LvfCoordinateSystem, lvfPath),
    (lvhef, GisNewDrawingProfile.LvhefCoordinateSystem, lvhefPath)
})
{
    Run($"{crs}: exact deployment profile selection", () =>
    {
        True(GisNewDrawingProfile.TryResolveProfilePath(crs, out string resolved, out string detail), detail);
        string expectedName = crs == GisNewDrawingProfile.LvfCoordinateSystem
            ? GisNewDrawingProfile.LvfProfileFileName : GisNewDrawingProfile.LvhefProfileFileName;
        Equal(GisNewDrawingProfile.ProfileFolder + "\\" + expectedName, resolved);
        Equal(string.Empty, detail);
    });
    Run($"{crs}: fixture preflight succeeds and preserves bytes", () =>
    {
        byte[] before = File.ReadAllBytes(path);
        True(GisNewDrawingProfile.TryLoad(path, crs, out var profile, out string detail), detail);
        True(profile != null);
        Equal(string.Empty, detail);
        True(before.SequenceEqual(File.ReadAllBytes(path)));
        Equal(crs, profile!.SourceCoordinateSystem);
        Equal("FDO_SDF", profile.FormatName);
        True(profile.DoCoordinateConversion);
        True(!profile.UsesSpatialClipping);
        Equal(5, profile.Tables.Count);
        Equal(2, profile.SelectedTables.Count);
        True(profile.SelectedTables.All(table => table.Selected));
        True(profile.Tables.All(table => table.OriginalCoordinateSystem == crs && table.NewCoordinateSystem == crs));
        Equal("Civil_Schema:Pipes,Civil_Schema:Structures", string.Join(",", profile.SelectedTables.Select(table => table.InputClass)));
        Equal("Pipes,Structures", string.Join(",", profile.SelectedTables.Select(table => table.LayerName)));
    });
    Run($"{crs}: exact selected field mappings", () =>
    {
        var profile = Parse(xml, crs);
        var pipes = profile.SelectedTables.Single(table => table.InputClass == GisNewDrawingProfile.PipesInputClass);
        var structures = profile.SelectedTables.Single(table => table.InputClass == GisNewDrawingProfile.StructuresInputClass);
        Equal("Name,InsideDiameter,Length,Slope,StartInvert,EndInvert,StructureStart,StructureEnd,PartSizeName",
            string.Join(",", pipes.MappedColumns.Select(column => column.ColumnName)));
        Equal("Name,PartSizeName", string.Join(",", structures.MappedColumns.Select(column => column.ColumnName)));
        Equal(12, pipes.Columns.Count);
        Equal(5, structures.Columns.Count);
        True(pipes.MappedColumns.All(column => column.OutputColumnName == column.ColumnName && column.OutputTableName == "Pipes"));
        True(structures.MappedColumns.All(column => column.OutputColumnName == column.ColumnName && column.OutputTableName == "Structures"));
        Equal("NotMapped", pipes.Columns.Single(column => column.ColumnName == "OutsideDiameter").ColumnMappingType);
        Equal("NotMapped", structures.Columns.Single(column => column.ColumnName == "RimElevation").ColumnMappingType);
    });
    Run($"{crs}: documented OD ambiguity is retained as diagnostics", () =>
    {
        var profile = Parse(xml, crs);
        Equal(2, profile.Diagnostics.Count);
        foreach (var table in profile.SelectedTables)
        {
            Equal(string.Empty, table.ObjectDataName);
            Equal("NoODTable", table.CreateObjectData);
            Equal("ImportMappingInvalid", table.DataMappingType);
            True(profile.Diagnostics.Any(diagnostic => diagnostic.Contains(table.InputClass, StringComparison.Ordinal) &&
                diagnostic.Contains("native OD readback", StringComparison.Ordinal)));
        }
    });
    Run($"{crs}: result collections cannot be mutated", () =>
    {
        var profile = Parse(xml, crs);
        ReadOnly(profile.Tables);
        ReadOnly(profile.SelectedTables);
        ReadOnly(profile.Diagnostics);
        foreach (var table in profile.Tables)
        {
            ReadOnly(table.Columns);
            ReadOnly(table.MappedColumns);
        }
    });
}

Run("Verified UNC paths are exact", () =>
{
    Equal(@"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Drawing Templates\Blank (2026).dwt", GisNewDrawingProfile.TemplatePath);
    Equal(@"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\SDF to SHP", GisNewDrawingProfile.ProfileFolder);
});
foreach (string? crs in new string?[] { null, "", "NV83.NCRS", "LVF", "LVHEF", "nv83.ncrs-lvf", " NV83.NCRS-LVF", "NV83.NCRS-LVHEF ", "EPSG:3421" })
{
    Run($"No default, alias or fallback for '{crs ?? "<null>"}'", () =>
    {
        True(!GisNewDrawingProfile.TryResolveProfilePath(crs, out string path, out string detail));
        Equal(string.Empty, path);
        True(detail.Contains("no default", StringComparison.Ordinal));
        Rejected(lvf, crs, "Unsupported source coordinate system");
    });
}
Run("LVF never accepted as LVHEF", () => Rejected(lvf, GisNewDrawingProfile.LvhefCoordinateSystem, "exactly match"));
Run("LVHEF never accepted as LVF", () => Rejected(lvhef, GisNewDrawingProfile.LvfCoordinateSystem, "exactly match"));
Run("Missing file fails without profile fallback", () =>
{
    True(!GisNewDrawingProfile.TryLoad(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ipf"),
        GisNewDrawingProfile.LvfCoordinateSystem, out var profile, out string detail));
    True(profile == null);
    True(detail.Contains("preflight failed", StringComparison.Ordinal));
});
Run("Empty XML is rejected", () => Rejected("", GisNewDrawingProfile.LvfCoordinateSystem, "preflight failed"));
Run("Malformed XML is rejected", () => Rejected("<AdMapImportProfile>", GisNewDrawingProfile.LvfCoordinateSystem, "preflight failed"));
Run("DTD and external entities are rejected", () => Rejected(
    "<!DOCTYPE AdMapImportProfile [<!ENTITY external SYSTEM 'file:///this-must-never-be-read'>]>" +
    lvf.Replace("<LoadedProfileName></LoadedProfileName>", "<LoadedProfileName>&external;</LoadedProfileName>", StringComparison.Ordinal),
    GisNewDrawingProfile.LvfCoordinateSystem, "preflight failed"));
Run("Oversized XML is rejected", () => Rejected(
    lvf.Replace("<LoadedProfileName></LoadedProfileName>", "<LoadedProfileName>" + new string('x', 2 * 1024 * 1024) + "</LoadedProfileName>", StringComparison.Ordinal),
    GisNewDrawingProfile.LvfCoordinateSystem, "preflight failed"));

RejectMutation("Other file provider", root => root.Element("FormatName")!.Value = "SHP", "FDO_SDF");
RejectMutation("Missing provider", root => root.Element("FormatName")!.Remove(), "FormatName");
RejectMutation("Disabled coordinate conversion", root => root.Element("DoCoordinateConversion")!.Value = "0", "DoCoordinateConversion");
RejectMutation("Unknown profile version", root => root.SetAttributeValue("version", "9.0"), "version");
RejectMutation("Root element case must be exact", root => root.Name = "admapimportprofile", "root");
RejectMutation("Unexpected root attribute", root => root.SetAttributeValue("SourceFile", "old.sdf"), "attributes");
RejectMutation("Duplicate provider cannot override first", root => root.Add(new XElement("FormatName", "SHP")), "Duplicate/conflicting");
RejectMutation("Qualified XML namespace cannot hide fields", root => root.Element("FormatName")!.Name = XName.Get("FormatName", "urn:other"), "Unsupported profile setting");
RejectMutation("Nested text setting rejected", root => root.Element("FormatName")!.Add(new XElement("Ignored", "FDO_SDF")), "plain text");
RejectMutation("Clipping enabled", root => root.Element("LocationWindowValues")!.Element("LocationWindowOption")!.Value = "Use", "Spatial clipping");
RejectMutation("Clipping extents cannot hide under DontUse", root => root.Element("LocationWindowValues")!.Add(new XElement("MinX", "100")), "Unsupported profile setting");
RejectMutation("Missing clipping setting", root => root.Element("LocationWindowValues")!.Remove(), "LocationWindowValues");
RejectMutation("Driver filename override", root => root.Element("DriverOptions")!.Add(new XElement("FileName", @"C:\old.sdf")), "DriverOptions");
RejectMutation("Driver connection attribute", root => root.Element("DriverOptions")!.SetAttributeValue("ConnectionString", "File=old.sdf"), "DriverOptions");
RejectMutation("Driver text override", root => root.Element("DriverOptions")!.Value = "File=old.sdf", "DriverOptions");
foreach (string setting in new[] { "SourceFileName", "InputFile", "FilePath", "AttributeFilter", "SpatialFilter", "ConnectionString" })
{
    RejectMutation($"Unexpected {setting} is not ignored", root => root.Add(new XElement(setting, "old.sdf")), "source filename/path");
}
RejectMutation("Pipes deselected", root => Table(root, "Pipes").Element("UseThisFeature")!.Value = "0", "Exactly Civil_Schema");
RejectMutation("Third class selected", root => Table(root, "Points").Element("UseThisFeature")!.Value = "1", "Exactly Civil_Schema");
RejectMutation("Missing Structures class", root => Table(root, "Structures").Remove(), "Exactly Civil_Schema");
RejectMutation("Duplicate selected class", root => root.Add(new XElement(Table(root, "Pipes"))), "Duplicate/conflicting input class");
RejectMutation("Case-colliding class", root =>
{
    var copy = new XElement(Table(root, "Pipes"));
    copy.Element("UniqueName")!.Value = "civil_schema:pipes";
    root.Add(copy);
}, "Duplicate/conflicting input class");
RejectMutation("Nonbinary selection flag", root => Table(root, "Pipes").Element("UseThisFeature")!.Value = "true", "UseThisFeature");
RejectMutation("Wrong CAD layer", root => Table(root, "Pipes").Element("LayerName")!.Value = "Other", "CAD layer");
RejectMutation("Indirect CAD layer", root => Table(root, "Pipes").Element("LayerNameType")!.Value = "FromAttribute", "Direct");
RejectMutation("Source CRS mismatch", root => Table(root, "Pipes").Element("OrigCoordSysName")!.Value = GisNewDrawingProfile.LvhefCoordinateSystem, "exactly match");
RejectMutation("Target CRS mismatch", root => Table(root, "Structures").Element("NewCoordSysName")!.Value = GisNewDrawingProfile.LvhefCoordinateSystem, "exactly match");
RejectMutation("Unsupported CRS on inactive table", root => Table(root, "Points").Element("OrigCoordSysName")!.Value = "unknown", "exactly match");
RejectMutation("Table filter", root => Table(root, "Pipes").Add(new XElement("Filter", "Name='old'")), "source filename/path");
RejectMutation("Subtable override", root => Table(root, "Pipes").Element("ContainsSubtables")!.Value = "1", "subtables");
RejectMutation("Classification override", root => Table(root, "Pipes").Element("FeatureClassName")!.Value = "Other", "classification");
RejectMutation("Unique key override", root => Table(root, "Pipes").Element("UseUniqueKeyField")!.Value = "1", "unique-key");
RejectMutation("Unexpected key field even when disabled", root => Table(root, "Pipes").Element("UseUniqueKeyFieldName")!.Value = "Name", "unique-key");
RejectMutation("Block mapping override", root => Table(root, "Structures").Element("BlockName")!.Value = "Block", "block/point");
RejectMutation("Point mapping override", root => Table(root, "Structures").Element("PointMapping")!.Value = "ToBlock", "block/point");
RejectMutation("Missing mapped field", root => Column(Table(root, "Pipes"), "InsideDiameter").Remove(), "mapped OD fields");
RejectMutation("Required field changed to unmapped", root => Column(Table(root, "Structures"), "Name").Element("ColumnMappingType")!.Value = "NotMapped", "mapped OD fields");
RejectMutation("Unexpected mapped field", root => Column(Table(root, "Pipes"), "OutsideDiameter").Element("ColumnMappingType")!.Value = "MappedToOD", "mapped OD fields");
RejectMutation("Renamed OD field", root => Column(Table(root, "Pipes"), "Name").Element("OutputColumnName")!.Value = "Other", "output column");
RejectMutation("Missing OD field destination", root => Column(Table(root, "Pipes"), "Name").Element("OutputColumnName")!.Value = "", "output column");
RejectMutation("Conflicting OD table", root => Column(Table(root, "Pipes"), "Name").Element("OutputTableName")!.Value = "Structures", "output table");
RejectMutation("Conflicting explicit OD table name", root => Table(root, "Pipes").Element("ObjectDataName")!.Value = "Other", "ObjectDataName conflicts");
RejectMutation("Duplicate source column", root => Table(root, "Pipes").Add(new XElement(Column(Table(root, "Pipes"), "Name"))), "duplicate/conflicting input column");
RejectMutation("Case-colliding source column", root =>
{
    var copy = new XElement(Column(Table(root, "Pipes"), "Name"));
    copy.Element("ColumnName")!.Value = "name";
    Table(root, "Pipes").Add(copy);
}, "duplicate/conflicting input column");
RejectMutation("Unknown column mapping", root => Column(Table(root, "Pipes"), "Name").Element("ColumnMappingType")!.Value = "MappedToLink", "unsupported column mapping");
RejectMutation("Object-class property mapping", root => Column(Table(root, "Pipes"), "Name").Element("ClassColumnMappingType")!.Value = "Mapped", "object-class property");
RejectMutation("Unmapped destination cannot contain hidden rename", root => Column(Table(root, "Pipes"), "OutsideDiameter").Element("OutputColumnName")!.Value = "Name", "output column");
RejectMutation("Duplicate leaf setting", root => Column(Table(root, "Pipes"), "Name").Add(new XElement("OutputColumnName", "Other")), "Duplicate/conflicting");

Run("Reordered tables and columns preserve the same mapping plan", () =>
{
    var root = XDocument.Parse(lvf).Root!;
    XElement[] tables = root.Elements("ProfileTable").Reverse().ToArray();
    root.Elements("ProfileTable").Remove();
    foreach (var table in tables)
    {
        XElement[] columns = table.Elements("ProfileColumn").Reverse().ToArray();
        table.Elements("ProfileColumn").Remove();
        table.Add(columns);
    }
    root.Add(tables);
    var result = Parse(root.ToString(), GisNewDrawingProfile.LvfCoordinateSystem);
    Equal("Civil_Schema:Pipes,Civil_Schema:Structures", string.Join(",", result.SelectedTables.Select(table => table.InputClass).OrderBy(value => value, StringComparer.Ordinal)));
    Equal(11, result.SelectedTables.Sum(table => table.MappedColumns.Count));
});
Run("Profile-name metadata is preserved without loading another profile", () =>
{
    var root = XDocument.Parse(lvf).Root!;
    root.Element("LoadedProfileName")!.Value = @"C:\earlier\metadata.ipf";
    var result = Parse(root.ToString(), GisNewDrawingProfile.LvfCoordinateSystem);
    Equal(@"C:\earlier\metadata.ipf", result.LoadedProfileName);
    Equal(2, result.SelectedTables.Count);
});
Run("Table constructor snapshots caller-owned collections", () =>
{
    var columns = new List<GisNewDrawingProfileColumn> { new("Name", "Name", "Pipes", "MappedToOD") };
    var table = new GisNewDrawingProfileTable("Civil_Schema:Pipes", "Pipes", true,
        GisNewDrawingProfile.LvfCoordinateSystem, GisNewDrawingProfile.LvfCoordinateSystem, "", "NoODTable", "ImportMappingInvalid", columns);
    columns.Clear();
    Equal(1, table.Columns.Count);
    Equal(1, table.MappedColumns.Count);
});
Run("Repeated preflight is deterministic and non-mutating", () =>
{
    var one = Parse(lvf, GisNewDrawingProfile.LvfCoordinateSystem);
    var two = Parse(lvf, GisNewDrawingProfile.LvfCoordinateSystem);
    Equal(string.Join("\n", one.Diagnostics), string.Join("\n", two.Diagnostics));
    True(one.SelectedTables.SelectMany(table => table.Columns).SequenceEqual(two.SelectedTables.SelectMany(table => table.Columns)));
    Equal(lvf, File.ReadAllText(lvfPath));
});

foreach ((string xml, string crs) in new[]
{
    (lvf, GisNewDrawingProfile.LvfCoordinateSystem),
    (lvhef, GisNewDrawingProfile.LvhefCoordinateSystem)
})
{
    Run(crs + ": runtime OD plan creates only the explicit mapped fields", () =>
    {
        var profile = Parse(xml, crs);
        var plans = GisNewDrawingObjectDataPlan.Build(profile, SourceSchemas(), Array.Empty<GisNewDrawingObjectDataTableSchema>());
        Equal(2, plans.Count);
        True(plans.All(plan => plan.MappingMode == "NewObjectDataOnly"));
        Equal("Civil_Schema:Pipes,Civil_Schema:Structures", string.Join(",", plans.Select(plan => plan.InputClass)));
        Equal("Pipes,Structures", string.Join(",", plans.Select(plan => plan.TableName)));
        Equal("Name,InsideDiameter,Length,Slope,StartInvert,EndInvert,StructureStart,StructureEnd,PartSizeName",
            string.Join(",", plans[0].Fields.Select(field => field.SourceName)));
        Equal("Name,PartSizeName", string.Join(",", plans[1].Fields.Select(field => field.SourceName)));
        Equal("Character,Real,Real,Real,Real,Real,Character,Character,Character",
            string.Join(",", plans[0].Fields.Select(field => field.ObjectDataType)));
        True(plans.SelectMany(plan => plan.Fields).All(field => field.SourceName == field.OutputName));
        Equal(11, plans.Sum(plan => plan.Fields.Count));
        True(profile.SelectedTables.All(table => table.ObjectDataName == "" && table.CreateObjectData == "NoODTable" &&
            table.DataMappingType == "ImportMappingInvalid"));
        Equal(xml, File.ReadAllText(crs == GisNewDrawingProfile.LvfCoordinateSystem ? lvfPath : lvhefPath));
    });
}
Run("Runtime OD plan reuses compatible existing tables regardless of field order", () =>
{
    var existing = ExistingSchemas().Select(table => new GisNewDrawingObjectDataTableSchema(table.TableName, table.Fields.Reverse()));
    var plans = Plan(existing: existing);
    True(plans.All(plan => plan.MappingMode == "ExistingObjectDataOnly"));
    Equal("Name", plans[0].Fields[0].OutputName);
    Equal("Name", plans[1].Fields[0].OutputName);
});
Run("Runtime OD plan chooses New and Existing independently", () =>
{
    var plans = Plan(existing: ExistingSchemas().Where(table => table.TableName == "Structures"));
    Equal("NewObjectDataOnly", plans[0].MappingMode);
    Equal("ExistingObjectDataOnly", plans[1].MappingMode);
});
Run("Unrelated OD tables do not add mappings or fields", () =>
{
    var plans = Plan(existing: new[] { new GisNewDrawingObjectDataTableSchema("Other", new[] { new GisNewDrawingObjectDataFieldSchema("Position", "Point") }) });
    True(plans.All(plan => plan.MappingMode == "NewObjectDataOnly"));
    Equal(11, plans.Sum(plan => plan.Fields.Count));
});
Run("Runtime field order follows the explicit IPF mapping order", () =>
{
    var root = XDocument.Parse(lvf).Root!;
    var pipes = Table(root, "Pipes");
    XElement[] columns = pipes.Elements("ProfileColumn").Reverse().ToArray();
    pipes.Elements("ProfileColumn").Remove(); pipes.Add(columns);
    var plans = GisNewDrawingObjectDataPlan.Build(Parse(root.ToString(), GisNewDrawingProfile.LvfCoordinateSystem),
        SourceSchemas(), ExistingSchemas());
    Equal("PartSizeName", plans[0].Fields[0].SourceName);
    Equal("Name", plans[0].Fields[^1].SourceName);
});
foreach ((string sourceType, string expectedType) in new[]
{
    ("DataType_String", "Character"), ("DataType_Int16", "Integer"), ("DataType_Int32", "Integer"),
    ("DataType_Single", "Real"), ("DataType_Double", "Real")
})
{
    Run("Runtime OD type comes from FDO schema: " + sourceType, () =>
    {
        var source = SourceSchemas(); PipeSchema(source)["Length"] = sourceType;
        var field = Plan(source).Single(plan => plan.TableName == "Pipes").Fields.Single(item => item.SourceName == "Length");
        Equal(sourceType, field.SourceDataType); Equal(expectedType, field.ObjectDataType);
    });
}
foreach (string sourceType in new[]
{
    "DataType_Byte", "DataType_Int64", "DataType_Boolean", "DataType_DateTime", "DataType_Decimal",
    "DataType_BLOB", "DataType_CLOB", "DataType_Unknown", "datatype_string", ""
})
{
    Run("Runtime OD rejects unapproved schema conversion: " + sourceType, () =>
    {
        var source = SourceSchemas(); PipeSchema(source)["Length"] = sourceType;
        PlanRejected(() => Plan(source), "no approved lossless Object Data mapping");
    });
}
Run("Runtime OD rejects a missing source class even without feature rows", () =>
{
    var source = SourceSchemas(); source.Remove(GisNewDrawingProfile.StructuresInputClass);
    PlanRejected(() => Plan(source), "exactly the selected profile input classes");
});
Run("Runtime OD rejects extra source classes", () =>
{
    var source = SourceSchemas(); source.Add("Civil_Schema:Points", new Dictionary<string, string>());
    PlanRejected(() => Plan(source), "exactly the selected profile input classes");
});
Run("Runtime OD rejects a case alias even with a case-insensitive source dictionary", () =>
{
    var source = new Dictionary<string, IReadOnlyDictionary<string, string>>(SourceSchemas(), StringComparer.OrdinalIgnoreCase);
    var pipes = source[GisNewDrawingProfile.PipesInputClass]; source.Remove(GisNewDrawingProfile.PipesInputClass);
    source.Add("civil_schema:pipes", pipes);
    PlanRejected(() => Plan(source), "exactly the selected profile input classes");
});
Run("Runtime OD rejects case-colliding source classes", () =>
{
    var source = SourceSchemas(); source.Add("civil_schema:pipes", source[GisNewDrawingProfile.PipesInputClass]);
    PlanRejected(() => Plan(source), "case-colliding");
});
Run("Runtime OD rejects missing source field schema", () =>
{
    var source = SourceSchemas(); PipeSchema(source).Remove("Length");
    PlanRejected(() => Plan(source), "exactly the explicitly mapped profile fields");
});
Run("Runtime OD rejects extra source field schema instead of mapping it", () =>
{
    var source = SourceSchemas(); PipeSchema(source).Add("OutsideDiameter", "DataType_Double");
    PlanRejected(() => Plan(source), "exactly the explicitly mapped profile fields");
});
Run("Runtime OD rejects case-aliased source fields", () =>
{
    var source = SourceSchemas(); PipeSchema(source).Remove("Name"); PipeSchema(source).Add("name", "DataType_String");
    PlanRejected(() => Plan(source), "exactly the explicitly mapped profile fields");
});
Run("Runtime OD rejects case-colliding source fields", () =>
{
    var source = SourceSchemas(); PipeSchema(source).Add("name", "DataType_String");
    PlanRejected(() => Plan(source), "case-colliding");
});
foreach (string duplicateName in new[] { "Pipes", "pipes" })
{
    Run("Runtime OD rejects duplicate or case-colliding existing tables: " + duplicateName, () =>
        PlanRejected(() => Plan(existing: ExistingSchemas().Append(new GisNewDrawingObjectDataTableSchema(duplicateName,
            ExistingSchemas()[0].Fields))), "case-colliding"));
}
Run("Runtime OD rejects a case-only existing table alias", () =>
    PlanRejected(() => Plan(existing: new[] { new GisNewDrawingObjectDataTableSchema("pipes", ExistingSchemas()[0].Fields) }), "case aliases"));
foreach ((string description, Func<IReadOnlyList<GisNewDrawingObjectDataFieldSchema>, IEnumerable<GisNewDrawingObjectDataFieldSchema>> mutate, string error) in
    new (string, Func<IReadOnlyList<GisNewDrawingObjectDataFieldSchema>, IEnumerable<GisNewDrawingObjectDataFieldSchema>>, string)[]
{
    ("missing field", fields => fields.Skip(1), "field names must exactly match"),
    ("extra field", fields => fields.Append(new("OutsideDiameter", "Real")), "field names must exactly match"),
    ("case-aliased field", fields => fields.Select(field => field.Name == "Name" ? new("name", field.ObjectDataType) : field), "field names must exactly match"),
    ("duplicate field", fields => fields.Append(new("Name", "Character")), "case-colliding"),
    ("case-colliding field", fields => fields.Append(new("name", "Character")), "case-colliding"),
    ("wrong numeric type", fields => fields.Select(field => field.Name == "Length" ? new("Length", "Integer") : field), "differs from schema-derived"),
    ("wrong character type", fields => fields.Select(field => field.Name == "Name" ? new("Name", "Real") : field), "differs from schema-derived")
})
{
    Run("Runtime OD rejects an existing table with " + description, () =>
        PlanRejected(() => Plan(existing: new[] { new GisNewDrawingObjectDataTableSchema("Pipes", mutate(ExistingSchemas()[0].Fields)) }), error));
}
Run("Incompatible second target returns no partial runtime plan", () =>
{
    IReadOnlyList<GisNewDrawingObjectDataClassPlan>? result = null;
    var existing = ExistingSchemas();
    existing[1] = new("Structures", new[] { new GisNewDrawingObjectDataFieldSchema("Name", "Integer"), new("PartSizeName", "Character") });
    PlanRejected(() => result = Plan(existing: existing), "Structures.Name");
    True(result == null);
});
Run("Runtime OD plan and existing schemas snapshot caller collections", () =>
{
    var source = SourceSchemas();
    var suppliedFields = ExistingSchemas()[0].Fields.ToList();
    var existing = new List<GisNewDrawingObjectDataTableSchema> { new("Pipes", suppliedFields) };
    suppliedFields.Clear();
    Equal(9, existing[0].Fields.Count); ReadOnly(existing[0].Fields);
    var plans = Plan(source, existing);
    PipeSchema(source)["Name"] = "DataType_Int64";
    source.Clear(); existing.Clear();
    Equal("DataType_String", plans[0].Fields[0].SourceDataType);
    Equal("ExistingObjectDataOnly", plans[0].MappingMode);
    Equal(2, plans.Count); ReadOnly(plans);
    foreach (var plan in plans) ReadOnly(plan.Fields);
});

foreach (bool existing in new[] { false, true })
{
    var plans = Plan(existing: existing ? ExistingSchemas() : Array.Empty<GisNewDrawingObjectDataTableSchema>());
    var pipes = plans.Single(plan => plan.TableName == "Pipes");
    string mode = pipes.MappingMode;
    string otherMode = existing ? "NewObjectDataOnly" : "ExistingObjectDataOnly";
    Run(mode + ": all exact mapped columns pass readback", () =>
    {
        foreach (var plan in plans)
            foreach (var field in plan.Fields)
                GisNewDrawingObjectDataPlan.VerifyColumnMapping(plan, field.SourceName, mode, field.OutputName);
    });
    foreach (string sourceName in new[] { "OutsideDiameter", "ExtraNativeColumn", "name" })
    {
        foreach (string clearedMode in new[] { "NoImportMapping", mode })
        {
            Run(mode + ": cleared " + sourceName + " accepts " + clearedMode, () =>
                GisNewDrawingObjectDataPlan.VerifyColumnMapping(pipes, sourceName, clearedMode, ""));
            Run(mode + ": unmapped " + sourceName + " rejects nonempty output with " + clearedMode, () =>
                PlanRejected(() => GisNewDrawingObjectDataPlan.VerifyColumnMapping(pipes, sourceName, clearedMode, "Name"),
                    "unmapped native column must have empty output"));
        }
        Run(mode + ": cleared " + sourceName + " rejects another OD mode", () =>
            PlanRejected(() => GisNewDrawingObjectDataPlan.VerifyColumnMapping(pipes, sourceName, otherMode, ""),
                "unmapped native column must have empty output"));
    }
    foreach (string wrongMode in new[] { "NoImportMapping", otherMode, "", "UnexpectedMapping", mode.ToLowerInvariant() })
    {
        Run(mode + ": mapped field rejects mode '" + wrongMode + "'", () =>
            PlanRejected(() => GisNewDrawingObjectDataPlan.VerifyColumnMapping(pipes, "Name", wrongMode, "Name"), "differs from planned"));
    }
    foreach (string wrongOutput in new[] { "", "name", "PartSizeName", "Name " })
    {
        Run(mode + ": mapped field rejects output '" + wrongOutput + "'", () =>
            PlanRejected(() => GisNewDrawingObjectDataPlan.VerifyColumnMapping(pipes, "Name", mode, wrongOutput), "differs from planned"));
    }
    foreach (string wrongMode in new[] { "", "UnexpectedMapping", "noimportmapping" })
    {
        Run(mode + ": unmapped field rejects mode '" + wrongMode + "'", () =>
            PlanRejected(() => GisNewDrawingObjectDataPlan.VerifyColumnMapping(pipes, "ExtraNativeColumn", wrongMode, ""),
                "unmapped native column must have empty output"));
    }
    Run(mode + ": unmapped field output must be empty rather than whitespace", () =>
        PlanRejected(() => GisNewDrawingObjectDataPlan.VerifyColumnMapping(pipes, "ExtraNativeColumn", mode, " "),
            "unmapped native column must have empty output"));
}

Console.WriteLine($"GisNewDrawingProfile: {passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;

void Run(string name, Action test)
{
    try { test(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.Error.WriteLine("FAIL " + name + ": " + ex); }
}
void RejectMutation(string name, Action<XElement> mutate, string expectedDetail)
    => Run(name, () =>
    {
        var root = XDocument.Parse(lvf).Root!;
        mutate(root);
        Rejected(root.ToString(), GisNewDrawingProfile.LvfCoordinateSystem, expectedDetail);
    });
static GisNewDrawingProfile Parse(string xml, string crs)
{
    True(GisNewDrawingProfile.TryParse(xml, crs, out var profile, out string detail), detail);
    True(profile != null);
    return profile!;
}
static void Rejected(string xml, string? crs, string expectedDetail)
{
    True(!GisNewDrawingProfile.TryParse(xml, crs, out var profile, out string detail), "Unexpected preflight success.");
    True(profile == null, "Rejected profile must not leak a usable plan.");
    True(detail.Contains(expectedDetail, StringComparison.Ordinal), $"Expected '{expectedDetail}' in '{detail}'.");
}
IReadOnlyList<GisNewDrawingObjectDataClassPlan> Plan(
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? source = null,
    IEnumerable<GisNewDrawingObjectDataTableSchema>? existing = null)
    => GisNewDrawingObjectDataPlan.Build(Parse(lvf, GisNewDrawingProfile.LvfCoordinateSystem),
        source ?? SourceSchemas(), existing ?? Array.Empty<GisNewDrawingObjectDataTableSchema>());
static Dictionary<string, IReadOnlyDictionary<string, string>> SourceSchemas() => new(StringComparer.Ordinal)
{
    [GisNewDrawingProfile.PipesInputClass] = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Name"] = "DataType_String", ["InsideDiameter"] = "DataType_Double", ["Length"] = "DataType_Double",
        ["Slope"] = "DataType_Double", ["StartInvert"] = "DataType_Double", ["EndInvert"] = "DataType_Double",
        ["StructureStart"] = "DataType_String", ["StructureEnd"] = "DataType_String", ["PartSizeName"] = "DataType_String"
    },
    [GisNewDrawingProfile.StructuresInputClass] = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Name"] = "DataType_String", ["PartSizeName"] = "DataType_String"
    }
};
static Dictionary<string, string> PipeSchema(Dictionary<string, IReadOnlyDictionary<string, string>> source)
    => (Dictionary<string, string>)source[GisNewDrawingProfile.PipesInputClass];
static GisNewDrawingObjectDataTableSchema[] ExistingSchemas() => new[]
{
    new GisNewDrawingObjectDataTableSchema("Pipes", new[]
    {
        new GisNewDrawingObjectDataFieldSchema("Name", "Character"), new("InsideDiameter", "Real"), new("Length", "Real"),
        new("Slope", "Real"), new("StartInvert", "Real"), new("EndInvert", "Real"), new("StructureStart", "Character"),
        new("StructureEnd", "Character"), new("PartSizeName", "Character")
    }),
    new GisNewDrawingObjectDataTableSchema("Structures", new[]
    {
        new GisNewDrawingObjectDataFieldSchema("Name", "Character"), new("PartSizeName", "Character")
    })
};
static void PlanRejected(Action action, string expectedDetail)
{
    try { action(); }
    catch (InvalidDataException ex)
    {
        True(ex.Message.Contains(expectedDetail, StringComparison.Ordinal), $"Expected '{expectedDetail}' in '{ex.Message}'.");
        return;
    }
    throw new Exception("Unexpected runtime OD plan success.");
}
static XElement Table(XElement root, string name)
    => root.Elements("ProfileTable").Single(table => table.Element("UniqueName")!.Value == "Civil_Schema:" + name);
static XElement Column(XElement table, string name)
    => table.Elements("ProfileColumn").Single(column => column.Element("ColumnName")!.Value == name);
static void ReadOnly<T>(IReadOnlyList<T> values)
{
    True(values is IList list && list.IsReadOnly);
    try { ((IList)values).Clear(); }
    catch (NotSupportedException) { return; }
    throw new Exception("Collection accepted mutation.");
}
static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
static void True(bool condition, string? detail = null) { if (!condition) throw new Exception(detail ?? "Assertion failed."); }
static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected '{expected}', got '{actual}'."); }
