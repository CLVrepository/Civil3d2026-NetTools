using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Civil.DatabaseServices;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;
using AcBlockReference = Autodesk.AutoCAD.DatabaseServices.BlockReference;
using AcDbPoint = Autodesk.AutoCAD.DatabaseServices.DBPoint;
using CogoPoint = Autodesk.Civil.DatabaseServices.CogoPoint;
using AcDbObject = Autodesk.AutoCAD.DatabaseServices.DBObject;
using WinButton = System.Windows.Forms.Button;
using WinLabel = System.Windows.Forms.Label;
using WinFlowDirection = System.Windows.Forms.FlowDirection;

namespace CLV_CivilTools.Survey
{
    /// <summary>
    /// Rigid 2D best-fit registration for survey-map overlays.
    ///
    /// Workflow:
    /// - User selects the xref / block reference to move.
    /// - User selects fixed survey-shot points one at a time.
    /// - For each survey shot, user picks / selects the matching point on the moving map linework.
    /// - User can review the proposed fit in a dialog before applying it.
    /// - The review dialog supports CONTROL toggles so users can exclude bad shots from the fit while still comparing all selected pairs.
    /// - A best-fit rotation + translation is computed from moving-map points to fixed survey points.
    /// - The selected block reference is moved / rotated in XY only. No scaling is applied.
    /// - A CSV residual report is written to disk and a summary is echoed to the command line.
    /// </summary>
    public static class SurveyBestFitMapCommands
    {
        private const string MarkerLayer = "V-TEMP-PIPEPICK";
        private const string ReportLayer = "V-SURV-CHCK";
        private const string DefaultReportPrefix = "SURVEY_BestFitMapReport";
        private const string SessionRecordName = "CLV_MAP_TRANSFORM_SESSION";
        private const int SessionFormatVersion = 1;
        private const int XrecordBinaryChunkSize = 127;
        private const string MapTransformRevision = "2026.08.06-HISTORY-R3";

        private enum ApplyDirection
        {
            Forward,
            Reverse
        }

        private enum ReviewAction
        {
            Cancel,
            Finalize,
            AddPair
        }

        private sealed class ControlPair
        {
            public int Index { get; set; }
            public ObjectId SurveyEntityId { get; set; }
            public string SurveyEntityType { get; set; } = string.Empty;
            public string SurveyLabel { get; set; } = string.Empty;
            public string MapLabel { get; set; } = string.Empty;
            public Point3d SurveyPoint { get; set; }
            public Point3d MapPoint { get; set; }
            public Point3d TransformedMapPoint { get; set; }
            public double Dx { get; set; }
            public double Dy { get; set; }
            public double Residual { get; set; }
            public bool UseForCalc { get; set; } = true;
            public bool UseForReference { get; set; } = true;
        }

        private sealed class TransformSession
        {
            public int Version { get; set; } = SessionFormatVersion;
            public double OriginalPositionX { get; set; }
            public double OriginalPositionY { get; set; }
            public double OriginalPositionZ { get; set; }
            public double OriginalRotation { get; set; }
            public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
            public List<SessionPair> Pairs { get; set; } = new List<SessionPair>();
        }

        private sealed class SessionPair
        {
            public int Index { get; set; }
            public string SurveyEntityHandle { get; set; } = string.Empty;
            public string SurveyEntityType { get; set; } = string.Empty;
            public string SurveyLabel { get; set; } = string.Empty;
            public string MapLabel { get; set; } = string.Empty;
            public double SurveyX { get; set; }
            public double SurveyY { get; set; }
            public double MapX { get; set; }
            public double MapY { get; set; }
            public bool UseForCalc { get; set; } = true;
        }

        private sealed class BlockPlacement
        {
            public Point3d Position { get; set; }
            public double Rotation { get; set; }
        }

        private sealed class FitResult
        {
            public string Name { get; set; } = string.Empty;
            public double RotationRadians { get; set; }
            public Vector3d Translation { get; set; } = new Vector3d(0.0, 0.0, 0.0);
            public Point3d SourceCentroid { get; set; }
            public Point3d TargetCentroid { get; set; }
            public double RmsError { get; set; }
            public double MaxError { get; set; }
            public int CalcCount { get; set; }
            public int ReferenceCount { get; set; }
        }

        private sealed class ReviewRow
        {
            public int OriginalIndex { get; set; }
            public int Index { get; set; }
            public bool Control { get; set; } = true;
            public string SurveyPointNumber { get; set; } = string.Empty;
            public string MapPointNumber { get; set; } = string.Empty;
            public double Dx { get; set; }
            public double Dy { get; set; }
            public double Residual { get; set; }
        }

        private sealed class ReviewDialogResult
        {
            public ReviewAction Action { get; set; } = ReviewAction.Cancel;
            public bool Accepted { get; set; }
            public ApplyDirection Direction { get; set; }
            public FitResult? AppliedFit { get; set; }
            public List<ControlPair> Pairs { get; set; } = new List<ControlPair>();
        }

        [CommandMethod("SURVEY-BESTFIT-MAP", CommandFlags.Modal)]
        [CommandMethod("UFLS", "UFLS-BESTFIT-MAP", CommandFlags.Modal)]
        public static void SurveyBestFitMap()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;
            var markerIds = new List<ObjectId>();
            ObjectId targetBlockId = ObjectId.Null;
            BlockPlacement? recalledPlacement = null;
            bool recalledSessionActive = false;

            ed.WriteMessage($"\nMAP TRANSFORM revision {MapTransformRevision}.");

