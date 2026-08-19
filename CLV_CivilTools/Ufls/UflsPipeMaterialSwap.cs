using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using ReflectionAssembly = System.Reflection.Assembly;
using System.Text.RegularExpressions;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDbObject = Autodesk.AutoCAD.DatabaseServices.DBObject;

namespace CLV_CivilTools.Ufls
{
    /// <summary>
    /// Pipe material swap helpers for sewer-main editing.
    /// Swaps the selected Civil 3D pipe to a target part family while preserving nominal size.
    /// Families used by this workflow:
    /// - CLV_PVC
    /// - CLV_C900
    /// - CLV_RCP
    /// </summary>
    public static class UflsPipeMaterialSwapCommands
    {
        private const string FamilyPvc = "CLV_PVC";
        private const string FamilyC900 = "CLV_C900";
        private const string FamilyRcp = "CLV_RCP";

        [CommandMethod("UFLS", "UFLS-PIPE-PVC-C900", CommandFlags.Modal)]
        public static void SwapPvcToC900() => RunPipeMaterialSwap(FamilyPvc, FamilyC900, "PVC --> C900");

        [CommandMethod("UFLS", "UFLS-PIPE-RCP-C900", CommandFlags.Modal)]
        public static void SwapRcpToC900() => RunPipeMaterialSwap(FamilyRcp, FamilyC900, "RCP --> C900");

        [CommandMethod("UFLS", "UFLS-PIPE-C900-RCP", CommandFlags.Modal)]
        public static void SwapC900ToRcp() => RunPipeMaterialSwap(FamilyC900, FamilyRcp, "C900 --> RCP");

        [CommandMethod("UFLS", "UFLS-PIPE-C900-PVC", CommandFlags.Modal)]
        public static void SwapC900ToPvc() => RunPipeMaterialSwap(FamilyC900, FamilyPvc, "C900 --> PVC");

        private static void RunPipeMaterialSwap(string expectedSourceFamily, string targetFamily, string commandLabel)
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                PromptEntityOptions peo = new PromptEntityOptions($"\n{commandLabel} - select pipe: ");
                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                    return;

                using Transaction tr = db.TransactionManager.StartTransaction();

                if (tr.GetObject(per.ObjectId, OpenMode.ForWrite, false) is not Pipe pipe)
                    throw new InvalidOperationException("Selected object is not a Civil 3D pipe.");

                string currentFamilyName = GetPipeFamilyName(tr, pipe);
                string currentSizeName = GetPipeSizeName(tr, pipe);
                int nominalInches = ResolvePipeNominalInches(tr, pipe, currentFamilyName, currentSizeName);

                if (!FamilyMatches(currentFamilyName, expectedSourceFamily))
                {
                    throw new InvalidOperationException(
                        $"Selected pipe family '{currentFamilyName}' does not match expected source family '{expectedSourceFamily}'.");
                }

                PartFamily targetPartFamily = ResolvePipePartFamily(tr, pipe, targetFamily);
                ObjectId targetSizeId = EnsureMatchingPipePartSize(tr, targetPartFamily, nominalInches, ed);
                string targetSizeName = GetPartSizeName(tr, targetSizeId);

                if (!TrySwapPipeToSize(pipe, targetPartFamily.ObjectId, targetSizeId, ed))
                    throw new InvalidOperationException("No supported Civil 3D pipe swap method was found on the selected pipe object.");

