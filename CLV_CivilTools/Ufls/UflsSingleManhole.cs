using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CogoPoint = Autodesk.Civil.DatabaseServices.CogoPoint;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace CLV_CivilTools.Ufls
{
    public static class UflsSingleManholeCommands
    {
        private const string LAYER_SURV_CHCK = "V-SURV-CHCK";
        private const string LAYER_PICK_MARKER = "V-TEMP-PIPEPICK";

        private const string MH_BLOCK_NAME = "UFLS-GIS-MH-CIRCULAR";
        private const string MH_MARK_BLOCK_NAME = "UFLS_MH_MARK";

        private const string SURVEY_BLOCK_FOLDER =
            @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\Blocks\Survey";

        private const string MH_BLOCK_DWG = MH_BLOCK_NAME + ".dwg";
        private const string MH_MARK_BLOCK_DWG = MH_MARK_BLOCK_NAME + ".dwg";

        private static readonly int[] KnownDiameters = { 48, 60, 72 };

        private sealed class PickedMhPoint
        {
            public Point3d Location { get; set; }
            public string RawDescription { get; set; } = string.Empty;
            public string FullDescription { get; set; } = string.Empty;
        }

        [CommandMethod("UFLS", "UFLS6", CommandFlags.Modal)]
        public static void Ufls6_SingleManholeFromCogo()
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            List<ObjectId> markerIds = new List<ObjectId>();

            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayer(db, tr, LAYER_PICK_MARKER, 2);
                    tr.Commit();
                }

                List<PickedMhPoint> pts = GetManholePointsFromCogo(ed, db, markerIds);
                if (pts.Count != 3)
                {
                    ed.WriteMessage("\nUFLS6: Need exactly 3 COGO points. Command cancelled.");
                    return;
                }

                Point3d center;
                if (!TryComputeCircleCenter2D(pts[0].Location, pts[1].Location, pts[2].Location, out center))
                    center = ComputeCentroidFlattened(pts);

                int diameterInches = InferDiameterInches(pts);
                string? visibilityOption = MapDiameterToVisibility(diameterInches);

                EnsureManholeLayersAndBlocks(db, ed);

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    InsertManholeAndMarker(db, tr, center, 0.0, visibilityOption);

                    foreach (ObjectId id in markerIds)
                    {
                        if (!id.IsNull && id.IsValid)
                        {
                            AcEntity? ent = tr.GetObject(id, OpenMode.ForWrite, false) as AcEntity;
                            ent?.Erase();
                        }
                    }

                    tr.Commit();
                }

                string sizeNote = visibilityOption ?? "default visibility";
                ed.WriteMessage($"\nUFLS6: Manhole created from 3 COGO points on layer {LAYER_SURV_CHCK} ({sizeNote}).");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUFLS6 error: {ex.Message}");
                RemoveMarkers(db, markerIds);
            }
        }


        [CommandMethod("UFLS", "UFLS61P", CommandFlags.Modal)]
        public static void Ufls61P_SingleManholeFromSingleCogo()
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            List<ObjectId> markerIds = new List<ObjectId>();

            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayer(db, tr, LAYER_PICK_MARKER, 2);
                    tr.Commit();
                }

                PickedMhPoint? picked = GetSingleManholePointFromCogo(ed, db, markerIds);
                if (picked == null)
                {
                    ed.WriteMessage("\nUFLS61P: Need exactly 1 COGO point. Command cancelled.");
                    return;
                }

                int diameterInches = InferDiameterInches(new[] { picked });
                string? visibilityOption = MapDiameterToVisibility(diameterInches);

                EnsureManholeLayersAndBlocks(db, ed);

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    InsertManholeAndMarker(db, tr, picked.Location, 0.0, visibilityOption);

                    foreach (ObjectId id in markerIds)
                    {
                        if (!id.IsNull && id.IsValid)
                        {
                            AcEntity? ent = tr.GetObject(id, OpenMode.ForWrite, false) as AcEntity;
                            ent?.Erase();
                        }
                    }

                    tr.Commit();
                }

                string sizeNote = visibilityOption ?? "default visibility";
                ed.WriteMessage($"\nUFLS61P: Manhole created from 1 COGO point on layer {LAYER_SURV_CHCK} ({sizeNote}).");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUFLS61P error: {ex.Message}");
                RemoveMarkers(db, markerIds);
            }
        }

        [CommandMethod("UFLS", "UFLS6PC", CommandFlags.Modal)]
        public static void Ufls6Pc_SingleManholeFromPointCloud()
        {
            Document? doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                List<Point3d> pts = new List<Point3d>();
                for (int i = 1; i <= 3; i++)
                {
                    PromptPointOptions ppo = new PromptPointOptions($"\nPick manhole point {i} (use point cloud osnaps): ");
                    PromptPointResult ppr = ed.GetPoint(ppo);
                    if (ppr.Status != PromptStatus.OK)
                    {
                        ed.WriteMessage("\nUFLS6PC: Command cancelled.");
                        return;
                    }

                    Point3d pick = ppr.Value;
                    pts.Add(new Point3d(pick.X, pick.Y, 0.0));
                }

                Point3d center;
                if (!TryComputeCircleCenter2D(pts[0], pts[1], pts[2], out center))
                    center = ComputeCentroidFlattened(pts);

                EnsureManholeLayersAndBlocks(db, ed);

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    InsertManholeAndMarker(db, tr, center, 0.0, null);
                    tr.Commit();
                }

                ed.WriteMessage($"\nUFLS6PC: Manhole created from 3 snapped points on layer {LAYER_SURV_CHCK}.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nUFLS6PC error: {ex.Message}");
            }
        }

        private static PickedMhPoint? GetSingleManholePointFromCogo(Editor ed, Database db, List<ObjectId> markerIds)
        {
            PromptEntityOptions peo = new PromptEntityOptions("\nSELECT MANHOLE CENTER POINT: ");
            peo.SetRejectMessage("\nOnly COGO points are allowed.");
            peo.AddAllowedClass(typeof(CogoPoint), exactMatch: false);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
                return null;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                CogoPoint cp = (CogoPoint)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                Point3d loc3d = cp.Location;
                Point3d flat = new Point3d(loc3d.X, loc3d.Y, 0.0);

                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                ViewTableRecord view = ed.GetCurrentView();
                double textHeight = Math.Max(view.Height * 0.03, 0.5);

                DBText txt = new DBText
                {
                    Position = flat,
                    Height = textHeight,
                    TextString = "1",
                    Layer = LAYER_PICK_MARKER,
                    Justify = AttachmentPoint.MiddleCenter,
                    AlignmentPoint = flat
                };

                ms.AppendEntity(txt);
                tr.AddNewlyCreatedDBObject(txt, true);
                markerIds.Add(txt.ObjectId);

                PickedMhPoint result = new PickedMhPoint
                {
                    Location = flat,
                    RawDescription = cp.RawDescription ?? string.Empty,
                    FullDescription = cp.FullDescription ?? string.Empty
                };

                tr.Commit();
                return result;
            }
        }

        private static List<PickedMhPoint> GetManholePointsFromCogo(Editor ed, Database db, List<ObjectId> markerIds)
        {
            List<PickedMhPoint> pts = new List<PickedMhPoint>();
            bool first = true;

            while (pts.Count < 3)
            {
                string prompt = first
                    ? "\nSelect COGO point 1 of 3 for manhole: "
                    : $"\nSelect COGO point {pts.Count + 1} of 3 for manhole: ";

                PromptEntityOptions peo = new PromptEntityOptions(prompt);
                peo.SetRejectMessage("\nOnly COGO points are allowed.");
                peo.AddAllowedClass(typeof(CogoPoint), exactMatch: false);

                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                    break;

                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    CogoPoint cp = (CogoPoint)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                    Point3d loc3d = cp.Location;
                    Point3d flat = new Point3d(loc3d.X, loc3d.Y, 0.0);

                    pts.Add(new PickedMhPoint
                    {
                        Location = flat,
                        RawDescription = cp.RawDescription ?? string.Empty,
                        FullDescription = cp.FullDescription ?? string.Empty
                    });

                    BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                    ViewTableRecord view = ed.GetCurrentView();
                    double textHeight = Math.Max(view.Height * 0.03, 0.5);

                    DBText txt = new DBText
                    {
                        Position = flat,
                        Height = textHeight,
                        TextString = pts.Count.ToString(),
                        Layer = LAYER_PICK_MARKER,
                        Justify = AttachmentPoint.MiddleCenter,
                        AlignmentPoint = flat
                    };

                    ms.AppendEntity(txt);
                    tr.AddNewlyCreatedDBObject(txt, true);
                    markerIds.Add(txt.ObjectId);

                    tr.Commit();
                }

                first = false;
            }

            return pts;
        }

        private static bool TryComputeCircleCenter2D(Point3d p1, Point3d p2, Point3d p3, out Point3d center)
        {
            Point3d f1 = new Point3d(p1.X, p1.Y, 0.0);
            Point3d f2 = new Point3d(p2.X, p2.Y, 0.0);
            Point3d f3 = new Point3d(p3.X, p3.Y, 0.0);

            try
            {
                using (CircularArc3d arc = new CircularArc3d(f1, f2, f3))
                {
                    Point3d c = arc.Center;
                    center = new Point3d(c.X, c.Y, 0.0);
                    return true;
                }
            }
            catch
            {
                center = new Point3d();
                return false;
            }
        }

        private static Point3d ComputeCentroidFlattened(IReadOnlyList<PickedMhPoint> pts)
        {
            double sx = 0.0;
            double sy = 0.0;
            foreach (PickedMhPoint p in pts)
            {
                sx += p.Location.X;
                sy += p.Location.Y;
            }

            double n = pts.Count;
            return new Point3d(sx / n, sy / n, 0.0);
        }

        private static Point3d ComputeCentroidFlattened(IReadOnlyList<Point3d> pts)
        {
            double sx = 0.0;
            double sy = 0.0;
            foreach (Point3d p in pts)
            {
                sx += p.X;
                sy += p.Y;
            }

            double n = pts.Count;
            return new Point3d(sx / n, sy / n, 0.0);
        }

        private static int InferDiameterInches(IReadOnlyList<PickedMhPoint> pts)
        {
            foreach (PickedMhPoint p in pts)
            {
                int rawVal = TryParseDiameterFromText(p.RawDescription);
                if (rawVal > 0)
                    return rawVal;

                int fullVal = TryParseDiameterFromText(p.FullDescription);
                if (fullVal > 0)
                    return fullVal;
            }

            return 0;
        }

        private static int TryParseDiameterFromText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            MatchCollection matches = Regex.Matches(value, @"\d+");
            foreach (Match match in matches)
            {
                if (!int.TryParse(match.Value, out int parsed))
                    continue;

                foreach (int known in KnownDiameters)
                {
                    if (parsed == known)
                        return parsed;
                }
            }

            return 0;
        }

        private static string? MapDiameterToVisibility(int diameterInches)
        {
            return diameterInches switch
            {
                48 => "48\" MANHOLE",
                60 => "60\" MANHOLE",
                72 => "72\" MANHOLE",
                _ => null
            };
        }

        private static void EnsureLayer(Database db, Transaction tr, string name, short colorIndex)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(name))
            {
                lt.UpgradeOpen();
                LayerTableRecord ltr = new LayerTableRecord
                {
                    Name = name,
                    Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                        Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex)
                };
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
        }

        private static void EnsureManholeLayersAndBlocks(Database db, Editor ed)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                EnsureLayer(db, tr, LAYER_SURV_CHCK, 3);
                tr.Commit();
            }

            EnsureBlockDefinition(db, MH_BLOCK_NAME, Path.Combine(SURVEY_BLOCK_FOLDER, MH_BLOCK_DWG), ed);
            EnsureBlockDefinition(db, MH_MARK_BLOCK_NAME, Path.Combine(SURVEY_BLOCK_FOLDER, MH_MARK_BLOCK_DWG), ed);
        }

        private static void EnsureBlockDefinition(Database db, string blockName, string sourcePath, Editor ed)
        {
            bool hasBlock;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                hasBlock = bt.Has(blockName);
                tr.Commit();
            }

            if (hasBlock)
                return;

            if (!File.Exists(sourcePath))
            {
                ed.WriteMessage($"\nWarning: Source DWG not found for block {blockName}: {sourcePath}");
                return;
            }

            using (Database srcDb = new Database(false, true))
            {
                srcDb.ReadDwgFile(sourcePath, FileShare.Read, true, string.Empty);
                db.Insert(blockName, srcDb, true);
            }
        }

        private static void InsertManholeAndMarker(Database db, Transaction tr, Point3d center, double rotationRadians, string? visibilityOption)
        {
            Editor ed = AcApp.DocumentManager.MdiActiveDocument.Editor;
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            if (!bt.Has(MH_BLOCK_NAME))
            {
                ed.WriteMessage($"\nBlock {MH_BLOCK_NAME} not defined – cannot place manhole.");
                return;
            }

            BlockReference mhRef = new BlockReference(center, bt[MH_BLOCK_NAME])
            {
                Layer = LAYER_SURV_CHCK,
                Rotation = rotationRadians
            };

            ms.AppendEntity(mhRef);
            tr.AddNewlyCreatedDBObject(mhRef, true);
            TrySetVisibility(mhRef, visibilityOption);

            if (bt.Has(MH_MARK_BLOCK_NAME))
            {
                BlockReference markRef = new BlockReference(center, bt[MH_MARK_BLOCK_NAME])
                {
                    Layer = LAYER_SURV_CHCK,
                    Rotation = rotationRadians
                };

                ms.AppendEntity(markRef);
                tr.AddNewlyCreatedDBObject(markRef, true);
            }
            else
            {
                ed.WriteMessage($"\nWarning: Block {MH_MARK_BLOCK_NAME} not defined – center marker not placed.");
            }
        }

        private static void TrySetVisibility(BlockReference br, string? visibilityOption)
        {
            if (!br.IsDynamicBlock)
                return;

            try
            {
                DynamicBlockReferencePropertyCollection dynProps = br.DynamicBlockReferencePropertyCollection;
                foreach (DynamicBlockReferenceProperty prop in dynProps)
                {
                    if (prop.PropertyName.IndexOf("Visibility", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;

                    if (!string.IsNullOrWhiteSpace(visibilityOption))
                    {
                        foreach (object allowed in prop.GetAllowedValues())
                        {
                            if (allowed is string s && string.Equals(s, visibilityOption, StringComparison.OrdinalIgnoreCase))
                            {
                                prop.Value = s;
                                return;
                            }
                        }
                    }

                    foreach (object allowed in prop.GetAllowedValues())
                    {
                        if (allowed is string s && s.IndexOf("48", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            prop.Value = s;
                            return;
                        }
                    }
                }
            }
            catch
            {
                // leave default visibility if anything goes wrong
            }
        }

        private static void RemoveMarkers(Database db, List<ObjectId> markerIds)
        {
            if (markerIds.Count == 0)
                return;

            try
            {
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    foreach (ObjectId id in markerIds)
                    {
                        if (!id.IsErased && id.IsValid)
                        {
                            AcEntity? ent = tr.GetObject(id, OpenMode.ForWrite, false) as AcEntity;
                            ent?.Erase();
                        }
                    }

                    tr.Commit();
                }
            }
            catch
            {
            }
        }
    }
}
