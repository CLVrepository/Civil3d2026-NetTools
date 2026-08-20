using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

namespace CLV_CivilTools.Ufls
{
    /// <summary>
    /// Prepares Pipe Top Check labels for exhibits by identifying checks that exceed
    /// the configured tolerance, assigning exhibit IDs, switching them to exhibit
    /// display, and moving in-tolerance checks to a separate review layer.
    /// </summary>
    public static class PipeTopCheckToleranceCommands
    {
        private const double DefaultTolerance = 0.30;
        private const double ExhibitTextHeight = 0.28;
        private const double DetailedTextHeight = 0.10;

        [CommandMethod("UFLS-PIPE-TOP-EXCEEDS-TOLERANCE")]
        public static void SetPipeTopCheckExceedsTolerance()
        {
            Autodesk.AutoCAD.ApplicationServices.Document? doc =
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            PromptDoubleOptions toleranceOptions = new PromptDoubleOptions(
                $"\nTolerance <{DefaultTolerance:0.00}>: ")
            {
                AllowNegative = false,
                AllowZero = false,
                DefaultValue = DefaultTolerance,
                UseDefaultValue = true
            };

            PromptDoubleResult toleranceResult = ed.GetDouble(toleranceOptions);
            if (toleranceResult.Status != PromptStatus.OK)
                return;

            double tolerance = toleranceResult.Value;
            const string baseLayer = "V-SURV-CHCK";
            string goodLayer = baseLayer + "-GOOD";

            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    EnsureGoodLayer(tr, db, baseLayer, goodLayer);

                    List<CheckItem> checks = FindPipeTopChecks(db, tr);
                    if (checks.Count == 0)
                    {
                        ed.WriteMessage("\nNo Pipe Top Check labels were found in the drawing.");
                        tr.Commit();
                        return;
                    }

                    HashSet<string> usedIds = new HashSet<string>(
                        checks
                            .Select(c => c.Snapshot.ExhibitId)
                            .Where(id => !string.IsNullOrWhiteSpace(id)),
                        StringComparer.OrdinalIgnoreCase);

                    int nextNumber = FindHighestNumericId(checks) + 1;
                    List<CheckItem> missingIds = checks
                        .Where(c => Math.Abs(c.Snapshot.Difference) > tolerance && string.IsNullOrWhiteSpace(c.Snapshot.ExhibitId))
                        .OrderByDescending(c => c.Snapshot.LabelLocation.Y)
                        .ThenBy(c => c.Snapshot.LabelLocation.X)
                        .ToList();

                    foreach (CheckItem item in missingIds)
                    {
                        while (usedIds.Contains(nextNumber.ToString("00", CultureInfo.InvariantCulture)))
                            nextNumber++;

                        string exhibitId = nextNumber.ToString("00", CultureInfo.InvariantCulture);
                        item.Snapshot = item.Snapshot with { ExhibitId = exhibitId };
                        usedIds.Add(exhibitId);
                        nextNumber++;
                    }

                    int exceedsCount = 0;
                    int goodCount = 0;
                    int idsAssigned = missingIds.Count;

                    foreach (CheckItem item in checks)
                    {
                        if (tr.GetObject(item.ObjectId, OpenMode.ForWrite, false) is not MText label)
                            continue;

                        bool exceeds = Math.Abs(item.Snapshot.Difference) > tolerance;

                        if (exceeds)
                        {
                            if (string.IsNullOrWhiteSpace(item.Snapshot.ExhibitId))
                                continue;

                            label.Layer = baseLayer;
                            label.Contents = item.Snapshot.ExhibitId;
                            label.TextHeight = ExhibitTextHeight;
                            label.Annotative = AnnotativeStates.True;

                            PipeTopCheckData.Write(
                                label,
                                tr,
                                item.Snapshot with { Mode = PipeTopCheckData.DisplayMode.Exhibit });

                            exceedsCount++;
                        }
                        else
                        {
                            label.Layer = goodLayer;
                            label.Contents = BuildDetailedLabel(
                                item.Snapshot.PlanTopElevation,
                                item.Snapshot.SurveyTopElevation,
                                item.Snapshot.Difference);
                            label.TextHeight = DetailedTextHeight;
                            label.Annotative = AnnotativeStates.False;

                            PipeTopCheckData.Write(
                                label,
                                tr,
                                item.Snapshot with { Mode = PipeTopCheckData.DisplayMode.Detailed });

                            goodCount++;
                        }
                    }

                    tr.Commit();

                    ed.WriteMessage(
                        $"\nPipe Top Check tolerance review complete: {exceedsCount} over tolerance, {goodCount} within tolerance." +
                        $" Tolerance = {tolerance:0.000}." +
                        $" Good checks moved to {goodLayer}." +
                        (idsAssigned > 0 ? $" {idsAssigned} exhibit ID(s) assigned." : string.Empty));
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUFLS-PIPE-TOP-EXCEEDS-TOLERANCE error: {ex.Message}");
            }
        }

