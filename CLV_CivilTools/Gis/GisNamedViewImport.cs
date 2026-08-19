using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Gis
{
    public static class GisNamedViewImportCommands
    {
        [CommandMethod("CLV-VIEW-IMPORT", CommandFlags.Modal)]
        [CommandMethod("CLVVIEWIMPORT", CommandFlags.Modal)]
        public static void ImportNamedViewsCommand()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var ed = doc?.Editor;
            if (doc == null || ed == null)
                return;

            try
            {
                using var dialog = new OpenFileDialog
                {
                    Title = "Select Source Drawing or Template",
                    Filter = "Drawing and Template Files (*.dwg;*.dwt)|*.dwg;*.dwt|Drawing Files (*.dwg)|*.dwg|Template Files (*.dwt)|*.dwt|All Files (*.*)|*.*",
                    Multiselect = false,
                    CheckFileExists = true,
                    CheckPathExists = true,
                    RestoreDirectory = true
                };

                if (dialog.ShowDialog() != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.FileName))
                    return;

                var sourcePath = dialog.FileName;
                var targetPath = doc.Name ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(targetPath) &&
                    Path.GetFullPath(sourcePath).Equals(Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
                {
                    ed.WriteMessage("\nVIEW IMPORT: Source file must be different from the active drawing.");
                    return;
                }

                var sourceViews = ReadNamedViews(sourcePath);
                if (sourceViews.Count == 0)
                {
                    ed.WriteMessage("\nVIEW IMPORT: No named views were found in the selected file.");
                    return;
                }

                using var picker = new NamedViewImportForm(sourcePath, sourceViews);
                if (AcadApp.ShowModalDialog(picker) != DialogResult.OK)
                    return;

                var selectedNames = picker.SelectedViewNames;
                if (selectedNames.Count == 0)
                {
                    ed.WriteMessage("\nVIEW IMPORT: No views were selected.");
                    return;
                }

                int imported = 0;
                int overwritten = 0;
                int skipped = 0;

                using (doc.LockDocument())
                {
                    ImportNamedViewsIntoCurrentDrawing(
                        sourcePath,
                        selectedNames,
                        picker.OverwriteExisting,
                        ref imported,
                        ref overwritten,
                        ref skipped);
                }

                ed.WriteMessage(
                    $"\nVIEW IMPORT: Imported {imported} view(s). Overwritten: {overwritten}. Skipped: {skipped}.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nVIEW IMPORT failed: {ex.Message}");
            }
        }

        private static List<string> ReadNamedViews(string sourcePath)
        {
            var viewNames = new List<string>();

            using var sourceDb = new Database(false, true);
            sourceDb.ReadDwgFile(sourcePath, FileOpenMode.OpenForReadAndAllShare, false, null);
            sourceDb.CloseInput(true);

            using var tr = sourceDb.TransactionManager.StartTransaction();
            var vt = (ViewTable)tr.GetObject(sourceDb.ViewTableId, OpenMode.ForRead);

            foreach (ObjectId id in vt)
            {
                var record = tr.GetObject(id, OpenMode.ForRead) as ViewTableRecord;
                if (record == null || record.IsErased || string.IsNullOrWhiteSpace(record.Name))
                    continue;

                viewNames.Add(record.Name);
            }

            tr.Commit();
            return viewNames
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void ImportNamedViewsIntoCurrentDrawing(
            string sourcePath,
            IReadOnlyCollection<string> selectedNames,
            bool overwriteExisting,
            ref int imported,
            ref int overwritten,
            ref int skipped)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            var targetDb = doc.Database;
            var selectedSet = new HashSet<string>(selectedNames, StringComparer.OrdinalIgnoreCase);

            using var sourceDb = new Database(false, true);
            sourceDb.ReadDwgFile(sourcePath, FileOpenMode.OpenForReadAndAllShare, false, null);
            sourceDb.CloseInput(true);

            using var sourceTr = sourceDb.TransactionManager.StartTransaction();
            using var targetTr = targetDb.TransactionManager.StartTransaction();

            var sourceViewTable = (ViewTable)sourceTr.GetObject(sourceDb.ViewTableId, OpenMode.ForRead);
            var targetViewTable = (ViewTable)targetTr.GetObject(targetDb.ViewTableId, OpenMode.ForRead);

            foreach (ObjectId sourceId in sourceViewTable)
            {
                var sourceRecord = sourceTr.GetObject(sourceId, OpenMode.ForRead) as ViewTableRecord;
                if (sourceRecord == null || sourceRecord.IsErased || string.IsNullOrWhiteSpace(sourceRecord.Name))
                    continue;

                if (!selectedSet.Contains(sourceRecord.Name))
                    continue;

                if (targetViewTable.Has(sourceRecord.Name))
                {
                    if (!overwriteExisting)
                    {
                        skipped++;
                        continue;
                    }

                    targetViewTable.UpgradeOpen();
                    var existingId = targetViewTable[sourceRecord.Name];
                    var existing = (ViewTableRecord)targetTr.GetObject(existingId, OpenMode.ForWrite);
                    CopyViewRecord(sourceRecord, existing);
                    overwritten++;
                    imported++;
                    continue;
                }

                targetViewTable.UpgradeOpen();
                var newRecord = new ViewTableRecord();
                CopyViewRecord(sourceRecord, newRecord);
                targetViewTable.Add(newRecord);
                targetTr.AddNewlyCreatedDBObject(newRecord, true);
                imported++;
            }

            targetTr.Commit();
            sourceTr.Commit();
        }

        private static void CopyViewRecord(ViewTableRecord source, ViewTableRecord target)
        {
            target.Name = source.Name;
            target.CenterPoint = source.CenterPoint;
            target.Height = source.Height;
            target.Width = source.Width;
            target.Target = source.Target;
            target.ViewDirection = source.ViewDirection;
            target.ViewTwist = source.ViewTwist;
            target.LensLength = source.LensLength;
            target.PerspectiveEnabled = source.PerspectiveEnabled;
            target.FrontClipEnabled = source.FrontClipEnabled;
            target.FrontClipDistance = source.FrontClipDistance;
            target.BackClipEnabled = source.BackClipEnabled;
            target.BackClipDistance = source.BackClipDistance;
        }
    }

    internal sealed class NamedViewImportForm : Form
    {
        private readonly CheckedListBox _viewsList;
        private readonly CheckBox _overwriteCheckBox;
        public NamedViewImportForm(string sourcePath, IReadOnlyList<string> viewNames)
        {
            Text = "VIEW IMPORT";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(540, 520);

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(12)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Controls.Add(root);

            var titleLabel = new Label
            {
                AutoSize = true,
                Font = new System.Drawing.Font(SystemFonts.DefaultFont, FontStyle.Bold),
                Text = "Import named views into the current drawing"
            };
            root.Controls.Add(titleLabel, 0, 0);

            var summaryLabel = new Label
            {
                AutoSize = true,
                Text = $"Source: {sourcePath}{Environment.NewLine}Select one or more views to import."
            };
            root.Controls.Add(summaryLabel, 0, 1);

            _viewsList = new CheckedListBox
            {
                Dock = DockStyle.Fill,
                CheckOnClick = true,
                IntegralHeight = false,
                HorizontalScrollbar = true
            };

            foreach (var viewName in viewNames)
                _viewsList.Items.Add(viewName, true);

            root.Controls.Add(_viewsList, 0, 2);

            var optionsPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 8)
            };

            var selectAllButton = CreateActionButton("SELECT ALL", (_, _) => SetAllChecked(true));
            var clearButton = CreateActionButton("CLEAR", (_, _) => SetAllChecked(false));
            optionsPanel.Controls.Add(selectAllButton);
            optionsPanel.Controls.Add(clearButton);

            _overwriteCheckBox = new CheckBox
            {
                AutoSize = true,
                Checked = true,
                Text = "Overwrite existing named views with the same name",
                Margin = new Padding(16, 8, 0, 0)
            };
            optionsPanel.Controls.Add(_overwriteCheckBox);

            root.Controls.Add(optionsPanel, 0, 3);

            var footer = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft,
                AutoSize = true
            };

            var importButton = CreateActionButton("IMPORT", (_, _) => ConfirmImport());
            importButton.DialogResult = DialogResult.None;
            var cancelButton = CreateActionButton("CANCEL", (_, _) => Close());
            cancelButton.DialogResult = DialogResult.Cancel;

            footer.Controls.Add(importButton);
            footer.Controls.Add(cancelButton);
            root.Controls.Add(footer, 0, 4);

            AcceptButton = importButton;
            CancelButton = cancelButton;
        }

        public List<string> SelectedViewNames
        {
            get
            {
                return _viewsList.CheckedItems
                    .Cast<object>()
                    .Select(item => item.ToString())
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Cast<string>()
                    .ToList();
            }
        }

        public bool OverwriteExisting => _overwriteCheckBox.Checked;

        private static Button CreateActionButton(string text, EventHandler onClick)
        {
            var button = new Button
            {
                AutoSize = false,
                Width = 120,
                Height = 30,
                Text = text,
                Margin = new Padding(4)
            };
            button.Click += onClick;
            return button;
        }

        private void SetAllChecked(bool isChecked)
        {
            for (int i = 0; i < _viewsList.Items.Count; i++)
                _viewsList.SetItemChecked(i, isChecked);
        }

        private void ConfirmImport()
        {
            if (SelectedViewNames.Count == 0)
            {
                MessageBox.Show(this, "Select at least one named view to import.", "VIEW IMPORT", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