            try
            {
                using (doc.LockDocument())
                {
                    using (Transaction trLayers = db.TransactionManager.StartTransaction())
                    {
                        EnsureLayer(db, trLayers, MarkerLayer, 2);
                        EnsureLayer(db, trLayers, ReportLayer, 3);
                        trLayers.Commit();
                    }

                    targetBlockId = PromptForTargetBlockReference(ed);
                    if (targetBlockId.IsNull)
                        return;

                    TransformSession? session = LoadTransformSession(db, targetBlockId);
                    List<ControlPair> pairs;

                    if (session != null && session.Pairs.Count >= 2)
                    {
                        pairs = ConvertSessionPairsToControlPairs(db, session);
                        recalledPlacement = RestoreTargetToOriginalForEdit(db, targetBlockId, session);
                        recalledSessionActive = true;
                        RebuildPairMarkers(db, pairs, markerIds);
                        ed.Regen();
                        ed.WriteMessage($"\nMAP TRANSFORM: Recalled {pairs.Count} saved point pair(s). The map has been temporarily restored to its original pre-transform position for editing.");
                        ed.WriteMessage("\nMAP TRANSFORM: Numbered pair markers have been restored. Cancel returns the map to its prior transformed position; Apply Updated Transform calculates from the original position.");
                    }
                    else
                    {
                        session = CreateNewTransformSession(db, targetBlockId);
                        pairs = CollectControlPairs(db, ed, markerIds);
                        if (pairs.Count < 2)
                        {
                            ed.WriteMessage("\nSURVEY-BESTFIT-MAP: Need at least 2 point pairs. Command cancelled.");
                            return;
                        }
                    }

                    while (true)
                    {
                        ReviewDialogResult review = ShowReviewDialog(pairs);

                        if (review.Action == ReviewAction.AddPair)
                        {
                            pairs = review.Pairs;
                            RebuildPairMarkers(db, pairs, markerIds);
                            ed.Regen();
                            try
                            {
                                ControlPair? addedPair = CollectAdditionalPair(db, ed, pairs.Count + 1, markerIds);
                                if (addedPair != null)
                                {
                                    pairs.Add(addedPair);
                                    RenumberPairs(pairs);
                                }
                            }
                            catch (OperationCanceledException)
                            {
                                ed.WriteMessage("\nAdd Pair cancelled. Returning to Map Transform review.");
                            }
                            continue;
                        }

                        if (!review.Accepted || review.Action != ReviewAction.Finalize || review.AppliedFit == null)
                        {
                            if (recalledSessionActive && recalledPlacement != null)
                                RestoreBlockPlacement(db, targetBlockId, recalledPlacement);

                            ed.WriteMessage("\nSURVEY-BESTFIT-MAP cancelled in review dialog. Previous map placement restored.");
                            TryCleanupMarkers(db, markerIds);
                            return;
                        }

                        pairs = review.Pairs;
                        FitResult appliedFit = review.AppliedFit;

                        using (Transaction tr = db.TransactionManager.StartTransaction())
                        {
                            AcBlockReference blockRef = (AcBlockReference)tr.GetObject(targetBlockId, OpenMode.ForWrite);
                            ValidateBlockReferenceForRigidPlanFit(blockRef);

                            ApplyFitFromOriginalBlockState(blockRef, appliedFit, session);
                            SaveTransformSession(tr, blockRef, session, pairs);

                            EraseMarkers(tr, markerIds);
                            tr.Commit();
                        }
                        recalledSessionActive = false;

                        TransformSession? verifySession = LoadTransformSession(db, targetBlockId);
                        if (verifySession == null || verifySession.Pairs.Count != pairs.Count)
                        {
                            ed.WriteMessage("\nMAP TRANSFORM HISTORY ERROR: Transform completed, but the editable point-pair session could not be read back from the selected map.");
                            ed.WriteMessage("\nDo not rely on history for this transform. Report this message before continuing testing.");
                        }
                        else
                        {
                            ed.WriteMessage($"\nSaved and verified editable Map Transform history with {verifySession.Pairs.Count} point pair(s).");
                        }

                        PopulateResiduals(pairs, appliedFit, review.Direction);
                        string reportPath = WriteCsvReport(doc, pairs, appliedFit, review.Direction);

                        ed.WriteMessage(
                            $"\nSURVEY-BESTFIT-MAP complete. Direction={appliedFit.Name}, calcPairs={appliedFit.CalcCount}, refPairs={appliedFit.ReferenceCount}, rotation={RadiansToDegrees(appliedFit.RotationRadians):0.####} deg, " +
                            $"translation=({appliedFit.Translation.X:0.###}, {appliedFit.Translation.Y:0.###}), RMS={appliedFit.RmsError:0.###}', max={appliedFit.MaxError:0.###}'.");
                        ed.WriteMessage($"\nResidual report: {reportPath}");
                        return;
                    }
                }
            }
            catch (System.Exception ex)
            {
                if (recalledSessionActive && recalledPlacement != null && !targetBlockId.IsNull)
                {
                    try
                    {
                        RestoreBlockPlacement(db, targetBlockId, recalledPlacement);
                    }
                    catch
                    {
                    }
                }

                TryCleanupMarkers(db, markerIds);
                ed.WriteMessage($"\nSURVEY-BESTFIT-MAP error: {ex.Message}");
            }
        }

        private static ReviewDialogResult ShowReviewDialog(List<ControlPair> inputPairs)
        {
            var clonedPairs = inputPairs
                .Select(ClonePair)
                .OrderBy(p => p.Index)
                .ToList();

            using var form = new SurveyBestFitReviewForm(clonedPairs);
            AcadApp.ShowModalDialog(form);
            if (form.Result == null)
            {
                return new ReviewDialogResult
                {
                    Action = ReviewAction.Cancel,
                    Accepted = false,
                    Pairs = clonedPairs
                };
            }

            return form.Result;
        }

        private static ControlPair ClonePair(ControlPair source)
        {
            return new ControlPair
            {
                Index = source.Index,
                SurveyEntityId = source.SurveyEntityId,
                SurveyEntityType = source.SurveyEntityType,
                SurveyLabel = source.SurveyLabel,
                MapLabel = source.MapLabel,
                SurveyPoint = source.SurveyPoint,
                MapPoint = source.MapPoint,
                TransformedMapPoint = source.TransformedMapPoint,
                Dx = source.Dx,
                Dy = source.Dy,
                Residual = source.Residual,
                UseForCalc = source.UseForCalc,
                UseForReference = source.UseForReference
            };
        }

        private static TransformSession CreateNewTransformSession(Database db, ObjectId targetBlockId)
        {
            using Transaction tr = db.TransactionManager.StartTransaction();
            AcBlockReference blockRef = (AcBlockReference)tr.GetObject(targetBlockId, OpenMode.ForRead);
            ValidateBlockReferenceForRigidPlanFit(blockRef);

            var session = new TransformSession
            {
                OriginalPositionX = blockRef.Position.X,
                OriginalPositionY = blockRef.Position.Y,
                OriginalPositionZ = blockRef.Position.Z,
                OriginalRotation = blockRef.Rotation,
                UpdatedUtc = DateTime.UtcNow
            };

            tr.Commit();
            return session;
        }

