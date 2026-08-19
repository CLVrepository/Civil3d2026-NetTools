using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

using CLV_CivilTools.Shared;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcException = Autodesk.AutoCAD.Runtime.Exception;

namespace CLV_CivilTools.Survey
{
    internal sealed class ClosureReviewSegment
    {
        public int Number { get; init; }
        public string Type { get; init; } = "LINE";
        public ObjectId OriginalObjectId { get; init; }
        public ObjectId AdjustedObjectId { get; init; }
        public Point3d OriginalStart { get; init; }
        public Point3d OriginalEnd { get; init; }
        public Point3d AdjustedStart { get; init; }
        public Point3d AdjustedEnd { get; init; }
        public double OriginalBulge { get; init; }
        public double AdjustedBulge { get; init; }
        public string TangencyIn { get; init; } = string.Empty;
        public string TangencyOut { get; init; } = string.Empty;
        public string TangencyStatus { get; init; } = string.Empty;
        public string ConstraintState { get; init; } = string.Empty;
        public double? TargetOffset { get; init; }
        public double? ActualOffset { get; init; }
        public double? OffsetDelta { get; init; }
        public Point3d Midpoint => new Point3d((OriginalStart.X + OriginalEnd.X + AdjustedStart.X + AdjustedEnd.X) / 4.0, (OriginalStart.Y + OriginalEnd.Y + AdjustedStart.Y + AdjustedEnd.Y) / 4.0, (OriginalStart.Z + OriginalEnd.Z + AdjustedStart.Z + AdjustedEnd.Z) / 4.0);
        public double OriginalLength => GetSegmentLength(OriginalStart, OriginalEnd, OriginalBulge);
        public double AdjustedLength => GetSegmentLength(AdjustedStart, AdjustedEnd, AdjustedBulge);
        public double LengthDelta => AdjustedLength - OriginalLength;
        public double BearingDeltaSeconds => SurveyClosureReviewData.GetBearingDeltaSeconds(OriginalStart, OriginalEnd, AdjustedStart, AdjustedEnd);
        public double StartShift => OriginalStart.DistanceTo(AdjustedStart);
        public double EndShift => OriginalEnd.DistanceTo(AdjustedEnd);
        public double OriginalRadius => GetSegmentRadius(OriginalStart, OriginalEnd, OriginalBulge);
        public double AdjustedRadius => GetSegmentRadius(AdjustedStart, AdjustedEnd, AdjustedBulge);
        public double RadiusDelta => AdjustedRadius - OriginalRadius;

        private static double GetSegmentRadius(Point3d start, Point3d end, double bulge)
        {
            double chord = start.DistanceTo(end);
            if (chord <= 1.0e-12 || Math.Abs(bulge) <= 1.0e-12)
                return 0.0;

            double includedAngle = 4.0 * Math.Atan(Math.Abs(bulge));
            double sinHalf = Math.Sin(includedAngle / 2.0);
            if (Math.Abs(sinHalf) <= 1.0e-12)
                return 0.0;

            return Math.Abs(chord / (2.0 * sinHalf));
        }

        private static double GetSegmentLength(Point3d start, Point3d end, double bulge)
        {
            double chord = start.DistanceTo(end);
            if (chord <= 1.0e-12 || Math.Abs(bulge) <= 1.0e-12)
                return chord;

            double includedAngle = 4.0 * Math.Atan(Math.Abs(bulge));
            double sinHalf = Math.Sin(includedAngle / 2.0);
            if (Math.Abs(sinHalf) <= 1.0e-12)
                return chord;

            double radius = chord / (2.0 * sinHalf);
            return Math.Abs(radius * includedAngle);
        }
    }

    internal sealed class ClosureReviewRun
    {
        public DateTime CreatedLocal { get; init; } = DateTime.Now;
        public string DrawingName { get; init; } = string.Empty;
        public double TraverseLength { get; init; }
        public double Misclosure { get; init; }
        public double RelativePrecisionRatio { get; init; }
        public double PartsPerMillionError { get; init; }
        public double OriginalArea { get; init; }
        public double AdjustedArea { get; init; }
        public double AreaDelta => AdjustedArea - OriginalArea;
        public IReadOnlyList<ClosureReviewSegment> Segments { get; init; } = Array.Empty<ClosureReviewSegment>();
    }

