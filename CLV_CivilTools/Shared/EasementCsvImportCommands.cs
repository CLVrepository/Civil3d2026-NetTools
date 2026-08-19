using System;
using System.Collections.Generic;
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
    /// Imports text/exhibit easement legal-description CSV files.
    ///
    /// Command names:
    ///   CLV_4_EASEMENT
    ///   CLV_4_EASEMENT_IMPORT
    ///   CLV-4-EASEMENT-IMPORT
    ///   SURVEY-EASEMENT-CSV-IMPORT
    ///
    /// Accepted CSV styles:
    /// 1) Prompt-Easement format:
    ///    SegmentID,LineType,FromPoint,ToPoint,Bearing,Azimuth_Deg,Distance_Ft,Start_X,Start_Y,End_X,End_Y,Delta_E,Delta_N,Source_Note
    /// 2) Current Prompt Easement rebuilt format:
    ///    FeatureID,FeatureType,Layer,Segment,Type,ImportRole,From,To,StartX,StartY,EndX,EndY,Bearing,Distance,...,MarkerLabel,LabelText,...,ImportStatus,QAStatus,Notes
    /// 3) Prompt 2-style geometry rows with easement FeatureType/LineType:
    ///    FeatureID,FeatureType,Layer,Segment,Type,StartX,StartY,EndX,EndY,Bearing,Distance,...,ImportStatus,QAStatus,Notes
    ///
    /// The importer intentionally allows standalone easement geometry anchored at 10000,10000.
    /// It does not require Prompt 1/Prompt 2 boundary anchoring.
    /// </summary>
    public class EasementCsvImportCommands
    {
        private const string LayerEasement = "V-MAPL-ESMT";
        private const string LayerEasementText = "V-MAPL-ESMT-TEXT";
        private const string LayerTie = "V-MAPL-CNTRL";
        private const string LayerTieText = "V-MAPL-CNTRL-TEXT";
        private const string LayerQa = "V-MAPL-QA";
        private const string LayerPob = "V-MAPL-ESMT-POB";
        private const string LayerPoc = "V-MAPL-ESMT-POC";
        private const string LayerPromptEasementLine = "V-ESMT-LINE";
        private const string LayerPromptEasementTie = "V-ESMT-TIE";
        private const string LayerPromptEasementMark = "V-ESMT-MARK";
        private const double DefaultTextHeight = 8.0;
        private const double ClosureTolerance = 0.02;

        [CommandMethod("CLV_4_EASEMENT")]
        public void Clv4Easement()
        {
            RunEasementImport();
        }

        [CommandMethod("CLV_4_EASEMENT_IMPORT")]
        public void Clv4EasementImport()
        {
            RunEasementImport();
        }

        [CommandMethod("CLV-4-EASEMENT-IMPORT")]
        public void Clv4EasementDashImport()
        {
            RunEasementImport();
        }

        [CommandMethod("SURVEY-EASEMENT-CSV-IMPORT")]
        public void SurveyEasementCsvImport()
        {
            RunEasementImport();
        }

        private static void RunEasementImport()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                return;
            }

            Editor ed = doc.Editor;
            Database db = doc.Database;

            string? csvPath = PromptForCsvPath();
            if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
            {
                ed.WriteMessage("\nEASEMENT IMPORT cancelled.");
                return;
            }

            List<EasementCsvRow> rows;
            try
            {
                rows = EasementCsvRow.Load(csvPath);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUnable to read easement CSV: {ex.Message}");
                return;
            }

            if (rows.Count == 0)
            {
                ed.WriteMessage("\nNo easement rows were found in the selected CSV.");
                return;
            }

            EasementImportSummary summary = new EasementImportSummary(csvPath);
            ed.WriteMessage("\nEASEMENT IMPORT defaults: standalone easement legal CSV aware; commencement ties import to control, easement boundary imports to V-MAPL-ESMT or the CSV-requested easement tie layer, easement boundary imports to V-MAPL-ESMT or the CSV-requested easement line layer, QA/manual-review rows import to V-MAPL-QA unless DO_NOT_IMPORT.");
            ed.WriteMessage("\nEASEMENT IMPORT supports the rebuilt Prompt Easement schema with Type=LINE/CURVE/POINT_MARKER and ImportRole=COMMENCEMENT/EASEMENT/POC_MARKER/POB_MARKER.");
            ed.WriteMessage("\nEASEMENT IMPORT allows POC/POB at 10000,10000 for text-only standalone easements. It does not require Prompt 1 boundary anchoring.");

            bool hasExplicitPocMarker = rows.Any(r => r.IsPointOnly && r.IsPocRow);
            bool hasExplicitPobMarker = rows.Any(r => r.IsPointOnly && r.IsPobRow);

            List<Point2d> boundaryVertices = new List<Point2d>();
            Point3d? firstBoundaryStart = null;
            Point3d? lastBoundaryEnd = null;
            Extents3d? extents = null;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EnsureLayer(db, tr, LayerEasement, 5, "M");
                EnsureLayer(db, tr, LayerEasementText, 3, "M");
                EnsureLayer(db, tr, LayerTie, 2, "M");
                EnsureLayer(db, tr, LayerTieText, 2, "M");
                EnsureLayer(db, tr, LayerQa, 1, "M");
                EnsureLayer(db, tr, LayerPob, 4, "M");
                EnsureLayer(db, tr, LayerPoc, 6, "M");

                // CSVs can carry prompt-generated layer names or older aliases.
                // Ensure every layer that may be assigned exists before Entity.Layer is set.
                EnsureCsvReferencedLayers(db, tr, rows);

                BlockTable blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                Point3d? currentEnd = null;

                foreach (EasementCsvRow row in rows.OrderBy(r => r.SortOrder))
                {
                    if (row.ImportStatus.Equals("DO_NOT_IMPORT", StringComparison.OrdinalIgnoreCase))
                    {
                        summary.AddSkipped(row.DisplaySegment, "ImportStatus is DO_NOT_IMPORT.");
                        continue;
                    }

                    if (row.IsNoteOnly)
                    {
                        summary.AddSkipped(row.DisplaySegment, "NOTE row skipped.");
                        continue;
                    }

                    if (row.IsPointOnly)
                    {
                        if (TryResolveStart(row, currentEnd, out Point3d point, out string pointNote))
                        {
                            AddPointMarker(modelSpace, tr, point, row.PointMarkerDisplayText, GetPointLayer(row), DefaultTextHeight);
                            IncludePoint(ref extents, point);
                            summary.PointCount++;
                            if (!string.IsNullOrWhiteSpace(pointNote))
                            {
                                summary.AddWarning(row.DisplaySegment, pointNote);
                            }
                        }
                        else
                        {
                            summary.AddSkipped(row.DisplaySegment, "Point row missing usable coordinate.");
                        }

                        continue;
                    }

                    if (!TryResolveStart(row, currentEnd, out Point3d start, out string startNote))
                    {
                        summary.AddSkipped(row.DisplaySegment, "Missing usable Start_X/StartY and no prior endpoint is available.");
                        currentEnd = null;
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(startNote))
                    {
                        summary.AddWarning(row.DisplaySegment, startNote);
                    }

                    if (!TryResolveEnd(row, start, out Point3d end, out string endNote))
                    {
                        summary.AddSkipped(row.DisplaySegment, "Missing usable End_X/EndY or Bearing/Distance.");
                        currentEnd = null;
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(endNote))
                    {
                        summary.AddWarning(row.DisplaySegment, endNote);
                    }

                    string geometryLayer = GetGeometryLayer(row);
                    string textLayer = GetTextLayer(row);

                    if (row.IsPocRow && !hasExplicitPocMarker)
                    {
                        AddPointMarker(modelSpace, tr, start, "POC", LayerPoc, DefaultTextHeight);
                    }

                    if (row.IsBoundary && !firstBoundaryStart.HasValue)
                    {
                        firstBoundaryStart = start;
                        if (!hasExplicitPobMarker)
                        {
                            AddPointMarker(modelSpace, tr, start, "POB", LayerPob, DefaultTextHeight);
                        }
                        boundaryVertices.Add(new Point2d(start.X, start.Y));
                    }

                    bool drew = false;
                    if (row.Type.Equals("CURVE", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryCreateArcFromEndpoints(row, start, end, out Arc? arc, out string arcNote))
                        {
                            arc.Layer = geometryLayer;
                            modelSpace.AppendEntity(arc);
                            tr.AddNewlyCreatedDBObject(arc, true);
                            drew = true;
                            summary.CurveCount++;
                            if (!string.IsNullOrWhiteSpace(arcNote))
                            {
                                summary.AddWarning(row.DisplaySegment, arcNote);
                            }
                        }
                        else
                        {
                            Line chord = new Line(start, end) { Layer = LayerQa };
                            modelSpace.AppendEntity(chord);
                            tr.AddNewlyCreatedDBObject(chord, true);
                            drew = true;
                            summary.ChordFallbackCount++;
                            summary.AddWarning(row.DisplaySegment, "Curve could not be built from endpoint/radius data; chord fallback drawn on QA layer. " + arcNote);
                        }
                    }
                    else
                    {
                        Line line = new Line(start, end) { Layer = geometryLayer };
                        modelSpace.AppendEntity(line);
                        tr.AddNewlyCreatedDBObject(line, true);
                        drew = true;
                        summary.LineCount++;
                    }

                    if (drew)
                    {
                        AddSegmentLabel(modelSpace, tr, MidPoint(start, end), row, textLayer, DefaultTextHeight);
                        IncludePoint(ref extents, start);
                        IncludePoint(ref extents, end);
                        currentEnd = end;

                        if (row.IsCommencementTie)
                        {
                            summary.TieCount++;
                        }

                        if (row.IsBoundary)
                        {
                            boundaryVertices.Add(new Point2d(end.X, end.Y));
                            lastBoundaryEnd = end;
                            summary.BoundaryCount++;
                        }

                        if (row.IsQa)
                        {
                            if (row.IsCommencementTie)
                            {
                                summary.AddWarning(row.DisplaySegment, "Commencement/control tie imported on V-MAPL-CNTRL; CSV still indicates QA/manual review for POC reliability.");
                            }
                            else
                            {
                                summary.AddWarning(row.DisplaySegment, "QA/manual-review row imported on QA layer.");
                            }
                        }
                    }
                }

                if (firstBoundaryStart.HasValue && lastBoundaryEnd.HasValue)
                {
                    double eastError = lastBoundaryEnd.Value.X - firstBoundaryStart.Value.X;
                    double northError = lastBoundaryEnd.Value.Y - firstBoundaryStart.Value.Y;
                    double closureError = Math.Sqrt(eastError * eastError + northError * northError);
                    summary.EastingError = eastError;
                    summary.NorthingError = northError;
                    summary.ClosureError = closureError;
                    summary.Perimeter = CalculatePerimeter(boundaryVertices);
                    summary.Area = CalculateArea(boundaryVertices);
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

        private static string? PromptForCsvPath()
        {
            using OpenFileDialog dialog = new OpenFileDialog
            {
                Title = "Select Easement Legal CSV",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                Multiselect = false,
                CheckFileExists = true
            };

            DialogResult result = dialog.ShowDialog();
            return result == DialogResult.OK ? dialog.FileName : null;
        }

        private static string GetGeometryLayer(EasementCsvRow row)
        {
            string requestedLayer = NormalizeLayerName(row.Layer);

            if (row.IsQa)
            {
                return LayerQa;
            }

            // Rebuilt Prompt Easement rows carry explicit layer names. Honor them when present.
            if (!string.IsNullOrWhiteSpace(requestedLayer) &&
                !requestedLayer.Equals("V-MAPL-BNDY", StringComparison.OrdinalIgnoreCase))
            {
                return requestedLayer;
            }

            if (row.IsCommencementTie)
            {
                return LayerTie;
            }

            if (row.IsBoundary)
            {
                return LayerEasement;
            }

            return LayerTie;
        }

        private static string GetTextLayer(EasementCsvRow row)
        {
            if (row.IsQa)
            {
                return LayerQa;
            }

            string requestedLayer = NormalizeLayerName(row.Layer);
            if (requestedLayer.Equals(LayerPromptEasementLine, StringComparison.OrdinalIgnoreCase))
            {
                return LayerEasementText;
            }

            if (requestedLayer.Equals(LayerPromptEasementTie, StringComparison.OrdinalIgnoreCase))
            {
                return LayerTieText;
            }

            if (row.IsCommencementTie)
            {
                return LayerTieText;
            }

            return row.IsBoundary ? LayerEasementText : LayerTieText;
        }

        private static string GetPointLayer(EasementCsvRow row)
        {
            // POC/POB point markers get their own visible marker layers even when the
            // coordinate needs QA/manual review. This keeps the true POC and POB visually
            // distinct while the report/CSV status carries the reliability warning.
            if (row.IsQa)
            {
                return LayerQa;
            }

            string requestedLayer = NormalizeLayerName(row.Layer);
            if (!string.IsNullOrWhiteSpace(requestedLayer) &&
                !requestedLayer.Equals("V-MAPL-BNDY", StringComparison.OrdinalIgnoreCase))
            {
                return requestedLayer;
            }

            if (row.IsPocRow || row.ImportRole.Equals("POC_MARKER", StringComparison.OrdinalIgnoreCase) || row.MarkerLabel.Equals("POC", StringComparison.OrdinalIgnoreCase) || row.ToPointOrSegment.Equals("POC", StringComparison.OrdinalIgnoreCase))
            {
                return LayerPoc;
            }

            if (row.IsPobRow || row.ImportRole.Equals("POB_MARKER", StringComparison.OrdinalIgnoreCase) || row.MarkerLabel.Equals("POB", StringComparison.OrdinalIgnoreCase) || row.ToPointOrSegment.Equals("POB", StringComparison.OrdinalIgnoreCase) || row.LineType.Contains("POB", StringComparison.OrdinalIgnoreCase))
            {
                return LayerPob;
            }

            return LayerTie;
        }

        private static void EnsureCsvReferencedLayers(Database db, Transaction tr, IEnumerable<EasementCsvRow> rows)
        {
            HashSet<string> layerNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                LayerEasement,
                LayerEasementText,
                LayerTie,
                LayerTieText,
                LayerQa,
                LayerPob,
                LayerPoc,
                LayerPromptEasementLine,
                LayerPromptEasementTie,
                LayerPromptEasementMark
            };

            foreach (EasementCsvRow row in rows)
            {
                layerNames.Add(GetGeometryLayer(row));
                layerNames.Add(GetTextLayer(row));
                if (row.IsPointOnly)
                {
                    layerNames.Add(GetPointLayer(row));
                }
            }

            foreach (string layerName in layerNames.Where(n => !string.IsNullOrWhiteSpace(n)))
            {
                EnsureLayer(db, tr, layerName, DefaultLayerColor(layerName), "M");
            }
        }

        private static short DefaultLayerColor(string layerName)
        {
            if (layerName.Equals(LayerQa, StringComparison.OrdinalIgnoreCase)) return 1;
            if (layerName.Equals(LayerTie, StringComparison.OrdinalIgnoreCase) || layerName.Equals(LayerTieText, StringComparison.OrdinalIgnoreCase) || layerName.Equals(LayerPromptEasementTie, StringComparison.OrdinalIgnoreCase)) return 2;
            if (layerName.Equals(LayerEasementText, StringComparison.OrdinalIgnoreCase)) return 3;
            if (layerName.Equals(LayerPob, StringComparison.OrdinalIgnoreCase)) return 4;
            if (layerName.Equals(LayerEasement, StringComparison.OrdinalIgnoreCase) || layerName.Equals(LayerPromptEasementLine, StringComparison.OrdinalIgnoreCase)) return 5;
            if (layerName.Equals(LayerPoc, StringComparison.OrdinalIgnoreCase) || layerName.Equals(LayerPromptEasementMark, StringComparison.OrdinalIgnoreCase)) return 6;
            return 7;
        }

        private static string NormalizeLayerName(string layerName)
        {
            if (string.IsNullOrWhiteSpace(layerName))
            {
                return string.Empty;
            }

            string clean = layerName.Trim().Trim('"');

            // Accept older/generated easement layer names without causing eKeyNotFound on Entity.Layer.
            if (clean.Equals(LayerPromptEasementLine, StringComparison.OrdinalIgnoreCase) ||
                clean.Equals(LayerPromptEasementTie, StringComparison.OrdinalIgnoreCase) ||
                clean.Equals(LayerPromptEasementMark, StringComparison.OrdinalIgnoreCase))
            {
                return clean;
            }

            if (clean.Equals("V-MAPL-ESMT", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("V-MAPL-EASE", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("V-MAPL-EASEMENT", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("V-MAPL-EASEMENTS", StringComparison.OrdinalIgnoreCase))
            {
                return LayerEasement;
            }

            if (clean.Equals("V-MAPL-ESMT-TEXT", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("V-MAPL-EASE-TEXT", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("V-MAPL-EASEMENT-TEXT", StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("V-MAPL-EASEMENTS-TEXT", StringComparison.OrdinalIgnoreCase))
            {
                return LayerEasementText;
            }

            return clean;
        }

        private static bool TryResolveStart(EasementCsvRow row, Point3d? currentEnd, out Point3d start, out string note)
        {
            note = string.Empty;
            if (row.HasStartCoordinate)
            {
                start = new Point3d(row.StartX!.Value, row.StartY!.Value, 0.0);
                if (currentEnd.HasValue && row.AllowChainContinuityCheck)
                {
                    double gap = Distance2d(start, currentEnd.Value);
                    if (gap > 0.02)
                    {
                        note = $"CSV start differs from prior endpoint by {gap:0.0000} ft; CSV start was held.";
                    }
                }

                return true;
            }

            if (currentEnd.HasValue)
            {
                start = currentEnd.Value;
                note = "Start coordinate blank; prior endpoint was used.";
                return true;
            }

            start = Point3d.Origin;
            return false;
        }

        private static bool TryResolveEnd(EasementCsvRow row, Point3d start, out Point3d end, out string note)
        {
            note = string.Empty;
            if (row.HasEndCoordinate)
            {
                end = new Point3d(row.EndX!.Value, row.EndY!.Value, 0.0);
                if (row.Distance.HasValue && row.Distance.Value > 0.0)
                {
                    double cadDistance = Distance2d(start, end);
                    if (Math.Abs(cadDistance - row.Distance.Value) > 0.05)
                    {
                        note = $"Coordinate distance {cadDistance:0.0000} differs from CSV/legal distance {row.Distance.Value:0.0000} by {Math.Abs(cadDistance - row.Distance.Value):0.0000} ft; coordinates were held.";
                    }
                }

                return true;
            }

            if (row.Distance.HasValue && row.Distance.Value > 0.0 && TryDirectionVector(row, out Vector2d direction))
            {
                end = new Point3d(start.X + direction.X * row.Distance.Value, start.Y + direction.Y * row.Distance.Value, 0.0);
                return true;
            }

            end = start;
            return false;
        }

        private static bool TryDirectionVector(EasementCsvRow row, out Vector2d direction)
        {
            if (row.AzimuthDeg.HasValue)
            {
                double radians = DegreesToRadians(row.AzimuthDeg.Value);
                direction = new Vector2d(Math.Sin(radians), Math.Cos(radians));
                return true;
            }

            return TryBearingToVector(row.Bearing, out direction);
        }

        private static bool TryCreateArcFromEndpoints(EasementCsvRow row, Point3d start, Point3d end, out Arc? arc, out string note)
        {
            arc = null;
            note = string.Empty;

            if (!row.Radius.HasValue || row.Radius.Value <= 0.0)
            {
                note = "Curve is missing Radius.";
                return false;
            }

            double radius = row.Radius.Value;
            Point2d start2 = new Point2d(start.X, start.Y);
            Point2d end2 = new Point2d(end.X, end.Y);
            Vector2d chord = end2 - start2;
            double chordLength = chord.Length;
            if (chordLength <= 1e-8)
            {
                note = "Curve endpoint chord is zero.";
                return false;
            }

            if (chordLength > 2.0 * radius + 0.001)
            {
                note = "Curve chord is longer than diameter.";
                return false;
            }

            bool directionKnown = TryGetCurveDirection(row, out bool sourceLeft, out bool sourceRight);
            List<ArcCandidate> candidates = BuildCurveCandidates(row, start2, end2, radius, directionKnown, sourceLeft, sourceRight);
            if (candidates.Count == 0)
            {
                note = "No valid curve candidate could be calculated from endpoints/radius/radial data.";
                return false;
            }

            ScoreCurveCandidates(row, start, end, candidates);
            ArcCandidate best = candidates.OrderBy(c => c.Score).First();
            arc = best.ToArc();

            List<string> notes = new List<string>();
            if (!directionKnown)
            {
                notes.Add("CurveDirection blank/unknown; LEFT and RIGHT options were tested.");
            }

            if (best.Source.Contains("RADIAL", StringComparison.OrdinalIgnoreCase))
            {
                notes.Add($"{best.Source} curve candidate selected.");
                if (best.RadialDirectionUsed.Equals("REVERSED", StringComparison.OrdinalIgnoreCase))
                {
                    notes.Add("RadialDirectionUsed=REVERSED; reversed radial was intentionally used.");
                }
                else if (best.RadialDirectionUsed.Equals("BOTH_TESTED", StringComparison.OrdinalIgnoreCase))
                {
                    notes.Add("Shown and reversed radial options were both tested.");
                }
            }
            else if (!string.IsNullOrWhiteSpace(row.RadialBearing) || !string.IsNullOrWhiteSpace(row.StartRadialBearing))
            {
                notes.Add("Radial bearing was present, but endpoint/radius candidate scored best or radial did not fit endpoints within tolerance.");
            }

            if (!string.IsNullOrWhiteSpace(best.Warnings))
            {
                notes.Add(best.Warnings);
            }

            string statusWarnings = BuildCurveStatusWarning(row);
            if (!string.IsNullOrWhiteSpace(statusWarnings))
            {
                notes.Add(statusWarnings);
            }

            note = string.Join(" ", notes.Where(n => !string.IsNullOrWhiteSpace(n)));
            return true;
        }

        private static bool TryGetCurveDirection(EasementCsvRow row, out bool left, out bool right)
        {
            string dir = row.CurveDirection.Trim();
            left = dir.Contains("LEFT", StringComparison.OrdinalIgnoreCase) || dir.Equals("L", StringComparison.OrdinalIgnoreCase) || dir.Contains("CCW", StringComparison.OrdinalIgnoreCase);
            right = dir.Contains("RIGHT", StringComparison.OrdinalIgnoreCase) || dir.Equals("R", StringComparison.OrdinalIgnoreCase) || dir.Contains("CW", StringComparison.OrdinalIgnoreCase);
            if (left && right)
            {
                // Prefer explicit LEFT/RIGHT words over CW text if a mixed note is present.
                left = dir.Contains("LEFT", StringComparison.OrdinalIgnoreCase);
                right = dir.Contains("RIGHT", StringComparison.OrdinalIgnoreCase);
            }

            return left || right;
        }

        private static List<ArcCandidate> BuildCurveCandidates(EasementCsvRow row, Point2d start, Point2d end, double radius, bool directionKnown, bool sourceLeft, bool sourceRight)
        {
            List<ArcCandidate> candidates = new List<ArcCandidate>();
            Vector2d chord = end - start;
            double chordLength = chord.Length;
            double halfChord = chordLength / 2.0;
            double h = Math.Sqrt(Math.Max(0.0, radius * radius - halfChord * halfChord));
            Vector2d unitChord = chord.GetNormal();
            Vector2d leftNormal = new Vector2d(-unitChord.Y, unitChord.X);
            Point2d midpoint = start + chord * 0.5;

            bool[] directions = directionKnown ? new[] { sourceLeft } : new[] { true, false };
            foreach (bool left in directions)
            {
                Point2d center = midpoint + (left ? leftNormal * h : leftNormal.Negate() * h);
                candidates.Add(new ArcCandidate(center, radius, start, end, left, "ENDPOINT_RADIUS", string.Empty));
            }

            AddRadialCandidates(row, candidates, start, end, radius, directionKnown, sourceLeft, sourceRight);
            return DeduplicateCandidates(candidates);
        }

        private static void AddRadialCandidates(EasementCsvRow row, List<ArcCandidate> candidates, Point2d start, Point2d end, double radius, bool directionKnown, bool sourceLeft, bool sourceRight)
        {
            string radialText = !string.IsNullOrWhiteSpace(row.StartRadialBearing) ? row.StartRadialBearing : row.RadialBearing;
            if (string.IsNullOrWhiteSpace(radialText) || !TryBearingToVector(radialText, out Vector2d radialVector))
            {
                return;
            }

            string requestedRadialMode = row.RadialDirectionUsed.Trim();
            List<(Vector2d Vector, string Mode)> radialVectors = new List<(Vector2d, string)>();
            if (requestedRadialMode.Equals("REVERSED", StringComparison.OrdinalIgnoreCase))
            {
                radialVectors.Add((radialVector.Negate(), "REVERSED"));
            }
            else if (requestedRadialMode.Equals("SHOWN", StringComparison.OrdinalIgnoreCase))
            {
                radialVectors.Add((radialVector, "SHOWN"));
            }
            else
            {
                radialVectors.Add((radialVector, "SHOWN"));
                radialVectors.Add((radialVector.Negate(), "REVERSED"));
            }

            bool[] directions = directionKnown ? new[] { sourceLeft } : new[] { true, false };
            foreach ((Vector2d vector, string mode) in radialVectors)
            {
                Point2d center = start - vector.GetNormal() * radius;
                double endRadiusError = Math.Abs(center.GetDistanceTo(end) - radius);
                string source = "RADIAL_CONTROLLED_" + mode;
                string radialUsed = radialVectors.Count > 1 ? "BOTH_TESTED" : mode;
                foreach (bool left in directions)
                {
                    ArcCandidate candidate = new ArcCandidate(center, radius, start, end, left, source, radialUsed);
                    candidate.Score += endRadiusError * 50.0;
                    if (endRadiusError > 0.05)
                    {
                        candidate.Warnings = AppendNote(candidate.Warnings, $"Radial endpoint radius error {endRadiusError:0.0000} ft.");
                    }

                    candidates.Add(candidate);
                }
            }
        }

        private static List<ArcCandidate> DeduplicateCandidates(IEnumerable<ArcCandidate> candidates)
        {
            List<ArcCandidate> result = new List<ArcCandidate>();
            foreach (ArcCandidate candidate in candidates)
            {
                bool exists = result.Any(r => r.Left == candidate.Left && r.Center.GetDistanceTo(candidate.Center) < 0.0001 && Math.Abs(r.Radius - candidate.Radius) < 0.0001);
                if (!exists)
                {
                    result.Add(candidate);
                }
            }

            return result;
        }

        private static void ScoreCurveCandidates(EasementCsvRow row, Point3d start, Point3d end, List<ArcCandidate> candidates)
        {
            TryParseDeltaDegrees(row.Delta, out double deltaDegrees);
            bool hasDelta = deltaDegrees > 0.0;
            bool hasChordBearing = TryBearingToAzimuthDegrees(row.ChordBearing, out double chordBearingAzimuth);
            double actualChordAzimuth = AzimuthDegreesFromPoints(start, end);

            foreach (ArcCandidate candidate in candidates)
            {
                Arc arc = candidate.ToArc();
                double score = candidate.Score;

                if (row.ArcLength.HasValue && row.ArcLength.Value > 0.0)
                {
                    double arcDiff = Math.Abs(arc.Length - row.ArcLength.Value);
                    score += arcDiff * 8.0;
                    if (arcDiff > 0.05)
                    {
                        candidate.Warnings = AppendNote(candidate.Warnings, $"Arc length {arc.Length:0.0000} differs from CSV/legal arc {row.ArcLength.Value:0.0000} by {arcDiff:0.0000} ft.");
                    }
                }

                if (hasDelta)
                {
                    double calcArcLength = candidate.Radius * DegreesToRadians(deltaDegrees);
                    double arcDiff = Math.Abs(arc.Length - calcArcLength);
                    score += arcDiff * 4.0;
                    if (arcDiff > 0.05)
                    {
                        candidate.Warnings = AppendNote(candidate.Warnings, $"Delta-derived arc {calcArcLength:0.0000} differs from drawn arc {arc.Length:0.0000} by {arcDiff:0.0000} ft.");
                    }
                }

                if (row.ChordLength.HasValue && row.ChordLength.Value > 0.0)
                {
                    double chordDiff = Math.Abs(candidate.ChordLength - row.ChordLength.Value);
                    score += chordDiff * 8.0;
                    if (chordDiff > 0.05)
                    {
                        candidate.Warnings = AppendNote(candidate.Warnings, $"Chord length {candidate.ChordLength:0.0000} differs from CSV/legal chord {row.ChordLength.Value:0.0000} by {chordDiff:0.0000} ft.");
                    }
                }

                if (hasChordBearing)
                {
                    double bearingDiff = SmallestAngleDifferenceDegrees(actualChordAzimuth, chordBearingAzimuth);
                    score += bearingDiff * 2.0;
                    if (bearingDiff > (5.0 / 3600.0))
                    {
                        candidate.Warnings = AppendNote(candidate.Warnings, $"Chord bearing differs from CSV/legal chord bearing by {bearingDiff:0.000000} degrees.");
                    }
                }

                candidate.Score = score;
            }
        }

        private static string BuildCurveStatusWarning(EasementCsvRow row)
        {
            List<string> warnings = new List<string>();
            string combined = (row.QAStatus + " " + row.Notes).Trim();
            string[] flaggedTokens =
            {
                "RADIAL_REVERSED",
                "BOTH_RADIAL_DIRECTIONS_TESTED",
                "LEFT_RIGHT_TESTED",
                "CW_CCW_CORRECTED",
                "LEFT_RIGHT_CORRECTED",
                "NON_TANGENT_RADIAL_CONTROL",
                "TANGENCY_CONFLICT",
                "RADIAL_CONFLICT",
                "CURVE_DIRECTION_CONFLICT",
                "CURVE_DATA_CONFLICT",
                "CURVE_MATH_CONFLICT",
                "CURVE_DATA_INCOMPLETE",
                "UNKNOWN_CURVE_CONTROL",
                "MANUAL_REVIEW"
            };

            foreach (string token in flaggedTokens)
            {
                if (combined.Contains(token, StringComparison.OrdinalIgnoreCase))
                {
                    warnings.Add(token);
                }
            }

            return warnings.Count > 0 ? "Curve QA flag(s): " + string.Join(", ", warnings.Distinct(StringComparer.OrdinalIgnoreCase)) + "." : string.Empty;
        }

        private static bool TryParseDeltaDegrees(string delta, out double degrees)
        {
            degrees = 0.0;
            if (string.IsNullOrWhiteSpace(delta))
            {
                return false;
            }

            return TryBearingToAzimuthDegrees("N" + delta.Trim() + "E", out degrees) || TryBearingToAzimuthDegrees(delta.Trim(), out degrees);
        }

        private static double AzimuthDegreesFromPoints(Point3d start, Point3d end)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double radians = Math.Atan2(dx, dy);
            double degrees = radians * 180.0 / Math.PI;
            return degrees < 0.0 ? degrees + 360.0 : degrees;
        }

        private static double SmallestAngleDifferenceDegrees(double a, double b)
        {
            double diff = Math.Abs((a - b) % 360.0);
            return diff > 180.0 ? 360.0 - diff : diff;
        }

        private sealed class ArcCandidate
        {
            public ArcCandidate(Point2d center, double radius, Point2d start, Point2d end, bool left, string source, string radialDirectionUsed)
            {
                Center = center;
                Radius = radius;
                Start = start;
                End = end;
                Left = left;
                Source = source;
                RadialDirectionUsed = radialDirectionUsed;
                ChordLength = start.GetDistanceTo(end);
            }

            public Point2d Center { get; }
            public double Radius { get; }
            public Point2d Start { get; }
            public Point2d End { get; }
            public bool Left { get; }
            public string Source { get; }
            public string RadialDirectionUsed { get; }
            public double ChordLength { get; }
            public double Score { get; set; }
            public string Warnings { get; set; } = string.Empty;

            public Arc ToArc()
            {
                Point3d center3 = new Point3d(Center.X, Center.Y, 0.0);
                double startAngle = Math.Atan2(Start.Y - Center.Y, Start.X - Center.X);
                double endAngle = Math.Atan2(End.Y - Center.Y, End.X - Center.X);
                return Left
                    ? new Arc(center3, Radius, NormalizeRadians(startAngle), NormalizeRadians(endAngle))
                    : new Arc(center3, Radius, NormalizeRadians(endAngle), NormalizeRadians(startAngle));
            }
        }

        private static void AddPointMarker(BlockTableRecord modelSpace, Transaction tr, Point3d point, string label, string layer, double textHeight)
        {
            Circle circle = new Circle(point, Vector3d.ZAxis, textHeight * 0.45) { Layer = layer };
            modelSpace.AppendEntity(circle);
            tr.AddNewlyCreatedDBObject(circle, true);

            DBText text = new DBText
            {
                Position = new Point3d(point.X + textHeight, point.Y + textHeight, 0.0),
                Height = textHeight,
                TextString = Truncate(label, 80),
                Layer = layer
            };
            modelSpace.AppendEntity(text);
            tr.AddNewlyCreatedDBObject(text, true);
        }

        private static void AddSegmentLabel(BlockTableRecord modelSpace, Transaction tr, Point3d position, EasementCsvRow row, string layer, double textHeight)
        {
            string call;
            if (row.Type.Equals("CURVE", StringComparison.OrdinalIgnoreCase))
            {
                call = $"{row.DisplaySegment}: {row.CurveDirection} R={FormatDouble(row.Radius)} Δ={row.Delta} L={FormatDouble(row.ArcLength)}";
            }
            else
            {
                string distanceText = row.Distance.HasValue ? row.Distance.Value.ToString("0.00", CultureInfo.InvariantCulture) + "'" : string.Empty;
                call = $"{row.DisplaySegment}: {row.Bearing} {distanceText}".Trim();
            }

            if (row.IsCommencementTie)
            {
                call = "TIE - " + call;
            }
            else if (row.IsBoundary)
            {
                call = "EASE - " + call;
            }

            DBText text = new DBText
            {
                Position = position,
                Height = textHeight,
                TextString = Truncate(call, 120),
                Layer = layer
            };
            modelSpace.AppendEntity(text);
            tr.AddNewlyCreatedDBObject(text, true);
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

        private static void TrySetPlotStyleName(LayerTableRecord layer, string preferredPlotStyle)
        {
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
            }
        }

        private static string? TryWriteReport(EasementImportSummary summary)
        {
            try
            {
                string? dir = Path.GetDirectoryName(summary.CsvPath);
                if (string.IsNullOrWhiteSpace(dir))
                {
                    return null;
                }

                string fileName = Path.GetFileNameWithoutExtension(summary.CsvPath) + "_easement_import_report.txt";
                string path = Path.Combine(dir, fileName);
                File.WriteAllText(path, summary.BuildReport(), Encoding.UTF8);
                return path;
            }
            catch
            {
                return null;
            }
        }

        private static void WriteSummaryToCommandLine(Editor ed, EasementImportSummary summary, string? reportPath)
        {
            ed.WriteMessage("\nEASEMENT Import Complete");
            ed.WriteMessage($"\nCSV: {summary.CsvPath}");
            ed.WriteMessage($"\nCommencement/tie segments imported: {summary.TieCount}");
            ed.WriteMessage($"\nEasement boundary segments imported: {summary.BoundaryCount}");
            ed.WriteMessage($"\nLines imported: {summary.LineCount}");
            ed.WriteMessage($"\nCurves imported: {summary.CurveCount}");
            ed.WriteMessage($"\nCurve chord fallbacks: {summary.ChordFallbackCount}");
            ed.WriteMessage($"\nPoint markers imported: {summary.PointCount}");
            ed.WriteMessage($"\nSkipped segments: {summary.Skipped.Count}");

            if (summary.ClosureError.HasValue)
            {
                ed.WriteMessage($"\nEasement closure error: {summary.ClosureError.Value:0.000000} ft (E={summary.EastingError:0.000000}, N={summary.NorthingError:0.000000})");
                ed.WriteMessage($"\nEasement perimeter: {summary.Perimeter:0.0000} ft");
                ed.WriteMessage($"\nEasement area: {summary.Area:0.0000} sq ft");
            }

            if (summary.Warnings.Count > 0)
            {
                ed.WriteMessage($"\nWarnings: {summary.Warnings.Count}. See report for details.");
            }

            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                ed.WriteMessage($"\nReport written: {reportPath}");
            }
        }

        private static void ZoomToExtents(Editor ed, Extents3d extents)
        {
            try
            {
                double padX = Math.Max((extents.MaxPoint.X - extents.MinPoint.X) * 0.10, 25.0);
                double padY = Math.Max((extents.MaxPoint.Y - extents.MinPoint.Y) * 0.10, 25.0);
                Point3d min = new Point3d(extents.MinPoint.X - padX, extents.MinPoint.Y - padY, 0.0);
                Point3d max = new Point3d(extents.MaxPoint.X + padX, extents.MaxPoint.Y + padY, 0.0);
                using ViewTableRecord view = ed.GetCurrentView();
                view.CenterPoint = new Point2d((min.X + max.X) / 2.0, (min.Y + max.Y) / 2.0);
                view.Width = Math.Max(max.X - min.X, 10.0);
                view.Height = Math.Max(max.Y - min.Y, 10.0);
                ed.SetCurrentView(view);
            }
            catch
            {
            }
        }

        private static void IncludePoint(ref Extents3d? extents, Point3d point)
        {
            if (!extents.HasValue)
            {
                extents = new Extents3d(point, point);
            }
            else
            {
                Extents3d updated = extents.Value;
                updated.AddPoint(point);
                extents = updated;
            }
        }

        private static double CalculatePerimeter(List<Point2d> vertices)
        {
            if (vertices.Count < 2)
            {
                return 0.0;
            }

            double total = 0.0;
            for (int i = 1; i < vertices.Count; i++)
            {
                total += vertices[i - 1].GetDistanceTo(vertices[i]);
            }
            return total;
        }

        private static double CalculateArea(List<Point2d> vertices)
        {
            if (vertices.Count < 3)
            {
                return 0.0;
            }

            List<Point2d> pts = new List<Point2d>(vertices);
            if (pts[0].GetDistanceTo(pts[^1]) > ClosureTolerance)
            {
                pts.Add(pts[0]);
            }

            double sum = 0.0;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                sum += pts[i].X * pts[i + 1].Y - pts[i + 1].X * pts[i].Y;
            }

            return Math.Abs(sum) / 2.0;
        }

        private static Point3d MidPoint(Point3d a, Point3d b)
        {
            return new Point3d((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0, 0.0);
        }

        private static double Distance2d(Point3d a, Point3d b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }

        private static double NormalizeRadians(double radians)
        {
            double twoPi = Math.PI * 2.0;
            radians %= twoPi;
            if (radians < 0.0)
            {
                radians += twoPi;
            }

            return radians;
        }

        private static string AppendNote(string current, string addition)
        {
            if (string.IsNullOrWhiteSpace(current))
            {
                return addition;
            }

            return current + " " + addition;
        }

        private static string FormatDouble(double? value)
        {
            return value.HasValue ? value.Value.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, Math.Max(0, maxLength - 3)) + "...";
        }

        private static bool TryBearingToVector(string bearing, out Vector2d vector)
        {
            vector = Vector2d.XAxis;
            if (!TryBearingToAzimuthDegrees(bearing, out double azimuthDegrees))
            {
                return false;
            }

            double radians = DegreesToRadians(azimuthDegrees);
            vector = new Vector2d(Math.Sin(radians), Math.Cos(radians));
            return true;
        }

        private static bool TryBearingToAzimuthDegrees(string bearing, out double azimuthDegrees)
        {
            azimuthDegrees = 0.0;
            if (string.IsNullOrWhiteSpace(bearing))
            {
                return false;
            }

            string text = bearing.Trim().ToUpperInvariant();
            text = text.Replace("º", "°").Replace("D", "°");
            text = Regex.Replace(text, @"\s+", "");

            Match quadrant = Regex.Match(text, @"^([NS])(\d+(?:\.\d+)?)(?:°)?(?:(\d+(?:\.\d+)?)')?(?:(\d+(?:\.\d+)?)""?)?([EW])$");
            if (!quadrant.Success)
            {
                Match numeric = Regex.Match(text, @"^(\d+(?:\.\d+)?)$");
                if (numeric.Success && double.TryParse(numeric.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double numericAzimuth))
                {
                    azimuthDegrees = numericAzimuth;
                    return true;
                }

                return false;
            }

            string ns = quadrant.Groups[1].Value;
            double deg = double.Parse(quadrant.Groups[2].Value, CultureInfo.InvariantCulture);
            double min = quadrant.Groups[3].Success ? double.Parse(quadrant.Groups[3].Value, CultureInfo.InvariantCulture) : 0.0;
            double sec = quadrant.Groups[4].Success ? double.Parse(quadrant.Groups[4].Value, CultureInfo.InvariantCulture) : 0.0;
            string ew = quadrant.Groups[5].Value;
            double angle = deg + min / 60.0 + sec / 3600.0;

            if (ns == "N" && ew == "E")
            {
                azimuthDegrees = angle;
            }
            else if (ns == "S" && ew == "E")
            {
                azimuthDegrees = 180.0 - angle;
            }
            else if (ns == "S" && ew == "W")
            {
                azimuthDegrees = 180.0 + angle;
            }
            else
            {
                azimuthDegrees = 360.0 - angle;
            }

            return true;
        }

        private sealed class EasementCsvRow
        {
            public string SegmentId { get; set; } = string.Empty;
            public int Segment { get; set; }
            public string FeatureType { get; set; } = string.Empty;
            public string LineType { get; set; } = string.Empty;
            public string Layer { get; set; } = string.Empty;
            public string Type { get; set; } = "LINE";
            public string ImportRole { get; set; } = string.Empty;
            public string MarkerLabel { get; set; } = string.Empty;
            public string LabelText { get; set; } = string.Empty;
            public string FromPoint { get; set; } = string.Empty;
            public string ToPoint { get; set; } = string.Empty;
            public string Bearing { get; set; } = string.Empty;
            public double? AzimuthDeg { get; set; }
            public double? Distance { get; set; }
            public double? StartX { get; set; }
            public double? StartY { get; set; }
            public double? EndX { get; set; }
            public double? EndY { get; set; }
            public string CurveDirection { get; set; } = string.Empty;
            public double? Radius { get; set; }
            public string Delta { get; set; } = string.Empty;
            public double? ArcLength { get; set; }
            public double? Tangent { get; set; }
            public string ChordBearing { get; set; } = string.Empty;
            public double? ChordLength { get; set; }
            public string RadialBearing { get; set; } = string.Empty;
            public string StartRadialBearing { get; set; } = string.Empty;
            public string EndRadialBearing { get; set; } = string.Empty;
            public string RadialDirectionUsed { get; set; } = string.Empty;
            public string ImportStatus { get; set; } = string.Empty;
            public string QAStatus { get; set; } = string.Empty;
            public string Notes { get; set; } = string.Empty;
            public int SortOrder { get; set; }

            public string DisplaySegment => !string.IsNullOrWhiteSpace(SegmentId) ? SegmentId : Segment.ToString(CultureInfo.InvariantCulture);
            public string ToPointOrSegment => !string.IsNullOrWhiteSpace(ToPoint) ? ToPoint : DisplaySegment;
            public string PointMarkerDisplayText
            {
                get
                {
                    string label = !string.IsNullOrWhiteSpace(MarkerLabel) ? MarkerLabel : ToPointOrSegment;
                    if (!string.IsNullOrWhiteSpace(LabelText) && !LabelText.Equals(label, StringComparison.OrdinalIgnoreCase))
                    {
                        return label + " - " + LabelText;
                    }

                    return label;
                }
            }
            public bool HasStartCoordinate => StartX.HasValue && StartY.HasValue;
            public bool HasEndCoordinate => EndX.HasValue && EndY.HasValue;
            // FeatureType often equals the broad value "EASEMENT" for every row in Prompt Easement CSV files.
            // Do not use that broad feature value alone to classify boundary rows; otherwise commencement ties
            // are incorrectly treated as boundary geometry and the POB marker is placed at the POC.
            public bool IsBoundary => ImportRole.Equals("EASEMENT", StringComparison.OrdinalIgnoreCase) || ContainsAny(LineType, "EASEMENT_BOUNDARY", "BOUNDARY") || ContainsAny(FeatureType, "EASEMENT_BOUNDARY") || QAStatus.Contains("EASEMENT_BOUNDARY", StringComparison.OrdinalIgnoreCase) || DisplaySegment.StartsWith("ESMT-B", StringComparison.OrdinalIgnoreCase);
            public bool IsCommencementTie => ImportRole.Equals("COMMENCEMENT", StringComparison.OrdinalIgnoreCase) || ContainsAny(LineType, "COMMENCEMENT", "POC_TIE", "POB_TIE") || ContainsAny(FeatureType, "COMMENCEMENT", "POC_TIE", "POB_TIE");
            public bool IsPocRow => ImportRole.Equals("POC_MARKER", StringComparison.OrdinalIgnoreCase) || MarkerLabel.Equals("POC", StringComparison.OrdinalIgnoreCase) || FromPoint.Equals("POC", StringComparison.OrdinalIgnoreCase) || ToPoint.Equals("POC", StringComparison.OrdinalIgnoreCase) || LineType.Contains("POC", StringComparison.OrdinalIgnoreCase);
            public bool IsPobRow => ImportRole.Equals("POB_MARKER", StringComparison.OrdinalIgnoreCase) || MarkerLabel.Equals("POB", StringComparison.OrdinalIgnoreCase) || FromPoint.Equals("POB", StringComparison.OrdinalIgnoreCase) || ToPoint.Equals("POB", StringComparison.OrdinalIgnoreCase) || LineType.Contains("POB", StringComparison.OrdinalIgnoreCase);
            public bool IsQa => ContainsAny(LineType, "QA") || ContainsAny(FeatureType, "QA") || Layer.Equals(LayerQa, StringComparison.OrdinalIgnoreCase) || ImportStatus.Equals("IMPORT_QA_LAYER", StringComparison.OrdinalIgnoreCase) || ImportStatus.Equals("MANUAL_REVIEW", StringComparison.OrdinalIgnoreCase) || QAStatus.Contains("REVIEW", StringComparison.OrdinalIgnoreCase) || QAStatus.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase) || QAStatus.Contains("INCOMPLETE", StringComparison.OrdinalIgnoreCase) || QAStatus.Contains("UNKNOWN_CURVE_CONTROL", StringComparison.OrdinalIgnoreCase);
            public bool IsNoteOnly => Type.Equals("NOTE", StringComparison.OrdinalIgnoreCase);
            public bool IsPointOnly => Type.Equals("POINT_MARKER", StringComparison.OrdinalIgnoreCase) || Type.Equals("POINT", StringComparison.OrdinalIgnoreCase) || Type.Equals("POINT_TIE", StringComparison.OrdinalIgnoreCase) || ImportRole.EndsWith("_MARKER", StringComparison.OrdinalIgnoreCase) || ContainsAny(LineType, "POINT_MARKER", "POINT_TIE", "CONTROL_REFERENCE") || ContainsAny(FeatureType, "POINT_MARKER", "POINT_TIE", "CONTROL_REFERENCE");
            public bool AllowChainContinuityCheck => IsBoundary || IsCommencementTie;

            public static List<EasementCsvRow> Load(string path)
            {
                string[] lines = File.ReadAllLines(path);
                if (lines.Length == 0)
                {
                    return new List<EasementCsvRow>();
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

                List<EasementCsvRow> rows = new List<EasementCsvRow>();
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]))
                    {
                        continue;
                    }

                    List<string> values = SplitCsvLine(lines[i]);
                    string segmentId = FirstString(map, values, "SegmentID", "FeatureID", "Segment");
                    int segment = GetInt(map, values, "Segment");
                    if (segment == 0)
                    {
                        segment = ExtractFirstInt(segmentId) ?? rows.Count + 1;
                    }

                    string lineType = FirstString(map, values, "LineType", "ReferenceClass", "FeatureType", "ImportRole");
                    string featureType = GetString(map, values, "FeatureType");
                    string importRole = GetString(map, values, "ImportRole");
                    string type = FirstString(map, values, "Type", "GeometryType");
                    if (string.IsNullOrWhiteSpace(type))
                    {
                        type = lineType.Contains("CURVE", StringComparison.OrdinalIgnoreCase) ? "CURVE" : "LINE";
                    }

                    EasementCsvRow row = new EasementCsvRow
                    {
                        SegmentId = segmentId,
                        Segment = segment,
                        SortOrder = rows.Count + 1,
                        FeatureType = featureType,
                        LineType = lineType,
                        Layer = GetString(map, values, "Layer"),
                        Type = type,
                        ImportRole = importRole,
                        MarkerLabel = GetString(map, values, "MarkerLabel"),
                        LabelText = GetString(map, values, "LabelText"),
                        FromPoint = FirstString(map, values, "FromPoint", "From"),
                        ToPoint = FirstString(map, values, "ToPoint", "To"),
                        Bearing = GetString(map, values, "Bearing"),
                        AzimuthDeg = FirstNullableDouble(map, values, "Azimuth_Deg", "Azimuth"),
                        Distance = FirstNullableDouble(map, values, "Distance_Ft", "Distance", "AdjustedDistance", "OriginalDistance"),
                        StartX = FirstNullableDouble(map, values, "Start_X", "StartX"),
                        StartY = FirstNullableDouble(map, values, "Start_Y", "StartY"),
                        EndX = FirstNullableDouble(map, values, "End_X", "EndX"),
                        EndY = FirstNullableDouble(map, values, "End_Y", "EndY"),
                        CurveDirection = GetString(map, values, "CurveDirection"),
                        Radius = GetNullableDouble(map, values, "Radius"),
                        Delta = GetString(map, values, "Delta"),
                        ArcLength = GetNullableDouble(map, values, "ArcLength"),
                        Tangent = GetNullableDouble(map, values, "Tangent"),
                        ChordBearing = GetString(map, values, "ChordBearing"),
                        ChordLength = GetNullableDouble(map, values, "ChordLength"),
                        RadialBearing = GetString(map, values, "RadialBearing"),
                        StartRadialBearing = GetString(map, values, "StartRadialBearing"),
                        EndRadialBearing = GetString(map, values, "EndRadialBearing"),
                        RadialDirectionUsed = GetString(map, values, "RadialDirectionUsed"),
                        ImportStatus = GetString(map, values, "ImportStatus"),
                        QAStatus = GetString(map, values, "QAStatus"),
                        Notes = FirstString(map, values, "Source_Note", "Notes", "SourceLabel")
                    };

                    if (string.IsNullOrWhiteSpace(row.Type) && row.ImportRole.EndsWith("_MARKER", StringComparison.OrdinalIgnoreCase))
                    {
                        row.Type = "POINT_MARKER";
                    }

                    if (string.IsNullOrWhiteSpace(row.ImportStatus))
                    {
                        row.ImportStatus = "IMPORT";
                    }

                    rows.Add(row);
                }

                return rows;
            }

            private static bool ContainsAny(string value, params string[] tokens)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return false;
                }

                return tokens.Any(t => value.Contains(t, StringComparison.OrdinalIgnoreCase));
            }

            private static int? ExtractFirstInt(string value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return null;
                }

                Match match = Regex.Match(value, @"\d+");
                if (match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
                {
                    return result;
                }

                return null;
            }

            private static string FirstString(Dictionary<string, int> map, List<string> values, params string[] keys)
            {
                foreach (string key in keys)
                {
                    string value = GetString(map, values, key);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        return value;
                    }
                }

                return string.Empty;
            }

            private static double? FirstNullableDouble(Dictionary<string, int> map, List<string> values, params string[] keys)
            {
                foreach (string key in keys)
                {
                    double? value = GetNullableDouble(map, values, key);
                    if (value.HasValue)
                    {
                        return value;
                    }
                }

                return null;
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

        private sealed class EasementImportSummary
        {
            public EasementImportSummary(string csvPath)
            {
                CsvPath = csvPath;
            }

            public string CsvPath { get; }
            public int TieCount { get; set; }
            public int BoundaryCount { get; set; }
            public int LineCount { get; set; }
            public int CurveCount { get; set; }
            public int ChordFallbackCount { get; set; }
            public int PointCount { get; set; }
            public double? EastingError { get; set; }
            public double? NorthingError { get; set; }
            public double? ClosureError { get; set; }
            public double Perimeter { get; set; }
            public double Area { get; set; }
            public List<string> Warnings { get; } = new List<string>();
            public List<string> Skipped { get; } = new List<string>();

            public void AddWarning(string segment, string note)
            {
                Warnings.Add($"{segment}: {note}");
            }

            public void AddSkipped(string segment, string note)
            {
                Skipped.Add($"{segment}: {note}");
            }

            public string BuildReport()
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("EASEMENT CSV IMPORT REPORT");
                sb.AppendLine(new string('=', 34));
                sb.AppendLine($"CSV: {CsvPath}");
                sb.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine();
                sb.AppendLine("IMPORT SUMMARY");
                sb.AppendLine($"Commencement/tie segments imported: {TieCount}");
                sb.AppendLine($"Easement boundary segments imported: {BoundaryCount}");
                sb.AppendLine($"Lines imported: {LineCount}");
                sb.AppendLine($"Curves imported: {CurveCount}");
                sb.AppendLine($"Curve chord fallbacks: {ChordFallbackCount}");
                sb.AppendLine($"Point markers imported: {PointCount}");
                sb.AppendLine($"Skipped segments: {Skipped.Count}");
                sb.AppendLine();

                sb.AppendLine("EASEMENT CLOSURE / AREA");
                if (ClosureError.HasValue)
                {
                    sb.AppendLine($"Easting error: {EastingError:0.000000}");
                    sb.AppendLine($"Northing error: {NorthingError:0.000000}");
                    sb.AppendLine($"Linear closure error: {ClosureError:0.000000} ft");
                    sb.AppendLine($"Closure status: {(ClosureError.Value <= ClosureTolerance ? "PASS" : "REVIEW")}");
                    sb.AppendLine($"Perimeter: {Perimeter:0.0000} ft");
                    sb.AppendLine($"Area: {Area:0.0000} sq ft");
                }
                else
                {
                    sb.AppendLine("No easement boundary closure was calculated. Confirm EASEMENT_BOUNDARY rows exist.");
                }

                sb.AppendLine();
                sb.AppendLine("WARNINGS");
                if (Warnings.Count == 0)
                {
                    sb.AppendLine("None.");
                }
                else
                {
                    foreach (string warning in Warnings)
                    {
                        sb.AppendLine("- " + warning);
                    }
                }

                sb.AppendLine();
                sb.AppendLine("SKIPPED ROWS");
                if (Skipped.Count == 0)
                {
                    sb.AppendLine("None.");
                }
                else
                {
                    foreach (string skipped in Skipped)
                    {
                        sb.AppendLine("- " + skipped);
                    }
                }

                sb.AppendLine();
                sb.AppendLine("NOTES");
                sb.AppendLine("Standalone text-only easements may intentionally start from POC/POB at 10000,10000 and can be moved into final CAD location after import.");
                sb.AppendLine("Commencement/tie rows are drawn on control/tie layers; easement boundary rows are drawn on V-MAPL-ESMT or the CSV-requested easement line layer unless the row is QA.");
                sb.AppendLine("Rebuilt Prompt Easement CSVs are supported with Type=LINE/CURVE/POINT_MARKER and ImportRole=COMMENCEMENT/EASEMENT/POC_MARKER/POB_MARKER.");
                return sb.ToString();
            }
        }
    }
}
