using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Ufls
{
    public static class UflsPlaceAccessManholeCommands
    {
        [CommandMethod("UFLS", "UFLS-PLACE-ACCESS-MH", CommandFlags.Modal)]
        public static void PlaceAccessManhole()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    PromptEntityOptions markPrompt = new PromptEntityOptions("\nSelect UFLS_MH_MARK: ");
                    markPrompt.SetRejectMessage("\nSelect a UFLS_MH_MARK block.");
                    markPrompt.AddAllowedClass(typeof(BlockReference), false);
                    PromptEntityResult markResult = ed.GetEntity(markPrompt);
                    if (markResult.Status != PromptStatus.OK) return;

                    BlockReference mark = (BlockReference)tr.GetObject(markResult.ObjectId, OpenMode.ForRead);
                    BlockTableRecord markDef = (BlockTableRecord)tr.GetObject(mark.BlockTableRecord, OpenMode.ForRead);
                    if (!string.Equals(markDef.Name, "UFLS_MH_MARK", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The selected block is not UFLS_MH_MARK.");
                    Point3d location = mark.Position;

                    PromptEntityOptions boxPrompt = new PromptEntityOptions("\nSelect Type 2 box structure: ");
                    boxPrompt.SetRejectMessage("\nSelect a Civil 3D structure.");
                    boxPrompt.AddAllowedClass(typeof(Structure), false);
                    PromptEntityResult boxResult = ed.GetEntity(boxPrompt);
                    if (boxResult.Status != PromptStatus.OK) return;

                    Structure box = (Structure)tr.GetObject(boxResult.ObjectId, OpenMode.ForRead);
                    if (box.NetworkId.IsNull)
                        throw new InvalidOperationException("The selected structure does not belong to a pipe network.");

                    Network network = (Network)tr.GetObject(box.NetworkId, OpenMode.ForRead);
                    ObjectId partsListId = GetObjectIdProperty(network, "PartsListId");
                    if (partsListId.IsNull)
                        throw new InvalidOperationException("The selected network does not have a Parts List.");

                    PartsList partsList = (PartsList)tr.GetObject(partsListId, OpenMode.ForRead);
                    ObjectIdCollection familyIds = partsList.GetPartFamilyIdsByDomain(DomainType.Structure);
                    var families = new List<PartFamily>();

                    foreach (ObjectId familyId in familyIds)
                    {
                        if (tr.GetObject(familyId, OpenMode.ForRead) is PartFamily family &&
                            family.Name.IndexOf("ACCESS STRUCTURE", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            families.Add(family);
                        }
                    }

                    if (families.Count == 0)
                        throw new InvalidOperationException("No ACCESS STRUCTURE part family was found in the selected network Parts List.");

                    var choices = new List<PartChoice>();
                    foreach (PartFamily family in families.OrderBy(f => f.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase))
                    {
                        for (int i = 0; i < family.PartSizeCount; i++)
                        {
                            ObjectId sizeId = family[i];
                            if (sizeId.IsNull) continue;
                            string sizeName = GetPartSizeName(tr, sizeId);
                            choices.Add(new PartChoice(family.ObjectId, sizeId, family.Name + " — " + sizeName));
                        }
                    }

                    if (choices.Count == 0)
                        throw new InvalidOperationException("No ACCESS STRUCTURE part sizes were found in the selected Parts List.");

                    ed.WriteMessage("\nAvailable ACCESS STRUCTURE parts:");
                    for (int i = 0; i < choices.Count; i++)
                        ed.WriteMessage($"\n  {i + 1}. {choices[i].DisplayName}");

                    PromptIntegerOptions choicePrompt = new PromptIntegerOptions($"\nSelect access structure [1-{choices.Count}] <1>: ")
                    {
                        AllowNone = true,
                        LowerLimit = 1,
                        UpperLimit = choices.Count
                    };
                    PromptIntegerResult choiceResult = ed.GetInteger(choicePrompt);
                    if (choiceResult.Status != PromptStatus.OK && choiceResult.Status != PromptStatus.None) return;
                    PartChoice choice = choices[choiceResult.Status == PromptStatus.None ? 0 : choiceResult.Value - 1];

                    PromptKeywordOptions rimPrompt = new PromptKeywordOptions("\nRim elevation source [BOX/User/AEC] <BOX>: ", "BOX User AEC")
                    {
                        AllowNone = true
                    };
                    PromptResult rimResult = ed.GetKeywords(rimPrompt);
                    if (rimResult.Status != PromptStatus.OK && rimResult.Status != PromptStatus.None) return;

                    string rimMode = rimResult.Status == PromptStatus.None ? "BOX" : rimResult.StringResult;
                    double rimElevation;
                    if (rimMode.Equals("BOX", StringComparison.OrdinalIgnoreCase))
                    {
                        rimElevation = box.RimElevation;
                    }
                    else if (rimMode.Equals("User", StringComparison.OrdinalIgnoreCase))
                    {
                        PromptDoubleResult elev = ed.GetDouble(new PromptDoubleOptions("\nEnter rim elevation: ") { AllowNegative = true });
                        if (elev.Status != PromptStatus.OK) return;
                        rimElevation = elev.Value;
                    }
                    else
                    {
                        PromptEntityOptions pointPrompt = new PromptEntityOptions("\nSelect COGO point: ");
                        pointPrompt.SetRejectMessage("\nSelect a COGO point.");
                        pointPrompt.AddAllowedClass(typeof(CogoPoint), false);
                        PromptEntityResult pointResult = ed.GetEntity(pointPrompt);
                        if (pointResult.Status != PromptStatus.OK) return;
                        CogoPoint point = (CogoPoint)tr.GetObject(pointResult.ObjectId, OpenMode.ForRead);
                        rimElevation = point.Elevation;
                    }

                    string newName = RemoveJsSuffix(box.Name);
                    ObjectId newStructureId = ObjectId.Null;
                    network.UpgradeOpen();
                    network.AddStructure(choice.FamilyId, choice.SizeId, location, 0.0, ref newStructureId, false);

                    Structure newStructure = (Structure)tr.GetObject(newStructureId, OpenMode.ForWrite);
                    newStructure.Name = newName;
                    newStructure.RimElevation = rimElevation;
                    newStructure.SumpElevation = rimElevation;

                    tr.Commit();
                    ed.WriteMessage($"\nPLACE ACCESS MANHOLE: Created '{newName}' using '{choice.DisplayName}'. Rim={rimElevation:F3}.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPLACE ACCESS MANHOLE error: {ex.Message}");
            }
        }

        private static ObjectId GetObjectIdProperty(object obj, string propertyName)
        {
            PropertyInfo? property = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property?.PropertyType == typeof(ObjectId) && property.GetValue(obj) is ObjectId id)
                return id;
            return ObjectId.Null;
        }

        private static string GetPartSizeName(Transaction tr, ObjectId sizeId)
        {
            DBObject obj = tr.GetObject(sizeId, OpenMode.ForRead);
            foreach (string propertyName in new[] { "Name", "DisplayName", "Description", "PartSizeName" })
            {
                PropertyInfo? property = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                if (property?.PropertyType == typeof(string) && property.GetValue(obj) is string value && !string.IsNullOrWhiteSpace(value))
                    return value;
            }
            return obj.GetType().Name;
        }

        private static string RemoveJsSuffix(string name)
            => name.EndsWith("-JS", StringComparison.OrdinalIgnoreCase) ? name[..^3] : name;

        private sealed class PartChoice
        {
            public ObjectId FamilyId { get; }
            public ObjectId SizeId { get; }
            public string DisplayName { get; }

            public PartChoice(ObjectId familyId, ObjectId sizeId, string displayName)
            {
                FamilyId = familyId;
                SizeId = sizeId;
                DisplayName = displayName;
            }
        }
    }
}
