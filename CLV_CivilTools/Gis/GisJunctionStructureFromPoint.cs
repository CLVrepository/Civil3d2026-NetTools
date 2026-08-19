using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CLV_CivilTools.Shared;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDocument = Autodesk.AutoCAD.ApplicationServices.Document;
using AcPolyline = Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace CLV_CivilTools.Gis
{
    /// <summary>
    /// Converts manually copied junction-structure review linework to GIS layers and transfers OD.
    /// User selects only the imported Structures point. The routine finds the outer/inner closed polylines
    /// around that point automatically, moves them to the GIS structure layers, then copies OD from the point
    /// to the outer polyline using the ADE/LISP helper.
    /// </summary>
    public static class GisJunctionStructureFromPoint
    {
        private const string OdHelperPath = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\CLV_GIS_OD_HELPERS.lsp";
        private const string StructuresPointLayer = "Structures";
        private const string TargetInnerLayer = "C-STRM-STRC-INNR";
        private const string TargetOuterLayer = "C-STRM-STRC-E";
        private const double MaxSearchRadius = 25.0;
        private const double ExtentsTolerance = 1.0;

        [CommandMethod("CLV-GIS-JS-FROM-POINT", CommandFlags.Modal)]
        public static void ConvertJunctionStructureFromPoint()
        {
            AcDocument? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                LayerStandards.EnsureGisLayers(db, ed);

                PromptEntityOptions peo = new PromptEntityOptions("\nSELECT JUNCTION STRUCTURE POINT: ");
                peo.SetRejectMessage("\nSelect the imported GIS structure point.");
                peo.AddAllowedClass(typeof(DBPoint), exactMatch: false);
                peo.AddAllowedClass(typeof(BlockReference), exactMatch: false);

                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                    return;

                string sourcePointHandle = string.Empty;
                string outerHandle = string.Empty;
                string innerHandle = string.Empty;
                double distanceOuter = -1.0;
                double distanceInner = -1.0;
                int candidateCount = 0;

                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    if (tr.GetObject(per.ObjectId, OpenMode.ForRead, false) is not Entity pointEnt || pointEnt.IsErased)
                    {
                        ed.WriteMessage("\nCLV-GIS-JS-FROM-POINT: unable to open selected point.");
                        return;
                    }

                    if (!string.Equals(pointEnt.Layer, StructuresPointLayer, StringComparison.OrdinalIgnoreCase))
                    {
                        ed.WriteMessage($"\nCLV-GIS-JS-FROM-POINT: selected entity is on layer '{pointEnt.Layer}', expected '{StructuresPointLayer}'.");
                        return;
                    }

                    Point3d? maybePoint = TryGetEntityPoint(pointEnt);
                    if (!maybePoint.HasValue)
                    {
                        ed.WriteMessage("\nCLV-GIS-JS-FROM-POINT: selected entity does not provide a point location.");
                        return;
                    }

                    Point3d center = maybePoint.Value;
                    sourcePointHandle = pointEnt.Handle.ToString();

                    EnsureLayer(db, tr, TargetInnerLayer);
                    EnsureLayer(db, tr, TargetOuterLayer);

                    List<PolylineCandidate> candidates = FindClosedPolylineCandidates(tr, db, center, MaxSearchRadius);
                    candidateCount = candidates.Count;

                    PolylineCandidate? outer = ChooseOuterCandidate(candidates, center);
                    PolylineCandidate? inner = ChooseInnerCandidate(candidates, outer, center);

                    if (outer != null && tr.GetObject(outer.Id, OpenMode.ForWrite, false) is Entity outerEnt)
                    {
                        outerEnt.Layer = TargetOuterLayer;
                        outerHandle = outerEnt.Handle.ToString();
                        distanceOuter = Distance2d(center, outer.Center);
                    }

                    if (inner != null && tr.GetObject(inner.Id, OpenMode.ForWrite, false) is Entity innerEnt)
                    {
                        innerEnt.Layer = TargetInnerLayer;
                        innerHandle = innerEnt.Handle.ToString();
                        distanceInner = Distance2d(center, inner.Center);
                    }

                    tr.Commit();
                }

                bool odQueued = false;
                if (!string.IsNullOrWhiteSpace(sourcePointHandle) && !string.IsNullOrWhiteSpace(outerHandle))
                    odQueued = QueueCopyObjectDataViaLisp(sourcePointHandle, outerHandle, ed);

                int xDataCleaned = GisXDataCleanup.CleanCurrentDrawingEntityXData(doc, preserveClvCache: true);

                ed.WriteMessage(
                    $"\nCLV-GIS-JS-FROM-POINT complete. candidates={candidateCount}, outer={(string.IsNullOrWhiteSpace(outerHandle) ? "not found" : outerHandle)}, inner={(string.IsNullOrWhiteSpace(innerHandle) ? "not found" : innerHandle)}, outerDistance={(distanceOuter < 0 ? "n/a" : distanceOuter.ToString("0.###"))}, innerDistance={(distanceInner < 0 ? "n/a" : distanceInner.ToString("0.###"))}, odCopyQueued={(odQueued ? "yes" : "no")}, xDataCleaned={xDataCleaned}."
                );
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-JS-FROM-POINT failed: {ex.Message}");
            }
        }

        private static void EnsureLayer(Database db, Transaction tr, string layerName)
        {
            Editor? ed = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            if (ed != null && LayerStandards.TryEnsureManagedGisLayer(db, tr, ed, layerName))
                return;

            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(layerName))
                return;

            lt.UpgradeOpen();
            LayerTableRecord ltr = new LayerTableRecord { Name = layerName };
            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }

        private static Point3d? TryGetEntityPoint(Entity ent)
        {
            if (ent is DBPoint dbPoint)
                return dbPoint.Position;

            if (ent is BlockReference br)
                return br.Position;

            return null;
        }

        private static List<PolylineCandidate> FindClosedPolylineCandidates(Transaction tr, Database db, Point3d center, double maxRadius)
        {
            List<PolylineCandidate> results = new List<PolylineCandidate>();
            if (tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead) is not BlockTableRecord space)
                return results;

            foreach (ObjectId id in space)
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is not AcPolyline pl || pl.IsErased || !pl.Closed)
                    continue;

                if (pl.Area <= 0.0)
                    continue;

                if (!CouldBelongToStructure(pl))
                    continue;

                Point3d plCenter = GetPolylineCenter(pl);
                double centerDist = Distance2d(center, plCenter);
                if (centerDist > maxRadius)
                    continue;

                Extents3d ext;
                try
                {
                    ext = pl.GeometricExtents;
                }
                catch
                {
                    continue;
                }

                if (!PointWithinExtents(center, ext, ExtentsTolerance))
                    continue;

                bool contains = false;
                try
                {
                    contains = IsPointInsideClosedPolyline(pl, center);
                }
                catch
                {
                    contains = false;
                }

                if (!contains)
                    continue;

                results.Add(new PolylineCandidate(id, pl.Handle.ToString(), Math.Abs(pl.Area), plCenter, ext));
            }

            return results;
        }

        private static bool CouldBelongToStructure(AcPolyline pl)
        {
            string layer = pl.Layer ?? string.Empty;
            if (string.Equals(layer, StructuresPointLayer, StringComparison.OrdinalIgnoreCase))
                return false;

            if (layer.IndexOf("CURB", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            if (string.Equals(layer, "C-DETL-MARK", StringComparison.OrdinalIgnoreCase))
                return false;

            return true;
        }

        private static PolylineCandidate? ChooseOuterCandidate(List<PolylineCandidate> candidates, Point3d center)
        {
            if (candidates.Count == 0)
                return null;

            return candidates
                .OrderByDescending(c => c.Area)
                .ThenBy(c => Distance2d(center, c.Center))
                .FirstOrDefault();
        }

        private static PolylineCandidate? ChooseInnerCandidate(List<PolylineCandidate> candidates, PolylineCandidate? outer, Point3d center)
        {
            if (outer == null)
                return null;

            return candidates
                .Where(c => c.Id != outer.Id && c.Area < outer.Area && ExtentsContainedWithin(c.Extents, outer.Extents, 0.5))
                .OrderByDescending(c => c.Area)
                .ThenBy(c => Distance2d(center, c.Center))
                .FirstOrDefault();
        }

        private static bool PointWithinExtents(Point3d point, Extents3d ext, double tol)
        {
            return point.X >= ext.MinPoint.X - tol &&
                   point.X <= ext.MaxPoint.X + tol &&
                   point.Y >= ext.MinPoint.Y - tol &&
                   point.Y <= ext.MaxPoint.Y + tol;
        }

        private static bool ExtentsContainedWithin(Extents3d inner, Extents3d outer, double tol)
        {
            return inner.MinPoint.X >= outer.MinPoint.X - tol &&
                   inner.MaxPoint.X <= outer.MaxPoint.X + tol &&
                   inner.MinPoint.Y >= outer.MinPoint.Y - tol &&
                   inner.MaxPoint.Y <= outer.MaxPoint.Y + tol;
        }

        private static Point3d GetPolylineCenter(AcPolyline pl)
        {
            Extents3d ext = pl.GeometricExtents;
            return new Point3d(
                (ext.MinPoint.X + ext.MaxPoint.X) * 0.5,
                (ext.MinPoint.Y + ext.MaxPoint.Y) * 0.5,
                0.0);
        }

        private static bool IsPointInsideClosedPolyline(AcPolyline pl, Point3d point)
        {
            var vertices = new List<Point2d>();
            int count = pl.NumberOfVertices;
            for (int i = 0; i < count; i++)
            {
                Point2d pt = pl.GetPoint2dAt(i);
                vertices.Add(pt);
            }

            if (vertices.Count < 3)
                return false;

            bool inside = false;
            double x = point.X;
            double y = point.Y;
            int j = vertices.Count - 1;

            for (int i = 0; i < vertices.Count; i++)
            {
                double xi = vertices[i].X;
                double yi = vertices[i].Y;
                double xj = vertices[j].X;
                double yj = vertices[j].Y;

                bool intersect = ((yi > y) != (yj > y)) &&
                                 (x < ((xj - xi) * (y - yi) / ((yj - yi) == 0.0 ? 1e-12 : (yj - yi)) + xi));
                if (intersect)
                    inside = !inside;

                j = i;
            }

            return inside;
        }

        private static double Distance2d(Point3d a, Point3d b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        private static bool QueueCopyObjectDataViaLisp(string sourceHandle, string destHandle, Editor ed)
        {
            if (string.IsNullOrWhiteSpace(sourceHandle) || string.IsNullOrWhiteSpace(destHandle) || !File.Exists(OdHelperPath))
                return false;

            try
            {
                AcDocument? doc = AcadApp.DocumentManager.MdiActiveDocument;
                if (doc == null)
                    return false;

                string escapedPath = OdHelperPath.Replace("\\", "\\\\").Replace("\"", "\\\"");
                string escapedSource = sourceHandle.Replace("\"", "\\\"");
                string escapedDest = destHandle.Replace("\"", "\\\"");
                string expr = $"(progn (vl-load-com) (load \"{escapedPath}\") (CLV-GIS-OD-COPY-HANDLES \"{escapedSource}\" \"{escapedDest}\") (princ)) ";
                doc.SendStringToExecute(expr, true, false, false);
                return true;
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-JS-FROM-POINT OD helper queue failed: {ex.Message}");
                return false;
            }
        }

        private sealed class PolylineCandidate
        {
            public PolylineCandidate(ObjectId id, string handle, double area, Point3d center, Extents3d extents)
            {
                Id = id;
                Handle = handle;
                Area = area;
                Center = center;
                Extents = extents;
            }

            public ObjectId Id { get; }
            public string Handle { get; }
            public double Area { get; }
            public Point3d Center { get; }
            public Extents3d Extents { get; }
        }
    }
}
