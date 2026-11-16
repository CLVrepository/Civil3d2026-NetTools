using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.DatabaseServices;

// Alias AutoCAD Application
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

// Map 3D / MapGuide
using Autodesk.Gis.Map.Platform;
using OSGeo.MapGuide;

namespace CLV_CivilTools.Gis
{
    public static class Aerials
    {
        // Folder containing your Nearmap .layer files
        public const string NearmapFolder =
            @"W:\PW_GRID_City\000-City\NearMap";

        private const string AerialGroupName = "Aerial Imagery";

        private static ViewTableRecord? _savedView;
        private static Document? _viewDocument;
        private static bool _viewRestoreHookAttached;

        // ============================================================
        // COMMAND-LINE ENTRY (still available)
        // ============================================================

        [CommandMethod("NM_AERIAL")]
        public static void LoadNearmapFromList()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;

            try
            {
                if (!Directory.Exists(NearmapFolder))
                {
                    ed.WriteMessage($"\nNearmap folder not found:\n{NearmapFolder}");
                    return;
                }

                var files = Directory
                    .GetFiles(NearmapFolder, "*.layer")
                    .OrderByDescending(File.GetLastWriteTime)
                    .ToList();

                if (files.Count == 0)
                {
                    ed.WriteMessage("\nNo Nearmap .layer files found.");
                    return;
                }

                ed.WriteMessage("\nAvailable Nearmap Images:");

                for (int i = 0; i < files.Count; i++)
                {
                    ed.WriteMessage(
                        $"\n[{i + 1}] {Path.GetFileNameWithoutExtension(files[i])}");
                }

                var pio = new PromptIntegerOptions("\nEnter number to load:")
                {
                    LowerLimit = 1,
                    UpperLimit = files.Count,
                    AllowNone = false
                };

                var pir = ed.GetInteger(pio);
                if (pir.Status != PromptStatus.OK)
                    return;

                string selectedFile = files[pir.Value - 1];

                LoadAerialFromFile(selectedFile);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nError in NM_AERIAL: {ex.Message}");
            }
        }

        // ============================================================
        // PUBLIC HELPER USED BY COMMAND + PALETTE
        // ============================================================

        /// <summary>
        /// Core loading logic used by both NM_AERIAL and the palette.
        /// </summary>
        public static void LoadAerialFromFile(string layerFilePath)
        {
            if (string.IsNullOrWhiteSpace(layerFilePath))
                return;

            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;

            if (!File.Exists(layerFilePath))
            {
                ed.WriteMessage($"\nLayer file not found:\n{layerFilePath}");
                return;
            }

            string displayName = Path.GetFileNameWithoutExtension(layerFilePath);

            try
            {
                using (var docLock = doc.LockDocument())
                {
                    AcMapMap map = AcMapMap.GetCurrentMap();
                    if (map == null)
                    {
                        ed.WriteMessage("\nAcMapMap.GetCurrentMap() returned null.");
                        return;
                    }

                    SaveCurrentView(doc);
                    try
                    {
                        RemoveExistingNearmapLayers(map);
                        HashSet<string> before = LayerIds(map);
                        map.LoadLayer(layerFilePath);
                        // Map's internal names need not match the file name. Track every layer
                        // actually added by this import and persist ownership through its group.
                        List<MgLayerBase> added = Layers(map).Where(l => !before.Contains(l.GetObjectId())).ToList();
                        if (added.Count == 0)
                            throw new InvalidOperationException("The layer file added no new Display Manager layers.");
                        PlaceLayersInAerialGroupAndSendToBack(map, added, displayName);
                        BringAllCadEntitiesToFront();
                        RefreshMap(ed);
                    }
                    finally
                    {
                        try { RestoreSavedView(doc); }
                        finally { AttachViewRestoreHook(); }
                    }
                }

                ed.WriteMessage($"\nLoaded Nearmap layer: {displayName}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nError loading aerial: {ex.Message}");
            }
        }

        // ============================================================
        // VIEW RESTORE - AFTER MAP'S AUTO ZOOM
        // ============================================================

