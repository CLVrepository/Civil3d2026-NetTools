using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcPolyline = Autodesk.AutoCAD.DatabaseServices.Polyline;

namespace CLV_CivilTools.Gis
{
    internal sealed class StormTrimBatchResult
    {
        internal StormTrimBatchResult(int trimmedCount, int unchangedCount, IEnumerable<string> reviews,
            IEnumerable<ObjectId> wallIds)
        {
            TrimmedCount = trimmedCount;
            UnchangedCount = unchangedCount;
            ReviewItems = Array.AsReadOnly(reviews.ToArray());
            RetainedIds = new System.Collections.ObjectModel.ReadOnlyDictionary<ObjectId, IReadOnlyList<ObjectId>>(
                wallIds.Distinct().ToDictionary(id => id, id => (IReadOnlyList<ObjectId>)Array.AsReadOnly(new[] { id })));
        }

        internal int TrimmedCount { get; }
        internal int UnchangedCount { get; }
        internal IReadOnlyList<string> ReviewItems { get; }
        internal IReadOnlyDictionary<ObjectId, IReadOnlyList<ObjectId>> RetainedIds { get; }
        internal bool Success => ReviewItems.Count == 0;
    }

    public static class GisTrimInsideBoundaryCommands
    {
        /// <summary>
        /// Nonprompt storm-only batch. The caller must hold the document lock, supply
        /// current verified completion-owned straight DropInlet/JunctionBox outer
        /// IDs and proven pipe-wall IDs, preserving each structure's native OD role.
        /// The caller must abort its transaction on ANY thrown exception. This method
        /// never commits. Reviews are returned before this method mutates geometry;
        /// a caller with earlier pipeline writes must also abort on an unsuccessful
        /// result. Only success allows dependent point/marker cleanup to proceed.
        /// Supplied connection contexts must cover every wall. Legacy callers without
        /// contexts receive a 25-unit gap review with neither terminal allowed open.
        /// </summary>
        internal static StormTrimBatchResult TrimVerifiedStormBoxWalls(Database db, Transaction tr,
            IReadOnlyList<ObjectId> verifiedBoxOuterIds, IReadOnlyList<ObjectId> verifiedPipeWallIds,
            IReadOnlyDictionary<ObjectId, StormWallConnectionContext>? connectionContexts = null)
        {
            if (db == null || tr == null) throw new ArgumentNullException(db == null ? nameof(db) : nameof(tr));
            if (verifiedBoxOuterIds == null || verifiedPipeWallIds == null)
                throw new ArgumentNullException(verifiedBoxOuterIds == null ? nameof(verifiedBoxOuterIds) : nameof(verifiedPipeWallIds));
            ObjectId[] boundaryIds = verifiedBoxOuterIds.ToArray();
            ObjectId[] wallIds = verifiedPipeWallIds.ToArray();
            Dictionary<ObjectId, StormWallConnectionContext>? contexts = connectionContexts?.ToDictionary(item => item.Key, item => item.Value);
            var reviews = new List<string>();
            var boundaries = new List<StormBoxBoundary>();
            var walls = new List<StormWallEdit>();
            if (AcadApp.DocumentManager.MdiActiveDocument?.Database != db || tr.TransactionManager != db.TransactionManager)
                throw new InvalidOperationException("Storm trim requires the active drawing and its caller-owned transaction.");
            if (boundaryIds.Distinct().Count() != boundaryIds.Length || wallIds.Distinct().Count() != wallIds.Length)
                reviews.Add("Duplicate box or pipe-wall IDs are not a verified batch.");
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            ObjectId modelId = bt[BlockTableRecord.ModelSpace];

            foreach (ObjectId id in boundaryIds.Distinct())
            {
                try
                {
                    Entity entity = RequireStormEntity(db, tr, modelId, id, "C-STRM-STRC-E");
                    if (entity is not AcPolyline box || !box.Closed || !HasWorldXyNormal(box))
                        throw new InvalidOperationException("Box must be a closed world-XY lightweight polyline.");
                    StormStructureVertex[] points = ReadStraightPolyline(box);
                    if (!StormTerminalTrim.TryValidateBoundary(points, out string geometryDetail))
                        throw new InvalidOperationException(geometryDetail);
                    if (!GisImportCommands.TryReadStructuresIdentity(id, out string name, out string part, out string identityDetail))
                        throw new InvalidOperationException("Physical box structure identity is not verified: " + identityDetail);
                    StormStructureRole role = StormStructureMatching.Classify(name, part);
                    if (role != StormStructureRole.JunctionBox && role != StormStructureRole.DropInlet)
                        throw new InvalidOperationException("Only verified straight drop-inlet or junction-box outer outlines can be trimmed; the native OD role is retained.");
                    var state = GisImportCommands.InspectObjectDataFingerprint(id, out string fingerprint, out string odDetail);
                    if (state != GisImportCommands.ObjectDataFingerprintState.Present)
                        throw new InvalidOperationException("Box complete native OD is not readable: " + odDetail);
                    boundaries.Add(new StormBoxBoundary(id, points, box.Elevation, fingerprint));
                }
                catch (System.Exception ex) { reviews.Add($"Box {StormEntityHandle(id)}: {ex.Message}"); }
            }

            foreach (ObjectId id in wallIds.Distinct())
            {
                try
                {
                    StormWallConnectionContext context = new(25.0, false, false);
                    if (contexts != null && (!contexts.TryGetValue(id, out context!) || context == null))
                        throw new InvalidOperationException("Pipe wall is missing its proven terminal connection context.");
                    if (!double.IsFinite(context.SearchDistance) || context.SearchDistance <= StormTerminalTrim.Tolerance)
                        throw new InvalidOperationException("Pipe wall connection search distance must be positive and finite.");
                    Entity entity = RequireStormEntity(db, tr, modelId, id, "C-STRM-PIPE-E");
                    StormStructureVertex[] points;
                    double elevation;
                    if (entity is Line line)
                    {
                        if (!FinitePoint(line.StartPoint) || !FinitePoint(line.EndPoint) ||
                            Math.Abs(line.StartPoint.Z - line.EndPoint.Z) > StormTerminalTrim.Tolerance)
                            throw new InvalidOperationException("Pipe line must have finite horizontal geometry.");
                        points = new[] { new StormStructureVertex(line.StartPoint.X, line.StartPoint.Y),
                            new StormStructureVertex(line.EndPoint.X, line.EndPoint.Y) };
                        elevation = line.StartPoint.Z;
                    }
                    else if (entity is AcPolyline polyline && !polyline.Closed && HasWorldXyNormal(polyline))
                    {
                        points = ReadStraightPolyline(polyline);
                        elevation = polyline.Elevation;
                    }
                    else throw new InvalidOperationException("Only lines and open straight world-XY lightweight pipe polylines are supported.");
                    if (!StormTerminalTrim.TryValidatePath(points, out string geometryDetail))
                        throw new InvalidOperationException(geometryDetail);

                    double first = 0, last = points.Length - 1;
                    bool clippedStart = false, clippedEnd = false;
                    foreach (StormBoxBoundary box in boundaries)
                    {
                        // Distant plan extents cannot affect this wall, regardless of elevation.
                        if (!OverlapsPlanExtents(points, box.Points)) continue;
                        if (Math.Abs(elevation - box.Elevation) > StormTerminalTrim.Tolerance)
                            throw new InvalidOperationException($"Pipe wall and box {StormEntityHandle(box.Id)} are at incompatible elevations.");
                        StormTerminalTrimDecision decision = StormTerminalTrim.Plan(box.Points, points);
                        if (decision.Kind == StormTerminalTrimKind.Review)
                            throw new InvalidOperationException($"Box {StormEntityHandle(box.Id)}: {decision.Reason}");
                        if (decision.Kind == StormTerminalTrimKind.Unchanged) continue;
                        if (decision.StartParameter > 0)
                        {
                            if (clippedStart) throw new InvalidOperationException("Multiple boxes claim the same pipe start.");
                            clippedStart = true;
                            first = decision.StartParameter;
                        }
                        if (decision.EndParameter < points.Length - 1)
                        {
                            if (clippedEnd) throw new InvalidOperationException("Multiple boxes claim the same pipe end.");
                            clippedEnd = true;
                            last = decision.EndParameter;
                        }
                    }
                    if (first >= last) throw new InvalidOperationException("Box clips overlap or leave no exterior pipe wall.");
                    var state = GisImportCommands.InspectObjectDataFingerprint(id, out string fingerprint, out string odDetail);
                    if (state == GisImportCommands.ObjectDataFingerprintState.ReadFailed)
                        throw new InvalidOperationException(odDetail);
                    var edit = new StormWallEdit(entity, points, elevation, context, first, last, clippedStart || clippedEnd, state, fingerprint);
                    if (edit.Changed) StageStormWallEdit(edit);
                    walls.Add(edit);
                }
                catch (System.Exception ex) { reviews.Add($"Pipe wall {StormEntityHandle(id)}: {ex.Message}"); }
            }

            // Evaluate gaps only after each wall's actual terminal clip decisions are
            // known. A proposed extension never counts as completed connection geometry.
            foreach (StormWallEdit edit in walls)
            {
                if (edit.First == 0 && !edit.Connection.AllowStartGap)
                    ReviewStormTerminalGap(edit, edit.Path[0], edit.Path[1], "start", boundaries, reviews);
                if (edit.Last == edit.Path.Length - 1 && !edit.Connection.AllowEndGap)
                    ReviewStormTerminalGap(edit, edit.Path[edit.Path.Length - 1], edit.Path[edit.Path.Length - 2], "end", boundaries, reviews);
            }

            if (reviews.Count > 0) return new StormTrimBatchResult(0, 0, reviews, wallIds.Where(id => id.IsValid && !id.IsErased));

            // All plans and replacement vertex data exist before the first write.
            // Exceptions from here deliberately propagate: never fall back after mutation.
            foreach (StormBoxBoundary box in boundaries)
                RequireUnchangedStormOd(box.Id, GisImportCommands.ObjectDataFingerprintState.Present, box.Fingerprint);
            foreach (StormWallEdit edit in walls)
                RequireUnchangedStormOd(edit.Entity.ObjectId, edit.OdState, edit.Fingerprint);
            foreach (StormWallEdit edit in walls.Where(edit => edit.Changed))
            {
                if (!edit.Entity.IsWriteEnabled) edit.Entity.UpgradeOpen();
                if (edit.Entity is Line line)
                {
                    line.StartPoint = edit.LineStart;
                    line.EndPoint = edit.LineEnd;
                }
                else if (edit.Entity is AcPolyline polyline)
                {
                    // Retain the same entity, OD, XData, extension dictionary and display properties.
                    while (polyline.NumberOfVertices > 0) polyline.RemoveVertexAt(polyline.NumberOfVertices - 1);
                    foreach (StormWallVertex vertex in edit.Vertices)
                        polyline.AddVertexAt(polyline.NumberOfVertices, vertex.Point, 0, vertex.StartWidth, vertex.EndWidth);
                }
                VerifyStormWallGeometry(edit);
                RequireUnchangedStormOd(edit.Entity.ObjectId, edit.OdState, edit.Fingerprint);
                edit.Entity.RecordGraphicsModified(true);
            }
            // Check the complete batch after mutation, including untouched entities.
            foreach (StormWallEdit edit in walls)
                RequireUnchangedStormOd(edit.Entity.ObjectId, edit.OdState, edit.Fingerprint);
            foreach (StormBoxBoundary box in boundaries)
                RequireUnchangedStormOd(box.Id, GisImportCommands.ObjectDataFingerprintState.Present, box.Fingerprint);
            tr.TransactionManager.QueueForGraphicsFlush();
            return new StormTrimBatchResult(walls.Count(edit => edit.Changed), walls.Count(edit => !edit.Changed),
                Array.Empty<string>(), wallIds);
        }

