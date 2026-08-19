using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

using Autodesk.Gis.Map.Platform;
using OSGeo.MapGuide;

namespace CLV_CivilTools.Gis
{
    public static class GisReferenceLayers
    {
        public const string ReferenceLayerFolder =
            @"\\ci.las-vegas.nv.us\pw_data_depot\PW_GRID_City\000-City\FDO";

        private static readonly string[] CoordinateZoneLayerFiles =
        {
            "NV83.NCRS-LVF.layer",
            "NV83.NCRS-LVHEF.layer"
        };

        private static readonly string[] CoordinateZoneLayerNames =
        {
            "NV83.NCRS-LVF",
            "NV83.NCRS-LVHEF"
        };

        private const string ReferenceGroupName = "Map Base";

        private static ViewTableRecord? _savedView;

        [CommandMethod("CLV-GIS-LOAD-REFERENCE-LAYERS")]
        [CommandMethod("CLV-GIS-DISPLAY-COORDINATE-ZONES")]
        public static void LoadReferenceLayersCommand()
        {
            LoadReferenceLayers();
        }

        [CommandMethod("CLV-GIS-UNLOAD-REFERENCE-LAYERS")]
        [CommandMethod("CLV-GIS-UNLOAD-COORDINATE-ZONES")]
        public static void UnloadReferenceLayersCommand()
        {
            RemoveReferenceLayers();
        }

        public static void LoadReferenceLayers()
        {
            LoadReferenceLayerSet(
                CoordinateZoneLayerFiles,
                CoordinateZoneLayerNames,
                "coordinate zone reference layer files");
        }

        public static void RemoveReferenceLayers()
        {
            RemoveReferenceLayerSet(
                CoordinateZoneLayerNames,
                "coordinate-zone reference layers");
        }

        internal static void LoadReferenceLayerSet(
            string[] layerFiles,
            string[] layerNames,
            string displayName)
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;

            try
            {
                string[] fullPaths = layerFiles
                    .Select(fileName => Path.Combine(ReferenceLayerFolder, fileName))
                    .ToArray();

                string[] missing = fullPaths.Where(path => !File.Exists(path)).ToArray();
                if (missing.Length > 0)
                {
                    ed.WriteMessage("\nReference layer file(s) not found:");
                    foreach (string path in missing)
                        ed.WriteMessage($"\n - {path}");
                    return;
                }

                using (doc.LockDocument())
                {
                    AcMapMap map = AcMapMap.GetCurrentMap();
                    if (map == null)
                    {
                        ed.WriteMessage("\nAcMapMap.GetCurrentMap() returned null.");
                        return;
                    }

                    SaveCurrentView(ed);

                    RemoveExistingReferenceLayers(map, layerNames);
                    TryRemoveReferenceConnections(ed, layerNames);

                    foreach (string path in fullPaths)
                    {
                        map.LoadLayer(path);
                    }

                    PlaceReferenceLayersInGroup(map, layerNames);
                    BringAllCadEntitiesToFront();
                    RestoreSavedView(ed);
                }

                ed.WriteMessage($"\nLoaded {displayName}:");
                foreach (string layerName in layerNames)
                    ed.WriteMessage($"\n - {layerName}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nError loading {displayName}: {ex.Message}");
            }
        }

        internal static void RemoveReferenceLayerSet(
            string[] layerNames,
            string displayName)
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;

