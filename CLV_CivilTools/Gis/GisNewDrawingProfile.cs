using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace CLV_CivilTools.Gis
{
    /// <summary>
    /// Read-only preflight of the two supplied Map import profiles. No Autodesk
    /// dependencies, XML rewriting, coordinate aliases or profile fallback.
    /// A valid profile is an import plan, not proof that native import created OD.
    /// </summary>
    internal sealed class GisNewDrawingProfile
    {
        internal const string TemplatePath = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Drawing Templates\Blank (2026).dwt";
        internal const string ProfileFolder = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\SDF to SHP";
        internal const string LvfCoordinateSystem = "NV83.NCRS-LVF";
        internal const string LvhefCoordinateSystem = "NV83.NCRS-LVHEF";
        internal const string LvfProfileFileName = "UFLS-IMPORT-NV83.NCRS-LVF.ipf";
        internal const string LvhefProfileFileName = "UFLS-IMPORT-NV83.NCRS.LVHEF.ipf";
        internal const string PipesInputClass = "Civil_Schema:Pipes";
        internal const string StructuresInputClass = "Civil_Schema:Structures";
        private const int MaximumProfileCharacters = 2 * 1024 * 1024;

        private static readonly string[] RootFields =
        {
            "LoadedProfileName", "DoCoordinateConversion", "ImportPolygonsAsClosedPolylines",
            "AuditClassifiedAfterImport", "FormatName", "DriverOptions", "LocationWindowValues", "ProfileTable"
        };
        private static readonly string[] TableFields =
        {
            "UniqueName", "UseThisFeature", "FeatureClassName", "ContainsSubtables", "LayerName", "LayerNameType",
            "NewCoordSysName", "OrigCoordSysName", "ObjectDataName", "CreateObjectData", "DataMappingType",
            "UseUniqueKeyFieldName", "UseUniqueKeyField", "BlockName", "UseForBlockAttributes", "PointMapping", "ProfileColumn"
        };
        private static readonly string[] ColumnFields =
        {
            "ColumnMappingType", "ColumnName", "OutputColumnName", "OutputTableName",
            "ClassColumnMappingType", "OutputClassPropertyName"
        };
        private static readonly string[] PipeMappedFields =
        {
            "Name", "InsideDiameter", "Length", "Slope", "StartInvert", "EndInvert", "StructureStart", "StructureEnd", "PartSizeName"
        };
        private static readonly string[] StructureMappedFields = { "Name", "PartSizeName" };

        private GisNewDrawingProfile(string sourceCoordinateSystem, string loadedProfileName,
            IEnumerable<GisNewDrawingProfileTable> tables, IEnumerable<string> diagnostics)
        {
            SourceCoordinateSystem = sourceCoordinateSystem;
            LoadedProfileName = loadedProfileName;
            Tables = Snapshot(tables);
            SelectedTables = Snapshot(Tables.Where(table => table.Selected));
            Diagnostics = Snapshot(diagnostics);
        }

        internal string SourceCoordinateSystem { get; }
        internal string LoadedProfileName { get; }
        internal string FormatName => "FDO_SDF";
        internal bool DoCoordinateConversion => true;
        internal bool UsesSpatialClipping => false;
        internal IReadOnlyList<GisNewDrawingProfileTable> Tables { get; }
        internal IReadOnlyList<GisNewDrawingProfileTable> SelectedTables { get; }
        internal IReadOnlyList<string> Diagnostics { get; }

        internal static bool IsSupportedCoordinateSystem(string? coordinateSystem)
            => coordinateSystem == LvfCoordinateSystem || coordinateSystem == LvhefCoordinateSystem;

        internal static bool TryResolveProfilePath(string? exactSourceCrs, out string path, out string detail)
        {
            path = string.Empty;
            if (!IsSupportedCoordinateSystem(exactSourceCrs))
            {
                detail = UnsupportedCoordinateSystem(exactSourceCrs);
                return false;
            }
            // These are Windows deployment paths even when the pure tests run on Linux.
            path = ProfileFolder + "\\" + (exactSourceCrs == LvfCoordinateSystem ? LvfProfileFileName : LvhefProfileFileName);
            detail = string.Empty;
            return true;
        }

        internal static bool TryLoad(string path, string? expectedSourceCoordinateSystem,
            out GisNewDrawingProfile? profile, out string detail)
        {
            profile = null;
            if (!IsSupportedCoordinateSystem(expectedSourceCoordinateSystem))
            {
                detail = UnsupportedCoordinateSystem(expectedSourceCoordinateSystem);
                return false;
            }
            try
            {
                // Hold only read access and parse directly; never save or normalize the shared IPF.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = XmlReader.Create(stream, ReaderSettings());
                profile = ParseDocument(XDocument.Load(reader), expectedSourceCoordinateSystem!);
                detail = string.Empty;
                return true;
            }
            catch (System.Exception ex) when (IsProfileReadException(ex))
            {
                detail = "Import profile preflight failed: " + ex.Message;
                return false;
            }
        }

        internal static bool TryParse(string xml, string? expectedSourceCoordinateSystem,
            out GisNewDrawingProfile? profile, out string detail)
        {
            profile = null;
            if (!IsSupportedCoordinateSystem(expectedSourceCoordinateSystem))
            {
                detail = UnsupportedCoordinateSystem(expectedSourceCoordinateSystem);
                return false;
            }
            try
            {
                using var text = new StringReader(xml);
                using var reader = XmlReader.Create(text, ReaderSettings());
                profile = ParseDocument(XDocument.Load(reader), expectedSourceCoordinateSystem!);
                detail = string.Empty;
                return true;
            }
            catch (System.Exception ex) when (IsProfileReadException(ex))
            {
                detail = "Import profile preflight failed: " + ex.Message;
                return false;
            }
        }

        private static XmlReaderSettings ReaderSettings() => new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumProfileCharacters,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true
        };

        private static bool IsProfileReadException(System.Exception ex)
            => ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is XmlException ||
                ex is ArgumentException || ex is NotSupportedException || ex is System.Security.SecurityException;

        private static GisNewDrawingProfile ParseDocument(XDocument document, string sourceCrs)
        {
            XElement root = document.Root ?? throw Invalid("The profile has no root element.");
            Require(root.Name == "AdMapImportProfile", "Expected an unqualified AdMapImportProfile XML root.");
            Require((string?)root.Attribute("version") == "2.1.2", "Unsupported import-profile version; expected 2.1.2.");
            Require(root.Attributes().All(attribute => attribute.Name == "version"), "Unsupported import-profile root attributes.");
            CheckChildren(root, RootFields, "ProfileTable");
            Require(Text(root, "FormatName") == "FDO_SDF", "FormatName must be exactly FDO_SDF.");
            Require(Text(root, "DoCoordinateConversion") == "1", "DoCoordinateConversion must be 1.");
            Require(Text(root, "ImportPolygonsAsClosedPolylines") == "0", "Unexpected polygon import setting.");
            Require(Text(root, "AuditClassifiedAfterImport") == "1", "Unexpected classified-object audit setting.");
            string loadedProfileName = Text(root, "LoadedProfileName");
            XElement driver = Single(root, "DriverOptions");
            Require(!driver.HasAttributes && !driver.HasElements && string.IsNullOrWhiteSpace(driver.Value),
                "DriverOptions must be empty; source filename/path, connection or filter overrides require review.");
            XElement window = Single(root, "LocationWindowValues");
            CheckChildren(window, new[] { "LocationWindowOption" });
            Require(Text(window, "LocationWindowOption") == "DontUse", "Spatial clipping/filter settings are not allowed; LocationWindowOption must be DontUse.");

            var diagnostics = new List<string>();
            var tables = root.Elements("ProfileTable").Select(element => ParseTable(element, sourceCrs)).ToArray();
            Require(tables.Length > 0, "The profile has no input classes.");
            Require(tables.Select(table => table.InputClass).Distinct(StringComparer.OrdinalIgnoreCase).Count() == tables.Length,
                "Duplicate/conflicting input class entries are not allowed.");
            GisNewDrawingProfileTable[] selected = tables.Where(table => table.Selected).ToArray();
            Require(selected.Length == 2 && selected.Any(table => table.InputClass == PipesInputClass) &&
                selected.Any(table => table.InputClass == StructuresInputClass),
                "Exactly Civil_Schema:Pipes and Civil_Schema:Structures must be selected.");
            foreach (GisNewDrawingProfileTable table in selected)
            {
                ValidateSelectedTable(table);
                // These observed table-level values contradict the explicit column OD mappings.
                // Preserve/report the input bytes. The runtime will activate only
                // the validated explicit columns, then require native OD readback.
                if (table.ObjectDataName.Length == 0 || table.CreateObjectData == "NoODTable" ||
                    table.DataMappingType == "ImportMappingInvalid")
                {
                    diagnostics.Add($"{table.InputClass}: explicit MappedToOD columns target {table.LayerName}, while " +
                        $"ObjectDataName='{table.ObjectDataName}', CreateObjectData='{table.CreateObjectData}', " +
                        $"DataMappingType='{table.DataMappingType}'. The shared profile is unchanged; its explicit columns will be activated for this import and native OD readback is required.");
                }
            }
            return new GisNewDrawingProfile(sourceCrs, loadedProfileName, tables, diagnostics);
        }

        private static GisNewDrawingProfileTable ParseTable(XElement element, string sourceCrs)
        {
            CheckChildren(element, TableFields, "ProfileColumn");
            string inputClass = Text(element, "UniqueName");
            Require(!string.IsNullOrWhiteSpace(inputClass), "An input class has no UniqueName.");
            string selectedText = Text(element, "UseThisFeature");
            Require(selectedText == "0" || selectedText == "1", $"{inputClass}: UseThisFeature must be 0 or 1.");
            string originalCrs = Text(element, "OrigCoordSysName");
            string newCrs = Text(element, "NewCoordSysName");
            Require(originalCrs == sourceCrs && newCrs == sourceCrs,
                $"{inputClass}: original/new coordinate systems must both exactly match {sourceCrs}; found '{originalCrs}'/'{newCrs}'. No fallback is allowed.");
            Require(Text(element, "ContainsSubtables") == "0", $"{inputClass}: subtables are unsupported.");
            Require(Text(element, "LayerNameType") == "Direct", $"{inputClass}: the CAD layer must use Direct mapping.");
            Require(Text(element, "FeatureClassName").Length == 0, $"{inputClass}: feature classification overrides are unsupported.");
            Require(Text(element, "UseUniqueKeyField") == "0" && Text(element, "UseUniqueKeyFieldName").Length == 0,
                $"{inputClass}: unique-key/filter overrides are unsupported.");
            Require(Text(element, "BlockName").Length == 0 && Text(element, "UseForBlockAttributes") == "0" &&
                Text(element, "PointMapping") == "ToPoint", $"{inputClass}: block/point mapping differs from the supplied profile.");
            GisNewDrawingProfileColumn[] columns = element.Elements("ProfileColumn").Select(column => ParseColumn(column, inputClass)).ToArray();
            Require(columns.Select(column => column.ColumnName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == columns.Length,
                $"{inputClass}: duplicate/conflicting input column mappings are not allowed.");
            return new GisNewDrawingProfileTable(inputClass, Text(element, "LayerName"), selectedText == "1",
                originalCrs, newCrs, Text(element, "ObjectDataName"), Text(element, "CreateObjectData"),
                Text(element, "DataMappingType"), columns);
        }

        private static GisNewDrawingProfileColumn ParseColumn(XElement column, string inputClass)
        {
            CheckChildren(column, ColumnFields);
            string name = Text(column, "ColumnName");
            Require(!string.IsNullOrWhiteSpace(name), $"{inputClass}: an input column name is blank.");
            string mapping = Text(column, "ColumnMappingType");
            Require(mapping == "MappedToOD" || mapping == "NotMapped", $"{inputClass}.{name}: unsupported column mapping '{mapping}'.");
            Require(Text(column, "ClassColumnMappingType") == "NotMapped" && Text(column, "OutputClassPropertyName").Length == 0,
                $"{inputClass}.{name}: object-class property mapping is unsupported.");
            return new GisNewDrawingProfileColumn(name, Text(column, "OutputColumnName"), Text(column, "OutputTableName"), mapping);
        }

        private static void ValidateSelectedTable(GisNewDrawingProfileTable table)
        {
            string expectedLayer = table.InputClass == PipesInputClass ? "Pipes" : "Structures";
            Require(table.LayerName == expectedLayer, $"{table.InputClass}: CAD layer must be exactly {expectedLayer}.");
            Require(table.ObjectDataName.Length == 0 || table.ObjectDataName == expectedLayer,
                $"{table.InputClass}: ObjectDataName conflicts with the {expectedLayer} column mappings.");
            string[] expected = table.InputClass == PipesInputClass ? PipeMappedFields : StructureMappedFields;
            GisNewDrawingProfileColumn[] mapped = table.Columns.Where(column => column.IsMappedToObjectData).ToArray();
            Require(mapped.Length == expected.Length && expected.All(name => mapped.Any(column => column.ColumnName == name)),
                $"{table.InputClass}: mapped OD fields must be exactly {string.Join(", ", expected)}.");
            foreach (GisNewDrawingProfileColumn column in table.Columns)
            {
                Require(column.OutputTableName == expectedLayer,
                    $"{table.InputClass}.{column.ColumnName}: output table conflicts with {expectedLayer}.");
                Require(column.IsMappedToObjectData ? column.OutputColumnName == column.ColumnName : column.OutputColumnName.Length == 0,
                    $"{table.InputClass}.{column.ColumnName}: output column is renamed, missing or conflicts with its mapping flag.");
            }
        }

        private static void CheckChildren(XElement element, IReadOnlyCollection<string> allowedNames, string? repeatable = null)
        {
            Require(!element.HasAttributes || element.Name == "AdMapImportProfile", $"{element.Name}: unsupported XML attributes.");
            foreach (XElement child in element.Elements())
            {
                Require(child.Name.Namespace == XNamespace.None && allowedNames.Contains(child.Name.LocalName),
                    $"Unsupported profile setting '{child.Name}' in {element.Name}; source filename/path, filters and other overrides require review.");
                if (child.Name.LocalName != repeatable)
                    Require(element.Elements(child.Name).Count() == 1, $"Duplicate/conflicting profile setting '{child.Name}' in {element.Name}.");
            }
            Require(!element.Nodes().OfType<XText>().Any(text => !string.IsNullOrWhiteSpace(text.Value)),
                $"{element.Name}: unexpected XML text outside a setting.");
        }

        private static XElement Single(XElement parent, string name)
        {
            XElement[] matches = parent.Elements(name).ToArray();
            Require(matches.Length == 1, $"Expected exactly one {name} setting in {parent.Name}.");
            return matches[0];
        }

        private static string Text(XElement parent, string name)
        {
            XElement element = Single(parent, name);
            Require(!element.HasAttributes && !element.HasElements, $"{name}: expected a plain text setting.");
            return element.Value;
        }

        private static string UnsupportedCoordinateSystem(string? coordinateSystem)
            => $"Unsupported source coordinate system '{coordinateSystem ?? "<missing>"}'. Select exactly {LvfCoordinateSystem} or {LvhefCoordinateSystem}; no default, alias or fallback is allowed.";

        private static InvalidDataException Invalid(string message) => new(message);
        private static void Require(bool condition, string message) { if (!condition) throw Invalid(message); }

        internal static ReadOnlyCollection<T> Snapshot<T>(IEnumerable<T> values) => Array.AsReadOnly(values.ToArray());
    }

    internal sealed class GisNewDrawingProfileTable
    {
        internal GisNewDrawingProfileTable(string inputClass, string layerName, bool selected,
            string originalCoordinateSystem, string newCoordinateSystem, string objectDataName,
            string createObjectData, string dataMappingType, IEnumerable<GisNewDrawingProfileColumn> columns)
        {
            InputClass = inputClass;
            LayerName = layerName;
            Selected = selected;
            OriginalCoordinateSystem = originalCoordinateSystem;
            NewCoordinateSystem = newCoordinateSystem;
            ObjectDataName = objectDataName;
            CreateObjectData = createObjectData;
            DataMappingType = dataMappingType;
            Columns = GisNewDrawingProfile.Snapshot(columns);
            MappedColumns = GisNewDrawingProfile.Snapshot(Columns.Where(column => column.IsMappedToObjectData));
        }

        internal string InputClass { get; }
        internal string LayerName { get; }
        internal bool Selected { get; }
        internal string OriginalCoordinateSystem { get; }
        internal string NewCoordinateSystem { get; }
        internal string ObjectDataName { get; }
        internal string CreateObjectData { get; }
        internal string DataMappingType { get; }
        internal IReadOnlyList<GisNewDrawingProfileColumn> Columns { get; }
        internal IReadOnlyList<GisNewDrawingProfileColumn> MappedColumns { get; }
    }

    internal sealed record GisNewDrawingProfileColumn(string ColumnName, string OutputColumnName,
        string OutputTableName, string ColumnMappingType)
    {
        internal bool IsMappedToObjectData => ColumnMappingType == "MappedToOD";
    }
}
