using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;

using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Shared
{
    /// <summary>
    /// Bulk Civil 3D style-name migration helper.
    /// Initial migration target: rename legacy R26_* styles to R27_*.
    /// </summary>
    public static class CivilStyleMigrationCommands
    {
        private const string DefaultOldPrefix = "R26_";
        private const string DefaultNewPrefix = "R27_";
        private const int PreviewLineLimit = 20;
        private const int MaxTraversalDepth = 12;

        [CommandMethod("CLV-CIVIL-STYLE-RENAME", CommandFlags.Modal)]
        [CommandMethod("CLV-STYLE-MIGRATE-R27", CommandFlags.Modal)]
        [CommandMethod("R26TOR27", CommandFlags.Modal)]
        public static void RenameCivilStylesByPrefix()
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                string oldPrefix = PromptForPrefix(ed, "\nExisting style-name prefix to replace", DefaultOldPrefix);
                if (string.IsNullOrWhiteSpace(oldPrefix))
                    return;

                string newPrefix = PromptForPrefix(ed, "\nNew style-name prefix", DefaultNewPrefix);
                if (string.IsNullOrWhiteSpace(newPrefix))
                    return;

                List<StyleRenameCandidate> preview;
                using (doc.LockDocument())
                {
                    preview = CollectRenameCandidates(db, oldPrefix, newPrefix);
                }

                if (preview.Count == 0)
                {
                    ed.WriteMessage($"\nCivil style migration: no Civil 3D style names starting with '{oldPrefix}' were found.");
                    return;
                }

                WritePreview(ed, oldPrefix, newPrefix, preview);

                PromptKeywordOptions confirmOptions = new PromptKeywordOptions(
                    $"\nRename {preview.Count} Civil 3D style name(s) from '{oldPrefix}' to '{newPrefix}'?")
                {
                    AllowNone = false
                };
                confirmOptions.Keywords.Add("Yes");
                confirmOptions.Keywords.Add("No");
                confirmOptions.Keywords.Default = "No";

                PromptResult confirm = ed.GetKeywords(confirmOptions);
                if (confirm.Status != PromptStatus.OK ||
                    !string.Equals(confirm.StringResult, "Yes", StringComparison.OrdinalIgnoreCase))
                {
                    ed.WriteMessage("\nCivil style migration: rename cancelled after preview.");
                    return;
                }

                StyleRenameSummary summary;
                using (doc.LockDocument())
                {
                    summary = ApplyRenameCandidates(db, oldPrefix, newPrefix);
                }

                WriteSummary(ed, summary);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCivil style migration error: {ex.Message}");
            }
        }

        private static string PromptForPrefix(Editor ed, string message, string defaultValue)
        {
            PromptStringOptions options = new PromptStringOptions(message)
            {
                AllowSpaces = false,
                DefaultValue = defaultValue,
                UseDefaultValue = true
            };

            PromptResult result = ed.GetString(options);
            if (result.Status != PromptStatus.OK)
                return string.Empty;

            return string.IsNullOrWhiteSpace(result.StringResult)
                ? defaultValue
                : result.StringResult.Trim();
        }

        private static List<StyleRenameCandidate> CollectRenameCandidates(Database db, string oldPrefix, string newPrefix)
        {
            using Transaction tr = db.TransactionManager.StartTransaction();
            List<StyleRenameCandidate> candidates = CollectRenameCandidatesCore(tr, oldPrefix, newPrefix);
            tr.Commit();
            return candidates;
        }

        private static StyleRenameSummary ApplyRenameCandidates(Database db, string oldPrefix, string newPrefix)
        {
            using Transaction tr = db.TransactionManager.StartTransaction();
            List<StyleRenameCandidate> candidates = CollectRenameCandidatesCore(tr, oldPrefix, newPrefix);

            int renamed = 0;
            int skipped = 0;
            int failed = 0;
            List<string> notes = new List<string>();

            foreach (StyleRenameCandidate candidate in candidates)
            {
                try
                {
                    if (candidate.TargetNameExists)
                    {
                        skipped++;
                        notes.Add($"SKIP existing target name: {candidate.CurrentName} -> {candidate.TargetName}");
                        continue;
                    }

                    DBObject dbo = tr.GetObject(candidate.ObjectId, OpenMode.ForWrite, false);
                    if (!TrySetStyleName(dbo, candidate.TargetName, out string? renameMessage))
                    {
                        skipped++;
                        if (!string.IsNullOrWhiteSpace(renameMessage))
                            notes.Add($"SKIP {candidate.CurrentName}: {renameMessage}");
                        continue;
                    }

                    renamed++;
                }
                catch (System.Exception ex)
                {
                    failed++;
                    notes.Add($"FAIL {candidate.CurrentName} -> {candidate.TargetName}: {ex.Message}");
                }
            }

            tr.Commit();
            return new StyleRenameSummary(candidates.Count, renamed, skipped, failed, notes);
        }

        private static List<StyleRenameCandidate> CollectRenameCandidatesCore(Transaction tr, string oldPrefix, string newPrefix)
        {
            CivilDocument civDoc = CivilApplication.ActiveDocument;
            object stylesRoot = civDoc.Styles;

            HashSet<ObjectId> discoveredIds = new HashSet<ObjectId>();
            HashSet<string> visitedNodes = new HashSet<string>(StringComparer.Ordinal);
            TraverseStyleContainer(stylesRoot, discoveredIds, visitedNodes, 0);

            List<StyleRenameCandidate> candidates = new List<StyleRenameCandidate>();
            foreach (ObjectId id in discoveredIds)
            {
                if (id.IsNull || !id.IsValid)
                    continue;

                DBObject dbo;
                try
                {
                    dbo = tr.GetObject(id, OpenMode.ForRead, false);
                }
                catch
                {
                    continue;
                }

                if (!IsCivilStyleObject(dbo))
                    continue;

                if (!TryGetStyleName(dbo, out string? currentName) || string.IsNullOrWhiteSpace(currentName))
                    continue;

                if (!currentName.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string targetName = newPrefix + currentName.Substring(oldPrefix.Length);
                bool targetNameExists = DoesSiblingStyleNameExist(tr, dbo, targetName);
                string collectionLabel = GetOwningCollectionLabel(tr, dbo);

                candidates.Add(new StyleRenameCandidate(id, currentName, targetName, collectionLabel, targetNameExists));
            }

            return candidates
                .OrderBy(x => x.CollectionLabel, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.CurrentName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void TraverseStyleContainer(object? node, HashSet<ObjectId> discoveredIds, HashSet<string> visitedNodes, int depth)
        {
            if (node == null || depth > MaxTraversalDepth)
                return;

            Type nodeType = node.GetType();
            if (IsSimpleType(nodeType))
                return;

            string visitedKey = $"{nodeType.FullName}:{node.GetHashCode()}";
            if (!visitedNodes.Add(visitedKey))
                return;

            if (node is ObjectId objectId)
            {
                if (!objectId.IsNull && objectId.IsValid)
                    discoveredIds.Add(objectId);
                return;
            }

            if (node is ObjectIdCollection objectIdCollection)
            {
                foreach (ObjectId id in objectIdCollection)
                {
                    if (!id.IsNull && id.IsValid)
                        discoveredIds.Add(id);
                }
                return;
            }

            if (node is IEnumerable enumerable && node is not string)
            {
                foreach (object? item in enumerable)
                    TraverseStyleContainer(item, discoveredIds, visitedNodes, depth + 1);
            }

            foreach (PropertyInfo property in nodeType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.GetIndexParameters().Length != 0 || !property.CanRead)
                    continue;

                object? value;
                try
                {
                    value = property.GetValue(node, null);
                }
                catch
                {
                    continue;
                }

                if (value == null || IsSimpleType(value.GetType()))
                    continue;

                TraverseStyleContainer(value, discoveredIds, visitedNodes, depth + 1);
            }
        }

        private static bool IsSimpleType(Type type)
        {
            if (type.IsPrimitive || type.IsEnum)
                return true;

            return type == typeof(string)
                || type == typeof(decimal)
                || type == typeof(DateTime)
                || type == typeof(TimeSpan)
                || type == typeof(Guid);
        }

        private static bool IsCivilStyleObject(DBObject dbo)
        {
            Type type = dbo.GetType();
            string fullName = type.FullName ?? string.Empty;
            return fullName.Contains("Autodesk.Civil", StringComparison.Ordinal)
                   && fullName.Contains("Style", StringComparison.Ordinal);
        }

        private static bool TryGetStyleName(DBObject dbo, out string? styleName)
        {
            styleName = null;
            PropertyInfo? property = dbo.GetType().GetProperty("Name", BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.CanRead || property.PropertyType != typeof(string))
                return false;

            try
            {
                styleName = property.GetValue(dbo) as string;
                return !string.IsNullOrWhiteSpace(styleName);
            }
            catch
            {
                return false;
            }
        }

        private static bool TrySetStyleName(DBObject dbo, string styleName, out string? message)
        {
            message = null;
            PropertyInfo? property = dbo.GetType().GetProperty("Name", BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.CanWrite || property.PropertyType != typeof(string))
            {
                message = "style object does not expose a writable Name property";
                return false;
            }

            try
            {
                property.SetValue(dbo, styleName);
                return true;
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                message = tie.InnerException.Message;
                return false;
            }
            catch (System.Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        private static bool DoesSiblingStyleNameExist(Transaction tr, DBObject dbo, string targetName)
        {
            ObjectId ownerId = dbo.OwnerId;
            if (ownerId.IsNull || !ownerId.IsValid)
                return false;

            try
            {
                DBObject owner = tr.GetObject(ownerId, OpenMode.ForRead, false);
                if (owner is DBDictionary dictionary)
                {
                    foreach (DBDictionaryEntry entry in dictionary)
                    {
                        if (string.Equals(entry.Key, targetName, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
            catch
            {
                // Let the direct rename attempt decide if owner introspection is unavailable.
            }

            return false;
        }

        private static string GetOwningCollectionLabel(Transaction tr, DBObject dbo)
        {
            ObjectId ownerId = dbo.OwnerId;
            if (ownerId.IsNull || !ownerId.IsValid)
                return dbo.GetType().Name;

            try
            {
                DBObject owner = tr.GetObject(ownerId, OpenMode.ForRead, false);
                return owner.GetType().Name;
            }
            catch
            {
                return dbo.GetType().Name;
            }
        }

        private static void WritePreview(Editor ed, string oldPrefix, string newPrefix, IReadOnlyList<StyleRenameCandidate> preview)
        {
            ed.WriteMessage($"\nCivil style migration preview: found {preview.Count} candidate style(s) for '{oldPrefix}' -> '{newPrefix}'.");

            int lineCount = Math.Min(preview.Count, PreviewLineLimit);
            for (int i = 0; i < lineCount; i++)
            {
                StyleRenameCandidate item = preview[i];
                string collision = item.TargetNameExists ? " [target exists]" : string.Empty;
                ed.WriteMessage($"\n  {item.CurrentName} -> {item.TargetName} ({item.CollectionLabel}){collision}");
            }

            if (preview.Count > lineCount)
                ed.WriteMessage($"\n  ... plus {preview.Count - lineCount} more style(s).");
        }

        private static void WriteSummary(Editor ed, StyleRenameSummary summary)
        {
            ed.WriteMessage($"\nCivil style migration complete: scanned {summary.TotalCandidates} candidate style(s), renamed {summary.Renamed}, skipped {summary.Skipped}, failed {summary.Failed}.");

            if (summary.Notes.Count == 0)
                return;

            ed.WriteMessage("\nCivil style migration detail:");
            foreach (string note in summary.Notes.Take(PreviewLineLimit))
                ed.WriteMessage($"\n  {note}");

            if (summary.Notes.Count > PreviewLineLimit)
                ed.WriteMessage($"\n  ... plus {summary.Notes.Count - PreviewLineLimit} more detail line(s).");
        }

        private sealed record StyleRenameCandidate(
            ObjectId ObjectId,
            string CurrentName,
            string TargetName,
            string CollectionLabel,
            bool TargetNameExists);

        private sealed record StyleRenameSummary(
            int TotalCandidates,
            int Renamed,
            int Skipped,
            int Failed,
            List<string> Notes);
    }
}
