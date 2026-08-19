using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Colors;


using AcApplication = Autodesk.AutoCAD.ApplicationServices.Application;
using WinException = System.Exception;
using DrawingImage = System.Drawing.Image;

namespace CLV_CivilTools.PdfViewer
{
    public sealed class PdfViewerControl : UserControl
    {
        private readonly ComboBox _sheetList = new();
        private readonly Label _status = new();
        private readonly PdfImagePanel _imagePanel = new();
        private Button _returnCurrent = new();
        private readonly Button _previous = new();
        private readonly Button _next = new();
        private Button _openPdf = new();
        private Button _addPlan = new();
        private Button _addReference = new();
        private Button _remove = new();
        private Button _editBoundary = new();
        private Button _unlockPdf = new();
        private Button _pickPdfPoint = new();
        private Button _saveReferenceView = new();
        private readonly System.Windows.Forms.Timer _syncTimer = new();

        private PdfViewerDrawingState _state = new();
        private PdfSheetMapping? _displayedSheet;
        private string _resolvedPdfPath = string.Empty;
        private int _pageCount;
        private bool _autoFollow = true;
        private bool _updatingList;
        private PointF? _pendingPdfPoint1;
        private PdfSheetMapping? _pendingPlan;
        private PdfSheetMapping? _pendingReference;
        private ViewSnapshot? _lastView;
        private Document? _activeDocument;
        private bool _refreshingDocument;
        private bool _syncInProgress;
        private bool _pdfUnlocked;
        private RectangleF? _manualPdfBounds;
        private bool _awaitingPdfPoint;
        private Autodesk.AutoCAD.DatabaseServices.Polyline? _boundaryTransient;

        public PdfViewerControl()
        {
            BuildUi();
            _syncTimer.Interval = 250;
            _syncTimer.Tick += (_, _) => SyncToCurrentView();
            RefreshForActiveDocument();
            _syncTimer.Start();
        }

        public void RefreshForActiveDocument()
        {
            if (_refreshingDocument)
                return;

            _refreshingDocument = true;
            try
            {
                Document? document = AcApplication.DocumentManager.MdiActiveDocument;
                _activeDocument = document;

                if (document == null)
                {
                    _state = new PdfViewerDrawingState();
                    _resolvedPdfPath = string.Empty;
                    _status.Text = "No active drawing.";
                    RebuildSheetList();
                    return;
                }

                _state = PdfViewerStorage.Load(document.Database);
                _resolvedPdfPath = PdfViewerStorage.ResolvePdfPath(_state, document.Database);
                LoadPdfMetadata();
                _autoFollow = true;
                _lastView = null;
                RebuildSheetList();
            }
            finally
            {
                _refreshingDocument = false;
            }

            SyncToCurrentView(force: true);
        }

        private void BuildUi()
        {
            Dock = DockStyle.Fill;
            Padding = new Padding(6);

            TableLayoutPanel root = new()
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 5
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            FlowLayoutPanel selector = new()
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                WrapContents = false
            };
            _sheetList.DropDownStyle = ComboBoxStyle.DropDownList;
            _sheetList.Width = 430;
            _sheetList.MaxDropDownItems = 24;
            _sheetList.IntegralHeight = false;
            _sheetList.DropDownHeight = 480;
            _sheetList.SelectedIndexChanged += (_, _) => OnSheetSelected();
            selector.Controls.Add(_sheetList);
            _previous.Text = "◀";
            _previous.Width = 36;
            _previous.Click += (_, _) => MoveSheet(-1);
            selector.Controls.Add(_previous);
            _next.Text = "▶";
            _next.Width = 36;
            _next.Click += (_, _) => MoveSheet(1);
            selector.Controls.Add(_next);
            root.Controls.Add(selector, 0, 0);

            _imagePanel.Dock = DockStyle.Fill;
            _imagePanel.BackColor = System.Drawing.Color.FromArgb(44, 44, 44);
            _imagePanel.PdfPointClicked += OnPdfPointClicked;
            _imagePanel.ManualViewRequested += OnManualViewRequested;
            root.Controls.Add(_imagePanel, 0, 1);

            _status.Dock = DockStyle.Fill;
            _status.AutoSize = true;
            _status.Padding = new Padding(2, 5, 2, 5);
            _status.Text = "Open a PDF to begin.";
            root.Controls.Add(_status, 0, 2);

            FlowLayoutPanel navigation = new() { AutoSize = true, Dock = DockStyle.Fill };
            _returnCurrent = CreateButton("RETURN TO CURRENT", 160);
            _returnCurrent.Click += (_, _) => ReturnToCurrent();
            navigation.Controls.Add(_returnCurrent);
            _unlockPdf = CreateButton("UNLOCK PDF", 110);
            _unlockPdf.Click += (_, _) => TogglePdfUnlock();
            navigation.Controls.Add(_unlockPdf);
            _pickPdfPoint = CreateButton("PICK PDF POINT", 130);
            _pickPdfPoint.Enabled = false;
            _pickPdfPoint.Visible = false;
            _pickPdfPoint.Click += (_, _) => ArmPdfPointPick();
            navigation.Controls.Add(_pickPdfPoint);
            _saveReferenceView = CreateButton("SAVE REFERENCE VIEW", 150);
            _saveReferenceView.Enabled = false;
            _saveReferenceView.Visible = false;
            _saveReferenceView.Click += (_, _) => SavePendingReferenceView();
            navigation.Controls.Add(_saveReferenceView);
            root.Controls.Add(navigation, 0, 3);

