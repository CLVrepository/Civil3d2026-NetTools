using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using CLV_CivilTools.Shared;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CogoPoint = Autodesk.Civil.DatabaseServices.CogoPoint;
using TinSurface = Autodesk.Civil.DatabaseServices.TinSurface;

namespace CLV_CivilTools.PointCloud
{
    public static partial class CloudGridCommands
    {
        private const string App = "CLV_CLOUD_GRID_V1";
        private sealed record Job(string Key, CloudGridFrame Frame, double MinU, double MaxU, double MinV, double MaxV,
            string SurfaceHandle, string Units, string Created);
        private sealed record Tag(string Job, int Row, int Column, double OriginalZ, string Status, bool Added = false);
        private sealed class Review
        {
            internal Document Document = null!;
            internal Job Job = null!;
            internal ObjectId Cloud;
            internal int[] Rows = Array.Empty<int>();
            internal int Index;
            internal bool Columns, PlanView;
            internal double MarkerPercent = 1;
            internal readonly List<Entity> Markers = new();
            internal EventHandler? MarkerViewChanged, MarkerIdle;
            internal DocumentBeginCloseEventHandler? CloseGuard;
            internal bool MarkerPending = true;
            internal double Thickness, MinZ, MaxZ;
            internal Matrix3d Ucs;
            internal ViewTableRecord View = null!;
        }
        private static Review? review;
        static CloudGridCommands()
        {
            AcadApp.DocumentManager.DocumentToBeDestroyed += (_, args) =>
            {
                // Never open Civil database objects or write/erase surface styles
                // during destruction. Use CGEND before saving/closing; saved R9
                // surface journals remain recoverable by CGEND after reopening.
                if (review != null && ReferenceEquals(review.Document, args.Document))
                {
                    StopReviewMarkers(review);
                    review.View.Dispose(); review = null;
                }
            };
        }
        private static string Layer(Job job, string kind) => "V-PNTC-GRID-" + job.Key + "-" + kind;