        private static void SaveCurrentView(Document doc)
        {
            CancelViewRestore();
            _savedView = doc.Editor.GetCurrentView();
            _viewDocument = doc;
        }

        private static void RestoreSavedView(Document doc)
        {
            if (_savedView != null && ReferenceEquals(_viewDocument, doc))
                doc.Editor.SetCurrentView(_savedView);
        }

        private static void AttachViewRestoreHook()
        {
            if (_savedView == null || _viewRestoreHookAttached) return;
            // One idle turn also catches Map's deferred auto-zoom. Never wait for the
            // user's next ZOOM command, and never restore another drawing's saved view.
            AcadApp.Idle += OnRestoreView;
            if (_viewDocument != null) _viewDocument.CommandWillStart += OnViewCommandStart;
            _viewRestoreHookAttached = true;
        }

        private static void OnViewCommandStart(object sender, CommandEventArgs e) => CancelViewRestore();

        private static void OnRestoreView(object? sender, EventArgs e)
        {
            Document? doc = _viewDocument;
            try
            {
                if (doc != null && ReferenceEquals(doc, AcadApp.DocumentManager.MdiActiveDocument))
                    using (doc.LockDocument()) RestoreSavedView(doc);
            }
            catch (System.Exception ex)
            {
                AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\nAerial view restore: " + ex.Message);
            }
            finally { CancelViewRestore(); }
        }

        private static void CancelViewRestore()
        {
            if (_viewRestoreHookAttached) AcadApp.Idle -= OnRestoreView;
            if (_viewRestoreHookAttached && _viewDocument != null) _viewDocument.CommandWillStart -= OnViewCommandStart;
            _viewRestoreHookAttached = false;
            _savedView?.Dispose(); _savedView = null; _viewDocument = null;
        }

        // ============================================================
        // MAP LAYER MANAGEMENT
        // ============================================================

        /// <summary>
        /// Public helper for palette "Unload Aerials" button.
        /// </summary>
        [CommandMethod("NM_UNLOAD_AERIALS", CommandFlags.Modal)]
        public static void UnloadNearmapCommand()
        {
            try { RemoveAllNearmapLayers(); }
            catch (System.Exception ex)
            {
                AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\nAerial unload did not complete: " + ex.Message);
            }
        }

