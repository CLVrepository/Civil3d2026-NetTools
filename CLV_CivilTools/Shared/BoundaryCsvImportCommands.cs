using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace CLV_CivilTools.Shared
{
    /// <summary>
    /// Imports boundary-only review CSV files created from record-of-survey/legal-description review.
    ///
    /// Command names:
    ///   CLV_BOUNDARY_IMPORT
    ///   CLV-MAP-CSV-IMPORT
    ///   CLV-BOUNDARY-CSV-IMPORT
    ///
    /// CSV columns expected:
    /// Segment,Type,StartX,StartY,Bearing,Distance,CurveDirection,Radius,Delta,ArcLength,Tangent,
    /// ChordBearing,ChordLength,SourceLabel,CorrectedLabel,Notes
    /// </summary>
    public class BoundaryCsvImportCommands
    {
        private const string LayerBoundary = "V-MAPL-BNDY";
        private const string LayerText = "V-MAPL-BNDY-TEXT";
        private const string LayerWarn = "V-MAPL-QA";
        private const string LayerPob = "V-MAPL-BNDY-POB";
        private const string LayerPreviewLeft = "V-SURV-MAP-REVIEW-PREVIEW-LEFT";
        private const string LayerPreviewRight = "V-SURV-MAP-REVIEW-PREVIEW-RIGHT";
        private const string LayerPreviewRadial = "V-SURV-MAP-REVIEW-PREVIEW-RADIAL";
        private const string LayerPreviewRadialFlip = "V-SURV-MAP-REVIEW-PREVIEW-RADIAL-FLIP";
        private const string LayerTangency = "V-SURV-MAP-REVIEW-TANGENCY";
        private const double DefaultTangencyToleranceDegrees = 0.10;

        private enum CurveReviewMode
        {
            None,
            QaOnly,
            All
        }

        private enum SegmentLabelMode
        {
            None,
            SegmentOnly,
            BearingDistance,
            FullNotes
        }

        private sealed class ImportOptions
        {
            public bool ChainBuild { get; set; } = true;
            public CurveReviewMode CurveReview { get; set; } = CurveReviewMode.QaOnly;
            public SegmentLabelMode LabelMode { get; set; } = SegmentLabelMode.BearingDistance;
            public bool ImportManualReviewRows { get; set; } = true;
            public bool HighlightTangencyIssues { get; set; } = true;
            public double TangencyToleranceDegrees { get; set; } = DefaultTangencyToleranceDegrees;

            // 2026-07-08: Prompt 2 boundary import now follows the 4-EASEMENT_IMPORT behavior:
            // automatically test curve LEFT/RIGHT and radial shown/reversed candidates, then hold the
            // best-scoring option based on CSV endpoint, chord bearing, and next-line tangency.
            // The old interactive Keep/Flip/ReverseRadial prompt code is intentionally left below as
            // a rollback/reference path; set AutoResolveCurveDirection=false and CurveReview=QaOnly/All
            // if field review prompts need to be restored later.
            public bool AutoResolveCurveDirection { get; set; } = true;
        }

        [CommandMethod("CLV_BOUNDARY_IMPORT")]
        public void ClvBoundaryImport()
        {
            RunBoundaryImport("BOUNDARY ONLY IMPORT");
        }

        [CommandMethod("CLV_2_BOUNDARY")]
        public void Clv2Boundary()
        {
            RunBoundaryImport("BOUNDARY IMPORT AUTO");
        }

        [CommandMethod("CLV_2_BOUNDARY_IMPORT")]
        public void Clv2BoundaryImport()
        {
            RunBoundaryImport("BOUNDARY IMPORT AUTO");
        }

        [CommandMethod("CLV_2A_BOUNDARY_IMPORT_INPUT")]
        public void Clv2ABoundaryImportInput()
        {
            RunBoundaryImport("BOUNDARY IMPORT MANUAL");
        }

        [CommandMethod("CLV_2A_BOUNDARY")]
        public void Clv2ABoundary()
        {
            RunBoundaryImport("BOUNDARY IMPORT MANUAL");
        }

        [CommandMethod("CLV-2A-BOUNDARY-IMPORT-INPUT")]
        public void Clv2ABoundaryDashImportInput()
        {
            RunBoundaryImport("BOUNDARY IMPORT MANUAL");
        }

        [CommandMethod("CLV-2-BOUNDARY-IMPORT")]
        public void Clv2BoundaryDashImport()
        {
            RunBoundaryImport("BOUNDARY IMPORT AUTO");
        }

        [CommandMethod("CLV-MAP-CSV-IMPORT")]
        public void ClvMapCsvImport()
        {
            RunBoundaryImport("BOUNDARY ONLY IMPORT");
        }

        [CommandMethod("CLV-BOUNDARY-CSV-IMPORT")]
        public void ClvBoundaryCsvImport()
        {
            RunBoundaryImport("BOUNDARY ONLY IMPORT");
        }

        [CommandMethod("SURVEY-BOUNDARY-CSV-IMPORT")]
        public void SurveyBoundaryCsvImport()
        {
            RunBoundaryImport("BOUNDARY ONLY IMPORT");
        }

        private static void RunBoundaryImport(string workflowName = "BOUNDARY ONLY IMPORT")
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;

            string? csvPath = PromptForCsvPath(workflowName);
            if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
            {
                ed.WriteMessage($"\n{workflowName} cancelled.");
                return;
            }

            // Use workflow defaults with no setup prompts:
            // Label Text=8, Build Continuously=Yes, Curve Direction=QAOnly,
            // Label=Bearing, Manual Review Rows=Yes.
            double textHeight = 8.0;

            ImportOptions options = CreateDefaultImportOptions(workflowName);
            ed.WriteMessage($"\n{workflowName} defaults: Label Text=8, Build Continuously=Yes, Curve Direction=QAOnly, Label=Bearing, Manual Review Rows=Yes.");
            if (IsPrompt2BoundaryWorkflow(workflowName))
            {
                ed.WriteMessage("\nBOUNDARY IMPORT uses the standalone Prompt 2 POC -> POB -> boundary CSV workflow. It imports the supplied CSV coordinates/calls as provided and does not require Prompt 1 sectional/control geometry.");
            }

            List<BoundaryCsvRow> rows;
            try
            {
                rows = BoundaryCsvRow.Load(csvPath);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUnable to read CSV: {ex.Message}");
                return;
            }

            if (rows.Count == 0)
            {
                ed.WriteMessage("\nNo boundary rows were found in the selected CSV.");
                return;
            }

            ImportSummary summary = new ImportSummary(csvPath);

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EnsureLayer(db, tr, LayerBoundary, 7, "M");
                EnsureLayer(db, tr, LayerText, 3, "M");
                EnsureLayer(db, tr, LayerWarn, 1, "M");
                EnsureLayer(db, tr, LayerPob, 2, "M");
                EnsureLayer(db, tr, "V-MAPL-CNTRL", 2, "M");
                EnsureBoundaryCsvReferencedLayers(db, tr, rows);
                EnsureLayer(db, tr, LayerPreviewLeft, 1, "M");
                EnsureLayer(db, tr, LayerPreviewRight, 3, "M");
                EnsureLayer(db, tr, LayerPreviewRadial, 4, "M");
                EnsureLayer(db, tr, LayerPreviewRadialFlip, 6, "M");

                BlockTable blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                Point3d? firstStart = null;
                Point3d? firstBoundaryStart = null;
                Point3d? lastBoundaryEnd = null;
                Point3d? currentEnd = null;
                double? currentTangentAzimuth = null;
                Extents3d? extents = null;
                List<BoundaryCsvRow> orderedRows = rows.OrderBy(r => r.Segment).ToList();
                int firstSegment = orderedRows.Count > 0 ? orderedRows.Min(r => r.Segment) : 0;
                bool hasExplicitPointMarkerRows = orderedRows.Any(r => r.Type.Equals("POINT_MARKER", StringComparison.OrdinalIgnoreCase));

                try
                {
                    for (int rowIndex = 0; rowIndex < orderedRows.Count; rowIndex++)
                    {
                        BoundaryCsvRow row = orderedRows[rowIndex];
                        BoundaryCsvRow? nextRow = rowIndex + 1 < orderedRows.Count ? orderedRows[rowIndex + 1] : null;
                        if (row.ImportStatus.Equals("DO_NOT_IMPORT", StringComparison.OrdinalIgnoreCase))
                        {
                            summary.AddSkipped(row.Segment, "ImportStatus is DO_NOT_IMPORT.");
                            continue;
                        }

                        if (!options.ImportManualReviewRows && row.ImportStatus.Equals("MANUAL_REVIEW", StringComparison.OrdinalIgnoreCase))
                        {
                            summary.AddSkipped(row.Segment, "ImportStatus is MANUAL_REVIEW and manual-review import is disabled.");
                            continue;
                        }

                        Point3d start;
                        if (options.ChainBuild && currentEnd.HasValue)
                        {
                            start = currentEnd.Value;
                            if (row.HasStartCoordinate)
                            {
                                double startGap = Distance2d(start, new Point3d(row.StartX!.Value, row.StartY!.Value, 0.0));
                                if (startGap > 0.02)
                                {
                                    summary.AddWarning(row.Segment, $"CSV StartX/StartY differs from chain endpoint by {startGap:0.0000} ft. Chain endpoint was used.");
                                }
                            }
                            else
                            {
                                summary.AddWarning(row.Segment, "StartX/StartY blank; chain endpoint was used.");
                            }
                        }
                        else if (row.HasStartCoordinate)
                        {
                            start = new Point3d(row.StartX!.Value, row.StartY!.Value, 0.0);
                        }
                        else if (currentEnd.HasValue)
                        {
                            start = currentEnd.Value;
                            summary.AddWarning(row.Segment, "StartX/StartY blank; used prior segment endpoint.");
                        }
                        else
                        {
                            summary.AddSkipped(row.Segment, "Missing StartX/StartY and no prior endpoint is available.");
                            continue;
                        }

                        firstStart ??= start;

                        if (row.Segment == firstSegment && !hasExplicitPointMarkerRows)
                        {
                            AddPobMarker(modelSpace, tr, start, textHeight);
                        }

                        bool rowWarning = IsWarningRow(row);
                        // Final accepted geometry should not stay on the red review layer just because
                        // the source CSV row was marked QA/MANUAL_REVIEW.  The row flags are used to
                        // decide whether to pause for curve review and to write the QA report; once the
                        // user accepts a chain-built segment, draw it on the normal boundary layer.
                        string entityLayer = GetFinalGeometryLayer(row);

                        if (row.Type.Equals("POINT_MARKER", StringComparison.OrdinalIgnoreCase))
                        {
                            Point3d markerPoint = row.HasStartCoordinate
                                ? new Point3d(row.StartX!.Value, row.StartY!.Value, 0.0)
                                : start;
                            AddPointMarker(modelSpace, tr, markerPoint, row, textHeight, GetFinalGeometryLayer(row));
                            IncludePoint(ref extents, markerPoint);
                            summary.PointMarkerCount++;
                        }
                        else if (row.Type.Equals("LINE", StringComparison.OrdinalIgnoreCase))
                        {
                            Point3d end;
                            double? lineAzimuth = null;
                            bool builtFromCoordinates = false;

                            if (TryBearingToVector(row.Bearing, out Vector2d direction) && row.Distance.HasValue && TryBearingToAzimuthRadians(row.Bearing, out double parsedAzimuth))
                            {
                                end = new Point3d(
                                    start.X + direction.X * row.Distance.Value,
                                    start.Y + direction.Y * row.Distance.Value,
                                    0.0);
                                lineAzimuth = parsedAzimuth;
                            }
                            else if (row.HasEndCoordinate)
                            {
                                end = new Point3d(row.EndX!.Value, row.EndY!.Value, 0.0);
                                Vector2d coordinateVector = new Vector2d(end.X - start.X, end.Y - start.Y);
                                if (coordinateVector.Length > 1e-8)
                                {
                                    lineAzimuth = VectorToAzimuth(coordinateVector);
                                }
                                builtFromCoordinates = true;
                                summary.AddWarning(row.Segment, "Line used StartX/StartY -> EndX/EndY because Bearing/Distance was missing or not usable.");
                            }
                            else
                            {
                                summary.AddSkipped(row.Segment, "Line is missing usable Bearing/Distance and EndX/EndY.");
                                if (!options.ChainBuild)
                                {
                                    currentEnd = null;
                                    currentTangentAzimuth = null;
                                }
                                continue;
                            }

                            Line line = new Line(start, end) { Layer = entityLayer };
                            modelSpace.AppendEntity(line);
                            tr.AddNewlyCreatedDBObject(line, true);

                            AddSegmentLabel(modelSpace, tr, MidPoint(start, end), row, textHeight, false, builtFromCoordinates ? "COORD" : null, options.LabelMode);
                            IncludePoint(ref extents, start);
                            IncludePoint(ref extents, end);

                            CheckCsvEnd(row, end, summary);
                            if (IsBoundaryGeometryRow(row))
                            {
                                firstBoundaryStart ??= start;
                                lastBoundaryEnd = end;
                            }
                            currentEnd = end;
                            currentTangentAzimuth = lineAzimuth;
                            summary.LineCount++;
                        }
                        else if (row.Type.Equals("CURVE", StringComparison.OrdinalIgnoreCase))
                        {
                            string requestedDirection = NormalizeCurveDirection(row.CurveDirection);
                            string selectedDirection = ResolveCurveDirection(
                                ed,
                                modelSpace,
                                tr,
                                row,
                                nextRow,
                                start,
                                currentTangentAzimuth,
                                requestedDirection,
                                options);
                            if (selectedDirection.Equals("CANCEL", StringComparison.OrdinalIgnoreCase))
                            {
                                throw new OperationCanceledException("Boundary CSV import cancelled during curve review.");
                            }

                            if (selectedDirection.Equals("SKIP", StringComparison.OrdinalIgnoreCase))
                            {
                                summary.AddSkipped(row.Segment, "Skipped during curve review.");
                                continue;
                            }

                            row.CurveDirectionOverride = selectedDirection;
                            CurveCreateResult curveResult = TryCreateArcFromBestAvailable(
                                row,
                                start,
                                currentTangentAzimuth,
                                selectedDirection,
                                nextRow,
                                out string curveBuildMethod);

                            if (curveResult.Success && curveResult.Arc != null && curveResult.EndPoint.HasValue)
                            {
                                curveResult.Arc.Layer = entityLayer;
                                modelSpace.AppendEntity(curveResult.Arc);
                                tr.AddNewlyCreatedDBObject(curveResult.Arc, true);

                                AddSegmentLabel(modelSpace, tr, curveResult.LabelPoint ?? MidPoint(start, curveResult.EndPoint.Value), row, textHeight, false, null, options.LabelMode);
                                IncludePoint(ref extents, start);
                                IncludePoint(ref extents, curveResult.EndPoint.Value);
                                IncludePoint(ref extents, curveResult.LabelPoint ?? MidPoint(start, curveResult.EndPoint.Value));

                                CheckCsvEnd(row, curveResult.EndPoint.Value, summary);
                                if (IsBoundaryGeometryRow(row))
                                {
                                    firstBoundaryStart ??= start;
                                    lastBoundaryEnd = curveResult.EndPoint.Value;
                                }
                                currentEnd = curveResult.EndPoint.Value;
                                currentTangentAzimuth = curveResult.OutgoingTangentAzimuth;
                                summary.CurveCount++;

                                if (!string.IsNullOrWhiteSpace(curveBuildMethod) &&
                                    !curveBuildMethod.Equals("TANGENT", StringComparison.OrdinalIgnoreCase) &&
                                    !curveBuildMethod.Equals("CHORD", StringComparison.OrdinalIgnoreCase))
                                {
                                    summary.AddWarning(row.Segment, $"Curve built using {curveBuildMethod}." + (!string.IsNullOrWhiteSpace(row.CurveRadialOverride) ? $" Radial option={row.CurveRadialOverride}." : string.Empty));
                                }

                                if (!selectedDirection.Equals(NormalizeCurveDirection(row.CurveDirection), StringComparison.OrdinalIgnoreCase))
                                {
                                    summary.AddWarning(row.Segment, $"Curve direction changed from '{row.CurveDirection}' to '{selectedDirection}' during import review.");
                                }

                                if (!string.IsNullOrWhiteSpace(curveResult.Note))
                                {
                                    summary.AddWarning(row.Segment, curveResult.Note);
                                }

                            }
                            else if (!currentTangentAzimuth.HasValue && TryCreateChordLineFromRow(row, start, out Line? chordLine, out Point3d chordEnd, out string chordNote))
                            {
                                chordLine.Layer = LayerWarn;
                                modelSpace.AppendEntity(chordLine);
                                tr.AddNewlyCreatedDBObject(chordLine, true);

                                AddSegmentLabel(modelSpace, tr, MidPoint(start, chordEnd), row, textHeight, true, "CHORD ONLY", options.LabelMode);
                                IncludePoint(ref extents, start);
                                IncludePoint(ref extents, chordEnd);

                                CheckCsvEnd(row, chordEnd, summary);
                                if (IsBoundaryGeometryRow(row))
                                {
                                    firstBoundaryStart ??= start;
                                    lastBoundaryEnd = chordEnd;
                                }
                                currentEnd = chordEnd;
                                currentTangentAzimuth = null;
                                summary.ChordFallbackCount++;
                                summary.AddWarning(row.Segment, chordNote);
                            }
                            else
                            {
                                summary.AddSkipped(row.Segment, curveResult.Note ?? "Curve is missing usable Radius/Delta/Chord data.");
                                if (!options.ChainBuild)
                                {
                                    currentEnd = null;
                                    currentTangentAzimuth = null;
                                }
                            }
                        }
                        else
                        {
                            summary.AddSkipped(row.Segment, $"Unknown Type '{row.Type}'. Expected LINE, CURVE, or POINT_MARKER.");
                            if (!options.ChainBuild)
                            {
                                currentEnd = null;
                                currentTangentAzimuth = null;
                            }
                        }

                        if (rowWarning)
                        {
                            summary.AddWarning(row.Segment, GetRowWarningText(row));
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    tr.Abort();
                    ed.WriteMessage($"\n{workflowName} cancelled.");
                    return;
                }

                Point3d? closureStart = firstBoundaryStart ?? firstStart;
                Point3d? closureEnd = lastBoundaryEnd ?? currentEnd;
                if (closureStart.HasValue && closureEnd.HasValue)
                {
                    double eastError = closureEnd.Value.X - closureStart.Value.X;
                    double northError = closureEnd.Value.Y - closureStart.Value.Y;
                    double closureError = Math.Sqrt(eastError * eastError + northError * northError);
                    summary.EastingError = eastError;
                    summary.NorthingError = northError;
                    summary.ClosureError = closureError;
                }

                tr.Commit();

                if (extents.HasValue)
                {
                    ZoomToExtents(ed, extents.Value);
                }
            }

            string? reportPath = TryWriteReport(summary);
            WriteSummaryToCommandLine(ed, summary, reportPath);
        }

        private static string? PromptForCsvPath(string workflowName)
        {
            using OpenFileDialog dialog = new OpenFileDialog
            {
                Title = IsPrompt2BoundaryWorkflow(workflowName) ? "Select Prompt 2 Boundary CSV" : "Select Boundary Review CSV",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                Multiselect = false,
                CheckFileExists = true
            };

            DialogResult result = dialog.ShowDialog();
            return result == DialogResult.OK ? dialog.FileName : null;
        }

        private static ImportOptions CreateDefaultImportOptions(string workflowName)
        {
            bool isPrompt2AInput = IsPrompt2ABoundaryInputWorkflow(workflowName);

            return new ImportOptions
            {
                ChainBuild = true,
                CurveReview = isPrompt2AInput ? CurveReviewMode.All : CurveReviewMode.None,
                AutoResolveCurveDirection = !isPrompt2AInput,
                LabelMode = SegmentLabelMode.BearingDistance,
                ImportManualReviewRows = true,
                HighlightTangencyIssues = false,
                TangencyToleranceDegrees = DefaultTangencyToleranceDegrees
            };
        }

        private static bool IsPrompt2BoundaryWorkflow(string workflowName)
        {
            return workflowName.Contains("BOUNDARY IMPORT", StringComparison.OrdinalIgnoreCase) ||
                   workflowName.Contains("2-BOUNDARY", StringComparison.OrdinalIgnoreCase) ||
                   IsPrompt2ABoundaryInputWorkflow(workflowName);
        }

        private static bool IsPrompt2ABoundaryInputWorkflow(string workflowName)
        {
            return workflowName.Contains("BOUNDARY IMPORT MANUAL", StringComparison.OrdinalIgnoreCase) ||
                   workflowName.Contains("2A-BOUNDARY", StringComparison.OrdinalIgnoreCase);
        }

        private static void EnsureLayer(Database db, Transaction tr, string layerName, short colorIndex, string preferredPlotStyle)
        {
            LayerTable layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!layerTable.Has(layerName))
            {
                layerTable.UpgradeOpen();
                LayerTableRecord layer = new LayerTableRecord
                {
                    Name = layerName,
                    Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(ColorMethod.ByAci, colorIndex)
                };
                TrySetPlotStyleName(layer, preferredPlotStyle);
                layerTable.Add(layer);
                tr.AddNewlyCreatedDBObject(layer, true);
            }
            else
            {
                LayerTableRecord layer = (LayerTableRecord)tr.GetObject(layerTable[layerName], OpenMode.ForWrite);
                if (layer.Color.ColorIndex == 7 || layer.Color.ColorIndex == 0)
                {
                    layer.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(ColorMethod.ByAci, colorIndex);
                }
                TrySetPlotStyleName(layer, preferredPlotStyle);
            }
        }

        private static void EnsureBoundaryCsvReferencedLayers(Database db, Transaction tr, IEnumerable<BoundaryCsvRow> rows)
        {
            foreach (BoundaryCsvRow row in rows)
            {
                if (!string.IsNullOrWhiteSpace(row.Layer))
                {
                    EnsureLayer(db, tr, row.Layer.Trim(), row.Layer.Contains("QA", StringComparison.OrdinalIgnoreCase) ? (short)1 : (short)7, "M");
                }
            }
        }

        private static void TrySetPlotStyleName(LayerTableRecord layer, string preferredPlotStyle)
        {
            // Uses reflection so the command remains compatible if the active AutoCAD/Civil 3D API build
            // exposes plot-style settings differently. This is intentionally non-fatal.
            try
            {
                System.Reflection.PropertyInfo? prop = layer.GetType().GetProperty("PlotStyleName");
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(layer, preferredPlotStyle, null);
                }
            }
            catch
            {
                // Do not block import because of a plot-style setting issue.
            }
        }

        private static void AddPointMarker(BlockTableRecord modelSpace, Transaction tr, Point3d point, BoundaryCsvRow row, double textHeight, string layerName)
        {
            Circle circle = new Circle(point, Vector3d.ZAxis, textHeight * 0.50) { Layer = layerName };
            modelSpace.AppendEntity(circle);
            tr.AddNewlyCreatedDBObject(circle, true);

            string label = !string.IsNullOrWhiteSpace(row.ImportRole)
                ? row.ImportRole.Replace("_MARKER", string.Empty)
                : (!string.IsNullOrWhiteSpace(row.FeatureType) ? row.FeatureType.Replace("_MARKER", string.Empty) : $"PT-{row.Segment}");

            DBText text = new DBText
            {
                Position = new Point3d(point.X + textHeight, point.Y + textHeight, 0.0),
                Height = textHeight,
                TextString = TruncateForDbText(label, 80),
                Layer = layerName
            };
            modelSpace.AppendEntity(text);
            tr.AddNewlyCreatedDBObject(text, true);
        }

        private static void AddPobMarker(BlockTableRecord modelSpace, Transaction tr, Point3d pob, double textHeight)
        {
            Circle circle = new Circle(pob, Vector3d.ZAxis, textHeight * 0.65) { Layer = LayerPob };
            modelSpace.AppendEntity(circle);
            tr.AddNewlyCreatedDBObject(circle, true);

            DBText text = new DBText
            {
                Position = new Point3d(pob.X + textHeight, pob.Y + textHeight, 0.0),
                Height = textHeight,
                TextString = "POB",
                Layer = LayerPob
            };
            modelSpace.AppendEntity(text);
            tr.AddNewlyCreatedDBObject(text, true);
        }

        private static void AddSegmentLabel(BlockTableRecord modelSpace, Transaction tr, Point3d position, BoundaryCsvRow row, double textHeight, bool warning, string? prefix = null, SegmentLabelMode labelMode = SegmentLabelMode.BearingDistance)
        {
            if (labelMode == SegmentLabelMode.None)
            {
                return;
            }

            string curveDirection = !string.IsNullOrWhiteSpace(row.CurveDirectionOverride)
                ? row.CurveDirectionOverride
                : NormalizeCurveDirection(row.CurveDirection);

            string callText;
            if (labelMode == SegmentLabelMode.SegmentOnly)
            {
                callText = row.Type.Equals("CURVE", StringComparison.OrdinalIgnoreCase)
                    ? $"{row.Segment}: {curveDirection}"
                    : $"{row.Segment}";
            }
            else if (row.Type.Equals("LINE", StringComparison.OrdinalIgnoreCase))
            {
                callText = $"{row.Segment}: {row.Bearing} {FormatDouble(row.Distance)}'";
            }
            else
            {
                callText = $"{row.Segment}: {curveDirection} R={FormatDouble(row.Radius)} Δ={row.Delta} L={FormatDouble(row.ArcLength)}";
            }

            if (!string.IsNullOrWhiteSpace(prefix))
            {
                callText = prefix + " - " + callText;
            }

            if (labelMode == SegmentLabelMode.FullNotes)
            {
                string noteText = string.IsNullOrWhiteSpace(row.Notes) || row.Notes.Equals("OK", StringComparison.OrdinalIgnoreCase)
                    ? string.Empty
                    : $" - {row.Notes}";
                callText += noteText;
            }

            DBText text = new DBText
            {
                Position = position,
                Height = textHeight,
                TextString = TruncateForDbText(callText, labelMode == SegmentLabelMode.FullNotes ? 240 : 120),
                Layer = warning ? LayerWarn : LayerText
            };
            modelSpace.AppendEntity(text);
            tr.AddNewlyCreatedDBObject(text, true);
        }

        private static string TruncateForDbText(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, Math.Max(0, maxLength - 3)) + "...";
        }

        private static string ResolveCurveDirection(
            Editor ed,
            BlockTableRecord modelSpace,
            Transaction tr,
            BoundaryCsvRow row,
            BoundaryCsvRow? nextRow,
            Point3d start,
            double? incomingTangentAzimuth,
            string defaultDirection,
            ImportOptions options)
        {
            string normalizedDefault = NormalizeCurveDirection(defaultDirection);
            if (string.IsNullOrWhiteSpace(normalizedDefault) || normalizedDefault.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase))
            {
                normalizedDefault = "RIGHT";
            }

            if (options.AutoResolveCurveDirection)
            {
                return AutoResolveCurveDirection(row, nextRow, start, incomingTangentAzimuth, normalizedDefault);
            }

            bool shouldReview = options.CurveReview == CurveReviewMode.All ||
                                (options.CurveReview == CurveReviewMode.QaOnly && RowNeedsCurveReview(row));
            if (!shouldReview)
            {
                return normalizedDefault;
            }

            if (TryFindStartRadialBearing(row, out _))
            {
                return ResolveRadialCurveDirection(
                    ed,
                    modelSpace,
                    tr,
                    row,
                    nextRow,
                    start,
                    incomingTangentAzimuth,
                    normalizedDefault,
                    options);
            }

            // Keep the review decision simple: show the CSV/default curve and the flipped alternative,
            // then ask the user only whether to keep the CSV direction or flip it.  Avoid exposing
            // LEFT/RIGHT choices as separate command options because that is easier to misread in the field.
            string flippedDirection = normalizedDefault.Equals("LEFT", StringComparison.OrdinalIgnoreCase) ? "RIGHT" : "LEFT";
            List<ObjectId> previewIds = new List<ObjectId>();
            Extents3d? previewExtents = null;
            try
            {
                AddCurveDirectionPreview(modelSpace, tr, row, nextRow, start, incomingTangentAzimuth, normalizedDefault, LayerPreviewRight, options, previewIds, ref previewExtents, "KEEP / CSV");
                AddCurveDirectionPreview(modelSpace, tr, row, nextRow, start, incomingTangentAzimuth, flippedDirection, LayerPreviewLeft, options, previewIds, ref previewExtents, "FLIP OPTION");

                if (previewExtents.HasValue)
                {
                    ZoomToExtents(ed, previewExtents.Value);
                }

                tr.TransactionManager.QueueForGraphicsFlush();
                ed.UpdateScreen();

                ed.WriteMessage($"\nCurve Review - Segment {row.Segment}");
                ed.WriteMessage("\n  Green = KEEP the CSV/current direction.");
                ed.WriteMessage("\n  Red   = FLIP to the opposite direction.");
                ed.WriteMessage($"\n  CSV direction: {row.CurveDirection}");
                ed.WriteMessage($"\n  Keep direction: {normalizedDefault}    Flip direction: {flippedDirection}");
                ed.WriteMessage($"\n  Radius: {FormatDouble(row.Radius)}  Delta: {row.Delta}  Arc: {FormatDouble(row.ArcLength)}");
                if (!string.IsNullOrWhiteSpace(row.Notes))
                {
                    ed.WriteMessage($"\n  Notes: {TruncateForDbText(row.Notes, 160)}");
                }

                PromptKeywordOptions optionsPrompt = new PromptKeywordOptions("\nCurve direction [Keep/Flip/Cancel] <Keep>: ")
                {
                    AllowNone = true
                };
                optionsPrompt.Keywords.Add("Keep");
                optionsPrompt.Keywords.Add("Flip");
                optionsPrompt.Keywords.Add("Cancel");
                optionsPrompt.Keywords.Default = "Keep";

                PromptResult result = ed.GetKeywords(optionsPrompt);
                if (result.Status == PromptStatus.Cancel ||
                    (result.Status == PromptStatus.OK && result.StringResult.Equals("Cancel", StringComparison.OrdinalIgnoreCase)))
                {
                    return "CANCEL";
                }

                if (result.Status == PromptStatus.OK && result.StringResult.Equals("Flip", StringComparison.OrdinalIgnoreCase))
                {
                    return flippedDirection;
                }

                return normalizedDefault;
            }
            finally
            {
                foreach (ObjectId id in previewIds)
                {
                    try
                    {
                        DBObject obj = tr.GetObject(id, OpenMode.ForWrite, false);
                        obj.Erase();
                    }
                    catch
                    {
                        // Preview cleanup must never block the import.
                    }
                }

                tr.TransactionManager.QueueForGraphicsFlush();
                ed.UpdateScreen();
            }
        }


        private static string AutoResolveCurveDirection(
            BoundaryCsvRow row,
            BoundaryCsvRow? nextRow,
            Point3d start,
            double? incomingTangentAzimuth,
            string normalizedDefault)
        {
            // Non-interactive curve selection for BOUNDARY IMPORT AUTO.
            // This mirrors the 4-EASEMENT_IMPORT approach: test valid geometry candidates and choose
            // the one that best fits the supplied CSV endpoint, chord bearing, and next-segment tangency.
            // Interactive snippets below are retained for rollback/reference if manual curve review is needed.
            string flippedDirection = normalizedDefault.Equals("LEFT", StringComparison.OrdinalIgnoreCase) ? "RIGHT" : "LEFT";
            List<(string Direction, string RadialOverride, double Score, bool Success)> candidates = new List<(string, string, double, bool)>();

            void AddCandidate(string direction, string radialOverride)
            {
                BoundaryCsvRow testRow = row.CloneForPreview(direction);
                testRow.CurveRadialOverride = radialOverride;
                CurveCreateResult result = TryCreateArcFromBestAvailable(
                    testRow,
                    start,
                    incomingTangentAzimuth,
                    direction,
                    nextRow,
                    out _);

                if (!result.Success || !result.EndPoint.HasValue)
                {
                    candidates.Add((direction, radialOverride, double.MaxValue, false));
                    return;
                }

                double score = ScoreCurveCandidate(testRow, nextRow, start, result);
                candidates.Add((direction, radialOverride, score, true));
            }

            if (TryFindStartRadialBearing(row, out _))
            {
                AddCandidate(normalizedDefault, "DIRECT");
                AddCandidate(flippedDirection, "DIRECT");
                AddCandidate(normalizedDefault, "REVERSED");
                AddCandidate(flippedDirection, "REVERSED");
            }
            else
            {
                AddCandidate(normalizedDefault, string.Empty);
                AddCandidate(flippedDirection, string.Empty);
            }

            (string Direction, string RadialOverride, double Score, bool Success) best = candidates
                .Where(c => c.Success)
                .OrderBy(c => c.Score)
                .ThenBy(c => c.Direction.Equals(normalizedDefault, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .FirstOrDefault();

            if (!best.Success)
            {
                row.CurveRadialOverride = string.Empty;
                return normalizedDefault;
            }

            row.CurveRadialOverride = best.RadialOverride;
            return best.Direction;
        }


        private static string ResolveRadialCurveDirection(
            Editor ed,
            BlockTableRecord modelSpace,
            Transaction tr,
            BoundaryCsvRow row,
            BoundaryCsvRow? nextRow,
            Point3d start,
            double? incomingTangentAzimuth,
            string normalizedDefault,
            ImportOptions options)
        {
            string flippedDirection = normalizedDefault.Equals("LEFT", StringComparison.OrdinalIgnoreCase) ? "RIGHT" : "LEFT";
            List<ObjectId> previewIds = new List<ObjectId>();
            Extents3d? previewExtents = null;
            try
            {
                AddCurveDirectionPreview(modelSpace, tr, row, nextRow, start, incomingTangentAzimuth, normalizedDefault, LayerPreviewRight, options, previewIds, ref previewExtents, "KEEP / CSV", "DIRECT");
                AddCurveDirectionPreview(modelSpace, tr, row, nextRow, start, incomingTangentAzimuth, flippedDirection, LayerPreviewLeft, options, previewIds, ref previewExtents, "FLIP CURVE", "DIRECT");
                AddCurveDirectionPreview(modelSpace, tr, row, nextRow, start, incomingTangentAzimuth, normalizedDefault, LayerPreviewRadial, options, previewIds, ref previewExtents, "REVERSE RADIAL", "REVERSED");
                AddCurveDirectionPreview(modelSpace, tr, row, nextRow, start, incomingTangentAzimuth, flippedDirection, LayerPreviewRadialFlip, options, previewIds, ref previewExtents, "REVERSE BOTH", "REVERSED");

                if (previewExtents.HasValue)
                {
                    ZoomToExtents(ed, previewExtents.Value);
                }

                tr.TransactionManager.QueueForGraphicsFlush();
                ed.UpdateScreen();

                ed.WriteMessage($"\nRadial Curve Review - Segment {row.Segment}");
                ed.WriteMessage("\n  Green   = KEEP CSV direction and shown radial.");
                ed.WriteMessage("\n  Red     = FLIP curve direction only.");
                ed.WriteMessage("\n  Yellow  = REVERSE radial 180 degrees only.");
                ed.WriteMessage("\n  Magenta = BOTH: reverse radial 180 degrees and flip curve direction.");
                ed.WriteMessage($"\n  CSV direction: {row.CurveDirection}");
                if (!string.IsNullOrWhiteSpace(row.RadialBearing))
                {
                    ed.WriteMessage($"\n  CSV radial: {row.RadialBearing}");
                }
                ed.WriteMessage($"\n  Radius: {FormatDouble(row.Radius)}  Delta: {row.Delta}  Arc: {FormatDouble(row.ArcLength)}");
                ed.WriteMessage("\n  Pick the preview that follows the map. Default is Keep.");

                PromptKeywordOptions optionsPrompt = new PromptKeywordOptions("\nCurve/radial option [Keep/FlipCurve/ReverseRadial/Both/Cancel] <Keep>: ")
                {
                    AllowNone = true
                };
                optionsPrompt.Keywords.Add("Keep");
                optionsPrompt.Keywords.Add("FlipCurve");
                optionsPrompt.Keywords.Add("ReverseRadial");
                optionsPrompt.Keywords.Add("Both");
                optionsPrompt.Keywords.Add("Cancel");
                optionsPrompt.Keywords.Default = "Keep";

                PromptResult result = ed.GetKeywords(optionsPrompt);
                if (result.Status == PromptStatus.Cancel ||
                    (result.Status == PromptStatus.OK && result.StringResult.Equals("Cancel", StringComparison.OrdinalIgnoreCase)))
                {
                    return "CANCEL";
                }

                if (result.Status == PromptStatus.OK && result.StringResult.Equals("FlipCurve", StringComparison.OrdinalIgnoreCase))
                {
                    row.CurveRadialOverride = "DIRECT";
                    return flippedDirection;
                }

                if (result.Status == PromptStatus.OK && result.StringResult.Equals("ReverseRadial", StringComparison.OrdinalIgnoreCase))
                {
                    row.CurveRadialOverride = "REVERSED";
                    return normalizedDefault;
                }

                if (result.Status == PromptStatus.OK && result.StringResult.Equals("Both", StringComparison.OrdinalIgnoreCase))
                {
                    row.CurveRadialOverride = "REVERSED";
                    return flippedDirection;
                }

                row.CurveRadialOverride = "DIRECT";
                return normalizedDefault;
            }
            finally
            {
                foreach (ObjectId id in previewIds)
                {
                    try
                    {
                        DBObject obj = tr.GetObject(id, OpenMode.ForWrite, false);
                        obj.Erase();
                    }
                    catch
                    {
                        // Preview cleanup must never block the import.
                    }
                }

                tr.TransactionManager.QueueForGraphicsFlush();
                ed.UpdateScreen();
            }
        }

        private static void AddCurveDirectionPreview(
            BlockTableRecord modelSpace,
            Transaction tr,
            BoundaryCsvRow row,
            BoundaryCsvRow? nextRow,
            Point3d start,
            double? incomingTangentAzimuth,
            string direction,
            string layerName,
            ImportOptions options,
            List<ObjectId> previewIds,
            ref Extents3d? previewExtents,
            string reviewLabelPrefix = "PREVIEW",
            string radialOverride = "")
        {
            BoundaryCsvRow previewRow = row.CloneForPreview(direction);
            previewRow.CurveRadialOverride = radialOverride;
            CurveCreateResult result = TryCreateArcFromBestAvailable(
                previewRow,
                start,
                incomingTangentAzimuth,
                direction,
                nextRow,
                out string previewBuildMethod);

            if (!result.Success || result.Arc == null || !result.EndPoint.HasValue)
            {
                DBText failText = new DBText
                {
                    Position = new Point3d(start.X + 8.0, start.Y + (direction.Equals("LEFT", StringComparison.OrdinalIgnoreCase) ? 16.0 : -16.0), 0.0),
                    Height = 8.0,
                    TextString = $"{direction} preview failed",
                    Layer = layerName
                };
                modelSpace.AppendEntity(failText);
                tr.AddNewlyCreatedDBObject(failText, true);
                previewIds.Add(failText.ObjectId);
                IncludePoint(ref previewExtents, failText.Position);
                return;
            }

            result.Arc.Layer = layerName;
            modelSpace.AppendEntity(result.Arc);
            tr.AddNewlyCreatedDBObject(result.Arc, true);
            previewIds.Add(result.Arc.ObjectId);

            IncludePoint(ref previewExtents, start);
            IncludePoint(ref previewExtents, result.EndPoint.Value);
            if (result.LabelPoint.HasValue)
            {
                IncludePoint(ref previewExtents, result.LabelPoint.Value);
            }

            string labelText = $"{reviewLabelPrefix} - Seg {row.Segment} ({direction})";
            if (!string.IsNullOrWhiteSpace(previewBuildMethod) && !previewBuildMethod.Equals("TANGENT", StringComparison.OrdinalIgnoreCase))
            {
                labelText += $"  {previewBuildMethod}";
            }
            DBText label = new DBText
            {
                Position = result.LabelPoint ?? MidPoint(start, result.EndPoint.Value),
                Height = 8.0,
                TextString = TruncateForDbText(labelText, 120),
                Layer = layerName
            };
            modelSpace.AppendEntity(label);
            tr.AddNewlyCreatedDBObject(label, true);
            previewIds.Add(label.ObjectId);
            IncludePoint(ref previewExtents, label.Position);

        }

        private static CurveCreateResult TryCreateArcFromBestAvailable(
            BoundaryCsvRow row,
            Point3d start,
            double? incomingTangentAzimuth,
            string direction,
            BoundaryCsvRow? nextRow,
            out string buildMethod)
        {
            buildMethod = "";

            if (TryFindStartRadialBearing(row, out string radialBearing) &&
                TryBearingToAzimuthRadians(radialBearing, out double radialAzimuth))
            {
                List<(CurveCreateResult Result, string Method, double Score)> candidates = new List<(CurveCreateResult, string, double)>();

                CurveCreateResult direct = TryCreateArcFromStartRadial(row, start, radialAzimuth, direction, radialBearing, false);
                if (direct.Success)
                {
                    candidates.Add((direct, "RADIAL", ScoreCurveCandidate(row, nextRow, start, direct)));
                }

                CurveCreateResult reversed = TryCreateArcFromStartRadial(row, start, NormalizeRadians(radialAzimuth + Math.PI), direction, radialBearing, true);
                if (reversed.Success)
                {
                    candidates.Add((reversed, "RADIAL_REVERSED", ScoreCurveCandidate(row, nextRow, start, reversed)));
                }

                if (candidates.Count > 0)
                {
                    if (row.CurveRadialOverride.Equals("DIRECT", StringComparison.OrdinalIgnoreCase))
                    {
                        (CurveCreateResult Result, string Method, double Score) selected = candidates.FirstOrDefault(c => c.Method.Equals("RADIAL", StringComparison.OrdinalIgnoreCase));
                        if (selected.Result != null)
                        {
                            buildMethod = selected.Method;
                            return selected.Result;
                        }
                    }

                    if (row.CurveRadialOverride.Equals("REVERSED", StringComparison.OrdinalIgnoreCase))
                    {
                        (CurveCreateResult Result, string Method, double Score) selected = candidates.FirstOrDefault(c => c.Method.Equals("RADIAL_REVERSED", StringComparison.OrdinalIgnoreCase));
                        if (selected.Result != null)
                        {
                            buildMethod = selected.Method;
                            return selected.Result;
                        }
                    }

                    (CurveCreateResult Result, string Method, double Score) best = candidates.OrderBy(c => c.Score).First();
                    buildMethod = best.Method;
                    return best.Result;
                }
            }

            if (incomingTangentAzimuth.HasValue)
            {
                buildMethod = "TANGENT";
                return TryCreateArcFromTangent(row, start, incomingTangentAzimuth.Value, direction);
            }

            buildMethod = "CHORD";
            return TryCreateArcFromRow(row, start);
        }

        private static CurveCreateResult TryCreateArcFromStartRadial(
            BoundaryCsvRow row,
            Point3d start,
            double pointToCenterRadialAzimuth,
            string direction,
            string sourceRadialBearing,
            bool radialReversed)
        {
            if (!row.Radius.HasValue || row.Radius.Value <= 0.0)
            {
                return CurveCreateResult.Fail("Curve is missing Radius.");
            }

            if (!TryDmsAngleToRadians(row.Delta, out double deltaRadians) || deltaRadians <= 0.0)
            {
                return CurveCreateResult.Fail("Curve is missing usable Delta.");
            }

            double radius = row.Radius.Value;
            bool left = IsLeft(direction);
            Vector2d radialVector = AzimuthToVector(pointToCenterRadialAzimuth);
            Point3d center = new Point3d(
                start.X + radialVector.X * radius,
                start.Y + radialVector.Y * radius,
                0.0);

            double startAngle = Math.Atan2(start.Y - center.Y, start.X - center.X);
            double signedDelta = left ? deltaRadians : -deltaRadians;
            double endAngleForPoint = startAngle + signedDelta;
            Point3d end = new Point3d(
                center.X + Math.Cos(endAngleForPoint) * radius,
                center.Y + Math.Sin(endAngleForPoint) * radius,
                0.0);

            Arc arc = left
                ? new Arc(center, radius, NormalizeRadians(startAngle), NormalizeRadians(endAngleForPoint))
                : new Arc(center, radius, NormalizeRadians(endAngleForPoint), NormalizeRadians(startAngle));

            double midAngle = startAngle + signedDelta / 2.0;
            Point3d labelPoint = new Point3d(
                center.X + Math.Cos(midAngle) * (radius + 12.0),
                center.Y + Math.Sin(midAngle) * (radius + 12.0),
                0.0);

            double tangentMathAngle = left
                ? endAngleForPoint + Math.PI / 2.0
                : endAngleForPoint - Math.PI / 2.0;
            double outgoingTangentAzimuth = VectorToAzimuth(new Vector2d(Math.Cos(tangentMathAngle), Math.Sin(tangentMathAngle)));

            string? note = $"Curve oriented from start radial {sourceRadialBearing}" + (radialReversed ? " reversed 180 degrees." : ".");
            if (row.ArcLength.HasValue)
            {
                double calcArc = radius * deltaRadians;
                if (Math.Abs(calcArc - row.ArcLength.Value) > 0.03)
                {
                    note = AppendNote(note, $"Curve arc length check differs by {Math.Abs(calcArc - row.ArcLength.Value):0.000} ft.");
                }
            }

            if (row.Tangent.HasValue)
            {
                double calcTangent = radius * Math.Tan(deltaRadians / 2.0);
                if (Math.Abs(calcTangent - row.Tangent.Value) > 0.03)
                {
                    note = AppendNote(note, $"Curve tangent check differs by {Math.Abs(calcTangent - row.Tangent.Value):0.000} ft.");
                }
            }

            return CurveCreateResult.Ok(arc, end, labelPoint, note, outgoingTangentAzimuth);
        }

        private static double ScoreCurveCandidate(BoundaryCsvRow row, BoundaryCsvRow? nextRow, Point3d start, CurveCreateResult candidate)
        {
            double score = 0.0;
            if (candidate.EndPoint.HasValue && row.HasEndCoordinate)
            {
                score += Distance2d(candidate.EndPoint.Value, new Point3d(row.EndX!.Value, row.EndY!.Value, 0.0));
            }

            if (candidate.EndPoint.HasValue && TryGetNextLineTangencyError(nextRow, candidate.EndPoint.Value, candidate.OutgoingTangentAzimuth, out double tangencyError, out _))
            {
                score += tangencyError * 5.0;
            }

            if (candidate.EndPoint.HasValue && TryBearingToAzimuthRadians(row.ChordBearing, out double chordAzimuth))
            {
                Vector2d chord = new Vector2d(candidate.EndPoint.Value.X - start.X, candidate.EndPoint.Value.Y - start.Y);
                if (chord.Length > 1e-8)
                {
                    double candidateChordAzimuth = VectorToAzimuth(chord);
                    score += RadiansToDegrees(SmallestAngleDifference(candidateChordAzimuth, chordAzimuth));
                }
            }

            return score;
        }

        private static CurveCreateResult TryCreateArcFromTangent(BoundaryCsvRow row, Point3d start, double incomingTangentAzimuth, string direction)
        {
            if (!row.Radius.HasValue || row.Radius.Value <= 0.0)
            {
                return CurveCreateResult.Fail("Curve is missing Radius.");
            }

            if (!TryDmsAngleToRadians(row.Delta, out double deltaRadians) || deltaRadians <= 0.0)
            {
                return CurveCreateResult.Fail("Curve is missing usable Delta.");
            }

            double radius = row.Radius.Value;
            bool left = IsLeft(direction);
            Vector2d tangent = AzimuthToVector(incomingTangentAzimuth);
            Vector2d leftNormal = new Vector2d(-tangent.Y, tangent.X).GetNormal();
            Vector2d centerOffset = left ? leftNormal * radius : leftNormal.Negate() * radius;
            Point3d center = new Point3d(start.X + centerOffset.X, start.Y + centerOffset.Y, 0.0);

            double startAngle = Math.Atan2(start.Y - center.Y, start.X - center.X);
            double signedDelta = left ? deltaRadians : -deltaRadians;
            double endAngleForPoint = startAngle + signedDelta;
            Point3d end = new Point3d(
                center.X + Math.Cos(endAngleForPoint) * radius,
                center.Y + Math.Sin(endAngleForPoint) * radius,
                0.0);

            Arc arc = left
                ? new Arc(center, radius, NormalizeRadians(startAngle), NormalizeRadians(endAngleForPoint))
                : new Arc(center, radius, NormalizeRadians(endAngleForPoint), NormalizeRadians(startAngle));

            double midAngle = startAngle + signedDelta / 2.0;
            Point3d labelPoint = new Point3d(
                center.X + Math.Cos(midAngle) * (radius + 12.0),
                center.Y + Math.Sin(midAngle) * (radius + 12.0),
                0.0);

            string? note = null;
            if (row.ArcLength.HasValue)
            {
                double calcArc = radius * deltaRadians;
                if (Math.Abs(calcArc - row.ArcLength.Value) > 0.03)
                {
                    note = $"Curve arc length check differs by {Math.Abs(calcArc - row.ArcLength.Value):0.000} ft.";
                }
            }

            if (row.Tangent.HasValue)
            {
                double calcTangent = radius * Math.Tan(deltaRadians / 2.0);
                if (Math.Abs(calcTangent - row.Tangent.Value) > 0.03)
                {
                    note = AppendNote(note, $"Curve tangent check differs by {Math.Abs(calcTangent - row.Tangent.Value):0.000} ft.");
                }
            }

            double outgoingTangentAzimuth = left
                ? NormalizeRadians(incomingTangentAzimuth - deltaRadians)
                : NormalizeRadians(incomingTangentAzimuth + deltaRadians);

            return CurveCreateResult.Ok(arc, end, labelPoint, note, outgoingTangentAzimuth);
        }

        private static CurveCreateResult TryCreateArcFromRow(BoundaryCsvRow row, Point3d start)
        {
            if (!row.Radius.HasValue || row.Radius.Value <= 0.0)
            {
                return CurveCreateResult.Fail("Curve is missing Radius.");
            }

            if (!TryDmsAngleToRadians(row.Delta, out double deltaRadians) || deltaRadians <= 0.0)
            {
                return CurveCreateResult.Fail("Curve is missing usable Delta.");
            }

            double chordLength = row.ChordLength ?? (2.0 * row.Radius.Value * Math.Sin(deltaRadians / 2.0));
            if (chordLength <= 0.0 || chordLength > 2.0 * row.Radius.Value + 0.0001)
            {
                return CurveCreateResult.Fail("Curve has invalid ChordLength/Radius relationship.");
            }

            if (!TryBearingToVector(row.ChordBearing, out Vector2d chordDirection))
            {
                if (TryFindStartTangentBearing(row.CorrectedLabel, out string tangentBearing) && TryBearingToAzimuthRadians(tangentBearing, out double tangentAzimuth))
                {
                    double chordAzimuth = IsLeft(!string.IsNullOrWhiteSpace(row.CurveDirectionOverride) ? row.CurveDirectionOverride : row.CurveDirection)
                        ? tangentAzimuth - deltaRadians / 2.0
                        : tangentAzimuth + deltaRadians / 2.0;
                    chordDirection = AzimuthToVector(chordAzimuth);
                }
                else
                {
                    return CurveCreateResult.Fail("Curve is missing ChordBearing, and no start tangent could be parsed from CorrectedLabel.");
                }
            }

            Point3d end = new Point3d(
                start.X + chordDirection.X * chordLength,
                start.Y + chordDirection.Y * chordLength,
                0.0);

            Point2d start2 = new Point2d(start.X, start.Y);
            Point2d end2 = new Point2d(end.X, end.Y);
            Vector2d chord = end2 - start2;
            double actualChordLength = chord.Length;
            if (actualChordLength <= 1e-8)
            {
                return CurveCreateResult.Fail("Curve chord length is zero.");
            }

            double halfChord = actualChordLength / 2.0;
            double radius = row.Radius.Value;
            double hSquared = radius * radius - halfChord * halfChord;
            if (hSquared < -0.001)
            {
                return CurveCreateResult.Fail("Curve Radius is too small for the chord length.");
            }

            double h = Math.Sqrt(Math.Max(0.0, hSquared));
            Vector2d unitChord = chord.GetNormal();
            Vector2d leftNormal = new Vector2d(-unitChord.Y, unitChord.X);
            bool left = IsLeft(!string.IsNullOrWhiteSpace(row.CurveDirectionOverride) ? row.CurveDirectionOverride : row.CurveDirection);
            Vector2d centerOffset = left ? leftNormal * h : leftNormal.Negate() * h;
            Point2d midpoint = start2 + chord * 0.5;
            Point2d center2 = midpoint + centerOffset;
            Point3d center = new Point3d(center2.X, center2.Y, 0.0);

            double startAngle = Math.Atan2(start.Y - center.Y, start.X - center.X);
            double endAngle = Math.Atan2(end.Y - center.Y, end.X - center.X);

            Arc arc = left
                ? new Arc(center, radius, startAngle, endAngle)
                : new Arc(center, radius, endAngle, startAngle);

            // Midpoint for label only. This places the label near the bulge side of the curve.
            Point3d labelPoint = new Point3d(
                midpoint.X + centerOffset.GetNormal().X * 8.0,
                midpoint.Y + centerOffset.GetNormal().Y * 8.0,
                0.0);

            string? note = null;
            if (row.ArcLength.HasValue)
            {
                double calcArc = radius * deltaRadians;
                if (Math.Abs(calcArc - row.ArcLength.Value) > 0.03)
                {
                    note = $"Curve arc length check differs by {Math.Abs(calcArc - row.ArcLength.Value):0.000} ft.";
                }
            }

            if (row.Tangent.HasValue)
            {
                double calcTangent = radius * Math.Tan(deltaRadians / 2.0);
                if (Math.Abs(calcTangent - row.Tangent.Value) > 0.03)
                {
                    note = AppendNote(note, $"Curve tangent check differs by {Math.Abs(calcTangent - row.Tangent.Value):0.000} ft.");
                }
            }

            return CurveCreateResult.Ok(arc, end, labelPoint, note);
        }

        private static bool TryCreateChordLineFromRow(BoundaryCsvRow row, Point3d start, [NotNullWhen(true)] out Line? line, out Point3d end, out string note)
        {
            line = null;
            end = start;
            note = "Curve fell back to chord line.";

            if (!row.ChordLength.HasValue || row.ChordLength.Value <= 0.0 || !TryBearingToVector(row.ChordBearing, out Vector2d direction))
            {
                return false;
            }

            end = new Point3d(
                start.X + direction.X * row.ChordLength.Value,
                start.Y + direction.Y * row.ChordLength.Value,
                0.0);

            line = new Line(start, end);
            note = "Curve was drawn as chord only because true arc creation failed.";
            return true;
        }

        private static string NormalizeCurveDirection(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "UNKNOWN";
            }

            if (value.Contains("LEFT", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("CCW", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("COUNTER", StringComparison.OrdinalIgnoreCase))
            {
                return "LEFT";
            }

            if (value.Contains("RIGHT", StringComparison.OrdinalIgnoreCase) ||
                value.Contains("CW", StringComparison.OrdinalIgnoreCase))
            {
                return "RIGHT";
            }

            return "UNKNOWN";
        }

        private static bool RowNeedsCurveReview(BoundaryCsvRow row)
        {
            return row.Type.Equals("CURVE", StringComparison.OrdinalIgnoreCase) &&
                   (IsWarningRow(row) ||
                    row.ImportStatus.Equals("IMPORT_QA_LAYER", StringComparison.OrdinalIgnoreCase) ||
                    row.ImportStatus.Equals("MANUAL_REVIEW", StringComparison.OrdinalIgnoreCase) ||
                    row.QAStatus.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase) ||
                    row.QAStatus.Contains("REVIEW", StringComparison.OrdinalIgnoreCase) ||
                    NormalizeCurveDirection(row.CurveDirection).Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsBoundaryGeometryRow(BoundaryCsvRow row)
        {
            return row.ImportRole.Equals("BOUNDARY", StringComparison.OrdinalIgnoreCase) ||
                   row.FeatureType.Equals("BOUNDARY", StringComparison.OrdinalIgnoreCase) ||
                   row.FeatureType.Contains("BOUNDARY", StringComparison.OrdinalIgnoreCase) ||
                   (string.IsNullOrWhiteSpace(row.ImportRole) && string.IsNullOrWhiteSpace(row.FeatureType) &&
                    (row.Type.Equals("LINE", StringComparison.OrdinalIgnoreCase) || row.Type.Equals("CURVE", StringComparison.OrdinalIgnoreCase)));
        }

        private static string GetFinalGeometryLayer(BoundaryCsvRow row)
        {
            if (row.ImportStatus.Equals("IMPORT_QA_LAYER", StringComparison.OrdinalIgnoreCase) ||
                row.ImportStatus.Equals("MANUAL_REVIEW", StringComparison.OrdinalIgnoreCase))
            {
                return LayerWarn;
            }

            string role = row.ImportRole.Trim();
            string featureType = row.FeatureType.Trim();
            string csvLayer = row.Layer.Trim();

            if (role.Contains("POC", StringComparison.OrdinalIgnoreCase) ||
                featureType.Contains("POC", StringComparison.OrdinalIgnoreCase))
            {
                return LayerPob;
            }

            if (role.Contains("POB", StringComparison.OrdinalIgnoreCase) ||
                featureType.Contains("POB", StringComparison.OrdinalIgnoreCase))
            {
                return LayerPob;
            }

            if (role.Contains("COMMENCEMENT", StringComparison.OrdinalIgnoreCase) ||
                role.Contains("CONTROL", StringComparison.OrdinalIgnoreCase) ||
                featureType.Contains("TIE", StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(csvLayer) ? "V-MAPL-CNTRL" : csvLayer;
            }

            if (!string.IsNullOrWhiteSpace(csvLayer) && !csvLayer.Equals("V-MAPL-QA", StringComparison.OrdinalIgnoreCase))
            {
                return csvLayer;
            }

            return LayerBoundary;
        }

        private static bool IsWarningRow(BoundaryCsvRow row)
        {
            return IsWarningNote(row.Notes) ||
                   row.ImportStatus.Equals("IMPORT_QA_LAYER", StringComparison.OrdinalIgnoreCase) ||
                   row.ImportStatus.Equals("MANUAL_REVIEW", StringComparison.OrdinalIgnoreCase) ||
                   row.QAStatus.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase) ||
                   row.QAStatus.Contains("REVIEW", StringComparison.OrdinalIgnoreCase) ||
                   row.QAStatus.Contains("GAP", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetRowWarningText(BoundaryCsvRow row)
        {
            List<string> parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(row.ImportStatus))
            {
                parts.Add("ImportStatus=" + row.ImportStatus);
            }
            if (!string.IsNullOrWhiteSpace(row.QAStatus))
            {
                parts.Add("QAStatus=" + row.QAStatus);
            }
            if (!string.IsNullOrWhiteSpace(row.Notes) && !row.Notes.Equals("OK", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(row.Notes);
            }

            return parts.Count == 0 ? "QA warning row." : string.Join("; ", parts);
        }

        private static void CheckAndHighlightOutgoingTangency(
            BlockTableRecord modelSpace,
            Transaction tr,
            BoundaryCsvRow row,
            BoundaryCsvRow? nextRow,
            Point3d curveEnd,
            double outgoingTangentAzimuth,
            double toleranceDegrees,
            double textHeight,
            ImportSummary summary,
            ref Extents3d? extents)
        {
            if (!TryGetNextLineTangencyError(nextRow, curveEnd, outgoingTangentAzimuth, out double errorDegrees, out string nextBearing))
            {
                return;
            }

            if (errorDegrees <= toleranceDegrees)
            {
                return;
            }

            List<ObjectId>? unusedPreviewIds = null;
            AddTangencyMarker(modelSpace, tr, curveEnd, errorDegrees, textHeight, unusedPreviewIds, ref extents);
            summary.AddWarning(row.Segment, $"Curve outgoing tangent differs from next line bearing {nextBearing} by {errorDegrees:0.000} degrees.");
        }

        private static bool TryGetNextLineTangencyError(
            BoundaryCsvRow? nextRow,
            Point3d curveEnd,
            double? outgoingTangentAzimuth,
            out double errorDegrees,
            out string nextBearing)
        {
            errorDegrees = 0.0;
            nextBearing = string.Empty;

            if (nextRow == null || !outgoingTangentAzimuth.HasValue)
            {
                return false;
            }

            if (!nextRow.Type.Equals("LINE", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!TryBearingToAzimuthRadians(nextRow.Bearing, out double nextLineAzimuth))
            {
                return false;
            }

            errorDegrees = RadiansToDegrees(SmallestAngleDifference(outgoingTangentAzimuth.Value, nextLineAzimuth));
            nextBearing = nextRow.Bearing;
            return true;
        }

        private static void AddTangencyMarker(
            BlockTableRecord modelSpace,
            Transaction tr,
            Point3d point,
            double errorDegrees,
            double textHeight,
            List<ObjectId>? previewIds,
            ref Extents3d? extents)
        {
            double radius = Math.Max(textHeight * 1.25, 6.0);
            Circle circle = new Circle(point, Vector3d.ZAxis, radius) { Layer = LayerTangency };
            modelSpace.AppendEntity(circle);
            tr.AddNewlyCreatedDBObject(circle, true);
            previewIds?.Add(circle.ObjectId);

            DBText text = new DBText
            {
                Position = new Point3d(point.X + radius * 1.4, point.Y + radius * 1.4, 0.0),
                Height = Math.Max(textHeight, 6.0),
                TextString = $"TANGENCY CHECK {errorDegrees:0.00}°",
                Layer = LayerTangency
            };
            modelSpace.AppendEntity(text);
            tr.AddNewlyCreatedDBObject(text, true);
            previewIds?.Add(text.ObjectId);

            IncludePoint(ref extents, new Point3d(point.X - radius, point.Y - radius, 0.0));
            IncludePoint(ref extents, new Point3d(point.X + radius, point.Y + radius, 0.0));
            IncludePoint(ref extents, text.Position);
        }

        private static void CheckCsvEnd(BoundaryCsvRow row, Point3d calculatedEnd, ImportSummary summary)
        {
            if (!row.HasEndCoordinate)
            {
                return;
            }

            Point3d csvEnd = new Point3d(row.EndX!.Value, row.EndY!.Value, 0.0);
            double endGap = Distance2d(calculatedEnd, csvEnd);
            if (endGap > 0.02)
            {
                summary.AddWarning(row.Segment, $"Calculated endpoint differs from CSV EndX/EndY by {endGap:0.0000} ft. Chain-built endpoint was used.");
            }
        }

        private static double Distance2d(Point3d a, Point3d b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static bool IsLeft(string value)
        {
            return value.Contains("LEFT", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("CCW", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("COUNTER", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWarningNote(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Trim().Equals("OK", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return value.Contains("manual", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("verify", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("unclear", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("conflict", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("reverse", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("does not close", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("missing", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("warning", StringComparison.OrdinalIgnoreCase) ||
                   !value.Trim().Equals("OK", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryFindStartRadialBearing(BoundaryCsvRow row, out string radialBearing)
        {
            radialBearing = string.Empty;

            if (!string.IsNullOrWhiteSpace(row.RadialBearing) && TryExtractBearing(row.RadialBearing, out radialBearing))
            {
                return true;
            }

            string combined = string.Join(" ", new[] { row.SourceLabel, row.CorrectedLabel, row.Notes }.Where(v => !string.IsNullOrWhiteSpace(v)));
            if (string.IsNullOrWhiteSpace(combined))
            {
                return false;
            }

            string normalized = NormalizeAngleText(combined);
            MatchCollection matches = Regex.Matches(normalized, @"R\s*\d+\s+([NS]\s*\d+(?:\.\d+)?\s*[°d]\s*\d+(?:\.\d+)?\s*['’]\s*\d+(?:\.\d+)?\s*(?:""|”)?\s*[EW])", RegexOptions.IgnoreCase);
            foreach (Match match in matches)
            {
                int prefixStart = Math.Max(0, match.Index - 20);
                string prefix = normalized.Substring(prefixStart, match.Index - prefixStart);
                if (prefix.Contains("end radial", StringComparison.OrdinalIgnoreCase) ||
                    prefix.Contains("end", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                radialBearing = match.Groups[1].Value;
                return true;
            }

            return false;
        }

        private static bool TryExtractBearing(string value, out string bearing)
        {
            bearing = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string text = NormalizeAngleText(value);
            Match match = Regex.Match(text, @"([NS]\s*\d+(?:\.\d+)?\s*[°d]\s*\d+(?:\.\d+)?\s*['’]\s*\d+(?:\.\d+)?\s*(?:""|”)?\s*[EW])", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }

            bearing = match.Groups[1].Value;
            return true;
        }

        private static bool TryFindStartTangentBearing(string value, out string tangentBearing)
        {
            tangentBearing = string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            Match match = Regex.Match(value, @"start\s+tangent\s+([NS]\s*\d+\s*[°d]\s*\d+\s*['’]\s*\d+(?:\.\d+)?\s*(?:""|”)?\s*[EW])", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }

            tangentBearing = match.Groups[1].Value;
            return true;
        }

        private static bool TryBearingToVector(string bearing, out Vector2d vector)
        {
            vector = Vector2d.XAxis;
            if (!TryBearingToAzimuthRadians(bearing, out double azimuthRadians))
            {
                return false;
            }

            vector = AzimuthToVector(azimuthRadians);
            return true;
        }

        private static Vector2d AzimuthToVector(double azimuthRadians)
        {
            // Azimuth is clockwise from north. AutoCAD XY vector uses X east, Y north.
            return new Vector2d(Math.Sin(azimuthRadians), Math.Cos(azimuthRadians)).GetNormal();
        }

        private static double VectorToAzimuth(Vector2d vector)
        {
            if (vector.Length <= 1e-12)
            {
                return 0.0;
            }

            Vector2d unit = vector.GetNormal();
            return NormalizeRadians(Math.Atan2(unit.X, unit.Y));
        }

        private static bool TryBearingToAzimuthRadians(string bearing, out double azimuthRadians)
        {
            azimuthRadians = 0.0;
            if (string.IsNullOrWhiteSpace(bearing))
            {
                return false;
            }

            string text = NormalizeAngleText(bearing);
            Match match = Regex.Match(text, @"^\s*([NS])\s*(\d+(?:\.\d+)?)\s*[°d]\s*(\d+(?:\.\d+)?)?\s*['’]?\s*(\d+(?:\.\d+)?)?\s*(?:""|”)?\s*([EW])\s*$", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }

            string ns = match.Groups[1].Value.ToUpperInvariant();
            double degrees = ParseDouble(match.Groups[2].Value);
            double minutes = match.Groups[3].Success ? ParseDouble(match.Groups[3].Value) : 0.0;
            double seconds = match.Groups[4].Success ? ParseDouble(match.Groups[4].Value) : 0.0;
            string ew = match.Groups[5].Value.ToUpperInvariant();

            double theta = DegreesToRadians(degrees + minutes / 60.0 + seconds / 3600.0);

            if (ns == "N" && ew == "E")
            {
                azimuthRadians = theta;
            }
            else if (ns == "S" && ew == "E")
            {
                azimuthRadians = Math.PI - theta;
            }
            else if (ns == "S" && ew == "W")
            {
                azimuthRadians = Math.PI + theta;
            }
            else if (ns == "N" && ew == "W")
            {
                azimuthRadians = 2.0 * Math.PI - theta;
            }
            else
            {
                return false;
            }

            azimuthRadians = NormalizeRadians(azimuthRadians);
            return true;
        }

        private static bool TryDmsAngleToRadians(string value, out double radians)
        {
            radians = 0.0;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string text = NormalizeAngleText(value);
            Match match = Regex.Match(text, @"(\d+(?:\.\d+)?)\s*[°d]\s*(\d+(?:\.\d+)?)?\s*['’]?\s*(\d+(?:\.\d+)?)?", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                return false;
            }

            double degrees = ParseDouble(match.Groups[1].Value);
            double minutes = match.Groups[2].Success ? ParseDouble(match.Groups[2].Value) : 0.0;
            double seconds = match.Groups[3].Success ? ParseDouble(match.Groups[3].Value) : 0.0;

            radians = DegreesToRadians(degrees + minutes / 60.0 + seconds / 3600.0);
            return true;
        }

        private static string NormalizeAngleText(string value)
        {
            return value
                .Replace("º", "°")
                .Replace("`", "'")
                .Replace("‘", "'")
                .Replace("’", "'")
                .Replace("“", "\"")
                .Replace("”", "\"")
                .Trim();
        }

        private static double ParseDouble(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0.0;
            }

            string cleaned = value.Replace("'", string.Empty)
                .Replace("\"", string.Empty)
                .Replace(",", string.Empty)
                .Trim();

            return double.Parse(cleaned, CultureInfo.InvariantCulture);
        }

        private static double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }

        private static double RadiansToDegrees(double radians)
        {
            return radians * 180.0 / Math.PI;
        }

        private static double SmallestAngleDifference(double a, double b)
        {
            double diff = Math.Abs(NormalizeRadians(a) - NormalizeRadians(b));
            while (diff > Math.PI * 2.0)
            {
                diff -= Math.PI * 2.0;
            }
            if (diff > Math.PI)
            {
                diff = Math.PI * 2.0 - diff;
            }
            return diff;
        }

        private static double NormalizeRadians(double radians)
        {
            double twoPi = Math.PI * 2.0;
            radians %= twoPi;
            return radians < 0 ? radians + twoPi : radians;
        }

        private static Point3d MidPoint(Point3d a, Point3d b)
        {
            return new Point3d((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0, 0.0);
        }

        private static void IncludePoint(ref Extents3d? extents, Point3d point)
        {
            if (!extents.HasValue)
            {
                extents = new Extents3d(point, point);
                return;
            }

            Extents3d updated = extents.Value;
            updated.AddPoint(point);
            extents = updated;
        }

        private static void ZoomToExtents(Editor ed, Extents3d extents)
        {
            try
            {
                double marginX = Math.Max(20.0, (extents.MaxPoint.X - extents.MinPoint.X) * 0.05);
                double marginY = Math.Max(20.0, (extents.MaxPoint.Y - extents.MinPoint.Y) * 0.05);

                Point2d min = new Point2d(extents.MinPoint.X - marginX, extents.MinPoint.Y - marginY);
                Point2d max = new Point2d(extents.MaxPoint.X + marginX, extents.MaxPoint.Y + marginY);

                using ViewTableRecord view = ed.GetCurrentView();
                view.CenterPoint = new Point2d((min.X + max.X) / 2.0, (min.Y + max.Y) / 2.0);
                view.Width = Math.Max(max.X - min.X, 1.0);
                view.Height = Math.Max(max.Y - min.Y, 1.0);
                ed.SetCurrentView(view);
            }
            catch
            {
                ed.WriteMessage("\nBoundary imported, but zoom-to-extents failed.");
            }
        }

        private static string AppendNote(string? existing, string addition)
        {
            if (string.IsNullOrWhiteSpace(existing))
            {
                return addition;
            }

            return existing + " " + addition;
        }

        private static string FormatDouble(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.####", CultureInfo.InvariantCulture) : string.Empty;
        }

        private static void WriteSummaryToCommandLine(Editor ed, ImportSummary summary, string? reportPath)
        {
            ed.WriteMessage("\nCLV Boundary Only Import Complete");
            ed.WriteMessage($"\n  CSV: {summary.CsvPath}");
            ed.WriteMessage($"\n  Lines imported: {summary.LineCount}");
            ed.WriteMessage($"\n  Curves imported: {summary.CurveCount}");
            ed.WriteMessage($"\n  Point markers imported: {summary.PointMarkerCount}");
            ed.WriteMessage($"\n  Curve chord fallbacks: {summary.ChordFallbackCount}");
            ed.WriteMessage($"\n  Skipped segments: {summary.Skipped.Count}");

            if (summary.ClosureError.HasValue)
            {
                ed.WriteMessage($"\n  Easting error: {summary.EastingError:0.0000} ft");
                ed.WriteMessage($"\n  Northing error: {summary.NorthingError:0.0000} ft");
                ed.WriteMessage($"\n  Closure error: {summary.ClosureError:0.0000} ft");
            }

            if (summary.Warnings.Count > 0)
            {
                ed.WriteMessage($"\n  Warnings: {summary.Warnings.Count}");
                foreach (string warning in summary.Warnings.Take(10))
                {
                    ed.WriteMessage($"\n    - {warning}");
                }

                if (summary.Warnings.Count > 10)
                {
                    ed.WriteMessage($"\n    ... {summary.Warnings.Count - 10} more warnings in report.");
                }
            }

            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                ed.WriteMessage($"\n  Report written: {reportPath}");
            }
        }

        private static string? TryWriteReport(ImportSummary summary)
        {
            try
            {
                string directory = Path.GetDirectoryName(summary.CsvPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string baseName = Path.GetFileNameWithoutExtension(summary.CsvPath);
                string reportPath = Path.Combine(directory, baseName + "_import_report.txt");

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("CLV Boundary Only Import Report");
                sb.AppendLine($"CSV: {summary.CsvPath}");
                sb.AppendLine($"Created: {DateTime.Now}");
                sb.AppendLine();
                sb.AppendLine($"Lines imported: {summary.LineCount}");
                sb.AppendLine($"Curves imported: {summary.CurveCount}");
                sb.AppendLine($"Point markers imported: {summary.PointMarkerCount}");
                sb.AppendLine($"Curve chord fallbacks: {summary.ChordFallbackCount}");
                sb.AppendLine($"Skipped segments: {summary.Skipped.Count}");
                if (summary.ClosureError.HasValue)
                {
                    sb.AppendLine($"Easting error: {summary.EastingError:0.0000} ft");
                    sb.AppendLine($"Northing error: {summary.NorthingError:0.0000} ft");
                    sb.AppendLine($"Closure error: {summary.ClosureError:0.0000} ft");
                }

                sb.AppendLine();
                sb.AppendLine("Warnings:");
                if (summary.Warnings.Count == 0)
                {
                    sb.AppendLine("  None");
                }
                else
                {
                    foreach (string warning in summary.Warnings)
                    {
                        sb.AppendLine("  - " + warning);
                    }
                }

                sb.AppendLine();
                sb.AppendLine("Skipped:");
                if (summary.Skipped.Count == 0)
                {
                    sb.AppendLine("  None");
                }
                else
                {
                    foreach (string skipped in summary.Skipped)
                    {
                        sb.AppendLine("  - " + skipped);
                    }
                }

                File.WriteAllText(reportPath, sb.ToString());
                return reportPath;
            }
            catch
            {
                return null;
            }
        }

        private sealed class CurveCreateResult
        {
            public bool Success { get; private set; }
            public Arc? Arc { get; private set; }
            public Point3d? EndPoint { get; private set; }
            public Point3d? LabelPoint { get; private set; }
            public double? OutgoingTangentAzimuth { get; private set; }
            public string? Note { get; private set; }

            public static CurveCreateResult Ok(Arc arc, Point3d endPoint, Point3d labelPoint, string? note, double? outgoingTangentAzimuth = null)
            {
                return new CurveCreateResult
                {
                    Success = true,
                    Arc = arc,
                    EndPoint = endPoint,
                    LabelPoint = labelPoint,
                    OutgoingTangentAzimuth = outgoingTangentAzimuth,
                    Note = note
                };
            }

            public static CurveCreateResult Fail(string note)
            {
                return new CurveCreateResult
                {
                    Success = false,
                    Note = note
                };
            }
        }

        private sealed class ImportSummary
        {
            public ImportSummary(string csvPath)
            {
                CsvPath = csvPath;
            }

            public string CsvPath { get; }
            public int LineCount { get; set; }
            public int CurveCount { get; set; }
            public int PointMarkerCount { get; set; }
            public int ChordFallbackCount { get; set; }
            public double? EastingError { get; set; }
            public double? NorthingError { get; set; }
            public double? ClosureError { get; set; }
            public List<string> Warnings { get; } = new List<string>();
            public List<string> Skipped { get; } = new List<string>();

            public void AddWarning(int segment, string note)
            {
                if (!string.IsNullOrWhiteSpace(note))
                {
                    Warnings.Add($"Segment {segment}: {note}");
                }
            }

            public void AddSkipped(int segment, string note)
            {
                Skipped.Add($"Segment {segment}: {note}");
            }
        }

        private sealed class BoundaryCsvRow
        {
            public int Segment { get; set; }
            public string FeatureType { get; set; } = string.Empty;
            public string Layer { get; set; } = string.Empty;
            public string ImportRole { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public double? StartX { get; set; }
            public double? StartY { get; set; }
            public double? EndX { get; set; }
            public double? EndY { get; set; }
            public string Bearing { get; set; } = string.Empty;
            public double? Distance { get; set; }
            public string CurveDirection { get; set; } = string.Empty;
            public double? Radius { get; set; }
            public string Delta { get; set; } = string.Empty;
            public double? ArcLength { get; set; }
            public double? Tangent { get; set; }
            public string ChordBearing { get; set; } = string.Empty;
            public double? ChordLength { get; set; }
            public string RadialBearing { get; set; } = string.Empty;
            public string SourceLabel { get; set; } = string.Empty;
            public string CorrectedLabel { get; set; } = string.Empty;
            public string Confidence { get; set; } = string.Empty;
            public string ImportStatus { get; set; } = string.Empty;
            public string QAStatus { get; set; } = string.Empty;
            public string Notes { get; set; } = string.Empty;
            public string CurveDirectionOverride { get; set; } = string.Empty;
            public string CurveRadialOverride { get; set; } = string.Empty;

            public BoundaryCsvRow CloneForPreview(string curveDirection)
            {
                return new BoundaryCsvRow
                {
                    Segment = Segment,
                    FeatureType = FeatureType,
                    Layer = Layer,
                    ImportRole = ImportRole,
                    Type = Type,
                    StartX = StartX,
                    StartY = StartY,
                    EndX = EndX,
                    EndY = EndY,
                    Bearing = Bearing,
                    Distance = Distance,
                    CurveDirection = curveDirection,
                    Radius = Radius,
                    Delta = Delta,
                    ArcLength = ArcLength,
                    Tangent = Tangent,
                    ChordBearing = ChordBearing,
                    ChordLength = ChordLength,
                    RadialBearing = RadialBearing,
                    SourceLabel = SourceLabel,
                    CorrectedLabel = CorrectedLabel,
                    Confidence = Confidence,
                    ImportStatus = ImportStatus,
                    QAStatus = QAStatus,
                    Notes = Notes,
                    CurveDirectionOverride = curveDirection,
                    CurveRadialOverride = CurveRadialOverride
                };
            }

            public bool HasStartCoordinate => StartX.HasValue && StartY.HasValue;
            public bool HasEndCoordinate => EndX.HasValue && EndY.HasValue;

            public static List<BoundaryCsvRow> Load(string path)
            {
                string[] lines = File.ReadAllLines(path);
                if (lines.Length == 0)
                {
                    return new List<BoundaryCsvRow>();
                }

                List<string> headers = SplitCsvLine(lines[0]);
                Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < headers.Count; i++)
                {
                    string header = headers[i].Trim();
                    if (!map.ContainsKey(header))
                    {
                        map.Add(header, i);
                    }
                }

                List<BoundaryCsvRow> rows = new List<BoundaryCsvRow>();
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]))
                    {
                        continue;
                    }

                    List<string> values = SplitCsvLine(lines[i]);
                    BoundaryCsvRow row = new BoundaryCsvRow
                    {
                        Segment = GetInt(map, values, "Segment"),
                        FeatureType = GetString(map, values, "FeatureType"),
                        Layer = GetString(map, values, "Layer"),
                        ImportRole = GetString(map, values, "ImportRole"),
                        Type = GetString(map, values, "Type"),
                        StartX = GetNullableDouble(map, values, "StartX"),
                        StartY = GetNullableDouble(map, values, "StartY"),
                        EndX = GetNullableDouble(map, values, "EndX"),
                        EndY = GetNullableDouble(map, values, "EndY"),
                        Bearing = GetString(map, values, "Bearing"),
                        Distance = GetNullableDouble(map, values, "Distance"),
                        CurveDirection = GetString(map, values, "CurveDirection"),
                        Radius = GetNullableDouble(map, values, "Radius"),
                        Delta = GetString(map, values, "Delta"),
                        ArcLength = GetNullableDouble(map, values, "ArcLength"),
                        Tangent = GetNullableDouble(map, values, "Tangent"),
                        ChordBearing = GetString(map, values, "ChordBearing"),
                        ChordLength = GetNullableDouble(map, values, "ChordLength"),
                        RadialBearing = GetString(map, values, "RadialBearing"),
                        SourceLabel = GetString(map, values, "SourceLabel"),
                        CorrectedLabel = GetString(map, values, "CorrectedLabel"),
                        Confidence = GetString(map, values, "Confidence"),
                        ImportStatus = GetString(map, values, "ImportStatus"),
                        QAStatus = GetString(map, values, "QAStatus"),
                        Notes = GetString(map, values, "Notes")
                    };

                    if (row.Segment == 0)
                    {
                        row.Segment = rows.Count + 1;
                    }

                    rows.Add(row);
                }

                return rows;
            }

            private static string GetString(Dictionary<string, int> map, List<string> values, string key)
            {
                return map.TryGetValue(key, out int index) && index >= 0 && index < values.Count
                    ? values[index].Trim()
                    : string.Empty;
            }

            private static int GetInt(Dictionary<string, int> map, List<string> values, string key)
            {
                string value = GetString(map, values, key);
                return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : 0;
            }

            private static double? GetNullableDouble(Dictionary<string, int> map, List<string> values, string key)
            {
                string value = GetString(map, values, key);
                if (string.IsNullOrWhiteSpace(value))
                {
                    return null;
                }

                value = value.Replace("'", string.Empty).Replace(",", string.Empty).Trim();
                return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) ? result : null;
            }

            private static List<string> SplitCsvLine(string line)
            {
                List<string> result = new List<string>();
                StringBuilder current = new StringBuilder();
                bool inQuotes = false;

                for (int i = 0; i < line.Length; i++)
                {
                    char c = line[i];
                    if (c == '"')
                    {
                        if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                        {
                            current.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = !inQuotes;
                        }
                    }
                    else if (c == ',' && !inQuotes)
                    {
                        result.Add(current.ToString());
                        current.Clear();
                    }
                    else
                    {
                        current.Append(c);
                    }
                }

                result.Add(current.ToString());
                return result;
            }
        }
    }
}