        [CommandMethod("CGRID", CommandFlags.Modal)]
        public static void CreateGrid() => Run(document =>
        {
            if (HasReviewClones(document)) throw new InvalidOperationException("Run CGEND before creating another grid.");
            Editor ed = document.Editor; Database db = document.Database;
            ObjectId surfaceId = Pick(ed, "\nSelect temporary TIN surface: ", typeof(TinSurface));
            if (surfaceId.IsNull) return;
            ObjectId boundaryId = Pick(ed, "\nSelect closed straight-sided plan boundary: ", typeof(Polyline));
            if (boundaryId.IsNull) return;
            var originResult = ed.GetPoint("\nPick grid origin (near a corner of the test area): ");
            if (originResult.Status != PromptStatus.OK) return;
            var directionResult = ed.GetPoint(new PromptPointOptions("\nPick direction for grid X axis: ")
            { BasePoint = originResult.Value, UseBasePoint = true });
            if (directionResult.Status != PromptStatus.OK) return;
            Matrix3d ucs = ed.CurrentUserCoordinateSystem;
            Point3d origin = originResult.Value.TransformBy(ucs), direction = directionResult.Value.TransformBy(ucs);
            Vector3d axis = new(direction.X - origin.X, direction.Y - origin.Y, 0);
            if (axis.Length < 1e-8) throw new ArgumentException("Grid direction requires two different XY positions.");
            axis = axis.GetNormal();
            string units = Units(db);
            ed.WriteMessage("\nGrid uses Civil drawing units: " + units + ". Start with a 20 x 20 area.");
            double? spacing = Positive(ed, "\nGrid spacing", 1);
            if (!spacing.HasValue) return;
            var frame = new CloudGridFrame(origin.X, origin.Y, axis.X, axis.Y, spacing.Value);
            using Transaction tr = db.TransactionManager.StartTransaction();
            var boundary = (Polyline)tr.GetObject(boundaryId, OpenMode.ForRead);
            if (!boundary.Closed || boundary.NumberOfVertices < 3 || boundary.NumberOfVertices > 1000 ||
                Math.Abs(Math.Abs(boundary.Normal.Z) - 1) > 1e-8)
                throw new ArgumentException("Use a closed horizontal 2D polyline with 3 to 1000 vertices.");
            var polygon = new List<GridXY>();
            for (int i = 0; i < boundary.NumberOfVertices; i++)
            {
                if (Math.Abs(boundary.GetBulgeAt(i)) > 1e-12) throw new ArgumentException("Starter boundary must have straight sides; arcs are not supported.");
                Point3d p = boundary.GetPoint3dAt(i);
                polygon.Add(new GridXY(p.X, p.Y));
            }
            List<GridNode> nodes = CloudGridMath.Plan(frame, polygon);
            if (nodes.Count == 0) throw new InvalidOperationException("No grid intersections fall inside the boundary.");
            var surface = (TinSurface)tr.GetObject(surfaceId, OpenMode.ForRead);
            var sampled = new List<(GridNode Node, double Z)>();
            int outside = 0;
            foreach (GridNode node in nodes)
            {
                try
                {
                    double z = surface.FindElevationAtXY(node.X, node.Y);
                    if (!double.IsFinite(z)) throw new InvalidOperationException("Surface returned an invalid elevation.");
                    sampled.Add((node, z));
                }
                catch (Autodesk.Civil.PointNotOnEntityException) { outside++; }
            }
            if (sampled.Count == 0) throw new InvalidOperationException("No grid nodes have surface coverage.");
            var confirm = new PromptKeywordOptions("\nCreate " + sampled.Count + " AutoCAD POINTs; skip " + outside + " outside surface [Yes/No] <No>: ");
            confirm.Keywords.Add("Yes"); confirm.Keywords.Add("No"); confirm.Keywords.Default = "No"; confirm.AllowNone = true;
            var response = ed.GetKeywords(confirm);
            if (response.Status != PromptStatus.OK || response.StringResult != "Yes") return;
            GridXY[] local = polygon.Select(frame.Local).ToArray();
            var job = new Job(Guid.NewGuid().ToString("N").Substring(0, 12), frame, local.Min(p => p.X), local.Max(p => p.X),
                local.Min(p => p.Y), local.Max(p => p.Y), surfaceId.Handle.ToString(), units,
                DateTimeOffset.Now.ToString("O", CultureInfo.InvariantCulture));
            Register(db, tr);
            EnsureLayer(db, tr, Layer(job, "POINTS"), 2, true);
            EnsureLayer(db, tr, Layer(job, "EXCLUDED"), 1, false);
            EnsureLayer(db, tr, Layer(job, "GUIDES"), 8, false);
            var model = Model(db, tr, OpenMode.ForWrite);
            var outline = (Polyline)boundary.Clone();
            outline.Layer = Layer(job, "GUIDES");
            outline.XData = new ResultBuffer(new TypedValue(1001, App), new TypedValue(1000, "JOB"),
                new TypedValue(1000, JsonSerializer.Serialize(job)));
            model.AppendEntity(outline); tr.AddNewlyCreatedDBObject(outline, true);
            foreach (var sample in sampled)
            {
                var point = new DBPoint(new Point3d(sample.Node.X, sample.Node.Y, sample.Z)) { Layer = Layer(job, "POINTS") };
                WriteTag(point, new Tag(job.Key, sample.Node.Row, sample.Node.Column, sample.Z, "Sampled"));
                model.AppendEntity(point); tr.AddNewlyCreatedDBObject(point, true);
            }
            // Bounding grid guides are intentionally nonplot; POINTs alone are clipped to the polygon.
            foreach (int row in sampled.Select(s => s.Node.Row).Distinct())
                AddGuide(model, tr, frame.World(job.MinU, row * frame.Spacing), frame.World(job.MaxU, row * frame.Spacing), job);
            foreach (int col in sampled.Select(s => s.Node.Column).Distinct())
                AddGuide(model, tr, frame.World(col * frame.Spacing, job.MinV), frame.World(col * frame.Spacing, job.MaxV), job);
            tr.Commit();
            ed.WriteMessage("\nCreated " + sampled.Count + " native POINTs. Job " + job.Key + "; skipped " + outside +
                ". No source surface/cloud changes. Run CGREVIEW. New CGRID runs create NEW jobs, not updates.");
            ed.Regen();
        });

