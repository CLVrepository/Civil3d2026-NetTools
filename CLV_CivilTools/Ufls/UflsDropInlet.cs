using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using AcApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CogoPoint = Autodesk.Civil.DatabaseServices.CogoPoint;
using DbPoint = Autodesk.AutoCAD.DatabaseServices.DBPoint;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;

namespace CLV_CivilTools.Ufls
{
    /// <summary>
    /// Drop inlet automation (UFLS-DROP-INLET).
    /// 1) Ask for inlet type (A, A MOD, C, CM, CM2, D, DM2)
    /// 2) Ask for size / depth / side (per type)
    /// 3) Ask for survey source (IN-HOUSE vs OTHERS)
    /// 4) Pick geometry:
    ///      IN-HOUSE: 4 corners + street-side pick
    ///      OTHERS  : 2 BOC points + street-side pick
    /// 5) Insert block on V-SURV-CHCK with correct visibility and rotation.
    /// 6) Insert UFLS_DI_MARK at DI_CENTER (if present) for future tools.
    /// </summary>
    public static class UflsDropInlet
    {
        // --------------------------------------------------------------------
        // Paths / names
        // --------------------------------------------------------------------

        private const string SurveyBlocksRoot =
            @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\Blocks\Survey";

        private const string MarkerBlockName = "UFLS_DI_MARK";
        private const string DiCenterAttributeTag = "DI_CENTER";

        private const string CheckLayerName = "V-SURV-CHCK";
        private const string LayerPickMarkerName = "V-TEMP-DIPICK";

        // --------------------------------------------------------------------
        // COMMAND ENTRY
        // --------------------------------------------------------------------

        [CommandMethod("UFLS-DROP-INLET", CommandFlags.Modal)]
        public static void UflsDropInletCommand()
        {
            RunDropInlet();
        }

        // --------------------------------------------------------------------
        // TYPE ENUM + SELECTION STRUCT
        // --------------------------------------------------------------------

        private enum DropInletType
        {
            TypeA,
            TypeAMod,
            TypeC,
            TypeCM,
            TypeCM2,
            TypeD,
            TypeDM2
        }

        private struct InletSelection
        {
            public DropInletType Type { get; }
            public double LengthFeet { get; }
            public bool IsDeep { get; }   // "over" threshold when applicable
            public string? SideSuffix { get; }   // "L"/"R" for C/D, else null
            public string VisibilityName { get; }

            public InletSelection(
                DropInletType type,
                double lengthFeet,
                bool isDeep,
                string? sideSuffix,
                string visibilityName)
            {
                Type = type;
                LengthFeet = lengthFeet;
                IsDeep = isDeep;
                SideSuffix = sideSuffix;
                VisibilityName = visibilityName;
            }
        }

        // --------------------------------------------------------------------
        // CORE IMPLEMENTATION
        // --------------------------------------------------------------------

