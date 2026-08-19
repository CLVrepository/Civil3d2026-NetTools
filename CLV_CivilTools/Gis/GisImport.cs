using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CLV_CivilTools.Shared;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcPolyline = Autodesk.AutoCAD.DatabaseServices.Polyline;
using WinFlowDirection = System.Windows.Forms.FlowDirection;

namespace CLV_CivilTools.Gis
{
    public static class GisImportCommands
    {
        private const string SourceCoordinateSystem = "NAD_1983_StatePlane_Nevada_East_FIPS_2701_Feet";
        private const string TempBoundaryLayer = "GIS-TEMP-BOUNDARY";
        private const string ManagedMapApiAssemblyName = "ManagedMapApi";
        private const string CacheRootFolder = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_GRID_City\000-City\GIS\_CLV_CACHE";
        private const string LayerMasterReferencePath = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_GRID_City\000-City\GIS\_CLV_CACHE\_MASTER\CLV_GIS_LAYER_MASTER.dwg";

        [CommandMethod("CLV-GIS-IMPORT", CommandFlags.Modal)]
        public static void ImportGisData()
        {
            RunInteractive(fromPalette: false);
        }

        [CommandMethod("CLV-GIS-CLEANUP", CommandFlags.Modal)]
        public static void CleanupImportedGisLinework()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            CleanupImportedGisLinework(doc.Editor, doc.Database);
        }

        [CommandMethod("CLV-GIS-CACHE-STATUS", CommandFlags.Modal)]
        public static void ReportGisCacheStatus()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            string? rawDrawingCs = TryGetRawDrawingCoordinateSystem();
            string drawingCs = NormalizeCoordinateSystemKey(rawDrawingCs);
            var ed = doc.Editor;

            ed.WriteMessage("\nCLV-GIS-CACHE-STATUS");
            ed.WriteMessage($"\n  Cache root    : {CacheRootFolder}");
            ed.WriteMessage($"\n  Drawing CS    : {drawingCs}");
            ed.WriteMessage($"\n  Raw Map CS    : {(string.IsNullOrWhiteSpace(rawDrawingCs) ? "<not detected>" : rawDrawingCs)}");

            foreach (var dataset in GisImportDataset.All)
            {
                string cachePath = GetCachePathForDataset(dataset, drawingCs);
                ed.WriteMessage($"\n  {dataset.DisplayName}: {(File.Exists(cachePath) ? "FOUND" : "MISSING")} -> {cachePath}");
            }