                tr.Commit();
                ed.WriteMessage($"\n{commandLabel}: swapped {nominalInches}\" pipe from {currentFamilyName} / {currentSizeName} to {targetFamily} / {targetSizeName}.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n{commandLabel} error: {ex.Message}");
            }
        }

        private static bool FamilyMatches(string actualFamilyName, string expectedFamilyName)
        {
            if (string.IsNullOrWhiteSpace(actualFamilyName))
                return false;

            if (actualFamilyName.Equals(expectedFamilyName, StringComparison.OrdinalIgnoreCase))
                return true;

            return actualFamilyName.IndexOf(expectedFamilyName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetPipeFamilyName(Transaction tr, Pipe pipe)
        {
            ObjectId familyId = GetObjectIdProperty(pipe, "PartFamilyId");
            if (!familyId.IsNull && tr.GetObject(familyId, OpenMode.ForRead, false) is PartFamily family)
            {
                if (!string.IsNullOrWhiteSpace(family.Name))
                    return family.Name;
            }

            foreach (string propertyName in new[] { "PartFamilyName", "FamilyName", "PartSubType" })
            {
                string value = GetStringProperty(pipe, propertyName);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return string.Empty;
        }

        private static string GetPipeSizeName(Transaction tr, Pipe pipe)
        {
            ObjectId sizeId = GetObjectIdProperty(pipe, "PartSizeId");
            if (!sizeId.IsNull)
                return GetPartSizeName(tr, sizeId);

            foreach (string propertyName in new[] { "PartSizeName", "SizeName", "Description" })
            {
                string value = GetStringProperty(pipe, propertyName);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return string.Empty;
        }

        private static int ResolvePipeNominalInches(Transaction tr, Pipe pipe, string familyName, string sizeName)
        {
            foreach (string propertyName in new[] { "NominalDiameter", "NominalDiameterOrWidth", "DiameterOrWidth", "InnerDiameter" })
            {
                if (TryGetDoubleProperty(pipe, propertyName, out double d) && d > 0.0)
                {
                    int rounded = (int)Math.Round(d, MidpointRounding.AwayFromZero);
                    if (rounded > 0)
                        return rounded;
                }
            }

            int fromSizeName = ParseNominalInches(sizeName);
            if (fromSizeName > 0)
                return fromSizeName;

            ObjectId sizeId = GetObjectIdProperty(pipe, "PartSizeId");
            if (!sizeId.IsNull)
            {
                string sizeIdName = GetPartSizeName(tr, sizeId);
                int parsed = ParseNominalInches(sizeIdName);
                if (parsed > 0)
                    return parsed;
            }

            throw new InvalidOperationException(
                $"Could not resolve nominal size from pipe family '{familyName}' and size '{sizeName}'.");
        }

        private static int ParseNominalInches(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            Match m = Regex.Match(text, @"(?<!\d)(\d{1,3})(?:\.0+)?\s*(?:""|INCH|IN|')", RegexOptions.IgnoreCase);
            if (!m.Success)
                m = Regex.Match(text, @"(?<!\d)(\d{1,3})(?:\.0+)?(?!\d)");

            if (!m.Success)
                return 0;

            return int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int inches)
                ? inches
                : 0;
        }

        private static PartFamily ResolvePipePartFamily(Transaction tr, Pipe pipe, string targetFamilyName)
        {
            ObjectId networkId = GetObjectIdProperty(pipe, "NetworkId");
            if (networkId.IsNull)
                throw new InvalidOperationException("Selected pipe does not have a valid NetworkId.");

            AcDbObject networkObj = tr.GetObject(networkId, OpenMode.ForRead, false);
            ObjectId partsListId = GetObjectIdProperty(networkObj, "PartsListId");
            if (partsListId.IsNull)
                throw new InvalidOperationException("Selected pipe's network does not have a valid PartsListId.");

            if (tr.GetObject(partsListId, OpenMode.ForRead, false) is not PartsList partsList)
                throw new InvalidOperationException("Could not open the network parts list.");

            ObjectIdCollection familyIds = partsList.GetPartFamilyIdsByDomain(DomainType.Pipe);
            foreach (ObjectId familyId in familyIds)
            {
                if (tr.GetObject(familyId, OpenMode.ForWrite, false) is not PartFamily family)
                    continue;

                string familyName = family.Name ?? string.Empty;
                if (FamilyMatches(familyName, targetFamilyName))
                    return family;
            }

            throw new InvalidOperationException(
                $"Could not find pipe family '{targetFamilyName}' in the selected pipe network parts list. Add that family to the assigned parts list and retry.");
        }

        private static ObjectId EnsureMatchingPipePartSize(Transaction tr, PartFamily family, int targetNominalInches, Editor ed)
        {
            List<PipePartSizeInfo> sizesBefore = GetPipeFamilyPartSizes(tr, family);
            if (TryFindExistingPipePartSize(sizesBefore, targetNominalInches, out PipePartSizeInfo existing))
            {
                ed.WriteMessage($"\nPIPE SWAP: found existing size '{existing.Name}'.");
                return existing.Id;
            }

            ObjectId addedSizeId = TryAddPipePartSizeByNominalDiameter(tr, family, targetNominalInches, ed);
            if (!addedSizeId.IsNull)
                return addedSizeId;

            List<PipePartSizeInfo> sizesAfter = GetPipeFamilyPartSizes(tr, family);
            if (TryFindExistingPipePartSize(sizesAfter, targetNominalInches, out PipePartSizeInfo added))
                return added.Id;

            throw new InvalidOperationException(
                $"Could not find or add nominal pipe size {targetNominalInches}\" in family '{family.Name}'.");
        }

        private static ObjectId TryAddPipePartSizeByNominalDiameter(Transaction tr, PartFamily family, int targetNominalInches, Editor ed)
        {
            try
            {
                ReflectionAssembly civilAsm = typeof(PartFamily).Assembly;
                Type? sizeFilterRecordType = civilAsm.GetTypes().FirstOrDefault(t => string.Equals(t.Name, "SizeFilterRecord", StringComparison.Ordinal));
                Type? partContextType = civilAsm.GetTypes().FirstOrDefault(t => string.Equals(t.Name, "PartContextType", StringComparison.Ordinal));
                if (sizeFilterRecordType == null || partContextType == null)
                    return ObjectId.Null;

                object? sizeFilterRecord = Activator.CreateInstance(sizeFilterRecordType, family);
                if (sizeFilterRecord == null)
                    return ObjectId.Null;

                object? diameterContext = ResolvePipeDiameterContext(partContextType);
                if (diameterContext == null)
                    return ObjectId.Null;

                MethodInfo? getParamMethod = sizeFilterRecordType.GetMethod("GetParamByContextAndIndex", BindingFlags.Instance | BindingFlags.Public);
                if (getParamMethod == null)
                    return ObjectId.Null;

                object? diameterField = getParamMethod.Invoke(sizeFilterRecord, new[] { diameterContext, (object)0 });
                if (diameterField == null)
                    return ObjectId.Null;

                double finalValue = ResolveNearestAllowedValue(diameterField, targetNominalInches);
                SetSizeFilterFieldValue(diameterField, finalValue);

                MethodInfo? addMethod = typeof(PartFamily).GetMethod("AddPartSize", BindingFlags.Instance | BindingFlags.Public);
                if (addMethod == null)
                    return ObjectId.Null;

                object? addResult = addMethod.Invoke(family, new[] { sizeFilterRecord });
                if (addResult is ObjectId newId && !newId.IsNull)
                {
                    ed.WriteMessage($"\nPIPE SWAP: AddPartSize created nominal {targetNominalInches}\" in family '{family.Name}'.");
                    return newId;
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPIPE SWAP: AddPartSize probe for family '{family.Name}' did not complete: {ex.Message}");
            }

            return ObjectId.Null;
        }

        private static object? ResolvePipeDiameterContext(Type partContextType)
        {
            string[] preferred =
            {
                "PipeInnerDiameter",
                "InnerDiameter",
                "PipeNominalDiameter",
                "NominalDiameter",
                "DiameterOrWidth",
                "PipeDiameter"
            };

            foreach (string name in preferred)
            {
                foreach (object value in Enum.GetValues(partContextType))
                {
                    if (string.Equals(value.ToString(), name, StringComparison.OrdinalIgnoreCase))
                        return value;
                }
            }

            foreach (object value in Enum.GetValues(partContextType))
            {
                string text = value.ToString() ?? string.Empty;
                if (text.IndexOf("Diameter", StringComparison.OrdinalIgnoreCase) >= 0)
                    return value;
            }

            return null;
        }

        private static double ResolveNearestAllowedValue(object sizeFilterField, double targetValue)
        {
            List<double> values = ExtractAllowedValues(sizeFilterField);
            if (values.Count == 0)
                return targetValue;

            double best = values[0];
            double bestDiff = Math.Abs(best - targetValue);
            for (int i = 1; i < values.Count; i++)
            {
                double diff = Math.Abs(values[i] - targetValue);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    best = values[i];
                }
            }

            return best;
        }

        private static List<double> ExtractAllowedValues(object sizeFilterField)
        {
            var values = new List<double>();
            foreach (string propName in new[] { "ValidValues", "ValueList", "AllowedValues", "List" })
            {
                PropertyInfo? pi = sizeFilterField.GetType().GetProperty(propName, BindingFlags.Instance | BindingFlags.Public);
                if (pi == null)
                    continue;

                object? raw = pi.GetValue(sizeFilterField);
                if (raw == null)
                    continue;

                if (raw is System.Collections.IEnumerable enumerable)
                {
                    foreach (object? item in enumerable)
                    {
                        if (TryConvertToDouble(item, out double d))
                        {
                            values.Add(d);
                            continue;
                        }

                        object? valueObj = GetPropertyValue(item, "Value") ?? GetPropertyValue(item, "DataValue");
                        if (TryConvertToDouble(valueObj, out d))
                            values.Add(d);
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

        private static List<PipePartSizeInfo> GetPipeFamilyPartSizes(Transaction tr, PartFamily family)
        {
            var result = new List<PipePartSizeInfo>();
            for (int i = 0; i < family.PartSizeCount; i++)
            {
                ObjectId sizeId = family[i];
                if (sizeId.IsNull)
                    continue;

                string sizeName = GetPartSizeName(tr, sizeId);
                int nominal = ParseNominalInches(sizeName);
                result.Add(new PipePartSizeInfo(sizeId, sizeName, nominal));
            }

            return result;
        }

        private static bool TryFindExistingPipePartSize(List<PipePartSizeInfo> sizes, int targetNominalInches, out PipePartSizeInfo best)
        {
            PipePartSizeInfo[] exact = sizes.Where(s => s.NominalInches == targetNominalInches).ToArray();
            if (exact.Length > 0)
            {
                best = exact[0];
                return true;
            }

            PipePartSizeInfo[] parsed = sizes.Where(s => s.NominalInches > 0).ToArray();
            if (parsed.Length == 0)
            {
                best = default;
                return false;
            }

            best = parsed[0];
            int bestDiff = Math.Abs(best.NominalInches - targetNominalInches);
            for (int i = 1; i < parsed.Length; i++)
            {
                int diff = Math.Abs(parsed[i].NominalInches - targetNominalInches);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    best = parsed[i];
                }
            }

            return best.NominalInches == targetNominalInches;
        }

        private static string GetPartSizeName(Transaction tr, ObjectId sizeId)
        {
            AcDbObject? sizeObj = tr.GetObject(sizeId, OpenMode.ForRead, false) as AcDbObject;
            if (sizeObj == null)
                return string.Empty;

            foreach (string propertyName in new[] { "Name", "DisplayName", "Description", "PartSizeName" })
            {
                PropertyInfo? pi = sizeObj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                if (pi == null || pi.PropertyType != typeof(string))
                    continue;

                string? value = pi.GetValue(sizeObj) as string;
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return sizeObj.GetType().Name;
        }

        private static bool TrySwapPipeToSize(Pipe pipe, ObjectId familyId, ObjectId sizeId, Editor ed)
        {
            MethodInfo? exact = pipe.GetType().GetMethod(
                "SwapPartFamilyAndSize",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(ObjectId), typeof(ObjectId) },
                modifiers: null);

            if (exact != null)
            {
                exact.Invoke(pipe, new object[] { familyId, sizeId });
                return true;
            }

            IEnumerable<MethodInfo> candidates = pipe.GetType()
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
                        mi.Invoke(pipe, new object[] { familyId, sizeId });
                        ed.WriteMessage($"\nPIPE SWAP: swap used method {mi.Name}(ObjectId,ObjectId).");
                        return true;
                    }

                    if (pars.Length == 1 && pars[0].ParameterType == typeof(ObjectId))
                    {
                        mi.Invoke(pipe, new object[] { sizeId });
                        ed.WriteMessage($"\nPIPE SWAP: swap used method {mi.Name}(ObjectId).");
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

        private static bool TryGetDoubleProperty(object obj, string propertyName, out double value)
        {
            value = 0.0;
            object? raw = GetPropertyValue(obj, propertyName);
            return TryConvertToDouble(raw, out value);
        }

        private static bool TryConvertToDouble(object? raw, out double value)
        {
            try
            {
                if (raw == null)
                {
                    value = 0.0;
                    return false;
                }

                value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                value = 0.0;
                return false;
            }
        }

        private static string GetStringProperty(object obj, string propertyName)
        {
            object? value = GetPropertyValue(obj, propertyName);
            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static ObjectId GetObjectIdProperty(object obj, string propertyName)
        {
            object? value = GetPropertyValue(obj, propertyName);
            return value is ObjectId id ? id : ObjectId.Null;
        }

        private static object? GetPropertyValue(object? obj, string propertyName)
        {
            if (obj == null)
                return null;

            PropertyInfo? pi = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            return pi?.GetValue(obj);
        }

        private readonly record struct PipePartSizeInfo(ObjectId Id, string Name, int NominalInches);
    }
}
