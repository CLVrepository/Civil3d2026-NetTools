using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using SD = System.Drawing;

namespace CLV_CivilTools.Ufls.StormInfo
{
    internal sealed class StormCatalogForm : Form
    {
        internal string ActionName { get; private set; } = "";
        internal string? SelectedName => _grid.CurrentRow?.Tag is StormStructure m ? m.Name : null;
        private readonly DataGridView _grid;
        internal StormCatalogForm(StormCatalog? c)
        {
            Text = "STORM STRUCTURE INFO - " + (c?.ProjectNumber ?? "No catalog imported");
            StartPosition = FormStartPosition.CenterParent; MinimumSize = new SD.Size(820, 540);
            Size = new SD.Size(1000, 690); AutoScaleMode = AutoScaleMode.Dpi;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(10) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Select a record to place or edit. All elevations are feet; all diameters are inches.\nImport JSON or add records manually. Transfer uses a placed block and automatic pipe matching." }, 0, 0);
            _grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false };
            foreach (string col in new[] { "Plan label", "Type", "Number", "Name", "Rim (ft)", "Structure (in)", "Connections", "Status" }) _grid.Columns.Add(col, col);
            if (c != null) foreach (StormStructure m in c.Structures.OrderBy(m => m.Number))
            {
                int row = _grid.Rows.Add(m.SourceId, m.Type, m.Number, m.Name, StormInfoModel.Elevation(m.RimElevation), StormInfoModel.StructureSize(m), m.Connections.Count, string.IsNullOrWhiteSpace(m.ApplicationHoldReason) ? (m.ReviewStatus ?? "needs review").Replace('_', ' ') : "HOLD: " + m.ApplicationHoldReason);
                _grid.Rows[row].Tag = m;
            }
            var details = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false };
            _grid.SelectionChanged += (_, _) => details.Text = _grid.CurrentRow?.Tag is StormStructure m ? StormInfoModel.Serialize(m) : "";
            layout.Controls.Add(_grid, 0, 1); layout.Controls.Add(details, 0, 2);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            foreach (var item in new[] { ("Import JSON", "import"), ("Add", "add"), ("Edit", "edit"), ("Place", "place"), ("Apply to network", "apply"), ("Refresh block", "refresh"), ("Export JSON", "export"), ("Close", "close") })
            {
                var b = new Button { Text = item.Item1, AutoSize = true, Height = 30 };
                string action = item.Item2;
                b.Click += (_, _) => { if ((action == "edit" || action == "place") && SelectedName == null) return; ActionName = action; DialogResult = DialogResult.OK; Close(); };
                buttons.Controls.Add(b);
            }
            layout.Controls.Add(buttons, 0, 3); Controls.Add(layout);
            if (_grid.Rows.Count > 0) { _grid.CurrentCell = _grid.Rows[0].Cells[0]; details.Text = StormInfoModel.Serialize((StormStructure)_grid.Rows[0].Tag!); }
        }
    }
    internal sealed class StormEditForm : Form
    {
        private readonly TextBox _project = new(), _number = new(), _rim = new(), _diameter = new(), _station = new(), _basis = new(), _notes = new();
        private readonly DataGridView _pipes = new();
        private readonly ComboBox _kind = new() { DropDownStyle = ComboBoxStyle.DropDownList }, _topBasis = new() { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox _width = new(), _length = new();
        private readonly TextBox _source = new(), _inlet = new(), _opening = new();
        private readonly CheckBox _clearHold = new() { Text = "I reviewed/corrected the plan conflict; clear the application hold.", AutoSize = true };
        private readonly StormCatalog? _catalog;
        private readonly StormStructure? _existing;
        internal StormCatalog? ResultCatalog { get; private set; }
        internal StormEditForm(StormCatalog? catalog, StormStructure? existing)
        {
            _catalog = catalog; _existing = existing;
            Text = existing == null ? "ADD STORM STRUCTURE" : "EDIT " + existing.Name;
            StartPosition = FormStartPosition.CenterParent; MinimumSize = new SD.Size(900, 620); Size = new SD.Size(1100, 740); AutoScaleMode = AutoScaleMode.Dpi;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Padding = new Padding(10) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 265)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 8 };
            for (int i = 0; i < 4; i++) fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, i % 2 == 0 ? 20 : 30));
            void Field(string title, Control box, int col, int row) { fields.Controls.Add(new Label { Text = title, AutoSize = true }, col, row); box.Dock = DockStyle.Fill; fields.Controls.Add(box, col + 1, row); }
            Field("Project", _project, 0, 0); Field("Structure ID (6, 6A)", _number, 2, 0);
            Field("Rim elevation (ft)", _rim, 0, 1); Field("Structure diameter (in)", _diameter, 2, 1);
            Field("Station (reference)", _station, 0, 2); Field("Diameter basis", _basis, 2, 2);
            _kind.Items.AddRange(new object[] { "SDMH", "SDDI" }); _kind.SelectedItem = existing?.Type ?? "SDMH"; _kind.Enabled = existing == null;
            _topBasis.Items.AddRange(new object[] { "unknown", "rim", "grate", "flowline", "top" }); _topBasis.SelectedItem = existing?.TopElevationBasis ?? "unknown";
            Field("Structure type", _kind, 0, 3); Field("Elevation basis", _topBasis, 2, 3);
            Field("Rect. width (in)", _width, 0, 4); Field("Rect. length (in)", _length, 2, 4);
            _width.Text = StormInfoModel.Number(existing?.StructureWidthInches); _length.Text = StormInfoModel.Number(existing?.StructureLengthInches);
            Field("Consultant label", _source, 0, 5); Field("Inlet type", _inlet, 2, 5);
            Field("Opening length (in)", _opening, 0, 6);
            _source.Text = existing?.SourceId ?? ""; _inlet.Text = existing?.InletType ?? ""; _opening.Text = StormInfoModel.Number(existing?.InletOpeningLengthInches);
            fields.Controls.Add(new Label { Text = "Names use project-SDMH-01 or project-SDDI-06A. Keep the consultant label separately. TC is not a rim/grate elevation. Refresh placed blocks after editing.", Dock = DockStyle.Fill }, 0, 7); fields.SetColumnSpan(fields.GetControlFromPosition(0, 7)!, 4);
            _project.Text = catalog?.ProjectNumber ?? ""; _project.ReadOnly = catalog != null;
            _number.Text = existing?.Number.ToString() ?? ""; _number.ReadOnly = existing != null;
            _rim.Text = StormInfoModel.Number(existing?.RimElevation); _diameter.Text = StormInfoModel.Number(existing?.StructureDiameterInches);
            _station.Text = existing?.Station ?? ""; _basis.Text = existing?.StructureDiameterBasis is null or "base_as_labeled" ? "BASE" : existing.StructureDiameterBasis; _notes.Text = existing?.Notes ?? "";
            layout.Controls.Add(fields, 0, 0);
            if (!string.IsNullOrWhiteSpace(existing?.ApplicationHoldReason))
            { _clearHold.Text += " " + existing.ApplicationHoldReason; _clearHold.MaximumSize = new SD.Size(1000, 0); }
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "PIPE CONNECTIONS - one row per connected pipe; include multiple IN pipes. Delete selected rows with Delete.\nMaterial code: C900, PVC, RCP, SDR35 or your project designation. Specification and material family remain separate." }, 0, 1);
            _pipes.Dock = DockStyle.Fill; _pipes.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _pipes.RowHeadersVisible = true; _pipes.AllowUserToAddRows = true; _pipes.AllowUserToDeleteRows = true;
            _pipes.Columns.Add(new DataGridViewComboBoxColumn { Name = "Flow", HeaderText = "Flow", DataSource = new[] { "in", "out" } });
            foreach (string col in new[] { "Direction", "Invert ft", "Diameter in", "Material code", "Specification", "Material family", "To/From structure", "Shape", "Width in", "Height in" }) _pipes.Columns.Add(col, col);
            if (existing != null) foreach (StormConnection p in existing.Connections)
            {
                int row = _pipes.Rows.Add(p.Flow, p.Direction, StormInfoModel.Number(p.InvertElevation), StormInfoModel.Number(p.DiameterInches), p.MaterialCode ?? "", p.PipeSpecification ?? "", p.Material ?? "", p.ConnectedStructureName ?? "", p.Shape, StormInfoModel.Number(p.WidthInches), StormInfoModel.Number(p.HeightInches));
                _pipes.Rows[row].Tag = p;
            }
            layout.Controls.Add(_pipes, 0, 2); _notes.Multiline = true; _notes.Dock = DockStyle.Fill; _notes.ScrollBars = ScrollBars.Vertical;
            var notesPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            if (!string.IsNullOrWhiteSpace(existing?.ApplicationHoldReason)) { notesPanel.Controls.Add(_clearHold, 0, 1); notesPanel.SetColumnSpan(_clearHold, 2); } notesPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70)); notesPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            notesPanel.Controls.Add(new Label { Text = "Notes", AutoSize = true }, 0, 0); notesPanel.Controls.Add(_notes, 1, 0); layout.Controls.Add(notesPanel, 0, 3);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill };
            var save = new Button { Text = "Save record", AutoSize = true }; save.Click += (_, _) => Save();
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            buttons.Controls.Add(save); buttons.Controls.Add(cancel); CancelButton = cancel; layout.Controls.Add(buttons, 0, 4); Controls.Add(layout);
        }
        private void Save()
        {
            try
            {
                _pipes.EndEdit();
                StormCatalog c = _catalog == null ? new StormCatalog { ProjectNumber = _project.Text.Trim().ToUpperInvariant() } : StormInfoModel.Copy(_catalog);
                StormStructureNumber number = StormStructureNumber.Parse(_number.Text);
                if (_existing == null && c.Structures.Any(m => m.Number == number && m.Type == (string)_kind.SelectedItem!)) throw new ArgumentException("That number already exists. Use Edit.");
                StormStructure m = _existing == null ? new StormStructure() : StormInfoModel.Copy(_existing);
                m.Number = number; m.Type = (string)_kind.SelectedItem!; m.Name = StormInfoModel.Name(c.ProjectNumber, number, m.Type); m.SourceId = string.IsNullOrWhiteSpace(m.SourceId) ? m.Type + "#" + number : m.SourceId;
                m.TopElevationBasis = (string)_topBasis.SelectedItem!;
                m.SourceId = string.IsNullOrWhiteSpace(_source.Text) ? m.SourceId : _source.Text.Trim(); m.InletType = _inlet.Text.Trim();
                m.InletOpeningLengthInches = StormInfoModel.ReadNumber(_opening.Text, "Opening length");
                m.StructureWidthInches = StormInfoModel.ReadNumber(_width.Text, "Width"); m.StructureLengthInches = StormInfoModel.ReadNumber(_length.Text, "Length");
                m.RimElevation = StormInfoModel.ReadNumber(_rim.Text, "Rim"); m.StructureDiameterInches = StormInfoModel.ReadNumber(_diameter.Text, "Structure diameter");
                m.StructureDiameterBasis = string.Equals(_basis.Text.Trim(), "BASE", StringComparison.OrdinalIgnoreCase) ? "base_as_labeled" : _basis.Text.Trim(); m.Station = _station.Text.Trim(); m.Notes = _notes.Text;
                if (_clearHold.Checked) m.ApplicationHoldReason = "";
                m.ReviewStatus = "manually_edited_needs_review"; m.Completeness = "manual";
                if (_existing?.StructureDiameterInches != m.StructureDiameterInches) m.StructureDiameterSourceLabel = null;
                m.Connections = new List<StormConnection>();
                foreach (DataGridViewRow row in _pipes.Rows)
                {
                    if (row.IsNewRow) continue;
                    string Cell(int i) => Convert.ToString(row.Cells[i].Value, CultureInfo.InvariantCulture)?.Trim() ?? "";
                    if (Enumerable.Range(0, 11).All(i => Cell(i).Length == 0)) continue;
                    StormConnection p = row.Tag is StormConnection prior ? StormInfoModel.Copy(prior) : new StormConnection { ConnectionId = m.Name + "-" + Guid.NewGuid().ToString("N") };
                    string material = StormInfoModel.Code(Cell(4)), specification = Cell(5);
                    if (material != p.MaterialCode || specification != p.PipeSpecification)
                    {
                        p.MaterialSourceLabel = null;
                        p.Extra ??= new();
                        p.Extra["materialSourceReferences"] = System.Text.Json.JsonSerializer.SerializeToElement(Array.Empty<string>());
                        p.Extra["materialStatus"] = System.Text.Json.JsonSerializer.SerializeToElement("manual_entry");
                    }
                    p.Flow = Cell(0); p.Direction = Cell(1).ToUpperInvariant(); p.InvertElevation = StormInfoModel.ReadNumber(Cell(2), "Pipe invert");
                    p.DiameterInches = StormInfoModel.ReadNumber(Cell(3), "Pipe diameter"); p.MaterialCode = material; p.PipeSpecification = specification;
                    p.Material = Cell(6); p.ConnectedStructureName = Cell(7);
                    p.Shape = string.IsNullOrWhiteSpace(Cell(8)) ? "circular" : Cell(8).ToLowerInvariant();
                    p.WidthInches = StormInfoModel.ReadNumber(Cell(9), "Pipe width"); p.HeightInches = StormInfoModel.ReadNumber(Cell(10), "Pipe height"); m.Connections.Add(p);
                }
                c.Structures.RemoveAll(x => x.Name == m.Name); c.Structures.Add(m); StormInfoModel.Validate(c);
                ResultCatalog = c; DialogResult = DialogResult.OK; Close();
            }
            catch (System.Exception e) { MessageBox.Show(this, e.Message, "Check structure data", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
    internal sealed class StormConfirmForm : Form
    {
        internal StormConfirmForm(string title, string details, string acknowledgement)
        {
            Text = title; StartPosition = FormStartPosition.CenterParent; MinimumSize = new SD.Size(740, 480); Size = new SD.Size(960, 650); AutoScaleMode = AutoScaleMode.Dpi;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(10) };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 65)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.Controls.Add(new TextBox { Text = details, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill }, 0, 0);
            var check = new CheckBox { Text = acknowledgement, Dock = DockStyle.Fill };
            var apply = new Button { Text = "Confirm", DialogResult = DialogResult.OK, Enabled = false, AutoSize = true };
            check.CheckedChanged += (_, _) => apply.Enabled = check.Checked;
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill }; buttons.Controls.Add(apply); buttons.Controls.Add(cancel);
            layout.Controls.Add(check, 0, 1); layout.Controls.Add(buttons, 0, 2); Controls.Add(layout); CancelButton = cancel;
        }
    }
}


