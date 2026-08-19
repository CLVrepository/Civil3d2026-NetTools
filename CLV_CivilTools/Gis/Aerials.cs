using System;
using System.Collections;
using System.Reflection;
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
        private static bool _pendingViewRestore;
        private static bool _zoomHookAttached;

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

                    // Save current view + hook zoom-ended event
                    SaveCurrentView(ed);
                    AttachZoomEndedHook();
                    _pendingViewRestore = true;

                    // Remove any existing Nearmap layers
                    RemoveExistingNearmapLayers(map);
                TryRemoveNearmapConnections(ed);

                    // Load the .layer file (Map will usually do a ZOOM EXTENTS)
                    map.LoadLayer(layerFilePath);

                    // Put the layer into "Aerial Imagery" group and send to back (within Map)
                    PlaceLayerInAerialGroupAndSendToBack(map, displayName);

                    // Ensure CAD entities are drawn above imagery
                    BringAllCadEntitiesToFront();
                }

                ed.WriteMessage($"\nLoaded Nearmap layer: {displayName}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nError loading aerial: {ex.Message}");
            }
        }

        // ============================================================
        // VIEW RESTORE – AFTER MAP'S AUTO ZOOM
        // ============================================================

        private static void AttachZoomEndedHook()
        {
            if (_zoomHookAttached)
                return;

            AcadApp.DocumentManager.MdiActiveDocument.CommandEnded += OnCommandEnded;
            _zoomHookAttached = true;
        }

        private static void OnCommandEnded(object sender, CommandEventArgs e)
        {
            if (!_pendingViewRestore)
                return;

            if (!string.Equals(e.GlobalCommandName, "ZOOM",
                StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null)
                    return;

                Editor ed = doc.Editor;

                using (var docLock = doc.LockDocument())
                {
                    if (_savedView != null)
                        ed.SetCurrentView(_savedView);
                }
            }
            catch
            {
                // ignore restore errors
            }
            finally
            {
                _pendingViewRestore = false;
            }
        }

        private static void SaveCurrentView(Editor ed)
        {
            ViewTableRecord v = ed.GetCurrentView();

            _savedView = new ViewTableRecord
            {
                CenterPoint = v.CenterPoint,
                Height = v.Height,
                Width = v.Width,
                ViewDirection = v.ViewDirection,
                Target = v.Target,
                ViewTwist = v.ViewTwist
            };
        }

        // ============================================================
        // MAP LAYER MANAGEMENT
        // ============================================================

        /// <summary>
        /// Public helper for palette "Unload Aerials" button.
        /// </summary>
        public static void RemoveAllNearmapLayers()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;

            using (var docLock = doc.LockDocument())
            {
                AcMapMap map = AcMapMap.GetCurrentMap();
                if (map == null)
                    return;

                RemoveExistingNearmapLayers(map);
                TryRemoveNearmapConnections(ed);
            }
        }

        private static void RemoveExistingNearmapLayers(AcMapMap map)
        {
            MgLayerCollection layers = map.GetLayers();
            int count = layers.GetCount();

            for (int i = count - 1; i >= 0; i--)
            {
                MgLayerBase layer = layers.GetItem(i);
                string name = layer.GetName();

                if (name.Contains("LasVegas",
                        StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Nearmap",
                        StringComparison.OrdinalIgnoreCase))
                {
                    layers.RemoveAt(i);
                }
            }
        }

        private static void TryRemoveNearmapConnections(Editor ed)
        {
            try
            {
                Assembly? managedMapApi = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, "ManagedMapApi", StringComparison.OrdinalIgnoreCase));
                if (managedMapApi == null)
                    return;

                Type? hostType = managedMapApi.GetType("Autodesk.Gis.Map.HostMapApplicationServices", throwOnError: false);
                object? mapApp = hostType?.GetProperty("Application", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (mapApp == null)
                    return;

                int removed = 0;
                foreach (string memberName in new[] { "Connections", "ConnectionManager", "FeatureService", "Map" })
                {
                    object? candidate = mapApp.GetType().GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(mapApp);
                    if (candidate != null)
                        removed += RemoveNamedConnectionsFromObject(candidate);
                }

                if (removed > 0)
                    ed.WriteMessage($"\nRemoved {removed} Nearmap data connection(s) from the current map session.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nNearmap connection cleanup skipped: {ex.Message}");
            }
        }

        private static int RemoveNamedConnectionsFromObject(object root)
        {
            int removed = 0;

            foreach (object item in EnumerateUnknown(root).ToList())
            {
                if (!IsNearmapConnectionLike(item))
                    continue;

                if (TryInvokeRemove(root, item))
                    removed++;
            }

            foreach (string memberName in new[] { "Connections", "Items", "Values", "Children" })
            {
                object? child = root.GetType().GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(root);
                if (child != null && !ReferenceEquals(child, root))
                    removed += RemoveNamedConnectionsFromObject(child);
            }

            return removed;
        }

        private static bool IsNearmapConnectionLike(object item)
        {
            foreach (string memberName in new[] { "Name", "ConnectionName", "FeatureSource", "ResourceId", "DisplayName" })
            {
                string? value = TryGetMemberString(item, memberName);
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                if (value.Contains("LasVegas", StringComparison.OrdinalIgnoreCase) ||
                    value.Contains("Nearmap", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string? TryGetMemberString(object source, string memberName)
        {
            try
            {
                object? value = source.GetType().GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(source);
                if (value != null)
                    return value.ToString();
            }
            catch
            {
            }

            try
            {
                MethodInfo? mi = source.GetType().GetMethod("Get" + memberName, BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                object? value = mi?.Invoke(source, null);
                if (value != null)
                    return value.ToString();
            }
            catch
            {
            }

            return null;
        }

        private static bool TryInvokeRemove(object owner, object item)
        {
            foreach (string methodName in new[] { "Remove", "Delete", "Disconnect", "RemoveConnection" })
            {
                foreach (MethodInfo mi in owner.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase)))
                {
                    ParameterInfo[] p = mi.GetParameters();
                    if (p.Length == 1)
                    {
                        try
                        {
                            mi.Invoke(owner, new[] { item });
                            return true;
                        }
                        catch
                        {
                        }
                    }
                }
            }

            return false;
        }

        private static IEnumerable<object> EnumerateUnknown(object source)
        {
            if (source is IEnumerable enumerable)
            {
                foreach (object? item in enumerable)
                {
                    if (item != null)
                        yield return item;
                }
                yield break;
            }

            MethodInfo? getCount = source.GetType().GetMethod("GetCount", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            MethodInfo? getItem = source.GetType().GetMethod("GetItem", BindingFlags.Public | BindingFlags.Instance);
            if (getCount != null && getItem != null)
            {
                int count = Convert.ToInt32(getCount.Invoke(source, null));
                for (int i = 0; i < count; i++)
                {
                    object? item = getItem.Invoke(source, new object[] { i });
                    if (item != null)
                        yield return item;
                }
            }
        }

        private static void PlaceLayerInAerialGroupAndSendToBack(
            AcMapMap map, string layerName)
        {
            MgLayerCollection layers = map.GetLayers();
            MgLayerBase? target = null;
            int targetIndex = -1;

            int layerCount = layers.GetCount();
            for (int i = 0; i < layerCount; i++)
            {
                MgLayerBase lyr = layers.GetItem(i);
                if (string.Equals(lyr.GetName(),
                        layerName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    target = lyr;
                    targetIndex = i;
                    break;
                }
            }

            if (target == null)
                return;

            // Ensure "Aerial Imagery" group exists
            MgLayerGroupCollection groups = map.GetLayerGroups();
            MgLayerGroup? aerialGroup = null;

            for (int i = 0; i < groups.GetCount(); i++)
            {
                var g = groups.GetItem(i);
                if (string.Equals(g.GetName(),
                        AerialGroupName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    aerialGroup = g;
                    break;
                }
            }

            if (aerialGroup == null)
            {
                aerialGroup = new MgLayerGroup(AerialGroupName);
                aerialGroup.SetLegendLabel(AerialGroupName);
                aerialGroup.SetDisplayInLegend(true);
                aerialGroup.SetVisible(true);
                groups.Add(aerialGroup);
            }

            // Put layer under the group, show in legend
            target.SetGroup(aerialGroup);
            target.SetDisplayInLegend(true);
            target.SetLegendLabel(layerName);

            // Send to back within Map's layer collection
            int lastIndex = layerCount - 1;

            if (targetIndex >= 0 && targetIndex != lastIndex)
            {
                layers.RemoveAt(targetIndex);
                layers.Insert(lastIndex, target);
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