using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.Civil.DatabaseServices.Styles;

namespace CLV_CivilTools.Ufls
{
    /// <summary>
    /// Shared structure-size service using the same reflection-based Civil 3D mechanism
    /// proven by the Q1 RESIZE JUNCTION / SD-JUNCTION-SIZE workflow.
    /// </summary>
    internal static class UflsStructurePartSizeService
    {
        internal static ObjectId EnsureMatchingBoxSize(
            Transaction tr,
            PartFamily family,
            double targetWidthInches,
            double targetLengthInches,
            double targetWallInches,
            Editor ed)
        {
            targetWidthInches = Math.Round(targetWidthInches, MidpointRounding.AwayFromZero);
            targetLengthInches = Math.Round(targetLengthInches, MidpointRounding.AwayFromZero);
            targetWallInches = targetWallInches > 0.0
                ? Math.Round(targetWallInches, MidpointRounding.AwayFromZero)
                : 0.0;

            List<PartSizeInfo> sizesBefore = GetFamilyPartSizes(tr, family);
            if (TryFindExactSize(sizesBefore, targetWidthInches, targetLengthInches, targetWallInches, out PartSizeInfo existing))
                return existing.Id;

            Assembly civilAsm = typeof(PartFamily).Assembly;
            Type sizeFilterRecordType = FindTypeByName(civilAsm, "SizeFilterRecord")
                ?? throw new InvalidOperationException("Could not locate SizeFilterRecord in the Civil 3D API assembly.");
            Type partContextType = FindTypeByName(civilAsm, "PartContextType")
                ?? throw new InvalidOperationException("Could not locate PartContextType in the Civil 3D API assembly.");

            object sizeFilterRecord = Activator.CreateInstance(sizeFilterRecordType, family)
                ?? throw new InvalidOperationException("Failed to create a SizeFilterRecord for the selected family.");

            try
            {
                object widthContext = ResolvePartContext(partContextType, true)
                    ?? throw new InvalidOperationException("Could not resolve a structure width context for the selected family.");
                object lengthContext = ResolvePartContext(partContextType, false)
                    ?? throw new InvalidOperationException("Could not resolve a structure length context for the selected family.");

                MethodInfo getParam = sizeFilterRecordType.GetMethod("GetParamByContextAndIndex", BindingFlags.Instance | BindingFlags.Public)
                    ?? throw new InvalidOperationException("SizeFilterRecord.GetParamByContextAndIndex(...) was not found.");

                object widthField = getParam.Invoke(sizeFilterRecord, new[] { widthContext, (object)0 })
                    ?? throw new InvalidOperationException("Could not access the width parameter field for the selected family.");
                object lengthField = getParam.Invoke(sizeFilterRecord, new[] { lengthContext, (object)0 })
                    ?? throw new InvalidOperationException("Could not access the length parameter field for the selected family.");

                double finalWidth = ResolveNearestAllowedValue(widthField, targetWidthInches);
                double finalLength = ResolveNearestAllowedValue(lengthField, targetLengthInches);

                if (Math.Abs(finalWidth - targetWidthInches) > 0.01 || Math.Abs(finalLength - targetLengthInches) > 0.01)
                {
                    throw new InvalidOperationException(
                        $"The selected family does not allow the exact legacy size W={targetWidthInches:0.##}\" L={targetLengthInches:0.##}\". " +
                        $"Nearest allowed size is W={finalWidth:0.##}\" L={finalLength:0.##}\".");
                }

                SetSizeFilterFieldValue(widthField, finalWidth);
                SetSizeFilterFieldValue(lengthField, finalLength);

                double finalWall = targetWallInches;
                if (targetWallInches > 0.0)
                {
                    object? wallContext = ResolveNamedContext(partContextType, "WallThickness");
                    if (wallContext == null)
                        throw new InvalidOperationException("Could not resolve the WallThickness context for the selected family.");

                    object wallField = getParam.Invoke(sizeFilterRecord, new[] { wallContext, (object)0 })
                        ?? throw new InvalidOperationException("Could not access the wall-thickness field for the selected family.");

                    finalWall = ResolveNearestAllowedValue(wallField, targetWallInches);
                    if (Math.Abs(finalWall - targetWallInches) > 0.01)
                    {
                        throw new InvalidOperationException(
                            $"The selected family does not allow the legacy wall thickness {targetWallInches:0.##}\". " +
                            $"Nearest allowed wall thickness is {finalWall:0.##}\".");
                    }

                    SetSizeFilterFieldValue(wallField, finalWall);
                }

                ed.WriteMessage(
                    $"\nPIPE CATALOG MIGRATION: adding target size W={finalWidth:0.##}\" L={finalLength:0.##}\"" +
                    (finalWall > 0.0 ? $" WALL={finalWall:0.##}\"" : string.Empty) +
                    $" to '{family.Name}'.");

                MethodInfo addMethod = typeof(PartFamily).GetMethod("AddPartSize", BindingFlags.Instance | BindingFlags.Public)
                    ?? throw new InvalidOperationException("PartFamily.AddPartSize(...) was not found.");
                object? addResult = addMethod.Invoke(family, new[] { sizeFilterRecord });
                if (addResult is ObjectId addedId && !addedId.IsNull)
                    return addedId;

                List<PartSizeInfo> sizesAfter = GetFamilyPartSizes(tr, family);
                foreach (PartSizeInfo size in sizesAfter)
                {
                    if (!sizesBefore.Any(s => s.Id == size.Id) && SizeMatches(size, targetWidthInches, targetLengthInches, targetWallInches))
                        return size.Id;
                }

                if (TryFindExactSize(sizesAfter, targetWidthInches, targetLengthInches, targetWallInches, out PartSizeInfo afterMatch))
                    return afterMatch.Id;

                throw new InvalidOperationException("AddPartSize completed but a matching size could not be resolved afterwards.");
            }
            finally
            {
                if (sizeFilterRecord is IDisposable disposable)
                    disposable.Dispose();
            }
        }

