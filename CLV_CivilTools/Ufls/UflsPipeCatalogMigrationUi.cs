using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace CLV_CivilTools.Ufls
{
    /// <summary>
    /// Dialog-based pipe catalog migration workflow.
    /// One grid row represents one legacy family/size/physical-size group.
    /// </summary>
    public static class UflsPipeCatalogMigrationUiCommands
    {
        private const string Unresolved = "<invalid/unresolved>";

        [CommandMethod("UFLS", "UFLS-PIPE-CATALOG-MIGRATE-UI", CommandFlags.Modal)]
        public static void MigrateWithDialog()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    CivilDocument civilDoc = CivilApplication.ActiveDocument;
                    ObjectIdCollection networkIds = civilDoc.GetPipeNetworkIds();
                    if (networkIds.Count == 0)
                    {
                        ed.WriteMessage("\nPIPE CATALOG MIGRATION: no pipe networks found.");
                        return;
                    }

                    ObjectId targetListId = SelectTargetPartsList(ed, civilDoc, tr);
                    if (targetListId.IsNull) return;

                    TargetInventory target = BuildTargetInventory(tr, targetListId);
                    List<MigrationRow> rows = BuildRows(tr, networkIds, target);

                    using var form = new MigrationReviewForm(target.Name, rows, target);
                    if (AcadApp.ShowModalDialog(form) != DialogResult.OK)
                    {
                        ed.WriteMessage("\nPIPE CATALOG MIGRATION: cancelled. Drawing unchanged.");
                        return;
                    }

                    if (MessageBox.Show(
                            $"Apply the selected automatic swaps to '{target.Name}'?\n\n" +
                            "Rows requiring manual work will be left unswapped and highlighted RED in the drawing.",
                            "CLV Pipe Catalog Migration",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Warning) != DialogResult.Yes)
                    {
                        ed.WriteMessage("\nPIPE CATALOG MIGRATION: cancelled. Drawing unchanged.");
                        return;
                    }

                    int swapped = 0;
                    int failed = 0;
                    int manual = 0;
                    int skipped = 0;

                    foreach (MigrationRow row in rows)
                    {
                        if (!row.Include)
                        {
                            if (RequiresManualAttention(row.Status)) manual++;
                            else skipped++;
                            continue;
                        }

                        TargetFamily? family = target.FindFamily(row.Domain, row.SelectedFamilyName);
                        if (family == null)
                        {
                            row.Status = "CHOOSE TARGET FAMILY";
                            manual++;
                            continue;
                        }

                        if (!PartTypesCompatible(row.LegacyPartType, family.PartType))
                        {
                            row.Status = "MANUAL REPLACEMENT";
                            manual++;
                            continue;
                        }

                        ObjectId targetSizeId = ObjectId.Null;
                        try
                        {
                            if (row.Domain == "Pipe")
                            {
                                TargetSize? pipeSize = FindPipeTargetSize(family, row);
                                if (pipeSize == null)
                                {
                                    row.Status = "NO TARGET SIZE";
                                    manual++;
                                    continue;
                                }
                                targetSizeId = pipeSize.Id;
                            }
                            else
                            {
                                targetSizeId = ResolveStructureTargetSize(tr, ed, family, row);
                                if (targetSizeId.IsNull)
                                {
                                    row.Status = "MANUAL SIZE/REPLACEMENT";
                                    manual++;
                                    continue;
                                }
                            }

                            foreach (ObjectId objectId in row.ObjectIds)
                            {
                                try
                                {
                                    if (tr.GetObject(objectId, OpenMode.ForWrite, false) is Part part)
                                    {
                                        part.SwapPartFamilyAndSize(family.Id, targetSizeId);
                                        swapped++;
                                    }
                                }
                                catch (System.Exception ex)
                                {
                                    failed++;
                                    row.Status = "FAILED";
                                    ed.WriteMessage($"\n  FAILED {row.Id} object {objectId.Handle}: {ex.Message}");
                                }
                            }
                        }
                        catch (System.Exception ex)
                        {
                            failed++;
                            row.Status = "FAILED";
                            ed.WriteMessage($"\n  FAILED {row.Id}: {ex.Message}");
                        }
                    }

                    // Make every unresolved/manual row easy to locate in plan view.
                    int redHighlighted = HighlightManualRowsRed(tr, rows);

                    if (swapped > 0)
                    {
                        foreach (ObjectId networkId in networkIds)
                        {
                            if (tr.GetObject(networkId, OpenMode.ForWrite, false) is Network network)
                                network.PartsListId = target.Id;
                        }
                    }

                    tr.Commit();

                    ed.WriteMessage("\n\nPIPE CATALOG MIGRATION RESULT");
                    ed.WriteMessage($"\n  Swapped parts: {swapped}");
                    ed.WriteMessage($"\n  Manual-review/replacement rows: {manual}");
                    ed.WriteMessage($"\n  Manual parts highlighted red: {redHighlighted}");
                    ed.WriteMessage($"\n  Skipped rows: {skipped}");
                    ed.WriteMessage($"\n  Failures: {failed}");
                    ed.WriteMessage($"\n  Target Parts List: {target.Name}");
                    ed.WriteMessage("\n  Red parts require manual review/replacement before finalizing the drawing.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPIPE CATALOG MIGRATION error: {ex.Message}");
            }
        }

        private static ObjectId SelectTargetPartsList(Editor ed, CivilDocument civilDoc, Transaction tr)
        {
            var choices = new List<(ObjectId Id, string Name)>();
            foreach (ObjectId id in civilDoc.Styles.PartsListSet)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead, false) is PartsList list)
                        choices.Add((id, list.Name));
                }
                catch { }
            }

            choices = choices.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (choices.Count == 0) return ObjectId.Null;

            ed.WriteMessage("\n\nSELECT TARGET PARTS LIST");
            for (int i = 0; i < choices.Count; i++)
                ed.WriteMessage($"\n  [{i + 1}] {choices[i].Name}");

            var opt = new PromptIntegerOptions("\nEnter target Parts List number: ")
            {
                LowerLimit = 1,
                UpperLimit = choices.Count,
                AllowZero = false,
                AllowNegative = false,
                AllowNone = false
            };
            PromptIntegerResult result = ed.GetInteger(opt);
            return result.Status == PromptStatus.OK ? choices[result.Value - 1].Id : ObjectId.Null;
        }

        private static TargetInventory BuildTargetInventory(Transaction tr, ObjectId listId)
        {
            if (tr.GetObject(listId, OpenMode.ForRead, false) is not PartsList list)
                throw new InvalidOperationException("Could not open target Parts List.");

            var target = new TargetInventory(listId, list.Name);
            LoadFamilies(tr, list, DomainType.Pipe, target.PipeFamilies);
            LoadFamilies(tr, list, DomainType.Structure, target.StructureFamilies);
            return target;
        }

        private static void LoadFamilies(Transaction tr, PartsList list, DomainType domain, List<TargetFamily> output)
        {
            foreach (ObjectId id in list.GetPartFamilyIdsByDomain(domain))
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead, false) is not PartFamily family) continue;
                    string name = SafeString(family, "Name");
                    if (string.IsNullOrWhiteSpace(name) || name.StartsWith("-----", StringComparison.Ordinal)) continue;

                    var item = new TargetFamily(
                        id,
                        name,
                        domain == DomainType.Pipe ? "Pipe" : "Structure",
                        SafeString(family, "PartType"),
                        domain == DomainType.Pipe ? SafeString(family, "SweptShape") : SafeString(family, "BoundingShape"));

                    for (int i = 0; i < family.PartSizeCount; i++)
                    {
                        ObjectId sizeId = family[i];
                        if (tr.GetObject(sizeId, OpenMode.ForRead, false) is PartSize size)
                            item.Sizes.Add(new TargetSize(sizeId, SafeString(size, "Name"), SafeString(size, "Description")));
                    }
                    output.Add(item);
                }
                catch { }
            }
        }

        private static List<MigrationRow> BuildRows(Transaction tr, ObjectIdCollection networkIds, TargetInventory target)
        {
            var rows = new List<MigrationRow>();
            var pipeGroups = new Dictionary<string, MigrationRow>(StringComparer.OrdinalIgnoreCase);
            var structureGroups = new Dictionary<string, MigrationRow>(StringComparer.OrdinalIgnoreCase);

            foreach (ObjectId networkId in networkIds)
            {
                if (tr.GetObject(networkId, OpenMode.ForRead, false) is not Network network) continue;

                foreach (ObjectId id in network.GetPipeIds())
                {
                    if (tr.GetObject(id, OpenMode.ForRead, false) is not Pipe pipe) continue;
                    string family = SafePartIdentity(pipe, "PartFamilyName");
                    string size = SafePartIdentity(pipe, "PartSizeName");
                    string type = SafePartIdentity(pipe, "PartType");
                    double diameterFt = SafeDouble(pipe, "InnerDiameterOrWidth");
                    string key = $"{family}|{size}|{type}|{diameterFt:0.######}";

                    if (!pipeGroups.TryGetValue(key, out MigrationRow? row))
                    {
                        row = new MigrationRow
                        {
                            Domain = "Pipe",
                            LegacyFamily = family,
                            LegacySize = size,
                            LegacyPartType = type,
                            DiameterFeet = diameterFt,
                            Dimensions = $"ID {diameterFt * 12.0:0.##}\""
                        };
                        pipeGroups.Add(key, row);
                    }
                    row.ObjectIds.Add(id);
                }

                foreach (ObjectId id in network.GetStructureIds())
                {
                    if (tr.GetObject(id, OpenMode.ForRead, false) is not Structure structure) continue;
                    string family = SafePartIdentity(structure, "PartFamilyName");
                    string size = SafePartIdentity(structure, "PartSizeName");
                    string type = SafePartIdentity(structure, "PartType");
                    double length = SafeDouble(structure, "InnerLength");
                    double width = SafeDouble(structure, "InnerDiameterOrWidth");
                    double height = SafeDouble(structure, "Height");
                    double wall = SafeDouble(structure, "WallThickness");

                    if (wall <= 0.0 && TryExtractWallInches(size, out double parsedWallInches))
                        wall = parsedWallInches / 12.0;

                    double diameter = length <= 0.0 ? width : 0.0;
                    string key = $"{family}|{size}|{type}|{length:0.######}|{width:0.######}|{diameter:0.######}|{wall:0.######}";

                    if (!structureGroups.TryGetValue(key, out MigrationRow? row))
                    {
                        row = new MigrationRow
                        {
                            Domain = "Structure",
                            LegacyFamily = family,
                            LegacySize = size,
                            LegacyPartType = type,
                            LengthFeet = length,
                            WidthFeet = width,
                            DiameterFeet = diameter,
                            HeightFeet = height,
                            WallThicknessFeet = wall,
                            Dimensions = length > 0.0
                                ? $"L {length * 12.0:0.##}\" x W {width * 12.0:0.##}\" | Wall {wall * 12.0:0.##}\" | H {height:0.##}'"
                                : $"Dia {diameter * 12.0:0.##}\" | Wall {wall * 12.0:0.##}\" | H {height:0.##}'"
                        };
                        structureGroups.Add(key, row);
                    }
                    row.ObjectIds.Add(id);
                }
            }

            int p = 1;
            foreach (MigrationRow row in pipeGroups.Values.OrderBy(r => r.LegacySize))
            {
                row.Id = $"P{p++:000}";
                AutoMapPipe(row, target);
                rows.Add(row);
            }

            int s = 1;
            foreach (MigrationRow row in structureGroups.Values.OrderBy(r => r.LegacyFamily).ThenBy(r => r.LegacySize))
            {
                row.Id = $"S{s++:000}";
                AutoMapStructure(row, target);
                rows.Add(row);
            }

            return rows;
        }

        private static void AutoMapPipe(MigrationRow row, TargetInventory target)
        {
            string material = DetectPipeMaterial(row.LegacyFamily + " " + row.LegacySize);
            double inches = row.DiameterFeet * 12.0;
            var candidates = target.PipeFamilies
                .SelectMany(f => f.Sizes.Select(s => new { Family = f, Size = s, Inches = ExtractFirstInches(s.Name) }))
                .Where(x => string.IsNullOrEmpty(material) || WholeToken(x.Family.Name + " " + x.Size.Name, material))
                .Where(x => x.Inches.HasValue && Math.Abs(x.Inches.Value - inches) < 0.11)
                .ToList();

            if (candidates.Count == 1)
            {
                row.SelectedFamilyName = candidates[0].Family.Name;
                row.TargetSizeName = candidates[0].Size.Name;
                row.Include = true;
                row.Status = "AUTO MATCH";
            }
            else
            {
                row.Include = false;
                row.Status = candidates.Count > 1 ? "REVIEW MATCH" : "NO MATCH";
            }
        }

        private static void AutoMapStructure(MigrationRow row, TargetInventory target)
        {
            if (string.Equals(row.LegacyFamily, Unresolved, StringComparison.OrdinalIgnoreCase))
            {
                row.Status = "CHOOSE FROM PLANS";
                row.Include = false;
                return;
            }

            TargetFamily? exactFamily = target.StructureFamilies.FirstOrDefault(f =>
                string.Equals(Normalize(f.Name), Normalize(row.LegacyFamily), StringComparison.OrdinalIgnoreCase));

            if (exactFamily == null)
            {
                row.Status = "CHOOSE TARGET FAMILY";
                row.Include = false;
                return;
            }

            row.SelectedFamilyName = exactFamily.Name;
            row.Include = PartTypesCompatible(row.LegacyPartType, exactFamily.PartType);
            row.Status = row.Include ? "FAMILY MATCH" : "MANUAL REPLACEMENT";
            row.TargetSizeName = row.LengthFeet > 0.0
                ? "Match/add L x W x Wall on Apply"
                : exactFamily.Sizes.FirstOrDefault(s => string.Equals(Normalize(s.Name), Normalize(row.LegacySize), StringComparison.OrdinalIgnoreCase))?.Name
                    ?? "Manual cylindrical size review";
        }

        private static ObjectId ResolveStructureTargetSize(Transaction tr, Editor ed, TargetFamily family, MigrationRow row)
        {
            // Box structures always resolve from physical dimensions so L/W/WALL are all preserved.
            if (row.LengthFeet > 0.0 && row.WidthFeet > 0.0)
            {
                if (tr.GetObject(family.Id, OpenMode.ForWrite, false) is not PartFamily civilFamily)
                    return ObjectId.Null;

                return UflsStructurePartSizeService.EnsureMatchingBoxSize(
                    tr,
                    civilFamily,
                    row.WidthFeet * 12.0,
                    row.LengthFeet * 12.0,
                    row.WallThicknessFeet * 12.0,
                    ed);
            }

            // Cylindrical structures are only auto-swapped when an exact named target size exists.
            TargetSize? exactName = family.Sizes.FirstOrDefault(s =>
                string.Equals(Normalize(s.Name), Normalize(row.LegacySize), StringComparison.OrdinalIgnoreCase));
            return exactName?.Id ?? ObjectId.Null;
        }

        private static TargetSize? FindPipeTargetSize(TargetFamily family, MigrationRow row)
        {
            if (!string.IsNullOrWhiteSpace(row.TargetSizeName))
            {
                TargetSize? named = family.Sizes.FirstOrDefault(s =>
                    string.Equals(s.Name, row.TargetSizeName, StringComparison.OrdinalIgnoreCase));
                if (named != null) return named;
            }

            double inches = row.DiameterFeet * 12.0;
            return family.Sizes.FirstOrDefault(s =>
                ExtractFirstInches(s.Name) is double d && Math.Abs(d - inches) < 0.11);
        }

        private static bool PartTypesCompatible(string a, string b)
            => !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
               string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

        private static string DetectPipeMaterial(string text)
        {
            foreach (string value in new[] { "HERCP", "RCP", "RCB", "C900", "PVC", "HDPE", "ACP" })
                if (WholeToken(text, value)) return value;
            return string.Empty;
        }

        private static bool WholeToken(string text, string token)
        {
            string n = " " + new string(text.ToUpperInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray()) + " ";
            return n.Contains(" " + token.ToUpperInvariant() + " ", StringComparison.Ordinal);
        }

        private static double? ExtractFirstInches(string text)
        {
            Match m = Regex.Match(
                text,
                @"(?<![0-9.])([0-9]+(?:\.[0-9]+)?)\s*(?:INCH|IN\b|''|"")",
                RegexOptions.IgnoreCase);
            return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                ? d
                : null;
        }

        private static bool TryExtractWallInches(string text, out double wallInches)
        {
            wallInches = 0.0;
            Match m = Regex.Match(
                text,
                @"WALL(?:S)?\s*=\s*(?<wall>[0-9]+(?:\.[0-9]+)?)\s*(?:''|"")?\s*(?:INCH(?:ES)?)?",
                RegexOptions.IgnoreCase);
            return m.Success &&
                   double.TryParse(m.Groups["wall"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out wallInches);
        }

        private static string Normalize(string value)
            => Regex.Replace((value ?? string.Empty).Trim().ToUpperInvariant(), @"\s+", " ");

        private static string SafePartIdentity(object source, string prop)
        {
            string value = SafeString(source, prop);
            return string.IsNullOrWhiteSpace(value) ? Unresolved : value;
        }

        private static string SafeString(object source, string prop)
        {
            try { return source.GetType().GetProperty(prop)?.GetValue(source)?.ToString() ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static double SafeDouble(object source, string prop)
        {
            try { return source.GetType().GetProperty(prop)?.GetValue(source) is double d ? d : 0.0; }
            catch { return 0.0; }
        }

        private static bool RequiresManualAttention(string status)
        {
            string value = Normalize(status);
            return value.Contains("NO MATCH", StringComparison.Ordinal) ||
                   value.Contains("CHOOSE", StringComparison.Ordinal) ||
                   value.Contains("MANUAL", StringComparison.Ordinal) ||
                   value.Contains("FAILED", StringComparison.Ordinal) ||
                   value.Contains("NO TARGET SIZE", StringComparison.Ordinal);
        }

        private static int HighlightManualRowsRed(Transaction tr, IEnumerable<MigrationRow> rows)
        {
            int highlighted = 0;
            foreach (MigrationRow row in rows.Where(r => RequiresManualAttention(r.Status)))
            {
                foreach (ObjectId id in row.ObjectIds)
                {
                    try
                    {
                        if (tr.GetObject(id, OpenMode.ForWrite, false) is AcEntity ent)
                        {
                            ent.ColorIndex = 1;
                            ent.RecordGraphicsModified(true);
                            highlighted++;
                        }
                    }
                    catch
                    {
                        // Do not abort migration because one object could not be highlighted.
                    }
                }
            }
            return highlighted;
        }

        private sealed class MigrationReviewForm : Form
        {
            private readonly DataGridView _grid = new();
            private readonly List<MigrationRow> _rows;
            private readonly TargetInventory _target;

            internal MigrationReviewForm(string targetName, List<MigrationRow> rows, TargetInventory target)
            {
                _rows = rows;
                _target = target;

                Text = $"CLV Pipe Catalog Migration - {targetName}";
                Width = 1500;
                Height = 780;
                StartPosition = FormStartPosition.CenterScreen;
                MinimizeBox = false;

                var info = new System.Windows.Forms.Label
                {
                    Dock = DockStyle.Top,
                    Height = 58,
                    Padding = new Padding(10, 8, 10, 4),
                    Text = "Review one row per legacy part group. Red rows require manual attention. " +
                           "For box structures, physical L/W/WALL will be matched or added to the selected target family on Apply. " +
                           "Old unresolved structures remain unchecked until classified from the plans."
                };

                ConfigureGrid();
                LoadRows();

                var buttons = new FlowLayoutPanel
                {
                    Dock = DockStyle.Bottom,
                    Height = 48,
                    FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft,
                    Padding = new Padding(8)
                };

                var apply = new Button { Text = "Apply Selected", Width = 110, DialogResult = DialogResult.OK };
                var cancel = new Button { Text = "Cancel", Width = 90, DialogResult = DialogResult.Cancel };
                buttons.Controls.Add(apply);
                buttons.Controls.Add(cancel);
                AcceptButton = apply;
                CancelButton = cancel;

                Controls.Add(_grid);
                Controls.Add(info);
                Controls.Add(buttons);
            }

            private void ConfigureGrid()
            {
                _grid.Dock = DockStyle.Fill;
                _grid.AllowUserToAddRows = false;
                _grid.AllowUserToDeleteRows = false;
                _grid.AutoGenerateColumns = false;
                _grid.RowHeadersVisible = false;
                _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;

                _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Include", HeaderText = "Use", Width = 42 });
                _grid.Columns.Add(TextColumn("Id", "ID", 48));
                _grid.Columns.Add(TextColumn("Domain", "Type", 65));
                _grid.Columns.Add(TextColumn("Count", "Count", 55));
                _grid.Columns.Add(TextColumn("LegacyFamily", "Legacy Family", 205));
                _grid.Columns.Add(TextColumn("LegacySize", "Legacy Size", 280));
                _grid.Columns.Add(TextColumn("Dimensions", "Physical Size", 245));
                _grid.Columns.Add(new DataGridViewComboBoxColumn
                {
                    Name = "TargetFamily",
                    HeaderText = "Target Family",
                    Width = 235,
                    FlatStyle = FlatStyle.Flat
                });
                _grid.Columns.Add(TextColumn("TargetSize", "Target Size", 225));
                _grid.Columns.Add(TextColumn("Status", "Status", 155));

                _grid.CellValueChanged += GridCellValueChanged;
                _grid.CurrentCellDirtyStateChanged += (_, _) =>
                {
                    if (_grid.IsCurrentCellDirty)
                        _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                };
            }

            private static DataGridViewTextBoxColumn TextColumn(string name, string header, int width)
                => new() { Name = name, HeaderText = header, Width = width, ReadOnly = true };

            private void LoadRows()
            {
                foreach (MigrationRow item in _rows)
                {
                    int index = _grid.Rows.Add();
                    DataGridViewRow row = _grid.Rows[index];
                    row.Tag = item;
                    row.Cells["Include"].Value = item.Include;
                    row.Cells["Id"].Value = item.Id;
                    row.Cells["Domain"].Value = item.Domain;
                    row.Cells["Count"].Value = item.ObjectIds.Count;
                    row.Cells["LegacyFamily"].Value = item.LegacyFamily;
                    row.Cells["LegacySize"].Value = item.LegacySize;
                    row.Cells["Dimensions"].Value = item.Dimensions;
                    row.Cells["TargetSize"].Value = item.TargetSizeName;
                    row.Cells["Status"].Value = item.Status;

                    if (row.Cells["TargetFamily"] is DataGridViewComboBoxCell combo)
                    {
                        IEnumerable<string> names = item.Domain == "Pipe"
                            ? _target.PipeFamilies.Select(f => f.Name)
                            : _target.StructureFamilies.Select(f => f.Name);

                        combo.Items.Add(string.Empty);
                        combo.Items.AddRange(names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Cast<object>().ToArray());
                        combo.Value = combo.Items.Contains(item.SelectedFamilyName) ? item.SelectedFamilyName : string.Empty;
                    }

                    ApplyAttentionStyle(row, item);
                }
            }

            private void GridCellValueChanged(object? sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex < 0 || e.ColumnIndex < 0) return;
                DataGridViewRow gridRow = _grid.Rows[e.RowIndex];
                if (gridRow.Tag is not MigrationRow item) return;

                string columnName = _grid.Columns[e.ColumnIndex].Name;
                if (columnName == "Include")
                    item.Include = Convert.ToBoolean(gridRow.Cells["Include"].Value ?? false);

                if (columnName == "TargetFamily")
                {
                    item.SelectedFamilyName = Convert.ToString(gridRow.Cells["TargetFamily"].Value) ?? string.Empty;
                    TargetFamily? family = _target.FindFamily(item.Domain, item.SelectedFamilyName);

                    if (family == null)
                    {
                        item.Include = false;
                        item.Status = "CHOOSE TARGET FAMILY";
                        item.TargetSizeName = string.Empty;
                    }
                    else if (!PartTypesCompatible(item.LegacyPartType, family.PartType))
                    {
                        item.Include = false;
                        item.Status = "MANUAL REPLACEMENT";
                        item.TargetSizeName = "Replace/reconnect manually";
                    }
                    else
                    {
                        item.Include = true;
                        item.Status = item.Domain == "Pipe" ? "READY" : "MATCH/ADD SIZE ON APPLY";
                        item.TargetSizeName = item.Domain == "Pipe"
                            ? FindPipeTargetSize(family, item)?.Name ?? "No matching diameter"
                            : item.LengthFeet > 0.0
                                ? "Match/add L x W x Wall on Apply"
                                : "Exact cylindrical size required";
                    }

                    gridRow.Cells["Include"].Value = item.Include;
                    gridRow.Cells["TargetSize"].Value = item.TargetSizeName;
                    gridRow.Cells["Status"].Value = item.Status;
                }

                ApplyAttentionStyle(gridRow, item);
            }

            private static void ApplyAttentionStyle(DataGridViewRow row, MigrationRow item)
            {
                if (RequiresManualAttention(item.Status))
                {
                    row.DefaultCellStyle.BackColor = Color.MistyRose;
                    row.DefaultCellStyle.ForeColor = Color.DarkRed;
                }
                else
                {
                    row.DefaultCellStyle.BackColor = SystemColors.Window;
                    row.DefaultCellStyle.ForeColor = SystemColors.ControlText;
                }
            }
        }

        private sealed class MigrationRow
        {
            public string Id { get; set; } = string.Empty;
            public string Domain { get; set; } = string.Empty;
            public string LegacyFamily { get; set; } = string.Empty;
            public string LegacySize { get; set; } = string.Empty;
            public string LegacyPartType { get; set; } = string.Empty;
            public string Dimensions { get; set; } = string.Empty;
            public string SelectedFamilyName { get; set; } = string.Empty;
            public string TargetSizeName { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public bool Include { get; set; }
            public double LengthFeet { get; set; }
            public double WidthFeet { get; set; }
            public double DiameterFeet { get; set; }
            public double HeightFeet { get; set; }
            public double WallThicknessFeet { get; set; }
            public List<ObjectId> ObjectIds { get; } = new();
        }

        private sealed class TargetInventory
        {
            public TargetInventory(ObjectId id, string name)
            {
                Id = id;
                Name = name;
            }

            public ObjectId Id { get; }
            public string Name { get; }
            public List<TargetFamily> PipeFamilies { get; } = new();
            public List<TargetFamily> StructureFamilies { get; } = new();

            public TargetFamily? FindFamily(string domain, string name)
            {
                IEnumerable<TargetFamily> source = domain == "Pipe" ? PipeFamilies : StructureFamilies;
                return source.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
            }
        }

        private sealed class TargetFamily
        {
            public TargetFamily(ObjectId id, string name, string domain, string partType, string shape)
            {
                Id = id;
                Name = name;
                Domain = domain;
                PartType = partType;
                Shape = shape;
            }

            public ObjectId Id { get; }
            public string Name { get; }
            public string Domain { get; }
            public string PartType { get; }
            public string Shape { get; }
            public List<TargetSize> Sizes { get; } = new();
        }

        private sealed record TargetSize(ObjectId Id, string Name, string Description);
    }
}