    internal static class SurveyClosureReviewData
    {
        private static ClosureReviewRun? _lastRun;

        internal static void SetLastRun(ClosureReviewRun run) => _lastRun = run;

        internal static bool TryGetLastRun(Editor ed, out ClosureReviewRun run)
        {
            if (_lastRun != null && _lastRun.Segments.Count > 0)
            {
                run = _lastRun;
                return true;
            }

            ed.WriteMessage("\nSURVEY-CLOSURE-REPORT: No in-session auto closure report data found. Run AUTO CLOSURE first.");
            run = new ClosureReviewRun();
            return false;
        }

        internal static double GetBearingDeltaSeconds(Point3d a1, Point3d a2, Point3d b1, Point3d b2)
        {
            double angle1 = Math.Atan2(a2.Y - a1.Y, a2.X - a1.X);
            double angle2 = Math.Atan2(b2.Y - b1.Y, b2.X - b1.X);
            double delta = NormalizeAngle(angle2 - angle1);
            return Math.Abs(delta) * 180.0 / Math.PI * 3600.0;
        }

        private static double NormalizeAngle(double radians)
        {
            while (radians > Math.PI)
                radians -= 2.0 * Math.PI;
            while (radians < -Math.PI)
                radians += 2.0 * Math.PI;
            return radians;
        }
    }

    public static class SurveyClosureReviewCommands
    {
        private static readonly List<Form> OpenReportForms = new List<Form>();

        [CommandMethod("SURVEY-CLOSURE-REPORT", CommandFlags.Modal)]
        public static void ShowClosureReport()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            if (!SurveyClosureReviewData.TryGetLastRun(doc.Editor, out ClosureReviewRun run))
                return;

            var form = new ClosureReportForm(run);
            OpenReportForms.Add(form);
            form.FormClosed += (_, _) => OpenReportForms.Remove(form);
            AcadApp.ShowModelessDialog(form);
        }

        [CommandMethod("SURVEY-CLOSURE-MARKERS", CommandFlags.Modal)]
        public static void CreateClosureMarkers()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            if (!SurveyClosureReviewData.TryGetLastRun(ed, out ClosureReviewRun run))
                return;

