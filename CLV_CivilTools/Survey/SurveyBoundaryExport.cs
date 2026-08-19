using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

namespace CLV_CivilTools.Survey
{
    /// <summary>
    /// Exports CAD-resolved boundary linework to a CSV that can be used as the locked Pass-1B boundary handoff.
    /// Supports LINE, ARC, LWPOLYLINE, 2D POLYLINE, and 3D POLYLINE selections.
    /// </summary>
    public sealed class SurveyBoundaryExportCommands
    {
        private const double DefaultTolerance = 0.01;

        [CommandMethod("CLV_BOUNDARY_EXPORT")]
        [CommandMethod("CLV-BOUNDARY-EXPORT")]
        [CommandMethod("SURVEY-BOUNDARY-EXPORT")]
        public void ExportBoundary()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptSelectionOptions selectionOptions = new PromptSelectionOptions
            {
                MessageForAdding = "\nSelect accepted boundary LINE/ARC/POLYLINE objects: ",
                AllowDuplicates = false,
                SingleOnly = false,
                SinglePickInSpace = false
            };

            TypedValue[] filterValues =
            {
                new TypedValue((int)DxfCode.Start, "LINE,ARC,LWPOLYLINE,POLYLINE")
            };
            SelectionFilter filter = new SelectionFilter(filterValues);

            PromptSelectionResult selectionResult = ed.GetSelection(selectionOptions, filter);
            if (selectionResult.Status != PromptStatus.OK || selectionResult.Value.Count == 0)
            {
                ed.WriteMessage("\nBoundary export cancelled. No LINE/ARC/POLYLINE objects were selected.");
                return;
            }

            double tolerance = DefaultTolerance;
            PromptDoubleOptions toleranceOptions = new PromptDoubleOptions($"\nEndpoint chain tolerance <{DefaultTolerance:0.###}>: ")
            {
                AllowNegative = false,
                AllowZero = false,
                DefaultValue = DefaultTolerance,
                UseDefaultValue = true
            };
            PromptDoubleResult toleranceResult = ed.GetDouble(toleranceOptions);
            if (toleranceResult.Status == PromptStatus.OK)
                tolerance = toleranceResult.Value;
            else if (toleranceResult.Status != PromptStatus.None)
            {
                ed.WriteMessage("\nBoundary export cancelled.");
                return;
            }

            Point3d? requestedStart = null;
            PromptPointOptions startPointOptions = new PromptPointOptions("\nPick desired boundary start point or press Enter to auto-start: ")
            {
                AllowNone = true
            };
            PromptPointResult startPointResult = ed.GetPoint(startPointOptions);
            if (startPointResult.Status == PromptStatus.OK)
                requestedStart = startPointResult.Value;
            else if (startPointResult.Status != PromptStatus.None)
            {
                ed.WriteMessage("\nBoundary export cancelled.");
                return;
            }

            List<BoundarySegment> rawSegments = new List<BoundarySegment>();
            List<string> readWarnings = new List<string>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject selected in selectionResult.Value)
                {
                    if (selected == null)
                        continue;

                    DBObject obj = tr.GetObject(selected.ObjectId, OpenMode.ForRead, false);
                    if (obj is Line line)
                    {
                        rawSegments.Add(BoundarySegment.FromLine(line));
                    }
                    else if (obj is Arc arc)
                    {
                        rawSegments.Add(BoundarySegment.FromArc(arc));
                    }
                    else if (obj is Polyline lwPolyline)
                    {
                        rawSegments.AddRange(ReadLightweightPolyline(lwPolyline, selected.ObjectId.Handle.ToString(), readWarnings));
                    }
                    else if (obj is Polyline2d polyline2d)
                    {
                        rawSegments.AddRange(ReadPolyline2d(polyline2d, tr, selected.ObjectId.Handle.ToString(), readWarnings));
                    }
                    else if (obj is Polyline3d polyline3d)
                    {
                        rawSegments.AddRange(ReadPolyline3d(polyline3d, tr, selected.ObjectId.Handle.ToString(), readWarnings));
                    }
                }

