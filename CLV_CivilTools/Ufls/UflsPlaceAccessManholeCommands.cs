using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.Civil.DatabaseServices;

namespace CLV_CivilTools.Ufls
{
    public static class UflsPlaceAccessManholeCommands
    {
        [Autodesk.AutoCAD.Runtime.CommandMethod("UFLS-PLACE-ACCESS-MH", Autodesk.AutoCAD.Runtime.CommandFlags.Modal)]
        public static void PlaceAccessManhole()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                PromptEntityOptions markPrompt = new PromptEntityOptions("\nSelect UFLS_MH_MARK: ");
                markPrompt.SetRejectMessage("\nSelect a UFLS_MH_MARK block.");
                markPrompt.AddAllowedClass(typeof(BlockReference), false);
                PromptEntityResult markResult = ed.GetEntity(markPrompt);
                if (markResult.Status != PromptStatus.OK)
                    return;

                Point3d location;
                ObjectId boxId;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockReference mark = tr.GetObject(markResult.ObjectId, OpenMode.ForRead) as BlockReference
                        ?? throw new InvalidOperationException("The selected object is not a block reference.");

                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(mark.BlockTableRecord, OpenMode.ForRead);
                    string blockName = btr.Name;
                    if (!string.Equals(blockName, "UFLS_MH_MARK", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("The selected block is not UFLS_MH_MARK.");

                    location = mark.Position;
                    tr.Commit();
                }

                PromptEntityOptions boxPrompt = new PromptEntityOptions("\nSelect Type 2 box structure: ");
                boxPrompt.SetRejectMessage("\nSelect a Civil 3D structure.");
                boxPrompt.AddAllowedClass(typeof(Structure), false);
                PromptEntityResult boxResult = ed.GetEntity(boxPrompt);
                if (boxResult.Status != PromptStatus.OK)
                    return;
                boxId = boxResult.ObjectId;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Structure box = tr.GetObject(boxId, OpenMode.ForRead) as Structure
                        ?? throw new InvalidOperationException("Unable to open the selected structure.");
                    Network network = tr.GetObject(box.NetworkId, OpenMode.ForRead) as Network
                        ?? throw new InvalidOperationException("The selected structure does not belong to a pipe network.");

                    ObjectId partsListId = network.PartsListId;
                    if (partsListId.IsNull)
                        throw new InvalidOperationException("The selected network does not have a Parts List.");

                    PartsList partsList = tr.GetObject(partsListId, OpenMode.ForRead) as PartsList
                        ?? throw new InvalidOperationException("Unable to open the network Parts List.");

                    List<PartFamily> families = new List<PartFamily>();
                    foreach (ObjectId familyId in partsList.GetPartFamilyIds())
                    {
                        PartFamily family = tr.GetObject(familyId, OpenMode.ForRead) as PartFamily;
                        if (family == null)
                            continue;

                        if (family.Name.IndexOf("ACCESS STRUCTURE", StringComparison.OrdinalIgnoreCase) >= 0)
                            families.Add(family);
                    }

                    if (families.Count == 0)
                        throw new InvalidOperationException("No ACCESS STRUCTURE part family was found in the selected network Parts List.");

                    List<PartChoice> choices = new List<PartChoice>();
                    foreach (PartFamily family in families.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        foreach (ObjectId sizeId in family.GetPartSizeIds())
                        {
                            PartSize size = tr.GetObject(sizeId, OpenMode.ForRead) as PartSize;
                            if (size == null)
                                continue;

                            string display = family.Name + " — " + size.Name;
                            choices.Add(new PartChoice(family.ObjectId, size.ObjectId, display));
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
                    if (choiceResult.Status != PromptStatus.OK && choiceResult.Status != PromptStatus.None)
                        return;

                    int choiceIndex = choiceResult.Status == PromptStatus.None ? 0 : choiceResult.Value - 1;
                    PartChoice choice = choices[choiceIndex];

                    PromptKeywordOptions rimPrompt = new PromptKeywordOptions("\nRim elevation source [BOX/User/AEC] <BOX>: ", "BOX User AEC")
                    {
                        AllowNone = true
                    };
                    PromptResult rimResult = ed.GetKeywords(rimPrompt);
                    if (rimResult.Status != PromptStatus.OK && rimResult.Status != PromptStatus.None)
                        return;

                    string rimMode = rimResult.Status == PromptStatus.None ? "BOX" : rimResult.StringResult;
                    double rimElevation;
                    if (rimMode.Equals("BOX", StringComparison.OrdinalIgnoreCase))
                    {
                        rimElevation = box.RimElevation;
                    }
                    else if (rimMode.Equals("User", StringComparison.OrdinalIgnoreCase))
                    {
                        PromptDoubleOptions elevPrompt = new PromptDoubleOptions("\nEnter rim elevation: ")
                        {
                            AllowNegative = true
                        };
                        PromptDoubleResult elevResult = ed.GetDouble(elevPrompt);
                        if (elevResult.Status != PromptStatus.OK)
                            return;
                        rimElevation = elevResult.Value;
                    }
                    else
                    {
                        PromptEntityOptions pointPrompt = new PromptEntityOptions("\nSelect AEC/COGO point: ");
                        pointPrompt.SetRejectMessage("\nSelect an AEC/COGO point.");
                        pointPrompt.AddAllowedClass(typeof(CogoPoint), false);
                        PromptEntityResult pointResult = ed.GetEntity(pointPrompt);
                        if (pointResult.Status != PromptStatus.OK)
                            return;

                        CogoPoint point = tr.GetObject(pointResult.ObjectId, OpenMode.ForRead) as CogoPoint
                            ?? throw new InvalidOperationException("Unable to open the selected COGO point.");
                        rimElevation = point.Elevation;
                    }

                    string newName = RemoveJsSuffix(box.Name);

                    tr.Commit();

                    using (Transaction createTr = db.TransactionManager.StartTransaction())
                    {
                        Network writableNetwork = createTr.GetObject(network.ObjectId, OpenMode.ForWrite) as Network
                            ?? throw new InvalidOperationException("Unable to open the network for writing.");

                        ObjectId newStructureId = ObjectId.Null;
                        writableNetwork.AddStructure(choice.FamilyId, choice.SizeId, location, 0.0, ref newStructureId, false);

                        Structure newStructure = createTr.GetObject(newStructureId, OpenMode.ForWrite) as Structure
                            ?? throw new InvalidOperationException("Civil 3D did not create the access structure.");

                        newStructure.Name = newName;
                        newStructure.RimElevation = rimElevation;
                        newStructure.SumpOverride = true;
                        newStructure.SumpElevation = rimElevation;

                        createTr.Commit();
                    }

                    ed.WriteMessage($"\nAccess manhole placed at {location.X:F3}, {location.Y:F3}. Rim elevation: {rimElevation:F3}. Name: {newName}");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPLACE ACCESS MANHOLE failed: {ex.Message}");
            }
        }

        private static string RemoveJsSuffix(string name)
        {
            if (name.EndsWith("-JS", StringComparison.OrdinalIgnoreCase))
                return name.Substring(0, name.Length - 3);
            return name;
        }

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