        [CommandMethod("CGREVIEW", CommandFlags.Modal)]
        public static void StartReview() => Run(document =>
        {
            if (review != null && !ReferenceEquals(review.Document, document))
                throw new InvalidOperationException("Finish the review in its original drawing with CGEND first.");
            EndReview(document);
            Editor ed = document.Editor;
            ObjectId pointId = PickGridPoint(document, null);
            if (pointId.IsNull) return;
            ObjectId cloudId = Pick(ed, "\nSelect attached point cloud to review: ", typeof(PointCloudEx));
            if (cloudId.IsNull) return;
            var direction = new PromptKeywordOptions("\nReview along grid [Rows/Columns] <Rows>: ");
            direction.Keywords.Add("Rows"); direction.Keywords.Add("Columns"); direction.Keywords.Default = "Rows"; direction.AllowNone = true;
            var answer = ed.GetKeywords(direction);
            if (answer.Status != PromptStatus.OK && answer.Status != PromptStatus.None) return;
            bool columns = answer.StringResult == "Columns";
            using Transaction tr = document.Database.TransactionManager.StartTransaction();
            var point = (Entity)tr.GetObject(pointId, OpenMode.ForRead);
            Tag tag = ReadTag(point) ?? throw new InvalidOperationException("Select a POINT created by CGRID.");
            Job job = FindJob(document.Database, tr, tag.Job);
            double defaultThickness = job.Units.StartsWith("Feet", StringComparison.Ordinal) ? 0.5 : 0.1524;
            double? thickness = Positive(ed, "\nTotal slice thickness in drawing units (six inches = 0.5 feet)", defaultThickness);
            if (!thickness.HasValue) return;
            if (thickness.Value >= job.Frame.Spacing)
                ed.WriteMessage("\nNote: slice is at least one grid spacing wide; adjacent rows may also be visible.");
            List<(ObjectId Id, Tag Tag, Point3d Position)> points = Points(document.Database, tr, job.Key);
            if (points.Count == 0) throw new InvalidOperationException("This job has no remaining points.");
            var source = (PointCloudEx)tr.GetObject(cloudId, OpenMode.ForRead);
            var sourceLayer = (LayerTableRecord)tr.GetObject(source.LayerId, OpenMode.ForRead);
            if (!source.Visible || sourceLayer.IsOff || sourceLayer.IsFrozen || sourceLayer.IsLocked)
                throw new InvalidOperationException("Source cloud must be visible, on a thawed/unlocked layer.");
            foreach (ObjectId otherId in Model(document.Database, tr, OpenMode.ForRead))
            {
                if (otherId == cloudId || tr.GetObject(otherId, OpenMode.ForRead) is not PointCloudEx other || !other.Visible) continue;
                var otherLayer = (LayerTableRecord)tr.GetObject(other.LayerId, OpenMode.ForRead);
                if (!otherLayer.IsOff && !otherLayer.IsFrozen)
                    throw new InvalidOperationException("Hide other point clouds before the starter review; review one attached cloud at a time.");
            }
            Register(document.Database, tr);
            EnsureLayer(document.Database, tr, Layer(job, "REVIEW"), 7, false);
            var clone = (PointCloudEx)source.Clone();
            clone.Layer = Layer(job, "REVIEW"); clone.Visible = true;
            clone.XData = new ResultBuffer(new TypedValue(1001, App), new TypedValue(1000, "REVIEW"),
                new TypedValue(1005, cloudId.Handle.ToString()), new TypedValue(1070, (short)1));
            Model(document.Database, tr, OpenMode.ForWrite).AppendEntity(clone); tr.AddNewlyCreatedDBObject(clone, true);
            source.UpgradeOpen(); source.Visible = false;
            HideReviewSurfaces(document.Database, tr);
            var state = new Review
            {
                Document = document, Job = job, Cloud = clone.ObjectId, Columns = columns,
                Thickness = thickness.Value, Rows = points.Select(p => columns ? p.Tag.Column : p.Tag.Row).Where(i => i != CloudGridMath.OffGrid).Distinct().OrderBy(i => i).ToArray(),
                MinZ = points.Min(p => p.Position.Z), MaxZ = points.Max(p => p.Position.Z),
                Ucs = ed.CurrentUserCoordinateSystem, View = ed.GetCurrentView()
            };
            if (state.Rows.Length == 0) { state.View.Dispose(); throw new InvalidOperationException("No points belong to grid lines in that direction."); }
            tr.Commit(); review = state;
            try
            {
                StartReviewMarkers(state);
                ed.WriteMessage("\nReview uses a tagged temporary cloud copy; source is temporarily hidden. Run CGEND before saving/closing. CGEND also recovers saved review copies.");
                ShowRow(state);
            }
            catch
            {
                // Preserve the existing cloud recovery path, but undo surface
                // presentation if initial review display setup fails.
                try { RestoreReviewSurfaces(document.Database); }
                catch (System.Exception error) { ed.WriteMessage("\nSurface recovery pending: " + error.Message); }
                throw;
            }
        });

