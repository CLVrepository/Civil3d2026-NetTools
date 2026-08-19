using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using CLV_CivilTools.Shared;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcEntity = Autodesk.AutoCAD.DatabaseServices.Entity;
using AcException = Autodesk.AutoCAD.Runtime.Exception;

namespace CLV_CivilTools.Survey
{
    /// <summary>
    /// Phase 2 survey-map closure assistant.
    ///
    /// Supports boundary geometry:
    /// - multiple AutoCAD LINE and ARC entities, or
    /// - one lightweight POLYLINE containing straight and bulged arc segments.
    ///
    /// The selected linework is preserved on V-SURV-MAP~-ORIG and a corrected overlay copy is
    /// created on V-SURV-MAP~-ADJ~. The default adjustment keeps each original segment direction
    /// fixed and solves only length changes on straight line segments so displayed bearings do not
    /// change. Curve segments are carried forward as translated geometry with their original bulge,
    /// radius, and tangent relationships preserved wherever the source geometry was already tangent.
    /// </summary>
    public static class SurveyAutoClosureCommands
    {
        private const double EndpointMatchTolerance = 0.10;
        private const double ComputationalClosureEpsilon = 1.0e-8;
        private const double DefaultDisplayedDistanceTolerance = 0.004;
        private const double DefaultBearingWarningSeconds = 10.0;
        private const double TangencyDetectToleranceSeconds = 60.0;
        private const double TangencyToleranceSeconds = 1.0;


        private sealed class SegmentRef
        {
            public ObjectId Id { get; set; }
            public bool Reversed { get; set; }
            public string Type { get; set; } = "LINE";
            public double OriginalBulge { get; set; }
            public double SourceLength { get; set; }
            public Point3d OriginalStart { get; set; }
            public Point3d OriginalEnd { get; set; }
            public Point3d AdjustedStart { get; set; }
            public Point3d AdjustedEnd { get; set; }
            public double AdjustedBulge { get; set; }
            public bool TangentInLocked { get; set; }
            public bool TangentOutLocked { get; set; }
            public bool TangencyWarning { get; set; }
            public string ConstraintState { get; set; } = string.Empty;
            public double? ConstraintTargetOffset { get; set; }
            public double? ConstraintActualOffset { get; set; }
            public double? ConstraintOffsetDelta { get; set; }
            public bool ConstraintLockBearing { get; set; }
            public bool ConstraintLockLength { get; set; }
            public bool ConstraintLockRadius { get; set; }
            public bool ConstraintFixedVector { get; set; }
            public double TangencyInErrorSeconds { get; set; }
            public double TangencyOutErrorSeconds { get; set; }
            public string TangencyInStatus => !TangentInLocked ? string.Empty : TangencyInErrorSeconds <= TangencyToleranceSeconds ? "PRESERVED" : "WARNING";
            public string TangencyOutStatus => !TangentOutLocked ? string.Empty : TangencyOutErrorSeconds <= TangencyToleranceSeconds ? "PRESERVED" : "WARNING";
            public string TangencyStatus => TangencyWarning ? "TANGENCY WARNING" : (TangentInLocked || TangentOutLocked ? "TANGENT OK" : string.Empty);
            public double OriginalLength => SourceLength > 1.0e-12 ? SourceLength : GetSegmentLength(OriginalStart, OriginalEnd, OriginalBulge);
            public double AdjustedLength => GetSegmentLength(AdjustedStart, AdjustedEnd, Math.Abs(AdjustedBulge) > 1.0e-12 ? AdjustedBulge : OriginalBulge);
            public double LengthDelta => AdjustedLength - OriginalLength;
            public double BearingDeltaSeconds => GetBearingDeltaSeconds(OriginalStart, OriginalEnd, AdjustedStart, AdjustedEnd);
        }


        private sealed class PolylineSegmentData
        {
            public Point3d Start { get; set; }
            public Point3d End { get; set; }
            public double Bulge { get; set; }
            public double AdjustedBulge { get; set; }
            public bool TangentInLocked { get; set; }
            public bool TangentOutLocked { get; set; }
            public bool TangencyWarning { get; set; }
            public string ConstraintState { get; set; } = string.Empty;
            public double? ConstraintTargetOffset { get; set; }
            public double? ConstraintActualOffset { get; set; }
            public double? ConstraintOffsetDelta { get; set; }
            public bool ConstraintLockBearing { get; set; }
            public bool ConstraintLockLength { get; set; }
            public bool ConstraintLockRadius { get; set; }
            public bool ConstraintFixedVector { get; set; }
            public double TangencyInErrorSeconds { get; set; }
            public double TangencyOutErrorSeconds { get; set; }
            public string TangencyInStatus => !TangentInLocked ? string.Empty : TangencyInErrorSeconds <= TangencyToleranceSeconds ? "PRESERVED" : "WARNING";
            public string TangencyOutStatus => !TangentOutLocked ? string.Empty : TangencyOutErrorSeconds <= TangencyToleranceSeconds ? "PRESERVED" : "WARNING";
            public string TangencyStatus => TangencyWarning ? "TANGENCY WARNING" : (TangentInLocked || TangentOutLocked ? "TANGENT OK" : string.Empty);
            public double StartWidth { get; set; }
            public double EndWidth { get; set; }
            public double Length => GetSegmentLength(Start, End, Bulge);
        }

        private enum AdjustmentOutputMode
        {
            Cancel,
            ReplaceOriginals,
            KeepOriginalReference
        }

        private sealed class ClosureReport
        {
            public int SegmentCount { get; set; }
            public Point3d StartPoint { get; set; }
            public Point3d EndPoint { get; set; }
            public double TotalLength { get; set; }
            public Vector3d MisclosureVector { get; set; }
            public double Misclosure { get; set; }
            public double ClosureBearingRadians { get; set; }
            public double RelativePrecisionRatio { get; set; }
            public double PartsPerMillionError { get; set; }
            public double OriginalArea { get; set; }
            public double AdjustedArea { get; set; }
            public double AreaDelta => AdjustedArea - OriginalArea;
            public double MaxAbsLengthDelta { get; set; }
            public double MaxAbsBearingDeltaSeconds { get; set; }
            public int WorstLengthSegmentIndex { get; set; }
            public int WorstBearingSegmentIndex { get; set; }
            public double TotalAbsBearingDeltaSeconds { get; set; }
            public bool ExceedsDistanceTolerance { get; set; }
            public bool ExceedsBearingWarning { get; set; }
        }

        [CommandMethod("SURVEY-AUTO-CLOSURE", CommandFlags.Modal)]
        public static void SurveyAutoClosure()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                PromptSelectionOptions pso = new PromptSelectionOptions
                {
                    MessageForAdding = "\nSelect boundary LINEs/ARCs or one open lightweight POLYLINE for AUTO CLOSURE: "
                };

                SelectionFilter filter = new SelectionFilter(new[]
                {
                    new TypedValue((int)DxfCode.Start, "LINE,ARC,LWPOLYLINE")
                });

                PromptSelectionResult psr = ed.GetSelection(pso, filter);
                if (psr.Status != PromptStatus.OK)
                    return;

                PromptPointOptions startOptions = new PromptPointOptions("\nSelect traverse start point: ");
                PromptPointResult startResult = ed.GetPoint(startOptions);
                if (startResult.Status != PromptStatus.OK)
                    return;

