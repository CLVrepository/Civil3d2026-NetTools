using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Gis.Map.Platform;

using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcException = Autodesk.AutoCAD.Runtime.Exception;

namespace CLV_CivilTools.Survey
{
    public static class SurveyGisSectionMarkerCommands
    {
        private const string MarkerBlockName = "GIS_SECTION_MARKER";
        private const string ApnFieldName = "APN";
        private const double SameSideTolerance = 0.01;

        private static readonly string[] QuadrantOrder = { "NW", "NE", "SW", "SE" };

        [CommandMethod("SURVEY-GIS-SECTION-MARKER", CommandFlags.Modal)]
        [CommandMethod("GISSECTIONMARKER", CommandFlags.Modal)]
        public static void CreateGisSectionMarker()
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                PromptPointResult pointResult = ed.GetPoint("\nPick GIS section marker insertion point: ");
                if (pointResult.Status != PromptStatus.OK)
                    return;

                Point3d markerPoint = pointResult.Value;

                PromptSelectionOptions selectionOptions = new PromptSelectionOptions
                {
                    MessageForAdding = "\nWindow/select CLV_Sections Map Features or imported closed polylines around the marker point: ",
                    AllowDuplicates = false
                };

                PromptSelectionResult selectionResult = ed.GetSelection(selectionOptions);
                if (selectionResult.Status != PromptStatus.OK)
                    return;

                List<SectionCandidate> candidates = new List<SectionCandidate>();

                // .layer / FDO workflow: the selected objects are Map Features, not DB polygon entities.
                // Read the active Map Feature selection through GenerateFilter + FeatureService.
                candidates.AddRange(ReadSelectedMapFeatureCandidates(selectionResult.Value, markerPoint));

                // MAPIMPORT workflow: selected objects are imported CAD entities, usually closed polylines
                // with Map Object Data attached. Read APN from attached Object Data records.
                ObjectId[] selectedIds = selectionResult.Value.GetObjectIds();
                if (selectedIds.Length > 0)
                    candidates.AddRange(ReadEntityObjectDataCandidates(db, selectedIds));

                candidates = candidates
                    .Where(c => !string.IsNullOrWhiteSpace(c.SectionKey))
                    .GroupBy(c => c.SectionKey, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.OrderBy(c => c.Center.DistanceTo(markerPoint)).First())
                    .ToList();

                Dictionary<string, SectionCandidate> byQuadrant = AssignQuadrants(markerPoint, candidates);
                if (byQuadrant.Count == 0)
                {
                    ed.WriteMessage("\nNo selected CLV_Sections features with an APN value were found.");
                    ed.WriteMessage("\nConfirm the section GIS layer is selectable and has Feature Properties > APN values like 126-36.");
                    return;
                }

                string preview = BuildPreview(byQuadrant);
                ed.WriteMessage(preview);

                PromptKeywordOptions confirmOptions = new PromptKeywordOptions("\nCreate GIS_SECTION_MARKER with these values? [Yes/No] <Yes>: ")
                {
                    AllowNone = true
                };
                confirmOptions.Keywords.Add("Yes");
                confirmOptions.Keywords.Add("No");
                confirmOptions.Keywords.Default = "Yes";

                PromptResult confirmResult = ed.GetKeywords(confirmOptions);
                if (confirmResult.Status == PromptStatus.Cancel || string.Equals(confirmResult.StringResult, "No", StringComparison.OrdinalIgnoreCase))
                {
                    ed.WriteMessage("\nGIS section marker cancelled.");
                    return;
                }

                using DocumentLock docLock = doc.LockDocument();
                using Transaction tr = db.TransactionManager.StartTransaction();

                ObjectId blockDefId = GetBlockDefinitionId(db, tr, MarkerBlockName);
                if (blockDefId.IsNull)
                {
                    ed.WriteMessage($"\nBlock definition '{MarkerBlockName}' was not found in this drawing. Insert/load the block definition first, then run the command again.");
                    return;
                }

                ObjectId markerId = InsertMarkerBlock(db, tr, blockDefId, markerPoint, byQuadrant);
                tr.Commit();