        [CommandMethod("CGNEXT", CommandFlags.Modal)]
        public static void Next() => Run(d => Navigate(d, 1));
        [CommandMethod("CGBACK", CommandFlags.Modal)]
        public static void Back() => Run(d => Navigate(d, -1));
        [CommandMethod("CGSECTION", CommandFlags.Modal)]
        public static void Section() => Run(d => ShowRow(NeedReview(d)));
        [CommandMethod("CGPLAN", CommandFlags.Modal)]
        public static void Plan() => Run(d =>
        {
            Review state = NeedReview(d);
            d.Editor.CurrentUserCoordinateSystem = state.Ucs;
            d.Editor.SetCurrentView(state.View);
            state.PlanView = true;
            d.Editor.Regen();
        });
        [CommandMethod("CGEND", CommandFlags.Modal)]
        public static void End() => Run(EndReview);

        [CommandMethod("CGSETZ", CommandFlags.Modal)]
        public static void SetElevation() => Run(d =>
        {
            Review state = NeedReview(d);
            ObjectId id = PickGridPoint(d, state);
            if (id.IsNull) return;
            using Transaction tr = d.Database.TransactionManager.StartTransaction();
            var point = (Entity)tr.GetObject(id, OpenMode.ForRead);
            Tag tag = RequireCurrent(state, point);
            var options = new PromptDoubleOptions("\nNew absolute Z elevation (XY stays fixed)")
            { DefaultValue = PointPosition(point).Z, UseDefaultValue = true };
            var z = d.Editor.GetDouble(options);
            if (z.Status != PromptStatus.OK) return;
            if (!double.IsFinite(z.Value)) throw new ArgumentException("Elevation must be finite.");
            point.UpgradeOpen();
            SetPointElevation(point, z.Value);
            WriteTag(point, tag with { Status = tag.Status == "Excluded" ? "Excluded" : "Adjusted" });
            tr.Commit(); d.Editor.Regen();
            d.Editor.WriteMessage("\nZ updated; original sampled Z retained. Navigation will not resample.");
        });

        [CommandMethod("CGTOGGLE", CommandFlags.Modal)]
        public static void Toggle() => Run(d =>
        {
            Review state = NeedReview(d);
            ObjectId id = PickGridPoint(d, state);
            if (id.IsNull) return;
            using Transaction tr = d.Database.TransactionManager.StartTransaction();
            var point = (Entity)tr.GetObject(id, OpenMode.ForRead);
            Tag tag = RequireCurrent(state, point);
            bool exclude = tag.Status != "Excluded";
            point.UpgradeOpen();
            point.Layer = Layer(state.Job, point is CogoPoint ? (exclude ? "BREAKLINE-EXCLUDED" : "BREAKLINE-COGO") : (exclude ? "EXCLUDED" : "POINTS"));
            WriteTag(point, tag with { Status = exclude ? "Excluded" : !tag.Added && PointPosition(point).Z == tag.OriginalZ ? "Sampled" : "Adjusted" });
            tr.Commit(); d.Editor.Regen();
            d.Editor.WriteMessage(exclude ? "\nPoint excluded on its red nonplot exclusion layer." : "\nPoint restored to its ground or breakline point layer.");
        });

        [CommandMethod("CGACCEPT", CommandFlags.Modal)]
        public static void Accept() => Run(d =>
        {
            Review state = NeedReview(d);
            using Transaction tr = d.Database.TransactionManager.StartTransaction();
            int count = 0;
            foreach (var item in Points(d.Database, tr, state.Job.Key).Where(p => (state.Columns ? p.Tag.Column : p.Tag.Row) == state.Rows[state.Index]))
            {
                if (item.Tag.Status != "Sampled") continue;
                var point = (Entity)tr.GetObject(item.Id, OpenMode.ForWrite);
                WriteTag(point, item.Tag with { Status = "Accepted" }); count++;
            }
            tr.Commit(); d.Editor.WriteMessage("\nMarked " + count + " sampled points accepted. This is a manual review mark, not an automatic accuracy test.");
        });