            try
            {
                using (doc.LockDocument())
                {
                    LayerStandards.EnsureSurveyMapClosureLayers(db, ed);
                    using Transaction tr = db.TransactionManager.StartTransaction();
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    double size = GetReviewMarkerSize(run);
                    foreach (ClosureReviewSegment segment in run.Segments)
                    {
                        Point3d mid = segment.Midpoint;

                        Circle circle = new Circle(mid, Vector3d.ZAxis, size);
                        circle.SetDatabaseDefaults(db);
                        circle.Layer = LayerStandards.SurveyMapReviewLayerName;
                        ms.AppendEntity(circle);
                        tr.AddNewlyCreatedDBObject(circle, true);

                        DBText text = new DBText
                        {
                            Position = new Point3d(mid.X - size * 0.35, mid.Y - size * 0.35, mid.Z),
                            Height = size * 0.70,
                            TextString = segment.Number.ToString(CultureInfo.InvariantCulture)
                        };
                        text.SetDatabaseDefaults(db);
                        text.Layer = LayerStandards.SurveyMapReviewLayerName;
                        ms.AppendEntity(text);
                        tr.AddNewlyCreatedDBObject(text, true);
                    }

                    tr.Commit();
                }

                ed.WriteMessage($"\nSURVEY-CLOSURE-MARKERS: Created {run.Segments.Count} numbered review marker(s) on {LayerStandards.SurveyMapReviewLayerName}.");
            }
            catch (AcException ex)
            {
                ed.WriteMessage("\nSURVEY-CLOSURE-MARKERS AutoCAD error: " + ex.Message);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nSURVEY-CLOSURE-MARKERS error: " + ex.Message);
            }
        }

        private static double GetReviewMarkerSize(ClosureReviewRun run)
        {
            if (run.Segments.Count == 0)
                return 1.0;

            double minX = run.Segments.Min(s => Math.Min(Math.Min(s.OriginalStart.X, s.OriginalEnd.X), Math.Min(s.AdjustedStart.X, s.AdjustedEnd.X)));
            double minY = run.Segments.Min(s => Math.Min(Math.Min(s.OriginalStart.Y, s.OriginalEnd.Y), Math.Min(s.AdjustedStart.Y, s.AdjustedEnd.Y)));
            double maxX = run.Segments.Max(s => Math.Max(Math.Max(s.OriginalStart.X, s.OriginalEnd.X), Math.Max(s.AdjustedStart.X, s.AdjustedEnd.X)));
            double maxY = run.Segments.Max(s => Math.Max(Math.Max(s.OriginalStart.Y, s.OriginalEnd.Y), Math.Max(s.AdjustedStart.Y, s.AdjustedEnd.Y)));
            double diagonal = Math.Sqrt(Math.Pow(maxX - minX, 2.0) + Math.Pow(maxY - minY, 2.0));

            // One consistent marker size per report run. The clamp prevents tiny markers on
            // small test parcels and oversized markers on large boundary maps.
            return Math.Min(Math.Max(diagonal * 0.018, 0.50), 5.0);
        }

        [CommandMethod("SURVEY-CLOSURE-CLEAR-REVIEW", CommandFlags.Modal)]
        public static void ClearClosureReviewMarkers()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                int erased = 0;
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                    {
                        if (!id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(Entity))))
                            continue;

                        Entity ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                        if (!string.Equals(ent.Layer, LayerStandards.SurveyMapReviewLayerName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        ent.UpgradeOpen();
                        ent.Erase();
                        erased++;
                    }

                    tr.Commit();
                }

                ed.WriteMessage($"\nSURVEY-CLOSURE-CLEAR-REVIEW: Removed {erased} review marker object(s).");
            }
            catch (AcException ex)
            {
                ed.WriteMessage("\nSURVEY-CLOSURE-CLEAR-REVIEW AutoCAD error: " + ex.Message);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nSURVEY-CLOSURE-CLEAR-REVIEW error: " + ex.Message);
            }
        }

        [CommandMethod("SURVEY-CLOSURE-GOTO", CommandFlags.Modal)]
        public static void GoToClosureSegment()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            if (!SurveyClosureReviewData.TryGetLastRun(ed, out ClosureReviewRun run))
                return;

            PromptIntegerOptions pio = new PromptIntegerOptions("\nEnter closure segment number to zoom/select: ")
            {
                AllowNegative = false,
                AllowZero = false,
                LowerLimit = 1,
                UpperLimit = run.Segments.Count
            };

            PromptIntegerResult pir = ed.GetInteger(pio);
            if (pir.Status != PromptStatus.OK)
                return;

            ClosureReviewSegment? segment = run.Segments.FirstOrDefault(s => s.Number == pir.Value);
            if (segment == null)
            {
                ed.WriteMessage("\nSURVEY-CLOSURE-GOTO: Segment not found in current report.");
                return;
            }

            try
            {
                SelectAndZoomToSegment(segment);
                ed.WriteMessage($"\nSURVEY-CLOSURE-GOTO: Zoomed to segment {segment.Number}.");
            }
            catch (AcException ex)
            {
                ed.WriteMessage("\nSURVEY-CLOSURE-GOTO AutoCAD error: " + ex.Message);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nSURVEY-CLOSURE-GOTO error: " + ex.Message);
            }
        }

        internal static void SelectAndZoomToSegment(ClosureReviewSegment segment)
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            using (doc.LockDocument())
            {
                ed.SetImpliedSelection(new[] { segment.OriginalObjectId, segment.AdjustedObjectId }.Where(id => !id.IsNull).ToArray());
                ZoomToSegment(ed, segment);
            }
        }

        private static void ZoomToSegment(Editor ed, ClosureReviewSegment segment)
        {
            double minX = new[] { segment.OriginalStart.X, segment.OriginalEnd.X, segment.AdjustedStart.X, segment.AdjustedEnd.X }.Min();
            double minY = new[] { segment.OriginalStart.Y, segment.OriginalEnd.Y, segment.AdjustedStart.Y, segment.AdjustedEnd.Y }.Min();
            double maxX = new[] { segment.OriginalStart.X, segment.OriginalEnd.X, segment.AdjustedStart.X, segment.AdjustedEnd.X }.Max();
            double maxY = new[] { segment.OriginalStart.Y, segment.OriginalEnd.Y, segment.AdjustedStart.Y, segment.AdjustedEnd.Y }.Max();

            double width = Math.Max(maxX - minX, 5.0);
            double height = Math.Max(maxY - minY, 5.0);
            double viewWidth = Math.Max(width, height * 1.6) * 1.8;
            double viewHeight = Math.Max(height, width / 1.6) * 1.8;
            Point2d center = new Point2d((minX + maxX) / 2.0, (minY + maxY) / 2.0);

            using ViewTableRecord view = ed.GetCurrentView();
            view.CenterPoint = center;
            view.Width = viewWidth;
            view.Height = viewHeight;
            ed.SetCurrentView(view);
        }
    }

    internal sealed class ClosureReportForm : Form
    {
        public ClosureReportForm(ClosureReviewRun run)
        {
            Text = "CLV Survey Closure Report";
            Width = 1220;
            Height = 720;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = true;
            MaximizeBox = true;

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3
            };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            Label summary = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                Padding = new Padding(10),
                Text = BuildSummary(run)
            };
            root.Controls.Add(summary, 0, 0);

            FlowLayoutPanel tools = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight,
                Padding = new Padding(8, 4, 8, 4),
                WrapContents = false
            };
            Button zoomButton = new Button { Text = "ZOOM TO SELECTED", Width = 150, Height = 28 };
            Button closeButton = new Button { Text = "CLOSE", Width = 90, Height = 28 };
            tools.Controls.Add(zoomButton);
            tools.Controls.Add(closeButton);
            root.Controls.Add(tools, 0, 1);

            DataGridView grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                ReadOnly = true,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells
            };

            AddColumns(grid);
            foreach (ClosureReviewSegment segment in run.Segments)
                AddRow(grid, segment);

            zoomButton.Click += (_, _) => ZoomSelected(grid, run);
            closeButton.Click += (_, _) => Close();
            grid.CellDoubleClick += (_, _) => ZoomSelected(grid, run);

            root.Controls.Add(grid, 0, 2);
            Controls.Add(root);
        }


        private static void ZoomSelected(DataGridView grid, ClosureReviewRun run)
        {
            if (grid.CurrentRow == null || grid.CurrentRow.Cells.Count == 0)
                return;

            object? value = grid.CurrentRow.Cells[0].Value;
            if (value == null || !int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                return;

            ClosureReviewSegment? segment = run.Segments.FirstOrDefault(s => s.Number == number);
            if (segment == null)
                return;

            SurveyClosureReviewCommands.SelectAndZoomToSegment(segment);
            grid.Focus();
        }

        private static string BuildSummary(ClosureReviewRun run)
        {
            return "Drawing: " + run.DrawingName + Environment.NewLine +
                   "Created: " + run.CreatedLocal.ToString("g", CultureInfo.CurrentCulture) + Environment.NewLine +
                   "Segments: " + run.Segments.Count.ToString(CultureInfo.InvariantCulture) +
                   "    Traverse Length: " + FormatDistance(run.TraverseLength) +
                   "    Misclosure: " + FormatDistance(run.Misclosure) +
                   "    Relative Precision: " + (run.RelativePrecisionRatio <= 0.0 ? "Closed" : "1:" + run.RelativePrecisionRatio.ToString("0", CultureInfo.InvariantCulture)) +
                   "    PPM: " + run.PartsPerMillionError.ToString("0.###", CultureInfo.InvariantCulture) + Environment.NewLine +
                   "Original Area: " + FormatArea(run.OriginalArea) +
                   "    Adjusted Area: " + FormatArea(run.AdjustedArea) +
                   "    Δ Area: " + FormatSignedArea(run.AreaDelta);
        }

        private static void AddColumns(DataGridView grid)
        {
            string[] columns =
            {
                "Seg #", "Type", "Orig Chord Bearing", "Adj Chord Bearing", "Δ Bearing", "Orig Length", "Adj Length", "Δ Length", "Orig Radius", "Adj Radius", "Δ Radius", "Constraint State", "Target Offset", "Actual Offset", "Δ Offset", "Tangency In", "Tangency Out", "Tangency Status", "Start Shift", "End Shift", "Status"
            };

            foreach (string column in columns)
                grid.Columns.Add(column.Replace(" ", string.Empty), column);
        }

        private static void AddRow(DataGridView grid, ClosureReviewSegment segment)
        {
            string status = !string.IsNullOrWhiteSpace(segment.TangencyStatus) && segment.TangencyStatus.IndexOf("WARNING", StringComparison.OrdinalIgnoreCase) >= 0
                ? "REVIEW"
                : Math.Abs(segment.LengthDelta) > 0.004 || segment.BearingDeltaSeconds > 10.0 ? "REVIEW" : "OK";
            grid.Rows.Add(
                segment.Number.ToString(CultureInfo.InvariantCulture),
                segment.Type,
                FormatBearing(segment.OriginalStart, segment.OriginalEnd),
                FormatBearing(segment.AdjustedStart, segment.AdjustedEnd),
                segment.BearingDeltaSeconds.ToString("0.###\"", CultureInfo.InvariantCulture),
                FormatDistance(segment.OriginalLength),
                FormatDistance(segment.AdjustedLength),
                FormatSignedDistance(segment.LengthDelta),
                FormatRadius(segment.OriginalRadius),
                FormatRadius(segment.AdjustedRadius),
                FormatSignedRadius(segment.RadiusDelta),
                segment.ConstraintState,
                FormatNullableDistance(segment.TargetOffset),
                FormatNullableDistance(segment.ActualOffset),
                FormatNullableSignedDistance(segment.OffsetDelta),
                segment.TangencyIn,
                segment.TangencyOut,
                segment.TangencyStatus,
                FormatDistance(segment.StartShift),
                FormatDistance(segment.EndShift),
                status);
        }

        private static string FormatDistance(double value) => value.ToString("0.0000", CultureInfo.InvariantCulture) + "'";
        private static string FormatNullableDistance(double? value) => value.HasValue ? FormatDistance(value.Value) : string.Empty;
        private static string FormatSignedDistance(double value) => value.ToString("+0.0000;-0.0000;0.0000", CultureInfo.InvariantCulture) + "'";
        private static string FormatArea(double value) => Math.Abs(value).ToString("0.00", CultureInfo.InvariantCulture) + " sq ft (" + (Math.Abs(value) / 43560.0).ToString("0.0000", CultureInfo.InvariantCulture) + " ac)";
        private static string FormatSignedArea(double value)
        {
            string sign = value > 0.0 ? "+" : value < 0.0 ? "-" : string.Empty;
            double abs = Math.Abs(value);
            return sign + abs.ToString("0.00", CultureInfo.InvariantCulture) + " sq ft (" + sign + (abs / 43560.0).ToString("0.0000", CultureInfo.InvariantCulture) + " ac)";
        }
        private static string FormatNullableSignedDistance(double? value) => value.HasValue ? FormatSignedDistance(value.Value) : string.Empty;
        private static string FormatRadius(double value) => value <= 1.0e-12 ? string.Empty : FormatDistance(value);
        private static string FormatSignedRadius(double value) => Math.Abs(value) <= 1.0e-12 ? string.Empty : FormatSignedDistance(value);

        private static string FormatBearing(Point3d start, Point3d end)
        {
            double degrees = Math.Atan2(end.Y - start.Y, end.X - start.X) * 180.0 / Math.PI;
            while (degrees < 0.0)
                degrees += 360.0;
            while (degrees >= 360.0)
                degrees -= 360.0;

            int d = (int)Math.Floor(degrees);
            double minutesRaw = (degrees - d) * 60.0;
            int m = (int)Math.Floor(minutesRaw);
            double seconds = (minutesRaw - m) * 60.0;
            return d.ToString("000", CultureInfo.InvariantCulture) + "°" +
                   m.ToString("00", CultureInfo.InvariantCulture) + "'" +
                   seconds.ToString("00.##", CultureInfo.InvariantCulture) + "\"";
        }
    }
}
