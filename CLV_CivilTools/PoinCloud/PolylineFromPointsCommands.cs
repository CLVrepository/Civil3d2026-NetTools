using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CogoPoint = Autodesk.Civil.DatabaseServices.CogoPoint;

namespace CLV_CivilTools.PointCloud
{
    public static partial class PolylineFromPointsCommands
    {
        private sealed record Conversion(Polyline Source, Point3d[] Vertices,
            VertexElevationMatcher.Match[] Matches, bool HasWidths);

        [CommandMethod("PCT3DFROMPOINTS", CommandFlags.Modal)]
        [CommandMethod("PL3DFROMPOINTS", CommandFlags.Modal)]
        public static void Create()
        {
            Document? d = AcadApp.DocumentManager.MdiActiveDocument;
            if (d == null) return;
            Editor ed = d.Editor; Database db = d.Database;
            try
            {
                if (!db.TileMode) throw new InvalidOperationException("Use the Model tab.");
                var options = new PromptSelectionOptions
                {
                    MessageForAdding = "\nSelect traced 2D polylines; elevation points will be found automatically: ",
                    AllowDuplicates = false
                };
                var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LWPOLYLINE") });
                PromptSelectionResult selected = ed.GetSelection(options, filter);
                if (selected.Status != PromptStatus.OK) return;

                PromptDoubleResult toleranceResult = ed.GetDouble(new PromptDoubleOptions(
                    "\nFallback XY matching tolerance in drawing units <0.001>: ")
                {
                    DefaultValue = 0.001, UseDefaultValue = true, AllowZero = false, AllowNegative = false
                });
                if (toleranceResult.Status != PromptStatus.OK) return;
                double tolerance = toleranceResult.Value;
                if (!double.IsFinite(tolerance) || tolerance <= 0 || !double.IsFinite(tolerance * tolerance)
                    || tolerance * tolerance == 0)
                    throw new ArgumentException("Tolerance must be finite, positive and within numeric range.");

                var keepOptions = new PromptKeywordOptions("\nOriginal 2D polylines [Keep/Replace] <Keep>: ");
                keepOptions.Keywords.Add("Keep"); keepOptions.Keywords.Add("Replace");
                keepOptions.Keywords.Default = "Keep"; keepOptions.AllowNone = true;
                PromptResult keepResult = ed.GetKeywords(keepOptions);
                if (keepResult.Status != PromptStatus.OK && keepResult.Status != PromptStatus.None) return;
                bool replace = string.Equals(keepResult.StringResult, "Replace", StringComparison.OrdinalIgnoreCase);

                LayerChoices previousLayers = ReadLayerChoices(db);
                string? outputLayerName = PromptLayerName(ed, "3D DTM breakline layer", previousLayers.Output);
                if (outputLayerName == null) return;
                string retainedLayerName = previousLayers.Retained;
                if (!replace)
                {
                    string? pickedLayer = PromptLayerName(ed, "Layer for kept 2D traces", previousLayers.Retained);
                    if (pickedLayer == null) return;
                    retainedLayerName = pickedLayer;
                    if (string.Equals(outputLayerName, retainedLayerName, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Choose different layers for 3D breaklines and kept 2D traces.");
                }

                using Transaction tr = db.TransactionManager.StartTransaction();
                ObjectId modelId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                ObjectId outputLayerId = RequireExistingDestination(db, tr, outputLayerName);
                bool createRetainedLayer = ValidateRetainedDestination(db, tr, retainedLayerName, replace);
                var sources = new List<(Polyline Source, Point3d[] Vertices, bool HasWidths)>();
                foreach (ObjectId id in selected.Value.GetObjectIds())
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Polyline source || source.OwnerId != modelId)
                        throw new InvalidOperationException("Select direct model-space lightweight 2D polylines, outside blocks and xrefs.");
                    try { sources.Add((source, ReadVertices(source, tr, replace, out bool widths), widths)); }
                    catch (System.Exception e)
                    { throw new InvalidOperationException("Polyline " + source.Handle + ": " + e.Message, e); }
                }

                if (sources.Count == 0) throw new InvalidOperationException("No traced polylines selected.");
                List<VertexElevationMatcher.Sample> samples = ReadPoints(tr, modelId, out int skipped);
                ed.WriteMessage($"\nAutomatically found {samples.Count} eligible model-space POINT/COGO points."
                    + $" Skipped {skipped} hidden, excluded or invalid points. Exact XY matches take priority.");
                if (samples.Count == 0) throw new InvalidOperationException("No eligible elevation points were found. No objects were changed.");

                var conversions = new List<Conversion>();
                int totalIssues = 0, reported = 0, exactCount = 0, fallbackCount = 0;
                foreach (var item in sources)
                {
                    VertexElevationMatcher.Match[] matches = VertexElevationMatcher.MatchVertices(
                        item.Vertices.Select(v => new VertexElevationMatcher.XY(v.X, v.Y)).ToArray(), samples, tolerance);
                    foreach (var match in matches)
                    {
                        Point3d vertex = item.Vertices[match.Vertex];
                        if (match.Point == null)
                        {
                            totalIssues++;
                            if (reported++ < 20)
                                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                                    "\nPolyline {0}, vertex {1}, E={2:F6} N={3:F6}: {4}",
                                    item.Source.Handle, match.Vertex + 1, vertex.X, vertex.Y, match.Issue));
                        }
                        else if (match.Point.X == vertex.X && match.Point.Y == vertex.Y) exactCount++;
                        else fallbackCount++;
                    }
                    conversions.Add(new Conversion(item.Source, item.Vertices, matches, item.HasWidths));
                }
                if (totalIssues != 0)
                {
                    if (totalIssues > 20) ed.WriteMessage($"\n... {totalIssues - 20} further vertex issues.");
                    ed.WriteMessage($"\nNo conversion: {totalIssues} vertices need attention."
                        + " Correct the missing/conflicting points or reduce an overly broad fallback tolerance."
                        + " The entire selection was left unchanged; no elevations were interpolated.");
                    return;
                }

