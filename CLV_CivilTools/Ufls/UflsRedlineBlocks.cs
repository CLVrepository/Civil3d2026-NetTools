using System;
using System.IO;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools
{
    /// <summary>
    /// REDLINE block helpers used by the Q11 CHECK tab.
    /// </summary>
    public static class UflsRedlineBlockCommands
    {
        private const string RedlineFolder = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\Blocks\Survey";
        private const string NoteBlockName = "REDLINE-MTEXT";
        private const string LeaderBlockName = "REDLINE-LEADER";

        [CommandMethod("UFLS", "UFLS-REDLINE-NOTE", CommandFlags.Modal)]
        public static void InsertRedlineNote()
        {
            InsertAndExplodeBlock(NoteBlockName);
        }

        [CommandMethod("UFLS", "UFLS-REDLINE-LEADER", CommandFlags.Modal)]
        public static void InsertRedlineLeader()
        {
            InsertAndExplodeBlock(LeaderBlockName);
        }

        private static void InsertAndExplodeBlock(string blockName)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                var ppr = ed.GetPoint($"\nSpecify insertion point for {blockName}: ");
                if (ppr.Status != PromptStatus.OK)
                    return;

                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    ObjectId blockDefId = EnsureBlockLoaded(db, tr, ed, blockName);
                    if (blockDefId.IsNull)
                    {
                        tr.Commit();
                        return;
                    }

                    var currentSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                    var br = new BlockReference(new Point3d(ppr.Value.X, ppr.Value.Y, ppr.Value.Z), blockDefId);
                    currentSpace.AppendEntity(br);
                    tr.AddNewlyCreatedDBObject(br, true);

                    if (br.AttributeCollection != null)
                    {
                        AddAttributesFromDefinition(br, tr);
                    }

                    var exploded = new DBObjectCollection();
                    br.Explode(exploded);

                    foreach (DBObject obj in exploded)
                    {
                        if (obj is not Entity ent)
                        {
                            obj.Dispose();
                            continue;
                        }

                        currentSpace.AppendEntity(ent);
                        tr.AddNewlyCreatedDBObject(ent, true);
                    }

                    br.Erase(true);
                    tr.Commit();

                    ed.WriteMessage($"\n{blockName}: inserted and exploded.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n{blockName} error: {ex.Message}");
            }
        }

        private static ObjectId EnsureBlockLoaded(Database db, Transaction tr, Editor ed, string blockName)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(blockName))
                return bt[blockName];

            string dwgPath = Path.Combine(RedlineFolder, blockName + ".dwg");
            if (!File.Exists(dwgPath))
            {
                ed.WriteMessage($"\nUnable to locate block file: {dwgPath}");
                return ObjectId.Null;
            }

            using (var sourceDb = new Database(false, true))
            {
                sourceDb.ReadDwgFile(dwgPath, FileOpenMode.OpenForReadAndAllShare, false, null);
                return db.Insert(blockName, sourceDb, false);
            }
        }

        private static void AddAttributesFromDefinition(BlockReference br, Transaction tr)
        {
            var blockDef = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
            foreach (ObjectId id in blockDef)
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is not AttributeDefinition attDef || attDef.Constant)
                    continue;

                var attRef = new AttributeReference();
                attRef.SetAttributeFromBlock(attDef, br.BlockTransform);
                attRef.TextString = attDef.TextString;

                br.AttributeCollection.AppendAttribute(attRef);
                tr.AddNewlyCreatedDBObject(attRef, true);
            }
        }
    }
}