            FlowLayoutPanel setup = new() { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
            _openPdf = CreateButton("OPEN / RELINK PDF", 130);
            _openPdf.Click += (_, _) => OpenPdf();
            setup.Controls.Add(_openPdf);
            _addPlan = CreateButton("ADD PLAN MAPPING", 130);
            _addPlan.Click += (_, _) => BeginPlanMapping();
            setup.Controls.Add(_addPlan);
            _addReference = CreateButton("ADD REFERENCE VIEW", 135);
            _addReference.Click += (_, _) => AddReferenceSheet();
            setup.Controls.Add(_addReference);
            _editBoundary = CreateButton("EDIT BOUNDARY", 110);
            _editBoundary.Click += (_, _) => EditSelectedBoundary();
            setup.Controls.Add(_editBoundary);
            _remove = CreateButton("REMOVE", 72);
            _remove.Click += (_, _) => RemoveSelectedSheet();
            setup.Controls.Add(_remove);
            root.Controls.Add(setup, 0, 4);

            Controls.Add(root);
        }

        private static Button CreateButton(string text, int width) => new()
        {
            Text = text,
            Width = width,
            Height = 26,
            Margin = new Padding(2)
        };

        private void RebuildSheetList(Guid? selectId = null)
        {
            _updatingList = true;
            try
            {
                Guid? currentSelection = selectId;
                if (!currentSelection.HasValue && _sheetList.SelectedItem is SheetListItem selected)
                    currentSelection = selected.Sheet.Id;

                _sheetList.Items.Clear();
                IEnumerable<PdfSheetMapping> sheets = _state.Sheets
                    .OrderByDescending(s => s.IsPinned)
                    .ThenBy(s => GetCategoryOrder(s.Category))
                    .ThenByDescending(s => s.Priority)
                    .ThenBy(s => s.PageIndex)
                    .ThenBy(s => s.Name);

                foreach (PdfSheetMapping sheet in sheets)
                    _sheetList.Items.Add(new SheetListItem(sheet));

                if (currentSelection.HasValue)
                {
                    for (int i = 0; i < _sheetList.Items.Count; i++)
                    {
                        if (_sheetList.Items[i] is SheetListItem item && item.Sheet.Id == currentSelection.Value)
                        {
                            _sheetList.SelectedIndex = i;
                            return;
                        }
                    }
                }

                _sheetList.SelectedIndex = -1;
            }
            finally
            {
                _updatingList = false;
            }
        }

        private static int GetCategoryOrder(PdfSheetCategory category) => category switch
        {
            PdfSheetCategory.Plans => 0,
            PdfSheetCategory.Profiles => 1,
            PdfSheetCategory.Tables => 2,
            PdfSheetCategory.Details => 3,
            PdfSheetCategory.Notes => 4,
            _ => 99
        };

        private void OnSheetSelected()
        {
            if (_updatingList || _sheetList.SelectedItem is not SheetListItem item)
                return;

            CancelPendingReferenceView(silent: true);
            _autoFollow = false;
            DisplaySheet(item.Sheet, null, force: true);
        }

        private void MoveSheet(int direction)
        {
            if (_sheetList.Items.Count == 0)
                return;

            int index = _sheetList.SelectedIndex;
            if (index < 0)
                index = 0;
            else
                index = (index + direction + _sheetList.Items.Count) % _sheetList.Items.Count;
            _sheetList.SelectedIndex = index;
        }

        private void ReturnToCurrent()
        {
            _pdfUnlocked = false;
            _manualPdfBounds = null;
            _imagePanel.SetNavigationEnabled(false);
            _unlockPdf.Text = "UNLOCK PDF";
            _awaitingPdfPoint = false;
            _pickPdfPoint.Enabled = false;
            _pickPdfPoint.Visible = false;
            _pendingReference = null;
            _saveReferenceView.Enabled = false;
            _saveReferenceView.Visible = false;
            _autoFollow = true;
            RebuildSheetList();
            SyncToCurrentView(force: true);
        }

        private void TogglePdfUnlock()
        {
            if (_displayedSheet == null)
                return;

            _pdfUnlocked = !_pdfUnlocked;
            _imagePanel.SetNavigationEnabled(_pdfUnlocked);
            _unlockPdf.Text = _pdfUnlocked ? "LOCK PDF" : "UNLOCK PDF";

            if (_pdfUnlocked)
            {
                _autoFollow = false;
                _manualPdfBounds = _imagePanel.CurrentPdfBounds;
                _status.Text = "PDF unlocked. Use the mouse wheel to zoom and drag to pan. RETURN TO CURRENT restores model-space tracking.";
            }
            else
            {
                if (_pendingReference != null)
                {
                    _pdfUnlocked = true;
                    _imagePanel.SetNavigationEnabled(true);
                    _unlockPdf.Text = "LOCK PDF";
                    _status.Text = "Finish the reference setup with SAVE REFERENCE VIEW, or use RETURN TO CURRENT to cancel.";
                    return;
                }

                _manualPdfBounds = null;
                _status.Text = "PDF navigation locked.";
            }
        }

        private void ArmPdfPointPick()
        {
            if (_pendingPlan == null)
                return;

            _awaitingPdfPoint = true;
            _pickPdfPoint.Enabled = false;
            _status.Text = _pendingPdfPoint1 == null
                ? "Click the first known point in the PDF. Pan and zoom are paused until the click is made."
                : "Click the second known point in the PDF. Pan and zoom are paused until the click is made.";
            _imagePanel.FocusPdf();
        }

        private void OnManualViewRequested(RectangleF bounds)
        {
            if (!_pdfUnlocked || _displayedSheet == null)
                return;

            _manualPdfBounds = bounds;
            DisplaySheet(_displayedSheet, null, force: true);
        }