        private static TransformSession? LoadTransformSession(Database db, ObjectId targetBlockId)
        {
            try
            {
                using Transaction tr = db.TransactionManager.StartTransaction();
                AcBlockReference blockRef = (AcBlockReference)tr.GetObject(targetBlockId, OpenMode.ForRead);
                if (blockRef.ExtensionDictionary.IsNull)
                    return null;

                DBDictionary extDict = (DBDictionary)tr.GetObject(blockRef.ExtensionDictionary, OpenMode.ForRead);
                if (!extDict.Contains(SessionRecordName))
                    return null;

                Xrecord xrecord = (Xrecord)tr.GetObject(extDict.GetAt(SessionRecordName), OpenMode.ForRead);
                if (xrecord.Data == null)
                    return null;

                using var bytes = new MemoryStream();
                foreach (TypedValue value in xrecord.Data)
                {
                    if (value.TypeCode == (int)DxfCode.BinaryChunk && value.Value is byte[] chunk)
                        bytes.Write(chunk, 0, chunk.Length);
                }

                if (bytes.Length == 0)
                    return null;

                TransformSession? session = JsonSerializer.Deserialize<TransformSession>(bytes.ToArray());
                if (session == null || session.Version != SessionFormatVersion)
                    return null;

                session.Pairs ??= new List<SessionPair>();
                return session;
            }
            catch
            {
                return null;
            }
        }

        private static void SaveTransformSession(Transaction tr, AcBlockReference blockRef, TransformSession session, List<ControlPair> pairs)
        {
            session.Version = SessionFormatVersion;
            session.UpdatedUtc = DateTime.UtcNow;
            session.Pairs = pairs.OrderBy(p => p.Index).Select(p => new SessionPair
            {
                Index = p.Index,
                SurveyEntityHandle = TryGetHandleString(p.SurveyEntityId),
                SurveyEntityType = p.SurveyEntityType,
                SurveyLabel = p.SurveyLabel,
                MapLabel = p.MapLabel,
                SurveyX = p.SurveyPoint.X,
                SurveyY = p.SurveyPoint.Y,
                MapX = p.MapPoint.X,
                MapY = p.MapPoint.Y,
                UseForCalc = p.UseForCalc
            }).ToList();

            byte[] json = JsonSerializer.SerializeToUtf8Bytes(session);
            var values = new List<TypedValue>();
            for (int offset = 0; offset < json.Length; offset += XrecordBinaryChunkSize)
            {
                int count = Math.Min(XrecordBinaryChunkSize, json.Length - offset);
                var chunk = new byte[count];
                Buffer.BlockCopy(json, offset, chunk, 0, count);
                values.Add(new TypedValue((int)DxfCode.BinaryChunk, chunk));
            }

            if (blockRef.ExtensionDictionary.IsNull)
                blockRef.CreateExtensionDictionary();

            DBDictionary extDict = (DBDictionary)tr.GetObject(blockRef.ExtensionDictionary, OpenMode.ForWrite);
            Xrecord xrecord;
            if (extDict.Contains(SessionRecordName))
            {
                xrecord = (Xrecord)tr.GetObject(extDict.GetAt(SessionRecordName), OpenMode.ForWrite);
            }
            else
            {
                xrecord = new Xrecord();
                extDict.SetAt(SessionRecordName, xrecord);
                tr.AddNewlyCreatedDBObject(xrecord, true);
            }

            xrecord.Data = new ResultBuffer(values.ToArray());
        }

        private static List<ControlPair> ConvertSessionPairsToControlPairs(Database db, TransformSession session)
        {
            return session.Pairs
                .OrderBy(p => p.Index)
                .Select(p => new ControlPair
                {
                    Index = p.Index,
                    SurveyEntityId = TryResolveHandle(db, p.SurveyEntityHandle),
                    SurveyEntityType = p.SurveyEntityType,
                    SurveyLabel = p.SurveyLabel,
                    MapLabel = p.MapLabel,
                    SurveyPoint = new Point3d(p.SurveyX, p.SurveyY, 0.0),
                    MapPoint = new Point3d(p.MapX, p.MapY, 0.0),
                    UseForCalc = p.UseForCalc,
                    UseForReference = true
                })
                .ToList();
        }

