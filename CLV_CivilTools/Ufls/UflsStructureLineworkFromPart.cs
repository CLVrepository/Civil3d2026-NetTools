using System;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace CLV_CivilTools.Ufls
{
    /// <summary>
    /// Reconstructs the GIS-required 2D inner/outer structure footprint from a selected
    /// Civil 3D Structure when the original survey linework is no longer available.
    /// Uses the same layers/standards as UFLS7 / UFLS8.
    /// </summary>
    public static class UflsStructureLineworkFromPartCommands
    {
        private const string LayerOuter = "V-SURV-STRC-OUTR-2D~~";
        private const string LayerInner = "V-SURV-STRC-INNR-2D~~";
        private const short StructureColorIndex = 141;
        private const string OuterLinetype = "CONTINUOUS";
        private const string InnerLinetype = "HIDDEN4";
        private const string PlotStyleName = "M";

        [CommandMethod("UFLS", "UFLS-STRC-2D-FROM-PART", CommandFlags.Modal)]
        public static void CreateStructure2dFromPart()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            var peo = new PromptEntityOptions("\nSelect Civil 3D structure to rebuild 2D inner/outer walls: ");
            peo.SetRejectMessage("\nSelect a Civil 3D pipe-network structure.");
            peo.AddAllowedClass(typeof(Structure), exactMatch: false);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return;

            try
            {
                using (doc.LockDocument())
                {
                    using Transaction tr = db.TransactionManager.StartTransaction();

                    if (tr.GetObject(per.ObjectId, OpenMode.ForRead, false) is not Structure structure)
                    {
                        ed.WriteMessage("\nUFLS-STRC-2D-FROM-PART: selected object is not a Civil 3D structure.");
                        return;
                    }

                    EnsureStructureLayers(db, tr);

                    Point3d location = structure.Location;
                    Point2d center = new(location.X, location.Y);

                    double innerLength = SafeDouble(structure, "InnerLength");
                    double innerWidth = SafeDouble(structure, "InnerDiameterOrWidth");
                    double wall = SafeDouble(structure, "WallThickness");

                    string sizeName = SafeString(structure, "PartSizeName");
                    if (wall <= 0.0 && TryParseWallFeet(sizeName, out double parsedWall))
                        wall = parsedWall;

                    double outerLength = FirstPositive(
                        SafeDouble(structure, "OuterLength"),
                        SafeDouble(structure, "OuterStructureLength"));
                    double outerWidth = FirstPositive(
                        SafeDouble(structure, "OuterDiameterOrWidth"),
                        SafeDouble(structure, "OuterWidth"),
                        SafeDouble(structure, "OuterStructureWidth"));

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    if (innerLength > 1e-6 && innerWidth > 1e-6)
                    {
                        if (outerLength <= 0.0 && wall > 0.0) outerLength = innerLength + 2.0 * wall;
                        if (outerWidth <= 0.0 && wall > 0.0) outerWidth = innerWidth + 2.0 * wall;

                        if (outerLength <= 0.0 || outerWidth <= 0.0)
                        {
                            ed.WriteMessage("\nUFLS-STRC-2D-FROM-PART: could not determine the outer box dimensions. Wall thickness/outer dimensions are unavailable.");
                            return;
                        }

                        double structureRotation = SafeDouble(structure, "Rotation");
                        if (Math.Abs(structureRotation) < 1e-12)
                            structureRotation = SafeDouble(structure, "RotationAngle");

                        double rectangleRotation = NormalizeAngle(structureRotation + Math.PI / 2.0);

                        Polyline inner = BuildCenteredRectangle(center, innerLength, innerWidth, rectangleRotation);
                        inner.Layer = LayerInner;
                        ms.AppendEntity(inner);
                        tr.AddNewlyCreatedDBObject(inner, true);
                        ObjectId innerId = inner.ObjectId;

                        Polyline outer = BuildCenteredRectangle(center, outerLength, outerWidth, rectangleRotation);
                        outer.Layer = LayerOuter;
                        ms.AppendEntity(outer);
                        tr.AddNewlyCreatedDBObject(outer, true);
                        ObjectId outerId = outer.ObjectId;

                        tr.Commit();
                        ed.Regen();

                        bool rotated = PromptAndRotateIfNeeded(
                            ed,
                            db,
                            new Point3d(location.X, location.Y, 0.0),
                            innerId,
                            outerId);

                        double finalRotation = rotated
                            ? NormalizeAngle(rectangleRotation + Math.PI / 2.0)
                            : rectangleRotation;

                        ed.WriteMessage(
                            $"\nUFLS-STRC-2D-FROM-PART: box footprint created. " +
                            $"Inner L={innerLength:0.###}' W={innerWidth:0.###}'; " +
                            $"Outer L={outerLength:0.###}' W={outerWidth:0.###}'. " +
                            $"Final footprint rotation={RadiansToDegrees(finalRotation):0.###}°" +
                            (rotated ? " (user rotated +90°)." : "."));
                        return;
                    }

                    double innerDiameter = innerWidth;
                    if (innerDiameter <= 1e-6)
                        innerDiameter = SafeDouble(structure, "InnerDiameter");

                    double outerDiameter = FirstPositive(
                        outerWidth,
                        SafeDouble(structure, "OuterDiameter"));
                    if (outerDiameter <= 0.0 && wall > 0.0)
                        outerDiameter = innerDiameter + 2.0 * wall;

                    if (innerDiameter <= 1e-6 || outerDiameter <= 1e-6)
                    {
                        ed.WriteMessage("\nUFLS-STRC-2D-FROM-PART: could not determine cylindrical inner/outer diameters.");
                        return;
                    }

                    Point3d flatCenter = new(location.X, location.Y, 0.0);
                    var innerCircle = new Circle(flatCenter, Vector3d.ZAxis, innerDiameter / 2.0)
                    {
                        Layer = LayerInner
                    };
                    ms.AppendEntity(innerCircle);
                    tr.AddNewlyCreatedDBObject(innerCircle, true);

                    var outerCircle = new Circle(flatCenter, Vector3d.ZAxis, outerDiameter / 2.0)
                    {
                        Layer = LayerOuter
                    };
                    ms.AppendEntity(outerCircle);
                    tr.AddNewlyCreatedDBObject(outerCircle, true);

                    tr.Commit();
                    ed.WriteMessage(
                        $"\nUFLS-STRC-2D-FROM-PART: circular footprint created. " +
                        $"Inner Dia={innerDiameter:0.###}'; Outer Dia={outerDiameter:0.###}'.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUFLS-STRC-2D-FROM-PART error: {ex.Message}");
            }
        }

        private static bool PromptAndRotateIfNeeded(
            Editor ed,
            Database db,
            Point3d center,
            ObjectId innerId,
            ObjectId outerId)
        {
            var options = new PromptKeywordOptions("\nOrientation Correct? [Yes/No] <Yes>: ")
            {
                AllowNone = true
            };
            options.Keywords.Add("Yes");
            options.Keywords.Add("No");
            options.Keywords.Default = "Yes";

            PromptResult result = ed.GetKeywords(options);
            if (result.Status != PromptStatus.OK && result.Status != PromptStatus.None)
                return false;

            string choice = string.IsNullOrWhiteSpace(result.StringResult)
                ? "Yes"
                : result.StringResult;

            if (!string.Equals(choice, "No", StringComparison.OrdinalIgnoreCase))
                return false;

            using Transaction rotateTr = db.TransactionManager.StartTransaction();
            Matrix3d rotate90 = Matrix3d.Rotation(Math.PI / 2.0, Vector3d.ZAxis, center);

            if (rotateTr.GetObject(innerId, OpenMode.ForWrite, false) is AcEntity innerEntity)
                innerEntity.TransformBy(rotate90);

            if (rotateTr.GetObject(outerId, OpenMode.ForWrite, false) is AcEntity outerEntity)
                outerEntity.TransformBy(rotate90);

            rotateTr.Commit();
            ed.Regen();
            return true;
        }

        private static Polyline BuildCenteredRectangle(Point2d center, double length, double width, double rotation)
        {
            double halfL = length / 2.0;
            double halfW = width / 2.0;
            double c = Math.Cos(rotation);
            double s = Math.Sin(rotation);

            var local = new[]
            {
                new Point2d(-halfL, -halfW),
                new Point2d( halfL, -halfW),
                new Point2d( halfL,  halfW),
                new Point2d(-halfL,  halfW)
            };

            var pl = new Polyline(4);
            for (int i = 0; i < local.Length; i++)
            {
                double x = center.X + local[i].X * c - local[i].Y * s;
                double y = center.Y + local[i].X * s + local[i].Y * c;
                pl.AddVertexAt(i, new Point2d(x, y), 0.0, 0.0, 0.0);
            }
            pl.Closed = true;
            pl.Elevation = 0.0;
            return pl;
        }

        private static double NormalizeAngle(double angle)
        {
            double twoPi = Math.PI * 2.0;
            angle %= twoPi;
            if (angle < 0.0) angle += twoPi;
            return angle;
        }

        private static double RadiansToDegrees(double radians)
        {
            return radians * 180.0 / Math.PI;
        }

        private static void EnsureStructureLayers(Database db, Transaction tr)
        {
            EnsureLayer(db, tr, LayerOuter, StructureColorIndex, OuterLinetype, PlotStyleName);
            EnsureLayer(db, tr, LayerInner, StructureColorIndex, InnerLinetype, PlotStyleName);
        }

        private static void EnsureLayer(Database db, Transaction tr, string layerName, short colorIndex, string linetypeName, string plotStyleName)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            LayerTableRecord ltr;

            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();
                ltr = new LayerTableRecord { Name = layerName };
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
            else
            {
                ltr = (LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForWrite);
            }

            ltr.Color = AcColor.FromColorIndex(ColorMethod.ByAci, colorIndex);

            ObjectId linetypeId = GetOrLoadLinetypeId(db, tr, linetypeName);
            if (!linetypeId.IsNull)
                ltr.LinetypeObjectId = linetypeId;

            TryAssignNamedPlotStyle(db, tr, ltr, plotStyleName);
        }

        private static ObjectId GetOrLoadLinetypeId(Database db, Transaction tr, string linetypeName)
        {
            LinetypeTable ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (ltt.Has(linetypeName)) return ltt[linetypeName];

            try { db.LoadLineTypeFile(linetypeName, "acad.lin"); }
            catch { }

            ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (ltt.Has(linetypeName)) return ltt[linetypeName];
            return ltt.Has("Continuous") ? ltt["Continuous"] : ObjectId.Null;
        }

        private static void TryAssignNamedPlotStyle(Database db, Transaction tr, LayerTableRecord ltr, string plotStyleName)
        {
            try
            {
                DBDictionary psDict = (DBDictionary)tr.GetObject(db.PlotStyleNameDictionaryId, OpenMode.ForRead);
                if (psDict.Contains(plotStyleName))
                    ltr.PlotStyleNameId = psDict.GetAt(plotStyleName);
            }
            catch { }
        }

        private static double SafeDouble(object source, string propertyName)
        {
            try
            {
                PropertyInfo? pi = source.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                object? value = pi?.GetValue(source);
                return value == null ? 0.0 : Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0.0;
            }
        }

        private static string SafeString(object source, string propertyName)
        {
            try
            {
                return source.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(source)?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static double FirstPositive(params double[] values)
        {
            foreach (double value in values)
                if (value > 1e-6) return value;
            return 0.0;
        }

        private static bool TryParseWallFeet(string text, out double feet)
        {
            feet = 0.0;
            Match m = Regex.Match(
                text ?? string.Empty,
                @"WALL(?:S)?\s*=\s*(?<wall>[0-9]+(?:\.[0-9]+)?)\s*(?<unit>''|""|INCH(?:ES)?)?",
                RegexOptions.IgnoreCase);
            if (!m.Success || !double.TryParse(m.Groups["wall"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                return false;

            feet = value / 12.0;
            return feet > 0.0;
        }
    }
}