        // Backward-compatible overload for callers that do not care about wall thickness.
        internal static ObjectId EnsureMatchingBoxSize(
            Transaction tr,
            PartFamily family,
            double targetWidthInches,
            double targetLengthInches,
            Editor ed)
            => EnsureMatchingBoxSize(tr, family, targetWidthInches, targetLengthInches, 0.0, ed);

        private static List<PartSizeInfo> GetFamilyPartSizes(Transaction tr, PartFamily family)
        {
            var result = new List<PartSizeInfo>();
            for (int i = 0; i < family.PartSizeCount; i++)
            {
                ObjectId sizeId = family[i];
                if (sizeId.IsNull) continue;
                string name = GetPartSizeName(tr, sizeId);

                if (TryParseLengthWidth(name, out double length, out double width))
                {
                    double wall = TryParseWall(name, out double parsedWall) ? parsedWall : double.NaN;
                    result.Add(new PartSizeInfo(sizeId, name, width, length, wall));
                }
                else
                {
                    result.Add(new PartSizeInfo(sizeId, name, double.NaN, double.NaN, double.NaN));
                }
            }
            return result;
        }

        private static bool TryFindExactSize(
            IEnumerable<PartSizeInfo> sizes,
            double width,
            double length,
            double wall,
            out PartSizeInfo best)
        {
            foreach (PartSizeInfo size in sizes)
            {
                if (SizeMatches(size, width, length, wall))
                {
                    best = size;
                    return true;
                }
            }
            best = default;
            return false;
        }

        private static bool SizeMatches(PartSizeInfo size, double width, double length, double wall)
        {
            if (double.IsNaN(size.WidthInches) || double.IsNaN(size.LengthInches)) return false;

            bool horizontalMatch =
                (Math.Abs(size.WidthInches - width) <= 0.01 && Math.Abs(size.LengthInches - length) <= 0.01) ||
                (Math.Abs(size.WidthInches - length) <= 0.01 && Math.Abs(size.LengthInches - width) <= 0.01);

            if (!horizontalMatch) return false;
            if (wall <= 0.0) return true;

            // If an existing size name exposes a wall value, require it to match.
            // Unknown wall values are not accepted as an exact wall-preserving match.
            return !double.IsNaN(size.WallInches) && Math.Abs(size.WallInches - wall) <= 0.01;
        }

        private static string GetPartSizeName(Transaction tr, ObjectId sizeId)
        {
            DBObject dbo = tr.GetObject(sizeId, OpenMode.ForRead, false);
            foreach (string propertyName in new[] { "Name", "DisplayName", "Description", "PartSizeName" })
            {
                PropertyInfo? pi = dbo.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                if (pi?.PropertyType != typeof(string)) continue;
                if (pi.GetValue(dbo) is string value && !string.IsNullOrWhiteSpace(value)) return value;
            }
            return dbo.GetType().Name;
        }

