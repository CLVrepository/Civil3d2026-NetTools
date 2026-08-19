using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;
using Autodesk.Civil.DatabaseServices.Styles;

using CLV_CivilTools.Shared;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;
using AcPolyline = Autodesk.AutoCAD.DatabaseServices.Polyline;
using AcPolyline3d = Autodesk.AutoCAD.DatabaseServices.Polyline3d;
using CogoPoint = Autodesk.Civil.DatabaseServices.CogoPoint;
using AcDbObject = Autodesk.AutoCAD.DatabaseServices.DBObject;

namespace CLV_CivilTools.Ufls
{
    /// <summary>
    /// Sewer lateral workflows.
    ///
    /// SINGLE behavior:
    /// - User selects ordered COGO shots from lot toward main.
    /// - Last selected point is used as the main geometry reference shot only.
    /// - Lateral shots are adjusted from top-of-pipe to centerline using the shared 4" PVC catalog value.
    /// - Main connection is projected in plan to the nearest main centerline on V-SURV-PIPE-CNTR.
    /// - A 3D centerline polyline is created on V-SURV-PIPE-LATR.
    /// - Pipe-network segments are added directly into the existing sewer network whose name contains -SSWR-E,
    ///   using family CLV_PVC and the 4" PVC size from that network's active parts list.
    /// - No temporary lateral network is created and the main pipe is not split.
    ///
    /// ALL behavior:
    /// - Finds WYE COGO shots automatically by description text.
    /// - For each WYE, derives a search line perpendicular to the nearest main centerline.
    /// - Extends that line a user-defined distance each side and gathers non-WYE shots within a user-defined search band from the line.
    /// - If shots fall on only one side of the WYE, it runs the same lateral creation logic automatically.
    /// - If shots fall on both sides of the WYE, it creates an aligned QA polygon on V-SURV-RDLN and skips auto-creation.
    /// </summary>
    public static class UflsLateralsCommands
    {
        private const string LAYER_LATERAL_3D = "V-SURV-PIPE-LATR";
        private const string LAYER_PICK_MARKER = "V-TEMP-PIPEPICK";
        private const string LAYER_MAIN_CENTERLINE = "V-SURV-PIPE-CNTR";
        private const string LAYER_REDLINE = "V-SURV-RDLN";

        private const string TargetNetworkSuffix = "-SSWR-E";
        private sealed class LateralMaterialConfig
        {
            public string FamilyName { get; }
            public string SizeToken { get; }
            public PipeMaterial CatalogMaterial { get; }
            public string Label { get; }

            public LateralMaterialConfig(string familyName, string sizeToken, PipeMaterial catalogMaterial, string label)
            {
                FamilyName = familyName;
                SizeToken = sizeToken;
                CatalogMaterial = catalogMaterial;
                Label = label;
            }
        }

        private static readonly LateralMaterialConfig PvcLateralConfig =
            new LateralMaterialConfig("CLV_PVC", "PVC", PipeMaterial.Pvc, "PVC");

        private static readonly LateralMaterialConfig C900LateralConfig =
            new LateralMaterialConfig("CLV_C900", "C900", PipeMaterial.C900, "C900");

        private const double DefaultAutoPerpHalfLengthFeet = 75.0;
        private const double DefaultAutoBandHalfWidthFeet = 2.0;
        private const double AutoQaPadAlongFeet = 2.0;
        private const double AutoQaPadAcrossFeet = 1.0;
        private const double SideToleranceFeet = 0.25;

        [CommandMethod("UFLS", "UFLS-LATERAL-SINGLE", CommandFlags.Modal)]
        public static void UflsLateralSingle()
        {
            RunLateralSingle(PvcLateralConfig, "UFLS-LATERAL-SINGLE");
        }

        [CommandMethod("UFLS", "UFLS-LATERAL-C900-SINGLE", CommandFlags.Modal)]
        public static void UflsLateralC900Single()
        {
            RunLateralSingle(C900LateralConfig, "UFLS-LATERAL-C900-SINGLE");
        }

        private static void RunLateralSingle(LateralMaterialConfig materialConfig, string commandName)
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            var markerIds = new List<ObjectId>();

