using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilCogoPoint = Autodesk.Civil.DatabaseServices.CogoPoint;

namespace CLV_CivilTools.Ufls
{
    /// <summary>
    /// Places a circular access structure at the UFLS_MH_MARK location.
    /// The selected Type 2 box structure supplies the pipe network and, when
    /// requested, the rim elevation and source name. No pipes are created.
    /// </summary>
    public static class UflsPlaceAccessManholeCommands
    {
        private const string MarkerBlockName = "UFLS_MH_MARK";
        private const string AccessFamilyText = "ACCESS STRUCTURE";

        private sealed class AccessPartChoice
        {
            public ObjectId FamilyId { get; init; }
            public ObjectId SizeId { get; init; }
            public string FamilyName { get; init; } = string.Empty;
            public string SizeName { get; init; } = string.Empty;
            public string DisplayName { get; init; } = string.Empty;
        }

        [CommandMethod("UFLS-PLACE-ACCESS-MH", CommandFlags.Modal)]
        public static void PlaceAccessManhole()
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
                    Point3d markerLocation = PromptForManholeMarker(ed, tr);
                    if (markerLocation == Point3d.Origin && !PromptForMarkerSucceeded)
                        return;

                    Structure boxStructure = PromptForBoxStructure(ed, tr);
                    if (boxStructure == null)
                        return;

                    if (boxStructure.NetworkId.IsNull)
                        throw new InvalidOperationException("The selected box structure is not assigned to a pipe network.");

                    Network network = (Network)tr.GetObject(boxStructure.NetworkId, OpenMode.ForWrite);
                    if (network.PartsListId.IsNull)
                        throw new InvalidOperationException("The selected network does not have a Parts List.");

                    PartsList partsList = (PartsList)tr.GetObject(network.PartsListId, OpenMode.ForRead);
                    List<AccessPartChoice> choices = GetAccessPartChoices(tr, partsList);
                    if (choices.Count == 0)
                    {
                        throw new InvalidOperationException(
                            "No ACCESS STRUCTURE part sizes were found in the selected network's Parts List.");
                    }

                    AccessPartChoice? selectedPart = PromptForAccessPart(ed, choices);
                    if (selectedPart == null)
                        return;

                    double rimElevation = PromptForRimElevation(ed, tr, boxStructure);
                    if (!PromptForRimElevationSucceeded)
                        return;

                    ObjectId newStructureId = ObjectId.Null;
                    network.AddStructure(
                        selectedPart.FamilyId,
                        selectedPart.SizeId,
                        markerLocation,
                        0.0,
                        ref newStructureId,
                        false);

                    if (newStructureId.IsNull)
                        throw new InvalidOperationException("Civil 3D did not return the new access structure ObjectId.");

                    Structure accessStructure = (Structure)tr.GetObject(newStructureId, OpenMode.ForWrite);

                    accessStructure.AutomaticRimSurfaceAdjustment = false;
                    accessStructure.RimElevation = rimElevation;
                    accessStructure.ControlSumpBy = StructureControlSumpType.ByElevation;

                    string sourceName = boxStructure.Name ?? string.Empty;
                    string newName = RemoveJsSuffix(sourceName);
                    if (!string.IsNullOrWhiteSpace(newName))
                        accessStructure.Name = newName;

                    accessStructure.RecordGraphicsModified(true);
                    tr.Commit();

