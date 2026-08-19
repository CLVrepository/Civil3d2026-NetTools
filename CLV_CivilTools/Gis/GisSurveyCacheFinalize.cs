using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using CLV_CivilTools.Shared;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using ColorMethod = Autodesk.AutoCAD.Colors.ColorMethod;
using AcPolyline = Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace CLV_CivilTools.Gis
{
    public static class GisSurveyCacheFinalizeCommands
    {
        private const string CacheRootFolder = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_GRID_City\000-City\GIS\_CLV_CACHE";
        private const string ExactMarkerLayer = "V-GIS-CACHE-CHECK-EXACT";
        private const string NearMarkerLayer = "V-GIS-CACHE-CHECK-NEAR";
        private const string SewerStructuresPreviewName = "CLV_CACHE_SSWR_STRC";
        private const string SewerPipesPreviewName = "CLV_CACHE_SSWR_PIPE";
        private const string StormStructuresPreviewName = "CLV_CACHE_STRM_STRC";
        private const string StormPipesPreviewName = "CLV_CACHE_STRM_PIPE";
        private const short PreviewColorIndex = 251;
        private const double ExactStructureTolerance = 0.25;
        private const double NearStructureTolerance = 2.0;
        private const double ExactPipeTolerance = 0.25;
        private const double NearPipeTolerance = 2.0;
        private const double HighDuplicateRatioStop = 0.85;
        private const string CacheTrackingRegAppName = "CLV_GIS_CACHE";
        private const string CacheTrackingHtmlTitle = "CLV GIS Cache Import Log";

        [CommandMethod("CLV-GIS-FINALIZE-STRC", CommandFlags.Modal)]
        public static void FinalizeStructures()
        {
            RunFinalize(FinalizeCategory.Structures);
        }

        [CommandMethod("CLV-GIS-FINALIZE-PIPES", CommandFlags.Modal)]
        public static void FinalizePipes()
        {
            RunFinalize(FinalizeCategory.Pipes);
        }

        [CommandMethod("CLV-GIS-COMPARE", CommandFlags.Modal)]
        public static void CompareCurrentDrawingToCache()
        {
            RunComparePreview();
        }

        [CommandMethod("CLV-GIS-REMOVE-COMPARE", CommandFlags.Modal)]
        public static void RemoveComparePreview()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            var db = doc.Database;
            var ed = doc.Editor;
            try
            {
                using var docLock = doc.LockDocument();
                DetachAllPreviewXrefs(db);
                ClearCompareMarkers(db);
                ed.WriteMessage("\nCLV-GIS-REMOVE-COMPARE complete. Removed preview cache xrefs and compare markers.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-REMOVE-COMPARE failed: {ex.Message}");
            }
        }

        [CommandMethod("GISCACHELOGOPEN", CommandFlags.Modal)]
        public static void OpenCacheLogReport()
        {
            RunCacheLogAction(openAfterGenerate: true);
        }

        [CommandMethod("GISCACHELOGREPORT", CommandFlags.Modal)]
        public static void RegenerateCacheLogReport()
        {
            RunCacheLogAction(openAfterGenerate: false);
        }

        [CommandMethod("GISCACHEIMPORTS", CommandFlags.Modal)]
        public static void ListCacheImports()
        {
            RunCacheImportsAction();
        }

        [CommandMethod("GISCACHEHIGHLIGHTIMPORT", CommandFlags.Modal)]
        public static void HighlightCacheImport()
        {
            RunCacheHighlightAction();
        }

        [CommandMethod("GISCACHEREMOVEBATCH", CommandFlags.Modal)]
        public static void RemoveCacheBatch()
        {
            RunCacheRemoveBatchAction();
        }

        [CommandMethod("GISSURVEYREPORTHTML", CommandFlags.Modal)]
        public static void GenerateCombinedSurveyReportHtml()
        {
            RunCombinedSurveyReportAction(openAfterGenerate: true);
        }

        private static void RunComparePreview()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            var ed = doc.Editor;
            var db = doc.Database;

            try
            {
                using var docLock = doc.LockDocument();

                CompareConfig? resolvedConfig = ResolveCompareConfig(db, ed);
                if (resolvedConfig == null)
                    return;

                CompareConfig config = resolvedConfig.Value;
                string drawingCs = DetectDrawingCoordinateSystem();
                string structuresPath = Path.Combine(CacheRootFolder, drawingCs, config.StructuresCacheFileName);
                string pipesPath = Path.Combine(CacheRootFolder, drawingCs, config.PipesCacheFileName);

                if (!File.Exists(structuresPath) && !File.Exists(pipesPath))
                {
                    ed.WriteMessage("\nCompare cancelled. Matching cache preview files were not found.");
                    return;
                }

                AttachPreviewXrefs(db, config.UtilityKind, structuresPath, pipesPath);
                ClearCompareMarkers(db);

                var sourceEntityIds = CollectSourceEntityIds(db, config.SourceLayers);
                var sourceSignatures = ReadSourceSignatures(db, sourceEntityIds);
                var targetSignatures = new List<EntitySignature>();

                if (File.Exists(structuresPath))
                {
                    using var structuresDb = new Database(false, true);
                    structuresDb.ReadDwgFile(structuresPath, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
                    structuresDb.CloseInput(true);
                    targetSignatures.AddRange(ReadTargetSignatures(structuresDb, config.SourceLayers));
                }

                if (File.Exists(pipesPath))
                {
                    using var pipesDb = new Database(false, true);
                    pipesDb.ReadDwgFile(pipesPath, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
                    pipesDb.CloseInput(true);
                    targetSignatures.AddRange(ReadTargetSignatures(pipesDb, config.SourceLayers));
                }

                CompareSummary compareSummary = CompareSignatures(sourceSignatures, targetSignatures);
                DrawCompareMarkers(db, compareSummary);

                ed.WriteMessage("\nCLV-GIS-COMPARE");
                ed.WriteMessage($"\n  Utility          : {config.UtilityKind}");
                ed.WriteMessage($"\n  Coordinate System: {drawingCs}");
                ed.WriteMessage($"\n  Structures Cache : {structuresPath}");
                ed.WriteMessage($"\n  Pipes Cache      : {pipesPath}");
                ed.WriteMessage($"\n  Source Entities  : {sourceSignatures.Count}");
                ed.WriteMessage($"\n  Exact Duplicates : {compareSummary.ExactMatches.Count}");
                ed.WriteMessage($"\n  Near Conflicts   : {compareSummary.NearMatches.Count}");
                ed.WriteMessage("\n  Visualization    : preview cache xrefs attached in gray (ACI 251); red markers = exact duplicates, yellow markers = nearby conflicts.");
                if (sourceSignatures.Count == 0)
                    ed.WriteMessage("\n  Note             : No current finalized GIS entities were found on the active utility layers, so only the gray cache preview was attached.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCompare failed: {ex.Message}");
            }
        }

        private static void RunFinalize(FinalizeCategory category)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            var ed = doc.Editor;
            var db = doc.Database;

            try
            {
                using var docLock = doc.LockDocument();

                FinalizeConfig? resolvedConfig = ResolveFinalizeConfig(db, ed, category);
                if (resolvedConfig == null)
                    return;

                FinalizeConfig config = resolvedConfig.Value;
                string drawingCs = DetectDrawingCoordinateSystem();
                string structuresPath = Path.Combine(CacheRootFolder, drawingCs, config.StructuresCacheFileName);
                string pipesPath = Path.Combine(CacheRootFolder, drawingCs, config.PipesCacheFileName);
                string targetPath = category == FinalizeCategory.Structures ? structuresPath : pipesPath;
                string cacheName = GetCacheNameForConfig(config, category);
                string dataSetType = GetDataSetTypeForConfig(config, category);
                string projectNumber = PromptForProjectNumber(ed);
                if (string.IsNullOrWhiteSpace(projectNumber))
                {
                    ed.WriteMessage("\nFinalize cancelled. Project Number is required.");
                    return;
                }

                string userName = GetCurrentUserName();
                string sourceDwg = GetSourceDrawingPath(doc, db);
                string importId = BuildImportId(cacheName, projectNumber);
                DateTime importTimestampUtc = DateTime.UtcNow;

                if (!File.Exists(targetPath))
                {
                    ed.WriteMessage($"\nFinalize cancelled. Target cache file was not found: {targetPath}");
                    return;
                }

                var sourceEntityIds = CollectSourceEntityIds(db, config.SourceLayers);
                if (sourceEntityIds.Count == 0)
                {
                    ed.WriteMessage("\nFinalize cancelled. No source entities were found on the expected layers.");
                    return;
                }

                var sourceSignatures = ReadSourceSignatures(db, sourceEntityIds);
                if (sourceSignatures.Count == 0)
                {
                    ed.WriteMessage("\nFinalize cancelled. Source entities were found, but no comparable geometry could be read.");
                    return;
                }

                AttachPreviewXrefs(db, config.UtilityKind, structuresPath, pipesPath);
                ClearCompareMarkers(db);

                CompareSummary compareSummary;
                using (var cacheDb = new Database(false, true))
                {
                    cacheDb.ReadDwgFile(targetPath, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
                    cacheDb.CloseInput(true);
                    var targetSignatures = ReadTargetSignatures(cacheDb, config.SourceLayers);
                    compareSummary = CompareSignatures(sourceSignatures, targetSignatures);
                }

                var exactDuplicateIds = compareSummary.ExactMatches
                    .Select(x => x.EntityId)
                    .Where(x => !x.IsNull && x.IsValid)
                    .ToHashSet();

                var pendingCloneIds = sourceEntityIds
                    .Where(x => !exactDuplicateIds.Contains(x))
                    .ToList();

                DrawCompareMarkers(db, compareSummary);
                ed.WriteMessage($"\nCLV-GIS-FINALIZE-{(category == FinalizeCategory.Structures ? "STRC" : "PIPES")}");
                ed.WriteMessage($"\n  Utility          : {config.UtilityKind}");
                ed.WriteMessage($"\n  Coordinate System: {drawingCs}");
                ed.WriteMessage($"\n  Target Cache     : {targetPath}");
                ed.WriteMessage($"\n  Source Entities  : {sourceSignatures.Count}");
                ed.WriteMessage($"\n  Exact Duplicates : {compareSummary.ExactMatches.Count}");
                ed.WriteMessage($"\n  Near Conflicts   : {compareSummary.NearMatches.Count}");
                ed.WriteMessage($"\n  Pending Append   : {pendingCloneIds.Count}");
                ed.WriteMessage("\n  Visualization    : preview cache xrefs attached in gray (ACI 251); red markers = exact duplicates, yellow markers = nearby conflicts.");

                if (pendingCloneIds.Count == 0)
                {
                    ed.WriteMessage("\nDuplicate detection found no new entities to append. Finalize stopped without modifying the cache.");
                    return;
                }

                double exactRatio = sourceSignatures.Count == 0
                    ? 0.0
                    : (double)compareSummary.ExactMatches.Count / sourceSignatures.Count;

                if (sourceSignatures.Count >= 10 && exactRatio >= HighDuplicateRatioStop)
                {
                    ed.WriteMessage("\nHigh duplicate ratio detected. This looks like a possible accidental full-site re-export.");
                    if (!PromptProceed(ed, $"Proceed anyway and append to cache? Exact duplicate ratio = {exactRatio:P0}"))
                    {
                        ed.WriteMessage("\nFinalize stopped. Review the gray xrefs and conflict markers before running again.");
                        return;
                    }
                }
                else if (!PromptProceed(ed, "Proceed with cache finalize after review?"))
                {
                    ed.WriteMessage("\nFinalize stopped. Review the gray xrefs and conflict markers before running again.");
                    return;
                }

                DetachPreviewXrefs(db, config.UtilityKind);
                int duplicateCountSkipped = sourceEntityIds.Count - pendingCloneIds.Count;
                int objectCountAdded = CloneEntitiesToCache(db, pendingCloneIds, targetPath, importId, projectNumber, dataSetType, sourceDwg, userName, cacheName, importTimestampUtc);
                AppendManifestEntry(targetPath, cacheName, new CacheManifestEntry
                {
                    ImportId = importId,
                    TimestampUtc = importTimestampUtc,
                    UserName = userName,
                    ProjectNumber = projectNumber,
                    DataSetType = dataSetType,
                    SourceDwg = sourceDwg,
                    CacheTarget = targetPath,
                    ObjectCountAdded = objectCountAdded,
                    ObjectCountSkipped = duplicateCountSkipped,
                    Status = duplicateCountSkipped > 0 ? $"Imported (Skipped {duplicateCountSkipped} exact duplicate(s))" : "Imported"
                });

                AttachPreviewXrefs(db, config.UtilityKind, structuresPath, pipesPath);

                string reportPath = GetHtmlReportPath(targetPath);
                ed.WriteMessage($"\nFinalize complete. Appended {objectCountAdded} entity(ies) to {targetPath}");
                ed.WriteMessage($"\n  Exact Duplicates Skipped: {duplicateCountSkipped}");
                ed.WriteMessage($"\n  Import ID        : {importId}");
                ed.WriteMessage($"\n  Project Number   : {projectNumber}");
                ed.WriteMessage($"\n  Cache Name       : {cacheName}");
                ed.WriteMessage($"\n  Log Report       : {reportPath}");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nFinalize failed: {ex.Message}");
            }
        }

        private static FinalizeConfig? ResolveFinalizeConfig(Database db, Editor ed, FinalizeCategory category)
        {
            UtilityKind? utilityKind = ResolveUtilityKind(db, ed,
                category == FinalizeCategory.Structures
                    ? new[] { "C-SSWR-STRC-E", "C-SSWR-STRC-INNR" }
                    : new[] { "C-SSWR-PIPE-E", "C-SSWR-PIPE-CNTR-E" },
                category == FinalizeCategory.Structures
                    ? new[] { "C-STRM-STRC-E", "C-STRM-STRC-INNR" }
                    : new[] { "C-STRM-PIPE-E", "C-STRM-PIPE-CNTR-E" },
                "exported");

            if (utilityKind == null)
                return null;

            if (utilityKind == UtilityKind.Sewer)
            {
                return category == FinalizeCategory.Structures
                    ? new FinalizeConfig(utilityKind.Value, category, new[] { "C-SSWR-STRC-E", "C-SSWR-STRC-INNR" }, "Survey_Sewer_Structures.dwg", "Survey_Sewer_Pipes.dwg")
                    : new FinalizeConfig(utilityKind.Value, category, new[] { "C-SSWR-PIPE-E", "C-SSWR-PIPE-CNTR-E" }, "Survey_Sewer_Structures.dwg", "Survey_Sewer_Pipes.dwg");
            }

            return category == FinalizeCategory.Structures
                ? new FinalizeConfig(utilityKind.Value, category, new[] { "C-STRM-STRC-E", "C-STRM-STRC-INNR" }, "Survey_Storm_Structures.dwg", "Survey_Storm_Pipes.dwg")
                : new FinalizeConfig(utilityKind.Value, category, new[] { "C-STRM-PIPE-E", "C-STRM-PIPE-CNTR-E" }, "Survey_Storm_Structures.dwg", "Survey_Storm_Pipes.dwg");
        }

        private static CompareConfig? ResolveCompareConfig(Database db, Editor ed)
        {
            UtilityKind? utilityKind = ResolveUtilityKind(db, ed,
                new[] { "C-SSWR-STRC-E", "C-SSWR-STRC-INNR", "C-SSWR-PIPE-E", "C-SSWR-PIPE-CNTR-E" },
                new[] { "C-STRM-STRC-E", "C-STRM-STRC-INNR", "C-STRM-PIPE-E", "C-STRM-PIPE-CNTR-E" },
                "compared");

            if (utilityKind == null)
                return null;

            return utilityKind == UtilityKind.Sewer
                ? new CompareConfig(utilityKind.Value,
                    new[] { "C-SSWR-STRC-E", "C-SSWR-STRC-INNR", "C-SSWR-PIPE-E", "C-SSWR-PIPE-CNTR-E" },
                    "Survey_Sewer_Structures.dwg",
                    "Survey_Sewer_Pipes.dwg")
                : new CompareConfig(utilityKind.Value,
                    new[] { "C-STRM-STRC-E", "C-STRM-STRC-INNR", "C-STRM-PIPE-E", "C-STRM-PIPE-CNTR-E" },
                    "Survey_Storm_Structures.dwg",
                    "Survey_Storm_Pipes.dwg");
        }

        private static UtilityKind? ResolveUtilityKind(Database db, Editor ed, IReadOnlyCollection<string> sewerLayers, IReadOnlyCollection<string> stormLayers, string verb)
        {
            int sewerCount = CountEntitiesOnLayers(db, sewerLayers);
            int stormCount = CountEntitiesOnLayers(db, stormLayers);

            if (sewerCount > 0 && stormCount == 0)
                return UtilityKind.Sewer;

            if (stormCount > 0 && sewerCount == 0)
                return UtilityKind.Storm;

            if (stormCount == 0 && sewerCount == 0)
            {
                ed.WriteMessage("\nUnable to detect sewer or storm source layers in model space.");
                return null;
            }

            var pko = new PromptKeywordOptions($"\nBoth sewer and storm finalized layers were found. Which utility should be {verb}? [Sewer/Storm/Cancel]", "Sewer Storm Cancel")
            {
                AllowNone = false
            };
            var pkr = ed.GetKeywords(pko);
            if (pkr.Status != PromptStatus.OK || pkr.StringResult.Equals("Cancel", StringComparison.OrdinalIgnoreCase))
                return null;

            return pkr.StringResult.Equals("Sewer", StringComparison.OrdinalIgnoreCase)
                ? UtilityKind.Sewer
                : UtilityKind.Storm;
        }

        private static string DetectDrawingCoordinateSystem()
        {
            try
            {
                MethodInfo? method = typeof(GisImportCommands).GetMethod("InferDrawingCoordinateSystem", BindingFlags.Static | BindingFlags.NonPublic);
                string? value = method?.Invoke(null, null) as string;
                return string.IsNullOrWhiteSpace(value) ? "NV83.NCRS-LVF" : value.Trim();
            }
            catch
            {
                return "NV83.NCRS-LVF";
            }
        }

        private static int CountEntitiesOnLayers(Database db, IReadOnlyCollection<string> layerNames)
        {
            int count = 0;
            using var tr = db.TransactionManager.StartTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var model = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId id in model)
            {
                if (id.IsErased || !id.IsValid)
                    continue;

                if (tr.GetObject(id, OpenMode.ForRead, false) is Entity ent && layerNames.Contains(ent.Layer, StringComparer.OrdinalIgnoreCase))
                    count++;
            }

            return count;
        }

        private static List<ObjectId> CollectSourceEntityIds(Database db, IReadOnlyCollection<string> layerNames)
        {
            var ids = new List<ObjectId>();
            using var tr = db.TransactionManager.StartTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var model = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId id in model)
            {
                if (id.IsErased || !id.IsValid)
                    continue;

                if (tr.GetObject(id, OpenMode.ForRead, false) is Entity ent && layerNames.Contains(ent.Layer, StringComparer.OrdinalIgnoreCase))
                    ids.Add(id);
            }

            return ids;
        }

        private static List<EntitySignature> ReadSourceSignatures(Database db, IReadOnlyCollection<ObjectId> entityIds)
        {
            var result = new List<EntitySignature>();
            using var tr = db.TransactionManager.StartTransaction();
            foreach (ObjectId id in entityIds)
            {
                if (id.IsErased || !id.IsValid)
                    continue;

                if (tr.GetObject(id, OpenMode.ForRead, false) is Entity ent && TryCreateSignature(ent, id, out EntitySignature sig))
                    result.Add(sig);
            }

            return result;
        }

        private static List<EntitySignature> ReadTargetSignatures(Database db, IReadOnlyCollection<string> layerNames)
        {
            var result = new List<EntitySignature>();
            using var tr = db.TransactionManager.StartTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var model = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId id in model)
            {
                if (id.IsErased || !id.IsValid)
                    continue;

                if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity ent)
                    continue;

                if (!layerNames.Contains(ent.Layer, StringComparer.OrdinalIgnoreCase))
                    continue;

                if (TryCreateSignature(ent, id, out EntitySignature sig))
                    result.Add(sig);
            }

            return result;
        }

        private static bool TryCreateSignature(Entity ent, ObjectId id, out EntitySignature signature)
        {
            signature = default;
            if (!TryGetExtents(ent, out Extents3d ext))
                return false;

            Point3d center = new Point3d(
                (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5,
                (ext.MinPoint.Z + ext.MaxPoint.Z) * 0.5);

            bool isLinear = false;
            Point3d start = Point3d.Origin;
            Point3d end = Point3d.Origin;
            double length = 0.0;

            if (ent is Line ln)
            {
                isLinear = true;
                start = ln.StartPoint;
                end = ln.EndPoint;
                length = ln.Length;
            }
            else if (ent is AcPolyline pl)
            {
                if (!pl.Closed && pl.NumberOfVertices >= 2)
                {
                    isLinear = true;
                    start = pl.StartPoint;
                    end = pl.EndPoint;
                    length = pl.Length;
                }
            }

            signature = new EntitySignature(id, ent.Layer, center, ext, isLinear, start, end, length);
            return true;
        }

        private static CompareSummary CompareSignatures(IReadOnlyList<EntitySignature> source, IReadOnlyList<EntitySignature> target)
        {
            var exact = new List<EntitySignature>();
            var near = new List<EntitySignature>();
            var targetByLayer = target
                .GroupBy(x => x.Layer, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

            foreach (EntitySignature src in source)
            {
                if (!targetByLayer.TryGetValue(src.Layer, out List<EntitySignature>? candidates) || candidates == null || candidates.Count == 0)
                    continue;

                bool foundExact = candidates.Any(t => IsExactMatch(src, t));
                if (foundExact)
                {
                    exact.Add(src);
                    continue;
                }

                bool foundNear = candidates.Any(t => IsNearMatch(src, t));
                if (foundNear)
                    near.Add(src);
            }

            return new CompareSummary(exact, near);
        }

        private static bool IsExactMatch(EntitySignature a, EntitySignature b)
        {
            if (a.IsLinear && b.IsLinear)
            {
                return EndpointsMatch(a.StartPoint, a.EndPoint, b.StartPoint, b.EndPoint, ExactPipeTolerance);
            }

            return a.Center.DistanceTo(b.Center) <= ExactStructureTolerance;
        }

        private static bool IsNearMatch(EntitySignature a, EntitySignature b)
        {
            if (a.IsLinear && b.IsLinear)
            {
                Point3d amid = Midpoint(a.StartPoint, a.EndPoint);
                Point3d bmid = Midpoint(b.StartPoint, b.EndPoint);
                if (amid.DistanceTo(bmid) <= NearPipeTolerance && Math.Abs(a.Length - b.Length) <= 5.0)
                    return true;

                return OneEndpointNear(a.StartPoint, a.EndPoint, b.StartPoint, b.EndPoint, NearPipeTolerance);
            }

            return a.Center.DistanceTo(b.Center) <= NearStructureTolerance;
        }

        private static bool EndpointsMatch(Point3d a1, Point3d a2, Point3d b1, Point3d b2, double tol)
        {
            bool direct = a1.DistanceTo(b1) <= tol && a2.DistanceTo(b2) <= tol;
            bool reverse = a1.DistanceTo(b2) <= tol && a2.DistanceTo(b1) <= tol;
            return direct || reverse;
        }

        private static bool OneEndpointNear(Point3d a1, Point3d a2, Point3d b1, Point3d b2, double tol)
        {
            return a1.DistanceTo(b1) <= tol || a1.DistanceTo(b2) <= tol || a2.DistanceTo(b1) <= tol || a2.DistanceTo(b2) <= tol;
        }

        private static Point3d Midpoint(Point3d a, Point3d b)
        {
            return new Point3d((a.X + b.X) * 0.5, (a.Y + b.Y) * 0.5, (a.Z + b.Z) * 0.5);
        }

        private static bool TryGetExtents(Entity ent, out Extents3d extents)
        {
            try
            {
                extents = ent.GeometricExtents;
                return true;
            }
            catch
            {
                extents = default;
                return false;
            }
        }

        private static void DrawCompareMarkers(Database db, CompareSummary summary)
        {
            EnsureMarkerLayer(db, ExactMarkerLayer, 1);
            EnsureMarkerLayer(db, NearMarkerLayer, 2);

            using var tr = db.TransactionManager.StartTransaction();
            var currentSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

            foreach (EntitySignature sig in summary.ExactMatches)
            {
                var circle = CreateMarkerCircle(sig, ExactMarkerLayer, 1);
                currentSpace.AppendEntity(circle);
                tr.AddNewlyCreatedDBObject(circle, true);
            }

            foreach (EntitySignature sig in summary.NearMatches)
            {
                var circle = CreateMarkerCircle(sig, NearMarkerLayer, 2);
                currentSpace.AppendEntity(circle);
                tr.AddNewlyCreatedDBObject(circle, true);
            }

            tr.Commit();
        }

        private static Circle CreateMarkerCircle(EntitySignature sig, string layerName, short colorIndex)
        {
            double dx = Math.Abs(sig.Extents.MaxPoint.X - sig.Extents.MinPoint.X);
            double dy = Math.Abs(sig.Extents.MaxPoint.Y - sig.Extents.MinPoint.Y);
            double radius = Math.Max(Math.Max(dx, dy) * 0.6, 1.5);
            if (sig.IsLinear)
                radius = Math.Max(radius, Math.Max(sig.Length * 0.2, 2.0));

            return new Circle(sig.Center, Vector3d.ZAxis, radius)
            {
                Layer = layerName,
                Color = AcColor.FromColorIndex(ColorMethod.ByAci, colorIndex)
            };
        }

        private static void ClearCompareMarkers(Database db)
        {
            LayerState.EnsureLayer(ExactMarkerLayer);
            LayerState.EnsureLayer(NearMarkerLayer);
            LayerState.DeleteEntitiesOnLayer(db, ExactMarkerLayer);
            LayerState.DeleteEntitiesOnLayer(db, NearMarkerLayer);
        }

        private static void EnsureMarkerLayer(Database db, string layerName, short colorIndex)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            LayerTableRecord ltr;
            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();
                ltr = new LayerTableRecord { Name = layerName };
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
            else
            {
                ltr = (LayerTableRecord)tr.GetObject(lt[layerName], OpenMode.ForWrite);
            }

            ltr.Color = AcColor.FromColorIndex(ColorMethod.ByAci, colorIndex);
            tr.Commit();
        }

        private static bool PromptProceed(Editor ed, string message)
        {
            var pko = new PromptKeywordOptions($"\n{message} [Yes/No]", "Yes No")
            {
                AllowNone = false
            };
            var pkr = ed.GetKeywords(pko);
            return pkr.Status == PromptStatus.OK && pkr.StringResult.Equals("Yes", StringComparison.OrdinalIgnoreCase);
        }

        private static int CloneEntitiesToCache(
            Database sourceDb,
            IReadOnlyCollection<ObjectId> sourceIds,
            string targetPath,
            string importId,
            string projectNumber,
            string dataSetType,
            string sourceDwg,
            string userName,
            string cacheName,
            DateTime importTimestampUtc)
        {
            using var targetDb = new Database(false, true);
            targetDb.ReadDwgFile(targetPath, FileOpenMode.OpenForReadAndWriteNoShare, true, string.Empty);
            targetDb.CloseInput(true);

            var ids = new ObjectIdCollection(sourceIds.ToArray());
            var mapping = new IdMapping();
            sourceDb.WblockCloneObjects(ids, targetDb.CurrentSpaceId, mapping, DuplicateRecordCloning.Ignore, false);

            int stampedCount = 0;
            using (var tr = targetDb.TransactionManager.StartTransaction())
            {
                EnsureRegAppRecord(targetDb, tr, CacheTrackingRegAppName);
                foreach (IdPair idPair in mapping)
                {
                    if (!idPair.IsCloned || idPair.Value.IsNull || !idPair.Value.IsValid)
                        continue;

                    if (tr.GetObject(idPair.Value, OpenMode.ForWrite, false) is not Entity clonedEntity)
                        continue;

                    StampEntityWithCacheMetadata(clonedEntity, importId, projectNumber, dataSetType, sourceDwg, userName, cacheName, importTimestampUtc);
                    stampedCount++;
                }

                tr.Commit();
            }

            targetDb.SaveAs(targetPath, DwgVersion.Current);
            return stampedCount;
        }

        private static void AttachPreviewXrefs(Database db, UtilityKind utilityKind, string structuresPath, string pipesPath)
        {
            EnsurePreviewXrefDisplaySettings();

            string structuresName = utilityKind == UtilityKind.Sewer ? SewerStructuresPreviewName : StormStructuresPreviewName;
            string pipesName = utilityKind == UtilityKind.Sewer ? SewerPipesPreviewName : StormPipesPreviewName;

            if (File.Exists(structuresPath))
                AttachOrReplacePreviewXref(db, structuresPath, structuresName);

            if (File.Exists(pipesPath))
                AttachOrReplacePreviewXref(db, pipesPath, pipesName);
        }

        private static void DetachPreviewXrefs(Database db, UtilityKind utilityKind)
        {
            string structuresName = utilityKind == UtilityKind.Sewer ? SewerStructuresPreviewName : StormStructuresPreviewName;
            string pipesName = utilityKind == UtilityKind.Sewer ? SewerPipesPreviewName : StormPipesPreviewName;
            DetachPreviewXref(db, structuresName);
            DetachPreviewXref(db, pipesName);
        }

        private static void DetachAllPreviewXrefs(Database db)
        {
            DetachPreviewXref(db, SewerStructuresPreviewName);
            DetachPreviewXref(db, SewerPipesPreviewName);
            DetachPreviewXref(db, StormStructuresPreviewName);
            DetachPreviewXref(db, StormPipesPreviewName);
        }

        private static void AttachOrReplacePreviewXref(Database db, string xrefPath, string xrefName)
        {
            DetachPreviewXref(db, xrefName);
            EnsurePreviewXrefDisplaySettings();

            using var tr = db.TransactionManager.StartTransaction();
            ObjectId xrefBtrId = db.AttachXref(xrefPath, xrefName);
            if (xrefBtrId.IsNull)
            {
                tr.Commit();
                return;
            }

            var currentSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var br = new BlockReference(Point3d.Origin, xrefBtrId)
            {
                Layer = "0",
                Color = AcColor.FromColorIndex(ColorMethod.ByAci, PreviewColorIndex)
            };
            currentSpace.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);
            tr.Commit();

            db.ReloadXrefs(new ObjectIdCollection(new[] { xrefBtrId }));
            ApplyPreviewLayerOverrides(db, xrefName);
        }


        private static void EnsurePreviewXrefDisplaySettings()
        {
            try { AcadApp.SetSystemVariable("VISRETAIN", 1); } catch { }
            try { AcadApp.SetSystemVariable("VISRETAINMODE", 0); } catch { }
            try { AcadApp.SetSystemVariable("XREFOVERRIDE", 1); } catch { }
        }

        private static void DetachPreviewXref(Database db, string xrefName)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(xrefName))
            {
                tr.Commit();
                return;
            }

            ObjectId btrId = bt[xrefName];
            tr.Commit();
            db.DetachXref(btrId);
        }

        private static void ApplyPreviewLayerOverrides(Database db, string xrefName)
        {
            EnsurePreviewXrefDisplaySettings();
            string prefix = xrefName + "|";
            using var tr = db.TransactionManager.StartTransaction();
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (ObjectId id in lt)
            {
                var ltr = (LayerTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                if (!ltr.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                ltr.Color = AcColor.FromColorIndex(ColorMethod.ByAci, PreviewColorIndex);
                ltr.IsLocked = true;
            }

            ApplyPreviewReferenceColorOverride(tr, db, xrefName);
            tr.Commit();
        }

        private static void ApplyPreviewReferenceColorOverride(Transaction tr, Database db, string xrefName)
        {
            var currentSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            foreach (ObjectId id in currentSpace)
            {
                if (!id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(BlockReference))))
                    continue;

                var br = (BlockReference)tr.GetObject(id, OpenMode.ForWrite);
                var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                if (!string.Equals(btr.Name, xrefName, StringComparison.OrdinalIgnoreCase))
                    continue;

                br.Color = AcColor.FromColorIndex(ColorMethod.ByAci, PreviewColorIndex);
            }
        }

        private static void RunCacheImportsAction()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            var ed = doc.Editor;
            try
            {
                string? cachePath = GetActiveCacheDrawingPath(doc.Database, ed);
                if (string.IsNullOrWhiteSpace(cachePath))
                    return;

                string jsonPath = GetJsonManifestPath(cachePath);
                CacheManifest manifest = ReadManifest(jsonPath);
                if (manifest.Entries.Count == 0)
                {
                    ed.WriteMessage("\nNo import batches were found for the active cache drawing.");
                    return;
                }

                ed.WriteMessage("\nGISCACHEIMPORTS");
                ed.WriteMessage("\n  Cache: " + GetCacheDisplayName(cachePath, manifest));
                for (int i = 0; i < manifest.Entries.Count; i++)
                {
                    CacheManifestEntry entry = manifest.Entries
                        .OrderByDescending(x => x.TimestampUtc)
                        .ToList()[i];

                    ed.WriteMessage($@"
  [{i + 1}] {entry.ImportId}
      TimestampUtc     : {entry.TimestampUtc:yyyy-MM-dd HH:mm:ss}
      ProjectNumber    : {entry.ProjectNumber}
      DataSetType      : {entry.DataSetType}
      SourceDwg        : {entry.SourceDwg}
      ObjectCountAdded : {entry.ObjectCountAdded}
      ObjectCountSkipped: {entry.ObjectCountSkipped}
      Status           : {entry.Status}");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nGISCACHEIMPORTS failed: " + ex.Message);
            }
        }

        private static void RunCacheHighlightAction()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            var ed = doc.Editor;
            var db = doc.Database;
            try
            {
                string? cachePath = GetActiveCacheDrawingPath(db, ed);
                if (string.IsNullOrWhiteSpace(cachePath))
                    return;

                CacheManifest manifest = ReadManifest(GetJsonManifestPath(cachePath));
                CacheManifestEntry? selectedEntry = PromptForManifestEntry(ed, manifest, includeRemoved: true, "highlight");
                if (selectedEntry == null)
                    return;

                List<ObjectId> ids = FindEntitiesByImportId(db, selectedEntry.ImportId);
                if (ids.Count == 0)
                {
                    ed.WriteMessage("\nNo entities were found in the active cache drawing for that ImportId.");
                    ed.SetImpliedSelection(Array.Empty<ObjectId>());
                    return;
                }

                ed.SetImpliedSelection(ids.ToArray());
                ed.WriteMessage($@"
Highlighted {ids.Count} entity(ies) for ImportId: {selectedEntry.ImportId}
  Use the active selection set to inspect the batch.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nGISCACHEHIGHLIGHTIMPORT failed: " + ex.Message);
            }
        }

        private static void RunCacheRemoveBatchAction()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            var ed = doc.Editor;
            var db = doc.Database;
            try
            {
                string? cachePath = GetActiveCacheDrawingPath(db, ed);
                if (string.IsNullOrWhiteSpace(cachePath))
                    return;

                string jsonPath = GetJsonManifestPath(cachePath);
                CacheManifest manifest = ReadManifest(jsonPath);
                CacheManifestEntry? selectedEntry = PromptForManifestEntry(ed, manifest, includeRemoved: false, "remove");
                if (selectedEntry == null)
                    return;

                List<ObjectId> ids = FindEntitiesByImportId(db, selectedEntry.ImportId);
                if (ids.Count == 0)
                {
                    ed.WriteMessage("\nNo active entities were found in the cache drawing for that ImportId.");
                    return;
                }

                if (!PromptProceed(ed, $"Erase {ids.Count} entity(ies) for ImportId {selectedEntry.ImportId}?"))
                {
                    ed.WriteMessage("\nBatch removal cancelled.");
                    return;
                }

                int erasedCount = EraseEntities(db, ids);
                MarkManifestEntryRemoved(manifest, selectedEntry.ImportId);
                SaveManifest(jsonPath, manifest);
                GenerateHtmlReport(cachePath, manifest);

                ed.SetImpliedSelection(Array.Empty<ObjectId>());
                ed.WriteMessage($@"
Removed {erasedCount} entity(ies) for ImportId: {selectedEntry.ImportId}
  Manifest status updated to Removed.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nGISCACHEREMOVEBATCH failed: " + ex.Message);
            }
        }

        private static string? GetActiveCacheDrawingPath(Database db, Editor ed)
        {
            string filePath = db.Filename;
            if (string.IsNullOrWhiteSpace(filePath))
            {
                ed.WriteMessage("\nThis command must be run from a saved cache DWG.");
                return null;
            }

            string fileName = Path.GetFileName(filePath);
            bool isSupported = fileName.Equals("Survey_Sewer_Pipes.dwg", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("Survey_Sewer_Structures.dwg", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("Survey_Storm_Pipes.dwg", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("Survey_Storm_Structures.dwg", StringComparison.OrdinalIgnoreCase);

            if (!isSupported)
            {
                ed.WriteMessage("\nThis command must be run inside a supported cache DWG.");
                return null;
            }

            return filePath;
        }

        private static CacheManifestEntry? PromptForManifestEntry(Editor ed, CacheManifest manifest, bool includeRemoved, string actionVerb)
        {
            List<CacheManifestEntry> entries = manifest.Entries
                .OrderByDescending(x => x.TimestampUtc)
                .Where(x => includeRemoved || !x.Status.Equals("Removed", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (entries.Count == 0)
            {
                ed.WriteMessage("\nNo matching import batches are available for this action.");
                return null;
            }

            ed.WriteMessage("\nAvailable import batches:");
            for (int i = 0; i < entries.Count; i++)
            {
                CacheManifestEntry entry = entries[i];
                ed.WriteMessage($@"
  [{i + 1}] {entry.ImportId} | {entry.ProjectNumber} | {entry.DataSetType} | Added={entry.ObjectCountAdded} | Status={entry.Status}");
            }

            var pio = new PromptIntegerOptions($@"
Enter batch number to {actionVerb}")
            {
                AllowNegative = false,
                AllowZero = false,
                LowerLimit = 1,
                UpperLimit = entries.Count,
                AllowNone = false
            };

            PromptIntegerResult pir = ed.GetInteger(pio);
            if (pir.Status != PromptStatus.OK)
                return null;

            return entries[pir.Value - 1];
        }

        private static List<ObjectId> FindEntitiesByImportId(Database db, string importId)
        {
            var result = new List<ObjectId>();
            using var tr = db.TransactionManager.StartTransaction();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var model = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId id in model)
            {
                if (id.IsErased || !id.IsValid)
                    continue;

                if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity ent)
                    continue;

                var metadata = ReadCacheMetadata(ent);
                if (metadata.TryGetValue("IMPORT_ID", out string? value) && string.Equals(value, importId, StringComparison.OrdinalIgnoreCase))
                    result.Add(id);
            }

            return result;
        }

        private static Dictionary<string, string> ReadCacheMetadata(Entity entity)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ResultBuffer? rb = entity.XData;
            if (rb == null)
                return result;

            bool inOurApp = false;
            foreach (TypedValue tv in rb)
            {
                if (tv.TypeCode == 1001)
                {
                    inOurApp = string.Equals(tv.Value as string, CacheTrackingRegAppName, StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inOurApp || tv.TypeCode != 1000 || tv.Value is not string text)
                    continue;

                int idx = text.IndexOf('=');
                if (idx <= 0 || idx >= text.Length - 1)
                    continue;

                string key = text[..idx].Trim();
                string value = text[(idx + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(key))
                    result[key] = value;
            }

            return result;
        }

        private static int EraseEntities(Database db, IReadOnlyCollection<ObjectId> ids)
        {
            int erased = 0;
            using var tr = db.TransactionManager.StartTransaction();
            foreach (ObjectId id in ids)
            {
                if (id.IsErased || !id.IsValid)
                    continue;

                if (tr.GetObject(id, OpenMode.ForWrite, false) is not Entity ent)
                    continue;

                ent.Erase();
                erased++;
            }

            tr.Commit();
            return erased;
        }

        private static void MarkManifestEntryRemoved(CacheManifest manifest, string importId)
        {
            CacheManifestEntry? entry = manifest.Entries
                .FirstOrDefault(x => x.ImportId.Equals(importId, StringComparison.OrdinalIgnoreCase));

            if (entry == null)
                return;

            entry.Status = "Removed";
        }

        private static void SaveManifest(string jsonPath, CacheManifest manifest)
        {
            manifest.ManifestVersion = Math.Max(manifest.ManifestVersion, 1);
            manifest.Entries = manifest.Entries
                .OrderByDescending(x => x.TimestampUtc)
                .ToList();

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(jsonPath, JsonSerializer.Serialize(manifest, options));
        }


        private static void RunCacheLogAction(bool openAfterGenerate)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            var ed = doc.Editor;
            try
            {
                string drawingCs = DetectDrawingCoordinateSystem();
                string? cacheFileName = PromptForCacheLogTarget(ed);
                if (string.IsNullOrWhiteSpace(cacheFileName))
                    return;

                string cachePath = Path.Combine(CacheRootFolder, drawingCs, cacheFileName);
                if (!File.Exists(cachePath))
                {
                    ed.WriteMessage($"\nCache log action cancelled. Cache file was not found: {cachePath}");
                    return;
                }

                string reportPath = GenerateHtmlReport(cachePath);
                ed.WriteMessage($"\nCache log report ready: {reportPath}");
                if (openAfterGenerate)
                    OpenFileInShell(reportPath, ed);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCache log action failed: {ex.Message}");
            }
        }

        private static string? PromptForCacheLogTarget(Editor ed)
        {
            var pko = new PromptKeywordOptions("\nSelect cache report target [SS_Pipes/SS_Structures/SD_Pipes/SD_Structures/Cancel]", "SS_Pipes SS_Structures SD_Pipes SD_Structures Cancel")
            {
                AllowNone = false
            };

            PromptResult pkr = ed.GetKeywords(pko);
            if (pkr.Status != PromptStatus.OK || pkr.StringResult.Equals("Cancel", StringComparison.OrdinalIgnoreCase))
                return null;

            return pkr.StringResult switch
            {
                "SS_Pipes" => "Survey_Sewer_Pipes.dwg",
                "SS_Structures" => "Survey_Sewer_Structures.dwg",
                "SD_Pipes" => "Survey_Storm_Pipes.dwg",
                "SD_Structures" => "Survey_Storm_Structures.dwg",
                _ => null
            };
        }

        private static void RunCombinedSurveyReportAction(bool openAfterGenerate)
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            var ed = doc.Editor;
            try
            {
                string drawingCs = DetectDrawingCoordinateSystem();
                string reportPath = GenerateCombinedSurveyHtmlReport(drawingCs);
                ed.WriteMessage($"\nCombined survey GIS HTML report ready: {reportPath}");
                if (openAfterGenerate)
                    OpenFileInShell(reportPath, ed);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCombined survey GIS HTML report failed: {ex.Message}");
            }
        }

        private static string GenerateCombinedSurveyHtmlReport(string drawingCs)
        {
            var reports = new List<CombinedSurveyReportSection>
            {
                BuildCombinedSurveySection(drawingCs, "SS_Pipes", "SEWER PIPES", "Survey_Sewer_Pipes.dwg"),
                BuildCombinedSurveySection(drawingCs, "SS_Structures", "SEWER STRUCTURES", "Survey_Sewer_Structures.dwg"),
                BuildCombinedSurveySection(drawingCs, "SD_Pipes", "STORM PIPES", "Survey_Storm_Pipes.dwg"),
                BuildCombinedSurveySection(drawingCs, "SD_Structures", "STORM STRUCTURES", "Survey_Storm_Structures.dwg")
            };

            string reportPath = Path.Combine(CacheRootFolder, drawingCs, "CLV_Survey_GIS_Report.html");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath) ?? CacheRootFolder);

            int totalImports = reports.Sum(x => x.Manifest.Entries.Count);
            int totalAdded = reports.Sum(x => x.Manifest.Entries.Sum(e => e.ObjectCountAdded));
            int totalSkipped = reports.Sum(x => x.Manifest.Entries.Sum(e => e.ObjectCountSkipped));

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang='en'><head><meta charset='utf-8'><title>CLV Survey GIS Report</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body{font-family:Arial,Helvetica,sans-serif;margin:24px;color:#1f2937;background:#f8fafc;}"
                + ".wrap{max-width:1500px;margin:0 auto;}"
                + "h1{margin:0 0 6px;}"
                + ".sub{margin:0 0 18px;color:#4b5563;line-height:1.5;}"
                + ".summary{display:flex;flex-wrap:wrap;gap:14px;margin:18px 0 22px;}"
                + ".card{background:#ffffff;border:1px solid #d1d5db;border-radius:14px;padding:14px 16px;min-width:180px;box-shadow:0 3px 10px rgba(15,23,42,.06);}"
                + ".card .label{font-size:12px;letter-spacing:.04em;color:#6b7280;text-transform:uppercase;margin-bottom:6px;}"
                + ".card .value{font-size:28px;font-weight:700;}"
                + ".jump{background:#ffffff;border:1px solid #d1d5db;border-radius:14px;padding:14px 16px;margin-bottom:20px;}"
                + ".jump a{margin-right:14px;color:#2563eb;text-decoration:none;font-weight:600;}"
                + ".section{background:#ffffff;border:1px solid #d1d5db;border-radius:16px;padding:18px 18px 14px;margin:0 0 18px;box-shadow:0 3px 10px rgba(15,23,42,.06);}"
                + ".section h2{margin:0 0 6px;font-size:22px;}"
                + ".meta{color:#4b5563;margin:0 0 14px;line-height:1.5;}"
                + "table{border-collapse:collapse;width:100%;margin-top:10px;}"
                + "th,td{border:1px solid #d1d5db;padding:8px;vertical-align:top;text-align:left;font-size:14px;}"
                + "th{background:#f3f4f6;}"
                + "tr:nth-child(even){background:#fafafa;}"
                + ".empty{padding:14px;border:1px dashed #cbd5e1;border-radius:12px;background:#f8fafc;color:#475569;}"
                + "</style></head><body><div class='wrap'>");
            sb.AppendLine("<h1>CLV Survey GIS Report</h1>");
            sb.AppendLine("<p class='sub'>Coordinate System: " + WebUtility.HtmlEncode(drawingCs) + "<br>Generated (UTC): " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + "</p>");
            sb.AppendLine("<div class='summary'>");
            sb.AppendLine(BuildSummaryCardHtml("Sections", reports.Count.ToString()));
            sb.AppendLine(BuildSummaryCardHtml("Total Imports", totalImports.ToString()));
            sb.AppendLine(BuildSummaryCardHtml("Objects Added", totalAdded.ToString()));
            sb.AppendLine(BuildSummaryCardHtml("Exact Duplicates Skipped", totalSkipped.ToString()));
            sb.AppendLine("</div>");
            sb.AppendLine("<div class='jump'><strong>Jump to:</strong> " + string.Join(string.Empty, reports.Select(x => "<a href='#" + WebUtility.HtmlEncode(x.AnchorId) + "'>" + WebUtility.HtmlEncode(x.DisplayTitle) + "</a>")) + "</div>");

            foreach (CombinedSurveyReportSection report in reports)
            {
                int sectionAdded = report.Manifest.Entries.Sum(e => e.ObjectCountAdded);
                int sectionSkipped = report.Manifest.Entries.Sum(e => e.ObjectCountSkipped);
                sb.AppendLine("<section class='section' id='" + WebUtility.HtmlEncode(report.AnchorId) + "'>");
                sb.AppendLine("<h2>" + WebUtility.HtmlEncode(report.DisplayTitle) + "</h2>");
                sb.AppendLine("<p class='meta'>Cache: " + WebUtility.HtmlEncode(report.CacheName)
                    + "<br>Source DWG: " + WebUtility.HtmlEncode(report.CachePath)
                    + "<br>Individual HTML: " + WebUtility.HtmlEncode(report.IndividualHtmlPath)
                    + "<br>Total Imports: " + report.Manifest.Entries.Count
                    + " | Objects Added: " + sectionAdded
                    + " | Exact Duplicates Skipped: " + sectionSkipped + "</p>");

                if (report.Manifest.Entries.Count == 0)
                {
                    sb.AppendLine("<div class='empty'>No cache-import manifest entries were found for this dataset yet.</div>");
                }
                else
                {
                    sb.AppendLine("<table><thead><tr><th>Import ID</th><th>Timestamp (UTC)</th><th>Project Number</th><th>Dataset Type</th><th>Source DWG</th><th>Object Count Added</th><th>Exact Duplicates Skipped</th><th>User</th><th>Status</th></tr></thead><tbody>");
                    foreach (CacheManifestEntry entry in report.Manifest.Entries.OrderByDescending(x => x.TimestampUtc))
                    {
                        sb.AppendLine("<tr>"
                            + "<td>" + WebUtility.HtmlEncode(entry.ImportId) + "</td>"
                            + "<td>" + WebUtility.HtmlEncode(entry.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss")) + "</td>"
                            + "<td>" + WebUtility.HtmlEncode(entry.ProjectNumber) + "</td>"
                            + "<td>" + WebUtility.HtmlEncode(entry.DataSetType) + "</td>"
                            + "<td>" + WebUtility.HtmlEncode(entry.SourceDwg) + "</td>"
                            + "<td>" + entry.ObjectCountAdded + "</td>"
                            + "<td>" + entry.ObjectCountSkipped + "</td>"
                            + "<td>" + WebUtility.HtmlEncode(entry.UserName) + "</td>"
                            + "<td>" + WebUtility.HtmlEncode(entry.Status) + "</td>"
                            + "</tr>");
                    }
                    sb.AppendLine("</tbody></table>");
                }

                sb.AppendLine("</section>");
            }

            sb.AppendLine("</div></body></html>");
            File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
            return reportPath;
        }

        private static CombinedSurveyReportSection BuildCombinedSurveySection(string drawingCs, string cacheName, string displayTitle, string cacheFileName)
        {
            string cachePath = Path.Combine(CacheRootFolder, drawingCs, cacheFileName);
            CacheManifest manifest = File.Exists(cachePath)
                ? ReadManifest(GetJsonManifestPath(cachePath))
                : new CacheManifest { CacheName = cacheName };

            if (string.IsNullOrWhiteSpace(manifest.CacheName))
                manifest.CacheName = cacheName;

            return new CombinedSurveyReportSection(
                cacheName,
                displayTitle,
                displayTitle.Replace(" ", string.Empty, StringComparison.Ordinal),
                cachePath,
                GetHtmlReportPath(cachePath),
                manifest);
        }

        private static string BuildSummaryCardHtml(string label, string value)
        {
            return "<div class='card'><div class='label'>" + WebUtility.HtmlEncode(label) + "</div><div class='value'>" + WebUtility.HtmlEncode(value) + "</div></div>";
        }

        private static string PromptForProjectNumber(Editor ed)
        {
            var pso = new PromptStringOptions("\nEnter Project Number")
            {
                AllowSpaces = true
            };

            PromptResult pr = ed.GetString(pso);
            return pr.Status == PromptStatus.OK ? pr.StringResult.Trim() : string.Empty;
        }

        private static string GetCurrentUserName()
        {
            string userName = Environment.UserName?.Trim() ?? string.Empty;
            return string.IsNullOrWhiteSpace(userName) ? "unknown" : userName;
        }

        private static string GetSourceDrawingPath(Document doc, Database db)
        {
            string path = db.Filename;
            if (string.IsNullOrWhiteSpace(path))
                path = doc.Name;

            return string.IsNullOrWhiteSpace(path) ? "(unsaved drawing)" : path;
        }

        private static string BuildImportId(string cacheName, string projectNumber)
        {
            string cleanProject = SanitizeToken(projectNumber);
            string cleanCache = SanitizeToken(cacheName);
            return $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{cleanCache}_{cleanProject}";
        }

        private static string SanitizeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "NA";

            var chars = value.Trim().Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray();
            string sanitized = new string(chars);
            while (sanitized.Contains("__", StringComparison.Ordinal))
                sanitized = sanitized.Replace("__", "_", StringComparison.Ordinal);

            return sanitized.Trim('_');
        }

        private static string GetCacheNameForConfig(FinalizeConfig config, FinalizeCategory category)
        {
            return (config.UtilityKind, category) switch
            {
                (UtilityKind.Sewer, FinalizeCategory.Pipes) => "SS_Pipes",
                (UtilityKind.Sewer, FinalizeCategory.Structures) => "SS_Structures",
                (UtilityKind.Storm, FinalizeCategory.Pipes) => "SD_Pipes",
                _ => "SD_Structures"
            };
        }

        private static string GetDataSetTypeForConfig(FinalizeConfig config, FinalizeCategory category)
        {
            return (config.UtilityKind, category) switch
            {
                (UtilityKind.Sewer, FinalizeCategory.Pipes) => "Sewer Pipes",
                (UtilityKind.Sewer, FinalizeCategory.Structures) => "Sewer Structures",
                (UtilityKind.Storm, FinalizeCategory.Pipes) => "Storm Pipes",
                _ => "Storm Structures"
            };
        }

        private static void EnsureRegAppRecord(Database db, Transaction tr, string appName)
        {
            var regTable = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (regTable.Has(appName))
                return;

            regTable.UpgradeOpen();
            var regApp = new RegAppTableRecord { Name = appName };
            regTable.Add(regApp);
            tr.AddNewlyCreatedDBObject(regApp, true);
        }

        private static void StampEntityWithCacheMetadata(
            Entity entity,
            string importId,
            string projectNumber,
            string dataSetType,
            string sourceDwg,
            string userName,
            string cacheName,
            DateTime importTimestampUtc)
        {
            DateTime importDateLocal = importTimestampUtc.ToLocalTime();
            entity.XData = new ResultBuffer(
                new TypedValue(1001, CacheTrackingRegAppName),
                new TypedValue(1000, "IMPORT_ID=" + importId),
                new TypedValue(1000, "PROJECT_NUMBER=" + projectNumber),
                new TypedValue(1000, "DATASET_TYPE=" + dataSetType),
                new TypedValue(1000, "SOURCE_DWG=" + sourceDwg),
                new TypedValue(1000, "USER_NAME=" + userName),
                new TypedValue(1000, "CACHE_NAME=" + cacheName),
                new TypedValue(1000, "IMPORT_DATE=" + importDateLocal.ToString("yyyy-MM-dd HH:mm:ss")),
                new TypedValue(1000, "IMPORT_DATE_UTC=" + importTimestampUtc.ToString("yyyy-MM-ddTHH:mm:ssZ")));
        }

        private static void AppendManifestEntry(string targetPath, string cacheName, CacheManifestEntry entry)
        {
            string jsonPath = GetJsonManifestPath(targetPath);
            CacheManifest manifest = ReadManifest(jsonPath);
            manifest.ManifestVersion = Math.Max(manifest.ManifestVersion, 1);
            manifest.CacheName = string.IsNullOrWhiteSpace(cacheName) ? GetCacheNameFromTargetPath(targetPath) : cacheName;
            manifest.Entries.Add(entry);
            SaveManifest(jsonPath, manifest);
            GenerateHtmlReport(targetPath, manifest);
        }

        private static CacheManifest ReadManifest(string jsonPath)
        {
            if (!File.Exists(jsonPath))
                return new CacheManifest();

            try
            {
                string json = File.ReadAllText(jsonPath);
                return JsonSerializer.Deserialize<CacheManifest>(json) ?? new CacheManifest();
            }
            catch
            {
                return new CacheManifest();
            }
        }

        private static string GenerateHtmlReport(string targetPath)
        {
            CacheManifest manifest = ReadManifest(GetJsonManifestPath(targetPath));
            return GenerateHtmlReport(targetPath, manifest);
        }

        private static string GenerateHtmlReport(string targetPath, CacheManifest manifest)
        {
            string htmlPath = GetHtmlReportPath(targetPath);
            string displayCacheName = GetCacheDisplayName(targetPath, manifest);
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang='en'><head><meta charset='utf-8'><title>" + WebUtility.HtmlEncode(CacheTrackingHtmlTitle) + "</title>");
            sb.AppendLine("<style>body{font-family:Arial,Helvetica,sans-serif;margin:20px;color:#1f2937;}h1{margin-bottom:4px;}p{margin-top:0;color:#4b5563;}table{border-collapse:collapse;width:100%;}th,td{border:1px solid #d1d5db;padding:8px;vertical-align:top;text-align:left;}th{background:#f3f4f6;}tr:nth-child(even){background:#fafafa;}</style></head><body>");
            sb.AppendLine("<h1>" + WebUtility.HtmlEncode(CacheTrackingHtmlTitle) + "</h1>");
            sb.AppendLine("<p>Cache: " + WebUtility.HtmlEncode(displayCacheName) + "<br>Generated (UTC): " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + "<br>Total Imports: " + manifest.Entries.Count + "</p>");
            sb.AppendLine("<table><thead><tr><th>Import ID</th><th>Timestamp (UTC)</th><th>Project Number</th><th>Dataset Type</th><th>Source DWG</th><th>Object Count Added</th><th>Exact Duplicates Skipped</th><th>User</th><th>Status</th></tr></thead><tbody>");

            foreach (CacheManifestEntry entry in manifest.Entries.OrderByDescending(x => x.TimestampUtc))
            {
                sb.AppendLine("<tr>"
                    + "<td>" + WebUtility.HtmlEncode(entry.ImportId) + "</td>"
                    + "<td>" + WebUtility.HtmlEncode(entry.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss")) + "</td>"
                    + "<td>" + WebUtility.HtmlEncode(entry.ProjectNumber) + "</td>"
                    + "<td>" + WebUtility.HtmlEncode(entry.DataSetType) + "</td>"
                    + "<td>" + WebUtility.HtmlEncode(entry.SourceDwg) + "</td>"
                    + "<td>" + entry.ObjectCountAdded + "</td>"
                    + "<td>" + entry.ObjectCountSkipped + "</td>"
                    + "<td>" + WebUtility.HtmlEncode(entry.UserName) + "</td>"
                    + "<td>" + WebUtility.HtmlEncode(entry.Status) + "</td>"
                    + "</tr>");
            }

            sb.AppendLine("</tbody></table></body></html>");
            File.WriteAllText(htmlPath, sb.ToString(), Encoding.UTF8);
            return htmlPath;
        }

        private static string GetCacheDisplayName(string targetPath, CacheManifest manifest)
        {
            return !string.IsNullOrWhiteSpace(manifest.CacheName)
                ? manifest.CacheName
                : GetCacheNameFromTargetPath(targetPath);
        }

        private static string GetCacheNameFromTargetPath(string targetPath)
        {
            string fileName = Path.GetFileName(targetPath);
            return fileName switch
            {
                "Survey_Sewer_Pipes.dwg" => "SS_Pipes",
                "Survey_Sewer_Structures.dwg" => "SS_Structures",
                "Survey_Storm_Pipes.dwg" => "SD_Pipes",
                "Survey_Storm_Structures.dwg" => "SD_Structures",
                _ => Path.GetFileNameWithoutExtension(targetPath)
            };
        }

        private static string GetJsonManifestPath(string targetPath)
        {
            string? directory = Path.GetDirectoryName(targetPath);
            string fileName = Path.GetFileNameWithoutExtension(targetPath);
            return Path.Combine(directory ?? string.Empty, fileName + ".importlog.json");
        }

        private static string GetHtmlReportPath(string targetPath)
        {
            string? directory = Path.GetDirectoryName(targetPath);
            string fileName = Path.GetFileNameWithoutExtension(targetPath);
            return Path.Combine(directory ?? string.Empty, fileName + ".importlog.html");
        }

        private static void OpenFileInShell(string filePath, Editor ed)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                };

                Process.Start(psi);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUnable to open file: {ex.Message}");
            }
        }

        private readonly record struct FinalizeConfig(
            UtilityKind UtilityKind,
            FinalizeCategory Category,
            string[] SourceLayers,
            string StructuresCacheFileName,
            string PipesCacheFileName);

        private readonly record struct CompareConfig(
            UtilityKind UtilityKind,
            string[] SourceLayers,
            string StructuresCacheFileName,
            string PipesCacheFileName);

        private readonly record struct EntitySignature(
            ObjectId EntityId,
            string Layer,
            Point3d Center,
            Extents3d Extents,
            bool IsLinear,
            Point3d StartPoint,
            Point3d EndPoint,
            double Length);

        private readonly record struct CompareSummary(
            List<EntitySignature> ExactMatches,
            List<EntitySignature> NearMatches);

        private readonly record struct CombinedSurveyReportSection(
            string CacheName,
            string DisplayTitle,
            string AnchorId,
            string CachePath,
            string IndividualHtmlPath,
            CacheManifest Manifest);

        private sealed class CacheManifest
        {
            public int ManifestVersion { get; set; } = 1;
            public string CacheName { get; set; } = string.Empty;
            public List<CacheManifestEntry> Entries { get; set; } = new();
        }

        private sealed class CacheManifestEntry
        {
            public string ImportId { get; set; } = string.Empty;
            public DateTime TimestampUtc { get; set; }
            public string UserName { get; set; } = string.Empty;
            public string ProjectNumber { get; set; } = string.Empty;
            public string DataSetType { get; set; } = string.Empty;
            public string SourceDwg { get; set; } = string.Empty;
            public string CacheTarget { get; set; } = string.Empty;
            public int ObjectCountAdded { get; set; }
            public int ObjectCountSkipped { get; set; }
            public string Status { get; set; } = string.Empty;
        }

        private enum FinalizeCategory
        {
            Structures,
            Pipes
        }

        private enum UtilityKind
        {
            Sewer,
            Storm
        }
    }
}