        private static void Navigate(Document d, int delta)
        {
            Review state = NeedReview(d);
            state.Index = Math.Clamp(state.Index + delta, 0, state.Rows.Length - 1);
            ShowRow(state);
        }
        private static Review NeedReview(Document d) => review != null && ReferenceEquals(review.Document, d)
            ? review : throw new InvalidOperationException("Run CGREVIEW in this drawing first.");
        private static Tag RequireCurrent(Review state, Entity point)
        {
            Tag tag = ReadTag(point) ?? throw new InvalidOperationException("Not a generated grid POINT.");
            if (tag.Job != state.Job.Key || (!state.PlanView && (state.Columns ? tag.Column : tag.Row) != state.Rows[state.Index]))
                throw new InvalidOperationException("Pick a point from the current job and slice.");
            return tag;
        }

        private static void ShowRow(Review state)
        {
            Document d = state.Document; Editor ed = d.Editor; Job job = state.Job;
            if (!ReferenceEquals(AcadApp.DocumentManager.MdiActiveDocument, d)) throw new InvalidOperationException("Activate the review drawing.");
            using (Transaction tr = d.Database.TransactionManager.StartOpenCloseTransaction())
                if (!state.Cloud.IsValid || state.Cloud.IsErased || tr.GetObject(state.Cloud, OpenMode.ForRead) is not PointCloudEx)
                    throw new InvalidOperationException("Review cloud is no longer available. Run CGEND, then CGREVIEW.");
            GridXY[] strip = CloudGridMath.Strip(job.Frame, state.Columns, state.Rows[state.Index],
                job.MinU, job.MaxU, job.MinV, job.MaxV, state.Thickness);
            ed.CurrentUserCoordinateSystem = Matrix3d.Identity;
            ed.Command("_.PLAN", "_W");
            using (ViewTableRecord top = ed.GetCurrentView())
            {
                top.FrontClipEnabled = false; top.BackClipEnabled = false; top.PerspectiveEnabled = false;
                ed.SetCurrentView(top);
            }
            object oldSnap = AcadApp.GetSystemVariable("OSMODE"), old3dSnap = AcadApp.GetSystemVariable("3DOSMODE");
            try
            {
                AcadApp.SetSystemVariable("OSMODE", 0); AcadApp.SetSystemVariable("3DOSMODE", 0);
                PointCloudUtils.UncropPointCloudById(ed, state.Cloud);
                PointCloudUtils.CropPointCloudPolygonById(ed, state.Cloud, strip.Select(p => new Point3d(p.X, p.Y, 0)).ToList());
            }
            finally
            {
                AcadApp.SetSystemVariable("OSMODE", oldSnap); AcadApp.SetSystemVariable("3DOSMODE", old3dSnap);
            }
            double index = state.Rows[state.Index] * job.Frame.Spacing;
            GridXY mid = state.Columns ? job.Frame.World(index, (job.MinV + job.MaxV) / 2) :
                job.Frame.World((job.MinU + job.MaxU) / 2, index);
            Vector3d axis = state.Columns ? new Vector3d(-job.Frame.AxisY, job.Frame.AxisX, 0) : new Vector3d(job.Frame.AxisX, job.Frame.AxisY, 0);
            Point3d center = new(mid.X, mid.Y, (state.MinZ + state.MaxZ) / 2);
            ViewState.SetCrossSectionUcs(center, axis); ViewState.PlanCurrentUcs(ed);
            using ViewTableRecord view = ed.GetCurrentView();
            double aspect = view.Width / Math.Max(view.Height, 1e-8);
            double length = state.Columns ? job.MaxV - job.MinV : job.MaxU - job.MinU;
            view.Target = center; view.CenterPoint = Point2d.Origin;
            view.Height = Math.Max(length / Math.Max(aspect, 0.1), state.MaxZ - state.MinZ + 2 * Math.Max(job.Frame.Spacing, 1)) * 1.15;
            view.Width = view.Height * aspect;
            view.FrontClipAtEye = false; view.FrontClipDistance = state.Thickness / 2; view.BackClipDistance = -state.Thickness / 2;
            view.FrontClipEnabled = true; view.BackClipEnabled = true;
            ed.SetCurrentView(view); state.PlanView = false; ed.Regen();
            ed.WriteMessage("\nCLOUD GRID TEST: " + (state.Columns ? "column " : "row ") + state.Rows[state.Index] +
                " (" + (state.Index + 1) + "/" + state.Rows.Length + "), total thickness " + state.Thickness.ToString("F3", CultureInfo.InvariantCulture) +
                ". NEXT/BACK changes view only; SET Z holds XY fixed. CGEND restores the source cloud.");
        }

