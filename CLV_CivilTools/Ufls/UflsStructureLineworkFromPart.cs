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
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
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

                        // Civil 3D rectangular structures are normally oriented with their
                        // LENGTH axis perpendicular to the connected pipe run. Derive that
                        // orientation from the actual connected pipe geometry rather than
                        // trusting the structure Rotation value alone.
                        bool pipeRotationUsed = TryGetPipeAxisRotation(structure, tr, out double pipeAxisRotation, out int pipeCountUsed);
                        double rectangleRotation;
                        string rotationSource;

                        if (pipeRotationUsed)
                        {
                            rectangleRotation = NormalizeAngle(pipeAxisRotation + Math.PI / 2.0);
                            rotationSource = $"connected pipe geometry ({pipeCountUsed} pipe{(pipeCountUsed == 1 ? string.Empty : "s")})";
                        }
                        else
                        {
                            double structureRotation = SafeDouble(structure, "Rotation");
                            if (Math.Abs(structureRotation) < 1e-12)
                                structureRotation = SafeDouble(structure, "RotationAngle");

                            // The Civil 3D structure rotation tracks the pipe-facing axis for
                            // these rectangular structures, so the long footprint axis is +90°.
                            rectangleRotation = NormalizeAngle(structureRotation + Math.PI / 2.0);
                            rotationSource = "structure Rotation fallback";
                        }

                        Polyline inner = BuildCenteredRectangle(center, innerLength, innerWidth, rectangleRotation);
                        inner.Layer = LayerInner;
                        ms.AppendEntity(inner);
                        tr.AddNewlyCreatedDBObject(inner, true);

                        Polyline outer = BuildCenteredRectangle(center, outerLength, outerWidth, rectangleRotation);
                        outer.Layer = LayerOuter;
                        ms.AppendEntity(outer);
                        tr.AddNewlyCreatedDBObject(outer, true);

                        tr.Commit();
                        ed.WriteMessage(
                            $"\nUFLS-STRC-2D-FROM-PART: box footprint created. " +
                            $"Inner L={innerLength:0.###}' W={innerWidth:0.###}'; " +
                            $"Outer L={outerLength:0.###}' W={outerWidth:0.###}'. " +
                            $"Rotation={RadiansToDegrees(rectangleRotation):0.###}° from {rotationSource}.");
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

        /// <summary>
        /// Returns the dominant connected-pipe AXIS angle in the XY plane. Because pipe
        /// direction can be forward or reverse, doubled-angle vector averaging is used;
        /// this treats angles 180 degrees apart as the same axis. Longer pipes carry more
        /// weight so a short side connection does not dominate an inline run.
        /// </summary>
        private static bool TryGetPipeAxisRotation(Structure structure, Transaction tr, out double rotation, out int pipeCountUsed)
        {
            rotation = 0.0;
            pipeCountUsed = 0;

            try
            {
                int count = structure.ConnectedPipesCount;
                if (count <= 0)
                    return false;

                PropertyInfo? connectedPipeProperty = structure.GetType().GetProperty(
                    "ConnectedPipe",
                    BindingFlags.Instance | BindingFlags.Public);

                if (connectedPipeProperty == null)
                    return false;

                double sumCos = 0.0;
                double sumSin = 0.0;
                double totalWeight = 0.0;
                double firstAngle = 0.0;
                bool haveFirstAngle = false;

                for (int i = 0; i < count; i++)
                {
                    ObjectId pipeId;
                    try
                    {
                        object? value = connectedPipeProperty.GetValue(structure, new object[] { i });
                        if (value is not ObjectId id || id.IsNull || !id.IsValid)
                            continue;
                        pipeId = id;
                    }
                    catch
                    {
                        continue;
                    }

                    if (tr.GetObject(pipeId, OpenMode.ForRead, false) is not Pipe pipe)
                        continue;

                    Point3d start = pipe.StartPoint;
                    Point3d end = pipe.EndPoint;
                    double dx = end.X - start.X;
                    double dy = end.Y - start.Y;
                    double length = Math.Sqrt(dx * dx + dy * dy);
                    if (length <= 1e-9)
                        continue;

                    double angle = Math.Atan2(dy, dx);
                    if (!haveFirstAngle)
                    {
                        firstAngle = angle;
                        haveFirstAngle = true;
                    }

                    double weight = Math.Max(length, 1.0);
                    sumCos += Math.Cos(2.0 * angle) * weight;
                    sumSin += Math.Sin(2.0 * angle) * weight;
                    totalWeight += weight;
                    pipeCountUsed++;
                }

                if (pipeCountUsed == 0)
                    return false;

                // A balanced set of perpendicular connections can cancel the doubled-angle
                // mean. In that uncommon case, use the first valid connected pipe axis.
                double resultant = Math.Sqrt(sumCos * sumCos + sumSin * sumSin);
                if (resultant <= Math.Max(totalWeight * 1e-6, 1e-9))
                {
                    rotation = NormalizeAngle(firstAngle);
                    return true;
                }

                rotation = NormalizeAngle(0.5 * Math.Atan2(sumSin, sumCos));
                return true;
            }
            catch
            {
                return false;
            }
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

            // Existing structure size names use inches for wall values even where the unit text is omitted.
            feet = value / 12.0;
            return feet > 0.0;
        }
    }
}
