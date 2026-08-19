using System;
using System.Collections.Generic;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDocument = Autodesk.AutoCAD.ApplicationServices.Document;

namespace CLV_CivilTools.Gis
{
    /// <summary>
    /// Removes transient/orphaned XData from drawing entities created or touched during GIS prep workflows.
    /// This is intentionally called before finalize so Phase 1 cache-tracking XData is only introduced by finalize.
    /// </summary>
    public static class GisXDataCleanup
    {
        private static readonly HashSet<string> PreservedRegApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CLV_GIS_CACHE"
        };

        [CommandMethod("CLV-GIS-CLEAN-XDATA", CommandFlags.Modal)]
        public static void CleanCurrentDrawingEntityXDataCommand()
        {
            AcDocument? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            CleanCurrentDrawingEntityXData(doc, preserveClvCache: true);
        }

        public static int CleanCurrentDrawingEntityXData(AcDocument doc, bool preserveClvCache)
        {
            Database db = doc.Database;
            Editor ed = doc.Editor;
            int cleaned = 0;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                if (tr.GetObject(db.BlockTableId, OpenMode.ForRead) is not BlockTable bt)
                {
                    ed.WriteMessage("\nCLV-GIS-CLEAN-XDATA: unable to open block table.");
                    return 0;
                }

                HashSet<ObjectId> seen = new HashSet<ObjectId>();

                foreach (ObjectId btrId in bt)
                {
                    if (tr.GetObject(btrId, OpenMode.ForRead) is not BlockTableRecord btr)
                        continue;

                    // Clean everything that lives in this drawing except external references.
                    if (btr.IsFromExternalReference || btr.IsFromOverlayReference)
                        continue;

                    foreach (ObjectId id in btr)
                    {
                        if (!seen.Add(id))
                            continue;

                        if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity ent || ent.IsErased)
                            continue;

                        ResultBuffer? existing = ent.XData;
                        if (existing == null)
                            continue;

                        TypedValue[] values = existing.AsArray();
                        if (values.Length == 0)
                            continue;

                        if (TryRewriteEntityXData(ent, values, preserveClvCache))
                            cleaned++;
                    }
                }

                RemoveUnusedTransientRegApps(tr, db, preserveClvCache);
                tr.Commit();
            }

            ed.WriteMessage($"\nCLV-GIS-CLEAN-XDATA: cleaned XData on {cleaned} object(s).");
            return cleaned;
        }

        private static bool TryRewriteEntityXData(Entity ent, TypedValue[] values, bool preserveClvCache)
        {
            List<List<TypedValue>> sections = SplitByRegApp(values);
            if (sections.Count == 0)
                return false;

            List<TypedValue> kept = new List<TypedValue>();
            List<string> removableApps = new List<string>();
            bool changed = false;

            foreach (List<TypedValue> section in sections)
            {
                if (section.Count == 0 || section[0].TypeCode != 1001)
                    continue;

                string appName = section[0].Value as string ?? string.Empty;
                if (string.IsNullOrWhiteSpace(appName))
                    continue;

                bool preserve = preserveClvCache && PreservedRegApps.Contains(appName);
                if (preserve)
                {
                    kept.AddRange(section);
                }
                else
                {
                    removableApps.Add(appName);
                    changed = true;
                }
            }

            if (!changed)
                return false;

            ent.UpgradeOpen();

            // Per AutoCAD behavior, a result buffer containing only the regapp name clears XData for that app.
            foreach (string appName in removableApps)
            {
                using ResultBuffer clearBuffer = new ResultBuffer(new TypedValue(1001, appName));
                ent.XData = clearBuffer;
            }

            if (kept.Count > 0)
            {
                using ResultBuffer keepBuffer = new ResultBuffer(kept.ToArray());
                ent.XData = keepBuffer;
            }

            return true;
        }

        private static List<List<TypedValue>> SplitByRegApp(TypedValue[] values)
        {
            List<List<TypedValue>> sections = new List<List<TypedValue>>();
            List<TypedValue>? current = null;

            foreach (TypedValue tv in values)
            {
                if (tv.TypeCode == 1001)
                {
                    current = new List<TypedValue> { tv };
                    sections.Add(current);
                    continue;
                }

                if (current != null)
                    current.Add(tv);
            }

            return sections;
        }

        private static void RemoveUnusedTransientRegApps(Transaction tr, Database db, bool preserveClvCache)
        {
            if (tr.GetObject(db.RegAppTableId, OpenMode.ForRead) is not RegAppTable regTable)
                return;

            List<ObjectId> toErase = new List<ObjectId>();
            foreach (ObjectId id in regTable)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not RegAppTableRecord reg)
                    continue;

                string name = reg.Name ?? string.Empty;
                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (preserveClvCache && PreservedRegApps.Contains(name))
                    continue;

                if (name.Equals("AcDbBlockRepETag", StringComparison.OrdinalIgnoreCase))
                    toErase.Add(id);
            }

            foreach (ObjectId id in toErase)
            {
                if (tr.GetObject(id, OpenMode.ForWrite) is RegAppTableRecord reg && !reg.IsErased)
                    reg.Erase();
            }
        }
    }
}
