using System;
using System.Collections.Generic;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;
using AcPolyline = Autodesk.AutoCAD.DatabaseServices.Polyline;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Shared
{
    /// <summary>
    /// Point cloud related helpers (detecting clouds, crop/uncrop, etc.).
    /// Extracted from CommonUtils (point cloud parts).
    /// </summary>
    internal static class PointCloudUtils
    {
        // ------------------------------------------------------------
        // Detection
        // ------------------------------------------------------------

        public static bool IsPointCloudEntity(AcEntity ent)
        {
            string name = (ent.GetRXClass()?.Name ?? "").ToUpperInvariant();
            return name.Contains("POINTCLOUD");
        }

        /// <summary>
        /// If exactly one point cloud exists in model space, returns its ObjectId.
        /// If none or more than one are found, returns ObjectId.Null.
        /// </summary>
        public static ObjectId FindSinglePointCloudInModelSpace(Database db)
        {
            ObjectId found = ObjectId.Null;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    var obj = tr.GetObject(id, OpenMode.ForRead) as AcEntity;
                    if (obj == null) continue;

                    if (IsPointCloudEntity(obj))
                    {
                        if (!found.IsNull)
                        {
                            // More than one
                            tr.Commit();
                            return ObjectId.Null;
                        }
                        found = id;
                    }
                }

                tr.Commit();
            }

            return found;
        }

        /// <summary>
        /// Prompts the user to pick a point cloud entity. Returns ObjectId.Null
        /// if the selected object is not a point cloud or the user cancels.
        /// </summary>
        public static ObjectId PromptForPointCloud(Editor ed, Database db, string msg)
        {
            var peo = new PromptEntityOptions(msg);
            var per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) return ObjectId.Null;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ent = tr.GetObject(per.ObjectId, OpenMode.ForRead) as AcEntity;
                if (ent == null || !IsPointCloudEntity(ent))
                {
                    ed.WriteMessage("\nSelected object is not a point cloud.");
                    return ObjectId.Null;
                }
                tr.Commit();
            }

            return per.ObjectId;
        }

        // ------------------------------------------------------------
        // Polyline → polygon helpers for crop
        // ------------------------------------------------------------

        /// <summary>
        /// Returns a list of WCS points (XY + constant Z) describing the vertices
        /// of a polyline intended for use as a plan-view crop polygon.
        /// </summary>
        public static bool TryGetPolylineVerticesWcsPlan(
            ObjectId plId,
            Database db,
            out List<Point3d> ptsPlan)
        {
            ptsPlan = new List<Point3d>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ent = tr.GetObject(plId, OpenMode.ForRead) as AcEntity;
                if (ent is not AcPolyline pl || pl.NumberOfVertices < 3)
                {
                    tr.Commit();
                    return false;
                }

                double zConst = 0.0;
                try { zConst = pl.Elevation; } catch { zConst = 0.0; }

                int n = pl.NumberOfVertices;
                var raw = new List<Point3d>(n);
                for (int i = 0; i < n; i++)
                    raw.Add(pl.GetPoint3dAt(i));

                if (pl.Closed && raw.Count > 1 && raw[0].DistanceTo(raw[^1]) < 1e-8)
                    raw.RemoveAt(raw.Count - 1);

                foreach (var p in raw)
                    ptsPlan.Add(new Point3d(p.X, p.Y, zConst));

                tr.Commit();
            }

            return ptsPlan.Count >= 3;
        }

        // ------------------------------------------------------------
        // Crop / Uncrop commands
        // ------------------------------------------------------------

        /// <summary>
        /// Wrapper that uncrops a point cloud by ID using POINTCLOUDUNCROP.
        /// </summary>
        public static void UncropPointCloudById(Editor ed, ObjectId cloudId)
        {
            if (ed == null || cloudId.IsNull) return;

            ed.SetImpliedSelection(new[] { cloudId });
            ed.Command("_.POINTCLOUDUNCROP");
            ed.SetImpliedSelection(Array.Empty<ObjectId>());
        }

        /// <summary>
        /// Wrapper that polygon-crops a point cloud by ID using POINTCLOUDCROP.
        /// Expects polygonPtsPlan in WCS (XY with constant Z).
        /// </summary>
        public static void CropPointCloudPolygonById(Editor ed, ObjectId cloudId, List<Point3d> polygonPtsPlan)
        {
            if (ed == null || cloudId.IsNull || polygonPtsPlan == null || polygonPtsPlan.Count < 3)
                return;

            ed.SetImpliedSelection(new[] { cloudId });

            var args = new List<object>
            {
                "_.POINTCLOUDCROP",
                "_POLYGON"
            };

            foreach (var p in polygonPtsPlan)
                args.Add(p);

            args.Add(""); // finish polygon
            args.Add(""); // accept default <Inside>

            ed.Command(args.ToArray());

            ed.SetImpliedSelection(Array.Empty<ObjectId>());
        }
    }
}