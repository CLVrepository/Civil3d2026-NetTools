using System;
using System.Collections.Generic;
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
        private const string BaseLayer = "V-SURV-CHCK";
        private const string GoodLayer = BaseLayer + "-GOOD";
        private const double DetailedTextHeight = 0.10;
        private const double ExhibitTextHeight = 0.28;

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

            int changed = 0;
            int skipped = 0;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                List<ObjectId> labelIds = FindPipeTopCheckLabels(db, tr);

                foreach (ObjectId labelId in labelIds)
                {
                    if (tr.GetObject(labelId, OpenMode.ForWrite, false) is not MText label)
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

                        label.Layer = BaseLayer;
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
                        label.TextHeight = DetailedTextHeight;
                        label.Annotative = AnnotativeStates.False;
                    }

                    PipeTopCheckData.Write(
                        label,
                        tr,
                        snapshot with { Mode = targetMode });

                    changed++;
                }

                SetGoodLayerState(tr, db, targetMode == PipeTopCheckData.DisplayMode.Detailed);
                tr.Commit();
            }

            string modeText = targetMode == PipeTopCheckData.DisplayMode.Exhibit
                ? "exhibit"
                : "detailed";

            ed.WriteMessage(
                $"\nPipe Top Check: {changed} label(s) set to {modeText} display" +
                (skipped > 0 ? $"; {skipped} skipped." : "."));
        }

        private static List<ObjectId> FindPipeTopCheckLabels(Database db, Transaction tr)
        {
            List<ObjectId> labels = new List<ObjectId>();
            DBDictionary layoutDictionary =
                (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);

            foreach (DBDictionaryEntry layoutEntry in layoutDictionary)
            {
                Layout layout = (Layout)tr.GetObject(layoutEntry.Value, OpenMode.ForRead, false);
                BlockTableRecord blockRecord = (BlockTableRecord)tr.GetObject(
                    layout.BlockTableRecordId,
                    OpenMode.ForRead,
                    false);

                foreach (ObjectId entityId in blockRecord)
                {
                    if (tr.GetObject(entityId, OpenMode.ForRead, false) is not MText label)
                        continue;

                    if (PipeTopCheckData.TryRead(label, tr, out _))
                        labels.Add(entityId);
                }
            }

            return labels;
        }

        private static void SetGoodLayerState(Transaction tr, Database db, bool turnOn)
        {
            LayerTable layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!layerTable.Has(GoodLayer))
                return;

            LayerTableRecord goodLayer = (LayerTableRecord)tr.GetObject(
                layerTable[GoodLayer],
                OpenMode.ForWrite,
                false);
            goodLayer.IsOff = !turnOn;
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