        private void OpenPdf()
        {
            Document? document = AcApplication.DocumentManager.MdiActiveDocument;
            if (document == null)
                return;

            using OpenFileDialog dialog = new()
            {
                Filter = "PDF files (*.pdf)|*.pdf",
                Title = "Select plan PDF"
            };
            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            string selectedPath = dialog.FileName;
            _status.Text = $"Reading {Path.GetFileName(selectedPath)}...";
            _status.Refresh();

            if (!TryLoadPdfMetadata(selectedPath, out int pageCount, out string error))
            {
                _status.Text = $"PDF load failed: {error}";
                MessageBox.Show(
                    "The selected PDF could not be opened by the isolated renderer.\n\n" + error +
                    "\n\nMultipage PDF documents are supported and do not need to be split into individual files.",
                    "PDF Viewer",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            PdfViewerStorage.SetPdfPath(_state, document.Database, selectedPath);
            _resolvedPdfPath = selectedPath;
            _pageCount = pageCount;
            SaveState();
            RebuildSheetList();
            _status.Text = $"Loaded {Path.GetFileName(_resolvedPdfPath)} ({_pageCount} pages).";
        }

        private void LoadPdfMetadata()
        {
            _pageCount = 0;
            if (string.IsNullOrWhiteSpace(_resolvedPdfPath) || !File.Exists(_resolvedPdfPath))
                return;

            if (TryLoadPdfMetadata(_resolvedPdfPath, out int pageCount, out string error))
            {
                _pageCount = pageCount;
                return;
            }

            _status.Text = $"PDF load failed: {error}";
        }

        private static bool TryLoadPdfMetadata(string pdfPath, out int pageCount, out string error)
        {
            pageCount = 0;
            error = string.Empty;

            try
            {
                if (string.IsNullOrWhiteSpace(pdfPath))
                {
                    error = "No PDF path was supplied.";
                    return false;
                }

                if (!File.Exists(pdfPath))
                {
                    error = $"The PDF file was not found: {pdfPath}";
                    return false;
                }

                pageCount = PdfRenderClient.GetPageCount(pdfPath);
                if (pageCount <= 0)
                {
                    error = "The renderer returned zero pages.";
                    return false;
                }

                return true;
            }
            catch (WinException ex)
            {
                error = GetConciseExceptionMessage(ex);
                return false;
            }
        }

        private static string GetConciseExceptionMessage(System.Exception exception)
        {
            System.Exception current = exception;
            while (current.InnerException != null)
                current = current.InnerException;

            string message = current.Message;
            if (string.IsNullOrWhiteSpace(message))
                message = exception.Message;
            return string.IsNullOrWhiteSpace(message)
                ? "The renderer returned an unknown error."
                : message.Trim();
        }

        private void BeginPlanMapping()
        {
            CancelPendingReferenceView(silent: true);
            if (!EnsurePdfReady())
                return;

            using SheetSetupDialog dialog = new(_pageCount, PdfSheetCategory.Plans, allowCategoryChange: false);
            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            SizeF pageSize = PdfRenderClient.GetPageSize(_resolvedPdfPath, dialog.PageIndex);
            _pendingPlan = new PdfSheetMapping
            {
                Name = dialog.SheetName,
                PageIndex = dialog.PageIndex,
                Category = PdfSheetCategory.Plans,
                HasModelMapping = true,
                PdfPageHeight = pageSize.Height
            };
            _pendingPdfPoint1 = null;
            _awaitingPdfPoint = false;
            _pdfUnlocked = true;
            _manualPdfBounds = null;
            _imagePanel.SetNavigationEnabled(true);
            _unlockPdf.Text = "LOCK PDF";
            _pickPdfPoint.Text = "PICK FIRST PDF POINT";
            _pickPdfPoint.Visible = true;
            _pickPdfPoint.Enabled = true;
            DisplaySheet(_pendingPlan, null, force: true);
            _imagePanel.FocusPdf();
            _status.Text = "Calibration: pan with left-drag and zoom with the mouse wheel. When ready, click PICK FIRST PDF POINT.";
        }

        private void OnPdfPointClicked(PointF point)
        {
            if (_pendingPlan == null || !_awaitingPdfPoint)
                return;

            _awaitingPdfPoint = false;
            Document? document = AcApplication.DocumentManager.MdiActiveDocument;
            if (document == null)
                return;

            if (_pendingPdfPoint1 == null)
            {
                PromptPointResult result = document.Editor.GetPoint("\nSelect the matching first point in model space: ");
                if (result.Status != PromptStatus.OK)
                {
                    CancelPendingMapping();
                    return;
                }

                PointF cartesianPoint = ToPdfCartesian(point, _pendingPlan.PdfPageHeight);
                _pendingPdfPoint1 = cartesianPoint;
                _pendingPlan.PdfPoint1 = new PdfPoint { X = cartesianPoint.X, Y = cartesianPoint.Y };
                _pendingPlan.DrawingPoint1 = new DrawingPoint { X = result.Value.X, Y = result.Value.Y };
                _pickPdfPoint.Text = "PICK SECOND PDF POINT";
                _pickPdfPoint.Enabled = true;
                _imagePanel.FocusPdf();
                _status.Text = "First pair saved. Pan and zoom to the second location, then click PICK SECOND PDF POINT.";
                return;
            }

            PromptPointResult second = document.Editor.GetPoint("\nSelect the matching second point in model space: ");
            if (second.Status != PromptStatus.OK)
            {
                CancelPendingMapping();
                return;
            }

            PointF secondCartesianPoint = ToPdfCartesian(point, _pendingPlan.PdfPageHeight);
            _pendingPlan.PdfPoint2 = new PdfPoint { X = secondCartesianPoint.X, Y = secondCartesianPoint.Y };
            _pendingPlan.DrawingPoint2 = new DrawingPoint { X = second.Value.X, Y = second.Value.Y };

            try
            {
                _ = SimilarityTransform2D.Create(_pendingPlan);
                using ViewTableRecord view = document.Editor.GetCurrentView();
                DrawingBounds currentViewBounds = GetDrawingBounds(view);
                CoveragePromptResult coverage = PromptForCoverageBoundary(document, currentViewBounds);
                if (!coverage.Accepted)
                    coverage = CoveragePromptResult.CurrentView(currentViewBounds);
                _pendingPlan.Coverage = coverage.Bounds;
                _pendingPlan.CoveragePolygon = coverage.Vertices;
                _pendingPlan.HasCustomCoverage = coverage.IsCustom;
                _state.Sheets.Add(_pendingPlan);
                Guid id = _pendingPlan.Id;
                SaveState();
                _pendingPlan = null;
                _pendingPdfPoint1 = null;
                RebuildSheetList(id);
                _pdfUnlocked = false;
                _manualPdfBounds = null;
                _imagePanel.SetNavigationEnabled(false);
                _unlockPdf.Text = "UNLOCK PDF";
                _awaitingPdfPoint = false;
                _pickPdfPoint.Enabled = false;
                _pickPdfPoint.Visible = false;
                _autoFollow = true;
                _status.Text = _state.Sheets.Last(s => s.Id == id).HasCustomCoverage
                    ? "Plan sheet mapped with a custom polygon/polyline coverage boundary."
                    : "Plan sheet mapped. Coverage uses the current model-space view.";
                SyncToCurrentView(force: true);
            }
            catch (WinException ex)
            {
                _status.Text = $"Calibration failed: {ex.Message}";
                CancelPendingMapping();
            }
        }

        private void CancelPendingMapping()
        {
            bool hadPendingPlan = _pendingPlan != null;
            _pendingPlan = null;
            _pendingPdfPoint1 = null;
            _pdfUnlocked = false;
            _manualPdfBounds = null;
            _imagePanel.SetNavigationEnabled(false);
            _unlockPdf.Text = "UNLOCK PDF";
            _awaitingPdfPoint = false;
            _pickPdfPoint.Enabled = false;
            _pickPdfPoint.Visible = false;
            if (hadPendingPlan)
                _status.Text = "Plan mapping cancelled.";
        }

        private static PointF ToPdfCartesian(PointF topLeftPoint, double pageHeight) =>
            new(topLeftPoint.X, (float)(pageHeight - topLeftPoint.Y));

        private void AddReferenceSheet()
        {
            CancelPendingMapping();
            if (!EnsurePdfReady())
                return;

            using SheetSetupDialog dialog = new(_pageCount, PdfSheetCategory.Tables, allowCategoryChange: true);
            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            _pendingReference = new PdfSheetMapping
            {
                Name = dialog.SheetName,
                PageIndex = dialog.PageIndex,
                Category = dialog.Category,
                IsPinned = dialog.IsPinned,
                HasModelMapping = false
            };

            _autoFollow = false;
            _pdfUnlocked = true;
            _manualPdfBounds = null;
            _imagePanel.SetNavigationEnabled(true);
            _unlockPdf.Text = "LOCK PDF";
            _saveReferenceView.Visible = true;
            _saveReferenceView.Enabled = true;
            DisplaySheet(_pendingReference, null, force: true);
            _imagePanel.FocusPdf();
            _status.Text = "Reference view setup: pan with left-drag and zoom with the mouse wheel, then click SAVE REFERENCE VIEW.";
        }

        private void SavePendingReferenceView()
        {
            if (_pendingReference == null)
                return;

            RectangleF? currentBounds = _imagePanel.CurrentPdfBounds;
            if (!currentBounds.HasValue || currentBounds.Value.Width <= 0.01f || currentBounds.Value.Height <= 0.01f)
            {
                _status.Text = "Unable to save the reference view because the current PDF view is invalid.";
                return;
            }

            _pendingReference.HasSavedPdfView = true;
            _pendingReference.SavedPdfView = PdfViewBounds.FromRectangleF(currentBounds.Value);
            _state.Sheets.Add(_pendingReference);
            Guid id = _pendingReference.Id;
            string name = _pendingReference.Name;
            SaveState();

            _pendingReference = null;
            _pdfUnlocked = false;
            _manualPdfBounds = null;
            _imagePanel.SetNavigationEnabled(false);
            _unlockPdf.Text = "UNLOCK PDF";
            _saveReferenceView.Enabled = false;
            _saveReferenceView.Visible = false;
            _autoFollow = false;
            RebuildSheetList(id);
            if (_sheetList.SelectedItem is SheetListItem item)
                DisplaySheet(item.Sheet, null, force: true);
            _status.Text = $"Saved reference view '{name}'. RETURN TO CURRENT restores model-space tracking.";
        }

        private void CancelPendingReferenceView(bool silent = false)
        {
            if (_pendingReference == null)
                return;

            _pendingReference = null;
            _pdfUnlocked = false;
            _manualPdfBounds = null;
            _imagePanel.SetNavigationEnabled(false);
            _unlockPdf.Text = "UNLOCK PDF";
            _saveReferenceView.Enabled = false;
            _saveReferenceView.Visible = false;
            if (!silent)
                _status.Text = "Reference view setup cancelled.";
        }

        private void RemoveSelectedSheet()
        {
            if (_sheetList.SelectedItem is not SheetListItem item)
                return;

            if (MessageBox.Show(
                    $"Remove '{item.Sheet.Name}'?",
                    "PDF Viewer",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) != DialogResult.Yes)
                return;

            _state.Sheets.RemoveAll(s => s.Id == item.Sheet.Id);
            SaveState();
            _displayedSheet = null;
            ClearBoundaryHighlight();
            _imagePanel.SetImage(null, null);
            RebuildSheetList();
            SyncToCurrentView(force: true);
        }

        private bool EnsurePdfReady()
        {
            if (!string.IsNullOrWhiteSpace(_resolvedPdfPath) && File.Exists(_resolvedPdfPath) && _pageCount > 0)
                return true;

            _status.Text = "Open or relink the PDF first.";
            OpenPdf();
            return !string.IsNullOrWhiteSpace(_resolvedPdfPath) && _pageCount > 0;
        }

        private void SyncToCurrentView(bool force = false)
        {
            if (_syncInProgress || _refreshingDocument || !_autoFollow || _pendingPlan != null || _pendingReference != null || !Visible)
                return;

            Document? document = AcApplication.DocumentManager.MdiActiveDocument;
            if (document == null || document.Database.TileMode == false)
                return;

            if (!ReferenceEquals(_activeDocument, document))
            {
                RefreshForActiveDocument();
                return;
            }

            _syncInProgress = true;
            try
            {
                using ViewTableRecord view = document.Editor.GetCurrentView();
                ViewSnapshot snapshot = ViewSnapshot.From(view);
                if (!force && _lastView.HasValue && _lastView.Value.NearlyEquals(snapshot))
                    return;
                _lastView = snapshot;

                PdfSheetMapping? current = _state.Sheets
                    .Where(s => s.Category == PdfSheetCategory.Plans && s.HasModelMapping)
                    .Where(s => s.ContainsCoverage(view.CenterPoint.X, view.CenterPoint.Y))
                    .OrderBy(s => s.GetCoverageArea())
                    .ThenByDescending(s => s.Priority)
                    .ThenByDescending(s => _displayedSheet?.Id == s.Id)
                    .FirstOrDefault();

                if (current == null)
                {
                    _status.Text = "No mapped plan sheet covers the current model-space view.";
                    return;
                }

                if (_displayedSheet?.Id != current.Id)
                    RebuildSheetList(current.Id);
                DisplaySheet(current, view, force);
            }
            catch (System.Exception ex)
            {
                _status.Text = $"View synchronization failed: {ex.Message}";
            }
            finally
            {
                _syncInProgress = false;
            }
        }

        private void DisplaySheet(PdfSheetMapping sheet, ViewTableRecord? view, bool force)
        {
            if (!EnsurePdfReady())
                return;
            if (!force && _displayedSheet?.Id == sheet.Id && view == null)
                return;

            RectangleF? bounds = _pdfUnlocked ? _manualPdfBounds : null;
            if (!_pdfUnlocked && !sheet.HasModelMapping && sheet.HasSavedPdfView)
                bounds = sheet.SavedPdfView.ToRectangleF();
            if (!_pdfUnlocked && view != null && sheet.HasModelMapping)
            {
                SimilarityTransform2D transform = SimilarityTransform2D.Create(sheet);
                DrawingBounds drawingBounds = GetDrawingBounds(view);
                PointF p1 = transform.DrawingToPdf(drawingBounds.MinX, drawingBounds.MinY);
                PointF p2 = transform.DrawingToPdf(drawingBounds.MaxX, drawingBounds.MinY);
                PointF p3 = transform.DrawingToPdf(drawingBounds.MaxX, drawingBounds.MaxY);
                PointF p4 = transform.DrawingToPdf(drawingBounds.MinX, drawingBounds.MaxY);
                float minX = new[] { p1.X, p2.X, p3.X, p4.X }.Min();
                float maxX = new[] { p1.X, p2.X, p3.X, p4.X }.Max();
                float minCartesianY = new[] { p1.Y, p2.Y, p3.Y, p4.Y }.Min();
                float maxCartesianY = new[] { p1.Y, p2.Y, p3.Y, p4.Y }.Max();
                float pageHeight = sheet.PdfPageHeight > 0
                    ? (float)sheet.PdfPageHeight
                    : PdfRenderClient.GetPageSize(_resolvedPdfPath, sheet.PageIndex).Height;
                bounds = RectangleF.FromLTRB(
                    minX,
                    pageHeight - maxCartesianY,
                    maxX,
                    pageHeight - minCartesianY);
            }

            try
            {
                int width = Math.Max(400, _imagePanel.ClientSize.Width);
                int height = Math.Max(300, _imagePanel.ClientSize.Height);
                SizeF pageSize = PdfRenderClient.GetPageSize(_resolvedPdfPath, sheet.PageIndex);

                RectangleF pageRectangle = new(0, 0, pageSize.Width, pageSize.Height);
                if (bounds.HasValue)
                {
                    RectangleF clipped = RectangleF.Intersect(pageRectangle, bounds.Value);
                    bounds = clipped.Width > 0.01f && clipped.Height > 0.01f ? clipped : pageRectangle;
                }

                Bitmap image = PdfRenderClient.Render(
                    _resolvedPdfPath,
                    sheet.PageIndex,
                    width,
                    height,
                    bounds);
                _imagePanel.SetImage(image, bounds ?? pageRectangle);
                _displayedSheet = sheet;
                ShowBoundaryHighlight(sheet);
                _status.Text = $"{sheet.Name}  |  Page {sheet.PageIndex + 1} of {_pageCount}" +
                    (_autoFollow ? "  |  Following model space" : string.Empty);
            }
            catch (WinException ex)
            {
                _status.Text = $"PDF render failed: {ex.Message}";
            }
        }

        private static CoveragePromptResult PromptForCoverageBoundary(Document document, DrawingBounds fallback)
        {
            Editor editor = document.Editor;
            PromptKeywordOptions options = new(
                "\nDefine sheet coverage [Current view/Rectangle/Polygon] <Current view>: ");
            options.AllowNone = true;
            options.Keywords.Add("Current");
            options.Keywords.Add("Rectangle");
            options.Keywords.Add("Polygon");

            PromptResult choice = editor.GetKeywords(options);
            if (choice.Status == PromptStatus.Cancel)
                return CoveragePromptResult.Cancelled();
            if (choice.Status == PromptStatus.None || choice.StringResult == "Current")
                return CoveragePromptResult.CurrentView(fallback);

            return choice.StringResult switch
            {
                "Rectangle" => PromptForRectangleCoverage(editor),
                "Polygon" => PromptForPolygonCoverage(editor),
                _ => CoveragePromptResult.CurrentView(fallback)
            };
        }

        private static CoveragePromptResult PromptForRectangleCoverage(Editor editor)
        {
            PromptPointResult first = editor.GetPoint("\nSpecify first corner of sheet coverage rectangle: ");
            if (first.Status != PromptStatus.OK)
                return CoveragePromptResult.Cancelled();

            PromptCornerOptions cornerOptions = new("\nSpecify opposite corner of sheet coverage rectangle: ", first.Value);
            PromptPointResult opposite = editor.GetCorner(cornerOptions);
            if (opposite.Status != PromptStatus.OK)
                return CoveragePromptResult.Cancelled();

            DrawingBounds bounds = new()
            {
                MinX = Math.Min(first.Value.X, opposite.Value.X),
                MinY = Math.Min(first.Value.Y, opposite.Value.Y),
                MaxX = Math.Max(first.Value.X, opposite.Value.X),
                MaxY = Math.Max(first.Value.Y, opposite.Value.Y)
            };
            return CoveragePromptResult.Custom(bounds, CoverageGeometry.Rectangle(bounds));
        }

        private static CoveragePromptResult PromptForPolygonCoverage(Editor editor)
        {
            PromptPointResult first = editor.GetPoint("\nSpecify first polygon vertex: ");
            if (first.Status != PromptStatus.OK)
                return CoveragePromptResult.Cancelled();

            List<CoverageVertex> vertices = new()
            {
                new CoverageVertex { X = first.Value.X, Y = first.Value.Y }
            };

            while (true)
            {
                PromptPointOptions nextOptions = new(
                    vertices.Count < 3
                        ? "\nSpecify next polygon vertex: "
                        : "\nSpecify next polygon vertex or press Enter to close: ")
                {
                    AllowNone = vertices.Count >= 3,
                    UseBasePoint = true,
                    BasePoint = new Point3d(vertices[^1].X, vertices[^1].Y, 0)
                };
                PromptPointResult next = editor.GetPoint(nextOptions);
                if (next.Status == PromptStatus.None && vertices.Count >= 3)
                    break;
                if (next.Status != PromptStatus.OK)
                    return CoveragePromptResult.Cancelled();

                CoverageVertex prior = vertices[^1];
                if (Math.Abs(next.Value.X - prior.X) < 1e-8 && Math.Abs(next.Value.Y - prior.Y) < 1e-8)
                    continue;

                vertices.Add(new CoverageVertex { X = next.Value.X, Y = next.Value.Y });
            }

            DrawingBounds bounds = CoverageGeometry.GetBounds(vertices);
            return CoveragePromptResult.Custom(bounds, vertices);
        }

        private void EditSelectedBoundary()
        {
            if (_sheetList.SelectedItem is not SheetListItem item || !item.Sheet.HasModelMapping)
            {
                _status.Text = "Select a mapped plan sheet before editing its coverage boundary.";
                return;
            }

            Document? document = AcApplication.DocumentManager.MdiActiveDocument;
            if (document == null)
                return;

            DrawingBounds fallback = item.Sheet.Coverage;
            if (Math.Abs(fallback.MaxX - fallback.MinX) < 1e-8 || Math.Abs(fallback.MaxY - fallback.MinY) < 1e-8)
            {
                using ViewTableRecord view = document.Editor.GetCurrentView();
                fallback = GetDrawingBounds(view);
            }

            CoveragePromptResult result = PromptForCoverageBoundary(document, fallback);
            if (!result.Accepted)
            {
                _status.Text = "Coverage boundary edit cancelled.";
                return;
            }

            item.Sheet.Coverage = result.Bounds;
            item.Sheet.CoveragePolygon = result.Vertices;
            item.Sheet.HasCustomCoverage = result.IsCustom;
            SaveState();
            ShowBoundaryHighlight(item.Sheet);
            _status.Text = result.IsCustom
                ? $"Updated '{item.Sheet.Name}' with a polygon coverage boundary."
                : $"Updated '{item.Sheet.Name}' to use the current model-space view as coverage.";
        }

        private void ShowBoundaryHighlight(PdfSheetMapping sheet)
        {
            ClearBoundaryHighlight();
            if (!sheet.HasModelMapping)
                return;

            List<CoverageVertex> vertices = sheet.CoveragePolygon.Count >= 3
                ? sheet.CoveragePolygon
                : CoverageGeometry.Rectangle(sheet.Coverage);
            if (vertices.Count < 3)
                return;

            Autodesk.AutoCAD.DatabaseServices.Polyline polyline = new(vertices.Count)
            {
                Closed = true,
                // Light orange highlight, slightly heavier than normal linework, and partially transparent.
                Color = Autodesk.AutoCAD.Colors.Color.FromRgb(255, 178, 92),
                LineWeight = LineWeight.LineWeight070,
                Transparency = new Autodesk.AutoCAD.Colors.Transparency(166)
            };
            for (int i = 0; i < vertices.Count; i++)
            {
                CoverageVertex vertex = vertices[i];
                polyline.AddVertexAt(i, new Point2d(vertex.X, vertex.Y), vertex.Bulge, 0, 0);
            }

            IntegerCollection viewports = new();
            TransientManager.CurrentTransientManager.AddTransient(
                polyline,
                TransientDrawingMode.DirectShortTerm,
                128,
                viewports);
            _boundaryTransient = polyline;
        }

        private void ClearBoundaryHighlight()
        {
            if (_boundaryTransient == null)
                return;

            try
            {
                IntegerCollection viewports = new();
                TransientManager.CurrentTransientManager.EraseTransient(_boundaryTransient, viewports);
            }
            catch
            {
                // Civil 3D may already have cleared transients during a document/view change.
            }
            finally
            {
                _boundaryTransient.Dispose();
                _boundaryTransient = null;
            }
        }

        private static DrawingBounds GetDrawingBounds(ViewTableRecord view)
        {
            double halfWidth = view.Width / 2.0;
            double halfHeight = view.Height / 2.0;
            return new DrawingBounds
            {
                MinX = view.CenterPoint.X - halfWidth,
                MinY = view.CenterPoint.Y - halfHeight,
                MaxX = view.CenterPoint.X + halfWidth,
                MaxY = view.CenterPoint.Y + halfHeight
            };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _syncTimer.Stop();
                ClearBoundaryHighlight();
            }
            base.Dispose(disposing);
        }

        private void SaveState()
        {
            Document? document = AcApplication.DocumentManager.MdiActiveDocument;
            if (document == null)
                return;

            using (document.LockDocument())
                PdfViewerStorage.Save(document.Database, _state);
        }

        private sealed class CoveragePromptResult
        {
            public bool Accepted { get; init; }
            public bool IsCustom { get; init; }
            public DrawingBounds Bounds { get; init; } = new();
            public List<CoverageVertex> Vertices { get; init; } = new();

            public static CoveragePromptResult Cancelled() => new() { Accepted = false };

            public static CoveragePromptResult CurrentView(DrawingBounds bounds) => new()
            {
                Accepted = true,
                IsCustom = false,
                Bounds = bounds,
                Vertices = CoverageGeometry.Rectangle(bounds)
            };

            public static CoveragePromptResult Custom(DrawingBounds bounds, List<CoverageVertex> vertices) => new()
            {
                Accepted = true,
                IsCustom = true,
                Bounds = bounds,
                Vertices = vertices
            };
        }

        private sealed class SheetListItem
        {
            public SheetListItem(PdfSheetMapping sheet) => Sheet = sheet;
            public PdfSheetMapping Sheet { get; }
            public override string ToString() => $"{(Sheet.IsPinned ? "★ " : string.Empty)}[{GetCategoryLabel(Sheet.Category)}]  {Sheet.Name}  (P{Sheet.PageIndex + 1})";

            private static string GetCategoryLabel(PdfSheetCategory category) => category switch
            {
                PdfSheetCategory.Plans => "PLAN",
                PdfSheetCategory.Profiles => "PROFILE",
                PdfSheetCategory.Tables => "TABLE",
                PdfSheetCategory.Details => "DETAIL",
                PdfSheetCategory.Notes => "NOTES",
                _ => category.ToString().ToUpperInvariant()
            };
        }

        private readonly record struct ViewSnapshot(double X, double Y, double Width, double Height)
        {
            public static ViewSnapshot From(ViewTableRecord view) => new(view.CenterPoint.X, view.CenterPoint.Y, view.Width, view.Height);
            public bool NearlyEquals(ViewSnapshot other)
            {
                double tolerance = Math.Max(Width, Height) * 0.0005;
                return Math.Abs(X - other.X) <= tolerance &&
                       Math.Abs(Y - other.Y) <= tolerance &&
                       Math.Abs(Width - other.Width) <= tolerance &&
                       Math.Abs(Height - other.Height) <= tolerance;
            }
        }
    }

