using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using CLV_CivilTools.Shared;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcException = Autodesk.AutoCAD.Runtime.Exception;
using AcColor = Autodesk.AutoCAD.Colors.Color;

namespace CLV_CivilTools.Survey
{
    public static class SurveyLineworkReviewCommands
    {
        private const double DefaultTolerance = 0.1;
        private const double ExactDuplicateTolerance = 0.001;
        private const short ExactHighlightColorIndex = 3;
        private const short NearHighlightColorIndex = 1;
        private const short ShortDuplicateHighlightColorIndex = 30;
        private const double MinimumZoomSize = 2.0;

        private static List<LineworkReviewIssue> _lastIssues = new();

        [CommandMethod("SURVEY-LINEWORK-REVIEW", CommandFlags.Modal)]
        [CommandMethod("LINEWORKREVIEW", CommandFlags.Modal)]
        public static void ReviewLinework()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;

            try
            {
                // Always remove any previous review overlay before starting a new review or exiting early.
                ClearReviewHighlightEntities(db, ed);
                ClearSelectionHighlight(ed);

                LineworkReviewOptions? options = ShowOptionsDialog();
                if (options == null)
                    return;

                PromptSelectionOptions pso = new PromptSelectionOptions
                {
                    MessageForAdding = "\nSelect linework to review for duplicate/overlapping geometry: ",
                    RejectObjectsOnLockedLayers = false
                };

                SelectionFilter filter = new SelectionFilter(new[]
                {
                    new TypedValue((int)DxfCode.Start, "LINE,ARC,CIRCLE,LWPOLYLINE,POLYLINE")
                });

                PromptSelectionResult psr = ed.GetSelection(pso, filter);
                if (psr.Status != PromptStatus.OK)
                {
                    ClearReviewHighlightEntities(db, ed);
                    ClearSelectionHighlight(ed);
                    return;
                }

                bool includeExact = !options.Mode.Equals("Partial", StringComparison.OrdinalIgnoreCase);
                bool includePartial = !options.Mode.Equals("Exact", StringComparison.OrdinalIgnoreCase);

                List<LineworkReviewIssue> issues;
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    List<GeometryPart> parts = CollectGeometryParts(tr, psr.Value.GetObjectIds(), ExactDuplicateTolerance);
                    issues = FindIssues(parts, options.Tolerance, includeExact, includePartial);
                    tr.Commit();
                }

                _lastIssues = issues;

