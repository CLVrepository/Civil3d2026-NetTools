using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.PointCloud
{
    /// <summary>Explicit focus synchronization; ordinary navigation is independent.</summary>
    public static class CloudReviewViews
    {
        private const string BackupPrefix = "CLV_CLOUD_VIEWS_RETURN_";
        private const double Split = 0.35;
        private sealed class Session
        {
            internal string Backup = "";
            internal Point3d Focus;
            internal ObjectId Plan, Oblique;
            internal Matrix3d OriginalUcs;
            internal Point2d OriginalActiveCorner;
            internal int OriginalCount;
        }
        private static readonly Dictionary<Document, Session> Sessions = new();

        static CloudReviewViews()
        {
            // Managed values only: never restore views or access the database at close.
            AcadApp.DocumentManager.DocumentToBeDestroyed += (_, e) => Sessions.Remove(e.Document);
        }

        internal static bool IsActive(Document document) => Sessions.ContainsKey(document);

        [CommandMethod("CGVIEWS", CommandFlags.Modal)]
        public static void Start() => Run(d =>
        {
            if (Sessions.ContainsKey(d))
                throw new InvalidOperationException("Views are already active. Use CGFOCUS, CGORBIT or CGVIEWEND.");
            string[] pending = BackupNames(d.Database);
            if (pending.Length != 0)
                throw new InvalidOperationException("A saved return configuration exists. Run CGVIEWEND first.");
            RequireNoSlice(d);
            Editor ed = d.Editor;
            using ViewTableRecord original = ed.GetCurrentView();
            Point3d? picked = PickFocus(d);
            if (!picked.HasValue) return;

            ed.UpdateTiledViewportsInDatabase();
            var s = new Session
            {
                Backup = BackupPrefix + Guid.NewGuid().ToString("N"),
                Focus = picked.Value,
                OriginalUcs = ed.CurrentUserCoordinateSystem
            };
            using (Transaction tr = d.Database.TransactionManager.StartOpenCloseTransaction())
            {
                var active = (ViewportTableRecord)tr.GetObject(d.Database.CurrentViewportTableRecordId, OpenMode.ForRead);
                s.OriginalActiveCorner = active.LowerLeftCorner;
                s.OriginalCount = ActiveIds(d.Database, tr).Count;
            }
            SaveConfiguration(d, s.Backup);
            if (!BackupNames(d.Database).Contains(s.Backup, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Could not save the original viewport configuration; views were not replaced.");
            Sessions.Add(d, s);
            ed.WriteMessage("\nReturn configuration: " + s.Backup);
            try
            {
                CreatePair(d, s, original);
                ed.WriteMessage("\nPLAN is left; OBLIQUE is right. Pan/zoom independently."
                    + "\nCGFOCUS sets a new shared center; CGORBIT orbits that center; CGVIEWCENTER recenters."
                    + "\nCGVIEWEND restores the previous arrangement. This tool does not extract or edit points.");
            }
            catch
            {
                try { Restore(d, s.Backup, s); }
                catch (System.Exception restoreError)
                {
                    ed.WriteMessage("\nView rollback needs CGVIEWEND: " + restoreError.Message
                        + "\nSaved return configuration retained: " + s.Backup);
                }
                throw;
            }
        });

        [CommandMethod("CGFOCUS", CommandFlags.Modal)]
        public static void Focus() => Run(d =>
        {
            Session s = GetSession(d);
            RequirePair(d, s);
            RequireNoSlice(d);
            Point3d? picked = PickFocus(d);
            if (!picked.HasValue) return;
            CenterPair(d, s, picked.Value);
            s.Focus = picked.Value;
            ReportFocus(d.Editor, s.Focus);
        });

        [CommandMethod("CGVIEWCENTER", CommandFlags.Modal)]
        public static void Recenter() => Run(d =>
        {
            Session s = GetSession(d);
            RequireNoSlice(d);
            CenterPair(d, s, s.Focus);
            ReportFocus(d.Editor, s.Focus);
        });

        [CommandMethod("CGORBIT", CommandFlags.Modal)]
        public static void Orbit() => Run(d =>
        {
            Session s = GetSession(d);
            RequireNoSlice(d);
            CenterPair(d, s, s.Focus);
            SetActive(d, s.Oblique);
            d.Editor.SetImpliedSelection(Array.Empty<ObjectId>());
            // WCS '*' prefix avoids UCS/dynamic-input relative-coordinate ambiguity.
            // Queue the interactive native command only after all transactions end.
            string coordinate = "*" + s.Focus.X.ToString("R", CultureInfo.InvariantCulture)
                + "," + s.Focus.Y.ToString("R", CultureInfo.InvariantCulture)
                + "," + s.Focus.Z.ToString("R", CultureInfo.InvariantCulture);
            d.Editor.WriteMessage("\nOrbit the right view; press Esc to finish. Plan remains north-up.");
            d.SendStringToExecute("_.3DORBITCTR\n_non\n" + coordinate + "\n", true, false, false);
        });

        [CommandMethod("CGVIEWEND", CommandFlags.Modal)]
        public static void End() => Run(d =>
        {
            if (Sessions.TryGetValue(d, out Session? s))
            {
                if (!BackupNames(d.Database).Contains(s.Backup, StringComparer.OrdinalIgnoreCase))
                {
                    Sessions.Remove(d);
                    d.Editor.WriteMessage("\nReturn configuration was removed, possibly by Undo. View session ended; current views left unchanged."
                        + "\nUse -VPORTS Single if you want one viewport.");
                    return;
                }
                Restore(d, s.Backup, s);
                return;
            }
            string[] names = BackupNames(d.Database);
            if (names.Length == 0)
            {
                d.Editor.WriteMessage("\nNo cloud-view return configuration is pending.");
                return;
            }
            if (names.Length != 1)
                throw new InvalidOperationException("Multiple return configurations exist. Restore the desired one with -VPORTS Restore: "
                    + string.Join(", ", names));
            // A drawing saved/reopened in split view can recover its saved arrangement.
            Restore(d, names[0], null);
        });

        private static Session GetSession(Document d) => Sessions.TryGetValue(d, out Session? s) ? s :
            throw new InvalidOperationException("Run CGVIEWS first. After reopening a saved split view, use CGVIEWEND then CGVIEWS.");

        private static void CreatePair(Document d, Session s, ViewTableRecord original)
        {
            Editor ed = d.Editor;
            Point2d screen = (Point2d)AcadApp.GetSystemVariable("SCREENSIZE");
            if (screen.X <= 0 || screen.Y <= 0 || !double.IsFinite(original.Height) || original.Height <= 0)
                throw new InvalidOperationException("Invalid drawing window size.");
            double aspect = screen.X / screen.Y;
            Vector3d direction = original.ViewDirection.GetNormal();
            if (direction.Z < 0.1 || direction.Z > 0.999) direction = new Vector3d(1, -1, 1).GetNormal();

            using (Transaction tr = d.Database.TransactionManager.StartTransaction())
            {
                var table = (ViewportTable)tr.GetObject(d.Database.ViewportTableId, OpenMode.ForWrite);
                // Clone the active viewport settings; preserve all named configurations.
                var source = (ViewportTableRecord)tr.GetObject(d.Database.CurrentViewportTableRecordId, OpenMode.ForRead);
                using var plan = (ViewportTableRecord)source.Clone();
                using var oblique = (ViewportTableRecord)source.Clone();
                foreach (ObjectId id in ActiveIds(d.Database, tr))
                    tr.GetObject(id, OpenMode.ForWrite).Erase();
                Configure(plan, s.Focus, Vector3d.ZAxis, original.Height, original.Height * aspect * Split,
                    Point2d.Origin, new Point2d(Split, 1));
                Configure(oblique, s.Focus, direction, original.Height, original.Height * aspect * (1 - Split),
                    new Point2d(Split, 0), new Point2d(1, 1));
                s.Plan = table.Add(plan); tr.AddNewlyCreatedDBObject(plan, true);
                s.Oblique = table.Add(oblique); tr.AddNewlyCreatedDBObject(oblique, true);
                ed.UpdateTiledViewportsFromDatabase();
                tr.Commit();
            }
            RequirePair(d, s);
            SetActive(d, s.Oblique);
            ed.Regen();
            ReportFocus(ed, s.Focus);
        }

        private static void Configure(ViewportTableRecord v, Point3d focus, Vector3d direction,
            double height, double width, Point2d lower, Point2d upper)
        {
            v.Name = "*Active";
            v.LowerLeftCorner = lower; v.UpperRightCorner = upper;
            v.Target = focus; v.CenterPoint = Point2d.Origin;
            v.ViewDirection = direction; v.ViewTwist = 0;
            v.Height = height; v.Width = width;
            v.PerspectiveEnabled = false;
            v.FrontClipEnabled = false; v.BackClipEnabled = false;
            v.UcsFollowMode = false; v.SetUcsToWorld(); v.UcsSavedWithViewport = true;
        }

        private static void CenterPair(Document d, Session s, Point3d focus)
        {
            RequirePair(d, s);
            ObjectId active = d.Database.CurrentViewportTableRecordId;
            // Pull current pan, zoom and orbit state before touching the stored records.
            d.Editor.UpdateTiledViewportsInDatabase();
            using (Transaction tr = d.Database.TransactionManager.StartTransaction())
            {
                var plan = (ViewportTableRecord)tr.GetObject(s.Plan, OpenMode.ForWrite);
                var oblique = (ViewportTableRecord)tr.GetObject(s.Oblique, OpenMode.ForWrite);
                plan.Target = focus; plan.CenterPoint = Point2d.Origin;
                plan.ViewDirection = Vector3d.ZAxis; plan.ViewTwist = 0;
                oblique.Target = focus; oblique.CenterPoint = Point2d.Origin;
                // Orthographic viewing keeps coordinate comparisons predictable.
                plan.PerspectiveEnabled = false; oblique.PerspectiveEnabled = false;
                d.Editor.UpdateTiledViewportsFromDatabase();
                tr.Commit();
            }
            SetActive(d, active);
            d.Editor.Regen();
        }

        private static Point3d? PickFocus(Document d)
        {
            Editor ed = d.Editor;
            int inputViewport = Convert.ToInt32(AcadApp.GetSystemVariable("CVPORT"));
            Matrix3d inputUcs = ed.CurrentUserCoordinateSystem;
            var saved = new Dictionary<string, object>();
            Point3d? result = null;
            var failures = new List<string>();
            try
            {
                foreach (string name in new[] { "OSMODE", "3DOSMODE", "OSNAPZ", "ORTHOMODE", "SNAPMODE" })
                    saved.Add(name, AcadApp.GetSystemVariable(name));
                AcadApp.SetSystemVariable("OSMODE", 0);
                AcadApp.SetSystemVariable("3DOSMODE", 128);
                AcadApp.SetSystemVariable("OSNAPZ", 0);
                AcadApp.SetSystemVariable("ORTHOMODE", 0);
                AcadApp.SetSystemVariable("SNAPMODE", 0);
                ed.WriteMessage("\nChoose an approximate view focus, or wait for the cloud NODE glyph for its elevation."
                    + "\nThis is navigation only; the pick is not an extracted survey point.");
                PromptPointResult pick = ed.GetPoint(new PromptPointOptions("\nPick shared focus <cancel>: ") { AllowNone = true });
                if (pick.Status != PromptStatus.OK) return null;
                if (Convert.ToInt32(AcadApp.GetSystemVariable("CVPORT")) != inputViewport)
                    throw new InvalidOperationException("Activate the desired pane before SET FOCUS; do not switch panes during its prompts.");
                Point3d wcs = pick.Value.TransformBy(inputUcs);
                PromptDoubleResult z = ed.GetDouble(new PromptDoubleOptions(string.Format(CultureInfo.InvariantCulture,
                    "\nFocus elevation <{0:F3}>: ", wcs.Z))
                {
                    DefaultValue = wcs.Z, UseDefaultValue = true, AllowNegative = true, AllowZero = true
                });
                if (z.Status != PromptStatus.OK) return null;
                if (Convert.ToInt32(AcadApp.GetSystemVariable("CVPORT")) != inputViewport)
                    throw new InvalidOperationException("The active pane changed; focus was not changed. Run SET FOCUS again.");
                if (!double.IsFinite(wcs.X) || !double.IsFinite(wcs.Y) || !double.IsFinite(z.Value))
                    throw new InvalidOperationException("Focus coordinates must be finite.");
                result = new Point3d(wcs.X, wcs.Y, z.Value);
            }
            finally
            {
                try
                {
                    if (Convert.ToInt32(AcadApp.GetSystemVariable("CVPORT")) != inputViewport)
                        AcadApp.SetSystemVariable("CVPORT", inputViewport);
                }
                catch (System.Exception e) { failures.Add("Input viewport: " + e.Message); }
                foreach (var pair in saved)
                    try { AcadApp.SetSystemVariable(pair.Key, pair.Value); }
                    catch (System.Exception e) { failures.Add(pair.Key + ": " + e.Message); }
                if (failures.Count != 0)
                    throw new InvalidOperationException("Could not restore input settings: " + string.Join("; ", failures));
            }
            return result;
        }

        private static List<ObjectId> ActiveIds(Database db, Transaction tr)
        {
            var result = new List<ObjectId>();
            foreach (ObjectId id in (ViewportTable)tr.GetObject(db.ViewportTableId, OpenMode.ForRead))
                if (!id.IsErased && ((ViewportTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name.Equals("*Active", StringComparison.OrdinalIgnoreCase))
                    result.Add(id);
            return result;
        }

        private static string[] BackupNames(Database db)
        {
            using Transaction tr = db.TransactionManager.StartOpenCloseTransaction();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ObjectId id in (ViewportTable)tr.GetObject(db.ViewportTableId, OpenMode.ForRead))
            {
                string name = ((ViewportTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name;
                if (name.StartsWith(BackupPrefix, StringComparison.OrdinalIgnoreCase)) names.Add(name);
            }
            return names.ToArray();
        }

        private static void SaveConfiguration(Document d, string name)
        {
            using Transaction tr = d.Database.TransactionManager.StartTransaction();
            var table = (ViewportTable)tr.GetObject(d.Database.ViewportTableId, OpenMode.ForWrite);
            foreach (ObjectId id in ActiveIds(d.Database, tr))
            {
                var source = (ViewportTableRecord)tr.GetObject(id, OpenMode.ForRead);
                using var copy = (ViewportTableRecord)source.Clone();
                copy.Name = name;
                table.Add(copy); tr.AddNewlyCreatedDBObject(copy, true);
            }
            tr.Commit();
        }

        private static void RequirePair(Document d, Session s)
        {
            d.Editor.UpdateTiledViewportsInDatabase();
            using Transaction tr = d.Database.TransactionManager.StartOpenCloseTransaction();
            List<ObjectId> ids = ActiveIds(d.Database, tr);
            if (ids.Count != 2 || !ids.Contains(s.Plan) || !ids.Contains(s.Oblique))
                throw new InvalidOperationException("The viewport arrangement changed. Run CGVIEWEND before starting again.");
            var plan = (ViewportTableRecord)tr.GetObject(s.Plan, OpenMode.ForRead);
            var oblique = (ViewportTableRecord)tr.GetObject(s.Oblique, OpenMode.ForRead);
            if (!plan.LowerLeftCorner.IsEqualTo(Point2d.Origin) || !plan.UpperRightCorner.IsEqualTo(new Point2d(Split, 1))
                || !oblique.LowerLeftCorner.IsEqualTo(new Point2d(Split, 0)) || !oblique.UpperRightCorner.IsEqualTo(new Point2d(1, 1)))
                throw new InvalidOperationException("The viewport boundaries changed. Run CGVIEWEND before starting again.");
        }

        private static void SetActive(Document d, ObjectId id)
        {
            int number;
            using (Transaction tr = d.Database.TransactionManager.StartOpenCloseTransaction())
                number = ((ViewportTableRecord)tr.GetObject(id, OpenMode.ForRead)).Number;
            if (number <= 1) throw new InvalidOperationException("The model viewport is not available.");
            AcadApp.SetSystemVariable("CVPORT", number);
        }

        private static void Restore(Document d, string backup, Session? s)
        {
            if (!BackupNames(d.Database).Contains(backup, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Return configuration missing: " + backup + ". Use -VPORTS Single if needed.");
            using (Transaction tr = d.Database.TransactionManager.StartTransaction())
            {
                var table = (ViewportTable)tr.GetObject(d.Database.ViewportTableId, OpenMode.ForWrite);
                var saved = new List<ObjectId>();
                foreach (ObjectId id in table)
                    if (((ViewportTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name.Equals(backup, StringComparison.OrdinalIgnoreCase))
                        saved.Add(id);
                if (saved.Count == 0 || (s != null && saved.Count != s.OriginalCount))
                    throw new InvalidOperationException("The return configuration is incomplete; it was retained.");
                foreach (ObjectId id in ActiveIds(d.Database, tr))
                    tr.GetObject(id, OpenMode.ForWrite).Erase();
                foreach (ObjectId id in saved)
                {
                    var source = (ViewportTableRecord)tr.GetObject(id, OpenMode.ForRead);
                    using var copy = (ViewportTableRecord)source.Clone();
                    copy.Name = "*Active";
                    table.Add(copy); tr.AddNewlyCreatedDBObject(copy, true);
                }
                d.Editor.UpdateTiledViewportsFromDatabase();
                tr.Commit();
            }
            ObjectId active = ObjectId.Null;
            using (Transaction tr = d.Database.TransactionManager.StartOpenCloseTransaction())
            {
                List<ObjectId> ids = ActiveIds(d.Database, tr);
                if (s != null)
                {
                    if (ids.Count != s.OriginalCount)
                        throw new InvalidOperationException("Restore did not return the saved viewport count. Backup retained.");
                    active = ids.FirstOrDefault(id => ((ViewportTableRecord)tr.GetObject(id, OpenMode.ForRead))
                        .LowerLeftCorner.IsEqualTo(s.OriginalActiveCorner));
                    if (active.IsNull) throw new InvalidOperationException("Saved active viewport could not be identified. Backup retained.");
                }
            }
            if (!active.IsNull) SetActive(d, active);
            if (s != null) d.Editor.CurrentUserCoordinateSystem = s.OriginalUcs;
            d.Editor.Regen();
            using (Transaction tr = d.Database.TransactionManager.StartTransaction())
            {
                var remove = new List<ObjectId>();
                foreach (ObjectId id in (ViewportTable)tr.GetObject(d.Database.ViewportTableId, OpenMode.ForRead))
                    if (((ViewportTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name.Equals(backup, StringComparison.OrdinalIgnoreCase))
                        remove.Add(id);
                foreach (ObjectId id in remove) tr.GetObject(id, OpenMode.ForWrite).Erase();
                tr.Commit();
            }
            if (BackupNames(d.Database).Contains(backup, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Views restored, but the return configuration could not be removed. Retry CGVIEWEND.");
            Sessions.Remove(d);
            d.Editor.WriteMessage("\nOriginal viewport arrangement restored. Drawing geometry and cloud crops were not changed.");
        }

        private static void RequireNoSlice(Document d)
        {
            d.Editor.UpdateTiledViewportsInDatabase();
            using Transaction tr = d.Database.TransactionManager.StartOpenCloseTransaction();
            foreach (ObjectId id in ActiveIds(d.Database, tr))
            {
                var v = (ViewportTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (v.FrontClipEnabled || v.BackClipEnabled)
                    throw new InvalidOperationException("Finish the section/slice review and restore its view before using cloud views.");
            }
            var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(d.Database), OpenMode.ForRead);
            foreach (ObjectId id in model)
            {
                if (!id.ObjectClass.IsDerivedFrom(RXObject.GetClass(typeof(PointCloudEx)))) continue;
                var cloud = (PointCloudEx)tr.GetObject(id, OpenMode.ForRead);
                using ResultBuffer? data = cloud.GetXDataForApplication("CLV_CLOUD_GRID_V1");
                TypedValue[] values = data?.AsArray() ?? Array.Empty<TypedValue>();
                if (values.Length >= 2 && values[1].Value is string kind && kind == "REVIEW")
                    throw new InvalidOperationException("Run CGEND to finish the ground-grid review before using cloud views.");
            }
        }

        private static void ReportFocus(Editor ed, Point3d focus) => ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
            "\nShared focus WCS: E={0:F3}, N={1:F3}, Z={2:F3}.", focus.X, focus.Y, focus.Z));

        private static void Run(Action<Document> action)
        {
            Document? d = AcadApp.DocumentManager.MdiActiveDocument;
            if (d == null) return;
            try
            {
                if (!d.Database.TileMode) throw new InvalidOperationException("Use the Model tab.");
                action(d);
            }
            catch (Autodesk.AutoCAD.Runtime.Exception e) when (e.ErrorStatus == ErrorStatus.UserBreak) { }
            catch (System.Exception e) { d.Editor.WriteMessage("\nCLOUD VIEWS: " + e.Message); }
        }
    }
}