        private static void ReviewStormTerminalGap(StormWallEdit edit, StormStructureVertex endpoint,
            StormStructureVertex neighbor, string terminal, IReadOnlyList<StormBoxBoundary> boundaries, List<string> reviews)
        {
            if (boundaries.Any(box => StormTerminalTrim.ContainsEndpoint(box.Points, endpoint))) return;
            foreach (StormBoxBoundary box in boundaries)
            {
                if (Math.Abs(edit.Elevation - box.Elevation) > StormTerminalTrim.Tolerance) continue;
                StormTerminalGapProbe probe = StormTerminalTrim.ProbeTerminalGap(box.Points, endpoint, neighbor, edit.Connection.SearchDistance);
                if (probe.Kind != StormTerminalGapKind.Clear)
                    reviews.Add($"Pipe wall {edit.Entity.Handle} {terminal}, box {StormEntityHandle(box.Id)}: {probe.Reason}");
            }
        }

        private static void VerifyStormWallGeometry(StormWallEdit edit)
        {
            if (edit.Entity is Line line)
            {
                if (line.StartPoint != edit.LineStart || line.EndPoint != edit.LineEnd)
                    throw new InvalidOperationException("Pipe line geometry readback differs from the staged trim; abort the transaction.");
                return;
            }
            var polyline = (AcPolyline)edit.Entity;
            if (polyline.Closed || polyline.NumberOfVertices != edit.Vertices.Count || !HasWorldXyNormal(polyline))
                throw new InvalidOperationException("Pipe polyline geometry readback differs from the staged trim; abort the transaction.");
            for (int i = 0; i < edit.Vertices.Count; i++)
            {
                StormWallVertex vertex = edit.Vertices[i];
                if (polyline.GetPoint2dAt(i) != vertex.Point || polyline.GetBulgeAt(i) != 0 ||
                    polyline.GetStartWidthAt(i) != vertex.StartWidth || polyline.GetEndWidthAt(i) != vertex.EndWidth)
                    throw new InvalidOperationException("Pipe polyline vertex readback differs from the staged trim; abort the transaction.");
            }
        }