    internal sealed class PdfImagePanel : Panel
    {
        private DrawingImage? _image;
        private RectangleF? _pdfBounds;
        private bool _navigationEnabled;
        private bool _dragging;
        private bool _suppressClick;
        private Point _dragStart;
        private RectangleF _dragStartBounds;

        public event Action<PointF>? PdfPointClicked;
        public event Action<RectangleF>? ManualViewRequested;

        public RectangleF? CurrentPdfBounds => _pdfBounds;

        public PdfImagePanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Cross;
        }

        public void FocusPdf()
        {
            if (CanFocus)
                Focus();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            FocusPdf();
        }

        public void SetNavigationEnabled(bool enabled)
        {
            _navigationEnabled = enabled;
            _dragging = false;
            Cursor = enabled ? Cursors.Hand : Cursors.Cross;
        }

        public void SetImage(DrawingImage? image, RectangleF? pdfBounds)
        {
            DrawingImage? old = _image;
            _image = image;
            _pdfBounds = pdfBounds;
            old?.Dispose();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (_image == null)
            {
                using Brush brush = new SolidBrush(System.Drawing.Color.Gainsboro);
                e.Graphics.DrawString("No PDF sheet displayed", Font, brush, 12, 12);
                return;
            }

            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            Rectangle destination = FitRectangle(_image.Size, ClientRectangle);
            e.Graphics.DrawImage(_image, destination);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!_navigationEnabled || _image == null || !_pdfBounds.HasValue)
                return;

