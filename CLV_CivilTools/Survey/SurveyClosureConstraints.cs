using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using CLV_CivilTools.Shared;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;
using AcException = Autodesk.AutoCAD.Runtime.Exception;

namespace CLV_CivilTools.Survey
{
    internal enum ClosureConstraintKind
    {
        LockRadius,
        LockBearing,
        LockLength,
        KeepParallel,
        ParallelToReference,
        OffsetToReference,
        PerpendicularToReference
    }

    internal sealed class ClosureConstraint
    {
        public ClosureConstraintKind Kind { get; init; }
        public ObjectId FirstObjectId { get; init; }
        public ObjectId SecondObjectId { get; init; }
        public string FirstHandle { get; init; } = string.Empty;
        public string SecondHandle { get; init; } = string.Empty;
        public double ReferenceOffsetDistance { get; init; }
        public bool PreserveReferenceOffset { get; init; }
        public bool ReferenceOffsetUserSpecified { get; init; }
        public DateTime CreatedLocal { get; init; } = DateTime.Now;
    }

    internal static class SurveyClosureConstraintStore
    {
        private static readonly List<ClosureConstraint> Constraints = new List<ClosureConstraint>();
        private static readonly Dictionary<ObjectId, short> OriginalColorIndexes = new Dictionary<ObjectId, short>();

        internal static event EventHandler? Changed;

        internal static IReadOnlyList<ClosureConstraint> Current => Constraints;

        internal static void Add(ClosureConstraint constraint)
        {
            Constraints.Add(constraint);
            Changed?.Invoke(null, EventArgs.Empty);
        }

        internal static void Clear()
        {
            Constraints.Clear();
            OriginalColorIndexes.Clear();
            Changed?.Invoke(null, EventArgs.Empty);
        }

        internal static bool ContainsHighlightedObject(ObjectId id) => OriginalColorIndexes.ContainsKey(id);

        internal static void TrackOriginalColor(ObjectId id, short colorIndex)
        {
            if (!id.IsNull && !OriginalColorIndexes.ContainsKey(id))
                OriginalColorIndexes.Add(id, colorIndex);
        }

        internal static IReadOnlyDictionary<ObjectId, short> GetOriginalColors() => OriginalColorIndexes;
    }

    public static class SurveyClosureConstraintCommands
    {
        private static ClosureConstraintManagerForm? _constraintForm;

        [CommandMethod("SURVEY-CLOSURE-ADD-CONSTRAINT", CommandFlags.Modal)]
        public static void AddConstraint()
        {
            ShowConstraintDialog();
        }

        [CommandMethod("SURVEY-CLOSURE-CONSTRAINTS", CommandFlags.Modal)]
        public static void ShowConstraintDialog()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            if (_constraintForm == null || _constraintForm.IsDisposed)
                _constraintForm = new ClosureConstraintManagerForm();

            if (!_constraintForm.Visible)
                AcadApp.ShowModelessDialog(_constraintForm);

            _constraintForm.Activate();
        }

        [CommandMethod("SURVEY-CLOSURE-LIST-CONSTRAINTS", CommandFlags.Modal)]
        public static void ListConstraints()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            IReadOnlyList<ClosureConstraint> constraints = SurveyClosureConstraintStore.Current;
            if (constraints.Count == 0)
            {
                ed.WriteMessage("\nSURVEY-CLOSURE-LIST-CONSTRAINTS: No in-session constraints are currently stored.");
                return;
            }