        private static Entity RequireStormEntity(Database db, Transaction tr, ObjectId modelId, ObjectId id, string layer)
        {
            if (id.IsNull || !id.IsValid || id.IsErased || id.Database != db ||
                tr.GetObject(id, OpenMode.ForRead, false) is not Entity entity || entity.IsErased ||
                entity.OwnerId != modelId || !string.Equals(entity.Layer, layer, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Entity is not live model-space geometry on {layer}.");
            return entity;
        }

        private static string StormEntityHandle(ObjectId id)
        {
            try { return id.IsNull ? "<null>" : id.IsValid ? id.Handle.ToString() : "<invalid>"; }
            catch { return "<unavailable>"; }
        }

        private static bool HasWorldXyNormal(AcPolyline polyline)
            => polyline.Normal.X == 0 && polyline.Normal.Y == 0 && polyline.Normal.Z == 1 && double.IsFinite(polyline.Elevation);

        private static StormStructureVertex[] ReadStraightPolyline(AcPolyline polyline)
        {
            var result = new StormStructureVertex[polyline.NumberOfVertices];
            for (int i = 0; i < result.Length; i++)
            {
                if (polyline.GetBulgeAt(i) != 0 || !FinitePoint(polyline.GetPoint3dAt(i)))
                    throw new InvalidOperationException("Polyline must have finite vertices and exactly straight segments.");
                Point2d point = polyline.GetPoint2dAt(i);
                result[i] = new StormStructureVertex(point.X, point.Y);
            }
            return result;
        }

        private static bool FinitePoint(Point3d point)
            => double.IsFinite(point.X) && double.IsFinite(point.Y) && double.IsFinite(point.Z);

        private static bool OverlapsPlanExtents(StormStructureVertex[] first, StormStructureVertex[] second)
            => first.Min(point => point.X) <= second.Max(point => point.X) + StormTerminalTrim.Tolerance &&
                first.Max(point => point.X) >= second.Min(point => point.X) - StormTerminalTrim.Tolerance &&
                first.Min(point => point.Y) <= second.Max(point => point.Y) + StormTerminalTrim.Tolerance &&
                first.Max(point => point.Y) >= second.Min(point => point.Y) - StormTerminalTrim.Tolerance;

        private static void RequireUnchangedStormOd(ObjectId id, GisImportCommands.ObjectDataFingerprintState expected,
            string fingerprint)
        {
            var actual = GisImportCommands.InspectObjectDataFingerprint(id, out string current, out string detail);
            if (actual == GisImportCommands.ObjectDataFingerprintState.ReadFailed || actual != expected ||
                (actual == GisImportCommands.ObjectDataFingerprintState.Present && !string.Equals(current, fingerprint, StringComparison.Ordinal)))
                throw new InvalidOperationException($"Complete native OD changed or is unreadable on {StormEntityHandle(id)}: {detail}; abort the storm trim transaction.");
        }

        private static void StageStormWallEdit(StormWallEdit edit)
        {
            if (edit.Entity is Line line)
            {
                Vector3d delta = line.EndPoint - line.StartPoint;
                edit.LineStart = edit.First == 0 ? line.StartPoint : line.StartPoint + delta * edit.First;
                edit.LineEnd = edit.Last == 1 ? line.EndPoint : line.StartPoint + delta * edit.Last;
                if (edit.LineStart.DistanceTo(edit.LineEnd) <= StormTerminalTrim.Tolerance)
                    throw new InvalidOperationException("Trim would leave a degenerate line.");
                return;
            }
            var polyline = (AcPolyline)edit.Entity;
            // Stage using the original segment data. No split/recreate native entity is needed.
            var parameters = new List<double> { edit.First };
            for (int i = 1; i < polyline.NumberOfVertices - 1; i++)
                if (i > edit.First && i < edit.Last) parameters.Add(i);
            parameters.Add(edit.Last);
            for (int i = 0; i < parameters.Count; i++)
            {
                double parameter = parameters[i];
                int segment = Math.Min((int)Math.Floor(parameter), polyline.NumberOfVertices - 2);
                double fraction = parameter - segment;
                Point2d a = polyline.GetPoint2dAt(segment), b = polyline.GetPoint2dAt(segment + 1);
                // Preserve untouched vertices exactly, including large survey coordinates.
                Point2d point = fraction == 0 ? a : fraction == 1 ? b : a + (b - a) * fraction;
                double startWidth = 0, endWidth = 0;
                if (i < parameters.Count - 1)
                {
                    // Each staged segment stays within one original segment.
                    double endFraction = parameters[i + 1] - segment;
                    double widthA = polyline.GetStartWidthAt(segment), widthB = polyline.GetEndWidthAt(segment);
                    startWidth = widthA + (widthB - widthA) * fraction;
                    endWidth = widthA + (widthB - widthA) * endFraction;
                }
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || !double.IsFinite(startWidth) ||
                    !double.IsFinite(endWidth) || startWidth < 0 || endWidth < 0)
                    throw new InvalidOperationException("Trimmed polyline coordinates or widths are invalid.");
                edit.Vertices.Add(new StormWallVertex(point, startWidth, endWidth));
            }
            if (!StormTerminalTrim.TryValidatePath(edit.Vertices.Select(vertex => new StormStructureVertex(vertex.Point.X, vertex.Point.Y)).ToArray(), out string reason))
                throw new InvalidOperationException("Trimmed polyline is invalid: " + reason);
        }

        private sealed record StormBoxBoundary(ObjectId Id, StormStructureVertex[] Points, double Elevation, string Fingerprint);
        private sealed record StormWallVertex(Point2d Point, double StartWidth, double EndWidth);
        private sealed class StormWallEdit
        {
            internal StormWallEdit(Entity entity, StormStructureVertex[] path, double elevation,
                StormWallConnectionContext connection, double first, double last, bool changed,
                GisImportCommands.ObjectDataFingerprintState odState, string fingerprint)
            { Entity = entity; Path = path; Elevation = elevation; Connection = connection;
                First = first; Last = last; Changed = changed; OdState = odState; Fingerprint = fingerprint; }
            internal Entity Entity { get; }
            internal StormStructureVertex[] Path { get; }
            internal double Elevation { get; }
            internal StormWallConnectionContext Connection { get; }
            internal double First { get; }
            internal double Last { get; }
            internal bool Changed { get; }
            internal GisImportCommands.ObjectDataFingerprintState OdState { get; }
            internal string Fingerprint { get; }
            internal Point3d LineStart { get; set; }
            internal Point3d LineEnd { get; set; }
            internal List<StormWallVertex> Vertices { get; } = new();
        }

        [CommandMethod("CLV-GIS-TRIM-INSIDE", CommandFlags.Modal)]
        public static void TrimInsideBoundary()
        {
            var doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            var db = doc.Database;
            var ed = doc.Editor;

            try
            {
                using var docLock = doc.LockDocument();

                var peo = new PromptEntityOptions("\nSelect closed structure boundary (circle or closed polyline): ");
                peo.SetRejectMessage("\nSelect a circle or closed polyline.");
                peo.AddAllowedClass(typeof(Circle), exactMatch: false);
                peo.AddAllowedClass(typeof(AcPolyline), exactMatch: false);
                var per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK)
                    return;

                using var tr = db.TransactionManager.StartTransaction();
                if (tr.GetObject(per.ObjectId, OpenMode.ForRead) is not Curve selectedBoundary)
                {
                    ed.WriteMessage("\nSelected object is not a valid boundary curve.");
                    return;
                }

                if (!TryCreateBoundaryInfo(selectedBoundary, out BoundaryInfo? selectedInfo) || selectedInfo == null)
                {
                    ed.WriteMessage("\nBoundary must be a circle or closed straight-segment polyline.");
                    return;
                }

                var boundaryIds = new List<ObjectId> { per.ObjectId };

                int trimmedCount = 0;
                int skippedCount = 0;

                foreach (ObjectId boundaryId in boundaryIds)
                {
                    if (!boundaryId.IsValid || boundaryId.IsErased)
                        continue;

                    if (tr.GetObject(boundaryId, OpenMode.ForRead, false) is not Curve boundaryCurve)
                        continue;

                    if (!TryCreateBoundaryInfo(boundaryCurve, out BoundaryInfo? boundaryInfo) || boundaryInfo == null)
                    {
                        skippedCount++;
                        continue;
                    }

                    var candidateIds = CollectCandidateCurveIds(tr, db, boundaryId, boundaryInfo);
                    foreach (ObjectId candidateId in candidateIds)
                    {
                        if (!candidateId.IsValid || candidateId.IsErased)
                            continue;

                        if (tr.GetObject(candidateId, OpenMode.ForWrite, false) is not Curve candidate)
                            continue;

                        TrimResult result = TrimCurveAgainstBoundary(tr, db, candidate, boundaryCurve, boundaryInfo);
                        if (result == TrimResult.Trimmed)
                            trimmedCount++;
                        else if (result == TrimResult.Skipped)
                            skippedCount++;
                    }
                }

                tr.Commit();
                ed.WriteMessage($"\nCLV-GIS-TRIM-INSIDE complete. Trimmed: {trimmedCount}. Skipped: {skippedCount}. Boundaries processed: {boundaryIds.Count}. Mode: Single boundary. Candidate layers: C-STRM-PIPE-E, C-SSWR-PIPE-E.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nCLV-GIS-TRIM-INSIDE failed: {ex.Message}");
            }
        }