                ed.WriteMessage($"\nCreated {MarkerBlockName} ({BuildMarkerId(byQuadrant)}) at {FormatPoint(markerPoint)}. ObjectId: {markerId}.");
            }
            catch (System.Exception ex) when (ex is not AcException)
            {
                ed.WriteMessage($"\nSURVEY-GIS-SECTION-MARKER error: {ex.Message}");
            }
            catch (AcException ex)
            {
                ed.WriteMessage($"\nSURVEY-GIS-SECTION-MARKER AutoCAD error: {ex.Message}");
            }
        }

        private static Dictionary<string, SectionCandidate> AssignQuadrants(Point3d markerPoint, IEnumerable<SectionCandidate> candidates)
        {
            Dictionary<string, SectionCandidate> result = new Dictionary<string, SectionCandidate>(StringComparer.OrdinalIgnoreCase);

            foreach (SectionCandidate candidate in candidates)
            {
                string? quadrant = DetermineQuadrant(markerPoint, candidate.Center);
                if (quadrant == null)
                    continue;

                if (!result.TryGetValue(quadrant, out SectionCandidate? existing))
                {
                    result[quadrant] = candidate;
                    continue;
                }

                double currentDistance = candidate.Center.DistanceTo(markerPoint);
                double existingDistance = existing.Center.DistanceTo(markerPoint);
                if (currentDistance < existingDistance)
                    result[quadrant] = candidate;
            }

            return result;
        }

        private static string? DetermineQuadrant(Point3d markerPoint, Point3d centerPoint)
        {
            double dx = centerPoint.X - markerPoint.X;
            double dy = centerPoint.Y - markerPoint.Y;

            if (Math.Abs(dx) <= SameSideTolerance || Math.Abs(dy) <= SameSideTolerance)
                return null;

            if (dx < 0.0 && dy > 0.0) return "NW";
            if (dx > 0.0 && dy > 0.0) return "NE";
            if (dx < 0.0 && dy < 0.0) return "SW";
            if (dx > 0.0 && dy < 0.0) return "SE";

            return null;
        }

        private static string BuildPreview(Dictionary<string, SectionCandidate> byQuadrant)
        {
            List<string> lines = new List<string>
            {
                "\nFound GIS section data:",
                $"  NW = {GetDisplay(byQuadrant, "NW")}",
                $"  NE = {GetDisplay(byQuadrant, "NE")}",
                $"  SW = {GetDisplay(byQuadrant, "SW")}",
                $"  SE = {GetDisplay(byQuadrant, "SE")}",
                $"  MARKER_ID = {BuildMarkerId(byQuadrant)}"
            };

            return string.Join("\n", lines);
        }

        private static string GetDisplay(Dictionary<string, SectionCandidate> byQuadrant, string quadrant)
        {
            return byQuadrant.TryGetValue(quadrant, out SectionCandidate? candidate)
                ? candidate.SectionKey
                : "Missing";
        }

        private static string BuildMarkerId(Dictionary<string, SectionCandidate> byQuadrant)
        {
            List<string> parts = new List<string>();
            foreach (string quadrant in QuadrantOrder)
            {
                if (byQuadrant.TryGetValue(quadrant, out SectionCandidate? candidate) && !string.IsNullOrWhiteSpace(candidate.SectionKey))
                    parts.Add(quadrant + candidate.SectionKey);
            }

            return parts.Count == 0 ? string.Empty : string.Join("_", parts);
        }

        private static ObjectId InsertMarkerBlock(Database db, Transaction tr, ObjectId blockDefId, Point3d markerPoint, Dictionary<string, SectionCandidate> byQuadrant)
        {
            BlockTableRecord currentSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            BlockReference br = new BlockReference(markerPoint, blockDefId)
            {
                LayerId = db.Clayer
            };

            ObjectId blockRefId = currentSpace.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);

            AddAttributesFromDefinition(br, tr);

            Dictionary<string, string> attributeValues = BuildAttributeValues(byQuadrant);
            ApplyAttributeValues(br, tr, attributeValues);

            return blockRefId;
        }

        private static Dictionary<string, string> BuildAttributeValues(Dictionary<string, SectionCandidate> byQuadrant)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["MARKER_ID"] = BuildMarkerId(byQuadrant)
            };

            foreach (string quadrant in QuadrantOrder)
            {
                values[$"{quadrant}_TOWNSHIP"] = string.Empty;
                values[$"{quadrant}_SECTION"] = string.Empty;
                values[$"{quadrant}_SECTION_KEY"] = string.Empty;

                if (!byQuadrant.TryGetValue(quadrant, out SectionCandidate? candidate))
                    continue;

                values[$"{quadrant}_TOWNSHIP"] = candidate.Township;
                values[$"{quadrant}_SECTION"] = candidate.Section;
                values[$"{quadrant}_SECTION_KEY"] = candidate.SectionKey;
            }

            return values;
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

        private static ObjectId GetBlockDefinitionId(Database db, Transaction tr, string blockName)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            return bt.Has(blockName) ? bt[blockName] : ObjectId.Null;
        }

        private static List<SectionCandidate> ReadSelectedMapFeatureCandidates(SelectionSet acadSelection, Point3d markerPoint)
        {
            List<SectionCandidate> candidates = new List<SectionCandidate>();

            // FDO/.layer selections are represented in AutoCAD as feature/bulk entities.
            // Convert the AutoCAD selection set to a Map platform MgSelectionBase first.
            // This is the documented path through AcMapFeatureEntityService.GetSelection(...).
            object? selection = GetPlatformSelectionFromAcadSelection(acadSelection);

            // Fallback to the current Map feature selection if Map already has it populated.
            if (selection == null)
            {
                try
                {
                    AcMapMap map = AcMapMap.GetCurrentMap();
                    selection = TryInvokeWithResult(map, "GetFeatureSelection")
                        ?? GetPropertyValue(map, "FeatureSelection")
                        ?? GetPropertyValue(map, "Selection");
                }
                catch
                {
                    selection = null;
                }
            }

            if (selection == null)
                return candidates;

            try
            {
                object? layers = TryInvokeWithResult(selection, "GetLayers")
                    ?? GetPropertyValue(selection, "Layers");

                foreach (object layer in Enumerate(layers))
                {
                    string featureClassName = Convert.ToString(
                        TryInvokeWithResult(layer, "GetFeatureClassName")
                        ?? GetPropertyValue(layer, "FeatureClassName"), CultureInfo.InvariantCulture) ?? string.Empty;

                    object? reader = CreateSelectedFeatureReader(selection, layer, featureClassName);
                    if (reader == null)
                        continue;

                    try
                    {
                        while (ReadNext(reader))
                        {
                            string? apn = TryReadFeatureProperty(reader, ApnFieldName);
                            if (!TryParseSectionKey(apn, out string township, out string section, out string sectionKey))
                                continue;

                            Point3d center = TryReadFeatureGeometryCenter(reader, markerPoint);
                            candidates.Add(new SectionCandidate(township, section, sectionKey, center));
                        }
                    }
                    finally
                    {
                        TryInvoke(reader, "Close");
                        TryInvoke(reader, "Dispose");
                    }
                }
            }
            catch
            {
                // Feature selection access varies by Map/Civil 3D install and layer type.
                // Object-data fallback and user-facing no-data messages handle failure.
            }

            return candidates;
        }

        private static object? GetPlatformSelectionFromAcadSelection(SelectionSet acadSelection)
        {
            try
            {
                Type? serviceType = FindLoadedType("Autodesk.Gis.Map.Platform.Interop.AcMapFeatureEntityService")
                    ?? FindLoadedType("Autodesk.Gis.Map.Platform.Interop.AcMapFeatureEntityService", loadAssemblyName: "Autodesk.Map.Platform");
                if (serviceType == null)
                    return null;

                object? service = null;
                try
                {
                    service = Activator.CreateInstance(serviceType, nonPublic: true);
                }
                catch
                {
                    try
                    {
                        service = Activator.CreateInstance(serviceType);
                    }
                    catch
                    {
                        service = null;
                    }
                }

                return TryInvokeWithResult(service, "GetSelection", acadSelection)
                    ?? TryInvokeWithResult(serviceType, "GetSelection", acadSelection)
                    ?? TryInvokeWithResult(service, "GetFeatures", ToObjectIdCollection(acadSelection))
                    ?? TryInvokeWithResult(serviceType, "GetFeatures", ToObjectIdCollection(acadSelection));
            }
            catch
            {
                return null;
            }
        }

        private static ObjectIdCollection ToObjectIdCollection(SelectionSet selectionSet)
        {
            ObjectIdCollection ids = new ObjectIdCollection();
            foreach (ObjectId id in selectionSet.GetObjectIds())
            {
                if (!id.IsNull && !id.IsErased)
                    ids.Add(id);
            }

            return ids;
        }

        private static bool ReadNext(object reader)
        {
            object? result = TryInvokeWithResult(reader, "ReadNext");
            return result is bool value && value;
        }


        private static object? CreateSelectedFeatureReader(object selection, object layer, string featureClassName)
        {
            // Preferred Map 3D/FDO path. Feature selections are identified by feature IDs;
            // GenerateFilter builds the ID filter, then FeatureService.SelectFeatures returns
            // the actual feature reader containing properties like APN and the geometry.
            try
            {
                string? selectionFilter = Convert.ToString(
                    TryInvokeWithResult(selection, "GenerateFilter", layer, featureClassName)
                    ?? TryInvokeWithResult(selection, "GenerateFilter", layer), CultureInfo.InvariantCulture);

                if (!string.IsNullOrWhiteSpace(selectionFilter))
                {
                    object? featureService = GetFeatureService();
                    object? queryOptions = CreateMapGuideObject("OSGeo.MapGuide.MgFeatureQueryOptions");
                    if (featureService != null && queryOptions != null)
                    {
                        TryInvoke(queryOptions, "SetFilter", selectionFilter);

                        string? featureSourceId = Convert.ToString(
                            TryInvokeWithResult(layer, "GetFeatureSourceId")
                            ?? GetPropertyValue(layer, "FeatureSourceId"), CultureInfo.InvariantCulture);

                        object? resourceId = string.IsNullOrWhiteSpace(featureSourceId)
                            ? null
                            : CreateMapGuideObject("OSGeo.MapGuide.MgResourceIdentifier", featureSourceId);

                        if (resourceId != null)
                        {
                            object? reader = TryInvokeWithResult(featureService, "SelectFeatures", resourceId, featureClassName, queryOptions);
                            if (reader != null)
                                return reader;
                        }
                    }
                }
            }
            catch
            {
                // Fall through to legacy/direct selection attempts.
            }

            // Legacy/reflection fallbacks. Some Map builds expose helper methods directly on selection.
            return TryInvokeWithResult(selection, "GetSelectedFeatures", layer, featureClassName, false)
                ?? TryInvokeWithResult(selection, "GetSelectedFeatures", layer, featureClassName)
                ?? TryInvokeWithResult(selection, "GetSelectedFeatures", layer, false)
                ?? TryInvokeWithResult(selection, "GetSelectedFeatures", layer);
        }

        private static object? GetFeatureService()
        {
            try
            {
                Type? serviceFactoryType = FindLoadedType("Autodesk.Gis.Map.Platform.AcMapServiceFactory")
                    ?? FindLoadedType("Autodesk.Gis.Map.Platform.AcMapServiceFactory", loadAssemblyName: "Autodesk.Map.Platform");
                if (serviceFactoryType == null)
                    return null;

                Type? serviceType = FindLoadedType("OSGeo.MapGuide.MgServiceType")
                    ?? FindLoadedType("OSGeo.MapGuide.MgServiceType", loadAssemblyName: "OSGeo.MapGuide.PlatformBase")
                    ?? FindLoadedType("OSGeo.MapGuide.MgServiceType", loadAssemblyName: "OSGeo.MapGuide.Foundation");
                object? featureServiceValue = null;
                if (serviceType != null)
                {
                    foreach (string name in new[] { "FeatureService", "Feature" })
                    {
                        try
                        {
                            featureServiceValue = Enum.Parse(serviceType, name, ignoreCase: true);
                            break;
                        }
                        catch
                        {
                            // try next name
                        }
                    }

                    if (featureServiceValue == null && serviceType.IsEnum)
                    {
                        foreach (object value in Enum.GetValues(serviceType))
                        {
                            if (value.ToString()?.IndexOf("Feature", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                featureServiceValue = value;
                                break;
                            }
                        }
                    }
                }

                if (featureServiceValue == null)
                    return null;

                return TryInvokeWithResult(serviceFactoryType, "GetService", featureServiceValue)
                    ?? TryInvokeWithResult(serviceFactoryType, "GetService", Convert.ToInt32(featureServiceValue, CultureInfo.InvariantCulture));
            }
            catch
            {
                return null;
            }
        }

        private static object? CreateMapGuideObject(string typeName, params object[] args)
        {
            try
            {
                Type? type = FindLoadedType(typeName)
                    ?? FindLoadedType(typeName, loadAssemblyName: "OSGeo.MapGuide.PlatformBase")
                    ?? FindLoadedType(typeName, loadAssemblyName: "OSGeo.MapGuide.Foundation");
                return type == null ? null : Activator.CreateInstance(type, args);
            }
            catch
            {
                return null;
            }
        }

        private static Type? FindLoadedType(string typeName, string? loadAssemblyName = null)
        {
            if (!string.IsNullOrWhiteSpace(loadAssemblyName))
            {
                try
                {
                    Assembly loaded = Assembly.Load(loadAssemblyName);
                    Type? loadedType = loaded.GetType(typeName, throwOnError: false);
                    if (loadedType != null)
                        return loadedType;
                }
                catch
                {
                    // try already loaded assemblies next
                }
            }

            return AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(typeName, throwOnError: false))
                .FirstOrDefault(t => t != null);
        }

        private static string? TryReadFeatureProperty(object reader, string preferredName)
        {
            foreach (string propertyName in GetFeaturePropertyNames(reader))
            {
                if (!string.Equals(propertyName, preferredName, StringComparison.OrdinalIgnoreCase))
                    continue;

                object? value = TryGetFeatureValue(reader, propertyName);
                string? text = Convert.ToString(value, CultureInfo.InvariantCulture);
                return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            }

            object? direct = TryGetFeatureValue(reader, preferredName);
            string? directText = Convert.ToString(direct, CultureInfo.InvariantCulture);
            return string.IsNullOrWhiteSpace(directText) ? null : directText.Trim();
        }

        private static IEnumerable<string> GetFeaturePropertyNames(object reader)
        {
            object? countObject = TryInvokeWithResult(reader, "GetPropertyCount");
            if (!TryToInt(countObject, out int count) || count <= 0)
                yield break;

            for (int i = 0; i < count; i++)
            {
                string? name = Convert.ToString(TryInvokeWithResult(reader, "GetPropertyName", i), CultureInfo.InvariantCulture);
                if (!string.IsNullOrWhiteSpace(name))
                    yield return name;
            }
        }

        private static object? TryGetFeatureValue(object reader, string propertyName)
        {
            object? isNull = TryInvokeWithResult(reader, "IsNull", propertyName);
            if (isNull is bool isNullValue && isNullValue)
                return null;

            foreach (string methodName in new[]
            {
                "GetString", "GetInt32", "GetInt64", "GetDouble", "GetSingle", "GetDecimal", "GetBoolean", "GetValue"
            })
            {
                object? value = TryInvokeWithResult(reader, methodName, propertyName);
                if (value != null)
                    return value;
            }

            return null;
        }

        private static Point3d TryReadFeatureGeometryCenter(object reader, Point3d fallback)
        {
            foreach (string propertyName in GetFeaturePropertyNames(reader).Concat(new[] { "Geometry", "GEOMETRY", "Geom", "SHAPE" }).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                object? byteReader = TryInvokeWithResult(reader, "GetGeometry", propertyName);
                if (byteReader == null)
                    continue;

                object? geometry = TryReadAgfGeometry(byteReader);
                if (geometry == null)
                    continue;

                if (TryGetGeometryEnvelopeCenter(geometry, out Point3d center))
                    return center;
            }

            return fallback;
        }

        private static object? TryReadAgfGeometry(object byteReader)
        {
            try
            {
                Type? agfType = Type.GetType("OSGeo.MapGuide.MgAgfReaderWriter, OSGeo.MapGuide.PlatformBase", throwOnError: false)
                    ?? Type.GetType("OSGeo.MapGuide.MgAgfReaderWriter, OSGeo.MapGuide.Foundation", throwOnError: false)
                    ?? AppDomain.CurrentDomain.GetAssemblies()
                        .Select(a => a.GetType("OSGeo.MapGuide.MgAgfReaderWriter", throwOnError: false))
                        .FirstOrDefault(t => t != null);

                if (agfType == null)
                    return null;

                object? agf = Activator.CreateInstance(agfType);
                return agf == null ? null : TryInvokeWithResult(agf, "Read", byteReader);
            }
            catch
            {
                return null;
            }
        }

        private static bool TryGetGeometryEnvelopeCenter(object geometry, out Point3d center)
        {
            center = Point3d.Origin;

            object? envelope = TryInvokeWithResult(geometry, "Envelope")
                ?? TryInvokeWithResult(geometry, "GetEnvelope")
                ?? GetPropertyValue(geometry, "Envelope");

            if (envelope == null)
                return false;

            object? lower = TryInvokeWithResult(envelope, "GetLowerLeftCoordinate")
                ?? TryInvokeWithResult(envelope, "LowerLeftCoordinate")
                ?? GetPropertyValue(envelope, "LowerLeftCoordinate");
            object? upper = TryInvokeWithResult(envelope, "GetUpperRightCoordinate")
                ?? TryInvokeWithResult(envelope, "UpperRightCoordinate")
                ?? GetPropertyValue(envelope, "UpperRightCoordinate");

            if (!TryGetCoordinateXY(lower, out double x1, out double y1) || !TryGetCoordinateXY(upper, out double x2, out double y2))
                return false;

            center = new Point3d((x1 + x2) * 0.5, (y1 + y2) * 0.5, 0.0);
            return true;
        }

        private static bool TryGetCoordinateXY(object? coordinate, out double x, out double y)
        {
            x = 0.0;
            y = 0.0;
            if (coordinate == null)
                return false;

            object? xObject = TryInvokeWithResult(coordinate, "GetX") ?? GetPropertyValue(coordinate, "X");
            object? yObject = TryInvokeWithResult(coordinate, "GetY") ?? GetPropertyValue(coordinate, "Y");
            return TryToDouble(xObject, out x) && TryToDouble(yObject, out y);
        }

        private static List<SectionCandidate> ReadEntityObjectDataCandidates(Database db, ObjectId[] objectIds)
        {
            List<SectionCandidate> candidates = new List<SectionCandidate>();

            using Transaction tr = db.TransactionManager.StartOpenCloseTransaction();
            foreach (ObjectId objectId in objectIds)
            {
                if (objectId.IsNull || objectId.IsErased)
                    continue;

                string? apn = TryReadEntityObjectDataValue(objectId, ApnFieldName);
                if (!TryParseSectionKey(apn, out string township, out string section, out string sectionKey))
                    continue;

                if (tr.GetObject(objectId, OpenMode.ForRead, false) is not Entity entity)
                    continue;

                Point3d center = GetEntityCenter(entity);
                candidates.Add(new SectionCandidate(township, section, sectionKey, center));
            }
            tr.Commit();

            return candidates;
        }

        private static string? TryReadEntityObjectDataValue(ObjectId entityId, string fieldName)
        {
            try
            {
                foreach (object table in GetAllObjectDataTables())
                {
                    object? records = GetObjectDataRecords(table, entityId);
                    if (records == null)
                        continue;

                    List<ObjectDataFieldDefinitionSnapshot> definitions = GetObjectDataFieldDefinitions(table);
                    int index = definitions.FindIndex(d => string.Equals(d.Name, fieldName, StringComparison.OrdinalIgnoreCase));
                    if (index < 0)
                        continue;

                    foreach (object record in Enumerate(records))
                    {
                        object? mapValue = TryInvokeWithResult(record, "get_Item", index)
                            ?? TryInvokeWithResult(record, "Item", index)
                            ?? TryInvokeWithResult(record, "GetAt", index)
                            ?? TryInvokeWithResult(record, "GetValue", index);

                        object? extracted = ExtractMapValue(mapValue);
                        string? text = Convert.ToString(extracted, CultureInfo.InvariantCulture);
                        if (!string.IsNullOrWhiteSpace(text))
                            return text.Trim();
                    }
                }
            }
            catch
            {
                // ignored - Map OD API availability varies by install.
            }

            return null;
        }

        private static IEnumerable<object> GetAllObjectDataTables()
        {
            Assembly asm = LoadManagedMapApiAssembly();
            Type hostType = asm.GetType("Autodesk.Gis.Map.HostMapApplicationServices", throwOnError: true)!;
            object? application = GetPropertyValue(hostType, "Application");
            object? activeProject = application == null ? null : GetPropertyValue(application, "ActiveProject");
            object? tables = activeProject == null
                ? null
                : GetPropertyValue(activeProject, "ODTables")
                    ?? GetPropertyValue(activeProject, "ObjectDataTables")
                    ?? GetPropertyValue(activeProject, "Tables");

            return Enumerate(tables).ToList();
        }

        private static object? GetObjectDataRecords(object table, ObjectId entityId)
        {
            object? mapOpenMode = GetMapObjectDataOpenMode(table.GetType().Assembly);
            List<object[]> argSets = new List<object[]>();

            if (mapOpenMode != null)
            {
                argSets.Add(new object[] { 0u, entityId, mapOpenMode, false });
                argSets.Add(new object[] { 0, entityId, mapOpenMode, false });
                argSets.Add(new object[] { entityId, mapOpenMode, false });
                argSets.Add(new object[] { entityId, mapOpenMode });
            }

            // MAPIMPORT-created polylines often expose OD records through overloads that use
            // AutoCAD.DatabaseServices.OpenMode instead of the Map OD OpenMode enum.
            argSets.Add(new object[] { 0u, entityId, OpenMode.ForRead, false });
            argSets.Add(new object[] { 0, entityId, OpenMode.ForRead, false });
            argSets.Add(new object[] { entityId, OpenMode.ForRead, false });
            argSets.Add(new object[] { entityId, OpenMode.ForRead });
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

        private static object? GetMapObjectDataOpenMode(Assembly assembly)
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

                foreach (string name in new[] { "OpenForRead", "ForRead", "Read" })
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
                    return values.GetValue(0);
            }

            return null;
        }

        private static List<ObjectDataFieldDefinitionSnapshot> GetObjectDataFieldDefinitions(object table)
        {
            List<ObjectDataFieldDefinitionSnapshot> result = new List<ObjectDataFieldDefinitionSnapshot>();
            foreach (string propertyName in new[] { "FieldDefinitions", "Definitions", "Columns", "Fields" })
            {
                object? definitions = GetPropertyValue(table, propertyName);
                if (definitions == null)
                    continue;

                int index = 0;
                foreach (object definition in Enumerate(definitions))
                {
                    string name = Convert.ToString(
                        GetPropertyValue(definition, "Name")
                        ?? GetPropertyValue(definition, "ColumnName")
                        ?? GetPropertyValue(definition, "FieldName"), CultureInfo.InvariantCulture) ?? $"Field{index}";

                    result.Add(new ObjectDataFieldDefinitionSnapshot(index, name));
                    index++;
                }

                if (result.Count > 0)
                    break;
            }

            return result;
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
                if (nested != null)
                    return nested;
            }

            return Convert.ToString(mapValue, CultureInfo.InvariantCulture);
        }

        private static Point3d GetEntityCenter(Entity entity)
        {
            try
            {
                Extents3d extents = entity.GeometricExtents;
                return new Point3d(
                    (extents.MinPoint.X + extents.MaxPoint.X) * 0.5,
                    (extents.MinPoint.Y + extents.MaxPoint.Y) * 0.5,
                    (extents.MinPoint.Z + extents.MaxPoint.Z) * 0.5);
            }
            catch
            {
                return Point3d.Origin;
            }
        }

        private static bool TryParseSectionKey(string? rawValue, out string township, out string section, out string sectionKey)
        {
            township = string.Empty;
            section = string.Empty;
            sectionKey = string.Empty;

            if (string.IsNullOrWhiteSpace(rawValue))
                return false;

            Match match = Regex.Match(rawValue.Trim(), @"^\s*(?<township>[^\s\-]+)\s*-\s*(?<section>[^\s\-]+)\s*$", RegexOptions.IgnoreCase);
            if (!match.Success)
                return false;

            township = match.Groups["township"].Value.Trim();
            section = match.Groups["section"].Value.Trim();
            sectionKey = township + "-" + section;
            return true;
        }

        private static string FormatPoint(Point3d point)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0:0.###},{1:0.###},{2:0.###}", point.X, point.Y, point.Z);
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

        private static IEnumerable<object> Enumerate(object? source)
        {
            if (source == null)
                yield break;

            if (source is IEnumerable enumerable && source is not string)
            {
                foreach (object item in enumerable)
                    yield return item;
                yield break;
            }

            object? countObject = TryInvokeWithResult(source, "GetCount") ?? GetPropertyValue(source, "Count");
            if (TryToInt(countObject, out int count))
            {
                for (int i = 0; i < count; i++)
                {
                    object? item = TryInvokeWithResult(source, "GetItem", i)
                        ?? TryInvokeWithResult(source, "get_Item", i)
                        ?? TryInvokeWithResult(source, "Item", i);
                    if (item != null)
                        yield return item;
                }
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

                object?[] converted = new object?[args.Length];
                bool canUse = true;
                for (int i = 0; i < args.Length; i++)
                {
                    if (!TryConvertArgument(args[i], parameters[i].ParameterType, out object? convertedArg))
                    {
                        canUse = false;
                        break;
                    }
                    converted[i] = convertedArg;
                }

                if (!canUse)
                    continue;

                try
                {
                    return method.Invoke(instance, converted);
                }
                catch
                {
                    // try next overload
                }
            }

            return null;
        }

        private static bool TryInvoke(object? target, string methodName, params object[] args)
        {
            return TryInvokeWithResult(target, methodName, args) != null;
        }

        private static bool TryConvertArgument(object? value, Type targetType, out object? converted)
        {
            converted = null;
            Type effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            if (value == null)
                return !effectiveType.IsValueType;

            if (effectiveType.IsInstanceOfType(value))
            {
                converted = value;
                return true;
            }

            try
            {
                if (effectiveType.IsEnum)
                {
                    converted = Enum.Parse(effectiveType, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, ignoreCase: true);
                    return true;
                }

                converted = Convert.ChangeType(value, effectiveType, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryToInt(object? value, out int result)
        {
            result = 0;
            try
            {
                if (value == null)
                    return false;
                result = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryToDouble(object? value, out double result)
        {
            result = 0.0;
            try
            {
                if (value == null)
                    return false;
                result = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private sealed class SectionCandidate
        {
            internal SectionCandidate(string township, string section, string sectionKey, Point3d center)
            {
                Township = township;
                Section = section;
                SectionKey = sectionKey;
                Center = center;
            }

            internal string Township { get; }
            internal string Section { get; }
            internal string SectionKey { get; }
            internal Point3d Center { get; }
        }

        private sealed class ObjectDataFieldDefinitionSnapshot
        {
            internal ObjectDataFieldDefinitionSnapshot(int index, string name)
            {
                Index = index;
                Name = name;
            }

            internal int Index { get; }
            internal string Name { get; }
        }
    }
}
