using System;
using System.Globalization;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace CLV_CivilTools.Ufls
{
    /// <summary>
    /// Switches Pipe Top Check labels between detailed plan-review text and
    /// compact exhibit identifiers while preserving the structured metadata.
    /// </summary>
    public static class PipeTopCheckDisplayCommands
    {
        private const double ExhibitTextHeight = 0.14;

        [CommandMethod("UFLS-PIPE-TOP-EXHIBIT")]
        public static void SetPipeTopCheckExhibitDisplay()
        {
            SetDisplayMode(PipeTopCheckData.DisplayMode.Exhibit);
        }

        [CommandMethod("UFLS-PIPE-TOP-DETAILED")]
        public static void SetPipeTopCheckDetailedDisplay()
        {
            SetDisplayMode(PipeTopCheckData.DisplayMode.Detailed);
        }

        private static void SetDisplayMode(PipeTopCheckData.DisplayMode targetMode)
        {
            Autodesk.AutoCAD.ApplicationServices.Document? doc =
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptSelectionOptions options = new PromptSelectionOptions
            {
                MessageForAdding = targetMode == PipeTopCheckData.DisplayMode.Exhibit
                    ? "\nSelect Pipe Top Check labels for exhibit display: "
                    : "\nSelect Pipe Top Check labels to restore detailed display: "
            };

            SelectionFilter filter = new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Start, "MTEXT")
            });

            PromptSelectionResult selection = ed.GetSelection(options, filter);
            if (selection.Status != PromptStatus.OK)
                return;

            int changed = 0;
            int skipped = 0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject? selected in selection.Value)
                {
                    if (selected == null)
                        continue;

                    if (tr.GetObject(selected.ObjectId, OpenMode.ForWrite, false) is not MText label)
                    {
                        skipped++;
                        continue;
                    }

                    if (!PipeTopCheckData.TryRead(label, tr, out PipeTopCheckData.Snapshot snapshot))
                    {
                        skipped++;
                        continue;
                    }

                    if (targetMode == PipeTopCheckData.DisplayMode.Exhibit)
                    {
                        if (string.IsNullOrWhiteSpace(snapshot.ExhibitId))
                        {
                            skipped++;
                            continue;
                        }

                        label.Contents = snapshot.ExhibitId;
                        label.TextHeight = ExhibitTextHeight;
                        label.Annotative = AnnotativeStates.True;
                    }
                    else
                    {
                        label.Contents = BuildDetailedLabel(
                            snapshot.PlanTopElevation,
                            snapshot.SurveyTopElevation,
                            snapshot.Difference);
                    }

                    PipeTopCheckData.Write(
                        label,
                        tr,
                        snapshot with { Mode = targetMode });

                    changed++;
                }

                tr.Commit();
            }

            string modeText = targetMode == PipeTopCheckData.DisplayMode.Exhibit
                ? "exhibit"
                : "detailed";

            ed.WriteMessage(
                $"\nPipe Top Check: {changed} label(s) set to {modeText} display" +
                (skipped > 0 ? $"; {skipped} skipped." : "."));
        }

        private static string BuildDetailedLabel(
            double planTopElevation,
            double surveyTopElevation,
            double difference)
            => $"PLAN - TOP = {FormatElevation(planTopElevation)}\\P" +
               $"SURV - TOP = {FormatElevation(surveyTopElevation)}\\P" +
               $"\\C1;DIFF = {FormatSignedDifference(difference)}";

        private static string FormatElevation(double value)
            => double.IsNaN(value)
                ? "<not available>"
                : value.ToString("0.000", CultureInfo.InvariantCulture);

        private static string FormatSignedDifference(double value)
            => double.IsNaN(value)
                ? "<not available>"
                : value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);
    }
}