        private static List<ObjectId> CollectSimilarBoundaryIds(Transaction tr, Database db, ObjectId seedId, Curve seedBoundary, BoundaryInfo seedInfo)
        {
            _ = seedInfo;
            var allMatches = new List<(ObjectId Id, Curve Curve)>();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var model = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

            string layer = seedBoundary.Layer;

            foreach (ObjectId id in model)
            {
                if (id.IsErased || !id.IsValid)
                    continue;

                if (tr.GetObject(id, OpenMode.ForRead, false) is not Curve curve)
                    continue;

                if (!string.Equals(curve.Layer, layer, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (IsSupportedBoundaryCurve(curve))
                    allMatches.Add((id, curve));
            }

            var result = FilterNestedCompanionBoundaries(seedBoundary, allMatches);

            if (!result.Contains(seedId))
                result.Add(seedId);

            return result;
        }

        private static bool IsPipeWallLayer(string layerName)
        {
            return string.Equals(layerName, "C-STRM-PIPE-E", StringComparison.OrdinalIgnoreCase)
                || string.Equals(layerName, "C-SSWR-PIPE-E", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSupportedBoundaryCurve(Curve curve)
        {
            return curve is Circle
                || (curve is AcPolyline polyline && polyline.Closed);
        }

        private static List<ObjectId> FilterNestedCompanionBoundaries(Curve seedBoundary, List<(ObjectId Id, Curve Curve)> allMatches)
        {
            if (seedBoundary is not Circle seedCircle)
                return allMatches.Select(x => x.Id).ToList();

            const double centerTolerance = 0.10;
            const double radiusTolerance = 0.01;
            var circleMatches = allMatches
                .Where(x => x.Curve is Circle)
                .ToList();
            var nonCircleMatches = allMatches
                .Where(x => x.Curve is not Circle)
                .Select(x => x.Id)
                .ToList();

            bool seedHasLargerCompanion = circleMatches.Any(x =>
                x.Curve is Circle circle
                && !ReferenceEquals(circle, seedCircle)
                && circle.Center.DistanceTo(seedCircle.Center) <= centerTolerance
                && circle.Radius > seedCircle.Radius + radiusTolerance);
            bool seedHasSmallerCompanion = circleMatches.Any(x =>
                x.Curve is Circle circle
                && !ReferenceEquals(circle, seedCircle)
                && circle.Center.DistanceTo(seedCircle.Center) <= centerTolerance
                && circle.Radius < seedCircle.Radius - radiusTolerance);

            if (!seedHasLargerCompanion && !seedHasSmallerCompanion)
                return allMatches.Select(x => x.Id).ToList();

            bool keepInnerCompanions = seedHasLargerCompanion || !seedHasSmallerCompanion;
            var filtered = new List<ObjectId>();

            foreach (var match in circleMatches)
            {
                if (match.Curve is not Circle circle)
                    continue;

                bool hasLargerCompanion = circleMatches.Any(x =>
                    x.Curve is Circle other
                    && !ReferenceEquals(other, circle)
                    && other.Center.DistanceTo(circle.Center) <= centerTolerance
                    && other.Radius > circle.Radius + radiusTolerance);
                bool hasSmallerCompanion = circleMatches.Any(x =>
                    x.Curve is Circle other
                    && !ReferenceEquals(other, circle)
                    && other.Center.DistanceTo(circle.Center) <= centerTolerance
                    && other.Radius < circle.Radius - radiusTolerance);

                if (keepInnerCompanions)
                {
                    if (!hasSmallerCompanion)
                        filtered.Add(match.Id);
                }
                else if (!hasLargerCompanion)
                {
                    filtered.Add(match.Id);
                }
            }

            filtered.AddRange(nonCircleMatches);
            return filtered;
        }

        private static List<ObjectId> CollectCandidateCurveIds(Transaction tr, Database db, ObjectId boundaryId, BoundaryInfo boundaryInfo)
        {
            var result = new List<ObjectId>();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var model = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            Extents3d ext = boundaryInfo.Extents;

            foreach (ObjectId id in model)
            {
                if (id == boundaryId || id.IsErased || !id.IsValid)
                    continue;

                if (tr.GetObject(id, OpenMode.ForRead, false) is not Curve curve)
                    continue;

                if (!IsPipeWallLayer(curve.Layer))
                    continue;

                if (curve is Circle)
                    continue;

                if (curve is AcPolyline candidatePoly && candidatePoly.Closed)
                    continue;

                try
                {
                    Extents3d candidateExt = curve.GeometricExtents;
                    if (!Intersects(ext, candidateExt))
                        continue;
                }
                catch
                {
                    continue;
                }

                result.Add(id);
            }

            return result;
        }

        private static TrimResult TrimCurveAgainstBoundary(Transaction tr, Database db, Curve candidate, Curve boundaryCurve, BoundaryInfo boundaryInfo)
        {
            var intersections = new Point3dCollection();
            try
            {
                candidate.IntersectWith(boundaryCurve, Intersect.OnBothOperands, intersections, IntPtr.Zero, IntPtr.Zero);
            }
            catch
            {
                return TrimResult.Skipped;
            }

            if (intersections.Count == 0)
                return TrimResult.None;

            Point3d start = candidate.StartPoint;
            Point3d end = candidate.EndPoint;
            bool startInside = boundaryInfo.Contains(start);
            bool endInside = boundaryInfo.Contains(end);

            if (!startInside && !endInside)
            {
                if (intersections.Count == 1)
                {
                    double startDist = start.DistanceTo(boundaryInfo.Center);
                    double endDist = end.DistanceTo(boundaryInfo.Center);
                    startInside = startDist < endDist;
                    endInside = !startInside;
                }
                else
                {
                    return TrimResult.Skipped;
                }
            }

            List<(double Param, Point3d Point)> paramHits = new();
            foreach (Point3d pt in intersections.Cast<Point3d>())
            {
                try
                {
                    double param = candidate.GetParameterAtPoint(candidate.GetClosestPointTo(pt, false));
                    paramHits.Add((param, pt));
                }
                catch
                {
                    // skip bad hit
                }
            }

            if (paramHits.Count == 0)
                return TrimResult.Skipped;

            paramHits = paramHits
                .OrderBy(x => x.Param)
                .GroupBy(x => Math.Round(x.Param, 8))
                .Select(g => g.First())
                .ToList();

            if (startInside)
            {
                double splitParam = paramHits.First().Param;
                return KeepOneSideAfterSplit(tr, db, candidate, splitParam, keepStartSide: false);
            }

            if (endInside)
            {
                double splitParam = paramHits.Last().Param;
                return KeepOneSideAfterSplit(tr, db, candidate, splitParam, keepStartSide: true);
            }

            return TrimResult.Skipped;
        }

        private static TrimResult KeepOneSideAfterSplit(Transaction tr, Database db, Curve candidate, double splitParam, bool keepStartSide)
        {
            double startParam = candidate.StartParam;
            double endParam = candidate.EndParam;
            const double tol = 1e-8;
            if (Math.Abs(splitParam - startParam) < tol || Math.Abs(splitParam - endParam) < tol)
                return TrimResult.None;

            Point3d originalStart = candidate.StartPoint;
            Point3d originalEnd = candidate.EndPoint;

            if (TryTrimInPlace(candidate, splitParam, keepStartSide))
                return TrimResult.Trimmed;

            DBObjectCollection pieces;
            try
            {
                var splitParams = new DoubleCollection { splitParam };
                pieces = candidate.GetSplitCurves(splitParams);
            }
            catch
            {
                return TrimResult.Skipped;
            }

            if (pieces.Count < 2)
            {
                DisposePieces(pieces);
                return TrimResult.Skipped;
            }

            Curve? kept = null;
            double best = double.MaxValue;

            foreach (DBObject dbo in pieces)
            {
                if (dbo is not Curve piece)
                    continue;

                double metric = keepStartSide
                    ? Math.Min(piece.StartPoint.DistanceTo(originalStart), piece.EndPoint.DistanceTo(originalStart))
                    : Math.Min(piece.StartPoint.DistanceTo(originalEnd), piece.EndPoint.DistanceTo(originalEnd));

                if (metric < best)
                {
                    kept = piece;
                    best = metric;
                }
            }

            if (kept == null)
            {
                DisposePieces(pieces);
                return TrimResult.Skipped;
            }

            var owner = (BlockTableRecord)tr.GetObject(candidate.OwnerId, OpenMode.ForWrite);
            foreach (DBObject dbo in pieces)
            {
                if (ReferenceEquals(dbo, kept))
                    continue;

                dbo.Dispose();
            }

            owner.AppendEntity(kept);
            tr.AddNewlyCreatedDBObject(kept, true);
            candidate.Erase();
            return TrimResult.Trimmed;
        }


        private static bool TryTrimInPlace(Curve candidate, double splitParam, bool keepStartSide)
        {
            try
            {
                Point3d splitPoint = candidate.GetPointAtParameter(splitParam);

                if (candidate is Line line)
                {
                    if (keepStartSide)
                        line.EndPoint = splitPoint;
                    else
                        line.StartPoint = splitPoint;

                    return true;
                }

                if (candidate is AcPolyline polyline)
                    return TryTrimPolylineInPlace(polyline, splitParam, keepStartSide);
            }
            catch
            {
                // Fall back to split/recreate for uncommon curve types.
            }

            return false;
        }

        private static bool TryTrimPolylineInPlace(AcPolyline original, double splitParam, bool keepStartSide)
        {
            DBObjectCollection pieces;
            try
            {
                var splitParams = new DoubleCollection { splitParam };
                pieces = original.GetSplitCurves(splitParams);
            }
            catch
            {
                return false;
            }

            try
            {
                if (pieces.Count < 2)
                    return false;

                Point3d target = keepStartSide ? original.StartPoint : original.EndPoint;
                AcPolyline? kept = null;
                double best = double.MaxValue;

                foreach (DBObject dbo in pieces)
                {
                    if (dbo is not AcPolyline piece)
                        continue;

                    double metric = Math.Min(piece.StartPoint.DistanceTo(target), piece.EndPoint.DistanceTo(target));
                    if (metric < best)
                    {
                        kept = piece;
                        best = metric;
                    }
                }

                if (kept == null || kept.NumberOfVertices < 2)
                    return false;

                original.Closed = false;
                while (original.NumberOfVertices > 0)
                    original.RemoveVertexAt(original.NumberOfVertices - 1);

                original.Elevation = kept.Elevation;
                original.Normal = kept.Normal;
                for (int i = 0; i < kept.NumberOfVertices; i++)
                {
                    original.AddVertexAt(i, kept.GetPoint2dAt(i), kept.GetBulgeAt(i), kept.GetStartWidthAt(i), kept.GetEndWidthAt(i));
                }

                return true;
            }
            finally
            {
                DisposePieces(pieces);
            }
        }

        private static void DisposePieces(DBObjectCollection pieces)
        {
            foreach (DBObject dbo in pieces)
                dbo.Dispose();
        }

        private static bool TryCreateBoundaryInfo(Curve boundaryCurve, out BoundaryInfo? info)
        {
            info = null;
            try
            {
                if (boundaryCurve is Circle circle)
                {
                    info = BoundaryInfo.FromCircle(circle);
                    return true;
                }

                if (boundaryCurve is AcPolyline polyline && polyline.Closed)
                {
                    info = BoundaryInfo.FromPolyline(polyline);
                    return info != null;
                }
            }
            catch
            {
                info = null;
            }

            return false;
        }

        private static bool Intersects(Extents3d a, Extents3d b)
        {
            return a.MinPoint.X <= b.MaxPoint.X
                && a.MaxPoint.X >= b.MinPoint.X
                && a.MinPoint.Y <= b.MaxPoint.Y
                && a.MaxPoint.Y >= b.MinPoint.Y;
        }

        private sealed class BoundaryInfo
        {
            private readonly Point2d[]? _polygonVertices;
            private readonly Circle? _circle;

            private BoundaryInfo(Point3d center, Extents3d extents, Circle? circle, Point2d[]? polygonVertices)
            {
                Center = center;
                Extents = extents;
                _circle = circle;
                _polygonVertices = polygonVertices;
            }

            public Point3d Center { get; }
            public Extents3d Extents { get; }

            public static BoundaryInfo FromCircle(Circle circle)
            {
                return new BoundaryInfo(circle.Center, circle.GeometricExtents, circle, null);
            }

            public static BoundaryInfo? FromPolyline(AcPolyline polyline)
            {
                if (!polyline.Closed || polyline.NumberOfVertices < 3)
                    return null;

                var points = new List<Point2d>();
                for (int i = 0; i < polyline.NumberOfVertices; i++)
                {
                    if (Math.Abs(polyline.GetBulgeAt(i)) > 1e-9)
                        return null;

                    points.Add(polyline.GetPoint2dAt(i));
                }

                double sumX = points.Sum(p => p.X);
                double sumY = points.Sum(p => p.Y);
                var center = new Point3d(sumX / points.Count, sumY / points.Count, 0.0);
                return new BoundaryInfo(center, polyline.GeometricExtents, null, points.ToArray());
            }

            public bool Contains(Point3d point)
            {
                if (_circle != null)
                    return _circle.Center.DistanceTo(point) < (_circle.Radius - 1e-6);

                if (_polygonVertices == null || _polygonVertices.Length < 3)
                    return false;

                var p = new Point2d(point.X, point.Y);
                bool inside = false;
                for (int i = 0, j = _polygonVertices.Length - 1; i < _polygonVertices.Length; j = i++)
                {
                    Point2d pi = _polygonVertices[i];
                    Point2d pj = _polygonVertices[j];
                    bool intersect = ((pi.Y > p.Y) != (pj.Y > p.Y))
                                     && (p.X < (pj.X - pi.X) * (p.Y - pi.Y) / ((pj.Y - pi.Y) == 0.0 ? 1e-12 : (pj.Y - pi.Y)) + pi.X);
                    if (intersect)
                        inside = !inside;
                }

                return inside;
            }
        }

        private enum TrimResult
        {
            None,
            Trimmed,
            Skipped
        }
    }
}

