using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace CLV_CivilTools.LegalDescription
{
    internal static class LegalGeometryService
    {
        internal const double EndpointTolerance = 0.10;

        private sealed class SourceSegment
        {
            public ObjectId Id { get; init; }
            public string Handle { get; init; } = string.Empty;
            public string Type { get; init; } = "LINE";
            public Point3d Start { get; init; }
            public Point3d End { get; init; }
            public Point3d Center { get; init; }
            public double Radius { get; init; }
            public double ArcLength { get; init; }
            public double Delta { get; init; }
        }

        internal static LegalDescriptionSession BuildSession(
            Database db,
            IReadOnlyCollection<ObjectId> boundaryIds,
            Point3d requestedPob,
            Point3d? poc,
            IReadOnlyCollection<ObjectId>? tieIds)
        {
            List<LegalCourse> boundary = BuildCourses(db, boundaryIds, requestedPob, "BOUNDARY");
            Point3d actualPob = new(boundary[0].StartX, boundary[0].StartY, 0.0);

            var session = new LegalDescriptionSession
            {
                DrawingName = db.Filename,
                PointOfBoundaryX = actualPob.X,
                PointOfBoundaryY = actualPob.Y,
                PointOfCommencementX = (poc ?? actualPob).X,
                PointOfCommencementY = (poc ?? actualPob).Y,
                PointOfCommencementEqualsBoundary = !poc.HasValue || poc.Value.DistanceTo(actualPob) <= EndpointTolerance,
                Courses = boundary
            };

            if (!session.PointOfCommencementEqualsBoundary)
            {
                if (tieIds == null || tieIds.Count == 0)
                    throw new InvalidOperationException("A separate Point of Commencement requires connected tie LINE and ARC entities from the POC to the POB.");

                Point3d actualPoc = new(session.PointOfCommencementX, session.PointOfCommencementY, 0.0);
                List<LegalCourse> ties = BuildCourses(db, tieIds, actualPoc, "TIE");
                LegalCourse finalTie = ties[^1];
                double tieToPob = Distance(finalTie.EndX, finalTie.EndY, actualPob.X, actualPob.Y);
                if (tieToPob > EndpointTolerance)
                    throw new InvalidOperationException($"The selected tie traverse does not end at the Point of Beginning. Endpoint gap is {tieToPob:F3} feet.");
                session.TieCourses = ties;
            }

            return session;
        }

        private static List<LegalCourse> BuildCourses(Database db, IReadOnlyCollection<ObjectId> ids, Point3d startPoint, string group)
        {
            List<SourceSegment> source = ReadSegments(db, ids);
            if (source.Count == 0)
                throw new InvalidOperationException("No supported LINE or ARC entities were selected.");

            List<(SourceSegment Segment, bool Reversed)> ordered = OrderSegments(source, startPoint);
            var courses = new List<LegalCourse>();
            int number = 1;
            foreach ((SourceSegment segment, bool reversed) in ordered)
            {
                Point3d start = reversed ? segment.End : segment.Start;
                Point3d end = reversed ? segment.Start : segment.End;
                bool curveRight = false;
                if (segment.Type == "ARC")
                {
                    Vector2d chord = new(end.X - start.X, end.Y - start.Y);
                    Vector2d towardCenter = new(segment.Center.X - start.X, segment.Center.Y - start.Y);
                    double cross = chord.X * towardCenter.Y - chord.Y * towardCenter.X;
                    curveRight = cross < 0.0;
                }

                courses.Add(new LegalCourse
                {
                    Number = number++,
                    Group = group,
                    Handle = segment.Handle,
                    EntityType = segment.Type,
                    Reversed = reversed,
                    StartX = start.X,
                    StartY = start.Y,
                    EndX = end.X,
                    EndY = end.Y,
                    CenterX = segment.Center.X,
                    CenterY = segment.Center.Y,
                    Radius = segment.Radius,
                    ArcLength = segment.ArcLength,
                    DeltaRadians = segment.Delta,
                    CurveRight = curveRight
                });
            }
            return courses;
        }


        internal static void RefreshFromSourceGeometry(Database db, LegalDescriptionSession session)
        {
            using Transaction tr = db.TransactionManager.StartOpenCloseTransaction();
            RefreshCourses(db, tr, session.TieCourses);
            RefreshCourses(db, tr, session.Courses);
            tr.Commit();

            if (session.Courses.Count > 0)
            {
                session.PointOfBoundaryX = session.Courses[0].StartX;
                session.PointOfBoundaryY = session.Courses[0].StartY;
            }
            if (session.PointOfCommencementEqualsBoundary)
            {
                session.PointOfCommencementX = session.PointOfBoundaryX;
                session.PointOfCommencementY = session.PointOfBoundaryY;
            }
            else if (session.TieCourses.Count > 0)
            {
                session.PointOfCommencementX = session.TieCourses[0].StartX;
                session.PointOfCommencementY = session.TieCourses[0].StartY;
            }
        }

        private static void RefreshCourses(Database db, Transaction tr, IEnumerable<LegalCourse> courses)
        {
            foreach (LegalCourse course in courses)
            {
                if (!TryGetObjectId(db, course.Handle, out ObjectId id) || id.IsErased)
                    continue;

                DBObject obj;
                try { obj = tr.GetObject(id, OpenMode.ForRead, false); }
                catch (System.Exception) { continue; }

                if (obj is Line line)
                {
                    Point3d start = course.Reversed ? line.EndPoint : line.StartPoint;
                    Point3d end = course.Reversed ? line.StartPoint : line.EndPoint;
                    course.EntityType = "LINE";
                    course.StartX = start.X;
                    course.StartY = start.Y;
                    course.EndX = end.X;
                    course.EndY = end.Y;
                    course.CenterX = 0.0;
                    course.CenterY = 0.0;
                    course.Radius = 0.0;
                    course.ArcLength = 0.0;
                    course.DeltaRadians = 0.0;
                    course.CurveRight = false;
                }
                else if (obj is Arc arc)
                {
                    Point3d start = course.Reversed ? arc.EndPoint : arc.StartPoint;
                    Point3d end = course.Reversed ? arc.StartPoint : arc.EndPoint;
                    Vector2d chord = new(end.X - start.X, end.Y - start.Y);
                    Vector2d towardCenter = new(arc.Center.X - start.X, arc.Center.Y - start.Y);
                    double cross = chord.X * towardCenter.Y - chord.Y * towardCenter.X;

                    course.EntityType = "ARC";
                    course.StartX = start.X;
                    course.StartY = start.Y;
                    course.EndX = end.X;
                    course.EndY = end.Y;
                    course.CenterX = arc.Center.X;
                    course.CenterY = arc.Center.Y;
                    course.Radius = arc.Radius;
                    course.ArcLength = arc.Length;
                    course.DeltaRadians = arc.TotalAngle;
                    course.CurveRight = cross < 0.0;
                }
            }
        }

        private static bool TryGetObjectId(Database db, string handleText, out ObjectId id)
        {
            id = ObjectId.Null;
            try
            {
                long value = Convert.ToInt64(handleText, 16);
                id = db.GetObjectId(false, new Handle(value), 0);
                return !id.IsNull && id.IsValid;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        internal static LegalGeometrySummary Summarize(LegalDescriptionSession session)
        {
            if (session.Courses.Count == 0)
                return new LegalGeometrySummary { Warning = "No boundary courses are loaded." };

            double length = 0.0;
            double twiceArea = 0.0;
            foreach (LegalCourse course in session.Courses)
            {
                length += course.EntityType == "ARC" ? course.ArcLength : Distance(course.StartX, course.StartY, course.EndX, course.EndY);
                twiceArea += course.StartX * course.EndY - course.EndX * course.StartY;
                if (course.EntityType == "ARC" && course.Radius > 0.0)
                {
                    double segmentArea = 0.5 * course.Radius * course.Radius * (course.DeltaRadians - Math.Sin(course.DeltaRadians));
                    twiceArea += (course.CurveRight ? -2.0 : 2.0) * segmentArea;
                }
            }

            LegalCourse first = session.Courses[0];
            LegalCourse last = session.Courses[^1];
            double forward = Distance(last.EndX, last.EndY, first.StartX, first.StartY);

            double reverseX = first.StartX;
            double reverseY = first.StartY;
            for (int index = session.Courses.Count - 1; index >= 0; index--)
            {
                LegalCourse course = session.Courses[index];
                reverseX += course.StartX - course.EndX;
                reverseY += course.StartY - course.EndY;
            }
            double reverse = Distance(reverseX, reverseY, first.StartX, first.StartY);

            string warning = forward <= EndpointTolerance
                ? string.Empty
                : $"Boundary traverse is open by {forward.ToString("F3", CultureInfo.InvariantCulture)} feet.";

            if (!session.PointOfCommencementEqualsBoundary && session.TieCourses.Count > 0)
            {
                LegalCourse tieFirst = session.TieCourses[0];
                LegalCourse tieLast = session.TieCourses[^1];
                double pocGap = Distance(tieFirst.StartX, tieFirst.StartY, session.PointOfCommencementX, session.PointOfCommencementY);
                double pobGap = Distance(tieLast.EndX, tieLast.EndY, session.PointOfBoundaryX, session.PointOfBoundaryY);
                if (pocGap > EndpointTolerance || pobGap > EndpointTolerance)
                    warning = string.Join(" ", new[] { warning, $"Tie connection warning: POC gap {pocGap:F3} ft; POB gap {pobGap:F3} ft." }.Where(s => !string.IsNullOrWhiteSpace(s)));
            }

            return new LegalGeometrySummary
            {
                TraverseLength = length,
                ForwardMisclosure = forward,
                ReverseMisclosure = reverse,
                SignedArea = twiceArea / 2.0,
                IsClosed = forward <= EndpointTolerance,
                Warning = warning
            };
        }

        private static List<SourceSegment> ReadSegments(Database db, IReadOnlyCollection<ObjectId> ids)
        {
            var result = new List<SourceSegment>();
            using Transaction tr = db.TransactionManager.StartOpenCloseTransaction();
            foreach (ObjectId id in ids)
            {
                DBObject obj = tr.GetObject(id, OpenMode.ForRead);
                if (obj is Line line)
                {
                    result.Add(new SourceSegment { Id = id, Handle = id.Handle.ToString(), Type = "LINE", Start = line.StartPoint, End = line.EndPoint });
                }
                else if (obj is Arc arc)
                {
                    result.Add(new SourceSegment
                    {
                        Id = id,
                        Handle = id.Handle.ToString(),
                        Type = "ARC",
                        Start = arc.StartPoint,
                        End = arc.EndPoint,
                        Center = arc.Center,
                        Radius = arc.Radius,
                        ArcLength = arc.Length,
                        Delta = arc.TotalAngle
                    });
                }
            }
            tr.Commit();
            return result;
        }

        private static List<(SourceSegment Segment, bool Reversed)> OrderSegments(List<SourceSegment> remaining, Point3d requestedStart)
        {
            var pool = new List<SourceSegment>(remaining);
            SourceSegment first = pool.OrderBy(s => Math.Min(s.Start.DistanceTo(requestedStart), s.End.DistanceTo(requestedStart))).First();
            double nearestStartGap = Math.Min(first.Start.DistanceTo(requestedStart), first.End.DistanceTo(requestedStart));
            if (nearestStartGap > EndpointTolerance)
                throw new InvalidOperationException($"The selected start point is not on an endpoint of the selected traverse. Gap is {nearestStartGap:F3} feet.");

            bool firstReversed = first.End.DistanceTo(requestedStart) < first.Start.DistanceTo(requestedStart);
            var ordered = new List<(SourceSegment Segment, bool Reversed)> { (first, firstReversed) };
            pool.Remove(first);
            Point3d cursor = firstReversed ? first.Start : first.End;

            while (pool.Count > 0)
            {
                SourceSegment? next = null;
                bool reversed = false;
                double best = double.MaxValue;
                foreach (SourceSegment candidate in pool)
                {
                    double toStart = cursor.DistanceTo(candidate.Start);
                    if (toStart < best) { best = toStart; next = candidate; reversed = false; }
                    double toEnd = cursor.DistanceTo(candidate.End);
                    if (toEnd < best) { best = toEnd; next = candidate; reversed = true; }
                }

                if (next == null || best > EndpointTolerance)
                    throw new InvalidOperationException($"Selected geometry is not one connected traverse. The next endpoint gap is {best:F3} feet.");

                ordered.Add((next, reversed));
                pool.Remove(next);
                cursor = reversed ? next.Start : next.End;
            }
            return ordered;
        }

        private static double Distance(double x1, double y1, double x2, double y2)
        {
            double dx = x2 - x1;
            double dy = y2 - y1;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
