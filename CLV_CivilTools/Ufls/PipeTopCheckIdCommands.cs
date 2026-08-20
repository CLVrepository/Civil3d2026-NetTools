using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace CLV_CivilTools.Ufls
{
    /// <summary>
    /// Assigns sequential exhibit identifiers to Pipe Top Check labels without changing
    /// their detailed label text. The identifiers are stored in the label metadata for
    /// later exhibit/table generation.
    /// </summary>
    public static class PipeTopCheckIdCommands
    {
        [CommandMethod("UFLS-PIPE-TOP-ID")]
        public static void AssignPipeTopCheckIds()
        {
            Autodesk.AutoCAD.ApplicationServices.Document? doc =
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                PromptSelectionOptions pso = new PromptSelectionOptions
                {
                    MessageForAdding = "\nSelect Pipe Top Check labels to assign exhibit IDs: ",
                    AllowDuplicates = false,
                    RejectObjectsOnLockedLayers = true
                };

                SelectionFilter filter = new SelectionFilter(new[]
                {
                    new TypedValue((int)DxfCode.Start, "MTEXT")
                });

                PromptSelectionResult psr = ed.GetSelection(pso, filter);
                if (psr.Status != PromptStatus.OK)
                    return;

                PromptIntegerOptions numberOptions = new PromptIntegerOptions("\nStarting exhibit ID number <1>: ")
                {
                    AllowNegative = false,
                    AllowZero = false,
                    DefaultValue = 1,
                    UseDefaultValue = true
                };

                PromptIntegerResult numberResult = ed.GetInteger(numberOptions);
                if (numberResult.Status != PromptStatus.OK)
                    return;

                int nextNumber = numberResult.Value;
                List<SelectedCheck> checks = new List<SelectedCheck>();

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (SelectedObject selected in psr.Value)
                    {
                        if (selected == null)
                            continue;

                        if (tr.GetObject(selected.ObjectId, OpenMode.ForRead, false) is not MText label)
                            continue;

                        if (!PipeTopCheckData.TryRead(label, tr, out PipeTopCheckData.Snapshot snapshot))
                            continue;

                        checks.Add(new SelectedCheck(selected.ObjectId, snapshot));
                    }

                    tr.Commit();
                }

                if (checks.Count == 0)
                {
                    ed.WriteMessage("\nNo Pipe Top Check labels were found in the selection.");
                    return;
                }

                // Use a deterministic plan-view order rather than AutoCAD selection order:
                // highest Y first, then lowest X. This makes repeated exhibit generation predictable.
                checks.Sort((a, b) =>
                {
                    int yCompare = b.Snapshot.LabelLocation.Y.CompareTo(a.Snapshot.LabelLocation.Y);
                    return yCompare != 0
                        ? yCompare
                        : a.Snapshot.LabelLocation.X.CompareTo(b.Snapshot.LabelLocation.X);
                });

                int assignedCount = 0;
                int skippedCount = psr.Value.Count - checks.Count;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (SelectedCheck check in checks)
                    {
                        if (tr.GetObject(check.ObjectId, OpenMode.ForWrite, false) is not MText label)
                            continue;

                        PipeTopCheckData.Snapshot updated = check.Snapshot with
                        {
                            ExhibitId = nextNumber.ToString("00")
                        };

                        PipeTopCheckData.Write(label, tr, updated);
                        nextNumber++;
                        assignedCount++;
                    }

                    tr.Commit();
                }

                ed.WriteMessage($"\nAssigned exhibit IDs to {assignedCount} Pipe Top Check label(s).");
                if (skippedCount > 0)
                    ed.WriteMessage($" {skippedCount} selected object(s) were not Pipe Top Check labels.");
                ed.WriteMessage(" Labels remain in Detailed display mode; only their stored exhibit IDs were updated.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUFLS-PIPE-TOP-ID error: {ex.Message}");
            }
        }

        private readonly record struct SelectedCheck(
            ObjectId ObjectId,
            PipeTopCheckData.Snapshot Snapshot);
    }
}
