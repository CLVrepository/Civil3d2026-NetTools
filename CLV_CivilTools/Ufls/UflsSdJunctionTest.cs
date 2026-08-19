using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Forms;
using DrawingFont = System.Drawing.Font;
using DrawingFontStyle = System.Drawing.FontStyle;
using DrawingPoint = System.Drawing.Point;
using DrawingSize = System.Drawing.Size;
using DrawingSystemFonts = System.Drawing.SystemFonts;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Ufls
{
    /// <summary>
    /// Test command for SD-JUNCTION placed-structure size overrides.
    /// Reads a closed inner-wall polyline, computes a best-fit rotated width/length,
    /// then attempts to write the selected Civil 3D structure's width/length style
    /// fields and properties in-place so data-reference behavior can be tested.
    /// </summary>
    public static class UflsSdJunctionTestCommands
    {
        [CommandMethod("UFLS", "SD-JUNCTION", CommandFlags.Modal)]
        [CommandMethod("UFLS-SD-JUNCTION", CommandFlags.Modal)]
        public static void SdJunction_TestPlacedStructureOverride()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Polyline? footprint = PromptForClosedPolyline(ed, tr);
                    if (footprint == null)
                        return;

                    DBObject? structureObj = PromptForStructure(ed, tr);
                    if (structureObj == null)
                        return;

                    if (!TryGetBestFitFootprintSize(footprint, out double longSideFeet, out double shortSideFeet, out double bestRotationRadians))
                        throw new InvalidOperationException("Unable to determine width and length from the selected closed polyline.");

                    double defaultWidthFeet = RoundFeetToNearestInch(shortSideFeet);
                    double defaultLengthFeet = RoundFeetToNearestInch(longSideFeet);

                    using var form = new SdJunctionOverrideForm(defaultWidthFeet, defaultLengthFeet, bestRotationRadians);
                    DialogResult result = AcadApp.ShowModalDialog(form);
                    if (result != DialogResult.OK)
                    {
                        ed.WriteMessage("\nSD-JUNCTION: Cancelled.");
                        return;
                    }

                    double targetWidthFeet = form.WidthFeet;
                    double targetLengthFeet = form.LengthFeet;

                    var log = new List<string>();
                    int updates = ApplyPlacedStructureOverride(structureObj, targetWidthFeet, targetLengthFeet, log);

                    tr.Commit();

                    ed.WriteMessage($"\nSD-JUNCTION: Width={FormatFeetInches(targetWidthFeet)}, Length={FormatFeetInches(targetLengthFeet)}.");
                    ed.WriteMessage($"\nSD-JUNCTION: Updated {updates} structure size field(s)/property(ies).");
                    foreach (string line in log.Take(12))
                        ed.WriteMessage($"\n  - {line}");

                    if (log.Count > 12)
                        ed.WriteMessage($"\n  - ... {log.Count - 12} more update message(s)");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nSD-JUNCTION error: {ex.Message}");
            }
        }

        private static Polyline? PromptForClosedPolyline(Editor ed, Transaction tr)
        {
            var peo = new PromptEntityOptions("\nSelect closed INNER wall polyline: ");
            peo.SetRejectMessage("\nSelect a closed LWPOLYLINE.");
            peo.AddAllowedClass(typeof(Polyline), exactMatch: false);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
                return null;

            Polyline pl = (Polyline)tr.GetObject(per.ObjectId, OpenMode.ForRead);
            if (!pl.Closed)
                throw new InvalidOperationException("Selected polyline is not closed.");

            if (pl.NumberOfVertices < 3)
                throw new InvalidOperationException("Selected polyline does not have enough vertices.");

            return pl;
        }

        private static DBObject? PromptForStructure(Editor ed, Transaction tr)
        {
            PromptEntityOptions peo = new PromptEntityOptions("\nSelect SD-JUNCTION structure to resize: ");
            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
                return null;

            DBObject dbo = tr.GetObject(per.ObjectId, OpenMode.ForWrite, false);
            if (!IsCivilStructure(dbo))
                throw new InvalidOperationException("Selected object is not a Civil 3D structure.");

            return dbo;
        }

        private static bool TryGetBestFitFootprintSize(Polyline pl, out double longSide, out double shortSide, out double bestRotationRadians)
        {
            longSide = 0.0;
            shortSide = 0.0;
            bestRotationRadians = 0.0;

            List<Point2d> pts = GetUniquePolylineVertices(pl);
            if (pts.Count < 3)
                return false;

            double bestArea = double.MaxValue;
            double bestW = 0.0;
            double bestL = 0.0;
            double bestAngle = 0.0;

            for (int i = 0; i < pts.Count; i++)
            {
                Point2d a = pts[i];
                Point2d b = pts[(i + 1) % pts.Count];
                Vector2d edge = b - a;
                if (edge.Length < 1e-8)
                    continue;

                Vector2d u = edge.GetNormal();
                Vector2d v = new Vector2d(-u.Y, u.X);

                double minU = double.MaxValue;
                double maxU = double.MinValue;
                double minV = double.MaxValue;
                double maxV = double.MinValue;

                foreach (Point2d p in pts)
                {
                    double pu = (p.X * u.X) + (p.Y * u.Y);
                    double pv = (p.X * v.X) + (p.Y * v.Y);
                    if (pu < minU) minU = pu;
                    if (pu > maxU) maxU = pu;
                    if (pv < minV) minV = pv;
                    if (pv > maxV) maxV = pv;
                }

                double dimU = maxU - minU;
                double dimV = maxV - minV;
                double area = dimU * dimV;
                if (area < bestArea)
                {
                    bestArea = area;
                    bestL = Math.Max(dimU, dimV);
                    bestW = Math.Min(dimU, dimV);
                    bestAngle = Math.Atan2(u.Y, u.X);
                }
            }

            if (bestArea == double.MaxValue || bestL < 1e-8 || bestW < 1e-8)
                return false;

            longSide = bestL;
            shortSide = bestW;
            bestRotationRadians = bestAngle;
            return true;
        }

        private static List<Point2d> GetUniquePolylineVertices(Polyline pl)
        {
            var pts = new List<Point2d>();
            Point2d? prior = null;
            for (int i = 0; i < pl.NumberOfVertices; i++)
            {
                Point2d p = pl.GetPoint2dAt(i);
                if (prior == null || prior.Value.GetDistanceTo(p) > 1e-8)
                    pts.Add(p);
                prior = p;
            }

            if (pts.Count > 1 && pts[0].GetDistanceTo(pts[^1]) < 1e-8)
                pts.RemoveAt(pts.Count - 1);

            return pts;
        }

        private static double RoundFeetToNearestInch(double feet)
            => Math.Round(feet * 12.0, MidpointRounding.AwayFromZero) / 12.0;

        private static int ApplyPlacedStructureOverride(DBObject structureObj, double widthFeet, double lengthFeet, List<string> log)
        {
            double widthInches = widthFeet * 12.0;
            double lengthInches = lengthFeet * 12.0;

            int updates = 0;

            // Direct structure properties first. These often behave like drawing-unit / geometric fields,
            // so keep feeding them feet.
            updates += TrySetExactNumericProperty(structureObj, "Width", widthFeet, log);
            updates += TrySetExactNumericProperty(structureObj, "Length", lengthFeet, log);
            updates += TrySetExactNumericProperty(structureObj, "StructureWidth", widthFeet, log);
            updates += TrySetExactNumericProperty(structureObj, "StructureLength", lengthFeet, log);
            updates += TrySetExactNumericProperty(structureObj, "OverallWidth", widthFeet, log);
            updates += TrySetExactNumericProperty(structureObj, "OverallLength", lengthFeet, log);
            updates += TrySetExactNumericProperty(structureObj, "OuterWidth", widthFeet, log);
            updates += TrySetExactNumericProperty(structureObj, "OuterLength", lengthFeet, log);

            // Part-data fields exposed by the structure part family typically show inch values in the Part Size Creator.
            if (TryGetProperty(structureObj, "PartData", out PropertyInfo? partDataPi) && partDataPi != null)
            {
                object? partDataObj = GetPropertyValueSafely(partDataPi, structureObj);
                if (partDataObj != null)
                    updates += ApplyPartDataTargets(partDataObj, widthFeet, lengthFeet, widthInches, lengthInches, log);
            }

            RunPostUpdateHooks(structureObj, log);

            if (structureObj is Entity ent)
            {
                ent.RecordGraphicsModified(true);
            }

            if (updates == 0)
                throw new InvalidOperationException(
                    "No writable width/length targets were found on the selected structure. " +
                    "This part family may expose different field names than the current test command expects.");

            return updates;
        }

        private static int ApplyPartDataTargets(object root, double widthFeet, double lengthFeet, double widthInches, double lengthInches, List<string> log)
        {
            var visited = new HashSet<int>();
            var fields = new List<PartFieldTarget>();
            DiscoverPartFieldTargets(root, visited, 0, fields, "PartData");

            if (fields.Count == 0)
            {
                log.Add("No part-data field targets were discovered under PartData.");
            }
            else
            {
                foreach (PartFieldTarget sample in fields.Take(20))
                    log.Add($"Discovered field target: {sample.Path} :: {sample.Label} :: value={sample.ValueProperty.Name}");

                if (fields.Count > 20)
                    log.Add($"... {fields.Count - 20} more field target(s) discovered.");
            }

            int updates = 0;
            updates += ApplyBestTargets(fields, widthFeet, widthInches, isWidth: true, log);
            updates += ApplyBestTargets(fields, lengthFeet, lengthInches, isWidth: false, log);
            return updates;
        }

        private static int ApplyBestTargets(List<PartFieldTarget> fields, double valueFeet, double valueInches, bool isWidth, List<string> log)
        {
            string dimensionName = isWidth ? "width" : "length";
            var candidates = fields
                .Where(f => GetDimensionScore(f.Label, isWidth) > 0)
                .OrderByDescending(f => GetDimensionScore(f.Label, isWidth))
                .ThenBy(f => f.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();

            int updates = 0;
            bool highConfidenceApplied = false;

            foreach (PartFieldTarget target in candidates)
            {
                int score = GetDimensionScore(target.Label, isWidth);
                if (highConfidenceApplied && score < 100)
                    break;

                double valueToWrite = ChooseFieldWriteValue(target, valueFeet, valueInches);
                if (TryAssignFieldValue(target, valueToWrite, log))
                {
                    updates++;
                    if (score >= 100)
                        highConfidenceApplied = true;
                }
            }

            if (updates == 0)
                log.Add($"No part-data {dimensionName} field match found.");

            return updates;
        }

        private static void DiscoverPartFieldTargets(object obj, HashSet<int> visited, int depth, List<PartFieldTarget> fields, string path)
        {
            if (obj == null || depth > 5)
                return;

            Type type = obj.GetType();
            if (type == typeof(string))
                return;

            int id = RuntimeHelpers.GetHashCode(obj);
            if (!visited.Add(id))
                return;

            if (TryBuildPartFieldTarget(obj, path, out PartFieldTarget? target) && target != null)
            {
                fields.Add(target);
            }

            if (obj is IEnumerable enumerable)
            {
                int count = 0;
                foreach (object? item in enumerable)
                {
                    if (item != null)
                        DiscoverPartFieldTargets(item, visited, depth + 1, fields, path + "[]");

                    count++;
                    if (count > 250)
                        break;
                }
                return;
            }

            foreach (PropertyInfo pi in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!pi.CanRead)
                    continue;
                if (pi.GetIndexParameters().Length != 0)
                    continue;

                Type pt = pi.PropertyType;
                if (pt.IsPrimitive || pt.IsEnum || pt == typeof(string) || pt == typeof(decimal))
                    continue;

                object? child = null;
                try
                {
                    child = pi.GetValue(obj);
                }
                catch
                {
                    continue;
                }

                if (child == null)
                    continue;

                string pName = pi.Name;
                if (pName.IndexOf("sync", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    pName.IndexOf("document", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    pName.IndexOf("database", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    continue;
                }

                DiscoverPartFieldTargets(child, visited, depth + 1, fields, path + "." + pName);
            }
        }

        private static bool TryBuildPartFieldTarget(object obj, string path, out PartFieldTarget? target)
        {
            target = null;

            Type type = obj.GetType();
            PropertyInfo? valuePi = FindWritableValueProperty(type);
            if (valuePi == null)
                return false;

            string label = GetBestFieldLabel(obj);
            if (string.IsNullOrWhiteSpace(label))
                label = path.Split('.').LastOrDefault() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(label))
                return false;

            target = new PartFieldTarget(obj, valuePi, label, GetBestUnitsLabel(obj), path);
            return true;
        }

        private static PropertyInfo? FindWritableValueProperty(Type type)
        {
            foreach (string name in new[] { "Value", "DataValue", "CurrentValue", "DoubleValue", "NumericValue", "StringValue", "IntValue" })
            {
                PropertyInfo? pi = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (pi != null && pi.CanWrite)
                {
                    Type pt = Nullable.GetUnderlyingType(pi.PropertyType) ?? pi.PropertyType;
                    if (pt == typeof(double) || pt == typeof(float) || pt == typeof(decimal) ||
                        pt == typeof(int) || pt == typeof(short) || pt == typeof(long) || pt == typeof(string))
                    {
                        return pi;
                    }
                }
            }

            return null;
        }

        private static string GetBestFieldLabel(object obj)
        {
            foreach (string name in new[] { "ContextString", "DisplayName", "Name", "Description" })
            {
                if (TryGetProperty(obj, name, out PropertyInfo? pi))
                {
                    object? value = pi != null ? GetPropertyValueSafely(pi, obj) : null;
                    if (value is string s && !string.IsNullOrWhiteSpace(s))
                        return s;
                }
            }

            return string.Empty;
        }

        private static string GetBestUnitsLabel(object obj)
        {
            foreach (string name in new[] { "Units", "Unit", "UnitString", "UnitsString", "ContextUnits" })
            {
                if (TryGetProperty(obj, name, out PropertyInfo? pi))
                {
                    object? value = pi != null ? GetPropertyValueSafely(pi, obj) : null;
                    if (value != null)
                    {
                        string s = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(s))
                            return s;
                    }
                }
            }

            return string.Empty;
        }

        private static int GetDimensionScore(string label, bool isWidth)
        {
            string s = NormalizeLabel(label);
            if (string.IsNullOrWhiteSpace(s))
                return 0;

            bool mentionsPrimary = isWidth ? s.Contains("width") : s.Contains("length");
            if (!mentionsPrimary)
                return 0;

            if (s.Contains("wall") || s.Contains("thickness") || s.Contains("depth") || s.Contains("height") ||
                s.Contains("diameter") || s.Contains("sump") || s.Contains("rim") || s.Contains("frame") ||
                s.Contains("cone") || s.Contains("clearopen") || s.Contains("opening"))
            {
                return 0;
            }

            int score = 60;

            if (s.Contains("inner") || s.Contains("inside"))
                score += 90;

            if (s.Contains("overall") || s.Contains("outer") || s.Contains("structure") || s.Contains("box") || s.Contains("rectangular"))
                score += 20;

            if (s == (isWidth ? "width" : "length"))
                score += 40;

            return score;
        }

        private static string NormalizeLabel(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length);
            foreach (char ch in value)
            {
                if (char.IsLetterOrDigit(ch))
                    sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }

        private static object? GetPropertyValueSafely(PropertyInfo propertyInfo, object target)
        {
            try
            {
                return propertyInfo.GetValue(target);
            }
            catch
            {
                return null;
            }
        }

        private static double ChooseFieldWriteValue(PartFieldTarget target, double valueFeet, double valueInches)
        {
            string units = NormalizeLabel(target.UnitsLabel);
            if (units.Contains("inch") || units == "in")
                return valueInches;

            if (units.Contains("foot") || units == "ft")
                return valueFeet;

            // Part Builder fields for these structure dimensions are typically inch-based.
            return valueInches;
        }

        private static void RunPostUpdateHooks(object target, List<string> log)
        {
            foreach (string methodName in new[]
            {
                "ResizeJunctionStructure",
                "ResizeByPartData",
                "ResizePart",
                "Resize",
                "UpdatePartData",
                "ApplyRules",
                "Update"
            })
            {
                TryInvokeZeroArgMethod(target, methodName, log);
            }
        }

        private static bool TryInvokeZeroArgMethod(object target, string methodName, List<string> log)
        {
            try
            {
                MethodInfo? mi = target.GetType().GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (mi == null)
                    return false;

                mi.Invoke(target, null);
                log.Add($"Invoked method '{methodName}()'.");
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryAssignFieldValue(PartFieldTarget target, double valueFeet, List<string> log)
        {
            Type rawType = Nullable.GetUnderlyingType(target.ValueProperty.PropertyType) ?? target.ValueProperty.PropertyType;

            try
            {
                object converted = rawType == typeof(string)
                    ? Convert.ToString(valueFeet, CultureInfo.InvariantCulture)!
                    : Convert.ChangeType(valueFeet, rawType, CultureInfo.InvariantCulture);

                target.ValueProperty.SetValue(target.TargetObject, converted);
                log.Add($"Updated part data field '{target.Label}' at {target.Path} ({(string.IsNullOrWhiteSpace(target.UnitsLabel) ? "units?" : target.UnitsLabel)}) = {valueFeet.ToString("0.###", CultureInfo.InvariantCulture)}");
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int TrySetExactNumericProperty(object target, string propertyName, double valueFeet, List<string> log)
        {
            if (!TryGetProperty(target, propertyName, out PropertyInfo? pi))
                return 0;

            if (pi == null || !pi.CanWrite)
                return 0;

            Type rawType = Nullable.GetUnderlyingType(pi.PropertyType) ?? pi.PropertyType;
            if (rawType != typeof(double) && rawType != typeof(float) && rawType != typeof(decimal) &&
                rawType != typeof(int) && rawType != typeof(short) && rawType != typeof(long))
            {
                return 0;
            }

            try
            {
                object converted = Convert.ChangeType(valueFeet, rawType, CultureInfo.InvariantCulture);
                pi.SetValue(target, converted);
                log.Add($"Updated property '{propertyName}' = {FormatFeetInches(valueFeet)}");
                return 1;
            }
            catch
            {
                return 0;
            }
        }

        private static bool TryGetProperty(object target, string propertyName, [NotNullWhen(true)] out PropertyInfo? propertyInfo)
        {
            propertyInfo = target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
            return propertyInfo != null;
        }

        private static bool IsCivilStructure(object? obj)
        {
            Type? t = obj?.GetType();
            while (t != null)
            {
                if (string.Equals(t.FullName, "Autodesk.Civil.DatabaseServices.Structure", StringComparison.Ordinal) ||
                    string.Equals(t.Name, "AeccStructure", StringComparison.Ordinal) ||
                    string.Equals(t.Name, "Structure", StringComparison.Ordinal))
                    return true;

                t = t.BaseType;
            }

            return false;
        }

        private static string FormatFeetInches(double feet)
        {
            double inches = feet * 12.0;
            return string.Format(CultureInfo.InvariantCulture, "{0:0.###} ft ({1:0.###}\")", feet, inches);
        }

        private sealed record PartFieldTarget(object TargetObject, PropertyInfo ValueProperty, string Label, string UnitsLabel, string Path);
    }

    internal sealed class SdJunctionOverrideForm : Form
    {
        private readonly TextBox _txtWidth;
        private readonly TextBox _txtLength;

        public double WidthFeet => ParseFeetFromInchText(_txtWidth.Text);
        public double LengthFeet => ParseFeetFromInchText(_txtLength.Text);

        public SdJunctionOverrideForm(double defaultWidthFeet, double defaultLengthFeet, double bestRotationRadians)
        {
            Text = "SD-JUNCTION TEST";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = false;
            MaximizeBox = false;
            ClientSize = new DrawingSize(420, 210);

            double rotationDegrees = bestRotationRadians * 180.0 / Math.PI;

            Controls.Add(new Label
            {
                Text = "BEST-FIT INNER-WALL FOOTPRINT SIZE",
                Location = new DrawingPoint(12, 14),
                AutoSize = true,
                Font = (System.Drawing.Font)new DrawingFont(DrawingSystemFonts.DefaultFont, DrawingFontStyle.Bold)
            });

            Controls.Add(new Label
            {
                Text = $"Detected orientation: {rotationDegrees:0.##}°",
                Location = new DrawingPoint(12, 40),
                AutoSize = true
            });

            Controls.Add(new Label
            {
                Text = "Width (in):",
                Location = new DrawingPoint(12, 78),
                AutoSize = true
            });

            _txtWidth = new TextBox
            {
                Location = new DrawingPoint(120, 74),
                Width = 120,
                Text = (defaultWidthFeet * 12.0).ToString("0.###", CultureInfo.InvariantCulture)
            };
            Controls.Add(_txtWidth);

            Controls.Add(new Label
            {
                Text = "Length (in):",
                Location = new DrawingPoint(12, 110),
                AutoSize = true
            });

            _txtLength = new TextBox
            {
                Location = new DrawingPoint(120, 106),
                Width = 120,
                Text = (defaultLengthFeet * 12.0).ToString("0.###", CultureInfo.InvariantCulture)
            };
            Controls.Add(_txtLength);

            Controls.Add(new Label
            {
                Text = "Values will be written to INNER structure size fields first, then post-update hooks will run.",
                Location = new DrawingPoint(12, 144),
                Size = new DrawingSize(390, 32)
            });

            var btnOk = new Button
            {
                Text = "OK",
                DialogResult = DialogResult.OK,
                Width = 80,
                Location = new DrawingPoint(240, 176)
            };
            Controls.Add(btnOk);

            var btnCancel = new Button
            {
                Text = "CANCEL",
                DialogResult = DialogResult.Cancel,
                Width = 80,
                Location = new DrawingPoint(330, 176)
            };
            Controls.Add(btnCancel);

            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        private static double ParseFeetFromInchText(string text)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double inches) || inches <= 0.0)
                throw new InvalidOperationException("Width and length must be positive numeric inch values.");

            return inches / 12.0;
        }
    }
}