                if (issues.Count > 0)
                {
                    CreateHighlightOverlays(db, ed, issues);
                    ShowReport(issues, options.Tolerance, options.Mode);
                    ed.WriteMessage($"\nLINEWORK REVIEW: Found {issues.Count} duplicate/near-duplicate issue(s). Exact duplicates are highlighted green; same-line length differences highlight the longer object green and the shorter object orange; offset/possible-error duplicates are highlighted red.");
                }
                else
                {
                    ClearReviewHighlightEntities(db, ed);
                    ClearSelectionHighlight(ed);
                    ed.WriteMessage("\nLINEWORK REVIEW: No duplicate/near-duplicate issues found with the current settings.");
                }
            }
            catch (AcException ex)
            {
                ed.WriteMessage("\nLINEWORK REVIEW AutoCAD error: " + ex.Message);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nLINEWORK REVIEW error: " + ex.Message);
            }
        }

        [CommandMethod("SURVEY-LINEWORK-CLEAR-REVIEW", CommandFlags.Modal)]
        [CommandMethod("LINEWORKCLEAR", CommandFlags.Modal)]
        public static void ClearLineworkReview()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            try
            {
                ClearReviewHighlightEntities(doc.Database, ed);
                ClearSelectionHighlight(ed);
                _lastIssues.Clear();
                ed.WriteMessage("\nLINEWORK REVIEW: Cleared linework review highlights.");
            }
            catch (AcException ex)
            {
                ed.WriteMessage("\nLINEWORK CLEAR AutoCAD error: " + ex.Message);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage("\nLINEWORK CLEAR error: " + ex.Message);
            }
        }

        internal static void ZoomToIssue(int issueId)
        {
            LineworkReviewIssue? issue = _lastIssues.FirstOrDefault(i => i.Id == issueId);
            if (issue == null)
                return;

            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            using (doc.LockDocument())
            {
                CreateHighlightOverlays(doc.Database, ed, new[] { issue });
                ZoomToExtents(ed, issue.Extents, 1.8);
            }
        }

        internal static void HighlightIssueOnly(int issueId)
        {
            LineworkReviewIssue? issue = _lastIssues.FirstOrDefault(i => i.Id == issueId);
            if (issue == null)
                return;

            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            using (doc.LockDocument())
                CreateHighlightOverlays(doc.Database, doc.Editor, new[] { issue });
        }

        internal static void HighlightAllIssues()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            using (doc.LockDocument())
                CreateHighlightOverlays(doc.Database, doc.Editor, _lastIssues);
        }

        internal static void ClearCurrentHighlight()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            using (doc.LockDocument())
            {
                ClearReviewHighlightEntities(doc.Database, doc.Editor);
                ClearSelectionHighlight(doc.Editor);
            }
        }

        internal static void RemoveDuplicatesFromAllLinework()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            ObjectId[] ids;
            using (doc.LockDocument())
            {
                // Remove review overlay entities before OVERKILL so temporary highlight linework is not included.
                ClearReviewHighlightEntities(doc.Database, doc.Editor);
                ClearSelectionHighlight(doc.Editor);

                ids = GetAllCurrentSpaceLineworkObjectIds(doc.Database);
                doc.Editor.SetImpliedSelection(ids);
            }

            if (ids.Length == 0)
            {
                doc.Editor.WriteMessage("\nLINEWORK REVIEW: No non-xref linework found for REMOVE DUPLICATES.");
                return;
            }

            doc.Editor.WriteMessage($"\nLINEWORK REVIEW: Starting OVERKILL for {ids.Length} non-xref linework object(s).");
            doc.SendStringToExecute("_.OVERKILL ", true, false, false);
        }

        private static List<GeometryPart> CollectGeometryParts(Transaction tr, IEnumerable<ObjectId> objectIds, double tolerance)
        {
            List<GeometryPart> parts = new();
            int nextPartNumber = 1;

            foreach (ObjectId objectId in objectIds)
            {
                if (objectId.IsNull || !objectId.IsValid)
                    continue;

                if (tr.GetObject(objectId, OpenMode.ForRead, false) is not Entity entity)
                    continue;

                if (IsExternalReferenceEntity(entity))
                    continue;

                if (entity is Line line)
                {
                    TryAddLinePart(parts, ref nextPartNumber, entity, line.StartPoint, line.EndPoint, tolerance);
                }
                else if (entity is Arc arc)
                {
                    TryAddArcPart(parts, ref nextPartNumber, entity, arc.Center, arc.Radius, arc.StartAngle, arc.EndAngle, tolerance);
                }
                else if (entity is Circle circle)
                {
                    parts.Add(GeometryPart.CreateCircle(nextPartNumber++, entity, circle.Center, circle.Radius, tolerance));
                }
                else if (entity is Polyline polyline)
                {
                    AddPolylineParts(parts, ref nextPartNumber, entity, polyline, tolerance);
                }
                else if (entity is Polyline2d polyline2d)
                {
                    AddPolyline2dParts(parts, ref nextPartNumber, tr, entity, polyline2d, tolerance);
                }
            }

            return parts;
        }

        private static void AddPolylineParts(List<GeometryPart> parts, ref int nextPartNumber, Entity entity, Polyline polyline, double tolerance)
        {
            int count = polyline.NumberOfVertices;
            if (count < 2)
                return;

            int segmentCount = polyline.Closed ? count : count - 1;
            for (int i = 0; i < segmentCount; i++)
            {
                SegmentType type = polyline.GetSegmentType(i);
                if (type == SegmentType.Line)
                {
                    LineSegment2d seg = polyline.GetLineSegment2dAt(i);
                    TryAddLinePart(parts, ref nextPartNumber, entity,
                        new Point3d(seg.StartPoint.X, seg.StartPoint.Y, polyline.Elevation),
                        new Point3d(seg.EndPoint.X, seg.EndPoint.Y, polyline.Elevation), tolerance);
                }
                else if (type == SegmentType.Arc)
                {
                    CircularArc2d seg = polyline.GetArcSegment2dAt(i);
                    TryAddArcPart(parts, ref nextPartNumber, entity,
                        new Point3d(seg.Center.X, seg.Center.Y, polyline.Elevation),
                        seg.Radius,
                        Math.Atan2(seg.StartPoint.Y - seg.Center.Y, seg.StartPoint.X - seg.Center.X),
                        Math.Atan2(seg.EndPoint.Y - seg.Center.Y, seg.EndPoint.X - seg.Center.X), tolerance);
                }
            }
        }

        private static void AddPolyline2dParts(List<GeometryPart> parts, ref int nextPartNumber, Transaction tr, Entity entity, Polyline2d polyline, double tolerance)
        {
            List<Point3d> vertices = new();
            foreach (ObjectId vertexId in polyline)
            {
                if (tr.GetObject(vertexId, OpenMode.ForRead, false) is Vertex2d vertex)
                    vertices.Add(vertex.Position);
            }

            if (vertices.Count < 2)
                return;

            int segmentCount = polyline.Closed ? vertices.Count : vertices.Count - 1;
            for (int i = 0; i < segmentCount; i++)
            {
                Point3d start = vertices[i];
                Point3d end = vertices[(i + 1) % vertices.Count];
                TryAddLinePart(parts, ref nextPartNumber, entity, start, end, tolerance);
            }
        }

        private static void TryAddLinePart(List<GeometryPart> parts, ref int nextPartNumber, Entity entity, Point3d start, Point3d end, double tolerance)
        {
            if (Distance2d(start, end) <= tolerance)
                return;

            parts.Add(GeometryPart.CreateLine(nextPartNumber++, entity, start, end, tolerance));
        }

        private static void TryAddArcPart(List<GeometryPart> parts, ref int nextPartNumber, Entity entity, Point3d center, double radius, double startAngle, double endAngle, double tolerance)
        {
            if (radius <= tolerance)
                return;

            parts.Add(GeometryPart.CreateArc(nextPartNumber++, entity, center, radius, startAngle, endAngle, tolerance));
        }

        private static List<LineworkReviewIssue> FindIssues(List<GeometryPart> parts, double tolerance, bool includeExact, bool includePartial)
        {
            List<LineworkReviewIssue> issues = new();
            int nextIssueId = 1;
            HashSet<string> exactPairKeys = new(StringComparer.OrdinalIgnoreCase);

            if (includeExact)
            {
                foreach (IGrouping<string, GeometryPart> group in parts.GroupBy(p => p.ExactKey, StringComparer.OrdinalIgnoreCase))
                {
                    List<GeometryPart> grouped = group.ToList();
                    if (grouped.Count < 2)
                        continue;

                    foreach (string pairKey in GetPairKeys(grouped))
                        exactPairKeys.Add(pairKey);

                    issues.Add(BuildIssue(nextIssueId++, "Exact duplicate", grouped, "Same geometry within exact tolerance.", ExactHighlightColorIndex));
                }
            }

            if (includePartial)
            {
                List<GeometryPart> lineParts = parts.Where(p => p.Kind == GeometryKind.Line).ToList();
                for (int i = 0; i < lineParts.Count; i++)
                {
                    for (int j = i + 1; j < lineParts.Count; j++)
                    {
                        GeometryPart a = lineParts[i];
                        GeometryPart b = lineParts[j];
                        if (exactPairKeys.Contains(MakePairKey(a, b)))
                            continue;

                        if (TryGetLineOverlap(a, b, tolerance, out Extents3d overlapExtents, out double overlapLength))
                        {
                            if (overlapLength <= tolerance)
                                continue;

                            if (IsSameLineWithinTolerance(a, b, ExactDuplicateTolerance))
                            {
                                List<LineworkHighlightTarget> targets = BuildSameLineHighlightTargets(a, b);
                                issues.Add(BuildIssue(
                                    nextIssueId++,
                                    "Same-line length difference",
                                    new[] { a, b },
                                    $"Linework falls directly on the same path, but segment lengths/endpoints differ. Longer object is green; shorter object is orange. Overlapping length: {overlapLength:0.###}",
                                    ExactHighlightColorIndex,
                                    overlapExtents,
                                    targets));
                            }
                            else
                            {
                                issues.Add(BuildIssue(
                                    nextIssueId++,
                                    "Offset near duplicate",
                                    new[] { a, b },
                                    $"Offset/possible linework error within tolerance. Overlapping length: {overlapLength:0.###}",
                                    NearHighlightColorIndex,
                                    overlapExtents));
                            }
                        }
                    }
                }

                List<GeometryPart> arcParts = parts.Where(p => p.Kind == GeometryKind.Arc).ToList();
                for (int i = 0; i < arcParts.Count; i++)
                {
                    for (int j = i + 1; j < arcParts.Count; j++)
                    {
                        GeometryPart a = arcParts[i];
                        GeometryPart b = arcParts[j];
                        if (exactPairKeys.Contains(MakePairKey(a, b)))
                            continue;

                        if (TryGetArcOverlap(a, b, tolerance, out double overlapRadians))
                        {
                            double length = overlapRadians * a.Radius;
                            if (length > tolerance)
                                issues.Add(BuildIssue(nextIssueId++, "Near duplicate arc", new[] { a, b }, $"Center/radius/endpoints within tolerance. Overlapping arc length: {length:0.###}", NearHighlightColorIndex));
                        }
                    }
                }
            }

            if (includePartial)
            {
                List<GeometryPart> circleParts = parts.Where(p => p.Kind == GeometryKind.Circle).ToList();
                for (int i = 0; i < circleParts.Count; i++)
                {
                    for (int j = i + 1; j < circleParts.Count; j++)
                    {
                        GeometryPart a = circleParts[i];
                        GeometryPart b = circleParts[j];
                        if (exactPairKeys.Contains(MakePairKey(a, b)))
                            continue;

                        if (Distance2d(a.Center, b.Center) <= tolerance && Math.Abs(a.Radius - b.Radius) <= tolerance)
                        {
                            issues.Add(BuildIssue(nextIssueId++, "Near duplicate circle", new[] { a, b }, "Center/radius difference within tolerance.", NearHighlightColorIndex));
                        }
                    }
                }
            }

            return MergeDuplicateIssues(issues);
        }

        private static LineworkReviewIssue BuildIssue(
            int id,
            string issueType,
            IEnumerable<GeometryPart> parts,
            string notes,
            short highlightColorIndex,
            Extents3d? overrideExtents = null,
            IReadOnlyList<LineworkHighlightTarget>? highlightTargets = null)
        {
            List<GeometryPart> list = parts.ToList();
            Extents3d extents = overrideExtents ?? UnionExtents(list.Select(p => p.Extents));
            ObjectId[] objectIds = list.Select(p => p.ObjectId).Distinct().ToArray();
            string layers = string.Join(", ", list.Select(p => p.Layer).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
            string types = string.Join(", ", list.Select(p => p.SourceType).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
            string handles = string.Join(", ", list.Select(p => p.Handle).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase));
            string objectDetails = string.Join("; ", list
                .GroupBy(p => p.ObjectId)
                .Select(g => g.First())
                .OrderBy(p => p.Handle, StringComparer.OrdinalIgnoreCase)
                .Select(p => $"{p.Handle} | {p.Layer} | {p.SourceType}"));

            IReadOnlyList<LineworkHighlightTarget> targets = highlightTargets ?? objectIds.Select(oid => new LineworkHighlightTarget(oid, highlightColorIndex)).ToList();
            return new LineworkReviewIssue(id, issueType, list.Count, objectIds, layers, types, handles, objectDetails, notes, extents, targets);
        }

        private static List<LineworkReviewIssue> MergeDuplicateIssues(List<LineworkReviewIssue> issues)
        {
            List<LineworkReviewIssue> merged = new();
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            int nextId = 1;

            foreach (LineworkReviewIssue issue in issues)
            {
                string key = issue.IssueType + "|" + string.Join(",", issue.ObjectIds.Select(id => id.Handle.ToString()).OrderBy(s => s, StringComparer.OrdinalIgnoreCase)) + "|" + issue.Extents.MinPoint.ToString() + issue.Extents.MaxPoint.ToString();
                if (!seen.Add(key))
                    continue;

                merged.Add(issue with { Id = nextId++ });
            }

            return merged;
        }

        private static IEnumerable<string> GetPairKeys(List<GeometryPart> parts)
        {
            for (int i = 0; i < parts.Count; i++)
                for (int j = i + 1; j < parts.Count; j++)
                    yield return MakePairKey(parts[i], parts[j]);
        }

        private static string MakePairKey(GeometryPart a, GeometryPart b)
        {
            string ha = a.ObjectId.Handle.ToString();
            string hb = b.ObjectId.Handle.ToString();
            int compare = string.Compare(ha, hb, StringComparison.OrdinalIgnoreCase);
            return compare <= 0 ? ha + "|" + hb : hb + "|" + ha;
        }

        private static List<LineworkHighlightTarget> BuildSameLineHighlightTargets(GeometryPart a, GeometryPart b)
        {
            double aLength = LineLength(a);
            double bLength = LineLength(b);
            List<LineworkHighlightTarget> targets = new();

            if (Math.Abs(aLength - bLength) <= ExactDuplicateTolerance)
            {
                targets.Add(new LineworkHighlightTarget(a.ObjectId, ExactHighlightColorIndex));
                targets.Add(new LineworkHighlightTarget(b.ObjectId, ExactHighlightColorIndex));
                return targets;
            }

            GeometryPart longer = aLength >= bLength ? a : b;
            GeometryPart shorter = aLength >= bLength ? b : a;
            targets.Add(new LineworkHighlightTarget(longer.ObjectId, ExactHighlightColorIndex));
            targets.Add(new LineworkHighlightTarget(shorter.ObjectId, ShortDuplicateHighlightColorIndex));
            return targets;
        }

        private static bool IsSameLineWithinTolerance(GeometryPart a, GeometryPart b, double tolerance)
        {
            Vector2d dir = new Vector2d(a.End.X - a.Start.X, a.End.Y - a.Start.Y);
            double len = dir.Length;
            if (len <= tolerance)
                return false;

            Vector2d unit = dir / len;
            Vector2d normal = new Vector2d(-unit.Y, unit.X);
            Point2d origin = new Point2d(a.Start.X, a.Start.Y);
            Point2d b0 = new Point2d(b.Start.X, b.Start.Y);
            Point2d b1 = new Point2d(b.End.X, b.End.Y);
            double b0Off = Math.Abs((b0 - origin).DotProduct(normal));
            double b1Off = Math.Abs((b1 - origin).DotProduct(normal));
            if (b0Off > tolerance || b1Off > tolerance)
                return false;

            Vector2d bdir = new Vector2d(b.End.X - b.Start.X, b.End.Y - b.Start.Y);
            double bLen = bdir.Length;
            if (bLen <= tolerance)
                return false;

            double cross = Math.Abs(unit.X * (bdir.Y / bLen) - unit.Y * (bdir.X / bLen));
            return cross <= 0.000001;
        }

        private static double LineLength(GeometryPart part)
        {
            return Distance2d(part.Start, part.End);
        }

        private static bool TryGetLineOverlap(GeometryPart a, GeometryPart b, double tolerance, out Extents3d overlapExtents, out double overlapLength)
        {
            overlapExtents = new Extents3d();
            overlapLength = 0.0;

            Vector2d dir = new Vector2d(a.End.X - a.Start.X, a.End.Y - a.Start.Y);
            double len = dir.Length;
            if (len <= tolerance)
                return false;

            Vector2d unit = dir / len;
            Vector2d normal = new Vector2d(-unit.Y, unit.X);
            Point2d origin = new Point2d(a.Start.X, a.Start.Y);

            Point2d b0 = new Point2d(b.Start.X, b.Start.Y);
            Point2d b1 = new Point2d(b.End.X, b.End.Y);
            double b0Off = Math.Abs((b0 - origin).DotProduct(normal));
            double b1Off = Math.Abs((b1 - origin).DotProduct(normal));
            if (b0Off > tolerance || b1Off > tolerance)
                return false;

            Vector2d bdir = new Vector2d(b.End.X - b.Start.X, b.End.Y - b.Start.Y);
            if (bdir.Length <= tolerance)
                return false;

            double cross = Math.Abs(unit.X * (bdir.Y / bdir.Length) - unit.Y * (bdir.X / bdir.Length));
            if (cross > Math.Max(0.000001, tolerance / Math.Max(len, bdir.Length)))
                return false;

            double a0 = 0.0;
            double a1 = len;
            double b0t = (b0 - origin).DotProduct(unit);
            double b1t = (b1 - origin).DotProduct(unit);
            double bMin = Math.Min(b0t, b1t);
            double bMax = Math.Max(b0t, b1t);
            double overlapMin = Math.Max(a0, bMin);
            double overlapMax = Math.Min(a1, bMax);
            overlapLength = overlapMax - overlapMin;

            if (overlapLength <= tolerance)
                return false;

            Point3d p0 = new Point3d(origin.X + unit.X * overlapMin, origin.Y + unit.Y * overlapMin, 0.0);
            Point3d p1 = new Point3d(origin.X + unit.X * overlapMax, origin.Y + unit.Y * overlapMax, 0.0);
            overlapExtents = ExtentsFromPoints(new[] { p0, p1 });
            return true;
        }

        private static bool TryGetArcOverlap(GeometryPart a, GeometryPart b, double tolerance, out double overlapRadians)
        {
            overlapRadians = 0.0;
            if (Distance2d(a.Center, b.Center) > tolerance)
                return false;

            if (Math.Abs(a.Radius - b.Radius) > tolerance)
                return false;

            List<(double Start, double End)> aIntervals = ToNormalizedIntervals(a.StartAngle, a.EndAngle);
            List<(double Start, double End)> bIntervals = ToNormalizedIntervals(b.StartAngle, b.EndAngle);

            foreach (var ai in aIntervals)
            {
                foreach (var bi in bIntervals)
                {
                    double start = Math.Max(ai.Start, bi.Start);
                    double end = Math.Min(ai.End, bi.End);
                    if (end - start > overlapRadians)
                        overlapRadians = end - start;
                }
            }

            return overlapRadians * a.Radius > tolerance;
        }

        private static List<(double Start, double End)> ToNormalizedIntervals(double start, double end)
        {
            double twoPi = Math.PI * 2.0;
            start = NormalizeAngle(start);
            end = NormalizeAngle(end);

            if (end < start)
                return new List<(double Start, double End)> { (start, twoPi), (0.0, end) };

            if (Math.Abs(end - start) < 1e-10)
                return new List<(double Start, double End)> { (0.0, twoPi) };

            return new List<(double Start, double End)> { (start, end) };
        }

        private static double NormalizeAngle(double angle)
        {
            double twoPi = Math.PI * 2.0;
            angle %= twoPi;
            if (angle < 0.0)
                angle += twoPi;
            return angle;
        }

        private static LineworkReviewOptions? ShowOptionsDialog()
        {
            using LineworkReviewOptionsForm form = new LineworkReviewOptionsForm();
            DialogResult result = AcadApp.ShowModalDialog(form);
            return result == DialogResult.OK ? form.Options : null;
        }

        private static void CreateHighlightOverlays(Database db, Editor ed, IReadOnlyList<LineworkReviewIssue> issues)
        {
            using Transaction tr = db.TransactionManager.StartTransaction();
            ClearReviewHighlightEntities(db, tr);
            LayerStandards.EnsureSurveyLineworkReviewLayer(db, tr, ed);

            BlockTableRecord currentSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            HashSet<string> cloned = new(StringComparer.OrdinalIgnoreCase);

            foreach (LineworkReviewIssue issue in issues)
            {
                foreach (LineworkHighlightTarget target in issue.HighlightTargets.Where(t => !t.ObjectId.IsNull && t.ObjectId.IsValid))
                {
                    if (!cloned.Add(issue.Id.ToString(CultureInfo.InvariantCulture) + "|" + target.ObjectId.Handle.ToString()))
                        continue;

                    if (tr.GetObject(target.ObjectId, OpenMode.ForRead, false) is not Entity source)
                        continue;

                    if (IsExternalReferenceEntity(source))
                        continue;

                    if (source.Clone() is not Entity overlay)
                        continue;

                    overlay.Layer = LayerStandards.SurveyLineworkReviewLayerName;
                    overlay.Color = AcColor.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, target.ColorIndex);
                    overlay.Linetype = "Continuous";
                    overlay.LineWeight = LineWeight.LineWeight050;

                    currentSpace.AppendEntity(overlay);
                    tr.AddNewlyCreatedDBObject(overlay, true);
                }
            }

            tr.Commit();
            ed.Regen();
        }

        private static void ClearReviewHighlightEntities(Database db, Editor ed)
        {
            using Transaction tr = db.TransactionManager.StartTransaction();
            ClearReviewHighlightEntities(db, tr);
            tr.Commit();
            ed.Regen();
        }

        private static void ClearReviewHighlightEntities(Database db, Transaction tr)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(LayerStandards.SurveyLineworkReviewLayerName))
                return;

            BlockTableRecord currentSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            foreach (ObjectId id in currentSpace)
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is not Entity entity)
                    continue;

                if (!entity.Layer.Equals(LayerStandards.SurveyLineworkReviewLayerName, StringComparison.OrdinalIgnoreCase))
                    continue;

                entity.UpgradeOpen();
                entity.Erase();
            }
        }

        private static ObjectId[] GetAllCurrentSpaceLineworkObjectIds(Database db)
        {
            List<ObjectId> ids = new();
            using Transaction tr = db.TransactionManager.StartTransaction();
            BlockTableRecord currentSpace = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            foreach (ObjectId id in currentSpace)
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is Entity entity && IsSupportedLinework(entity) && !IsExternalReferenceEntity(entity))
                    ids.Add(id);
            }

            tr.Commit();
            return ids.ToArray();
        }

        private static bool IsSupportedLinework(Entity entity)
        {
            return entity is Line || entity is Arc || entity is Circle || entity is Polyline || entity is Polyline2d;
        }

        private static bool IsExternalReferenceEntity(Entity entity)
        {
            return entity is BlockReference || entity.Layer.Contains("|", StringComparison.OrdinalIgnoreCase);
        }

        private static void ClearSelectionHighlight(Editor ed)
        {
            ed.SetImpliedSelection(Array.Empty<ObjectId>());
        }

        private static void ShowReport(IReadOnlyList<LineworkReviewIssue> issues, double tolerance, string mode)
        {
            LineworkReviewReportForm form = new LineworkReviewReportForm(issues, tolerance, mode);
            AcadApp.ShowModelessDialog(form);
        }

        private static void ZoomToExtents(Editor ed, Extents3d extents, double scale)
        {
            double width = Math.Max(extents.MaxPoint.X - extents.MinPoint.X, MinimumZoomSize) * scale;
            double height = Math.Max(extents.MaxPoint.Y - extents.MinPoint.Y, MinimumZoomSize) * scale;
            Point2d center = new Point2d((extents.MinPoint.X + extents.MaxPoint.X) / 2.0, (extents.MinPoint.Y + extents.MaxPoint.Y) / 2.0);

            using ViewTableRecord view = ed.GetCurrentView();
            view.CenterPoint = center;
            view.Width = Math.Max(width, height * 1.6);
            view.Height = Math.Max(height, width / 1.6);
            ed.SetCurrentView(view);
        }

        private static Extents3d UnionExtents(IEnumerable<Extents3d> extents)
        {
            bool first = true;
            Extents3d result = new Extents3d();
            foreach (Extents3d ext in extents)
            {
                if (first)
                {
                    result = ext;
                    first = false;
                }
                else
                {
                    result.AddExtents(ext);
                }
            }

            return result;
        }

        private static Extents3d ExtentsFromPoints(IEnumerable<Point3d> points)
        {
            bool first = true;
            Extents3d extents = new Extents3d();
            foreach (Point3d point in points)
            {
                if (first)
                {
                    extents = new Extents3d(point, point);
                    first = false;
                }
                else
                {
                    extents.AddPoint(point);
                }
            }

            return extents;
        }

        private static Extents3d ExpandExtents(Extents3d extents, double margin)
        {
            return new Extents3d(
                new Point3d(extents.MinPoint.X - margin, extents.MinPoint.Y - margin, extents.MinPoint.Z),
                new Point3d(extents.MaxPoint.X + margin, extents.MaxPoint.Y + margin, extents.MaxPoint.Z));
        }

        private static double Distance2d(Point3d a, Point3d b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private enum GeometryKind
        {
            Line,
            Arc,
            Circle
        }

        private sealed record GeometryPart(
            int PartNumber,
            GeometryKind Kind,
            ObjectId ObjectId,
            string Handle,
            string Layer,
            string SourceType,
            Point3d Start,
            Point3d End,
            Point3d Center,
            double Radius,
            double StartAngle,
            double EndAngle,
            Extents3d Extents,
            string ExactKey)
        {
            public static GeometryPart CreateLine(int partNumber, Entity entity, Point3d start, Point3d end, double tolerance)
            {
                string keyA = PointKey(start, tolerance);
                string keyB = PointKey(end, tolerance);
                string canonical = string.Compare(keyA, keyB, StringComparison.OrdinalIgnoreCase) <= 0 ? keyA + "|" + keyB : keyB + "|" + keyA;
                return new GeometryPart(partNumber, GeometryKind.Line, entity.ObjectId, entity.Handle.ToString(), entity.Layer, entity.GetType().Name, start, end, Point3d.Origin, 0.0, 0.0, 0.0, ExtentsFromPoints(new[] { start, end }), "LINE|" + canonical);
            }

            public static GeometryPart CreateArc(int partNumber, Entity entity, Point3d center, double radius, double startAngle, double endAngle, double tolerance)
            {
                double ns = NormalizeAngle(startAngle);
                double ne = NormalizeAngle(endAngle);
                string a = AngleKey(ns, tolerance, radius);
                string b = AngleKey(ne, tolerance, radius);
                string canonicalAngles = string.Compare(a, b, StringComparison.OrdinalIgnoreCase) <= 0 ? a + "|" + b : b + "|" + a;
                Extents3d extents = TryGetEntityExtents(entity, center, radius);
                return new GeometryPart(partNumber, GeometryKind.Arc, entity.ObjectId, entity.Handle.ToString(), entity.Layer, entity.GetType().Name, PointOnCircle(center, radius, ns), PointOnCircle(center, radius, ne), center, radius, ns, ne, extents, $"ARC|{PointKey(center, tolerance)}|{RoundKey(radius, tolerance)}|{canonicalAngles}");
            }

            public static GeometryPart CreateCircle(int partNumber, Entity entity, Point3d center, double radius, double tolerance)
            {
                Extents3d extents = new Extents3d(new Point3d(center.X - radius, center.Y - radius, center.Z), new Point3d(center.X + radius, center.Y + radius, center.Z));
                return new GeometryPart(partNumber, GeometryKind.Circle, entity.ObjectId, entity.Handle.ToString(), entity.Layer, entity.GetType().Name, Point3d.Origin, Point3d.Origin, center, radius, 0.0, Math.PI * 2.0, extents, $"CIRCLE|{PointKey(center, tolerance)}|{RoundKey(radius, tolerance)}");
            }

            private static Extents3d TryGetEntityExtents(Entity entity, Point3d center, double radius)
            {
                try
                {
                    return entity.GeometricExtents;
                }
                catch
                {
                    return new Extents3d(new Point3d(center.X - radius, center.Y - radius, center.Z), new Point3d(center.X + radius, center.Y + radius, center.Z));
                }
            }

            private static Point3d PointOnCircle(Point3d center, double radius, double angle)
            {
                return new Point3d(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius, center.Z);
            }

            private static string PointKey(Point3d point, double tolerance)
            {
                return $"{RoundKey(point.X, tolerance)},{RoundKey(point.Y, tolerance)},{RoundKey(point.Z, tolerance)}";
            }

            private static string AngleKey(double angle, double tolerance, double radius)
            {
                double angleTolerance = radius > tolerance ? tolerance / radius : tolerance;
                return RoundKey(NormalizeAngle(angle), angleTolerance);
            }

            private static string RoundKey(double value, double tolerance)
            {
                double tol = tolerance <= 0.0 ? DefaultTolerance : tolerance;
                return Math.Round(value / tol).ToString(CultureInfo.InvariantCulture);
            }
        }

        internal sealed record LineworkReviewIssue(
            int Id,
            string IssueType,
            int PartCount,
            ObjectId[] ObjectIds,
            string Layers,
            string ObjectTypes,
            string Handles,
            string ObjectDetails,
            string Notes,
            Extents3d Extents,
            IReadOnlyList<LineworkHighlightTarget> HighlightTargets);

        internal sealed record LineworkHighlightTarget(ObjectId ObjectId, short ColorIndex);

        private sealed record LineworkReviewOptions(double Tolerance, string Mode);

        private sealed class LineworkReviewOptionsForm : Form
        {
            private readonly NumericUpDown _tolerance;
            private readonly ComboBox _mode;

            public LineworkReviewOptions Options { get; private set; } = new LineworkReviewOptions(DefaultTolerance, "All");

            public LineworkReviewOptionsForm()
            {
                Text = "CLV Linework Review Options";
                Width = 440;
                Height = 230;
                StartPosition = FormStartPosition.CenterScreen;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = false;
                MinimizeBox = false;

                TableLayoutPanel root = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 2,
                    RowCount = 4,
                    Padding = new Padding(12)
                };
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
                root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));

                root.Controls.Add(new Label { Text = "Tolerance", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 0);
                _tolerance = new NumericUpDown
                {
                    Dock = DockStyle.Fill,
                    DecimalPlaces = 6,
                    Minimum = 0.000001M,
                    Maximum = 10M,
                    Increment = 0.001M,
                    Value = (decimal)DefaultTolerance
                };
                root.Controls.Add(_tolerance, 1, 0);

                root.Controls.Add(new Label { Text = "Review mode", Dock = DockStyle.Fill, TextAlign = System.Drawing.ContentAlignment.MiddleLeft }, 0, 1);
                _mode = new ComboBox
                {
                    Dock = DockStyle.Fill,
                    DropDownStyle = ComboBoxStyle.DropDownList
                };
                _mode.Items.AddRange(new object[] { "All", "Exact", "Partial" });
                _mode.SelectedItem = "All";
                root.Controls.Add(_mode, 1, 1);

                Label note = new Label
                {
                    Dock = DockStyle.Fill,
                    AutoSize = false,
                    Text = "Exact duplicates are highlighted green. Same-line length differences use green for the longer object and orange for the shorter object. Offset/possible-error duplicates within tolerance are highlighted red. Xrefs/block references are excluded.",
                    Padding = new Padding(0, 10, 0, 0)
                };
                root.Controls.Add(note, 0, 2);
                root.SetColumnSpan(note, 2);

                FlowLayoutPanel buttons = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft
                };

                Button cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 90 };
                Button ok = new Button { Text = "Select Linework", DialogResult = DialogResult.OK, Width = 120 };
                ok.Click += (_, _) => Options = new LineworkReviewOptions((double)_tolerance.Value, _mode.SelectedItem?.ToString() ?? "All");
                buttons.Controls.Add(cancel);
                buttons.Controls.Add(ok);
                root.Controls.Add(buttons, 0, 3);
                root.SetColumnSpan(buttons, 2);

                AcceptButton = ok;
                CancelButton = cancel;
                Controls.Add(root);
            }
        }

        private sealed class LineworkReviewReportForm : Form
        {
            private readonly DataGridView _grid;

            public LineworkReviewReportForm(IReadOnlyList<LineworkReviewIssue> issues, double tolerance, string mode)
            {
                Text = "CLV Linework Review";
                Width = 1240;
                Height = 620;
                StartPosition = FormStartPosition.CenterScreen;
                MinimizeBox = true;
                MaximizeBox = true;

                TableLayoutPanel root = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    ColumnCount = 1,
                    RowCount = 3
                };
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
                root.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
                root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

                Label summary = new Label
                {
                    Dock = DockStyle.Fill,
                    AutoSize = false,
                    Padding = new Padding(10),
                    Text = $"Found {issues.Count} duplicate/overlap issue(s). Tolerance: {tolerance:0.########}.\r\nExact duplicates are green. Same-line length differences use green for the longer object and orange for the shorter object. Offset/possible-error duplicates are red. REMOVE DUPLICATES starts OVERKILL for all non-xref linework in the current space."
                };
                root.Controls.Add(summary, 0, 0);

                FlowLayoutPanel buttons = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight,
                    Padding = new Padding(8, 4, 8, 4)
                };

                Button highlightButton = new Button { Text = "Highlight Selected", Width = 130, Height = 28 };
                highlightButton.Click += (_, _) => HighlightSelected();
                buttons.Controls.Add(highlightButton);

                Button zoomButton = new Button { Text = "Zoom Selected", Width = 120, Height = 28 };
                zoomButton.Click += (_, _) => ZoomSelected();
                buttons.Controls.Add(zoomButton);

                Button overkillButton = new Button { Text = "REMOVE DUPLICATES", Width = 145, Height = 28 };
                overkillButton.Click += (_, _) => SurveyLineworkReviewCommands.RemoveDuplicatesFromAllLinework();
                buttons.Controls.Add(overkillButton);

                Button clearButton = new Button { Text = "Clear Highlight", Width = 120, Height = 28 };
                clearButton.Click += (_, _) => SurveyLineworkReviewCommands.ClearCurrentHighlight();
                buttons.Controls.Add(clearButton);
                root.Controls.Add(buttons, 0, 1);

                _grid = new DataGridView
                {
                    Dock = DockStyle.Fill,
                    AllowUserToAddRows = false,
                    AllowUserToDeleteRows = false,
                    ReadOnly = true,
                    SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                    MultiSelect = false,
                    AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells
                };
                AddColumns(_grid);
                foreach (LineworkReviewIssue issue in issues)
                    AddRow(_grid, issue);

                _grid.CellDoubleClick += (_, _) => ZoomSelected();
                root.Controls.Add(_grid, 0, 2);

                Controls.Add(root);
                FormClosed += (_, _) => SurveyLineworkReviewCommands.ClearCurrentHighlight();
            }

            private void HighlightSelected()
            {
                if (_grid.CurrentRow == null || _grid.CurrentRow.Tag is not int issueId)
                    return;

                SurveyLineworkReviewCommands.HighlightIssueOnly(issueId);
            }

            private void ZoomSelected()
            {
                if (_grid.CurrentRow == null || _grid.CurrentRow.Tag is not int issueId)
                    return;

                SurveyLineworkReviewCommands.ZoomToIssue(issueId);
            }

            private static void AddColumns(DataGridView grid)
            {
                grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "ID", Width = 55 });
                grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Issue", HeaderText = "Issue", Width = 150 });
                grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Parts", HeaderText = "Parts", Width = 60 });
                grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Layers", HeaderText = "Layers", Width = 220 });
                grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Types", HeaderText = "Types", Width = 140 });
                grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Handles", HeaderText = "Handles", Width = 160 });
                grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "ObjectDetails", HeaderText = "Object / Layer / Type", Width = 330 });
                grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Notes", HeaderText = "Notes", Width = 240 });
            }

            private static void AddRow(DataGridView grid, LineworkReviewIssue issue)
            {
                int rowIndex = grid.Rows.Add(issue.Id, issue.IssueType, issue.PartCount, issue.Layers, issue.ObjectTypes, issue.Handles, issue.ObjectDetails, issue.Notes);
                grid.Rows[rowIndex].Tag = issue.Id;
            }
        }
    }
}
