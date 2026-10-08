using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools.Gis
{
    /// <summary>
    /// Makes a separate, unsaved GIS work drawing for manual MAPIMPORT. The source
    /// drawing and shared DWT are unchanged. No SDF is opened or import queued.
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
                stage = "locking the destination drawing for setup verification";
                using (destination.LockDocument())
                {
                    stage = "verifying the destination coordinate system, units and unchanged template";
                    GisNewDrawingMapApi.VerifyProjection(sourceCs);
                    if (destination.Database.Insunits != sourceUnits)
                        throw new InvalidOperationException("The copied drawing's insertion units differ from the source.");
                    if (Hash(templateGuard) != templateHash)
                        throw new InvalidOperationException("The shared Blank template changed during setup.");
                    stage = "recording setup completion for manual import";
                    SetSetupState(destination.Database, "SETUP_READY_MANUAL_IMPORT", source.Name, profilePath, sourceCs);
                    stage = "regenerating the prepared drawing";
                    destination.Editor.Regen();
                    complete = true;
                    stage = "reporting the prepared unsaved drawing";
                    destination.Editor.WriteMessage($"\nCLV-GIS-NEW-DRAWING setup complete: {cloned.CopiedCount} survey object(s) copied and verified at original coordinates. " +
                        $"Coordinate system: {sourceCs}.\nNew drawing: {destination.Name}. It is active and unsaved.\n" +
                        "Next: run MAPIMPORT, select your exported SDF, and load this matching profile:\n" +
                        profilePath + "\nComplete MAPIMPORT manually, then use SAVEAS to choose the drawing name and folder. " +
                        "Run the existing GIS preparation command after importing the network.");
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
