using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using SD = System.Drawing;

namespace CLV_CivilTools.Ufls.SewerInfo
{
    internal sealed class SewerCatalogForm : Form
    {
        internal string ActionName { get; private set; } = "";
        internal string? SelectedName => _grid.CurrentRow?.Tag is SewerManhole m ? m.Name : null;
        private readonly DataGridView _grid;
        internal SewerCatalogForm(SewerCatalog? c)
        {
            Text = "SEWER MANHOLE INFO - " + (c?.ProjectNumber ?? "No catalog imported");
            StartPosition = FormStartPosition.CenterParent; MinimumSize = new SD.Size(820, 540);
            Size = new SD.Size(1000, 690); AutoScaleMode = AutoScaleMode.Dpi;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Padding = new Padding(10) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Select a record to place or edit. All elevations are feet; all diameters are inches.\nImport JSON or add records manually. Transfer uses a placed block and an explicit pipe mapping." }, 0, 0);
            _grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false };
            foreach (string col in new[] { "Number", "Name", "Rim (ft)", "Structure (in)", "Connections", "Status" }) _grid.Columns.Add(col, col);
            if (c != null) foreach (SewerManhole m in c.Manholes.OrderBy(m => m.Number))
            {
                int row = _grid.Rows.Add(m.Number, m.Name, SewerInfoModel.Elevation(m.RimElevation), SewerInfoModel.Number(m.StructureDiameterInches), m.Connections.Count, (m.ReviewStatus ?? "needs review").Replace('_', ' '));
                _grid.Rows[row].Tag = m;
            }
            var details = new TextBox { Dock = DockStyle.Fill, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false };
            _grid.SelectionChanged += (_, _) => details.Text = _grid.CurrentRow?.Tag is SewerManhole m ? SewerInfoModel.Serialize(m) : "";
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
            if (_grid.Rows.Count > 0) { _grid.CurrentCell = _grid.Rows[0].Cells[0]; details.Text = SewerInfoModel.Serialize((SewerManhole)_grid.Rows[0].Tag!); }
        }
    }
    internal sealed class SewerEditForm : Form
    {
        private readonly TextBox _project = new(), _number = new(), _rim = new(), _diameter = new(), _station = new(), _basis = new(), _notes = new();
        private readonly DataGridView _pipes = new();
        private readonly SewerCatalog? _catalog;
        private readonly SewerManhole? _existing;
        internal SewerCatalog? ResultCatalog { get; private set; }
        internal SewerEditForm(SewerCatalog? catalog, SewerManhole? existing)
        {
            _catalog = catalog; _existing = existing;
            Text = existing == null ? "ADD SEWER MANHOLE" : "EDIT " + existing.Name;
            StartPosition = FormStartPosition.CenterParent; MinimumSize = new SD.Size(900, 620); Size = new SD.Size(1100, 740); AutoScaleMode = AutoScaleMode.Dpi;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Padding = new Padding(10) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 145)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 4 };
            for (int i = 0; i < 4; i++) fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, i % 2 == 0 ? 20 : 30));
            void Field(string title, TextBox box, int col, int row) { fields.Controls.Add(new Label { Text = title, AutoSize = true }, col, row); box.Dock = DockStyle.Fill; fields.Controls.Add(box, col + 1, row); }
            Field("Project", _project, 0, 0); Field("Manhole number", _number, 2, 0);
            Field("Rim elevation (ft)", _rim, 0, 1); Field("Structure diameter (in)", _diameter, 2, 1);
            Field("Station (reference)", _station, 0, 2); Field("Diameter basis", _basis, 2, 2);
            fields.Controls.Add(new Label { Text = "Name is generated as project-SSMH-01. Blank numeric fields remain UNKNOWN. Editing a record requires refreshing its placed blocks.", Dock = DockStyle.Fill }, 0, 3); fields.SetColumnSpan(fields.GetControlFromPosition(0, 3)!, 4);
            _project.Text = catalog?.ProjectNumber ?? ""; _project.ReadOnly = catalog != null;
            _number.Text = existing?.Number.ToString(CultureInfo.InvariantCulture) ?? ""; _number.ReadOnly = existing != null;
            _rim.Text = SewerInfoModel.Number(existing?.RimElevation); _diameter.Text = SewerInfoModel.Number(existing?.StructureDiameterInches);
            _station.Text = existing?.Station ?? ""; _basis.Text = existing?.StructureDiameterBasis is null or "base_as_labeled" ? "BASE" : existing.StructureDiameterBasis; _notes.Text = existing?.Notes ?? "";
            layout.Controls.Add(fields, 0, 0);
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "PIPE CONNECTIONS - one row per connected pipe; include multiple IN pipes. Delete selected rows with Delete.\nMaterial code: C900, PVC, RCP, SDR35 or your project designation. Specification and material family remain separate." }, 0, 1);
            _pipes.Dock = DockStyle.Fill; _pipes.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _pipes.RowHeadersVisible = true; _pipes.AllowUserToAddRows = true; _pipes.AllowUserToDeleteRows = true;
            _pipes.Columns.Add(new DataGridViewComboBoxColumn { Name = "Flow", HeaderText = "Flow", DataSource = new[] { "in", "out" } });
            foreach (string col in new[] { "Direction", "Invert ft", "Diameter in", "Material code", "Specification", "Material family", "To/From MH" }) _pipes.Columns.Add(col, col);
            if (existing != null) foreach (SewerConnection p in existing.Connections)
            {
                int row = _pipes.Rows.Add(p.Flow, p.Direction, SewerInfoModel.Number(p.InvertElevation), SewerInfoModel.Number(p.DiameterInches), p.MaterialCode ?? "", p.PipeSpecification ?? "", p.Material ?? "", p.ConnectedManholeName ?? "");
                _pipes.Rows[row].Tag = p;
            }
            layout.Controls.Add(_pipes, 0, 2); _notes.Multiline = true; _notes.Dock = DockStyle.Fill; _notes.ScrollBars = ScrollBars.Vertical;
            var notesPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 }; notesPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70)); notesPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
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
                SewerCatalog c = _catalog == null ? new SewerCatalog { ProjectNumber = _project.Text.Trim().ToUpperInvariant() } : SewerInfoModel.Copy(_catalog);
                if (!int.TryParse(_number.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int number)) throw new ArgumentException("Enter a positive whole-number manhole number.");
                if (_existing == null && c.Manholes.Any(m => m.Number == number)) throw new ArgumentException("That number already exists. Use Edit.");
                SewerManhole m = _existing == null ? new SewerManhole() : SewerInfoModel.Copy(_existing);
                m.Number = number; m.Name = SewerInfoModel.Name(c.ProjectNumber, number); m.SourceId = string.IsNullOrWhiteSpace(m.SourceId) ? "SSMH#" + number : m.SourceId;
                m.RimElevation = SewerInfoModel.ReadNumber(_rim.Text, "Rim"); m.StructureDiameterInches = SewerInfoModel.ReadNumber(_diameter.Text, "Structure diameter");
                m.StructureDiameterBasis = string.Equals(_basis.Text.Trim(), "BASE", StringComparison.OrdinalIgnoreCase) ? "base_as_labeled" : _basis.Text.Trim(); m.Station = _station.Text.Trim(); m.Notes = _notes.Text;
                m.ReviewStatus = "manually_edited_needs_review"; m.Completeness = "manual";
                if (_existing?.StructureDiameterInches != m.StructureDiameterInches) m.StructureDiameterSourceLabel = null;
                m.Connections = new List<SewerConnection>();
                foreach (DataGridViewRow row in _pipes.Rows)
                {
                    if (row.IsNewRow) continue;
                    string Cell(int i) => Convert.ToString(row.Cells[i].Value, CultureInfo.InvariantCulture)?.Trim() ?? "";
                    if (Enumerable.Range(0, 8).All(i => Cell(i).Length == 0)) continue;
                    SewerConnection p = row.Tag is SewerConnection prior ? SewerInfoModel.Copy(prior) : new SewerConnection { ConnectionId = m.Name + "-" + Guid.NewGuid().ToString("N") };
                    string material = SewerInfoModel.Code(Cell(4)), specification = Cell(5);
                    if (material != p.MaterialCode || specification != p.PipeSpecification)
                    {
                        p.MaterialSourceLabel = null;
                        p.Extra ??= new();
                        p.Extra["materialSourceReferences"] = System.Text.Json.JsonSerializer.SerializeToElement(Array.Empty<string>());
                        p.Extra["materialStatus"] = System.Text.Json.JsonSerializer.SerializeToElement("manual_entry");
                    }
                    p.Flow = Cell(0); p.Direction = Cell(1).ToUpperInvariant(); p.InvertElevation = SewerInfoModel.ReadNumber(Cell(2), "Pipe invert");
                    p.DiameterInches = SewerInfoModel.ReadNumber(Cell(3), "Pipe diameter"); p.MaterialCode = material; p.PipeSpecification = specification;
                    p.Material = Cell(6); p.ConnectedManholeName = Cell(7); m.Connections.Add(p);
                }
                c.Manholes.RemoveAll(x => x.Number == number); c.Manholes.Add(m); SewerInfoModel.Validate(c);
                ResultCatalog = c; DialogResult = DialogResult.OK; Close();
            }
            catch (System.Exception e) { MessageBox.Show(this, e.Message, "Check manhole data", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
    }
    internal sealed class SewerMapForm : Form
    {
        private sealed record Choice(int Index, string Label);
        private readonly DataGridView _grid = new();
        private readonly SewerManhole _mh;
        internal SewerInfoModel.AssignmentPlan? MappingPlan { get; private set; }
        internal SewerMapForm(SewerManhole mh, string[] pipeLabels)
        {
            _mh = mh; Text = "MATCH EXISTING SEWER PIPES - " + mh.Name;
            StartPosition = FormStartPosition.CenterParent; MinimumSize = new SD.Size(900, 420); Size = new SD.Size(1200, 560); AutoScaleMode = AutoScaleMode.Dpi;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(10) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 75)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
            layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Plan connections: " + mh.Connections.Count + "    Existing connected pipes: " + pipeLabels.Length
                + "\nChoose the correct plan connection for each existing pipe, or explicitly leave that pipe unchanged.\nPlan connections without a modeled pipe stay pending. Name and rim are included in the final transfer preview." }, 0, 0);
            _grid.Dock = DockStyle.Fill; _grid.AllowUserToAddRows = false; _grid.AllowUserToDeleteRows = false; _grid.RowHeadersVisible = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
            _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Native", HeaderText = "Existing Civil pipe / connected endpoint / current invert", ReadOnly = true, FillWeight = 45 });
            var choices = new List<Choice> { new Choice(-1, "Leave this existing pipe unchanged") };
            choices.AddRange(mh.Connections.Select((c, i) => new Choice(i, (i + 1) + ": " + SewerInfoModel.PipeLabel(c))));
            _grid.Columns.Add(new DataGridViewComboBoxColumn { Name = "Plan", HeaderText = "Plan connection to apply", DataSource = choices, DisplayMember = "Label", ValueMember = "Index", FillWeight = 55, DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton });
            foreach (string label in pipeLabels) _grid.Rows.Add(label);
            _grid.CurrentCellDirtyStateChanged += (_, _) => { if (_grid.IsCurrentCellDirty) _grid.CommitEdit(DataGridViewDataErrorContexts.Commit); };
            layout.Controls.Add(_grid, 0, 1);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill };
            var next = new Button { Text = "Review transfer", AutoSize = true };
            next.Click += (_, _) =>
            {
                try
                {
                    _grid.EndEdit();
                    MappingPlan = SewerInfoModel.PlanAssignments(_mh.Connections, _grid.Rows.Cast<DataGridViewRow>().Select(r => r.Cells[1].Value is int i ? (int?)i : null).ToArray());
                    DialogResult = DialogResult.OK; Close();
                }
                catch (System.Exception e) { MessageBox.Show(this, e.Message, "Match existing pipes", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true }; CancelButton = cancel;
            buttons.Controls.Add(next); buttons.Controls.Add(cancel); layout.Controls.Add(buttons, 0, 2); Controls.Add(layout);
        }
    }
    internal sealed class SewerConfirmForm : Form
    {
        internal SewerConfirmForm(string title, string details, string acknowledgement)
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