            ed.WriteMessage("\nSURVEY-CLOSURE-LIST-CONSTRAINTS:");
            for (int i = 0; i < constraints.Count; i++)
            {
                ClosureConstraint c = constraints[i];
                string pair = string.IsNullOrWhiteSpace(c.SecondHandle) ? c.FirstHandle : c.FirstHandle + " / " + c.SecondHandle;
                ed.WriteMessage("\n  " + (i + 1).ToString(CultureInfo.InvariantCulture) + ". " + FormatConstraintKind(c.Kind) + "  Handle(s): " + pair);
            }
        }

        [CommandMethod("SURVEY-CLOSURE-CLEAR-CONSTRAINTS", CommandFlags.Modal)]
        public static void ClearConstraints()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            int count = SurveyClosureConstraintStore.Current.Count;
            RestoreConstraintHighlightColors(doc);
            SurveyClosureConstraintStore.Clear();
            doc.Editor.WriteMessage("\nSURVEY-CLOSURE-CLEAR-CONSTRAINTS: Cleared " + count.ToString(CultureInfo.InvariantCulture) + " in-session constraint(s).");
        }

        internal static bool TryAddConstraintFromDialog(ClosureConstraintKind kind)
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return false;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    ObjectId firstId = PromptForSupportedObject(ed, tr, GetFirstPrompt(kind), requireArc: kind == ClosureConstraintKind.LockRadius);
                    if (firstId.IsNull)
                        return false;

                    ObjectId secondId = ObjectId.Null;
                    double referenceOffsetDistance = 0.0;
                    bool preserveReferenceOffset = false;
                    bool referenceOffsetUserSpecified = false;

                    if (kind == ClosureConstraintKind.KeepParallel)
                    {
                        secondId = PromptForSupportedObject(ed, tr, "\nSelect second boundary LINE segment to keep parallel: ", requireLine: true);
                        if (secondId.IsNull)
                            return false;
                    }
                    else if (kind == ClosureConstraintKind.ParallelToReference || kind == ClosureConstraintKind.OffsetToReference || kind == ClosureConstraintKind.PerpendicularToReference)
                    {
                        secondId = PromptForSupportedObject(ed, tr, "\nSelect reference LINE (reference will not be adjusted): ", requireLine: true);
                        if (secondId.IsNull)
                            return false;

                        if (kind == ClosureConstraintKind.OffsetToReference)
                        {
                            preserveReferenceOffset = true;
                            if (!PromptForOffsetDistance(ed, tr, firstId, secondId, out referenceOffsetDistance, out bool userSpecifiedOffset))
                                return false;

                            // Store whether the user typed the intended offset so the dialog/report can distinguish
                            // measured control from an explicit plat/ROW offset.
                            referenceOffsetUserSpecified = userSpecifiedOffset;
                        }
                    }

                    string firstHandle = GetHandle(tr, firstId);
                    string secondHandle = secondId.IsNull ? string.Empty : GetHandle(tr, secondId);

                    HighlightConstrainedObject(tr, firstId);
                    if (!secondId.IsNull)
                        HighlightConstrainedObject(tr, secondId);

                    SurveyClosureConstraintStore.Add(new ClosureConstraint
                    {
                        Kind = kind,
                        FirstObjectId = firstId,
                        SecondObjectId = secondId,
                        FirstHandle = firstHandle,
                        SecondHandle = secondHandle,
                        ReferenceOffsetDistance = referenceOffsetDistance,
                        PreserveReferenceOffset = preserveReferenceOffset,
                        ReferenceOffsetUserSpecified = referenceOffsetUserSpecified
                    });

                    tr.Commit();
                }

                ed.WriteMessage("\nSURVEY-CLOSURE-CONSTRAINTS: Added " + FormatConstraintKind(kind) + " constraint.");
                return true;
            }
            catch (AcException ex)
            {
                ed.WriteMessage("\nSURVEY-CLOSURE-CONSTRAINTS AutoCAD error: " + ex.Message);
                return false;
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nSURVEY-CLOSURE-CONSTRAINTS error: " + ex.Message);
                return false;
            }
        }

        internal static void RestoreConstraintHighlightColors(Document doc)
        {
            Database db = doc.Database;
            IReadOnlyDictionary<ObjectId, short> originals = SurveyClosureConstraintStore.GetOriginalColors();
            if (originals.Count == 0)
                return;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (KeyValuePair<ObjectId, short> pair in originals)
                {
                    if (pair.Key.IsNull || pair.Key.IsErased)
                        continue;

                    if (tr.GetObject(pair.Key, OpenMode.ForWrite, false) is AcEntity ent)
                        ent.ColorIndex = pair.Value;
                }

                tr.Commit();
            }
        }

        private static string GetFirstPrompt(ClosureConstraintKind kind)
        {
            return kind switch
            {
                ClosureConstraintKind.LockRadius => "\nSelect ARC to lock radius: ",
                ClosureConstraintKind.LockBearing => "\nSelect LINE/ARC segment to lock bearing: ",
                ClosureConstraintKind.LockLength => "\nSelect LINE/ARC segment to lock length: ",
                ClosureConstraintKind.KeepParallel => "\nSelect first boundary LINE segment to keep parallel: ",
                ClosureConstraintKind.ParallelToReference => "\nSelect boundary LINE segment to keep parallel to reference: ",
                ClosureConstraintKind.OffsetToReference => "\nSelect boundary LINE segment to keep offset/parallel to reference: ",
                ClosureConstraintKind.PerpendicularToReference => "\nSelect boundary LINE segment to keep perpendicular to reference: ",
                _ => "\nSelect LINE/ARC segment: "
            };
        }

        private static void HighlightConstrainedObject(Transaction tr, ObjectId id)
        {
            if (id.IsNull || id.IsErased)
                return;

            if (tr.GetObject(id, OpenMode.ForWrite, false) is not AcEntity ent)
                return;

            if (!SurveyClosureConstraintStore.ContainsHighlightedObject(id))
                SurveyClosureConstraintStore.TrackOriginalColor(id, (short)ent.ColorIndex);

            ent.ColorIndex = LayerStandards.SurveyMapConstraintHighlightColorIndex;
        }

        internal static ClosureConstraintKind ParseConstraintKind(string value)
        {
            string normalized = value.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase).ToUpperInvariant();
            return normalized switch
            {
                "LOCKRADIUS" => ClosureConstraintKind.LockRadius,
                "LOCKBEARING" => ClosureConstraintKind.LockBearing,
                "LOCKLENGTH" => ClosureConstraintKind.LockLength,
                "KEEPPARALLEL" => ClosureConstraintKind.KeepParallel,
                "PARALLELTOREFERENCE" => ClosureConstraintKind.ParallelToReference,
                "OFFSETTOREFERENCE" => ClosureConstraintKind.OffsetToReference,
                "PERPENDICULARTOREFERENCE" => ClosureConstraintKind.PerpendicularToReference,
                _ => ClosureConstraintKind.LockRadius
            };
        }

        internal static string FormatConstraintKind(ClosureConstraintKind kind)
        {
            return kind switch
            {
                ClosureConstraintKind.LockRadius => "LOCK RADIUS",
                ClosureConstraintKind.LockBearing => "LOCK BEARING",
                ClosureConstraintKind.LockLength => "LOCK LENGTH",
                ClosureConstraintKind.KeepParallel => "KEEP PARALLEL",
                ClosureConstraintKind.ParallelToReference => "PARALLEL TO REFERENCE",
                ClosureConstraintKind.OffsetToReference => "OFFSET TO REFERENCE",
                ClosureConstraintKind.PerpendicularToReference => "PERPENDICULAR TO REFERENCE",
                _ => kind.ToString()
            };
        }

        private static ObjectId PromptForSupportedObject(Editor ed, Transaction tr, string message, bool requireArc = false, bool requireLine = false)
        {
            PromptEntityOptions peo = new PromptEntityOptions(message);
            peo.SetRejectMessage(requireArc ? "\nSelect an AutoCAD ARC." : requireLine ? "\nSelect an AutoCAD LINE." : "\nSelect an AutoCAD LINE or ARC.");
            if (requireArc)
                peo.AddAllowedClass(typeof(Arc), exactMatch: false);
            else if (requireLine)
                peo.AddAllowedClass(typeof(Line), exactMatch: false);
            else
            {
                peo.AddAllowedClass(typeof(Line), exactMatch: false);
                peo.AddAllowedClass(typeof(Arc), exactMatch: false);
            }

            PromptEntityResult result = ed.GetEntity(peo);
            if (result.Status != PromptStatus.OK)
                return ObjectId.Null;

            AcEntity ent = (AcEntity)tr.GetObject(result.ObjectId, OpenMode.ForRead);
            if (requireArc && ent is not Arc)
                return ObjectId.Null;
            if (requireLine && ent is not Line)
                return ObjectId.Null;
            if (!requireArc && !requireLine && ent is not Line && ent is not Arc)
                return ObjectId.Null;

            return result.ObjectId;
        }

        private static bool PromptForOffsetDistance(Editor ed, Transaction tr, ObjectId firstId, ObjectId secondId, out double signedOffsetDistance, out bool userSpecified)
        {
            signedOffsetDistance = ComputeLineOffsetDistance(tr, firstId, secondId);
            userSpecified = false;

            PromptKeywordOptions modeOptions = new PromptKeywordOptions("\nOFFSET TO REFERENCE - use current measured offset or specify value? [Current/Specify] <Current>: ");
            modeOptions.Keywords.Add("Current");
            modeOptions.Keywords.Add("Specify");
            modeOptions.Keywords.Default = "Current";
            modeOptions.AllowNone = true;
            PromptResult modeResult = ed.GetKeywords(modeOptions);
            if (modeResult.Status == PromptStatus.Cancel)
                return false;

            bool specify = string.Equals(modeResult.StringResult, "Specify", StringComparison.OrdinalIgnoreCase);
            if (!specify)
                return true;

            PromptDoubleOptions distanceOptions = new PromptDoubleOptions("\nEnter intended offset distance: ")
            {
                AllowNegative = false,
                AllowZero = false,
                AllowNone = false
            };
            PromptDoubleResult distanceResult = ed.GetDouble(distanceOptions);
            if (distanceResult.Status != PromptStatus.OK)
                return false;

            double magnitude = Math.Abs(distanceResult.Value);
            double currentSign = signedOffsetDistance < 0.0 ? -1.0 : 1.0;

            PromptKeywordOptions sideOptions = new PromptKeywordOptions("\nOffset side relative to reference direction [Auto/Left/Right] <Auto>: ");
            sideOptions.Keywords.Add("Auto");
            sideOptions.Keywords.Add("Left");
            sideOptions.Keywords.Add("Right");
            sideOptions.Keywords.Default = "Auto";
            sideOptions.AllowNone = true;
            PromptResult sideResult = ed.GetKeywords(sideOptions);
            if (sideResult.Status == PromptStatus.Cancel)
                return false;

            if (string.Equals(sideResult.StringResult, "Left", StringComparison.OrdinalIgnoreCase))
                currentSign = 1.0;
            else if (string.Equals(sideResult.StringResult, "Right", StringComparison.OrdinalIgnoreCase))
                currentSign = -1.0;

            signedOffsetDistance = magnitude * currentSign;
            userSpecified = true;
            return true;
        }

        private static double ComputeLineOffsetDistance(Transaction tr, ObjectId firstId, ObjectId secondId)
        {
            if (tr.GetObject(firstId, OpenMode.ForRead, false) is not Line boundary ||
                tr.GetObject(secondId, OpenMode.ForRead, false) is not Line reference)
                return 0.0;

            Vector3d refVector = reference.EndPoint - reference.StartPoint;
            if (refVector.Length <= 1.0e-9)
                return 0.0;

            Vector3d refDir = refVector.GetNormal();
            Vector3d normal = new Vector3d(-refDir.Y, refDir.X, 0.0);
            Vector3d delta = boundary.StartPoint - reference.StartPoint;
            return delta.DotProduct(normal);
        }

        private static string GetHandle(Transaction tr, ObjectId id)
        {
            DBObject obj = tr.GetObject(id, OpenMode.ForRead);
            return obj.Handle.ToString();
        }
    }

    internal sealed class ClosureConstraintManagerForm : Form
    {
        private readonly ComboBox _typeCombo;
        private readonly DataGridView _grid;
        private readonly Label _statusLabel;

        internal ClosureConstraintManagerForm()
        {
            Text = "CLV Closure Constraints";
            Width = 760;
            Height = 420;
            MinimumSize = new Size(620, 320);
            StartPosition = FormStartPosition.CenterScreen;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(10)
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            var top = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight,
                WrapContents = false
            };

            top.Controls.Add(new Label
            {
                Text = "Constraint type:",
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(0, 6, 4, 0)
            });

            _typeCombo = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Width = 170
            };
            _typeCombo.Items.Add(FormatComboItem(ClosureConstraintKind.LockRadius));
            _typeCombo.Items.Add(FormatComboItem(ClosureConstraintKind.LockBearing));
            _typeCombo.Items.Add(FormatComboItem(ClosureConstraintKind.LockLength));
            _typeCombo.Items.Add(FormatComboItem(ClosureConstraintKind.KeepParallel));
            _typeCombo.Items.Add(FormatComboItem(ClosureConstraintKind.ParallelToReference));
            _typeCombo.Items.Add(FormatComboItem(ClosureConstraintKind.OffsetToReference));
            _typeCombo.Items.Add(FormatComboItem(ClosureConstraintKind.PerpendicularToReference));
            _typeCombo.SelectedIndex = 0;
            top.Controls.Add(_typeCombo);

            var addButton = new Button
            {
                Text = "ADD / PICK",
                Width = 105,
                Height = 28,
                Margin = new Padding(12, 0, 4, 0)
            };
            addButton.Click += AddButton_Click;
            top.Controls.Add(addButton);

            var clearButton = new Button
            {
                Text = "CLEAR ALL",
                Width = 100,
                Height = 28,
                Margin = new Padding(4, 0, 4, 0)
            };
            clearButton.Click += ClearButton_Click;
            top.Controls.Add(clearButton);

            var closeButton = new Button
            {
                Text = "CLOSE",
                Width = 90,
                Height = 28,
                Margin = new Padding(4, 0, 4, 0)
            };
            closeButton.Click += (_, _) => Hide();
            top.Controls.Add(closeButton);

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                MultiSelect = false
            };
            _grid.Columns.Add("Number", "#");
            _grid.Columns.Add("Type", "Type");
            _grid.Columns.Add("First", "First Handle");
            _grid.Columns.Add("Second", "Second Handle");
            _grid.Columns.Add("Offset", "Offset");
            _grid.Columns.Add("Created", "Created");
            _grid.Columns[0].FillWeight = 35;
            _grid.Columns[1].FillWeight = 125;
            _grid.Columns[2].FillWeight = 120;
            _grid.Columns[3].FillWeight = 120;
            _grid.Columns[4].FillWeight = 80;
            _grid.Columns[5].FillWeight = 135;

            _statusLabel = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                Text = "Constraints are in-session only. Selected objects are highlighted until CLEAR ALL.",
                Padding = new Padding(0, 8, 0, 0)
            };

            root.Controls.Add(top, 0, 0);
            root.Controls.Add(_grid, 0, 1);
            root.Controls.Add(_statusLabel, 0, 2);
            Controls.Add(root);

            SurveyClosureConstraintStore.Changed += ConstraintStore_Changed;
            FormClosing += ClosureConstraintManagerForm_FormClosing;
            RefreshGrid();
        }

        private static string FormatComboItem(ClosureConstraintKind kind) => SurveyClosureConstraintCommands.FormatConstraintKind(kind);

        private void ConstraintStore_Changed(object? sender, EventArgs e)
        {
            if (IsDisposed)
                return;

            if (InvokeRequired)
                BeginInvoke(new Action(RefreshGrid));
            else
                RefreshGrid();
        }

        private void ClosureConstraintManagerForm_FormClosing(object? sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        private void AddButton_Click(object? sender, EventArgs e)
        {
            ClosureConstraintKind kind = SurveyClosureConstraintCommands.ParseConstraintKind(_typeCombo.SelectedItem?.ToString() ?? string.Empty);
            Hide();
            bool added = SurveyClosureConstraintCommands.TryAddConstraintFromDialog(kind);
            RefreshGrid();
            Show();
            Activate();
            _statusLabel.Text = added
                ? "Added " + SurveyClosureConstraintCommands.FormatConstraintKind(kind) + ". Objects are highlighted until CLEAR ALL."
                : "No constraint added. Pick was canceled or invalid.";
        }

        private void ClearButton_Click(object? sender, EventArgs e)
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc != null)
                SurveyClosureConstraintCommands.RestoreConstraintHighlightColors(doc);

            SurveyClosureConstraintStore.Clear();
            RefreshGrid();
            _statusLabel.Text = "Cleared constraints and restored highlighted object colors where possible.";
        }

        private void RefreshGrid()
        {
            _grid.Rows.Clear();
            IReadOnlyList<ClosureConstraint> constraints = SurveyClosureConstraintStore.Current;
            for (int i = 0; i < constraints.Count; i++)
            {
                ClosureConstraint c = constraints[i];
                _grid.Rows.Add(
                    (i + 1).ToString(CultureInfo.InvariantCulture),
                    SurveyClosureConstraintCommands.FormatConstraintKind(c.Kind),
                    c.FirstHandle,
                    c.SecondHandle,
                    c.PreserveReferenceOffset ? c.ReferenceOffsetDistance.ToString("0.####'", CultureInfo.InvariantCulture) + (c.ReferenceOffsetUserSpecified ? " (specified)" : " (current)") : string.Empty,
                    c.CreatedLocal.ToString("g", CultureInfo.CurrentCulture));
            }
        }
    }
}
