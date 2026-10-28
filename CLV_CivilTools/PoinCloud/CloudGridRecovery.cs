using System;
using System.Linq;
using System.Text.Json;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.DatabaseServices.Styles;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilSurface = Autodesk.Civil.DatabaseServices.Surface;

namespace CLV_CivilTools.PointCloud
{
    public static class CloudGridRecovery
    {
        private const string CropKey = "CLV_GRID_SOURCE_CROPS_R6";
        private const string SurfaceKey = "CLV_CLOUD_GRID_SURFACE_DISPLAY_V1";

        internal static bool HasPending(Autodesk.AutoCAD.ApplicationServices.Document document)
        {
            using Transaction transaction = document.Database.TransactionManager.StartOpenCloseTransaction();
            foreach (ObjectId id in Model(document.Database, transaction))
                if (transaction.GetObject(id, OpenMode.ForRead) is Entity entity &&
                    (Has(entity, transaction, CropKey) || Has(entity, transaction, SurfaceKey))) return true;
            return false;
        }

        [CommandMethod("CGRECOVERYCHECK", CommandFlags.Modal)]
        public static void Audit()
        {
            var document = AcadApp.DocumentManager.MdiActiveDocument;
            if (document == null) return;
            try
            {
                if (!document.Database.TileMode) throw new InvalidOperationException("Use the Model tab.");
                document.Editor.WriteMessage("\n=== ROLLBACK RECOVERY AUDIT R7 (read-only; no crop objects constructed) ===");
                document.Editor.WriteMessage("\nExecuting assembly: " + typeof(CloudGridRecovery).Assembly.Location);
                using Transaction transaction = document.Database.TransactionManager.StartOpenCloseTransaction();
                int count = 0;
                foreach (ObjectId id in Model(document.Database, transaction))
                {
                    if (transaction.GetObject(id, OpenMode.ForRead) is not Entity entity) continue;
                    if (Has(entity, transaction, CropKey))
                    {
                        count++;
                        using JsonDocument snapshot = ReadCropJournal(entity, transaction);
                        JsonElement original = snapshot.RootElement.GetProperty("Original");
                        int crops = original.GetProperty("Crops").GetArrayLength();
                        document.Editor.WriteMessage("\nR6 crop journal on handle " + entity.Handle + ": original crops=" + crops +
                            (crops == 0 ? "; uncropped recovery available via CGRECOVERDISPLAY on a copy." :
                            "; automatic reconstruction intentionally disabled. Use a pre-R6 drawing/backup; journal retained."));
                    }
                    if (Has(entity, transaction, SurfaceKey))
                    {
                        count++;
                        document.Editor.WriteMessage("\nSaved surface-style journal on handle " + entity.Handle + ".");
                    }
                }
                document.Editor.WriteMessage("\nPending journals=" + count +
                    ". Prefer a pre-R6 drawing copy. Never invoke R6 CGREVIEW for recovery. No drawing changes applied.");
            }
            catch (System.Exception error) { document.Editor.WriteMessage("\nRECOVERY AUDIT: " + error.Message); }
        }

