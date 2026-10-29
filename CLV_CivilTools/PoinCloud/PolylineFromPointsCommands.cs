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
    public static class PolylineFromPointsCommands
    {
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
                var entityOptions = new PromptEntityOptions("\nSelect traced 2D polyline (straight segments): ");
                entityOptions.SetRejectMessage("\nSelect a lightweight 2D polyline, not a 3D polyline, cloud or block.");
                entityOptions.AddAllowedClass(typeof(Polyline), true);
                PromptEntityResult entityResult = ed.GetEntity(entityOptions);
                if (entityResult.Status != PromptStatus.OK) return;

                PromptDoubleResult toleranceResult = ed.GetDouble(new PromptDoubleOptions(
                    "\nXY point-to-vertex tolerance in drawing units <0.001>: ")
                {
                    DefaultValue = 0.001, UseDefaultValue = true, AllowZero = false, AllowNegative = false
                });
                if (toleranceResult.Status != PromptStatus.OK) return;
                double tolerance = toleranceResult.Value;
                if (!double.IsFinite(tolerance) || tolerance <= 0)
                    throw new ArgumentException("Tolerance must be finite and positive.");

                var pointOptions = new PromptSelectionOptions
                {
                    MessageForAdding = "\nSelect elevation POINT objects and/or COGO points for this line: ",
                    AllowDuplicates = false
                };
                var filter = new SelectionFilter(new[]
                {
                    new TypedValue((int)DxfCode.Operator, "<OR"),
                    new TypedValue((int)DxfCode.Start, "POINT"),
                    new TypedValue((int)DxfCode.Start, "AECC_COGO_POINT"),
                    new TypedValue((int)DxfCode.Operator, "OR>")
                });
                PromptSelectionResult pointResult = ed.GetSelection(pointOptions, filter);
                if (pointResult.Status != PromptStatus.OK) return;

                var keepOptions = new PromptKeywordOptions("\nOriginal 2D polyline [Keep/Replace] <Keep>: ");
                keepOptions.Keywords.Add("Keep"); keepOptions.Keywords.Add("Replace");
                keepOptions.Keywords.Default = "Keep"; keepOptions.AllowNone = true;
                PromptResult keepResult = ed.GetKeywords(keepOptions);
                if (keepResult.Status != PromptStatus.OK && keepResult.Status != PromptStatus.None) return;
                bool replace = string.Equals(keepResult.StringResult, "Replace", StringComparison.OrdinalIgnoreCase);

                using Transaction tr = db.TransactionManager.StartTransaction();
                var source = (Polyline)tr.GetObject(entityResult.ObjectId, OpenMode.ForRead);
                ObjectId modelId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                if (source.OwnerId != modelId)
                    throw new InvalidOperationException("Select a direct model-space polyline, outside blocks and xrefs.");
                if (replace)
                {
                    using ResultBuffer? jobData = source.GetXDataForApplication("CLV_CLOUD_GRID_V1");
                    TypedValue[] values = jobData?.AsArray() ?? Array.Empty<TypedValue>();
                    if (values.Length >= 2 && values[1].Value is string kind && kind == "JOB")
                        throw new InvalidOperationException("Keep the tagged grid-job boundary. Choose Keep or use a separate breakline trace.");
                }
                var sourceLayer = (LayerTableRecord)tr.GetObject(source.LayerId, OpenMode.ForRead);
                if (sourceLayer.IsLocked || sourceLayer.IsOff || sourceLayer.IsFrozen || !source.Visible)
                    throw new InvalidOperationException("The source polyline must be visible on an on, thawed, unlocked layer.");
                int count = source.NumberOfVertices;
                if (count < (source.Closed ? 3 : 2))
                    throw new InvalidOperationException("The trace needs at least two open or three closed vertices.");
                var vertices = new Point3d[count];
                bool widths = source.Thickness != 0;
                for (int i = 0; i < count; i++)
                {
                    vertices[i] = source.GetPoint3dAt(i); // WCS, including a non-world source OCS.
                    if (!double.IsFinite(vertices[i].X) || !double.IsFinite(vertices[i].Y) || !double.IsFinite(vertices[i].Z))
                        throw new InvalidOperationException("The source contains non-finite coordinates.");
                    if (i < count - 1 || source.Closed)
                    {
                        if (source.GetBulgeAt(i) != 0)
                            throw new InvalidOperationException("Arc segments cannot be retained by a 3D polyline."
                                + " Use a straight-segment trace with elevation points at its vertices; no arcs were chorded.");
                        widths |= source.GetStartWidthAt(i) != 0 || source.GetEndWidthAt(i) != 0;
                    }
                }
                for (int i = 0; i < (source.Closed ? count : count - 1); i++)
                {
                    Point3d a = vertices[i], b = vertices[(i + 1) % count];
                    if (a.X == b.X && a.Y == b.Y)
                        throw new InvalidOperationException("Consecutive vertices have identical WCS XY. Remove the duplicate before conversion.");
                }

                var samples = new List<VertexElevationMatcher.Sample>();
                int skipped = 0;
                foreach (ObjectId id in pointResult.Value.GetObjectIds())
                {
                    if (tr.GetObject(id, OpenMode.ForRead) is not Entity point || point.OwnerId != modelId)
                    { skipped++; continue; }
                    var layer = (LayerTableRecord)tr.GetObject(point.LayerId, OpenMode.ForRead);
                    if (!point.Visible || layer.IsOff || layer.IsFrozen || IsExcluded(point))
                    { skipped++; continue; }
                    Point3d position;
                    string label;
                    if (point is DBPoint native)
                    { position = native.Position; label = "POINT handle " + point.Handle; }
                    else if (point is CogoPoint cogo)
                    {
                        // The documented unset elevation value is NaN.
                        if (!double.IsFinite(cogo.Elevation)) { skipped++; continue; }
                        position = new Point3d(cogo.Easting, cogo.Northing, cogo.Elevation);
                        label = "COGO " + cogo.PointNumber + " handle " + point.Handle;
                    }
                    else { skipped++; continue; }
                    if (!double.IsFinite(position.X) || !double.IsFinite(position.Y) || !double.IsFinite(position.Z))
                    { skipped++; continue; }
                    samples.Add(new VertexElevationMatcher.Sample(samples.Count, label, position.X, position.Y, position.Z));
                }
                if (samples.Count == 0) throw new InvalidOperationException("No eligible elevation points were selected.");
                if (skipped != 0) ed.WriteMessage($"\nSkipped {skipped} hidden, excluded, unsupported or unset-elevation points.");

                VertexElevationMatcher.Match[] matches = VertexElevationMatcher.MatchVertices(
                    vertices.Select(v => new VertexElevationMatcher.XY(v.X, v.Y)).ToArray(), samples, tolerance);
                var failures = matches.Where(m => m.Point == null).ToArray();
                if (failures.Length != 0)
                {
                    foreach (var failure in failures.Take(20))
                    {
                        Point3d vertex = vertices[failure.Vertex];
                        ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                            "\nVertex {0} E={1:F6} N={2:F6}: {3}", failure.Vertex + 1, vertex.X, vertex.Y, failure.Issue));
                    }
                    if (failures.Length > 20) ed.WriteMessage($"\n... {failures.Length - 20} further vertex issues.");
                    ed.WriteMessage($"\nNo conversion: {failures.Length} of {count} vertices need attention."
                        + " Place/correct their points, select the intended point set or reduce an overly broad tolerance."
                        + " No objects were changed; no elevations were interpolated.");
                    return;
                }

                double minZ = matches.Min(m => m.Point!.Z), maxZ = matches.Max(m => m.Point!.Z);
                ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                    "\nMatched all {0} vertices. Z range {1:F3} to {2:F3}. {3} original 2D line.",
                    count, minZ, maxZ, replace ? "Replace" : "Keep"));
                if (widths) ed.WriteMessage("\nThe 3D result is a centerline without 2D width/thickness.");
                var confirm = new PromptKeywordOptions("\nCreate the 3D polyline [Yes/No] <No>: ");
                confirm.Keywords.Add("Yes"); confirm.Keywords.Add("No");
                confirm.Keywords.Default = "No"; confirm.AllowNone = true;
                PromptResult confirmation = ed.GetKeywords(confirm);
                if (confirmation.Status != PromptStatus.OK || !string.Equals(confirmation.StringResult, "Yes", StringComparison.OrdinalIgnoreCase)) return;

                var positions = new Point3dCollection();
                for (int i = 0; i < count; i++)
                    positions.Add(new Point3d(vertices[i].X, vertices[i].Y, matches[i].Point!.Z));
                using var output = new Polyline3d(Poly3dType.SimplePoly, positions, source.Closed);
                output.SetDatabaseDefaults(db);
                output.SetPropertiesFrom(source);
                var model = (BlockTableRecord)tr.GetObject(modelId, OpenMode.ForWrite);
                model.AppendEntity(output); tr.AddNewlyCreatedDBObject(output, true);
                int readback = 0;
                foreach (ObjectId id in output)
                {
                    Point3d actual = ((PolylineVertex3d)tr.GetObject(id, OpenMode.ForRead)).Position;
                    if (readback >= count || actual.X != positions[readback].X || actual.Y != positions[readback].Y
                        || actual.Z != positions[readback].Z)
                        throw new InvalidOperationException("3D vertex verification failed; conversion was rolled back.");
                    readback++;
                }
                if (readback != count || output.Closed != source.Closed)
                    throw new InvalidOperationException("3D vertex count/closure verification failed; conversion was rolled back.");
                if (replace) { source.UpgradeOpen(); source.Erase(); }
                ObjectId outputId = output.ObjectId;
                tr.Commit();
                ed.SetImpliedSelection(new[] { outputId });
                ed.Regen();
                ed.WriteMessage($"\nCreated 3D polyline {outputId.Handle} with {count} measured elevations."
                    + (replace ? " Original 2D line replaced." : " Original 2D line retained.")
                    + " Source points were not changed. Add the 3D line to the final surface as a breakline when ready.");
            }
            catch (Autodesk.AutoCAD.Runtime.Exception e) when (e.ErrorStatus == ErrorStatus.UserBreak) { }
            catch (System.Exception e) { ed.WriteMessage("\n3D FROM POINTS: " + e.Message); }
        }

        private static bool IsExcluded(Entity point)
        {
            using ResultBuffer? data = point.GetXDataForApplication("CLV_CLOUD_GRID_V1");
            TypedValue[] values = data?.AsArray() ?? Array.Empty<TypedValue>();
            return values.Length >= 7 && values[1].Value is string kind && kind == "POINT"
                && values[6].Value is string status && status == "Excluded";
        }
    }
}
