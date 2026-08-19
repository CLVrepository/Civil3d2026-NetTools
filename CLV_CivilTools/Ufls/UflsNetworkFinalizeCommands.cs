using System;
using System.Collections.Generic;
using DrawingFont = System.Drawing.Font;
using DrawingFontStyle = System.Drawing.FontStyle;
using DrawingSize = System.Drawing.Size;
using System.Reflection;
using System.Windows.Forms;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Civil.DatabaseServices;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDbObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using Forms = System.Windows.Forms;

namespace CLV_CivilTools.Ufls
{
    public static class UflsNetworkFinalizeCommands
    {
        private static readonly QualityOption[] QualityOptions =
        {
            new("QL-A (H0.3/V0.3)", "Final Location Maps provided by Developer/Contractor Surveyor"),
            new("QL-A (H0.1/V0.3)", "UFLS with single shot RTK GPS elevations"),
            new("QL-A (H0.1/V0.1)", "CLV projects with design quality measurements")
        };

        [CommandMethod("UFLS", "UFLS-FINALIZE-QUALITY", CommandFlags.Modal)]
        public static void AssignQualityDescription()
        {
            using var dlg = new QualityLevelDialog(QualityOptions);
            DialogResult result = AcadApp.ShowModalDialog(dlg);
            if (result != DialogResult.OK || string.IsNullOrWhiteSpace(dlg.SelectedQualityText))
                return;

            ApplyQualityToAllParts(dlg.SelectedQualityText);
        }

        private static void ApplyQualityToAllParts(string descriptionText)
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            int pipeCount = 0;
            int structureCount = 0;
            int skippedCount = 0;
            int descriptionUpdatedCount = 0;
            var warnings = new List<string>();

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    if (id.IsNull || id.IsErased)
                        continue;

                    AcDbObject? obj = null;
                    try
                    {
                        obj = tr.GetObject(id, OpenMode.ForRead, false);
                    }
                    catch (System.Exception ex)
                    {
                        skippedCount++;
                        AddWarning(warnings, $"Could not open object {id.Handle}: {ex.Message}");
                        continue;
                    }

                    bool isPipe = obj is Pipe;
                    bool isStructure = obj is Structure;
                    if (!isPipe && !isStructure)
                        continue;

                    obj.UpgradeOpen();

                    if (isPipe)
                        pipeCount++;
                    else
                        structureCount++;

                    if (TrySetStringProperty(obj, "Description", descriptionText, out string? descWarning))
                    {
                        descriptionUpdatedCount++;
                    }
                    else
                    {
                        AddWarning(warnings, $"{ObjectLabel(obj)}: {descWarning ?? "Description property was not writable."}");
                    }
                }

                tr.Commit();
            }

            ed.WriteMessage("\nUFLS-FINALIZE-QUALITY complete.");
            ed.WriteMessage($"\n  Pipes processed        : {pipeCount}");
            ed.WriteMessage($"\n  Structures processed   : {structureCount}");
            ed.WriteMessage($"\n  Skipped objects        : {skippedCount}");
            ed.WriteMessage($"\n  Descriptions updated   : {descriptionUpdatedCount}");

            foreach (string warning in warnings)
                ed.WriteMessage($"\n  Warning: {warning}");
        }

        private static bool TrySetStringProperty(object obj, string propertyName, string value, out string? warning)
        {
            warning = null;
            PropertyInfo? prop = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (prop == null)
            {
                warning = $"{propertyName} property was not found.";
                return false;
            }

            if (!prop.CanWrite)
            {
                warning = $"{propertyName} property is read-only.";
                return false;
            }

            try
            {
                if (prop.PropertyType == typeof(string))
                {
                    prop.SetValue(obj, value);
                    return true;
                }

                object? converted = Convert.ChangeType(value, prop.PropertyType, System.Globalization.CultureInfo.InvariantCulture);
                prop.SetValue(obj, converted);
                return true;
            }
            catch (System.Exception ex)
            {
                warning = $"Could not set {propertyName}: {ex.Message}";
                return false;
            }
        }

        private static string ObjectLabel(AcDbObject obj)
        {
            string name = TryGetStringProperty(obj, "Name");
            if (!string.IsNullOrWhiteSpace(name))
                return name;

            return obj.ObjectId.IsNull ? obj.GetType().Name : $"{obj.GetType().Name} {obj.ObjectId.Handle}";
        }

        private static string TryGetStringProperty(object obj, string propertyName)
        {
            try
            {
                PropertyInfo? prop = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                return prop?.GetValue(obj)?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static void AddWarning(List<string> warnings, string text)
        {
            if (warnings.Count < 20)
                warnings.Add(text);
            else if (warnings.Count == 20)
                warnings.Add("Additional warnings suppressed.");
        }

        private sealed record QualityOption(string Text, string Detail);

        private sealed class QualityLevelDialog : Form
        {
            private readonly RadioButton[] _radioButtons;

            public string? SelectedQualityText { get; private set; }

            public QualityLevelDialog(IReadOnlyList<QualityOption> options)
            {
                Text = "Assign UFLS Quality";
                StartPosition = FormStartPosition.CenterParent;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                ClientSize = new DrawingSize(560, 265);

                var main = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 4,
                    Padding = new Padding(12)
                };
                main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                main.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                main.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                var header = new Forms.Label
                {
                    AutoSize = true,
                    Text = "Choose the quality level to write to the Civil 3D pipe/structure Description property:",
                    Font = new DrawingFont(Font, DrawingFontStyle.Bold),
                    Margin = new Padding(0, 0, 0, 8)
                };
                main.Controls.Add(header, 0, 0);

                var group = new GroupBox
                {
                    Dock = DockStyle.Top,
                    Text = "Quality Level",
                    Height = 140
                };

                var stack = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = Forms.FlowDirection.TopDown,
                    WrapContents = false,
                    Padding = new Padding(8)
                };

                _radioButtons = new RadioButton[options.Count];
                for (int i = 0; i < options.Count; i++)
                {
                    QualityOption option = options[i];
                    var rb = new RadioButton
                    {
                        AutoSize = true,
                        Width = 510,
                        Text = $"{option.Text}  —  {option.Detail}",
                        Tag = option.Text,
                        Checked = i == 0,
                        Margin = new Padding(3, 6, 3, 2)
                    };
                    _radioButtons[i] = rb;
                    stack.Controls.Add(rb);
                }

                group.Controls.Add(stack);
                main.Controls.Add(group, 0, 1);
                main.Controls.Add(new Forms.Label { AutoSize = true }, 0, 2);

                var buttons = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = Forms.FlowDirection.RightToLeft,
                    AutoSize = true
                };

                var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90 };
                var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
                ok.Click += (_, _) => SelectedQualityText = GetSelectedQualityText();
                buttons.Controls.Add(ok);
                buttons.Controls.Add(cancel);

                AcceptButton = ok;
                CancelButton = cancel;
                main.Controls.Add(buttons, 0, 3);

                Controls.Add(main);
            }

            private string? GetSelectedQualityText()
            {
                foreach (RadioButton rb in _radioButtons)
                {
                    if (rb.Checked)
                        return rb.Tag?.ToString();
                }

                return null;
            }
        }
    }
}
