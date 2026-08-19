using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using CLV_CivilTools.Shared;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
namespace CLV_CivilTools.Survey
{
    /// <summary>
    /// Survey legend builder using prebuilt legend-row DWG blocks.
    /// Blocks are loaded from the shared survey legend block folder and stacked using the spacing rules
    /// in Reference/SurveyLegend.csv.
    /// </summary>
    public static class SurveyLegendCommands
    {
        private const string LegendLayerName = LayerStandards.SurveyLegendLayerName;
        private const string HeaderBlockName = "SURV_LEG_HEADER";
        private const string LegendRegAppName = "CLV_SURVEY_LEGEND";
        private const string LegendGroupPrefix = "CLVSL_";
        private const string LegendBlockFolder = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\Blocks\Survey\Legend";

        private const double HeaderToFirstItemSpacing = 0.2671;
        private const double SingleToSingleSpacing = 0.3054;
        private const double SingleToDoubleSpacing = 0.39;
        private const double DoubleToSingleSpacing = 0.39;
        private const double DoubleToDoubleSpacing = 0.4746;

        private static readonly string[] CategoryOrder =
        {
            "Linework",
            "Symbols",
            "Abbreviations",
            "Construction Notes"
        };

        [CommandMethod("SURVEY-CREATE-LEGEND")]
        [CommandMethod("CREATELEGEND")]
        [CommandMethod("CREATE-LEGEND")]
        public static void CreateLegend()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            var ed = doc.Editor;
            var items = SurveyLegendCatalog.Load(ed);
            if (items.Count == 0)
            {
                ed.WriteMessage("\nSurvey legend: no legend items were found in SurveyLegend.csv.");
                return;
            }

            using var dialog = new SurveyLegendForm(items, Array.Empty<string>(), "CREATE SURVEY LEGEND");
            if (AcadApp.ShowModalDialog(dialog) != DialogResult.OK)
                return;

            var selectedItems = dialog.SelectedItems;
            if (selectedItems.Count == 0)
            {
                ed.WriteMessage("\nSurvey legend: no legend items were selected.");
                return;
            }

            var ppr = ed.GetPoint("\nPick SURV_LEG_HEADER insertion point: ");
            if (ppr.Status != PromptStatus.OK)
                return;

            try
            {
                BuildLegend(doc, ppr.Value, selectedItems);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nSurvey legend creation failed: {ex.Message}");
            }
        }

        [CommandMethod("SURVEY-UPDATE-LEGEND")]
        [CommandMethod("UPDATELEGEND")]
        [CommandMethod("UPDATE-LEGEND")]
        public static void UpdateLegend()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            var ed = doc.Editor;
            var items = SurveyLegendCatalog.Load(ed);
            if (items.Count == 0)
            {
                ed.WriteMessage("\nSurvey legend: no legend items were found in SurveyLegend.csv.");
                return;
            }

            var peo = new PromptEntityOptions("\nSelect survey legend header to update: ");
            peo.SetRejectMessage("\nSelect the SURV_LEG_HEADER block from a generated survey legend.");
            peo.AddAllowedClass(typeof(BlockReference), exactMatch: false);
            var per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
                return;

            SurveyLegendMetadata? metadata;
            Point3d headerPoint;

            using (doc.LockDocument())
            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                if (tr.GetObject(per.ObjectId, OpenMode.ForRead, false) is not BlockReference headerRef)
                {
                    ed.WriteMessage("\nSurvey legend: selected object is not a block reference.");
                    return;
                }

                metadata = ReadLegendMetadata(headerRef);
                if (metadata == null)
                {
                    ed.WriteMessage("\nSurvey legend: selected header does not contain generated legend metadata.");
                    return;
                }

