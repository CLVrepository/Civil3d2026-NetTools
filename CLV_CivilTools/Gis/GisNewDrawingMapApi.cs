using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace CLV_CivilTools.Gis
{
    /// <summary>
    /// Exact, documented Map 2026 members. Reflection avoids a second managed Map
    /// wrapper lifetime model; failures are never converted into successful defaults.
    /// Application, ActiveProject, Importer and ODTables are borrowed host objects.
    /// </summary>
    internal static class GisNewDrawingMapApi
    {
        internal sealed record ImportSummary(int Pipes, int Structures);

        internal static string ReadProjection()
            => Get(Project(), "Projection") as string
                ?? throw new InvalidOperationException("Map project Projection is not a coordinate-system string.");

        internal static void AssignProjection(string coordinateSystem)
        {
            object project = Project();
            GisNewDrawingCoordinateSystem.AssignSameCode(coordinateSystem,
                code => SetTextProperty(project, "Projection", code),
                () => Get(project, "Projection") as string, "New drawing Map Projection");
        }

        internal static void VerifyProjection(string expectedCode)
            => GisNewDrawingCoordinateSystem.VerifySameCode(expectedCode, ReadProjection(), "New drawing Map Projection");

        internal static ImportSummary ImportAndVerify(Document document, GisNewDrawingSdfSnapshot sdf,
            string profilePath, GisNewDrawingProfile profile)
        {
            if (document != Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument ||
                document.Database != HostApplicationServices.WorkingDatabase)
                throw new InvalidOperationException("The destination drawing must be active and its database current for Map import.");
            VerifyProjection(profile.SourceCoordinateSystem);
            GisNewDrawingCoordinateSystem.VerifySameCode(profile.SourceCoordinateSystem, sdf.CoordinateSystem,
                "SDF snapshot's source-drawing code");
            var pipes = new Dictionary<string, GisNewDrawingSdfFeature>(StringComparer.Ordinal);
            var structures = new Dictionary<string, GisNewDrawingSdfFeature>(StringComparer.Ordinal);
            AddExpected(pipes, sdf.Pipes, "Pipes");
            AddExpected(structures, sdf.Structures, "Structures");
            if (pipes.Count + structures.Count == 0)
                throw new InvalidOperationException("The selected SDF contains no Pipes or Structures to import.");
            IReadOnlyList<GisNewDrawingObjectDataTableSchema> existingSchemas = ReadTargetOdSchemas(profile);
            IReadOnlyList<GisNewDrawingObjectDataClassPlan> odPlans = GisNewDrawingObjectDataPlan.Build(
                profile, sdf.FieldTypesByClass, existingSchemas);
            VerifyMappedSourceValues(sdf, odPlans);
            object importer = Get(Application(), "Importer");
            Invoke(importer, "Init", profile.FormatName, sdf.FilePath);
            // LoadImportFormat returns SCHEMA CHANGED, not success. Exceptions indicate
            // failure, and all input-layer/column wrappers are acquired after this call.
            object? changed = Invoke(importer, "LoadImportFormat", profilePath);
            if (changed is not bool) throw new InvalidOperationException("LoadImportFormat returned an unexpected result.");
            ConfigureAndVerifyProfile(importer, profile, odPlans, document);

            HashSet<ObjectId> before = ModelSpaceIds(document.Database);
            object result = Invoke(importer, "Import", true)
                ?? throw new InvalidOperationException("Map import returned no ImportResults.");
            long count = Convert.ToInt64(Get(result, "EntitiesImported"), CultureInfo.InvariantCulture);
            long skipped = Convert.ToInt64(Get(result, "EntitiesSkippedCouldNotTransform"), CultureInfo.InvariantCulture);
            if (skipped != 0) throw new InvalidOperationException($"Map import skipped {skipped} entity(s) because coordinate transformation failed.");
            if (count != sdf.Pipes.Count + sdf.Structures.Count)
                throw new InvalidOperationException($"Map import count {count} differs from the SDF's {sdf.Pipes.Count} Pipes + {sdf.Structures.Count} Structures.");
            VerifyProjection(profile.SourceCoordinateSystem);
            VerifyImportedOdSchemas(profile, sdf, odPlans);

            ObjectId[] imported = ModelSpaceIds(document.Database).Where(id => !before.Contains(id)).ToArray();
            if (imported.Length != count)
                throw new InvalidOperationException($"Imported model-space count {imported.Length} differs from Map's result {count}.");
            using Transaction tr = document.Database.TransactionManager.StartOpenCloseTransaction();
            foreach (ObjectId id in imported)
            {
                var entity = tr.GetObject(id, OpenMode.ForRead, false) as Entity
                    ?? throw new InvalidOperationException($"Imported object {id.Handle} is not an entity.");
                bool isPipe = entity.Layer == "Pipes";
                if ((!isPipe && entity.Layer != "Structures") || (isPipe ? entity is not Curve : entity is not DBPoint))
                    throw new InvalidOperationException($"Unexpected imported object {id.Handle}: {entity.GetType().Name} on {entity.Layer}.");
                string tableName = isPipe ? "Pipes" : "Structures";
                GisNewDrawingObjectDataClassPlan odPlan = odPlans.Single(plan => plan.TableName == tableName);
                IReadOnlyDictionary<string, object?> values = ReadSingleRecord(id, odPlan);
                if (!values.TryGetValue("Name", out object? nameValue) || nameValue is not string name || string.IsNullOrEmpty(name))
                    throw new InvalidOperationException($"Imported {tableName} object {id.Handle} has no nonempty OD Name.");
                Dictionary<string, GisNewDrawingSdfFeature> expected = isPipe ? pipes : structures;
                if (!expected.TryGetValue(name, out GisNewDrawingSdfFeature? feature))
                    throw new InvalidOperationException($"Imported object {id.Handle} has unexpected or duplicate {tableName} Name '{name}'.");
                VerifyGeometry(entity, feature, tr);
                GisNewDrawingProfileTable table = profile.SelectedTables.Single(item => item.LayerName == tableName);
                if (values.Count != table.MappedColumns.Count)
                    throw new InvalidOperationException($"Imported {tableName} OD schema has {values.Count} fields; expected {table.MappedColumns.Count}.");
                foreach (GisNewDrawingProfileColumn column in table.MappedColumns)
                {
                    if (!feature.Scalars.TryGetValue(column.ColumnName, out object? original) ||
                        !values.TryGetValue(column.OutputColumnName, out object? actual) || !SameValue(original, actual))
                        throw new InvalidOperationException($"Imported {tableName} '{name}', handle {id.Handle}: OD field {column.OutputColumnName} differs from the SDF.");
                }
                expected.Remove(name);
            }
            if (pipes.Count != 0 || structures.Count != 0)
                throw new InvalidOperationException($"Imported OD identities are incomplete: {pipes.Count} pipe(s), {structures.Count} structure(s) missing.");
            return new ImportSummary(sdf.Pipes.Count, sdf.Structures.Count);
        }

        private static void VerifyGeometry(Entity entity, GisNewDrawingSdfFeature feature, Transaction transaction)
        {
            var actual = new List<Point3d>();
            if (feature.GeometryType == "Point" && entity is DBPoint point)
                actual.Add(point.Position);
            else if (feature.GeometryType == "LineString")
            {
                switch (entity)
                {
                    case Line line:
                        actual.Add(line.StartPoint); actual.Add(line.EndPoint); break;
                    case Polyline polyline when !polyline.Closed:
                        for (int i = 0; i < polyline.NumberOfVertices; i++)
                        {
                            if (polyline.GetBulgeAt(i) != 0)
                                throw new InvalidOperationException($"Imported pipe {entity.Handle} contains arcs absent from the SDF LineString.");
                            actual.Add(polyline.GetPoint3dAt(i));
                        }
                        break;
                    case Polyline2d polyline when !polyline.Closed && polyline.PolyType == Poly2dType.SimplePoly:
                        int vertexIndex = 0;
                        foreach (ObjectId vertexId in polyline)
                        {
                            var vertex = (Vertex2d)transaction.GetObject(vertexId, OpenMode.ForRead);
                            if (vertex.Bulge != 0)
                                throw new InvalidOperationException($"Imported pipe {entity.Handle} contains arcs absent from the SDF LineString.");
                            actual.Add(polyline.GetPointAtParameter(polyline.StartParam + vertexIndex++));
                        }
                        break;
                    case Polyline3d polyline when !polyline.Closed && polyline.PolyType == Poly3dType.SimplePoly:
                        foreach (ObjectId vertexId in polyline)
                            actual.Add(((PolylineVertex3d)transaction.GetObject(vertexId, OpenMode.ForRead)).Position);
                        break;
                    default:
                        throw new InvalidOperationException($"Cannot verify imported {entity.GetType().Name} geometry against SDF LineString '{feature.Name}'.");
                }
            }
            else throw new InvalidOperationException($"Imported geometry kind differs from SDF '{feature.Name}': {feature.GeometryType}.");
            if (actual.Count != feature.Coordinates.Count)
                throw new InvalidOperationException($"Imported '{feature.Name}', handle {entity.Handle}: vertex count differs from the SDF.");
            const double tolerance = 0.000001; // absolute drawing units; no relative tolerance at large State Plane coordinates
            for (int index = 0; index < actual.Count; index++)
            {
                GisNewDrawingSdfCoordinate expected = feature.Coordinates[index];
                Point3d value = actual[index];
                if (!double.IsFinite(value.X) || !double.IsFinite(value.Y) || !double.IsFinite(value.Z) ||
                    Math.Abs(value.X - expected.X) > tolerance || Math.Abs(value.Y - expected.Y) > tolerance ||
                    Math.Abs(value.Z - (expected.Z ?? 0d)) > tolerance)
                    throw new InvalidOperationException($"Imported '{feature.Name}', handle {entity.Handle}: vertex {index + 1} XYZ differs from the raw SDF coordinates. No transformed/scaled geometry was accepted.");
            }
        }

        private static void AddExpected(Dictionary<string, GisNewDrawingSdfFeature> target,
            IEnumerable<GisNewDrawingSdfFeature> features, string name)
        {
            foreach (GisNewDrawingSdfFeature feature in features)
                if (string.IsNullOrEmpty(feature.Name) || !target.TryAdd(feature.Name, feature))
                    throw new InvalidOperationException($"SDF {name} has an empty or duplicate Name '{feature.Name}'; identities cannot be verified unambiguously.");
        }

        private static void ConfigureAndVerifyProfile(object importer, GisNewDrawingProfile profile,
            IReadOnlyList<GisNewDrawingObjectDataClassPlan> odPlans, Document document)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            // Reassert only the profile's explicit no-clipping setting. A viewport-sized
            // import from an initially empty drawing would otherwise silently lose data.
            MethodInfo windowMethod = importer.GetType().GetMethods().Single(method =>
                method.Name == "SetLocationWindowAndOptions" && method.GetParameters().Length == 5);
            Type locationType = windowMethod.GetParameters()[4].ParameterType;
            Invoke(importer, "SetLocationWindowAndOptions", 0d, 0d, 0d, 0d, Enum.Parse(locationType, "DoNotUse"));

            foreach (object layer in Enumerate(importer))
            {
                try
                {
                    string name = Text(Get(layer, "Name"));
                    bool selected = (bool)Get(layer, "ImportFromInputLayerOn");
                    GisNewDrawingProfileTable? expected = profile.Tables.SingleOrDefault(table => table.InputClass == name);
                    if (expected == null)
                        throw new InvalidOperationException("SDF exposes an input class absent from the validated profile: " + name);
                    if (!seen.Add(name)) throw new InvalidOperationException("Duplicate native input layer: " + name);
                    if (selected != expected.Selected)
                        throw new InvalidOperationException($"IPF selection did not load as expected for {name}: selected={selected}.");
                    if (!selected) continue;
                    // The documented property name is counterintuitive: it sets
                    // the incoming/from CRS shown in MAPIMPORT's Coordinate System
                    // column. Explicitly interpret raw SDF coordinates in the source
                    // drawing's assigned code; the new drawing has that same code.
                    // This overrides embedded SDF labels in this import session only.
                    GisNewDrawingCoordinateSystem.AssignSameCode(profile.SourceCoordinateSystem,
                        code => SetTextProperty(layer, "TargetCoordinateSystem", code),
                        () => Get(layer, "TargetCoordinateSystem") as string,
                        name + " incoming import coordinate system");
                    GisNewDrawingNativeOutputs.EnumTextOutput layerMapping = GisNewDrawingNativeOutputs.ReadPair(layer, "LayerName");
                    string layerMode = Text(layerMapping.Mode);
                    string layerName = Text(layerMapping.Text);
                    if (layerMode != "LayerNameDirect" || layerName != expected.LayerName)
                        throw new InvalidOperationException("Native CAD layer mapping differs from the profile: " + name);
                    GisNewDrawingObjectDataClassPlan odPlan = odPlans.Single(plan => plan.InputClass == name);
                    GisNewDrawingNativeOutputs.EnumTextOutput loadedMapping = GisNewDrawingNativeOutputs.ReadPair(layer, "DataMapping");
                    document.Editor.WriteMessage($"\nOD setup: {name}; loaded {loadedMapping.Mode} / '{Text(loadedMapping.Text)}'; " +
                        $"applying {odPlan.MappingMode} / '{odPlan.TableName}' from the profile's explicit column mappings.");
                    // New mode creates the table during Import, not here. Existing
                    // mode was approved only after exact schema/type comparison.
                    // No shared IPF or existing table definition is edited.
                    Invoke(layer, "SetDataMapping", Enum.Parse(loadedMapping.Mode.GetType(), odPlan.MappingMode), odPlan.TableName);
                    ConfigureColumns(layer, expected, odPlan);
                    GisNewDrawingNativeOutputs.EnumTextOutput tableMapping = GisNewDrawingNativeOutputs.ReadPair(layer, "DataMapping");
                    string mode = Text(tableMapping.Mode);
                    string tableName = Text(tableMapping.Text);
                    if (mode != odPlan.MappingMode || tableName != odPlan.TableName)
                        throw new InvalidOperationException($"Native OD setup for {name} did not retain {odPlan.MappingMode} / '{odPlan.TableName}': " +
                            $"read {mode} / '{tableName}'. No import was run.");
                    if (expected.LayerName == "Structures")
                    {
                        GisNewDrawingNativeOutputs.EnumTextOutput pointMapping = GisNewDrawingNativeOutputs.ReadPair(layer, "PointToBlockMapping");
                        if (Text(pointMapping.Mode) != "MapPointToPoint")
                            throw new InvalidOperationException("The Structures profile does not import native DBPoints.");
                    }
                    VerifyColumns(layer, expected, odPlan);
                }
                finally { DisposeOwned(layer); }
            }
            foreach (GisNewDrawingProfileTable table in profile.SelectedTables)
                if (!seen.Contains(table.InputClass)) throw new InvalidOperationException("SDF is missing selected input class " + table.InputClass);
        }

        private static void ConfigureColumns(object layer, GisNewDrawingProfileTable expected, GisNewDrawingObjectDataClassPlan plan)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var exactNames = new HashSet<string>(StringComparer.Ordinal);
            // Acquire columns after SetDataMapping initializes its defaults. All
            // incoming columns are covered, including fields absent from the IPF.
            foreach (object column in Enumerate(layer))
            {
                string name = "<unread>";
                try
                {
                    name = Text(Get(column, "ColumnName"));
                    if (string.IsNullOrEmpty(name) || !seen.Add(name))
                        throw new InvalidOperationException("Blank, duplicate or case-colliding input column: " + name);
                    exactNames.Add(name);
                    GisNewDrawingObjectDataFieldPlan? field = plan.Fields.SingleOrDefault(item => item.SourceName == name);
                    // New mode auto-maps all source fields. Explicitly remove the
                    // unmapped fields and retain only the validated IPF allowlist.
                    Invoke(column, "SetColumnDataMapping", field?.OutputName ?? string.Empty);
                }
                catch (System.Exception ex)
                {
                    throw new InvalidOperationException($"Runtime OD mapping failed for {expected.InputClass}.{name}.", ex);
                }
                finally { DisposeOwned(column); }
            }
            foreach (GisNewDrawingObjectDataFieldPlan field in plan.Fields)
                if (!exactNames.Contains(field.SourceName))
                    throw new InvalidOperationException($"SDF lacks exact mapped field {expected.InputClass}.{field.SourceName}.");
        }

        private static void VerifyColumns(object layer, GisNewDrawingProfileTable expected, GisNewDrawingObjectDataClassPlan plan)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var exactNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (object column in Enumerate(layer))
            {
                try
                {
                    string name = Text(Get(column, "ColumnName"));
                    if (string.IsNullOrEmpty(name) || !seen.Add(name))
                        throw new InvalidOperationException("Blank, duplicate or case-colliding native input column: " + name);
                    exactNames.Add(name);
                    GisNewDrawingNativeOutputs.EnumTextOutput columnMapping = GisNewDrawingNativeOutputs.ReadColumnMapping(column);
                    string actualOutput = Text(columnMapping.Text);
                    string actualMode = Text(columnMapping.Mode);
                    GisNewDrawingObjectDataPlan.VerifyColumnMapping(plan, name, actualMode, actualOutput);
                }
                finally { DisposeOwned(column); }
            }
            foreach (GisNewDrawingProfileColumn column in expected.MappedColumns)
                if (!exactNames.Contains(column.ColumnName)) throw new InvalidOperationException($"SDF lacks mapped field {expected.InputClass}.{column.ColumnName}.");
        }

        private static IReadOnlyList<GisNewDrawingObjectDataTableSchema> ReadTargetOdSchemas(GisNewDrawingProfile profile)
        {
            object project = Project();
            object drawingSet = Get(project, "DrawingSet"); // borrowed project singleton
            if (Convert.ToInt32(Get(drawingSet, "AllDrawingsCount"), CultureInfo.InvariantCulture) != 0)
                throw new InvalidOperationException("The new GIS drawing has attached Map drawings. Local OD table ownership is ambiguous; no mappings or attached tables were changed.");
            object tables = Get(project, "ODTables"); // borrowed host container
            object names = Invoke(tables, "GetTableNames") ?? throw new InvalidOperationException("Native OD table inventory is unavailable.");
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var schemas = new List<GisNewDrawingObjectDataTableSchema>();
            try
            {
                foreach (object item in Enumerate(names))
                {
                    if (item is not string name || string.IsNullOrWhiteSpace(name) || !seen.Add(name))
                        throw new InvalidOperationException("Native OD table inventory has a blank, duplicate or case-colliding name.");
                    if (!profile.SelectedTables.Any(table => string.Equals(table.LayerName, name, StringComparison.OrdinalIgnoreCase))) continue;
                    object? table = null, definitions = null;
                    try
                    {
                        table = Invoke(tables, "get_Item", name) ?? throw new InvalidOperationException("Native OD table is unavailable: " + name);
                        string actualName = Text(Get(table, "Name"));
                        if (actualName != name) throw new InvalidOperationException($"OD table inventory '{name}' resolves to '{actualName}'.");
                        definitions = Get(table, "FieldDefinitions");
                        int count = Convert.ToInt32(Get(definitions, "Count"), CultureInfo.InvariantCulture);
                        if (count < 0 || count > 10000) throw new InvalidOperationException("Native OD field count exceeds the schema verification limit.");
                        var fields = new List<GisNewDrawingObjectDataFieldSchema>();
                        for (int index = 0; index < count; index++)
                        {
                            object definition = Invoke(definitions, "get_Item", index) ?? throw new InvalidOperationException("OD field definition is unavailable.");
                            try { fields.Add(new GisNewDrawingObjectDataFieldSchema(Text(Get(definition, "Name")), Text(Get(definition, "Type")))); }
                            finally { DisposeOwned(definition); }
                        }
                        schemas.Add(new GisNewDrawingObjectDataTableSchema(actualName, fields));
                    }
                    finally { DisposeOwned(definitions); DisposeOwned(table); }
                }
            }
            finally { DisposeOwned(names); }
            foreach (GisNewDrawingProfileTable table in profile.SelectedTables)
            {
                bool exists = Invoke(tables, "IsTableDefined", table.LayerName) is bool present ? present
                    : throw new InvalidOperationException("Native OD table existence result is invalid.");
                if (exists != schemas.Any(schema => string.Equals(schema.TableName, table.LayerName, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Native OD table inventory and accessible-table lookup disagree for " + table.LayerName);
            }
            return Array.AsReadOnly(schemas.ToArray());
        }

        private static void VerifyMappedSourceValues(GisNewDrawingSdfSnapshot sdf, IReadOnlyList<GisNewDrawingObjectDataClassPlan> plans)
        {
            foreach (GisNewDrawingObjectDataClassPlan plan in plans)
            {
                IReadOnlyList<GisNewDrawingSdfFeature> features = plan.InputClass == GisNewDrawingProfile.PipesInputClass ? sdf.Pipes : sdf.Structures;
                foreach (GisNewDrawingSdfFeature feature in features)
                    foreach (GisNewDrawingObjectDataFieldPlan field in plan.Fields)
                        if (!feature.Scalars.TryGetValue(field.SourceName, out object? value) || value == null)
                            throw new InvalidOperationException($"{plan.InputClass} '{feature.Name}' has a missing/null mapped {field.SourceName} value. " +
                                "This workflow cannot prove null-preserving OD import; no default value was substituted.");
            }
        }

        private static void VerifyImportedOdSchemas(GisNewDrawingProfile profile, GisNewDrawingSdfSnapshot sdf,
            IReadOnlyList<GisNewDrawingObjectDataClassPlan> originalPlans)
        {
            IReadOnlyList<GisNewDrawingObjectDataTableSchema> schemas = ReadTargetOdSchemas(profile);
            // Rebuilding validates every materialized table's exact names/types.
            GisNewDrawingObjectDataPlan.Build(profile, sdf.FieldTypesByClass, schemas);
            foreach (GisNewDrawingObjectDataClassPlan plan in originalPlans)
            {
                int count = plan.InputClass == GisNewDrawingProfile.PipesInputClass ? sdf.Pipes.Count : sdf.Structures.Count;
                bool exists = schemas.Any(schema => schema.TableName == plan.TableName);
                if (!exists && (count != 0 || plan.MappingMode == "ExistingObjectDataOnly"))
                    throw new InvalidOperationException($"Required imported OD table '{plan.TableName}' is missing.");
                // Documented native behavior: a new table is created only when its
                // first entity is imported. A zero-row new class can have no table.
            }
        }

        private static IReadOnlyDictionary<string, object?> ReadSingleRecord(ObjectId id, GisNewDrawingObjectDataClassPlan plan)
        {
            string tableName = plan.TableName;
            object tables = Get(Project(), "ODTables"); // borrowed; never dispose
            object? table = null, records = null, definitions = null;
            try
            {
                if (Invoke(tables, "IsTableDefined", tableName) is not true)
                    throw new InvalidOperationException("Imported OD table is missing: " + tableName);
                table = Invoke(tables, "get_Item", tableName) ?? throw new InvalidOperationException("OD table is unavailable.");
                Type readModeType = table.GetType().Assembly.GetType("Autodesk.Gis.Map.Constants.OpenMode", true)!;
                records = Invoke(table, "GetObjectTableRecords", 0u, id, Enum.Parse(readModeType, "OpenForRead"), false)
                    ?? throw new InvalidOperationException("OD records are unavailable.");
                if (Convert.ToInt32(Get(records, "Count"), CultureInfo.InvariantCulture) != 1)
                    throw new InvalidOperationException($"Imported {tableName} entity {id.Handle} must have exactly one OD record.");
                definitions = Get(table, "FieldDefinitions");
                int count = Convert.ToInt32(Get(definitions, "Count"), CultureInfo.InvariantCulture);
                var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                int read = 0;
                foreach (object record in Enumerate(records))
                {
                    try
                    {
                        if (++read != 1 || Convert.ToInt32(Get(record, "Count"), CultureInfo.InvariantCulture) != count)
                            throw new InvalidOperationException("Imported OD record/schema counts differ.");
                        for (int i = 0; i < count; i++)
                        {
                            object? definition = null, value = null;
                            try
                            {
                                definition = Invoke(definitions, "get_Item", i) ?? throw new InvalidOperationException("OD definition missing.");
                                value = Invoke(record, "get_Item", i) ?? throw new InvalidOperationException("OD value missing.");
                                string field = Text(Get(definition, "Name"));
                                string kind = Text(Get(value, "Type"));
                                GisNewDrawingObjectDataFieldPlan? expectedField = plan.Fields.SingleOrDefault(item => item.OutputName == field);
                                if (expectedField == null || kind != expectedField.ObjectDataType)
                                    throw new InvalidOperationException($"Imported {tableName}.{field} has OD value type '{kind}', expected '{expectedField?.ObjectDataType ?? "<unmapped>"}'.");
                                object scalar = kind switch
                                {
                                    "Character" => Get(value, "StrValue"),
                                    "Integer" => Get(value, "Int32Value"),
                                    "Real" => Get(value, "DoubleValue"),
                                    _ => throw new InvalidOperationException("Unsupported imported OD scalar type: " + kind)
                                };
                                if (!result.TryAdd(field, scalar)) throw new InvalidOperationException("Duplicate imported OD field: " + field);
                            }
                            finally { DisposeOwned(value); DisposeOwned(definition); }
                        }
                    }
                    finally { DisposeOwned(record); }
                }
                if (read != 1) throw new InvalidOperationException("Imported OD enumeration was incomplete.");
                return result;
            }
            finally { DisposeOwned(definitions); DisposeOwned(records); DisposeOwned(table); }
        }

        private static bool SameValue(object? source, object? destination)
        {
            if (source is string text) return destination is string actual && string.Equals(text, actual, StringComparison.Ordinal);
            if (source == null || destination == null) return source == null && destination == null;
            if (source is IConvertible && destination is IConvertible && source is not bool && destination is not string)
            {
                double left = Convert.ToDouble(source, CultureInfo.InvariantCulture);
                double right = Convert.ToDouble(destination, CultureInfo.InvariantCulture);
                return double.IsFinite(left) && double.IsFinite(right) && Math.Abs(left - right) <= 1e-10 * Math.Max(1d, Math.Abs(left));
            }
            return Equals(source, destination);
        }

        private static HashSet<ObjectId> ModelSpaceIds(Database db)
        {
            using Transaction tr = db.TransactionManager.StartOpenCloseTransaction();
            var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var model = (BlockTableRecord)tr.GetObject(blocks[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            return model.Cast<ObjectId>().Where(id => !id.IsErased).ToHashSet();
        }

        private static object Application()
        {
            Type type = Assembly.Load("ManagedMapApi").GetType("Autodesk.Gis.Map.HostMapApplicationServices", true)!;
            return type.GetProperty("Application", BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                ?? throw new InvalidOperationException("Map application is unavailable.");
        }
        private static object Project() => Get(Application(), "ActiveProject");
        private static object Get(object target, string name) => target.GetType().GetProperty(name)?.GetValue(target)
            ?? throw new InvalidOperationException($"Native property {target.GetType().FullName}.{name} is unavailable.");
        private static void SetTextProperty(object target, string name, string value)
        {
            PropertyInfo property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
                ?? throw new MissingMemberException(target.GetType().FullName, name);
            if (!property.CanRead || !property.CanWrite || property.PropertyType != typeof(string))
                throw new InvalidOperationException($"Native {target.GetType().FullName}.{name} must be a readable/writable coordinate-system string.");
            property.SetValue(target, value);
        }
        private static string Text(object? value) => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        private static object? Invoke(object target, string name, params object?[] arguments)
        {
            MethodInfo[] methods = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(method => method.Name == name && method.GetParameters().Length == arguments.Length &&
                    method.GetParameters().Select((parameter, index) =>
                        parameter.ParameterType.IsByRef || arguments[index] == null || parameter.ParameterType.IsInstanceOfType(arguments[index])).All(match => match)).ToArray();
            if (methods.Length != 1)
                throw new MissingMethodException($"Expected one native {target.GetType().FullName}.{name} overload, found {methods.Length}.");
            return methods[0].Invoke(target, arguments);
        }
        private static IEnumerable<object> Enumerate(object target)
        {
            IEnumerator enumerator = (target as IEnumerable)?.GetEnumerator()
                ?? Invoke(target, "GetEnumerator") as IEnumerator
                ?? throw new InvalidOperationException("Native collection is not enumerable: " + target.GetType().FullName);
            try { while (enumerator.MoveNext()) yield return enumerator.Current ?? throw new InvalidOperationException("Null native collection item."); }
            finally { DisposeOwned(enumerator); }
        }
        private static void DisposeOwned(object? value) { if (value is IDisposable disposable) disposable.Dispose(); }
    }
}