        private static string TryGetHandleString(ObjectId id)
        {
            try
            {
                return id.IsNull || !id.IsValid ? string.Empty : id.Handle.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        private static ObjectId TryResolveHandle(Database db, string handleText)
        {
            if (string.IsNullOrWhiteSpace(handleText))
                return ObjectId.Null;

            try
            {
                long value = long.Parse(handleText, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return db.GetObjectId(false, new Handle(value), 0);
            }
            catch
            {
                return ObjectId.Null;
            }
        }

        private static ControlPair? CollectAdditionalPair(
            Database db,
            Editor ed,
            int pairIndex,
            List<ObjectId> markerIds)
        {
            var peo = new PromptEntityOptions("\nSELECT SURVEY POINT FOR NEW PAIR")
            {
                AllowNone = true
            };
            peo.SetRejectMessage("\nSELECT SURVEY POINT");
            peo.AddAllowedClass(typeof(CogoPoint), exactMatch: false);
            peo.AddAllowedClass(typeof(AcDbPoint), exactMatch: false);
            peo.AddAllowedClass(typeof(AcBlockReference), exactMatch: false);

            PromptEntityResult perSurvey = ed.GetEntity(peo);
            if (perSurvey.Status != PromptStatus.OK)
                return null;

            using Transaction tr = db.TransactionManager.StartTransaction();
            Point3d surveyPoint = GetSurveyReferencePoint(tr, perSurvey.ObjectId, out string surveyEntityType, out string surveyLabel);
            CreatePickMarker(db, tr, surveyPoint, pairIndex, markerIds, isSurveySide: true);

            Point3d mapPoint = PromptForMovingMapPoint(db, ed, tr, pairIndex, out string mapLabel);
            CreatePickMarker(db, tr, mapPoint, pairIndex, markerIds, isSurveySide: false);

            tr.Commit();

            return new ControlPair
            {
                Index = pairIndex,
                SurveyEntityId = perSurvey.ObjectId,
                SurveyEntityType = surveyEntityType,
                SurveyLabel = surveyLabel,
                MapLabel = mapLabel,
                SurveyPoint = Flatten(surveyPoint),
                MapPoint = Flatten(mapPoint),
                UseForCalc = true,
                UseForReference = true
            };
        }

        private static BlockPlacement RestoreTargetToOriginalForEdit(Database db, ObjectId targetBlockId, TransformSession session)
        {
            using Transaction tr = db.TransactionManager.StartTransaction();
            AcBlockReference blockRef = (AcBlockReference)tr.GetObject(targetBlockId, OpenMode.ForWrite);
            ValidateBlockReferenceForRigidPlanFit(blockRef);

            var priorPlacement = new BlockPlacement
            {
                Position = blockRef.Position,
                Rotation = blockRef.Rotation
            };

            blockRef.Position = new Point3d(session.OriginalPositionX, session.OriginalPositionY, session.OriginalPositionZ);
            blockRef.Rotation = session.OriginalRotation;
            tr.Commit();
            return priorPlacement;
        }

        private static void RestoreBlockPlacement(Database db, ObjectId targetBlockId, BlockPlacement placement)
        {
            using Transaction tr = db.TransactionManager.StartTransaction();
            AcBlockReference blockRef = (AcBlockReference)tr.GetObject(targetBlockId, OpenMode.ForWrite);
            blockRef.Position = placement.Position;
            blockRef.Rotation = placement.Rotation;
            tr.Commit();
        }

        private static void RebuildPairMarkers(Database db, List<ControlPair> pairs, List<ObjectId> markerIds)
        {
            using Transaction tr = db.TransactionManager.StartTransaction();
            EraseMarkers(tr, markerIds);
            markerIds.Clear();

            foreach (ControlPair pair in pairs.OrderBy(p => p.Index))
            {
                CreatePickMarker(db, tr, pair.SurveyPoint, pair.Index, markerIds, isSurveySide: true);
                CreatePickMarker(db, tr, pair.MapPoint, pair.Index, markerIds, isSurveySide: false);
            }

            tr.Commit();
        }

        private static void ApplyFitFromOriginalBlockState(AcBlockReference blockRef, FitResult fit, TransformSession session)
        {
            Point3d originalPosition = new Point3d(session.OriginalPositionX, session.OriginalPositionY, session.OriginalPositionZ);
            Point3d transformedPosition = TransformPoint2d(originalPosition, fit.RotationRadians, fit.Translation);
            blockRef.Position = new Point3d(transformedPosition.X, transformedPosition.Y, session.OriginalPositionZ);
            blockRef.Rotation = session.OriginalRotation + fit.RotationRadians;
        }

        private static void RenumberPairs(List<ControlPair> pairs)
        {
            for (int i = 0; i < pairs.Count; i++)
                pairs[i].Index = i + 1;
        }

        private static ObjectId PromptForTargetBlockReference(Editor ed)
        {
            var peo = new PromptEntityOptions("\nSelect survey map xref / block reference to move: ");
            peo.SetRejectMessage("\nSelect an inserted block or xref reference.");
            peo.AddAllowedClass(typeof(AcBlockReference), exactMatch: false);

            PromptEntityResult per = ed.GetEntity(peo);
            return per.Status == PromptStatus.OK ? per.ObjectId : ObjectId.Null;
        }

        private static List<ControlPair> CollectControlPairs(Database db, Editor ed, List<ObjectId> markerIds)
        {
            var pairs = new List<ControlPair>();

            while (true)
            {
                int nextIndex = pairs.Count + 1;

                var peo = new PromptEntityOptions("\nSELECT SURVEY POINT")
                {
                    AllowNone = true
                };
                peo.SetRejectMessage("\nSELECT SURVEY POINT");
                peo.AddAllowedClass(typeof(CogoPoint), exactMatch: false);
                peo.AddAllowedClass(typeof(AcDbPoint), exactMatch: false);
                peo.AddAllowedClass(typeof(AcBlockReference), exactMatch: false);
                peo.Keywords.Add("Undo");
                peo.Keywords.Add("Finish");
                peo.Keywords.Default = "Finish";
                peo.AppendKeywordsToMessage = false;
                PromptEntityResult perSurvey = ed.GetEntity(peo);
                if (perSurvey.Status == PromptStatus.None)
                    break;

                if (perSurvey.Status == PromptStatus.Keyword)
                {
                    if (string.Equals(perSurvey.StringResult, "Undo", StringComparison.OrdinalIgnoreCase))
                    {
                        if (pairs.Count == 0)
                        {
                            ed.WriteMessage("\nNo pairs to remove.");
                            continue;
                        }

                        RemoveLastPairAndMarkers(db, pairs, markerIds);
                        ed.WriteMessage($"\nRemoved pair {nextIndex - 1}.");
                        continue;
                    }

                    break;
                }

                if (perSurvey.Status != PromptStatus.OK)
                    break;

                using Transaction tr = db.TransactionManager.StartTransaction();

                Point3d surveyPoint = GetSurveyReferencePoint(tr, perSurvey.ObjectId, out string surveyEntityType, out string surveyLabel);
                CreatePickMarker(db, tr, surveyPoint, nextIndex, markerIds, isSurveySide: true);

                Point3d mapPoint = PromptForMovingMapPoint(db, ed, tr, nextIndex, out string mapLabel);
                CreatePickMarker(db, tr, mapPoint, nextIndex, markerIds, isSurveySide: false);

                pairs.Add(new ControlPair
                {
                    Index = nextIndex,
                    SurveyEntityId = perSurvey.ObjectId,
                    SurveyEntityType = surveyEntityType,
                    SurveyLabel = surveyLabel,
                    MapLabel = mapLabel,
                    SurveyPoint = Flatten(surveyPoint),
                    MapPoint = Flatten(mapPoint),
                    UseForCalc = true,
                    UseForReference = true
                });

                tr.Commit();

                if (pairs.Count >= 2)
                {
                    FitResult preview = ComputeRigidBestFit(
                        pairs.Select(p => p.MapPoint).ToList(),
                        pairs.Select(p => p.SurveyPoint).ToList(),
                        "MAP->SURVEY");

                    ed.WriteMessage(
                        $"\nPreview after {pairs.Count} pair(s): rot={RadiansToDegrees(preview.RotationRadians):0.####} deg, " +
                        $"dX={preview.Translation.X:0.###}, dY={preview.Translation.Y:0.###}, RMS={preview.RmsError:0.###}'.");
                }
            }

            return pairs;
        }

        private static Point3d PromptForMovingMapPoint(Database db, Editor ed, Transaction tr, int pairIndex, out string mapLabel)
        {
            mapLabel = string.Empty;

            var peo = new PromptEntityOptions("\nSELECT MAP POINT");
            peo.SetRejectMessage("\nSELECT MAP POINT");
            peo.AddAllowedClass(typeof(CogoPoint), exactMatch: false);
            peo.AddAllowedClass(typeof(AcDbPoint), exactMatch: false);
            peo.AddAllowedClass(typeof(AcBlockReference), exactMatch: false);

            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK)
                throw new OperationCanceledException("Map-point selection cancelled.");

            return GetSurveyReferencePoint(tr, per.ObjectId, out _, out mapLabel);
        }
        private static Point3d GetSurveyReferencePoint(Transaction tr, ObjectId id, out string entityType, out string label)
        {
            entityType = string.Empty;
            label = string.Empty;

            AcDbObject dbo = tr.GetObject(id, OpenMode.ForRead);
            switch (dbo)
            {
                case CogoPoint cp:
                    entityType = nameof(CogoPoint);
                    label = GetCogoDisplayLabel(cp);
                    return Flatten(cp.Location);

                case AcDbPoint dbp:
                    entityType = nameof(AcDbPoint);
                    label = "DBPOINT";
                    return Flatten(dbp.Position);

                case AcBlockReference br:
                    entityType = nameof(AcBlockReference);
                    label = !string.IsNullOrWhiteSpace(br.Name) ? br.Name : "BLOCK";
                    return Flatten(br.Position);

                default:
                    throw new InvalidOperationException("Unsupported survey point type. Expected COGO point, DBPoint, or block insertion.");
            }
        }


        private static string GetCogoDisplayLabel(CogoPoint cp)
        {
            string[] preferredPropertyNames = { "Name", "PointName" };

            foreach (string propertyName in preferredPropertyNames)
            {
                try
                {
                    var prop = cp.GetType().GetProperty(propertyName);
                    if (prop?.CanRead == true)
                    {
                        object? value = prop.GetValue(cp);
                        if (value is string text && !string.IsNullOrWhiteSpace(text))
                            return text.Trim().ToUpperInvariant();
                    }
                }
                catch
                {
                }
            }

            return $"P{cp.PointNumber}".ToUpperInvariant();
        }

        private static void CreatePickMarker(Database db, Transaction tr, Point3d point, int index, List<ObjectId> markerIds, bool isSurveySide)
        {
            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            double textHeight = GetMarkerTextHeight();
            double offset = textHeight * 0.6;
            Point3d textPoint = isSurveySide
                ? new Point3d(point.X + offset, point.Y + offset, 0.0)
                : new Point3d(point.X - offset, point.Y - offset, 0.0);

            var txt = new DBText
            {
                Position = textPoint,
                Height = textHeight,
                TextString = index.ToString(CultureInfo.InvariantCulture),
                Layer = MarkerLayer,
                HorizontalMode = TextHorizontalMode.TextCenter,
                VerticalMode = TextVerticalMode.TextVerticalMid,
                AlignmentPoint = textPoint
            };

            ms.AppendEntity(txt);
            tr.AddNewlyCreatedDBObject(txt, true);
            markerIds.Add(txt.ObjectId);

            var line = new Line(textPoint, point)
            {
                Layer = MarkerLayer
            };

            ms.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
            markerIds.Add(line.ObjectId);
        }

        private static double GetMarkerTextHeight()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return 1.0;

            try
            {
                ViewTableRecord view = doc.Editor.GetCurrentView();
                return Math.Max(view.Height * 0.03, 0.75);
            }
            catch
            {
                return 1.0;
            }
        }

        private static FitResult ComputeRigidBestFit(IReadOnlyList<Point3d> sourcePoints, IReadOnlyList<Point3d> targetPoints, string name)
        {
            if (sourcePoints == null || targetPoints == null || sourcePoints.Count != targetPoints.Count || sourcePoints.Count < 2)
                throw new InvalidOperationException("At least 2 matching point pairs are required for best fit.");

            Point3d sourceCentroid = ComputeCentroid(sourcePoints);
            Point3d targetCentroid = ComputeCentroid(targetPoints);

            double sumCross = 0.0;
            double sumDot = 0.0;
            for (int i = 0; i < sourcePoints.Count; i++)
            {
                Point3d source = sourcePoints[i];
                Point3d target = targetPoints[i];

                double sx = source.X - sourceCentroid.X;
                double sy = source.Y - sourceCentroid.Y;
                double tx = target.X - targetCentroid.X;
                double ty = target.Y - targetCentroid.Y;

                sumCross += sx * ty - sy * tx;
                sumDot += sx * tx + sy * ty;
            }

            if (Math.Abs(sumCross) < 1e-12 && Math.Abs(sumDot) < 1e-12)
                throw new InvalidOperationException("Control pairs are degenerate. Pick points with more spread in plan.");

            double theta = Math.Atan2(sumCross, sumDot);
            Point3d rotatedSourceCentroid = RotatePointAroundOrigin(sourceCentroid, theta);
            Vector3d translation = targetCentroid - rotatedSourceCentroid;

            FitResult fit = new FitResult
            {
                Name = name,
                RotationRadians = theta,
                Translation = translation,
                SourceCentroid = sourceCentroid,
                TargetCentroid = targetCentroid
            };

            PopulateFitErrors(fit, sourcePoints, targetPoints);
            return fit;
        }

        private static void PopulateFitErrors(FitResult fit, IReadOnlyList<Point3d> sourcePoints, IReadOnlyList<Point3d> targetPoints)
        {
            double sumSq = 0.0;
            double max = 0.0;

            for (int i = 0; i < sourcePoints.Count; i++)
            {
                Point3d transformed = TransformPoint2d(sourcePoints[i], fit.RotationRadians, fit.Translation);
                double dx = targetPoints[i].X - transformed.X;
                double dy = targetPoints[i].Y - transformed.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                sumSq += dist * dist;
                if (dist > max)
                    max = dist;
            }

            fit.RmsError = Math.Sqrt(sumSq / sourcePoints.Count);
            fit.MaxError = max;
        }

        private static void PopulateResiduals(List<ControlPair> pairs, FitResult fit, ApplyDirection direction)
        {
            double sumSq = 0.0;
            double max = 0.0;
            int refCount = 0;

            foreach (ControlPair pair in pairs)
            {
                Point3d moving = direction == ApplyDirection.Forward ? pair.MapPoint : pair.SurveyPoint;
                Point3d fixedPoint = direction == ApplyDirection.Forward ? pair.SurveyPoint : pair.MapPoint;

                Point3d transformed = TransformPoint2d(moving, fit.RotationRadians, fit.Translation);
                double dx = fixedPoint.X - transformed.X;
                double dy = fixedPoint.Y - transformed.Y;
                double dist = Math.Sqrt(dx * dx + dy * dy);

                pair.TransformedMapPoint = transformed;
                pair.Dx = dx;
                pair.Dy = dy;
                pair.Residual = dist;

                if (pair.UseForReference)
                {
                    refCount++;
                    sumSq += dist * dist;
                    if (dist > max)
                        max = dist;
                }
            }

            fit.ReferenceCount = refCount;
            if (refCount > 0)
            {
                fit.RmsError = Math.Sqrt(sumSq / refCount);
                fit.MaxError = max;
            }
        }

        private static void ApplyFitToBlockReference(AcBlockReference blockRef, FitResult fit)
        {
            Point3d oldPosition = blockRef.Position;
            Point3d newPosition = TransformPoint2d(oldPosition, fit.RotationRadians, fit.Translation);

            blockRef.Position = newPosition;
            blockRef.Rotation += fit.RotationRadians;
        }

        private static Point3d TransformPoint2d(Point3d point, double rotationRadians, Vector3d translation)
        {
            Point3d rotated = RotatePointAroundOrigin(point, rotationRadians);
            return new Point3d(rotated.X + translation.X, rotated.Y + translation.Y, 0.0);
        }

        private static Point3d RotatePointAroundOrigin(Point3d point, double rotationRadians)
        {
            double cos = Math.Cos(rotationRadians);
            double sin = Math.Sin(rotationRadians);

            double x = cos * point.X - sin * point.Y;
            double y = sin * point.X + cos * point.Y;
            return new Point3d(x, y, 0.0);
        }

        private static Point3d ComputeCentroid(IEnumerable<Point3d> points)
        {
            double sx = 0.0;
            double sy = 0.0;
            int count = 0;

            foreach (Point3d point in points)
            {
                sx += point.X;
                sy += point.Y;
                count++;
            }

            if (count == 0)
                throw new InvalidOperationException("No points were available to compute a centroid.");

            return new Point3d(sx / count, sy / count, 0.0);
        }

        private static Point3d Flatten(Point3d point)
        {
            return new Point3d(point.X, point.Y, 0.0);
        }

        private static void ValidateBlockReferenceForRigidPlanFit(AcBlockReference blockRef)
        {
            if (blockRef == null)
                throw new InvalidOperationException("Selected block reference is not valid.");

            if (Math.Abs(blockRef.ScaleFactors.X - blockRef.ScaleFactors.Y) > 1e-9)
                throw new InvalidOperationException("Selected block/xref has non-uniform XY scale. SURVEY-BESTFIT-MAP currently supports uniform plan scale only.");

            if (blockRef.Normal.IsEqualTo(Vector3d.ZAxis, new Tolerance(1e-9, 1e-9)) == false)
                throw new InvalidOperationException("Selected block/xref is not planar in world XY. SURVEY-BESTFIT-MAP currently supports world-plan inserts only.");
        }

        private static string WriteCsvReport(Document doc, List<ControlPair> pairs, FitResult fit, ApplyDirection direction)
        {
            string baseFolder = ResolveReportFolder(doc);
            Directory.CreateDirectory(baseFolder);

            string drawingName = string.IsNullOrWhiteSpace(doc.Name)
                ? "Drawing"
                : Path.GetFileNameWithoutExtension(doc.Name);

            string fileName = $"{DefaultReportPrefix}_{SanitizeFilePart(drawingName)}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            string path = Path.Combine(baseFolder, fileName);

            var sb = new StringBuilder();
            sb.AppendLine("INDEX,USE_FOR_CALC,USE_FOR_REFERENCE,SURVEY_LABEL,MAP_LABEL,SURVEY_TYPE,SURVEY_X,SURVEY_Y,MAP_X,MAP_Y,FIT_X,FIT_Y,DX,DY,RESIDUAL");

            foreach (ControlPair pair in pairs.OrderBy(p => p.Index))
            {
                sb.AppendLine(string.Join(",",
                    pair.Index.ToString(CultureInfo.InvariantCulture),
                    pair.UseForCalc ? "YES" : "NO",
                    pair.UseForReference ? "YES" : "NO",
                    EscapeCsv(pair.SurveyLabel),
                    EscapeCsv(pair.MapLabel),
                    EscapeCsv(pair.SurveyEntityType),
                    pair.SurveyPoint.X.ToString("0.###", CultureInfo.InvariantCulture),
                    pair.SurveyPoint.Y.ToString("0.###", CultureInfo.InvariantCulture),
                    pair.MapPoint.X.ToString("0.###", CultureInfo.InvariantCulture),
                    pair.MapPoint.Y.ToString("0.###", CultureInfo.InvariantCulture),
                    pair.TransformedMapPoint.X.ToString("0.###", CultureInfo.InvariantCulture),
                    pair.TransformedMapPoint.Y.ToString("0.###", CultureInfo.InvariantCulture),
                    pair.Dx.ToString("0.###", CultureInfo.InvariantCulture),
                    pair.Dy.ToString("0.###", CultureInfo.InvariantCulture),
                    pair.Residual.ToString("0.###", CultureInfo.InvariantCulture)));
            }

            sb.AppendLine();
            sb.AppendLine($"AppliedDirection,{fit.Name}");
            sb.AppendLine($"ResidualReference,{(direction == ApplyDirection.Forward ? "SurveyShots" : "MapPoints")}");
            sb.AppendLine($"RotationDegrees,{RadiansToDegrees(fit.RotationRadians).ToString("0.######", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"TranslationX,{fit.Translation.X.ToString("0.######", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"TranslationY,{fit.Translation.Y.ToString("0.######", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"RmsError,{fit.RmsError.ToString("0.######", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"MaxError,{fit.MaxError.ToString("0.######", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"CalcPairCount,{fit.CalcCount.ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine($"ReferencePairCount,{fit.ReferenceCount.ToString(CultureInfo.InvariantCulture)}");
            sb.AppendLine($"TotalPairCount,{pairs.Count.ToString(CultureInfo.InvariantCulture)}");

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return path;
        }

        private static string ResolveReportFolder(Document doc)
        {
            try
            {
                string? drawingPath = doc.Database.Filename;
                if (!string.IsNullOrWhiteSpace(drawingPath))
                {
                    string? folder = Path.GetDirectoryName(drawingPath);
                    if (!string.IsNullOrWhiteSpace(folder))
                        return folder;
                }
            }
            catch
            {
            }

            return Path.GetTempPath();
        }

        private static string SanitizeFilePart(string value)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
                sb.Append(invalid.Contains(c) ? '_' : c);

            return sb.ToString();
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            bool mustQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
            if (!mustQuote)
                return value;

            return '"' + value.Replace("\"", "\"\"") + '"';
        }

        private static double RadiansToDegrees(double radians)
        {
            return radians * 180.0 / Math.PI;
        }

        private static void EnsureLayer(Database db, Transaction tr, string layerName, short colorIndex)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(layerName))
                return;

