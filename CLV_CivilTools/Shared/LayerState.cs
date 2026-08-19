using System;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Shared
{
    /// <summary>
    /// Layer creation, toggling, and cleanup helpers.
    /// Replaces the layer parts of CommonUtils.
    /// </summary>
    internal static class LayerState
    {
        // ------------------------------------------------------------
        // Basic helpers
        // ------------------------------------------------------------

        public static void EnsureLayer(string layerName)
        {
            if (string.IsNullOrWhiteSpace(layerName)) return;

            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (!lt.Has(layerName))
                {
                    lt.UpgradeOpen();
                    var ltr = new LayerTableRecord { Name = layerName };
                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }
                tr.Commit();
            }
        }

        public static void SetLayerOff(string layerName, bool off)
        {
            if (string.IsNullOrWhiteSpace(layerName)) return;

            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            var db = doc.Database;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (!lt.Has(layerName))
                {
                    tr.Commit();
                    return;
                }

                var ltr = (LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForWrite);
                ltr.IsOff = off;
                tr.Commit();
            }
        }

        /// <summary>
        /// Delete all entities that live on the given layer, but keep the layer itself.
        /// </summary>
        public static void DeleteEntitiesOnLayer(Database db, string layerName)
        {
            if (db == null || string.IsNullOrWhiteSpace(layerName)) return;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (!lt.Has(layerName))
                {
                    tr.Commit();
                    return;
                }

                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId btrId in bt)
                {
                    var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                    foreach (ObjectId id in btr)
                    {
                        if (!id.IsValid || id.IsErased) continue;

                        if (tr.GetObject(id, OpenMode.ForRead, false) is AcEntity ent)
                        {
                            if (ent.Layer.Equals(layerName, StringComparison.OrdinalIgnoreCase))
                            {
                                ent.UpgradeOpen();
                                ent.Erase(true);
                            }
                        }
                    }
                }

                tr.Commit();
            }
        }

        /// <summary>
        /// Case-insensitive check if a layer name matches any of the provided names.
        /// </summary>
        public static bool IsLayerOneOf(string? layer, params string[] names)
        {
            if (string.IsNullOrWhiteSpace(layer) || names == null || names.Length == 0)
                return false;

            foreach (var n in names)
            {
                if (layer.Equals(n, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------
        // Scoped visibility helper (optional)
        // ------------------------------------------------------------

        /// <summary>
        /// Temporarily sets specified layers ON or OFF and restores their previous
        /// OFF state on dispose.
        /// Example:
        /// using (LayerState.ScopeOff("V-PNTC-SAMP", "V-PNTC-CROP")) { ... }
        /// </summary>
        public sealed class ScopeOff : IDisposable
        {
            private readonly Database _db;
            private readonly (string Name, bool WasOff)[] _layers;
            private bool _disposed;

            private ScopeOff(Database db, (string Name, bool WasOff)[] layers)
            {
                _db = db;
                _layers = layers;
            }

            public static ScopeOff Create(params string[] layerNames)
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                var db = doc.Database;

                if (layerNames == null || layerNames.Length == 0)
                    return new ScopeOff(db, Array.Empty<(string, bool)>());

                var states = new System.Collections.Generic.List<(string, bool)>();

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

                    foreach (var name in layerNames)
                    {
                        if (string.IsNullOrWhiteSpace(name) || !lt.Has(name))
                            continue;

                        var ltr = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForWrite);
                        bool wasOff = ltr.IsOff;
                        ltr.IsOff = true;

                        states.Add((name, wasOff));
                    }

                    tr.Commit();
                }

                return new ScopeOff(db, states.ToArray());
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                if (_layers.Length == 0) return;

                using (var tr = _db.TransactionManager.StartTransaction())
                {
                    var lt = (LayerTable)tr.GetObject(_db.LayerTableId, OpenMode.ForRead);

                    foreach (var (name, wasOff) in _layers)
                    {
                        if (!lt.Has(name)) continue;
                        var ltr = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForWrite);
                        ltr.IsOff = wasOff;
                    }

                    tr.Commit();
                }
            }
        }
    }
}