                    ed.WriteMessage($"\nPLACE ACCESS MANHOLE: Created '{accessStructure.Name}'.");
                    ed.WriteMessage($"\nPLACE ACCESS MANHOLE: Part = {selectedPart.DisplayName}.");
                    ed.WriteMessage($"\nPLACE ACCESS MANHOLE: Location = {markerLocation.X:0.###}, {markerLocation.Y:0.###}.");
                    ed.WriteMessage($"\nPLACE ACCESS MANHOLE: Rim elevation = {rimElevation:0.##}.");
                    ed.WriteMessage("\nPLACE ACCESS MANHOLE: Sump control = Elevation. No pipes created.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPLACE ACCESS MANHOLE error: {ex.Message}");
            }
        }

        private static bool PromptForMarkerSucceeded { get; set; }
        private static bool PromptForRimElevationSucceeded { get; set; }

        private static Point3d PromptForManholeMarker(Editor ed, Transaction tr)
        {
            PromptForMarkerSucceeded = false;

            PromptEntityOptions peo = new PromptEntityOptions(
                "\nSelect UFLS_MH_MARK for access manhole center: ");
            peo.SetRejectMessage("\nSelect the UFLS_MH_MARK block.");
            peo.AddAllowedClass(typeof(BlockReference), exactMatch: true);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
                return Point3d.Origin;

            BlockReference marker = (BlockReference)tr.GetObject(per.ObjectId, OpenMode.ForRead);
            string blockName = GetEffectiveBlockName(tr, marker);
            if (!string.Equals(blockName, MarkerBlockName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Selected block is '{blockName}', not {MarkerBlockName}.");

            PromptForMarkerSucceeded = true;
            Point3d p = marker.Position;
            return new Point3d(p.X, p.Y, p.Z);
        }

        private static string GetEffectiveBlockName(Transaction tr, BlockReference blockReference)
        {
            ObjectId definitionId = blockReference.IsDynamicBlock
                ? blockReference.DynamicBlockTableRecord
                : blockReference.BlockTableRecord;

            if (definitionId.IsNull)
                return string.Empty;

            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(definitionId, OpenMode.ForRead);
            return btr.Name ?? string.Empty;
        }

        private static Structure PromptForBoxStructure(Editor ed, Transaction tr)
        {
            PromptEntityOptions peo = new PromptEntityOptions(
                "\nSelect TYPE 2 BOX STRUCTURE (network/name/rim source): ");
            peo.SetRejectMessage("\nSelect a Civil 3D structure.");
            peo.AddAllowedClass(typeof(Structure), exactMatch: true);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
                return null!;

            Structure structure = (Structure)tr.GetObject(per.ObjectId, OpenMode.ForRead);
            return structure;
        }

        private static List<AccessPartChoice> GetAccessPartChoices(Transaction tr, PartsList partsList)
        {
            var choices = new List<AccessPartChoice>();
            ObjectIdCollection familyIds = partsList.GetPartFamilyIdsByDomain(DomainType.Structure);

            foreach (ObjectId familyId in familyIds)
            {
                PartFamily family = (PartFamily)tr.GetObject(familyId, OpenMode.ForRead);
                string familyName = family.Name ?? string.Empty;

                if (familyName.IndexOf(AccessFamilyText, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                for (int i = 0; i < family.PartSizeCount; i++)
                {
                    ObjectId sizeId = family[i];
                    if (sizeId.IsNull)
                        continue;

                    PartSize size = (PartSize)tr.GetObject(sizeId, OpenMode.ForRead);
                    string sizeName = size.Name ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(sizeName))
                        continue;

                    string barrel = TryGetBarrelDiameter(size);
                    string display = string.IsNullOrWhiteSpace(barrel)
                        ? $"{familyName} / {sizeName}"
                        : $"{barrel} BARREL / {familyName} / {sizeName}";

                    choices.Add(new AccessPartChoice
                    {
                        FamilyId = family.ObjectId,
                        SizeId = size.ObjectId,
                        FamilyName = familyName,
                        SizeName = sizeName,
                        DisplayName = display
                    });
                }
            }

            return choices
                .OrderBy(c => GetSortBarrelDiameter(c.DisplayName))
                .ThenBy(c => c.FamilyName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.SizeName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static AccessPartChoice? PromptForAccessPart(Editor ed, IReadOnlyList<AccessPartChoice> choices)
        {
            int maxOptions = Math.Min(choices.Count, 35);
            if (choices.Count > maxOptions)
            {
                ed.WriteMessage(
                    $"\nPLACE ACCESS MANHOLE: {choices.Count} access part sizes are available; showing the first {maxOptions}.");
            }

            PromptKeywordOptions pko = new PromptKeywordOptions("\nSelect ACCESS STRUCTURE part [");
            for (int i = 0; i < maxOptions; i++)
            {
                string token = MakeOptionToken(i);
                if (i > 0)
                    pko.Keywords.Add(token);
                else
                    pko.Keywords.Add(token);

                pko.AppendKeywordsToMessage = false;
            }

            pko.AppendKeywordsToMessage = false;
            pko.AllowNone = false;

            ed.WriteMessage("\n");
            for (int i = 0; i < maxOptions; i++)
                ed.WriteMessage($"  {MakeOptionToken(i)}={choices[i].DisplayName}\n");

            pko.Message = "\nSelect ACCESS STRUCTURE part: ";
            PromptResult pr = ed.GetKeywords(pko);
            if (pr.Status != PromptStatus.OK)
                return null;

            int selectedIndex = ParseOptionToken(pr.StringResult);
            if (selectedIndex < 0 || selectedIndex >= maxOptions)
                return null;

            return choices[selectedIndex];
        }

        private static string MakeOptionToken(int index)
        {
            if (index < 9)
                return (index + 1).ToString(CultureInfo.InvariantCulture);

            return ((char)('A' + (index - 9))).ToString();
        }

        private static int ParseOptionToken(string token)
        {
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int numeric))
                return numeric - 1;

            if (token.Length == 1 && token[0] >= 'A' && token[0] <= 'Z')
                return 9 + (token[0] - 'A');

            return -1;
        }

        private static double PromptForRimElevation(Editor ed, Transaction tr, Structure boxStructure)
        {
            PromptForRimElevationSucceeded = false;

            PromptKeywordOptions sourceOptions = new PromptKeywordOptions(
                "\nRIM ELEVATION SOURCE [BOX/USER/AEC]: ",
                "BOX USER AEC");
            sourceOptions.Keywords.Add("BOX");
            sourceOptions.Keywords.Add("USER");
            sourceOptions.Keywords.Add("AEC");
            sourceOptions.AllowNone = false;

            PromptResult sourceResult = ed.GetKeywords(sourceOptions);
            if (sourceResult.Status != PromptStatus.OK)
                return 0.0;

            double elevation;
            switch (sourceResult.StringResult.ToUpperInvariant())
            {
                case "BOX":
                    elevation = boxStructure.RimElevation;
                    break;

                case "USER":
                    PromptDoubleOptions pdo = new PromptDoubleOptions(
                        "\nEnter rim elevation: ");
                    pdo.AllowNegative = true;
                    pdo.AllowZero = true;

                    PromptDoubleResult pdr = ed.GetDouble(pdo);
                    if (pdr.Status != PromptStatus.OK)
                        return 0.0;

                    elevation = pdr.Value;
                    break;

                case "AEC":
                    PromptEntityOptions peo = new PromptEntityOptions(
                        "\nSelect AEC/COGO point for rim elevation: ");
                    peo.SetRejectMessage("\nSelect a Civil 3D COGO point.");
                    peo.AddAllowedClass(typeof(CivilCogoPoint), exactMatch: true);

                    PromptEntityResult per = ed.GetEntity(peo);
                    if (per.Status != PromptStatus.OK)
                        return 0.0;

                    CivilCogoPoint point = (CivilCogoPoint)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                    elevation = point.Elevation;
                    break;

                default:
                    return 0.0;
            }

            PromptForRimElevationSucceeded = true;
            return elevation;
        }

        private static string RemoveJsSuffix(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return name;

            return Regex.Replace(
                name.Trim(),
                @"-JS$",
                string.Empty,
                RegexOptions.IgnoreCase);
        }

        private static string TryGetBarrelDiameter(PartSize size)
        {
            try
            {
                PartDataField? field = size.SizeDataRecord.GetDataFieldBy(PartContextType.StructInnerDiameter);
                if (field?.Value == null)
                    field = size.SizeDataRecord.GetDataFieldBy(PartContextType.StructDiameter);

                if (field?.Value == null)
                    return string.Empty;

                if (!double.TryParse(
                        Convert.ToString(field.Value, CultureInfo.InvariantCulture),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double value))
                {
                    return string.Empty;
                }

                if (value > 0.0 && value < 10.0)
                    value *= 12.0;

                if (value < 40.0 || value > 100.0)
                    return string.Empty;

                return $"{value:0.#}\"";
            }
            catch
            {
                return string.Empty;
            }
        }

        private static double GetSortBarrelDiameter(string displayName)
        {
            Match match = Regex.Match(displayName, @"^(?<d>\d+(?:\.\d+)?)\s*\"");
            return match.Success && double.TryParse(match.Groups["d"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value
                : double.MaxValue;
        }
    }
}
