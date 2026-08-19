using System;
using System.Collections.Generic;
using System.IO;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools
{
    /// <summary>
    /// Q11 layer-maintenance helpers that replace the legacy MERGE-STRC
    /// and LayerStatesUpdate LISP routines with native command entry points.
    /// </summary>
    public static class UflsLayerMaintenanceCommands
    {
        private const string LegacyInnerLayer = "V-SURV-STRC-INNER-2D";
        private const string LegacyOuterLayer = "V-SURV-STRC-OUTER-2D";
        private const string StandardInnerLayer = "V-SURV-STRC-INNR-2D~~";
        private const string StandardOuterLayer = "V-SURV-STRC-OUTR-2D~~";

        private const string LayerStateFolder = @"W:\PW_AutoCAD_Support\2026_Civil3D\Layer States";
        private const string LateralCreatePipeLas = @"W:\PW_AutoCAD_Support\2026_Civil3D\Layer States\lateralcreatepipe.las";
        private const string PipeCenterLas = @"W:\PW_AutoCAD_Support\2026_Civil3D\Layer States\PipeCenter.las";

        [CommandMethod("MERGE-STRC", CommandFlags.Modal)]
        [CommandMethod("UFLS", "UFLS-MERGE-STRC", CommandFlags.Modal)]
        public static void MergeStructureLayers()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    int movedCount = 0;

                    MoveLayerContents(tr, db, LegacyInnerLayer, StandardInnerLayer, ref movedCount);
                    MoveLayerContents(tr, db, LegacyOuterLayer, StandardOuterLayer, ref movedCount);

                    TryDeleteLayer(tr, db, LegacyInnerLayer);
                    TryDeleteLayer(tr, db, LegacyOuterLayer);

                    tr.Commit();
                    ed.WriteMessage(
                        $"\nMERGE-STRC: moved {movedCount} object(s); merged {LegacyInnerLayer} -> {StandardInnerLayer} and {LegacyOuterLayer} -> {StandardOuterLayer}.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nMERGE-STRC error: {ex.Message}");
            }
        }

        [CommandMethod("ReloadLayerStates", CommandFlags.Modal)]
        [CommandMethod("UFLS", "UFLS-LAYER-STATES-UPDATE", CommandFlags.Modal)]
        public static void ReloadLayerStates()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var ed = doc.Editor;

            try
            {
                var scriptLines = new List<string>
                {
                    "(if (layerstate-delete \"lateralcreatepipe\") (princ \"\\nLateralCreatePipe has been deleted.\") (princ \"\\nThere was an error deleting LateralCreatePipe\"))",
                    "(if (layerstate-delete \"PipeCenter\") (princ \"\\nPipeCenter has been deleted.\") (princ \"\\nThere was an error deleting PipeCenter\"))"
                };

                AddLayerStateImport(scriptLines, "LateralCreatePipe", LateralCreatePipeLas);
                AddLayerStateImport(scriptLines, "PipeCenter", PipeCenterLas);
                scriptLines.Add("(princ)");

                doc.SendStringToExecute(
                    string.Join(" ", scriptLines) + " ",
                    activate: true,
                    wrapUpInactiveDoc: false,
                    echoCommand: false);

                ed.WriteMessage($"\nReloadLayerStates: queued layer-state refresh from {LayerStateFolder}.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nReloadLayerStates error: {ex.Message}");
            }
        }

        private static void AddLayerStateImport(List<string> scriptLines, string stateName, string lasPath)
        {
            if (File.Exists(lasPath))
            {
                scriptLines.Add(
                    $"(if (layerstate-import \"{EscapeForLisp(lasPath)}\") (princ \"\\n{stateName}.las has been imported.\") (princ \"\\nThere was an error importing {stateName}.las.\"))");
            }
            else
            {
                scriptLines.Add($"(princ \"\\nMissing layer-state file: {EscapeForLisp(lasPath)}\")");
            }
        }

        private static string EscapeForLisp(string value)
        {
            return value.Replace("\\", "\\\\");
        }

        private static void MoveLayerContents(Transaction tr, Database db, string sourceLayer, string targetLayer, ref int movedCount)
        {
            EnsureLayer(tr, db, targetLayer);

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId btrId in bt)
            {
                var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                if (btr.IsLayout || btr.IsFromExternalReference)
                    continue;

                foreach (ObjectId id in btr)
                {
                    if (!id.IsValid || id.IsErased)
                        continue;

                    if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity ent)
                        continue;

                    if (!ent.Layer.Equals(sourceLayer, StringComparison.OrdinalIgnoreCase))
                        continue;

                    ent.UpgradeOpen();
                    ent.Layer = targetLayer;
                    movedCount++;
                }
            }
        }

        private static void EnsureLayer(Transaction tr, Database db, string layerName)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(layerName))
                return;

            lt.UpgradeOpen();
            var ltr = new LayerTableRecord { Name = layerName };
            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }

        private static void TryDeleteLayer(Transaction tr, Database db, string layerName)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layerName))
                return;

            ObjectId layerId = lt[layerName];
            if (layerId == db.Clayer)
                return;

            var layer = (LayerTableRecord)tr.GetObject(layerId, OpenMode.ForWrite);
            if (layer.IsDependent)
                return;

            try
            {
                layer.Erase(true);
            }
            catch
            {
                // Leave the old layer in place if AutoCAD still considers it in use.
            }
        }
    }
}
