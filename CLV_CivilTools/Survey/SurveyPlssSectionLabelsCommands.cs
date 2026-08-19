using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcException = Autodesk.AutoCAD.Runtime.Exception;
using AcPolyline = Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace CLV_CivilTools.Survey
{
    public static class SurveyPlssSectionLabelsCommands
    {
        private const string MarkerBlockName = "GIS_SECTION_MARKER";
        private const string CacheRootFolder = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_GRID_City\000-City\GIS\_CLV_CACHE";
        private const string CacheFileName = "GIS_Sections.dwg";
        private const string PlssBlockFolder = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\Blocks\Survey\PLSS Sections";
        private const double DuplicateTolerance = 0.05;

        private static readonly string[] Quadrants = { "NW", "NE", "SW", "SE" };

        private static readonly string[] PlssBlockNames =
        {
            "CLV_SECT_CORN",
            "CLV_SECT_NS_W16",
            "CLV_SECT_NS4",
            "CLV_SECT_NS_E16",
            "CLV_SECT_EW_N16",
            "CLV_SECT_INT_NW",
            "CLV_SECT_CC_N16",
            "CLV_SECT_INT_NE",
            "CLV_SECT_EW4",
            "CLV_SECT_CC_W16",
            "CLV_SECT_C4",
            "CLV_SECT_CC_E16",
            "CLV_SECT_EW_S16",
            "CLV_SECT_INT_SW",
            "CLV_SECT_CC_S16",
            "CLV_SECT_INT_SE"
        };

        [CommandMethod("SURVEY-PLSS-IMPORT-LABELS", CommandFlags.Modal)]
        [CommandMethod("PLSSIMPORTLABELS", CommandFlags.Modal)]
        public static void ImportPlssSectionLabels()
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                if (!TryGetBoundary(ed, db, out BoundaryArea? boundary) || boundary == null)
                    return;

                string drawingCs = InferDrawingCoordinateSystem();
                string cachePath = Path.Combine(CacheRootFolder, drawingCs, CacheFileName);
                if (!File.Exists(cachePath))
                {
                    ed.WriteMessage($"\nPLSS section cache not found for {drawingCs}.");
                    ed.WriteMessage($"\nExpected: {cachePath}");
                    return;
                }

                List<MarkerData> markers = ReadMarkerCache(cachePath);
                if (markers.Count == 0)
                {
                    ed.WriteMessage($"\nNo {MarkerBlockName} blocks were found in {cachePath}.");
                    return;
                }

                Dictionary<string, SectionData> sections = BuildSections(markers);
                List<SectionData> selectedSections = sections.Values
                    .Where(s => s.HasFourCorners && SectionIntersectsBoundary(s, boundary))
                    .OrderBy(s => s.SectionKey, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (selectedSections.Count == 0)
                {
                    ed.WriteMessage("\nNo complete PLSS sections were found inside/intersecting the selected area.");
                    ed.WriteMessage("\nConfirm the area includes complete GIS_SECTION_MARKER corner coverage.");
                    return;
                }

                ed.WriteMessage($"\nPLSS sections found: {selectedSections.Count}");
                ed.WriteMessage($"\nCache: {cachePath}");

                PromptKeywordOptions confirmOptions = new PromptKeywordOptions("\nImport PLSS section labels for these sections? [Yes/No] <Yes>: ")
                {
                    AllowNone = true
                };
                confirmOptions.Keywords.Add("Yes");
                confirmOptions.Keywords.Add("No");
                confirmOptions.Keywords.Default = "Yes";

                PromptResult confirm = ed.GetKeywords(confirmOptions);
                if (confirm.Status == PromptStatus.Cancel || string.Equals(confirm.StringResult, "No", StringComparison.OrdinalIgnoreCase))
                {
                    ed.WriteMessage("\nPLSS section label import cancelled.");
                    return;
                }

                using DocumentLock docLock = doc.LockDocument();
                EnsurePlssBlockDefinitions(db);

                double insertScale = GetCurrentInsertionScale();
                int insertedCount;
                int skippedDuplicateCount;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Dictionary<string, ObjectId> blockIds = GetBlockDefinitionIds(db, tr);
                    List<PlssInsertRequest> requests = BuildInsertRequests(selectedSections);
                    InsertRequestSet deduped = DeduplicateRequests(requests);

                    insertedCount = 0;
                    skippedDuplicateCount = deduped.SkippedDuplicateCount;

                    foreach (PlssInsertRequest request in deduped.Requests)
                    {
                        if (!blockIds.TryGetValue(request.BlockName, out ObjectId blockId) || blockId.IsNull)
                            continue;

                        InsertBlockReference(db, tr, blockId, request, insertScale);
                        insertedCount++;
                    }

                    tr.Commit();
                }

                ed.WriteMessage($"\nPLSS section labels imported: {insertedCount} block(s).");
                ed.WriteMessage($"\nDuplicate label locations skipped: {skippedDuplicateCount}.");
                ed.WriteMessage($"\nInsertion scale used: {insertScale.ToString("0.###", CultureInfo.InvariantCulture)}.");
            }
            catch (System.Exception ex) when (ex is not AcException)
            {
                ed.WriteMessage($"\nSURVEY-PLSS-IMPORT-LABELS error: {ex.Message}");
            }
            catch (AcException ex)
            {
                ed.WriteMessage($"\nSURVEY-PLSS-IMPORT-LABELS AutoCAD error: {ex.Message}");
            }
        }

        private static bool TryGetBoundary(Editor ed, Database db, out BoundaryArea? boundary)
        {
            boundary = null;

            PromptKeywordOptions modeOptions = new PromptKeywordOptions("\nPLSS label area by [Window/Object] <Window>: ")
            {
                AllowNone = true
            };
            modeOptions.Keywords.Add("Window");
            modeOptions.Keywords.Add("Object");
            modeOptions.Keywords.Default = "Window";

            PromptResult mode = ed.GetKeywords(modeOptions);
            if (mode.Status == PromptStatus.Cancel)
                return false;

            string selectedMode = string.IsNullOrWhiteSpace(mode.StringResult) ? "Window" : mode.StringResult;
            if (string.Equals(selectedMode, "Object", StringComparison.OrdinalIgnoreCase))
                return TryGetBoundaryFromPolyline(ed, db, out boundary);

            return TryGetBoundaryFromWindow(ed, out boundary);
        }

        private static bool TryGetBoundaryFromWindow(Editor ed, out BoundaryArea? boundary)
        {
            boundary = null;

            PromptPointResult first = ed.GetPoint("\nPick first corner of PLSS label area: ");
            if (first.Status != PromptStatus.OK)
                return false;

            PromptCornerOptions cornerOptions = new PromptCornerOptions("\nPick opposite corner of PLSS label area: ", first.Value);
            PromptPointResult second = ed.GetCorner(cornerOptions);
            if (second.Status != PromptStatus.OK)
                return false;

            double minX = Math.Min(first.Value.X, second.Value.X);
            double maxX = Math.Max(first.Value.X, second.Value.X);
            double minY = Math.Min(first.Value.Y, second.Value.Y);
            double maxY = Math.Max(first.Value.Y, second.Value.Y);

            List<Point2d> points = new List<Point2d>
            {
                new Point2d(minX, minY),
                new Point2d(maxX, minY),
                new Point2d(maxX, maxY),
                new Point2d(minX, maxY)
            };

            boundary = new BoundaryArea(points);
            return true;
        }

        private static bool TryGetBoundaryFromPolyline(Editor ed, Database db, out BoundaryArea? boundary)
        {
            boundary = null;

            PromptEntityOptions peo = new PromptEntityOptions("\nSelect closed polyline PLSS label area: ");
            peo.SetRejectMessage("\nSelect a closed polyline.");
            peo.AddAllowedClass(typeof(AcPolyline), exactMatch: false);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
                return false;

            using Transaction tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(per.ObjectId, OpenMode.ForRead, false) is not AcPolyline polyline || !polyline.Closed)
            {
                ed.WriteMessage("\nSelected object is not a closed polyline.");
                return false;
            }

            List<Point2d> points = new List<Point2d>();
            for (int i = 0; i < polyline.NumberOfVertices; i++)
                points.Add(polyline.GetPoint2dAt(i));

            tr.Commit();

            if (points.Count < 3)
            {
                ed.WriteMessage("\nSelected polyline does not have enough vertices.");
                return false;
            }

            boundary = new BoundaryArea(points);
            return true;
        }

        private static List<MarkerData> ReadMarkerCache(string cachePath)
        {
            List<MarkerData> markers = new List<MarkerData>();

            using Database sourceDb = new Database(false, true);
            sourceDb.ReadDwgFile(cachePath, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
            sourceDb.CloseInput(true);

            using Transaction tr = sourceDb.TransactionManager.StartTransaction();
            BlockTable bt = (BlockTable)tr.GetObject(sourceDb.BlockTableId, OpenMode.ForRead);
            BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in modelSpace)
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is not BlockReference br)
                    continue;

                string blockName = GetBlockName(br, tr);
                if (!string.Equals(blockName, MarkerBlockName, StringComparison.OrdinalIgnoreCase))
                    continue;

                Dictionary<string, string> attrs = ReadAttributes(br, tr);
                MarkerData marker = new MarkerData(br.Position, attrs);
                if (marker.HasAnySection)
                    markers.Add(marker);
            }

            tr.Commit();
            return markers;
        }

        private static string GetBlockName(BlockReference br, Transaction tr)
        {
            try
            {
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord.IsNull ? br.BlockTableRecord : br.DynamicBlockTableRecord, OpenMode.ForRead);
                return btr.Name;
            }
            catch
            {
                try
                {
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                    return btr.Name;
                }
                catch
                {
                    return string.Empty;
                }
            }
        }

        private static Dictionary<string, string> ReadAttributes(BlockReference br, Transaction tr)
        {
            Dictionary<string, string> attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId attId in br.AttributeCollection)
            {
                if (tr.GetObject(attId, OpenMode.ForRead, false) is AttributeReference attRef)
                    attrs[attRef.Tag] = attRef.TextString ?? string.Empty;
            }

            return attrs;
        }

        private static Dictionary<string, SectionData> BuildSections(IEnumerable<MarkerData> markers)
        {
            Dictionary<string, SectionData> sections = new Dictionary<string, SectionData>(StringComparer.OrdinalIgnoreCase);

            foreach (MarkerData marker in markers)
            {
                AddMarkerToSection(sections, marker.NW.SectionKey, "SE", marker);
                AddMarkerToSection(sections, marker.NE.SectionKey, "SW", marker);
                AddMarkerToSection(sections, marker.SW.SectionKey, "NE", marker);
                AddMarkerToSection(sections, marker.SE.SectionKey, "NW", marker);
            }

            return sections;
        }

        private static void AddMarkerToSection(Dictionary<string, SectionData> sections, string sectionKey, string corner, MarkerData marker)
        {
            if (string.IsNullOrWhiteSpace(sectionKey))
                return;

            if (!sections.TryGetValue(sectionKey, out SectionData? section))
            {
                section = new SectionData(sectionKey);
                sections[sectionKey] = section;
            }

            section.SetCorner(corner, marker);
        }

        private static bool SectionIntersectsBoundary(SectionData section, BoundaryArea boundary)
        {
            List<Point3d> corners = new List<Point3d> { section.NW!.Point, section.NE!.Point, section.SE!.Point, section.SW!.Point };
            Point3d center = section.GetPoint(0.5, 0.5);

            if (boundary.Contains(center) || corners.Any(boundary.Contains))
                return true;

            Extents2d sectionExtents = GetExtents(corners.Select(p => new Point2d(p.X, p.Y)));
            return boundary.ExtentsIntersect(sectionExtents);
        }

        private static List<PlssInsertRequest> BuildInsertRequests(IEnumerable<SectionData> sections)
        {
            List<PlssInsertRequest> requests = new List<PlssInsertRequest>();

            foreach (SectionData section in sections)
            {
                if (!section.HasFourCorners)
                    continue;

                string sec = FormatSectionLabelFromKey(section.SectionKey);

                string north = FormatSectionLabelFromKey(ChooseFirst(section.NW!.NE.SectionKey, section.NE!.NW.SectionKey));
                string south = FormatSectionLabelFromKey(ChooseFirst(section.SW!.SE.SectionKey, section.SE!.SW.SectionKey));
                string west = FormatSectionLabelFromKey(ChooseFirst(section.NW!.SW.SectionKey, section.SW!.NW.SectionKey));
                string east = FormatSectionLabelFromKey(ChooseFirst(section.NE!.SE.SectionKey, section.SE!.NE.SectionKey));

                AddCorner(requests, section.NW!);
                AddCorner(requests, section.NE!);
                AddCorner(requests, section.SW!);
                AddCorner(requests, section.SE!);

                AddNs(requests, section.GetPoint(0.25, 1.0), "CLV_SECT_NS_W16", north, sec);
                AddNs(requests, section.GetPoint(0.50, 1.0), "CLV_SECT_NS4", north, sec);
                AddNs(requests, section.GetPoint(0.75, 1.0), "CLV_SECT_NS_E16", north, sec);

                AddNs(requests, section.GetPoint(0.25, 0.0), "CLV_SECT_NS_W16", sec, south);
                AddNs(requests, section.GetPoint(0.50, 0.0), "CLV_SECT_NS4", sec, south);
                AddNs(requests, section.GetPoint(0.75, 0.0), "CLV_SECT_NS_E16", sec, south);

                AddEw(requests, section.GetPoint(0.0, 0.75), "CLV_SECT_EW_N16", west, sec);
                AddEw(requests, section.GetPoint(0.0, 0.50), "CLV_SECT_EW4", west, sec);
                AddEw(requests, section.GetPoint(0.0, 0.25), "CLV_SECT_EW_S16", west, sec);

                AddEw(requests, section.GetPoint(1.0, 0.75), "CLV_SECT_EW_N16", sec, east);
                AddEw(requests, section.GetPoint(1.0, 0.50), "CLV_SECT_EW4", sec, east);
                AddEw(requests, section.GetPoint(1.0, 0.25), "CLV_SECT_EW_S16", sec, east);

                AddSec(requests, section.GetPoint(0.25, 0.75), "CLV_SECT_INT_NW", sec);
                AddSec(requests, section.GetPoint(0.50, 0.75), "CLV_SECT_CC_N16", sec);
                AddSec(requests, section.GetPoint(0.75, 0.75), "CLV_SECT_INT_NE", sec);
                AddSec(requests, section.GetPoint(0.25, 0.50), "CLV_SECT_CC_W16", sec);
                AddSec(requests, section.GetPoint(0.50, 0.50), "CLV_SECT_C4", sec);
                AddSec(requests, section.GetPoint(0.75, 0.50), "CLV_SECT_CC_E16", sec);
                AddSec(requests, section.GetPoint(0.25, 0.25), "CLV_SECT_INT_SW", sec);
                AddSec(requests, section.GetPoint(0.50, 0.25), "CLV_SECT_CC_S16", sec);
                AddSec(requests, section.GetPoint(0.75, 0.25), "CLV_SECT_INT_SE", sec);
            }

            return requests;
        }

        private static void AddCorner(List<PlssInsertRequest> requests, MarkerData marker)
        {
            Dictionary<string, string> attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["NW"] = FormatSectionLabel(marker.NW.Section),
                ["NE"] = FormatSectionLabel(marker.NE.Section),
                ["SW"] = FormatSectionLabel(marker.SW.Section),
                ["SE"] = FormatSectionLabel(marker.SE.Section)
            };

            requests.Add(new PlssInsertRequest("CLV_SECT_CORN", marker.Point, attrs));
        }

        private static void AddNs(List<PlssInsertRequest> requests, Point3d point, string blockName, string north, string south)
        {
            requests.Add(new PlssInsertRequest(blockName, point, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["N"] = north,
                ["S"] = south
            }));
        }

        private static void AddEw(List<PlssInsertRequest> requests, Point3d point, string blockName, string west, string east)
        {
            requests.Add(new PlssInsertRequest(blockName, point, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["W"] = west,
                ["E"] = east
            }));
        }

        private static void AddSec(List<PlssInsertRequest> requests, Point3d point, string blockName, string section)
        {
            requests.Add(new PlssInsertRequest(blockName, point, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SEC"] = section,
                ["=SEC"] = section
            }));
        }

        private static InsertRequestSet DeduplicateRequests(IEnumerable<PlssInsertRequest> requests)
        {
            Dictionary<string, PlssInsertRequest> unique = new Dictionary<string, PlssInsertRequest>(StringComparer.OrdinalIgnoreCase);
            int skipped = 0;

            foreach (PlssInsertRequest request in requests)
            {
                string key = BuildDuplicateKey(request.BlockName, request.Position);
                if (!unique.TryGetValue(key, out PlssInsertRequest? existing))
                {
                    unique[key] = request;
                    continue;
                }

                skipped++;
                foreach (KeyValuePair<string, string> pair in request.Attributes)
                {
                    if (!existing.Attributes.TryGetValue(pair.Key, out string? current) || string.IsNullOrWhiteSpace(current))
                        existing.Attributes[pair.Key] = pair.Value;
                }
            }

            return new InsertRequestSet(unique.Values.ToList(), skipped);
        }

        private static string BuildDuplicateKey(string blockName, Point3d point)
        {
            double x = Math.Round(point.X / DuplicateTolerance) * DuplicateTolerance;
            double y = Math.Round(point.Y / DuplicateTolerance) * DuplicateTolerance;
            return string.Format(CultureInfo.InvariantCulture, "{0}|{1:0.###}|{2:0.###}", blockName, x, y);
        }

        private static void EnsurePlssBlockDefinitions(Database db)
        {
            foreach (string blockName in PlssBlockNames)
                EnsureBlockDefinition(db, blockName);
        }

        private static void EnsureBlockDefinition(Database db, string blockName)
        {
            bool exists;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                exists = bt.Has(blockName);
                tr.Commit();
            }

            if (exists)
                return;

            string blockPath = Path.Combine(PlssBlockFolder, blockName + ".dwg");
            if (!File.Exists(blockPath))
                throw new FileNotFoundException($"PLSS block not found: {blockPath}");

            using Database sourceDb = new Database(false, true);
            sourceDb.ReadDwgFile(blockPath, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
            sourceDb.CloseInput(true);
            db.Insert(blockName, sourceDb, true);
        }

        private static Dictionary<string, ObjectId> GetBlockDefinitionIds(Database db, Transaction tr)
        {
            Dictionary<string, ObjectId> ids = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (string blockName in PlssBlockNames)
            {
                if (bt.Has(blockName))
                    ids[blockName] = bt[blockName];
            }

            return ids;
        }

        private static ObjectId InsertBlockReference(Database db, Transaction tr, ObjectId blockId, PlssInsertRequest request, double scale)
        {
            BlockTableRecord currentSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            BlockReference br = new BlockReference(request.Position, blockId)
            {
                LayerId = db.Clayer,
                Rotation = 0.0,
                ScaleFactors = new Scale3d(scale)
            };

            ObjectId id = currentSpace.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);

            AddAttributesFromDefinition(br, tr);
            ApplyAttributeValues(br, tr, request.Attributes);

            return id;
        }

        private static void AddAttributesFromDefinition(BlockReference br, Transaction tr)
        {
            BlockTableRecord blockDef = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
            foreach (ObjectId id in blockDef)
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is not AttributeDefinition attDef || attDef.Constant)
                    continue;

                AttributeReference attRef = new AttributeReference();
                attRef.SetAttributeFromBlock(attDef, br.BlockTransform);
                attRef.TextString = attDef.TextString;
                br.AttributeCollection.AppendAttribute(attRef);
                tr.AddNewlyCreatedDBObject(attRef, true);
            }
        }

        private static void ApplyAttributeValues(BlockReference br, Transaction tr, Dictionary<string, string> values)
        {
            foreach (ObjectId attId in br.AttributeCollection)
            {
                if (tr.GetObject(attId, OpenMode.ForWrite, false) is not AttributeReference attRef)
                    continue;

                if (values.TryGetValue(attRef.Tag, out string? value))
                    attRef.TextString = value ?? string.Empty;
            }
        }

        private static string ChooseFirst(params string[] values)
        {
            return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
        }

        private static string FormatSectionLabelFromKey(string sectionKey)
        {
            if (string.IsNullOrWhiteSpace(sectionKey))
                return string.Empty;

            int dash = sectionKey.LastIndexOf('-');
            string section = dash >= 0 && dash < sectionKey.Length - 1
                ? sectionKey[(dash + 1)..]
                : sectionKey;

            return FormatSectionLabel(section);
        }

        private static string FormatSectionLabel(string section)
        {
            if (string.IsNullOrWhiteSpace(section))
                return string.Empty;

            string trimmed = section.Trim();
            if (trimmed.StartsWith("S", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed[1..].Trim();

            if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sectionNumber))
                return "S" + sectionNumber.ToString(CultureInfo.InvariantCulture);

            return "S" + trimmed;
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
                Type? hostType = asm.GetType("Autodesk.Gis.Map.HostMapApplicationServices", throwOnError: false);
                object? mapApp = hostType?.GetProperty("Application", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                if (mapApp == null)
                    return null;

                string? appCs = TryExtractCoordinateSystemText(mapApp);
                if (!string.IsNullOrWhiteSpace(appCs))
                    return appCs;

                object? project = GetPropertyValue(mapApp, "ActiveProject");
                string? projectCs = TryExtractCoordinateSystemText(project);
                if (!string.IsNullOrWhiteSpace(projectCs))
                    return projectCs;
            }
            catch
            {
                // default below
            }

            return null;
        }

        private static string? TryGetCoordinateSystemFromGeoData()
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            Database? db = doc?.Database;
            if (db == null)
                return null;

            try
            {
                using Transaction tr = db.TransactionManager.StartTransaction();
                DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                if (!nod.Contains("ACAD_GEOGRAPHICDATA"))
                    return null;

                DBObject geoObject = tr.GetObject(nod.GetAt("ACAD_GEOGRAPHICDATA"), OpenMode.ForRead);
                string? coord = Convert.ToString(GetPropertyValue(geoObject, "CoordinateSystemId"), CultureInfo.InvariantCulture);
                tr.Commit();
                return string.IsNullOrWhiteSpace(coord) ? null : coord.Trim();
            }
            catch
            {
                return null;
            }
        }

        private static string? TryExtractCoordinateSystemText(object? source)
        {
            if (source == null)
                return null;

            foreach (string propertyName in new[] { "CoordinateSystem", "CoordSys", "MapCoordinateSystem", "CurrentCoordinateSystem", "CoordinateSystemCode", "Code" })
            {
                string? text = ConvertToCoordinateSystemText(GetPropertyValue(source, propertyName));
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            foreach (string methodName in new[] { "GetCoordinateSystem", "GetMapCoordinateSystem", "GetCurrentCoordinateSystem", "GetCoordinateSystemCode" })
            {
                string? text = ConvertToCoordinateSystemText(TryInvokeWithResult(source, methodName));
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
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
            if (raw.IndexOf("LVHEF", StringComparison.OrdinalIgnoreCase) >= 0 || raw.IndexOf("LVH", StringComparison.OrdinalIgnoreCase) >= 0)
                return "NV83.NCRS-LVHEF";

            if (raw.IndexOf("LVF", StringComparison.OrdinalIgnoreCase) >= 0)
                return "NV83.NCRS-LVF";

            return "NV83.NCRS-LVF";
        }

        private static Assembly LoadManagedMapApiAssembly()
        {
            try
            {
                return Assembly.Load("ManagedMapApi");
            }
            catch
            {
                Assembly? loaded = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => string.Equals(a.GetName().Name, "ManagedMapApi", StringComparison.OrdinalIgnoreCase));
                if (loaded != null)
                    return loaded;

                throw;
            }
        }

        private static object? GetPropertyValue(object? target, string propertyName)
        {
            if (target == null)
                return null;

            Type type = target is Type staticType ? staticType : target.GetType();
            object? instance = target is Type ? null : target;
            PropertyInfo? property = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.IgnoreCase);
            if (property == null)
                return null;

            try
            {
                return property.GetValue(instance);
            }
            catch
            {
                return null;
            }
        }

        private static object? TryInvokeWithResult(object? target, string methodName, params object[] args)
        {
            if (target == null)
                return null;

            Type type = target is Type staticType ? staticType : target.GetType();
            object? instance = target is Type ? null : target;

            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static).Where(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase)))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != args.Length)
                    continue;

                try
                {
                    return method.Invoke(instance, args);
                }
                catch
                {
                    // try next overload
                }
            }

            return null;
        }

        private static double GetCurrentInsertionScale()
        {
            double dimScale = TryGetPositiveSystemVariable("DIMSCALE");
            if (dimScale > 0.0 && Math.Abs(dimScale - 1.0) > 1e-9)
                return dimScale;

            double cannoScaleValue = TryGetPositiveSystemVariable("CANNOSCALEVALUE");
            if (cannoScaleValue > 0.0)
            {
                // Civil 3D can report CANNOSCALEVALUE as the viewport/custom-scale style ratio.
                // Example: an active 50 scale may return 0.02, while the desired model-space
                // block insertion scale is 50. Normalize values below 1 by inverting them.
                if (cannoScaleValue < 1.0)
                    return 1.0 / cannoScaleValue;

                return cannoScaleValue;
            }

            if (dimScale > 0.0)
                return dimScale;

            return 1.0;
        }

        private static double TryGetPositiveSystemVariable(string sysVar)
        {
            try
            {
                object value = AcApp.GetSystemVariable(sysVar);
                double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return number > 0.0 ? number : 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        private static Extents2d GetExtents(IEnumerable<Point2d> points)
        {
            List<Point2d> list = points.ToList();
            double minX = list.Min(p => p.X);
            double maxX = list.Max(p => p.X);
            double minY = list.Min(p => p.Y);
            double maxY = list.Max(p => p.Y);
            return new Extents2d(minX, minY, maxX, maxY);
        }

        private sealed class MarkerData
        {
            internal MarkerData(Point3d point, Dictionary<string, string> attributes)
            {
                Point = point;
                NW = BuildQuadrant(attributes, "NW");
                NE = BuildQuadrant(attributes, "NE");
                SW = BuildQuadrant(attributes, "SW");
                SE = BuildQuadrant(attributes, "SE");
            }

            internal Point3d Point { get; }
            internal QuadrantData NW { get; }
            internal QuadrantData NE { get; }
            internal QuadrantData SW { get; }
            internal QuadrantData SE { get; }
            internal bool HasAnySection => !string.IsNullOrWhiteSpace(NW.SectionKey)
                || !string.IsNullOrWhiteSpace(NE.SectionKey)
                || !string.IsNullOrWhiteSpace(SW.SectionKey)
                || !string.IsNullOrWhiteSpace(SE.SectionKey);

            private static QuadrantData BuildQuadrant(Dictionary<string, string> attrs, string quadrant)
            {
                attrs.TryGetValue($"{quadrant}_TOWNSHIP", out string? township);
                attrs.TryGetValue($"{quadrant}_SECTION", out string? section);
                attrs.TryGetValue($"{quadrant}_SECTION_KEY", out string? sectionKey);

                township = (township ?? string.Empty).Trim();
                section = (section ?? string.Empty).Trim();
                sectionKey = (sectionKey ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(sectionKey) && !string.IsNullOrWhiteSpace(township) && !string.IsNullOrWhiteSpace(section))
                    sectionKey = township + "-" + section;

                if ((string.IsNullOrWhiteSpace(township) || string.IsNullOrWhiteSpace(section)) && !string.IsNullOrWhiteSpace(sectionKey))
                {
                    int dash = sectionKey.LastIndexOf('-');
                    if (dash > 0 && dash < sectionKey.Length - 1)
                    {
                        township = sectionKey[..dash];
                        section = sectionKey[(dash + 1)..];
                    }
                }

                return new QuadrantData(township, section, sectionKey);
            }
        }

        private sealed class QuadrantData
        {
            internal QuadrantData(string township, string section, string sectionKey)
            {
                Township = township;
                Section = section;
                SectionKey = sectionKey;
            }

            internal string Township { get; }
            internal string Section { get; }
            internal string SectionKey { get; }
        }

        private sealed class SectionData
        {
            internal SectionData(string sectionKey)
            {
                SectionKey = sectionKey;
            }

            internal string SectionKey { get; }
            internal MarkerData? NW { get; private set; }
            internal MarkerData? NE { get; private set; }
            internal MarkerData? SW { get; private set; }
            internal MarkerData? SE { get; private set; }
            internal bool HasFourCorners => NW != null && NE != null && SW != null && SE != null;

            internal void SetCorner(string corner, MarkerData marker)
            {
                switch (corner.ToUpperInvariant())
                {
                    case "NW": NW ??= marker; break;
                    case "NE": NE ??= marker; break;
                    case "SW": SW ??= marker; break;
                    case "SE": SE ??= marker; break;
                }
            }

            internal Point3d GetPoint(double u, double v)
            {
                Point3d nw = NW!.Point;
                Point3d ne = NE!.Point;
                Point3d sw = SW!.Point;
                Point3d se = SE!.Point;

                double x = (1.0 - u) * (1.0 - v) * sw.X + u * (1.0 - v) * se.X + (1.0 - u) * v * nw.X + u * v * ne.X;
                double y = (1.0 - u) * (1.0 - v) * sw.Y + u * (1.0 - v) * se.Y + (1.0 - u) * v * nw.Y + u * v * ne.Y;
                double z = (1.0 - u) * (1.0 - v) * sw.Z + u * (1.0 - v) * se.Z + (1.0 - u) * v * nw.Z + u * v * ne.Z;

                return new Point3d(x, y, z);
            }
        }

        private sealed class PlssInsertRequest
        {
            internal PlssInsertRequest(string blockName, Point3d position, Dictionary<string, string> attributes)
            {
                BlockName = blockName;
                Position = position;
                Attributes = attributes;
            }

            internal string BlockName { get; }
            internal Point3d Position { get; }
            internal Dictionary<string, string> Attributes { get; }
        }

        private sealed class InsertRequestSet
        {
            internal InsertRequestSet(List<PlssInsertRequest> requests, int skippedDuplicateCount)
            {
                Requests = requests;
                SkippedDuplicateCount = skippedDuplicateCount;
            }

            internal List<PlssInsertRequest> Requests { get; }
            internal int SkippedDuplicateCount { get; }
        }

        private sealed class BoundaryArea
        {
            internal BoundaryArea(List<Point2d> points)
            {
                Points = points;
                Extents = GetExtents(points);
            }

            private List<Point2d> Points { get; }
            private Extents2d Extents { get; }

            internal bool Contains(Point3d point)
            {
                return Contains(new Point2d(point.X, point.Y));
            }

            private bool Contains(Point2d point)
            {
                if (point.X < Extents.MinPoint.X || point.X > Extents.MaxPoint.X || point.Y < Extents.MinPoint.Y || point.Y > Extents.MaxPoint.Y)
                    return false;

                bool inside = false;
                int count = Points.Count;
                for (int i = 0, j = count - 1; i < count; j = i++)
                {
                    Point2d pi = Points[i];
                    Point2d pj = Points[j];

                    bool intersects = ((pi.Y > point.Y) != (pj.Y > point.Y))
                        && (point.X < (pj.X - pi.X) * (point.Y - pi.Y) / ((pj.Y - pi.Y) == 0.0 ? double.Epsilon : (pj.Y - pi.Y)) + pi.X);

                    if (intersects)
                        inside = !inside;
                }

                return inside;
            }

            internal bool ExtentsIntersect(Extents2d other)
            {
                return Extents.MinPoint.X <= other.MaxPoint.X && Extents.MaxPoint.X >= other.MinPoint.X
                    && Extents.MinPoint.Y <= other.MaxPoint.Y && Extents.MaxPoint.Y >= other.MinPoint.Y;
            }
        }
    }
}
