using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Ufls
{
    public static class UflsSdJunctionDiagnostics
    {
        private static readonly string[] FocusTokens =
        {
            "width", "length", "inner", "outer", "size", "part", "data",
            "record", "field", "param", "shape", "junction", "structure"
        };

        [CommandMethod("UFLS", "SD-JUNCTION-DUMP", CommandFlags.Modal)]
        [CommandMethod("UFLS", "UFLS-SD-JUNCTION-DUMP", CommandFlags.Modal)]
        public static void DumpSdJunctionStructureInfo()
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                PromptEntityOptions peo = new PromptEntityOptions("\nSelect SD-JUNCTION structure to inspect: ");
                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                    return;

                using Transaction tr = db.TransactionManager.StartTransaction();
                DBObject dbo = tr.GetObject(per.ObjectId, OpenMode.ForRead, false);

                string report = BuildReport(dbo, per.ObjectId, db);
                string reportPath = WriteReportToDisk(report, db);

                foreach (string line in BuildCommandLineSummary(report, reportPath))
                    ed.WriteMessage("\n" + line);

                tr.Commit();
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nSD-JUNCTION-DUMP error: {ex.Message}");
            }
        }

        private static string BuildReport(DBObject dbo, ObjectId objectId, Database db)
        {
            StringBuilder sb = new StringBuilder(32768);
            Type rootType = dbo.GetType();

            sb.AppendLine("SD-JUNCTION DIAGNOSTIC REPORT");
            sb.AppendLine(new string('=', 80));
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Drawing: {GetDrawingPath(db)}");
            sb.AppendLine($"ObjectId: {objectId}");
            sb.AppendLine($"Handle: {objectId.Handle}");
            sb.AppendLine($"Runtime Type: {rootType.FullName}");
            sb.AppendLine($"RX Class: {dbo.GetRXClass()?.Name ?? "<null>"}");
            sb.AppendLine();

            sb.AppendLine("TOP-LEVEL PUBLIC INSTANCE PROPERTIES");
            sb.AppendLine(new string('-', 80));
            foreach (PropertyInfo pi in GetReadableProperties(rootType).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                bool isFocus = IsFocusName(pi.Name) || IsFocusType(pi.PropertyType);
                string prefix = isFocus ? "*" : " ";
                object? value = SafeGetValue(pi, dbo);
                sb.AppendLine($"{prefix} {DescribeProperty(pi, value)}");
            }

            sb.AppendLine();
            sb.AppendLine("FOCUSED OBJECT GRAPH");
            sb.AppendLine(new string('-', 80));

            HashSet<object> visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            visited.Add(dbo);
            DumpFocusedObject(sb, dbo, "$root", depth: 0, maxDepth: 4, visited);

            sb.AppendLine();
            sb.AppendLine("FOCUSED WRITABLE TARGET CANDIDATES");
            sb.AppendLine(new string('-', 80));

            List<string> candidates = new List<string>();
            visited.Clear();
            visited.Add(dbo);
            CollectWritableCandidates(dbo, "$root", depth: 0, maxDepth: 4, visited, candidates);

            if (candidates.Count == 0)
            {
                sb.AppendLine("<none found>");
            }
            else
            {
                foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                    sb.AppendLine(candidate);
            }

            return sb.ToString();
        }

        private static void DumpFocusedObject(
            StringBuilder sb,
            object target,
            string path,
            int depth,
            int maxDepth,
            HashSet<object> visited)
        {
            if (depth > maxDepth)
                return;

            Type type = target.GetType();
            PropertyInfo[] props = GetReadableProperties(type);

            foreach (PropertyInfo pi in props.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                object? value = SafeGetValue(pi, target);
                string childPath = path + "." + pi.Name;
                bool isFocus = IsFocusName(pi.Name) || IsFocusType(pi.PropertyType) || IsInterestingValue(value);
                if (!isFocus)
                    continue;

                sb.AppendLine($"{new string(' ', depth * 2)}{DescribePathValue(pi, childPath, value)}");

                if (value == null)
                    continue;

                if (TryDumpEnumerable(sb, value, childPath, depth, maxDepth, visited))
                    continue;

                if (!ShouldRecurseInto(value.GetType()))
                    continue;

                if (!visited.Add(value))
                    continue;

                DumpFocusedObject(sb, value, childPath, depth + 1, maxDepth, visited);
            }
        }

        private static void CollectWritableCandidates(
            object target,
            string path,
            int depth,
            int maxDepth,
            HashSet<object> visited,
            List<string> candidates)
        {
            if (depth > maxDepth)
                return;

            Type type = target.GetType();
            foreach (PropertyInfo pi in GetReadableProperties(type))
            {
                string childPath = path + "." + pi.Name;
                object? value = SafeGetValue(pi, target);

                if (pi.CanWrite && IsFocusName(pi.Name))
                {
                    candidates.Add($"PROPERTY  {childPath}  :: {pi.PropertyType.FullName}");
                }

                if (value == null)
                    continue;

                if (value is IEnumerable enumerable && value is not string)
                {
                    int index = 0;
                    foreach (object? item in enumerable)
                    {
                        if (item == null)
                        {
                            index++;
                            if (index >= 10)
                                break;
                            continue;
                        }

                        string itemPath = $"{childPath}[{index}]";
                        Type itemType = item.GetType();
                        foreach (PropertyInfo childPi in GetReadableProperties(itemType))
                        {
                            if (childPi.CanWrite && IsFocusName(childPi.Name))
                                candidates.Add($"ITEM PROP {itemPath}.{childPi.Name}  :: {childPi.PropertyType.FullName}");
                        }

                        if (ShouldRecurseInto(itemType) && visited.Add(item))
                            CollectWritableCandidates(item, itemPath, depth + 1, maxDepth, visited, candidates);

                        index++;
                        if (index >= 10)
                            break;
                    }

                    continue;
                }

                Type valueType = value.GetType();
                if (!ShouldRecurseInto(valueType))
                    continue;

                if (!visited.Add(value))
                    continue;

                CollectWritableCandidates(value, childPath, depth + 1, maxDepth, visited, candidates);
            }
        }

        private static bool TryDumpEnumerable(
            StringBuilder sb,
            object value,
            string path,
            int depth,
            int maxDepth,
            HashSet<object> visited)
        {
            if (value is string || value is not IEnumerable enumerable)
                return false;

            int index = 0;
            foreach (object? item in enumerable)
            {
                string itemPath = $"{path}[{index}]";
                sb.AppendLine($"{new string(' ', (depth + 1) * 2)}{itemPath} = {FormatValue(item)}");

                if (item != null && depth + 1 < maxDepth && ShouldRecurseInto(item.GetType()) && visited.Add(item))
                    DumpFocusedObject(sb, item, itemPath, depth + 2, maxDepth, visited);

                index++;
                if (index >= 10)
                {
                    sb.AppendLine($"{new string(' ', (depth + 1) * 2)}{path}[...] <truncated after 10 items>");
                    break;
                }
            }

            if (index == 0)
                sb.AppendLine($"{new string(' ', (depth + 1) * 2)}{path} = <empty enumerable>");

            return true;
        }

        private static IEnumerable<string> BuildCommandLineSummary(string report, string reportPath)
        {
            List<string> lines = report
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                .Where(line =>
                    line.StartsWith("* ", StringComparison.Ordinal) ||
                    line.StartsWith("PROPERTY  $root", StringComparison.Ordinal) ||
                    line.StartsWith("ITEM PROP $root", StringComparison.Ordinal))
                .Take(40)
                .ToList();

            yield return "SD-JUNCTION-DUMP: report written.";
            yield return $"REPORT: {reportPath}";

            if (lines.Count == 0)
            {
                yield return "No focused width/length/part-data candidates were found in the summary.";
                yield break;
            }

            yield return "Top candidates:";
            foreach (string line in lines)
                yield return line;
        }

        private static string WriteReportToDisk(string report, Database db)
        {
            string baseFolder = GetWritableFolder(db);
            Directory.CreateDirectory(baseFolder);

            string fileName = $"SD_JUNCTION_DIAGNOSTIC_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            string fullPath = Path.Combine(baseFolder, fileName);
            File.WriteAllText(fullPath, report, Encoding.UTF8);
            return fullPath;
        }

        private static string GetWritableFolder(Database db)
        {
            string drawingPath = db.Filename ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(drawingPath))
            {
                string? dir = Path.GetDirectoryName(drawingPath);
                if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                    return dir;
            }

            return Path.Combine(Path.GetTempPath(), "CLV_CivilTools");
        }

        private static string GetDrawingPath(Database db)
        {
            return string.IsNullOrWhiteSpace(db.Filename) ? "<unsaved drawing>" : db.Filename;
        }

        private static PropertyInfo[] GetReadableProperties(Type type)
        {
            return type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(pi => pi.GetIndexParameters().Length == 0)
                .ToArray();
        }

        private static object? SafeGetValue(PropertyInfo pi, object target)
        {
            try
            {
                return pi.GetValue(target, null);
            }
            catch (TargetInvocationException tie)
            {
                return $"<TargetInvocationException: {tie.InnerException?.Message ?? tie.Message}>";
            }
            catch (System.Exception ex)
            {
                return $"<Exception: {ex.Message}>";
            }
        }

        private static string DescribeProperty(PropertyInfo pi, object? value)
        {
            string access = pi.CanWrite ? "read/write" : "read-only";
            return $"{pi.Name} : {pi.PropertyType.FullName} [{access}] = {FormatValue(value)}";
        }

        private static string DescribePathValue(PropertyInfo pi, string path, object? value)
        {
            string access = pi.CanWrite ? "read/write" : "read-only";
            return $"{path} : {pi.PropertyType.FullName} [{access}] = {FormatValue(value)}";
        }

        private static bool IsFocusName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            foreach (string token in FocusTokens)
            {
                if (name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static bool IsFocusType(Type type)
        {
            string fullName = type.FullName ?? type.Name;
            return IsFocusName(fullName);
        }

        private static bool IsInterestingValue(object? value)
        {
            if (value == null)
                return false;

            Type type = value.GetType();
            if (ShouldTreatAsLeaf(type))
                return false;

            string fullName = type.FullName ?? type.Name;
            return IsFocusName(fullName);
        }

        private static bool ShouldRecurseInto(Type type)
        {
            return !ShouldTreatAsLeaf(type);
        }

        private static bool ShouldTreatAsLeaf(Type type)
        {
            Type effective = Nullable.GetUnderlyingType(type) ?? type;

            if (effective.IsPrimitive || effective.IsEnum)
                return true;

            if (effective == typeof(string) ||
                effective == typeof(decimal) ||
                effective == typeof(DateTime) ||
                effective == typeof(TimeSpan) ||
                effective == typeof(Guid))
            {
                return true;
            }

            string ns = effective.Namespace ?? string.Empty;
            if (ns.StartsWith("System", StringComparison.Ordinal) &&
                !typeof(IEnumerable).IsAssignableFrom(effective))
            {
                return true;
            }

            return false;
        }

        private static string FormatValue(object? value)
        {
            if (value == null)
                return "<null>";

            if (value is string s)
                return s;

            if (value is double d)
                return d.ToString("0.###", CultureInfo.InvariantCulture);

            if (value is float f)
                return f.ToString("0.###", CultureInfo.InvariantCulture);

            if (value is IFormattable formattable)
                return formattable.ToString(null, CultureInfo.InvariantCulture) ?? value.ToString() ?? string.Empty;

            Type type = value.GetType();
            if (value is IEnumerable && value is not string)
                return $"<{type.FullName}>";

            return value.ToString() ?? $"<{type.FullName}>";
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            public new bool Equals(object? x, object? y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