            try
            {
                using (doc.LockDocument())
                {
                    AcMapMap map = AcMapMap.GetCurrentMap();
                    if (map == null)
                    {
                        ed.WriteMessage("\nAcMapMap.GetCurrentMap() returned null.");
                        return;
                    }

                    RemoveExistingReferenceLayers(map, layerNames);
                    TryRemoveReferenceConnections(ed, layerNames);
                }

                ed.WriteMessage($"\nRemoved {displayName}.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nError unloading {displayName}: {ex.Message}");
            }
        }

        private static void SaveCurrentView(Editor ed)
        {
            ViewTableRecord view = ed.GetCurrentView();
            _savedView = new ViewTableRecord
            {
                CenterPoint = view.CenterPoint,
                Height = view.Height,
                Width = view.Width,
                ViewDirection = view.ViewDirection,
                Target = view.Target,
                ViewTwist = view.ViewTwist
            };
        }

        private static void RestoreSavedView(Editor ed)
        {
            if (_savedView == null)
                return;

            var restore = new ViewTableRecord
            {
                CenterPoint = _savedView.CenterPoint,
                Height = _savedView.Height,
                Width = _savedView.Width,
                ViewDirection = _savedView.ViewDirection,
                Target = _savedView.Target,
                ViewTwist = _savedView.ViewTwist
            };

            ed.SetCurrentView(restore);
        }

        private static void RemoveExistingReferenceLayers(AcMapMap map, string[] layerNames)
        {
            MgLayerCollection layers = map.GetLayers();
            for (int i = layers.GetCount() - 1; i >= 0; i--)
            {
                MgLayerBase layer = layers.GetItem(i);
                string name = layer.GetName();

                if (IsReferenceLayerName(name, layerNames))
                    layers.RemoveAt(i);
            }
        }

        private static bool IsReferenceLayerName(string? name, string[] layerNames)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            return layerNames.Any(referenceName =>
                name.IndexOf(referenceName, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void PlaceReferenceLayersInGroup(AcMapMap map, string[] layerNames)
        {
            MgLayerCollection layers = map.GetLayers();
            MgLayerGroupCollection groups = map.GetLayerGroups();

            MgLayerGroup? group = null;
            for (int i = 0; i < groups.GetCount(); i++)
            {
                MgLayerGroup candidate = groups.GetItem(i);
                if (string.Equals(candidate.GetName(), ReferenceGroupName, StringComparison.OrdinalIgnoreCase))
                {
                    group = candidate;
                    break;
                }
            }

            if (group == null)
            {
                group = new MgLayerGroup(ReferenceGroupName);
                group.SetLegendLabel(ReferenceGroupName);
                group.SetDisplayInLegend(true);
                group.SetVisible(true);
                groups.Add(group);
            }

            for (int i = 0; i < layers.GetCount(); i++)
            {
                MgLayerBase layer = layers.GetItem(i);
                if (!IsReferenceLayerName(layer.GetName(), layerNames))
                    continue;

                layer.SetGroup(group);
                layer.SetDisplayInLegend(true);
                layer.SetLegendLabel(layer.GetName());
            }
        }

        private static void TryRemoveReferenceConnections(Editor ed, string[] layerNames)
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
                        removed += RemoveNamedConnectionsFromObject(candidate, layerNames);
                }

                if (removed > 0)
                    ed.WriteMessage($"\nRemoved {removed} reference layer data connection(s) from the current map session.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nReference layer connection cleanup skipped: {ex.Message}");
            }
        }

        private static int RemoveNamedConnectionsFromObject(object root, string[] layerNames)
        {
            int removed = 0;

            foreach (object item in EnumerateUnknown(root).ToList())
            {
                if (!IsReferenceConnectionLike(item, layerNames))
                    continue;

                if (TryInvokeRemove(root, item))
                    removed++;
            }

            foreach (string memberName in new[] { "Connections", "Items", "Values", "Children" })
            {
                object? child = root.GetType().GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(root);
                if (child != null && !ReferenceEquals(child, root))
                    removed += RemoveNamedConnectionsFromObject(child, layerNames);
            }

            return removed;
        }

        private static bool IsReferenceConnectionLike(object item, string[] layerNames)
        {
            foreach (string memberName in new[] { "Name", "ConnectionName", "FeatureSource", "ResourceId", "DisplayName" })
            {
                string? value = TryGetMemberString(item, memberName);
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                if (IsReferenceLayerName(value, layerNames))
                    return true;
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
                foreach (MethodInfo method in owner.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase)))
                {
                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length != 1)
                        continue;

                    try
                    {
                        method.Invoke(owner, new[] { item });
                        return true;
                    }
                    catch
                    {
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
            if (getCount == null || getItem == null)
                yield break;

            int count = Convert.ToInt32(getCount.Invoke(source, null));
            for (int i = 0; i < count; i++)
            {
                object? item = getItem.Invoke(source, new object[] { i });
                if (item != null)
                    yield return item;
            }
        }

        private static void BringAllCadEntitiesToFront()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;

            using (doc.LockDocument())
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                DrawOrderTable drawOrder = (DrawOrderTable)tr.GetObject(ms.DrawOrderTableId, OpenMode.ForWrite);

                ObjectIdCollection idsToMove = new ObjectIdCollection();
                foreach (ObjectId id in ms)
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is Entity entity && entity is not RasterImage)
                        idsToMove.Add(id);
                }

                if (idsToMove.Count > 0)
                    drawOrder.MoveToTop(idsToMove);

                tr.Commit();
            }
        }
    }
}
