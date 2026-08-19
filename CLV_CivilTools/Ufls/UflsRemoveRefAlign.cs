using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using CivDb = Autodesk.Civil.DatabaseServices;
using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDb = Autodesk.AutoCAD.DatabaseServices;

namespace CLV_CivilTools.Ufls
{
    public static class UflsRemoveRefAlign
    {
        [CommandMethod("UFLS", "REMOVE-REF-ALIGN", CommandFlags.Modal)]
        [CommandMethod("UFLS", "UFLS-REMOVE-REF-ALIGN", CommandFlags.Modal)]
        public static void RemoveReferenceAlignment()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            AcDb.Database db = doc.Database;

            try
            {
                PromptKeywordOptions pko = new PromptKeywordOptions(
                    "\nRemove reference alignment from pipe network parts [All/Select] <All>: ",
                    "All Select");
                pko.AllowNone = true;

                PromptResult modeRes = ed.GetKeywords(pko);
                if (modeRes.Status == PromptStatus.Cancel)
                    return;

                bool useAll = modeRes.Status == PromptStatus.None ||
                              string.Equals(modeRes.StringResult, "All", StringComparison.OrdinalIgnoreCase);

                SelectionSet? selection = null;
                if (!useAll)
                {
                    PromptSelectionResult psr = ed.GetSelection(new PromptSelectionOptions
                    {
                        MessageForAdding = "\nSelect pipe(s) and/or structure(s) to clear reference alignment: "
                    });

                    if (psr.Status != PromptStatus.OK || psr.Value == null)
                        return;

                    selection = psr.Value;
                }

                int totalSeen = 0;
                int partsProcessed = 0;
                int cleared = 0;
                int alreadyClear = 0;
                int skippedReferenceObjects = 0;
                int skippedNonParts = 0;
                int failed = 0;

                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    if (useAll)
                    {
                        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                        var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                        foreach (ObjectId id in ms)
                        {
                            totalSeen++;
                            ProcessOne(id, tr, ref partsProcessed, ref cleared, ref alreadyClear,
                                ref skippedReferenceObjects, ref skippedNonParts, ref failed);
                        }
                    }
                    else if (selection != null)
                    {
                        foreach (SelectedObject sel in selection)
                        {
                            if (sel == null || sel.ObjectId.IsNull)
                                continue;

                            totalSeen++;
                            ProcessOne(sel.ObjectId, tr, ref partsProcessed, ref cleared, ref alreadyClear,
                                ref skippedReferenceObjects, ref skippedNonParts, ref failed);
                        }
                    }

                    tr.Commit();
                }

                ed.WriteMessage(
                    $"\nREMOVE-REF-ALIGN: scanned {totalSeen} object(s). Pipe/structure parts checked {partsProcessed}. Cleared {cleared}. Already clear {alreadyClear}. Skipped reference objects {skippedReferenceObjects}. Skipped non-parts {skippedNonParts}. Failed {failed}.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nREMOVE-REF-ALIGN error: {ex.Message}");
            }
        }

        private static void ProcessOne(
            ObjectId id,
            Transaction tr,
            ref int partsProcessed,
            ref int cleared,
            ref int alreadyClear,
            ref int skippedReferenceObjects,
            ref int skippedNonParts,
            ref int failed)
        {
            try
            {
                AcDb.DBObject obj = tr.GetObject(id, OpenMode.ForWrite, false);

                if (obj is not CivDb.Part part)
                {
                    skippedNonParts++;
                    return;
                }

                if (obj is not CivDb.Pipe && obj is not CivDb.Structure)
                {
                    skippedNonParts++;
                    return;
                }

                partsProcessed++;

                if (part.IsReferenceObject || part.IsReferenceSubObject)
                {
                    skippedReferenceObjects++;
                    return;
                }

                if (part.RefAlignmentId.IsNull)
                {
                    alreadyClear++;
                    return;
                }

                part.RefAlignmentId = ObjectId.Null;
                cleared++;
            }
            catch
            {
                failed++;
            }
        }
    }
}