        private static void EndReview(Document d)
        {
            // Retire the cropped display while the tagged reference still exists.
            // Erasing a cropped clone immediately leaves no live object on which
            // to flush its display state. Do not clear the original source's crops.
            var retiring = new List<ObjectId>();
            using (Transaction prepare = d.Database.TransactionManager.StartTransaction())
            {
                foreach (ObjectId id in Model(d.Database, prepare, OpenMode.ForRead).Cast<ObjectId>().ToArray())
                {
                    if (prepare.GetObject(id, OpenMode.ForRead) is not PointCloudEx clone) continue;
                    using ResultBuffer? data = clone.GetXDataForApplication(App);
                    TypedValue[] values = data?.AsArray() ?? Array.Empty<TypedValue>();
                    if (values.Length != 4 || (string?)values[1].Value != "REVIEW") continue;
                    if (values[2].TypeCode != 1005 || values[2].Value is not string sourceHandle ||
                        !long.TryParse(sourceHandle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long handle) ||
                        values[3].Value is not short visible || visible is not (0 or 1))
                        throw new InvalidOperationException("Invalid review recovery tag; tagged copy retained.");
                    ObjectId sourceId = d.Database.GetObjectId(false, new Handle(handle), 0);
                    if (sourceId.IsNull || sourceId == id || sourceId.IsErased ||
                        prepare.GetObject(sourceId, OpenMode.ForRead) is not PointCloudEx source ||
                        source.PointCloudDefExId != clone.PointCloudDefExId)
                        throw new InvalidOperationException("Review source could not be verified; tagged copy retained.");
                    d.Editor.WriteMessage("\nR8 retire: copy=" + clone.Handle + ", source=" + source.Handle +
                        ", definition=" + clone.PointCloudDefExId.Handle + ", copy crops=" + clone.getCroppingCount() +
                        ", source crops=" + source.getCroppingCount() + ", source ShowCropped=" + source.ShowCropped);
                    retiring.Add(id);
                }
                // Validate every pair before modifying any copy.
                foreach (ObjectId id in retiring)
                {
                    var clone = (PointCloudEx)prepare.GetObject(id, OpenMode.ForWrite);
                    int before = clone.getCroppingCount();
                    bool cleared = clone.clearCropping();
                    if (before > 0 && !cleared)
                        throw new InvalidOperationException("Could not clear review-copy crops; copy retained for CGEND retry.");
                    clone.ShowCropped = false; clone.CroppingInverted = false;
                    if (clone.getCroppingCount() != 0 || clone.ShowCropped || clone.CroppingInverted)
                        throw new InvalidOperationException("Review-copy display did not reset; copy retained for CGEND retry.");
                    clone.RecordGraphicsModified(true);
                }
                prepare.Commit();
            }
            if (retiring.Count != 0)
            {
                d.Editor.SetImpliedSelection(Array.Empty<ObjectId>());
                d.Editor.Regen();
                // If this redraw fails, the source/copy tag and visibility remain
                // recoverable; do not erase the only recovery identity.
            }
            using Transaction tr = d.Database.TransactionManager.StartTransaction();
            int restored = 0;
            foreach (ObjectId id in Model(d.Database, tr, OpenMode.ForRead).Cast<ObjectId>().ToArray())
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not PointCloudEx clone) continue;
                using ResultBuffer? data = clone.GetXDataForApplication(App);
                TypedValue[] v = data?.AsArray() ?? Array.Empty<TypedValue>();
                if (v.Length != 4 || (string?)v[1].Value != "REVIEW" || v[2].TypeCode != 1005 || v[3].Value is not short visible) continue;
                long handle = long.Parse((string)v[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                ObjectId sourceId = d.Database.GetObjectId(false, new Handle(handle), 0);
                if (sourceId.IsNull || sourceId == id || sourceId.IsErased || visible is not (0 or 1) || tr.GetObject(sourceId, OpenMode.ForRead) is not PointCloudEx source ||
                    source.PointCloudDefExId != clone.PointCloudDefExId)
                    throw new InvalidOperationException("Review source could not be verified. No recovery changes applied.");
                source.UpgradeOpen(); source.Visible = visible != 0; source.RecordGraphicsModified(true);
                clone.UpgradeOpen(); clone.Erase(); restored++;
            }
            tr.Commit();
            if (review != null && ReferenceEquals(review.Document, d))
            {
                StopReviewMarkers(review);
                d.Editor.CurrentUserCoordinateSystem = review.Ucs;
                d.Editor.SetCurrentView(review.View); review.View.Dispose(); review = null;
            }
            d.Editor.SetImpliedSelection(Array.Empty<ObjectId>()); d.Editor.Regen();
            RestoreReviewSurfaces(d.Database);
            d.Editor.Regen();
            d.Editor.WriteMessage("\nGrid review ended. Restored " + restored + " source cloud(s); removed only tagged review copies. Grid points and corrections retained.");
        }

