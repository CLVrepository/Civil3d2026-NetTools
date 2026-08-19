using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices.Core;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDocument = Autodesk.AutoCAD.ApplicationServices.Document;
using CLV_CivilTools.Shared;

namespace CLV_CivilTools.Gis
{
    /// <summary>
    /// Performs a stronger GIS drawing cleanup after prep/conversion workflows.
    /// </summary>
    public static class GisDrawingCleanup
    {
        private static readonly string[] InterestingPrefixes = { "C-STRM-", "C-SSWR-" };

        [CommandMethod("CLV-GIS-CLEAN-DWG", CommandFlags.Modal)]
        public static void CleanCurrentDrawingCommand()
        {
            AcDocument? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            RunCleanup(doc);
        }
        public static void RunCleanup(AcDocument doc)
        {
            Editor ed = doc.Editor;
            int cleaned = GisXDataCleanup.CleanCurrentDrawingEntityXData(doc, preserveClvCache: true);
            int purged = PurgeUnusedObjects(doc.Database);
            LayerStandards.EnsureGisLayers(doc.Database, ed);
            ed.WriteMessage($"\nCLV-GIS-CLEAN-DWG: XData cleanup complete ({cleaned} object(s)). API purge removed {purged} object(s). GIS layer standards re-applied. Queueing single AUDIT pass.");

            doc.SendStringToExecute("._AUDIT _Y ", true, false, false);
        }

        private static int PurgeUnusedObjects(Database db)
        {
            int totalPurged = 0;

            for (int cycle = 0; cycle < 3; cycle++)
            {
                int purgedThisCycle = 0;

                using Transaction tr = db.TransactionManager.StartTransaction();
                ObjectIdCollection candidates = CollectPurgeCandidates(tr, db);
                if (candidates.Count == 0)
                {
                    tr.Commit();
                    break;
                }

                db.Purge(candidates);
                foreach (ObjectId id in candidates)
                {
                    if (!id.IsValid || id.IsErased)
                        continue;

                    if (tr.GetObject(id, OpenMode.ForWrite, false) is DBObject dbo && !dbo.IsErased)
                    {
                        try
                        {
                            dbo.Erase();
                            purgedThisCycle++;
                        }
                        catch
                        {
                            // Ignore non-erasable leftovers.
                        }
                    }
                }

                tr.Commit();
                totalPurged += purgedThisCycle;

                if (purgedThisCycle == 0)
                    break;
            }

            return totalPurged;
        }

        private static ObjectIdCollection CollectPurgeCandidates(Transaction tr, Database db)
        {
            ObjectIdCollection ids = new ObjectIdCollection();

            AddTableCandidates<LayerTable>(tr, db.LayerTableId, ids, BlockTableRecord.ModelSpace, BlockTableRecord.PaperSpace, "0", "Defpoints");
            AddTableCandidates<LinetypeTable>(tr, db.LinetypeTableId, ids, "ByBlock", "ByLayer", "Continuous");
            AddTableCandidates<TextStyleTable>(tr, db.TextStyleTableId, ids, "Standard");
            AddTableCandidates<DimStyleTable>(tr, db.DimStyleTableId, ids, "Standard");
            AddTableCandidates<RegAppTable>(tr, db.RegAppTableId, ids, "ACAD", "AcadAnnotative", "CLV_GIS_CACHE");
            AddTableCandidates<UcsTable>(tr, db.UcsTableId, ids);
            AddTableCandidates<ViewTable>(tr, db.ViewTableId, ids);
            AddTableCandidates<ViewportTable>(tr, db.ViewportTableId, ids, "*Active");
            AddTableCandidates<BlockTable>(tr, db.BlockTableId, ids, BlockTableRecord.ModelSpace, BlockTableRecord.PaperSpace);

            return ids;
        }

        private static void AddTableCandidates<TTable>(Transaction tr, ObjectId tableId, ObjectIdCollection ids, params string[] protectedNames)
            where TTable : SymbolTable
        {
            if (tr.GetObject(tableId, OpenMode.ForRead) is not TTable table)
                return;

            HashSet<string> protectedSet = new HashSet<string>(protectedNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId id in table)
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is not SymbolTableRecord rec || rec.IsErased)
                    continue;

                string name = rec.Name ?? string.Empty;
                if (protectedSet.Contains(name))
                    continue;

                ids.Add(id);
            }
        }

    }
}
