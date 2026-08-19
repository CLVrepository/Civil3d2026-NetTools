using System;
using System.Collections.Generic;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Ufls
{
    public static class UflsPipeLabels
    {
        private const string LabelLayer = "V-SURV-PIPE-INVT";

        [CommandMethod("UFLS", "UFLS-PIPE-LABEL-3D", CommandFlags.Modal)]
        public static void LabelSingle3dPipe()
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                PromptEntityOptions peo = new PromptEntityOptions("\nSelect 3D pipe polyline to label: ");
                peo.SetRejectMessage("\nOnly 3D polylines are allowed.");
                peo.AddAllowedClass(typeof(Polyline3d), exactMatch: false);

                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                    return;

                using Transaction tr = db.TransactionManager.StartTransaction();
                Polyline3d pl3d = (Polyline3d)tr.GetObject(per.ObjectId, OpenMode.ForRead);

                if (!TryGetEndpoints(tr, pl3d, out Point3d start, out Point3d end, out Vector3d startDir, out Vector3d endDir))
                    throw new InvalidOperationException("Selected 3D polyline does not contain usable endpoints.");

                EnsureLayer(db, tr, LabelLayer, 2);
                CreateEndpointLabel(tr, db, start, startDir, true);
                CreateEndpointLabel(tr, db, end, endDir, false);

                tr.Commit();
                ed.WriteMessage("\nUFLS-PIPE-LABEL-3D: labels created on layer V-SURV-PIPE-INVT.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUFLS-PIPE-LABEL-3D error: {ex.Message}");
            }
        }


private static bool TryGetEndpoints(Transaction tr, Polyline3d pl3d, out Point3d start, out Point3d end, out Vector3d startDir, out Vector3d endDir)
        {
            start = Point3d.Origin;
            end = Point3d.Origin;
            startDir = Vector3d.XAxis;
            endDir = Vector3d.XAxis;

            List<Point3d> verts = new List<Point3d>();
            foreach (ObjectId vId in pl3d)
            {
                if (tr.GetObject(vId, OpenMode.ForRead, false) is PolylineVertex3d vtx)
                    verts.Add(vtx.Position);
            }

            if (verts.Count == 0)
                return false;

            start = verts[0];
            end = verts[verts.Count - 1];

            if (verts.Count >= 2)
            {
                startDir = NormalizePlanVector(verts[1] - verts[0]);
                endDir = NormalizePlanVector(verts[verts.Count - 1] - verts[verts.Count - 2]);
            }

            return true;
        }


private static void CreateEndpointLabel(Transaction tr, Database db, Point3d point, Vector3d pipeDirection, bool isStart)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            string tag = isStart ? "START INVERT" : "END INVERT";
            string textValue = $"{tag}={point.Z:0.00}";

            double textHeight = 0.10;
            Vector3d planDir = NormalizePlanVector(pipeDirection);
            Vector3d offsetNormal = new Vector3d(-planDir.Y, planDir.X, 0.0);
            if (!isStart)
                offsetNormal = -offsetNormal;

            Point3d textPoint = point + (offsetNormal * 0.25);
            double rotation = GetReadableRotation(planDir);

            DBText txt = new DBText
            {
                Position = textPoint,
                Height = textHeight,
                TextString = textValue,
                Layer = LabelLayer,
                Rotation = rotation
            };

            ms.AppendEntity(txt);
            tr.AddNewlyCreatedDBObject(txt, true);
        }

        private static Vector3d NormalizePlanVector(Vector3d vector)
        {
            Vector3d plan = new Vector3d(vector.X, vector.Y, 0.0);
            return plan.Length > 1e-9 ? plan.GetNormal() : Vector3d.XAxis;
        }

        private static double GetReadableRotation(Vector3d planDir)
        {
            double angle = Math.Atan2(planDir.Y, planDir.X);
            if (angle > Math.PI / 2.0)
                angle -= Math.PI;
            else if (angle < -Math.PI / 2.0)
                angle += Math.PI;

            return angle;
        }

        private static void EnsureLayer(Database db, Transaction tr, string layerName, short colorIndex)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(layerName))
                return;

            lt.UpgradeOpen();
            LayerTableRecord ltr = new LayerTableRecord
            {
                Name = layerName,
                Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                    Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex)
            };
            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }
    }
}
