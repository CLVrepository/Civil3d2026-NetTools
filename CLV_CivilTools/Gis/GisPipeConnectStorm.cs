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
    public static class GisPipeConnectStorm
    {
        private const string PipeWallLayer = "C-STRM-PIPE-E";
        private const string StructureInnerLayer = "C-STRM-STRC-INNR";
        private const double MaxEndpointMove = 10.0;

        [CommandMethod("CLV-GIS-PIPE-CONNECT-STRM", CommandFlags.Modal)]
        public static void ConnectStormPipeWalls()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                PromptSelectionOptions pipeOpts = new PromptSelectionOptions
                {
                    MessageForAdding = "\nSELECT STORM PIPE WALL LINEWORK:"
                };
                PromptSelectionResult pipeRes = ed.GetSelection(pipeOpts, new SelectionFilter(new[]
                {
                    new TypedValue((int)DxfCode.Start, "LINE,LWPOLYLINE,POLYLINE")
                }));
                if (pipeRes.Status != PromptStatus.OK)
                    return;

                PromptSelectionOptions strcOpts = new PromptSelectionOptions
                {
                    MessageForAdding = "\nSELECT STRUCTURE INNER WALL POLYLINES:"
                };
                PromptSelectionResult strcRes = ed.GetSelection(strcOpts, new SelectionFilter(new[]
                {
                    new TypedValue((int)DxfCode.Start, "LWPOLYLINE,POLYLINE")
                }));
                if (strcRes.Status != PromptStatus.OK)
                    return;

                int movedEndpoints = 0;
                int pipeCount = 0;

                SelectionSet? pipeSet = pipeRes.Value;
                SelectionSet? strcSet = strcRes.Value;
                if (pipeSet == null || strcSet == null)
                {
                    ed.WriteMessage("\nCLV-GIS-PIPE-CONNECT-STRM: unable to read selected objects.");
                    return;
                }

                using (DocumentLock docLock = doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    List<Curve> structureCurves = new List<Curve>();
                    foreach (SelectedObject? so in strcSet)
                    {
                        if (so == null)
                            continue;

                        ObjectId strcId = so.ObjectId;
                        if (strcId == ObjectId.Null)
                            continue;

                        if (tr.GetObject(strcId, OpenMode.ForRead) is Curve c)
                        {
                            structureCurves.Add(c);
                        }
                    }

                    foreach (SelectedObject? so in pipeSet)
                    {
                        if (so == null)
                            continue;

                        ObjectId pipeId = so.ObjectId;
                        if (pipeId == ObjectId.Null)
                            continue;

                        Entity? ent = tr.GetObject(pipeId, OpenMode.ForWrite) as Entity;
                        if (ent == null)
                            continue;

                        ent.Layer = PipeWallLayer;
                        pipeCount++;

                        if (ent is Line ln)
                        {
                            if (TryFindClosestStructurePoint(ln.StartPoint, structureCurves, out Point3d newStart, out double d1) && d1 <= MaxEndpointMove)
                            {
                                ln.StartPoint = newStart;
                                movedEndpoints++;
                            }
                            if (TryFindClosestStructurePoint(ln.EndPoint, structureCurves, out Point3d newEnd, out double d2) && d2 <= MaxEndpointMove)
                            {
                                ln.EndPoint = newEnd;
                                movedEndpoints++;
                            }
                        }
                        else if (ent is Polyline pl)
                        {
                            if (pl.NumberOfVertices >= 2)
                            {
                                Point3d s = pl.GetPoint3dAt(0);
                                if (TryFindClosestStructurePoint(s, structureCurves, out Point3d newStart, out double d1) && d1 <= MaxEndpointMove)
                                {
                                    pl.SetPointAt(0, new Point2d(newStart.X, newStart.Y));
                                    movedEndpoints++;
                                }

                                int last = pl.NumberOfVertices - 1;
                                Point3d ept = pl.GetPoint3dAt(last);
                                if (TryFindClosestStructurePoint(ept, structureCurves, out Point3d newEnd, out double d2) && d2 <= MaxEndpointMove)
                                {
                                    pl.SetPointAt(last, new Point2d(newEnd.X, newEnd.Y));
                                    movedEndpoints++;
                                }
                            }
                        }
                    }

                    foreach (Curve c in structureCurves)
                    {
                        if (c is Entity e)
                            e.Layer = StructureInnerLayer;
                    }

                    tr.Commit();
                }

                ed.WriteMessage($"\nCLV-GIS-PIPE-CONNECT-STRM complete. pipes={pipeCount}, movedEndpoints={movedEndpoints}.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-PIPE-CONNECT-STRM error: {ex.Message}");
            }
        }

        private static bool TryFindClosestStructurePoint(Point3d testPoint, List<Curve> structureCurves, out Point3d bestPoint, out double bestDistance)
        {
            bestPoint = Point3d.Origin;
            bestDistance = double.MaxValue;
            bool found = false;

            foreach (Curve curve in structureCurves)
            {
                try
                {
                    Point3d onCurve = curve.GetClosestPointTo(testPoint, false);
                    double d = testPoint.DistanceTo(onCurve);
                    if (d < bestDistance)
                    {
                        bestDistance = d;
                        bestPoint = onCurve;
                        found = true;
                    }
                }
                catch
                {
                    // Ignore individual geometry failures.
                }
            }

            return found;
        }
    }
}
