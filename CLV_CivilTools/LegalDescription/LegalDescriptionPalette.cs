using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Windows;

using CLV_CivilTools.Shared;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.LegalDescription
{
    internal static class LegalDescriptionPalette
    {
        private static PaletteSet? _palette;
        private static LegalDescriptionControl? _control;

        internal static void Show(LegalDescriptionSession session)
        {
            if (_palette == null)
            {
                _control = new LegalDescriptionControl();
                _palette = new PaletteSet("LEGAL DESCRIPTION")
                {
                    DockEnabled = DockSides.Left | DockSides.Right | DockSides.Top | DockSides.Bottom
                };
                _palette.Add("PHASE 1-2", _control);
                PalettePositionHelper.ConfigureSize(_palette, new Size(760, 760), new Size(600, 520));
            }

            _control!.LoadSession(session);
            PalettePositionHelper.ShowNearAutoCadWindow(_palette, new Size(760, 760), new Size(600, 520), 420, 180);
        }
    }

    internal sealed class LegalDescriptionControl : UserControl
    {
        private readonly DataGridView _grid = new();
        private readonly RichTextBox _preview = new();
        private readonly Label _summary = new();
        private readonly NumericUpDown _distancePrecision = new();
        private readonly NumericUpDown _secondsPrecision = new();
        private readonly ComboBox _textStyle = new();
        private LegalDescriptionSession? _session;
        private ObjectId? _highlightedId;
        private Entity? _highlightTransient;
        private readonly List<Entity> _linkedMTextHighlightTransients = new();
        private bool _loading;

        internal LegalDescriptionControl()
        {
            BuildUi();
        }

        internal void LoadSession(LegalDescriptionSession session)
        {
            ClearHighlight();
            _session = session;
            _loading = true;
            try
            {
                _distancePrecision.Value = Math.Max(_distancePrecision.Minimum, Math.Min(_distancePrecision.Maximum, session.DistancePrecision));
                _secondsPrecision.Value = Math.Max(_secondsPrecision.Minimum, Math.Min(_secondsPrecision.Maximum, session.BearingSecondsPrecision));
                LoadTextStyles(session.TextStyleName);
                PopulateGrid();
                UpdateSummary();
                _preview.Text = string.IsNullOrWhiteSpace(session.FinalTextOverride)
                    ? LegalTextGenerator.Build(session)
                    : session.FinalTextOverride;
            }
            finally
            {
                _loading = false;
            }
        }

        private void BuildUi()
        {
            Dock = DockStyle.Fill;
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5,
                Padding = new Padding(6)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 54));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 46));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _summary.AutoSize = true;
            _summary.Padding = new Padding(2, 2, 2, 6);
            root.Controls.Add(_summary, 0, 0);

            var options = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            options.Controls.Add(new Label { Text = "Text style:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            _textStyle.DropDownStyle = ComboBoxStyle.DropDownList;
            _textStyle.Width = 145;
            options.Controls.Add(_textStyle);
            options.Controls.Add(new Label { Text = "Distance decimals:", AutoSize = true, Padding = new Padding(12, 6, 0, 0) });
            _distancePrecision.Minimum = 0; _distancePrecision.Maximum = 4; _distancePrecision.Width = 48;
            options.Controls.Add(_distancePrecision);
            options.Controls.Add(new Label { Text = "Seconds decimals:", AutoSize = true, Padding = new Padding(12, 6, 0, 0) });
            _secondsPrecision.Minimum = 0; _secondsPrecision.Maximum = 3; _secondsPrecision.Width = 48;
            options.Controls.Add(_secondsPrecision);
            options.Controls.Add(MakeButton("REVERSE BOUNDARY", (_, _) => ReverseCourses(isTie: false)));
            options.Controls.Add(MakeButton("REFRESH SOURCE", (_, _) => RefreshSourceGeometry()));
            options.Controls.Add(MakeButton("DESCRIPTION OPTIONS", (_, _) => EditDescriptionOptions()));
            options.Controls.Add(MakeButton("REGENERATE TEXT", (_, _) => RegenerateText()));
            root.Controls.Add(options, 0, 1);

            ConfigureGrid();
            root.Controls.Add(_grid, 0, 2);

            _preview.Dock = DockStyle.Fill;
            _preview.Multiline = true;
            _preview.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.Both;
            _preview.AcceptsTab = true;
            _preview.Font = new System.Drawing.Font(System.Drawing.FontFamily.GenericMonospace, 9.0f);
            root.Controls.Add(_preview, 0, 3);

            var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
            actions.Controls.Add(MakeButton("SAVE TO DRAWING", (_, _) => SaveSession()));
            actions.Controls.Add(MakeButton("EXPORT TXT", (_, _) => ExportText()));
            actions.Controls.Add(MakeButton("EXPORT LEGAL DOCX", (_, _) => ExportDocx()));
            actions.Controls.Add(MakeButton("PLACE LINKED MTEXT", (_, _) => PlaceLinkedMText()));
            actions.Controls.Add(MakeButton("UPDATE LINKED MTEXT", (_, _) => UpdateLinkedMText(writeMessage: true)));
            actions.Controls.Add(MakeButton("SELECT / ZOOM COURSE", (_, _) => ZoomSelectedCourse()));
            root.Controls.Add(actions, 0, 4);

            Controls.Add(root);
            _distancePrecision.ValueChanged += (_, _) => PrecisionChanged();
            _secondsPrecision.ValueChanged += (_, _) => PrecisionChanged();
            _textStyle.SelectedIndexChanged += (_, _) => TextStyleChanged();
        }

        private void LoadTextStyles(string selectedName)
        {
            _textStyle.Items.Clear();
            foreach (LegalTextStyle style in LegalTextStyleService.GetStyles())
                _textStyle.Items.Add(style.Name);
            int selected = _textStyle.FindStringExact(selectedName);
            _textStyle.SelectedIndex = selected >= 0 ? selected : 0;
        }

        private void ConfigureGrid()
        {
            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.AutoGenerateColumns = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.MultiSelect = false;
            _grid.RowHeadersVisible = false;
            _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Include", HeaderText = "Use", Width = 38 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Group", HeaderText = "Group", Width = 72, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Number", HeaderText = "#", Width = 38, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Type", HeaderText = "Type", Width = 48, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Geometry", HeaderText = "Calculated geometry", Width = 255, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CurveInClass", HeaderText = "Curve IN", Width = 92, ReadOnly = true });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "CurveOutClass", HeaderText = "Curve OUT", Width = 92, ReadOnly = true });
            var travelRelationshipColumn = new DataGridViewComboBoxColumn { Name = "Relationship", HeaderText = "Travel relationship", Width = 155, FlatStyle = FlatStyle.Flat };
            travelRelationshipColumn.DisplayMember = "Name";
            travelRelationshipColumn.ValueMember = "Key";
            travelRelationshipColumn.DataSource = LegalPhraseLibrary.LineRelationships
                .Where(option => string.Equals(option.Placement, "PREFIX", StringComparison.OrdinalIgnoreCase))
                .ToList();
            _grid.Columns.Add(travelRelationshipColumn);
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Reference", HeaderText = "Travel feature / reference", Width = 220 });
            var travelPlacementColumn = new DataGridViewComboBoxColumn { Name = "RelationshipPlacement", HeaderText = "Travel wording order", Width = 145, FlatStyle = FlatStyle.Flat };
            travelPlacementColumn.Items.AddRange("BEFORE GEOMETRY", "AFTER BEARING");
            _grid.Columns.Add(travelPlacementColumn);
            var destinationColumn = new DataGridViewComboBoxColumn { Name = "DestinationRelationship", HeaderText = "Destination clause", Width = 150, FlatStyle = FlatStyle.Flat };
            destinationColumn.DisplayMember = "Name";
            destinationColumn.ValueMember = "Key";
            destinationColumn.DataSource = LegalPhraseLibrary.LineRelationships
                .Where(option => string.Equals(option.Placement, "SUFFIX", StringComparison.OrdinalIgnoreCase) || string.Equals(option.Key, "NONE", StringComparison.OrdinalIgnoreCase))
                .ToList();
            _grid.Columns.Add(destinationColumn);
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "DestinationReference", HeaderText = "Destination feature / reference", Width = 220 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Context", HeaderText = "Additional context", Width = 180 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Prefix", HeaderText = "Custom prefix", Width = 110 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Suffix", HeaderText = "Custom suffix", Width = 110 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Override", HeaderText = "Geometry text override", Width = 220 });
            _grid.CellValueChanged += (_, _) => GridChanged();
            _grid.CurrentCellDirtyStateChanged += (_, _) =>
            {
                if (_grid.IsCurrentCellDirty)
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.SelectionChanged += (_, _) => HighlightSelectedCourse();
        }

        private static Button MakeButton(string text, EventHandler handler)
        {
            var button = new Button { Text = text, AutoSize = true, Height = 26, Margin = new Padding(6, 2, 0, 2) };
            button.Click += handler;
            return button;
        }

        private void PopulateGrid()
        {
            _grid.Rows.Clear();
            if (_session == null)
                return;
            LegalCurveAnalysisService.Analyze(_session);
            AddCourses(_session.TieCourses);
            AddCourses(_session.Courses);
        }

        private void AddCourses(IEnumerable<LegalCourse> courses)
        {
            if (_session == null)
                return;
            foreach (LegalCourse course in courses)
            {
                string displayNumber = string.Equals(course.Group, "TIE", StringComparison.OrdinalIgnoreCase)
                    ? "T" + course.Number.ToString(CultureInfo.InvariantCulture)
                    : course.Number.ToString(CultureInfo.InvariantCulture);
                int rowIndex = _grid.Rows.Add(
                    course.Include,
                    course.Group,
                    displayNumber,
                    course.EntityType,
                    LegalTextGenerator.BuildGeometryText(course, _session),
                    course.CurveInClassification,
                    course.CurveOutClassification,
                    NormalizeTravelRelationship(course),
                    course.RelationshipReference,
                    string.Equals(course.RelationshipPlacementKey, "AFTER_BEARING", StringComparison.OrdinalIgnoreCase) ? "AFTER BEARING" : "BEFORE GEOMETRY",
                    course.DestinationRelationshipKey,
                    course.DestinationRelationshipReference,
                    course.Context,
                    course.Prefix,
                    course.Suffix,
                    course.OverrideText);
                _grid.Rows[rowIndex].Tag = course;
                if (string.Equals(course.Group, "TIE", StringComparison.OrdinalIgnoreCase))
                    _grid.Rows[rowIndex].DefaultCellStyle.BackColor = System.Drawing.Color.FromArgb(235, 245, 255);
            }
        }


        private static string NormalizeTravelRelationship(LegalCourse course)
        {
            LegalPhraseOption? current = LegalPhraseLibrary.Find(LegalPhraseLibrary.LineRelationships, course.RelationshipKey);
            if (current != null && string.Equals(current.Placement, "SUFFIX", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(course.DestinationRelationshipKey, "NONE", StringComparison.OrdinalIgnoreCase))
                {
                    course.DestinationRelationshipKey = course.RelationshipKey;
                    course.DestinationRelationshipReference = course.RelationshipReference;
                }
                course.RelationshipKey = "NONE";
                course.RelationshipReference = string.Empty;
            }
            return course.RelationshipKey;
        }

        private void GridChanged()
        {
            if (_loading || _session == null)
                return;
            CommitRows();
            _session.FinalTextOverride = string.Empty;
            _preview.Text = LegalTextGenerator.Build(_session);
            HighlightSelectedPreviewText();
            UpdateLinkedMText(writeMessage: false);
        }

        private void CommitRows()
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.Tag is not LegalCourse course)
                    continue;
                course.Include = Convert.ToBoolean(row.Cells["Include"].Value ?? true, CultureInfo.InvariantCulture);
                course.RelationshipKey = Convert.ToString(row.Cells["Relationship"].Value, CultureInfo.InvariantCulture) ?? "NONE";
                course.RelationshipReference = Convert.ToString(row.Cells["Reference"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
                course.RelationshipPlacementKey = string.Equals(Convert.ToString(row.Cells["RelationshipPlacement"].Value, CultureInfo.InvariantCulture), "AFTER BEARING", StringComparison.OrdinalIgnoreCase)
                    ? "AFTER_BEARING"
                    : "BEFORE_GEOMETRY";
                course.DestinationRelationshipKey = Convert.ToString(row.Cells["DestinationRelationship"].Value, CultureInfo.InvariantCulture) ?? "NONE";
                course.DestinationRelationshipReference = Convert.ToString(row.Cells["DestinationReference"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
                course.Context = Convert.ToString(row.Cells["Context"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
                course.Prefix = Convert.ToString(row.Cells["Prefix"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
                course.Suffix = Convert.ToString(row.Cells["Suffix"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
                course.OverrideText = Convert.ToString(row.Cells["Override"].Value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
        }

        private void PrecisionChanged()
        {
            if (_loading || _session == null)
                return;
            _session.DistancePrecision = (int)_distancePrecision.Value;
            _session.BearingSecondsPrecision = (int)_secondsPrecision.Value;
            RefreshGeneratedContent();
        }

        private void TextStyleChanged()
        {
            if (_loading || _session == null || _textStyle.SelectedItem == null)
                return;
            _session.TextStyleName = _textStyle.SelectedItem.ToString() ?? "CLV Old Standard";
            RefreshGeneratedContent();
        }

        private void RefreshGeneratedContent()
        {
            if (_session == null)
                return;
            CommitRows();
            _session.FinalTextOverride = string.Empty;
            _loading = true;
            try
            {
                PopulateGrid();
                _preview.Text = LegalTextGenerator.Build(_session);
                HighlightSelectedPreviewText();
                UpdateSummary();
                UpdateLinkedMText(writeMessage: false);
            }
            finally
            {
                _loading = false;
            }
        }


        private void EditDescriptionOptions()
        {
            if (_session == null)
                return;

            using var dialog = new LegalDescriptionOptionsDialog(_session);
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            dialog.ApplyTo(_session);
            _session.FinalTextOverride = string.Empty;

            // Description options are session settings, not temporary dialog values.
            // Persist them immediately so reopening the dialog or command cannot
            // silently fall back to default wording selections.
            Document? activeDoc = AcadApp.DocumentManager.MdiActiveDocument;
            if (activeDoc != null)
            {
                using (activeDoc.LockDocument())
                    LegalSessionStorage.Save(activeDoc.Database, _session);
            }

            RefreshGeneratedContent();
        }

        private void RegenerateText()
        {
            if (_session == null)
                return;
            CommitRows();
            _session.FinalTextOverride = string.Empty;
            _preview.Text = LegalTextGenerator.Build(_session);
            HighlightSelectedPreviewText();
            UpdateLinkedMText(writeMessage: false);
        }

        private void RefreshSourceGeometry()
        {
            if (_session == null)
                return;
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;
            try
            {
                CommitRows();
                using (doc.LockDocument())
                    LegalGeometryService.RefreshFromSourceGeometry(doc.Database, _session);
                _session.FinalTextOverride = string.Empty;
                LoadSession(_session);
                UpdateLinkedMText(writeMessage: false);
                doc.Editor.WriteMessage("\nLEGALDESC: Source LINE and ARC geometry refreshed; linked MText updated.");
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage("\nLEGALDESC refresh error: " + ex.Message);
            }
        }

        private void ReverseCourses(bool isTie)
        {
            if (_session == null)
                return;
            CommitRows();
            List<LegalCourse> source = isTie ? _session.TieCourses : _session.Courses;
            if (source.Count == 0)
                return;

            List<LegalCourse> reversed = source.AsEnumerable().Reverse().ToList();
            for (int i = 0; i < reversed.Count; i++)
            {
                LegalCourse course = reversed[i];
                (course.StartX, course.EndX) = (course.EndX, course.StartX);
                (course.StartY, course.EndY) = (course.EndY, course.StartY);
                course.Reversed = !course.Reversed;
                if (course.EntityType == "ARC")
                    course.CurveRight = !course.CurveRight;
                course.Number = i + 1;
            }

            if (isTie)
            {
                _session.TieCourses = reversed;
                _session.PointOfCommencementX = reversed[0].StartX;
                _session.PointOfCommencementY = reversed[0].StartY;
            }
            else
            {
                _session.Courses = reversed;
                _session.PointOfBoundaryX = reversed[0].StartX;
                _session.PointOfBoundaryY = reversed[0].StartY;
            }
            _session.FinalTextOverride = string.Empty;
            LoadSession(_session);
        }

        private void UpdateSummary()
        {
            if (_session == null)
            {
                _summary.Text = "No legal-description session is loaded.";
                return;
            }
            LegalGeometrySummary summary = LegalGeometryService.Summarize(_session);
            string status = summary.IsClosed ? "CLOSED" : "OPEN";
            _summary.Text = string.Format(CultureInfo.InvariantCulture,
                "Tie: {0}   Boundary: {1}   Boundary length: {2:F2} ft   Area: {3:F2} sq ft   Forward: {4:F4} ft   Reverse: {5:F4} ft   {6}{7}",
                _session.TieCourses.Count, _session.Courses.Count, summary.TraverseLength, Math.Abs(summary.SignedArea),
                summary.ForwardMisclosure, summary.ReverseMisclosure, status,
                string.IsNullOrWhiteSpace(summary.Warning) ? string.Empty : " — " + summary.Warning);
        }

        private void SaveSession()
        {
            if (_session == null)
                return;
            CommitRows();
            _session.FinalTextOverride = _preview.Text;
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;
            try
            {
                int updated;
                using (doc.LockDocument())
                {
                    updated = LegalMTextService.UpdateLinkedMText(doc.Database, _session, _preview.Text);
                    LegalSessionStorage.Save(doc.Database, _session);
                }
                doc.Editor.WriteMessage(
                    $"\nLEGALDESC: Session saved in the drawing; {updated} linked MText object(s) updated. " +
                    "Save the DWG to retain the session after the drawing is closed.");
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage("\nLEGALDESC save error: " + ex.Message);
            }
        }

        private void PlaceLinkedMText()
        {
            if (_session == null)
                return;
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            PromptPointResult point = doc.Editor.GetPoint("\nSelect insertion point for linked legal-description MText: ");
            if (point.Status != PromptStatus.OK)
                return;

            try
            {
                CommitRows();
                _session.FinalTextOverride = _preview.Text;
                ObjectId id;
                using (doc.LockDocument())
                {
                    id = LegalMTextService.CreateLinkedMText(doc.Database, point.Value, _session, _preview.Text);
                    string handle = id.Handle.ToString();
                    if (!_session.LinkedMTextHandles.Contains(handle, StringComparer.OrdinalIgnoreCase))
                        _session.LinkedMTextHandles.Add(handle);
                    LegalSessionStorage.Save(doc.Database, _session);
                }
                doc.Editor.WriteMessage("\nLEGALDESC: Linked paragraph-form MText created. It will update when the legal session is saved, regenerated, or refreshed.");
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage("\nLEGALDESC MText creation error: " + ex.Message);
            }
        }

        private int UpdateLinkedMText(bool writeMessage)
        {
            if (_session == null)
                return 0;
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return 0;
            try
            {
                int updated;
                using (doc.LockDocument())
                    updated = LegalMTextService.UpdateLinkedMText(doc.Database, _session, _preview.Text);
                if (writeMessage)
                    doc.Editor.WriteMessage($"\nLEGALDESC: Updated {updated} linked MText object(s).");
                return updated;
            }
            catch (System.Exception ex)
            {
                if (writeMessage)
                    doc.Editor.WriteMessage("\nLEGALDESC MText update error: " + ex.Message);
                return 0;
            }
        }

        private void ExportDocx()
        {
            if (_session == null)
                return;

            CommitRows();
            _session.FinalTextOverride = _preview.Text;
            using var options = new LegalDocxExportOptionsDialog(_session);
            if (options.ShowDialog(this) != DialogResult.OK)
                return;
            options.ApplyTo(_session);

            using var dialog = new System.Windows.Forms.SaveFileDialog
            {
                Filter = "Word document (*.docx)|*.docx",
                FileName = string.IsNullOrWhiteSpace(_session.Apn)
                    ? "Legal_Description.docx"
                    : "Legal_" + _session.Apn.Replace("/", "-") + ".docx"
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                LegalDocxExporter.Export(_session, _preview.Text, dialog.FileName);
                Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
                doc?.Editor.WriteMessage("\nLEGALDESC: Word document exported: " + dialog.FileName);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(this, ex.Message, "LEGAL DESCRIPTION DOCX EXPORT", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExportText()
        {
            if (_session == null)
                return;
            using var dialog = new System.Windows.Forms.SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                FileName = "Legal_Description.txt"
            };
            if (dialog.ShowDialog() != DialogResult.OK)
                return;
            File.WriteAllText(dialog.FileName, _preview.Text);
        }

        private LegalCourse? SelectedCourse => _grid.SelectedRows.Count == 0 ? null : _grid.SelectedRows[0].Tag as LegalCourse;

        private void HighlightSelectedCourse()
        {
            ClearHighlight();
            HighlightSelectedPreviewText();
            LegalCourse? course = SelectedCourse;
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (course == null || doc == null)
                return;

            HighlightLinkedMTextCourse(course, doc);

            if (!TryGetObjectId(doc.Database, course.Handle, out ObjectId id))
            {
                doc.Editor.UpdateScreen();
                return;
            }

            try
            {
                using Transaction tr = doc.Database.TransactionManager.StartOpenCloseTransaction();
                Entity entity = (Entity)tr.GetObject(id, OpenMode.ForRead);
                Entity transient = (Entity)entity.Clone();
                transient.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 6);
                transient.LineWeight = LineWeight.LineWeight050;
                Autodesk.AutoCAD.GraphicsInterface.TransientManager.CurrentTransientManager.AddTransient(
                    transient,
                    Autodesk.AutoCAD.GraphicsInterface.TransientDrawingMode.Highlight,
                    128,
                    new IntegerCollection());
                _highlightTransient = transient;
                _highlightedId = id;
                tr.Commit();
                doc.Editor.UpdateScreen();
            }
            catch (System.Exception)
            {
                _highlightedId = null;
                _highlightTransient?.Dispose();
                _highlightTransient = null;
            }
        }

        private void HighlightLinkedMTextCourse(LegalCourse course, Document doc)
        {
            if (_session == null || _session.LinkedMTextHandles == null || _session.LinkedMTextHandles.Count == 0)
                return;

            string courseLine = LegalTextGenerator.BuildCourseLine(_session, course);
            string fallback = string.IsNullOrWhiteSpace(course.OverrideText)
                ? LegalTextGenerator.BuildGeometryText(course, _session)
                : course.OverrideText.Trim();

            try
            {
                foreach (MText clone in LegalMTextService.CreateCourseHighlightClones(
                    doc.Database,
                    _session,
                    new[] { courseLine, fallback }))
                {
                    Autodesk.AutoCAD.GraphicsInterface.TransientManager.CurrentTransientManager.AddTransient(
                        clone,
                        Autodesk.AutoCAD.GraphicsInterface.TransientDrawingMode.DirectShortTerm,
                        129,
                        new IntegerCollection());
                    _linkedMTextHighlightTransients.Add(clone);
                }
            }
            catch (System.Exception)
            {
                foreach (Entity transient in _linkedMTextHighlightTransients)
                    transient.Dispose();
                _linkedMTextHighlightTransients.Clear();
            }
        }

        private void HighlightSelectedPreviewText()
        {
            int originalStart = _preview.SelectionStart;
            int originalLength = _preview.SelectionLength;
            _preview.SelectAll();
            _preview.SelectionBackColor = System.Drawing.SystemColors.Window;
            _preview.SelectionColor = System.Drawing.SystemColors.WindowText;

            LegalCourse? course = SelectedCourse;
            if (_session == null || course == null || string.IsNullOrEmpty(_preview.Text))
            {
                _preview.Select(Math.Min(originalStart, _preview.TextLength), 0);
                return;
            }

            string courseLine = LegalTextGenerator.BuildCourseLine(_session, course);
            int index = _preview.Text.IndexOf(courseLine, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                string fallback = string.IsNullOrWhiteSpace(course.OverrideText)
                    ? LegalTextGenerator.BuildGeometryText(course, _session)
                    : course.OverrideText.Trim();
                index = _preview.Text.IndexOf(fallback, StringComparison.OrdinalIgnoreCase);
                if (index >= 0)
                    courseLine = fallback;
            }

            if (index >= 0)
            {
                _preview.Select(index, Math.Min(courseLine.Length, _preview.TextLength - index));
                _preview.SelectionBackColor = System.Drawing.Color.Yellow;
                _preview.SelectionColor = System.Drawing.Color.Black;
                _preview.ScrollToCaret();
            }
            else
            {
                _preview.Select(Math.Min(originalStart, _preview.TextLength), Math.Min(originalLength, Math.Max(0, _preview.TextLength - originalStart)));
            }
        }

        private void ClearHighlight()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            bool cleared = false;
            if (_highlightTransient != null)
            {
                try
                {
                    Autodesk.AutoCAD.GraphicsInterface.TransientManager.CurrentTransientManager.EraseTransient(
                        _highlightTransient,
                        new IntegerCollection());
                }
                catch (System.Exception) { }
                _highlightTransient.Dispose();
                _highlightTransient = null;
                cleared = true;
            }

            foreach (Entity transient in _linkedMTextHighlightTransients)
            {
                try
                {
                    Autodesk.AutoCAD.GraphicsInterface.TransientManager.CurrentTransientManager.EraseTransient(
                        transient,
                        new IntegerCollection());
                }
                catch (System.Exception) { }
                transient.Dispose();
                cleared = true;
            }
            _linkedMTextHighlightTransients.Clear();

            if (cleared)
                doc?.Editor.UpdateScreen();
            _highlightedId = null;
        }

        private void ZoomSelectedCourse()
        {
            LegalCourse? course = SelectedCourse;
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (course == null || doc == null)
                return;
            Point3d center = new((course.StartX + course.EndX) / 2.0, (course.StartY + course.EndY) / 2.0, 0.0);
            double size = Math.Max(10.0, Math.Sqrt(Math.Pow(course.EndX - course.StartX, 2.0) + Math.Pow(course.EndY - course.StartY, 2.0)) * 1.8);
            doc.SendStringToExecute($"_.ZOOM _C {center.X.ToString(CultureInfo.InvariantCulture)},{center.Y.ToString(CultureInfo.InvariantCulture)} {size.ToString(CultureInfo.InvariantCulture)} ", true, false, false);
        }

        private static bool TryGetObjectId(Database db, string handleText, out ObjectId id)
        {
            id = ObjectId.Null;
            try
            {
                long value = Convert.ToInt64(handleText, 16);
                id = db.GetObjectId(false, new Handle(value), 0);
                return !id.IsNull && id.IsValid;
            }
            catch (System.Exception) { return false; }
        }
    }

    internal sealed class LegalDescriptionOptionsDialog : Form
    {
        private readonly CheckBox _useStandardLandTemplate = new();
        private readonly TextBox _primaryQuarterName = new();
        private readonly TextBox _primaryQuarterCode = new();
        private readonly TextBox _secondaryQuarterName = new();
        private readonly TextBox _secondaryQuarterCode = new();
        private readonly TextBox _section = new();
        private readonly TextBox _township = new();
        private readonly TextBox _range = new();
        private readonly TextBox _intro = new();
        private readonly TextBox _poc = new();
        private readonly TextBox _pob = new();
        private readonly TextBox _area = new();
        private readonly TextBox _pocRelationship = new();
        private readonly ComboBox _sameBeginning = new();
        private readonly ComboBox _commencement = new();
        private readonly ComboBox _finalTie = new();
        private readonly ComboBox _returnCall = new();
        private readonly ComboBox _areaOutput = new();
        private readonly NumericUpDown _areaSfPrecision = new();
        private readonly NumericUpDown _areaAcresPrecision = new();
        private readonly CheckBox _areaComputerMethods = new();

        internal LegalDescriptionOptionsDialog(LegalDescriptionSession session)
        {
            Text = "LEGAL DESCRIPTION OPTIONS";
            StartPosition = FormStartPosition.CenterParent;
            Width = 860;
            Height = 960;
            MinimizeBox = false;
            MaximizeBox = false;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 18,
                Padding = new Padding(10),
                AutoScroll = true
            };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 255));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 18; i++)
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _useStandardLandTemplate.Text = "USE CITY SURVEYOR LAND DESCRIPTION TEMPLATE";
            _useStandardLandTemplate.AutoSize = true;
            _useStandardLandTemplate.Checked = session.UseStandardLandDescriptionTemplate;
            root.Controls.Add(_useStandardLandTemplate, 0, 0);
            root.SetColumnSpan(_useStandardLandTemplate, 2);

            var landPanel = BuildLandTemplatePanel();
            root.Controls.Add(landPanel, 0, 1);
            root.SetColumnSpan(landPanel, 2);

            var introPanel = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true };
            introPanel.Controls.Add(new Label
            {
                Text = "OPTIONAL — ENTER A COMPLETE PARAGRAPH ONLY TO OVERRIDE THE STANDARD TEMPLATE ABOVE.",
                AutoSize = true,
                ForeColor = System.Drawing.SystemColors.GrayText
            }, 0, 0);
            _intro.Multiline = true;
            _intro.ScrollBars = ScrollBars.Vertical;
            _intro.Height = 70;
            _intro.Dock = DockStyle.Top;
            introPanel.Controls.Add(_intro, 0, 1);
            AddOptionRow(root, 2, "CUSTOM LAND DESCRIPTION OVERRIDE:", introPanel);

            AddOptionRow(root, 3, "POINT OF COMMENCEMENT DESCRIPTION:", _poc);
            AddOptionRow(root, 4, "POC SAME/ALSO BEING DESCRIPTION:", _pocRelationship);
            AddOptionRow(root, 5, "POINT OF BEGINNING DESCRIPTION:", _pob);
            ConfigurePhraseCombo(_sameBeginning, LegalPhraseLibrary.SamePointBeginnings, session.SamePointBeginningKey);
            AddOptionRow(root, 6, "SAME POC/POB WORDING:", _sameBeginning);
            ConfigurePhraseCombo(_commencement, LegalPhraseLibrary.Commencements, session.CommencementKey);
            AddOptionRow(root, 7, "COMMENCEMENT WORDING:", _commencement);
            ConfigurePhraseCombo(_finalTie, LegalPhraseLibrary.FinalTieCalls, session.FinalTieKey);
            AddOptionRow(root, 8, "FINAL TIE / POB WORDING:", _finalTie);
            ConfigurePhraseCombo(_returnCall, LegalPhraseLibrary.ReturnCalls, session.ReturnCallKey);
            AddOptionRow(root, 9, "BOUNDARY RETURN WORDING:", _returnCall);

            ConfigureAreaOutputCombo(session.AreaOutputKey);
            AddOptionRow(root, 10, "AREA UNIT SELECTION:", _areaOutput);
            _areaSfPrecision.Minimum = 0;
            _areaSfPrecision.Maximum = 4;
            _areaSfPrecision.Value = ClampDecimal(session.AreaSquareFeetPrecision, 0, 4);
            AddOptionRow(root, 11, "SQUARE FEET DECIMALS:", _areaSfPrecision);
            _areaAcresPrecision.Minimum = 0;
            _areaAcresPrecision.Maximum = 6;
            _areaAcresPrecision.Value = ClampDecimal(session.AreaAcresPrecision, 0, 6);
            AddOptionRow(root, 12, "ACRES DECIMALS:", _areaAcresPrecision);
            _areaComputerMethods.Text = "INCLUDE ‘AS DETERMINED BY COMPUTER METHODS’";
            _areaComputerMethods.Checked = session.AreaIncludeComputerMethods;
            _areaComputerMethods.AutoSize = true;
            AddOptionRow(root, 13, "AREA WORDING:", _areaComputerMethods);

            var areaOverridePanel = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true };
            areaOverridePanel.Controls.Add(new Label
            {
                Text = "OPTIONAL — LEAVE BLANK TO USE THE AREA UNIT SELECTION ABOVE.",
                AutoSize = true,
                ForeColor = System.Drawing.SystemColors.GrayText
            }, 0, 0);
            _area.Dock = DockStyle.Top;
            areaOverridePanel.Controls.Add(_area, 0, 1);
            AddOptionRow(root, 14, "CUSTOM AREA STATEMENT:", areaOverridePanel);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft, AutoSize = true };
            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "CANCEL", DialogResult = DialogResult.Cancel, AutoSize = true };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            root.Controls.Add(buttons, 0, 17);
            root.SetColumnSpan(buttons, 2);
            Controls.Add(root);
            AcceptButton = ok;
            CancelButton = cancel;

            _primaryQuarterName.Text = session.LandPrimaryQuarterName;
            _primaryQuarterCode.Text = session.LandPrimaryQuarterCode;
            _secondaryQuarterName.Text = session.LandSecondaryQuarterName;
            _secondaryQuarterCode.Text = session.LandSecondaryQuarterCode;
            _section.Text = session.LandSection;
            _township.Text = session.LandTownship;
            _range.Text = session.LandRange;
            _intro.Text = session.IntroductoryText;
            _poc.Text = session.PointOfCommencementDescription;
            _pocRelationship.Text = session.PointOfCommencementRelationship;
            _pob.Text = session.PointOfBeginningDescription;
            _area.Text = session.AreaStatementOverride;

            _useStandardLandTemplate.CheckedChanged += (_, _) => UpdateLandTemplateEnabledState();
            UpdateLandTemplateEnabledState();
        }

        private TableLayoutPanel BuildLandTemplatePanel()
        {
            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 4,
                RowCount = 4,
                AutoSize = true,
                Padding = new Padding(8),
                CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            AddLandField(panel, 0, 0, "FIRST QUARTER NAME:", _primaryQuarterName);
            AddLandField(panel, 2, 0, "FIRST QUARTER CODE:", _primaryQuarterCode);
            AddLandField(panel, 0, 1, "SECOND QUARTER NAME:", _secondaryQuarterName);
            AddLandField(panel, 2, 1, "SECOND QUARTER CODE:", _secondaryQuarterCode);
            AddLandField(panel, 0, 2, "SECTION:", _section);
            AddLandField(panel, 2, 2, "TOWNSHIP:", _township);
            AddLandField(panel, 0, 3, "RANGE:", _range);
            var fixedText = new Label
            {
                Text = "FIXED: SOUTH / EAST / M.D.M. / CITY OF LAS VEGAS / CLARK COUNTY / NEVADA",
                AutoSize = true,
                Padding = new Padding(4, 7, 4, 4)
            };
            panel.Controls.Add(fixedText, 2, 3);
            panel.SetColumnSpan(fixedText, 2);
            return panel;
        }

        private static void AddLandField(TableLayoutPanel panel, int column, int row, string label, TextBox box)
        {
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(4, 7, 4, 4) }, column, row);
            box.Dock = DockStyle.Fill;
            panel.Controls.Add(box, column + 1, row);
        }

        private void UpdateLandTemplateEnabledState()
        {
            bool enabled = _useStandardLandTemplate.Checked;
            foreach (TextBox box in new[]
            {
                _primaryQuarterName, _primaryQuarterCode, _secondaryQuarterName,
                _secondaryQuarterCode, _section, _township, _range
            })
                box.Enabled = enabled;
        }

        private static void AddOptionRow(TableLayoutPanel root, int row, string label, Control control)
        {
            root.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, row);
            control.Dock = DockStyle.Top;
            root.Controls.Add(control, 1, row);
        }

        private static void ConfigurePhraseCombo(ComboBox combo, IReadOnlyList<LegalPhraseOption> options, string selectedKey)
        {
            combo.DropDownStyle = ComboBoxStyle.DropDownList;
            combo.DisplayMember = nameof(LegalPhraseOption.Name);
            combo.Items.Clear();
            foreach (LegalPhraseOption option in options)
                combo.Items.Add(option);

            int selectedIndex = -1;
            for (int i = 0; i < combo.Items.Count; i++)
            {
                if (combo.Items[i] is LegalPhraseOption option &&
                    (string.Equals(option.Key, selectedKey, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(option.Name, selectedKey, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(option.Template, selectedKey, StringComparison.OrdinalIgnoreCase)))
                {
                    selectedIndex = i;
                    break;
                }
            }
            combo.SelectedIndex = selectedIndex >= 0 ? selectedIndex : (combo.Items.Count > 0 ? 0 : -1);
        }

        private void ConfigureAreaOutputCombo(string selectedKey)
        {
            _areaOutput.DropDownStyle = ComboBoxStyle.DropDownList;
            _areaOutput.Items.Add(new AreaOutputOption("SQUARE_FEET", "SQUARE FEET"));
            _areaOutput.Items.Add(new AreaOutputOption("ACRES", "ACRES"));
            _areaOutput.Items.Add(new AreaOutputOption("BOTH", "SQUARE FEET AND ACRES"));
            int index = 0;
            for (int i = 0; i < _areaOutput.Items.Count; i++)
            {
                if (_areaOutput.Items[i] is AreaOutputOption option &&
                    string.Equals(option.Key, selectedKey, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }
            _areaOutput.SelectedIndex = index;
        }

        private static decimal ClampDecimal(int value, int min, int max) => Math.Max(min, Math.Min(max, value));

        private static string SelectedPhraseKey(ComboBox combo, string fallback)
        {
            if (combo.SelectedItem is LegalPhraseOption option && !string.IsNullOrWhiteSpace(option.Key))
                return option.Key;
            return fallback;
        }

        private sealed class AreaOutputOption
        {
            internal AreaOutputOption(string key, string name) { Key = key; Name = name; }
            internal string Key { get; }
            internal string Name { get; }
            public override string ToString() => Name;
        }

        internal void ApplyTo(LegalDescriptionSession session)
        {
            session.UseStandardLandDescriptionTemplate = _useStandardLandTemplate.Checked;
            session.LandPrimaryQuarterName = _primaryQuarterName.Text.Trim().ToUpperInvariant();
            session.LandPrimaryQuarterCode = _primaryQuarterCode.Text.Trim().ToUpperInvariant();
            session.LandSecondaryQuarterName = _secondaryQuarterName.Text.Trim().ToUpperInvariant();
            session.LandSecondaryQuarterCode = _secondaryQuarterCode.Text.Trim().ToUpperInvariant();
            session.LandSection = _section.Text.Trim().ToUpperInvariant();
            session.LandTownship = _township.Text.Trim().ToUpperInvariant();
            session.LandRange = _range.Text.Trim().ToUpperInvariant();
            session.IntroductoryText = _intro.Text.Trim();
            session.PointOfCommencementDescription = _poc.Text.Trim();
            session.PointOfCommencementRelationship = _pocRelationship.Text.Trim();
            session.PointOfBeginningDescription = _pob.Text.Trim();
            session.SamePointBeginningKey = SelectedPhraseKey(_sameBeginning, session.SamePointBeginningKey);
            session.CommencementKey = SelectedPhraseKey(_commencement, session.CommencementKey);
            session.FinalTieKey = SelectedPhraseKey(_finalTie, session.FinalTieKey);
            session.ReturnCallKey = SelectedPhraseKey(_returnCall, session.ReturnCallKey);
            session.AreaOutputKey = _areaOutput.SelectedItem is AreaOutputOption areaOption ? areaOption.Key : session.AreaOutputKey;
            session.AreaSquareFeetPrecision = (int)_areaSfPrecision.Value;
            session.AreaAcresPrecision = (int)_areaAcresPrecision.Value;
            session.AreaIncludeComputerMethods = _areaComputerMethods.Checked;
            session.AreaStatementOverride = _area.Text.Trim();
        }
    }


    internal sealed class LegalDocxExportOptionsDialog : Form
    {
        private readonly TextBox _apn = new();
        private readonly TextBox _date = new();
        private readonly TextBox _prepared = new();
        private readonly TextBox _reviewed = new();
        private readonly TextBox _explanation = new();
        private readonly TextBox _basis = new();
        private readonly TextBox _exhibit = new();

        internal LegalDocxExportOptionsDialog(LegalDescriptionSession session)
        {
            Text = "LEGAL DOCX EXPORT OPTIONS";
            StartPosition = FormStartPosition.CenterParent;
            Width = 760;
            Height = 680;
            MinimizeBox = false;
            MaximizeBox = false;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 9, Padding = new Padding(10) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++) root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 28));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 24));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            AddRow(root, 0, "APN:", _apn);
            AddRow(root, 1, "DATE:", _date);
            AddRow(root, 2, "PREPARED BY:", _prepared);
            AddRow(root, 3, "P.R. BY:", _reviewed);
            AddMultilineRow(root, 4, "EXPLANATION:", _explanation);
            AddMultilineRow(root, 5, "BASIS OF BEARINGS:", _basis);
            AddMultilineRow(root, 6, "EXHIBIT STATEMENT:", _exhibit);

            var note = new Label
            {
                Text = "THE CAD MTEXT REMAINS THE LIVE REVIEW COPY. THIS EXPORT USES THE CITY SURVEYOR WORD TEMPLATE AND THE CURRENT LEGAL BODY.",
                AutoSize = true,
                MaximumSize = new Size(690, 0),
                Padding = new Padding(0, 8, 0, 8)
            };
            root.SetColumnSpan(note, 2);
            root.Controls.Add(note, 0, 7);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft, AutoSize = true };
            var ok = new Button { Text = "EXPORT", DialogResult = DialogResult.OK, AutoSize = true };
            var cancel = new Button { Text = "CANCEL", DialogResult = DialogResult.Cancel, AutoSize = true };
            buttons.Controls.Add(ok); buttons.Controls.Add(cancel);
            root.SetColumnSpan(buttons, 2);
            root.Controls.Add(buttons, 0, 8);
            Controls.Add(root);
            AcceptButton = ok;
            CancelButton = cancel;

            _apn.Text = session.Apn;
            _date.Text = session.PreparationDate;
            _prepared.Text = session.PreparedBy;
            _reviewed.Text = session.PeerReviewedBy;
            _explanation.Text = session.ExplanationText;
            _basis.Text = session.BasisOfBearingsText;
            _exhibit.Text = session.ExhibitStatement;
        }

        private static void AddRow(TableLayoutPanel root, int row, string label, TextBox box)
        {
            root.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, row);
            box.Dock = DockStyle.Top;
            root.Controls.Add(box, 1, row);
        }

        private static void AddMultilineRow(TableLayoutPanel root, int row, string label, TextBox box)
        {
            root.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, row);
            box.Multiline = true;
            box.ScrollBars = ScrollBars.Vertical;
            box.Dock = DockStyle.Fill;
            root.Controls.Add(box, 1, row);
        }

        internal void ApplyTo(LegalDescriptionSession session)
        {
            session.Apn = _apn.Text.Trim().ToUpperInvariant();
            session.PreparationDate = _date.Text.Trim().ToUpperInvariant();
            session.PreparedBy = _prepared.Text.Trim().ToUpperInvariant();
            session.PeerReviewedBy = _reviewed.Text.Trim().ToUpperInvariant();
            session.ExplanationText = _explanation.Text.Trim().ToUpperInvariant();
            session.BasisOfBearingsText = _basis.Text.Trim().ToUpperInvariant();
            session.ExhibitStatement = _exhibit.Text.Trim().ToUpperInvariant();
        }
    }

}