        private static bool HasReviewClones(Document d)
        {
            using Transaction tr = d.Database.TransactionManager.StartOpenCloseTransaction();
            foreach (ObjectId id in Model(d.Database, tr, OpenMode.ForRead))
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not PointCloudEx cloud) continue;
                using ResultBuffer? data = cloud.GetXDataForApplication(App);
                var v = data?.AsArray();
                if (v?.Length == 4 && (string?)v[1].Value == "REVIEW") return true;
            }
            return false;
        }

        private static Job FindJob(Database db, Transaction tr, string key)
        {
            foreach (ObjectId id in Model(db, tr, OpenMode.ForRead))
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not Polyline guide) continue;
                using ResultBuffer? data = guide.GetXDataForApplication(App);
                TypedValue[] v = data?.AsArray() ?? Array.Empty<TypedValue>();
                if (v.Length != 3 || (string?)v[1].Value != "JOB") continue;
                Job? job = JsonSerializer.Deserialize<Job>((string)v[2].Value);
                if (job?.Key == key) { job.Frame.Validate(); return job; }
            }
            throw new InvalidOperationException("The tagged job boundary is missing. Retain the job GUIDES boundary.");
        }
        private static List<(ObjectId Id, Tag Tag, Point3d Position)> Points(Database db, Transaction tr, string key)
        {
            var points = new List<(ObjectId, Tag, Point3d)>();
            foreach (ObjectId id in Model(db, tr, OpenMode.ForRead))
                if (tr.GetObject(id, OpenMode.ForRead) is Entity point && (point is DBPoint || point is CogoPoint) && ReadTag(point) is Tag tag && tag.Job == key)
                {
                    if (!double.IsFinite(PointPosition(point).Z)) throw new InvalidOperationException("Generated point has an invalid elevation.");
                    points.Add((id, tag, PointPosition(point)));
                }
            return points;
        }
        private static Point3d PointPosition(Entity point) => point is DBPoint dbPoint ? dbPoint.Position :
            point is CogoPoint cogo ? cogo.Location : throw new InvalidOperationException("Not a supported review point.");
        private static void SetPointElevation(Entity point, double z)
        {
            if (point is CogoPoint cogo) cogo.Elevation = z;
            else if (point is DBPoint dbPoint) dbPoint.Position = new Point3d(dbPoint.Position.X, dbPoint.Position.Y, z);
            else throw new InvalidOperationException("Not a supported review point.");
        }
        private static Tag? ReadTag(Entity point)
        {
            using ResultBuffer? data = point.GetXDataForApplication(App);
            TypedValue[] v = data?.AsArray() ?? Array.Empty<TypedValue>();
            if ((v.Length != 7 && v.Length != 8) || (string?)v[1].Value != "POINT" || v[2].Value is not string job || v[3].Value is not int row ||
                v[4].Value is not int col || v[5].Value is not double z || v[6].Value is not string status) return null;
            if (v.Length == 8 && (v[7].TypeCode != 1070 || v[7].Value is not short added || added != 1)) return null;
            return new Tag(job, row, col, z, status, v.Length == 8);
        }
        private static void WriteTag(Entity point, Tag tag)
        {
            var values = new[] { new TypedValue(1001, App), new TypedValue(1000, "POINT"), new TypedValue(1000, tag.Job),
                new TypedValue(1071, tag.Row), new TypedValue(1071, tag.Column), new TypedValue(1040, tag.OriginalZ), new TypedValue(1000, tag.Status) };
            using var data = new ResultBuffer(tag.Added ? values.Append(new TypedValue(1070, (short)1)).ToArray() : values);
            point.XData = data;
        }
        private static void Register(Database db, Transaction tr)
        {
            var table = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (table.Has(App)) return;
            table.UpgradeOpen(); var record = new RegAppTableRecord { Name = App };
            table.Add(record); tr.AddNewlyCreatedDBObject(record, true);
        }
        private static void EnsureLayer(Database db, Transaction tr, string name, short color, bool plot)
        {
            var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (layers.Has(name)) return;
            layers.UpgradeOpen();
            var layer = new LayerTableRecord { Name = name, Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, color), IsPlottable = plot };
            layers.Add(layer); tr.AddNewlyCreatedDBObject(layer, true);
        }
        private static BlockTableRecord Model(Database db, Transaction tr, OpenMode mode)
        {
            var table = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            return (BlockTableRecord)tr.GetObject(table[BlockTableRecord.ModelSpace], mode);
        }
        private static void AddGuide(BlockTableRecord model, Transaction tr, GridXY a, GridXY b, Job job)
        {
            var line = new Line(new Point3d(a.X, a.Y, 0), new Point3d(b.X, b.Y, 0)) { Layer = Layer(job, "GUIDES") };
            model.AppendEntity(line); tr.AddNewlyCreatedDBObject(line, true);
        }
        private static ObjectId Pick(Editor ed, string message, Type type)
        {
            var options = new PromptEntityOptions(message); options.SetRejectMessage("\nWrong object type.");
            options.AddAllowedClass(type, false);
            var result = ed.GetEntity(options); return result.Status == PromptStatus.OK ? result.ObjectId : ObjectId.Null;
        }
        private static double? Positive(Editor ed, string message, double value)
        {
            var result = ed.GetDouble(new PromptDoubleOptions(message) { DefaultValue = value, UseDefaultValue = true, AllowNegative = false, AllowZero = false });
            if (result.Status != PromptStatus.OK) return null;
            if (!double.IsFinite(result.Value) || result.Value <= 0) throw new ArgumentException("Value must be finite and positive.");
            return result.Value;
        }
        private static string Units(Database db)
        {
            var zone = CivilDocument.GetCivilDocument(db).Settings.DrawingSettings.UnitZoneSettings;
            return zone.DrawingUnits + (zone.DrawingUnits.ToString() == "Feet" ? " (" + zone.ImperialToMetricConversion + ")" : "");
        }
        private static void Run(Action<Document> action)
        {
            Document? d = AcadApp.DocumentManager.MdiActiveDocument;
            if (d == null) return;
            try
            {
                if (!d.Database.TileMode) throw new InvalidOperationException("Use the Model tab.");
                if (CloudGridRecovery.HasPending(d))
                    throw new InvalidOperationException("Saved R6/surface journals found. Run CGRECOVERYCHECK; use a pre-R6 copy or the guarded CGRECOVERDISPLAY before reviewing.");
                action(d);
            }
            catch (System.Exception error)
            {
                d.Editor.WriteMessage("\nCLOUD GRID TEST: " + error.Message + "\nIf a review was active, use CGEND to restore the original cloud/view.");
            }
            finally
            {
                if (review != null && ReferenceEquals(review.Document, d))
                {
                    review.MarkerPending = true;
                    try { RefreshReviewMarkers(review); }
                    catch (System.Exception error) { ClearReviewMarkers(review); d.Editor.WriteMessage("\nReview markers: " + error.Message); }
                }
            }
        }
    }
}
