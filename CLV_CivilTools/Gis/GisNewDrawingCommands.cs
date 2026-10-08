using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Gis
{
    /// <summary>
    /// Makes a separate, unsaved GIS work drawing. The source drawing, source SDF,
    /// shared DWT and IPFs are read-only inputs. No downstream cleanup is queued.
    /// </summary>
    public static class GisNewDrawingCommands
    {
        private static bool _running;

        [CommandMethod("CLV-GIS-NEW-DRAWING", CommandFlags.Session)]
        public static void CreateGisDrawing()
        {
            if (_running) return;
            Document? source = AcadApp.DocumentManager.MdiActiveDocument;
            if (source == null) return;
            Document? destination = null;
            bool complete = false;
            string stage = "locking the source drawing";
            System.Exception? failure = null;
            string? failedStage = null;
            Database originalWorkingDatabase = HostApplicationServices.WorkingDatabase;
            _running = true;
            try
            {
                string sourceCs;
                GisNewDrawingClone.Snapshot sourceSnapshot;
                UnitsValue sourceUnits;
                using (source.LockDocument())
                {
                    stage = "selecting the source working database";
                    HostApplicationServices.WorkingDatabase = source.Database;
                    stage = "reading the source drawing coordinate system";
                    sourceCs = GisNewDrawingMapApi.ReadProjection();
                    GisNewDrawingCoordinateSystem.RequireSourceCode(sourceCs);
                    if (!GisNewDrawingProfile.TryResolveProfilePath(sourceCs, out _, out string csDetail))
                        throw new InvalidOperationException(csDetail);
                    sourceUnits = source.Database.Insunits;
                    stage = "capturing source survey geometry and blocks";
                    sourceSnapshot = GisNewDrawingClone.Capture(source);
                }

                stage = "selecting the exported SDF";
                string? sdfPath = SelectSdf(source);
                if (sdfPath == null) return;
                stage = "checking the SDF, template and import profile files";
                source.Editor.WriteMessage("\nGIS setup: checking the selected SDF, shared template and matching import profile...");
                if (!GisNewDrawingProfile.TryResolveProfilePath(sourceCs, out string profilePath, out string pathDetail))
                    throw new InvalidOperationException(pathDetail);
                RequireReadableFile(GisNewDrawingProfile.TemplatePath, "Blank template");
                RequireReadableFile(profilePath, "Import profile");
                RequireReadableFile(sdfPath, "Exported SDF");
                // Read-only handles prevent either input being edited between preflight
                // and import on Windows. Native readers may open additional read handles.
                using var sdfGuard = new FileStream(sdfPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var profileGuard = new FileStream(profilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var templateGuard = new FileStream(GisNewDrawingProfile.TemplatePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                string profileHash = Hash(profileGuard);
                stage = "validating the matching import profile";
                if (!GisNewDrawingProfile.TryLoad(profilePath, sourceCs, out GisNewDrawingProfile? profile, out string profileDetail) || profile == null)
                    throw new InvalidOperationException(profileDetail);
                stage = "reading SDF schema and raw network features";
                source.Editor.WriteMessage("\nGIS setup: reading SDF schema and raw network features (read-only)...");
                if (!GisNewDrawingSdf.TryRead(sdfPath, profile, out GisNewDrawingSdfSnapshot? sdf, out string sdfDetail) || sdf == null)
                    throw new InvalidOperationException(sdfDetail);

                source.Editor.WriteMessage($"\nGIS drawing preflight: {sourceCs}; {sourceSnapshot.SelectedCount} survey object(s), " +
                    $"{sdf.Pipes.Count} SDF pipe(s), {sdf.Structures.Count} SDF structure(s).\nProfile: {profilePath}\n" +
                    "The source drawing sets the import coordinate system; SDF coordinate-system labels are not used.");
                foreach (string diagnostic in profile.Diagnostics)
                    source.Editor.WriteMessage("\nProfile note: " + diagnostic);

                // Session context is required for Add/activation/CloseAndDiscard.
                source.Editor.WriteMessage("\nGIS setup: preflight passed; creating the new Blank (2026) drawing...");
                stage = "opening the Blank template as a new drawing";
                destination = DocumentCollectionExtension.Add(AcadApp.DocumentManager, GisNewDrawingProfile.TemplatePath);
                stage = "activating the new drawing";
                AcadApp.DocumentManager.MdiActiveDocument = destination;
                stage = "selecting the new drawing working database";
                HostApplicationServices.WorkingDatabase = destination.Database;
                stage = "locking the new drawing";
                using (destination.LockDocument())
                {
                    stage = "checking the Blank template model space";
                    RequireEmptyModelSpace(destination.Database);
                    // WblockCloneObjects does not scale coordinates. Match insertion-unit
                    // metadata to the source as well, including an explicit Unitless value.
                    stage = "matching the new drawing insertion units";
                    destination.Database.Insunits = sourceUnits;
                    stage = "assigning the source coordinate system to the new drawing";
                    GisNewDrawingMapApi.AssignProjection(sourceCs);
                    stage = "recording the initial new-drawing setup state";
                    SetSetupState(destination.Database, "INCOMPLETE", source.Name, sdfPath, profilePath, sourceCs);
                }
                stage = "copying and verifying survey linework and blocks";
                destination.Editor.WriteMessage("\nGIS setup: copying and verifying survey linework and blocks at their original coordinates...");
                GisNewDrawingClone.Result cloned = GisNewDrawingClone.CloneAndVerify(source, destination, sourceSnapshot);
                stage = "activating the copied destination drawing";
                AcadApp.DocumentManager.MdiActiveDocument = destination;
                stage = "selecting the copied destination working database";
                HostApplicationServices.WorkingDatabase = destination.Database;
                stage = "locking the destination drawing for import";
                using (destination.LockDocument())
                {
                    stage = "verifying the destination coordinate system and unchanged inputs";
                    GisNewDrawingMapApi.VerifyProjection(sourceCs);
                    if (!GisNewDrawingSdf.TryVerifyUnchanged(sdf, out string changedDetail))
                        throw new InvalidOperationException(changedDetail);
                    if (Hash(profileGuard) != profileHash)
                        throw new InvalidOperationException("The import profile changed after preflight.");

                    stage = "loading the profile and importing/verifying Pipes and Structures";
                    destination.Editor.WriteMessage("\nGIS setup: loading the selected profile and importing/verifying Pipes and Structures...");
                    GisNewDrawingMapApi.ImportSummary imported = GisNewDrawingMapApi.ImportAndVerify(
                        destination, sdf, profilePath, profile);
                    stage = "checking source files after import";
                    if (!GisNewDrawingSdf.TryVerifyUnchanged(sdf, out changedDetail))
                        throw new InvalidOperationException(changedDetail);
                    if (Hash(profileGuard) != profileHash)
                        throw new InvalidOperationException("The shared import profile changed during import.");
                    stage = "recording verified setup completion";
                    SetSetupState(destination.Database, "VERIFIED", source.Name, sdfPath, profilePath, sourceCs);
                    stage = "regenerating the verified drawing";
                    destination.Editor.Regen();
                    complete = true;
                    stage = "reporting the verified unsaved drawing";
                    destination.Editor.WriteMessage($"\nCLV-GIS-NEW-DRAWING verified: {cloned.CopiedCount} survey object(s) copied at original coordinates; " +
                        $"{imported.Pipes} pipe(s) and {imported.Structures} structure point(s) imported with verified Object Data. " +
                        $"Coordinate system: {sourceCs}.\nNew drawing: {destination.Name}. No output file has been saved. " +
                        "Use SAVEAS to choose its name and folder, then run the existing GIS preparation command.");
                }
            }
            catch (System.Exception ex)
            {
                // Keep the primary cause until the temporary document has been
                // closed/restored. A write through an inactive source Editor can
                // succeed without remaining visible after that document switch.
                failure = ex;
                failedStage = stage;
            }
            finally
            {
                // Reset re-entry protection even if host cleanup or formatting fails.
                try
                {
                    var cleanupNotes = new List<string>();
                    if (!complete)
                    {
                        bool sourceActive = false;
                        bool sourceDatabaseCurrent = false;
                        try
                        {
                            AcadApp.DocumentManager.MdiActiveDocument = source;
                            sourceActive = AcadApp.DocumentManager.MdiActiveDocument == source;
                            if (!sourceActive) throw new InvalidOperationException("The source did not become the active document.");
                        }
                        catch (System.Exception activationError)
                        {
                            cleanupNotes.Add("\nCould not reactivate the source drawing: " + activationError);
                        }
                        if (sourceActive)
                        {
                            try
                            {
                                HostApplicationServices.WorkingDatabase = source.Database;
                                sourceDatabaseCurrent = HostApplicationServices.WorkingDatabase == source.Database;
                                if (!sourceDatabaseCurrent) throw new InvalidOperationException("The source did not become the working database.");
                            }
                            catch (System.Exception databaseError)
                            {
                                cleanupNotes.Add("\nCould not select the source working database: " + databaseError);
                            }
                        }
                        if (destination != null)
                        {
                            if (sourceActive && sourceDatabaseCurrent)
                            {
                                try
                                {
                                    // Only this command's freshly created drawing is discarded.
                                    destination.CloseAndDiscard();
                                    cleanupNotes.Add("\nIncomplete new GIS drawing discarded. No output file was saved; the source drawing and input files were not changed.");
                                }
                                catch (System.Exception closeError)
                                {
                                    cleanupNotes.Add("\nThe incomplete new drawing could not be discarded: " + closeError +
                                        ". It is not verified; close it without saving. Do not run GIS preparation on it.");
                                }
                            }
                            else cleanupNotes.Add("\nThe incomplete new drawing remains open because the source context could not be restored. " +
                                "Close it without saving; do not run GIS preparation on it.");
                        }
                    }
                    try
                    {
                        HostApplicationServices.WorkingDatabase = complete && destination != null ? destination.Database : originalWorkingDatabase;
                    }
                    catch (System.Exception restoreError)
                    {
                        cleanupNotes.Add("\nCould not restore the working database: " + restoreError);
                    }
                    // Report after all cleanup/restoration. On the normal failure
                    // path the source is active, so this survives destination disposal.
                    // Secondary cleanup errors supplement the original cause.
                    if (failure != null)
                        WriteDiagnostic(source, $"\nCLV-GIS-NEW-DRAWING stopped while {failedStage}:\n" + DescribeFailure(failure));
                    foreach (string note in cleanupNotes) WriteDiagnostic(source, note);
                }
                finally { _running = false; }
            }
        }

        private static string DescribeFailure(System.Exception error)
        {
            // ToString retains exception types, complete inner details and stack
            // traces. Preserve native AutoCAD status separately when supplied.
            string detail = error.ToString();
            for (System.Exception? current = error; current != null; current = current.InnerException)
                if (current is Autodesk.AutoCAD.Runtime.Exception native)
                    detail += "\nNative AutoCAD ErrorStatus: " + native.ErrorStatus;
            return detail;
        }

        private static void WriteDiagnostic(Document source, string message)
        {
            // Source is active after normal failure cleanup. If restoration itself
            // failed, report in the remaining active document instead of silently
            // writing to an inactive editor. No document is closed after this flush.
            Document? active = null;
            try { active = AcadApp.DocumentManager.MdiActiveDocument; }
            catch { /* Try the captured source if the document manager is unavailable. */ }
            try { (active ?? source).Editor.WriteMessage("{0}", message); }
            catch
            {
                if (active != null && active != source)
                {
                    try { source.Editor.WriteMessage("{0}", message); }
                    catch { /* A document may have closed during host error recovery. */ }
                }
            }
        }

        private static string? SelectSdf(Document source)
        {
            var options = new PromptOpenFileOptions("\nSelect the exported pipe-network SDF")
            {
                Filter = "Autodesk SDF (*.sdf)|*.sdf",
                DialogCaption = "Create GIS Drawing - Exported SDF"
            };
            string folder = Path.GetDirectoryName(source.Name) ?? string.Empty;
            if (Directory.Exists(Path.Combine(folder, "GIS"))) folder = Path.Combine(folder, "GIS");
            if (Directory.Exists(folder)) options.InitialDirectory = folder;
            PromptFileNameResult result = source.Editor.GetFileNameForOpen(options);
            return result.Status == PromptStatus.OK ? Path.GetFullPath(result.StringResult) : null;
        }

        private static void RequireReadableFile(string path, string description)
        {
            if (!File.Exists(path)) throw new FileNotFoundException(description + " is unavailable: " + path, path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length == 0) throw new InvalidDataException(description + " is empty: " + path);
        }

        private static string Hash(Stream stream)
        {
            stream.Position = 0;
            string hash = Convert.ToHexString(SHA256.HashData(stream));
            stream.Position = 0;
            return hash;
        }

        private static void RequireEmptyModelSpace(Database db)
        {
            using Transaction tr = db.TransactionManager.StartOpenCloseTransaction();
            var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var model = (BlockTableRecord)tr.GetObject(blocks[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            if (model.Cast<ObjectId>().Any(id => !id.IsErased))
                throw new InvalidOperationException("Blank template contains model-space objects. No source geometry or SDF was imported.");
        }

        private static void SetSetupState(Database db, string state, string source, string sdf, string profile, string cs)
        {
            using Transaction tr = db.TransactionManager.StartTransaction();
            var dictionary = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
            const string key = "CLV_GIS_NEW_DRAWING_V1";
            Xrecord record;
            if (dictionary.Contains(key)) record = (Xrecord)tr.GetObject(dictionary.GetAt(key), OpenMode.ForWrite);
            else
            {
                record = new Xrecord();
                dictionary.SetAt(key, record);
                tr.AddNewlyCreatedDBObject(record, true);
            }
            using var data = new ResultBuffer(new TypedValue((int)DxfCode.Text, state),
                new TypedValue((int)DxfCode.Text, source), new TypedValue((int)DxfCode.Text, sdf),
                new TypedValue((int)DxfCode.Text, profile), new TypedValue((int)DxfCode.Text, cs),
                new TypedValue((int)DxfCode.Text, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)));
            record.Data = data;
            tr.Commit();
        }
    }
}
