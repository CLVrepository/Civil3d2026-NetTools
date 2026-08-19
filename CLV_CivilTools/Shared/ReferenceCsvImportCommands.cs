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
    /// Imports Prompt 1 sectional / control framework CSV files.
    ///
    /// Command names:
    ///   CLV_SECTIONAL_IMPORT
    ///   CLV_REFERENCE_IMPORT
    ///   CLV-REFERENCE-CSV-IMPORT
    ///   CLV-SECTIONAL-CSV-IMPORT
    ///   SURVEY-REFERENCE-CSV-IMPORT
    ///
    /// The primary importer expects the new Prompt 1 sectional/control CSV fields.
    /// It also remains backward compatible with older Prompt 3 reference CSVs.
    /// Prompt 1 / Prompt 4 rows are drawn from the POC-anchored coordinates exported by the prompt.
    /// Prompt 4 endpoint-controlled rows and the simplified Prompt 4 Final Import CSV
    /// always use StartX/StartY directly to EndX/EndY so resolved parent-boundary
    /// and split-point coordinates are not rebuilt from rounded bearing/distance labels. Legacy direct BEARING_DISTANCE rows still use AdjustedDistance
    /// when present so length-only closure corrections are respected while the original map
    /// distance is preserved for QA.
    /// </summary>
    public class ReferenceCsvImportCommands
    {
        private const string SectionalGeometryLayer = "0";
        private const string DefaultLayerText = "V-MAPL-REF-TEXT";
        private const string DefaultLayerQa = "V-MAPL-QA";
        private const string DefaultLayerNote = "V-MAPL-REF-NOTE";
        private const double DefaultTextHeight = 8.0;
        private const double WidthTolerance = 0.05;
        private const double BearingDistanceEndpointTolerance = 0.05;
        private const double BearingCoordinateAngleToleranceDegrees = 0.01;
        private const double ClosedTraverseTolerance = 0.000001;

        [CommandMethod("CLV_SECTIONAL_IMPORT")]
        public void ClvSectionalImport()
        {
            RunReferenceImport();
        }

        [CommandMethod("CLV_REFERENCE_IMPORT")]
        public void ClvReferenceImport()
        {
            RunReferenceImport();
        }

        [CommandMethod("CLV-REFERENCE-CSV-IMPORT")]
        public void ClvReferenceCsvImport()
        {
            RunReferenceImport();
        }

        [CommandMethod("CLV-SECTIONAL-CSV-IMPORT")]
        public void ClvSectionalCsvImport()
        {
            RunReferenceImport();
        }

        [CommandMethod("SURVEY-REFERENCE-CSV-IMPORT")]
        public void SurveyReferenceCsvImport()
        {
            RunReferenceImport();
        }

        private static void RunReferenceImport()
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
                ed.WriteMessage("\nSectional CSV import cancelled.");
                return;
            }

            List<ReferenceCsvRow> rows;
            try
            {
                rows = ReferenceCsvRow.Load(csvPath);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUnable to read sectional/control CSV: {ex.Message}");
                return;
            }

            if (rows.Count == 0)
            {
                ed.WriteMessage("\nNo sectional/control rows were found in the selected CSV.");
                return;
            }

            ReferenceImportSummary summary = new ReferenceImportSummary(csvPath);
            Dictionary<int, ResolvedGeometry> closedTraverseOverrides = ResolveClosedOuterTraverseRows(rows, summary);
            ed.WriteMessage("\n1-SECTIONAL IMPORT defaults: Prompt 1/Prompt 4 sectional-control CSV aware. Geometry is drawn on layer 0. Labels are limited to street names only. Prompt 4 Final Import CSV rows use CSV StartX/StartY -> EndX/EndY exactly; Bearing/Distance columns are optional and not required. REVIEW_ONLY/DO_NOT_IMPORT rows are skipped. Legacy BEARING_DISTANCE rows use Start + Bearing + AdjustedDistance when supplied; otherwise Distance is used. OUTER_CONTROL_TRAVERSE rows are chain-built only when explicitly marked.");

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EnsureDefaultLayers(db, tr);
                EnsureLayer(db, tr, SectionalGeometryLayer, 7, "M");

                BlockTable blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                Extents3d? extents = null;

                foreach (ReferenceCsvRow row in rows.OrderBy(r => r.Segment))
                {
                    if (ShouldSkipByImportStatus(row))
                    {
                        summary.AddSkipped(row.Segment, $"ImportStatus is {row.ImportStatus}.");
                        continue;
                    }

                    bool warning = IsWarningRow(row);
                    string layerName = ResolveLayer(row, warning);
                    EnsureLayer(db, tr, layerName, DefaultColorForLayer(layerName, row), "M");
                    string geometryLayerName = SectionalGeometryLayer;

                    if (row.Type.Equals("NOTE", StringComparison.OrdinalIgnoreCase))
                    {
                        if (TryGetAnyPoint(row, out Point3d notePoint))
                        {
                            AddReferenceLabel(modelSpace, tr, notePoint, row, DefaultTextHeight, true, "NOTE", layerName);
                            IncludePoint(ref extents, notePoint);
                            summary.NoteCount++;
                        }
                        else
                        {
                            summary.AddSkipped(row.Segment, "NOTE row has no usable coordinate.");
                        }

                        AddRowWarnings(summary, row);
                        continue;
                    }

                    if (!TryResolveStartEnd(row, closedTraverseOverrides, out Point3d start, out Point3d end, out string geometryNote))
                    {
                        summary.AddSkipped(row.Segment, geometryNote);
                        AddRowWarnings(summary, row);
                        continue;
                    }

                    string type = row.Type.Trim().ToUpperInvariant();
                    if (IsPointRowType(type))
                    {
                        DBPoint point = new DBPoint(start) { Layer = geometryLayerName };
                        modelSpace.AppendEntity(point);
                        tr.AddNewlyCreatedDBObject(point, true);
                        AddReferenceLabel(modelSpace, tr, start, row, DefaultTextHeight, warning, null, layerName: null);
                        IncludePoint(ref extents, start);
                        summary.TieCount++;
                    }
                    else if (type == "CURVE")
                    {
                        if (TryCreateArcFromStartEnd(row, start, end, out Arc? arc, out Point3d labelPoint, out string curveNote))
                        {
                            arc.Layer = geometryLayerName;
                            modelSpace.AppendEntity(arc);
                            tr.AddNewlyCreatedDBObject(arc, true);
                            AddReferenceLabel(modelSpace, tr, labelPoint, row, DefaultTextHeight, warning, curveNote, layerName: null);
                            IncludePoint(ref extents, start);
                            IncludePoint(ref extents, end);
                            IncludePoint(ref extents, labelPoint);
                            summary.CurveCount++;
                            if (!string.IsNullOrWhiteSpace(curveNote))
                            {
                                summary.AddWarning(row.Segment, curveNote);
                            }
                        }
                        else
                        {
                            Line chord = new Line(start, end) { Layer = geometryLayerName };
                            modelSpace.AppendEntity(chord);
                            tr.AddNewlyCreatedDBObject(chord, true);
                            AddReferenceLabel(modelSpace, tr, MidPoint(start, end), row, DefaultTextHeight, true, "CURVE CHORD QA", layerName: null);
                            IncludePoint(ref extents, start);
                            IncludePoint(ref extents, end);
                            summary.ChordFallbackCount++;
                            summary.AddWarning(row.Segment, "Curve could not be created as an arc; chord QA line was drawn.");
                        }
                    }
                    else
                    {
                        Line line = new Line(start, end) { Layer = geometryLayerName };
                        modelSpace.AppendEntity(line);
                        tr.AddNewlyCreatedDBObject(line, true);
                        AddReferenceLabel(modelSpace, tr, MidPoint(start, end), row, DefaultTextHeight, warning, null, layerName: null);
                        IncludePoint(ref extents, start);
                        IncludePoint(ref extents, end);

                        if (type == "RADIAL")
                        {
                            summary.RadialCount++;
                        }
                        else if (type == "POINT_TIE")
                        {
                            summary.TieCount++;
                        }
                        else
                        {
                            summary.LineCount++;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(geometryNote))
                    {
                        summary.AddWarning(row.Segment, geometryNote);
                    }

                    CheckWidthCallout(row, start, end, summary);
                    AddRowWarnings(summary, row);
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
                Title = "Select Prompt 1 Sectional / Control CSV",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                Multiselect = false,
                CheckFileExists = true
            };

            DialogResult result = dialog.ShowDialog();
            return result == DialogResult.OK ? dialog.FileName : null;
        }

        private static void EnsureDefaultLayers(Database db, Transaction tr)
        {
            EnsureLayer(db, tr, "V-MAPL-BNDY-REF", 7, "M");
            EnsureLayer(db, tr, "V-MAPL-ROAD-CL", 11, "M");
            EnsureLayer(db, tr, "V-MAPL-ROAD-ROW", 83, "M");
            EnsureLayer(db, tr, "V-MAPL-SECT", 6, "M");
            EnsureLayer(db, tr, "V-MAPL-QTR", 5, "M");
            EnsureLayer(db, tr, "V-MAPL-SIXTEENTH", 3, "M");
            EnsureLayer(db, tr, "V-MAPL-ESMT", 8, "M");
            EnsureLayer(db, tr, "V-MAPL-CNTRL", 1, "M");
            EnsureLayer(db, tr, "V-MAPL-RADIAL", 30, "M");
            EnsureLayer(db, tr, "V-MAPL-CNSTR", 9, "M");
            EnsureLayer(db, tr, DefaultLayerQa, 1, "M");
            EnsureLayer(db, tr, DefaultLayerText, 3, "M");
            EnsureLayer(db, tr, DefaultLayerNote, 7, "M");
        }

        private static bool ShouldSkipByImportStatus(ReferenceCsvRow row)
        {
            string status = row.ImportStatus.Trim();
            if (status.Equals("DO_NOT_IMPORT", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("REVIEW_ONLY", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("SOURCE_ONLY", StringComparison.OrdinalIgnoreCase) ||
                status.Equals("QA_ONLY", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static bool IsPointRowType(string type)
        {
            string normalized = type.Trim().ToUpperInvariant();
            return normalized == "POINT_TIE" ||
                   normalized == "POINT_MARKER" ||
                   normalized == "CONTROL_POINT" ||
                   normalized == "POINT";
        }

        private static string ResolveLayer(ReferenceCsvRow row, bool warning)
        {
            if (warning || row.ImportStatus.Equals("IMPORT_QA_LAYER", StringComparison.OrdinalIgnoreCase) || row.ImportStatus.Equals("MANUAL_REVIEW", StringComparison.OrdinalIgnoreCase))
            {
                return DefaultLayerQa;
            }

            if (!string.IsNullOrWhiteSpace(row.Layer))
            {
                return row.Layer.Trim();
            }

            string referenceClass = row.ReferenceClass.Trim().ToUpperInvariant();
            string featureType = row.FeatureType.Trim().ToUpperInvariant();

            if (referenceClass == "ROAD_CENTERLINE") return "V-MAPL-ROAD-CL";
            if (referenceClass == "RIGHT_OF_WAY" || featureType == "RIGHT_OF_WAY") return "V-MAPL-ROAD-ROW";
            if (referenceClass == "SECTION_LINE") return "V-MAPL-SECT";
            if (referenceClass == "QUARTER_SECTION_LINE") return "V-MAPL-QTR";
            if (referenceClass == "SIXTEENTH_SECTION_LINE") return "V-MAPL-SIXTEENTH";
            if (referenceClass == "EASEMENT" || featureType == "EASEMENT") return "V-MAPL-ESMT";
            if (referenceClass == "MONUMENT_TIE" || featureType == "MONUMENT_TIE") return "V-MAPL-CNTRL";
            if (referenceClass == "RADIAL_LINE" || featureType == "RADIAL_CONTROL") return "V-MAPL-RADIAL";
            if (referenceClass == "OFFSET_LINE") return "V-MAPL-CNSTR";
            if (featureType == "BOUNDARY_REFERENCE") return "V-MAPL-BNDY-REF";

            return "V-MAPL-CNTRL";
        }

        private static short DefaultColorForLayer(string layerName, ReferenceCsvRow row)
        {
            if (layerName.Equals(DefaultLayerQa, StringComparison.OrdinalIgnoreCase)) return 1;
            if (layerName.Contains("ROAD-CL", StringComparison.OrdinalIgnoreCase)) return 2;
            if (layerName.Contains("ROAD-ROW", StringComparison.OrdinalIgnoreCase)) return 4;
            if (layerName.Contains("SECT", StringComparison.OrdinalIgnoreCase)) return 6;
            if (layerName.Contains("QTR", StringComparison.OrdinalIgnoreCase)) return 5;
            if (layerName.Contains("SIXTEENTH", StringComparison.OrdinalIgnoreCase)) return 3;
            if (layerName.Contains("ESMT", StringComparison.OrdinalIgnoreCase)) return 8;
            if (layerName.Contains("RADIAL", StringComparison.OrdinalIgnoreCase)) return 30;
            if (layerName.Contains("CNSTR", StringComparison.OrdinalIgnoreCase)) return 9;
            return 7;
        }

        private static bool TryResolveStartEnd(ReferenceCsvRow row, Dictionary<int, ResolvedGeometry> closedTraverseOverrides, out Point3d start, out Point3d end, out string note)
        {
            note = string.Empty;
            start = Point3d.Origin;
            end = Point3d.Origin;

            if (closedTraverseOverrides.TryGetValue(row.Segment, out ResolvedGeometry? resolved))
            {
                start = resolved.Start;
                end = resolved.End;
                note = resolved.Note;
                row.AdjustedDistance = resolved.AdjustedDistance;
                row.LengthAdjustment = resolved.LengthAdjustment;
                if (!row.OriginalDistance.HasValue)
                {
                    row.OriginalDistance = resolved.OriginalDistance;
                }
                return Distance2d(start, end) >= 1e-8;
            }

            bool hasStart = row.StartX.HasValue && row.StartY.HasValue;
            bool hasEnd = row.EndX.HasValue && row.EndY.HasValue;
            Vector2d direction = Vector2d.XAxis;
            double? buildDistance = GetBuildDistance(row);
            bool canBuildFromBearingDistance = hasStart && buildDistance.HasValue && TryBearingToVector(row.Bearing, out direction);

            if (hasStart)
            {
                start = new Point3d(row.StartX!.Value, row.StartY!.Value, 0.0);
            }

            if (hasEnd)
            {
                end = new Point3d(row.EndX!.Value, row.EndY!.Value, 0.0);
            }

            string buildMode = NormalizeGeometryBuildMode(row);

            // Prompt 1 sectional/control geometry is already POC-anchored by the CSV.
            // The importer is not chain-built because full-length section/control rows,
            // monument ties, note rows, and derived lines may be independent.
            // Directly labeled BEARING_DISTANCE rows use Start + Bearing + build distance.
            // When AdjustedDistance is supplied by the length-only closure process, it
            // controls the CAD endpoint while OriginalDistance remains available for QA.
            if (canBuildFromBearingDistance && ShouldUseBearingDistance(row, buildMode))
            {
                Point3d calculatedEnd = new Point3d(start.X + direction.X * buildDistance!.Value, start.Y + direction.Y * buildDistance.Value, 0.0);
                if (hasEnd)
                {
                    AddCoordinateBearingConflictNote(row, start, end, calculatedEnd, ref note, "bearing/distance endpoint was used");
                }
                else
                {
                    AppendNote(ref note, "End coordinate derived from StartX/StartY + Bearing/Distance." + GetDistanceQaText(row));
                }

                end = calculatedEnd;
                return Distance2d(start, end) >= 1e-8;
            }

            if (hasStart && hasEnd)
            {
                if (Distance2d(start, end) < 1e-8)
                {
                    if (IsPointRowType(row.Type))
                    {
                        note = "Point row imported from identical Start/End coordinate.";
                        return true;
                    }

                    note = "Start and end coordinates are identical.";
                    return false;
                }

                CheckCoordinateVsBearingDistance(row, start, end, ref note);
                return true;
            }

            if (canBuildFromBearingDistance)
            {
                end = new Point3d(start.X + direction.X * buildDistance!.Value, start.Y + direction.Y * buildDistance.Value, 0.0);
                note = "End coordinate derived from StartX/StartY + Bearing/Distance." + GetDistanceQaText(row);
                return true;
            }

            note = "Missing usable Start/End coordinates or Start + Bearing/Distance. Prompt 1 sectional/control CSV should be anchored to the orange POC / 10000,10000 coordinate system.";
            return false;
        }


        private static Dictionary<int, ResolvedGeometry> ResolveClosedOuterTraverseRows(List<ReferenceCsvRow> rows, ReferenceImportSummary summary)
        {
            Dictionary<int, ResolvedGeometry> result = new Dictionary<int, ResolvedGeometry>();
            List<ReferenceCsvRow> candidates = rows
                .Where(IsClosedOuterTraverseCandidate)
                .OrderBy(r => r.Segment)
                .ToList();

            if (candidates.Count == 0)
            {
                return result;
            }

            List<ReferenceCsvRow> chain = SelectLongestSequentialChain(candidates);
            if (chain.Count < 3)
            {
                summary.AddWarning(0, "OUTER_CONTROL_TRAVERSE markers were found, but fewer than three sequential LINE rows were usable for forced closure.");
                return result;
            }

            if (!TryResolveAnchorStart(chain, out Point3d anchorStart, out string anchorNote))
            {
                summary.AddWarning(0, "OUTER_CONTROL_TRAVERSE forced closure skipped: no usable start coordinate was found on the first chain row.");
                return result;
            }

            int n = chain.Count;
            double[] distances = new double[n];
            Vector2d[] directions = new Vector2d[n];
            Vector2d closure = new Vector2d(0.0, 0.0);
            double perimeter = 0.0;

            for (int i = 0; i < n; i++)
            {
                ReferenceCsvRow row = chain[i];
                double? baseDistance = GetBuildDistance(row) ?? row.Distance ?? row.OriginalDistance;
                if (!baseDistance.HasValue || baseDistance.Value <= 0.0 || !TryBearingToVector(row.Bearing, out Vector2d direction))
                {
                    summary.AddWarning(row.Segment, "OUTER_CONTROL_TRAVERSE forced closure skipped: missing usable bearing or distance.");
                    return result;
                }

                distances[i] = baseDistance.Value;
                directions[i] = direction;
                closure = new Vector2d(closure.X + direction.X * baseDistance.Value, closure.Y + direction.Y * baseDistance.Value);
                perimeter += baseDistance.Value;
            }

            double initialClosure = Math.Sqrt(closure.X * closure.X + closure.Y * closure.Y);
            if (initialClosure <= ClosedTraverseTolerance)
            {
                BuildClosedTraverseOverride(chain, distances, directions, anchorStart, result, 0.0, 0.0, perimeter, "Already closed by bearing/distance chain", anchorNote);
                summary.ForcedClosedTraverseCount += chain.Count;
                summary.ForcedClosureInitialError = initialClosure;
                summary.ForcedClosureFinalError = 0.0;
                summary.AddWarning(0, $"OUTER_CONTROL_TRAVERSE chain-built with 0.00 closure; no length adjustment required. Rows={chain.Count}, Perimeter={perimeter:0.0000}'.");
                return result;
            }

            if (!TryComputeLengthOnlyCorrections(distances, directions, closure, out double[] corrections, out string correctionNote))
            {
                summary.AddWarning(0, "OUTER_CONTROL_TRAVERSE forced closure failed: " + correctionNote);
                return result;
            }

            double[] adjusted = new double[n];
            for (int i = 0; i < n; i++)
            {
                adjusted[i] = distances[i] + corrections[i];
                if (adjusted[i] <= 0.0)
                {
                    summary.AddWarning(chain[i].Segment, "OUTER_CONTROL_TRAVERSE forced closure failed: length adjustment produced a non-positive segment length.");
                    return result;
                }
            }

            Vector2d finalClosure = new Vector2d(0.0, 0.0);
            for (int i = 0; i < n; i++)
            {
                finalClosure = new Vector2d(finalClosure.X + directions[i].X * adjusted[i], finalClosure.Y + directions[i].Y * adjusted[i]);
            }

            double finalError = Math.Sqrt(finalClosure.X * finalClosure.X + finalClosure.Y * finalClosure.Y);
            BuildClosedTraverseOverride(chain, adjusted, directions, anchorStart, result, initialClosure, finalError, perimeter, "Length-only distributed closure; bearings held true", anchorNote, distances);
            summary.ForcedClosedTraverseCount += chain.Count;
            summary.ForcedClosureInitialError = initialClosure;
            summary.ForcedClosureFinalError = finalError;
            summary.AddWarning(0, $"OUTER_CONTROL_TRAVERSE forced to 0.00 closure by distributed length-only adjustment. Rows={chain.Count}, InitialClosure={initialClosure:0.0000}', FinalClosure={finalError:0.000000}', Perimeter={perimeter:0.0000}'. Bearings were held true.");
            return result;
        }

        private static bool IsClosedOuterTraverseCandidate(ReferenceCsvRow row)
        {
            if (row.ImportStatus.Equals("DO_NOT_IMPORT", StringComparison.OrdinalIgnoreCase) ||
                row.ImportStatus.Equals("MANUAL_REVIEW", StringComparison.OrdinalIgnoreCase) ||
                row.Type.Equals("CURVE", StringComparison.OrdinalIgnoreCase) ||
                row.Type.Equals("NOTE", StringComparison.OrdinalIgnoreCase) ||
                row.Type.Equals("POINT_TIE", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string text = (row.QAStatus + " " + row.Notes + " " + row.AdjustmentMethod + " " + row.GeometryBuildMode).ToUpperInvariant();
            return text.Contains("OUTER_CONTROL_TRAVERSE") ||
                   text.Contains("MAIN_CONTROL_TRAVERSE") ||
                   text.Contains("FORCE_CLOSED_TRAVERSE") ||
                   text.Contains("CLOSED_AFTER_LENGTH_ADJUSTMENT");
        }

        private static List<ReferenceCsvRow> SelectLongestSequentialChain(List<ReferenceCsvRow> candidates)
        {
            List<ReferenceCsvRow> best = new List<ReferenceCsvRow>();
            List<ReferenceCsvRow> current = new List<ReferenceCsvRow>();
            int? prior = null;

            foreach (ReferenceCsvRow row in candidates.OrderBy(r => r.Segment))
            {
                if (prior.HasValue && row.Segment != prior.Value + 1)
                {
                    if (current.Count > best.Count)
                    {
                        best = current;
                    }
                    current = new List<ReferenceCsvRow>();
                }

                current.Add(row);
                prior = row.Segment;
            }

            if (current.Count > best.Count)
            {
                best = current;
            }

            return best;
        }

        private static bool TryResolveAnchorStart(List<ReferenceCsvRow> chain, out Point3d anchorStart, out string note)
        {
            ReferenceCsvRow first = chain[0];
            if (first.StartX.HasValue && first.StartY.HasValue)
            {
                anchorStart = new Point3d(first.StartX.Value, first.StartY.Value, 0.0);
                note = "First OUTER_CONTROL_TRAVERSE row StartX/StartY held as chain start.";
                return true;
            }

            ReferenceCsvRow anchored = chain.FirstOrDefault(r => r.AnchorMethod.Contains("ORANGE", StringComparison.OrdinalIgnoreCase) && r.StartX.HasValue && r.StartY.HasValue) ??
                                      chain.FirstOrDefault(r => r.CoordinateAnchor.Contains("10000", StringComparison.OrdinalIgnoreCase) && r.StartX.HasValue && r.StartY.HasValue) ??
                                      chain.FirstOrDefault(r => r.StartX.HasValue && r.StartY.HasValue);
            if (anchored != null)
            {
                anchorStart = new Point3d(anchored.StartX!.Value, anchored.StartY!.Value, 0.0);
                note = $"OUTER_CONTROL_TRAVERSE chain start derived from segment {anchored.Segment}.";
                return true;
            }

            anchorStart = Point3d.Origin;
            note = string.Empty;
            return false;
        }

        private static bool TryComputeLengthOnlyCorrections(double[] distances, Vector2d[] directions, Vector2d closure, out double[] corrections, out string note)
        {
            corrections = new double[distances.Length];
            note = string.Empty;

            double m00 = 0.0;
            double m01 = 0.0;
            double m11 = 0.0;
            for (int i = 0; i < distances.Length; i++)
            {
                double weight = Math.Max(distances[i], 1.0);
                m00 += weight * directions[i].X * directions[i].X;
                m01 += weight * directions[i].X * directions[i].Y;
                m11 += weight * directions[i].Y * directions[i].Y;
            }

            double determinant = m00 * m11 - m01 * m01;
            if (Math.Abs(determinant) < 1e-10)
            {
                note = "bearing directions are nearly singular, so length-only closure cannot be solved.";
                return false;
            }

            double bx = closure.X;
            double by = closure.Y;
            double lambdaX = (m11 * bx - m01 * by) / determinant;
            double lambdaY = (-m01 * bx + m00 * by) / determinant;

            for (int i = 0; i < distances.Length; i++)
            {
                double weight = Math.Max(distances[i], 1.0);
                corrections[i] = -weight * (directions[i].X * lambdaX + directions[i].Y * lambdaY);
            }

            return true;
        }

        private static void BuildClosedTraverseOverride(List<ReferenceCsvRow> chain, double[] adjustedDistances, Vector2d[] directions, Point3d startPoint, Dictionary<int, ResolvedGeometry> result, double initialClosure, double finalClosure, double perimeter, string method, string anchorNote, double[]? originalDistances = null)
        {
            Point3d current = startPoint;
            Point3d anchor = startPoint;
            for (int i = 0; i < chain.Count; i++)
            {
                ReferenceCsvRow row = chain[i];
                Point3d start = current;
                Point3d end;
                if (i == chain.Count - 1)
                {
                    end = anchor;
                    adjustedDistances[i] = Distance2d(start, end);
                }
                else
                {
                    end = new Point3d(start.X + directions[i].X * adjustedDistances[i], start.Y + directions[i].Y * adjustedDistances[i], 0.0);
                }

                double original = originalDistances != null ? originalDistances[i] : adjustedDistances[i];
                double adjustment = adjustedDistances[i] - original;
                string note = $"OUTER_CONTROL_TRAVERSE_FORCE_CLOSED: chain-built from prior endpoint, bearings held true, distributed length adjustment applied to force CAD closure to 0.00. {method}. OriginalDistance={original:0.0000}', AdjustedDistance={adjustedDistances[i]:0.0000}', LengthAdjustment={adjustment:0.0000}', InitialClosure={initialClosure:0.0000}', FinalClosure=0.0000', Perimeter={perimeter:0.0000}'. {anchorNote}";
                result[row.Segment] = new ResolvedGeometry(start, end, original, adjustedDistances[i], adjustment, note);
                current = end;
            }
        }

        private static string NormalizeGeometryBuildMode(ReferenceCsvRow row)
        {
            string mode = row.GeometryBuildMode.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(mode))
            {
                return string.Empty;
            }

            mode = mode.Replace("-", "_").Replace(" ", "_");
            return mode;
        }

        private static bool ShouldUseBearingDistance(ReferenceCsvRow row, string buildMode)
        {
            if (row.Type.Equals("CURVE", StringComparison.OrdinalIgnoreCase) ||
                IsPointRowType(row.Type) ||
                row.Type.Equals("NOTE", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            bool hasCompleteCsvEndpoint = row.StartX.HasValue && row.StartY.HasValue && row.EndX.HasValue && row.EndY.HasValue;

            // Prompt 4 and newer Prompt 1 rows are endpoint-controlled once the parent boundary
            // has been resolved and split points have been recalculated.  Do not rebuild these
            // rows from rounded DMS bearings/distances; use CSV StartX/StartY -> EndX/EndY exactly.
            if (hasCompleteCsvEndpoint && IsEndpointControlledPromptRow(row, buildMode))
            {
                return false;
            }

            if (buildMode.Equals("BEARING_DISTANCE", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (IsCoordinateControlledBuildMode(buildMode))
            {
                return false;
            }

            // Legacy Prompt 3 CSVs did not have GeometryBuildMode. Keep intersection-network
            // rows coordinate controlled so centerline networks continue to share nodes, but
            // use bearing/distance for directly labeled independent rows such as interior lot
            // lines, ties, radials, and ordinary reference lines.
            if (row.QAStatus.Contains("INTERSECTION", StringComparison.OrdinalIgnoreCase) ||
                row.QAStatus.Contains("NODE", StringComparison.OrdinalIgnoreCase) ||
                row.QAStatus.Contains("ENDPOINT_COORDINATES_CONTROL", StringComparison.OrdinalIgnoreCase) ||
                row.Notes.Contains("ENDPOINT_COORDINATES_CONTROL", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static bool IsEndpointControlledPromptRow(ReferenceCsvRow row, string buildMode)
        {
            if (IsCoordinateControlledBuildMode(buildMode))
            {
                return true;
            }

            string featureType = row.FeatureType.Trim().ToUpperInvariant();
            string referenceClass = row.ReferenceClass.Trim().ToUpperInvariant();
            string importRole = row.ImportRole.Trim().ToUpperInvariant();
            string text = (featureType + " " + referenceClass + " " + importRole + " " + row.QAStatus + " " + row.Notes + " " + row.AdjustmentMethod).ToUpperInvariant();

            return featureType.Equals("PARENT_BOUNDARY", StringComparison.OrdinalIgnoreCase) ||
                   featureType.Equals("DERIVED_SECTION_LINE", StringComparison.OrdinalIgnoreCase) ||
                   featureType.Equals("DERIVED_SPLIT_LINE", StringComparison.OrdinalIgnoreCase) ||
                   featureType.Equals("SECTIONAL_LINE", StringComparison.OrdinalIgnoreCase) ||
                   featureType.Equals("CONTROL_LINE", StringComparison.OrdinalIgnoreCase) ||
                   referenceClass.Equals("PARENT_BOUNDARY_LINE", StringComparison.OrdinalIgnoreCase) ||
                   referenceClass.Equals("PARENT_FRAME_LINE", StringComparison.OrdinalIgnoreCase) ||
                   importRole.Equals("FINAL_PARENT_BOUNDARY", StringComparison.OrdinalIgnoreCase) ||
                   importRole.Equals("FINAL_SECTION_LINE", StringComparison.OrdinalIgnoreCase) ||
                   importRole.Equals("FINAL_CONTROL_LINE", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("PROMPT4") ||
                   text.Contains("PROMPT 4") ||
                   text.Contains("RESOLVED_PARENT_BOUNDARY") ||
                   text.Contains("RESOLVED PARENT BOUNDARY") ||
                   text.Contains("DERIVED_FROM_RESOLVED_PARENT_BOUNDARY") ||
                   text.Contains("DERIVED_FROM_FINAL_EXPORTED_PARENT_ROW") ||
                   text.Contains("ENDPOINT_COORDINATES_CONTROL");
        }

        private static bool IsCoordinateControlledBuildMode(string buildMode)
        {
            return buildMode.Equals("COORDINATE", StringComparison.OrdinalIgnoreCase) ||
                   buildMode.Equals("QA_COORDINATE", StringComparison.OrdinalIgnoreCase) ||
                   buildMode.Equals("INTERSECTION_COORDINATE", StringComparison.OrdinalIgnoreCase) ||
                   buildMode.Equals("SPLIT_POINT_COORDINATE", StringComparison.OrdinalIgnoreCase) ||
                   buildMode.Equals("RESOLVED_PARENT_BOUNDARY_LENGTH_ADJUSTED", StringComparison.OrdinalIgnoreCase) ||
                   buildMode.Equals("PARENT_FRAME_LENGTH_ADJUSTED", StringComparison.OrdinalIgnoreCase) ||
                   buildMode.Equals("SOURCE_PARENT_BOUNDARY_BEARING_DISTANCE", StringComparison.OrdinalIgnoreCase) ||
                   buildMode.Equals("PARENT_FRAME_BEARING_DISTANCE", StringComparison.OrdinalIgnoreCase) ||
                   buildMode.Equals("OFFSET", StringComparison.OrdinalIgnoreCase);
        }

        private static double? GetBuildDistance(ReferenceCsvRow row)
        {
            // New Prompt 1 sectional/control CSVs preserve both original map distance and
            // adjusted CAD distance after length-only closure adjustment. The adjusted
            // value is the build value when supplied. Older CSVs only have Distance.
            return row.AdjustedDistance ?? row.Distance;
        }

        private static string GetDistanceQaText(ReferenceCsvRow row)
        {
            if (row.AdjustedDistance.HasValue && row.OriginalDistance.HasValue &&
                Math.Abs(row.AdjustedDistance.Value - row.OriginalDistance.Value) > 0.0001)
            {
                return $" OriginalDistance={row.OriginalDistance.Value:0.0000}', AdjustedDistance={row.AdjustedDistance.Value:0.0000}', LengthAdjustment={(row.LengthAdjustment ?? row.AdjustedDistance.Value - row.OriginalDistance.Value):0.0000}'.";
            }

            if (row.AdjustedDistance.HasValue && row.Distance.HasValue &&
                Math.Abs(row.AdjustedDistance.Value - row.Distance.Value) > 0.0001)
            {
                return $" CSV Distance={row.Distance.Value:0.0000}', AdjustedDistance={row.AdjustedDistance.Value:0.0000}'.";
            }

            return string.Empty;
        }

        private static void CheckCoordinateVsBearingDistance(ReferenceCsvRow row, Point3d start, Point3d end, ref string note)
        {
            double? buildDistance = GetBuildDistance(row);
            if (!buildDistance.HasValue || !TryBearingToAzimuthRadians(row.Bearing, out double sourceAzimuth))
            {
                return;
            }

            double measured = Distance2d(start, end);
            double lengthError = Math.Abs(measured - buildDistance.Value);
            double coordAzimuth = Math.Atan2(end.X - start.X, end.Y - start.Y);
            coordAzimuth = NormalizeRadians(coordAzimuth);
            double angleError = RadiansToDegrees(SmallestAngleDifference(coordAzimuth, sourceAzimuth));

            if (lengthError > BearingDistanceEndpointTolerance || angleError > BearingCoordinateAngleToleranceDegrees)
            {
                AppendNote(ref note, $"Bearing/coordinate QA: coordinates measure {FormatBearing(coordAzimuth)} {measured:0.0000}' but CSV says {row.Bearing} {buildDistance.Value:0.0000}'. Coordinate geometry was used because GeometryBuildMode is coordinate-controlled or intersection-controlled. LengthError={lengthError:0.0000}', AngleError={angleError:0.0000}°.");
            }
        }

        private static void AddCoordinateBearingConflictNote(ReferenceCsvRow row, Point3d start, Point3d csvEnd, Point3d calculatedEnd, ref string note, string action)
        {
            double endpointGap = Distance2d(csvEnd, calculatedEnd);
            if (endpointGap <= BearingDistanceEndpointTolerance)
            {
                return;
            }

            double measured = Distance2d(start, csvEnd);
            double csvAzimuth = Math.Atan2(csvEnd.X - start.X, csvEnd.Y - start.Y);
            csvAzimuth = NormalizeRadians(csvAzimuth);

            AppendNote(ref note, $"BEARING_COORDINATE_CONFLICT: CSV Start/End coordinates measure {FormatBearing(csvAzimuth)} {measured:0.0000}' but CSV Bearing/Distance says {row.Bearing} {GetBuildDistance(row)!.Value:0.0000}'. End coordinate differs from calculated bearing/distance endpoint by {endpointGap:0.0000} ft; {action}.");
        }

        private static void AppendNote(ref string note, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            note = string.IsNullOrWhiteSpace(note) ? text : note + " " + text;
        }

        private static bool TryGetAnyPoint(ReferenceCsvRow row, out Point3d point)
        {
            if (row.StartX.HasValue && row.StartY.HasValue)
            {
                point = new Point3d(row.StartX.Value, row.StartY.Value, 0.0);
                return true;
            }

            if (row.EndX.HasValue && row.EndY.HasValue)
            {
                point = new Point3d(row.EndX.Value, row.EndY.Value, 0.0);
                return true;
            }

            point = Point3d.Origin;
            return false;
        }

        private static bool TryCreateArcFromStartEnd(ReferenceCsvRow row, Point3d start, Point3d end, out Arc? arc, out Point3d labelPoint, out string note)
        {
            arc = null;
            labelPoint = MidPoint(start, end);
            note = string.Empty;

            if (!row.Radius.HasValue || row.Radius.Value <= 0.0)
            {
                note = "Missing Radius.";
                return false;
            }

            double radius = row.Radius.Value;
            Point2d start2 = new Point2d(start.X, start.Y);
            Point2d end2 = new Point2d(end.X, end.Y);
            Vector2d chord = end2 - start2;
            double chordLength = chord.Length;
            if (chordLength <= 1e-8 || chordLength > radius * 2.0 + 0.001)
            {
                note = "Invalid start/end chord for Radius.";
                return false;
            }

            double halfChord = chordLength / 2.0;
            double hSquared = radius * radius - halfChord * halfChord;
            if (hSquared < -0.001)
            {
                note = "Radius is too small for start/end chord.";
                return false;
            }

            double h = Math.Sqrt(Math.Max(0.0, hSquared));
            Vector2d unitChord = chord.GetNormal();
            Vector2d leftNormal = new Vector2d(-unitChord.Y, unitChord.X);
            bool left = NormalizeCurveDirection(row.CurveDirection).Equals("LEFT", StringComparison.OrdinalIgnoreCase);
            Vector2d centerOffset = left ? leftNormal * h : leftNormal.Negate() * h;
            Point2d midpoint = start2 + chord * 0.5;
            Point2d center2 = midpoint + centerOffset;
            Point3d center = new Point3d(center2.X, center2.Y, 0.0);

            double startAngle = Math.Atan2(start.Y - center.Y, start.X - center.X);
            double endAngle = Math.Atan2(end.Y - center.Y, end.X - center.X);
            arc = left
                ? new Arc(center, radius, startAngle, endAngle)
                : new Arc(center, radius, endAngle, startAngle);

            labelPoint = new Point3d(midpoint.X + centerOffset.GetNormal().X * 8.0, midpoint.Y + centerOffset.GetNormal().Y * 8.0, 0.0);

            if (TryDmsAngleToRadians(row.Delta, out double deltaRadians) && row.ArcLength.HasValue)
            {
                double calcArc = radius * deltaRadians;
                double arcError = Math.Abs(calcArc - row.ArcLength.Value);
                if (arcError > 0.05)
                {
                    note = $"Arc length check differs by {arcError:0.0000} ft.";
                }
            }

            return true;
        }

        private static void CheckWidthCallout(ReferenceCsvRow row, Point3d start, Point3d end, ReferenceImportSummary summary)
        {
            if (!row.HasAnyWidthData)
            {
                return;
            }

            string checkStatus = row.WidthCheckStatus;

            // ROW width normally describes a perpendicular distance between centerline/ROW/boundary
            // features, not the length of the ROW line itself.  Only perform a direct measured-length
            // check for rows that are explicitly offset/tie/perpendicular measurement rows.
            bool rowRepresentsWidthMeasurement =
                row.Type.Equals("OFFSET", StringComparison.OrdinalIgnoreCase) ||
                row.Type.Equals("POINT_TIE", StringComparison.OrdinalIgnoreCase) ||
                row.RelationshipType.Contains("PERPENDICULAR", StringComparison.OrdinalIgnoreCase) ||
                row.RelationshipType.Contains("OFFSET", StringComparison.OrdinalIgnoreCase);

            if (rowRepresentsWidthMeasurement)
            {
                double measured = Distance2d(start, end);
                double? expected = row.MeasuredWidth ?? row.OffsetDistance ?? row.HalfRowWidth ?? row.RowWidth;
                if (expected.HasValue)
                {
                    double error = Math.Abs(measured - expected.Value);
                    if (error > WidthTolerance)
                    {
                        summary.AddWarning(row.Segment, $"Width/offset check: measured tie/offset length {measured:0.0000} ft differs from expected width/offset {expected.Value:0.0000} ft by {error:0.0000} ft. Check callout '{row.WidthCallout}'.");
                    }
                }
            }

            if (row.WidthError.HasValue && Math.Abs(row.WidthError.Value) > WidthTolerance)
            {
                summary.AddWarning(row.Segment, $"WidthError={row.WidthError.Value:0.0000} ft. WidthCallout='{row.WidthCallout}'.");
            }

            if (!string.IsNullOrWhiteSpace(checkStatus) &&
                (checkStatus.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase) || checkStatus.Contains("UNCERTAIN", StringComparison.OrdinalIgnoreCase)))
            {
                summary.AddWarning(row.Segment, $"WidthCheckStatus={checkStatus}. WidthCallout='{row.WidthCallout}'.");
            }
        }

        private static void AddRowWarnings(ReferenceImportSummary summary, ReferenceCsvRow row)
        {
            if (!string.IsNullOrWhiteSpace(row.AnchorMethod) && row.AnchorMethod.Equals("TEMP_LOCAL_QA", StringComparison.OrdinalIgnoreCase))
            {
                summary.AddWarning(row.Segment, "AnchorMethod=TEMP_LOCAL_QA. Geometry may not be tied to the Prompt 2 boundary/POB coordinate system.");
            }

            if (row.AdjustedDistance.HasValue && row.OriginalDistance.HasValue && Math.Abs(row.AdjustedDistance.Value - row.OriginalDistance.Value) > 0.0001)
            {
                summary.AddWarning(row.Segment, $"Prompt 1 length-only closure adjustment used. OriginalDistance={row.OriginalDistance.Value:0.0000}', AdjustedDistance={row.AdjustedDistance.Value:0.0000}', LengthAdjustment={(row.LengthAdjustment ?? row.AdjustedDistance.Value - row.OriginalDistance.Value):0.0000}'. Bearings were held by prompt design.");
            }

            if (IsWarningRow(row))
            {
                summary.AddWarning(row.Segment, GetRowWarningText(row));
            }
        }

        private static bool IsWarningRow(ReferenceCsvRow row)
        {
            return row.ImportStatus.Equals("IMPORT_QA_LAYER", StringComparison.OrdinalIgnoreCase) ||
                   row.ImportStatus.Equals("MANUAL_REVIEW", StringComparison.OrdinalIgnoreCase) ||
                   row.Confidence.Equals("UNCERTAIN", StringComparison.OrdinalIgnoreCase) ||
                   row.Confidence.Equals("APPROXIMATE", StringComparison.OrdinalIgnoreCase) ||
                   row.QAStatus.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase) ||
                   row.QAStatus.Contains("REVIEW", StringComparison.OrdinalIgnoreCase) ||
                   row.QAStatus.Contains("UNCERTAIN", StringComparison.OrdinalIgnoreCase) ||
                   row.RelationshipStatus.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase) ||
                   row.RelationshipStatus.Contains("UNCERTAIN", StringComparison.OrdinalIgnoreCase) ||
                   row.WidthCheckStatus.Contains("CONFLICT", StringComparison.OrdinalIgnoreCase) ||
                   row.WidthCheckStatus.Contains("UNCERTAIN", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetRowWarningText(ReferenceCsvRow row)
        {
            List<string> parts = new List<string>();
            AddPart(parts, "ImportStatus", row.ImportStatus);
            AddPart(parts, "QAStatus", row.QAStatus);
            AddPart(parts, "RelationshipStatus", row.RelationshipStatus);
            AddPart(parts, "WidthCheckStatus", row.WidthCheckStatus);
            AddPart(parts, "Notes", row.Notes);
            return parts.Count == 0 ? "QA warning row." : string.Join("; ", parts);
        }

        private static void AddPart(List<string> parts, string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value) && !value.Equals("OK", StringComparison.OrdinalIgnoreCase))
            {
                parts.Add(key + "=" + value);
            }
        }

        private static void AddReferenceLabel(BlockTableRecord modelSpace, Transaction tr, Point3d position, ReferenceCsvRow row, double textHeight, bool warning, string? prefix, string? layerName)
        {
            // 1-SECTIONAL IMPORT intentionally keeps labels minimal. The imported
            // linework is for geometric review, and users should add CAD line labels
            // when they want to verify bearings/distances. Only street names from
            // RoadName are placed automatically.
            string text = !string.IsNullOrWhiteSpace(row.RoadName) ? row.RoadName.Trim() : row.LineLabel.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            DBText dbText = new DBText
            {
                Position = new Point3d(position.X + textHeight * 0.2, position.Y + textHeight * 0.2, 0.0),
                Height = textHeight,
                TextString = TruncateForDbText(text, 180),
                Layer = layerName ?? DefaultLayerText
            };
            modelSpace.AppendEntity(dbText);
            tr.AddNewlyCreatedDBObject(dbText, true);
        }

        private static string TruncateForDbText(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }

            return value.Substring(0, Math.Max(0, maxLength - 3)) + "...";
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
                // Do not block import because of a plot-style setting issue.
            }
        }

        private static bool TryBearingToVector(string bearing, out Vector2d vector)
        {
            vector = Vector2d.XAxis;
            if (!TryBearingToAzimuthRadians(bearing, out double azimuth))
            {
                return false;
            }

            vector = AzimuthToVector(azimuth);
            return true;
        }

        private static Vector2d AzimuthToVector(double azimuthRadians)
        {
            return new Vector2d(Math.Sin(azimuthRadians), Math.Cos(azimuthRadians));
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
            if (ns == "N" && ew == "E") azimuthRadians = theta;
            else if (ns == "S" && ew == "E") azimuthRadians = Math.PI - theta;
            else if (ns == "S" && ew == "W") azimuthRadians = Math.PI + theta;
            else if (ns == "N" && ew == "W") azimuthRadians = Math.PI * 2.0 - theta;
            else return false;

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

        private static double Distance2d(Point3d a, Point3d b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

        private static double NormalizeRadians(double radians)
        {
            double twoPi = Math.PI * 2.0;
            radians %= twoPi;
            return radians < 0 ? radians + twoPi : radians;
        }

        private static double SmallestAngleDifference(double a, double b)
        {
            double diff = Math.Abs(NormalizeRadians(a) - NormalizeRadians(b));
            if (diff > Math.PI)
            {
                diff = Math.PI * 2.0 - diff;
            }

            return diff;
        }

        private static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;

        private static string FormatBearing(double azimuthRadians)
        {
            double azimuthDegrees = RadiansToDegrees(NormalizeRadians(azimuthRadians));
            string ns;
            string ew;
            double bearingDegrees;

            if (azimuthDegrees <= 90.0)
            {
                ns = "N";
                ew = "E";
                bearingDegrees = azimuthDegrees;
            }
            else if (azimuthDegrees <= 180.0)
            {
                ns = "S";
                ew = "E";
                bearingDegrees = 180.0 - azimuthDegrees;
            }
            else if (azimuthDegrees <= 270.0)
            {
                ns = "S";
                ew = "W";
                bearingDegrees = azimuthDegrees - 180.0;
            }
            else
            {
                ns = "N";
                ew = "W";
                bearingDegrees = 360.0 - azimuthDegrees;
            }

            int degrees = (int)Math.Floor(bearingDegrees);
            double minuteFloat = (bearingDegrees - degrees) * 60.0;
            int minutes = (int)Math.Floor(minuteFloat);
            double seconds = (minuteFloat - minutes) * 60.0;

            if (seconds >= 59.995)
            {
                seconds = 0.0;
                minutes++;
            }

            if (minutes >= 60)
            {
                minutes = 0;
                degrees++;
            }

            return $"{ns}{degrees:00}°{minutes:00}'{seconds:00.##}\"{ew}";
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
                ed.WriteMessage("\nSectional CSV imported, but zoom-to-extents failed.");
            }
        }

        private static string? TryWriteReport(ReferenceImportSummary summary)
        {
            try
            {
                string directory = Path.GetDirectoryName(summary.CsvPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string baseName = Path.GetFileNameWithoutExtension(summary.CsvPath);
                string reportPath = Path.Combine(directory, baseName + "_sectional_import_report.txt");

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("CLV 1-SECTIONAL IMPORT Report");
                sb.AppendLine($"CSV: {summary.CsvPath}");
                sb.AppendLine($"Created: {DateTime.Now}");
                sb.AppendLine();
                sb.AppendLine($"Lines imported: {summary.LineCount}");
                sb.AppendLine($"Curves imported: {summary.CurveCount}");
                sb.AppendLine($"Curve chord fallbacks: {summary.ChordFallbackCount}");
                sb.AppendLine($"Radial lines imported: {summary.RadialCount}");
                sb.AppendLine($"Tie lines imported: {summary.TieCount}");
                sb.AppendLine($"Notes imported: {summary.NoteCount}");
                sb.AppendLine($"Forced closed traverse rows: {summary.ForcedClosedTraverseCount}");
                if (summary.ForcedClosedTraverseCount > 0)
                {
                    sb.AppendLine($"Forced closure initial error: {summary.ForcedClosureInitialError:0.0000} ft");
                    sb.AppendLine($"Forced closure final error: {summary.ForcedClosureFinalError:0.000000} ft");
                }
                sb.AppendLine($"Skipped segments: {summary.Skipped.Count}");

                sb.AppendLine();
                sb.AppendLine("Warnings:");
                if (summary.Warnings.Count == 0) sb.AppendLine("  None");
                else foreach (string warning in summary.Warnings) sb.AppendLine("  - " + warning);

                sb.AppendLine();
                sb.AppendLine("Skipped:");
                if (summary.Skipped.Count == 0) sb.AppendLine("  None");
                else foreach (string skipped in summary.Skipped) sb.AppendLine("  - " + skipped);

                File.WriteAllText(reportPath, sb.ToString());
                return reportPath;
            }
            catch
            {
                return null;
            }
        }

        private static void WriteSummaryToCommandLine(Editor ed, ReferenceImportSummary summary, string? reportPath)
        {
            ed.WriteMessage("\nCLV 1-SECTIONAL IMPORT Complete");
            ed.WriteMessage($"\n  CSV: {summary.CsvPath}");
            ed.WriteMessage($"\n  Lines imported: {summary.LineCount}");
            ed.WriteMessage($"\n  Curves imported: {summary.CurveCount}");
            ed.WriteMessage($"\n  Curve chord fallbacks: {summary.ChordFallbackCount}");
            ed.WriteMessage($"\n  Radial lines imported: {summary.RadialCount}");
            ed.WriteMessage($"\n  Tie lines imported: {summary.TieCount}");
            ed.WriteMessage($"\n  Notes imported: {summary.NoteCount}");
            ed.WriteMessage($"\n  Forced closed traverse rows: {summary.ForcedClosedTraverseCount}");
            if (summary.ForcedClosedTraverseCount > 0)
            {
                ed.WriteMessage($"\n  Forced closure initial error: {summary.ForcedClosureInitialError:0.0000} ft");
                ed.WriteMessage($"\n  Forced closure final error: {summary.ForcedClosureFinalError:0.000000} ft");
            }
            ed.WriteMessage($"\n  Skipped segments: {summary.Skipped.Count}");

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

        private sealed class ResolvedGeometry
        {
            public ResolvedGeometry(Point3d start, Point3d end, double originalDistance, double adjustedDistance, double lengthAdjustment, string note)
            {
                Start = start;
                End = end;
                OriginalDistance = originalDistance;
                AdjustedDistance = adjustedDistance;
                LengthAdjustment = lengthAdjustment;
                Note = note;
            }

            public Point3d Start { get; }
            public Point3d End { get; }
            public double OriginalDistance { get; }
            public double AdjustedDistance { get; }
            public double LengthAdjustment { get; }
            public string Note { get; }
        }

        private sealed class ReferenceImportSummary
        {
            public ReferenceImportSummary(string csvPath)
            {
                CsvPath = csvPath;
            }

            public string CsvPath { get; }
            public int LineCount { get; set; }
            public int CurveCount { get; set; }
            public int ChordFallbackCount { get; set; }
            public int RadialCount { get; set; }
            public int TieCount { get; set; }
            public int NoteCount { get; set; }
            public int ForcedClosedTraverseCount { get; set; }
            public double ForcedClosureInitialError { get; set; }
            public double ForcedClosureFinalError { get; set; }
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

        private sealed class ReferenceCsvRow
        {
            public int Segment { get; set; }
            public string FeatureType { get; set; } = string.Empty;
            public string ReferenceClass { get; set; } = string.Empty;
            public string Layer { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
            public double? StartX { get; set; }
            public double? StartY { get; set; }
            public double? EndX { get; set; }
            public double? EndY { get; set; }
            public string Bearing { get; set; } = string.Empty;
            public double? Distance { get; set; }
            public double? OriginalDistance { get; set; }
            public double? AdjustedDistance { get; set; }
            public double? LengthAdjustment { get; set; }
            public string AdjustmentMethod { get; set; } = string.Empty;
            public string CurveDirection { get; set; } = string.Empty;
            public string CurveBuildMethod { get; set; } = string.Empty;
            public double? Radius { get; set; }
            public string Delta { get; set; } = string.Empty;
            public double? ArcLength { get; set; }
            public double? Tangent { get; set; }
            public string ChordBearing { get; set; } = string.Empty;
            public double? ChordLength { get; set; }
            public string RadialBearing { get; set; } = string.Empty;
            public string RoadName { get; set; } = string.Empty;
            public string LineLabel { get; set; } = string.Empty;
            public string ImportRole { get; set; } = string.Empty;
            public double? RowWidth { get; set; }
            public double? FullRowWidth { get; set; }
            public double? HalfRowWidth { get; set; }
            public string WidthCallout { get; set; } = string.Empty;
            public double? MeasuredWidth { get; set; }
            public double? WidthError { get; set; }
            public string WidthCheckStatus { get; set; } = string.Empty;
            public double? OffsetDistance { get; set; }
            public string OffsetDirection { get; set; } = string.Empty;
            public string FromPoint { get; set; } = string.Empty;
            public string ToPoint { get; set; } = string.Empty;
            public string MonumentName { get; set; } = string.Empty;
            public string MonumentType { get; set; } = string.Empty;
            public string RelatedBoundarySegment { get; set; } = string.Empty;
            public string RelatedBoundaryPoint { get; set; } = string.Empty;
            public string RelatedReferenceSegment { get; set; } = string.Empty;
            public string RelationshipType { get; set; } = string.Empty;
            public string RelationshipStatus { get; set; } = string.Empty;
            public string AnchorMethod { get; set; } = string.Empty;
            public string CoordinateAnchor { get; set; } = string.Empty;
            public string AnchorSourceSegment { get; set; } = string.Empty;
            public string AnchorSourcePoint { get; set; } = string.Empty;
            public string SourceSheet { get; set; } = string.Empty;
            public string SourceLabel { get; set; } = string.Empty;
            public string CorrectedLabel { get; set; } = string.Empty;
            public string GeometryBuildMode { get; set; } = string.Empty;
            public string Confidence { get; set; } = string.Empty;
            public string ImportStatus { get; set; } = string.Empty;
            public string QAStatus { get; set; } = string.Empty;
            public string Notes { get; set; } = string.Empty;

            public bool HasAnyWidthData => RowWidth.HasValue || FullRowWidth.HasValue || HalfRowWidth.HasValue ||
                                           MeasuredWidth.HasValue || WidthError.HasValue || OffsetDistance.HasValue ||
                                           !string.IsNullOrWhiteSpace(WidthCallout) || !string.IsNullOrWhiteSpace(WidthCheckStatus);

            public static List<ReferenceCsvRow> Load(string path)
            {
                string[] lines = File.ReadAllLines(path);
                if (lines.Length == 0)
                {
                    return new List<ReferenceCsvRow>();
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

                List<ReferenceCsvRow> rows = new List<ReferenceCsvRow>();
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]))
                    {
                        continue;
                    }

                    List<string> values = SplitCsvLine(lines[i]);
                    ReferenceCsvRow row = new ReferenceCsvRow
                    {
                        Segment = GetInt(map, values, "Segment"),
                        FeatureType = GetString(map, values, "FeatureType"),
                        ReferenceClass = GetString(map, values, "ReferenceClass"),
                        Layer = GetString(map, values, "Layer"),
                        Type = GetString(map, values, "Type"),
                        StartX = GetNullableDouble(map, values, "StartX"),
                        StartY = GetNullableDouble(map, values, "StartY"),
                        EndX = GetNullableDouble(map, values, "EndX"),
                        EndY = GetNullableDouble(map, values, "EndY"),
                        Bearing = GetString(map, values, "Bearing"),
                        Distance = GetNullableDouble(map, values, "Distance"),
                        OriginalDistance = GetNullableDouble(map, values, "OriginalDistance"),
                        AdjustedDistance = GetNullableDouble(map, values, "AdjustedDistance"),
                        LengthAdjustment = GetNullableDouble(map, values, "LengthAdjustment"),
                        AdjustmentMethod = GetString(map, values, "AdjustmentMethod"),
                        CurveDirection = GetString(map, values, "CurveDirection"),
                        CurveBuildMethod = GetString(map, values, "CurveBuildMethod"),
                        Radius = GetNullableDouble(map, values, "Radius"),
                        Delta = GetString(map, values, "Delta"),
                        ArcLength = GetNullableDouble(map, values, "ArcLength"),
                        Tangent = GetNullableDouble(map, values, "Tangent"),
                        ChordBearing = GetString(map, values, "ChordBearing"),
                        ChordLength = GetNullableDouble(map, values, "ChordLength"),
                        RadialBearing = GetString(map, values, "RadialBearing"),
                        RoadName = GetString(map, values, "RoadName"),
                        LineLabel = GetString(map, values, "LineLabel"),
                        ImportRole = GetString(map, values, "ImportRole"),
                        RowWidth = GetNullableDouble(map, values, "ROWWidth"),
                        FullRowWidth = GetNullableDouble(map, values, "FullROWWidth"),
                        HalfRowWidth = GetNullableDouble(map, values, "HalfROWWidth"),
                        WidthCallout = GetString(map, values, "WidthCallout"),
                        MeasuredWidth = GetNullableDouble(map, values, "MeasuredWidth"),
                        WidthError = GetNullableDouble(map, values, "WidthError"),
                        WidthCheckStatus = GetString(map, values, "WidthCheckStatus"),
                        OffsetDistance = GetNullableDouble(map, values, "OffsetDistance"),
                        OffsetDirection = GetString(map, values, "OffsetDirection"),
                        FromPoint = GetString(map, values, "FromPoint"),
                        ToPoint = GetString(map, values, "ToPoint"),
                        MonumentName = GetString(map, values, "MonumentName"),
                        MonumentType = GetString(map, values, "MonumentType"),
                        RelatedBoundarySegment = GetString(map, values, "RelatedBoundarySegment"),
                        RelatedBoundaryPoint = GetString(map, values, "RelatedBoundaryPoint"),
                        RelatedReferenceSegment = GetString(map, values, "RelatedReferenceSegment"),
                        RelationshipType = GetString(map, values, "RelationshipType"),
                        RelationshipStatus = GetString(map, values, "RelationshipStatus"),
                        AnchorMethod = GetString(map, values, "AnchorMethod"),
                        CoordinateAnchor = GetString(map, values, "CoordinateAnchor"),
                        AnchorSourceSegment = GetString(map, values, "AnchorSourceSegment"),
                        AnchorSourcePoint = GetString(map, values, "AnchorSourcePoint"),
                        SourceSheet = GetString(map, values, "SourceSheet"),
                        SourceLabel = GetString(map, values, "SourceLabel"),
                        CorrectedLabel = GetString(map, values, "CorrectedLabel"),
                        GeometryBuildMode = GetString(map, values, "GeometryBuildMode"),
                        Confidence = GetString(map, values, "Confidence"),
                        ImportStatus = GetString(map, values, "ImportStatus"),
                        QAStatus = GetString(map, values, "QAStatus"),
                        Notes = GetString(map, values, "Notes")
                    };

                    if (row.Segment == 0)
                    {
                        row.Segment = rows.Count + 1;
                    }

                    if (string.IsNullOrWhiteSpace(row.Type))
                    {
                        row.Type = "LINE";
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