        [CommandMethod("CGRECOVERDISPLAY", CommandFlags.Modal)]
        public static void RecoverUncroppedDisplay()
        {
            var document = AcadApp.DocumentManager.MdiActiveDocument;
            if (document == null) return;
            try
            {
                if (!document.Database.TileMode) throw new InvalidOperationException("Use the Model tab.");
                using Transaction transaction = document.Database.TransactionManager.StartTransaction();
                ObjectId[] ids = Model(document.Database, transaction).Cast<ObjectId>().ToArray();
                // Preflight every journal before clearing any display crop.
                foreach (ObjectId id in ids)
                {
                    if (transaction.GetObject(id, OpenMode.ForRead) is not Entity entity) continue;
                    if ((Has(entity, transaction, CropKey) || Has(entity, transaction, SurfaceKey)) &&
                        ((LayerTableRecord)transaction.GetObject(entity.LayerId, OpenMode.ForRead)).IsLocked)
                        throw new InvalidOperationException("A journaled object's layer is locked; no recovery changes committed.");
                    if (Has(entity, transaction, CropKey))
                    {
                        if (entity is not PointCloudEx cloud) throw new InvalidOperationException("R6 journal belongs to an unexpected object.");
                        using JsonDocument snapshot = ReadCropJournal(entity, transaction);
                        ValidateUncroppedRecovery(cloud, snapshot.RootElement);
                    }
                    if (Has(entity, transaction, SurfaceKey)) ValidateSurface(entity, transaction);
                }
                int clouds = 0, surfaces = 0;
                foreach (ObjectId id in ids)
                {
                    if (transaction.GetObject(id, OpenMode.ForRead) is not Entity entity) continue;
                    if (Has(entity, transaction, CropKey))
                    {
                        var cloud = (PointCloudEx)entity;
                        using JsonDocument snapshot = ReadCropJournal(entity, transaction);
                        JsonElement original = snapshot.RootElement.GetProperty("Original");
                        cloud.UpgradeOpen();
                        int previous = cloud.getCroppingCount();
                        bool cleared = cloud.clearCropping();
                        if ((previous > 0 && !cleared) || cloud.getCroppingCount() != 0)
                            throw new InvalidOperationException("Crop clear refused; recovery journal retained.");
                        cloud.ShowCropped = original.GetProperty("Show").GetBoolean();
                        cloud.CroppingInverted = original.GetProperty("Inverted").GetBoolean();
                        if (cloud.ShowCropped != original.GetProperty("Show").GetBoolean() ||
                            cloud.CroppingInverted != original.GetProperty("Inverted").GetBoolean())
                            throw new InvalidOperationException("Display flags did not restore; journal retained.");
                        cloud.RecordGraphicsModified(true);
                        Archive(entity, transaction, CropKey); clouds++;
                    }
                    if (Has(entity, transaction, SurfaceKey))
                    {
                        var surface = (CivilSurface)entity;
                        TypedValue[] values = SurfaceValues(entity, transaction);
                        ObjectId original = (ObjectId)values[1].Value, hidden = (ObjectId)values[2].Value;
                        if (!surface.IsWriteEnabled) surface.UpgradeOpen();
                        if (surface.StyleId == hidden) surface.StyleId = original;
                        Archive(entity, transaction, SurfaceKey); surfaces++;
                    }
                }
                transaction.Commit();
                document.Editor.Regen();
                document.Editor.WriteMessage("\nRecovered " + clouds + " originally-uncropped cloud display(s) and " + surfaces +
                    " surface style(s). Original journals archived, not deleted. No crop objects constructed, source files/placement/scan visibility unchanged.");
                document.Editor.WriteMessage("\nVerify the actual cloud display before saving. If still wrong, stop and use a pre-R6 drawing copy; do not retry R6 review.");
            }
            catch (System.Exception error)
            {
                document.Editor.WriteMessage("\nDISPLAY RECOVERY STOPPED: " + error.Message +
                    "\nNo recovery transaction committed. Preserve the drawing/journals and use a pre-R6 copy.");
            }
        }