                headerPoint = headerRef.Position;
                tr.Commit();
            }

            using var dialog = new SurveyLegendForm(items, metadata.SelectedBlockNames, "UPDATE SURVEY LEGEND");
            if (AcadApp.ShowModalDialog(dialog) != DialogResult.OK)
                return;

            var selectedItems = dialog.SelectedItems;
            if (selectedItems.Count == 0)
            {
                ed.WriteMessage("\nSurvey legend: update cancelled because no legend items were selected.");
                return;
            }

            try
            {
                EraseExistingLegendGroup(doc, metadata.GroupName);
                BuildLegend(doc, headerPoint, selectedItems);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nSurvey legend update failed: {ex.Message}");
            }
        }

        private static void BuildLegend(Autodesk.AutoCAD.ApplicationServices.Document doc, Point3d headerInsertionPoint, IReadOnlyList<SurveyLegendItem> selectedItems)
        {
            var db = doc.Database;
            var ed = doc.Editor;
            var insertedIds = new List<ObjectId>();
            var groupName = LegendGroupPrefix + Guid.NewGuid().ToString("N").Substring(0, 16).ToUpperInvariant();

            var orderedItems = selectedItems
                .OrderBy(i => i.SortOrder)
                .ThenBy(i => i.MenuDescription, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            EnsureBlockDefinition(db, HeaderBlockName);
            foreach (string blockName in orderedItems.Select(i => i.BlockName).Distinct(StringComparer.OrdinalIgnoreCase))
                EnsureBlockDefinition(db, blockName);

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                LayerStandards.EnsureSurveyLegendLayer(db, tr, ed);
                EnsureRegApp(db, tr, LegendRegAppName);

                ObjectId headerBlockId = GetBlockDefinitionId(db, tr, HeaderBlockName);
                ObjectId headerId = InsertBlockReference(db, tr, headerBlockId, headerInsertionPoint, LegendLayerName);
                insertedIds.Add(headerId);

                Point3d currentPoint = new Point3d(headerInsertionPoint.X, headerInsertionPoint.Y - HeaderToFirstItemSpacing, headerInsertionPoint.Z);

                for (int i = 0; i < orderedItems.Length; i++)
                {
                    var item = orderedItems[i];
                    ObjectId blockId = GetBlockDefinitionId(db, tr, item.BlockName);
                    ObjectId entityId = InsertBlockReference(db, tr, blockId, currentPoint, LegendLayerName);
                    insertedIds.Add(entityId);

                    if (i < orderedItems.Length - 1)
                    {
                        double nextSpacing = GetItemToItemSpacing(item.SpacingType, orderedItems[i + 1].SpacingType);
                        currentPoint = new Point3d(currentPoint.X, currentPoint.Y - nextSpacing, currentPoint.Z);
                    }
                }

                WriteMetadataToEntities(tr, insertedIds, groupName, orderedItems.Select(i => i.BlockName));
                CreateGroup(db, tr, groupName, insertedIds);

                tr.Commit();
            }

            ed.WriteMessage($"\nSurvey legend created with {selectedItems.Count} item(s).");
        }

        private static void EraseExistingLegendGroup(Autodesk.AutoCAD.ApplicationServices.Document doc, string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                return;

            var db = doc.Database;
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var groupDict = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForRead);
                if (groupDict.Contains(groupName))
                {
                    var group = (Group)tr.GetObject(groupDict.GetAt(groupName), OpenMode.ForWrite);
                    var ids = group.GetAllEntityIds();
                    foreach (ObjectId id in ids)
                    {
                        if (id.IsErased || id.IsNull)
                            continue;

                        if (tr.GetObject(id, OpenMode.ForWrite, false) is Entity ent && !ent.IsErased)
                            ent.Erase();
                    }

                    group.Erase();
                }

                tr.Commit();
            }
        }

        private static double GetItemToItemSpacing(string currentSpacingType, string nextSpacingType)
        {
            bool currentDouble = IsDoubleSpacing(currentSpacingType);
            bool nextDouble = IsDoubleSpacing(nextSpacingType);

            if (!currentDouble && !nextDouble) return SingleToSingleSpacing;
            if (!currentDouble && nextDouble) return SingleToDoubleSpacing;
            if (currentDouble && !nextDouble) return DoubleToSingleSpacing;
            return DoubleToDoubleSpacing;
        }

        private static bool IsDoubleSpacing(string spacingType) =>
            string.Equals((spacingType ?? string.Empty).Trim(), "double", StringComparison.OrdinalIgnoreCase);

        private static ObjectId InsertBlockReference(Database db, Transaction tr, ObjectId blockTableRecordId, Point3d position, string layerName)
        {
            var currentSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var blockRef = new BlockReference(position, blockTableRecordId)
            {
                Layer = layerName
            };

            ObjectId id = currentSpace.AppendEntity(blockRef);
            tr.AddNewlyCreatedDBObject(blockRef, true);
            return id;
        }

        private static void EnsureBlockDefinition(Database db, string blockName)
        {
            bool hasBlock;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                hasBlock = bt.Has(blockName);
                tr.Commit();
            }

            if (hasBlock)
                return;

            string blockPath = Path.Combine(LegendBlockFolder, blockName + ".dwg");
            if (!File.Exists(blockPath))
                throw new FileNotFoundException($"Legend block not found: {blockPath}");

            using var sourceDb = new Database(false, true);
            sourceDb.ReadDwgFile(blockPath, FileShare.Read, true, string.Empty);
            sourceDb.CloseInput(true);
            db.Insert(blockName, sourceDb, true);
        }

        private static ObjectId GetBlockDefinitionId(Database db, Transaction tr, string blockName)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(blockName))
                throw new InvalidOperationException($"Legend block '{blockName}' is not loaded in the current drawing.");

            return bt[blockName];
        }

        private static void CreateGroup(Database db, Transaction tr, string groupName, IReadOnlyList<ObjectId> entityIds)
        {
            var groupDict = (DBDictionary)tr.GetObject(db.GroupDictionaryId, OpenMode.ForWrite);
            var group = new Group(groupName, selectable: true);
            groupDict.SetAt(groupName, group);
            tr.AddNewlyCreatedDBObject(group, true);

            foreach (ObjectId id in entityIds)
            {
                if (!id.IsNull && !id.IsErased)
                    group.Append(id);
            }
        }

        private static void WriteMetadataToEntities(Transaction tr, IReadOnlyList<ObjectId> entityIds, string groupName, IEnumerable<string> selectedBlockNames)
        {
            var metadataValues = new List<TypedValue>
            {
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, LegendRegAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, "v1"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, groupName)
            };

            foreach (string blockName in selectedBlockNames.Select(s => s.Trim()).Where(s => s.Length > 0))
                metadataValues.Add(new TypedValue((int)DxfCode.ExtendedDataAsciiString, blockName));

            foreach (ObjectId id in entityIds)
            {
                if (tr.GetObject(id, OpenMode.ForWrite, false) is not Entity ent)
                    continue;

                ent.XData = new ResultBuffer(metadataValues.ToArray());
            }
        }

        private static SurveyLegendMetadata? ReadLegendMetadata(Entity entity)
        {
            using ResultBuffer? buffer = entity.GetXDataForApplication(LegendRegAppName);
            if (buffer == null)
                return null;

            var values = buffer.AsArray();
            var strings = values
                .Where(v => v.TypeCode == (int)DxfCode.ExtendedDataAsciiString)
                .Select(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture) ?? string.Empty)
                .ToArray();

            if (strings.Length < 2 || !string.Equals(strings[0], "v1", StringComparison.OrdinalIgnoreCase))
                return null;

            string groupName = strings[1];
            string[] selected = strings
                .Skip(2)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .ToArray();

            if (string.IsNullOrWhiteSpace(groupName))
                return null;

            return new SurveyLegendMetadata(groupName, selected);
        }

        private static void EnsureRegApp(Database db, Transaction tr, string appName)
        {
            var regTable = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (regTable.Has(appName))
                return;

            regTable.UpgradeOpen();
            var record = new RegAppTableRecord { Name = appName };
            regTable.Add(record);
            tr.AddNewlyCreatedDBObject(record, true);
        }

        private sealed record SurveyLegendMetadata(string GroupName, IReadOnlyList<string> SelectedBlockNames);

        internal sealed record SurveyLegendItem(
            string Category,
            string BlockName,
            string MenuDescription,
            string SpacingType,
            int SortOrder);

        internal static class SurveyLegendCatalog
        {
            private const string CsvRelativePath = "Reference/SurveyLegend.csv";

            internal static IReadOnlyList<SurveyLegendItem> Load(Editor ed)
            {
                string? csvPath = ResolveCatalogPath();

                try
                {
                    var csvLines = csvPath != null
                        ? File.ReadAllLines(csvPath)
                        : ReadEmbeddedCatalogLines();

                    if (csvLines.Length == 0)
                    {
                        ed.WriteMessage("\nSurvey legend: could not find Reference/SurveyLegend.csv next to the loaded DLL, in the current working folder, or as an embedded resource.");
                        return Array.Empty<SurveyLegendItem>();
                    }

                    var rows = csvLines
                        .Skip(1)
                        .Select(ParseCsvLine)
                        .Where(c => c.Count >= 5)
                        .Select(c => new SurveyLegendItem(
                            NormalizeCategory(c[0]),
                            c[1].Trim(),
                            c[2].Trim(),
                            NormalizeSpacingType(c[3]),
                            ParseSortOrder(c[4])))
                        .Where(i => !string.Equals(i.Category, "Header", StringComparison.OrdinalIgnoreCase))
                        .Where(i => !string.IsNullOrWhiteSpace(i.BlockName))
                        .Where(i => !string.IsNullOrWhiteSpace(i.MenuDescription))
                        .OrderBy(i => i.SortOrder)
                        .ThenBy(i => i.MenuDescription, StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    return rows;
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nSurvey legend: failed to read SurveyLegend.csv: {ex.Message}");
                    return Array.Empty<SurveyLegendItem>();
                }
            }

            private static string[] ReadEmbeddedCatalogLines()
            {
                var asm = Assembly.GetExecutingAssembly();
                string? resourceName = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("Reference.SurveyLegend.csv", StringComparison.OrdinalIgnoreCase));

                if (resourceName == null)
                    return Array.Empty<string>();

                using Stream? stream = asm.GetManifestResourceStream(resourceName);
                if (stream == null)
                    return Array.Empty<string>();

                using var reader = new StreamReader(stream);
                var lines = new List<string>();
                while (!reader.EndOfStream)
                    lines.Add(reader.ReadLine() ?? string.Empty);

                return lines.ToArray();
            }

            private static string? ResolveCatalogPath()
            {
                var candidates = new List<string>();

                string? assemblyLocation = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrWhiteSpace(assemblyLocation))
                {
                    string? assemblyDir = Path.GetDirectoryName(assemblyLocation);
                    if (!string.IsNullOrWhiteSpace(assemblyDir))
                    {
                        candidates.Add(Path.Combine(assemblyDir, CsvRelativePath));

                        // Civil 3D development/test loads commonly run from bin\Debug\net8.0-windows.
                        // Walk upward so the source-tree Reference folder is found without requiring
                        // a manual copy beside the DLL. The embedded CSV remains the final fallback.
                        var dir = new DirectoryInfo(assemblyDir);
                        for (int i = 0; i < 6 && dir != null; i++)
                        {
                            candidates.Add(Path.Combine(dir.FullName, CsvRelativePath));
                            dir = dir.Parent;
                        }
                    }
                }

                candidates.Add(Path.Combine(Environment.CurrentDirectory, CsvRelativePath));
                candidates.Add(Path.Combine(AppContext.BaseDirectory, CsvRelativePath));

                return candidates
                    .Select(Path.GetFullPath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault(File.Exists);
            }

            private static int ParseSortOrder(string raw)
            {
                return int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                    ? value
                    : int.MaxValue;
            }

            private static string NormalizeSpacingType(string raw)
            {
                return IsDoubleSpacing(raw) ? "double" : "single";
            }

            private static string NormalizeCategory(string raw)
            {
                string text = (raw ?? string.Empty).Trim();
                if (text.Equals("Construction Notes", StringComparison.OrdinalIgnoreCase) ||
                    text.Equals("Constructions Notes", StringComparison.OrdinalIgnoreCase))
                    return "Construction Notes";

                return text;
            }

            private static List<string> ParseCsvLine(string line)
            {
                var values = new List<string>();
                var sb = new StringBuilder();
                bool inQuotes = false;

                for (int i = 0; i < line.Length; i++)
                {
                    char c = line[i];
                    if (c == '"')
                    {
                        if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                        {
                            sb.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = !inQuotes;
                        }
                    }
                    else if (c == ',' && !inQuotes)
                    {
                        values.Add(sb.ToString());
                        sb.Clear();
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }

                values.Add(sb.ToString());
                return values;
            }
        }

        internal sealed class SurveyLegendForm : Form
        {
            private readonly IReadOnlyList<SurveyLegendItem> _items;
            private readonly Dictionary<string, CheckBox> _checkBoxes = new(StringComparer.OrdinalIgnoreCase);

            public SurveyLegendForm(IReadOnlyList<SurveyLegendItem> items, IReadOnlyCollection<string> initiallySelectedBlockNames, string title)
            {
                _items = items;
                Text = title;
                FormBorderStyle = FormBorderStyle.Sizable;
                MinimizeBox = false;
                StartPosition = FormStartPosition.CenterScreen;
                ClientSize = new Size(520, 620);
                MinimumSize = new Size(440, 420);

                BuildUi(initiallySelectedBlockNames);
            }

            public IReadOnlyList<SurveyLegendItem> SelectedItems => _items
                .Where(i => _checkBoxes.TryGetValue(i.BlockName, out CheckBox? cb) && cb.Checked)
                .OrderBy(i => i.SortOrder)
                .ThenBy(i => i.MenuDescription, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            private void BuildUi(IReadOnlyCollection<string> initiallySelectedBlockNames)
            {
                var selectedSet = new HashSet<string>(initiallySelectedBlockNames, StringComparer.OrdinalIgnoreCase);

                var root = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 3,
                    Padding = new Padding(10)
                };
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                var instructions = new Label
                {
                    Text = "Select legend items to place. Items are inserted by Sort Order using the spacing from SurveyLegend.csv.",
                    AutoSize = true,
                    Dock = DockStyle.Top,
                    MaximumSize = new Size(480, 0),
                    Padding = new Padding(0, 0, 0, 8)
                };

                var scroll = new Panel
                {
                    Dock = DockStyle.Fill,
                    AutoScroll = true,
                    BorderStyle = BorderStyle.FixedSingle
                };

                var list = new FlowLayoutPanel
                {
                    Dock = DockStyle.Top,
                    AutoSize = true,
                    FlowDirection = System.Windows.Forms.FlowDirection.TopDown,
                    WrapContents = false,
                    Padding = new Padding(8)
                };

                foreach (string category in CategoryOrder)
                {
                    var categoryItems = _items
                        .Where(i => string.Equals(i.Category, category, StringComparison.OrdinalIgnoreCase))
                        .OrderBy(i => i.SortOrder)
                        .ThenBy(i => i.MenuDescription, StringComparer.OrdinalIgnoreCase)
                        .ToArray();

                    if (categoryItems.Length == 0)
                        continue;

                    var group = new GroupBox
                    {
                        Text = category.ToUpperInvariant(),
                        Width = 455,
                        AutoSize = true,
                        Padding = new Padding(10, 18, 10, 10),
                        Margin = new Padding(0, 0, 0, 8)
                    };

                    var groupPanel = new FlowLayoutPanel
                    {
                        Dock = DockStyle.Fill,
                        AutoSize = true,
                        FlowDirection = System.Windows.Forms.FlowDirection.TopDown,
                        WrapContents = false
                    };

                    foreach (var item in categoryItems)
                    {
                        var cb = new CheckBox
                        {
                            Text = item.MenuDescription,
                            Tag = item,
                            AutoSize = true,
                            Width = 420,
                            Checked = selectedSet.Contains(item.BlockName),
                            Margin = new Padding(3, 2, 3, 2)
                        };
                        _checkBoxes[item.BlockName] = cb;
                        groupPanel.Controls.Add(cb);
                    }

                    group.Controls.Add(groupPanel);
                    list.Controls.Add(group);
                }

                scroll.Controls.Add(list);

                var buttonPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft,
                    AutoSize = true,
                    Padding = new Padding(0, 8, 0, 0)
                };

                var ok = new Button
                {
                    Text = "OK",
                    DialogResult = DialogResult.OK,
                    Width = 90
                };

                var cancel = new Button
                {
                    Text = "CANCEL",
                    DialogResult = DialogResult.Cancel,
                    Width = 90
                };

                var clear = new Button
                {
                    Text = "CLEAR ALL",
                    Width = 90
                };
                clear.Click += (_, _) => SetAllChecked(false);

                var selectAll = new Button
                {
                    Text = "SELECT ALL",
                    Width = 90
                };
                selectAll.Click += (_, _) => SetAllChecked(true);

                AcceptButton = ok;
                CancelButton = cancel;

                buttonPanel.Controls.Add(ok);
                buttonPanel.Controls.Add(cancel);
                buttonPanel.Controls.Add(clear);
                buttonPanel.Controls.Add(selectAll);

                root.Controls.Add(instructions, 0, 0);
                root.Controls.Add(scroll, 0, 1);
                root.Controls.Add(buttonPanel, 0, 2);

                Controls.Add(root);
            }

            private void SetAllChecked(bool isChecked)
            {
                foreach (var cb in _checkBoxes.Values)
                    cb.Checked = isChecked;
            }
        }
    }
}