                double minZ = double.PositiveInfinity, maxZ = double.NegativeInfinity;
                foreach (Conversion conversion in conversions)
                    foreach (VertexElevationMatcher.Match match in conversion.Matches)
                    { minZ = Math.Min(minZ, match.Point!.Z); maxZ = Math.Max(maxZ, match.Point.Z); }
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\nReady: {0} polylines, {1} exact and {2} tolerance matches. Z range {3:F3} to {4:F3}. {5} originals.",
                    conversions.Count, exactCount, fallbackCount, minZ, maxZ, replace ? "Replace" : "Keep"));
                if (conversions.Any(c => c.HasWidths)) ed.WriteMessage("\n3D results are centerlines without 2D width/thickness.");
                ed.WriteMessage("\n3D destination: " + outputLayerName
                    + (replace ? "; original 2D traces will be erased." : "; kept 2D destination: " + retainedLayerName + "."));
                if (createRetainedLayer) ed.WriteMessage("\nThe new kept-2D layer will be gray and nonplotting.");
                var confirm = new PromptKeywordOptions("\nCreate all 3D polylines [Yes/No] <No>: ");
                confirm.Keywords.Add("Yes"); confirm.Keywords.Add("No");
                confirm.Keywords.Default = "No"; confirm.AllowNone = true;
                PromptResult confirmation = ed.GetKeywords(confirm);
                if (confirmation.Status != PromptStatus.OK || !string.Equals(confirmation.StringResult, "Yes", StringComparison.OrdinalIgnoreCase)) return;

                ObjectId retainedLayerId = replace ? ObjectId.Null : GetRetainedDestination(db, tr, retainedLayerName);
                if (!replace && retainedLayerId == outputLayerId)
                    throw new InvalidOperationException("3D breaklines and kept 2D traces must use different layers.");
                var model = (BlockTableRecord)tr.GetObject(modelId, OpenMode.ForWrite);
                var outputs = new List<ObjectId>();
                foreach (Conversion conversion in conversions)
                {
                    var positions = new Point3dCollection();
                    for (int i = 0; i < conversion.Vertices.Length; i++)
                        positions.Add(new Point3d(conversion.Vertices[i].X, conversion.Vertices[i].Y, conversion.Matches[i].Point!.Z));
                    using var output = new Polyline3d(Poly3dType.SimplePoly, positions, conversion.Source.Closed);
                    output.SetDatabaseDefaults(db); output.SetPropertiesFrom(conversion.Source);
                    model.AppendEntity(output); tr.AddNewlyCreatedDBObject(output, true);
                    ApplyBreaklineLayer(output, outputLayerId);
                    int readback = 0;
                    foreach (ObjectId id in output)
                    {
                        var vertex = (PolylineVertex3d)tr.GetObject(id, OpenMode.ForWrite);
                        vertex.LayerId = outputLayerId;
                        Point3d actual = vertex.Position;
                        if (readback >= positions.Count || actual.X != positions[readback].X || actual.Y != positions[readback].Y
                            || actual.Z != positions[readback].Z || vertex.LayerId != outputLayerId)
                            throw new InvalidOperationException("3D vertex verification failed; the whole conversion was rolled back.");
                        readback++;
                    }
                    if (readback != positions.Count || output.Closed != conversion.Source.Closed || output.LayerId != outputLayerId)
                        throw new InvalidOperationException("3D count/closure verification failed; the whole conversion was rolled back.");
                    outputs.Add(output.ObjectId);
                }
                if (replace)
                    foreach (Conversion conversion in conversions) { conversion.Source.UpgradeOpen(); conversion.Source.Erase(); }
                else
                    foreach (Conversion conversion in conversions)
                    {
                        conversion.Source.UpgradeOpen();
                        conversion.Source.LayerId = retainedLayerId;
                        if (conversion.Source.LayerId != retainedLayerId)
                            throw new InvalidOperationException("Retained 2D layer verification failed; the whole conversion was rolled back.");
                    }
                SaveLayerChoices(db, tr, new LayerChoices(outputLayerName, retainedLayerName));
                tr.Commit();
                ed.SetImpliedSelection(outputs.ToArray()); ed.Regen();
                ed.WriteMessage($"\nCreated {outputs.Count} 3D polylines from detected vertex points."
                    + " 3D layer: " + outputLayerName + "."
                    + (replace ? " Original 2D lines replaced." : " Original 2D lines retained on " + retainedLayerName + ".")
                    + " Source points were not changed. Add the 3D lines to the final surface as breaklines when ready.");
            }
            catch (Autodesk.AutoCAD.Runtime.Exception e) when (e.ErrorStatus == ErrorStatus.UserBreak) { }
            catch (System.Exception e) { ed.WriteMessage("\n3D FROM POINTS: " + e.Message); }
        }

        private static Point3d[] ReadVertices(Polyline source, Transaction tr, bool replace, out bool widths)
        {
            if (replace)
            {
                using ResultBuffer? jobData = source.GetXDataForApplication("CLV_CLOUD_GRID_V1");
                TypedValue[] values = jobData?.AsArray() ?? Array.Empty<TypedValue>();
                if (values.Length >= 2 && values[1].Value is string kind && kind == "JOB")
                    throw new InvalidOperationException("Keep the tagged grid-job boundary. Choose Keep or use a separate trace.");
            }
            var layer = (LayerTableRecord)tr.GetObject(source.LayerId, OpenMode.ForRead);
            if (layer.IsLocked || layer.IsOff || layer.IsFrozen || !source.Visible)
                throw new InvalidOperationException("The trace must be visible on an on, thawed, unlocked layer.");
            int count = source.NumberOfVertices;
            if (count < (source.Closed ? 3 : 2))
                throw new InvalidOperationException("The trace needs at least two open or three closed vertices.");
            var vertices = new Point3d[count];
            widths = source.Thickness != 0;
            for (int i = 0; i < count; i++)
            {
                vertices[i] = source.GetPoint3dAt(i);
                if (!Finite(vertices[i])) throw new InvalidOperationException("Non-finite source coordinates.");
                if (i < count - 1 || source.Closed)
                {
                    if (source.GetBulgeAt(i) != 0)
                        throw new InvalidOperationException("Arc segments cannot be retained by a 3D polyline. Use a straight-segment trace; no arcs were chorded.");
                    widths |= source.GetStartWidthAt(i) != 0 || source.GetEndWidthAt(i) != 0;
                }
            }
            for (int i = 0; i < (source.Closed ? count : count - 1); i++)
                if (vertices[i].X == vertices[(i + 1) % count].X && vertices[i].Y == vertices[(i + 1) % count].Y)
                    throw new InvalidOperationException("Consecutive vertices have identical WCS XY. Remove the duplicate first.");
            return vertices;
        }

        private static List<VertexElevationMatcher.Sample> ReadPoints(Transaction tr, ObjectId modelId, out int skipped)
        {
            var samples = new List<VertexElevationMatcher.Sample>();
            skipped = 0;
            RXClass pointClass = RXObject.GetClass(typeof(DBPoint)), cogoClass = RXObject.GetClass(typeof(CogoPoint));
            foreach (ObjectId id in (BlockTableRecord)tr.GetObject(modelId, OpenMode.ForRead))
            {
                if (!id.ObjectClass.IsDerivedFrom(pointClass) && !id.ObjectClass.IsDerivedFrom(cogoClass)) continue;
                var point = (Entity)tr.GetObject(id, OpenMode.ForRead);
                var layer = (LayerTableRecord)tr.GetObject(point.LayerId, OpenMode.ForRead);
                if (!point.Visible || layer.IsOff || layer.IsFrozen || IsExcluded(point)) { skipped++; continue; }
                Point3d position;
                string label;
                if (point is DBPoint native) { position = native.Position; label = "POINT handle " + point.Handle; }
                else if (point is CogoPoint cogo)
                {
                    if (!double.IsFinite(cogo.Elevation)) { skipped++; continue; }
                    position = new Point3d(cogo.Easting, cogo.Northing, cogo.Elevation);
                    label = "COGO " + cogo.PointNumber + " handle " + point.Handle;
                }
                else { skipped++; continue; }
                if (!Finite(position)) { skipped++; continue; }
                samples.Add(new VertexElevationMatcher.Sample(samples.Count, label, position.X, position.Y, position.Z));
            }
            return samples;
        }

        private static bool Finite(Point3d p) => double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z);

        private static bool IsExcluded(Entity point)
        {
            using ResultBuffer? data = point.GetXDataForApplication("CLV_CLOUD_GRID_V1");
            TypedValue[] values = data?.AsArray() ?? Array.Empty<TypedValue>();
            return values.Length >= 7 && values[1].Value is string kind && kind == "POINT"
                && values[6].Value is string status && status == "Excluded";
        }
    }
}

