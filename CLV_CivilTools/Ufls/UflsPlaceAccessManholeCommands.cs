using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using WinForms = System.Windows.Forms;

namespace CLV_CivilTools.Ufls
{
    public static class UflsPlaceAccessManholeCommands
    {
        private const double TypeIMinimum48 = 4.330;
        private const double TypeIMinimum60 = 6.167;

        [CommandMethod("UFLS", "UFLS-PLACE-ACCESS-MH", CommandFlags.Modal)]
        public static void PlaceAccessManhole()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                ObjectId boxId;
                string boxName;
                ObjectId networkId;
                double boxRimElevation;

                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    PromptEntityOptions boxPrompt = new PromptEntityOptions("\nSelect box structure: ");
                    boxPrompt.SetRejectMessage("\nSelect a Civil 3D structure.");
                    boxPrompt.AddAllowedClass(typeof(Structure), false);

                    PromptEntityResult boxResult = ed.GetEntity(boxPrompt);
                    if (boxResult.Status != PromptStatus.OK)
                        return;

                    Structure box = (Structure)tr.GetObject(boxResult.ObjectId, OpenMode.ForRead);
                    if (box.NetworkId.IsNull)
                        throw new InvalidOperationException("The selected structure does not belong to a pipe network.");

                    boxId = box.ObjectId;
                    boxName = box.Name;
                    networkId = box.NetworkId;
                    boxRimElevation = box.RimElevation;

                    tr.Commit();
                }

                using var dialog = new AccessManholeSettingsForm();
                if (dialog.ShowDialog() != WinForms.DialogResult.OK)
                    return;

                int barrelInches = dialog.BarrelInches;
                int lidInches = dialog.LidInches;
                bool useAecPoint = dialog.UseAecPoint;
                double rimElevation;

