using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CLV_CivilTools.Gis
{
    /// <summary>
    /// Pure, immutable runtime interpretation of the profile's explicit MappedToOD
    /// columns. Source types come from FDO schema, including empty classes; feature
    /// values never determine a table definition. This does not modify the IPF or
    /// prove that the native importer accepted or materialized these mappings.
    /// </summary>
    internal static class GisNewDrawingObjectDataPlan
    {
        internal static IReadOnlyList<GisNewDrawingObjectDataClassPlan> Build(
            GisNewDrawingProfile profile,
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> sourceFieldTypesByClass,
            IEnumerable<GisNewDrawingObjectDataTableSchema> existingSchemas)
        {
            ArgumentNullException.ThrowIfNull(profile);
            ArgumentNullException.ThrowIfNull(sourceFieldTypesByClass);
            ArgumentNullException.ThrowIfNull(existingSchemas);

            var sourceClasses = sourceFieldTypesByClass.ToArray();
            RequireUniqueNames(sourceClasses.Select(item => item.Key), "FDO source classes");
            Require(sourceClasses.Length == profile.SelectedTables.Count &&
                sourceClasses.All(item => profile.SelectedTables.Any(table => table.InputClass == item.Key)),
                "FDO field schemas must contain exactly the selected profile input classes, with exact names.");

            GisNewDrawingObjectDataTableSchema[] existing = existingSchemas.ToArray();
            Require(existing.All(table => table != null), "An existing OD table schema is null.");
            RequireUniqueNames(existing.Select(table => table.TableName), "existing OD tables");

            var plans = new List<GisNewDrawingObjectDataClassPlan>();
            foreach (GisNewDrawingProfileTable table in profile.SelectedTables)
            {
                // Use the actual ordinal key after validating it. A caller's
                // case-insensitive dictionary must not silently resolve aliases.
                IReadOnlyDictionary<string, string> sourceFields = sourceClasses.Single(item => item.Key == table.InputClass).Value;
                Require(sourceFields != null, table.InputClass + ": the FDO field schema is null.");
                KeyValuePair<string, string>[] source = sourceFields!.ToArray();
                RequireUniqueNames(source.Select(item => item.Key), table.InputClass + " FDO fields");
                Require(source.Length == table.MappedColumns.Count &&
                    source.All(item => table.MappedColumns.Any(column => column.ColumnName == item.Key)),
                    table.InputClass + ": FDO field schema must contain exactly the explicitly mapped profile fields, with exact names.");

                var fields = new List<GisNewDrawingObjectDataFieldPlan>();
                foreach (GisNewDrawingProfileColumn column in table.MappedColumns)
                {
                    string sourceType = source.Single(item => item.Key == column.ColumnName).Value;
                    fields.Add(new GisNewDrawingObjectDataFieldPlan(column.ColumnName, column.OutputColumnName,
                        sourceType, ObjectDataType(sourceType, table.InputClass + "." + column.ColumnName)));
                }

                GisNewDrawingObjectDataTableSchema? target = existing.SingleOrDefault(item =>
                    string.Equals(item.TableName, table.LayerName, StringComparison.OrdinalIgnoreCase));
                string mode = "NewObjectDataOnly";
                if (target != null)
                {
                    Require(target.TableName == table.LayerName,
                        $"OD table '{target.TableName}' conflicts with exact target '{table.LayerName}'; case aliases are not allowed.");
                    RequireUniqueNames(target.Fields.Select(field => field.Name), table.LayerName + " OD fields");
                    Require(target.Fields.Count == fields.Count && target.Fields.All(field =>
                        fields.Any(expected => expected.OutputName == field.Name)),
                        table.LayerName + ": existing OD field names must exactly match the mapped fields; extra, missing or case-aliased fields are not allowed.");
                    foreach (GisNewDrawingObjectDataFieldPlan field in fields)
                    {
                        GisNewDrawingObjectDataFieldSchema actual = target.Fields.Single(item => item.Name == field.OutputName);
                        Require(actual.ObjectDataType == field.ObjectDataType,
                            $"{table.LayerName}.{field.OutputName}: existing OD type '{actual.ObjectDataType}' differs from schema-derived '{field.ObjectDataType}'.");
                    }
                    mode = "ExistingObjectDataOnly";
                }
                plans.Add(new GisNewDrawingObjectDataClassPlan(table.InputClass, table.LayerName, mode, fields));
            }

            // No partial plan escapes if either selected class is incompatible.
            return GisNewDrawingProfile.Snapshot(plans);
        }

        /// <summary>
        /// Verifies one native column readback. Map may report the layer's OD mode
        /// for a cleared column or leave that column's enum unwritten; the empty
        /// output name is still required. Null mode represents only a verified
        /// unwritten enum, not a default mode or a missing mapped field. The caller
        /// separately verifies that every required exact source name exists.
        /// </summary>
        internal static void VerifyColumnMapping(GisNewDrawingObjectDataClassPlan plan,
            string sourceName, string? actualMode, string actualOutput)
        {
            ArgumentNullException.ThrowIfNull(plan);
            GisNewDrawingObjectDataFieldPlan? expected = plan.Fields.SingleOrDefault(field => field.SourceName == sourceName);
            if (expected != null)
            {
                Require(actualMode == plan.MappingMode && actualOutput == expected.OutputName,
                    $"{plan.InputClass}.{sourceName}: native OD column mapping '{actualMode ?? "<unwritten>"}' / '{actualOutput}' " +
                    $"differs from planned '{plan.MappingMode}' / '{expected.OutputName}'.");
            }
            else
            {
                Require(actualOutput == string.Empty &&
                    (actualMode == null || actualMode == "NoImportMapping" || actualMode == plan.MappingMode),
                    $"{plan.InputClass}.{sourceName}: an unmapped native column must have empty output and " +
                    $"an unwritten enum, NoImportMapping or {plan.MappingMode}; " +
                    $"found '{actualMode ?? "<unwritten>"}' / '{actualOutput}'.");
            }
        }

        private static string ObjectDataType(string sourceType, string field) => sourceType switch
        {
            "DataType_String" => "Character",
            "DataType_Int16" or "DataType_Int32" => "Integer",
            "DataType_Single" or "DataType_Double" => "Real",
            _ => throw new InvalidDataException($"{field}: FDO type '{sourceType}' has no approved lossless Object Data mapping.")
        };

        private static void RequireUniqueNames(IEnumerable<string> names, string context)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
                Require(!string.IsNullOrWhiteSpace(name) && seen.Add(name),
                    context + ": blank, duplicate or case-colliding names are not allowed: '" + name + "'.");
        }

        private static void Require(bool condition, string detail)
        {
            if (!condition) throw new InvalidDataException(detail);
        }
    }

    internal sealed class GisNewDrawingObjectDataTableSchema
    {
        internal GisNewDrawingObjectDataTableSchema(string tableName,
            IEnumerable<GisNewDrawingObjectDataFieldSchema> fields)
        {
            ArgumentNullException.ThrowIfNull(fields);
            TableName = tableName;
            Fields = GisNewDrawingProfile.Snapshot(fields);
            if (Fields.Any(field => field == null)) throw new InvalidDataException("An OD field schema is null.");
        }

        internal string TableName { get; }
        internal IReadOnlyList<GisNewDrawingObjectDataFieldSchema> Fields { get; }
    }

    internal sealed record GisNewDrawingObjectDataFieldSchema(string Name, string ObjectDataType);

    internal sealed class GisNewDrawingObjectDataClassPlan
    {
        internal GisNewDrawingObjectDataClassPlan(string inputClass, string tableName, string mappingMode,
            IEnumerable<GisNewDrawingObjectDataFieldPlan> fields)
        {
            InputClass = inputClass;
            TableName = tableName;
            MappingMode = mappingMode;
            Fields = GisNewDrawingProfile.Snapshot(fields);
        }

        internal string InputClass { get; }
        internal string TableName { get; }
        internal string MappingMode { get; }
        internal IReadOnlyList<GisNewDrawingObjectDataFieldPlan> Fields { get; }
    }

    internal sealed record GisNewDrawingObjectDataFieldPlan(string SourceName, string OutputName,
        string SourceDataType, string ObjectDataType);
}
