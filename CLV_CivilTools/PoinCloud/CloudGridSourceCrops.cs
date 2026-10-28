using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace CLV_CivilTools.PointCloud
{
    public static partial class CloudGridCommands
    {
        internal const string SourceCropJournalKey = "CLV_GRID_SOURCE_CROPS_R6";
        private sealed record CropBoundary(int Type, bool Inside, bool Inverted,
            CropXYZ Origin, CropXYZ XAxis, CropXYZ YAxis, CropXYZ[] Vertices);
        private sealed record CropDisplay(string Definition, CropXYZ Location, double Rotation, double Scale,
            bool Show, bool Inverted, CropBoundary[] Crops);
        private sealed record SourceCropJournal(int Version, CropDisplay Original, CropDisplay Expected);
        private static CropXYZ XYZ(Point3d p) => new(p.X, p.Y, p.Z);
        private static CropXYZ XYZ(Vector3d p) => new(p.X, p.Y, p.Z);
        private static Point3d Point(CropXYZ p) => new(p.X, p.Y, p.Z);
        private static Vector3d Vector(CropXYZ p) => new(p.X, p.Y, p.Z);

        private static CropDisplay CaptureCropDisplay(PointCloudEx cloud)
        {
            int count = cloud.getCroppingCount();
            if (count < 0 || count > 256) throw new InvalidOperationException("Unexpected cloud crop count; review not started.");
            var crops = new List<CropBoundary>();
            for (int i = 0; i < count; i++)
            {
                using PointCloudCrop crop = cloud.getPointCloudCropping(i);
                using Plane plane = crop.CropPlane;
                using Point3dCollection vertices = crop.Vertices;
                CoordinateSystem3d basis = plane.GetCoordinateSystem();
                if (!crop.IsValid() || vertices.Count > 10000)
                    throw new InvalidOperationException("Cannot safely snapshot this cloud crop boundary.");
                crops.Add(new((int)crop.CropType, crop.Inside, crop.Inverted, XYZ(basis.Origin),
                    XYZ(basis.Xaxis), XYZ(basis.Yaxis), vertices.Cast<Point3d>().Select(XYZ).ToArray()));
            }
            if (cloud.PointCloudDefExId.IsNull) throw new InvalidOperationException("Cloud definition is unavailable.");
            return new(cloud.PointCloudDefExId.Handle.ToString(), XYZ(cloud.Location), cloud.Rotation, cloud.Scale,
                cloud.ShowCropped, cloud.CroppingInverted, crops.ToArray());
        }

        private static CropDisplay BeginSourceCropReview(PointCloudEx cloud, Transaction tr)
        {
            if (HasCropJournal(cloud, tr)) throw new InvalidOperationException("Run CGEND to recover the saved cloud display first.");
            ValidateCloudTransform(cloud);
            CropDisplay original = CaptureCropDisplay(cloud);
            // Build every boundary before any mutation, validating the supported
            // snapshot/reconstruction path even when the source was pre-cropped.
            foreach (CropBoundary boundary in original.Crops)
                using (PointCloudCrop test = MakeCrop(boundary)) { }
            SaveCropJournal(cloud, tr, new(1, original, original));
            return original;
        }

        private static PointCloudCrop MakeCrop(CropBoundary boundary)
        {
            if (!Enum.IsDefined(typeof(PointCloudCropType), boundary.Type) ||
                boundary.Type == (int)PointCloudCropType.Invalid || boundary.Vertices.Length > 10000)
                throw new InvalidOperationException("Invalid saved cloud crop type/vertices.");
            PointCloudCrop crop = PointCloudCrop.Create(IntPtr.Zero);
            try
            {
                using var plane = new Plane(Point(boundary.Origin), Vector(boundary.XAxis), Vector(boundary.YAxis));
                using var vertices = new Point3dCollection(boundary.Vertices.Select(Point).ToArray());
                crop.CropType = (PointCloudCropType)boundary.Type;
                crop.CropPlane = plane; crop.Vertices = vertices;
                crop.Inside = boundary.Inside; crop.Inverted = boundary.Inverted;
                if (!crop.IsValid()) throw new InvalidOperationException("Civil 3D rejected a saved/generated crop boundary.");
                return crop;
            }
            catch { crop.Dispose(); throw; }
        }

        private static void ApplySourceSlice(Review state)
        {
            using Transaction tr = state.Document.Database.TransactionManager.StartTransaction();
            var cloud = (PointCloudEx)tr.GetObject(state.Cloud, OpenMode.ForWrite);
            SourceCropJournal journal = ReadCropJournal(cloud, tr);
            RequireSameDisplay(CaptureCropDisplay(cloud), journal.Expected);
            GridXY[] world = CloudGridMath.Strip(state.Job.Frame, state.Columns, state.Rows[state.Index],
                state.Job.MinU, state.Job.MaxU, state.Job.MinV, state.Job.MaxV, state.Thickness);
            CropXYZ[] native = world.Select(p => CloudGridCropMath.ToNative(
                new CropXYZ(p.X, p.Y, 0), journal.Original.Location, journal.Original.Rotation, journal.Original.Scale)).ToArray();
            CropXYZ[] closed = native.Append(native[0]).ToArray();
            var boundary = new CropBoundary((int)PointCloudCropType.Polygonal, true, false,
                native[0], new(1, 0, 0), new(0, 1, 0), closed);
            using PointCloudCrop slice = MakeCrop(boundary);
            ClearCrops(cloud);
            cloud.addCroppingBoundary(slice);
            cloud.ShowCropped = true; cloud.CroppingInverted = false;
            CropDisplay expected = journal.Original with { Crops = new[] { boundary }, Show = true, Inverted = false };
            RequireSameDisplay(CaptureCropDisplay(cloud), expected);
            SaveCropJournal(cloud, tr, journal with { Expected = CaptureCropDisplay(cloud) });
            cloud.RecordGraphicsModified(true);
            tr.Commit();
            state.Document.Editor.WriteMessage("\nR6 finite cloud crop: one closed ECS polygon; WCS thickness " + state.Thickness +
                ", source scale " + journal.Original.Scale + ". Original display journal retained; native visual test required.");
        }

        private static void ClearCrops(PointCloudEx cloud)
        {
            int count = cloud.getCroppingCount();
            bool cleared = cloud.clearCropping();
            if ((count > 0 && !cleared) || cloud.getCroppingCount() != 0)
                throw new InvalidOperationException("Cloud refused crop clear. Recovery journal retained.");
        }

        private static int RestoreSourceCropReviews(Database db, Transaction tr)
        {
            int restored = 0;
            foreach (ObjectId id in Model(db, tr, OpenMode.ForRead))
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not PointCloudEx cloud || !HasCropJournal(cloud, tr)) continue;
                SourceCropJournal journal = ReadCropJournal(cloud, tr);
                RequireSameDisplay(CaptureCropDisplay(cloud), journal.Expected);
                // Prepare all unmanaged boundary objects before clearing anything.
                var originals = new List<PointCloudCrop>();
                try
                {
                    foreach (CropBoundary boundary in journal.Original.Crops) originals.Add(MakeCrop(boundary));
                    if (!cloud.IsWriteEnabled) cloud.UpgradeOpen();
                    ClearCrops(cloud);
                    foreach (PointCloudCrop boundary in originals) cloud.addCroppingBoundary(boundary);
                    cloud.ShowCropped = journal.Original.Show; cloud.CroppingInverted = journal.Original.Inverted;
                    RequireSameDisplay(CaptureCropDisplay(cloud), journal.Original);
                    cloud.RecordGraphicsModified(true);
                    var dictionary = (DBDictionary)tr.GetObject(cloud.ExtensionDictionary, OpenMode.ForWrite);
                    var record = (Xrecord)tr.GetObject(dictionary.GetAt(SourceCropJournalKey), OpenMode.ForWrite);
                    dictionary.Remove(SourceCropJournalKey); record.Erase();
                    restored++;
                }
                finally { foreach (PointCloudCrop boundary in originals) boundary.Dispose(); }
            }
            return restored;
        }

        internal static bool HasPendingSourceCropReview(Document d)
        {
            if (review != null && ReferenceEquals(review.Document, d)) return true;
            using Transaction tr = d.Database.TransactionManager.StartOpenCloseTransaction();
            foreach (ObjectId id in Model(d.Database, tr, OpenMode.ForRead))
                if (tr.GetObject(id, OpenMode.ForRead) is PointCloudEx cloud && HasCropJournal(cloud, tr)) return true;
            return false;
        }

        private static bool HasCropJournal(PointCloudEx cloud, Transaction tr) =>
            !cloud.ExtensionDictionary.IsNull &&
            ((DBDictionary)tr.GetObject(cloud.ExtensionDictionary, OpenMode.ForRead)).Contains(SourceCropJournalKey);

        private static SourceCropJournal ReadCropJournal(PointCloudEx cloud, Transaction tr)
        {
            if (!HasCropJournal(cloud, tr)) throw new InvalidOperationException("Source display journal is missing. Review stopped.");
            var dictionary = (DBDictionary)tr.GetObject(cloud.ExtensionDictionary, OpenMode.ForRead);
            var record = (Xrecord)tr.GetObject(dictionary.GetAt(SourceCropJournalKey), OpenMode.ForRead);
            using ResultBuffer data = record.Data;
            TypedValue[] values = data.AsArray();
            if (values.Length > 5000 || values.Any(v => v.TypeCode != 1 || v.Value is not string))
                throw new InvalidOperationException("Invalid source display journal. No recovery changes committed.");
            string json = string.Concat(values.Select(v => (string)v.Value));
            SourceCropJournal journal = JsonSerializer.Deserialize<SourceCropJournal>(json) ??
                throw new InvalidOperationException("Unreadable source display journal.");
            if (journal.Version != 1) throw new InvalidOperationException("Unsupported source display journal version.");
            return journal;
        }

        private static void SaveCropJournal(PointCloudEx cloud, Transaction tr, SourceCropJournal journal)
        {
            string json = JsonSerializer.Serialize(journal);
            if (json.Length > 1000000) throw new InvalidOperationException("Cloud crop snapshot exceeds the review safety limit.");
            if (!cloud.IsWriteEnabled) cloud.UpgradeOpen();
            if (cloud.ExtensionDictionary.IsNull) cloud.CreateExtensionDictionary();
            var dictionary = (DBDictionary)tr.GetObject(cloud.ExtensionDictionary, OpenMode.ForWrite);
            Xrecord record;
            if (dictionary.Contains(SourceCropJournalKey))
                record = (Xrecord)tr.GetObject(dictionary.GetAt(SourceCropJournalKey), OpenMode.ForWrite);
            else
            {
                record = new Xrecord();
                dictionary.SetAt(SourceCropJournalKey, record); tr.AddNewlyCreatedDBObject(record, true);
            }
            var values = new List<TypedValue>();
            for (int offset = 0; offset < json.Length; offset += 240)
                values.Add(new TypedValue(1, json.Substring(offset, Math.Min(240, json.Length - offset))));
            using var data = new ResultBuffer(values.ToArray());
            record.Data = data;
        }

        private static void RequireSameDisplay(CropDisplay actual, CropDisplay expected)
        {
            bool close(CropXYZ a, CropXYZ b) => Math.Abs(a.X - b.X) <= 1e-7 && Math.Abs(a.Y - b.Y) <= 1e-7 && Math.Abs(a.Z - b.Z) <= 1e-7;
            if (actual.Definition != expected.Definition || !close(actual.Location, expected.Location) ||
                Math.Abs(actual.Scale - expected.Scale) > 1e-10 || Math.Abs(actual.Rotation - expected.Rotation) > 1e-10 ||
                actual.Show != expected.Show || actual.Inverted != expected.Inverted || actual.Crops.Length != expected.Crops.Length)
                throw new InvalidOperationException("Cloud display/placement changed outside this review. No further crop changes committed; journal retained.");
            for (int i = 0; i < actual.Crops.Length; i++)
            {
                CropBoundary a = actual.Crops[i], b = expected.Crops[i];
                if (a.Type != b.Type || a.Inside != b.Inside || a.Inverted != b.Inverted ||
                    !close(a.Origin, b.Origin) || !close(a.XAxis, b.XAxis) || !close(a.YAxis, b.YAxis) ||
                    a.Vertices.Length != b.Vertices.Length || a.Vertices.Where((p, n) => !close(p, b.Vertices[n])).Any())
                    throw new InvalidOperationException("Cloud crop differs from the expected review snapshot. Journal retained; no further crop changes committed.");
            }
        }

        private static void ValidateCloudTransform(PointCloudEx cloud)
        {
            Extents3d native = cloud.NativeExtents, geometric = cloud.GeomExtents;
            var corners = new List<CropXYZ>();
            foreach (double x in new[] { native.MinPoint.X, native.MaxPoint.X })
                foreach (double y in new[] { native.MinPoint.Y, native.MaxPoint.Y })
                    foreach (double z in new[] { native.MinPoint.Z, native.MaxPoint.Z })
                        corners.Add(CloudGridCropMath.ToWorld(new(x, y, z), XYZ(cloud.Location), cloud.Rotation, cloud.Scale));
            CropXYZ min = new(corners.Min(p => p.X), corners.Min(p => p.Y), corners.Min(p => p.Z));
            CropXYZ max = new(corners.Max(p => p.X), corners.Max(p => p.Y), corners.Max(p => p.Z));
            if (Point(min).DistanceTo(geometric.MinPoint) > 1e-4 || Point(max).DistanceTo(geometric.MaxPoint) > 1e-4)
                throw new InvalidOperationException("Cloud ECS-to-WCS transform could not be verified against its bounds. No crop applied.");
        }
    }
}
