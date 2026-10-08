using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace CLV_CivilTools.Gis
{
    /// <summary>
    /// Reads SDF through the Civil/Map host's installed FDO provider. No SQLite
    /// interpretation, binary string scan, SQL, schema changes, locks or writes.
    /// Reflection avoids deploying a second copy/version of the host FDO runtime.
    ///
    /// API names checked against Map 3D 2026 FDO_API_managed.chm:
    /// FeatureAccessManager.GetConnectionManager; IConnection.CreateCommand;
    /// ISelect.Execute;
    /// IDescribeSchema.SchemaName/Execute; FeatureSchema.Classes;
    /// SchemaElement.Name/FeatureSchema/Parent; ClassDefinition.QualifiedName;
    /// IFeatureCommand.SetFeatureClassName; IFeatureReader.GetClassDefinition,
    /// GetGeometry; FeatureClass.GeometryProperty; IReader scalar getters/Close.
    /// SDF File/ReadOnly connection properties are documented in the OSGeo FDO
    /// Developer Guide (Connection, pp. 165-167) and The Essential FDO (p. 30).
    /// </summary>
    internal static class GisNewDrawingSdf
    {
        private const string FdoAssemblyName = "OSGeo.FDO";
        private const string AccessManagerType = "OSGeo.FDO.ClientServices.FeatureAccessManager";
        private const string CommandTypeName = "OSGeo.FDO.Commands.CommandType";
        private const string GeometryFactoryType = "OSGeo.FDO.Geometry.FgfGeometryFactory";
        private const int MaximumFeaturesPerClass = 1000000;
        private const int MaximumSchemaElements = 10000;

        internal static bool TryRead(string path, GisNewDrawingProfile profile,
            out GisNewDrawingSdfSnapshot? snapshot, out string detail)
        {
            snapshot = null;
            try
            {
                if (profile == null || !GisNewDrawingProfile.IsSupportedCoordinateSystem(profile.SourceCoordinateSystem))
                    throw new InvalidDataException("The source drawing/profile must specify an exact supported CLV coordinate system.");
                string fullPath = Path.GetFullPath(path);
                if (!string.Equals(Path.GetExtension(fullPath), ".sdf", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Select an existing SDF file.");

                // Also deny writes/deletion at the OS file-sharing level for the entire
                // preflight. An active writer or a provider requiring write access fails.
                using var guard = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                long length = guard.Length;
                DateTime modified = File.GetLastWriteTimeUtc(fullPath);
                string hash = Hash(guard);
                IReadOnlyList<GisNewDrawingSdfFeature> pipes;
                IReadOnlyList<GisNewDrawingSdfFeature> structures;
                var fieldTypesByClass = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
                using (var native = new NativeScope())
                {
                    Assembly assembly = FindHostFdoAssembly();
                    Type accessManager = assembly.GetType(AccessManagerType, throwOnError: true)!;
                    Type commandType = assembly.GetType(CommandTypeName, throwOnError: true)!;
                    Type factoryType = assembly.GetType(GeometryFactoryType, throwOnError: false)
                        ?? Assembly.Load(new AssemblyName("OSGeo.FDO.Geometry")).GetType(GeometryFactoryType, throwOnError: true)!;
                    object geometryFactory = native.Own(Activator.CreateInstance(factoryType));
                    object manager = native.Own(CallStatic(accessManager, "GetConnectionManager"));
                    // An unversioned registered provider name is supported by FDO's
                    // Feature Access Manager. Do not load a downloaded/native provider.
                    object connection = native.Own(Call(manager, "CreateConnection", "OSGeo.SDF"), close: true);
                    using (var properties = new NativeScope())
                    {
                        object info = properties.Own(Get(connection, "ConnectionInfo"));
                        object dictionary = properties.Own(Get(info, "ConnectionProperties"));
                        string[] names = Get(dictionary, "PropertyNames") as string[]
                            ?? throw new InvalidDataException("FDO did not expose its connection property names.");
                        if (!names.Contains("File", StringComparer.Ordinal) || !names.Contains("ReadOnly", StringComparer.Ordinal))
                            throw new InvalidDataException("The installed SDF provider does not expose File and ReadOnly. Read-only access cannot be guaranteed.");
                        Call(dictionary, "SetProperty", "File", fullPath);
                        Call(dictionary, "SetProperty", "ReadOnly", "TRUE");
                        if (!string.Equals(Text(Call(dictionary, "GetProperty", "ReadOnly")), "TRUE", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("The SDF provider did not retain ReadOnly=TRUE. The file was not opened.");
                        if (!string.Equals(Text(Call(dictionary, "GetProperty", "File")), fullPath, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("The SDF provider did not retain the selected file path. The file was not opened.");
                    }
                    if (Text(Call(connection, "Open")) != "ConnectionState_Open")
                        throw new InvalidDataException("The read-only SDF connection did not reach ConnectionState_Open.");
                    // The original drawing's supported CRS is authoritative. Read
                    // native coordinates without consulting SDF CRS metadata or
                    // requesting a coordinate transformation from the provider.
                    // Reader definitions may be detached copies. Establish exact schema
                    // membership independently before accepting either feature reader.
                    VerifyClassDeclarations(connection, commandType, profile.SelectedTables);
                    pipes = ReadFeatures(connection, commandType, geometryFactory, profile.SelectedTables.Single(table =>
                        table.InputClass == GisNewDrawingProfile.PipesInputClass), out var pipeFieldTypes);
                    fieldTypesByClass.Add(GisNewDrawingProfile.PipesInputClass, pipeFieldTypes);
                    structures = ReadFeatures(connection, commandType, geometryFactory, profile.SelectedTables.Single(table =>
                        table.InputClass == GisNewDrawingProfile.StructuresInputClass), out var structureFieldTypes);
                    fieldTypesByClass.Add(GisNewDrawingProfile.StructuresInputClass, structureFieldTypes);
                }
                // No success result escapes until readers/commands/connection have
                // closed/disposed and the original bytes have been checked again.
                if (length != guard.Length || modified != File.GetLastWriteTimeUtc(fullPath) || hash != Hash(guard))
                    throw new InvalidDataException("The SDF changed during preflight. Import was not started.");
                snapshot = new GisNewDrawingSdfSnapshot(fullPath, length, modified, hash,
                    profile.SourceCoordinateSystem, pipes, structures, fieldTypesByClass);
                detail = string.Empty;
                return true;
            }
            catch (System.Exception ex)
            {
                snapshot = null;
                detail = "SDF preflight failed: " + ExceptionDetail(ex);
                return false;
            }
        }

        internal static bool TryVerifyUnchanged(GisNewDrawingSdfSnapshot snapshot, out string detail)
        {
            try
            {
                using var stream = new FileStream(snapshot.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (stream.Length != snapshot.FileLength || File.GetLastWriteTimeUtc(snapshot.FilePath) != snapshot.LastWriteTimeUtc ||
                    Hash(stream) != snapshot.FileSha256)
                    throw new InvalidDataException("The selected SDF differs from the preflight snapshot. Run preflight again before importing.");
                detail = string.Empty;
                return true;
            }
            catch (System.Exception ex)
            {
                detail = "SDF verification failed: " + ExceptionDetail(ex);
                return false;
            }
        }

        private static IReadOnlyList<GisNewDrawingSdfFeature> ReadFeatures(object connection, Type commandType, object geometryFactory,
            GisNewDrawingProfileTable table, out IReadOnlyDictionary<string, string> fieldTypes)
        {
            using var scope = new NativeScope();
            object command = scope.Own(Call(connection, "CreateCommand", Enum.Parse(commandType, "CommandType_Select")));
            Call(command, "SetFeatureClassName", table.InputClass);
            // No filter or property projection: enumerate every feature, including
            // explicit null-structure stubs. ExecuteWithLock must never be used.
            object reader = scope.Own(Call(command, "Execute"), close: true);
            object definition = scope.Own(Call(reader, "GetClassDefinition"));
            // This method is reached only after VerifyClassDeclarations succeeds for
            // all requested classes on this same read-only, file-guarded connection.
            ValidateClassIdentity(scope, definition, table.InputClass, allowDetached: true);
            object geometry = scope.Own(Get(definition, "GeometryProperty"));
            string geometryName = Text(Get(geometry, "Name"));
            if (string.IsNullOrWhiteSpace(geometryName))
                throw new InvalidDataException($"{table.InputClass} has no default geometric property.");
            object properties = scope.Own(Get(definition, "Properties"));
            var fields = new List<(string Name, string Getter)>();
            var declaredTypes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (GisNewDrawingProfileColumn column in table.MappedColumns)
            {
                object property = scope.Own(GetItem(properties, column.ColumnName));
                if (Text(Get(property, "PropertyType")) != "PropertyType_DataProperty")
                    throw new InvalidDataException($"{table.InputClass}.{column.ColumnName} is not an FDO scalar data property.");
                string dataType = Text(Get(property, "DataType"));
                fields.Add((column.ColumnName, ScalarGetter(dataType, table.InputClass + "." + column.ColumnName)));
                // Retain the declaration already read for getter selection, even
                // when the class is empty or every value in this field is null.
                declaredTypes.Add(column.ColumnName, dataType);
            }
            if (!fields.Any(field => field.Name == "Name") || !fields.Any(field => field.Name == "PartSizeName"))
                throw new InvalidDataException($"{table.InputClass}: Name and PartSizeName must both be mapped.");

            var result = new List<GisNewDrawingSdfFeature>();
            while (Boolean(Call(reader, "ReadNext")))
            {
                if (result.Count >= MaximumFeaturesPerClass)
                    throw new InvalidDataException($"{table.InputClass} exceeds the {MaximumFeaturesPerClass} feature preflight safety limit.");
                var values = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach ((string name, string getter) in fields)
                {
                    object? value = Boolean(Call(reader, "IsNull", name)) ? null : Call(reader, getter, name);
                    if (value is double number && !double.IsFinite(number) || value is float single && !float.IsFinite(single))
                        throw new InvalidDataException($"{table.InputClass}.{name} contains a nonfinite scalar at feature {result.Count + 1}.");
                    values.Add(name, value);
                }
                if (values["Name"] is not null and not string || values["PartSizeName"] is not null and not string)
                    throw new InvalidDataException($"{table.InputClass}: Name and PartSizeName must be strings or null.");
                if (Boolean(Call(reader, "IsNull", geometryName)))
                    throw new InvalidDataException($"{table.InputClass}: feature {result.Count + 1} has null geometry.");
                byte[] geometryBytes = Call(reader, "GetGeometry", geometryName) as byte[]
                    ?? throw new InvalidDataException($"{table.InputClass}: FDO did not return native FGF geometry bytes.");
                if (geometryBytes.Length == 0)
                    throw new InvalidDataException($"{table.InputClass}: feature {result.Count + 1} has empty geometry.");
                geometryBytes = (byte[])geometryBytes.Clone();
                // Capture both raw FGF identity and documented native coordinates
                // before the next ReadNext invalidates the reader's buffer.
                (string type, IReadOnlyList<GisNewDrawingSdfCoordinate> coordinates) =
                    DecodeGeometry(geometryFactory, geometryBytes, table.InputClass);
                result.Add(new GisNewDrawingSdfFeature(values, geometryBytes, type, coordinates));
            }
            fieldTypes = declaredTypes;
            return Array.AsReadOnly(result.ToArray());
        }

        private static void VerifyClassDeclarations(object connection, Type commandType,
            IEnumerable<GisNewDrawingProfileTable> tables)
        {
            foreach (var group in tables.GroupBy(table => ClassIdentity(table.InputClass).Schema, StringComparer.Ordinal))
            {
                using var scope = new NativeScope();
                object command = scope.Own(Call(connection, "CreateCommand", Enum.Parse(commandType, "CommandType_DescribeSchema")));
                Set(command, "SchemaName", group.Key);
                object schemas = scope.Own(Call(command, "Execute"));
                int schemaCount = SchemaCount(schemas);
                int matches = 0;
                var declared = group.ToDictionary(table => ClassIdentity(table.InputClass).Class,
                    table => table.InputClass, StringComparer.Ordinal);
                var classMatches = declared.Keys.ToDictionary(name => name, _ => 0, StringComparer.Ordinal);
                var actualSchemas = new List<string>();
                foreach (int index in Enumerable.Range(0, schemaCount))
                {
                    using var schemaScope = new NativeScope();
                    object schema = schemaScope.Own(GetItem(schemas, index));
                    string schemaName = Text(Get(schema, "Name"));
                    actualSchemas.Add(schemaName);
                    // DescribeSchema can also return dependent schemas. They are not
                    // substitutes for the exact requested schema, even with like names.
                    if (!string.Equals(schemaName, group.Key, StringComparison.Ordinal)) continue;
                    matches++;
                    object classes = schemaScope.Own(Get(schema, "Classes"));
                    int count = SchemaCount(classes);
                    for (int classIndex = 0; classIndex < count; classIndex++)
                    {
                        using var classScope = new NativeScope();
                        object definition = classScope.Own(GetItem(classes, classIndex));
                        string name = Text(Get(definition, "Name"));
                        if (!declared.TryGetValue(name, out string? inputClass)) continue;
                        classMatches[name]++;
                        ValidateClassIdentity(classScope, definition, inputClass, allowDetached: false);
                    }
                }
                if (matches != 1)
                    throw new InvalidDataException($"The SDF must declare exactly one schema '{group.Key}'; found {matches}. " +
                        $"DescribeSchema returned [{string.Join(", ", actualSchemas)}].");
                foreach (var required in declared)
                    if (classMatches[required.Key] != 1)
                        throw new InvalidDataException($"The SDF must declare exactly one class '{required.Value}' in schema '{group.Key}'; " +
                            $"found {classMatches[required.Key]}.");
            }
        }

        private static int SchemaCount(object collection)
        {
            int count = Integer(Get(collection, "Count"));
            if (count < 0 || count > MaximumSchemaElements)
                throw new InvalidDataException("The SDF schema element count exceeds the preflight safety limit.");
            return count;
        }

        private static (string Schema, string Class) ClassIdentity(string inputClass)
        {
            // These are the only approved profile input classes. No suffix matching,
            // inferred default schema, aliases or case-insensitive comparisons.
            return inputClass switch
            {
                GisNewDrawingProfile.PipesInputClass => ("Civil_Schema", "Pipes"),
                GisNewDrawingProfile.StructuresInputClass => ("Civil_Schema", "Structures"),
                _ => throw new InvalidDataException($"Unsupported exact SDF input class '{inputClass}'.")
            };
        }

        private static void ValidateClassIdentity(NativeScope scope, object definition, string inputClass, bool allowDetached)
        {
            (string expectedSchema, string expectedClass) = ClassIdentity(inputClass);
            string name = Text(Get(definition, "Name"));
            string qualifiedName = Text(Get(definition, "QualifiedName"));
            object? schema = Get(definition, "FeatureSchema");
            if (schema != null) scope.Own(schema);
            object? parent = Get(definition, "Parent");
            if (parent != null) scope.Own(parent);
            string? schemaName = schema == null ? null : Text(Get(schema, "Name"));
            string? parentName = parent == null ? null : Text(Get(parent, "Name"));
            bool detached = schema == null && parent == null;
            bool qualifiedNameMatches = string.Equals(qualifiedName, inputClass, StringComparison.Ordinal) ||
                (allowDetached && detached && string.Equals(qualifiedName, expectedClass, StringComparison.Ordinal));
            if (!string.Equals(name, expectedClass, StringComparison.Ordinal) || !qualifiedNameMatches ||
                (schema != null && !string.Equals(schemaName, expectedSchema, StringComparison.Ordinal)) ||
                (parent != null && !string.Equals(parentName, expectedSchema, StringComparison.Ordinal)))
                throw new InvalidDataException($"The SDF returned a different class definition for {inputClass}. " +
                    $"Name='{name}', QualifiedName='{qualifiedName}', FeatureSchema='{schemaName ?? "<null>"}', " +
                    $"Parent='{parentName ?? "<null>"}' ({(allowDetached ? "reader" : "declared schema")}).");
        }

        private static (string Type, IReadOnlyList<GisNewDrawingSdfCoordinate> Coordinates) DecodeGeometry(
            object factory, byte[] bytes, string inputClass)
        {
            using var scope = new NativeScope();
            // Map 3D 2026 FDO_API_managed.chm: public FgfGeometryFactory()
            // constructor; CreateGeometryFromFgf(byte[]); IGeometry.DerivedType;
            // IPoint.Position; ILineString.Positions; IDirectPosition.X/Y/Z.
            object geometry = scope.Own(Call(factory, "CreateGeometryFromFgf", bytes));
            string type = Text(Get(geometry, "DerivedType")) switch
            {
                "GeometryType_Point" => "Point",
                "GeometryType_LineString" => "LineString",
                var unsupported => throw new InvalidDataException($"{inputClass}: unsupported native geometry '{unsupported}'. Curves/multipart geometry are not flattened.")
            };
            string required = inputClass == GisNewDrawingProfile.PipesInputClass ? "LineString" : "Point";
            if (type != required)
                throw new InvalidDataException($"{inputClass}: expected {required}, found native {type}.");
            int dimensionality = Integer(Get(geometry, "Dimensionality"));
            // FDO Developer Guide, Geometry/FGF dimensionality: XY=0, Z=1,
            // M=2. Only XY/XYZ are supported; never silently discard measures.
            if (dimensionality != 0 && dimensionality != 1)
                throw new InvalidDataException($"{inputClass}: unsupported ordinate dimensionality {dimensionality}; measures are not discarded.");
            var coordinates = new List<GisNewDrawingSdfCoordinate>();
            if (type == "Point")
            {
                object position = scope.Own(Get(geometry, "Position"));
                coordinates.Add(ReadCoordinate(position, dimensionality, inputClass));
            }
            else
            {
                object positions = scope.Own(Get(geometry, "Positions"));
                int count = Integer(Get(positions, "Count"));
                if (count < 2 || count > MaximumFeaturesPerClass)
                    throw new InvalidDataException($"{inputClass}: unsupported LineString vertex count {count}.");
                for (int index = 0; index < count; index++)
                {
                    using var positionScope = new NativeScope();
                    object position = positionScope.Own(GetItem(positions, index));
                    coordinates.Add(ReadCoordinate(position, dimensionality, inputClass));
                }
            }
            return (type, Array.AsReadOnly(coordinates.ToArray()));
        }

        private static GisNewDrawingSdfCoordinate ReadCoordinate(object position, int dimensionality, string inputClass)
        {
            if (Integer(Get(position, "Dimensionality")) != dimensionality)
                throw new InvalidDataException($"{inputClass}: geometry and coordinate dimensionalities disagree.");
            double x = Number(Get(position, "X")), y = Number(Get(position, "Y"));
            double? z = dimensionality == 1 ? Number(Get(position, "Z")) : null;
            if (!double.IsFinite(x) || !double.IsFinite(y) || z.HasValue && !double.IsFinite(z.Value))
                throw new InvalidDataException($"{inputClass}: native geometry contains nonfinite coordinates.");
            return new GisNewDrawingSdfCoordinate(x, y, z);
        }
        private static int Integer(object value) => value is int number ? number
            : throw new InvalidDataException("FDO returned an unexpected non-Int32 value.");
        private static double Number(object value) => value is double number ? number
            : throw new InvalidDataException("FDO returned an unexpected non-Double coordinate.");

        private static string ScalarGetter(string dataType, string field) => dataType switch
        {
            "DataType_Boolean" => "GetBoolean",
            "DataType_Byte" => "GetByte",
            "DataType_DateTime" => "GetDateTime",
            "DataType_Double" => "GetDouble",
            "DataType_Int16" => "GetInt16",
            "DataType_Int32" => "GetInt32",
            "DataType_Int64" => "GetInt64",
            "DataType_Single" => "GetSingle",
            "DataType_String" => "GetString",
            _ => throw new InvalidDataException($"{field}: unsupported FDO scalar type '{dataType}'. No lossy conversion is permitted.")
        };

        private static Assembly FindHostFdoAssembly()
        {
            Assembly? loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly =>
                assembly.GetName().Name == FdoAssemblyName);
            if (loaded != null) return loaded;
            try { return Assembly.Load(new AssemblyName(FdoAssemblyName)); }
            catch (System.Exception ex)
            {
                throw new InvalidOperationException("The installed Map/Civil FDO managed runtime (OSGeo.FDO) could not be loaded. " +
                    "Run inside supported Civil 3D with Map/FDO installed. Do not substitute SQLite or a downloaded provider.", ex);
            }
        }

        // Resolve only exact documented public API names and CLR argument types.
        // Interface lookup also handles explicit managed-wrapper implementations.
        private static IEnumerable<Type> ApiTypes(object target)
            => new[] { target.GetType() }.Concat(target.GetType().GetInterfaces());

        private static bool ArgumentsMatch(ParameterInfo[] parameters, object[] arguments)
            => parameters.Length == arguments.Length && parameters.Select((parameter, index) =>
                parameter.ParameterType.IsInstanceOfType(arguments[index])).All(match => match);

        private static object CallStatic(Type type, string name)
        {
            MethodInfo method = type.GetMethod(name, BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null)
                ?? throw new MissingMethodException(type.FullName, name);
            return method.Invoke(null, null) ?? throw new InvalidDataException($"FDO {name} returned null.");
        }

        private static object Call(object target, string name, params object[] arguments)
        {
            MethodInfo? method = ApiTypes(target).SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                .FirstOrDefault(candidate => candidate.Name == name && ArgumentsMatch(candidate.GetParameters(), arguments));
            if (method == null) throw new MissingMethodException(target.GetType().FullName, name);
            // Void calls have no value; all read calls are checked by their consumer.
            return method.Invoke(target, arguments)!;
        }

        private static PropertyInfo Property(object target, string name, object[] indexes)
            => ApiTypes(target).SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                .FirstOrDefault(candidate => candidate.Name == name && ArgumentsMatch(candidate.GetIndexParameters(), indexes))
                ?? throw new MissingMemberException(target.GetType().FullName, name);

        private static object Get(object target, string name)
            => Property(target, name, Array.Empty<object>()).GetValue(target)!;
        private static object GetItem(object target, object index)
            => Property(target, "Item", new[] { index }).GetValue(target, new[] { index })!;
        private static void Set(object target, string name, object value)
            => Property(target, name, Array.Empty<object>()).SetValue(target, value);
        private static string Text(object? value) => value?.ToString() ?? string.Empty;
        private static bool Boolean(object value) => value is bool result ? result
            : throw new InvalidDataException("FDO returned an unexpected non-Boolean value.");
        private static string Hash(Stream stream)
        {
            stream.Position = 0;
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        private static string ExceptionDetail(System.Exception error)
        {
            var parts = new List<string>();
            for (System.Exception? current = error; current != null; current = current.InnerException)
                if (current is not TargetInvocationException) parts.Add(current.GetType().Name + ": " + current.Message);
            return string.Join(" -> ", parts);
        }

        private sealed class NativeScope : IDisposable
        {
            private readonly List<(object Value, bool Close)> owned = new();
            internal object Own(object? value, bool close = false)
            {
                if (value == null) throw new InvalidDataException("An expected FDO object was null.");
                if (value is not IDisposable)
                    throw new InvalidDataException($"{value.GetType().FullName} does not expose the documented FDO disposable lifetime.");
                if (!owned.Any(item => ReferenceEquals(item.Value, value))) owned.Add((value, close));
                return value;
            }
            public void Dispose()
            {
                var failures = new List<System.Exception>();
                for (int index = owned.Count - 1; index >= 0; index--)
                {
                    (object value, bool close) = owned[index];
                    if (close)
                    {
                        try { Call(value, "Close"); }
                        catch (System.Exception ex) { failures.Add(ex); }
                    }
                    try { ((IDisposable)value).Dispose(); }
                    catch (System.Exception ex) { failures.Add(ex); }
                }
                owned.Clear();
                if (failures.Count != 0)
                    throw new AggregateException("FDO resources could not be fully closed/disposed; import must not proceed.", failures);
            }
        }
    }

    internal sealed class GisNewDrawingSdfSnapshot
    {
        internal GisNewDrawingSdfSnapshot(string path, long length, DateTime modified, string hash, string sourceCoordinateSystem,
            IEnumerable<GisNewDrawingSdfFeature> pipes, IEnumerable<GisNewDrawingSdfFeature> structures,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> fieldTypesByClass)
        {
            FilePath = path;
            FileLength = length;
            LastWriteTimeUtc = modified;
            FileSha256 = hash;
            CoordinateSystem = sourceCoordinateSystem;
            Pipes = Array.AsReadOnly(pipes.ToArray());
            Structures = Array.AsReadOnly(structures.ToArray());
            FieldTypesByClass = new ReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>(
                fieldTypesByClass.ToDictionary(pair => pair.Key,
                    pair => (IReadOnlyDictionary<string, string>)new ReadOnlyDictionary<string, string>(
                        new Dictionary<string, string>(pair.Value, StringComparer.Ordinal)), StringComparer.Ordinal));
        }
        internal string FilePath { get; }
        internal long FileLength { get; }
        internal DateTime LastWriteTimeUtc { get; }
        internal string FileSha256 { get; }
        // Authoritative original drawing/profile CRS, not a verified SDF CRS.
        internal string CoordinateSystem { get; }
        internal IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> FieldTypesByClass { get; }
        internal IReadOnlyList<GisNewDrawingSdfFeature> Pipes { get; }
        internal IReadOnlyList<GisNewDrawingSdfFeature> Structures { get; }
        internal int PipeCount => Pipes.Count;
        internal int StructureCount => Structures.Count;
    }

    internal sealed record GisNewDrawingSdfCoordinate(double X, double Y, double? Z);

    internal sealed class GisNewDrawingSdfFeature
    {
        private readonly byte[] geometryBytes;
        internal GisNewDrawingSdfFeature(IDictionary<string, object?> scalars, byte[] geometry, string geometryType,
            IEnumerable<GisNewDrawingSdfCoordinate> coordinates)
        {
            Scalars = new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>(scalars, StringComparer.Ordinal));
            geometryBytes = (byte[])geometry.Clone();
            GeometrySha256 = Convert.ToHexString(SHA256.HashData(geometryBytes));
            GeometryType = geometryType;
            Coordinates = Array.AsReadOnly(coordinates.ToArray());
        }
        internal string Name => Scalars.TryGetValue("Name", out object? value) ? value as string ?? string.Empty : string.Empty;
        internal string PartSizeName => Scalars.TryGetValue("PartSizeName", out object? value) ? value as string ?? string.Empty : string.Empty;
        internal IReadOnlyDictionary<string, object?> Scalars { get; }
        internal byte[] GeometryBytes => (byte[])geometryBytes.Clone();
        internal string GeometrySha256 { get; }
        internal string GeometryType { get; }
        internal IReadOnlyList<GisNewDrawingSdfCoordinate> Coordinates { get; }
    }
}
