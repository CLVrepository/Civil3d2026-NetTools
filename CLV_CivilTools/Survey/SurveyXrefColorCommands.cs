using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcException = Autodesk.AutoCAD.Runtime.Exception;
using CadColor = Autodesk.AutoCAD.Colors.Color;
using DrawingColor = System.Drawing.Color;

namespace CLV_CivilTools.Survey
{
    public static class SurveyXrefColorCommands
    {
        private const int TransparentPercent = 70;

        private static readonly IReadOnlyList<XrefColorChoice> ColorChoices = new[]
        {
            new XrefColorChoice("R", "Red", "Red", 1, 0, false, false),
            new XrefColorChoice("R70", "Red70", "Red 70%", 1, TransparentPercent, false, false),
            new XrefColorChoice("Y", "Yellow", "Yellow", 2, 0, false, false),
            new XrefColorChoice("Y70", "Yellow70", "Yellow 70%", 2, TransparentPercent, false, false),
            new XrefColorChoice("GN", "Green", "Green", 3, 0, false, false),
            new XrefColorChoice("GN70", "Green70", "Green 70%", 3, TransparentPercent, false, false),
            new XrefColorChoice("C", "Cyan", "Cyan", 4, 0, false, false),
            new XrefColorChoice("C70", "Cyan70", "Cyan 70%", 4, TransparentPercent, false, false),
            new XrefColorChoice("M", "Magenta", "Magenta", 6, 0, false, false),
            new XrefColorChoice("M70", "Magenta70", "Magenta 70%", 6, TransparentPercent, false, false),
            new XrefColorChoice("G", "Gray", "Gray", 252, 0, false, false),
            new XrefColorChoice("G70", "Gray70", "Gray 70%", 252, TransparentPercent, false, false),
            new XrefColorChoice("RESET", "Reset", "RESET", 0, 0, true, false),
            new XrefColorChoice("RESETALL", "ResetAll", "RESET ALL", 0, 0, false, true)
        };

        [CommandMethod("SURVEY-XREF-COLOR", CommandFlags.Modal)]
        [CommandMethod("XREFCOLOR", CommandFlags.Modal)]
        public static void ApplyXrefColorOverride()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                XrefColorChoice? choice = PromptForChoice(ed);
                if (choice == null)
                    return;

                if (choice.Value.IsResetAll)
                {
                    DialogResult confirmResetAll = MessageBox.Show(
                        "Reset layer color/transparency overrides for ALL attached/overlaid xrefs in the current drawing?",
                        "XREF COLOR - Reset All",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);

                    if (confirmResetAll != DialogResult.Yes)
                        return;

                    using DocumentLock resetAllDocLock = doc.LockDocument();
                    using Transaction resetAllTr = db.TransactionManager.StartTransaction();

                    ResetAllResult resetAllResult = ResetAllLayerOverridesToSource(db, resetAllTr);
                    resetAllTr.Commit();
                    ed.Regen();

                    ed.WriteMessage(
                        $"\nSURVEY-XREF-COLOR: Reset {resetAllResult.UpdatedLayerCount} layer(s) across {resetAllResult.ResetXrefCount} xref(s)." +
                        "\nThis changes host drawing xref layer overrides only; it does not edit source xref DWGs.");

                    if (resetAllResult.SkippedXrefs.Count > 0)
                        ed.WriteMessage($"\nSkipped xref(s): {string.Join(", ", resetAllResult.SkippedXrefs)}");

                    return;
                }

                PromptEntityOptions peo = new PromptEntityOptions("\nSelect xref to update/reset: ");
                peo.SetRejectMessage("\nSelect an xref block reference.");
                peo.AddAllowedClass(typeof(BlockReference), exactMatch: true);

                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                    return;

                using DocumentLock docLock = doc.LockDocument();
                using Transaction tr = db.TransactionManager.StartTransaction();

                BlockReference br = (BlockReference)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                BlockTableRecord xrefBtr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);

