using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.GraphicsInterface;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Windows;
using Autodesk.Civil.ApplicationServices;
using CogoPoint = Autodesk.Civil.DatabaseServices.CogoPoint;
using CLV_CivilTools.Shared;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcadEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace CLV_CivilTools.Survey
{
    public static class PointInverseCommands
    {
        private static PaletteSet? palette;
        private static PointInversePalette? control;
        private static Document? owner;
        private static ObjectId fromId = ObjectId.Null, toId = ObjectId.Null;
        private static readonly List<AcadEntity> previews = new();
        private static bool hooked, busy;
        private const string Intro = "Record/reference → surveyed/comparison. Differences = TO minus FROM.\nDrawing coordinates; no ground/grid reduction. REFRESH rereads the points.";

        [CommandMethod("SURVEY-POINT-INVERSE", CommandFlags.Modal)]
        [CommandMethod("POINTINVERSE", CommandFlags.Modal)]
        public static void NewPair() => Run(document =>
        {
            Clear();
            ObjectId first = Pick(document, "\nPick FROM record/reference point label or marker [Number]: ", ObjectId.Null);
            if (first.IsNull) return;
            ObjectId second = Pick(document, "\nPick TO surveyed/comparison point label or marker [Number]: ", first);
            if (second.IsNull) return;
            owner = document; fromId = first; toId = second;
            Render(document);
        });

        [CommandMethod("CLV-POINT-INVERSE-NEXT", CommandFlags.Modal)]
        public static void Next() => Run(document =>
        {
            RequirePair(document);
            ObjectId next = Pick(document, "\nPick next TO comparison point label or marker [Number]: ", fromId);
            if (next.IsNull) return;
            toId = next;
            Render(document);
        });

        [CommandMethod("CLV-POINT-INVERSE-REVERSE", CommandFlags.Modal)]
        public static void Reverse() => Run(document =>
        {
            RequirePair(document);
            (fromId, toId) = (toId, fromId);
            Render(document);
        });

        [CommandMethod("CLV-POINT-INVERSE-REFRESH", CommandFlags.Modal)]
        public static void Refresh() => Run(document => { RequirePair(document); Render(document); });

        [CommandMethod("CLV-POINT-INVERSE-CLEAR", CommandFlags.Modal)]
        public static void ClearCommand() => Run(_ => Clear());

        [CommandMethod("CLV-POINT-INVERSE-COPY", CommandFlags.Modal)]
        public static void Copy() => Run(document =>
        {
            RequirePair(document);
            var rows = Render(document);
            var report = new StringBuilder();
            report.AppendLine("Drawing: " + PointInverseResult.Clean(document.Name));
            foreach (InverseRow row in rows)
                report.AppendLine(row.Merge ? row.Label : row.Label + "\t" + row.From + "\t" + row.To);
            Clipboard.SetText(report.ToString());
            document.Editor.WriteMessage("\nPoint inverse report copied.");
        });

        [CommandMethod("CLV-POINT-INVERSE-TABLE", CommandFlags.Modal)]
        public static void InsertTable() => Run(document =>
        {
            RequirePair(document);
            if (document.Database.TileMode == false)
                throw new InvalidOperationException("Insert the point inverse table from the Model tab.");
            PromptPointResult location = document.Editor.GetPoint("\nPick point inverse snapshot table location: ");
            if (location.Status != PromptStatus.OK) return;
            using ViewTableRecord view = document.Editor.GetCurrentView();
            var heightOptions = new PromptDoubleOptions("\nTable text height in drawing units")
            {
                DefaultValue = Math.Max(view.Height * 0.008, 0.001),
                AllowNegative = false, AllowZero = false, UseDefaultValue = true
            };
            PromptDoubleResult heightResult = document.Editor.GetDouble(heightOptions);
            if (heightResult.Status != PromptStatus.OK) return;
            double height = heightResult.Value;
            if (!double.IsFinite(height) || height <= 0) throw new InvalidOperationException("Text height must be positive and finite.");
            // Re-read after prompts: never export old cached coordinates.
            var rows = Render(document);
            using Transaction tr = document.Database.TransactionManager.StartTransaction();
            var blocks = (BlockTable)tr.GetObject(document.Database.BlockTableId, OpenMode.ForRead);
            var model = (BlockTableRecord)tr.GetObject(blocks[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            using var table = new Table
            {
                TableStyle = document.Database.Tablestyle,
                Position = location.Value.TransformBy(document.Editor.CurrentUserCoordinateSystem),
                Layer = "0"
            };
            table.SetSize(rows.Count, 3);
            table.SetRowHeight(height * 1.9);
            table.Columns[0].Width = height * 32;
            table.Columns[1].Width = height * 25;
            table.Columns[2].Width = height * 25;
            for (int row = 0; row < rows.Count; row++)
            {
                string[] values = { rows[row].Label, rows[row].From, rows[row].To };
                if (rows[row].Merge)
                    table.MergeCells(CellRange.Create(table, row, 0, row, 2));
                else if (row >= 7)
                    table.MergeCells(CellRange.Create(table, row, 1, row, 2));
                for (int column = 0; column < 3; column++)
                {
                    // Only write the anchor of a merged range.
                    if (rows[row].Merge && column > 0 || !rows[row].Merge && row >= 7 && column == 2) continue;
                    table.Cells[row, column].TextHeight = height;
                    table.Cells[row, column].Alignment = CellAlignment.MiddleLeft;
                    table.Cells[row, column].TextString = Literal(values[column]);
                }
            }
            table.GenerateLayout();
            model.AppendEntity(table);
            tr.AddNewlyCreatedDBObject(table, true);
            tr.Commit();
            document.Editor.WriteMessage("\nPoint inverse snapshot table inserted on layer 0. It is not linked to the points.");
        });

        private static string Literal(string value) => value.Replace("\\", "\\\\").Replace("{", "\\{")
            .Replace("}", "\\}").Replace("%", "\\U+0025");

        internal static void Queue(string command)
        {
            if (busy) return;
            Document? document = AcadApp.DocumentManager.MdiActiveDocument;
            if (document == null) return;
            document.SendStringToExecute(command + " ", true, false, false);
        }

        private static void Run(Action<Document> action)
        {
            Document? document = AcadApp.DocumentManager.MdiActiveDocument;
            if (document == null || busy) return;
            EnsurePalette();
            busy = true;
            control!.Busy(true);
            try { action(document); }
            catch (System.Exception error)
            {
                Clear();
                control.Display(null, "Point inverse stopped: " + error.Message + "\nPick a new pair.");
                document.Editor.WriteMessage("\nPOINT INVERSE: " + error.Message);
            }
            finally
            {
                busy = false;
                control.Busy(false);
            }
        }

        private static void EnsurePalette()
        {
            if (palette == null)
            {
                control = new PointInversePalette();
                palette = new PaletteSet("POINT INVERSE", new Guid("c6573a21-3068-49ce-8bba-b8324e3f1948"))
                {
                    DockEnabled = DockSides.Left | DockSides.Right,
                    Style = PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowAutoHideButton
                };
                palette.Add("POINT INVERSE", control);
            }
            if (!hooked)
            {
                AcadApp.DocumentManager.DocumentActivated += (_, args) =>
                {
                    if (!ReferenceEquals(owner, args.Document)) Clear();
                };
                AcadApp.DocumentManager.DocumentToBeDestroyed += (_, args) =>
                {
                    if (ReferenceEquals(owner, args.Document)) Clear();
                };
                AcadApp.Idle += (_, _) =>
                {
                    if (!busy && palette != null && !palette.Visible && owner != null) Clear();
                };
                hooked = true;
            }
            PalettePositionHelper.ShowNearAutoCadWindow(palette, new Size(640, 720), new Size(500, 460));
        }

        private static ObjectId Pick(Document document, string message, ObjectId exclude)
        {
            while (true)
            {
                var options = new PromptEntityOptions(message);
                options.SetRejectMessage("\nPick an actual COGO point label or marker. Exploded text and xref contents are not supported.");
                options.AddAllowedClass(typeof(CogoPoint), false);
                options.Keywords.Add("Number");
                PromptEntityResult pick = document.Editor.GetEntity(options);
                ObjectId id;
                if (pick.Status == PromptStatus.Keyword)
                {
                    var input = new PromptStringOptions("\nEnter COGO point number: ") { AllowSpaces = false };
                    PromptResult numberResult = document.Editor.GetString(input);
                    if (numberResult.Status != PromptStatus.OK) return ObjectId.Null;
                    if (!uint.TryParse(numberResult.StringResult, NumberStyles.None, CultureInfo.InvariantCulture, out uint number) || number == 0)
                    {
                        document.Editor.WriteMessage("\nEnter a positive point number.");
                        continue;
                    }
                    // Exact identity lookup, never nearest-position matching.
                    try { id = CivilDocument.GetCivilDocument(document.Database).CogoPoints.GetPointByPointNumber(number); }
                    catch (ArgumentException)
                    {
                        document.Editor.WriteMessage("\nThat point number is not present in this drawing.");
                        continue;
                    }
                    if (id.IsNull)
                    {
                        document.Editor.WriteMessage("\nThat point number is not present in this drawing.");
                        continue;
                    }
                }
                else if (pick.Status == PromptStatus.OK) id = pick.ObjectId;
                else return ObjectId.Null;
                if (id == exclude)
                {
                    document.Editor.WriteMessage("\nThat is the FROM point again. Pick a different point, or use Number for overlapping shots.");
                    continue;
                }
                using Transaction tr = document.Database.TransactionManager.StartOpenCloseTransaction();
                InversePoint point = ReadPoint(tr, id);
                document.Editor.WriteMessage("\nSelected point {0}: {1}; N={2}, E={3}, Z={4}",
                    point.Number, PointInverseResult.Clean(point.Description), PointInverseResult.Number(point.Northing),
                    PointInverseResult.Number(point.Easting), PointInverseResult.Number(point.Elevation));
                return id;
            }
        }

        private static InversePoint ReadPoint(Transaction tr, ObjectId id)
        {
            if (id.IsNull || !id.IsValid || id.IsErased || tr.GetObject(id, OpenMode.ForRead) is not CogoPoint point)
                throw new InvalidOperationException("A selected COGO point is no longer available. Pick a new pair.");
            double z = point.Elevation;
            if (double.IsInfinity(z)) throw new InvalidOperationException("Point elevation is invalid.");
            return new InversePoint(point.PointNumber, point.RawDescription ?? "", point.Northing, point.Easting,
                double.IsNaN(z) ? null : z);
        }

        private static void RequirePair(Document document)
        {
            if (!ReferenceEquals(owner, document) || fromId.IsNull || toId.IsNull)
                throw new InvalidOperationException("Pick a FROM and TO point in the active drawing first.");
        }

        private static IReadOnlyList<InverseRow> Render(Document document)
        {
            RequirePair(document);
            using Transaction tr = document.Database.TransactionManager.StartOpenCloseTransaction();
            InversePoint from = ReadPoint(tr, fromId), to = ReadPoint(tr, toId);
            var result = new PointInverseResult(from, to);
            var zone = CivilDocument.GetCivilDocument(document.Database).Settings.DrawingSettings.UnitZoneSettings;
            string units = "Linear values: Civil drawing " + zone.DrawingUnits +
                (zone.DrawingUnits.ToString() == "Feet" ? " (" + zone.ImperialToMetricConversion + ")" : "") + "; no unit conversion.";
            var rows = result.Rows(units, DateTimeOffset.Now);
            control!.Display(rows, Intro + "\nFROM " + from.Number + " → TO " + to.Number +
                ". Positive differences mean north / east / higher." +
                (!from.Elevation.HasValue || !to.Elevation.HasValue ? "\nElevation unset: 3D / elevation difference / grade N/A; preview is XY at Z=0." : ""));
            DrawPreview(document, from, to);
            return rows;
        }

        private static void DrawPreview(Document document, InversePoint from, InversePoint to)
        {
            ErasePreview();
            try
            {
                using ViewTableRecord view = document.Editor.GetCurrentView();
                double radius = Math.Max(view.Height * 0.004, 0.001);
                bool planar = !from.Elevation.HasValue || !to.Elevation.HasValue;
                Point3d a = new(from.Easting, from.Northing, planar ? 0 : from.Elevation!.Value);
                Point3d b = new(to.Easting, to.Northing, planar ? 0 : to.Elevation!.Value);
                if (a != b) AddPreview(new Line(a, b) { ColorIndex = 2 });
                AddPreview(new Circle(a, Vector3d.ZAxis, radius) { ColorIndex = 3 });
                AddPreview(new Circle(b, Vector3d.ZAxis, radius * 1.3) { ColorIndex = 1 });
            }
            catch (System.Exception error)
            {
                ErasePreview();
                document.Editor.WriteMessage("\nPoint inverse graphics preview unavailable: " + error.Message);
            }
        }

        private static void AddPreview(AcadEntity entity)
        {
            previews.Add(entity);
            TransientManager.CurrentTransientManager.AddTransient(entity, TransientDrawingMode.DirectShortTerm, 128, new IntegerCollection());
        }

        private static void ErasePreview()
        {
            foreach (AcadEntity entity in previews)
            {
                try { TransientManager.CurrentTransientManager.EraseTransient(entity, new IntegerCollection()); }
                catch (System.Exception) { /* The host may already have discarded a closed document's transient. */ }
                entity.Dispose();
            }
            previews.Clear();
        }

        private static void Clear()
        {
            ErasePreview();
            owner = null;
            fromId = toId = ObjectId.Null;
            control?.Display(null, Intro + "\nPick a new point pair.");
        }
    }
}
