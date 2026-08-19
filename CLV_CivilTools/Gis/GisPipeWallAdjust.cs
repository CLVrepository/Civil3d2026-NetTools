using System;
using System.Collections.Generic;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Gis
{
    public static class GisPipeWallAdjust
    {
        private const string StructureInnerLayer = "C-STRM-STRC-INNR";
        private const double MaxSearchDistance = 25.0;

        [CommandMethod("CLV-GIS-PIPE-TRIM", CommandFlags.Modal)]
        public static void TrimPipeWallToStructure() => AdjustPipeWall("TRIM");

        [CommandMethod("CLV-GIS-PIPE-EXTEND", CommandFlags.Modal)]
        public static void ExtendPipeWallToStructure() => AdjustPipeWall("EXTEND");

        private static void AdjustPipeWall(string mode)
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                PromptEntityOptions peo = new PromptEntityOptions($"\nSELECT PIPE WALL TO {mode}:");
                peo.SetRejectMessage("\nSelect a line or polyline.");
                peo.AddAllowedClass(typeof(Line), exactMatch: false);
                peo.AddAllowedClass(typeof(Polyline), exactMatch: false);
                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                    return;

                using (DocumentLock docLock = doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    Entity? ent = tr.GetObject(per.ObjectId, OpenMode.ForWrite) as Entity;
                    if (ent == null)
                        return;

                    if (!TryGetEditableCurveData(ent, out Point3d startPoint, out Point3d endPoint, out bool isPolyline, out int lastIndex))
                    {
                        ed.WriteMessage($"\nCLV-GIS-PIPE-{mode}: unsupported object.");
                        return;
                    }

                    bool useStart = per.PickedPoint.DistanceTo(startPoint) <= per.PickedPoint.DistanceTo(endPoint);
                    Point3d movingEndpoint = useStart ? startPoint : endPoint;

                    if (!TryFindClosestStructurePoint(db, tr, movingEndpoint, out Point3d targetPoint, out double dist))
                    {
                        ed.WriteMessage($"\nCLV-GIS-PIPE-{mode}: no nearby {StructureInnerLayer} found.");
                        return;
                    }

                    if (dist > MaxSearchDistance)
                    {
                        ed.WriteMessage($"\nCLV-GIS-PIPE-{mode}: nearest structure wall is {dist:0.##}' away; skipped.");
                        return;
                    }

                    if (ent is Line ln)
                    {
                        if (useStart)
                            ln.StartPoint = targetPoint;
                        else
                            ln.EndPoint = targetPoint;
                    }
                    else if (ent is Polyline pl)
                    {
                        if (useStart)
                            pl.SetPointAt(0, new Point2d(targetPoint.X, targetPoint.Y));
                        else
                            pl.SetPointAt(lastIndex, new Point2d(targetPoint.X, targetPoint.Y));
                    }

                    tr.Commit();
                    ed.WriteMessage($"\nCLV-GIS-PIPE-{mode} complete. movedEndpoint={(useStart ? "start" : "end")}, distance={dist:0.###}.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-PIPE-{mode} error: {ex.Message}");
            }
        }

        private static bool TryGetEditableCurveData(Entity ent, out Point3d startPoint, out Point3d endPoint, out bool isPolyline, out int lastIndex)
        {
            startPoint = Point3d.Origin;
            endPoint = Point3d.Origin;
            isPolyline = false;
            lastIndex = 0;

            if (ent is Line ln)
            {
                startPoint = ln.StartPoint;
                endPoint = ln.EndPoint;
                return true;
            }

            if (ent is Polyline pl && pl.NumberOfVertices >= 2)
            {
                isPolyline = true;
                lastIndex = pl.NumberOfVertices - 1;
                startPoint = pl.GetPoint3dAt(0);
                endPoint = pl.GetPoint3dAt(lastIndex);
                return true;
            }

            return false;
        }

        private static bool TryFindClosestStructurePoint(Database db, Transaction tr, Point3d fromPoint, out Point3d bestPoint, out double bestDistance)
        {
            bestPoint = Point3d.Origin;
            bestDistance = double.MaxValue;
            bool found = false;

            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                Entity? ent = tr.GetObject(id, OpenMode.ForRead) as Entity;
                if (ent == null)
                    continue;

                if (!ent.Layer.Equals(StructureInnerLayer, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (ent is not Curve curve)
                    continue;

                try
                {
                    Point3d onCurve = curve.GetClosestPointTo(fromPoint, false);
                    double d = fromPoint.DistanceTo(onCurve);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        bestPoint = onCurve;
                        found = true;
                    }
                }
                catch
                {
                    // ignore bad geometry
                }
            }

            return found;
        }
    }
}
