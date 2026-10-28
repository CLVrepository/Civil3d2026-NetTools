using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices.Styles;
using CivilSurface = Autodesk.Civil.DatabaseServices.Surface;

namespace CLV_CivilTools.PointCloud
{
    public static partial class CloudGridCommands
    {
        private const string SurfaceJournal = "CLV_CLOUD_GRID_SURFACE_DISPLAY_R9";

        private static void RestoreReviewSurfaces(Database db)
        {
            using Transaction tr = db.TransactionManager.StartTransaction();
            RestoreReviewSurfaces(db, tr);
            tr.Commit();
        }

        private static void HideReviewSurfaces(Database db, Transaction tr)
        {
            var civil = CivilDocument.GetCivilDocument(db);
            ObjectId hiddenId = ObjectId.Null;
            foreach (ObjectId id in civil.GetSurfaceIds())
            {
                var surface = (CivilSurface)tr.GetObject(id, OpenMode.ForRead);
                // Reference objects are managed by their owning drawing.
                if (surface.IsReferenceObject) continue;
                var layer = (LayerTableRecord)tr.GetObject(surface.LayerId, OpenMode.ForRead);
                if (layer.IsLocked)
                    throw new InvalidOperationException("Unlock surface layer " + layer.Name + " before review. No surface styles changed.");
                if (hiddenId.IsNull)
                {
                    hiddenId = civil.Styles.SurfaceStyles.Add("CLV Grid Review No Display " + Guid.NewGuid().ToString("N"));
                    var hidden = (SurfaceStyle)tr.GetObject(hiddenId, OpenMode.ForWrite);
                    foreach (SurfaceDisplayStyleType component in Enum.GetValues<SurfaceDisplayStyleType>())
                    {
                        hidden.GetDisplayStylePlan(component).Visible = false;
                        hidden.GetDisplayStyleModel(component).Visible = false;
                    }
                }
                surface.UpgradeOpen();
                if (surface.ExtensionDictionary.IsNull) surface.CreateExtensionDictionary();
                var dictionary = (DBDictionary)tr.GetObject(surface.ExtensionDictionary, OpenMode.ForWrite);
                if (dictionary.Contains(SurfaceJournal))
                    throw new InvalidOperationException("Saved surface presentation found. Run CGEND before review.");
                var journal = new Xrecord();
                dictionary.SetAt(SurfaceJournal, journal); tr.AddNewlyCreatedDBObject(journal, true);
                using var data = new ResultBuffer(new TypedValue(90, 1),
                    new TypedValue(330, surface.StyleId), new TypedValue(330, hiddenId));
                journal.Data = data;
                surface.StyleId = hiddenId;
            }
        }

        private static void RestoreReviewSurfaces(Database db, Transaction tr)
        {
            var hiddenStyles = new HashSet<ObjectId>();
            foreach (ObjectId id in CivilDocument.GetCivilDocument(db).GetSurfaceIds())
            {
                var surface = (CivilSurface)tr.GetObject(id, OpenMode.ForRead);
                if (surface.ExtensionDictionary.IsNull) continue;
                var dictionary = (DBDictionary)tr.GetObject(surface.ExtensionDictionary, OpenMode.ForRead);
                if (!dictionary.Contains(SurfaceJournal)) continue;
                var journal = (Xrecord)tr.GetObject(dictionary.GetAt(SurfaceJournal), OpenMode.ForRead);
                using ResultBuffer? data = journal.Data;
                var values = data?.AsArray() ?? Array.Empty<TypedValue>();
                if (values.Length != 3 || values[0].TypeCode != 90 || values[0].Value is not int version || version != 1 ||
                    values[1].TypeCode != 330 || values[2].TypeCode != 330 ||
                    values[1].Value is not ObjectId original || values[2].Value is not ObjectId hidden ||
                    original.IsNull || !original.IsValid || original.IsErased ||
                    tr.GetObject(original, OpenMode.ForRead) is not SurfaceStyle)
                    throw new InvalidOperationException("Cannot verify saved surface style. Recovery journal retained; no recovery changes committed.");
                // Preserve an explicit style change made by the user during review.
                if (surface.StyleId == hidden)
                {
                    var layer = (LayerTableRecord)tr.GetObject(surface.LayerId, OpenMode.ForRead);
                    if (surface.IsReferenceObject || layer.IsLocked)
                        throw new InvalidOperationException("Surface restoration blocked by reference/locked layer: " + surface.Handle +
                            ". Recovery journal retained; unlock its layer and run CGEND.");
                    surface.UpgradeOpen(); surface.StyleId = original;
                }
                dictionary.UpgradeOpen(); dictionary.Remove(SurfaceJournal);
                journal.UpgradeOpen(); journal.Erase();
                hiddenStyles.Add(hidden);
            }
            // Delete only our generated style and only when no surface still uses it.
            foreach (ObjectId id in hiddenStyles)
            {
                if (id.IsNull || !id.IsValid || id.IsErased) continue;
                var style = (SurfaceStyle)tr.GetObject(id, OpenMode.ForRead);
                if (style.Name.StartsWith("CLV Grid Review No Display ", StringComparison.Ordinal) && !style.IsUsed)
                {
                    style.UpgradeOpen(); style.Erase();
                }
            }
        }
    }
}
