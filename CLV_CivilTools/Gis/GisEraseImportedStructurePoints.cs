using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Gis
{
    public static class GisEraseImportedStructurePoints
    {
        [CommandMethod("CLV-GIS-ERASE-POINTS", CommandFlags.Modal)]
        public static void EraseImportedPoints()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                int erased = 0;
                using (DocumentLock docLock = doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    foreach (ObjectId id in ms)
                    {
                        Entity? ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                        if (ent == null)
                            continue;

                        if (!ent.Layer.Equals("Structures", System.StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (ent is DBPoint || ent.GetRXClass().DxfName.Equals("POINT", System.StringComparison.OrdinalIgnoreCase))
                        {
                            ent.UpgradeOpen();
                            ent.Erase();
                            erased++;
                        }
                    }

                    tr.Commit();
                }

                ed.WriteMessage($"\nCLV-GIS-ERASE-POINTS complete. erased={erased}.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-ERASE-POINTS error: {ex.Message}");
            }
        }
    }
}