            lt.UpgradeOpen();
            var ltr = new LayerTableRecord
            {
                Name = layerName
            };
            ltr.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, colorIndex);
            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);
        }

        private static void RemoveLastPairAndMarkers(Database db, List<ControlPair> pairs, List<ObjectId> markerIds)
        {
            if (pairs.Count == 0)
                return;

            int removeFrom = Math.Max(0, markerIds.Count - 4);
            List<ObjectId> idsToErase = markerIds.Skip(removeFrom).ToList();

            using Transaction tr = db.TransactionManager.StartTransaction();
            EraseMarkers(tr, idsToErase);
            tr.Commit();

            markerIds.RemoveRange(removeFrom, markerIds.Count - removeFrom);
            pairs.RemoveAt(pairs.Count - 1);

            for (int i = 0; i < pairs.Count; i++)
                pairs[i].Index = i + 1;
        }

        private static void EraseMarkers(Transaction tr, IEnumerable<ObjectId> markerIds)
        {
            foreach (ObjectId id in markerIds)
            {
                if (id.IsNull || !id.IsValid)
                    continue;

                if (tr.GetObject(id, OpenMode.ForWrite, false) is AcEntity ent && !ent.IsErased)
                    ent.Erase();
            }
        }

        private static void TryCleanupMarkers(Database db, IEnumerable<ObjectId> markerIds)
        {
            try
            {
                using Transaction tr = db.TransactionManager.StartTransaction();
                EraseMarkers(tr, markerIds);
                tr.Commit();
            }
            catch
            {
            }
        }

        private sealed class SurveyBestFitReviewForm : Form
        {
            private readonly BindingList<ReviewRow> _rows;
            private readonly List<ControlPair> _pairs;
            private readonly DataGridView _grid;
            private readonly WinLabel _summaryLabel;
            private readonly RadioButton _forwardRadio;

            public ReviewDialogResult? Result { get; private set; }

            public SurveyBestFitReviewForm(List<ControlPair> pairs)
            {
                _pairs = pairs;
                _rows = new BindingList<ReviewRow>(pairs.Select(ToReviewRow).ToList());

                Text = "MAP TRANSFORM REVIEW";
                Width = 760;
                Height = 720;
                MinimizeBox = false;
                MaximizeBox = true;
                StartPosition = FormStartPosition.CenterScreen;

                var root = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 4,
                    Padding = new Padding(8)
                };
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                Controls.Add(root);

                var topPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    WrapContents = false,
                    FlowDirection = WinFlowDirection.LeftToRight
                };

                _forwardRadio = new RadioButton { Text = "Forward (Map -> Survey)", Checked = true, AutoSize = true, Margin = new Padding(3, 8, 12, 3), Enabled = false };

                topPanel.Controls.Add(_forwardRadio);
                root.Controls.Add(topPanel, 0, 0);

                _grid = new DataGridView
                {
                    Dock = DockStyle.Fill,
                    AutoGenerateColumns = false,
                    AllowUserToAddRows = false,
                    AllowUserToDeleteRows = false,
                    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    MultiSelect = true,
                    DataSource = _rows,
                    RowHeadersVisible = false
                };

                AddColumns(_grid);
                root.Controls.Add(_grid, 0, 1);

                _summaryLabel = new WinLabel
                {
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    Padding = new Padding(4, 8, 4, 8)
                };
                root.Controls.Add(_summaryLabel, 0, 2);

                var bottomPanel = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    FlowDirection = WinFlowDirection.RightToLeft,
                    WrapContents = false
                };
                var okButton = new WinButton { Text = "Apply Updated Transform", DialogResult = DialogResult.None, AutoSize = true };
                var cancelButton = new WinButton { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
                var addPairButton = new WinButton { Text = "Add Pair", DialogResult = DialogResult.None, AutoSize = true };
                var removePairButton = new WinButton { Text = "Remove Selected", DialogResult = DialogResult.None, AutoSize = true };
                bottomPanel.Controls.Add(okButton);
                bottomPanel.Controls.Add(cancelButton);
                bottomPanel.Controls.Add(addPairButton);
                bottomPanel.Controls.Add(removePairButton);
                root.Controls.Add(bottomPanel, 0, 3);

                AcceptButton = okButton;
                CancelButton = cancelButton;

                _grid.CurrentCellDirtyStateChanged += (_, _) =>
                {
                    if (_grid.IsCurrentCellDirty)
                        _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                };
                _grid.CellValueChanged += (_, _) => RefreshResults();
                _forwardRadio.CheckedChanged += (_, _) => RefreshResults();
                okButton.Click += (_, _) => FinalizeAndClose();
                addPairButton.Click += (_, _) => RequestAddPairAndClose();
                removePairButton.Click += (_, _) => RemoveSelectedRows();

                RefreshResults();
            }

            private static void AddColumns(DataGridView grid)
            {
                grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ReviewRow.Index), HeaderText = "Pair", Width = 55, ReadOnly = true });
                grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = nameof(ReviewRow.Control), HeaderText = "Control", Width = 70 });
                grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ReviewRow.SurveyPointNumber), HeaderText = "Survey", Width = 110, ReadOnly = true });
                grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ReviewRow.MapPointNumber), HeaderText = "Map", Width = 110, ReadOnly = true });
                grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ReviewRow.Dx), HeaderText = "dX", Width = 90, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Format = "N3" } });
                grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ReviewRow.Dy), HeaderText = "dY", Width = 90, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Format = "N3" } });
                grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(ReviewRow.Residual), HeaderText = "Error", Width = 95, ReadOnly = true, DefaultCellStyle = new DataGridViewCellStyle { Format = "N3" } });
            }

            private void RefreshResults()
            {
                try
                {
                    List<ControlPair> workingPairs = BuildPairsFromRows();
                    ApplyDirection direction = ApplyDirection.Forward;

                    List<ControlPair> calcPairs = workingPairs.Where(p => p.UseForCalc).ToList();
                    if (calcPairs.Count < 2)
                    {
                        _summaryLabel.Text = "Need at least 2 rows checked in CONTROL to compute a fit.";
                        ClearComputedColumns();
                        return;
                    }

                    FitResult fit = ComputeFitFromDirection(calcPairs, direction);
                    fit.CalcCount = calcPairs.Count;

                    PopulateResiduals(workingPairs, fit, direction);
                    PushComputedValuesToRows(workingPairs);

                    _summaryLabel.Text =
                        $"Direction: {fit.Name}    Control: {fit.CalcCount}    Compared: {fit.ReferenceCount}    " +
                        $"Rot: {RadiansToDegrees(fit.RotationRadians):0.####} deg    dX: {fit.Translation.X:0.###}    dY: {fit.Translation.Y:0.###}    " +
                        $"RMS: {fit.RmsError:0.###}'    Max: {fit.MaxError:0.###}'";

                    HighlightRows();
                }
                catch (System.Exception ex)
                {
                    _summaryLabel.Text = ex.Message;
                    ClearComputedColumns();
                }
            }

            private void ClearComputedColumns()
            {
                foreach (ReviewRow row in _rows)
                {
                    row.Dx = 0.0;
                    row.Dy = 0.0;
                    row.Residual = 0.0;
                }
                _grid.Refresh();
            }

            private void PushComputedValuesToRows(List<ControlPair> workingPairs)
            {
                foreach (ControlPair pair in workingPairs)
                {
                    ReviewRow? row = _rows.FirstOrDefault(r => r.Index == pair.Index);
                    if (row == null)
                        continue;

                    row.Dx = pair.Dx;
                    row.Dy = pair.Dy;
                    row.Residual = pair.Residual;
                }

                _grid.Refresh();
            }

            private void HighlightRows()
            {
                foreach (DataGridViewRow gridRow in _grid.Rows)
                {
                    if (gridRow.DataBoundItem is not ReviewRow row)
                        continue;

                    if (!row.Control)
                    {
                        gridRow.DefaultCellStyle.BackColor = System.Drawing.Color.Gainsboro;
                    }
                    else if (row.Residual >= 0.25)
                    {
                        gridRow.DefaultCellStyle.BackColor = System.Drawing.Color.MistyRose;
                    }
                    else if (row.Residual >= 0.10)
                    {
                        gridRow.DefaultCellStyle.BackColor = System.Drawing.Color.LemonChiffon;
                    }
                    else
                    {
                        gridRow.DefaultCellStyle.BackColor = System.Drawing.Color.White;
                    }
                }
            }

            private void FinalizeAndClose()
            {
                try
                {
                    List<ControlPair> workingPairs = BuildPairsFromRows();
                    ApplyDirection direction = ApplyDirection.Forward;
                    List<ControlPair> calcPairs = workingPairs.Where(p => p.UseForCalc).ToList();
                    if (calcPairs.Count < 2)
                        throw new InvalidOperationException("Need at least 2 CONTROL rows before finalizing.");

                    FitResult fit = ComputeFitFromDirection(calcPairs, direction);
                    fit.CalcCount = calcPairs.Count;
                    PopulateResiduals(workingPairs, fit, direction);

                    Result = new ReviewDialogResult
                    {
                        Action = ReviewAction.Finalize,
                        Accepted = true,
                        Direction = direction,
                        AppliedFit = fit,
                        Pairs = workingPairs
                    };

                    DialogResult = DialogResult.OK;
                    Close();
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Survey Best Fit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            private void RequestAddPairAndClose()
            {
                try
                {
                    List<ControlPair> workingPairs = BuildPairsFromRows();
                    Result = new ReviewDialogResult
                    {
                        Action = ReviewAction.AddPair,
                        Accepted = false,
                        Direction = ApplyDirection.Forward,
                        Pairs = workingPairs
                    };

                    DialogResult = DialogResult.Retry;
                    Close();
                }
                catch (System.Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Survey Best Fit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            private void RemoveSelectedRows()
            {
                var selected = _grid.SelectedRows
                    .Cast<DataGridViewRow>()
                    .Select(r => r.DataBoundItem as ReviewRow)
                    .Where(r => r != null)
                    .Cast<ReviewRow>()
                    .OrderByDescending(r => r.Index)
                    .ToList();

                if (selected.Count == 0)
                {
                    MessageBox.Show(this, "Select one or more pairs to remove.", "Survey Best Fit", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                foreach (ReviewRow row in selected)
                {
                    _rows.Remove(row);
                    ControlPair? source = _pairs.FirstOrDefault(p => p.Index == row.OriginalIndex);
                    if (source != null)
                        _pairs.Remove(source);
                }

                for (int i = 0; i < _rows.Count; i++)
                    _rows[i].Index = i + 1;

                _grid.Refresh();
                RefreshResults();
            }

            private List<ControlPair> BuildPairsFromRows()
            {
                var pairs = new List<ControlPair>();
                for (int i = 0; i < _rows.Count; i++)
                {
                    ReviewRow row = _rows[i];
                    ControlPair source = _pairs.First(p => p.Index == row.OriginalIndex);
                    ControlPair pair = ClonePair(source);
                    pair.Index = i + 1;
                    pair.UseForCalc = row.Control;
                    pair.UseForReference = true;
                    pairs.Add(pair);
                }
                return pairs;
            }

            private FitResult ComputeFitFromDirection(List<ControlPair> calcPairs, ApplyDirection direction)
            {
                if (direction == ApplyDirection.Forward)
                {
                    FitResult fit = ComputeRigidBestFit(
                        calcPairs.Select(p => p.MapPoint).ToList(),
                        calcPairs.Select(p => p.SurveyPoint).ToList(),
                        "MAP->SURVEY");
                    return fit;
                }

                return ComputeRigidBestFit(
                    calcPairs.Select(p => p.SurveyPoint).ToList(),
                    calcPairs.Select(p => p.MapPoint).ToList(),
                    "SURVEY->MAP");
            }

            private static ReviewRow ToReviewRow(ControlPair pair)
            {
                return new ReviewRow
                {
                    OriginalIndex = pair.Index,
                    Index = pair.Index,
                    Control = pair.UseForCalc,
                    SurveyPointNumber = pair.SurveyLabel,
                    MapPointNumber = pair.MapLabel,
                    Dx = pair.Dx,
                    Dy = pair.Dy,
                    Residual = pair.Residual
                };
            }
        }
    }
}