                using (doc.LockDocument())
                {
                    LayerStandards.EnsureSurveyMapClosureLayers(db, ed);

                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        List<ObjectId> ids = psr.Value.GetObjectIds().Distinct().ToList();
                        if (ids.Count == 0)
                        {
                            ed.WriteMessage("\nSURVEY-AUTO-CLOSURE: No supported objects selected.");
                            return;
                        }

                        List<ObjectId> curveIds = new List<ObjectId>();
                        List<ObjectId> polylineIds = new List<ObjectId>();

                        foreach (ObjectId id in ids)
                        {
                            AcEntity ent = (AcEntity)tr.GetObject(id, OpenMode.ForRead);
                            if (ent is Line || ent is Arc)
                                curveIds.Add(id);
                            else if (ent is Polyline)
                                polylineIds.Add(id);
                        }

                        if (polylineIds.Count > 0 && (curveIds.Count > 0 || polylineIds.Count > 1))
                        {
                            ed.WriteMessage("\nSURVEY-AUTO-CLOSURE: Select either LINE/ARC entities or one lightweight POLYLINE, not a mix with POLYLINE.");
                            return;
                        }

                        if (polylineIds.Count == 1)
                            RunPolylineClosure(db, tr, ed, polylineIds[0], startResult.Value);
                        else
                            RunCurveEntityClosure(db, tr, ed, curveIds, startResult.Value);

                        tr.Commit();
                    }
                }
            }
            catch (AcException ex)
            {
                ed.WriteMessage("\nSURVEY-AUTO-CLOSURE AutoCAD error: " + ex.Message);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nSURVEY-AUTO-CLOSURE error: " + ex.Message);
            }
        }

        private static void RunPolylineClosure(Database db, Transaction tr, Editor ed, ObjectId polylineId, Point3d pickedStartPoint)
        {
            Polyline original = (Polyline)tr.GetObject(polylineId, OpenMode.ForRead);
            if (original.NumberOfVertices < 3)
            {
                ed.WriteMessage("\nSURVEY-AUTO-CLOSURE: Polyline needs at least 3 vertices.");
                return;
            }

            if (original.Closed)
            {
                ed.WriteMessage("\nSURVEY-AUTO-CLOSURE: Selected polyline is already flagged Closed. AUTO CLOSURE expects open map linework with visible misclosure.");
                return;
            }

            if (!TryBuildOrderedPolylineSegments(original, pickedStartPoint, out List<PolylineSegmentData> orderedSegments))
            {
                ed.WriteMessage("\nSURVEY-AUTO-CLOSURE: Picked start point must be near a polyline endpoint for polyline adjustment.");
                return;
            }

            List<Point3d> orderedVertices = new List<Point3d> { orderedSegments[0].Start };
            orderedVertices.AddRange(orderedSegments.Select(seg => seg.End));

            List<Point3d> adjustedVertices = new List<Point3d>();
            ClosureReport report = AdjustPolylineChainByLineLengthsOnly(orderedSegments, adjustedVertices, ed);
            EvaluatePolylineArcTangencies(orderedSegments, adjustedVertices);

            AdjustmentOutputMode outputMode = ConfirmAdjustment(ed, report);
            if (outputMode == AdjustmentOutputMode.Cancel)
                return;

            bool keepOriginalReference = outputMode == AdjustmentOutputMode.KeepOriginalReference;
            string sourceLayer = original.Layer;
            original.UpgradeOpen();
            if (keepOriginalReference)
                original.Layer = LayerStandards.SurveyMapOriginalLayerName;

            Polyline adjusted = new Polyline();
            adjusted.SetDatabaseDefaults(db);
            adjusted.Layer = keepOriginalReference ? LayerStandards.SurveyMapAdjustedLayerName : sourceLayer;
            adjusted.Elevation = original.Elevation;
            adjusted.Normal = original.Normal;

            for (int i = 0; i < adjustedVertices.Count; i++)
            {
                Point3d p = adjustedVertices[i];
                PolylineSegmentData sourceSeg = orderedSegments[Math.Min(i, orderedSegments.Count - 1)];
                adjusted.AddVertexAt(i, new Point2d(p.X, p.Y), i < orderedSegments.Count ? orderedSegments[i].AdjustedBulge : 0.0, sourceSeg.StartWidth, sourceSeg.EndWidth);
            }

            ObjectId adjustedId = AppendToSameOwnerSpace(tr, polylineId, adjusted);
            if (!keepOriginalReference)
                original.Erase();

            List<ClosureReviewSegment> reviewSegments = new List<ClosureReviewSegment>();
            for (int i = 0; i < orderedSegments.Count; i++)
            {
                PolylineSegmentData source = orderedSegments[i];
                reviewSegments.Add(new ClosureReviewSegment
                {
                    Number = i + 1,
                    Type = Math.Abs(source.Bulge) > 1.0e-12 ? "ARC" : "POLYLINE",
                    OriginalObjectId = keepOriginalReference ? polylineId : ObjectId.Null,
                    AdjustedObjectId = adjustedId,
                    OriginalStart = source.Start,
                    OriginalEnd = source.End,
                    AdjustedStart = adjustedVertices[i],
                    AdjustedEnd = adjustedVertices[i + 1],
                    OriginalBulge = source.Bulge,
                    AdjustedBulge = source.AdjustedBulge,
                    TangencyIn = source.TangencyInStatus,
                    TangencyOut = source.TangencyOutStatus,
                    TangencyStatus = source.TangencyStatus,
                    ConstraintState = string.Empty
                });
            }

            StoreReviewRun(db, report, reviewSegments);

            bool hasCurves = orderedSegments.Any(seg => Math.Abs(seg.Bulge) > 1.0e-12);
            ed.WriteMessage(keepOriginalReference
                ? "\nSURVEY-AUTO-CLOSURE complete. Original polyline moved to V-SURV-MAP~-ORIG; adjusted overlay copy created on V-SURV-MAP~-ADJ~."
                : "\nSURVEY-AUTO-CLOSURE complete. Selected polyline was replaced by the adjusted closed linework on its original layer; no original reference copy was retained.");
            if (hasCurves)
                ed.WriteMessage("\nPhase 2 curve note: lightweight-polyline bulges were preserved. Review curve labels/tangency before final use.");
            WriteReport(ed, report);
        }

        private static void RunCurveEntityClosure(Database db, Transaction tr, Editor ed, List<ObjectId> curveIds, Point3d pickedStartPoint)
        {
            if (curveIds.Count < 3)
            {
                ed.WriteMessage("\nSURVEY-AUTO-CLOSURE: Need at least 3 LINE/ARC entities.");
                return;
            }

            List<SegmentRef> orderedSegments = BuildOrderedCurveEntitySegments(tr, curveIds, pickedStartPoint);
            if (orderedSegments.Count != curveIds.Count)
            {
                ed.WriteMessage("\nSURVEY-AUTO-CLOSURE: Could not order every selected LINE/ARC into one continuous boundary chain from the selected start point.");
                return;
            }

            List<Point3d> vertices = new List<Point3d> { orderedSegments[0].OriginalStart };
            vertices.AddRange(orderedSegments.Select(s => s.OriginalEnd));

            ClosureReport report = AdjustEntityChainByLineLengthsOnly(orderedSegments, ed);

            ApplyUserConstraints(orderedSegments, ed);
            ReapplyLockedSegmentConstraints(orderedSegments);
            EvaluateEntityArcTangencies(orderedSegments);
            ApplySegmentStats(orderedSegments, report);

            AdjustmentOutputMode outputMode = ConfirmAdjustment(ed, report);
            if (outputMode == AdjustmentOutputMode.Cancel)
                return;

            bool keepOriginalReference = outputMode == AdjustmentOutputMode.KeepOriginalReference;
            List<ClosureReviewSegment> reviewSegments = new List<ClosureReviewSegment>();
            for (int i = 0; i < orderedSegments.Count; i++)
            {
                SegmentRef segment = orderedSegments[i];
                AcEntity original = (AcEntity)tr.GetObject(segment.Id, OpenMode.ForWrite);
                string sourceLayer = original.Layer;
                if (keepOriginalReference)
                    original.Layer = LayerStandards.SurveyMapOriginalLayerName;

                AcEntity adjusted;
                if (string.Equals(segment.Type, "ARC", StringComparison.OrdinalIgnoreCase))
                {
                    Polyline adjustedArc = new Polyline();
                    adjustedArc.SetDatabaseDefaults(db);
                    adjustedArc.Layer = keepOriginalReference ? LayerStandards.SurveyMapAdjustedLayerName : sourceLayer;
                    adjustedArc.AddVertexAt(0, new Point2d(segment.AdjustedStart.X, segment.AdjustedStart.Y), segment.AdjustedBulge, 0.0, 0.0);
                    adjustedArc.AddVertexAt(1, new Point2d(segment.AdjustedEnd.X, segment.AdjustedEnd.Y), 0.0, 0.0, 0.0);
                    adjusted = adjustedArc;
                }
                else
                {
                    Line adjustedLine = new Line(segment.AdjustedStart, segment.AdjustedEnd);
                    adjustedLine.SetDatabaseDefaults(db);
                    adjustedLine.Layer = keepOriginalReference ? LayerStandards.SurveyMapAdjustedLayerName : sourceLayer;
                    adjusted = adjustedLine;
                }

                ObjectId adjustedId = AppendToSameOwnerSpace(tr, segment.Id, adjusted);
                if (!keepOriginalReference)
                    original.Erase();

                reviewSegments.Add(new ClosureReviewSegment
                {
                    Number = i + 1,
                    Type = segment.Type,
                    OriginalObjectId = keepOriginalReference ? segment.Id : ObjectId.Null,
                    AdjustedObjectId = adjustedId,
                    OriginalStart = segment.OriginalStart,
                    OriginalEnd = segment.OriginalEnd,
                    AdjustedStart = segment.AdjustedStart,
                    AdjustedEnd = segment.AdjustedEnd,
                    OriginalBulge = segment.OriginalBulge,
                    AdjustedBulge = segment.AdjustedBulge,
                    TangencyIn = segment.TangencyInStatus,
                    TangencyOut = segment.TangencyOutStatus,
                    TangencyStatus = segment.TangencyStatus,
                    ConstraintState = segment.ConstraintState,
                    TargetOffset = segment.ConstraintTargetOffset,
                    ActualOffset = segment.ConstraintActualOffset,
                    OffsetDelta = segment.ConstraintOffsetDelta
                });
            }

            StoreReviewRun(db, report, reviewSegments);

            ed.WriteMessage(keepOriginalReference
                ? "\nSURVEY-AUTO-CLOSURE complete. Original LINE/ARC entities moved to V-SURV-MAP~-ORIG; adjusted overlay copies created on V-SURV-MAP~-ADJ~."
                : "\nSURVEY-AUTO-CLOSURE complete. Selected LINE/ARC entities were replaced by the adjusted closed linework on their original layers; no original reference copies were retained.");
            WriteReport(ed, report);
        }

        private static List<SegmentRef> BuildOrderedCurveEntitySegments(Transaction tr, List<ObjectId> curveIds, Point3d pickedStartPoint)
        {
            List<SegmentRef> source = new List<SegmentRef>();
            foreach (ObjectId id in curveIds)
            {
                AcEntity ent = (AcEntity)tr.GetObject(id, OpenMode.ForRead);
                if (ent is Line line)
                {
                    source.Add(new SegmentRef
                    {
                        Id = id,
                        Type = "LINE",
                        OriginalStart = line.StartPoint,
                        OriginalEnd = line.EndPoint,
                        SourceLength = line.StartPoint.DistanceTo(line.EndPoint)
                    });
                }
                else if (ent is Arc arc)
                {
                    double includedAngle = GetArcIncludedAngle(arc);
                    source.Add(new SegmentRef
                    {
                        Id = id,
                        Type = "ARC",
                        OriginalStart = arc.StartPoint,
                        OriginalEnd = arc.EndPoint,
                        OriginalBulge = Math.Tan(includedAngle / 4.0),
                        AdjustedBulge = Math.Tan(includedAngle / 4.0),
                        SourceLength = Math.Abs(arc.Radius * includedAngle)
                    });
                }
            }

            List<(int Index, bool Reverse, double Distance)> startCandidates = new List<(int, bool, double)>();
            for (int i = 0; i < source.Count; i++)
            {
                startCandidates.Add((i, false, pickedStartPoint.DistanceTo(source[i].OriginalStart)));
                startCandidates.Add((i, true, pickedStartPoint.DistanceTo(source[i].OriginalEnd)));
            }

            foreach (var candidate in startCandidates.OrderBy(c => c.Distance))
            {
                List<SegmentRef> remaining = CloneSegments(source);
                SegmentRef first = remaining[candidate.Index];
                remaining.RemoveAt(candidate.Index);
                if (candidate.Reverse)
                    Reverse(first);

                List<SegmentRef> ordered = new List<SegmentRef> { first };
                Point3d current = first.OriginalEnd;

                while (remaining.Count > 0)
                {
                    int bestIndex = -1;
                    bool bestReverse = false;
                    double bestDistance = double.MaxValue;

                    for (int i = 0; i < remaining.Count; i++)
                    {
                        double dStart = current.DistanceTo(remaining[i].OriginalStart);
                        if (dStart < bestDistance)
                        {
                            bestDistance = dStart;
                            bestIndex = i;
                            bestReverse = false;
                        }

                        double dEnd = current.DistanceTo(remaining[i].OriginalEnd);
                        if (dEnd < bestDistance)
                        {
                            bestDistance = dEnd;
                            bestIndex = i;
                            bestReverse = true;
                        }
                    }

                    if (bestIndex < 0 || bestDistance > EndpointMatchTolerance)
                        break;

                    SegmentRef next = remaining[bestIndex];
                    remaining.RemoveAt(bestIndex);
                    if (bestReverse)
                        Reverse(next);

                    next.OriginalStart = current;
                    ordered.Add(next);
                    current = next.OriginalEnd;
                }

                if (ordered.Count == source.Count)
                    return ordered;
            }

            return new List<SegmentRef>();
        }

        private static List<SegmentRef> CloneSegments(List<SegmentRef> source)
        {
            return source.Select(s => new SegmentRef
            {
                Id = s.Id,
                Reversed = s.Reversed,
                Type = s.Type,
                OriginalBulge = s.OriginalBulge,
                AdjustedBulge = s.AdjustedBulge,
                SourceLength = s.SourceLength,
                OriginalStart = s.OriginalStart,
                OriginalEnd = s.OriginalEnd
            }).ToList();
        }

        private static void Reverse(SegmentRef segment)
        {
            Point3d temp = segment.OriginalStart;
            segment.OriginalStart = segment.OriginalEnd;
            segment.OriginalEnd = temp;
            segment.OriginalBulge = -segment.OriginalBulge;
            segment.AdjustedBulge = -segment.AdjustedBulge;
            segment.Reversed = !segment.Reversed;
        }

        private static double GetArcIncludedAngle(Arc arc)
        {
            double angle = arc.EndAngle - arc.StartAngle;
            while (angle < 0.0)
                angle += 2.0 * Math.PI;
            while (angle > 2.0 * Math.PI)
                angle -= 2.0 * Math.PI;
            return angle;
        }



        private static ClosureReport AdjustPolylineChainByLineLengthsOnly(List<PolylineSegmentData> segments, List<Point3d> adjustedVertices, Editor ed)
        {
            adjustedVertices.Clear();
            List<Point3d> originalVertices = new List<Point3d> { segments[0].Start };
            originalVertices.AddRange(segments.Select(seg => seg.End));

            Vector3d misclosureVector = originalVertices[originalVertices.Count - 1] - originalVertices[0];
            List<int> lineIndexes = new List<int>();
            List<Vector3d> lineDirections = new List<Vector3d>();
            List<double> lineWeights = new List<double>();

            for (int i = 0; i < segments.Count; i++)
            {
                if (Math.Abs(segments[i].Bulge) > 1.0e-12)
                    continue;

                Vector3d vector = segments[i].End - segments[i].Start;
                if (vector.Length <= 1.0e-9)
                    continue;

                lineIndexes.Add(i);
                lineDirections.Add(vector.GetNormal());
                lineWeights.Add(Math.Max(vector.Length, 1.0));
            }

            Dictionary<int, double> lengthDeltas = TrySolveLineLengthDeltas(lineIndexes, lineDirections, lineWeights, misclosureVector, out double residual)
                ? lineIndexes.Select((idx, n) => new { idx, delta = _lastLengthDeltaSolution[n] }).ToDictionary(x => x.idx, x => x.delta)
                : new Dictionary<int, double>();

            if (residual > 0.001)
                ed.WriteMessage("\nSURVEY-AUTO-CLOSURE length-only warning: selected straight segment bearings could not absorb the full misclosure. Closure residual after length-only solve = " + residual.ToString("0.####", CultureInfo.InvariantCulture) + ".");

            Point3d current = originalVertices[0];
            adjustedVertices.Add(current);
            for (int i = 0; i < segments.Count; i++)
            {
                Vector3d originalVector = segments[i].End - segments[i].Start;
                Vector3d adjustedVector = originalVector;
                if (lengthDeltas.TryGetValue(i, out double delta) && originalVector.Length > 1.0e-9)
                {
                    double newLength = Math.Max(originalVector.Length + delta, 1.0e-6);
                    adjustedVector = originalVector.GetNormal() * newLength;
                }

                current = current + adjustedVector;
                adjustedVertices.Add(current);
                segments[i].AdjustedBulge = segments[i].Bulge;
            }

            return BuildClosureReport(originalVertices, adjustedVertices, segments.Select(seg => seg.Length).ToList(), segments.Select(seg => seg.AdjustedBulge).ToList());
        }

        private static ClosureReport AdjustEntityChainByLineLengthsOnly(List<SegmentRef> segments, Editor ed)
        {
            List<Point3d> originalVertices = new List<Point3d> { segments[0].OriginalStart };
            originalVertices.AddRange(segments.Select(seg => seg.OriginalEnd));
            Vector3d misclosureVector = originalVertices[originalVertices.Count - 1] - originalVertices[0];

            List<int> lineIndexes = new List<int>();
            List<Vector3d> lineDirections = new List<Vector3d>();
            List<double> lineWeights = new List<double>();
            for (int i = 0; i < segments.Count; i++)
            {
                if (!string.Equals(segments[i].Type, "LINE", StringComparison.OrdinalIgnoreCase))
                    continue;

                Vector3d vector = segments[i].OriginalEnd - segments[i].OriginalStart;
                if (vector.Length <= 1.0e-9)
                    continue;

                lineIndexes.Add(i);
                lineDirections.Add(vector.GetNormal());
                lineWeights.Add(Math.Max(vector.Length, 1.0));
            }

            Dictionary<int, double> lengthDeltas = TrySolveLineLengthDeltas(lineIndexes, lineDirections, lineWeights, misclosureVector, out double residual)
                ? lineIndexes.Select((idx, n) => new { idx, delta = _lastLengthDeltaSolution[n] }).ToDictionary(x => x.idx, x => x.delta)
                : new Dictionary<int, double>();

            if (residual > 0.001)
                ed.WriteMessage("\nSURVEY-AUTO-CLOSURE length-only warning: selected straight segment bearings could not absorb the full misclosure. Closure residual after length-only solve = " + residual.ToString("0.####", CultureInfo.InvariantCulture) + ".");

            Point3d current = segments[0].OriginalStart;
            List<Point3d> adjustedVertices = new List<Point3d> { current };
            for (int i = 0; i < segments.Count; i++)
            {
                SegmentRef segment = segments[i];
                Vector3d originalVector = segment.OriginalEnd - segment.OriginalStart;
                Vector3d adjustedVector = originalVector;

                if (string.Equals(segment.Type, "LINE", StringComparison.OrdinalIgnoreCase)
                    && lengthDeltas.TryGetValue(i, out double delta)
                    && originalVector.Length > 1.0e-9)
                {
                    double newLength = Math.Max(originalVector.Length + delta, 1.0e-6);
                    adjustedVector = originalVector.GetNormal() * newLength;
                }

                segment.AdjustedStart = current;
                segment.AdjustedEnd = current + adjustedVector;
                segment.AdjustedBulge = segment.OriginalBulge;
                current = segment.AdjustedEnd;
                adjustedVertices.Add(current);
            }

            return BuildClosureReport(originalVertices, adjustedVertices, segments.Select(seg => seg.OriginalLength).ToList(), segments.Select(seg => seg.AdjustedBulge).ToList());
        }

        private static List<double> _lastLengthDeltaSolution = new List<double>();

        private static bool TrySolveLineLengthDeltas(List<int> lineIndexes, List<Vector3d> directions, List<double> weights, Vector3d misclosureVector, out double residual)
        {
            residual = misclosureVector.Length;
            _lastLengthDeltaSolution = new List<double>();
            if (lineIndexes.Count == 0 || directions.Count != lineIndexes.Count || weights.Count != lineIndexes.Count)
                return false;

            double m00 = 0.0;
            double m01 = 0.0;
            double m11 = 0.0;
            for (int i = 0; i < directions.Count; i++)
            {
                double w = Math.Max(weights[i], 1.0e-9);
                double ux = directions[i].X;
                double uy = directions[i].Y;
                m00 += w * ux * ux;
                m01 += w * ux * uy;
                m11 += w * uy * uy;
            }

            double det = m00 * m11 - m01 * m01;
            if (Math.Abs(det) <= 1.0e-12)
                return false;

            double bx = -misclosureVector.X;
            double by = -misclosureVector.Y;
            double lambdaX = (m11 * bx - m01 * by) / det;
            double lambdaY = (-m01 * bx + m00 * by) / det;

            Vector3d achieved = new Vector3d(0.0, 0.0, 0.0);
            for (int i = 0; i < directions.Count; i++)
            {
                double delta = Math.Max(weights[i], 1.0e-9) * (directions[i].X * lambdaX + directions[i].Y * lambdaY);
                _lastLengthDeltaSolution.Add(delta);
                achieved += directions[i] * delta;
            }

            residual = (new Vector3d(bx, by, 0.0) - achieved).Length;
            return true;
        }

        private static void EvaluatePolylineArcTangencies(List<PolylineSegmentData> segments, List<Point3d> adjustedVertices)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                PolylineSegmentData segment = segments[i];
                segment.AdjustedBulge = segment.Bulge;
                if (Math.Abs(segment.Bulge) <= 1.0e-12)
                    continue;

                bool tangentIn = false;
                bool tangentOut = false;
                double? adjustedInDirection = null;
                double? adjustedOutDirection = null;

                if (i > 0)
                {
                    PolylineSegmentData previous = segments[i - 1];
                    double previousOriginalEndTangent = GetSegmentEndTangent(previous.Start, previous.End, previous.Bulge);
                    double arcOriginalStartTangent = GetSegmentStartTangent(segment.Start, segment.End, segment.Bulge);
                    if (AngleDeltaSeconds(previousOriginalEndTangent, arcOriginalStartTangent) <= TangencyDetectToleranceSeconds)
                    {
                        tangentIn = true;
                        adjustedInDirection = GetSegmentEndTangent(adjustedVertices[i - 1], adjustedVertices[i], previous.AdjustedBulge);
                    }
                }

                if (i < segments.Count - 1)
                {
                    PolylineSegmentData next = segments[i + 1];
                    double arcOriginalEndTangent = GetSegmentEndTangent(segment.Start, segment.End, segment.Bulge);
                    double nextOriginalStartTangent = GetSegmentStartTangent(next.Start, next.End, next.Bulge);
                    if (AngleDeltaSeconds(arcOriginalEndTangent, nextOriginalStartTangent) <= TangencyDetectToleranceSeconds)
                    {
                        tangentOut = true;
                        adjustedOutDirection = GetSegmentStartTangent(adjustedVertices[i + 1], adjustedVertices[i + 2], next.AdjustedBulge);
                    }
                }

                if (!tangentIn && !tangentOut)
                    continue;

                double adjustedBulge = segment.Bulge;
                segment.AdjustedBulge = adjustedBulge;
                segment.TangentInLocked = tangentIn;
                segment.TangentOutLocked = tangentOut;
                segment.TangencyInErrorSeconds = tangentIn && adjustedInDirection.HasValue
                    ? AngleDeltaSeconds(adjustedInDirection.Value, GetSegmentStartTangent(adjustedVertices[i], adjustedVertices[i + 1], adjustedBulge))
                    : 0.0;
                segment.TangencyOutErrorSeconds = tangentOut && adjustedOutDirection.HasValue
                    ? AngleDeltaSeconds(GetSegmentEndTangent(adjustedVertices[i], adjustedVertices[i + 1], adjustedBulge), adjustedOutDirection.Value)
                    : 0.0;
                segment.TangencyWarning = segment.TangencyInErrorSeconds > TangencyToleranceSeconds || segment.TangencyOutErrorSeconds > TangencyToleranceSeconds;
            }
        }


        private sealed class SegmentConstraintState
        {
            public bool LockRadius { get; set; }
            public bool LockBearing { get; set; }
            public bool LockLength { get; set; }
            public List<int> ParallelWith { get; } = new List<int>();
            public double? ReferenceBearingRadians { get; set; }
            public bool PerpendicularToReference { get; set; }
            public double? ReferenceOffsetDistance { get; set; }
            public Point3d? ReferencePoint { get; set; }
            public Vector3d? ReferenceNormal { get; set; }
            public bool FixedVector => LockBearing && LockLength;

            public string Describe()
            {
                List<string> parts = new List<string>();
                if (FixedVector)
                    parts.Add("FIXED VECTOR");
                else
                {
                    if (LockBearing)
                        parts.Add("LOCK BEARING");
                    if (LockLength)
                        parts.Add("LOCK LENGTH");
                }

                if (LockRadius)
                    parts.Add("LOCK RADIUS");
                if (ParallelWith.Count > 0)
                    parts.Add("PARALLEL");
                if (ReferenceBearingRadians.HasValue && ReferenceOffsetDistance.HasValue)
                    parts.Add("OFFSET REF");
                else if (ReferenceBearingRadians.HasValue && PerpendicularToReference)
                    parts.Add("PERP REF");
                else if (ReferenceBearingRadians.HasValue)
                    parts.Add("PARALLEL REF");

                return string.Join("; ", parts);
            }
        }

        private static void ApplyUserConstraints(List<SegmentRef> segments, Editor ed)
        {
            IReadOnlyList<ClosureConstraint> constraints = SurveyClosureConstraintStore.Current;
            if (constraints.Count == 0)
                return;

            SegmentConstraintState[] states = Enumerable.Range(0, segments.Count).Select(_ => new SegmentConstraintState()).ToArray();
            int applied = 0;
            int warnings = 0;

            foreach (ClosureConstraint constraint in constraints)
            {
                int firstIndex = segments.FindIndex(s => s.Id == constraint.FirstObjectId);
                if (firstIndex < 0)
                {
                    warnings++;
                    ed.WriteMessage("\nSURVEY-AUTO-CLOSURE constraint warning: " + SurveyClosureConstraintCommands.FormatConstraintKind(constraint.Kind) + " skipped; selected object was not part of this boundary run.");
                    continue;
                }

                bool ok = true;
                switch (constraint.Kind)
                {
                    case ClosureConstraintKind.LockRadius:
                        ok = string.Equals(segments[firstIndex].Type, "ARC", StringComparison.OrdinalIgnoreCase);
                        states[firstIndex].LockRadius = ok;
                        break;

                    case ClosureConstraintKind.LockBearing:
                        states[firstIndex].LockBearing = true;
                        break;

                    case ClosureConstraintKind.LockLength:
                        states[firstIndex].LockLength = true;
                        break;

                    case ClosureConstraintKind.KeepParallel:
                        int secondIndex = segments.FindIndex(s => s.Id == constraint.SecondObjectId);
                        ok = secondIndex >= 0
                             && string.Equals(segments[firstIndex].Type, "LINE", StringComparison.OrdinalIgnoreCase)
                             && string.Equals(segments[secondIndex].Type, "LINE", StringComparison.OrdinalIgnoreCase);
                        if (ok)
                        {
                            states[firstIndex].ParallelWith.Add(secondIndex);
                            states[secondIndex].ParallelWith.Add(firstIndex);
                        }
                        break;

                    case ClosureConstraintKind.ParallelToReference:
                    case ClosureConstraintKind.OffsetToReference:
                    case ClosureConstraintKind.PerpendicularToReference:
                        double referenceBearing = 0.0;
                        Point3d referencePoint = Point3d.Origin;
                        Vector3d referenceNormal = Vector3d.XAxis;
                        ok = string.Equals(segments[firstIndex].Type, "LINE", StringComparison.OrdinalIgnoreCase)
                             && TryGetReferenceLineData(constraint.SecondObjectId, out referenceBearing, out referencePoint, out referenceNormal);
                        if (ok)
                        {
                            states[firstIndex].LockBearing = true;
                            states[firstIndex].ReferenceBearingRadians = referenceBearing;
                            states[firstIndex].ReferencePoint = referencePoint;
                            states[firstIndex].ReferenceNormal = referenceNormal;
                            states[firstIndex].PerpendicularToReference = constraint.Kind == ClosureConstraintKind.PerpendicularToReference;
                            if (constraint.Kind == ClosureConstraintKind.OffsetToReference)
                                states[firstIndex].ReferenceOffsetDistance = constraint.ReferenceOffsetDistance;
                        }
                        break;

                    default:
                        ok = false;
                        break;
                }

                if (ok)
                    applied++;
                else
                {
                    warnings++;
                    ed.WriteMessage("\nSURVEY-AUTO-CLOSURE constraint warning: " + SurveyClosureConstraintCommands.FormatConstraintKind(constraint.Kind) + " could not be fully applied.");
                }
            }

            ApplyCombinedConstraintStates(segments, states, ed, ref warnings);

            for (int i = 0; i < segments.Count; i++)
            {
                segments[i].ConstraintLockBearing = states[i].LockBearing;
                segments[i].ConstraintLockLength = states[i].LockLength;
                segments[i].ConstraintLockRadius = states[i].LockRadius;
                segments[i].ConstraintFixedVector = states[i].FixedVector;
                segments[i].ConstraintState = states[i].Describe();
                segments[i].ConstraintTargetOffset = states[i].ReferenceOffsetDistance;
                segments[i].ConstraintActualOffset = ComputeSignedOffsetIfReference(segments[i], states[i]);
                segments[i].ConstraintOffsetDelta = segments[i].ConstraintTargetOffset.HasValue && segments[i].ConstraintActualOffset.HasValue
                    ? segments[i].ConstraintActualOffset.Value - segments[i].ConstraintTargetOffset.Value
                    : null;
            }

            ed.WriteMessage("\nSURVEY-AUTO-CLOSURE constraints: applied " + applied.ToString(CultureInfo.InvariantCulture) + " constraint(s); warnings " + warnings.ToString(CultureInfo.InvariantCulture) + ".");
        }

        private static void ApplyCombinedConstraintStates(List<SegmentRef> segments, SegmentConstraintState[] states, Editor ed, ref int warnings)
        {
            if (segments.Count == 0)
                return;

            // First enforce stackable per-segment constraints. Bearing + Length means the
            // whole segment vector is fixed and must not receive later closure correction.
            RebuildChainWithConstraints(segments, states);

            // Parallel relationships are applied after the single-segment locks so the
            // relationship uses the controlling line direction.
            ApplyParallelConstraints(segments, states);
            RebuildChainWithConstraints(segments, states);

            // Any remaining closure residual is pushed only into unconstrained segments.
            // This is still a Phase 3A constrained redistribution, not a full least-squares
            // network solver, but it prevents locked vectors from being changed.
            Vector3d residual = segments[segments.Count - 1].AdjustedEnd - segments[0].AdjustedStart;
            if (residual.Length > ComputationalClosureEpsilon)
            {
                List<int> freeIndexes = Enumerable.Range(0, segments.Count)
                    .Where(i => !states[i].LockBearing && !states[i].LockLength)
                    .ToList();

                if (freeIndexes.Count == 0)
                {
                    warnings++;
                    ed.WriteMessage("\nSURVEY-AUTO-CLOSURE constraint warning: No fully free segments remained to absorb the residual closure vector. Locked constraints were preserved, but closure may not be exact.");
                }
                else
                {
                    DistributeResidualToFreeSegments(segments, states, residual, freeIndexes);
                    RebuildChainWithConstraints(segments, states);
                }
            }

            ApplyReferenceOffsetConstraints(segments, states, ed, ref warnings);

            // Radius locks are a bulge solve after the final chord position is known.
            for (int i = 0; i < segments.Count; i++)
            {
                if (states[i].LockRadius && !ApplyLockRadiusConstraint(segments[i]))
                {
                    warnings++;
                    ed.WriteMessage("\nSURVEY-AUTO-CLOSURE constraint warning: LOCK RADIUS could not be maintained for segment " + (i + 1).ToString(CultureInfo.InvariantCulture) + ".");
                }
            }
        }

        private static void ApplyReferenceOffsetConstraints(List<SegmentRef> segments, SegmentConstraintState[] states, Editor ed, ref int warnings)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                SegmentConstraintState state = states[i];
                if (!state.ReferenceOffsetDistance.HasValue || !state.ReferencePoint.HasValue || !state.ReferenceNormal.HasValue)
                    continue;

                if (state.FixedVector || state.LockLength)
                {
                    warnings++;
                    ed.WriteMessage("\nSURVEY-AUTO-CLOSURE constraint warning: OFFSET TO REFERENCE was not moved for segment " + (i + 1).ToString(CultureInfo.InvariantCulture) + " because its length/vector is locked.");
                    continue;
                }

                double currentOffset = ComputeSignedOffset(segments[i].AdjustedStart, state.ReferencePoint.Value, state.ReferenceNormal.Value);
                double delta = state.ReferenceOffsetDistance.Value - currentOffset;
                if (Math.Abs(delta) <= 1.0e-9)
                    continue;

                Vector3d shift = state.ReferenceNormal.Value.GetNormal().MultiplyBy(delta);
                segments[i].AdjustedStart = segments[i].AdjustedStart + shift;
                segments[i].AdjustedEnd = segments[i].AdjustedEnd + shift;

                // Keep the adjusted chain connected by moving adjacent shared vertices to the constrained endpoints.
                // This is still a first-pass constraint engine; neighboring segments absorb the position correction.
                if (i > 0 && !states[i - 1].FixedVector)
                    segments[i - 1].AdjustedEnd = segments[i].AdjustedStart;
                if (i + 1 < segments.Count && !states[i + 1].FixedVector)
                    segments[i + 1].AdjustedStart = segments[i].AdjustedEnd;
            }
        }

        private static double? ComputeSignedOffsetIfReference(SegmentRef segment, SegmentConstraintState state)
        {
            if (!state.ReferencePoint.HasValue || !state.ReferenceNormal.HasValue)
                return null;

            return ComputeSignedOffset(segment.AdjustedStart, state.ReferencePoint.Value, state.ReferenceNormal.Value);
        }

        private static double ComputeSignedOffset(Point3d point, Point3d referencePoint, Vector3d referenceNormal)
        {
            Vector3d normal = referenceNormal.Length <= 1.0e-9 ? Vector3d.YAxis : referenceNormal.GetNormal();
            return (point - referencePoint).DotProduct(normal);
        }

        private static void RebuildChainWithConstraints(List<SegmentRef> segments, SegmentConstraintState[] states)
        {
            Point3d currentStart = segments[0].AdjustedStart;
            for (int i = 0; i < segments.Count; i++)
            {
                SegmentRef segment = segments[i];
                Vector3d currentVector = segment.AdjustedEnd - segment.AdjustedStart;
                if (currentVector.Length <= 1.0e-9)
                    currentVector = segment.OriginalEnd - segment.OriginalStart;

                segment.AdjustedStart = currentStart;

                Vector3d newVector = currentVector;
                double currentLength = Math.Max(currentVector.Length, 1.0e-9);
                Vector3d currentDirection = currentVector / currentLength;

                if (states[i].FixedVector)
                {
                    double originalLength = GetSegmentChordLength(segment.OriginalStart, segment.OriginalEnd);
                    double originalBearing = GetConstraintBearing(segment, states[i]);
                    newVector = new Vector3d(Math.Cos(originalBearing) * originalLength, Math.Sin(originalBearing) * originalLength, 0.0);
                }
                else if (states[i].LockBearing)
                {
                    double originalBearing = GetConstraintBearing(segment, states[i]);
                    newVector = new Vector3d(Math.Cos(originalBearing) * currentLength, Math.Sin(originalBearing) * currentLength, 0.0);
                }
                else if (states[i].LockLength)
                {
                    double originalLength = GetSegmentChordLength(segment.OriginalStart, segment.OriginalEnd);
                    newVector = currentDirection * originalLength;
                }

                segment.AdjustedEnd = segment.AdjustedStart + newVector;
                currentStart = segment.AdjustedEnd;
                if (i + 1 < segments.Count)
                    segments[i + 1].AdjustedStart = currentStart;
            }
        }


        private static double GetConstraintBearing(SegmentRef segment, SegmentConstraintState state)
        {
            if (state.ReferenceBearingRadians.HasValue)
            {
                double bearing = state.ReferenceBearingRadians.Value;
                if (state.PerpendicularToReference)
                    bearing += Math.PI / 2.0;

                Vector3d originalDir = segment.OriginalEnd - segment.OriginalStart;
                Vector3d targetDir = new Vector3d(Math.Cos(bearing), Math.Sin(bearing), 0.0);
                if (originalDir.Length > 1.0e-9 && targetDir.DotProduct(originalDir.GetNormal()) < 0.0)
                    bearing += Math.PI;

                return NormalizeRadians(bearing);
            }

            return Math.Atan2(segment.OriginalEnd.Y - segment.OriginalStart.Y, segment.OriginalEnd.X - segment.OriginalStart.X);
        }

        private static bool TryGetReferenceLineData(ObjectId id, out double bearing, out Point3d referencePoint, out Vector3d referenceNormal)
        {
            bearing = 0.0;
            referencePoint = Point3d.Origin;
            referenceNormal = Vector3d.YAxis;
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null || id.IsNull || id.IsErased)
                return false;

            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is not Line line)
                    return false;

                Vector3d vector = line.EndPoint - line.StartPoint;
                if (vector.Length <= 1.0e-9)
                    return false;

                Vector3d direction = vector.GetNormal();
                bearing = Math.Atan2(direction.Y, direction.X);
                referencePoint = line.StartPoint;
                referenceNormal = new Vector3d(-direction.Y, direction.X, 0.0);
                tr.Commit();
                return true;
            }
        }

        private static double NormalizeRadians(double value)
        {
            double twoPi = Math.PI * 2.0;
            while (value < 0.0)
                value += twoPi;
            while (value >= twoPi)
                value -= twoPi;
            return value;
        }

        private static void ApplyParallelConstraints(List<SegmentRef> segments, SegmentConstraintState[] states)
        {
            for (int i = 0; i < states.Length; i++)
            {
                foreach (int targetIndex in states[i].ParallelWith)
                {
                    if (targetIndex < 0 || targetIndex >= segments.Count || targetIndex == i)
                        continue;

                    SegmentRef source = segments[i];
                    SegmentRef target = segments[targetIndex];
                    Vector3d sourceVector = source.AdjustedEnd - source.AdjustedStart;
                    Vector3d targetVector = target.AdjustedEnd - target.AdjustedStart;
                    if (sourceVector.Length <= 1.0e-9 || targetVector.Length <= 1.0e-9)
                        continue;

                    Vector3d direction = sourceVector.GetNormal();
                    Vector3d originalTargetDirection = (target.OriginalEnd - target.OriginalStart).GetNormal();
                    if (direction.DotProduct(originalTargetDirection) < 0.0)
                        direction = direction.MultiplyBy(-1.0);

                    target.AdjustedEnd = target.AdjustedStart + direction * targetVector.Length;
                }
            }
        }

        private static void DistributeResidualToFreeSegments(List<SegmentRef> segments, SegmentConstraintState[] states, Vector3d residual, List<int> freeIndexes)
        {
            double totalWeight = freeIndexes.Sum(i => Math.Max(segments[i].AdjustedStart.DistanceTo(segments[i].AdjustedEnd), 1.0));
            if (totalWeight <= 1.0e-9)
                return;

            double cumulativeWeight = 0.0;
            for (int i = 0; i < segments.Count; i++)
            {
                if (!freeIndexes.Contains(i))
                    continue;

                cumulativeWeight += Math.Max(segments[i].AdjustedStart.DistanceTo(segments[i].AdjustedEnd), 1.0);
                double ratio = cumulativeWeight / totalWeight;
                Point3d correctedEnd = segments[i].AdjustedEnd - residual.MultiplyBy(ratio);
                segments[i].AdjustedEnd = correctedEnd;
                if (i + 1 < segments.Count)
                    segments[i + 1].AdjustedStart = correctedEnd;
            }
        }

        private static double GetSegmentChordLength(Point3d start, Point3d end) => start.DistanceTo(end);

        private static bool ApplyLockRadiusConstraint(SegmentRef segment)
        {
            if (!string.Equals(segment.Type, "ARC", StringComparison.OrdinalIgnoreCase))
                return false;

            double radius = GetBulgeRadius(segment.OriginalStart, segment.OriginalEnd, segment.OriginalBulge);
            if (radius <= 1.0e-9)
                return false;

            double chord = segment.AdjustedStart.DistanceTo(segment.AdjustedEnd);
            if (chord <= 1.0e-9 || chord > radius * 2.0)
                return false;

            double theta = 2.0 * Math.Asin(Math.Max(-1.0, Math.Min(1.0, chord / (2.0 * radius))));
            double sign = Math.Sign(segment.OriginalBulge);
            if (Math.Abs(sign) < 1.0e-12)
                sign = 1.0;

            segment.AdjustedBulge = sign * Math.Tan(theta / 4.0);
            return !double.IsNaN(segment.AdjustedBulge) && !double.IsInfinity(segment.AdjustedBulge);
        }

        private static bool ApplyLockBearingConstraint(List<SegmentRef> segments, int index)
        {
            SegmentRef segment = segments[index];
            if (index >= segments.Count - 1)
                return false;

            double length = segment.AdjustedStart.DistanceTo(segment.AdjustedEnd);
            if (length <= 1.0e-9)
                return false;

            double bearing = Math.Atan2(segment.OriginalEnd.Y - segment.OriginalStart.Y, segment.OriginalEnd.X - segment.OriginalStart.X);
            Point3d newEnd = new Point3d(
                segment.AdjustedStart.X + Math.Cos(bearing) * length,
                segment.AdjustedStart.Y + Math.Sin(bearing) * length,
                segment.AdjustedEnd.Z);

            segment.AdjustedEnd = newEnd;
            segments[index + 1].AdjustedStart = newEnd;
            return true;
        }

        private static bool ApplyLockLengthConstraint(List<SegmentRef> segments, int index)
        {
            SegmentRef segment = segments[index];
            if (index >= segments.Count - 1)
                return false;

            double targetLength = segment.OriginalStart.DistanceTo(segment.OriginalEnd);
            double currentLength = segment.AdjustedStart.DistanceTo(segment.AdjustedEnd);
            if (targetLength <= 1.0e-9 || currentLength <= 1.0e-9)
                return false;

            Vector3d direction = (segment.AdjustedEnd - segment.AdjustedStart).GetNormal();
            Point3d newEnd = segment.AdjustedStart + direction * targetLength;
            segment.AdjustedEnd = newEnd;
            segments[index + 1].AdjustedStart = newEnd;
            return true;
        }

        private static bool ApplyKeepParallelConstraint(List<SegmentRef> segments, int firstIndex, ObjectId secondObjectId)
        {
            int secondIndex = segments.FindIndex(s => s.Id == secondObjectId);
            if (secondIndex < 0 || secondIndex >= segments.Count - 1)
                return false;

            SegmentRef first = segments[firstIndex];
            SegmentRef second = segments[secondIndex];
            if (!string.Equals(first.Type, "LINE", StringComparison.OrdinalIgnoreCase) || !string.Equals(second.Type, "LINE", StringComparison.OrdinalIgnoreCase))
                return false;

            double firstLength = first.AdjustedStart.DistanceTo(first.AdjustedEnd);
            double secondLength = second.AdjustedStart.DistanceTo(second.AdjustedEnd);
            if (firstLength <= 1.0e-9 || secondLength <= 1.0e-9)
                return false;

            Vector3d direction = (first.AdjustedEnd - first.AdjustedStart).GetNormal();
            Vector3d originalSecondDirection = (second.OriginalEnd - second.OriginalStart).GetNormal();
            if (direction.DotProduct(originalSecondDirection) < 0.0)
                direction = direction.MultiplyBy(-1.0);

            Point3d newEnd = second.AdjustedStart + direction * secondLength;
            second.AdjustedEnd = newEnd;
            segments[secondIndex + 1].AdjustedStart = newEnd;
            return true;
        }

        private static void ReapplyLockedSegmentConstraints(List<SegmentRef> segments)
        {
            if (segments.Count == 0)
                return;

            for (int i = 0; i < segments.Count; i++)
            {
                SegmentRef segment = segments[i];
                if (!segment.ConstraintLockBearing && !segment.ConstraintLockLength)
                    continue;

                Vector3d currentVector = segment.AdjustedEnd - segment.AdjustedStart;
                if (currentVector.Length <= 1.0e-9)
                    currentVector = segment.OriginalEnd - segment.OriginalStart;

                double targetLength = segment.ConstraintLockLength
                    ? GetSegmentChordLength(segment.OriginalStart, segment.OriginalEnd)
                    : Math.Max(currentVector.Length, 1.0e-9);

                double targetBearing = segment.ConstraintLockBearing
                    ? Math.Atan2(segment.OriginalEnd.Y - segment.OriginalStart.Y, segment.OriginalEnd.X - segment.OriginalStart.X)
                    : Math.Atan2(currentVector.Y, currentVector.X);

                Point3d newEnd = new Point3d(
                    segment.AdjustedStart.X + Math.Cos(targetBearing) * targetLength,
                    segment.AdjustedStart.Y + Math.Sin(targetBearing) * targetLength,
                    segment.AdjustedEnd.Z);

                if (segment.AdjustedEnd.DistanceTo(newEnd) <= 1.0e-10)
                    continue;

                segment.AdjustedEnd = newEnd;
                if (i + 1 < segments.Count)
                    segments[i + 1].AdjustedStart = newEnd;
            }
        }

        private static void EvaluateEntityArcTangencies(List<SegmentRef> segments)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                SegmentRef segment = segments[i];
                segment.AdjustedBulge = segment.OriginalBulge;
                if (!string.Equals(segment.Type, "ARC", StringComparison.OrdinalIgnoreCase) || Math.Abs(segment.OriginalBulge) <= 1.0e-12)
                    continue;

                bool tangentIn = false;
                bool tangentOut = false;
                double? adjustedInDirection = null;
                double? adjustedOutDirection = null;

                if (i > 0)
                {
                    SegmentRef previous = segments[i - 1];
                    double previousOriginalEndTangent = GetSegmentEndTangent(previous.OriginalStart, previous.OriginalEnd, previous.OriginalBulge);
                    double arcOriginalStartTangent = GetSegmentStartTangent(segment.OriginalStart, segment.OriginalEnd, segment.OriginalBulge);
                    if (AngleDeltaSeconds(previousOriginalEndTangent, arcOriginalStartTangent) <= TangencyDetectToleranceSeconds)
                    {
                        tangentIn = true;
                        adjustedInDirection = GetSegmentEndTangent(previous.AdjustedStart, previous.AdjustedEnd, previous.AdjustedBulge);
                    }
                }

                if (i < segments.Count - 1)
                {
                    SegmentRef next = segments[i + 1];
                    double arcOriginalEndTangent = GetSegmentEndTangent(segment.OriginalStart, segment.OriginalEnd, segment.OriginalBulge);
                    double nextOriginalStartTangent = GetSegmentStartTangent(next.OriginalStart, next.OriginalEnd, next.OriginalBulge);
                    if (AngleDeltaSeconds(arcOriginalEndTangent, nextOriginalStartTangent) <= TangencyDetectToleranceSeconds)
                    {
                        tangentOut = true;
                        adjustedOutDirection = GetSegmentStartTangent(next.AdjustedStart, next.AdjustedEnd, next.AdjustedBulge);
                    }
                }

                segment.TangentInLocked = tangentIn;
                segment.TangentOutLocked = tangentOut;
                segment.TangencyInErrorSeconds = tangentIn && adjustedInDirection.HasValue
                    ? AngleDeltaSeconds(adjustedInDirection.Value, GetSegmentStartTangent(segment.AdjustedStart, segment.AdjustedEnd, segment.AdjustedBulge))
                    : 0.0;
                segment.TangencyOutErrorSeconds = tangentOut && adjustedOutDirection.HasValue
                    ? AngleDeltaSeconds(GetSegmentEndTangent(segment.AdjustedStart, segment.AdjustedEnd, segment.AdjustedBulge), adjustedOutDirection.Value)
                    : 0.0;
                segment.TangencyWarning = segment.TangencyInErrorSeconds > TangencyToleranceSeconds || segment.TangencyOutErrorSeconds > TangencyToleranceSeconds;
            }
        }

        private static double GetBulgeRadius(Point3d start, Point3d end, double bulge)
        {
            double chord = start.DistanceTo(end);
            if (chord <= 1.0e-12 || Math.Abs(bulge) <= 1.0e-12)
                return 0.0;

            double includedAngle = 4.0 * Math.Atan(Math.Abs(bulge));
            double sinHalf = Math.Sin(includedAngle / 2.0);
            if (Math.Abs(sinHalf) <= 1.0e-12)
                return 0.0;

            return Math.Abs(chord / (2.0 * sinHalf));
        }

        private static bool TryApplyAdjustedLineFillet(SegmentRef previousLine, SegmentRef arcSegment, SegmentRef nextLine, double radius, out bool warning)
        {
            warning = false;
            if (radius <= 1.0e-9)
            {
                warning = true;
                return false;
            }

            if (!TryIntersectInfiniteLines(previousLine.AdjustedStart, previousLine.AdjustedEnd, nextLine.AdjustedStart, nextLine.AdjustedEnd, out Point3d intersection))
            {
                warning = true;
                return false;
            }

            Vector3d rayToPreviousStart = previousLine.AdjustedStart - intersection;
            Vector3d rayToNextEnd = nextLine.AdjustedEnd - intersection;
            if (rayToPreviousStart.Length <= 1.0e-9 || rayToNextEnd.Length <= 1.0e-9)
            {
                warning = true;
                return false;
            }

            Vector3d v1 = rayToPreviousStart.GetNormal();
            Vector3d v2 = rayToNextEnd.GetNormal();
            double dot = Math.Max(-1.0, Math.Min(1.0, v1.DotProduct(v2)));
            double interiorAngle = Math.Acos(dot);
            if (interiorAngle <= 1.0e-6 || interiorAngle >= Math.PI - 1.0e-6)
            {
                warning = true;
                return false;
            }

            double tangentDistance = radius / Math.Tan(interiorAngle / 2.0);
            if (double.IsNaN(tangentDistance) || double.IsInfinity(tangentDistance) || tangentDistance <= 1.0e-9)
            {
                warning = true;
                return false;
            }

            Point3d tangentStart = intersection + (v1 * tangentDistance);
            Point3d tangentEnd = intersection + (v2 * tangentDistance);
            double includedAngle = Math.PI - interiorAngle;
            double bulgeMagnitude = Math.Abs(Math.Tan(includedAngle / 4.0));
            if (bulgeMagnitude <= 1.0e-12 || double.IsNaN(bulgeMagnitude) || double.IsInfinity(bulgeMagnitude))
            {
                warning = true;
                return false;
            }

            double positiveBulge = bulgeMagnitude;
            double negativeBulge = -bulgeMagnitude;
            double positiveError = GetTotalTangencyErrorSeconds(previousLine.AdjustedStart, tangentStart, tangentStart, tangentEnd, positiveBulge, tangentEnd, nextLine.AdjustedEnd);
            double negativeError = GetTotalTangencyErrorSeconds(previousLine.AdjustedStart, tangentStart, tangentStart, tangentEnd, negativeBulge, tangentEnd, nextLine.AdjustedEnd);
            double sourceSignPreferred = Math.Sign(arcSegment.OriginalBulge) >= 0.0 ? positiveBulge : negativeBulge;
            double chosenBulge;

            if (Math.Abs(positiveError - negativeError) <= 1.0e-6)
                chosenBulge = sourceSignPreferred;
            else
                chosenBulge = positiveError < negativeError ? positiveBulge : negativeBulge;

            previousLine.AdjustedEnd = tangentStart;
            arcSegment.AdjustedStart = tangentStart;
            arcSegment.AdjustedEnd = tangentEnd;
            arcSegment.AdjustedBulge = chosenBulge;
            nextLine.AdjustedStart = tangentEnd;

            double chosenError = Math.Min(positiveError, negativeError);
            if (chosenError > TangencyToleranceSeconds)
                warning = true;

            return true;
        }

        private static double GetTotalTangencyErrorSeconds(Point3d previousStart, Point3d arcStart, Point3d arcEndStart, Point3d arcEnd, double bulge, Point3d nextStart, Point3d nextEnd)
        {
            double previousEndTangent = GetSegmentEndTangent(previousStart, arcStart, 0.0);
            double arcStartTangent = GetSegmentStartTangent(arcEndStart, arcEnd, bulge);
            double arcEndTangent = GetSegmentEndTangent(arcEndStart, arcEnd, bulge);
            double nextStartTangent = GetSegmentStartTangent(nextStart, nextEnd, 0.0);
            return AngleDeltaSeconds(previousEndTangent, arcStartTangent) + AngleDeltaSeconds(arcEndTangent, nextStartTangent);
        }

        private static bool TryIntersectInfiniteLines(Point3d a1, Point3d a2, Point3d b1, Point3d b2, out Point3d intersection)
        {
            intersection = Point3d.Origin;
            double x1 = a1.X;
            double y1 = a1.Y;
            double x2 = a2.X;
            double y2 = a2.Y;
            double x3 = b1.X;
            double y3 = b1.Y;
            double x4 = b2.X;
            double y4 = b2.Y;
            double denominator = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
            if (Math.Abs(denominator) <= 1.0e-12)
                return false;

            double px = ((x1 * y2 - y1 * x2) * (x3 - x4) - (x1 - x2) * (x3 * y4 - y3 * x4)) / denominator;
            double py = ((x1 * y2 - y1 * x2) * (y3 - y4) - (y1 - y2) * (x3 * y4 - y3 * x4)) / denominator;
            intersection = new Point3d(px, py, a1.Z);
            return true;
        }

        private static double SolveBulgeForTangency(Point3d start, Point3d end, double sourceBulge, double? tangentInDirection, double? tangentOutDirection, out bool warning)
        {
            warning = false;
            double chordLength = start.DistanceTo(end);
            if (chordLength <= 1.0e-9 || Math.Abs(sourceBulge) <= 1.0e-12)
            {
                warning = true;
                return sourceBulge;
            }

            double chordDirection = Math.Atan2(end.Y - start.Y, end.X - start.X);
            List<double> thetaCandidates = new List<double>();

            // For an AutoCAD bulge segment, signed included angle theta uses:
            //   start tangent = chord direction - theta / 2
            //   end tangent   = chord direction + theta / 2
            // The previous Phase 2C attempt had these signs reversed, which prevented
            // tangent detection/reporting and could break originally tangent arcs.
            if (tangentInDirection.HasValue)
                thetaCandidates.Add(2.0 * NormalizeAngle(chordDirection - tangentInDirection.Value));

            if (tangentOutDirection.HasValue)
                thetaCandidates.Add(2.0 * NormalizeAngle(tangentOutDirection.Value - chordDirection));

            if (thetaCandidates.Count == 0)
                return sourceBulge;

            double sourceTheta = 4.0 * Math.Atan(sourceBulge);
            double theta = BlendThetaCandidates(thetaCandidates, sourceTheta, out warning);
            if (Math.Abs(theta) < 1.0e-9 || Math.Abs(theta) >= Math.PI * 1.98)
            {
                warning = true;
                return sourceBulge;
            }

            double bulge = Math.Tan(theta / 4.0);
            if (double.IsNaN(bulge) || double.IsInfinity(bulge) || Math.Abs(bulge) > 100.0)
            {
                warning = true;
                return sourceBulge;
            }

            return bulge;
        }

        private static double BlendThetaCandidates(List<double> candidates, double sourceTheta, out bool warning)
        {
            warning = false;
            if (candidates.Count == 1)
                return NormalizeThetaNear(candidates[0], sourceTheta);

            List<double> normalized = candidates.Select(c => NormalizeThetaNear(c, sourceTheta)).ToList();
            double spreadSeconds = Math.Abs(NormalizeAngle(normalized[0] - normalized[1])) * 180.0 / Math.PI * 3600.0;
            if (spreadSeconds > TangencyToleranceSeconds)
                warning = true;

            double x = normalized.Sum(Math.Cos);
            double y = normalized.Sum(Math.Sin);
            double theta = Math.Atan2(y, x);
            return NormalizeThetaNear(theta, sourceTheta);
        }

        private static double NormalizeThetaNear(double theta, double target)
        {
            while (theta - target > Math.PI)
                theta -= 2.0 * Math.PI;
            while (theta - target < -Math.PI)
                theta += 2.0 * Math.PI;
            return theta;
        }

        private static double GetSegmentStartTangent(Point3d start, Point3d end, double bulge)
        {
            double chordDirection = Math.Atan2(end.Y - start.Y, end.X - start.X);
            if (Math.Abs(bulge) <= 1.0e-12)
                return chordDirection;

            double theta = 4.0 * Math.Atan(bulge);
            return NormalizeAngle(chordDirection - theta / 2.0);
        }

        private static double GetSegmentEndTangent(Point3d start, Point3d end, double bulge)
        {
            double chordDirection = Math.Atan2(end.Y - start.Y, end.X - start.X);
            if (Math.Abs(bulge) <= 1.0e-12)
                return chordDirection;

            double theta = 4.0 * Math.Atan(bulge);
            return NormalizeAngle(chordDirection + theta / 2.0);
        }

        private static double AngleDeltaSeconds(double angle1, double angle2)
        {
            return Math.Abs(NormalizeAngle(angle2 - angle1)) * 180.0 / Math.PI * 3600.0;
        }

        private static bool TryBuildOrderedPolylineSegments(Polyline polyline, Point3d pickedStartPoint, out List<PolylineSegmentData> orderedSegments)
        {
            orderedSegments = new List<PolylineSegmentData>();
            int lastVertexIndex = polyline.NumberOfVertices - 1;
            double startDistance = pickedStartPoint.DistanceTo(polyline.GetPoint3dAt(0));
            double endDistance = pickedStartPoint.DistanceTo(polyline.GetPoint3dAt(lastVertexIndex));

            if (startDistance <= endDistance && startDistance <= EndpointMatchTolerance)
            {
                for (int i = 0; i < lastVertexIndex; i++)
                {
                    orderedSegments.Add(new PolylineSegmentData
                    {
                        Start = polyline.GetPoint3dAt(i),
                        End = polyline.GetPoint3dAt(i + 1),
                        Bulge = polyline.GetBulgeAt(i),
                        AdjustedBulge = polyline.GetBulgeAt(i),
                        StartWidth = polyline.GetStartWidthAt(i),
                        EndWidth = polyline.GetEndWidthAt(i)
                    });
                }

                return orderedSegments.Count > 0;
            }

            if (endDistance < startDistance && endDistance <= EndpointMatchTolerance)
            {
                for (int i = lastVertexIndex - 1; i >= 0; i--)
                {
                    orderedSegments.Add(new PolylineSegmentData
                    {
                        Start = polyline.GetPoint3dAt(i + 1),
                        End = polyline.GetPoint3dAt(i),
                        Bulge = -polyline.GetBulgeAt(i),
                        AdjustedBulge = -polyline.GetBulgeAt(i),
                        StartWidth = polyline.GetEndWidthAt(i),
                        EndWidth = polyline.GetStartWidthAt(i)
                    });
                }

                return orderedSegments.Count > 0;
            }

            return false;
        }

        private static ClosureReport AdjustVertexChain(List<Point3d> originalVertices, List<Point3d> adjustedVertices, List<double>? measuredSegmentLengths = null, List<double>? segmentBulges = null)
        {
            adjustedVertices.Clear();
            if (originalVertices.Count < 2)
                return new ClosureReport();

            List<double> segmentLengths = new List<double>();
            double totalLength = 0.0;
            for (int i = 0; i < originalVertices.Count - 1; i++)
            {
                double length = measuredSegmentLengths != null && i < measuredSegmentLengths.Count
                    ? measuredSegmentLengths[i]
                    : originalVertices[i].DistanceTo(originalVertices[i + 1]);
                segmentLengths.Add(length);
                totalLength += length;
            }

            Point3d first = originalVertices[0];
            Point3d last = originalVertices[originalVertices.Count - 1];
            Vector3d misclosureVector = last - first;
            double cumulativeLength = 0.0;
            adjustedVertices.Add(first);
            for (int i = 1; i < originalVertices.Count; i++)
            {
                cumulativeLength += segmentLengths[i - 1];
                double ratio = totalLength <= 1.0e-9 ? 0.0 : cumulativeLength / totalLength;
                Vector3d correction = misclosureVector.MultiplyBy(ratio);
                adjustedVertices.Add(originalVertices[i] - correction);
            }

            return BuildClosureReport(originalVertices, adjustedVertices, measuredSegmentLengths, segmentBulges);
        }

        private static ClosureReport BuildClosureReport(List<Point3d> originalVertices, List<Point3d> adjustedVertices, List<double>? measuredSegmentLengths = null, List<double>? segmentBulges = null)
        {
            if (originalVertices.Count < 2 || adjustedVertices.Count != originalVertices.Count)
                return new ClosureReport();

            Point3d first = originalVertices[0];
            Point3d last = originalVertices[originalVertices.Count - 1];
            Vector3d misclosureVector = last - first;
            double totalLength = 0.0;
            for (int i = 0; i < originalVertices.Count - 1; i++)
            {
                double bulge = segmentBulges != null && i < segmentBulges.Count ? segmentBulges[i] : 0.0;
                double length = measuredSegmentLengths != null && i < measuredSegmentLengths.Count
                    ? measuredSegmentLengths[i]
                    : GetSegmentLength(originalVertices[i], originalVertices[i + 1], bulge);
                totalLength += length;
            }

            ClosureReport report = new ClosureReport
            {
                SegmentCount = originalVertices.Count - 1,
                StartPoint = first,
                EndPoint = last,
                TotalLength = totalLength,
                MisclosureVector = misclosureVector,
                Misclosure = first.DistanceTo(last),
                ClosureBearingRadians = Math.Atan2(misclosureVector.Y, misclosureVector.X),
                RelativePrecisionRatio = first.DistanceTo(last) <= 1.0e-12 ? 0.0 : totalLength / first.DistanceTo(last),
                PartsPerMillionError = totalLength <= 1.0e-12 ? 0.0 : first.DistanceTo(last) / totalLength * 1000000.0,
                OriginalArea = ComputeSignedBoundaryArea(originalVertices, segmentBulges),
                AdjustedArea = ComputeSignedBoundaryArea(adjustedVertices, segmentBulges)
            };

            double totalAbsBearingDelta = 0.0;
            for (int i = 0; i < originalVertices.Count - 1; i++)
            {
                double bulge = segmentBulges != null && i < segmentBulges.Count ? segmentBulges[i] : 0.0;
                double originalLength = measuredSegmentLengths != null && i < measuredSegmentLengths.Count
                    ? measuredSegmentLengths[i]
                    : GetSegmentLength(originalVertices[i], originalVertices[i + 1], bulge);
                double adjustedLength = GetSegmentLength(adjustedVertices[i], adjustedVertices[i + 1], bulge);
                double absLengthDelta = Math.Abs(adjustedLength - originalLength);
                double absBearingDelta = Math.Abs(GetBearingDeltaSeconds(originalVertices[i], originalVertices[i + 1], adjustedVertices[i], adjustedVertices[i + 1]));

                if (absLengthDelta > report.MaxAbsLengthDelta)
                {
                    report.MaxAbsLengthDelta = absLengthDelta;
                    report.WorstLengthSegmentIndex = i + 1;
                }

                if (absBearingDelta > report.MaxAbsBearingDeltaSeconds)
                {
                    report.MaxAbsBearingDeltaSeconds = absBearingDelta;
                    report.WorstBearingSegmentIndex = i + 1;
                }

                totalAbsBearingDelta += absBearingDelta;
            }

            report.TotalAbsBearingDeltaSeconds = totalAbsBearingDelta;
            report.ExceedsDistanceTolerance = report.MaxAbsLengthDelta > DefaultDisplayedDistanceTolerance;
            report.ExceedsBearingWarning = report.MaxAbsBearingDeltaSeconds > DefaultBearingWarningSeconds;
            return report;
        }

        private static void ApplySegmentStats(List<SegmentRef> orderedSegments, ClosureReport report)
        {
            report.MaxAbsLengthDelta = 0.0;
            report.MaxAbsBearingDeltaSeconds = 0.0;
            report.TotalAbsBearingDeltaSeconds = 0.0;
            report.WorstLengthSegmentIndex = 0;
            report.WorstBearingSegmentIndex = 0;
            report.OriginalArea = ComputeSignedBoundaryArea(orderedSegments.Select(s => s.OriginalStart).Concat(new[] { orderedSegments.Last().OriginalEnd }).ToList(), orderedSegments.Select(s => s.OriginalBulge).ToList());
            report.AdjustedArea = ComputeSignedBoundaryArea(orderedSegments.Select(s => s.AdjustedStart).Concat(new[] { orderedSegments.Last().AdjustedEnd }).ToList(), orderedSegments.Select(s => s.AdjustedBulge).ToList());

            for (int i = 0; i < orderedSegments.Count; i++)
            {
                double absLengthDelta = Math.Abs(orderedSegments[i].LengthDelta);
                double absBearingDelta = Math.Abs(orderedSegments[i].BearingDeltaSeconds);

                if (absLengthDelta > report.MaxAbsLengthDelta)
                {
                    report.MaxAbsLengthDelta = absLengthDelta;
                    report.WorstLengthSegmentIndex = i + 1;
                }

                if (absBearingDelta > report.MaxAbsBearingDeltaSeconds)
                {
                    report.MaxAbsBearingDeltaSeconds = absBearingDelta;
                    report.WorstBearingSegmentIndex = i + 1;
                }

                report.TotalAbsBearingDeltaSeconds += absBearingDelta;
            }

            report.ExceedsDistanceTolerance = report.MaxAbsLengthDelta > DefaultDisplayedDistanceTolerance;
            report.ExceedsBearingWarning = report.MaxAbsBearingDeltaSeconds > DefaultBearingWarningSeconds;
        }

        private static AdjustmentOutputMode ConfirmAdjustment(Editor ed, ClosureReport report)
        {
            WriteReport(ed, report);

            if (report.Misclosure <= ComputationalClosureEpsilon)
            {
                ed.WriteMessage("\nSURVEY-AUTO-CLOSURE: Boundary is already mathematically closed within computational epsilon; no adjustment was required.");
                return AdjustmentOutputMode.Cancel;
            }

            if (report.ExceedsDistanceTolerance || report.ExceedsBearingWarning)
            {
                ed.WriteMessage("\nWARNING: Proposed adjustment exceeds the Phase 2 display-precision warning threshold.");
                ed.WriteMessage("\n         Review before accepting. This may indicate the paper-map data cannot close within shown precision.");
            }

            PromptKeywordOptions pko = new PromptKeywordOptions("\nRetain a copy of the original linework on the ORIG layer for reference? [Yes/No] <No>: ");
            pko.Keywords.Add("Yes");
            pko.Keywords.Add("No");
            pko.Keywords.Default = "No";
            pko.AllowNone = true;

            PromptResult pr = ed.GetKeywords(pko);
            if (pr.Status == PromptStatus.Cancel || pr.Status == PromptStatus.Error)
                return AdjustmentOutputMode.Cancel;

            return pr.Status == PromptStatus.OK && string.Equals(pr.StringResult, "Yes", StringComparison.OrdinalIgnoreCase)
                ? AdjustmentOutputMode.KeepOriginalReference
                : AdjustmentOutputMode.ReplaceOriginals;
        }

        private static void WriteReport(Editor ed, ClosureReport report)
        {
            ed.WriteMessage("\nSURVEY-AUTO-CLOSURE Phase 2 Report");
            ed.WriteMessage("\n  Segments: " + report.SegmentCount.ToString(CultureInfo.InvariantCulture));
            ed.WriteMessage("\n  Start point: " + FormatPoint(report.StartPoint));
            ed.WriteMessage("\n  End point: " + FormatPoint(report.EndPoint));
            ed.WriteMessage("\n  Traverse length: " + FormatDistance(report.TotalLength));
            ed.WriteMessage("\n  Existing misclosure: " + FormatDistance(report.Misclosure));
            ed.WriteMessage("\n  Closure vector bearing: " + FormatAzimuthBearing(report.ClosureBearingRadians));
            ed.WriteMessage("\n  Relative precision: " + FormatRelativePrecision(report.RelativePrecisionRatio));
            ed.WriteMessage("\n  PPM error: " + report.PartsPerMillionError.ToString("0.###", CultureInfo.InvariantCulture));
            ed.WriteMessage("\n  Original area: " + FormatArea(report.OriginalArea));
            ed.WriteMessage("\n  Adjusted area: " + FormatArea(report.AdjustedArea));
            ed.WriteMessage("\n  Area delta: " + FormatSignedArea(report.AreaDelta));
            ed.WriteMessage("\n  Max segment length change: " + FormatSignedDistance(report.MaxAbsLengthDelta) + " at segment " + report.WorstLengthSegmentIndex.ToString(CultureInfo.InvariantCulture));
            ed.WriteMessage("\n  Max bearing change: " + report.MaxAbsBearingDeltaSeconds.ToString("0.###", CultureInfo.InvariantCulture) + "\" at segment " + report.WorstBearingSegmentIndex.ToString(CultureInfo.InvariantCulture));
            ed.WriteMessage("\n  Total absolute bearing adjustment: " + report.TotalAbsBearingDeltaSeconds.ToString("0.###", CultureInfo.InvariantCulture) + "\"");
            ed.WriteMessage("\n  Original layer: " + LayerStandards.SurveyMapOriginalLayerName);
            ed.WriteMessage("\n  Adjusted layer: " + LayerStandards.SurveyMapAdjustedLayerName);
        }

        private static ObjectId AppendToSameOwnerSpace(Transaction tr, ObjectId sourceId, AcEntity entity)
        {
            AcEntity source = (AcEntity)tr.GetObject(sourceId, OpenMode.ForRead);
            BlockTableRecord owner = (BlockTableRecord)tr.GetObject(source.OwnerId, OpenMode.ForWrite);
            ObjectId id = owner.AppendEntity(entity);
            tr.AddNewlyCreatedDBObject(entity, true);
            return id;
        }

        private static void StoreReviewRun(Database db, ClosureReport report, IReadOnlyList<ClosureReviewSegment> segments)
        {
            SurveyClosureReviewData.SetLastRun(new ClosureReviewRun
            {
                DrawingName = string.IsNullOrWhiteSpace(db.Filename) ? "Unsaved drawing" : System.IO.Path.GetFileName(db.Filename),
                TraverseLength = report.TotalLength,
                Misclosure = report.Misclosure,
                RelativePrecisionRatio = report.RelativePrecisionRatio,
                PartsPerMillionError = report.PartsPerMillionError,
                OriginalArea = report.OriginalArea,
                AdjustedArea = report.AdjustedArea,
                Segments = segments.ToList()
            });
        }


        private static double ComputeSignedBoundaryArea(IReadOnlyList<Point3d> vertices, IReadOnlyList<double>? bulges)
        {
            if (vertices.Count < 3)
                return 0.0;

            double area = 0.0;
            int segmentCount = vertices.Count - 1;
            for (int i = 0; i < segmentCount; i++)
            {
                Point3d start = vertices[i];
                Point3d end = vertices[i + 1];
                double bulge = bulges != null && i < bulges.Count ? bulges[i] : 0.0;
                area += GetSegmentSignedArea(start, end, bulge);
            }

            // Treat the input as a boundary even when the source map linework has a small open
            // misclosure. The final chord closes the area computation for the report only.
            Point3d last = vertices[vertices.Count - 1];
            Point3d first = vertices[0];
            if (last.DistanceTo(first) > 1.0e-8)
                area += GetSegmentSignedArea(last, first, 0.0);

            return area;
        }

        private static double GetSegmentSignedArea(Point3d start, Point3d end, double bulge)
        {
            double chordArea = 0.5 * (start.X * end.Y - end.X * start.Y);
            if (Math.Abs(bulge) <= 1.0e-12)
                return chordArea;

            double chord = start.DistanceTo(end);
            if (chord <= 1.0e-12)
                return chordArea;

            double theta = 4.0 * Math.Atan(bulge);
            double sinHalf = Math.Sin(theta / 2.0);
            if (Math.Abs(sinHalf) <= 1.0e-12)
                return chordArea;

            double radius = Math.Abs(chord / (2.0 * sinHalf));
            double circularSegmentArea = 0.5 * radius * radius * (theta - Math.Sin(theta));
            return chordArea + circularSegmentArea;
        }

        private static double GetSegmentLength(Point3d start, Point3d end, double bulge)
        {
            double chord = start.DistanceTo(end);
            if (chord <= 1.0e-12 || Math.Abs(bulge) <= 1.0e-12)
                return chord;

            double includedAngle = 4.0 * Math.Atan(Math.Abs(bulge));
            double sinHalf = Math.Sin(includedAngle / 2.0);
            if (Math.Abs(sinHalf) <= 1.0e-12)
                return chord;

            double radius = chord / (2.0 * sinHalf);
            return Math.Abs(radius * includedAngle);
        }

        private static double GetBearingDeltaSeconds(Point3d a1, Point3d a2, Point3d b1, Point3d b2)
        {
            double angle1 = Math.Atan2(a2.Y - a1.Y, a2.X - a1.X);
            double angle2 = Math.Atan2(b2.Y - b1.Y, b2.X - b1.X);
            double delta = NormalizeAngle(angle2 - angle1);
            return Math.Abs(delta) * 180.0 / Math.PI * 3600.0;
        }

        private static double NormalizeAngle(double radians)
        {
            while (radians > Math.PI)
                radians -= 2.0 * Math.PI;
            while (radians < -Math.PI)
                radians += 2.0 * Math.PI;
            return radians;
        }

        private static string FormatPoint(Point3d point)
        {
            return point.X.ToString("0.0000", CultureInfo.InvariantCulture) + ", " +
                   point.Y.ToString("0.0000", CultureInfo.InvariantCulture) + ", " +
                   point.Z.ToString("0.0000", CultureInfo.InvariantCulture);
        }

        private static string FormatDistance(double value)
        {
            return value.ToString("0.0000", CultureInfo.InvariantCulture) + "'";
        }

        private static string FormatSignedDistance(double value)
        {
            return value.ToString("+0.0000;-0.0000;0.0000", CultureInfo.InvariantCulture) + "'";
        }

        private static string FormatArea(double value)
        {
            double abs = Math.Abs(value);
            return abs.ToString("0.00", CultureInfo.InvariantCulture) + " sq ft (" + (abs / 43560.0).ToString("0.0000", CultureInfo.InvariantCulture) + " ac)";
        }

        private static string FormatSignedArea(double value)
        {
            string sign = value > 0.0 ? "+" : value < 0.0 ? "-" : string.Empty;
            double abs = Math.Abs(value);
            return sign + abs.ToString("0.00", CultureInfo.InvariantCulture) + " sq ft (" + sign + (abs / 43560.0).ToString("0.0000", CultureInfo.InvariantCulture) + " ac)";
        }

        private static string FormatRelativePrecision(double ratio)
        {
            if (ratio <= 0.0 || double.IsNaN(ratio) || double.IsInfinity(ratio))
                return "Closed";

            return "1:" + ratio.ToString("0", CultureInfo.InvariantCulture);
        }

        private static string FormatAzimuthBearing(double radians)
        {
            double degrees = radians * 180.0 / Math.PI;
            while (degrees < 0.0)
                degrees += 360.0;
            while (degrees >= 360.0)
                degrees -= 360.0;

            int d = (int)Math.Floor(degrees);
            double minutesRaw = (degrees - d) * 60.0;
            int m = (int)Math.Floor(minutesRaw);
            double seconds = (minutesRaw - m) * 60.0;
            return d.ToString("000", CultureInfo.InvariantCulture) + "°" +
                   m.ToString("00", CultureInfo.InvariantCulture) + "'" +
                   seconds.ToString("00.##", CultureInfo.InvariantCulture) + "\"";
        }
    }
}