            Rectangle destination = FitRectangle(_image.Size, ClientRectangle);
            if (!destination.Contains(e.Location))
                return;

            RectangleF source = _pdfBounds.Value;
            float normalizedX = (e.X - destination.Left) / (float)destination.Width;
            float normalizedY = (e.Y - destination.Top) / (float)destination.Height;
            float anchorX = source.Left + (normalizedX * source.Width);
            float anchorY = source.Top + (normalizedY * source.Height);
            float factor = e.Delta > 0 ? 0.75f : 1.333333f;
            float newWidth = Math.Max(0.01f, source.Width * factor);
            float newHeight = Math.Max(0.01f, source.Height * factor);
            RectangleF next = new(
                anchorX - (normalizedX * newWidth),
                anchorY - (normalizedY * newHeight),
                newWidth,
                newHeight);
            ManualViewRequested?.Invoke(next);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            FocusPdf();
            if (!_navigationEnabled || e.Button != MouseButtons.Left || !_pdfBounds.HasValue || _image == null)
                return;

            Rectangle destination = FitRectangle(_image.Size, ClientRectangle);
            if (!destination.Contains(e.Location))
                return;

            _dragging = true;
            _suppressClick = false;
            _dragStart = e.Location;
            _dragStartBounds = _pdfBounds.Value;
            Capture = true;
            Cursor = Cursors.SizeAll;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_dragging || _image == null)
                return;

