using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

using CLV_CivilTools.Gis;
using CLV_CivilTools.Shared;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Gis.Map.Platform;
using OSGeo.MapGuide;

using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Survey
{
    public static class SurveyRoadLabelCommands
    {
        private const string LabelLayerName = "C-LABL-STNM";
        private const string DrawBoundaryKeyword = "Draw";
        private const string SelectBoundaryKeyword = "Select";
        private const string RoadLayerFilePath = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_GRID_City\000-City\GIS\Assessor Street Centerlines\Street_Centerlines.layer";
        private const string AnnotativeTextStyleName = "CLV-Standard.14";
        private const string SettingsTemplatePath = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Drawing Templates\Reference Templates\Settings (2026).dwt";
        private const double DesiredRoadLabelPaperHeight = 0.14;
        private static readonly string[] RoadMapLayerNameHints = { "Street_Centerlines", "Assessor Street Centerlines" };

        private sealed class PendingMapLabelConversion
        {
            internal PendingMapLabelConversion(HashSet<string> beforeHandles, BoundaryFilter? boundaryFilter, bool unloadConnectionAfter)
            {
                BeforeHandles = beforeHandles;
                BoundaryFilter = boundaryFilter;
                UnloadConnectionAfter = unloadConnectionAfter;
            }

            internal HashSet<string> BeforeHandles { get; }
            internal BoundaryFilter? BoundaryFilter { get; }
            internal bool UnloadConnectionAfter { get; }
        }

        private sealed class BoundaryFilter
        {
            internal BoundaryFilter(IReadOnlyList<Point2d> vertices)
            {
                Vertices = vertices;
            }

            internal IReadOnlyList<Point2d> Vertices { get; }

            internal bool Contains(Point3d point)
            {
                if (Vertices.Count < 3)
                    return true;

                Point2d test = new Point2d(point.X, point.Y);
                const double edgeTolerance = 1.0;

                for (int i = 0, j = Vertices.Count - 1; i < Vertices.Count; j = i++)
                {
                    if (DistancePointToSegment(test, Vertices[j], Vertices[i]) <= edgeTolerance)
                        return true;
                }

                bool inside = false;
                for (int i = 0, j = Vertices.Count - 1; i < Vertices.Count; j = i++)
                {
                    Point2d pi = Vertices[i];
                    Point2d pj = Vertices[j];
                    bool crosses = ((pi.Y > test.Y) != (pj.Y > test.Y)) &&
                        (test.X < (pj.X - pi.X) * (test.Y - pi.Y) / ((pj.Y - pi.Y) == 0.0 ? double.Epsilon : (pj.Y - pi.Y)) + pi.X);
                    if (crosses)
                        inside = !inside;
                }

                return inside;
            }

            private static double DistancePointToSegment(Point2d point, Point2d start, Point2d end)
            {
                double dx = end.X - start.X;
                double dy = end.Y - start.Y;
                double len2 = (dx * dx) + (dy * dy);
                if (len2 <= double.Epsilon)
                    return point.GetDistanceTo(start);

                double t = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / len2;
                t = Math.Max(0.0, Math.Min(1.0, t));
                Point2d projection = new Point2d(start.X + t * dx, start.Y + t * dy);
                return point.GetDistanceTo(projection);
            }
        }

        private static readonly Dictionary<Document, PendingMapLabelConversion> PendingConversions =
            new Dictionary<Document, PendingMapLabelConversion>();

        [CommandMethod("SURVEY-LABEL-ROADS", CommandFlags.Modal)]
        [CommandMethod("Q4LABELROADS", CommandFlags.Modal)]
        public static void RunLabelRoads()
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                RunAutoMode(doc, db, ed);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nSURVEY-LABEL-ROADS error: {ex.Message}");
            }
        }

        [CommandMethod("SURVEY-LABEL-ROADS-CONVERT", CommandFlags.Modal)]
        public static void RunLabelRoadsConvertOnly()
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                StartMapLabelConversion(doc, db, ed, announceOnly: true);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nSURVEY-LABEL-ROADS-CONVERT error: {ex.Message}");
            }
        }

        private static void RunAutoMode(Document doc, Database db, Editor ed)
        {
            ObjectId boundaryId = ObjectId.Null;
            bool eraseBoundaryWhenDone = false;

            try
            {
                using DocumentLock docLock = doc.LockDocument();

                PromptKeywordOptions boundaryOptions = new PromptKeywordOptions("\nBoundary mode [Select/Draw] <Draw>: ")
                {
                    AllowNone = true
                };
                boundaryOptions.Keywords.Add(SelectBoundaryKeyword);
                boundaryOptions.Keywords.Add(DrawBoundaryKeyword);
                boundaryOptions.Keywords.Default = DrawBoundaryKeyword;

                PromptResult boundaryModeResult = ed.GetKeywords(boundaryOptions);
                if (boundaryModeResult.Status == PromptStatus.Cancel)
                    return;

                string boundaryMode = string.IsNullOrWhiteSpace(boundaryModeResult.StringResult)
                    ? DrawBoundaryKeyword
                    : boundaryModeResult.StringResult;

                boundaryId = string.Equals(boundaryMode, SelectBoundaryKeyword, StringComparison.OrdinalIgnoreCase)
                    ? PromptForBoundaryPolyline(doc)
                    : PromptForTemporaryBoundaryPolygon(doc);

                if (boundaryId.IsNull)
                {
                    ed.WriteMessage("\nSURVEY-LABEL-ROADS cancelled: no valid boundary was supplied.");
                    return;
                }

                eraseBoundaryWhenDone = !string.Equals(boundaryMode, SelectBoundaryKeyword, StringComparison.OrdinalIgnoreCase);
                LayerStandards.EnsureSurveyRoadLabelLayer(db, ed);
                EnsureTextStyleFromSettings(db, ed, AnnotativeTextStyleName);

                Extents3d? boundaryExtents = TryGetEntityExtents(db, boundaryId, out Extents3d extents)
                    ? ExpandExtents(extents, 0.08)
                    : null;

                LoadRoadLayerForLabeling(ed);

                BoundaryFilter? boundaryFilter = CreateBoundaryFilter(db, boundaryId);
                if (boundaryFilter == null)
                    ed.WriteMessage("\nRoad labels: boundary cleanup filter could not be created; converted labels will still be standardized, but outside-boundary label deletion may be skipped.");

                if (boundaryExtents.HasValue)
                    ZoomWindow(ed, boundaryExtents.Value);

                PromptKeywordOptions proceed = new PromptKeywordOptions("\nAdjust zoom if needed, then continue? [Continue/Cancel] <Continue>: ", "Continue Cancel")
                {
                    AllowNone = true
                };
                PromptResult proceedResult = ed.GetKeywords(proceed);
                if (proceedResult.Status == PromptStatus.Cancel || string.Equals(proceedResult.StringResult, "Cancel", StringComparison.OrdinalIgnoreCase))
                {
                    UnloadRoadLayer(ed);
                    ed.WriteMessage("\nRoad labels cancelled before MAPLABEL2ANN.");
                    return;
                }

                StartMapLabelConversion(doc, db, ed, announceOnly: false, boundaryFilter: boundaryFilter);
            }
            finally
            {
                if (eraseBoundaryWhenDone && !boundaryId.IsNull)
                    TryEraseEntity(db, boundaryId);
            }
        }

        private static void StartMapLabelConversion(Document doc, Database db, Editor ed, bool announceOnly, BoundaryFilter? boundaryFilter = null, bool unloadConnectionAfter = true)
        {
            using DocumentLock docLock = doc.LockDocument();

            LayerStandards.EnsureSurveyRoadLabelLayer(db, ed);
            EnsureTextStyleFromSettings(db, ed, AnnotativeTextStyleName);

            HashSet<string> beforeHandles = SnapshotModelSpaceHandles(db);
            RegisterPendingConversion(doc, beforeHandles, boundaryFilter, unloadConnectionAfter);

            if (announceOnly)
            {
                ed.WriteMessage($"\nRoad labels: run MAPLABEL2ANN on the visible roadway labels. When MAPLABEL2ANN finishes, the new text will be moved to {LabelLayerName}, switched to {AnnotativeTextStyleName} when available, forced annotative, set to {DesiredRoadLabelPaperHeight.ToString(CultureInfo.InvariantCulture)} paper height using the current annotation scale, set to ByLayer color, converted to uppercase, and outside-boundary labels will be erased when a boundary filter is available.");
            }
            else
            {
                ed.WriteMessage("\nRoad labels: loaded Street_Centerlines.layer, zoomed to the selected area, and launched MAPLABEL2ANN. If MAPLABEL2ANN prompts for selection, convert the visible labels in the current view.");
            }

            doc.SendStringToExecute("_.MAPLABEL2ANN\n", true, false, false);
        }

        private static void LoadRoadLayerForLabeling(Editor ed)
        {
            if (!File.Exists(RoadLayerFilePath))
                throw new FileNotFoundException("Street centerline layer file was not found.", RoadLayerFilePath);

            AcMapMap map = AcMapMap.GetCurrentMap();
            if (map == null)
                throw new InvalidOperationException("AcMapMap.GetCurrentMap() returned null.");

            RemoveExistingRoadLayers(map);
            TryRemoveRoadConnections(ed);
            map.LoadLayer(RoadLayerFilePath);

            ed.WriteMessage($"\nRoad labels: loaded map layer '{Path.GetFileName(RoadLayerFilePath)}'.");
        }

        private static void UnloadRoadLayer(Editor ed)
        {
            try
            {
                AcMapMap map = AcMapMap.GetCurrentMap();
                if (map != null)
                    RemoveExistingRoadLayers(map);

                TryRemoveRoadConnections(ed);
                ed.WriteMessage("\nRoad labels: removed temporary street centerline map layer(s).");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nRoad labels: temporary road layer cleanup skipped -> {ex.Message}");
            }
        }

        private static void RegisterPendingConversion(Document doc, HashSet<string> beforeHandles, BoundaryFilter? boundaryFilter, bool unloadConnectionAfter)
        {
            ClearPendingConversion(doc);
            PendingConversions[doc] = new PendingMapLabelConversion(beforeHandles, boundaryFilter, unloadConnectionAfter);
            doc.CommandEnded += OnTrackedMapLabelCommandFinished;
            doc.CommandCancelled += OnTrackedMapLabelCommandFinished;
            doc.CommandFailed += OnTrackedMapLabelCommandFinished;
        }

        private static void ClearPendingConversion(Document? doc)
        {
            if (doc == null)
                return;

            doc.CommandEnded -= OnTrackedMapLabelCommandFinished;
            doc.CommandCancelled -= OnTrackedMapLabelCommandFinished;
            doc.CommandFailed -= OnTrackedMapLabelCommandFinished;
            PendingConversions.Remove(doc);
        }

        private static void OnTrackedMapLabelCommandFinished(object sender, CommandEventArgs e)
        {
            Document? doc = sender as Document;
            if (doc == null)
                return;

            if (!PendingConversions.TryGetValue(doc, out PendingMapLabelConversion? pending))
            {
                ClearPendingConversion(doc);
                return;
            }

            string finishedCommand = NormalizeCommandName(e.GlobalCommandName);
            if (!string.Equals(finishedCommand, "MAPLABEL2ANN", StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                ProcessConvertedRoadLabels(doc, pending);
            }
            finally
            {
                ClearPendingConversion(doc);
            }
        }

        private static void ProcessConvertedRoadLabels(Document doc, PendingMapLabelConversion pending)
        {
            Database db = doc.Database;
            Editor ed = doc.Editor;

            int textCount = 0;
            int erasedOutsideCount = 0;
            bool styleAvailable = EnsureTextStyleFromSettings(db, ed, AnnotativeTextStyleName);
            ObjectId textStyleId = styleAvailable ? GetTextStyleId(db, AnnotativeTextStyleName) : ObjectId.Null;
            double annotationScaleFactor = GetCurrentAnnotationScaleFactor(db);
            double modelTextHeight = DesiredRoadLabelPaperHeight * annotationScaleFactor;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    if (!id.IsValid || id.IsErased)
                        continue;

                    string handle = id.Handle.ToString();
                    if (pending.BeforeHandles.Contains(handle))
                        continue;

                    if (tr.GetObject(id, OpenMode.ForWrite, false) is MText mt)
                    {
                        Point3d labelPoint = GetLabelReferencePoint(mt);
                        if (pending.BoundaryFilter != null && !pending.BoundaryFilter.Contains(labelPoint))
                        {
                            mt.Erase();
                            erasedOutsideCount++;
                            continue;
                        }

                        mt.Layer = LabelLayerName;
                        mt.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(ColorMethod.ByLayer, 256);
                        if (!textStyleId.IsNull)
                            mt.TextStyleId = textStyleId;
                        mt.Contents = UppercaseLabel(mt.Contents);
                        mt.Annotative = AnnotativeStates.True;
                        mt.TextHeight = modelTextHeight;
                        textCount++;
                    }
                    else if (tr.GetObject(id, OpenMode.ForWrite, false) is DBText dbText)
                    {
                        Point3d labelPoint = GetLabelReferencePoint(dbText);
                        if (pending.BoundaryFilter != null && !pending.BoundaryFilter.Contains(labelPoint))
                        {
                            dbText.Erase();
                            erasedOutsideCount++;
                            continue;
                        }

                        dbText.Layer = LabelLayerName;
                        dbText.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(ColorMethod.ByLayer, 256);
                        if (!textStyleId.IsNull)
                            dbText.TextStyleId = textStyleId;
                        dbText.TextString = UppercaseLabel(dbText.TextString);
                        dbText.Annotative = AnnotativeStates.True;
                        dbText.Height = modelTextHeight;
                        textCount++;
                    }
                }

                tr.Commit();
            }

            if (textCount == 0 && erasedOutsideCount == 0)
                ed.WriteMessage("\nRoad labels: MAPLABEL2ANN finished, but no new DBText/MText objects were found to clean up.");
            else
                ed.WriteMessage($"\nRoad labels complete. cleanedText={textCount}, erasedOutsideBoundary={erasedOutsideCount}, layer={LabelLayerName}, textStyle={(styleAvailable ? AnnotativeTextStyleName : "unchanged")}, annotative=True, color=ByLayer, paperHeight={DesiredRoadLabelPaperHeight.ToString(CultureInfo.InvariantCulture)}, modelHeight={modelTextHeight.ToString(CultureInfo.InvariantCulture)}, annotationScaleFactor={annotationScaleFactor.ToString(CultureInfo.InvariantCulture)}.");

            if (pending.UnloadConnectionAfter)
                UnloadRoadLayer(ed);
        }

        private static BoundaryFilter? CreateBoundaryFilter(Database db, ObjectId boundaryId)
        {
            if (boundaryId.IsNull || !boundaryId.IsValid || boundaryId.IsErased)
                return null;

            try
            {
                using Transaction tr = db.TransactionManager.StartTransaction();
                if (tr.GetObject(boundaryId, OpenMode.ForRead, false) is not Entity boundary)
                {
                    tr.Commit();
                    return null;
                }

                List<Point2d> vertices = new List<Point2d>();
                switch (boundary)
                {
                    case Polyline pl:
                        for (int i = 0; i < pl.NumberOfVertices; i++)
                            AddDistinctVertex(vertices, pl.GetPoint2dAt(i));
                        break;

                    case Polyline2d pl2:
                        foreach (ObjectId vertexId in pl2)
                        {
                            if (tr.GetObject(vertexId, OpenMode.ForRead, false) is Vertex2d vertex)
                                AddDistinctVertex(vertices, new Point2d(vertex.Position.X, vertex.Position.Y));
                        }
                        break;

                    case Polyline3d pl3:
                        foreach (ObjectId vertexId in pl3)
                        {
                            if (tr.GetObject(vertexId, OpenMode.ForRead, false) is PolylineVertex3d vertex)
                                AddDistinctVertex(vertices, new Point2d(vertex.Position.X, vertex.Position.Y));
                        }
                        break;
                }

                tr.Commit();
                return vertices.Count >= 3 ? new BoundaryFilter(vertices) : null;
            }
            catch
            {
                return null;
            }
        }

        private static void AddDistinctVertex(List<Point2d> vertices, Point2d point)
        {
            if (vertices.Count > 0 && vertices[^1].GetDistanceTo(point) <= Tolerance.Global.EqualPoint)
                return;

            if (vertices.Count > 2 && vertices[0].GetDistanceTo(point) <= Tolerance.Global.EqualPoint)
                return;

            vertices.Add(point);
        }

        private static Point3d GetLabelReferencePoint(Entity ent)
        {
            try
            {
                Extents3d extents = ent.GeometricExtents;
                return new Point3d(
                    (extents.MinPoint.X + extents.MaxPoint.X) * 0.5,
                    (extents.MinPoint.Y + extents.MaxPoint.Y) * 0.5,
                    (extents.MinPoint.Z + extents.MaxPoint.Z) * 0.5);
            }
            catch
            {
                if (ent is MText mt)
                    return mt.Location;

                if (ent is DBText dbText)
                {
                    try
                    {
                        if (dbText.Justify != AttachmentPoint.BaseLeft)
                            return dbText.AlignmentPoint;
                    }
                    catch
                    {
                    }

                    return dbText.Position;
                }

                return Point3d.Origin;
            }
        }

        private static double GetCurrentAnnotationScaleFactor(Database db)
        {
            try
            {
                object? annotationScale = db.GetType().GetProperty("Cannoscale", BindingFlags.Public | BindingFlags.Instance)?.GetValue(db);
                if (annotationScale == null)
                    return 1.0;

                double drawingUnits = TryGetDoubleProperty(annotationScale, "DrawingUnits", 1.0);
                double paperUnits = TryGetDoubleProperty(annotationScale, "PaperUnits", 1.0);
                if (Math.Abs(paperUnits) <= double.Epsilon || drawingUnits <= 0.0)
                    return 1.0;

                return drawingUnits / paperUnits;
            }
            catch
            {
                return 1.0;
            }
        }

        private static double TryGetDoubleProperty(object source, string propertyName, double fallback)
        {
            try
            {
                object? value = source.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(source);
                if (value == null)
                    return fallback;

                return Convert.ToDouble(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        private static readonly HashSet<string> DirectionPrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "N",
            "S",
            "E",
            "W"
        };

        private static readonly Dictionary<string, string> SuffixExpansions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AVE"] = "AVENUE",
            ["BLVD"] = "BOULEVARD",
            ["ST"] = "STREET",
            ["RD"] = "ROAD",
            ["DR"] = "DRIVE",
            ["PKWY"] = "PARKWAY",
            ["CT"] = "COURT",
            ["LN"] = "LANE",
            ["CIR"] = "CIRCLE",
            ["WAY"] = "WAY"
        };

        private static string UppercaseLabel(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            string[] parts = value
                .Trim()
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
                return string.Empty;

            if (parts.Length > 1 && DirectionPrefixes.Contains(parts[0]))
                parts = parts.Skip(1).ToArray();

            if (parts.Length == 0)
                return string.Empty;

            string last = parts[^1].TrimEnd('.');
            if (SuffixExpansions.TryGetValue(last, out string? expandedSuffix))
                parts[^1] = expandedSuffix;

            return string.Join(" ", parts).ToUpper(CultureInfo.InvariantCulture);
        }

        private static bool EnsureTextStyleFromSettings(Database targetDb, Editor ed, string styleName)
        {
            if (HasTextStyle(targetDb, styleName))
                return true;

            if (!File.Exists(SettingsTemplatePath))
            {
                ed.WriteMessage($"\nRoad labels: settings template was not found -> {SettingsTemplatePath}");
                return false;
            }

            try
            {
                using Database sourceDb = new Database(false, true);
                sourceDb.ReadDwgFile(SettingsTemplatePath, FileShare.Read, true, string.Empty);
                sourceDb.CloseInput(true);

                using Transaction sourceTr = sourceDb.TransactionManager.StartTransaction();
                TextStyleTable sourceTextStyles = (TextStyleTable)sourceTr.GetObject(sourceDb.TextStyleTableId, OpenMode.ForRead);
                if (!sourceTextStyles.Has(styleName))
                {
                    ed.WriteMessage($"\nRoad labels: text style '{styleName}' was not found in '{SettingsTemplatePath}'.");
                    return false;
                }

                ObjectIdCollection ids = new ObjectIdCollection
                {
                    sourceTextStyles[styleName]
                };

                IdMapping mapping = new IdMapping();
                sourceDb.WblockCloneObjects(ids, targetDb.TextStyleTableId, mapping, DuplicateRecordCloning.Ignore, false);
                sourceTr.Commit();
                ed.WriteMessage($"\nRoad labels: imported text style '{styleName}' from settings template.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nRoad labels: unable to import text style '{styleName}' from settings template -> {ex.Message}");
                return false;
            }

            return HasTextStyle(targetDb, styleName);
        }

        private static bool HasTextStyle(Database db, string styleName)
        {
            using Transaction tr = db.TransactionManager.StartTransaction();
            TextStyleTable tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            bool hasStyle = tst.Has(styleName);
            tr.Commit();
            return hasStyle;
        }

        private static ObjectId GetTextStyleId(Database db, string styleName)
        {
            using Transaction tr = db.TransactionManager.StartTransaction();
            TextStyleTable tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            ObjectId id = tst.Has(styleName) ? tst[styleName] : ObjectId.Null;
            tr.Commit();
            return id;
        }

        private static double GetTextStyleHeight(Database db, ObjectId textStyleId)
        {
            if (textStyleId.IsNull)
                return 0.0;

            using Transaction tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(textStyleId, OpenMode.ForRead, false) is not TextStyleTableRecord textStyle)
            {
                tr.Commit();
                return 0.0;
            }

            double height = textStyle.TextSize;
            tr.Commit();
            return height;
        }

        private static ObjectId PromptForBoundaryPolyline(Document doc)
        {
            return InvokeStatic<ObjectId>(typeof(GisImportCommands), "PromptForBoundaryPolyline", doc);
        }

        private static ObjectId PromptForTemporaryBoundaryPolygon(Document doc)
        {
            return InvokeStatic<ObjectId>(typeof(GisImportCommands), "PromptForTemporaryBoundaryPolygon", doc);
        }

        private static HashSet<string> SnapshotModelSpaceHandles(Database db)
        {
            HashSet<string> handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using Transaction tr = db.TransactionManager.StartTransaction();
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                if (!id.IsValid || id.IsErased)
                    continue;

                handles.Add(id.Handle.ToString());
            }

            tr.Commit();
            return handles;
        }

        private static bool TryGetEntityExtents(Database db, ObjectId id, out Extents3d extents)
        {
            extents = default;

            using Transaction tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity ent)
            {
                tr.Commit();
                return false;
            }

            try
            {
                extents = ent.GeometricExtents;
                tr.Commit();
                return true;
            }
            catch
            {
                tr.Commit();
                return false;
            }
        }

        private static Extents3d ExpandExtents(Extents3d extents, double factor)
        {
            Point3d min = extents.MinPoint;
            Point3d max = extents.MaxPoint;
            double dx = Math.Max(Math.Abs(max.X - min.X) * factor, 5.0);
            double dy = Math.Max(Math.Abs(max.Y - min.Y) * factor, 5.0);
            return new Extents3d(
                new Point3d(min.X - dx, min.Y - dy, min.Z),
                new Point3d(max.X + dx, max.Y + dy, max.Z));
        }

        private static void ZoomWindow(Editor ed, Extents3d extents)
        {
            Point3d min = extents.MinPoint;
            Point3d max = extents.MaxPoint;

            ViewTableRecord view = ed.GetCurrentView();
            double width = Math.Max(Math.Abs(max.X - min.X), 10.0);
            double height = Math.Max(Math.Abs(max.Y - min.Y), 10.0);
            double aspect = view.Height > Tolerance.Global.EqualPoint ? view.Width / view.Height : 1.0;
            if (aspect < 1e-6)
                aspect = 1.0;

            double requiredHeightFromWidth = width / aspect;
            if (requiredHeightFromWidth > height)
                height = requiredHeightFromWidth;
            else
                width = height * aspect;

            Point3d center = new Point3d((min.X + max.X) * 0.5, (min.Y + max.Y) * 0.5, 0.0);
            Matrix3d wcsToUcs = ed.CurrentUserCoordinateSystem.Inverse();
            Point3d centerUcs = center.TransformBy(wcsToUcs);

            view.CenterPoint = new Point2d(centerUcs.X, centerUcs.Y);
            view.Width = width;
            view.Height = height;
            ed.SetCurrentView(view);
        }

        private static void TryEraseEntity(Database db, ObjectId id)
        {
            if (id.IsNull || !id.IsValid || id.IsErased)
                return;

            try
            {
                using Transaction tr = db.TransactionManager.StartTransaction();
                if (tr.GetObject(id, OpenMode.ForWrite, false) is Entity ent && !ent.IsErased)
                    ent.Erase();
                tr.Commit();
            }
            catch
            {
                // best effort cleanup only
            }
        }

        private static void RemoveExistingRoadLayers(AcMapMap map)
        {
            MgLayerCollection layers = map.GetLayers();
            for (int i = layers.GetCount() - 1; i >= 0; i--)
            {
                MgLayerBase layer = layers.GetItem(i);
                if (IsRoadLayerName(layer.GetName()) || IsRoadLayerName(layer.GetLegendLabel()))
                    layers.RemoveAt(i);
            }
        }

        private static bool IsRoadLayerName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            return RoadMapLayerNameHints.Any(hint => name.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void TryRemoveRoadConnections(Editor ed)
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
                    ed.WriteMessage($"\nRoad labels: removed {removed} temporary street-centerline data connection(s) from the current map session.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nRoad labels: connection cleanup skipped -> {ex.Message}");
            }
        }

        private static int RemoveNamedConnectionsFromObject(object root)
        {
            int removed = 0;

            foreach (object item in EnumerateUnknown(root).ToList())
            {
                if (!IsRoadConnectionLike(item))
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

        private static bool IsRoadConnectionLike(object item)
        {
            foreach (string memberName in new[] { "Name", "ConnectionName", "FeatureSource", "ResourceId", "DisplayName" })
            {
                string? value = TryGetMemberString(item, memberName);
                if (string.IsNullOrWhiteSpace(value))
                    continue;

                if (RoadMapLayerNameHints.Any(hint => value.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0))
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

        private static IEnumerable<object> EnumerateUnknown(object source)
        {
            if (source is string)
                yield break;

            if (source is IEnumerable enumerable)
            {
                foreach (object? item in enumerable)
                {
                    if (item != null)
                        yield return item;
                }
                yield break;
            }

            MethodInfo? getEnumerator = source.GetType().GetMethod("GetEnumerator", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (getEnumerator == null)
                yield break;

            object? enumerator = getEnumerator.Invoke(source, null);
            if (enumerator == null)
                yield break;

            PropertyInfo? currentProp = enumerator.GetType().GetProperty("Current", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo? moveNext = enumerator.GetType().GetMethod("MoveNext", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (currentProp == null || moveNext == null)
                yield break;

            while (true)
            {
                object? hasNextObj = moveNext.Invoke(enumerator, null);
                if (hasNextObj is not bool hasNext || !hasNext)
                    break;

                object? current = currentProp.GetValue(enumerator);
                if (current != null)
                    yield return current;
            }
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
                        Type parameterType = parameters[0].ParameterType;
                        if (!parameterType.IsInstanceOfType(item))
                            continue;

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

        private static string NormalizeCommandName(string command)
        {
            if (string.IsNullOrWhiteSpace(command))
                return string.Empty;

            string normalized = command.Trim();
            while (normalized.Length > 0 && (normalized[0] == '.' || normalized[0] == '_'))
                normalized = normalized.Substring(1);

            int firstSpace = normalized.IndexOf(' ');
            if (firstSpace >= 0)
                normalized = normalized.Substring(0, firstSpace);

            return normalized.ToUpperInvariant();
        }

        private static void InvokeStatic(Type type, string methodName, params object[] args)
        {
            _ = InvokeStatic(type, methodName, args, returnNullWhenMissing: false);
        }

        private static T InvokeStatic<T>(Type type, string methodName, params object[] args)
        {
            object? value = InvokeStatic(type, methodName, args, returnNullWhenMissing: false);
            if (value is T typed)
                return typed;

            return (T)Convert.ChangeType(value!, typeof(T), CultureInfo.InvariantCulture);
        }

        private static object? InvokeStatic(Type type, string methodName, object[] args, bool returnNullWhenMissing)
        {
            MethodInfo? method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
            {
                if (returnNullWhenMissing)
                    return null;

                throw new MissingMethodException(type.FullName, methodName);
            }

            try
            {
                return method.Invoke(null, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }
    }
}