        private static List<CheckItem> FindPipeTopChecks(Database db, Transaction tr)
        {
            List<CheckItem> checks = new List<CheckItem>();
            DBDictionary layoutDictionary = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);

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

                    if (!PipeTopCheckData.TryRead(label, tr, out PipeTopCheckData.Snapshot snapshot))
                        continue;

                    checks.Add(new CheckItem(entityId, snapshot, snapshot.ExhibitId));
                }
            }

            return checks;
        }

        private static int FindHighestNumericId(IEnumerable<CheckItem> checks)
        {
            int highest = 0;
            foreach (CheckItem item in checks)
            {
                if (int.TryParse(item.Snapshot.ExhibitId, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
                    highest = Math.Max(highest, value);
            }

            return highest;
        }

        private static void EnsureGoodLayer(Transaction tr, Database db, string sourceLayerName, string goodLayerName)
        {
            LayerTable layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (layerTable.Has(goodLayerName))
                return;

            layerTable.UpgradeOpen();

            LayerTableRecord goodLayer = new LayerTableRecord
            {
                Name = goodLayerName
            };

            if (layerTable.Has(sourceLayerName))
            {
                LayerTableRecord source = (LayerTableRecord)tr.GetObject(
                    layerTable[sourceLayerName],
                    OpenMode.ForRead);

                goodLayer.Color = source.Color;
                goodLayer.LinetypeObjectId = source.LinetypeObjectId;
                goodLayer.LineWeight = source.LineWeight;
                goodLayer.IsPlottable = source.IsPlottable;
                goodLayer.IsFrozen = false;
                goodLayer.IsOff = false;
            }
            else
            {
                goodLayer.IsPlottable = true;
                goodLayer.IsFrozen = false;
                goodLayer.IsOff = false;
            }

            layerTable.Add(goodLayer);
            tr.AddNewlyCreatedDBObject(goodLayer, true);

            // Transparency and plot style are assigned only after the new layer
            // has been added to the database. Setting these on an unattached
            // LayerTableRecord can raise eNoDatabase in AutoCAD.
            if (layerTable.Has(sourceLayerName))
            {
                LayerTableRecord source = (LayerTableRecord)tr.GetObject(
                    layerTable[sourceLayerName],
                    OpenMode.ForRead);

                goodLayer.Transparency = source.Transparency;

                if (!db.PlotStyleMode)
                {
                    string plotStyleName = source.PlotStyleName;
                    DBDictionary plotStyleDictionary =
                        (DBDictionary)tr.GetObject(db.PlotStyleNameDictionaryId, OpenMode.ForRead);
                    if (plotStyleDictionary.Contains(plotStyleName))
                        goodLayer.PlotStyleName = plotStyleName;
                }
            }
        }

        private static string BuildDetailedLabel(double planTopElevation, double surveyTopElevation, double difference)
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

        private sealed class CheckItem
        {
            internal CheckItem(ObjectId objectId, PipeTopCheckData.Snapshot snapshot, string originalExhibitId)
            {
                ObjectId = objectId;
                Snapshot = snapshot;
                OriginalExhibitId = originalExhibitId;
            }

            internal ObjectId ObjectId { get; }
            internal PipeTopCheckData.Snapshot Snapshot { get; set; }
            internal string OriginalExhibitId { get; }
        }
    }
}