                if (!xrefBtr.IsFromExternalReference && !xrefBtr.IsFromOverlayReference)
                {
                    ed.WriteMessage("\nSURVEY-XREF-COLOR: Selected block is not an attached or overlaid xref.");
                    return;
                }

                string xrefName = xrefBtr.Name;
                int updatedLayerCount = choice.Value.IsReset
                    ? ResetLayerOverridesToSource(db, tr, xrefBtr, xrefName)
                    : ApplyLayerOverrides(db, tr, xrefName, choice.Value);

                tr.Commit();
                ed.Regen();

                string actionMessage = choice.Value.IsReset
                    ? $"Reset {updatedLayerCount} xref layer(s) for '{xrefName}' back to the source DWG layer color/transparency."
                    : $"Applied {choice.Value.DisplayName} override to {updatedLayerCount} xref layer(s) for '{xrefName}'.";

                ed.WriteMessage(
                    $"\nSURVEY-XREF-COLOR: {actionMessage}" +
                    "\nThis changes host drawing xref layer overrides only; it does not edit the source xref DWG.");
            }
            catch (AcException ex)
            {
                ed.WriteMessage($"\nSURVEY-XREF-COLOR failed: {ex.Message}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nSURVEY-XREF-COLOR failed: {ex.Message}");
            }
        }

        private static XrefColorChoice? PromptForChoice(Editor ed)
        {
            using XrefColorChoiceDialog dialog = new XrefColorChoiceDialog(ColorChoices);
            DialogResult result = AcadApp.ShowModalDialog(dialog);
            if (result != DialogResult.OK)
                return null;

            XrefColorChoice? selectedChoice = dialog.SelectedChoice;
            if (selectedChoice == null)
                ed.WriteMessage("\nSURVEY-XREF-COLOR: No color option was selected.");

            return selectedChoice;
        }

        private static int ApplyLayerOverrides(Database db, Transaction tr, string xrefName, XrefColorChoice choice)
        {
            LayerTable layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            string prefix = xrefName + "|";
            int updated = 0;
            CadColor color = CadColor.FromColorIndex(ColorMethod.ByAci, choice.AciColorIndex);
            Transparency transparency = TransparencyFromPercent(choice.TransparencyPercent);

            foreach (ObjectId layerId in layerTable)
            {
                LayerTableRecord layer = (LayerTableRecord)tr.GetObject(layerId, OpenMode.ForRead);
                if (!layer.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!layer.IsWriteEnabled)
                    layer.UpgradeOpen();

                layer.Color = color;
                layer.Transparency = transparency;
                updated++;
            }

            return updated;
        }


        private static int ResetLayerOverridesToSource(Database hostDb, Transaction hostTr, BlockTableRecord xrefBtr, string xrefName)
        {
            string sourcePath = ResolveXrefPath(hostDb, xrefBtr.PathName);
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                throw new InvalidOperationException($"Unable to locate source xref DWG for '{xrefName}'. Path stored in drawing: '{xrefBtr.PathName}'.");

            Dictionary<string, XrefLayerSourceProperties> sourceLayers = ReadSourceLayerProperties(sourcePath);
            LayerTable layerTable = (LayerTable)hostTr.GetObject(hostDb.LayerTableId, OpenMode.ForRead);
            string prefix = xrefName + "|";
            int updated = 0;

            foreach (ObjectId layerId in layerTable)
            {
                LayerTableRecord layer = (LayerTableRecord)hostTr.GetObject(layerId, OpenMode.ForRead);
                if (!layer.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                string sourceLayerName = layer.Name.Substring(prefix.Length);
                if (!sourceLayers.TryGetValue(sourceLayerName, out XrefLayerSourceProperties sourceProperties))
                    continue;

                if (!layer.IsWriteEnabled)
                    layer.UpgradeOpen();

                layer.Color = sourceProperties.Color;
                layer.Transparency = sourceProperties.Transparency;
                updated++;
            }

            return updated;
        }

        private static ResetAllResult ResetAllLayerOverridesToSource(Database hostDb, Transaction hostTr)
        {
            BlockTable blockTable = (BlockTable)hostTr.GetObject(hostDb.BlockTableId, OpenMode.ForRead);
            int updatedLayerCount = 0;
            int resetXrefCount = 0;
            List<string> skippedXrefs = new List<string>();

            foreach (ObjectId btrId in blockTable)
            {
                BlockTableRecord btr = (BlockTableRecord)hostTr.GetObject(btrId, OpenMode.ForRead);
                if (!btr.IsFromExternalReference && !btr.IsFromOverlayReference)
                    continue;

                string xrefName = btr.Name;
                try
                {
                    int xrefUpdated = ResetLayerOverridesToSource(hostDb, hostTr, btr, xrefName);
                    updatedLayerCount += xrefUpdated;
                    resetXrefCount++;
                }
                catch (System.Exception ex)
                {
                    skippedXrefs.Add($"{xrefName} ({ex.Message})");
                }
            }

            return new ResetAllResult(updatedLayerCount, resetXrefCount, skippedXrefs);
        }

        private static Dictionary<string, XrefLayerSourceProperties> ReadSourceLayerProperties(string sourcePath)
        {
            Dictionary<string, XrefLayerSourceProperties> layers = new Dictionary<string, XrefLayerSourceProperties>(StringComparer.OrdinalIgnoreCase);

            using Database sourceDb = new Database(false, true);
            sourceDb.ReadDwgFile(sourcePath, FileShare.ReadWrite, true, string.Empty);
            sourceDb.CloseInput(true);

            using Transaction sourceTr = sourceDb.TransactionManager.StartTransaction();
            LayerTable sourceLayerTable = (LayerTable)sourceTr.GetObject(sourceDb.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId sourceLayerId in sourceLayerTable)
            {
                LayerTableRecord sourceLayer = (LayerTableRecord)sourceTr.GetObject(sourceLayerId, OpenMode.ForRead);
                layers[sourceLayer.Name] = new XrefLayerSourceProperties(sourceLayer.Color, sourceLayer.Transparency);
            }

            sourceTr.Commit();
            return layers;
        }

        private static string ResolveXrefPath(Database hostDb, string xrefPath)
        {
            if (string.IsNullOrWhiteSpace(xrefPath))
                return string.Empty;

            if (File.Exists(xrefPath))
                return Path.GetFullPath(xrefPath);

            if (!Path.IsPathRooted(xrefPath) && !string.IsNullOrWhiteSpace(hostDb.Filename))
            {
                string? hostDirectory = Path.GetDirectoryName(hostDb.Filename);
                if (!string.IsNullOrWhiteSpace(hostDirectory))
                {
                    string relativeToHost = Path.GetFullPath(Path.Combine(hostDirectory, xrefPath));
                    if (File.Exists(relativeToHost))
                        return relativeToHost;
                }
            }

            return xrefPath;
        }

        private static Transparency TransparencyFromPercent(int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 90) percent = 90;

            byte alpha = (byte)Math.Max(0, 255 - (percent * 255 / 100));
            return new Transparency(alpha);
        }

        private sealed class XrefColorChoiceDialog : Form
        {
            public XrefColorChoice? SelectedChoice { get; private set; }

            public XrefColorChoiceDialog(IReadOnlyList<XrefColorChoice> choices)
            {
                Text = "XREF COLOR";
                StartPosition = FormStartPosition.CenterScreen;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;
                ClientSize = new System.Drawing.Size(430, 500);

                TableLayoutPanel mainLayout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 4,
                    Padding = new Padding(10)
                };
                mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

                Label titleLabel = new Label
                {
                    Text = "Choose xref color override",
                    AutoSize = true,
                    Dock = DockStyle.Fill,
                    Padding = new Padding(0, 0, 0, 8)
                };
                mainLayout.Controls.Add(titleLabel, 0, 0);

                TableLayoutPanel colorLayout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 2,
                    RowCount = 7,
                    AutoSize = false
                };
                colorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
                colorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
                colorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
                for (int rowIndex = 1; rowIndex <= 6; rowIndex++)
                    colorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));

                Label standardHeader = new Label
                {
                    Text = "Standard",
                    Dock = DockStyle.Fill,
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                    AutoSize = false,
                    Height = 24
                };
                Label transparentHeader = new Label
                {
                    Text = "70% Transparent",
                    Dock = DockStyle.Fill,
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                    AutoSize = false,
                    Height = 24
                };
                colorLayout.Controls.Add(standardHeader, 0, 0);
                colorLayout.Controls.Add(transparentHeader, 1, 0);

                AddChoiceButtonRow(colorLayout, choices, 1, "R", "R70", DrawingColor.LightCoral);
                AddChoiceButtonRow(colorLayout, choices, 2, "Y", "Y70", DrawingColor.Khaki);
                AddChoiceButtonRow(colorLayout, choices, 3, "GN", "GN70", DrawingColor.LightGreen);
                AddChoiceButtonRow(colorLayout, choices, 4, "C", "C70", DrawingColor.LightCyan);
                AddChoiceButtonRow(colorLayout, choices, 5, "M", "M70", DrawingColor.Plum);
                AddChoiceButtonRow(colorLayout, choices, 6, "G", "G70", DrawingColor.Gainsboro);
                mainLayout.Controls.Add(colorLayout, 0, 1);

                TableLayoutPanel resetLayout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 2,
                    RowCount = 1,
                    Padding = new Padding(0, 8, 0, 0)
                };
                resetLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
                resetLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
                resetLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
                resetLayout.Controls.Add(CreateChoiceButton(GetChoice(choices, "RESET"), DrawingColor.WhiteSmoke), 0, 0);
                resetLayout.Controls.Add(CreateChoiceButton(GetChoice(choices, "RESETALL"), DrawingColor.WhiteSmoke), 1, 0);
                mainLayout.Controls.Add(resetLayout, 0, 2);

                Button cancelButton = new Button
                {
                    Text = "Cancel",
                    Dock = DockStyle.Right,
                    Width = 100,
                    DialogResult = DialogResult.Cancel
                };
                mainLayout.Controls.Add(cancelButton, 0, 3);
                CancelButton = cancelButton;

                Controls.Add(mainLayout);
            }

            private void AddChoiceButtonRow(TableLayoutPanel layout, IReadOnlyList<XrefColorChoice> choices, int row, string standardKeyword, string transparentKeyword, DrawingColor backColor)
            {
                layout.Controls.Add(CreateChoiceButton(GetChoice(choices, standardKeyword), backColor), 0, row);
                layout.Controls.Add(CreateChoiceButton(GetChoice(choices, transparentKeyword), backColor), 1, row);
            }

            private Button CreateChoiceButton(XrefColorChoice choice, DrawingColor backColor)
            {
                Button button = new Button
                {
                    Text = choice.DisplayName,
                    Dock = DockStyle.Fill,
                    Height = 34,
                    Margin = new Padding(4),
                    BackColor = backColor,
                    Tag = choice
                };

                button.Click += (_, _) =>
                {
                    if (button.Tag is XrefColorChoice selectedChoice)
                    {
                        SelectedChoice = selectedChoice;
                        DialogResult = DialogResult.OK;
                        Close();
                    }
                };

                return button;
            }

            private static XrefColorChoice GetChoice(IReadOnlyList<XrefColorChoice> choices, string keyword)
            {
                foreach (XrefColorChoice choice in choices)
                {
                    if (string.Equals(choice.Keyword, keyword, StringComparison.OrdinalIgnoreCase))
                        return choice;
                }

                throw new InvalidOperationException($"Missing XREF COLOR choice '{keyword}'.");
            }
        }

        private readonly record struct XrefColorChoice(
            string Keyword,
            string MenuName,
            string DisplayName,
            short AciColorIndex,
            int TransparencyPercent,
            bool IsReset,
            bool IsResetAll);

        private readonly record struct XrefLayerSourceProperties(
            CadColor Color,
            Transparency Transparency);

        private readonly record struct ResetAllResult(
            int UpdatedLayerCount,
            int ResetXrefCount,
            List<string> SkippedXrefs);
    }
}