            try
            {
                using (doc.LockDocument())
                using (var snap = SnapState.Capture())
                {
                    snap.Set(osnapZ: 0, osMode3d: 128);

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        EnsureLayer(db, tr, LAYER_PICK_MARKER, 2);
                        EnsureLayer(db, tr, LAYER_LATERAL_3D, 4);
                        tr.Commit();
                    }

                    List<Point3d> pickedPoints = CollectOrderedLateralPoints(ed, db, markerIds);
                    if (pickedPoints.Count < 2)
                    {
                        ed.WriteMessage($"\n{commandName}: Need at least 2 points. Command cancelled.");
                        return;
                    }

                    RunCreateSingleLateralWorkflow(db, ed, pickedPoints, markerIds, commandName, materialConfig);
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n{commandName} error: {ex.Message}");
                TryCleanupMarkers(db, markerIds);
            }
        }

        [CommandMethod("UFLS", "UFLS-LATERAL-ALL", CommandFlags.Modal)]
        public static void UflsLateralAll()
        {
            RunLateralAll(PvcLateralConfig, "UFLS-LATERAL-ALL");
        }

        [CommandMethod("UFLS", "UFLS-LATERAL-C900-ALL", CommandFlags.Modal)]
        public static void UflsLateralC900All()
        {
            RunLateralAll(C900LateralConfig, "UFLS-LATERAL-C900-ALL");
        }