                tr.Commit();
            }

            if (rawSegments.Count == 0)
            {
                ed.WriteMessage("\nBoundary export failed. The selected objects did not contain any supported boundary segments.");
                return;
            }

            ChainResult chain = ChainSegments(rawSegments, tolerance, requestedStart);
            string? csvPath = PromptForCsvPath(doc, "boundary_resolved_export.csv");
            if (string.IsNullOrWhiteSpace(csvPath))
            {
                ed.WriteMessage("\nBoundary export cancelled.");
                return;
            }

            try
            {
                WriteCsv(csvPath, chain.Segments);
                string reportPath = Path.ChangeExtension(csvPath, ".export_report.txt");
                WriteReport(reportPath, csvPath, rawSegments.Count, chain, readWarnings, tolerance, requestedStart);

                ed.WriteMessage($"\nBoundary export complete: {csvPath}");
                ed.WriteMessage($"\nBoundary export report: {reportPath}");
                ed.WriteMessage($"\nSegments exported: {chain.Segments.Count}");
                if (chain.Warnings.Count > 0 || readWarnings.Count > 0)
                {
                    ed.WriteMessage("\nBoundary export completed with warnings. Review the export report before using this as Pass-1B control.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nBoundary export failed: {ex.Message}");
            }
        }

        private static string? PromptForCsvPath(Document doc, string defaultName)
        {
            using SaveFileDialog dialog = new SaveFileDialog
            {
                Title = "Export resolved boundary CSV",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = defaultName,
                AddExtension = true,
                DefaultExt = "csv",
                OverwritePrompt = true
            };

            try
            {
                string dwgName = doc.Database.Filename;
                if (!string.IsNullOrWhiteSpace(dwgName))
                {
                    string? folder = Path.GetDirectoryName(dwgName);
                    if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                        dialog.InitialDirectory = folder;
                }
            }
            catch
            {
                // Ignore folder discovery issues; SaveFileDialog will use its default location.
            }

            DialogResult result = dialog.ShowDialog();
            return result == DialogResult.OK ? dialog.FileName : null;
        }

        private static IEnumerable<BoundarySegment> ReadLightweightPolyline(Polyline polyline, string handle, List<string> warnings)
        {
            int count = polyline.NumberOfVertices;
            if (count < 2)
                yield break;

            int segmentCount = polyline.Closed ? count : count - 1;
            for (int i = 0; i < segmentCount; i++)
            {
                int next = (i + 1) % count;
                Point3d start = polyline.GetPoint3dAt(i);
                Point3d end = polyline.GetPoint3dAt(next);
                double bulge = polyline.GetBulgeAt(i);

                if (start.DistanceTo(end) < Tolerance.Global.EqualPoint)
                {
                    warnings.Add($"Polyline {handle} segment {i + 1}: skipped zero-length segment.");
                    continue;
                }

                if (Math.Abs(bulge) < 1.0e-12)
                    yield return BoundarySegment.FromEndpoints(start, end, "POLYLINE", handle);
                else if (TryCreateArcFromBulge(start, end, bulge, handle, out BoundarySegment? curve))
                    yield return curve!;
                else
                    warnings.Add($"Polyline {handle} segment {i + 1}: failed to convert bulge to arc.");
            }
        }

        private static IEnumerable<BoundarySegment> ReadPolyline2d(Polyline2d polyline, Transaction tr, string handle, List<string> warnings)
        {
            List<Point3d> points = new List<Point3d>();
            foreach (ObjectId vertexId in polyline)
            {
                if (tr.GetObject(vertexId, OpenMode.ForRead, false) is Vertex2d vertex)
                    points.Add(new Point3d(vertex.Position.X, vertex.Position.Y, vertex.Position.Z));
            }

            int count = points.Count;
            if (count < 2)
                yield break;

            int segmentCount = polyline.Closed ? count : count - 1;
            for (int i = 0; i < segmentCount; i++)
            {
                int next = (i + 1) % count;
                Point3d start = points[i];
                Point3d end = points[next];
                if (start.DistanceTo(end) < Tolerance.Global.EqualPoint)
                {
                    warnings.Add($"2D polyline {handle} segment {i + 1}: skipped zero-length segment.");
                    continue;
                }

                // 2D Polyline arc bulges are not consistently represented in legacy drawings.
                // Export as chord/line if the curve cannot be safely read as a LWPOLYLINE.
                yield return BoundarySegment.FromEndpoints(start, end, "POLYLINE2D", handle);
            }
        }

        private static IEnumerable<BoundarySegment> ReadPolyline3d(Polyline3d polyline, Transaction tr, string handle, List<string> warnings)
        {
            List<Point3d> points = new List<Point3d>();
            foreach (ObjectId vertexId in polyline)
            {
                if (tr.GetObject(vertexId, OpenMode.ForRead, false) is PolylineVertex3d vertex)
                    points.Add(vertex.Position);
            }

            int count = points.Count;
            if (count < 2)
                yield break;

            int segmentCount = polyline.Closed ? count : count - 1;
            for (int i = 0; i < segmentCount; i++)
            {
                int next = (i + 1) % count;
                Point3d start = points[i];
                Point3d end = points[next];
                if (start.DistanceTo(end) < Tolerance.Global.EqualPoint)
                {
                    warnings.Add($"3D polyline {handle} segment {i + 1}: skipped zero-length segment.");
                    continue;
                }

                yield return BoundarySegment.FromEndpoints(start, end, "POLYLINE3D", handle);
            }
        }

        private static bool TryCreateArcFromBulge(Point3d start, Point3d end, double bulge, string handle, out BoundarySegment? segment)
        {
            segment = null;
            double chord = start.DistanceTo(end);
            if (chord <= 0.0 || Math.Abs(bulge) < 1.0e-12)
                return false;

            double theta = 4.0 * Math.Atan(Math.Abs(bulge));
            double radius = chord * (1.0 + bulge * bulge) / (4.0 * Math.Abs(bulge));
            double sagitta = bulge * chord / 2.0;
            double distanceFromMidToCenter = ((chord * chord) / (8.0 * Math.Abs(sagitta))) - (Math.Abs(sagitta) / 2.0);

            Vector3d chordVector = end - start;
            Vector3d leftNormal = new Vector3d(-chordVector.Y, chordVector.X, 0.0).GetNormal();
            Point3d midpoint = new Point3d((start.X + end.X) / 2.0, (start.Y + end.Y) / 2.0, (start.Z + end.Z) / 2.0);
            Point3d center = bulge > 0.0
                ? midpoint + leftNormal * distanceFromMidToCenter
                : midpoint - leftNormal * distanceFromMidToCenter;

            segment = BoundarySegment.FromCurve(start, end, center, radius, theta, bulge > 0.0 ? "CCW" : "CW", "LWPOLYLINE_ARC", handle);
            return true;
        }

        private static ChainResult ChainSegments(List<BoundarySegment> rawSegments, double tolerance, Point3d? requestedStart)
        {
            List<string> warnings = new List<string>();
            List<BoundarySegment> remaining = rawSegments.Select(s => s.Clone()).ToList();
            List<BoundarySegment> chained = new List<BoundarySegment>();

            if (remaining.Count == 1)
                return new ChainResult(remaining, warnings, 0.0, true);

            Point3d startPoint = requestedStart ?? ChooseAutoStart(remaining);
            int startIndex = FindClosestEndpointSegment(remaining, startPoint, out bool reverseStart, out double startDistance);
            if (startIndex < 0)
                return new ChainResult(remaining, new List<string> { "Could not choose a start segment." }, 0.0, false);

            BoundarySegment current = remaining[startIndex];
            remaining.RemoveAt(startIndex);
            if (reverseStart)
                current = current.Reversed();
            if (requestedStart.HasValue && startDistance > tolerance)
                warnings.Add($"Picked start point is {startDistance:0.####}' from the nearest selected endpoint.");
            chained.Add(current);

            double maxGap = 0.0;
            Point3d chainEnd = current.End;
            while (remaining.Count > 0)
            {
                int bestIndex = -1;
                bool bestReverse = false;
                double bestGap = double.MaxValue;

                for (int i = 0; i < remaining.Count; i++)
                {
                    double gapToStart = chainEnd.DistanceTo(remaining[i].Start);
                    if (gapToStart < bestGap)
                    {
                        bestGap = gapToStart;
                        bestIndex = i;
                        bestReverse = false;
                    }

                    double gapToEnd = chainEnd.DistanceTo(remaining[i].End);
                    if (gapToEnd < bestGap)
                    {
                        bestGap = gapToEnd;
                        bestIndex = i;
                        bestReverse = true;
                    }
                }

                if (bestIndex < 0)
                    break;

                BoundarySegment nextSegment = remaining[bestIndex];
                remaining.RemoveAt(bestIndex);
                if (bestReverse)
                    nextSegment = nextSegment.Reversed();

                nextSegment.ChainGapFromPrevious = bestGap;
                if (bestGap > tolerance)
                {
                    warnings.Add($"Gap of {bestGap:0.####}' before exported segment {chained.Count + 1}. Segment kept, but review chain order/connectivity.");
                }

                maxGap = Math.Max(maxGap, bestGap);
                chained.Add(nextSegment);
                chainEnd = nextSegment.End;
            }

            double closureGap = chained.Count > 1 ? chained[^1].End.DistanceTo(chained[0].Start) : 0.0;
            if (closureGap > tolerance)
                warnings.Add($"Closure gap is {closureGap:0.####}', which exceeds tolerance {tolerance:0.####}'.");

            bool continuous = maxGap <= tolerance && closureGap <= tolerance;
            return new ChainResult(chained, warnings, closureGap, continuous);
        }

        private static Point3d ChooseAutoStart(IEnumerable<BoundarySegment> segments)
        {
            // Stable default: lower-left endpoint. User can reverse/choose a start point when a specific POB is needed.
            return segments
                .SelectMany(s => new[] { s.Start, s.End })
                .OrderBy(p => p.Y)
                .ThenBy(p => p.X)
                .First();
        }

        private static int FindClosestEndpointSegment(List<BoundarySegment> segments, Point3d point, out bool reverse, out double distance)
        {
            reverse = false;
            distance = double.MaxValue;
            int index = -1;
            for (int i = 0; i < segments.Count; i++)
            {
                double startDistance = point.DistanceTo(segments[i].Start);
                if (startDistance < distance)
                {
                    distance = startDistance;
                    reverse = false;
                    index = i;
                }

                double endDistance = point.DistanceTo(segments[i].End);
                if (endDistance < distance)
                {
                    distance = endDistance;
                    reverse = true;
                    index = i;
                }
            }

            return index;
        }

        private static void WriteCsv(string path, IReadOnlyList<BoundarySegment> segments)
        {
            using StreamWriter writer = new StreamWriter(path, false, new UTF8Encoding(true));
            writer.WriteLine("FeatureID,FeatureType,Layer,Segment,Type,StartX,StartY,EndX,EndY,CenterX,CenterY,Bearing,Distance,CurveDirection,Radius,Delta,ArcLength,ChordBearing,ChordLength,SourceLabel,CorrectedLabel,Confidence,ImportStatus,Notes");

            for (int i = 0; i < segments.Count; i++)
            {
                BoundarySegment s = segments[i];
                string id = $"BNDY-{i + 1:000}";
                string segment = $"BNDY-{i + 1:000}";
                string layer = "V-SURV-MAP-REVIEW-BNDY";
                string notes = s.BuildNotes(i == 0);

                string[] values =
                {
                    id,
                    "BOUNDARY",
                    layer,
                    segment,
                    s.Type,
                    FormatDouble(s.Start.X),
                    FormatDouble(s.Start.Y),
                    FormatDouble(s.End.X),
                    FormatDouble(s.End.Y),
                    s.IsCurve ? FormatDouble(s.Center.X) : string.Empty,
                    s.IsCurve ? FormatDouble(s.Center.Y) : string.Empty,
                    s.Type == "LINE" ? ToBearing(s.Start, s.End) : string.Empty,
                    s.Type == "LINE" ? FormatDouble(s.Start.DistanceTo(s.End)) : string.Empty,
                    s.IsCurve ? s.CurveDirection : string.Empty,
                    s.IsCurve ? FormatDouble(s.Radius) : string.Empty,
                    s.IsCurve ? ToDms(s.DeltaRadians * 180.0 / Math.PI) : string.Empty,
                    s.IsCurve ? FormatDouble(s.Radius * s.DeltaRadians) : string.Empty,
                    s.IsCurve ? ToBearing(s.Start, s.End) : string.Empty,
                    s.IsCurve ? FormatDouble(s.Start.DistanceTo(s.End)) : string.Empty,
                    s.SourceLabel,
                    string.Empty,
                    "CAD_RESOLVED",
                    "IMPORT",
                    notes
                };

                writer.WriteLine(string.Join(",", values.Select(Csv)));
            }
        }

        private static void WriteReport(string reportPath, string csvPath, int rawCount, ChainResult chain, List<string> readWarnings, double tolerance, Point3d? requestedStart)
        {
            using StreamWriter writer = new StreamWriter(reportPath, false, new UTF8Encoding(true));
            writer.WriteLine("CLV Boundary Export Report");
            writer.WriteLine($"CSV: {csvPath}");
            writer.WriteLine($"Exported: {DateTime.Now:G}");
            writer.WriteLine($"Raw segments read: {rawCount}");
            writer.WriteLine($"Segments exported: {chain.Segments.Count}");
            writer.WriteLine($"Endpoint tolerance: {tolerance:0.####}'");
            writer.WriteLine($"Start point mode: {(requestedStart.HasValue ? "picked" : "auto lower-left endpoint")}");
            writer.WriteLine($"Closure gap: {chain.ClosureGap:0.####}'");
            writer.WriteLine($"Continuous within tolerance: {(chain.Continuous ? "YES" : "NO")}");
            writer.WriteLine();
            writer.WriteLine("Warnings:");
            if (readWarnings.Count == 0 && chain.Warnings.Count == 0)
            {
                writer.WriteLine("- None");
            }
            else
            {
                foreach (string warning in readWarnings)
                    writer.WriteLine("- " + warning);
                foreach (string warning in chain.Warnings)
                    writer.WriteLine("- " + warning);
            }
            writer.WriteLine();
            writer.WriteLine("Use this CSV as Pass-1B accepted boundary control only after the CAD linework has been manually verified.");
        }

        private static string FormatDouble(double value) => value.ToString("0.0000", CultureInfo.InvariantCulture);

        private static string Csv(string value)
        {
            value ??= string.Empty;
            return value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r')
                ? '"' + value.Replace("\"", "\"\"") + '"'
                : value;
        }

        private static string ToBearing(Point3d start, Point3d end)
        {
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            if (Math.Abs(dx) < 1.0e-12 && Math.Abs(dy) < 1.0e-12)
                return string.Empty;

            string ns = dy >= 0.0 ? "N" : "S";
            string ew = dx >= 0.0 ? "E" : "W";
            double angle = Math.Atan2(Math.Abs(dx), Math.Abs(dy)) * 180.0 / Math.PI;
            return ns + ToDms(angle) + ew;
        }

        private static string ToDms(double degrees)
        {
            degrees = Math.Abs(degrees);
            int d = (int)Math.Floor(degrees);
            double minutesFull = (degrees - d) * 60.0;
            int m = (int)Math.Floor(minutesFull);
            double secondsFull = (minutesFull - m) * 60.0;
            int s = (int)Math.Round(secondsFull, MidpointRounding.AwayFromZero);

            if (s >= 60)
            {
                s -= 60;
                m += 1;
            }
            if (m >= 60)
            {
                m -= 60;
                d += 1;
            }

            return string.Format(CultureInfo.InvariantCulture, "{0:00}°{1:00}'{2:00}\"", d, m, s);
        }

        private sealed class ChainResult
        {
            public ChainResult(List<BoundarySegment> segments, List<string> warnings, double closureGap, bool continuous)
            {
                Segments = segments;
                Warnings = warnings;
                ClosureGap = closureGap;
                Continuous = continuous;
            }

            public List<BoundarySegment> Segments { get; }
            public List<string> Warnings { get; }
            public double ClosureGap { get; }
            public bool Continuous { get; }
        }

        private sealed class BoundarySegment
        {
            private BoundarySegment(Point3d start, Point3d end, string type, string sourceType, string sourceLabel)
            {
                Start = start;
                End = end;
                Type = type;
                SourceType = sourceType;
                SourceLabel = sourceLabel;
            }

            public Point3d Start { get; private set; }
            public Point3d End { get; private set; }
            public Point3d Center { get; private set; }
            public string Type { get; }
            public string SourceType { get; }
            public string SourceLabel { get; }
            public bool IsCurve => Type == "CURVE";
            public string CurveDirection { get; private set; } = string.Empty;
            public double Radius { get; private set; }
            public double DeltaRadians { get; private set; }
            public double ChainGapFromPrevious { get; set; }

            public static BoundarySegment FromLine(Line line)
            {
                return FromEndpoints(line.StartPoint, line.EndPoint, "LINE", line.Handle.ToString());
            }

            public static BoundarySegment FromEndpoints(Point3d start, Point3d end, string sourceType, string handle)
            {
                return new BoundarySegment(Project(start), Project(end), "LINE", sourceType, handle);
            }

            public static BoundarySegment FromArc(Arc arc)
            {
                double delta = NormalizePositive(arc.EndAngle - arc.StartAngle);
                return FromCurve(arc.StartPoint, arc.EndPoint, arc.Center, arc.Radius, delta, "CCW", "ARC", arc.Handle.ToString());
            }

            public static BoundarySegment FromCurve(Point3d start, Point3d end, Point3d center, double radius, double deltaRadians, string direction, string sourceType, string handle)
            {
                BoundarySegment segment = new BoundarySegment(Project(start), Project(end), "CURVE", sourceType, handle)
                {
                    Center = Project(center),
                    Radius = radius,
                    DeltaRadians = deltaRadians,
                    CurveDirection = direction
                };
                return segment;
            }

            public BoundarySegment Clone()
            {
                return new BoundarySegment(Start, End, Type, SourceType, SourceLabel)
                {
                    Center = Center,
                    CurveDirection = CurveDirection,
                    Radius = Radius,
                    DeltaRadians = DeltaRadians,
                    ChainGapFromPrevious = ChainGapFromPrevious
                };
            }

            public BoundarySegment Reversed()
            {
                BoundarySegment reversed = new BoundarySegment(End, Start, Type, SourceType, SourceLabel)
                {
                    Center = Center,
                    Radius = Radius,
                    DeltaRadians = DeltaRadians,
                    CurveDirection = CurveDirection == "CCW" ? "CW" : CurveDirection == "CW" ? "CCW" : CurveDirection,
                    ChainGapFromPrevious = ChainGapFromPrevious
                };
                return reversed;
            }

            public string BuildNotes(bool first)
            {
                List<string> notes = new List<string>
                {
                    $"CAD resolved export from {SourceType} handle {SourceLabel}"
                };

                if (first)
                    notes.Add("Boundary export start segment");
                if (ChainGapFromPrevious > 0.00005)
                    notes.Add($"ChainGapFromPrevious={ChainGapFromPrevious:0.####}");

                return string.Join("; ", notes);
            }

            private static Point3d Project(Point3d point) => new Point3d(point.X, point.Y, 0.0);

            private static double NormalizePositive(double radians)
            {
                double twoPi = Math.PI * 2.0;
                while (radians < 0.0)
                    radians += twoPi;
                while (radians >= twoPi)
                    radians -= twoPi;
                return radians;
            }
        }
    }
}
