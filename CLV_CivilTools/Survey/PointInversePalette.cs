using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace CLV_CivilTools.Survey
{
    internal sealed class PointInversePalette : UserControl
    {
        private readonly DataGridView grid;
        private readonly System.Windows.Forms.Label status;
        private readonly FlowLayoutPanel actions;

        internal PointInversePalette()
        {
            Dock = DockStyle.Fill;
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            status = new System.Windows.Forms.Label
            {
                AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(8),
                Text = "Pick record/reference first, surveyed/comparison second.\nDifferences = TO minus FROM. Number entry is available for overlapping points."
            };
            grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoGenerateColumns = false,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells,
                SelectionMode = DataGridViewSelectionMode.CellSelect, MultiSelect = false,
                BackgroundColor = SystemColors.Window, ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableAlwaysIncludeHeaderText
            };
            grid.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Information", FillWeight = 43, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "FROM / Result", FillWeight = 29, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "TO", FillWeight = 28, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(5), WrapContents = true };
            AddButton("PICK NEW PAIR", "SURVEY-POINT-INVERSE");
            AddButton("KEEP FROM / PICK NEXT", "CLV-POINT-INVERSE-NEXT");
            AddButton("REVERSE", "CLV-POINT-INVERSE-REVERSE");
            AddButton("REFRESH", "CLV-POINT-INVERSE-REFRESH");
            AddButton("COPY REPORT", "CLV-POINT-INVERSE-COPY");
            AddButton("INSERT TABLE", "CLV-POINT-INVERSE-TABLE");
            AddButton("CLEAR", "CLV-POINT-INVERSE-CLEAR");
            layout.Controls.Add(status, 0, 0);
            layout.Controls.Add(grid, 0, 1);
            layout.Controls.Add(actions, 0, 2);
            Controls.Add(layout);
        }

        private void AddButton(string text, string command)
        {
            var button = new Button { Text = text, AutoSize = true, MinimumSize = new Size(120, 28) };
            button.Click += (_, _) => PointInverseCommands.Queue(command);
            actions.Controls.Add(button);
        }

        internal void Busy(bool busy) => actions.Enabled = !busy;

        internal void Display(IReadOnlyList<InverseRow>? rows, string message)
        {
            grid.Rows.Clear();
            if (rows != null)
                foreach (InverseRow row in rows)
                    grid.Rows.Add(row.Label, row.From, row.To);
            status.Text = message;
        }
    }
}
