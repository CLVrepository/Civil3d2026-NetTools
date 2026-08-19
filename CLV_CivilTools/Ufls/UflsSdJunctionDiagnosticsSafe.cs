using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Ufls
{
    /// <summary>
    /// Conservative diagnostic command for SD-JUNCTION structures.
    /// This command avoids recursive object walking and avoids calling most Autodesk property getters.
    /// It reports metadata first so Civil 3D remains stable while we inspect the available API surface.
    /// </summary>
    public static class UflsSdJunctionDiagnosticsSafeCommands
    {
        [CommandMethod("UFLS", "SD-JUNCTION-DUMP-SAFE", CommandFlags.Modal)]
        [CommandMethod("UFLS-SD-JUNCTION-DUMP-SAFE", CommandFlags.Modal)]
        public static void SdJunctionDumpSafe()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    PromptEntityOptions peo = new PromptEntityOptions("\nSelect SD-JUNCTION structure to inspect: ");
                    PromptEntityResult per = ed.GetEntity(peo);
                    if (per.Status != PromptStatus.OK)
                        return;

                    DBObject dbo = tr.GetObject(per.ObjectId, OpenMode.ForRead, false);
                    if (!IsCivilStructure(dbo))
                        throw new InvalidOperationException("Selected object is not a Civil 3D structure.");

                    string reportPath = BuildReportPath(db, per.ObjectId);
                    string report = BuildSafeReport(dbo, per.ObjectId);
                    File.WriteAllText(reportPath, report, Encoding.UTF8);

                    ed.WriteMessage("\nSD-JUNCTION-DUMP-SAFE complete.");
                    ed.WriteMessage($"\nReport written to: {reportPath}");
                    ed.WriteMessage("\nThis safe dump lists type / property metadata and a small set of safe primitive values only.");

                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nSD-JUNCTION-DUMP-SAFE error: {ex.Message}");
            }
        }

        private static string BuildSafeReport(DBObject dbo, ObjectId id)
        {
            Type type = dbo.GetType();
            var sb = new StringBuilder(12288);

            sb.AppendLine("CLV_CivilTools - SD-JUNCTION SAFE DIAGNOSTIC REPORT");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine("OBJECT SUMMARY");
            sb.AppendLine($"CLR Type: {type.FullName}");
            sb.AppendLine($"Assembly: {type.Assembly.FullName}");
            sb.AppendLine($"ObjectId: {id}");
            sb.AppendLine($"Handle: {TryGetHandleString(dbo)}");
            sb.AppendLine($"RX Class: {TryGetRxClassName(dbo)}");
            sb.AppendLine($"DXF Name: {TryGetDxfName(dbo)}");
            sb.AppendLine();

            sb.AppendLine("SAFE VALUE PROBE");
            foreach (string line in GetSafeValueLines(dbo, type))
                sb.AppendLine(line);
            sb.AppendLine();

            sb.AppendLine("PUBLIC INSTANCE PROPERTIES (METADATA ONLY)");
            foreach (PropertyInfo pi in type.GetProperties(BindingFlags.Instance | BindingFlags.Public).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                MethodInfo? getter = pi.GetGetMethod(nonPublic: false);
                MethodInfo? setter = pi.GetSetMethod(nonPublic: false);
                sb.AppendLine($"- {pi.Name} : {GetFriendlyTypeName(pi.PropertyType)} | Read={(getter != null)} | Write={(setter != null)} | Indexer={(pi.GetIndexParameters().Length > 0)}");
            }
            sb.AppendLine();

            sb.AppendLine("FOCUSED SIZE/PART PROPERTY NAMES");
            foreach (PropertyInfo pi in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(IsInterestingPropertyName)
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                MethodInfo? getter = pi.GetGetMethod(nonPublic: false);
                MethodInfo? setter = pi.GetSetMethod(nonPublic: false);
                sb.AppendLine($"- {pi.Name} : {GetFriendlyTypeName(pi.PropertyType)} | Read={(getter != null)} | Write={(setter != null)}");
            }
            sb.AppendLine();

            AppendNamedObjectProbe(sb, dbo, type, "ParamsDouble");
            AppendNamedObjectProbe(sb, dbo, type, "PartData");

            return sb.ToString();
        }

        private static void AppendNamedObjectProbe(StringBuilder sb, object owner, Type ownerType, string propertyName)
        {
            sb.AppendLine($"{propertyName.ToUpperInvariant()} TARGETED PROBE");

            PropertyInfo? pi = ownerType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (pi == null)
            {
                sb.AppendLine("- Property not found.");
                sb.AppendLine();
                return;
            }

            if (pi.GetIndexParameters().Length != 0)
            {
                sb.AppendLine("- Property is an indexer and was skipped.");
                sb.AppendLine();
                return;
            }

            MethodInfo? getter = pi.GetGetMethod(nonPublic: false);
            if (getter == null)
            {
                sb.AppendLine("- Property has no public getter.");
                sb.AppendLine();
                return;
            }

            object? target;
            try
            {
                target = getter.Invoke(owner, null);
            }
            catch (TargetInvocationException tie)
            {
                sb.AppendLine($"- Getter threw: {tie.InnerException?.GetType().Name ?? tie.GetType().Name}");
                sb.AppendLine();
                return;
            }
            catch (System.Exception ex)
            {
                sb.AppendLine($"- Getter failed: {ex.GetType().Name}");
                sb.AppendLine();
                return;
            }

            if (target == null)
            {
                sb.AppendLine("- Value is <null>.");
                sb.AppendLine();
                return;
            }

            Type targetType = target.GetType();
            sb.AppendLine($"- Runtime Type: {targetType.FullName}");
            sb.AppendLine($"- Assembly: {targetType.Assembly.FullName}");

            sb.AppendLine("- Public Properties (metadata):");
            foreach (PropertyInfo childPi in targetType.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                MethodInfo? getter2 = childPi.GetGetMethod(nonPublic: false);
                MethodInfo? setter2 = childPi.GetSetMethod(nonPublic: false);
                sb.AppendLine($"  - {childPi.Name} : {GetFriendlyTypeName(childPi.PropertyType)} | Read={(getter2 != null)} | Write={(setter2 != null)} | Indexer={(childPi.GetIndexParameters().Length > 0)}");
            }

            sb.AppendLine("- Selected Value Probe:");
            foreach (string line in GetSafeChildValueLines(target, targetType))
                sb.AppendLine($"  {line}");

            AppendEnumerableProbe(sb, target);
            sb.AppendLine();
        }

        private static IReadOnlyList<string> GetSafeChildValueLines(object target, Type targetType)
        {
            string[] preferredNames =
            {
                "Name",
                "DisplayName",
                "Description",
                "Value",
                "DataValue",
                "CurrentValue",
                "DoubleValue",
                "StringValue",
                "Count",
                "Length",
                "Size",
                "Key",
                "Tag",
                "Code",
                "Id"
            };

            var lines = new List<string>();
            foreach (string name in preferredNames)
            {
                PropertyInfo? pi = targetType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (pi == null || pi.GetIndexParameters().Length != 0)
                    continue;

                MethodInfo? getter = pi.GetGetMethod(nonPublic: false);
                if (getter == null)
                    continue;

                if (!IsSafePropertyType(pi.PropertyType))
                {
                    lines.Add($"- {pi.Name} : {GetFriendlyTypeName(pi.PropertyType)} = <not probed>");
                    continue;
                }

                try
                {
                    object? value = getter.Invoke(target, null);
                    lines.Add($"- {pi.Name} : {GetFriendlyTypeName(pi.PropertyType)} = {FormatValue(value)}");
                }
                catch (TargetInvocationException tie)
                {
                    lines.Add($"- {pi.Name} : {GetFriendlyTypeName(pi.PropertyType)} = <getter threw: {tie.InnerException?.GetType().Name ?? tie.GetType().Name}>");
                }
                catch (System.Exception ex)
                {
                    lines.Add($"- {pi.Name} : {GetFriendlyTypeName(pi.PropertyType)} = <getter failed: {ex.GetType().Name}>");
                }
            }

            if (lines.Count == 0)
                lines.Add("- No simple safe child values matched the preferred probe list.");

            return lines;
        }

        private static void AppendEnumerableProbe(StringBuilder sb, object target)
        {
            if (target is not IEnumerable enumerable || target is string)
            {
                sb.AppendLine("- Enumerable Probe: target is not IEnumerable.");
                return;
            }

            sb.AppendLine("- Enumerable Probe:");
            int index = 0;
            try
            {
                foreach (object? item in enumerable)
                {
                    if (index >= 25)
                    {
                        sb.AppendLine("  - Enumeration truncated after 25 items.");
                        break;
                    }

                    if (item == null)
                    {
                        sb.AppendLine($"  - [{index}] <null>");
                        index++;
                        continue;
                    }

                    Type itemType = item.GetType();
                    sb.AppendLine($"  - [{index}] Type = {itemType.FullName}");
                    foreach (string line in GetInterestingItemLines(item, itemType))
                        sb.AppendLine($"    {line}");

                    index++;
                }

                if (index == 0)
                    sb.AppendLine("  - Enumerable contained no items.");
            }
            catch (TargetInvocationException tie)
            {
                sb.AppendLine($"  - Enumeration threw: {tie.InnerException?.GetType().Name ?? tie.GetType().Name}");
            }
            catch (System.Exception ex)
            {
                sb.AppendLine($"  - Enumeration failed: {ex.GetType().Name}");
            }
        }

        private static IReadOnlyList<string> GetInterestingItemLines(object item, Type itemType)
        {
            string[] preferredNames =
            {
                "Name",
                "DisplayName",
                "Description",
                "Value",
                "DataValue",
                "CurrentValue",
                "DoubleValue",
                "StringValue",
                "IntValue",
                "LongValue",
                "Tag",
                "Code",
                "Id",
                "Context",
                "Field",
                "FieldName",
                "Record",
                "Key"
            };

            var lines = new List<string>();
            foreach (string name in preferredNames)
            {
                PropertyInfo? pi = itemType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (pi == null || pi.GetIndexParameters().Length != 0)
                    continue;

                MethodInfo? getter = pi.GetGetMethod(nonPublic: false);
                if (getter == null)
                    continue;

                if (!IsSafePropertyType(pi.PropertyType))
                {
                    lines.Add($"- {pi.Name} : {GetFriendlyTypeName(pi.PropertyType)} = <not probed>");
                    continue;
                }

                try
                {
                    object? value = getter.Invoke(item, null);
                    lines.Add($"- {pi.Name} : {GetFriendlyTypeName(pi.PropertyType)} = {FormatValue(value)}");
                }
                catch (TargetInvocationException tie)
                {
                    lines.Add($"- {pi.Name} : {GetFriendlyTypeName(pi.PropertyType)} = <getter threw: {tie.InnerException?.GetType().Name ?? tie.GetType().Name}>");
                }
                catch (System.Exception ex)
                {
                    lines.Add($"- {pi.Name} : {GetFriendlyTypeName(pi.PropertyType)} = <getter failed: {ex.GetType().Name}>");
                }
            }

            if (lines.Count == 0)
                lines.Add("- No preferred item properties were exposed.");

            return lines;
        }

        private static IReadOnlyList<string> GetSafeValueLines(DBObject dbo, Type type)
        {
            string[] preferredNames =
            {
                "Name",
                "DisplayName",
                "Description",
                "StyleName",
                "Layer",
                "LayerId",
                "PartFamilyName",
                "PartFamilyId",
                "PartSizeName",
                "PartSizeId",
                "Rotation",
                "Position",
                "Location",
                "CenterPoint"
            };

            var lines = new List<string>();

            foreach (string name in preferredNames)
            {
                PropertyInfo? pi = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
                if (pi == null)
                    continue;

                if (pi.GetIndexParameters().Length != 0)
                    continue;

                MethodInfo? getter = pi.GetGetMethod(nonPublic: false);
                if (getter == null)
                    continue;

                if (!IsSafePropertyType(pi.PropertyType))
                {
                    lines.Add($"- {pi.Name} : {GetFriendlyTypeName(pi.PropertyType)} = <not probed>");
                    continue;
                }

                try
                {
                    object? value = getter.Invoke(dbo, null);
                    lines.Add($"- {pi.Name} : {GetFriendlyTypeName(pi.PropertyType)} = {FormatValue(value)}");
                }
                catch (TargetInvocationException tie)
                {
                    lines.Add($"- {pi.Name} : {GetFriendlyTypeName(pi.PropertyType)} = <getter threw: {tie.InnerException?.GetType().Name ?? tie.GetType().Name}>");
                }
                catch (System.Exception ex)
                {
                    lines.Add($"- {pi.Name} : {GetFriendlyTypeName(pi.PropertyType)} = <getter failed: {ex.GetType().Name}>");
                }
            }

            return lines;
        }

        private static bool IsSafePropertyType(Type type)
        {
            Type t = Nullable.GetUnderlyingType(type) ?? type;

            if (t.IsEnum)
                return true;

            if (t == typeof(string) ||
                t == typeof(bool) ||
                t == typeof(byte) ||
                t == typeof(short) ||
                t == typeof(int) ||
                t == typeof(long) ||
                t == typeof(float) ||
                t == typeof(double) ||
                t == typeof(decimal) ||
                t == typeof(Guid))
            {
                return true;
            }

            string fullName = t.FullName ?? string.Empty;
            return fullName == "Autodesk.AutoCAD.Geometry.Point2d" ||
                   fullName == "Autodesk.AutoCAD.Geometry.Point3d" ||
                   fullName == "Autodesk.AutoCAD.Geometry.Vector2d" ||
                   fullName == "Autodesk.AutoCAD.Geometry.Vector3d";
        }

        private static bool IsInterestingPropertyName(PropertyInfo pi)
        {
            string name = pi.Name.ToUpperInvariant();
            return name.Contains("PART") ||
                   name.Contains("SIZE") ||
                   name.Contains("WIDTH") ||
                   name.Contains("LENGTH") ||
                   name.Contains("INNER") ||
                   name.Contains("OUTER") ||
                   name.Contains("PARAM") ||
                   name.Contains("FIELD") ||
                   name.Contains("RECORD") ||
                   name.Contains("JUNCTION");
        }

        private static string FormatValue(object? value)
        {
            if (value == null)
                return "<null>";

            return value switch
            {
                string s => s,
                IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture) ?? value.ToString() ?? string.Empty,
                _ => value.ToString() ?? string.Empty,
            };
        }

        private static string GetFriendlyTypeName(Type type)
        {
            if (!type.IsGenericType)
                return type.FullName ?? type.Name;

            Type generic = type.GetGenericTypeDefinition();
            string genericName = generic.FullName ?? generic.Name;
            int tick = genericName.IndexOf('`');
            if (tick >= 0)
                genericName = genericName.Substring(0, tick);

            string args = string.Join(", ", type.GetGenericArguments().Select(GetFriendlyTypeName));
            return $"{genericName}<{args}>";
        }

        private static string TryGetHandleString(DBObject dbo)
        {
            try
            {
                if (dbo.ObjectId.IsNull)
                    return "<null>";
                return dbo.ObjectId.Handle.ToString();
            }
            catch
            {
                return "<unavailable>";
            }
        }

        private static string TryGetRxClassName(DBObject dbo)
        {
            try
            {
                return dbo.GetRXClass()?.Name ?? "<null>";
            }
            catch
            {
                return "<unavailable>";
            }
        }

        private static string TryGetDxfName(DBObject dbo)
        {
            try
            {
                return dbo.GetRXClass()?.DxfName ?? "<null>";
            }
            catch
            {
                return "<unavailable>";
            }
        }

        private static string BuildReportPath(Database db, ObjectId id)
        {
            string baseFolder = string.IsNullOrWhiteSpace(db.Filename)
                ? Path.GetTempPath()
                : Path.GetDirectoryName(db.Filename) ?? Path.GetTempPath();

            string dwgName = string.IsNullOrWhiteSpace(db.Filename)
                ? "UNSAVED_DWG"
                : Path.GetFileNameWithoutExtension(db.Filename);

            string fileName = $"{dwgName}_SD_JUNCTION_SAFE_DUMP_{id.Handle}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            return Path.Combine(baseFolder, fileName);
        }

        private static bool IsCivilStructure(DBObject obj)
        {
            string name = obj.GetType().FullName ?? obj.GetType().Name;
            name = name.ToUpperInvariant();
            return name.Contains("STRUCTURE") &&
                   (name.Contains("AECC") || name.Contains("CIVIL"));
        }
    }
}
