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
    /// Prepares a separate GIS drawing, then offers a new-file save and reopen for
    /// manual MAPIMPORT. The source and shared DWT are unchanged; no SDF is opened.
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
                string profilePath;
                GisNewDrawingClone.Snapshot sourceSnapshot;
                UnitsValue sourceUnits;
                using (source.LockDocument())
                {
                    stage = "selecting the source working database";
                    HostApplicationServices.WorkingDatabase = source.Database;
                    stage = "reading the source drawing coordinate system";
                    sourceCs = GisNewDrawingMapApi.ReadProjection();
                    GisNewDrawingCoordinateSystem.RequireSourceCode(sourceCs);
                    if (!GisNewDrawingResources.TryResolveProfilePath(sourceCs, out profilePath, out string csDetail))
                        throw new InvalidOperationException(csDetail);
                    sourceUnits = source.Database.Insunits;
                    stage = "capturing source survey geometry and blocks";
                    sourceSnapshot = GisNewDrawingClone.Capture(source);
                }

                stage = "checking the shared Blank template";
                RequireReadableFile(GisNewDrawingResources.TemplatePath, "Blank template");
                using var templateGuard = new FileStream(GisNewDrawingResources.TemplatePath,
                    FileMode.Open, FileAccess.Read, FileShare.Read);
                string templateHash = Hash(templateGuard);
                source.Editor.WriteMessage($"\nGIS drawing setup: {sourceCs}; {sourceSnapshot.SelectedCount} survey object(s). " +
                    "The new drawing will be prepared for manual MAPIMPORT.");

                // Session context is required for Add/activation/CloseAndDiscard.
                source.Editor.WriteMessage("\nGIS setup: creating the new Blank (2026) drawing...");
                stage = "opening the Blank template as a new drawing";
                destination = DocumentCollectionExtension.Add(AcadApp.DocumentManager, GisNewDrawingResources.TemplatePath);
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
                    if (destination.Database.Insunits != sourceUnits)
                        throw new InvalidOperationException("The new drawing did not retain the source insertion units.");
                    stage = "assigning the source coordinate system to the new drawing";
                    GisNewDrawingMapApi.AssignProjection(sourceCs);
                    stage = "recording the initial new-drawing setup state";
                    SetSetupState(destination.Database, "INCOMPLETE", source.Name, profilePath, sourceCs);
                }
                stage = "copying and verifying survey linework and blocks";
                destination.Editor.WriteMessage("\nGIS setup: copying and verifying survey linework and blocks at their original coordinates...");
                GisNewDrawingClone.Result cloned = GisNewDrawingClone.CloneAndVerify(source, destination, sourceSnapshot);
                stage = "activating the copied destination drawing";
                AcadApp.DocumentManager.MdiActiveDocument = destination;
                stage = "selecting the copied destination working database";
                HostApplicationServices.WorkingDatabase = destination.Database;
                var displayWarnings = new List<string>();
                stage = "locking the destination drawing for setup verification";
                using (destination.LockDocument())
                {
                    stage = "verifying the destination coordinate system, units and unchanged template";
                    GisNewDrawingMapApi.VerifyProjection(sourceCs);
                    if (destination.Database.Insunits != sourceUnits)
                        throw new InvalidOperationException("The copied drawing's insertion units differ from the source.");
                    if (Hash(templateGuard) != templateHash)
                        throw new InvalidOperationException("The shared Blank template changed during setup.");
                    // Native cloning preserved the database objects, but a later
                    // REGEN alone did not make their initial graphics appear.
                    // Register only the verified destination copies, while they
                    // are transaction-resident, just as the storm prep path does.
                    try { QueueClonedGraphics(destination.Database, cloned.Ids.Values); }
                    catch (System.Exception graphicsError)
                    {
                        // In particular, do not unlock a source-derived layer to
                        // perform a display-only write. The copied data is valid.
                        displayWarnings.Add("Could not register the copied objects for initial display: " + DescribeFailure(graphicsError));
                    }
                    stage = "recording setup completion for manual import";
                    SetSetupState(destination.Database, "SETUP_READY_MANUAL_IMPORT", source.Name, profilePath, sourceCs);
                }
                complete = true;
                // Refresh only after the graphics transaction and final document
                // lock have closed. A display-only failure must retain the verified
                // unsaved drawing and its manual-import handoff.
                stage = "refreshing the prepared drawing display";
                try { destination.Editor.Regen(); destination.Editor.UpdateScreen(); }
                catch (System.Exception graphicsError)
                {
                    displayWarnings.Add("Could not refresh the prepared drawing display: " + DescribeFailure(graphicsError));
                }
                stage = "reporting the prepared unsaved drawing";
                destination.Editor.WriteMessage($"\nCLV-GIS-NEW-DRAWING setup complete: {cloned.CopiedCount} survey object(s) copied and verified at original coordinates. " +
                    $"Coordinate system: {sourceCs}.\nNew drawing: {destination.Name}. Choose a new DWG filename to save and reopen it.");
                foreach (string warning in displayWarnings)
                    destination.Editor.WriteMessage("\nDISPLAY WARNING: {0}\nThe copied geometry remains verified and this drawing is still open and unsaved.", warning);
                // Keep only stable values across closing/reopening. New documents
                // have new ObjectIds, Editors and Database instances.
                var copiedHandles = cloned.Ids.Values.Select(id => id.Handle.ToString()).ToHashSet(StringComparer.Ordinal);
                stage = "saving and reopening the prepared drawing";
                SaveAndReopenPreparedDrawing(source, ref destination, sourceUnits, sourceCs, profilePath, copiedHandles);
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
                        if (complete)
                        {
                            // destination can be null during a failed reopen. Never
                            // dereference the original destination after its close.
                            Document? live = AcadApp.DocumentManager.MdiActiveDocument
                                ?? FindLiveDocument(destination) ?? FindLiveDocument(source);
                            if (live != null) HostApplicationServices.WorkingDatabase = live.Database;
                        }
                        else HostApplicationServices.WorkingDatabase = originalWorkingDatabase;
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

        private static void SaveAndReopenPreparedDrawing(Document source, ref Document? destination,
            UnitsValue units, string coordinateSystem, string profilePath, IReadOnlySet<string> copiedHandles)
        {
            Document prepared = destination ?? throw new InvalidOperationException("The prepared drawing is unavailable.");
            string preparedName = prepared.Name;
            string sourceName = source.Name;
            string? path;
            string? stagingPath = null;
            try { path = SelectNewDrawingPath(prepared, sourceName); }
            catch (System.Exception error)
            {
                WriteDiagnostic(source, "\nThe save prompt could not be completed. The prepared drawing remains open.\n" + DescribeFailure(error));
                WriteManualImportInstructions(source, profilePath, saved: false);
                return;
            }
            if (path == null)
            {
                WriteDiagnostic(source, "\nSave/reopen canceled. The prepared drawing remains open and unsaved. " +
                    "Use SAVEAS and reopen it manually when ready.");
                WriteManualImportInstructions(source, profilePath, saved: false);
                return;
            }

            try
            {
                ActivateDocument(prepared);
                using (prepared.LockDocument())
                {
                    // Recheck after the prompt and immediately before writing.
                    // This workflow never authorizes replacing an existing DWG.
                    if (!GisNewDrawingResources.TryValidateNewDrawingPath(path, ProtectedDrawingPaths(),
                        out string checkedPath, out string detail)) throw new InvalidOperationException(detail);
                    path = checkedPath;
                    stagingPath = Path.Combine(Path.GetDirectoryName(path)!, "CLV_GIS_SAVE_" + Guid.NewGuid().ToString("N") + ".dwg");
                    if (File.Exists(stagingPath) || Directory.Exists(stagingPath))
                        throw new IOException("The temporary save path is already occupied.");
                    // Save a full copy without renaming the prepared document.
                    // Publishing with non-overwriting Move protects a target that
                    // another process creates after our last existence check.
                    prepared.Database.SaveAs(stagingPath, false, DwgVersion.Current, prepared.Database.SecurityParameters);
                }
                File.Move(stagingPath, path);
                stagingPath = null;
                // SaveAs returning is not the close gate by itself. Open the file
                // read-only through a separate database and verify basic setup data.
                VerifySavedDrawing(path, units, sourceName, coordinateSystem, profilePath, copiedHandles);
            }
            catch (System.Exception error)
            {
                WriteDiagnostic(source, "\nSave/reopen was not completed. The prepared drawing remains open; no drawing was closed. " +
                    $"Requested path: {path}\n" + DescribeFailure(error));
                if (stagingPath != null && File.Exists(stagingPath))
                    WriteDiagnostic(source, "\nA temporary save file remains at: " + stagingPath + ". The prepared drawing remains the working copy.");
                WriteManualImportInstructions(source, profilePath, saved: false);
                return;
            }

            WriteDiagnostic(source, $"\nSaved DWG verified: {path}");
            try
            {
                // Close from Session context after every document lock and
                // verification database has been released. The source is not saved.
                ActivateDocument(source);
                prepared.CloseAndDiscard();
                destination = FindLiveDocument(prepared, preparedName);
                if (destination != null)
                {
                    ActivateDocument(destination);
                    WriteDiagnostic(source, $"\nSaved at {path}, but the prepared document remained open. Close and reopen this saved file manually.");
                    WriteManualImportInstructions(source, profilePath, saved: true);
                    return;
                }
            }
            catch (System.Exception error)
            {
                destination = FindLiveDocument(prepared, preparedName) ?? FindLiveDocument(null, path);
                WriteDiagnostic(source, $"\nThe DWG is saved and verified at {path}, but automatic closing did not complete. " +
                    "If its tab is still open, close it; then OPEN this saved path. The saved file was not deleted.\n" + DescribeFailure(error));
                WriteManualImportInstructions(source, profilePath, saved: true, requiresOpen: true);
                return;
            }

            try
            {
                // Clear the old document before Open can throw. The caller's
                // finally must never touch the closed document/database.
                destination = null;
                destination = DocumentCollectionExtension.Open(AcadApp.DocumentManager, path, false);
                ActivateDocument(destination);
                using (destination.LockDocument())
                {
                    VerifyPreparedDatabase(destination.Database, units, sourceName, coordinateSystem, profilePath, copiedHandles);
                    GisNewDrawingMapApi.VerifyProjection(coordinateSystem);
                }
                WriteDiagnostic(source, $"\nPrepared GIS drawing saved and reopened: {path}\nCoordinate system: {coordinateSystem}.");
                WriteManualImportInstructions(source, profilePath, saved: true);
            }
            catch (System.Exception error)
            {
                destination = FindLiveDocument(destination, path);
                WriteDiagnostic(source, $"\nThe DWG remains saved at {path}, but automatic reopen/verification did not complete. " +
                    "OPEN that path (or select its tab if already open) and inspect it before continuing. No saved file was deleted.\n" + DescribeFailure(error));
                WriteManualImportInstructions(source, profilePath, saved: true, requiresOpen: true);
            }
        }

        private static string? SelectNewDrawingPath(Document prepared, string sourceName)
        {
            var options = new PromptSaveFileOptions("\nSave prepared GIS drawing to a NEW DWG file (Cancel keeps it open)")
            {
                DialogCaption = "Save and Reopen GIS Drawing - New File Only",
                Filter = "AutoCAD drawing (*.dwg)|*.dwg",
                InitialFileName = Path.GetFileNameWithoutExtension(sourceName) + "_GIS.dwg"
            };
            string? folder = Path.GetDirectoryName(sourceName);
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder)) options.InitialDirectory = folder;
            while (true)
            {
                PromptFileNameResult choice = prepared.Editor.GetFileNameForSave(options);
                if (choice.Status != PromptStatus.OK) return null;
                if (GisNewDrawingResources.TryValidateNewDrawingPath(choice.StringResult, ProtectedDrawingPaths(),
                    out string path, out string detail)) return path;
                prepared.Editor.WriteMessage("\n{0}\nChoose a different NEW filename, or Cancel to keep this drawing open.", detail);
            }
        }

        private static IReadOnlyList<string> ProtectedDrawingPaths()
        {
            var paths = new List<string> { GisNewDrawingResources.TemplatePath };
            foreach (Document document in AcadApp.DocumentManager)
            {
                paths.Add(document.Name);
                paths.Add(document.Database.Filename);
            }
            return paths;
        }

        private static Document? FindLiveDocument(Document? preferred, string? savedPath = null)
        {
            foreach (Document document in AcadApp.DocumentManager)
            {
                if (ReferenceEquals(document, preferred)) return document;
                if (savedPath != null && string.Equals(document.Name, savedPath, StringComparison.OrdinalIgnoreCase)) return document;
                if (savedPath != null && Path.IsPathFullyQualified(savedPath) && Path.IsPathFullyQualified(document.Name) &&
                    string.Equals(Path.GetFullPath(document.Name), savedPath, StringComparison.OrdinalIgnoreCase)) return document;
            }
            return null;
        }

        private static void ActivateDocument(Document document)
        {
            AcadApp.DocumentManager.MdiActiveDocument = document;
            HostApplicationServices.WorkingDatabase = document.Database;
            if (AcadApp.DocumentManager.MdiActiveDocument != document || HostApplicationServices.WorkingDatabase != document.Database)
                throw new InvalidOperationException("The requested drawing could not become active/current.");
        }

        private static void VerifySavedDrawing(string path, UnitsValue units, string sourceName,
            string coordinateSystem, string profilePath, IReadOnlySet<string> copiedHandles)
        {
            RequireReadableFile(path, "Saved DWG");
            using var saved = new Database(false, true);
            saved.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
            saved.CloseInput(true);
            VerifyPreparedDatabase(saved, units, sourceName, coordinateSystem, profilePath, copiedHandles);
        }

        private static void VerifyPreparedDatabase(Database database, UnitsValue units, string sourceName,
            string coordinateSystem, string profilePath, IReadOnlySet<string> copiedHandles)
        {
            if (database.Insunits != units) throw new InvalidDataException("Saved drawing insertion units differ from the verified setup.");
            using Transaction transaction = database.TransactionManager.StartOpenCloseTransaction();
            var blocks = (BlockTable)transaction.GetObject(database.BlockTableId, OpenMode.ForRead);
            var model = (BlockTableRecord)transaction.GetObject(blocks[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            var handles = model.Cast<ObjectId>().Where(id => !id.IsErased).Select(id => id.Handle.ToString()).ToHashSet(StringComparer.Ordinal);
            if (!handles.SetEquals(copiedHandles)) throw new InvalidDataException("Saved drawing model-space objects differ from the verified setup.");
            var dictionary = (DBDictionary)transaction.GetObject(database.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!dictionary.Contains("CLV_GIS_NEW_DRAWING_V2")) throw new InvalidDataException("Saved drawing is missing its setup marker.");
            var record = transaction.GetObject(dictionary.GetAt("CLV_GIS_NEW_DRAWING_V2"), OpenMode.ForRead) as Xrecord
                ?? throw new InvalidDataException("Saved drawing setup marker has an unexpected type.");
            using ResultBuffer? data = record.Data;
            TypedValue[] values = data?.AsArray() ?? throw new InvalidDataException("Saved drawing setup marker is empty.");
            if (values.Length != 5 || values.Any(value => value.TypeCode != (int)DxfCode.Text) ||
                !Equals(values[0].Value, "SETUP_READY_MANUAL_IMPORT") || !Equals(values[1].Value, sourceName) ||
                !Equals(values[2].Value, profilePath) || !Equals(values[3].Value, coordinateSystem))
                throw new InvalidDataException("Saved drawing setup marker differs from the verified setup.");
            transaction.Commit();
        }

        private static void WriteManualImportInstructions(Document source, string profilePath, bool saved, bool requiresOpen = false)
        {
            WriteDiagnostic(source, (requiresOpen ? "\nAfter opening the saved GIS drawing, " : "\nNext, in the prepared GIS drawing, ") +
                "run MAPIMPORT, select the exported SDF, and load this matching profile:\n" + profilePath +
                (saved ? "\nSave the drawing again after the manual import." : "\nUse SAVEAS when ready; the source drawing is unchanged.") +
                " Run the existing GIS preparation command after importing the network.");
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

        private static void QueueClonedGraphics(Database database, IEnumerable<ObjectId> copiedIds)
        {
            using Transaction graphics = database.TransactionManager.StartTransaction();
            foreach (ObjectId id in copiedIds.Distinct())
            {
                if (id.IsNull || !id.IsValid || id.IsErased || id.Database != database)
                    throw new InvalidOperationException("A verified clone is unavailable for destination graphics registration.");
                var entity = graphics.GetObject(id, OpenMode.ForWrite, false) as Entity
                    ?? throw new InvalidOperationException("A verified clone is not an Entity during graphics registration.");
                entity.RecordGraphicsModified(true);
            }
            graphics.TransactionManager.QueueForGraphicsFlush();
            graphics.Commit();
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
                throw new InvalidOperationException("Blank template contains model-space objects. No source geometry was copied.");
        }

        private static void SetSetupState(Database db, string state, string source, string profile, string cs)
        {
            using Transaction tr = db.TransactionManager.StartTransaction();
            var dictionary = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
            const string key = "CLV_GIS_NEW_DRAWING_V2";
            Xrecord record;
            if (dictionary.Contains(key)) record = (Xrecord)tr.GetObject(dictionary.GetAt(key), OpenMode.ForWrite);
            else
            {
                record = new Xrecord();
                dictionary.SetAt(key, record);
                tr.AddNewlyCreatedDBObject(record, true);
            }
            using var data = new ResultBuffer(new TypedValue((int)DxfCode.Text, state),
                new TypedValue((int)DxfCode.Text, source),
                new TypedValue((int)DxfCode.Text, profile), new TypedValue((int)DxfCode.Text, cs),
                new TypedValue((int)DxfCode.Text, DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture)));
            record.Data = data;
            tr.Commit();
        }
    }
}