        public static void RemoveAllNearmapLayers()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;
            using (doc.LockDocument())
            {
                if (ReferenceEquals(_viewDocument, doc)) CancelViewRestore();
                AcMapMap map = AcMapMap.GetCurrentMap();
                if (map == null) throw new InvalidOperationException("No current Map display is available.");
                int removed;
                try { removed = RemoveExistingNearmapLayers(map); }
                finally { RefreshMap(ed); }
                ed.WriteMessage(removed == 0
                    ? "\nNo matching Nearmap aerial layers found in the current map."
                    : "\nUnloaded " + removed + " aerial layer(s) from the current map.");
            }
        }

        private static List<MgLayerBase> Layers(AcMapMap map)
        {
            MgLayerCollection layers = map.GetLayers();
            var result = new List<MgLayerBase>();
            for (int i = 0; i < layers.GetCount(); i++) result.Add(layers.GetItem(i));
            return result;
        }

        private static HashSet<string> LayerIds(AcMapMap map) =>
            new HashSet<string>(Layers(map).Select(l => l.GetObjectId()), StringComparer.Ordinal);

        private static bool IsAerial(MgLayerBase layer)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (MgLayerGroup? group = layer.GetGroup(); group != null; group = group.GetGroup())
            {
                if (!seen.Add(group.GetObjectId())) throw new InvalidOperationException("Cyclic Map layer groups; no aerial ownership guess was made.");
                if (AerialLayerIdentity.IsAerialGroup(group.GetName(), group.GetLegendLabel())) return true;
            }
            if (AerialLayerIdentity.IsAerial(layer.GetName(), layer.GetLegendLabel())) return true;
            // Drawing/base-map layers need not have a feature source. Optional resource
            // metadata must not prevent unloading positively identified aerial layers.
            try { return AerialLayerIdentity.IsAerial(layer.GetFeatureSourceId()); }
            catch (System.Exception) { return false; }
        }

        private static int RemoveExistingNearmapLayers(AcMapMap map)
        {
            // Resolve all candidates before changing the collection. Resource connections may
            // be shared with other GIS layers; unload display layers, not arbitrary services.
            var candidates = new HashSet<string>(Layers(map).Where(IsAerial).Select(l => l.GetObjectId()), StringComparer.Ordinal);
            MgLayerCollection layers = map.GetLayers();
            int removed = 0;
            for (int i = layers.GetCount() - 1; i >= 0; i--)
            {
                MgLayerBase layer = layers.GetItem(i);
                if (!candidates.Contains(layer.GetObjectId())) continue;
                layers.RemoveAt(i); removed++;
            }
            if (Layers(map).Any(l => candidates.Contains(l.GetObjectId())))
                throw new InvalidOperationException("Aerial layers remain after unload; inspect Display Manager.");

            // Remove only an empty tool-owned root group. Nonempty or nested groups stay intact.
            MgLayerGroupCollection groups = map.GetLayerGroups();
            for (int i = groups.GetCount() - 1; i >= 0; i--)
            {
                MgLayerGroup group = groups.GetItem(i);
                if (!AerialLayerIdentity.IsAerialGroup(group.GetName(), group.GetLegendLabel())) continue;
                string id = group.GetObjectId();
                bool used = Layers(map).Any(l => l.GetGroup()?.GetObjectId() == id);
                for (int j = 0; j < groups.GetCount() && !used; j++)
                    used = groups.GetItem(j).GetGroup()?.GetObjectId() == id;
                if (!used) groups.RemoveAt(i);
            }
            return removed;
        }

        private static void RefreshMap(Editor ed)
        {
            AcMapMap.ForceScreenRefresh();
            ed.Regen();
        }

        private static void PlaceLayersInAerialGroupAndSendToBack(
            AcMapMap map, IReadOnlyList<MgLayerBase> added, string displayName)
        {
            MgLayerGroupCollection groups = map.GetLayerGroups();
            MgLayerGroup? aerialGroup = null;
            for (int i = 0; i < groups.GetCount(); i++)
            {
                MgLayerGroup g = groups.GetItem(i);
                if (string.Equals(g.GetName(), AerialGroupName, StringComparison.OrdinalIgnoreCase))
                { aerialGroup = g; break; }
            }
            if (aerialGroup == null)
            {
                aerialGroup = new MgLayerGroup(AerialGroupName);
                aerialGroup.SetLegendLabel(AerialGroupName);
                aerialGroup.SetDisplayInLegend(true);
                aerialGroup.SetVisible(true);
                groups.Add(aerialGroup);
            }
            MgLayerCollection layers = map.GetLayers();
            foreach (MgLayerBase layer in added)
            {
                layer.SetGroup(aerialGroup);
                layer.SetDisplayInLegend(true);
                layer.SetVisible(true);
                if (added.Count == 1) layer.SetLegendLabel(displayName);
                int index = layers.IndexOf(layer);
                if (index < 0) throw new InvalidOperationException("Imported aerial disappeared before grouping.");
                if (index != layers.GetCount() - 1)
                {
                    layers.RemoveAt(index);
                    layers.Insert(layers.GetCount(), layer);
                }
            }
        }

        // ============================================================
        // FORCE CAD ENTITIES TO FRONT
        // ============================================================

        private static void BringAllCadEntitiesToFront()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;

            using (var docLock = doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(
                    db.BlockTableId, OpenMode.ForRead);

                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(
                    bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                DrawOrderTable drawOrder = (DrawOrderTable)tr.GetObject(
                    ms.DrawOrderTableId, OpenMode.ForWrite);

                ObjectIdCollection idsToMove = new ObjectIdCollection();

                foreach (ObjectId id in ms)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is Entity ent)
                    {
                        // Leave raster images behind if present
                        if (ent is RasterImage)
                            continue;

                        idsToMove.Add(id);
                    }
                }

                if (idsToMove.Count > 0)
                    drawOrder.MoveToTop(idsToMove);

                tr.Commit();
            }
        }
    }
}