                if (useAecPoint)
                {
                    using (doc.LockDocument())
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        PromptEntityOptions pointPrompt = new PromptEntityOptions("\nSelect AEC/COGO point for rim elevation: ");
                        pointPrompt.SetRejectMessage("\nSelect a COGO point.");
                        pointPrompt.AddAllowedClass(typeof(CogoPoint), false);

                        PromptEntityResult pointResult = ed.GetEntity(pointPrompt);
                        if (pointResult.Status != PromptStatus.OK)
                            return;

                        CogoPoint point = (CogoPoint)tr.GetObject(pointResult.ObjectId, OpenMode.ForRead);
                        rimElevation = point.Elevation;
                        tr.Commit();
                    }
                }
                else
                {
                    rimElevation = dialog.ManualRimElevation;
                }

                // The access-man­hole barrel depth is measured from the access rim down to
                // the rim of the underlying box structure. The box rim, not the box sump,
                // is the elevation used for the new access structure's sump.
                double availableHeight = rimElevation - boxRimElevation;
                double typeIMinimum = barrelInches == 48 ? TypeIMinimum48 : TypeIMinimum60;
                string structureType = availableHeight >= typeIMinimum ? "TYPE I" : "TYPE IA";

                ed.WriteMessage(
                    $"\nPLACE ACCESS MANHOLE: Rim={rimElevation:F3}, " +
                    $"Box Rim={boxRimElevation:F3}, Available Height={availableHeight:F3}'.");
                ed.WriteMessage(
                    $"\nPLACE ACCESS MANHOLE: {barrelInches}\" barrel requires {typeIMinimum:F3}' for Type I -> {structureType}.");

                ObjectId markId;
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    PromptEntityOptions markPrompt = new PromptEntityOptions("\nSelect UFLS_MH_MARK for access manhole center: ");
                    markPrompt.SetRejectMessage("\nSelect the UFLS_MH_MARK block.");
                    markPrompt.AddAllowedClass(typeof(BlockReference), false);

                    PromptEntityResult markResult = ed.GetEntity(markPrompt);
                    if (markResult.Status != PromptStatus.OK)
                        return;

                    BlockReference mark = (BlockReference)tr.GetObject(markResult.ObjectId, OpenMode.ForRead);
                    BlockTableRecord markDef =
                        (BlockTableRecord)tr.GetObject(mark.BlockTableRecord, OpenMode.ForRead);

                    if (!string.Equals(markDef.Name, "UFLS_MH_MARK", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The selected block is not UFLS_MH_MARK.");

                    markId = mark.ObjectId;
                    tr.Commit();
                }

                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Network network = (Network)tr.GetObject(networkId, OpenMode.ForRead);
                    ObjectId partsListId = GetObjectIdProperty(network, "PartsListId");
                    if (partsListId.IsNull)
                        throw new InvalidOperationException("The selected network does not have a Parts List.");

                    PartsList partsList = (PartsList)tr.GetObject(partsListId, OpenMode.ForRead);
                    PartChoice choice = FindAccessPart(
                        tr,
                        partsList,
                        structureType,
                        barrelInches,
                        lidInches);

                    BlockReference mark = (BlockReference)tr.GetObject(markId, OpenMode.ForRead);
                    var location = mark.Position;

                    ObjectId newStructureId = ObjectId.Null;
                    network.UpgradeOpen();
                    network.AddStructure(
                        choice.FamilyId,
                        choice.SizeId,
                        location,
                        0.0,
                        ref newStructureId,
                        false);

                    Structure newStructure = (Structure)tr.GetObject(newStructureId, OpenMode.ForWrite);

                    newStructure.AutomaticRimSurfaceAdjustment = false;
                    newStructure.ControlSumpBy = StructureControlSumpType.ByElevation;
                    newStructure.RimElevation = rimElevation;
                    newStructure.SumpElevation = boxRimElevation;
                    newStructure.Name = RemoveJsSuffix(boxName);

                    tr.Commit();

                    ed.WriteMessage(
                        $"\nPLACE ACCESS MANHOLE: Created '{newStructure.Name}' using '{choice.DisplayName}'.");
                    ed.WriteMessage(
                        $"\n  Type={structureType}, Barrel={barrelInches}\", Lid={lidInches}\", " +
                        $"Rim={rimElevation:F3}, Sump={boxRimElevation:F3}, " +
                        "Auto Surface Adjustment=False, Control Sump By=Elevation.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPLACE ACCESS MANHOLE error: {ex.Message}");
            }
        }

        private static PartChoice FindAccessPart(
            Transaction tr,
            PartsList partsList,
            string structureType,
            int barrelInches,
            int lidInches)
        {
            ObjectIdCollection familyIds = partsList.GetPartFamilyIdsByDomain(DomainType.Structure);
            var familyMatches = new List<PartFamily>();

            foreach (ObjectId familyId in familyIds)
            {
                if (tr.GetObject(familyId, OpenMode.ForRead) is not PartFamily family)
                    continue;

                string familyName = family.Name ?? string.Empty;
                if (familyName.IndexOf("ACCESS STRUCTURE", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                bool typeMatch = structureType.Equals("TYPE IA", StringComparison.OrdinalIgnoreCase)
                    ? Regex.IsMatch(familyName, @"\bTYPE\s+IA\b", RegexOptions.IgnoreCase)
                    : Regex.IsMatch(familyName, @"\bTYPE\s+I\b(?!A)", RegexOptions.IgnoreCase);

                if (!typeMatch)
                    continue;

                if (!ContainsSizeToken(familyName, lidInches))
                    continue;

                familyMatches.Add(family);
            }

            if (familyMatches.Count == 0)
            {
                throw new InvalidOperationException(
                    $"No {structureType} ACCESS STRUCTURE family for a {lidInches}\" lid was found in the selected network Parts List.");
            }

            var sizeMatches = new List<PartChoice>();

            foreach (PartFamily family in familyMatches)
            {
                for (int i = 0; i < family.PartSizeCount; i++)
                {
                    ObjectId sizeId = family[i];
                    if (sizeId.IsNull)
                        continue;

                    string sizeName = GetPartSizeName(tr, sizeId);
                    if (!ContainsSizeToken(sizeName, barrelInches))
                        continue;

                    sizeMatches.Add(new PartChoice(
                        family.ObjectId,
                        sizeId,
                        family.Name + " — " + sizeName));
                }
            }

            if (sizeMatches.Count == 0)
            {
                string families = string.Join("; ", familyMatches.Select(f => f.Name));
                throw new InvalidOperationException(
                    $"The {structureType} {lidInches}\" ACCESS STRUCTURE family was found, " +
                    $"but no {barrelInches}\" barrel size was found. Families checked: {families}");
            }

            return sizeMatches[0];
        }

        private static bool ContainsSizeToken(string text, int inches)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            // Verbatim string: an embedded quote is represented by two quotes.
            string pattern = $@"(?<!\d){inches}(?:\.\d+)?(?:\s*(?:INCH(?:ES)?|IN|DIA|DIAMETER|""))?(?!\d)";
            return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase);
        }

        private static ObjectId GetObjectIdProperty(object obj, string propertyName)
        {
            PropertyInfo? property = obj.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public);

            if (property?.PropertyType == typeof(ObjectId) &&
                property.GetValue(obj) is ObjectId id)
                return id;

            return ObjectId.Null;
        }

        private static string GetPartSizeName(Transaction tr, ObjectId sizeId)
        {
            Autodesk.AutoCAD.DatabaseServices.DBObject obj = tr.GetObject(sizeId, OpenMode.ForRead);

            foreach (string propertyName in new[]
                     { "Name", "DisplayName", "Description", "PartSizeName" })
            {
                PropertyInfo? property = obj.GetType().GetProperty(
                    propertyName,
                    BindingFlags.Instance | BindingFlags.Public);

                if (property?.PropertyType == typeof(string) &&
                    property.GetValue(obj) is string value &&
                    !string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return obj.GetType().Name;
        }

        private static string RemoveJsSuffix(string name)
            => name.EndsWith("-JS", StringComparison.OrdinalIgnoreCase)
                ? name[..^3]
                : name;

        private sealed class PartChoice
        {
            public ObjectId FamilyId { get; }
            public ObjectId SizeId { get; }
            public string DisplayName { get; }

            public PartChoice(ObjectId familyId, ObjectId sizeId, string displayName)
            {
                FamilyId = familyId;
                SizeId = sizeId;
                DisplayName = displayName;
            }
        }

        private sealed class AccessManholeSettingsForm : WinForms.Form
        {
            private readonly WinForms.ComboBox _barrelCombo;
            private readonly WinForms.ComboBox _lidCombo;
            private readonly WinForms.RadioButton _aecRadio;
            private readonly WinForms.RadioButton _manualRadio;
            private readonly WinForms.TextBox _elevationTextBox;

            public int BarrelInches => Convert.ToInt32(_barrelCombo.SelectedItem, CultureInfo.InvariantCulture);
            public int LidInches => Convert.ToInt32(_lidCombo.SelectedItem, CultureInfo.InvariantCulture);
            public bool UseAecPoint => _aecRadio.Checked;

            public double ManualRimElevation
            {
                get
                {
                    if (!double.TryParse(
                            _elevationTextBox.Text,
                            NumberStyles.Float,
                            CultureInfo.CurrentCulture,
                            out double value))
                    {
                        throw new InvalidOperationException("Enter a valid rim elevation.");
                    }

                    return value;
                }
            }

            public AccessManholeSettingsForm()
            {
                Text = "PLACE ACCESS MANHOLE";
                FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
                StartPosition = WinForms.FormStartPosition.CenterScreen;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;
                ClientSize = new System.Drawing.Size(400, 285);

                var barrelLabel = new WinForms.Label
                {
                    Text = "Barrel Size:",
                    AutoSize = true,
                    Left = 20,
                    Top = 22
                };

                _barrelCombo = new WinForms.ComboBox
                {
                    Left = 145,
                    Top = 18,
                    Width = 205,
                    DropDownStyle = WinForms.ComboBoxStyle.DropDownList
                };
                _barrelCombo.Items.AddRange(new object[] { 48, 60 });
                _barrelCombo.SelectedIndex = 0;

                var lidLabel = new WinForms.Label
                {
                    Text = "Lid Size:",
                    AutoSize = true,
                    Left = 20,
                    Top = 58
                };

                _lidCombo = new WinForms.ComboBox
                {
                    Left = 145,
                    Top = 54,
                    Width = 205,
                    DropDownStyle = WinForms.ComboBoxStyle.DropDownList
                };
                _lidCombo.Items.AddRange(new object[] { 24, 30 });
                _lidCombo.SelectedIndex = 0;

                var rimGroup = new WinForms.GroupBox
                {
                    Text = "Rim Elevation Source",
                    Left = 15,
                    Top = 92,
                    Width = 370,
                    Height = 110
                };

                _aecRadio = new WinForms.RadioButton
                {
                    Text = "Select AEC / COGO Point",
                    Left = 15,
                    Top = 25,
                    AutoSize = true,
                    Checked = true
                };

                _manualRadio = new WinForms.RadioButton
                {
                    Text = "Enter Elevation:",
                    Left = 15,
                    Top = 59,
                    AutoSize = true
                };

                _elevationTextBox = new WinForms.TextBox
                {
                    Left = 145,
                    Top = 56,
                    Width = 205,
                    Enabled = false
                };

                _aecRadio.CheckedChanged += (_, _) =>
                    _elevationTextBox.Enabled = _manualRadio.Checked;

                var note = new WinForms.Label
                {
                    Text = "Type I / Type IA is calculated automatically from available height.",
                    AutoSize = false,
                    Width = 370,
                    Height = 30,
                    Left = 15,
                    Top = 207
                };

                var okButton = new WinForms.Button
                {
                    Text = "OK",
                    DialogResult = WinForms.DialogResult.OK,
                    Left = 224,
                    Top = 235,
                    Width = 75,
                    Height = 28
                };

                var cancelButton = new WinForms.Button
                {
                    Text = "Cancel",
                    DialogResult = WinForms.DialogResult.Cancel,
                    Left = 310,
                    Top = 235,
                    Width = 75,
                    Height = 28
                };

                AcceptButton = okButton;
                CancelButton = cancelButton;

                rimGroup.Controls.Add(_aecRadio);
                rimGroup.Controls.Add(_manualRadio);
                rimGroup.Controls.Add(_elevationTextBox);

                Controls.Add(barrelLabel);
                Controls.Add(_barrelCombo);
                Controls.Add(lidLabel);
                Controls.Add(_lidCombo);
                Controls.Add(rimGroup);
                Controls.Add(note);
                Controls.Add(okButton);
                Controls.Add(cancelButton);
            }

            protected override void OnFormClosing(WinForms.FormClosingEventArgs e)
            {
                if (DialogResult == WinForms.DialogResult.OK && !_aecRadio.Checked)
                {
                    if (!double.TryParse(
                            _elevationTextBox.Text,
                            NumberStyles.Float,
                            CultureInfo.CurrentCulture,
                            out _))
                    {
                        WinForms.MessageBox.Show(
                            this,
                            "Enter a valid rim elevation.",
                            "PLACE ACCESS MANHOLE",
                            WinForms.MessageBoxButtons.OK,
                            WinForms.MessageBoxIcon.Warning);

                        e.Cancel = true;
                        return;
                    }
                }

                base.OnFormClosing(e);
            }
        }
    }
}