        private static void RunLateralAll(LateralMaterialConfig materialConfig, string commandName)
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                using (doc.LockDocument())
                using (var snap = SnapState.Capture())
                {
                    snap.Set(osnapZ: 0, osMode3d: 128);

                    using (Transaction trLayers = db.TransactionManager.StartTransaction())
                    {
                        EnsureLayer(db, trLayers, LAYER_LATERAL_3D, 4);
                        EnsureLayer(db, trLayers, LAYER_REDLINE, 1);
                        trLayers.Commit();
                    }

                    if (!TryPromptForAutoBandHalfWidth(ed, out double searchHalfWidthFeet))
                        return;

                    if (!TryPromptForAutoSearchHalfLength(ed, out double searchHalfLengthFeet))
                        return;

                    using Transaction tr = db.TransactionManager.StartTransaction();

                    CivilDocument civDoc = CivilApplication.ActiveDocument;
                    if (civDoc == null)
                        throw new InvalidOperationException("CivilDocument is not available.");

                    List<CogoShot> allShots = GetAllCogoShots(tr, civDoc);
                    if (allShots.Count == 0)
                        throw new InvalidOperationException("No COGO points were found in the active drawing.");

                    List<CogoShot> wyeShots = allShots
                        .Where(IsWyeShot)
                        .OrderBy(s => s.PointNumber)
                        .ThenBy(s => s.Position.X)
                        .ThenBy(s => s.Position.Y)
                        .ToList();

                    if (wyeShots.Count == 0)
                        throw new InvalidOperationException("No WYE shots were found. Expected Raw or Full Description text containing 'WYE'.");

                    int createdLaterals = 0;
                    int createdPipes = 0;
                    int qaPolygons = 0;
                    int skippedNoGroup = 0;

                    HashSet<ObjectId> usedShotIds = new HashSet<ObjectId>();

                    foreach (CogoShot wye in wyeShots)
                    {
                        Point3d mainConnection = FindNearestMainConnectionPoint(tr, db, wye.Position, out ObjectId mainCurveId);
                        if (mainCurveId.IsNull)
                        {
                            ed.WriteMessage($"\n{commandName}: WYE {FormatShot(wye)} skipped. No main centerline found on {LAYER_MAIN_CENTERLINE}.");
                            skippedNoGroup++;
                            continue;
                        }

                        if (tr.GetObject(mainCurveId, OpenMode.ForRead, false) is not Curve mainCurve)
                        {
                            skippedNoGroup++;
                            continue;
                        }

                        Vector3d tangent = GetCurveTangentAtPoint(mainCurve, mainConnection);
                        Vector3d perp = GeometryUtils.PerpCCW(GeometryUtils.SafeTangentXY(tangent)).GetNormal();

                        List<CogoShot> candidates = CollectAutoLateralCandidates(
                            allShots,
                            wye,
                            perp,
                            usedShotIds,
                            searchHalfWidthFeet,
                            searchHalfLengthFeet);

                        if (candidates.Count == 0)
                        {
                            ed.WriteMessage($"\n{commandName}: WYE {FormatShot(wye)} skipped. No lateral shots found within {searchHalfWidthFeet:0.##}' of the search line.");
                            skippedNoGroup++;
                            continue;
                        }

                        bool hasPositive = candidates.Any(s => GetSignedOffsetAlongAxis(wye.Position, perp, s.Position) > SideToleranceFeet);
                        bool hasNegative = candidates.Any(s => GetSignedOffsetAlongAxis(wye.Position, perp, s.Position) < -SideToleranceFeet);

                        if (hasPositive && hasNegative)
                        {
                            CreateQaPolygonForShots(tr, db, wye, perp, candidates, searchHalfWidthFeet, searchHalfLengthFeet);
                            qaPolygons++;
                            ed.WriteMessage($"\n{commandName}: WYE {FormatShot(wye)} sent to QA on {LAYER_REDLINE}. Shots were found on both sides of the main.");
                            continue;
                        }

                        List<Point3d> orderedPoints = candidates
                            .OrderByDescending(s => Math.Abs(GetSignedOffsetAlongAxis(wye.Position, perp, s.Position)))
                            .ThenBy(s => s.PointNumber)
                            .Select(s => s.Position)
                            .ToList();

                        orderedPoints.Add(wye.Position);

                        int pipesForLateral = RunCreateSingleLateralWorkflow(
                            tr,
                            db,
                            ed,
                            orderedPoints,
                            commandLabel: commandName,
                            materialConfig: materialConfig);

                        createdLaterals++;
                        createdPipes += pipesForLateral;

                        foreach (CogoShot shot in candidates)
                            usedShotIds.Add(shot.Id);
                    }

                    tr.Commit();

                    ed.WriteMessage(
                        $"\n{commandName} complete. Search width {searchHalfWidthFeet:0.##}' each side. Created {createdLaterals} lateral(s), added {createdPipes} pipe segment(s), " +
                        $"flagged {qaPolygons} QA polygon(s), skipped {skippedNoGroup} WYE shot(s) with no usable auto group.");
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n{commandName} error: {ex.Message}");
            }
        }

        private static int RunCreateSingleLateralWorkflow(
            Database db,
            Editor ed,
            List<Point3d> pickedPoints,
            List<ObjectId> markerIds,
            string commandLabel,
            LateralMaterialConfig materialConfig)
        {
            using Transaction tr = db.TransactionManager.StartTransaction();
            int createdCount = RunCreateSingleLateralWorkflow(tr, db, ed, pickedPoints, commandLabel, materialConfig);
            EraseMarkers(tr, markerIds);
            tr.Commit();
            return createdCount;
        }

        private static int RunCreateSingleLateralWorkflow(
            Transaction tr,
            Database db,
            Editor ed,
            List<Point3d> pickedPoints,
            string commandLabel,
            LateralMaterialConfig materialConfig)
        {
            if (pickedPoints.Count < 2)
                throw new InvalidOperationException("Need at least 2 points to create a lateral.");

            Point3d mainReferencePoint = pickedPoints[pickedPoints.Count - 1];
            List<Point3d> lateralShotPoints = pickedPoints.Take(pickedPoints.Count - 1).ToList();
            List<Point3d> adjustedLateralPoints = ApplyFourInchTopToCenterAdjustment(lateralShotPoints, materialConfig.CatalogMaterial);

            Point3d connectedPoint = FindNearestMainConnectionPoint(tr, db, mainReferencePoint, out ObjectId mainCurveId);
            if (mainCurveId.IsNull)
                throw new InvalidOperationException(
                    $"No main centerline found on layer {LAYER_MAIN_CENTERLINE} near the final picked main shot.");

            List<Point3d> finalPts = new List<Point3d>(adjustedLateralPoints);
            if (finalPts.Count == 0)
                throw new InvalidOperationException("No lateral shots were collected before the main reference point.");

            Point3d lastLateralPoint = finalPts[finalPts.Count - 1];
            if (Distance3d(lastLateralPoint, connectedPoint) > 1e-6)
                finalPts.Add(connectedPoint);

            ObjectId polylineId = CreateLateralCenterlinePolyline(tr, db, finalPts);
            int createdPipeCount = CreatePipeNetworkSegments(tr, db, ed, finalPts, materialConfig, commandLabel);

            ed.WriteMessage(
                $"\n{commandLabel}: Created 3D lateral centerline ({polylineId.Handle}) and {createdPipeCount} pipe network segment(s). " +
                $"The lateral was added directly into the existing sewer network matching *{TargetNetworkSuffix}* without splitting the main pipe.");

            return createdPipeCount;
        }

        private static List<Point3d> CollectOrderedLateralPoints(Editor ed, Database db, List<ObjectId> markerIds)
        {
            var pts = new List<Point3d>();
            ed.WriteMessage(
                "\nSelect sewer lateral COGO shots in order from the lot toward the main. " +
                "The LAST selection should be the main reference shot. Press Enter when done.");

            double markerRadius = 0.25;
            bool first = true;

            while (true)
            {
                PromptEntityOptions peo = new PromptEntityOptions(
                    first
                        ? "\nSelect first lateral COGO shot <Enter to cancel>: "
                        : "\nSelect next COGO shot (last selection = main reference) <Enter to finish>: ");

                peo.SetRejectMessage("\nOnly COGO points are allowed.");
                peo.AddAllowedClass(typeof(CogoPoint), exactMatch: false);

                PromptEntityResult per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                    break;

                using Transaction tr = db.TransactionManager.StartTransaction();
                if (tr.GetObject(per.ObjectId, OpenMode.ForRead, false) is not CogoPoint cp)
                    break;

                Point3d pt = cp.Location;
                pts.Add(pt);
                AddPickMarker(db, markerIds, pt, pts.Count, markerRadius);
                tr.Commit();
                first = false;
            }

            return pts;
        }

        private static List<CogoShot> GetAllCogoShots(Transaction tr, CivilDocument civDoc)
        {
            var shots = new List<CogoShot>();

            foreach (ObjectId id in civDoc.CogoPoints)
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is not CogoPoint cp)
                    continue;

                shots.Add(new CogoShot
                {
                    Id = id,
                    PointNumber = cp.PointNumber,
                    Position = cp.Location,
                    RawDescription = cp.RawDescription ?? string.Empty,
                    FullDescription = cp.FullDescription ?? string.Empty
                });
            }

            return shots;
        }

        private static bool IsWyeShot(CogoShot shot)
        {
            string raw = shot.RawDescription.ToUpperInvariant();
            string full = shot.FullDescription.ToUpperInvariant();
            return raw.Contains("WYE") || full.Contains("WYE");
        }


        private static bool TryPromptForAutoBandHalfWidth(Editor ed, out double searchHalfWidthFeet)
        {
            PromptDoubleOptions opts = new PromptDoubleOptions(
                $"\nSearch half-width from perpendicular line <{DefaultAutoBandHalfWidthFeet:0.##}'>: ")
            {
                DefaultValue = DefaultAutoBandHalfWidthFeet,
                UseDefaultValue = true,
                AllowNegative = false,
                AllowZero = false,
                AllowNone = true
            };

            PromptDoubleResult res = ed.GetDouble(opts);
            if (res.Status == PromptStatus.Cancel)
            {
                searchHalfWidthFeet = DefaultAutoBandHalfWidthFeet;
                ed.WriteMessage("\nUFLS-LATERAL-ALL cancelled.");
                return false;
            }

            searchHalfWidthFeet = res.Status == PromptStatus.OK
                ? res.Value
                : DefaultAutoBandHalfWidthFeet;

            return true;
        }


        private static bool TryPromptForAutoSearchHalfLength(Editor ed, out double searchHalfLengthFeet)
        {
            PromptDoubleOptions opts = new PromptDoubleOptions(
                $"\nSearch line half-length from WYE <{DefaultAutoPerpHalfLengthFeet:0.##}'>: ")
            {
                DefaultValue = DefaultAutoPerpHalfLengthFeet,
                UseDefaultValue = true,
                AllowNegative = false,
                AllowZero = false,
                AllowNone = true
            };

            PromptDoubleResult res = ed.GetDouble(opts);
            if (res.Status == PromptStatus.Cancel)
            {
                searchHalfLengthFeet = DefaultAutoPerpHalfLengthFeet;
                ed.WriteMessage("\nUFLS-LATERAL-ALL cancelled.");
                return false;
            }

            searchHalfLengthFeet = res.Status == PromptStatus.OK
                ? res.Value
                : DefaultAutoPerpHalfLengthFeet;

            return true;
        }

        private static List<CogoShot> CollectAutoLateralCandidates(
            List<CogoShot> allShots,
            CogoShot wye,
            Vector3d axis,
            HashSet<ObjectId> usedShotIds,
            double searchHalfWidthFeet,
            double searchHalfLengthFeet)
        {
            var candidates = new List<CogoShot>();

            foreach (CogoShot shot in allShots)
            {
                if (shot.Id == wye.Id)
                    continue;

                if (usedShotIds.Contains(shot.Id))
                    continue;

                if (IsWyeShot(shot))
                    continue;

                double along = GetSignedOffsetAlongAxis(wye.Position, axis, shot.Position);
                if (Math.Abs(along) > searchHalfLengthFeet)
                    continue;

                double cross = GetPerpendicularOffsetFromAxis(wye.Position, axis, shot.Position);
                if (cross > searchHalfWidthFeet)
                    continue;

                candidates.Add(shot);
            }

            return candidates;
        }

        private static List<Point3d> ApplyFourInchTopToCenterAdjustment(List<Point3d> pts, PipeMaterial material)
        {
            PipeSize size = material switch
            {
                PipeMaterial.C900 => PipeCatalog.C900Sizes.First(s => s.NominalInches == 4),
                _ => PipeCatalog.PvcSizes.First(s => s.NominalInches == 4)
            };
            double outerRadiusFt = PipeCatalog.GetOuterRadiusFeet(size);

            var adjusted = new List<Point3d>(pts.Count);
            foreach (Point3d pt in pts)
                adjusted.Add(new Point3d(pt.X, pt.Y, pt.Z - outerRadiusFt));

            return adjusted;
        }

        private static ObjectId CreateLateralCenterlinePolyline(Transaction tr, Database db, List<Point3d> finalPts)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            Point3dCollection plPts = new Point3dCollection();
            foreach (Point3d pt in finalPts)
                plPts.Add(pt);

            AcPolyline3d pl3d = new AcPolyline3d(Poly3dType.SimplePoly, plPts, false);
            pl3d.SetDatabaseDefaults();
            pl3d.Layer = LAYER_LATERAL_3D;
            ms.AppendEntity(pl3d);
            tr.AddNewlyCreatedDBObject(pl3d, true);
            return pl3d.ObjectId;
        }

        private static int CreatePipeNetworkSegments(Transaction tr, Database db, Editor ed, List<Point3d> finalPts, LateralMaterialConfig materialConfig, string commandLabel)
        {
            if (finalPts.Count < 2)
                return 0;

            CivilDocument civDoc = CivilApplication.ActiveDocument;
            if (civDoc == null)
                throw new InvalidOperationException("CivilDocument is not available.");

            ObjectId targetNetworkId = FindTargetSewerNetworkId(tr, civDoc);
            if (targetNetworkId.IsNull)
                throw new InvalidOperationException(
                    $"Could not find the target sewer network. Expected one network whose name contains {TargetNetworkSuffix}.");

            object targetNetwork = tr.GetObject(targetNetworkId, OpenMode.ForWrite, false);
            string networkName = GetObjectName(targetNetwork);

            (ObjectId familyId, ObjectId sizeId, string familyName, string sizeName) = ResolveLateralPartIds(tr, targetNetwork, materialConfig);

            MethodInfo? addLinePipeMethod = targetNetwork.GetType().GetMethod(
                "AddLinePipe",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(ObjectId), typeof(ObjectId), typeof(LineSegment3d), typeof(ObjectId).MakeByRefType(), typeof(bool) },
                modifiers: null);

            if (addLinePipeMethod == null)
                throw new InvalidOperationException("Could not resolve Network.AddLinePipe(...) on the target Civil 3D network object.");

            int createdCount = 0;
            for (int i = 1; i < finalPts.Count; i++)
            {
                Point3d start = finalPts[i - 1];
                Point3d end = finalPts[i];
                if (Distance3d(start, end) <= 1e-6)
                    continue;

                LineSegment3d line = new LineSegment3d(start, end);
                object[] args = new object[] { familyId, sizeId, line, ObjectId.Null, false };
                addLinePipeMethod.Invoke(targetNetwork, args);

                if (args[3] is ObjectId newPipeId && !newPipeId.IsNull)
                    createdCount++;
            }

            if (createdCount == 0)
            {
                throw new InvalidOperationException(
                    $"No lateral pipes were created in network '{networkName}'.");
            }

            ed.WriteMessage(
                $"\n{commandLabel}: Added {createdCount} segment(s) to network '{networkName}' using family '{familyName}' and size '{sizeName}'.");

            return createdCount;
        }

        private static ObjectId FindTargetSewerNetworkId(Transaction tr, CivilDocument civDoc)
        {
            ObjectIdCollection networkIds = civDoc.GetPipeNetworkIds();
            if (networkIds.Count == 0)
                return ObjectId.Null;

            var candidates = new List<(ObjectId id, string name)>();
            foreach (ObjectId id in networkIds)
            {
                object networkObj = tr.GetObject(id, OpenMode.ForWrite, false);
                string name = GetObjectName(networkObj);
                if (name.IndexOf(TargetNetworkSuffix, StringComparison.OrdinalIgnoreCase) >= 0)
                    candidates.Add((id, name));
            }

            if (candidates.Count == 1)
                return candidates[0].id;

            if (candidates.Count > 1)
            {
                Regex rx = new Regex(@"^[A-Z]\d{2}-\d{5}-SSWR-E$", RegexOptions.IgnoreCase);
                List<(ObjectId id, string name)> strict = candidates.Where(n => rx.IsMatch(n.name)).ToList();
                if (strict.Count == 1)
                    return strict[0].id;
            }

            if (networkIds.Count == 1)
                return networkIds[0];

            return ObjectId.Null;
        }

        private static (ObjectId familyId, ObjectId sizeId, string familyName, string sizeName) ResolveLateralPartIds(Transaction tr, object network, LateralMaterialConfig materialConfig)
        {
            ObjectId partsListId = GetObjectIdProperty(network, "PartsListId");
            string networkName = GetObjectName(network);

            if (partsListId.IsNull)
                throw new InvalidOperationException(
                    $"Network '{networkName}' does not have a valid Parts List assigned.");

            PartsList? partsList = tr.GetObject(partsListId, OpenMode.ForRead, false) as PartsList;
            if (partsList == null)
                throw new InvalidOperationException(
                    $"Could not open Parts List for network '{networkName}'.");

            const int targetNominalInches = 4;

            ObjectIdCollection pipeFamilies = partsList.GetPartFamilyIdsByDomain(DomainType.Pipe);
            foreach (ObjectId familyId in pipeFamilies)
            {
                if (tr.GetObject(familyId, OpenMode.ForRead, false) is not PartFamily family)
                    continue;

                string familyName = family.Name ?? string.Empty;
                if (!FamilyMatches(familyName, materialConfig))
                    continue;

                for (int i = 0; i < family.PartSizeCount; i++)
                {
                    ObjectId sizeId = family[i];
                    if (sizeId.IsNull)
                        continue;

                    AcDbObject? sizeObj = tr.GetObject(sizeId, OpenMode.ForRead, false);
                    string sizeName = GetBestPartSizeName(sizeObj);
                    if (PartSizeMatches(sizeName, materialConfig, targetNominalInches))
                        return (familyId, sizeId, familyName, sizeName);
                }

                if (family.PartSizeCount == 1)
                {
                    ObjectId onlySizeId = family[0];
                    AcDbObject? onlySizeObj = tr.GetObject(onlySizeId, OpenMode.ForRead, false);
                    string onlySizeName = GetBestPartSizeName(onlySizeObj);
                    return (familyId, onlySizeId, familyName, onlySizeName);
                }
            }

            throw new InvalidOperationException(
                $"Could not find pipe family '{materialConfig.FamilyName}' with a 4-inch size matching '{materialConfig.SizeToken}' in the target sewer network parts list. " +
                "The lookup accepts names like 4\" C900, 4 in, or 4-inch. Add that family / size to the drawing template or assigned network parts list and retry.");
        }

        private static bool FamilyMatches(string familyName, LateralMaterialConfig materialConfig)
        {
            if (string.IsNullOrWhiteSpace(familyName))
                return false;

            if (familyName.Equals(materialConfig.FamilyName, StringComparison.OrdinalIgnoreCase) ||
                familyName.IndexOf(materialConfig.FamilyName, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            return familyName.IndexOf(materialConfig.SizeToken, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool PartSizeMatches(string sizeName, LateralMaterialConfig materialConfig, int targetNominalInches)
        {
            int nominalInches = ParseNominalInches(sizeName);
            if (nominalInches > 0 && nominalInches != targetNominalInches)
                return false;

            bool hasMaterialToken = sizeName.IndexOf(materialConfig.SizeToken, StringComparison.OrdinalIgnoreCase) >= 0;
            if (hasMaterialToken)
                return nominalInches == 0 || nominalInches == targetNominalInches;

            return nominalInches == targetNominalInches;
        }

        private static int ParseNominalInches(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            Match m = Regex.Match(text, @"(?<!\d)(\d{1,3})(?:\.0+)?\s*(?:""|INCH|IN|')", RegexOptions.IgnoreCase);
            if (!m.Success)
                m = Regex.Match(text, @"(?<!\d)(\d{1,3})(?:\.0+)?(?!\d)");

            if (!m.Success)
                return 0;

            return int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int inches)
                ? inches
                : 0;
        }

        private static string GetObjectName(object? obj)
        {
            if (obj == null)
                return string.Empty;

            PropertyInfo? prop = obj.GetType().GetProperty("Name", BindingFlags.Instance | BindingFlags.Public);
            if (prop == null)
                return string.Empty;

            return Convert.ToString(prop.GetValue(obj), CultureInfo.InvariantCulture) ?? string.Empty;
        }

        private static ObjectId GetObjectIdProperty(object obj, string propertyName)
        {
            PropertyInfo? prop = obj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (prop == null)
                return ObjectId.Null;

            object? value = prop.GetValue(obj);
            return value is ObjectId id ? id : ObjectId.Null;
        }

        private static string GetBestPartSizeName(AcDbObject? sizeObj)
        {
            if (sizeObj == null)
                return string.Empty;

            foreach (string propertyName in new[] { "Name", "DisplayName", "Description", "PartSizeName" })
            {
                PropertyInfo? pi = sizeObj.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
                if (pi == null || pi.PropertyType != typeof(string))
                    continue;

                string? value = pi.GetValue(sizeObj) as string;
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            return sizeObj.GetType().Name;
        }

        private static List<ObjectId> GetMainCenterlineIds(Transaction tr, Database db)
        {
            var ids = new List<ObjectId>();

            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            foreach (ObjectId id in ms)
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is Curve curve &&
                    string.Equals(curve.Layer, LAYER_MAIN_CENTERLINE, StringComparison.OrdinalIgnoreCase))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        private static Point3d FindNearestMainConnectionPoint(Transaction tr, Database db, Point3d reference, out ObjectId mainCurveId)
        {
            mainCurveId = ObjectId.Null;
            Point3d bestPoint = Point3d.Origin;
            double bestDist = double.MaxValue;

            foreach (ObjectId id in GetMainCenterlineIds(tr, db))
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is not Curve curve)
                    continue;

                Point3d nearestPlan = GetClosestPointProjectedToPlan(curve, reference);
                double dist = Distance2d(reference, nearestPlan);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestPoint = nearestPlan;
                    mainCurveId = id;
                }
            }

            return bestPoint;
        }

        private static Point3d GetClosestPointProjectedToPlan(Curve curve, Point3d reference)
        {
            Point3d projected = curve.GetClosestPointTo(reference, Vector3d.ZAxis, false);

            try
            {
                double param = curve.GetParameterAtPoint(projected);
                Point3d curvePoint = curve.GetPointAtParameter(param);
                return new Point3d(projected.X, projected.Y, curvePoint.Z);
            }
            catch
            {
                return projected;
            }
        }

        private static Vector3d GetCurveTangentAtPoint(Curve curve, Point3d pointOnCurve)
        {
            try
            {
                double param = curve.GetParameterAtPoint(pointOnCurve);
                Vector3d deriv = curve.GetFirstDerivative(pointOnCurve);
                if (deriv.Length > 1e-9)
                    return deriv;

                Point3d paramPoint = curve.GetPointAtParameter(param);
                deriv = curve.GetFirstDerivative(paramPoint);
                if (deriv.Length > 1e-9)
                    return deriv;
            }
            catch
            {
                // fall through
            }

            try
            {
                Point3d start = curve.StartPoint;
                Point3d end = curve.EndPoint;
                Vector3d chord = end - start;
                if (chord.Length > 1e-9)
                    return chord;
            }
            catch
            {
                // ignored
            }

            return Vector3d.XAxis;
        }

        private static double Distance2d(Point3d a, Point3d b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        private static double Distance3d(Point3d a, Point3d b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            double dz = a.Z - b.Z;
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }

        private static double GetSignedOffsetAlongAxis(Point3d origin, Vector3d axis, Point3d point)
        {
            Vector3d delta = point - origin;
            return delta.X * axis.X + delta.Y * axis.Y;
        }

        private static double GetPerpendicularOffsetFromAxis(Point3d origin, Vector3d axis, Point3d point)
        {
            Vector3d delta = point - origin;
            double along = delta.X * axis.X + delta.Y * axis.Y;
            double px = delta.X - (along * axis.X);
            double py = delta.Y - (along * axis.Y);
            return Math.Sqrt((px * px) + (py * py));
        }

        private static void CreateQaPolygonForShots(
            Transaction tr,
            Database db,
            CogoShot wye,
            Vector3d axis,
            List<CogoShot> candidates,
            double searchHalfWidthFeet,
            double searchHalfLengthFeet)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            double minAlong = 0.0;
            double maxAlong = 0.0;
            double maxCross = 0.0;

            foreach (CogoShot shot in candidates)
            {
                double along = GetSignedOffsetAlongAxis(wye.Position, axis, shot.Position);
                double cross = GetPerpendicularOffsetFromAxis(wye.Position, axis, shot.Position);

                if (along < minAlong)
                    minAlong = along;
                if (along > maxAlong)
                    maxAlong = along;
                if (cross > maxCross)
                    maxCross = cross;
            }

            minAlong -= AutoQaPadAlongFeet;
            maxAlong += AutoQaPadAlongFeet;
            double halfWidth = Math.Max(searchHalfWidthFeet, maxCross) + AutoQaPadAcrossFeet;

            Vector3d perp = GeometryUtils.PerpCCW(axis).GetNormal();

            Point3d c1 = OffsetPoint(wye.Position, axis, minAlong, perp, -halfWidth);
            Point3d c2 = OffsetPoint(wye.Position, axis, maxAlong, perp, -halfWidth);
            Point3d c3 = OffsetPoint(wye.Position, axis, maxAlong, perp, halfWidth);
            Point3d c4 = OffsetPoint(wye.Position, axis, minAlong, perp, halfWidth);

            AcPolyline qa = GeometryUtils.MakeRectFromCorners(c1, c2, c3, c4, LAYER_REDLINE);
            qa.Elevation = 0.0;
            ms.AppendEntity(qa);
            tr.AddNewlyCreatedDBObject(qa, true);
        }

        private static Point3d OffsetPoint(Point3d origin, Vector3d axis, double along, Vector3d crossAxis, double cross)
        {
            return new Point3d(
                origin.X + (axis.X * along) + (crossAxis.X * cross),
                origin.Y + (axis.Y * along) + (crossAxis.Y * cross),
                0.0);
        }

        private static string FormatShot(CogoShot shot)
        {
            return $"#{shot.PointNumber}";
        }

        private static void AddPickMarker(Database db, List<ObjectId> markerIds, Point3d center, int index, double radius)
        {
            using Transaction tr = db.TransactionManager.StartTransaction();
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            Circle circle = new Circle(center, Vector3d.ZAxis, radius)
            {
                Layer = LAYER_PICK_MARKER
            };
            ms.AppendEntity(circle);
            tr.AddNewlyCreatedDBObject(circle, true);
            markerIds.Add(circle.ObjectId);

            DBText txt = new DBText
            {
                Position = new Point3d(center.X + radius * 1.25, center.Y + radius * 1.25, center.Z),
                Height = radius * 1.2,
                TextString = index.ToString(CultureInfo.InvariantCulture),
                Layer = LAYER_PICK_MARKER
            };
            ms.AppendEntity(txt);
            tr.AddNewlyCreatedDBObject(txt, true);
            markerIds.Add(txt.ObjectId);

            tr.Commit();
        }

        private static void EraseMarkers(Transaction tr, List<ObjectId> markerIds)
        {
            foreach (ObjectId id in markerIds)
            {
                if (!id.IsNull && id.IsValid && !id.IsErased)
                {
                    if (tr.GetObject(id, OpenMode.ForWrite, false) is AcEntity ent)
                        ent.Erase();
                }
            }
        }

        private static void TryCleanupMarkers(Database db, List<ObjectId> markerIds)
        {
            try
            {
                using Transaction tr = db.TransactionManager.StartTransaction();
                EraseMarkers(tr, markerIds);
                tr.Commit();
            }
            catch
            {
                // ignore cleanup errors
            }
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

        private sealed class CogoShot
        {
            public ObjectId Id { get; init; }
            public uint PointNumber { get; init; }
            public Point3d Position { get; init; }
            public string RawDescription { get; init; } = string.Empty;
            public string FullDescription { get; init; } = string.Empty;
        }
    }
}
