using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CLV_CivilTools.Shared;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcPolyline = Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace CLV_CivilTools.Gis
{
    public static class GisLocateParcelCommands
    {
        [CommandMethod("CLV-LOCATE-PARCEL", CommandFlags.Modal)]
        [CommandMethod("CLVLOCATEPARCEL", CommandFlags.Modal)]
        public static void ShowLocateParcelDialogCommand()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            try
            {
                EnsureHybridMap(doc);
                using var form = new GisLocateParcelForm(doc);
                AcadApp.ShowModalDialog(form);
            }
            catch (System.Exception ex)
            {
                doc.Editor.WriteMessage($"\nCLV-LOCATE-PARCEL failed: {ex.Message}");
            }
        }

        public static void ShowLocateParcelDialogFromPalette()
        {
            ShowLocateParcelDialogCommand();
        }

        private static void EnsureHybridMap(Document doc)
        {
            try
            {
                doc.SendStringToExecute("._GEOMAP _Hybrid ", true, false, false);
            }
            catch
            {
            }
        }
    }

    internal enum ParcelSearchMode
    {
        Apn,
        Owner
    }

    internal sealed class ParcelSearchHit
    {
        public int RecordIndex { get; init; }
        public string Parcel { get; init; } = string.Empty;
        public string Owner { get; init; } = string.Empty;
        public string StreetName { get; init; } = string.Empty;
        public string StreetType { get; init; } = string.Empty;

        public string StreetDisplay
        {
            get
            {
                string street = string.Join(" ", new[] { StreetName, StreetType }
                    .Where(x => !string.IsNullOrWhiteSpace(x)));
                return street.Trim();
            }
        }

        public string DisplayText
        {
            get
            {
                string street = StreetDisplay;
                if (string.IsNullOrWhiteSpace(street))
                    return $"{Parcel} | {Owner}";

                return $"{Parcel} | {Owner} | {street}";
            }
        }

        public override string ToString() => DisplayText;
    }

    internal sealed class ParcelGeometry
    {
        public List<List<Point2d>> Parts { get; } = new();
        public Extents2d Extents { get; set; }

        public Point2d Center => new(
            (Extents.MinPoint.X + Extents.MaxPoint.X) * 0.5,
            (Extents.MinPoint.Y + Extents.MaxPoint.Y) * 0.5);

        public static ParcelGeometry FromParts(IEnumerable<List<Point2d>> parts)
        {
            var geometry = new ParcelGeometry();
            bool hasPoint = false;
            double xmin = 0.0;
            double ymin = 0.0;
            double xmax = 0.0;
            double ymax = 0.0;

            foreach (List<Point2d> part in parts)
            {
                var clone = new List<Point2d>(part);
                geometry.Parts.Add(clone);
                foreach (Point2d pt in clone)
                {
                    if (!hasPoint)
                    {
                        xmin = xmax = pt.X;
                        ymin = ymax = pt.Y;
                        hasPoint = true;
                    }
                    else
                    {
                        xmin = Math.Min(xmin, pt.X);
                        ymin = Math.Min(ymin, pt.Y);
                        xmax = Math.Max(xmax, pt.X);
                        ymax = Math.Max(ymax, pt.Y);
                    }
                }
            }

            geometry.Extents = hasPoint
                ? new Extents2d(new Point2d(xmin, ymin), new Point2d(xmax, ymax))
                : new Extents2d(new Point2d(0.0, 0.0), new Point2d(0.0, 0.0));
            return geometry;
        }
    }

    internal sealed class ParcelDataSource
    {
        public required string CoordinateSystem { get; init; }
        public required string DbfPath { get; init; }
        public required string ShapefilePath { get; init; }
        public string PrjPath => Path.ChangeExtension(ShapefilePath, ".prj");
    }

    internal static class GisParcelLocator
    {
        private const string ParcelDbfPathLvf = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_GRID_City\000-City\GIS\Assessor Parcels\LVF\Parcels-LVF.dbf";
        private const string ParcelDbfPathLvhef = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_GRID_City\000-City\GIS\Assessor Parcels\LVHEF\Parcels-LVHEF.dbf";
        private const string HighlightLayerName = "GIS-PARCEL-HILITE";
        private const string MarkerLayerName = "GIS-PARCEL-MARK";
        private const int MaxSearchResults = 200;

        public static string GetDbfPathForCurrentDrawing() => GetParcelDataSource(InferDrawingCoordinateSystem()).DbfPath;

        public static ParcelDataSource GetCurrentDataSource() => GetParcelDataSource(InferDrawingCoordinateSystem());

        public static List<ParcelSearchHit> Search(ParcelSearchMode mode, string rawQuery)
        {
            string query = rawQuery?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
                return new List<ParcelSearchHit>();

            ParcelDataSource dataSource = GetCurrentDataSource();
            if (!File.Exists(dataSource.DbfPath))
                throw new FileNotFoundException("Parcel DBF was not found.", dataSource.DbfPath);

            Regex matcher = BuildWildcardRegex(query);
            string normalizedQuery = NormalizeApnQuery(query);
            string targetField = mode == ParcelSearchMode.Apn ? "PARCEL" : "OWNER";
            var hits = new List<ParcelSearchHit>();

            foreach (DbfRecord record in DbfReader.ReadRecords(dataSource.DbfPath))
            {
                string candidate = record.GetValue(targetField);
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                string trimmedCandidate = candidate.Trim();
                bool isMatch = matcher.IsMatch(trimmedCandidate);
                if (!isMatch && mode == ParcelSearchMode.Apn)
                    isMatch = IsNormalizedApnMatch(normalizedQuery, trimmedCandidate);

                if (!isMatch)
                    continue;

                hits.Add(new ParcelSearchHit
                {
                    RecordIndex = record.RecordIndex,
                    Parcel = record.GetValue("PARCEL"),
                    Owner = record.GetValue("OWNER"),
                    StreetName = record.GetValue("STRNAME"),
                    StreetType = record.GetValue("STRTYPE")
                });

                if (hits.Count >= MaxSearchResults)
                    break;
            }

            return hits;
        }

        public static void LocateSelected(Document doc, ParcelSearchHit hit, bool insertMarker, bool bringHighlight)
        {
            if (doc == null)
                throw new ArgumentNullException(nameof(doc));
            if (hit == null)
                throw new ArgumentNullException(nameof(hit));

            string drawingCs = InferDrawingCoordinateSystem();
            ParcelDataSource dataSource = GetParcelDataSource(drawingCs);
            if (!File.Exists(dataSource.ShapefilePath))
                throw new FileNotFoundException("Parcel shapefile was not found.", dataSource.ShapefilePath);

            ParcelGeometry sourceGeometry = ShapefileReader.ReadPolygonGeometry(dataSource.ShapefilePath, hit.RecordIndex);
            if (sourceGeometry.Parts.Count == 0)
                throw new InvalidOperationException($"Parcel geometry was not found for record {hit.RecordIndex + 1}.");

            Editor ed = doc.Editor;
            ParcelGeometry geometry = TransformGeometryToDrawingCoordinateSystem(sourceGeometry, dataSource.CoordinateSystem, drawingCs, ed, dataSource.PrjPath);

            using var docLock = doc.LockDocument();
            Database db = doc.Database;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EnsureLayer(tr, db, HighlightLayerName, 6, LineWeight.LineWeight070);
                EnsureLayer(tr, db, MarkerLayerName, 1, LineWeight.LineWeight050);

                EraseLayerContents(tr, db, HighlightLayerName);
                EraseLayerContents(tr, db, MarkerLayerName);

                if (bringHighlight)
                {
                    foreach (List<Point2d> part in geometry.Parts)
                    {
                        if (part.Count < 2)
                            continue;

                        var pline = new AcPolyline();
                        for (int i = 0; i < part.Count; i++)
                        {
                            Point2d pt = part[i];
                            pline.AddVertexAt(i, pt, 0.0, 0.0, 0.0);
                        }

                        if (!part[0].IsEqualTo(part[^1]))
                            pline.Closed = true;

                        double polylineWidth = ComputeHighlightPolylineWidth(geometry.Extents);
                        pline.Layer = HighlightLayerName;
                        pline.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByLayer, 6);
                        pline.ConstantWidth = polylineWidth;
                        pline.LineWeight = LineWeight.ByLayer;
                        AppendEntityToModelSpace(tr, db, pline);
                    }
                }

                if (insertMarker)
                {
                    Point2d center = geometry.Center;
                    double maxSpan = Math.Max(geometry.Extents.MaxPoint.X - geometry.Extents.MinPoint.X,
                        geometry.Extents.MaxPoint.Y - geometry.Extents.MinPoint.Y);
                    double radius = Math.Max(10.0, maxSpan * 0.05);

                    var marker = new Circle(new Point3d(center.X, center.Y, 0.0), Vector3d.ZAxis, radius)
                    {
                        Layer = MarkerLayerName,
                        Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByLayer, 1),
                        LineWeight = LineWeight.LineWeight050
                    };
                    AppendEntityToModelSpace(tr, db, marker);
                }

                tr.Commit();
            }

            ZoomToExtents(ed, geometry.Extents);
            ed.WriteMessage($"\nCLV-LOCATE-PARCEL: located parcel {hit.Parcel} using {dataSource.CoordinateSystem} parcel source in {drawingCs}.");
        }

        private static double ComputeHighlightPolylineWidth(Extents2d extents)
        {
            double maxSpan = Math.Max(extents.MaxPoint.X - extents.MinPoint.X, extents.MaxPoint.Y - extents.MinPoint.Y);
            double width = maxSpan * 0.01;
            if (width < 2.0)
                width = 2.0;
            if (width > 8.0)
                width = 8.0;
            return width;
        }

        private static ParcelGeometry TransformGeometryToDrawingCoordinateSystem(ParcelGeometry sourceGeometry, string sourceCs, string drawingCs, Editor ed, string prjPath)
        {
            if (sourceGeometry == null)
                throw new ArgumentNullException(nameof(sourceGeometry));

            if (CoordinateSystemsEquivalent(sourceCs, drawingCs))
                return sourceGeometry;

            var transformedParts = new List<List<Point2d>>(sourceGeometry.Parts.Count);
            bool transformedAny = false;

            foreach (List<Point2d> part in sourceGeometry.Parts)
            {
                var transformedPart = new List<Point2d>(part.Count);
                foreach (Point2d point in part)
                {
                    Point2d transformed = CoordinateTransformUtility.TransformPoint(point, GetSourceCoordinateSystemCandidates(sourceCs, prjPath), GetTargetCoordinateSystemCandidates(drawingCs));
                    if (!point.IsEqualTo(transformed))
                        transformedAny = true;
                    transformedPart.Add(transformed);
                }
                transformedParts.Add(transformedPart);
            }

            if (!transformedAny)
            {
                ed.WriteMessage($"\nCLV-LOCATE-PARCEL: coordinate transform returned unchanged geometry. Source={sourceCs}, Target={drawingCs}.");
            }

            return ParcelGeometry.FromParts(transformedParts);
        }

        private static bool CoordinateSystemsEquivalent(string sourceCs, string targetCs)
        {
            string left = NormalizeCoordinateSystemName(sourceCs);
            string right = NormalizeCoordinateSystemName(targetCs);
            if (string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
                return true;

            bool leftEf = left.Contains("NV83EF", StringComparison.OrdinalIgnoreCase) || left.Contains("NEVADAEASTFIPS2701FEET", StringComparison.OrdinalIgnoreCase);
            bool rightEf = right.Contains("NV83EF", StringComparison.OrdinalIgnoreCase) || right.Contains("NEVADAEASTFIPS2701FEET", StringComparison.OrdinalIgnoreCase);
            return leftEf && rightEf;
        }

        private static string NormalizeCoordinateSystemName(string value)
        {
            return Regex.Replace(value ?? string.Empty, "[^A-Za-z0-9]", string.Empty).ToUpperInvariant();
        }

        private static IEnumerable<string> GetSourceCoordinateSystemCandidates(string sourceCs, string prjPath)
        {
            if (!string.IsNullOrWhiteSpace(prjPath) && File.Exists(prjPath))
            {
                string wkt = File.ReadAllText(prjPath).Trim();
                if (!string.IsNullOrWhiteSpace(wkt))
                    yield return wkt;
            }

            if (!string.IsNullOrWhiteSpace(sourceCs))
                yield return sourceCs;

            string normalized = NormalizeCoordinateSystemName(sourceCs);
            if (normalized.Contains("LVHEF", StringComparison.OrdinalIgnoreCase))
            {
                yield return "NV83.NCRS-LVHEF";
                yield return "NAD83 / Las Vegas High Easting (ftUS)";
            }
            else if (normalized.Contains("LVF", StringComparison.OrdinalIgnoreCase))
            {
                yield return "NV83.NCRS-LVF";
                yield return "NAD83 / Las Vegas Valley (ftUS)";
            }
        }

        private static IEnumerable<string> GetTargetCoordinateSystemCandidates(string drawingCs)
        {
            if (!string.IsNullOrWhiteSpace(drawingCs))
                yield return drawingCs.Trim();

            string normalized = NormalizeCoordinateSystemName(drawingCs);
            if (normalized.Contains("LVHEF", StringComparison.OrdinalIgnoreCase))
            {
                yield return "NV83.NCRS-LVHEF";
                yield return "NAD83 / Las Vegas High Easting (ftUS)";
            }
            else if (normalized.Contains("LVF", StringComparison.OrdinalIgnoreCase))
            {
                yield return "NV83.NCRS-LVF";
                yield return "NAD83 / Las Vegas Valley (ftUS)";
            }
            else if (normalized.Contains("EF", StringComparison.OrdinalIgnoreCase))
            {
            }
        }

        private static void EnsureLayer(Transaction tr, Database db, string layerName, short aciColor, LineWeight lineWeight)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(layerName))
            {
                LayerTableRecord existing = (LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForWrite);
                existing.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, aciColor);
                existing.LineWeight = lineWeight;
                return;
            }

            lt.UpgradeOpen();
            var ltr = new LayerTableRecord
            {
                Name = layerName,
                Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, aciColor),
                LineWeight = lineWeight
            };
            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }

        private static void EraseLayerContents(Transaction tr, Database db, string layerName)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            foreach (ObjectId id in ms)
            {
                if (!id.IsValid || id.IsErased)
                    continue;

                if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity ent)
                    continue;

                if (!string.Equals(ent.Layer, layerName, StringComparison.OrdinalIgnoreCase))
                    continue;

                ent.UpgradeOpen();
                ent.Erase();
            }
        }

        private static void AppendEntityToModelSpace(Transaction tr, Database db, Entity entity)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            ms.AppendEntity(entity);
            tr.AddNewlyCreatedDBObject(entity, true);
        }

        private static void ZoomToExtents(Editor ed, Extents2d extents)
        {
            double width = Math.Max(100.0, extents.MaxPoint.X - extents.MinPoint.X);
            double height = Math.Max(100.0, extents.MaxPoint.Y - extents.MinPoint.Y);
            Point3d center = new(
                (extents.MinPoint.X + extents.MaxPoint.X) * 0.5,
                (extents.MinPoint.Y + extents.MaxPoint.Y) * 0.5,
                0.0);

            ViewState.ZoomCenterByRect(center, width * 1.25, height * 1.25);
        }

        private static Regex BuildWildcardRegex(string query)
        {
            string escaped = Regex.Escape(query.Trim());
            escaped = escaped.Replace(@"\*", ".*").Replace(@"\?", ".");
            return new Regex("^" + escaped + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static string NormalizeApnQuery(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return string.Empty;

            return Regex.Replace(query.Trim(), "[^0-9*?]", string.Empty);
        }

        private static bool IsNormalizedApnMatch(string normalizedQuery, string candidate)
        {
            if (string.IsNullOrWhiteSpace(normalizedQuery))
                return false;

            string normalizedCandidate = Regex.Replace(candidate, "[^0-9]", string.Empty);
            string escaped = Regex.Escape(normalizedQuery).Replace(@"\*", ".*").Replace(@"\?", ".");
            return Regex.IsMatch(normalizedCandidate, "^" + escaped + "$", RegexOptions.CultureInvariant);
        }

        private static ParcelDataSource GetParcelDataSource(string drawingCs)
        {
            string normalized = NormalizeCoordinateSystemName(drawingCs);
            if (normalized.Contains("LVHEF", StringComparison.OrdinalIgnoreCase))
            {
                return BuildDataSource("NV83.NCRS-LVHEF", ParcelDbfPathLvhef);
            }

            return BuildDataSource("NV83.NCRS-LVF", ParcelDbfPathLvf);
        }

        private static ParcelDataSource BuildDataSource(string coordinateSystem, string dbfPath)
        {
            string shapefilePath = Path.ChangeExtension(dbfPath, ".shp");
            return new ParcelDataSource
            {
                CoordinateSystem = coordinateSystem,
                DbfPath = dbfPath,
                ShapefilePath = shapefilePath
            };
        }

        private static string InferDrawingCoordinateSystem()
        {
            try
            {
                MethodInfo? method = typeof(GisImportCommands).GetMethod(
                    "InferDrawingCoordinateSystem",
                    BindingFlags.Static | BindingFlags.NonPublic);

                if (method?.Invoke(null, null) is string cs && !string.IsNullOrWhiteSpace(cs))
                    return cs.Trim();
            }
            catch
            {
            }

            return "NV83.NCRS-LVF";
        }
    }

    internal sealed class GisLocateParcelForm : Form
    {
        private readonly Document _document;
        private readonly Editor _editor;
        private readonly ComboBox _cmbMode;
        private readonly TextBox _txtQuery;
        private readonly ListBox _lstResults;
        private readonly Label _lblPrompt;
        private readonly Label _lblStatus;
        private readonly Button _btnSearch;
        private readonly Button _btnOk;
        private List<ParcelSearchHit> _results = new();

        public GisLocateParcelForm(Document document)
        {
            _document = document;
            _editor = document.Editor;

            Text = "CLV LOCATE PARCEL";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new System.Drawing.Size(780, 420);

            var lblMode = new Label
            {
                Left = 12,
                Top = 14,
                Width = 120,
                Height = 18,
                Text = "Search type"
            };

            _cmbMode = new ComboBox
            {
                Left = 12,
                Top = 36,
                Width = 160,
                Height = 26,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbMode.Items.Add("APN");
            _cmbMode.Items.Add("OWNER");
            _cmbMode.SelectedIndex = 0;
            _cmbMode.SelectedIndexChanged += (s, e) => UpdatePrompt();

            _lblPrompt = new Label
            {
                Left = 184,
                Top = 14,
                Width = 420,
                Height = 18,
                Text = "APN or APN wildcard"
            };

            _txtQuery = new TextBox
            {
                Left = 184,
                Top = 36,
                Width = 480,
                Height = 26
            };
            _txtQuery.KeyDown += TxtQuery_KeyDown;

            var btnClose = new Button
            {
                Left = 676,
                Top = 12,
                Width = 90,
                Height = 28,
                Text = "Close",
                DialogResult = DialogResult.Cancel
            };

            _btnSearch = new Button
            {
                Left = 676,
                Top = 44,
                Width = 90,
                Height = 28,
                Text = "Search"
            };
            _btnSearch.Click += (s, e) => ExecuteSearch();

            var lblResults = new Label
            {
                Left = 12,
                Top = 82,
                Width = 120,
                Height = 18,
                Text = "Results"
            };

            _lstResults = new ListBox
            {
                Left = 12,
                Top = 104,
                Width = 754,
                Height = 218,
                HorizontalScrollbar = true
            };
            _lstResults.DoubleClick += (s, e) => ExecuteLocate();

            _lblStatus = new Label
            {
                Left = 12,
                Top = 334,
                Width = 520,
                Height = 42,
                Text = BuildStatusText()
            };

            _btnOk = new Button
            {
                Left = 666,
                Top = 360,
                Width = 100,
                Height = 30,
                Text = "OK",
                Enabled = false
            };
            _btnOk.Click += (s, e) => ExecuteLocate();

            Controls.Add(lblMode);
            Controls.Add(_cmbMode);
            Controls.Add(_lblPrompt);
            Controls.Add(_txtQuery);
            Controls.Add(btnClose);
            Controls.Add(_btnSearch);
            Controls.Add(lblResults);
            Controls.Add(_lstResults);
            Controls.Add(_lblStatus);
            Controls.Add(_btnOk);

            AcceptButton = _btnSearch;
            CancelButton = btnClose;
            UpdatePrompt();
        }

        private ParcelSearchMode SelectedMode => _cmbMode.SelectedIndex == 1 ? ParcelSearchMode.Owner : ParcelSearchMode.Apn;

        private void UpdatePrompt()
        {
            _lblPrompt.Text = SelectedMode == ParcelSearchMode.Apn
                ? "APN or APN wildcard"
                : "Owner name or owner wildcard";
        }

        private void ExecuteSearch()
        {
            string query = _txtQuery.Text.Trim();
            if (string.IsNullOrWhiteSpace(query))
            {
                MessageBox.Show(this,
                    SelectedMode == ParcelSearchMode.Apn ? "Enter an APN number." : "Enter an owner name.",
                    "CLV LOCATE PARCEL",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            try
            {
                _editor.WriteMessage($"\nCLV-LOCATE-PARCEL: searching {(SelectedMode == ParcelSearchMode.Apn ? "PARCEL" : "OWNER")} in {GisParcelLocator.GetDbfPathForCurrentDrawing()}");
                _results = GisParcelLocator.Search(SelectedMode, query);
                _lstResults.BeginUpdate();
                _lstResults.Items.Clear();
                foreach (ParcelSearchHit hit in _results)
                    _lstResults.Items.Add(hit);
                _lstResults.EndUpdate();

                if (_lstResults.Items.Count > 0)
                {
                    _lstResults.SelectedIndex = 0;
                    ParcelDataSource dataSource = GisParcelLocator.GetCurrentDataSource();
                    _lblStatus.Text = $"Found {_lstResults.Items.Count} result(s) from {Path.GetFileName(dataSource.DbfPath)}.";
                    _btnOk.Enabled = true;
                }
                else
                {
                    _lblStatus.Text = "Nothing Found.";
                    _btnOk.Enabled = false;
                }
            }
            catch (System.Exception ex)
            {
                _lblStatus.Text = "Search failed.";
                _btnOk.Enabled = false;
                _editor.WriteMessage($"\nCLV-LOCATE-PARCEL: {ex.Message}");
                MessageBox.Show(this, ex.Message, "CLV LOCATE PARCEL", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ExecuteLocate()
        {
            if (_lstResults.SelectedItem is not ParcelSearchHit hit)
            {
                MessageBox.Show(this, "Select a parcel result first.", "CLV LOCATE PARCEL", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                GisParcelLocator.LocateSelected(_document, hit, insertMarker: false, bringHighlight: true);
                Close();
            }
            catch (System.Exception ex)
            {
                _editor.WriteMessage($"\nCLV-LOCATE-PARCEL: {ex.Message}");
                MessageBox.Show(this, ex.Message, "CLV LOCATE PARCEL", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string BuildStatusText()
        {
            return $"Search source: {GisParcelLocator.GetDbfPathForCurrentDrawing()}";
        }

        private void TxtQuery_KeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                ExecuteSearch();
            }
        }
    }

    internal sealed class DbfFieldDescriptor
    {
        public string Name { get; init; } = string.Empty;
        public int Length { get; init; }
    }

    internal sealed class DbfRecord
    {
        private readonly Dictionary<string, string> _values;

        public DbfRecord(int recordIndex, Dictionary<string, string> values)
        {
            RecordIndex = recordIndex;
            _values = values;
        }

        public int RecordIndex { get; }

        public string GetValue(string fieldName)
        {
            return _values.TryGetValue(fieldName, out string? value) ? value : string.Empty;
        }
    }


    internal static class CoordinateTransformUtility
    {
        public static Point2d TransformPoint(Point2d point, IEnumerable<string> sourceCandidates, IEnumerable<string> targetCandidates)
        {
            foreach (string source in sourceCandidates.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (string target in targetCandidates.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (TryTransformPoint(point, source, target, out Point2d transformed))
                        return transformed;
                }
            }

            throw new InvalidOperationException("Unable to transform parcel geometry from NV83-EF into the current drawing coordinate system.");
        }

        private static bool TryTransformPoint(Point2d point, string sourceCs, string targetCs, out Point2d transformed)
        {
            transformed = point;
            try
            {
                object? factory = CreateCoordinateSystemFactory();
                if (factory == null)
                    return false;

                object? source = CreateCoordinateSystem(factory, sourceCs);
                object? target = CreateCoordinateSystem(factory, targetCs);
                if (source == null || target == null)
                    return false;

                object? transform = CreateTransform(factory, source, target);
                if (transform == null)
                    return false;

                return TryApplyTransform(transform, point, out transformed);
            }
            catch
            {
                transformed = point;
                return false;
            }
        }

        private static object? CreateCoordinateSystemFactory()
        {
            Type? factoryType = FindType(
                "OSGeo.MapGuide.MgCoordinateSystemFactory",
                "OSGeo.MapGuide.PlatformBase.MgCoordinateSystemFactory");

            return factoryType == null ? null : Activator.CreateInstance(factoryType);
        }

        private static object? CreateCoordinateSystem(object factory, string csText)
        {
            foreach (string methodName in new[] { "Create", "CreateFromCode", "CreateCoordinateSystem", "GetCoordinateSystem" })
            {
                object? created = TryInvokeStringMethod(factory, methodName, csText);
                if (created != null)
                    return created;
            }

            object? wkt = TryInvokeStringMethod(factory, "ConvertCoordinateSystemCodeToWkt", csText);
            if (wkt is string wktText && !string.IsNullOrWhiteSpace(wktText))
            {
                foreach (string methodName in new[] { "Create", "CreateFromCode", "CreateCoordinateSystem" })
                {
                    object? created = TryInvokeStringMethod(factory, methodName, wktText);
                    if (created != null)
                        return created;
                }
            }

            return null;
        }

        private static object? CreateTransform(object factory, object source, object target)
        {
            foreach (string methodName in new[] { "GetTransform", "CreateTransform", "GetCoordinateSystemTransform" })
            {
                MethodInfo[] methods = factory.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .Where(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                foreach (MethodInfo method in methods)
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 2)
                    {
                        try
                        {
                            return method.Invoke(factory, new[] { source, target });
                        }
                        catch
                        {
                        }
                    }
                }
            }

            return null;
        }

        private static bool TryApplyTransform(object transform, Point2d point, out Point2d transformed)
        {
            transformed = point;
            foreach (MethodInfo method in transform.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(m => string.Equals(m.Name, "Transform", StringComparison.OrdinalIgnoreCase)))
            {
                ParameterInfo[] parameters = method.GetParameters();

                if (parameters.Length == 2 && parameters.All(p => p.ParameterType == typeof(double) || p.ParameterType == typeof(double).MakeByRefType()))
                {
                    object[] args = { point.X, point.Y };
                    try
                    {
                        object? result = method.Invoke(transform, args);
                        if (TryReadPoint(result, args, out transformed))
                            return true;
                    }
                    catch
                    {
                    }
                }

                if (parameters.Length == 3 && parameters[0].ParameterType == typeof(double).MakeByRefType())
                {
                    object[] args = { point.X, point.Y, 0.0 };
                    try
                    {
                        object? result = method.Invoke(transform, args);
                        if (TryReadPoint(result, args, out transformed))
                            return true;
                    }
                    catch
                    {
                    }
                }

                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(double[]))
                {
                    object[] args = { new[] { point.X, point.Y, 0.0 } };
                    try
                    {
                        object? result = method.Invoke(transform, args);
                        if (args[0] is double[] arr && arr.Length >= 2)
                        {
                            transformed = new Point2d(arr[0], arr[1]);
                            return true;
                        }
                        if (TryReadPoint(result, args, out transformed))
                            return true;
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }

        private static bool TryReadPoint(object? result, object[] args, out Point2d point)
        {
            point = default;

            if (args.Length >= 2 && args[0] is double x && args[1] is double y)
            {
                point = new Point2d(x, y);
                return true;
            }

            if (result == null)
                return false;

            PropertyInfo? px = result.GetType().GetProperty("X");
            PropertyInfo? py = result.GetType().GetProperty("Y");
            if (px?.GetValue(result) is double rx && py?.GetValue(result) is double ry)
            {
                point = new Point2d(rx, ry);
                return true;
            }

            return false;
        }

        private static object? TryInvokeStringMethod(object target, string methodName, string argument)
        {
            foreach (MethodInfo method in target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase)))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1 || parameters[0].ParameterType != typeof(string))
                    continue;

                try
                {
                    return method.Invoke(target, new object[] { argument });
                }
                catch
                {
                }
            }

            return null;
        }

        private static Type? FindType(params string[] fullNames)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (string fullName in fullNames)
                {
                    Type? found = asm.GetType(fullName, false, false);
                    if (found != null)
                        return found;
                }
            }

            foreach (string assemblyName in new[] { "OSGeo.MapGuide.Foundation", "OSGeo.MapGuide.PlatformBase", "Autodesk.Map.Platform", "ManagedMapApi" })
            {
                try
                {
                    Assembly asm = Assembly.Load(assemblyName);
                    foreach (string fullName in fullNames)
                    {
                        Type? found = asm.GetType(fullName, false, false);
                        if (found != null)
                            return found;
                    }
                }
                catch
                {
                }
            }

            return null;
        }
    }

    internal static class DbfReader
    {
        private static bool _encodingProviderRegistered;

        private static void EnsureEncodingProviderRegistered()
        {
            if (_encodingProviderRegistered)
                return;

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _encodingProviderRegistered = true;
        }

        public static IEnumerable<DbfRecord> ReadRecords(string dbfPath)
        {
            EnsureEncodingProviderRegistered();

            using var stream = File.Open(dbfPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(stream, Encoding.GetEncoding(1252));

            reader.ReadByte();
            reader.ReadBytes(3);
            int recordCount = reader.ReadInt32();
            short headerLength = reader.ReadInt16();
            short recordLength = reader.ReadInt16();
            reader.ReadBytes(20);

            var fields = new List<DbfFieldDescriptor>();
            while (true)
            {
                byte first = reader.ReadByte();
                if (first == 0x0D)
                    break;

                byte[] rest = reader.ReadBytes(31);
                byte[] nameBytes = new byte[11];
                nameBytes[0] = first;
                Array.Copy(rest, 0, nameBytes, 1, 10);
                string name = Encoding.ASCII.GetString(nameBytes).TrimEnd('\0', ' ');
                int length = rest[15];
                fields.Add(new DbfFieldDescriptor
                {
                    Name = name,
                    Length = length
                });
            }

            stream.Position = headerLength;
            for (int recordIndex = 0; recordIndex < recordCount; recordIndex++)
            {
                byte deletedFlag = reader.ReadByte();
                byte[] rawRecord = reader.ReadBytes(recordLength - 1);
                if (deletedFlag == 0x2A)
                    continue;

                int offset = 0;
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (DbfFieldDescriptor field in fields)
                {
                    string value = Encoding.GetEncoding(1252).GetString(rawRecord, offset, field.Length).Trim();
                    values[field.Name] = value;
                    offset += field.Length;
                }

                yield return new DbfRecord(recordIndex, values);
            }
        }
    }

    internal static class ShapefileReader
    {
        public static ParcelGeometry ReadPolygonGeometry(string shpPath, int recordIndex)
        {
            string shxPath = Path.ChangeExtension(shpPath, ".shx");
            if (!File.Exists(shxPath))
                throw new FileNotFoundException("Parcel SHX was not found.", shxPath);

            (int offsetBytes, _) = ReadShxRecord(shxPath, recordIndex);
            using var stream = File.Open(shpPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(stream);

            stream.Position = offsetBytes;
            ReadInt32BigEndian(reader);
            ReadInt32BigEndian(reader);
            int shapeType = reader.ReadInt32();
            if (shapeType == 0)
                return new ParcelGeometry { Extents = new Extents2d(new Point2d(0, 0), new Point2d(0, 0)) };

            bool isPolyline = shapeType == 3 || shapeType == 13;
            bool isPolygon = shapeType == 5 || shapeType == 15;
            if (!isPolygon && !isPolyline)
                throw new InvalidOperationException($"Unsupported parcel shape type {shapeType}. Supported parcel shape types are 3, 5, 13, and 15.");

            double xmin = reader.ReadDouble();
            double ymin = reader.ReadDouble();
            double xmax = reader.ReadDouble();
            double ymax = reader.ReadDouble();
            int numParts = reader.ReadInt32();
            int numPoints = reader.ReadInt32();

            var partStarts = new int[numParts];
            for (int i = 0; i < numParts; i++)
                partStarts[i] = reader.ReadInt32();

            var points = new Point2d[numPoints];
            for (int i = 0; i < numPoints; i++)
                points[i] = new Point2d(reader.ReadDouble(), reader.ReadDouble());

            var geometry = new ParcelGeometry
            {
                Extents = new Extents2d(new Point2d(xmin, ymin), new Point2d(xmax, ymax))
            };

            for (int i = 0; i < numParts; i++)
            {
                int start = partStarts[i];
                int end = i == numParts - 1 ? numPoints : partStarts[i + 1];
                var part = new List<Point2d>();
                for (int j = start; j < end; j++)
                    part.Add(points[j]);

                if (isPolygon && part.Count > 1 && !part[0].IsEqualTo(part[^1]))
                    part.Add(part[0]);

                geometry.Parts.Add(part);
            }

            return geometry;
        }

        private static (int offsetBytes, int contentLengthBytes) ReadShxRecord(string shxPath, int recordIndex)
        {
            using var stream = File.Open(shxPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(stream);
            const int headerBytes = 100;
            stream.Position = headerBytes + (recordIndex * 8L);
            if (stream.Position + 8 > stream.Length)
                throw new InvalidOperationException($"Parcel record {recordIndex + 1} was outside the SHX index range.");

            int offsetWords = ReadInt32BigEndian(reader);
            int contentLengthWords = ReadInt32BigEndian(reader);
            return (offsetWords * 2, contentLengthWords * 2);
        }

        private static int ReadInt32BigEndian(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            if (bytes.Length != 4)
                throw new EndOfStreamException();

            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);
            return BitConverter.ToInt32(bytes, 0);
        }
    }
}
