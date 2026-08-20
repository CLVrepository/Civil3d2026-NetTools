using System;
using System.Drawing;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;


using CLV_CivilTools.Shared;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Ufls
{
    /// <summary>
    /// Reports Civil 3D pipe information at the station nearest a user-picked location.
    /// Workflow: pick a location, select a Civil 3D pipe, then review interpolated pipe data.
    /// </summary>
    public static class UflsPipeInfoCommands
    {
        [CommandMethod("UFLS-PIPE-INFO")]
        public static void ShowPipeInfo()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                PromptPointResult ppr = ed.GetPoint("\nSelect location perpendicular to pipe: ");
                if (ppr.Status != PromptStatus.OK)
                    return;

                PromptEntityOptions peo = new PromptEntityOptions("\nSelect Civil 3D pipe: ");
                peo.AllowNone = false;
                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                    return;

                PipeInfoResult info;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    DBObject dbo = tr.GetObject(per.ObjectId, OpenMode.ForRead, false);
                    if (!IsCivilPipe(dbo))
                    {
                        ed.WriteMessage("\nSelected object is not a Civil 3D pipe.");
                        return;
                    }

                    info = BuildPipeInfo(dbo, ppr.Value);
                    tr.Commit();
                }

                using (PipeInfoForm form = new PipeInfoForm(info))
                {
                    AcadApp.ShowModalDialog(form);
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUFLS-PIPE-INFO error: {ex.Message}");
            }
        }



        [CommandMethod("UFLS-PIPE-TOP-CHECK")]
        public static void RunPipeTopCheck()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                PipeTopCheckPoint? checkPoint = PromptForCogoCheckPoint(ed, db);
                if (checkPoint == null)
                    return;

                PromptEntityOptions peo = new PromptEntityOptions("\nSelect Civil 3D pipe: ");
                peo.AllowNone = false;
                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                    return;

                PipeInfoResult info;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    DBObject dbo = tr.GetObject(per.ObjectId, OpenMode.ForRead, false);
                    if (!IsCivilPipe(dbo))
                    {
                        ed.WriteMessage("\nSelected object is not a Civil 3D pipe.");
                        return;
                    }

                    info = BuildPipeInfo(dbo, checkPoint.Value.Location);
                    tr.Commit();
                }

                if (double.IsNaN(info.TopOfPipeElevation))
                {
                    ed.WriteMessage("\nPipe top elevation could not be calculated for the selected pipe.");
                    return;
                }

                ObjectId textStyleId = GetRequiredTextStyleId(db, PipeTopCheckTextStyleName);
                if (textStyleId.IsNull)
                {
                    ed.WriteMessage("\nCLV-Non Anno text style is not present in this drawing. Please load it from the CLV template.");
                    return;
                }

                double planTopElevation = info.TopOfPipeElevation;
                double surveyTopElevation = checkPoint.Value.Elevation;
                double difference = surveyTopElevation - planTopElevation;

                PromptPointResult labelPoint = ed.GetPoint("\nSpecify label location: ");
                if (labelPoint.Status != PromptStatus.OK)
                    return;

                ObjectId originalLayerId = db.Clayer;
                try
                {
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        LayerStandards.TryEnsureManagedLayer(db, tr, ed, LayerStandards.UflsPipeTopCheckLayerName);
                        LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                        if (!lt.Has(LayerStandards.UflsPipeTopCheckLayerName))
                            throw new InvalidOperationException($"Layer {LayerStandards.UflsPipeTopCheckLayerName} could not be created.");

                        db.Clayer = lt[LayerStandards.UflsPipeTopCheckLayerName];

                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                        MText label = new MText
                        {
                            Location = labelPoint.Value,
                            Attachment = AttachmentPoint.TopLeft,
                            TextHeight = PipeTopCheckTextHeight,
                            TextStyleId = textStyleId,
                            Layer = LayerStandards.UflsPipeTopCheckLayerName,
                            Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByLayer, 256),
                            Contents = BuildPipeTopCheckLabel(planTopElevation, surveyTopElevation, difference)
                        };

                        ms.AppendEntity(label);
                        tr.AddNewlyCreatedDBObject(label, true);
                        tr.Commit();
                    }
                }
                finally
                {
                    RestoreCurrentLayer(db, originalLayerId, ed);
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUFLS-PIPE-TOP-CHECK error: {ex.Message}");
            }
        }

        private const string PipeTopCheckTextStyleName = "CLV-Non Anno";
        private const double PipeTopCheckTextHeight = 0.1;

        private static PipeTopCheckPoint? PromptForCogoCheckPoint(Editor ed, Database db)
        {
            while (true)
            {
                PromptEntityOptions peo = new PromptEntityOptions("\nSelect AEC point for pipe top check: ");
                peo.AllowNone = false;
                peo.SetRejectMessage("\nSelected object is not a Civil 3D COGO point.");
                peo.AddAllowedClass(typeof(Autodesk.Civil.DatabaseServices.CogoPoint), exactMatch: false);

                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                    return null;

                using Transaction tr = db.TransactionManager.StartTransaction();
                if (tr.GetObject(per.ObjectId, OpenMode.ForRead, false) is Autodesk.Civil.DatabaseServices.CogoPoint cp)
                {
                    Point3d location = cp.Location;
                    double elevation = cp.Elevation;
                    tr.Commit();
                    return new PipeTopCheckPoint(new Point3d(location.X, location.Y, 0.0), elevation);
                }

                tr.Commit();
                ed.WriteMessage("\nSelected object is not a Civil 3D COGO point.");
            }
        }

        private static ObjectId GetRequiredTextStyleId(Database db, string styleName)
        {
            using Transaction tr = db.TransactionManager.StartTransaction();
            TextStyleTable tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            ObjectId id = tst.Has(styleName) ? tst[styleName] : ObjectId.Null;
            tr.Commit();
            return id;
        }

        private static void RestoreCurrentLayer(Database db, ObjectId layerId, Editor ed)
        {
            if (layerId.IsNull)
                return;

            try
            {
                using Transaction tr = db.TransactionManager.StartTransaction();
                if (!layerId.IsErased && tr.GetObject(layerId, OpenMode.ForRead, false) is LayerTableRecord)
                    db.Clayer = layerId;
                tr.Commit();
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUnable to restore the original current layer: {ex.Message}");
            }
        }

        private static string BuildPipeTopCheckLabel(double planTopElevation, double surveyTopElevation, double difference)
            => $"PLAN - TOP = {FormatElevation(planTopElevation)}\\P" +
               $"SURV - TOP = {FormatElevation(surveyTopElevation)}\\P" +
               $"\\C1;DIFF = {FormatSignedDifference(difference)}";

        private static string FormatSignedDifference(double value)
    => double.IsNaN(value)
        ? "<not available>"
        : value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);

        private readonly record struct PipeTopCheckPoint(Point3d Location, double Elevation);

        internal static PipeInfoResult BuildPipeInfo(DBObject pipeObj, Point3d pickedPoint)
        {
            Point3d start = GetPointProperty(pipeObj, "StartPoint");
            Point3d end = GetPointProperty(pipeObj, "EndPoint");

            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double len2d = Math.Sqrt((dx * dx) + (dy * dy));
            if (len2d <= 1.0e-9)
                throw new InvalidOperationException("Selected pipe has zero plan length.");

            double rawT = (((pickedPoint.X - start.X) * dx) + ((pickedPoint.Y - start.Y) * dy)) / (len2d * len2d);
            double t = Math.Max(0.0, Math.Min(1.0, rawT));

            Point3d perpendicularPoint = new Point3d(
                start.X + (dx * t),
                start.Y + (dy * t),
                0.0);

            double offset = Distance2d(pickedPoint, perpendicularPoint);
            double station = len2d * t;

            string material = GetPipeMaterialText(pipeObj);
            bool isBoxPipe = IsBoxPipe(pipeObj);
            double innerVerticalSize = GetPipeVerticalSize(pipeObj, isBoxPipe);

            (double startInvert, double endInvert) = GetPipeInvertElevations(pipeObj, start, end, innerVerticalSize, isBoxPipe);

            double invertAtPoint = startInvert + ((endInvert - startInvert) * t);

            double wallThickness = GetPipeWallThickness(pipeObj, material);
            double topOfPipe = double.IsNaN(innerVerticalSize)
                ? double.NaN
                : invertAtPoint + innerVerticalSize + (double.IsNaN(wallThickness) ? 0.0 : wallThickness);

            double slope = GetDoubleAny(pipeObj, double.NaN, "Slope");
            if (double.IsNaN(slope))
                slope = (endInvert - startInvert) / len2d;

            return new PipeInfoResult(
                GetStringAny(pipeObj, "Name"),
                GetPipeSizeText(pipeObj),
                material,
                slope,
                invertAtPoint,
                topOfPipe,
                wallThickness,
                startInvert,
                endInvert,
                station,
                len2d,
                offset,
                rawT < 0.0 || rawT > 1.0);
        }

        private static (double StartInvert, double EndInvert) GetPipeInvertElevations(object pipeObj, Point3d start, Point3d end, double innerVerticalSize, bool isBoxPipe)
        {
            double startInvert = GetDoubleAny(pipeObj, double.NaN, "StartInvertElevation", "StartInvert", "StartInvertElev");
            double endInvert = GetDoubleAny(pipeObj, double.NaN, "EndInvertElevation", "EndInvert", "EndInvertElev");

            if (!double.IsNaN(innerVerticalSize) && innerVerticalSize > 0.0)
            {
                if (isBoxPipe)
                {
                    double startCrown = GetDoubleAny(pipeObj, double.NaN, "StartCrownElevation", "StartCrown", "StartCrownElev");
                    double endCrown = GetDoubleAny(pipeObj, double.NaN, "EndCrownElevation", "EndCrown", "EndCrownElev");

                    if (!double.IsNaN(startCrown) && !double.IsNaN(endCrown))
                        return (startCrown - innerVerticalSize, endCrown - innerVerticalSize);
                }

                // Civil 3D can expose pipe StartPoint/EndPoint.Z as centerline elevation through reflection.
                // If the reported invert matches that centerline elevation, convert to invert using the inner vertical size.
                if (!double.IsNaN(startInvert) && Math.Abs(startInvert - start.Z) <= 0.01)
                    startInvert -= innerVerticalSize / 2.0;

                if (!double.IsNaN(endInvert) && Math.Abs(endInvert - end.Z) <= 0.01)
                    endInvert -= innerVerticalSize / 2.0;
            }

            if (double.IsNaN(startInvert))
                startInvert = !double.IsNaN(innerVerticalSize) && innerVerticalSize > 0.0
                    ? start.Z - (innerVerticalSize / 2.0)
                    : start.Z;

            if (double.IsNaN(endInvert))
                endInvert = !double.IsNaN(innerVerticalSize) && innerVerticalSize > 0.0
                    ? end.Z - (innerVerticalSize / 2.0)
                    : end.Z;

            return (startInvert, endInvert);
        }

        private static string GetPipeSizeText(object pipeObj)
        {
            double width = GetDoubleAny(pipeObj, double.NaN,
                "InnerPipeWidth", "InnerWidth", "InsideWidth", "InnerDiameterOrWidth",
                "InnerDiameter", "InnerPipeDiameter", "InsideDiameter", "Diameter", "PipeDiameter");
            double height = GetDoubleAny(pipeObj, double.NaN,
                "InnerPipeHeight", "InnerHeight", "InsideHeight");

            bool isBoxPipe = IsBoxPipe(pipeObj);
            if (isBoxPipe)
            {
                if (!double.IsNaN(width) && !double.IsNaN(height))
                    return $"{FormatPipeDimension(width)} W x {FormatPipeDimension(height)} H";

                if (!double.IsNaN(height))
                    return $"{FormatPipeDimension(height)} H";
            }

            if (!double.IsNaN(width))
                return FormatPipeDimension(width);

            if (!double.IsNaN(height))
                return FormatPipeDimension(height);

            return "<not available>";
        }

        private static string GetPipeMaterialText(object pipeObj)
        {
            string family = GetStringAny(pipeObj, "PartFamilyName", "FamilyName");
            string description = GetStringAny(pipeObj, "PartDescription", "Description");
            string sizeName = GetStringAny(pipeObj, "PartSizeName", "PartSize", "SizeName");

            string combined = string.Join(" | ", new[] { family, description, sizeName }.WhereNotBlank());
            if (string.IsNullOrWhiteSpace(combined))
                return "<not available>";

            string upper = combined.ToUpperInvariant();
            if (upper.Contains("C900")) return "C900";
            if (upper.Contains("PVC")) return "PVC";
            if (upper.Contains("RCB")) return "RCB";
            if (upper.Contains("RCP")) return "RCP";
            if (upper.Contains("HDPE")) return "HDPE";
            if (upper.Contains("CMP")) return "CMP";
            if (upper.Contains("DIP") || upper.Contains("DUCTILE")) return "DIP";

            return family.NotBlankOr(description.NotBlankOr(sizeName.NotBlankOr("<not available>")));
        }

        private static double GetPipeVerticalSize(object pipeObj, bool isBoxPipe)
        {
            if (isBoxPipe)
            {
                double height = GetDoubleAny(pipeObj, double.NaN,
                    "InnerPipeHeight", "InnerHeight", "InsideHeight");
                if (!double.IsNaN(height) && height > 0.0)
                    return NormalizePipeDimensionToFeet(height);
            }

            double diameter = GetDoubleAny(pipeObj, double.NaN,
                "InnerPipeDiameter", "InnerDiameterOrWidth", "InnerDiameter", "InsideDiameter", "Diameter", "PipeDiameter");
            return diameter > 0.0 ? NormalizePipeDimensionToFeet(diameter) : double.NaN;
        }

        private static double GetPipeWallThickness(object pipeObj, string material)
        {
            double directWallThickness = GetDoubleAny(pipeObj, double.NaN,
                "WallThickness", "WallThicknessValue", "PipeWallThickness", "InnerWallThickness");
            if (!double.IsNaN(directWallThickness) && directWallThickness > 0.0)
                return NormalizeWallThicknessToFeet(directWallThickness);

            double innerSize = GetPipeVerticalSize(pipeObj, IsBoxPipe(pipeObj));
            if (double.IsNaN(innerSize) || innerSize <= 0.0)
                return double.NaN;

            double innerInches = ToPipeInches(innerSize);
            string upper = material.ToUpperInvariant();

            double thicknessInches = double.NaN;
            if (upper.Contains("RCP") || upper.Contains("RCB") || upper.Contains("CONCRETE"))
                thicknessInches = LookupNearestWallThickness(innerInches, RcpWallTable);
            else if (upper.Contains("C900"))
                thicknessInches = LookupNearestWallThickness(innerInches, C900WallTable);
            else if (upper.Contains("PVC"))
                thicknessInches = LookupNearestWallThickness(innerInches, PvcWallTable);

            return double.IsNaN(thicknessInches) ? double.NaN : thicknessInches / 12.0;
        }

        private static double LookupNearestWallThickness(double innerInches, (double InnerInches, double WallInches)[] table)
        {
            double bestDelta = double.MaxValue;
            double bestWall = double.NaN;

            foreach ((double tableInner, double wall) in table)
            {
                double delta = Math.Abs(tableInner - innerInches);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    bestWall = wall;
                }
            }

            return bestDelta <= 0.75 ? bestWall : double.NaN;
        }

        private static double NormalizePipeDimensionToFeet(double value)
        {
            if (double.IsNaN(value))
                return double.NaN;

            return value > 8.0 ? value / 12.0 : value;
        }

        private static double NormalizeWallThicknessToFeet(double value)
        {
            if (double.IsNaN(value))
                return double.NaN;

            return value > 2.0 ? value / 12.0 : value;
        }

        private static bool IsBoxPipe(object pipeObj)
        {
            string shape = GetStringAny(pipeObj, "CrossSectionalShape", "CrossSectionShape", "Shape");
            if (!string.IsNullOrWhiteSpace(shape))
            {
                string upperShape = shape.ToUpperInvariant();
                if (upperShape.Contains("RECT") || upperShape.Contains("BOX"))
                    return true;
            }

            string partText = string.Join(" | ", new[]
            {
                GetStringAny(pipeObj, "PartDescription", "Description"),
                GetStringAny(pipeObj, "PartSizeName", "PartSize", "SizeName"),
                GetStringAny(pipeObj, "PartFamilyName", "FamilyName")
            }.WhereNotBlank()).ToUpperInvariant();

            return partText.Contains("RCB") || partText.Contains("BOX") || partText.Contains("RECT");
        }

        private static double ToPipeInches(double value)
        {
            if (double.IsNaN(value))
                return double.NaN;

            return value > 8.0 ? value : value * 12.0;
        }

        private static readonly (double InnerInches, double WallInches)[] RcpWallTable =
        {
            (12.0, 2.0), (15.0, 2.25), (18.0, 2.5), (21.0, 2.75), (24.0, 3.0),
            (30.0, 3.5), (36.0, 4.0), (42.0, 4.5), (48.0, 5.0), (54.0, 5.5),
            (60.0, 6.0), (66.0, 6.5), (72.0, 7.0), (84.0, 8.0), (96.0, 9.0)
        };

        private static readonly (double InnerInches, double WallInches)[] C900WallTable =
        {
            (4.416, 0.192), (6.348, 0.276), (8.326, 0.362), (10.212, 0.444),
            (12.144, 0.528), (14.358, 0.471), (16.154, 0.573), (18.018, 0.641),
            (20.134, 0.716), (23.328, 0.836), (28.799, 1.161), (33.612, 1.394)
        };

        private static readonly (double InnerInches, double WallInches)[] PvcWallTable =
        {
            (0.546, 0.147), (0.742, 0.154), (0.957, 0.179), (1.278, 0.191),
            (1.5, 0.2), (1.939, 0.218), (2.323, 0.276), (2.9, 0.3),
            (3.826, 0.337), (4.813, 0.432), (5.761, 0.562), (7.625, 0.687),
            (9.562, 0.843), (11.374, 1.031), (13.124, 1.25), (15.0, 1.5)
        };

        internal static bool IsCivilPipe(object? obj)
            => HasTypeName(obj, "Autodesk.Civil.DatabaseServices.Pipe") || HasTypeName(obj, "AeccPipe");

        private static bool HasTypeName(object? obj, string fullOrShortName)
        {
            Type? t = obj?.GetType();
            while (t != null)
            {
                if (string.Equals(t.FullName, fullOrShortName, StringComparison.Ordinal) ||
                    string.Equals(t.Name, fullOrShortName, StringComparison.Ordinal))
                    return true;

                t = t.BaseType;
            }

            return false;
        }

        private static Point3d GetPointProperty(object obj, string propertyName)
        {
            object? raw = GetPropertyValue(obj, propertyName);
            if (raw is Point3d p)
                return p;

            throw new InvalidOperationException($"Pipe property {propertyName} was not available.");
        }

        private static string GetStringAny(object obj, params string[] propertyNames)
        {
            foreach (string propertyName in propertyNames)
            {
                object? raw = GetPropertyValue(obj, propertyName);
                string? text = raw?.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text.Trim();
            }

            return string.Empty;
        }

        private static double GetDoubleAny(object obj, double fallback, params string[] propertyNames)
        {
            foreach (string propertyName in propertyNames)
            {
                object? raw = GetPropertyValue(obj, propertyName);
                if (TryConvertToDouble(raw, out double value))
                    return value;
            }

            return fallback;
        }

        private static object? GetPropertyValue(object obj, string propertyName)
        {
            PropertyInfo? pi = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            if (pi == null)
                return null;

            try
            {
                return pi.GetValue(obj);
            }
            catch
            {
                return null;
            }
        }

        private static bool TryConvertToDouble(object? raw, out double value)
        {
            value = double.NaN;
            if (raw == null)
                return false;

            if (raw is double d)
            {
                value = d;
                return true;
            }

            if (raw is float f)
            {
                value = f;
                return true;
            }

            if (raw is decimal m)
            {
                value = (double)m;
                return true;
            }

            if (raw is int i)
            {
                value = i;
                return true;
            }

            if (raw is long l)
            {
                value = l;
                return true;
            }

            string? text = raw.ToString();
            return !string.IsNullOrWhiteSpace(text) &&
                   double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static double Distance2d(Point3d a, Point3d b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        private static string FormatPipeDimension(double value)
        {
            if (double.IsNaN(value))
                return "<not available>";

            return $"{ToPipeInches(value):0.000}\"";
        }

        internal static string FormatElevation(double value)
            => double.IsNaN(value) ? "<not available>" : value.ToString("0.000", CultureInfo.InvariantCulture);

        private static string FormatSlope(double value)
        {
            if (double.IsNaN(value))
                return "<not available>";

            return $"{value * 100.0:0.000}% ({value:0.000000} ft/ft)";
        }

        internal sealed record PipeInfoResult(
            string Name,
            string Size,
            string Material,
            double Slope,
            double InvertElevation,
            double TopOfPipeElevation,
            double WallThickness,
            double StartInvertElevation,
            double EndInvertElevation,
            double StationFromStart,
            double PipeLength2d,
            double PickedOffset,
            bool WasClampedToPipeEnd);

        private sealed class PipeInfoForm : Form
        {
            public PipeInfoForm(PipeInfoResult info)
            {
                Text = "PIPE INFO @ POINT";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;
                StartPosition = FormStartPosition.CenterScreen;
                ClientSize = new Size(430, 365);

                Label title = new Label
                {
                    Text = "PIPE INFO @ POINT",
                    AutoSize = true,
                    Font = new System.Drawing.Font(SystemFonts.DefaultFont, FontStyle.Bold),
                    Location = new Point(14, 12)
                };

                TextBox details = new TextBox
                {
                    Location = new Point(14, 42),
                    Size = new Size(402, 265),
                    Multiline = true,
                    ReadOnly = true,
                    ScrollBars = ScrollBars.Vertical,
                    Font = new System.Drawing.Font(FontFamily.GenericMonospace, 9.0f),
                    Text = BuildDisplayText(info)
                };

                Button copy = new Button
                {
                    Text = "COPY",
                    Location = new Point(244, 322),
                    Size = new Size(80, 28)
                };
                copy.Click += (_, _) => Clipboard.SetText(details.Text);

                Button close = new Button
                {
                    Text = "CLOSE",
                    DialogResult = DialogResult.OK,
                    Location = new Point(336, 322),
                    Size = new Size(80, 28)
                };

                AcceptButton = close;
                CancelButton = close;

                Controls.Add(title);
                Controls.Add(details);
                Controls.Add(copy);
                Controls.Add(close);
            }

            private static string BuildDisplayText(PipeInfoResult info)
            {
                StringBuilder sb = new StringBuilder();
                AddLine(sb, "Name", info.Name.NotBlankOr("<not available>"));
                AddLine(sb, "Pipe Size", info.Size);
                AddLine(sb, "Material", info.Material);
                AddLine(sb, "Slope", FormatSlope(info.Slope));
                AddLine(sb, "Invert Elev.", FormatElevation(info.InvertElevation));
                AddLine(sb, "Top of Pipe", FormatElevation(info.TopOfPipeElevation));
                AddLine(sb, "Wall Thick.", FormatPipeDimension(info.WallThickness));
                sb.AppendLine();
                AddLine(sb, "Start Invert", FormatElevation(info.StartInvertElevation));
                AddLine(sb, "End Invert", FormatElevation(info.EndInvertElevation));
                AddLine(sb, "Station", $"{info.StationFromStart:0.00} ft from start / {info.PipeLength2d:0.00} ft");
                AddLine(sb, "Pick Offset", $"{info.PickedOffset:0.00} ft from pipe centerline");

                if (info.WasClampedToPipeEnd)
                {
                    sb.AppendLine();
                    sb.AppendLine("NOTE: Pick projected beyond pipe limits; values shown at nearest pipe end.");
                }

                return sb.ToString();
            }

            private static void AddLine(StringBuilder sb, string label, string value)
                => sb.AppendLine($"{label,-14}: {value}");
        }
    }

    internal static class UflsPipeInfoStringExtensions
    {
        public static string NotBlankOr(this string? value, string fallback)
            => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

        public static System.Collections.Generic.IEnumerable<string> WhereNotBlank(this System.Collections.Generic.IEnumerable<string?> values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    yield return value.Trim();
            }
        }
    }
}