            Rectangle destination = FitRectangle(_image.Size, ClientRectangle);
            if (destination.Width <= 0 || destination.Height <= 0)
                return;

            if (Math.Abs(e.X - _dragStart.X) > 3 || Math.Abs(e.Y - _dragStart.Y) > 3)
                _suppressClick = true;

            float dx = (e.X - _dragStart.X) * (_dragStartBounds.Width / destination.Width);
            float dy = (e.Y - _dragStart.Y) * (_dragStartBounds.Height / destination.Height);
            RectangleF next = new(
                _dragStartBounds.Left - dx,
                _dragStartBounds.Top - dy,
                _dragStartBounds.Width,
                _dragStartBounds.Height);
            ManualViewRequested?.Invoke(next);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_dragging)
                return;

            _dragging = false;
            Capture = false;
            Cursor = _navigationEnabled ? Cursors.Hand : Cursors.Cross;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (_image == null)
                return;
            if (_suppressClick)
            {
                _suppressClick = false;
                return;
            }

            Rectangle destination = FitRectangle(_image.Size, ClientRectangle);
            if (!destination.Contains(e.Location))
                return;

            float normalizedX = (e.X - destination.Left) / (float)destination.Width;
            float normalizedY = (e.Y - destination.Top) / (float)destination.Height;
            RectangleF source = _pdfBounds ?? new RectangleF(0, 0, _image.Width, _image.Height);
            PdfPointClicked?.Invoke(new PointF(
                source.Left + (normalizedX * source.Width),
                source.Top + (normalizedY * source.Height)));
        }


        private static Rectangle FitRectangle(Size image, Rectangle client)
        {
            if (image.Width <= 0 || image.Height <= 0 || client.Width <= 0 || client.Height <= 0)
                return Rectangle.Empty;

            double scale = Math.Min(client.Width / (double)image.Width, client.Height / (double)image.Height);
            int width = Math.Max(1, (int)Math.Round(image.Width * scale));
            int height = Math.Max(1, (int)Math.Round(image.Height * scale));
            return new Rectangle(
                client.Left + ((client.Width - width) / 2),
                client.Top + ((client.Height - height) / 2),
                width,
                height);
        }
    }

    internal sealed class SheetSetupDialog : Form
    {
        private readonly TextBox _name = new();
        private readonly NumericUpDown _page = new();
        private readonly ComboBox _category = new();
        private readonly CheckBox _pinned = new();

        public SheetSetupDialog(int pageCount, PdfSheetCategory initialCategory, bool allowCategoryChange)
        {
            Text = allowCategoryChange ? "Add PDF Reference" : "Add Plan Mapping";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new Size(360, allowCategoryChange ? 205 : 165);

            TableLayoutPanel layout = new()
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(10),
                ColumnCount = 2,
                RowCount = allowCategoryChange ? 5 : 4
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            layout.Controls.Add(new Label { Text = "Name:", AutoSize = true }, 0, 0);
            _name.Dock = DockStyle.Fill;
            layout.Controls.Add(_name, 1, 0);

            layout.Controls.Add(new Label { Text = "PDF page:", AutoSize = true }, 0, 1);
            _page.Minimum = 1;
            _page.Maximum = Math.Max(1, pageCount);
            _page.Value = 1;
            layout.Controls.Add(_page, 1, 1);

            int buttonRow;
            if (allowCategoryChange)
            {
                layout.Controls.Add(new Label { Text = "Category:", AutoSize = true }, 0, 2);
                _category.DropDownStyle = ComboBoxStyle.DropDownList;
                _category.DataSource = Enum.GetValues<PdfSheetCategory>()
                    .Where(c => c != PdfSheetCategory.Plans)
                    .ToArray();
                _category.SelectedItem = initialCategory;
                layout.Controls.Add(_category, 1, 2);

                _pinned.Text = "Pin at top of list";
                _pinned.AutoSize = true;
                layout.Controls.Add(_pinned, 1, 3);
                buttonRow = 4;
            }
            else
            {
                _category.Items.Add(initialCategory);
                _category.SelectedIndex = 0;
                buttonRow = 3;
            }

            FlowLayoutPanel buttons = new() { AutoSize = true, FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft, Dock = DockStyle.Fill };
            Button ok = new() { Text = "OK", DialogResult = DialogResult.OK };
            Button cancel = new() { Text = "Cancel", DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(ok);
            buttons.Controls.Add(cancel);
            layout.Controls.Add(buttons, 0, buttonRow);
            layout.SetColumnSpan(buttons, 2);

            AcceptButton = ok;
            CancelButton = cancel;
            Controls.Add(layout);
        }

        public string SheetName => string.IsNullOrWhiteSpace(_name.Text) ? $"Page {_page.Value}" : _name.Text.Trim();
        public int PageIndex => (int)_page.Value - 1;
        public PdfSheetCategory Category => _category.SelectedItem is PdfSheetCategory value ? value : PdfSheetCategory.Tables;
        public bool IsPinned => _pinned.Checked;
    }
}