        private static void ValidateUncroppedRecovery(PointCloudEx cloud, JsonElement root)
        {
            JsonElement original = root.GetProperty("Original"), expected = root.GetProperty("Expected");
            if (original.GetProperty("Crops").GetArrayLength() != 0)
                throw new InvalidOperationException("Original crop list is nonempty. Native crop reconstruction is disabled after the R6 crash; use a pre-R6 backup.");
            if (!double.IsFinite(cloud.Location.X) || !double.IsFinite(cloud.Location.Y) || !double.IsFinite(cloud.Location.Z) ||
                !double.IsFinite(cloud.Scale) || !double.IsFinite(cloud.Rotation))
                throw new InvalidOperationException("Invalid current cloud placement; no recovery attempted.");
            if (cloud.PointCloudDefExId.IsNull || cloud.PointCloudDefExId.Handle.ToString() != original.GetProperty("Definition").GetString() ||
                cloud.PointCloudDefExId.Handle.ToString() != expected.GetProperty("Definition").GetString())
                throw new InvalidOperationException("Cloud definition no longer matches the journal.");
            foreach (JsonElement state in new[] { original, expected })
            {
                JsonElement location = state.GetProperty("Location");
                double x = location.GetProperty("X").GetDouble(), y = location.GetProperty("Y").GetDouble(), z = location.GetProperty("Z").GetDouble();
                if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z) ||
                    !double.IsFinite(state.GetProperty("Scale").GetDouble()) || !double.IsFinite(state.GetProperty("Rotation").GetDouble()))
                    throw new InvalidOperationException("Invalid saved placement; journals retained.");
                Point3d saved = new(x, y, z);
                if (cloud.Location.DistanceTo(saved) > 1e-7 ||
                    Math.Abs(cloud.Scale - state.GetProperty("Scale").GetDouble()) > 1e-10 ||
                    Math.Abs(cloud.Rotation - state.GetProperty("Rotation").GetDouble()) > 1e-10)
                    throw new InvalidOperationException("Cloud placement changed. It will not be overwritten.");
            }
            if (cloud.getCroppingCount() != expected.GetProperty("Crops").GetArrayLength() ||
                cloud.ShowCropped != expected.GetProperty("Show").GetBoolean() ||
                cloud.CroppingInverted != expected.GetProperty("Inverted").GetBoolean())
                throw new InvalidOperationException("Current display flags/count differ from expected R6 state. Preserve journals; no blind clear.");
        }

        private static JsonDocument ReadCropJournal(Entity entity, Transaction transaction)
        {
            var record = Journal(entity, transaction, CropKey);
            using ResultBuffer data = record.Data;
            TypedValue[] values = data.AsArray();
            if (values.Length > 5000 || values.Any(v => v.TypeCode != 1 || v.Value is not string))
                throw new InvalidOperationException("Malformed R6 crop journal.");
            JsonDocument result = JsonDocument.Parse(string.Concat(values.Select(v => (string)v.Value)));
            if (result.RootElement.GetProperty("Version").GetInt32() != 1)
            {
                result.Dispose(); throw new InvalidOperationException("Unsupported R6 journal version.");
            }
            return result;
        }

        private static void ValidateSurface(Entity entity, Transaction transaction)
        {
            if (entity is not CivilSurface surface || surface.IsReferenceObject)
                throw new InvalidOperationException("Surface journal belongs to an unexpected or referenced object.");
            TypedValue[] values = SurfaceValues(entity, transaction);
            if (values.Length != 3 || values[0].Value is not int version || version != 1 ||
                values[1].Value is not ObjectId original || original.IsNull || !original.IsValid || original.IsErased ||
                values[2].Value is not ObjectId || transaction.GetObject(original, OpenMode.ForRead) is not SurfaceStyle)
                throw new InvalidOperationException("Saved surface style cannot be verified; journals retained.");
        }

        private static TypedValue[] SurfaceValues(Entity entity, Transaction transaction)
        {
            using ResultBuffer data = Journal(entity, transaction, SurfaceKey).Data;
            return data.AsArray();
        }

        private static Xrecord Journal(Entity entity, Transaction transaction, string key) =>
            (Xrecord)transaction.GetObject(((DBDictionary)transaction.GetObject(entity.ExtensionDictionary, OpenMode.ForRead)).GetAt(key), OpenMode.ForRead);
        private static bool Has(Entity entity, Transaction transaction, string key) =>
            !entity.ExtensionDictionary.IsNull &&
            ((DBDictionary)transaction.GetObject(entity.ExtensionDictionary, OpenMode.ForRead)).Contains(key);
        private static void Archive(Entity entity, Transaction transaction, string key)
        {
            var dictionary = (DBDictionary)transaction.GetObject(entity.ExtensionDictionary, OpenMode.ForWrite);
            string archived = key + "_RECOVERED_" + Guid.NewGuid().ToString("N");
            dictionary.SetName(key, archived);
            if (dictionary.Contains(key) || !dictionary.Contains(archived))
                throw new InvalidOperationException("Could not archive recovery journal; transaction not committed.");
        }
        private static BlockTableRecord Model(Database db, Transaction transaction)
        {
            var table = (BlockTable)transaction.GetObject(db.BlockTableId, OpenMode.ForRead);
            return (BlockTableRecord)transaction.GetObject(table[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        }
    }
}