            ed.WriteMessage("\nCache workflow:");
            ed.WriteMessage("\n  1. Build one clean DWG per dataset and target coordinate system.");
            ed.WriteMessage("\n  2. Import and transform the GIS data once into that DWG.");
            ed.WriteMessage("\n  3. Put entities on the final CLV GIS layer for that dataset.");
            ed.WriteMessage("\n  4. Save to the cache path shown above. CLV-GIS-IMPORT will use it automatically when present.");
            ed.WriteMessage($"\n  Layer master: {(File.Exists(LayerMasterReferencePath) ? "FOUND" : "MISSING")} -> {LayerMasterReferencePath}");
        }

        [CommandMethod("CLV-GIS-OD-INSPECT", CommandFlags.Modal)]
        public static void InspectObjectDataCommand()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            using var docLock = doc.LockDocument();
            InspectObjectData(doc.Editor, doc.Database);
        }

        internal static bool TryGetObjectDataFieldValue(ObjectId entityId, string fieldName, out string? value, out string? tableName)
        {
            value = null;
            tableName = null;

            if (!TryReadEntityObjectDataSnapshots(entityId, out var snapshots, out _))
                return false;

            foreach (ObjectDataTableSnapshot snapshot in snapshots)
            {
                string? candidate = GetFieldValueFromSnapshot(snapshot, fieldName);
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                value = candidate;
                tableName = snapshot.TableName;
                return true;
            }

            return false;
        }

        internal static void RunFromPalette()
        {
            RunInteractive(fromPalette: true);
        }

        private static void RunInteractive(bool fromPalette)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            using var dlg = new GisImportOptionsForm();
            if (AcadApp.ShowModalDialog(dlg) != DialogResult.OK)
                return;

            var options = dlg.BuildOptions();
            if (!options.HasAnyDatasetSelected)
            {
                doc.Editor.WriteMessage("\nCLV-GIS-IMPORT: no GIS datasets were selected.");
                return;
            }

            ObjectId boundaryId = ObjectId.Null;
            bool eraseBoundaryWhenDone = false;

            try
            {
                boundaryId = options.BoundaryMode == GisBoundaryMode.SelectExistingPolyline
                    ? PromptForBoundaryPolyline(doc)
                    : PromptForTemporaryBoundaryPolygon(doc);

                if (boundaryId == ObjectId.Null)
                {
                    doc.Editor.WriteMessage("\nCLV-GIS-IMPORT cancelled.");
                    return;
                }

                eraseBoundaryWhenDone = options.BoundaryMode == GisBoundaryMode.DrawTemporaryPolygon;
                RunAutomaticImport(doc, options, boundaryId, eraseBoundaryWhenDone, fromPalette);
            }
            finally
            {
                if (eraseBoundaryWhenDone && boundaryId != ObjectId.Null)
                {
                    TryEraseEntity(doc.Database, boundaryId);
                }
            }
        }

        private static void RunAutomaticImport(Document doc, GisImportOptions options, ObjectId boundaryId, bool eraseBoundaryWhenDone, bool fromPalette)
        {
            using var docLock = doc.LockDocument();

            var ed = doc.Editor;
            var db = doc.Database;

            EnsureImportLayers(ed, db);

            string drawingCs = InferDrawingCoordinateSystem();
            var selected = options.SelectedDatasets.ToList();
            int importedEntities = 0;
            int keptEntities = 0;
            int clippedEntities = 0;
            int erasedOutside = 0;
            int failedDatasets = 0;

            ed.WriteMessage("\nCLV-GIS-IMPORT starting automatic import...");
            ed.WriteMessage($"\n  Source CS  : {SourceCoordinateSystem}");
            ed.WriteMessage($"\n  Target CS  : {drawingCs}");
            ed.WriteMessage("\n  Boundary note: cache DWGs are expected to already be in the active drawing coordinate system. Imported cache entities are clipped to the selected boundary after clone.");
            ed.WriteMessage("\n  Layer note   : target GIS layers are synced from the CLV GIS layer master when available, then corrected with CLV fallback settings.");

            foreach (var dataset in selected)
            {
                try
                {
                    ImportRunResult result;
                    if (TryGetExistingCachePath(dataset, drawingCs, out string? cachePath))
                    {
                        ed.WriteMessage($"\n  {dataset.DisplayName}: using cache {cachePath}");
                        result = ImportDatasetFromCacheDrawing(doc, dataset, boundaryId, cachePath!);
                    }
                    else
                    {
                        if (dataset.CacheOnly)
                        {
                            ed.WriteMessage($"\n  {dataset.DisplayName}: cache not found, skipped.");
                            failedDatasets++;
                            continue;
                        }

                        ed.WriteMessage($"\n  {dataset.DisplayName}: cache not found, falling back to direct SHP import.");

                        if (!File.Exists(dataset.ShapefilePath))
                        {
                            ed.WriteMessage($"\n  {dataset.DisplayName}: shapefile not found, skipped.");
                            failedDatasets++;
                            continue;
                        }

                        result = ImportDatasetViaMapApi(doc, dataset, boundaryId, drawingCs);
                    }

                    importedEntities += result.ImportedCount;
                    keptEntities += result.KeptCount;
                    clippedEntities += result.ClippedCount;
                    erasedOutside += result.ErasedOutsideCount;

                    ed.WriteMessage($"\n  {dataset.DisplayName}: imported {result.ImportedCount}, kept {result.KeptCount}, clipped {result.ClippedCount}, erased outside {result.ErasedOutsideCount}.");
                }
                catch (System.Exception ex)
                {
                    failedDatasets++;
                    ed.WriteMessage($"\n  {dataset.DisplayName}: import failed - {ex.Message}");
                }
            }

            if (options.RunCleanupAfterImport)
            {
                CleanupImportedGisLinework(ed, db);
            }

            ed.WriteMessage("\nCLV-GIS-IMPORT finished.");
            ed.WriteMessage($"\n  Datasets attempted : {selected.Count}");
            ed.WriteMessage($"\n  Datasets failed    : {failedDatasets}");
            ed.WriteMessage($"\n  Imported entities  : {importedEntities}");
            ed.WriteMessage($"\n  Kept entities      : {keptEntities}");
            ed.WriteMessage($"\n  Clipped entities   : {clippedEntities}");
            ed.WriteMessage($"\n  Erased outside     : {erasedOutside}");

            if (fromPalette)
            {
                ed.WriteMessage("\nPalette import run complete.");
            }
        }

        private static bool TryGetExistingCachePath(GisImportDataset dataset, string drawingCs, out string? cachePath)
        {
            cachePath = GetCachePathForDataset(dataset, drawingCs);
            return File.Exists(cachePath);
        }

        private static string GetCachePathForDataset(GisImportDataset dataset, string drawingCs)
        {
            string csFolder = SanitizePathSegment(NormalizeCoordinateSystemKey(drawingCs));
            return Path.Combine(CacheRootFolder, csFolder, dataset.CacheFileName);
        }

        private static string SanitizePathSegment(string raw)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var chars = raw.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
            return new string(chars);
        }

        private static ImportRunResult ImportDatasetFromCacheDrawing(Document doc, GisImportDataset dataset, ObjectId boundaryId, string cachePath)
        {
            var db = doc.Database;
            Extents3d boundaryExtents = GetBoundaryExtents(db, boundaryId);
            var sourceIds = new ObjectIdCollection();

            using var sourceDb = new Database(false, true);
            sourceDb.ReadDwgFile(cachePath, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
            sourceDb.CloseInput(true);

            if (!dataset.IsLinework)
                TryApplyPointDisplayFromSourceCache(doc.Editor, sourceDb);

            using (var sourceTr = sourceDb.TransactionManager.StartTransaction())
            {
                var sourceBt = (BlockTable)sourceTr.GetObject(sourceDb.BlockTableId, OpenMode.ForRead);
                var sourceMs = (BlockTableRecord)sourceTr.GetObject(sourceBt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId sourceId in sourceMs)
                {
                    if (!sourceId.IsValid || sourceId.IsErased)
                        continue;

                    if (!(sourceTr.GetObject(sourceId, OpenMode.ForRead, false) is Entity sourceEnt))
                        continue;

                    bool layerMatches = dataset.ImportAllFromCache
                        || string.IsNullOrWhiteSpace(dataset.TargetLayer)
                        || string.Equals(sourceEnt.Layer, dataset.TargetLayer, StringComparison.OrdinalIgnoreCase);

                    if (!layerMatches)
                        continue;

                    if (EntityMayIntersectExtents(sourceEnt, boundaryExtents))
                        sourceIds.Add(sourceId);
                }

                sourceTr.Commit();
            }

            if (sourceIds.Count == 0)
                return new ImportRunResult();

            var newIds = new List<ObjectId>(sourceIds.Count);
            var mapping = new IdMapping();

            using (var targetTr = db.TransactionManager.StartTransaction())
            {
                var targetBt = (BlockTable)targetTr.GetObject(db.BlockTableId, OpenMode.ForRead);
                ObjectId targetModelSpaceId = targetBt[BlockTableRecord.ModelSpace];
                sourceDb.WblockCloneObjects(sourceIds, targetModelSpaceId, mapping, DuplicateRecordCloning.Ignore, false);
                targetTr.Commit();
            }

            foreach (IdPair pair in mapping)
            {
                if (pair.IsCloned && pair.Value.IsValid && !pair.Value.IsErased)
                    newIds.Add(pair.Value);
            }

            if (newIds.Count == 0)
                return new ImportRunResult();

            ClipRunResult clipResult = ClipImportedObjectsToBoundary(db, boundaryId, newIds);

            return new ImportRunResult
            {
                ImportedCount = newIds.Count,
                KeptCount = clipResult.KeptCount,
                ClippedCount = clipResult.ClippedCount,
                ErasedOutsideCount = clipResult.ErasedOutsideCount,
            };
        }

        private static Extents3d GetBoundaryExtents(Database db, ObjectId boundaryId)
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (!(tr.GetObject(boundaryId, OpenMode.ForRead) is AcPolyline boundary))
                throw new InvalidOperationException("Boundary polyline could not be read.");

            Extents3d extents = boundary.GeometricExtents;
            tr.Commit();
            return extents;
        }

        private static bool EntityMayIntersectExtents(Entity ent, Extents3d boundaryExtents)
        {
            try
            {
                return ExtentsIntersect(ent.GeometricExtents, boundaryExtents);
            }
            catch
            {
                return true;
            }
        }

        private static bool ExtentsIntersect(Extents3d a, Extents3d b)
        {
            return a.MinPoint.X <= b.MaxPoint.X && a.MaxPoint.X >= b.MinPoint.X &&
                   a.MinPoint.Y <= b.MaxPoint.Y && a.MaxPoint.Y >= b.MinPoint.Y &&
                   a.MinPoint.Z <= b.MaxPoint.Z && a.MaxPoint.Z >= b.MinPoint.Z;
        }

        private static ImportRunResult ImportDatasetViaMapApi(Document doc, GisImportDataset dataset, ObjectId boundaryId, string drawingCs)
        {
            var db = doc.Database;
            var ed = doc.Editor;

            var before = SnapshotModelSpaceHandles(db);

            object importer = CreateImporter();
            ConfigureImporter(importer, dataset, drawingCs);

            bool imported = TryInvoke(importer, "Import") || TryInvoke(importer, "Import", true);
            if (!imported)
                throw new InvalidOperationException("ManagedMapApi importer did not execute. Verify Map 3D import API is available.");

            var newIds = GetNewModelSpaceObjectIds(db, before);
            if (newIds.Count == 0)
            {
                ed.WriteMessage($"\n  {dataset.DisplayName}: importer completed but no new drawing objects were detected.");
                return new ImportRunResult();
            }

            ReassignImportedLayers(db, newIds, dataset.TargetLayer);
            var clipResult = ClipImportedObjectsToBoundary(db, boundaryId, newIds);

            return new ImportRunResult
            {
                ImportedCount = newIds.Count,
                KeptCount = clipResult.KeptCount,
                ClippedCount = clipResult.ClippedCount,
                ErasedOutsideCount = clipResult.ErasedOutsideCount,
            };
        }

        private static object CreateImporter()
        {
            Assembly asm = LoadManagedMapApiAssembly();
            Type hostType = asm.GetType("Autodesk.Gis.Map.HostMapApplicationServices", throwOnError: true)!;
            object? mapApp = hostType.GetProperty("Application", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (mapApp == null)
                throw new InvalidOperationException("Unable to access HostMapApplicationServices.Application.");

            object? importer = mapApp.GetType().GetProperty("Importer", BindingFlags.Public | BindingFlags.Instance)?.GetValue(mapApp);
            if (importer == null)
                throw new InvalidOperationException("Unable to access ManagedMapApi Importer.");

            return importer;
        }

        private static Assembly LoadManagedMapApiAssembly()
        {
            try
            {
                return Assembly.Load(ManagedMapApiAssemblyName);
            }
            catch
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (string.Equals(asm.GetName().Name, ManagedMapApiAssemblyName, StringComparison.OrdinalIgnoreCase))
                        return asm;
                }
                throw;
            }
        }

        private static void ConfigureImporter(object importer, GisImportDataset dataset, string drawingCs)
        {
            if (!TryInvoke(importer, "Init", "SHP", dataset.ShapefilePath))
            {
                string[] single = { dataset.ShapefilePath };
                if (!TryInvoke(importer, "Init", "SHP", single))
                    throw new InvalidOperationException("Unable to initialize ManagedMapApi importer for SHP format.");
            }

            TryConfigureDriverOptions(importer, dataset);

            foreach (object inputLayer in Enumerate(importer))
            {
                ConfigureInputLayer(inputLayer, dataset, drawingCs);
            }
        }

        private static void TryConfigureDriverOptions(object importer, GisImportDataset dataset)
        {
            object? options = TryInvokeWithResult(importer, "DriverOptions");
            if (options == null)
                options = GetPropertyValue(importer, "DriverOptions");
            if (options == null)
                return;

            SetNameValueOption(options, "ImportPolygonsAsClosedPolylines", true);
            SetNameValueOption(options, "UseInputLayerNameForTableName", true);
            SetNameValueOption(options, "ImportAttributeData", true);
            SetNameValueOption(options, "AttributeData", true);
            SetNameValueOption(options, "CoordinateSystem", SourceCoordinateSystem);

            if (!dataset.IsLinework)
            {
                SetNameValueOption(options, "PointMode", "ACAD_POINT");
                SetNameValueOption(options, "PointToBlockMapping", "ACAD_POINT");
            }

            TryInvoke(importer, "SetDriverOptions", options);
        }

        private static void ConfigureInputLayer(object inputLayer, GisImportDataset dataset, string drawingCs)
        {
            SetBoolSwitch(inputLayer, "ImportFromInputLayerOn", true);
            SetStringValue(inputLayer, "TargetCoordSys", drawingCs);
            SetStringValue(inputLayer, "OriginalCoordSys", SourceCoordinateSystem);

            if (!dataset.IsLinework)
            {
                TrySetEnumChoice(inputLayer, "SetPointToBlockMapping", new[] { "AcadPoint", "ACAD_POINT", "Point", "PointEntity" });
            }

            TryConfigureObjectDataMapping(inputLayer, dataset.ObjectDataTableName);
        }

        private static void TryConfigureObjectDataMapping(object inputLayer, string objectDataTableName)
        {
            MethodInfo[] methods = inputLayer.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => string.Equals(m.Name, "SetDataMapping", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                if (parameters.Length == 2 && parameters[1].ParameterType == typeof(string))
                {
                    object? enumValue = CreateEnumCandidate(parameters[0].ParameterType,
                        "NewObjectDataOnly", "ObjectData", "NewObjectData", "ExistingObjectData", "ExistingObjectDataOnly");
                    if (enumValue != null)
                    {
                        try
                        {
                            method.Invoke(inputLayer, new[] { enumValue, objectDataTableName });
                            break;
                        }
                        catch
                        {
                            // try next overload / candidate
                        }
                    }
                }
                else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string))
                {
                    try
                    {
                        method.Invoke(inputLayer, new object[] { objectDataTableName });
                        break;
                    }
                    catch
                    {
                        // try next overload
                    }
                }
            }

            foreach (object column in EnumerateColumns(inputLayer))
            {
                TryConfigureColumnMapping(column);
            }
        }

        private static void TryConfigureColumnMapping(object column)
        {
            string? columnName = Convert.ToString(GetPropertyValue(column, "ColumnName") ?? GetPropertyValue(column, "Name"), CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(columnName))
                return;

            foreach (MethodInfo method in column.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => string.Equals(m.Name, "SetColumnDataMapping", StringComparison.OrdinalIgnoreCase)))
            {
                var parameters = method.GetParameters();
                try
                {
                    if (parameters.Length == 2 && parameters[1].ParameterType == typeof(string))
                    {
                        object? enumValue = CreateEnumCandidate(parameters[0].ParameterType,
                            "CreateNewField", "ObjectData", "NewObjectDataOnly", "ExistingObjectData", "None");
                        if (enumValue != null)
                        {
                            method.Invoke(column, new object[] { enumValue, SanitizeFieldName(columnName) });
                            return;
                        }
                    }
                    else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string))
                    {
                        method.Invoke(column, new object[] { SanitizeFieldName(columnName) });
                        return;
                    }
                }
                catch
                {
                    // continue trying overloads
                }
            }
        }

        private static IEnumerable<object> EnumerateColumns(object inputLayer)
        {
            object? columns = GetPropertyValue(inputLayer, "Columns");
            if (columns is IEnumerable enumerableColumns)
            {
                foreach (object item in enumerableColumns)
                    yield return item;
                yield break;
            }

            if (inputLayer is IEnumerable enumerableLayer)
            {
                foreach (object item in enumerableLayer)
                    yield return item;
            }
        }

        private static string InferDrawingCoordinateSystem()
        {
            string? raw = TryGetRawDrawingCoordinateSystem();
            return NormalizeCoordinateSystemKey(raw);
        }

        private static string? TryGetRawDrawingCoordinateSystem()
        {
            string? geoDataCs = TryGetCoordinateSystemFromGeoData();
            if (!string.IsNullOrWhiteSpace(geoDataCs))
                return geoDataCs;

            try
            {
                Assembly asm = LoadManagedMapApiAssembly();

                string? acMapMapCs = TryGetCoordinateSystemFromAcMapMap(asm);
                if (!string.IsNullOrWhiteSpace(acMapMapCs))
                    return acMapMapCs;

                Type? hostType = asm.GetType("Autodesk.Gis.Map.HostMapApplicationServices", throwOnError: false);
                object? mapApp = hostType?.GetProperty("Application", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (mapApp == null)
                    return null;

                string? appCs = TryExtractCoordinateSystemText(mapApp);
                if (!string.IsNullOrWhiteSpace(appCs))
                    return appCs;

                object? project = mapApp.GetType().GetProperty("ActiveProject", BindingFlags.Public | BindingFlags.Instance)?.GetValue(mapApp);
                if (project == null)
                    return null;

                string? projectCs = TryExtractCoordinateSystemText(project);
                if (!string.IsNullOrWhiteSpace(projectCs))
                    return projectCs;
            }
            catch
            {
                // fall through
            }

            return null;
        }

        private static string? TryGetCoordinateSystemFromGeoData()
        {
            try
            {
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                Database? db = doc?.Database;
                if (db == null)
                    return null;

                PropertyInfo? geoDataProperty = db.GetType().GetProperty("GeoDataObject", BindingFlags.Public | BindingFlags.Instance)
                    ?? db.GetType().GetProperty("GeoDataObjectId", BindingFlags.Public | BindingFlags.Instance);
                if (geoDataProperty == null)
                    return null;

                object? geoDataIdObj = geoDataProperty.GetValue(db);
                if (geoDataIdObj is not ObjectId geoDataId || geoDataId == ObjectId.Null || !geoDataId.IsValid)
                    return null;

                using var tr = db.TransactionManager.StartOpenCloseTransaction();
                DBObject dbo = tr.GetObject(geoDataId, OpenMode.ForRead);
                string? cs = ConvertToCoordinateSystemText(GetPropertyValue(dbo, "CoordinateSystem"))
                    ?? ConvertToCoordinateSystemText(GetPropertyValue(dbo, "CoordinateSystemDefinition"))
                    ?? ConvertToCoordinateSystemText(dbo);
                tr.Commit();
                return cs;
            }
            catch
            {
                return null;
            }
        }

        private static string? TryGetCoordinateSystemFromAcMapMap(Assembly asm)
        {
            try
            {
                foreach (string typeName in new[]
                {
                    "Autodesk.Gis.Map.Platform.AcMapMap",
                    "Autodesk.Gis.Map.Platform.Interop.AcMapMap",
                    "Autodesk.Gis.Map.Platform.AcMapMapManager"
                })
                {
                    Type? type = asm.GetType(typeName, throwOnError: false);
                    if (type == null)
                        continue;

                    object? map = InvokeZeroArgMember(type, null, "GetCurrentMap")
                        ?? InvokeZeroArgMember(type, null, "CurrentMap")
                        ?? InvokeZeroArgMember(type, null, "GetMap");
                    if (map == null)
                        continue;

                    string? cs = TryExtractCoordinateSystemText(map);
                    if (!string.IsNullOrWhiteSpace(cs))
                        return cs;
                }
            }
            catch
            {
                // fall through
            }

            return null;
        }

        private static string? TryExtractCoordinateSystemText(object root)
        {
            var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
            return TryExtractCoordinateSystemText(root, 0, visited);
        }

        private static string? TryExtractCoordinateSystemText(object? root, int depth, HashSet<object> visited)
        {
            if (root == null || depth > 4)
                return null;

            Type type = root.GetType();
            if (!type.IsValueType)
            {
                if (!visited.Add(root))
                    return null;
            }

            string? direct = ConvertToCoordinateSystemText(root);
            if (LooksLikeKnownCoordinateSystem(direct))
                return direct;

            foreach (string memberName in new[]
            {
                "MapCoordinateSystem",
                "CurrentCoordinateSystem",
                "CoordinateSystem",
                "CurrentMapCoordinateSystem",
                "MapSRS",
                "CurrentMapSRS",
                "CoordinateCode",
                "CurrentCoordinateCode",
                "Code",
                "Name",
                "DisplayName",
                "Projection",
                "ProjectProjection",
                "GeoLocation",
                "GeoData"
            })
            {
                object? value = GetPropertyValue(root, memberName) ?? InvokeZeroArgMember(root.GetType(), root, memberName);
                string? text = ConvertToCoordinateSystemText(value);
                if (LooksLikeKnownCoordinateSystem(text))
                    return text;
            }

            foreach (PropertyInfo prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length != 0)
                    continue;

                if (!IsCoordinateSystemRelatedName(prop.Name))
                    continue;

                object? value;
                try
                {
                    value = prop.GetValue(root);
                }
                catch
                {
                    continue;
                }

                string? text = ConvertToCoordinateSystemText(value);
                if (LooksLikeKnownCoordinateSystem(text))
                    return text;

                if (value != null)
                {
                    string? nested = TryExtractCoordinateSystemText(value, depth + 1, visited);
                    if (!string.IsNullOrWhiteSpace(nested))
                        return nested;
                }
            }

            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (method.ReturnType == typeof(void) || method.GetParameters().Length != 0)
                    continue;

                if (!IsCoordinateSystemRelatedName(method.Name))
                    continue;

                object? value;
                try
                {
                    value = method.Invoke(method.IsStatic ? null : root, null);
                }
                catch
                {
                    continue;
                }

                string? text = ConvertToCoordinateSystemText(value);
                if (LooksLikeKnownCoordinateSystem(text))
                    return text;

                if (value != null)
                {
                    string? nested = TryExtractCoordinateSystemText(value, depth + 1, visited);
                    if (!string.IsNullOrWhiteSpace(nested))
                        return nested;
                }
            }

            return null;
        }

        private static bool IsCoordinateSystemRelatedName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            return name.IndexOf("coord", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("srs", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("projection", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("geo", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("map", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksLikeKnownCoordinateSystem(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;

            return text.IndexOf("LVHEF", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("LVH", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("LVF", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("NCRS", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("StatePlane_Nevada_East", StringComparison.OrdinalIgnoreCase) >= 0
                || text.IndexOf("FIPS_2701", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static object? InvokeZeroArgMember(Type type, object? target, string memberName)
        {
            try
            {
                PropertyInfo? prop = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
                if (prop != null && prop.GetIndexParameters().Length == 0)
                    return prop.GetValue(target);

                MethodInfo? method = type.GetMethod(memberName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static, binder: null, Type.EmptyTypes, modifiers: null);
                if (method != null)
                    return method.Invoke(method.IsStatic ? null : target, null);
            }
            catch
            {
                // ignore probing failures
            }

            return null;
        }

        private static string? ConvertToCoordinateSystemText(object? value)
        {
            if (value == null)
                return null;

            if (value is string s)
                return string.IsNullOrWhiteSpace(s) ? null : s.Trim();

            foreach (string propertyName in new[] { "Code", "Name", "CoordinateSystem", "MapCoordinateSystem", "Wkt", "WellKnownText" })
            {
                string? nested = Convert.ToString(GetPropertyValue(value, propertyName), CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(nested))
                    return nested.Trim();
            }

            string? text = Convert.ToString(value, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        private static string NormalizeCoordinateSystemKey(string? rawCoordinateSystem)
        {
            string raw = string.IsNullOrWhiteSpace(rawCoordinateSystem) ? string.Empty : rawCoordinateSystem.Trim();

            if (raw.IndexOf("LVHEF", StringComparison.OrdinalIgnoreCase) >= 0
                || raw.IndexOf("LVH", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "NV83.NCRS-LVHEF";
            }

            if (raw.IndexOf("LVF", StringComparison.OrdinalIgnoreCase) >= 0)
                return "NV83.NCRS-LVF";

            return "NV83.NCRS-LVF";
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new();

            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        private static HashSet<string> SnapshotModelSpaceHandles(Database db)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var tr = db.TransactionManager.StartTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (!id.IsValid || id.IsErased)
                    continue;
                result.Add(id.Handle.ToString());
            }
            tr.Commit();
            return result;
        }

        private static List<ObjectId> GetNewModelSpaceObjectIds(Database db, HashSet<string> before)
        {
            var result = new List<ObjectId>();
            using var tr = db.TransactionManager.StartTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (!id.IsValid || id.IsErased)
                    continue;
                if (!before.Contains(id.Handle.ToString()))
                    result.Add(id);
            }
            tr.Commit();
            return result;
        }

        private static void ReassignImportedLayers(Database db, IEnumerable<ObjectId> ids, string targetLayer)
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (ObjectId id in ids)
            {
                if (!id.IsValid || id.IsErased)
                    continue;

                if (tr.GetObject(id, OpenMode.ForWrite, false) is Entity ent)
                {
                    ent.Layer = targetLayer;
                }
            }
            tr.Commit();
        }

        private static ClipRunResult ClipImportedObjectsToBoundary(Database db, ObjectId boundaryId, IReadOnlyList<ObjectId> ids)
        {
            using var tr = db.TransactionManager.StartTransaction();
            if (!(tr.GetObject(boundaryId, OpenMode.ForRead) is AcPolyline boundary))
                throw new InvalidOperationException("Boundary polyline could not be read.");

            var boundaryClone = (AcPolyline)boundary.Clone();
            Extents3d boundaryExtents = boundaryClone.GeometricExtents;
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            int keptCount = 0;
            int clippedCount = 0;
            int erasedOutsideCount = 0;

            foreach (ObjectId id in ids)
            {
                if (!id.IsValid || id.IsErased)
                    continue;

                if (!(tr.GetObject(id, OpenMode.ForWrite, false) is Entity ent) || ent.IsErased)
                    continue;

                if (TryGetRepresentativePoint(ent, out Point3d point))
                {
                    if (IsPointInsideBoundary(boundaryClone, point))
                    {
                        keptCount++;
                    }
                    else
                    {
                        ent.Erase();
                        erasedOutsideCount++;
                    }
                    continue;
                }

                if (TryEntityExtentsOutsideBoundary(ent, boundaryExtents))
                {
                    ent.Erase();
                    erasedOutsideCount++;
                    continue;
                }

                if (TryEntityExtentsFullyInsideBoundary(ent, boundaryClone))
                {
                    keptCount++;
                    continue;
                }

                if (ent is Curve curve)
                {
                    var action = ClipCurveEntity(curve, boundaryClone, ms, tr);
                    keptCount += action.KeptCount;
                    clippedCount += action.ClippedCount;
                    erasedOutsideCount += action.ErasedOutsideCount;
                    continue;
                }

                // Fallback for unsupported geometry.
                try
                {
                    Extents3d ext = ent.GeometricExtents;
                    Point3d center = new Point3d(
                        (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                        (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5,
                        0.0);
                    if (IsPointInsideBoundary(boundaryClone, center))
                    {
                        keptCount++;
                    }
                    else
                    {
                        ent.Erase();
                        erasedOutsideCount++;
                    }
                }
                catch
                {
                    keptCount++;
                }
            }

            boundaryClone.Dispose();
            tr.Commit();

            return new ClipRunResult
            {
                KeptCount = keptCount,
                ClippedCount = clippedCount,
                ErasedOutsideCount = erasedOutsideCount,
            };
        }

        private static ClipRunResult ClipCurveEntity(Curve curve, AcPolyline boundary, BlockTableRecord ms, Transaction tr)
        {
            var result = new ClipRunResult();
            DBObjectCollection pieces = new DBObjectCollection();
            var splitParams = GetCurveSplitParameters(curve, boundary);

            try
            {
                if (splitParams.Count > 0)
                {
                    pieces = curve.GetSplitCurves(splitParams);
                }
            }
            catch
            {
                pieces = new DBObjectCollection();
            }

            if (pieces.Count == 0)
            {
                Point3d testPoint = GetCurveMidPoint(curve);
                if (IsPointInsideBoundary(boundary, testPoint))
                {
                    result.KeptCount++;
                }
                else
                {
                    curve.Erase();
                    result.ErasedOutsideCount++;
                }
                return result;
            }

            bool keptAny = false;
            foreach (DBObject dbo in pieces)
            {
                if (dbo is not Curve piece)
                {
                    dbo.Dispose();
                    continue;
                }

                Point3d mid = GetCurveMidPoint(piece);
                if (IsPointInsideBoundary(boundary, mid))
                {
                    if (piece is Entity pieceEnt)
                    {
                        pieceEnt.Layer = curve.Layer;
                        ms.AppendEntity(pieceEnt);
                        tr.AddNewlyCreatedDBObject(pieceEnt, true);
                        keptAny = true;
                        result.KeptCount++;
                    }
                    else
                    {
                        piece.Dispose();
                    }
                }
                else
                {
                    piece.Dispose();
                }
            }

            curve.Erase();
            if (keptAny)
                result.ClippedCount++;
            else
                result.ErasedOutsideCount++;

            return result;
        }

        private static DoubleCollection GetCurveSplitParameters(Curve curve, AcPolyline boundary)
        {
            var parameters = new List<double>();
            var intersections = new Point3dCollection();

            try
            {
                curve.IntersectWith(boundary, Intersect.OnBothOperands, intersections, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
                return new DoubleCollection();
            }

            foreach (Point3d pt in intersections)
            {
                try
                {
                    Point3d onCurve = curve.GetClosestPointTo(pt, false);
                    double param = curve.GetParameterAtPoint(onCurve);
                    if (!parameters.Any(x => Math.Abs(x - param) < 1e-6))
                        parameters.Add(param);
                }
                catch
                {
                    // ignore individual failures
                }
            }

            parameters.Sort();
            var dc = new DoubleCollection();
            foreach (double p in parameters)
                dc.Add(p);
            return dc;
        }

        private static Point3d GetCurveMidPoint(Curve curve)
        {
            try
            {
                double start = curve.StartParam;
                double end = curve.EndParam;
                return curve.GetPointAtParameter((start + end) * 0.5);
            }
            catch
            {
                Extents3d ext = curve.GeometricExtents;
                return new Point3d(
                    (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                    (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5,
                    (ext.MinPoint.Z + ext.MaxPoint.Z) * 0.5);
            }
        }

        private static bool TryEntityExtentsOutsideBoundary(Entity ent, Extents3d boundaryExtents)
        {
            try
            {
                return !ExtentsIntersect(ent.GeometricExtents, boundaryExtents);
            }
            catch
            {
                return false;
            }
        }

        private static bool TryEntityExtentsFullyInsideBoundary(Entity ent, AcPolyline boundary)
        {
            try
            {
                Extents3d ext = ent.GeometricExtents;
                var corners = new[]
                {
                    new Point3d(ext.MinPoint.X, ext.MinPoint.Y, 0.0),
                    new Point3d(ext.MinPoint.X, ext.MaxPoint.Y, 0.0),
                    new Point3d(ext.MaxPoint.X, ext.MinPoint.Y, 0.0),
                    new Point3d(ext.MaxPoint.X, ext.MaxPoint.Y, 0.0),
                };

                foreach (Point3d corner in corners)
                {
                    if (!IsPointInsideBoundary(boundary, corner))
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetRepresentativePoint(Entity ent, out Point3d pt)
        {
            switch (ent)
            {
                case DBPoint dbPoint:
                    pt = dbPoint.Position;
                    return true;
                case BlockReference br:
                    pt = br.Position;
                    return true;
                case Circle circle:
                    pt = circle.Center;
                    return true;
                default:
                    pt = Point3d.Origin;
                    return false;
            }
        }

        private static bool IsPointInsideBoundary(AcPolyline boundary, Point3d point)
        {
            bool inside = false;
            int count = boundary.NumberOfVertices;
            double x = point.X;
            double y = point.Y;

            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                Point2d pi = boundary.GetPoint2dAt(i);
                Point2d pj = boundary.GetPoint2dAt(j);

                bool intersect = ((pi.Y > y) != (pj.Y > y)) &&
                                 (x < (pj.X - pi.X) * (y - pi.Y) / ((pj.Y - pi.Y) == 0.0 ? 1e-12 : (pj.Y - pi.Y)) + pi.X);
                if (intersect)
                    inside = !inside;
            }

            return inside;
        }

        private static ObjectId PromptForBoundaryPolyline(Document doc)
        {
            var ed = doc.Editor;

            var peo = new PromptEntityOptions("\nSelect closed boundary polyline: ");
            peo.SetRejectMessage("\nObject must be a closed polyline.");
            peo.AddAllowedClass(typeof(AcPolyline), exactMatch: false);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
                return ObjectId.Null;

            using var tr = doc.Database.TransactionManager.StartTransaction();
            if (!(tr.GetObject(per.ObjectId, OpenMode.ForRead) is AcPolyline pl) || !pl.Closed)
                return ObjectId.Null;

            tr.Commit();
            return per.ObjectId;
        }

        private static ObjectId PromptForTemporaryBoundaryPolygon(Document doc)
        {
            var ed = doc.Editor;
            var db = doc.Database;
            var pts = new List<Point2d>();

            var first = ed.GetPoint("\nPick first boundary point: ");
            if (first.Status != PromptStatus.OK)
                return ObjectId.Null;
            pts.Add(new Point2d(first.Value.X, first.Value.Y));

            Point3d previous = first.Value;
            while (true)
            {
                var ppo = new PromptPointOptions("\nPick next boundary point or press Enter to finish: ")
                {
                    AllowNone = true,
                    UseBasePoint = true,
                    BasePoint = previous
                };

                var next = ed.GetPoint(ppo);
                if (next.Status == PromptStatus.None)
                    break;
                if (next.Status != PromptStatus.OK)
                    return ObjectId.Null;

                previous = next.Value;
                pts.Add(new Point2d(next.Value.X, next.Value.Y));
            }

            if (pts.Count < 3)
            {
                ed.WriteMessage("\nAt least 3 boundary points are required.");
                return ObjectId.Null;
            }

            using var tr = db.TransactionManager.StartTransaction();
            EnsureLayerExists(tr, db, TempBoundaryLayer);
            var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            var pl = new AcPolyline();
            for (int i = 0; i < pts.Count; i++)
                pl.AddVertexAt(i, pts[i], 0.0, 0.0, 0.0);
            pl.Closed = true;
            pl.Layer = TempBoundaryLayer;

            ObjectId id = ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            tr.Commit();
            return id;
        }

        private static void EnsureImportLayers(Editor ed, Database db)
        {
            try
            {
                TrySyncLayersFromMasterReference(ed, db);

                using var tr = db.TransactionManager.StartTransaction();
                EnsureLayerExists(tr, db, TempBoundaryLayer, new GisLayerSpec(7, "Continuous", "Normal"));
                EnsureLayerExists(tr, db, GisImportDataset.ParcelLines.TargetLayer, new GisLayerSpec(252, "Continuous", "S"));
                EnsureLayerExists(tr, db, GisImportDataset.StreetCenterlines.TargetLayer, new GisLayerSpec(1, "Continuous", "L"));
                EnsureLayerExists(tr, db, GisImportDataset.SewerPipes.TargetLayer, new GisLayerSpec(106, "Continuous", "SSWR-PIPE-E"));
                EnsureLayerExists(tr, db, GisImportDataset.SewerStructures.TargetLayer, new GisLayerSpec(106, "Continuous", "SSWR-STRC-E"));
                EnsureLayerExists(tr, db, GisImportDataset.StormPipes.TargetLayer, new GisLayerSpec(60, "Continuous", "STRM-PIPE-E"));
                EnsureLayerExists(tr, db, GisImportDataset.StormStructures.TargetLayer, new GisLayerSpec(60, "Continuous", "STRM-STRC-E"));
                tr.Commit();
            }
            catch
            {
                // keep import setup non-fatal if layer creation fails
            }
        }

        private static void TrySyncLayersFromMasterReference(Editor ed, Database db)
        {
            try
            {
                if (!File.Exists(LayerMasterReferencePath))
                    return;

                using var sourceDb = new Database(false, true);
                sourceDb.ReadDwgFile(LayerMasterReferencePath, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
                sourceDb.CloseInput(true);

                var sourceLayerIds = new ObjectIdCollection();
                using (var sourceTr = sourceDb.TransactionManager.StartTransaction())
                {
                    var sourceLt = (LayerTable)sourceTr.GetObject(sourceDb.LayerTableId, OpenMode.ForRead);
                    foreach (string layerName in GisImportDataset.All.Select(x => x.TargetLayer).Append(TempBoundaryLayer).Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (sourceLt.Has(layerName))
                            sourceLayerIds.Add(sourceLt[layerName]);
                    }

                    sourceTr.Commit();
                }

                if (sourceLayerIds.Count == 0)
                    return;

                using var targetTr = db.TransactionManager.StartTransaction();
                var mapping = new IdMapping();
                sourceDb.WblockCloneObjects(sourceLayerIds, db.LayerTableId, mapping, DuplicateRecordCloning.Replace, false);
                targetTr.Commit();

                ed.WriteMessage($"\n  GIS layer master synced: {LayerMasterReferencePath}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n  GIS layer master sync skipped: {ex.Message}");
            }
        }

        private static void EnsureLayerExists(Transaction tr, Database db, string layerName)
        {
            EnsureLayerExists(tr, db, layerName, new GisLayerSpec(7, "Continuous", null));
        }

        private static void EnsureLayerExists(Transaction tr, Database db, string layerName, GisLayerSpec spec)
        {
            Editor? ed = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            if (ed != null && LayerStandards.TryEnsureManagedGisLayer(db, tr, ed, layerName))
                return;

            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            LayerTableRecord ltr;
            if (lt.Has(layerName))
            {
                ltr = (LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForWrite);
            }
            else
            {
                lt.UpgradeOpen();
                ltr = new LayerTableRecord { Name = layerName };
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }

            ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, (short)spec.ColorIndex);

            ObjectId linetypeId = GetOrLoadLinetypeId(db, tr, spec.LinetypeName);
            if (!linetypeId.IsNull)
                ltr.LinetypeObjectId = linetypeId;

            if (!string.IsNullOrWhiteSpace(spec.PlotStyleName))
                TryAssignNamedPlotStyle(db, tr, ltr, spec.PlotStyleName);
        }

        private static ObjectId GetOrLoadLinetypeId(Database db, Transaction tr, string linetypeName)
        {
            var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (ltt.Has(linetypeName))
                return ltt[linetypeName];

            try
            {
                db.LoadLineTypeFile(linetypeName, "acad.lin");
            }
            catch
            {
                // ignore, will fall back below
            }

            ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (ltt.Has(linetypeName))
                return ltt[linetypeName];

            return ltt.Has("Continuous") ? ltt["Continuous"] : ObjectId.Null;
        }

        private static void TryAssignNamedPlotStyle(Database db, Transaction tr, LayerTableRecord ltr, string plotStyleName)
        {
            try
            {
                var psDict = (DBDictionary)tr.GetObject(db.PlotStyleNameDictionaryId, OpenMode.ForRead);
                if (psDict.Contains(plotStyleName))
                    ltr.PlotStyleNameId = psDict.GetAt(plotStyleName);
            }
            catch
            {
                // skip if drawing does not support named plot styles
            }
        }

        private static void TryEraseEntity(Database db, ObjectId id)
        {
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                if (id.IsValid && !id.IsErased && tr.GetObject(id, OpenMode.ForWrite, false) is Entity ent)
                    ent.Erase();
                tr.Commit();
            }
            catch
            {
                // ignore cleanup failures
            }
        }

        private static void TryApplyPointDisplayFromSourceCache(Editor ed, Database sourceDb)
        {
            try
            {
                short sourcePdmode = Convert.ToInt16(sourceDb.Pdmode, CultureInfo.InvariantCulture);
                double sourcePdsize = Convert.ToDouble(sourceDb.Pdsize, CultureInfo.InvariantCulture);

                object? currentPdmodeObj = AcadApp.GetSystemVariable("PDMODE");
                object? currentPdsizeObj = AcadApp.GetSystemVariable("PDSIZE");

                short currentPdmode = currentPdmodeObj == null ? (short)0 : Convert.ToInt16(currentPdmodeObj, CultureInfo.InvariantCulture);
                double currentPdsize = currentPdsizeObj == null ? 0.0 : Convert.ToDouble(currentPdsizeObj, CultureInfo.InvariantCulture);

                bool changed = false;
                if (currentPdmode != sourcePdmode)
                {
                    AcadApp.SetSystemVariable("PDMODE", sourcePdmode);
                    changed = true;
                }

                if (Math.Abs(currentPdsize - sourcePdsize) > 1e-9)
                {
                    AcadApp.SetSystemVariable("PDSIZE", sourcePdsize);
                    changed = true;
                }

                if (changed)
                {
                    ed.WriteMessage($"\n  Point display synced from cache: PDMODE={sourcePdmode}, PDSIZE={sourcePdsize}.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n  Point display sync from cache skipped: {ex.Message}");
            }
        }

        private static void ApplyImportedPointDisplayPreference(Document doc, GisImportOptions options, Editor ed)
        {
            // Legacy no-op. Point display is now synced from each structure cache/source drawing
            // so imported DBPoints match the source cache PDMODE / PDSIZE settings.
        }

        private static void CleanupImportedGisLinework(Editor ed, Database db)
        {
            try
            {
                int erasedCount = 0;
                var targetLayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    GisImportDataset.ParcelLines.TargetLayer,
                    GisImportDataset.StreetCenterlines.TargetLayer,
                    GisImportDataset.SewerPipes.TargetLayer,
                    GisImportDataset.StormPipes.TargetLayer,
                };

                var seen = new HashSet<string>(StringComparer.Ordinal);

                using var tr = db.TransactionManager.StartTransaction();
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased)
                        continue;

                    if (!(tr.GetObject(id, OpenMode.ForRead, false) is Entity ent))
                        continue;

                    if (!targetLayers.Contains(ent.Layer))
                        continue;

                    string? signature = BuildGeometrySignature(ent);
                    if (string.IsNullOrWhiteSpace(signature))
                        continue;

                    if (!seen.Add(signature))
                    {
                        ent.UpgradeOpen();
                        ent.Erase();
                        erasedCount++;
                    }
                }

                tr.Commit();
                ed.WriteMessage($"\nCLV-GIS-CLEANUP removed {erasedCount} duplicate GIS linework object(s).");
                ed.WriteMessage("\nReview imported GIS linework, then run OVERKILL manually if you want additional simplification.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-CLEANUP error: {ex.Message}");
            }
        }

        private static string? BuildGeometrySignature(Entity ent)
        {
            if (ent is Line line)
                return BuildLineSignature(line);
            if (ent is AcPolyline pl)
                return BuildPolylineSignature(pl);
            return null;
        }

        private static string BuildLineSignature(Line line)
        {
            string a = FormatXY(line.StartPoint);
            string b = FormatXY(line.EndPoint);
            return string.CompareOrdinal(a, b) <= 0
                ? $"LINE|{line.Layer}|{a}|{b}"
                : $"LINE|{line.Layer}|{b}|{a}";
        }

        private static string BuildPolylineSignature(AcPolyline pl)
        {
            var pts = new List<string>();
            for (int i = 0; i < pl.NumberOfVertices; i++)
            {
                pts.Add(FormatXY(pl.GetPoint3dAt(i)) + "@" + pl.GetBulgeAt(i).ToString("0.######", CultureInfo.InvariantCulture));
            }

            if (!pl.Closed)
            {
                string forward = string.Join(";", pts);
                pts.Reverse();
                string reverse = string.Join(";", pts);
                return string.CompareOrdinal(forward, reverse) <= 0
                    ? $"PLINE|{pl.Layer}|O|{forward}"
                    : $"PLINE|{pl.Layer}|O|{reverse}";
            }

            string normalized = NormalizeClosedSequence(pts);
            return $"PLINE|{pl.Layer}|C|{normalized}";
        }

        private static string NormalizeClosedSequence(List<string> pts)
        {
            if (pts.Count == 0)
                return string.Empty;

            var candidates = new List<string>();
            for (int i = 0; i < pts.Count; i++)
                candidates.Add(string.Join(";", Rotate(pts, i)));

            var rev = new List<string>(pts);
            rev.Reverse();
            for (int i = 0; i < rev.Count; i++)
                candidates.Add(string.Join(";", Rotate(rev, i)));

            candidates.Sort(StringComparer.Ordinal);
            return candidates[0];
        }

        private static IEnumerable<string> Rotate(IReadOnlyList<string> pts, int index)
        {
            for (int i = 0; i < pts.Count; i++)
                yield return pts[(index + i) % pts.Count];
        }

        private static string FormatXY(Point3d pt)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.###},{1:0.###}", pt.X, pt.Y);
        }

        private static string SanitizeFieldName(string raw)
        {
            var chars = raw.Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray();
            string value = new string(chars);
            if (string.IsNullOrWhiteSpace(value))
                value = "FIELD";
            if (char.IsDigit(value[0]))
                value = "F_" + value;
            return value.Length <= 31 ? value : value.Substring(0, 31);
        }

        private static IEnumerable<object> Enumerate(object source)
        {
            if (source is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                    yield return item;
            }
        }

        private static bool TryInvoke(object target, string methodName, params object?[] args)
        {
            return TryInvokeInternal(target, methodName, args, out _);
        }

        private static object? TryInvokeWithResult(object target, string methodName, params object?[] args)
        {
            return TryInvokeInternal(target, methodName, args, out object? result) ? result : null;
        }

        private static bool TryInvokeInternal(object target, string methodName, object?[] args, out object? result)
        {
            result = null;
            foreach (MethodInfo method in target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase)))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != args.Length)
                    continue;

                object?[]? converted = TryConvertArguments(parameters, args);
                if (converted == null)
                    continue;

                try
                {
                    result = method.Invoke(target, converted);
                    return true;
                }
                catch
                {
                    // try next overload
                }
            }
            return false;
        }

        private static object?[]? TryConvertArguments(ParameterInfo[] parameters, object?[] args)
        {
            var converted = new object?[args.Length];
            for (int i = 0; i < args.Length; i++)
            {
                Type targetType = parameters[i].ParameterType;
                object? arg = args[i];

                if (arg == null)
                {
                    converted[i] = null;
                    continue;
                }

                if (targetType.IsInstanceOfType(arg))
                {
                    converted[i] = arg;
                    continue;
                }

                try
                {
                    if (targetType.IsEnum && arg is string s)
                    {
                        converted[i] = Enum.Parse(targetType, s, true);
                        continue;
                    }

                    if (targetType == typeof(string[]) && arg is IEnumerable<string> stringEnumerable)
                    {
                        converted[i] = stringEnumerable.ToArray();
                        continue;
                    }

                    converted[i] = Convert.ChangeType(arg, targetType, CultureInfo.InvariantCulture);
                }
                catch
                {
                    return null;
                }
            }
            return converted;
        }

        private static object? GetPropertyValue(object target, string propertyName)
        {
            return target.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.GetValue(target);
        }

        private static void SetBoolSwitch(object target, string baseName, bool value)
        {
            PropertyInfo? prop = target.GetType().GetProperty(baseName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop != null && prop.CanWrite && prop.PropertyType == typeof(bool))
            {
                prop.SetValue(target, value);
                return;
            }

            TryInvoke(target, "Set" + baseName, value);
        }

        private static void SetStringValue(object target, string baseName, string value)
        {
            PropertyInfo? prop = target.GetType().GetProperty(baseName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (prop != null && prop.CanWrite && prop.PropertyType == typeof(string))
            {
                prop.SetValue(target, value);
                return;
            }

            TryInvoke(target, "Set" + baseName, value);
        }

        private static void SetNameValueOption(object options, string optionName, object value)
        {
            if (TryInvoke(options, "Set", optionName, value))
                return;
            if (TryInvoke(options, "Add", optionName, value))
                return;
            TryInvoke(options, "Item", optionName, value);
        }

        private static void TrySetEnumChoice(object target, string methodName, string[] enumNames)
        {
            foreach (MethodInfo method in target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase)))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1 || !parameters[0].ParameterType.IsEnum)
                    continue;

                object? enumValue = CreateEnumCandidate(parameters[0].ParameterType, enumNames);
                if (enumValue == null)
                    continue;

                try
                {
                    method.Invoke(target, new[] { enumValue });
                    return;
                }
                catch
                {
                    // continue
                }
            }
        }

        private static void InspectObjectData(Editor ed, Database db)
        {
            try
            {
                TryFocusDrawingView();
                var doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null)
                    return;

                if (!EnsureOdHelperLoaded(doc, ed, out string helperPath))
                {
                    ed.WriteMessage("\nCLV-GIS-OD-INSPECT: unable to load CLV_GIS_OD_HELPERS.lsp.");
                    return;
                }

                ed.WriteMessage($"\nCLV-GIS-OD-INSPECT: using helper {helperPath}");
                doc.SendStringToExecute("CLV-GIS-OD-INSPECT-LSP ", true, false, false);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-OD-INSPECT failed: {ex.Message}");
            }
        }

        private static void TryFocusDrawingView()
        {
            try
            {
                Type? utilsType = Type.GetType("Autodesk.AutoCAD.Internal.Utils, AcCoreMgd", throwOnError: false)
                    ?? Type.GetType("Autodesk.AutoCAD.Internal.Utils, AcMgd", throwOnError: false);

                MethodInfo? method = utilsType?.GetMethod("SetFocusToDwgView", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                method?.Invoke(null, null);
            }
            catch
            {
                // Best-effort only. Prompting still works even if drawing-view focus cannot be forced.
            }
        }

        private static string GetOdHelperPath()
        {
            return @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\CLV_GIS_OD_HELPERS.lsp";
        }

        private static bool EnsureOdHelperLoaded(Document? doc, Editor ed, out string helperPath)
        {
            helperPath = GetOdHelperPath();
            if (doc == null || string.IsNullOrWhiteSpace(helperPath) || !File.Exists(helperPath))
                return false;

            try
            {
                string escapedPath = helperPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
                doc.SendStringToExecute($"(progn (vl-load-com) (load \"{escapedPath}\") (princ)) ", true, false, false);
                return true;
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nOD helper load failed: {ex.Message}");
                return false;
            }
        }

        private static ObjectDataTableSnapshot? TryGetEntityObjectDataTableSnapshot(ObjectId entityId, string tableName)
        {
            if (!TryReadEntityObjectDataSnapshots(entityId, out var snapshots, out _))
                return null;

            string requested = NormalizeObjectDataTableName(tableName);
            return snapshots.FirstOrDefault(s => string.Equals(NormalizeObjectDataTableName(s.TableName), requested, StringComparison.OrdinalIgnoreCase));
        }

        private static bool TryReadEntityObjectDataSnapshots(ObjectId entityId, out List<ObjectDataTableSnapshot> snapshots, out string detail)
        {
            snapshots = new List<ObjectDataTableSnapshot>();
            detail = "No OD tables were attached to the selected object, or the Map OD API did not return any records for it.";

            try
            {
                foreach (object table in GetAllObjectDataTables())
                {
                    string tableName = Convert.ToString(GetPropertyValue(table, "Name"), CultureInfo.InvariantCulture) ?? "<unnamed>";
                    object? records = GetObjectDataRecords(table, entityId, forWrite: false, createIfMissing: false);
                    if (records == null)
                        continue;

                    List<ObjectDataRecordSnapshot> recordSnapshots = new List<ObjectDataRecordSnapshot>();
                    foreach (object record in Enumerate(records))
                    {
                        List<ObjectDataFieldSnapshot> fields = new List<ObjectDataFieldSnapshot>();
                        foreach (ObjectDataFieldDefinitionSnapshot def in GetObjectDataFieldDefinitions(table))
                        {
                            object? mapValue = GetRecordValue(record, def.Index);
                            object? value = ExtractMapValue(mapValue);
                            fields.Add(new ObjectDataFieldSnapshot(def.Name, value));
                        }

                        recordSnapshots.Add(new ObjectDataRecordSnapshot(fields));
                    }

                    if (recordSnapshots.Count > 0)
                        snapshots.Add(new ObjectDataTableSnapshot(tableName, recordSnapshots));
                }

                detail = snapshots.Count > 0 ? $"Found {snapshots.Count} OD table(s)." : detail;
                return snapshots.Count > 0;
            }
            catch (System.Exception ex)
            {
                detail = ex.Message;
                return false;
            }
        }

        private static string? GetFieldValueFromSnapshot(ObjectDataTableSnapshot? snapshot, string fieldName)
        {
            if (snapshot == null)
                return null;

            foreach (ObjectDataRecordSnapshot record in snapshot.Records)
            {
                ObjectDataFieldSnapshot? field = record.Fields.FirstOrDefault(f => string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase));
                if (field == null)
                    continue;

                string? text = field.Value == null ? null : Convert.ToString(field.Value, CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(text))
                    return text.Trim();
            }

            return null;
        }

        private static string NormalizeObjectDataTableName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string normalized = value.Trim();
            if (normalized.StartsWith("OD:", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(3);
            if (normalized.EndsWith("_OD", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(0, normalized.Length - 3);

            return normalized.Replace(" ", string.Empty)
                .Replace("-", string.Empty)
                .Replace("_", string.Empty)
                .ToUpperInvariant();
        }

        private static IEnumerable<object> GetAllObjectDataTables()
        {
            object? tables = null;
            try
            {
                Assembly asm = LoadManagedMapApiAssembly();
                Type hostType = asm.GetType("Autodesk.Gis.Map.HostMapApplicationServices", throwOnError: true)!;
                object? mapApp = hostType.GetProperty("Application", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                object? project = mapApp == null ? null : mapApp.GetType().GetProperty("ActiveProject", BindingFlags.Public | BindingFlags.Instance)?.GetValue(mapApp);
                tables = project == null ? null
                    : GetPropertyValue(project, "ODTables")
                    ?? GetPropertyValue(project, "ObjectDataTables")
                    ?? GetPropertyValue(project, "Tables");
            }
            catch
            {
                yield break;
            }

            if (tables == null)
                yield break;

            foreach (object item in Enumerate(tables))
                yield return item;
        }

        private static object? GetObjectDataTable(string tableName)
        {
            try
            {
                Assembly asm = LoadManagedMapApiAssembly();
                Type hostType = asm.GetType("Autodesk.Gis.Map.HostMapApplicationServices", throwOnError: true)!;
                object? mapApp = hostType.GetProperty("Application", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                object? project = mapApp == null ? null : mapApp.GetType().GetProperty("ActiveProject", BindingFlags.Public | BindingFlags.Instance)?.GetValue(mapApp);
                if (project == null)
                    return null;

                object? tables = GetPropertyValue(project, "ODTables")
                    ?? GetPropertyValue(project, "ObjectDataTables")
                    ?? GetPropertyValue(project, "Tables");
                if (tables == null)
                    return null;

                foreach (string candidateName in new[]
                {
                    tableName,
                    "OD:" + tableName,
                    tableName.EndsWith("_OD", StringComparison.OrdinalIgnoreCase) ? tableName[..^3] : tableName + "_OD"
                }.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    object? byIndexer = TryInvokeWithResult(tables, "get_Item", candidateName) ?? TryInvokeWithResult(tables, "Item", candidateName);
                    if (byIndexer != null)
                        return byIndexer;
                }

                string requestedNormalized = NormalizeObjectDataTableName(tableName);

                foreach (object item in Enumerate(tables))
                {
                    string? name = Convert.ToString(GetPropertyValue(item, "Name"), CultureInfo.InvariantCulture);
                    if (string.Equals(name, tableName, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(NormalizeObjectDataTableName(name), requestedNormalized, StringComparison.OrdinalIgnoreCase))
                    {
                        return item;
                    }
                }
            }
            catch
            {
                // fall through
            }

            return null;
        }

        private static object? GetObjectDataRecords(object table, ObjectId entityId, bool forWrite, bool createIfMissing)
        {
            object? mapOpenMode = GetMapObjectDataOpenMode(table.GetType().Assembly, forWrite);
            List<object[]> argSets = new List<object[]>();

            if (mapOpenMode != null)
            {
                argSets.Add(new object[] { 0u, entityId, mapOpenMode, createIfMissing });
                argSets.Add(new object[] { 0, entityId, mapOpenMode, createIfMissing });
                argSets.Add(new object[] { entityId, mapOpenMode, createIfMissing });
                argSets.Add(new object[] { entityId, mapOpenMode });
            }

            OpenMode acadMode = forWrite ? OpenMode.ForWrite : OpenMode.ForRead;
            argSets.Add(new object[] { 0u, entityId, acadMode, createIfMissing });
            argSets.Add(new object[] { 0, entityId, acadMode, createIfMissing });
            argSets.Add(new object[] { entityId, acadMode, createIfMissing });
            argSets.Add(new object[] { entityId, acadMode });
            argSets.Add(new object[] { entityId });

            foreach (object[] args in argSets)
            {
                object? result = TryInvokeWithResult(table, "GetObjectTableRecords", args)
                    ?? TryInvokeWithResult(table, "GetObjectRecords", args)
                    ?? TryInvokeWithResult(table, "GetRecords", args);
                if (result != null)
                    return result;
            }

            return null;
        }

        private static object? GetMapObjectDataOpenMode(Assembly assembly, bool forWrite)
        {
            foreach (string typeName in new[]
            {
                "Autodesk.Gis.Map.Constants.OpenMode",
                "Autodesk.Gis.Map.ObjectData.OpenMode"
            })
            {
                Type? enumType = assembly.GetType(typeName, throwOnError: false);
                if (enumType == null || !enumType.IsEnum)
                    continue;

                foreach (string name in forWrite
                    ? new[] { "OpenForWrite", "ForWrite", "Write" }
                    : new[] { "OpenForRead", "ForRead", "Read" })
                {
                    try
                    {
                        return Enum.Parse(enumType, name, ignoreCase: true);
                    }
                    catch
                    {
                        // try next name
                    }
                }

                Array values = Enum.GetValues(enumType);
                if (values.Length > 0)
                    return values.GetValue(forWrite ? Math.Min(1, values.Length - 1) : 0);
            }

            return null;
        }

        private static object? CreateObjectDataRecord(object table)
        {
            object? record = null;
            Type? recordType = table.GetType().Assembly.GetType("Autodesk.Gis.Map.ObjectData.Record", throwOnError: false);
            if (recordType != null)
            {
                MethodInfo? createMethod = recordType.GetMethod("Create", BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase)
                    ?? recordType.GetMethod("CreateRecord", BindingFlags.Public | BindingFlags.Static | BindingFlags.IgnoreCase);
                if (createMethod != null)
                {
                    try
                    {
                        record = createMethod.Invoke(null, Array.Empty<object>());
                    }
                    catch
                    {
                        record = null;
                    }
                }

                if (record == null)
                {
                    try
                    {
                        record = Activator.CreateInstance(recordType, nonPublic: true);
                    }
                    catch
                    {
                        record = null;
                    }
                }
            }

            if (record != null)
                TryInvoke(table, "InitRecord", record);

            return record;
        }

        private static bool AddObjectDataRecord(object table, object record, ObjectId entityId)
        {
            return TryInvoke(table, "AddRecord", record, entityId)
                || TryInvoke(table, "AddRecord", entityId, record)
                || TryInvoke(table, "AppendRecord", record, entityId)
                || TryInvoke(table, "AppendRecord", entityId, record)
                || TryInvoke(table, "Add", record, entityId)
                || TryInvoke(table, "Add", entityId, record);
        }

        private static List<ObjectDataFieldDefinitionSnapshot> GetObjectDataFieldDefinitions(object table)
        {
            var result = new List<ObjectDataFieldDefinitionSnapshot>();

            foreach (string propertyName in new[] { "FieldDefinitions", "Definitions", "Columns", "Fields" })
            {
                object? defs = GetPropertyValue(table, propertyName);
                if (defs == null)
                    continue;

                int index = 0;
                foreach (object def in Enumerate(defs))
                {
                    string name = Convert.ToString(GetPropertyValue(def, "Name")
                        ?? GetPropertyValue(def, "ColumnName")
                        ?? GetPropertyValue(def, "FieldName"), CultureInfo.InvariantCulture) ?? $"Field{index}";
                    result.Add(new ObjectDataFieldDefinitionSnapshot(index, name));
                    index++;
                }

                if (result.Count > 0)
                    break;
            }

            return result;
        }

        private static int GetObjectDataFieldIndex(object table, string fieldName)
        {
            foreach (ObjectDataFieldDefinitionSnapshot def in GetObjectDataFieldDefinitions(table))
            {
                if (string.Equals(def.Name, fieldName, StringComparison.OrdinalIgnoreCase))
                    return def.Index;
            }

            return 0;
        }

        private static object? GetRecordValue(object record, int index)
        {
            return TryInvokeWithResult(record, "get_Item", index)
                ?? TryInvokeWithResult(record, "Item", index)
                ?? TryInvokeWithResult(record, "GetAt", index)
                ?? TryInvokeWithResult(record, "GetValue", index);
        }

        private static bool SetRecordValue(object record, int index, object? value)
        {
            object? mapValue = GetRecordValue(record, index);
            if (mapValue == null)
                return false;

            if (TryInvoke(mapValue, "Assign", value))
                return true;
            if (TryInvoke(mapValue, "SetValue", value))
                return true;

            foreach (string propertyName in new[] { "StrValue", "StringValue", "Value" })
            {
                PropertyInfo? prop = mapValue.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop == null || !prop.CanWrite)
                    continue;

                try
                {
                    object? converted = ConvertValueForTargetType(value, prop.PropertyType);
                    prop.SetValue(mapValue, converted);
                    return true;
                }
                catch
                {
                    // try next write path
                }
            }

            return false;
        }

        private static object? ExtractMapValue(object? mapValue)
        {
            if (mapValue == null)
                return null;

            if (mapValue is string || mapValue.GetType().IsPrimitive || mapValue is decimal || mapValue is DateTime)
                return mapValue;

            foreach (string propertyName in new[] { "Value", "StrValue", "StringValue", "Int32Value", "IntValue", "DoubleValue", "RealValue", "BoolValue", "BooleanValue" })
            {
                object? nested = GetPropertyValue(mapValue, propertyName);
                if (nested == null)
                    continue;

                if (nested is string || nested.GetType().IsPrimitive || nested is decimal || nested is DateTime)
                    return nested;
            }

            string? raw = Convert.ToString(mapValue, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        }

        private static object? ConvertValueForTargetType(object? value, Type targetType)
        {
            if (value == null)
                return null;

            Type effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (effectiveType.IsInstanceOfType(value))
                return value;

            string text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            if (effectiveType == typeof(string))
                return text;
            if (effectiveType == typeof(bool))
                return bool.TryParse(text, out bool boolValue) ? boolValue : false;
            if (effectiveType == typeof(int))
                return int.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out int intValue) ? intValue : 0;
            if (effectiveType == typeof(double))
                return double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out double doubleValue) ? doubleValue : 0d;
            if (effectiveType == typeof(short))
                return short.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out short shortValue) ? shortValue : (short)0;
            if (effectiveType == typeof(long))
                return long.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out long longValue) ? longValue : 0L;
            if (effectiveType == typeof(decimal))
                return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal decimalValue) ? decimalValue : 0m;
            if (effectiveType.IsEnum)
                return Enum.Parse(effectiveType, text, ignoreCase: true);

            return Convert.ChangeType(value, effectiveType, CultureInfo.InvariantCulture);
        }

        private static object? CreateEnumCandidate(Type enumType, params string[] candidates)
        {
            foreach (string name in candidates)
            {
                try
                {
                    return Enum.Parse(enumType, name, true);
                }
                catch
                {
                    // try next
                }
            }
            return null;
        }
    }

    internal sealed class ObjectDataFieldDefinitionSnapshot
    {
        public ObjectDataFieldDefinitionSnapshot(int index, string name)
        {
            Index = index;
            Name = name;
        }

        public int Index { get; }
        public string Name { get; }
    }

    internal sealed class ObjectDataFieldSnapshot
    {
        public ObjectDataFieldSnapshot(string name, object? value)
        {
            Name = name;
            Value = value;
        }

        public string Name { get; }
        public object? Value { get; }
    }

    internal sealed class ObjectDataRecordSnapshot
    {
        public ObjectDataRecordSnapshot(List<ObjectDataFieldSnapshot> fields)
        {
            Fields = fields;
        }

        public List<ObjectDataFieldSnapshot> Fields { get; }
    }

    internal sealed class ObjectDataTableSnapshot
    {
        public ObjectDataTableSnapshot(string tableName, List<ObjectDataRecordSnapshot> records)
        {
            TableName = tableName;
            Records = records;
        }

        public string TableName { get; }
        public List<ObjectDataRecordSnapshot> Records { get; }
        public bool HasAnyRecords => Records.Count > 0;
    }



    internal readonly record struct GisLayerSpec(int ColorIndex, string LinetypeName, string? PlotStyleName);

    internal sealed class ImportRunResult
    {
        public int ImportedCount { get; set; }
        public int KeptCount { get; set; }
        public int ClippedCount { get; set; }
        public int ErasedOutsideCount { get; set; }
    }

    internal sealed class ClipRunResult
    {
        public int KeptCount { get; set; }
        public int ClippedCount { get; set; }
        public int ErasedOutsideCount { get; set; }
    }

    internal enum GisBoundaryMode
    {
        SelectExistingPolyline,
        DrawTemporaryPolygon,
    }

    internal sealed class GisImportOptions
    {
        public bool ImportParcels { get; init; }
        public bool ImportStreetCenterlines { get; init; }
        public bool ImportSewerPipes { get; init; }
        public bool ImportSewerStructures { get; init; }
        public bool ImportStormPipes { get; init; }
        public bool ImportStormStructures { get; init; }
        public bool ImportSurveySewerPipes { get; init; }
        public bool ImportSurveySewerStructures { get; init; }
        public bool ImportSurveyStormPipes { get; init; }
        public bool ImportSurveyStormStructures { get; init; }
        public bool RunCleanupAfterImport { get; init; }
        public GisBoundaryMode BoundaryMode { get; init; }

        public bool HasAnyDatasetSelected => SelectedDatasets.Any();

        public IEnumerable<GisImportDataset> SelectedDatasets
        {
            get
            {
                if (ImportParcels) yield return GisImportDataset.ParcelLines;
                if (ImportStreetCenterlines) yield return GisImportDataset.StreetCenterlines;
                if (ImportSewerPipes) yield return GisImportDataset.SewerPipes;
                if (ImportSewerStructures) yield return GisImportDataset.SewerStructures;
                if (ImportStormPipes) yield return GisImportDataset.StormPipes;
                if (ImportStormStructures) yield return GisImportDataset.StormStructures;
                if (ImportSurveySewerPipes) yield return GisImportDataset.SurveySewerPipes;
                if (ImportSurveySewerStructures) yield return GisImportDataset.SurveySewerStructures;
                if (ImportSurveyStormPipes) yield return GisImportDataset.SurveyStormPipes;
                if (ImportSurveyStormStructures) yield return GisImportDataset.SurveyStormStructures;
            }
        }
    }

    internal sealed class GisImportDataset
    {
        public string DisplayName { get; init; } = string.Empty;
        public string ShapefilePath { get; init; } = string.Empty;
        public string TargetLayer { get; init; } = string.Empty;
        public string GeometryKind { get; init; } = string.Empty;
        public string ObjectDataTableName { get; init; } = string.Empty;
        public bool IsLinework { get; init; }
        public string CacheFileName { get; init; } = string.Empty;
        public bool CacheOnly { get; init; }
        public bool ImportAllFromCache { get; init; }

        public static GisImportDataset ParcelLines { get; } = new GisImportDataset
        {
            DisplayName = "Parcels",
            ShapefilePath = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_GRID_City\000-City\GIS\Assessor Parcels\Parcels.shp",
            TargetLayer = "GIS-CC-PRCL",
            GeometryKind = "Polyline",
            ObjectDataTableName = "Parcels",
            IsLinework = true,
            CacheFileName = "Parcels.dwg",
        };

        public static GisImportDataset StreetCenterlines { get; } = new GisImportDataset
        {
            DisplayName = "Street Centerlines",
            ShapefilePath = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_GRID_City\000-City\GIS\Street Centerlines\Street_Centerlines.shp",
            TargetLayer = "GIS-ROAD-CNTR",
            GeometryKind = "Polyline",
            ObjectDataTableName = "Street_Centerlines",
            IsLinework = true,
            CacheFileName = "Street_Centerlines.dwg",
        };

        public static GisImportDataset SewerPipes { get; } = new GisImportDataset
        {
            DisplayName = "Sewer Pipes",
            ShapefilePath = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_GRID_City\000-City\GIS\Sewer\SS_Pipes.shp",
            TargetLayer = "GIS-SSWR-PIPE-E",
            GeometryKind = "Polyline",
            ObjectDataTableName = "SS_Pipes",
            IsLinework = true,
            CacheFileName = "Sewer_Pipes.dwg",
        };

        public static GisImportDataset SewerStructures { get; } = new GisImportDataset
        {
            DisplayName = "Sewer Structures",
            ShapefilePath = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_GRID_City\000-City\GIS\Sewer\SS_Structures.shp",
            TargetLayer = "GIS-SSWR-STRC-E",
            GeometryKind = "Point",
            ObjectDataTableName = "SS_Structures",
            IsLinework = false,
            CacheFileName = "Sewer_Structures.dwg",
        };

        public static GisImportDataset StormPipes { get; } = new GisImportDataset
        {
            DisplayName = "Storm Pipes",
            ShapefilePath = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_GRID_City\000-City\GIS\Storm Drain\SD_Pipes.shp",
            TargetLayer = "GIS-STRM-PIPE-E",
            GeometryKind = "Polyline",
            ObjectDataTableName = "SD_Pipes",
            IsLinework = true,
            CacheFileName = "Storm_Pipes.dwg",
        };

        public static GisImportDataset StormStructures { get; } = new GisImportDataset
        {
            DisplayName = "Storm Structures",
            ShapefilePath = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_GRID_City\000-City\GIS\Storm Drain\SD_Structures.shp",
            TargetLayer = "GIS-STRM-STRC-E",
            GeometryKind = "Point",
            ObjectDataTableName = "SD_Structures",
            IsLinework = false,
            CacheFileName = "Storm_Structures.dwg",
        };



        public static GisImportDataset SurveySewerPipes { get; } = new GisImportDataset
        {
            DisplayName = "Survey Sewer Pipes",
            ShapefilePath = string.Empty,
            TargetLayer = string.Empty,
            GeometryKind = "Polyline",
            ObjectDataTableName = string.Empty,
            IsLinework = true,
            CacheFileName = "Survey_Sewer_Pipes.dwg",
            CacheOnly = true,
            ImportAllFromCache = true,
        };

        public static GisImportDataset SurveySewerStructures { get; } = new GisImportDataset
        {
            DisplayName = "Survey Sewer Structures",
            ShapefilePath = string.Empty,
            TargetLayer = string.Empty,
            GeometryKind = "Mixed",
            ObjectDataTableName = string.Empty,
            IsLinework = false,
            CacheFileName = "Survey_Sewer_Structures.dwg",
            CacheOnly = true,
            ImportAllFromCache = true,
        };

        public static GisImportDataset SurveyStormPipes { get; } = new GisImportDataset
        {
            DisplayName = "Survey Storm Pipes",
            ShapefilePath = string.Empty,
            TargetLayer = string.Empty,
            GeometryKind = "Polyline",
            ObjectDataTableName = string.Empty,
            IsLinework = true,
            CacheFileName = "Survey_Storm_Pipes.dwg",
            CacheOnly = true,
            ImportAllFromCache = true,
        };

        public static GisImportDataset SurveyStormStructures { get; } = new GisImportDataset
        {
            DisplayName = "Survey Storm Structures",
            ShapefilePath = string.Empty,
            TargetLayer = string.Empty,
            GeometryKind = "Mixed",
            ObjectDataTableName = string.Empty,
            IsLinework = false,
            CacheFileName = "Survey_Storm_Structures.dwg",
            CacheOnly = true,
            ImportAllFromCache = true,
        };


        public static IReadOnlyList<GisImportDataset> All { get; } = new[]
        {
            ParcelLines,
            StreetCenterlines,
            SewerPipes,
            SewerStructures,
            StormPipes,
            StormStructures,
            SurveySewerPipes,
            SurveySewerStructures,
            SurveyStormPipes,
            SurveyStormStructures,
        };
    }

    internal sealed class GisImportOptionsForm : Form
    {
        private readonly CheckBox _chkParcels;
        private readonly CheckBox _chkCenterlines;
        private readonly CheckBox _chkSewerPipes;
        private readonly CheckBox _chkSewerStructures;
        private readonly CheckBox _chkStormPipes;
        private readonly CheckBox _chkStormStructures;
        private readonly CheckBox _chkSurveySewerPipes;
        private readonly CheckBox _chkSurveySewerStructures;
        private readonly CheckBox _chkSurveyStormPipes;
        private readonly CheckBox _chkSurveyStormStructures;
        private readonly CheckBox _chkCleanup;
        private readonly RadioButton _radSelectPolyline;
        private readonly RadioButton _radDrawPolygon;

        public GisImportOptionsForm()
        {
            Text = "CLV GIS Import";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Width = 540;
            Height = 680;
            MinimumSize = new System.Drawing.Size(540, 680);

            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 6,
                Padding = new Padding(12),
            };
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

            var lbl1 = new Label
            {
                AutoSize = true,
                Text = "Select GIS datasets to import"
            };
            panel.Controls.Add(lbl1, 0, 0);

            var datasets = new FlowLayoutPanel
            {
                FlowDirection = WinFlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(6, 4, 0, 4)
            };

            _chkParcels = NewCheckBox("Parcels", true);
            _chkCenterlines = NewCheckBox("Street Centerlines", true);
            _chkSewerPipes = NewCheckBox("Sewer Pipes", false);
            _chkSewerStructures = NewCheckBox("Sewer Structures", false);
            _chkStormPipes = NewCheckBox("Storm Pipes", false);
            _chkStormStructures = NewCheckBox("Storm Structures", false);
            datasets.Controls.AddRange(new Control[]
            {
                _chkParcels, _chkCenterlines, _chkSewerPipes, _chkSewerStructures, _chkStormPipes, _chkStormStructures
            });
            panel.Controls.Add(datasets, 0, 1);

            var lblSurvey = new Label
            {
                AutoSize = true,
                Text = "Select SURVEYED datasets to import"
            };
            panel.Controls.Add(lblSurvey, 0, 2);

            var surveyDatasets = new FlowLayoutPanel
            {
                FlowDirection = WinFlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(6, 4, 0, 4)
            };

            _chkSurveySewerPipes = NewCheckBox("Sewer Pipes", false);
            _chkSurveySewerStructures = NewCheckBox("Sewer Structures", false);
            _chkSurveyStormPipes = NewCheckBox("Storm Pipes", false);
            _chkSurveyStormStructures = NewCheckBox("Storm Structures", false);
            surveyDatasets.Controls.AddRange(new Control[]
            {
                _chkSurveySewerPipes, _chkSurveySewerStructures, _chkSurveyStormPipes, _chkSurveyStormStructures
            });
            panel.Controls.Add(surveyDatasets, 0, 3);

            var boundaryGroup = new GroupBox
            {
                Text = "Boundary method",
                AutoSize = true,
                Dock = DockStyle.Top,
                Padding = new Padding(10)
            };

            var boundaryPanel = new FlowLayoutPanel
            {
                FlowDirection = WinFlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                Dock = DockStyle.Fill
            };
            _radSelectPolyline = new RadioButton { AutoSize = true, Text = "Select existing closed polyline", Checked = true };
            _radDrawPolygon = new RadioButton { AutoSize = true, Text = "Draw temporary polygon" };
            boundaryPanel.Controls.Add(_radSelectPolyline);
            boundaryPanel.Controls.Add(_radDrawPolygon);
            boundaryGroup.Controls.Add(boundaryPanel);
            panel.Controls.Add(boundaryGroup, 0, 4);

            var bottom = new FlowLayoutPanel
            {
                FlowDirection = WinFlowDirection.TopDown,
                WrapContents = false,
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(0, 6, 0, 0)
            };

            _chkCleanup = NewCheckBox("Run duplicate cleanup automatically after import", true);
            bottom.Controls.Add(_chkCleanup);

            var buttons = new FlowLayoutPanel
            {
                FlowDirection = WinFlowDirection.RightToLeft,
                Dock = DockStyle.Top,
                AutoSize = true,
                Padding = new Padding(0, 12, 0, 0),
                Anchor = AnchorStyles.Right
            };
            var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 90 };
            var btnCancel = new Button { Text = "CANCEL", DialogResult = DialogResult.Cancel, Width = 90 };
            buttons.Controls.Add(btnOk);
            buttons.Controls.Add(btnCancel);
            bottom.Controls.Add(buttons);

            panel.Controls.Add(bottom, 0, 5);

            Controls.Add(panel);
            AcceptButton = btnOk;
            CancelButton = btnCancel;
        }

        public GisImportOptions BuildOptions()
        {
            return new GisImportOptions
            {
                ImportParcels = _chkParcels.Checked,
                ImportStreetCenterlines = _chkCenterlines.Checked,
                ImportSewerPipes = _chkSewerPipes.Checked,
                ImportSewerStructures = _chkSewerStructures.Checked,
                ImportStormPipes = _chkStormPipes.Checked,
                ImportStormStructures = _chkStormStructures.Checked,
                ImportSurveySewerPipes = _chkSurveySewerPipes.Checked,
                ImportSurveySewerStructures = _chkSurveySewerStructures.Checked,
                ImportSurveyStormPipes = _chkSurveyStormPipes.Checked,
                ImportSurveyStormStructures = _chkSurveyStormStructures.Checked,
                RunCleanupAfterImport = _chkCleanup.Checked,
                BoundaryMode = _radDrawPolygon.Checked ? GisBoundaryMode.DrawTemporaryPolygon : GisBoundaryMode.SelectExistingPolyline,
            };
        }

        private static CheckBox NewCheckBox(string text, bool isChecked)
        {
            return new CheckBox
            {
                AutoSize = true,
                Text = text,
                Checked = isChecked
            };
        }
    }
}