        public static void RunDropInlet()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            using (doc.LockDocument())
            {
                var db = doc.Database;
                var ed = doc.Editor;
                var markerIds = new List<ObjectId>();

                try
                {
                    // Ensure pick-marker layer exists (yellow)
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        EnsureLayer(db, tr, LayerPickMarkerName, 2);
                        tr.Commit();
                    }

                    // ------------------------------------------------------------------
                    // 1) Pick inlet TYPE
                    // ------------------------------------------------------------------
                    DropInletType inletType;
                    using (var form = new DropInletTypeForm())
                    {
                        var result = AcApp.ShowModalDialog(form);
                        if (result != DialogResult.OK)
                        {
                            ed.WriteMessage("\nDrop inlet: cancelled (no type selected).");
                            return;
                        }

                        inletType = form.SelectedType;
                    }

                    // ------------------------------------------------------------------
                    // 2) Pick size / depth / side for that type
                    // ------------------------------------------------------------------
                    InletSelection selection;

                    switch (inletType)
                    {
                        case DropInletType.TypeA:
                            using (var f = new TypeAInletForm())
                            {
                                var r = AcApp.ShowModalDialog(f);
                                if (r != DialogResult.OK || !f.Selection.HasValue)
                                {
                                    ed.WriteMessage("\nDrop inlet: cancelled (no size/depth).");
                                    return;
                                }
                                selection = f.Selection.Value;
                            }
                            break;

                        case DropInletType.TypeAMod:
                            using (var f = new TypeAModInletForm())
                            {
                                var r = AcApp.ShowModalDialog(f);
                                if (r != DialogResult.OK || !f.Selection.HasValue)
                                {
                                    ed.WriteMessage("\nDrop inlet: cancelled (no size/depth).");
                                    return;
                                }
                                selection = f.Selection.Value;
                            }
                            break;

                        case DropInletType.TypeC:
                            using (var f = new TypeCInletForm())
                            {
                                var r = AcApp.ShowModalDialog(f);
                                if (r != DialogResult.OK || !f.Selection.HasValue)
                                {
                                    ed.WriteMessage("\nDrop inlet: cancelled (no size/side/depth).");
                                    return;
                                }
                                selection = f.Selection.Value;
                            }
                            break;

                        case DropInletType.TypeCM:
                            using (var f = new TypeCMInletForm())
                            {
                                var r = AcApp.ShowModalDialog(f);
                                if (r != DialogResult.OK || !f.Selection.HasValue)
                                {
                                    ed.WriteMessage("\nDrop inlet: cancelled (no size).");
                                    return;
                                }
                                selection = f.Selection.Value;
                            }
                            break;

                        case DropInletType.TypeCM2:
                            using (var f = new TypeCM2InletForm())
                            {
                                var r = AcApp.ShowModalDialog(f);
                                if (r != DialogResult.OK || !f.Selection.HasValue)
                                {
                                    ed.WriteMessage("\nDrop inlet: cancelled (no size/depth).");
                                    return;
                                }
                                selection = f.Selection.Value;
                            }
                            break;

                        case DropInletType.TypeD:
                            using (var f = new TypeDInletForm())
                            {
                                var r = AcApp.ShowModalDialog(f);
                                if (r != DialogResult.OK || !f.Selection.HasValue)
                                {
                                    ed.WriteMessage("\nDrop inlet: cancelled (no size/side/depth).");
                                    return;
                                }
                                selection = f.Selection.Value;
                            }
                            break;

                        case DropInletType.TypeDM2:
                            using (var f = new TypeDM2InletForm())
                            {
                                var r = AcApp.ShowModalDialog(f);
                                if (r != DialogResult.OK || !f.Selection.HasValue)
                                {
                                    ed.WriteMessage("\nDrop inlet: cancelled (no size/depth).");
                                    return;
                                }
                                selection = f.Selection.Value;
                            }
                            break;

                        default:
                            ed.WriteMessage("\nDrop inlet: unknown type.");
                            return;
                    }

                    // ------------------------------------------------------------------
                    // 3) Source: IN-HOUSE vs OTHERS
                    // ------------------------------------------------------------------
                    bool isInHouse;
                    using (var form = new DropInletSourceForm())
                    {
                        var result = AcApp.ShowModalDialog(form);
                        if (result != DialogResult.OK)
                        {
                            ed.WriteMessage("\nDrop inlet source: cancelled.");
                            return;
                        }

                        isInHouse = form.IsInHouse;
                    }

                    // ------------------------------------------------------------------
                    // 4) Collect geometry (points + side pick)
                    // ------------------------------------------------------------------
                    Point3d referencePoint;
                    double rotation;

                    if (isInHouse)
                    {
                        if (!TryGetInHousePlacement(ed, db, markerIds, out referencePoint, out rotation))
                        {
                            ed.WriteMessage("\nDrop inlet (IN-HOUSE): cancelled.");
                            return;
                        }
                    }
                    else
                    {
                        if (!TryGetOthersPlacement(ed, db, markerIds, out referencePoint, out rotation))
                        {
                            ed.WriteMessage("\nDrop inlet (OTHERS): cancelled.");
                            return;
                        }
                    }

                    // ------------------------------------------------------------------
                    // 5) Insert block + set visibility + center marker
                    // ------------------------------------------------------------------
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        ObjectId btrId = EnsureDropInletBlockLoaded(db, tr, ed, selection.Type);
                        if (btrId.IsNull)
                        {
                            tr.Commit();
                            return;
                        }

                        EnsureLayer(db, tr, CheckLayerName);

                        Point3d insertionPoint = referencePoint;

                        if (isInHouse)
                        {
                            // For IN-HOUSE, try to honor DI_CENTER attribute if it exists
                            string blockName = GetBlockName(selection.Type);
                            Point3d? localCenter = GetLocalDiCenter(db, tr, blockName);
                            if (!localCenter.HasValue)
                            {
                                ed.WriteMessage(
                                    $"\nWarning: DI_CENTER attribute not found in block \"{blockName}\".");
                            }
                            else
                            {
                                insertionPoint =
                                    ComputeInsertionFromCenter(referencePoint, rotation, localCenter.Value);
                            }
                        }

                        var curSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                        var diRef = new BlockReference(insertionPoint, btrId)
                        {
                            Layer = CheckLayerName,
                            Rotation = rotation,
                            ScaleFactors = new Scale3d(1.0)
                        };

                        curSpace.AppendEntity(diRef);
                        tr.AddNewlyCreatedDBObject(diRef, true);

                        AddAttributesFromDefinition(diRef, tr);
                        SetVisibilityForSelection(diRef, selection);

                        // Insert UFLS_DI_MARK at center if available
                        ObjectId markerBtrId = EnsureMarkerBlockLoaded(db, tr, ed);
                        if (!markerBtrId.IsNull)
                        {
                            InsertCenterMarkerForInlet(diRef, markerBtrId, tr);
                        }

                        tr.Commit();
                    }
                }
                catch (System.Exception ex)
                {
                    ed.WriteMessage($"\nUFLS-DROP-INLET error: {ex.Message}");
                }
                finally
                {
                    RemoveMarkers(doc.Database, markerIds);
                }
            }
        }

        // --------------------------------------------------------------------
        // DI_CENTER_TEST (unchanged, but now generic for any inlet block)
        // --------------------------------------------------------------------
        [CommandMethod("UFLS", "DI_CENTER_TEST", CommandFlags.Modal)]
        public static void PlaceCenterMarkerOnExistingInlet()
        {
            var doc = AcApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            var db = doc.Database;
            var ed = doc.Editor;

            var pso = new PromptEntityOptions("\nSelect a drop inlet block: ");
            pso.SetRejectMessage("\nOnly block references are supported.");
            pso.AddAllowedClass(typeof(BlockReference), exactMatch: false);

            var per = ed.GetEntity(pso);
            if (per.Status != PromptStatus.OK)
                return;

            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var br = (BlockReference)tr.GetObject(per.ObjectId, OpenMode.ForRead);

                var centerAtt = FindDiCenterAttribute(br, tr);
                if (centerAtt == null)
                {
                    ed.WriteMessage("\nSelected block does not contain a DI_CENTER attribute.");
                    tr.Commit();
                    return;
                }

                ObjectId markerBtrId = EnsureMarkerBlockLoaded(db, tr, ed);
                if (markerBtrId.IsNull)
                {
                    tr.Commit();
                    return;
                }

                EnsureLayer(db, tr, CheckLayerName);

                var curSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

                var markerRef = new BlockReference(centerAtt.Position, markerBtrId)
                {
                    Layer = CheckLayerName,
                    Rotation = br.Rotation,
                    ScaleFactors = new Scale3d(1.0)
                };

                curSpace.AppendEntity(markerRef);
                tr.AddNewlyCreatedDBObject(markerRef, true);

                tr.Commit();
            }
        }

        // --------------------------------------------------------------------
        // GEOMETRY & PICK HELPERS
        // --------------------------------------------------------------------

        private static bool PromptDropInletPoint(
            Editor ed,
            Database db,
            List<ObjectId> markerIds,
            string message,
            int markerIndex,
            out Point3d pt)
        {
            pt = Point3d.Origin;

            var peo = new PromptEntityOptions(
                message + " (select COGO/POINT, or press Enter to pick any location): ");
            peo.SetRejectMessage("\nSelect a COGO point or AutoCAD POINT, or press Enter to pick any location.");
            peo.AddAllowedClass(typeof(CogoPoint), false);
            peo.AddAllowedClass(typeof(DbPoint), false);

            var per = ed.GetEntity(peo);
            if (per.Status == PromptStatus.OK)
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var ent = tr.GetObject(per.ObjectId, OpenMode.ForRead);
                    if (ent is CogoPoint cp)
                    {
                        pt = new Point3d(cp.Location.X, cp.Location.Y, 0.0);
                    }
                    else if (ent is DbPoint dp)
                    {
                        pt = new Point3d(dp.Position.X, dp.Position.Y, 0.0);
                    }
                    else
                    {
                        return false;
                    }

                    CreatePickMarker(ed, db, tr, pt, markerIndex, markerIds);
                    tr.Commit();
                }
                return true;
            }

            if (per.Status == PromptStatus.Cancel)
            {
                var ppr = ed.GetPoint(message + ": ");
                if (ppr.Status != PromptStatus.OK)
                    return false;

                pt = new Point3d(ppr.Value.X, ppr.Value.Y, 0.0);

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    CreatePickMarker(ed, db, tr, pt, markerIndex, markerIds);
                    tr.Commit();
                }

                return true;
            }

            return false;
        }

        private static void CreatePickMarker(
            Editor ed,
            Database db,
            Transaction tr,
            Point3d loc,
            int markerIndex,
            List<ObjectId> markerIds)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(
                bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            var view = ed.GetCurrentView();
            double ht = Math.Max(view.Height * 0.03, 0.5);

            var txt = new DBText
            {
                Position = loc,
                Height = ht,
                TextString = markerIndex.ToString(),
                Layer = LayerPickMarkerName
            };

            txt.Justify = AttachmentPoint.MiddleCenter;
            txt.AlignmentPoint = loc;

            ms.AppendEntity(txt);
            tr.AddNewlyCreatedDBObject(txt, true);
            markerIds.Add(txt.ObjectId);
        }

        // ---------- IN-HOUSE ORIENTATION ----------
        private static bool TryGetInHousePlacement(
            Editor ed,
            Database db,
            List<ObjectId> markerIds,
            out Point3d center,
            out double rotation)
        {
            center = Point3d.Origin;
            rotation = 0.0;

            var pts = new Point3d[4];

            for (int i = 0; i < 4; i++)
            {
                if (!PromptDropInletPoint(
                        ed, db, markerIds,
                        $"\nPick corner point {i + 1} of inlet",
                        i + 1,
                        out pts[i]))
                {
                    return false;
                }
            }

            double cx = 0, cy = 0;
            for (int i = 0; i < 4; i++)
            {
                cx += pts[i].X;
                cy += pts[i].Y;
            }
            cx /= 4.0;
            cy /= 4.0;
            center = new Point3d(cx, cy, 0.0);

            int[] e1 = { 0, 1, 2, 3 };
            int[] e2 = { 1, 2, 3, 0 };

            double maxLen = -1.0;
            Vector3d bestEdge = Vector3d.XAxis;

            for (int i = 0; i < 4; i++)
            {
                Vector3d e = pts[e2[i]] - pts[e1[i]];
                double len = e.Length;
                if (len > maxLen)
                {
                    maxLen = len;
                    bestEdge = e;
                }
            }

            if (maxLen < 1e-6)
                return false;

            Vector3d t = bestEdge.GetNormal();
            Vector3d n1 = new Vector3d(-t.Y, t.X, 0.0);
            Vector3d n2 = -n1;

            var psRes = ed.GetPoint("\nPick a point on STREET side of inlet for rotation: ");
            if (psRes.Status != PromptStatus.OK)
                return false;

            Point3d ps = new Point3d(psRes.Value.X, psRes.Value.Y, 0.0);
            Vector3d cToPick = ps - center;
            double dot1 = n1.DotProduct(cToPick);

            Vector3d chosenN = (dot1 >= 0.0) ? n1 : n2;
            rotation = Math.Atan2(chosenN.Y, chosenN.X);

            return true;
        }

        // ---------- OTHERS ORIENTATION ----------
        private static bool TryGetOthersPlacement(
            Editor ed,
            Database db,
            List<ObjectId> markerIds,
            out Point3d basePoint,
            out double rotation)
        {
            basePoint = Point3d.Origin;
            rotation = 0.0;

            Point3d p1, p2;
            if (!PromptDropInletPoint(
                    ed, db, markerIds,
                    "\nPick first back-of-curb point at inlet",
                    1,
                    out p1))
                return false;

            if (!PromptDropInletPoint(
                    ed, db, markerIds,
                    "\nPick second back-of-curb point at inlet",
                    2,
                    out p2))
                return false;

            basePoint = new Point3d(
                0.5 * (p1.X + p2.X),
                0.5 * (p1.Y + p2.Y),
                0.0);

            Vector3d t = p2 - p1;
            if (t.Length < 1e-6)
                return false;
            t = t.GetNormal();

            Vector3d n1 = new Vector3d(-t.Y, t.X, 0.0);
            Vector3d n2 = -n1;

            var psRes = ed.GetPoint("\nPick a point on STREET side of inlet for rotation: ");
            if (psRes.Status != PromptStatus.OK)
                return false;

            Point3d ps = new Point3d(psRes.Value.X, psRes.Value.Y, 0.0);
            Vector3d mToPick = ps - basePoint;
            double dot1 = n1.DotProduct(mToPick);

            Vector3d chosenN = (dot1 >= 0.0) ? n1 : n2;
            rotation = Math.Atan2(chosenN.Y, chosenN.X);

            return true;
        }

        private static Point3d ComputeInsertionFromCenter(
            Point3d desiredCenter,
            double rotation,
            Point3d localCenter)
        {
            Matrix3d rotMat = Matrix3d.Rotation(rotation, Vector3d.ZAxis, Point3d.Origin);
            Point3d rotatedLocalCenter = localCenter.TransformBy(rotMat);

            Vector3d offset = new Vector3d(rotatedLocalCenter.X, rotatedLocalCenter.Y, 0.0);

            return new Point3d(
                desiredCenter.X - offset.X,
                desiredCenter.Y - offset.Y,
                desiredCenter.Z);
        }

        // --------------------------------------------------------------------
        // BLOCK / LAYER HELPERS
        // --------------------------------------------------------------------

        private static string GetBlockName(DropInletType type)
        {
            return type switch
            {
                DropInletType.TypeA => "TYPE_A-USD_411",
                DropInletType.TypeAMod => "TYPE_A_MOD-USD_411.1",
                DropInletType.TypeC => "TYPE_C-USD_413",
                DropInletType.TypeCM => "TYPE_CM-USD_422",
                DropInletType.TypeCM2 => "TYPE_CM2-USD_412.1",
                DropInletType.TypeD => "TYPE_D-USD_414",
                DropInletType.TypeDM2 => "TYPE_DM2-USD_412.1",
                _ => "TYPE_A-USD_411"
            };
        }

        private static string GetBlockFile(DropInletType type)
        {
            return GetBlockName(type) + ".dwg";
        }

        private static ObjectId EnsureDropInletBlockLoaded(
            Database db,
            Transaction tr,
            Editor ed,
            DropInletType type)
        {
            string blockName = GetBlockName(type);
            string dwgName = GetBlockFile(type);
            string dwgPath = Path.Combine(SurveyBlocksRoot, dwgName);

            if (!File.Exists(dwgPath))
            {
                ed.WriteMessage(
                    $"\nDrop inlet block \"{dwgName}\" not found at:\n  {dwgPath}");
                return ObjectId.Null;
            }

            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(blockName))
            {
                ObjectId existingId = bt[blockName];
                if (BlockDefinitionHasAttribute(existingId, tr, DiCenterAttributeTag))
                    return existingId;

                ed.WriteMessage(
                    $"\nExisting block definition \"{blockName}\" is missing {DiCenterAttributeTag}; refreshing from server block.");

                if (!TryRenameExistingBlockDefinition(db, tr, ed, existingId, blockName, out string backupName))
                    return ObjectId.Null;

                ed.WriteMessage(
                    $"\nOld definition preserved as \"{backupName}\".");
            }

            return ImportBlockDefinitionFromDwg(db, tr, ed, blockName, dwgPath, "drop inlet block");
        }

        private static bool BlockDefinitionHasAttribute(
            ObjectId blockTableRecordId,
            Transaction tr,
            string attributeTag)
        {
            if (blockTableRecordId.IsNull)
                return false;

            var btr = (BlockTableRecord)tr.GetObject(blockTableRecordId, OpenMode.ForRead);
            foreach (ObjectId entId in btr)
            {
                if (tr.GetObject(entId, OpenMode.ForRead) is AttributeDefinition attDef &&
                    string.Equals(attDef.Tag, attributeTag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryRenameExistingBlockDefinition(
            Database db,
            Transaction tr,
            Editor ed,
            ObjectId blockTableRecordId,
            string originalName,
            out string backupName)
        {
            backupName = string.Empty;

            try
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string candidate = $"{originalName}_CLV_OLD_{stamp}";
                int suffix = 1;

                while (bt.Has(candidate))
                {
                    candidate = $"{originalName}_CLV_OLD_{stamp}_{suffix}";
                    suffix++;
                }

                var btr = (BlockTableRecord)tr.GetObject(blockTableRecordId, OpenMode.ForWrite);
                btr.Name = candidate;
                backupName = candidate;
                return true;
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage(
                    $"\nUnable to preserve/reload existing block definition \"{originalName}\": {ex.Message}");
                return false;
            }
        }

        private static ObjectId ImportBlockDefinitionFromDwg(
            Database db,
            Transaction tr,
            Editor ed,
            string blockName,
            string dwgPath,
            string description)
        {
            try
            {
                using (var srcDb = new Database(false, true))
                {
                    srcDb.ReadDwgFile(dwgPath, FileShare.Read, true, string.Empty);
                    ObjectId importedId = db.Insert(blockName, srcDb, true);

                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    if (!bt.Has(blockName))
                    {
                        ed.WriteMessage(
                            $"\nFailed to import {description} from:\n  {dwgPath}");
                        return ObjectId.Null;
                    }

                    ObjectId blockId = bt[blockName];
                    return blockId.IsNull ? importedId : blockId;
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage(
                    $"\nError loading {description} \"{blockName}\" from:\n  {dwgPath}\n  {ex.Message}");
                return ObjectId.Null;
            }
        }

        private static ObjectId EnsureMarkerBlockLoaded(Database db, Transaction tr, Editor ed)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(MarkerBlockName))
                return bt[MarkerBlockName];

            string dwgPath = Path.Combine(SurveyBlocksRoot, MarkerBlockName + ".dwg");
            if (!File.Exists(dwgPath))
            {
                ed.WriteMessage(
                    $"\nMarker block \"{MarkerBlockName}\" not found at:\n  {dwgPath}");
                return ObjectId.Null;
            }

            return ImportBlockDefinitionFromDwg(db, tr, ed, MarkerBlockName, dwgPath, "marker block");
        }

        private static void EnsureLayer(Database db, Transaction tr, string layerName)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(layerName))
                return;

            lt.UpgradeOpen();

            var ltr = new LayerTableRecord
            {
                Name = layerName
            };

            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }

        private static void EnsureLayer(Database db, Transaction tr, string layerName, short colorIndex)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();
                var ltr = new LayerTableRecord
                {
                    Name = layerName,
                    Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(
                        Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex)
                };
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
        }

        private static Point3d? GetLocalDiCenter(Database db, Transaction tr, string blockName)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (!bt.Has(blockName))
                return null;

            var btr = (BlockTableRecord)tr.GetObject(bt[blockName], OpenMode.ForRead);

            foreach (ObjectId entId in btr)
            {
                if (tr.GetObject(entId, OpenMode.ForRead) is AttributeDefinition attDef)
                {
                    if (string.Equals(attDef.Tag, DiCenterAttributeTag, StringComparison.OrdinalIgnoreCase))
                        return attDef.Position;
                }
            }

            return null;
        }

        private static AttributeReference? FindDiCenterAttribute(BlockReference br, Transaction tr)
        {
            foreach (ObjectId attId in br.AttributeCollection)
            {
                if (attId.IsNull) continue;

                if (tr.GetObject(attId, OpenMode.ForRead) is AttributeReference attRef)
                {
                    if (string.Equals(attRef.Tag, DiCenterAttributeTag, StringComparison.OrdinalIgnoreCase))
                        return attRef;
                }
            }
            return null;
        }

        private static void AddAttributesFromDefinition(BlockReference br, Transaction tr)
        {
            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);

            foreach (ObjectId id in btr)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is AttributeDefinition attDef)
                {
                    if (attDef.Constant) continue;

                    var attRef = new AttributeReference();
                    attRef.SetAttributeFromBlock(attDef, br.BlockTransform);
                    br.AttributeCollection.AppendAttribute(attRef);
                    tr.AddNewlyCreatedDBObject(attRef, true);
                }
            }
        }

        private static void SetVisibilityForSelection(BlockReference br, InletSelection selection)
        {
            try
            {
                var props = br.DynamicBlockReferencePropertyCollection;
                foreach (DynamicBlockReferenceProperty prop in props)
                {
                    if (!prop.ReadOnly &&
                        prop.PropertyName.IndexOf("Visibility", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        prop.Value = selection.VisibilityName;
                        break;
                    }
                }
            }
            catch
            {
                // leave default visibility
            }
        }

        private static void InsertCenterMarkerForInlet(
            BlockReference diRef,
            ObjectId markerBtrId,
            Transaction tr)
        {
            Point3d? centerWorld = null;

            var centerAtt = FindDiCenterAttribute(diRef, tr);
            if (centerAtt != null)
            {
                centerWorld = centerAtt.Position;
            }
            else
            {
                var db = diRef.Database;
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(diRef.BlockTableRecord, OpenMode.ForRead);
                string blockName = btr.Name;

                var localCenter = GetLocalDiCenter(db, tr, blockName);
                if (localCenter.HasValue)
                {
                    centerWorld = localCenter.Value.TransformBy(diRef.BlockTransform);
                }
            }

            if (!centerWorld.HasValue)
                return;

            var db2 = diRef.Database;
            var curSpace = (BlockTableRecord)tr.GetObject(db2.CurrentSpaceId, OpenMode.ForWrite);

            var markerRef = new BlockReference(centerWorld.Value, markerBtrId)
            {
                Layer = CheckLayerName,
                Rotation = diRef.Rotation,
                ScaleFactors = new Scale3d(1.0)
            };

            curSpace.AppendEntity(markerRef);
            tr.AddNewlyCreatedDBObject(markerRef, true);
        }

        private static void RemoveMarkers(Database db, List<ObjectId> markerIds)
        {
            if (markerIds == null || markerIds.Count == 0)
                return;

            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    foreach (var id in markerIds)
                    {
                        if (!id.IsValid || id.IsErased) continue;

                        if (tr.GetObject(id, OpenMode.ForWrite, false) is AcEntity ent)
                            ent.Erase();
                    }
                    tr.Commit();
                }
            }
            catch
            {
                // ignore cleanup errors
            }
        }

        // --------------------------------------------------------------------
        // FORMS
        // --------------------------------------------------------------------

        // ----- Type selection -----

        private sealed class DropInletTypeForm : Form
        {
            public DropInletType SelectedType { get; private set; } = DropInletType.TypeA;

            public DropInletTypeForm()
            {
                Text = "Drop Inlet Type";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterScreen;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;

                // Size tuned to match your screenshot style
                Width = 360;
                Height = 360;

                // Main layout: single group box filling the form
                var mainLayout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 1
                };
                mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

                var grp = new GroupBox
                {
                    Text = "Drop Inlet",
                    Dock = DockStyle.Fill,
                    Padding = new Padding(12, 12, 12, 12)
                };

                // Inside group: stacked buttons, all same width
                var btnLayout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 7
                };
                btnLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                for (int i = 0; i < 7; i++)
                    btnLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 1f / 7f));

                void AddTypeButton(string text, DropInletType type, int rowIndex)
                {
                    var btn = new Button
                    {
                        Text = text,
                        Dock = DockStyle.Fill,
                        Margin = new Padding(4, 3, 4, 3),
                        Tag = type
                    };

                    btn.Click += (s, e) =>
                    {
                        if (s is Button { Tag: DropInletType selectedType })
                        {
                            SelectedType = selectedType;
                            DialogResult = DialogResult.OK;
                            Close();
                        }
                    };

                    btnLayout.Controls.Add(btn, 0, rowIndex);
                }

                // Order and labels per your screenshot
                AddTypeButton("TYPE A (USD 411)", DropInletType.TypeA, 0);
                AddTypeButton("TYPE A MOD (USD 411.1)", DropInletType.TypeAMod, 1);
                AddTypeButton("TYPE C (USD 413)", DropInletType.TypeC, 2);
                AddTypeButton("TYPE CM (USD 422)", DropInletType.TypeCM, 3);
                AddTypeButton("TYPE CM2 (USD 412.1)", DropInletType.TypeCM2, 4);
                AddTypeButton("TYPE D (USD 414)", DropInletType.TypeD, 5);
                AddTypeButton("TYPE DM2 (USD 412.1)", DropInletType.TypeDM2, 6);

                grp.Controls.Add(btnLayout);
                mainLayout.Controls.Add(grp, 0, 0);

                Controls.Add(mainLayout);
            }
        }

        // ----- Source form (IN-HOUSE / OTHERS) -----

        private sealed class DropInletSourceForm : Form
        {
            public bool IsInHouse { get; private set; }

            public DropInletSourceForm()
            {
                Text = "Drop Inlet Source";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterScreen;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;
                Width = 320;
                Height = 160;

                var lbl = new Label
                {
                    Text = "Select final placement method for drop inlet:",
                    Dock = DockStyle.Top,
                    Height = 30,
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter
                };

                var panel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight,
                    Padding = new Padding(12),
                    AutoSize = true
                };

                var btnInHouse = new Button
                {
                    Text = "4 POINT",
                    Width = 120,
                    Height = 30
                };
                btnInHouse.Click += (s, e) =>
                {
                    IsInHouse = true;
                    DialogResult = DialogResult.OK;
                    Close();
                };

                var btnOthers = new Button
                {
                    Text = "2 POINT",
                    Width = 120,
                    Height = 30
                };
                btnOthers.Click += (s, e) =>
                {
                    IsInHouse = false;
                    DialogResult = DialogResult.OK;
                    Close();
                };

                panel.Controls.Add(btnInHouse);
                panel.Controls.Add(btnOthers);

                Controls.Add(panel);
                Controls.Add(lbl);
            }
        }

        // ----- TYPE A (USD 411) FORM -----

        private sealed class TypeAInletForm : Form
        {
            public InletSelection? Selection { get; private set; }

            public TypeAInletForm()
            {
                Text = "INSERT TYPE A INLET - USD 411";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterScreen;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;
                Width = 520;
                Height = 280;

                var mainLayout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    RowCount = 3,
                    ColumnCount = 1
                };
                mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
                mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

                var lblTitle = new Label
                {
                    Text = "SELECT INLET SIZE AND DEPTH",
                    Dock = DockStyle.Fill,
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter
                };

                var midPanel = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    RowCount = 1,
                    ColumnCount = 2,
                    Padding = new Padding(8)
                };
                midPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                midPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

                var grpUnder = CreateGroup("UNDER 8' DEEP   (< 8)", isDeep: false);
                var grpOver = CreateGroup("OVER  8' DEEP   (> 8)", isDeep: true);

                midPanel.Controls.Add(grpUnder, 0, 0);
                midPanel.Controls.Add(grpOver, 1, 0);

                var btnCancel = new Button
                {
                    Text = "CANCEL",
                    Dock = DockStyle.Right,
                    Width = 100,
                    Height = 28
                };
                btnCancel.Click += (s, e) =>
                {
                    Selection = null;
                    DialogResult = DialogResult.Cancel;
                    Close();
                };

                mainLayout.Controls.Add(lblTitle, 0, 0);
                mainLayout.Controls.Add(midPanel, 0, 1);
                mainLayout.Controls.Add(btnCancel, 0, 2);

                Controls.Add(mainLayout);
            }

            private GroupBox CreateGroup(string title, bool isDeep)
            {
                var grp = new GroupBox
                {
                    Text = title,
                    Dock = DockStyle.Fill
                };

                var layout = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight,
                    WrapContents = true,
                    AutoScroll = true,
                    Padding = new Padding(6)
                };

                int[] lengths = { 4, 5, 6, 7, 8, 9, 10, 11, 12 };

                foreach (int len in lengths)
                {
                    var btn = new Button
                    {
                        Text = $"{len}'",
                        Width = 52,
                        Height = 28
                    };

                    string vis = $"{len} {(isDeep ? "> 8' DEEP" : "< 8' DEEP")}";

                    btn.Tag = new InletSelection(
                        DropInletType.TypeA,
                        len,
                        isDeep,
                        sideSuffix: null,
                        visibilityName: vis);

                    btn.Click += (s, e) =>
                    {
                        if (s is Button { Tag: InletSelection selection })
                        {
                            Selection = selection;
                            DialogResult = DialogResult.OK;
                            Close();
                        }
                    };

                    layout.Controls.Add(btn);
                }

                grp.Controls.Add(layout);
                return grp;
            }
        }

        // ----- TYPE A MOD (USD 411.1) FORM -----

        private sealed class TypeAModInletForm : Form
        {
            public InletSelection? Selection { get; private set; }

            public TypeAModInletForm()
            {
                Text = "INSERT TYPE A MOD INLET - USD 411.1";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterScreen;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;
                Width = 520;
                Height = 280;

                var mainLayout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    RowCount = 3,
                    ColumnCount = 1
                };
                mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
                mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

                var lblTitle = new Label
                {
                    Text = "SELECT INLET SIZE AND DEPTH",
                    Dock = DockStyle.Fill,
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter
                };

                var midPanel = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    RowCount = 1,
                    ColumnCount = 2,
                    Padding = new Padding(8)
                };
                midPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                midPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

                var grpUnder = CreateGroup("UNDER 6' DEEP   (< 6)", isDeep: false);
                var grpOver = CreateGroup("OVER  6' DEEP   (> 6)", isDeep: true);

                midPanel.Controls.Add(grpUnder, 0, 0);
                midPanel.Controls.Add(grpOver, 1, 0);

                var btnCancel = new Button
                {
                    Text = "CANCEL",
                    Dock = DockStyle.Right,
                    Width = 100,
                    Height = 28
                };
                btnCancel.Click += (s, e) =>
                {
                    Selection = null;
                    DialogResult = DialogResult.Cancel;
                    Close();
                };

                mainLayout.Controls.Add(lblTitle, 0, 0);
                mainLayout.Controls.Add(midPanel, 0, 1);
                mainLayout.Controls.Add(btnCancel, 0, 2);

                Controls.Add(mainLayout);
            }

            private GroupBox CreateGroup(string title, bool isDeep)
            {
                var grp = new GroupBox
                {
                    Text = title,
                    Dock = DockStyle.Fill
                };

                var layout = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight,
                    WrapContents = true,
                    AutoScroll = true,
                    Padding = new Padding(6)
                };

                int[] lengths = { 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };

                foreach (int len in lengths)
                {
                    var btn = new Button
                    {
                        Text = $"{len}'",
                        Width = 52,
                        Height = 28
                    };

                    string vis = $"{len} {(isDeep ? "> 6' DEEP" : "< 6' DEEP")}";

                    btn.Tag = new InletSelection(
                        DropInletType.TypeAMod,
                        len,
                        isDeep,
                        sideSuffix: null,
                        visibilityName: vis);

                    btn.Click += (s, e) =>
                    {
                        if (s is Button { Tag: InletSelection selection })
                        {
                            Selection = selection;
                            DialogResult = DialogResult.OK;
                            Close();
                        }
                    };

                    layout.Controls.Add(btn);
                }

                grp.Controls.Add(layout);
                return grp;
            }
        }

        // ----- TYPE C (USD 413) FORM -----

        private sealed class TypeCInletForm : Form
        {
            public InletSelection? Selection { get; private set; }

            public TypeCInletForm()
            {
                Text = "INSERT TYPE C INLET - USD 413";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterScreen;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;
                Width = 520;
                Height = 320;

                var mainLayout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    RowCount = 3,
                    ColumnCount = 1
                };
                mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
                mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

                var lblTitle = new Label
                {
                    Text = "SELECT INLET SIZE, SIDE, AND DEPTH",
                    Dock = DockStyle.Fill,
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter
                };

                var midPanel = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    RowCount = 1,
                    ColumnCount = 2,
                    Padding = new Padding(8)
                };
                midPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                midPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

                var grpUnder = CreateGroup("UNDER 8' DEEP   (< 8)", isDeep: false);
                var grpOver = CreateGroup("OVER  8' DEEP   (> 8)", isDeep: true);

                midPanel.Controls.Add(grpUnder, 0, 0);
                midPanel.Controls.Add(grpOver, 1, 0);

                var btnCancel = new Button
                {
                    Text = "CANCEL",
                    Dock = DockStyle.Right,
                    Width = 100,
                    Height = 28
                };
                btnCancel.Click += (s, e) =>
                {
                    Selection = null;
                    DialogResult = DialogResult.Cancel;
                    Close();
                };

                mainLayout.Controls.Add(lblTitle, 0, 0);
                mainLayout.Controls.Add(midPanel, 0, 1);
                mainLayout.Controls.Add(btnCancel, 0, 2);

                Controls.Add(mainLayout);
            }

            private GroupBox CreateGroup(string title, bool isDeep)
            {
                var grp = new GroupBox
                {
                    Text = title,
                    Dock = DockStyle.Fill
                };

                var layout = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight,
                    WrapContents = true,
                    AutoScroll = true,
                    Padding = new Padding(6)
                };

                int[] sizes = { 4, 5, 6, 7, 8, 9, 10, 11, 12 };
                string[] sides = { "R", "L" };

                foreach (int size in sizes)
                {
                    foreach (string side in sides)
                    {
                        string label = $"{size}{side}";
                        var btn = new Button
                        {
                            Text = label,
                            Width = 52,
                            Height = 28
                        };

                        string vis = $"{label} {(isDeep ? "> 8' DEEP" : "< 8' DEEP")}";

                        btn.Tag = new InletSelection(
                            DropInletType.TypeC,
                            size,
                            isDeep,
                            sideSuffix: side,
                            visibilityName: vis);

                        btn.Click += (s, e) =>
                        {
                            if (s is Button { Tag: InletSelection selection })
                            {
                                Selection = selection;
                                DialogResult = DialogResult.OK;
                                Close();
                            }
                        };

                        layout.Controls.Add(btn);
                    }
                }

                grp.Controls.Add(layout);
                return grp;
            }
        }

        // ----- TYPE CM (USD 422) FORM -----

        private sealed class TypeCMInletForm : Form
        {
            public InletSelection? Selection { get; private set; }

            public TypeCMInletForm()
            {
                Text = "INSERT TYPE CM INLET - USD 422";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterScreen;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;
                Width = 420;
                Height = 220;

                var mainLayout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    RowCount = 3,
                    ColumnCount = 1
                };
                mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
                mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

                var lblTitle = new Label
                {
                    Text = "SELECT INLET SIZE",
                    Dock = DockStyle.Fill,
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter
                };

                var grp = new GroupBox
                {
                    Text = "SIZE (FEET)",
                    Dock = DockStyle.Fill
                };

                var layout = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight,
                    WrapContents = true,
                    AutoScroll = true,
                    Padding = new Padding(6)
                };

                double[] sizes =
                {
                    2.5, 5.0, 7.5, 10.0,
                    12.5, 15.0, 17.5, 20.0
                };

                foreach (double size in sizes)
                {
                    string label = size.ToString("0.#");
                    var btn = new Button
                    {
                        Text = label,
                        Width = 60,
                        Height = 28
                    };

                    string vis = label; // matches visibility list

                    btn.Tag = new InletSelection(
                        DropInletType.TypeCM,
                        size,
                        isDeep: false,
                        sideSuffix: null,
                        visibilityName: vis);

                    btn.Click += (s, e) =>
                    {
                        if (s is Button { Tag: InletSelection selection })
                        {
                            Selection = selection;
                            DialogResult = DialogResult.OK;
                            Close();
                        }
                    };

                    layout.Controls.Add(btn);
                }

                grp.Controls.Add(layout);

                var btnCancel = new Button
                {
                    Text = "CANCEL",
                    Dock = DockStyle.Right,
                    Width = 100,
                    Height = 28
                };
                btnCancel.Click += (s, e) =>
                {
                    Selection = null;
                    DialogResult = DialogResult.Cancel;
                    Close();
                };

                mainLayout.Controls.Add(lblTitle, 0, 0);
                mainLayout.Controls.Add(grp, 0, 1);
                mainLayout.Controls.Add(btnCancel, 0, 2);

                Controls.Add(mainLayout);
            }
        }

        // ----- TYPE CM2 (USD 412.1) FORM -----

        private sealed class TypeCM2InletForm : Form
        {
            public InletSelection? Selection { get; private set; }

            public TypeCM2InletForm()
            {
                Text = "INSERT TYPE CM2 INLET - USD 412.1";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterScreen;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;
                Width = 520;
                Height = 280;

                var mainLayout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    RowCount = 3,
                    ColumnCount = 1
                };
                mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
                mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

                var lblTitle = new Label
                {
                    Text = "SELECT INLET SIZE AND DEPTH",
                    Dock = DockStyle.Fill,
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter
                };

                var midPanel = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    RowCount = 1,
                    ColumnCount = 2,
                    Padding = new Padding(8)
                };
                midPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                midPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

                var grpUnder = CreateGroup("UNDER 6' DEEP   (< 6)", isDeep: false);
                var grpOver = CreateGroup("OVER  6' DEEP   (> 6)", isDeep: true);

                midPanel.Controls.Add(grpUnder, 0, 0);
                midPanel.Controls.Add(grpOver, 1, 0);

                var btnCancel = new Button
                {
                    Text = "CANCEL",
                    Dock = DockStyle.Right,
                    Width = 100,
                    Height = 28
                };
                btnCancel.Click += (s, e) =>
                {
                    Selection = null;
                    DialogResult = DialogResult.Cancel;
                    Close();
                };

                mainLayout.Controls.Add(lblTitle, 0, 0);
                mainLayout.Controls.Add(midPanel, 0, 1);
                mainLayout.Controls.Add(btnCancel, 0, 2);

                Controls.Add(mainLayout);
            }

            private GroupBox CreateGroup(string title, bool isDeep)
            {
                var grp = new GroupBox
                {
                    Text = title,
                    Dock = DockStyle.Fill
                };

                var layout = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight,
                    WrapContents = true,
                    AutoScroll = true,
                    Padding = new Padding(6)
                };

                double[] sizes =
                {
                    2.5, 5.0, 7.5, 10.0,
                    12.5, 15.0, 17.5, 20.0
                };

                foreach (double size in sizes)
                {
                    string label = size.ToString("0.#");
                    var btn = new Button
                    {
                        Text = label,
                        Width = 60,
                        Height = 28
                    };

                    string vis = $"{label} {(isDeep ? "> 6' DEEP" : "< 6' DEEP")}";

                    btn.Tag = new InletSelection(
                        DropInletType.TypeCM2,
                        size,
                        isDeep,
                        sideSuffix: null,
                        visibilityName: vis);

                    btn.Click += (s, e) =>
                    {
                        if (s is Button { Tag: InletSelection selection })
                        {
                            Selection = selection;
                            DialogResult = DialogResult.OK;
                            Close();
                        }
                    };

                    layout.Controls.Add(btn);
                }

                grp.Controls.Add(layout);
                return grp;
            }
        }

        // ----- TYPE D (USD 414) FORM -----

        private sealed class TypeDInletForm : Form
        {
            public InletSelection? Selection { get; private set; }

            public TypeDInletForm()
            {
                Text = "INSERT TYPE D INLET - USD 414";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterScreen;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;
                Width = 520;
                Height = 320;

                var mainLayout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    RowCount = 3,
                    ColumnCount = 1
                };
                mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
                mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

                var lblTitle = new Label
                {
                    Text = "SELECT INLET SIZE, SIDE, AND DEPTH",
                    Dock = DockStyle.Fill,
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter
                };

                var midPanel = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    RowCount = 1,
                    ColumnCount = 2,
                    Padding = new Padding(8)
                };
                midPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                midPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

                var grpUnder = CreateGroup("UNDER 8' DEEP   (< 8)", isDeep: false);
                var grpOver = CreateGroup("OVER  8' DEEP   (> 8)", isDeep: true);

                midPanel.Controls.Add(grpUnder, 0, 0);
                midPanel.Controls.Add(grpOver, 1, 0);

                var btnCancel = new Button
                {
                    Text = "CANCEL",
                    Dock = DockStyle.Right,
                    Width = 100,
                    Height = 28
                };
                btnCancel.Click += (s, e) =>
                {
                    Selection = null;
                    DialogResult = DialogResult.Cancel;
                    Close();
                };

                mainLayout.Controls.Add(lblTitle, 0, 0);
                mainLayout.Controls.Add(midPanel, 0, 1);
                mainLayout.Controls.Add(btnCancel, 0, 2);

                Controls.Add(mainLayout);
            }

            private GroupBox CreateGroup(string title, bool isDeep)
            {
                var grp = new GroupBox
                {
                    Text = title,
                    Dock = DockStyle.Fill
                };

                var layout = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight,
                    WrapContents = true,
                    AutoScroll = true,
                    Padding = new Padding(6)
                };

                int[] sizes = { 4, 5, 6, 7, 8, 9, 10, 11, 12 };
                string[] sides = { "R", "L" };

                foreach (int size in sizes)
                {
                    foreach (string side in sides)
                    {
                        string label = $"{size}{side}";
                        var btn = new Button
                        {
                            Text = label,
                            Width = 52,
                            Height = 28
                        };

                        string vis = $"{label} {(isDeep ? "> 8' DEEP" : "< 8' DEEP")}";

                        btn.Tag = new InletSelection(
                            DropInletType.TypeD,
                            size,
                            isDeep,
                            sideSuffix: side,
                            visibilityName: vis);

                        btn.Click += (s, e) =>
                        {
                            if (s is Button { Tag: InletSelection selection })
                            {
                                Selection = selection;
                                DialogResult = DialogResult.OK;
                                Close();
                            }
                        };

                        layout.Controls.Add(btn);
                    }
                }

                grp.Controls.Add(layout);
                return grp;
            }
        }

        // ----- TYPE DM2 (USD 412.1) FORM -----

        private sealed class TypeDM2InletForm : Form
        {
            public InletSelection? Selection { get; private set; }

            public TypeDM2InletForm()
            {
                Text = "INSERT TYPE DM2 INLET - USD 412.1";
                FormBorderStyle = FormBorderStyle.FixedDialog;
                StartPosition = FormStartPosition.CenterScreen;
                MaximizeBox = false;
                MinimizeBox = false;
                ShowInTaskbar = false;
                Width = 520;
                Height = 280;

                var mainLayout = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    RowCount = 3,
                    ColumnCount = 1
                };
                mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
                mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

                var lblTitle = new Label
                {
                    Text = "SELECT INLET SIZE AND DEPTH",
                    Dock = DockStyle.Fill,
                    TextAlign = System.Drawing.ContentAlignment.MiddleCenter
                };

                var midPanel = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    RowCount = 1,
                    ColumnCount = 2,
                    Padding = new Padding(8)
                };
                midPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                midPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

                var grpUnder = CreateGroup("UNDER 6' DEEP   (< 6)", isDeep: false);
                var grpOver = CreateGroup("OVER  6' DEEP   (> 6)", isDeep: true);

                midPanel.Controls.Add(grpUnder, 0, 0);
                midPanel.Controls.Add(grpOver, 1, 0);

                var btnCancel = new Button
                {
                    Text = "CANCEL",
                    Dock = DockStyle.Right,
                    Width = 100,
                    Height = 28
                };
                btnCancel.Click += (s, e) =>
                {
                    Selection = null;
                    DialogResult = DialogResult.Cancel;
                    Close();
                };

                mainLayout.Controls.Add(lblTitle, 0, 0);
                mainLayout.Controls.Add(midPanel, 0, 1);
                mainLayout.Controls.Add(btnCancel, 0, 2);

                Controls.Add(mainLayout);
            }

            private GroupBox CreateGroup(string title, bool isDeep)
            {
                var grp = new GroupBox
                {
                    Text = title,
                    Dock = DockStyle.Fill
                };

                var layout = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight,
                    WrapContents = true,
                    AutoScroll = true,
                    Padding = new Padding(6)
                };

                double[] sizes =
                {
                    2.5, 5.0, 7.5, 10.0,
                    12.5, 15.0, 17.5, 20.0
                };

                foreach (double size in sizes)
                {
                    string label = size.ToString("0.#");
                    var btn = new Button
                    {
                        Text = label,
                        Width = 60,
                        Height = 28
                    };

                    string vis = $"{label} {(isDeep ? "> 6' DEEP" : "< 6' DEEP")}";

                    btn.Tag = new InletSelection(
                        DropInletType.TypeDM2,
                        size,
                        isDeep,
                        sideSuffix: null,
                        visibilityName: vis);

                    btn.Click += (s, e) =>
                    {
                        if (s is Button { Tag: InletSelection selection })
                        {
                            Selection = selection;
                            DialogResult = DialogResult.OK;
                            Close();
                        }
                    };

                    layout.Controls.Add(btn);
                }

                grp.Controls.Add(layout);
                return grp;
            }
        }
    }
}