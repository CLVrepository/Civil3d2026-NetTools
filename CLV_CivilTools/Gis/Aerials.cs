using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

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

        private static AerialLoadSession? _loadSession;

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
        public static async Task LoadAerialFromPaletteAsync(string layerFilePath)
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            ViewTableRecord requestedView;
            using (doc.LockDocument()) requestedView = doc.Editor.GetCurrentView();
            using var requestedViewLifetime = requestedView;
            await AcadApp.DocumentManager.ExecuteInCommandContextAsync(_ =>
            {
                if (!ReferenceEquals(doc, AcadApp.DocumentManager.MdiActiveDocument))
                    throw new InvalidOperationException("The drawing changed before the aerial load started. Select the aerial again in the intended drawing.");
                LoadAerialCore(layerFilePath, requestedView);
                return Task.CompletedTask;
            }, null);
        }

        public static void LoadAerialFromFile(string layerFilePath) => LoadAerialCore(layerFilePath, null);

        private static void LoadAerialCore(string layerFilePath, ViewTableRecord? requestedView)
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

                    _loadSession?.Dispose();
                    bool previousSynchronousLoading = map.GetSynchronousLoading();
                    var session = new AerialLoadSession(doc, map, requestedView);
                    _loadSession = session;
                    try
                    {
                        map.SetSynchronousLoading(true);
                        RemoveExistingNearmapLayers(map, keepAerialGroup: true);
                        HashSet<string> before = LayerIds(map);
                        HashSet<string> groupsBefore = GroupIds(map);
                        map.LoadLayer(layerFilePath);
                        // Native loading may continue after LoadLayer returns.
                        session.Returned(() =>
                        {
                            List<MgLayerBase> added = Layers(map).Where(l => !before.Contains(l.GetObjectId())).ToList();
                            if (added.Count == 0) throw new InvalidOperationException("The import has no new aerial display layers.");
                            session.MarkStage("grouping");
                            MgLayerGroup aerialGroup = GroupAerialLayers(map, added, displayName);
                            session.MarkStage("pruning");
                            PruneEmptyGroups(map, GroupIds(map).Where(id => !groupsBefore.Contains(id)).ToHashSet(StringComparer.Ordinal), keepAerialGroup: true);
                            session.MarkStage("ordering");
                            AerialDisplayOrder.EnsureMapBaseFirst(doc.Database, map, aerialGroup, added);
                            if (added.Any(layer => layer.GetGroup()?.GetObjectId() != aerialGroup.GetObjectId()))
                                throw new InvalidOperationException("Aerial grouping changed during draw-order repair.");
                            session.MarkStage("refreshing");
                            BringAllCadEntitiesToFront();
                            RefreshMap(ed);
                        }, () =>
                        {
                            List<MgLayerBase> added = Layers(map).Where(l => !before.Contains(l.GetObjectId())).ToList();
                            return added.Count > 0 && added.All(l => l is AcMapLayer layer && layer.GetIsLoaded());
                        });
                        // Essential work runs in this real CAD command context, not in a
                        // timer that can wait for the very command context awaiting it.
                        if (Layers(map).Any(l => !before.Contains(l.GetObjectId()))) session.ApplyInitial();
                        session.QueueCompletion();
                    }
                    catch
                    {
                        try { session.RestoreNow(); }
                        finally { session.Dispose(); }
                        throw;
                    }
                    finally
                    {
                        try { map.SetSynchronousLoading(previousSynchronousLoading); }
                        finally { session.RestoreNow(); }
                    }
                }

                ed.WriteMessage($"\n[AERIAL-R7] Imported {displayName}; completion command queued.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nError loading aerial: {ex.Message}");
            }
        }

        // ============================================================
        // MAP LAYER MANAGEMENT
        // ============================================================

        /// <summary>
        /// Public helper for palette "Unload Aerials" button.
        /// </summary>
        [CommandMethod("NM_AERIAL_FINISH", CommandFlags.Modal)]
        public static void FinishAerialCommand()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            PromptResult token = doc.Editor.GetString(new PromptStringOptions("\n") { AllowSpaces = false });
            if (token.Status != PromptStatus.OK || _loadSession == null || token.StringResult != _loadSession.RequestId || !_loadSession.MatchesDocument(doc)) return;
            using (doc.LockDocument()) _loadSession.FinishFromCommand();
        }

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
                _loadSession?.Dispose(); _loadSession = null;
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

        private static int RemoveExistingNearmapLayers(AcMapMap map, bool keepAerialGroup = false)
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

            PruneEmptyGroups(map, new HashSet<string>(StringComparer.Ordinal), keepAerialGroup);
            return removed;
        }

        private static HashSet<string> GroupIds(AcMapMap map)
        {
            MgLayerGroupCollection groups = map.GetLayerGroups();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < groups.GetCount(); i++) ids.Add(groups.GetItem(i).GetObjectId());
            return ids;
        }

        private static void PruneEmptyGroups(AcMapMap map, HashSet<string> importedGroups, bool keepAerialGroup)
        {
            MgLayerGroupCollection groups = map.GetLayerGroups();
            string? keepId = null;
            if (keepAerialGroup)
                for (int i = 0; i < groups.GetCount(); i++)
                {
                    MgLayerGroup g = groups.GetItem(i);
                    if (AerialLayerIdentity.IsAerialGroup(g.GetName(), g.GetLegendLabel())) { keepId = g.GetObjectId(); break; }
                }
            bool changed;
            do
            {
                changed = false;
                for (int i = groups.GetCount() - 1; i >= 0; i--)
                {
                    MgLayerGroup g = groups.GetItem(i);
                    bool owned = AerialLayerIdentity.IsAerialGroup(g.GetName(), g.GetLegendLabel());
                    if (g.GetObjectId() == keepId || string.Equals(g.GetName(), "Map Base", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!owned && !importedGroups.Contains(g.GetObjectId()) && !string.IsNullOrWhiteSpace(g.GetLegendLabel())) continue;
                    string id = g.GetObjectId();
                    bool used = Layers(map).Any(l => l.GetGroup()?.GetObjectId() == id);
                    for (int j = 0; j < groups.GetCount() && !used; j++) used = groups.GetItem(j).GetGroup()?.GetObjectId() == id;
                    Database? db = AcadApp.DocumentManager.MdiActiveDocument?.Database;
                    if (!used && db != null && AerialDisplayOrder.IsGroupEmpty(db, g)) { groups.RemoveAt(i); changed = true; }
                }
            } while (changed);
        }

        private static void RefreshMap(Editor ed)
        {
            AcMapMap.ForceScreenRefresh();
            ed.Regen();
        }

        private static MgLayerGroup GroupAerialLayers(
            AcMapMap map, IReadOnlyList<MgLayerBase> added, string displayName)
        {
            MgLayerGroupCollection groups = map.GetLayerGroups();
            MgLayerGroup? aerialGroup = null;
            for (int i = 0; i < groups.GetCount(); i++)
            {
                MgLayerGroup g = groups.GetItem(i);
                if (AerialLayerIdentity.IsAerialGroup(g.GetName(), g.GetLegendLabel()))
                { aerialGroup = g; break; }
            }
            if (aerialGroup == null)
            {
                aerialGroup = new AcMapLayerGroup(AerialGroupName);
                aerialGroup.SetLegendLabel(AerialGroupName);
                aerialGroup.SetDisplayInLegend(true);
                aerialGroup.SetVisible(true);
                groups.Add(aerialGroup);
            }
            aerialGroup.SetLegendLabel(AerialGroupName);
            if (aerialGroup.GetGroup() != null) aerialGroup.SetGroup(null);
            aerialGroup.SetDisplayInLegend(true);
            aerialGroup.SetVisible(true);
            foreach (MgLayerBase layer in added)
            {
                if (layer.GetGroup()?.GetObjectId() != aerialGroup.GetObjectId()) layer.SetGroup(aerialGroup);
                layer.SetDisplayInLegend(true);
                layer.SetVisible(true);
                if (added.Count == 1) layer.SetLegendLabel(displayName);

            }
            return aerialGroup;
        }

        [CommandMethod("NM_AERIAL_STATE", CommandFlags.Modal)]
        public static void ReportAerialState()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                using (doc.LockDocument())
                {
                    AcMapMap map = AcMapMap.GetCurrentMap();
                    using ViewTableRecord view = doc.Editor.GetCurrentView();
                    doc.Editor.WriteMessage("\n[AERIAL-R7] " + typeof(Aerials).Assembly.Location + " | " + AerialLoadSession.ViewText(view));
                    doc.Editor.WriteMessage("\nSESSION " + (_loadSession?.Status ?? "none"));
                    if (_loadSession?.MatchesDocument(doc) == true) doc.Editor.WriteMessage("\nLAST LOAD TRACE\n" + _loadSession.Trace);
                    MgLayerGroupCollection groups = map.GetLayerGroups();
                    for (int i = 0; i < groups.GetCount(); i++)
                    {
                        MgLayerGroup g = groups.GetItem(i);
                        doc.Editor.WriteMessage("\nGROUP " + i + " id=" + g.GetObjectId() + " name='" + g.GetName() + "' label='" + g.GetLegendLabel() + "' parent=" + g.GetGroup()?.GetObjectId());
                    }
                    foreach (MgLayerBase layer in Layers(map))
                        doc.Editor.WriteMessage("\nLAYER id=" + layer.GetObjectId() + " name='" + layer.GetName() + "' label='" + layer.GetLegendLabel() + "' parent=" + layer.GetGroup()?.GetObjectId());
                    AerialDisplayOrder.Report(doc.Database, map, text => doc.Editor.WriteMessage("\n" + text));
                }
            }
            catch (System.Exception ex) { doc.Editor.WriteMessage("\n[AERIAL-R7] State read failed: " + ex.Message); }
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

