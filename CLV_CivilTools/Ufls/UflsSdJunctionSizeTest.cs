using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

using ReflectionAssembly = System.Reflection.Assembly;
using System.Text.RegularExpressions;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDbObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace CLV_CivilTools.Ufls
{
    /// <summary>
    /// Test command for SD-JUNCTION size matching using built-in part-family size records.
    /// Workflow:
    /// 1. Select closed inner-wall polyline.
    /// 2. Select an existing SD-JUNCTION structure already using the correct family/type.
    /// 3. Read polyline width/length.
    /// 4. Ensure the closest matching size exists in the current parts-list family.
    /// 5. Attempt to swap the selected structure to that matching family/size.
    /// </summary>
    public static class UflsSdJunctionSizeTestCommands
    {
        [CommandMethod("UFLS", "SD-JUNCTION-SIZE", CommandFlags.Modal)]
        [CommandMethod("UFLS", "UFLS-SD-JUNCTION-SIZE", CommandFlags.Modal)]
        public static void SdJunction_SizeFromPolyline_Test()
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

                    Structure? structure = PromptForStructure(ed, tr);
                    if (structure == null)
                        return;

                    if (!TryGetBestFitFootprintSize(footprint, out double longSideFeet, out double shortSideFeet, out _))
                        throw new InvalidOperationException("Unable to determine width and length from the selected closed polyline.");

                    double targetWidthInches = RoundToNearestInch(shortSideFeet * 12.0);
                    double targetLengthInches = RoundToNearestInch(longSideFeet * 12.0);

                    ed.WriteMessage(
                        $"\nSD-JUNCTION-SIZE: extracted polyline size L={targetLengthInches:0.##}\" W={targetWidthInches:0.##}\".");

                    ObjectId originalStyleId = structure.StyleId;
                    string originalStyleName = structure.StyleName ?? string.Empty;

                    PartFamily family = ResolveStructurePartFamily(tr, structure);

                    ObjectId matchedSizeId = EnsureMatchingPartSize(tr, family, structure, targetWidthInches, targetLengthInches, ed);
                    string matchedSizeName = GetPartSizeName(tr, matchedSizeId);

                    bool styleAppliedToDefinition = TryApplyStyleToPartSizeDefinition(tr, matchedSizeId, originalStyleId, originalStyleName, ed);
                    bool swapped = TrySwapStructureToSize(structure, family.ObjectId, matchedSizeId, ed);
                    bool styleRestoredOnStructure = TryRestoreStructureStyle(structure, originalStyleId, originalStyleName, ed);

                    if (structure is AcEntity ent)
                        ent.RecordGraphicsModified(true);

                    tr.Commit();

                    ed.WriteMessage($"\nSD-JUNCTION-SIZE: target size '{matchedSizeName}'.");
                    if (styleAppliedToDefinition)
                        ed.WriteMessage("\nSD-JUNCTION-SIZE: source structure style was applied to the matched part-size definition.");
                    else if (styleRestoredOnStructure)
                        ed.WriteMessage("\nSD-JUNCTION-SIZE: source structure style was restored on the swapped structure as a fallback.");
                    else
                        ed.WriteMessage("\nSD-JUNCTION-SIZE: no writable part-size style target was found; Civil 3D may still fall back to the family/default style.");

                    ed.WriteMessage(swapped
                        ? "\nSD-JUNCTION-SIZE: structure swap attempt completed."
                        : "\nSD-JUNCTION-SIZE: matching size was ensured in the family, but no supported swap method was found on the structure object.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nSD-JUNCTION-SIZE error: {ex.Message}");
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

        private static Structure? PromptForStructure(Editor ed, Transaction tr)
        {
            PromptEntityOptions peo = new PromptEntityOptions("\nSelect SD-JUNCTION structure to size-match: ");
            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
                return null;

            AcDbObject dbo = tr.GetObject(per.ObjectId, OpenMode.ForWrite, false);
            if (dbo is not Structure structure)
                throw new InvalidOperationException("Selected object is not a Civil 3D structure.");

            return structure;
        }

        internal static bool TryGetBestFitFootprintSize(Polyline pl, out double longSideFeet, out double shortSideFeet, out double bestRotationRadians)
        {
            longSideFeet = 0.0;
            shortSideFeet = 0.0;
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

            longSideFeet = bestL;
            shortSideFeet = bestW;
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

        private static double RoundToNearestInch(double inches)
            => Math.Round(inches, MidpointRounding.AwayFromZero);

        private static PartFamily ResolveStructurePartFamily(Transaction tr, Structure structure)
        {
            if (!structure.PartFamilyId.IsNull &&
                tr.GetObject(structure.PartFamilyId, OpenMode.ForWrite, false) is PartFamily directFamily)
            {
                return directFamily;
            }

            if (structure.NetworkId.IsNull)
                throw new InvalidOperationException("Selected structure does not have a valid NetworkId.");

            AcDbObject networkObj = tr.GetObject(structure.NetworkId, OpenMode.ForRead, false);
            ObjectId partsListId = GetObjectIdProperty(networkObj, "PartsListId");
            if (partsListId.IsNull)
                throw new InvalidOperationException("Selected structure's network does not have a valid PartsListId.");

            if (tr.GetObject(partsListId, OpenMode.ForRead, false) is not PartsList partsList)
                throw new InvalidOperationException("Could not open the network parts list.");

            ObjectIdCollection familyIds = partsList.GetPartFamilyIdsByDomain(DomainType.Structure);
            foreach (ObjectId familyId in familyIds)
            {
                if (tr.GetObject(familyId, OpenMode.ForWrite, false) is not PartFamily family)
                    continue;

                if (family.ObjectId == structure.PartFamilyId)
                    return family;

                if (!string.IsNullOrWhiteSpace(structure.PartFamilyName) &&
                    string.Equals(family.Name ?? string.Empty, structure.PartFamilyName, StringComparison.OrdinalIgnoreCase))
                {
                    return family;
                }
            }

            throw new InvalidOperationException($"Could not resolve PartFamily '{structure.PartFamilyName}' from the active network parts list.");
        }

        private static ObjectId EnsureMatchingPartSize(Transaction tr, PartFamily family, Structure structure, double targetWidthInches, double targetLengthInches, Editor ed)
        {
            List<PartSizeInfo> sizesBefore = GetFamilyPartSizes(tr, family);
            if (sizesBefore.Count == 0)
                throw new InvalidOperationException("The selected structure family does not expose any existing part sizes in the current parts list.");

            if (TryFindClosestExistingSize(sizesBefore, targetWidthInches, targetLengthInches, out PartSizeInfo existingMatch))
            {
                ed.WriteMessage($"\nSD-JUNCTION-SIZE: found existing family size '{existingMatch.Name}'.");
                return existingMatch.Id;
            }

            ReflectionAssembly civilAsm = typeof(PartFamily).Assembly;
            Type sizeFilterRecordType = FindTypeByName(civilAsm, "SizeFilterRecord")
                ?? throw new InvalidOperationException("Could not locate SizeFilterRecord in the Civil 3D API assembly.");

            Type partContextType = FindTypeByName(civilAsm, "PartContextType")
                ?? throw new InvalidOperationException("Could not locate PartContextType in the Civil 3D API assembly.");

            object sizeFilterRecord = Activator.CreateInstance(sizeFilterRecordType, family)
                ?? throw new InvalidOperationException("Failed to create a SizeFilterRecord for the selected family.");

            object widthContext = ResolvePartContext(partContextType, true)
                ?? throw new InvalidOperationException("Could not resolve a structure inner-width PartContextType for the selected family.");

            object lengthContext = ResolvePartContext(partContextType, false)
                ?? throw new InvalidOperationException("Could not resolve a structure inner-length PartContextType for the selected family.");

            MethodInfo getParamMethod = sizeFilterRecordType.GetMethod("GetParamByContextAndIndex", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException("SizeFilterRecord.GetParamByContextAndIndex(...) was not found.");

            object widthField = getParamMethod.Invoke(sizeFilterRecord, new[] { widthContext, (object)0 })
                ?? throw new InvalidOperationException("Could not access the width parameter field for the selected family.");

            object lengthField = getParamMethod.Invoke(sizeFilterRecord, new[] { lengthContext, (object)0 })
                ?? throw new InvalidOperationException("Could not access the length parameter field for the selected family.");

            double finalWidthInches = ResolveNearestAllowedValue(widthField, targetWidthInches);
            double finalLengthInches = ResolveNearestAllowedValue(lengthField, targetLengthInches);

            SetSizeFilterFieldValue(widthField, finalWidthInches);
            SetSizeFilterFieldValue(lengthField, finalLengthInches);

            ed.WriteMessage($"\nSD-JUNCTION-SIZE: requested W={targetWidthInches:0.##}\" L={targetLengthInches:0.##}\".");
            ed.WriteMessage($"\nSD-JUNCTION-SIZE: valid family size W={finalWidthInches:0.##}\" L={finalLengthInches:0.##}\".");

            ObjectId addedSizeId = TryAddPartSizeAndResolveId(tr, family, sizeFilterRecord, finalWidthInches, finalLengthInches, sizesBefore, ed);
            if (!addedSizeId.IsNull)
                return addedSizeId;

            List<PartSizeInfo> sizesAfter = GetFamilyPartSizes(tr, family);
            if (TryFindClosestExistingSize(sizesAfter, finalWidthInches, finalLengthInches, out PartSizeInfo afterMatch))
                return afterMatch.Id;

            throw new InvalidOperationException("The part size add attempt completed, but no matching size could be resolved in the family afterwards.");
        }

        private static ObjectId TryAddPartSizeAndResolveId(
            Transaction tr,
            PartFamily family,
            object sizeFilterRecord,
            double widthInches,
            double lengthInches,
            List<PartSizeInfo> sizesBefore,
            Editor ed)
        {
            MethodInfo addMethod = typeof(PartFamily).GetMethod("AddPartSize", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new InvalidOperationException("PartFamily.AddPartSize(...) was not found.");

            object? addResult = addMethod.Invoke(family, new[] { sizeFilterRecord });
            if (addResult is ObjectId addedId && !addedId.IsNull)
            {
                ed.WriteMessage("\nSD-JUNCTION-SIZE: AddPartSize returned a valid ObjectId.");
                return addedId;
            }

            List<PartSizeInfo> sizesAfter = GetFamilyPartSizes(tr, family);
            foreach (PartSizeInfo size in sizesAfter)
            {
                if (sizesBefore.Any(s => s.Id == size.Id))
                    continue;

                if (Math.Abs(size.WidthInches - widthInches) <= 0.01 &&
                    Math.Abs(size.LengthInches - lengthInches) <= 0.01)
                {
                    ed.WriteMessage("\nSD-JUNCTION-SIZE: AddPartSize created a new family entry detected by post-add scan.");
                    return size.Id;
                }
            }

            if (TryFindClosestExistingSize(sizesAfter, widthInches, lengthInches, out PartSizeInfo existingMatch))
            {
                ed.WriteMessage("\nSD-JUNCTION-SIZE: matching size was resolved after AddPartSize by family scan.");
                return existingMatch.Id;
            }

            return ObjectId.Null;
        }

        private static List<PartSizeInfo> GetFamilyPartSizes(Transaction tr, PartFamily family)
        {
            var result = new List<PartSizeInfo>();
            int partSizeCount = family.PartSizeCount;
            for (int i = 0; i < partSizeCount; i++)
            {
                ObjectId sizeId = family[i];
                if (sizeId.IsNull)
                    continue;

                string name = GetPartSizeName(tr, sizeId);
                if (TryParseLengthWidth(name, out double lengthInches, out double widthInches))
                {
                    result.Add(new PartSizeInfo(sizeId, name, widthInches, lengthInches));
                }
                else
                {
                    result.Add(new PartSizeInfo(sizeId, name, double.NaN, double.NaN));
                }
            }

            return result;
        }

        private static bool TryFindClosestExistingSize(List<PartSizeInfo> sizes, double targetWidthInches, double targetLengthInches, out PartSizeInfo best)
        {
            var exactOrParsed = sizes.Where(s => !double.IsNaN(s.WidthInches) && !double.IsNaN(s.LengthInches)).ToList();
            if (exactOrParsed.Count == 0)
            {
                best = default;
                return false;
            }

            best = exactOrParsed[0];
            double bestScore = GetSizeScore(best, targetWidthInches, targetLengthInches);

            foreach (PartSizeInfo size in exactOrParsed.Skip(1))
            {
                double score = GetSizeScore(size, targetWidthInches, targetLengthInches);
                if (score < bestScore)
                {
                    best = size;
                    bestScore = score;
                }
            }

            return Math.Abs(best.WidthInches - targetWidthInches) <= 0.01 &&
                   Math.Abs(best.LengthInches - targetLengthInches) <= 0.01;
        }

        private static double GetSizeScore(PartSizeInfo size, double targetWidthInches, double targetLengthInches)
            => Math.Abs(size.WidthInches - targetWidthInches) + Math.Abs(size.LengthInches - targetLengthInches);

        private static string GetPartSizeName(Transaction tr, ObjectId sizeId)
        {
            AcDbObject dbo = tr.GetObject(sizeId, OpenMode.ForRead, false);
            foreach (string propertyName in new[] { "Name", "DisplayName", "Description", "PartSizeName" })
            {
                PropertyInfo? pi = dbo.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                if (pi == null || pi.PropertyType != typeof(string))
                    continue;

                string? value = pi.GetValue(dbo) as string;
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return dbo.GetType().Name;
        }

        private static bool TryParseLengthWidth(string text, out double lengthInches, out double widthInches)
        {
            lengthInches = 0.0;
            widthInches = 0.0;

            Match m = Regex.Match(
                text,
                @"L\s*=\s*(?<len>[0-9]+(?:\.[0-9]+)?)\s*''\s*x\s*W\s*=\s*(?<wid>[0-9]+(?:\.[0-9]+)?)\s*''",
                RegexOptions.IgnoreCase);

            if (!m.Success)
                return false;

            if (!double.TryParse(m.Groups["len"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out lengthInches))
                return false;

            if (!double.TryParse(m.Groups["wid"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out widthInches))
                return false;

            return true;
        }

        private static Type? FindTypeByName(ReflectionAssembly asm, string typeName)
            => asm.GetTypes().FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.Ordinal));

        private static object? ResolvePartContext(Type enumType, bool isWidth)
        {
            string[] preferred = isWidth
                ? new[]
                {
                    "StructInnerWidth",
                    "StructInnerDiameterOrWidth",
                    "StructDiameterOrWidth",
                    "SIW"
                }
                : new[]
                {
                    "StructInnerLength",
                    "StructLength",
                    "SIL"
                };

            foreach (string name in preferred)
            {
                if (Enum.GetNames(enumType).Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                    return Enum.Parse(enumType, name, ignoreCase: true);
            }

            IEnumerable<string> fallbackNames = Enum.GetNames(enumType)
                .Where(n => isWidth
                    ? n.IndexOf("Width", StringComparison.OrdinalIgnoreCase) >= 0 &&
                      (n.IndexOf("Inner", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Diameter", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Struct", StringComparison.OrdinalIgnoreCase) >= 0)
                    : n.IndexOf("Length", StringComparison.OrdinalIgnoreCase) >= 0 &&
                      (n.IndexOf("Inner", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Struct", StringComparison.OrdinalIgnoreCase) >= 0));

            string? fallback = fallbackNames.FirstOrDefault();
            return fallback == null ? null : Enum.Parse(enumType, fallback, ignoreCase: true);
        }

        private static double ResolveNearestAllowedValue(object sizeFilterField, double targetValue)
        {
            object? valueList = GetPropertyValue(sizeFilterField, "ValueList");
            if (valueList == null)
                return targetValue;

            List<double> allowed = GetCandidateNumericValues(valueList);
            if (allowed.Count == 0)
            {
                MethodInfo? isValid = valueList.GetType().GetMethod("IsValidValue", BindingFlags.Instance | BindingFlags.Public);
                if (isValid != null)
                {
                    object? valid = isValid.Invoke(valueList, new object[] { targetValue });
                    if (valid is bool b && b)
                        return targetValue;
                }

                return targetValue;
            }

            return allowed.OrderBy(v => Math.Abs(v - targetValue)).First();
        }

        private static List<double> GetCandidateNumericValues(object valueList)
        {
            var values = new List<double>();

            if (valueList is IEnumerable enumerable)
            {
                foreach (object? item in enumerable)
                {
                    if (TryConvertToDouble(item, out double d))
                        values.Add(d);
                    else if (item != null)
                    {
                        object? val = GetPropertyValue(item, "Value") ?? GetPropertyValue(item, "DataValue");
                        if (TryConvertToDouble(val, out d))
                            values.Add(d);
                    }
                }
            }

            if (values.Count > 0)
                return values.Distinct().OrderBy(v => v).ToList();

            object? countObj = GetPropertyValue(valueList, "Count");
            if (TryConvertToInt32(countObj, out int count) && count > 0)
            {
                PropertyInfo? itemPi = valueList.GetType().GetProperty("Item", BindingFlags.Instance | BindingFlags.Public);
                if (itemPi != null)
                {
                    for (int i = 0; i < count; i++)
                    {
                        object? item = itemPi.GetValue(valueList, new object[] { i });
                        if (TryConvertToDouble(item, out double d))
                            values.Add(d);
                        else if (item != null)
                        {
                            object? val = GetPropertyValue(item, "Value") ?? GetPropertyValue(item, "DataValue");
                            if (TryConvertToDouble(val, out d))
                                values.Add(d);
                        }
                    }
                }
            }

            return values.Distinct().OrderBy(v => v).ToList();
        }

        private static void SetSizeFilterFieldValue(object sizeFilterField, double value)
        {
            PropertyInfo? valuePi = sizeFilterField.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
            if (valuePi == null || !valuePi.CanWrite)
                throw new InvalidOperationException("The size filter field does not expose a writable Value property.");

            object converted = Convert.ChangeType(value, valuePi.PropertyType, CultureInfo.InvariantCulture);
            valuePi.SetValue(sizeFilterField, converted);
        }

        private static bool TrySwapStructureToSize(Structure structure, ObjectId familyId, ObjectId sizeId, Editor ed)
        {
            MethodInfo? exact = structure.GetType().GetMethod(
                "SwapPartFamilyAndSize",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(ObjectId), typeof(ObjectId) },
                modifiers: null);

            if (exact != null)
            {
                exact.Invoke(structure, new object[] { familyId, sizeId });
                return true;
            }

            IEnumerable<MethodInfo> candidates = structure.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(m => m.Name.IndexOf("Swap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            (m.Name.IndexOf("Part", StringComparison.OrdinalIgnoreCase) >= 0 &&
                             m.Name.IndexOf("Size", StringComparison.OrdinalIgnoreCase) >= 0));

            foreach (MethodInfo mi in candidates)
            {
                ParameterInfo[] pars = mi.GetParameters();
                try
                {
                    if (pars.Length == 2 && pars[0].ParameterType == typeof(ObjectId) && pars[1].ParameterType == typeof(ObjectId))
                    {
                        mi.Invoke(structure, new object[] { familyId, sizeId });
                        ed.WriteMessage($"\nSD-JUNCTION-SIZE: swap used method {mi.Name}(ObjectId,ObjectId).");
                        return true;
                    }

                    if (pars.Length == 1 && pars[0].ParameterType == typeof(ObjectId))
                    {
                        mi.Invoke(structure, new object[] { sizeId });
                        ed.WriteMessage($"\nSD-JUNCTION-SIZE: swap used method {mi.Name}(ObjectId).");
                        return true;
                    }
                }
                catch
                {
                    // keep probing candidate methods
                }
            }

            return false;
        }


        private static bool TryApplyStyleToPartSizeDefinition(Transaction tr, ObjectId sizeId, ObjectId styleId, string styleName, Editor ed)
        {
            if (sizeId.IsNull)
                return false;

            try
            {
                AcDbObject dbo = tr.GetObject(sizeId, OpenMode.ForWrite, false);
                if (TryAssignStyleOnObject(dbo, styleId, styleName))
                {
                    ed.WriteMessage("\nSD-JUNCTION-SIZE: matched part size accepted the source structure style.");
                    return true;
                }

                object? partData = GetPropertyValue(dbo, "PartData");
                if (partData != null && TryAssignStyleOnObject(partData, styleId, styleName))
                {
                    ed.WriteMessage("\nSD-JUNCTION-SIZE: matched part size PartData accepted the source structure style.");
                    return true;
                }
            }
            catch
            {
                // fall back to applying the style on the placed structure after swap
            }

            return false;
        }

        private static bool TryRestoreStructureStyle(Structure structure, ObjectId styleId, string styleName, Editor ed)
        {
            try
            {
                if (TryAssignStyleOnObject(structure, styleId, styleName))
                    return true;

                if (!string.IsNullOrWhiteSpace(styleName))
                {
                    PropertyInfo? styleNamePi = structure.GetType().GetProperty("StyleName", BindingFlags.Instance | BindingFlags.Public);
                    if (styleNamePi != null && styleNamePi.CanWrite)
                    {
                        styleNamePi.SetValue(structure, styleName);
                        ed.WriteMessage("\nSD-JUNCTION-SIZE: reapplied source structure StyleName after swap.");
                        return true;
                    }
                }
            }
            catch
            {
                // last resort failed
            }

            return false;
        }

        private static bool TryAssignStyleOnObject(object obj, ObjectId styleId, string styleName)
        {
            if (!styleId.IsNull)
            {
                foreach (string propName in new[] { "StyleId", "PartStyleId", "ModelStyleId", "PlanStyleId" })
                {
                    PropertyInfo? pi = obj.GetType().GetProperty(propName, BindingFlags.Instance | BindingFlags.Public);
                    if (pi != null && pi.CanWrite && pi.PropertyType == typeof(ObjectId))
                    {
                        pi.SetValue(obj, styleId);
                        return true;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(styleName))
            {
                foreach (string propName in new[] { "StyleName", "PartStyleName", "ModelStyleName", "PlanStyleName" })
                {
                    PropertyInfo? pi = obj.GetType().GetProperty(propName, BindingFlags.Instance | BindingFlags.Public);
                    if (pi != null && pi.CanWrite && pi.PropertyType == typeof(string))
                    {
                        pi.SetValue(obj, styleName);
                        return true;
                    }
                }
            }

            return false;
        }

        private static ObjectId GetObjectIdProperty(object obj, string propertyName)
        {
            PropertyInfo? pi = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            object? value = pi?.GetValue(obj);
            return value is ObjectId id ? id : ObjectId.Null;
        }

        private static object? GetPropertyValue(object obj, string propertyName)
        {
            PropertyInfo? pi = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            return pi?.GetValue(obj);
        }

        private static bool TryConvertToDouble(object? value, out double result)
        {
            switch (value)
            {
                case null:
                    result = 0.0;
                    return false;
                case double d:
                    result = d;
                    return true;
                case float f:
                    result = f;
                    return true;
                case int i:
                    result = i;
                    return true;
                case long l:
                    result = l;
                    return true;
                case decimal m:
                    result = (double)m;
                    return true;
                default:
                    try
                    {
                        result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                        return true;
                    }
                    catch
                    {
                        result = 0.0;
                        return false;
                    }
            }
        }

        private static bool TryConvertToInt32(object? value, out int result)
        {
            try
            {
                if (value == null)
                {
                    result = 0;
                    return false;
                }

                result = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                result = 0;
                return false;
            }
        }

        private readonly record struct PartSizeInfo(ObjectId Id, string Name, double WidthInches, double LengthInches);
    }
}