        private static bool TryParseLengthWidth(string text, out double lengthInches, out double widthInches)
        {
            lengthInches = 0.0;
            widthInches = 0.0;
            var m = System.Text.RegularExpressions.Regex.Match(
                text,
                @"L\s*=\s*(?<len>[0-9]+(?:\.[0-9]+)?)\s*''\s*x\s*W\s*=\s*(?<wid>[0-9]+(?:\.[0-9]+)?)\s*''",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return m.Success &&
                   double.TryParse(m.Groups["len"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out lengthInches) &&
                   double.TryParse(m.Groups["wid"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out widthInches);
        }

        private static bool TryParseWall(string text, out double wallInches)
        {
            wallInches = 0.0;
            var m = System.Text.RegularExpressions.Regex.Match(
                text,
                @"WALL(?:S)?\s*=\s*(?<wall>[0-9]+(?:\.[0-9]+)?)\s*(?:''|\"|INCH(?:ES)?)?",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return m.Success &&
                   double.TryParse(m.Groups["wall"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out wallInches);
        }

        private static Type? FindTypeByName(Assembly asm, string typeName)
            => asm.GetTypes().FirstOrDefault(t => string.Equals(t.Name, typeName, StringComparison.Ordinal));

        private static object? ResolvePartContext(Type enumType, bool isWidth)
        {
            string[] preferred = isWidth
                ? new[] { "StructInnerWidth", "StructInnerDiameterOrWidth", "StructDiameterOrWidth", "SIW" }
                : new[] { "StructInnerLength", "StructLength", "SIL" };
            foreach (string name in preferred)
            {
                if (Enum.GetNames(enumType).Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                    return Enum.Parse(enumType, name, true);
            }
            string? fallback = Enum.GetNames(enumType).FirstOrDefault(n => isWidth
                ? n.Contains("Width", StringComparison.OrdinalIgnoreCase) && (n.Contains("Inner", StringComparison.OrdinalIgnoreCase) || n.Contains("Struct", StringComparison.OrdinalIgnoreCase))
                : n.Contains("Length", StringComparison.OrdinalIgnoreCase) && (n.Contains("Inner", StringComparison.OrdinalIgnoreCase) || n.Contains("Struct", StringComparison.OrdinalIgnoreCase)));
            return fallback == null ? null : Enum.Parse(enumType, fallback, true);
        }

        private static object? ResolveNamedContext(Type enumType, string name)
        {
            string? match = Enum.GetNames(enumType)
                .FirstOrDefault(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
            return match == null ? null : Enum.Parse(enumType, match, true);
        }

        private static double ResolveNearestAllowedValue(object field, double target)
        {
            object? valueList = GetPropertyValue(field, "ValueList");
            if (valueList == null) return target;
            List<double> allowed = GetCandidateNumericValues(valueList);
            return allowed.Count == 0 ? target : allowed.OrderBy(v => Math.Abs(v - target)).First();
        }

        private static List<double> GetCandidateNumericValues(object valueList)
        {
            var values = new List<double>();
            if (valueList is IEnumerable enumerable)
            {
                foreach (object? item in enumerable)
                {
                    if (TryConvertToDouble(item, out double d)) values.Add(d);
                    else if (item != null && TryConvertToDouble(GetPropertyValue(item, "Value") ?? GetPropertyValue(item, "DataValue"), out d)) values.Add(d);
                }
            }
            return values.Distinct().OrderBy(v => v).ToList();
        }

        private static void SetSizeFilterFieldValue(object field, double value)
        {
            PropertyInfo? pi = field.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
            if (pi == null || !pi.CanWrite)
                throw new InvalidOperationException("The size filter field does not expose a writable Value property.");
            pi.SetValue(field, Convert.ChangeType(value, pi.PropertyType, CultureInfo.InvariantCulture));
        }

        private static object? GetPropertyValue(object obj, string propertyName)
            => obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(obj);

        private static bool TryConvertToDouble(object? value, out double result)
        {
            try
            {
                if (value == null) throw new InvalidCastException();
                result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                result = 0.0;
                return false;
            }
        }

        private readonly record struct PartSizeInfo(
            ObjectId Id,
            string Name,
            double WidthInches,
            double LengthInches,
            double WallInches);
    }
}
