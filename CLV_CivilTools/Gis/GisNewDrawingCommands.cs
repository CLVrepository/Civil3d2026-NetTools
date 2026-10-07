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
            Database originalWorkingDatabase = HostApplicationServices.WorkingDatabase;
            _running = true;
            try
            {
                string sourceCs;
                GisNewDrawingClone.Snapshot sourceSnapshot;
                UnitsValue sourceUnits;
                using (source.LockDocument())
                {
                    HostApplicationServices.WorkingDatabase = source.Database;
                    sourceCs = GisNewDrawingMapApi.ReadProjection();
                    if (!GisNewDrawingProfile.TryResolveProfilePath(sourceCs, out _, out string csDetail))
                        throw new InvalidOperationException(csDetail);
                    sourceUnits = source.Database.Insunits;
                    sourceSnapshot = GisNewDrawingClone.Capture(source);
                }

                string? sdfPath = SelectSdf(source);
                if (sdfPath == null) return;
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
                if (!GisNewDrawingProfile.TryLoad(profilePath, sourceCs, out GisNewDrawingProfile? profile, out string profileDetail) || profile == null)
                    throw new InvalidOperationException(profileDetail);
                source.Editor.WriteMessage("\nGIS setup: reading SDF schema, coordinate system and network features (read-only)...");
                if (!GisNewDrawingSdf.TryRead(sdfPath, profile, out GisNewDrawingSdfSnapshot? sdf, out string sdfDetail) || sdf == null)
                    throw new InvalidOperationException(sdfDetail);

                source.Editor.WriteMessage($"\nGIS drawing preflight: {sourceCs}; {sourceSnapshot.SelectedCount} survey object(s), " +
                    $"{sdf.Pipes.Count} SDF pipe(s), {sdf.Structures.Count} SDF structure(s).\nProfile: {profilePath}");
                foreach (string diagnostic in profile.Diagnostics)
                    source.Editor.WriteMessage("\nProfile note: " + diagnostic);

                // Session context is required for Add/activation/CloseAndDiscard.
                source.Editor.WriteMessage("\nGIS setup: preflight passed; creating the new Blank (2026) drawing...");
                destination = DocumentCollectionExtension.Add(AcadApp.DocumentManager, GisNewDrawingProfile.TemplatePath);
                AcadApp.DocumentManager.MdiActiveDocument = destination;
                HostApplicationServices.WorkingDatabase = destination.Database;
                using (destination.LockDocument())
                {
                    RequireEmptyModelSpace(destination.Database);
                    // WblockCloneObjects does not scale coordinates. Match insertion-unit
                    // metadata to the source as well, including an explicit Unitless value.
                    destination.Database.Insunits = sourceUnits;
                    GisNewDrawingMapApi.AssignProjection(sourceCs);
                    SetSetupState(destination.Database, "INCOMPLETE", source.Name, sdfPath, profilePath, sourceCs);
                }
                destination.Editor.WriteMessage("\nGIS setup: copying and verifying survey linework and blocks at their original coordinates...");
                GisNewDrawingClone.Result cloned = GisNewDrawingClone.CloneAndVerify(source, destination, sourceSnapshot);
                AcadApp.DocumentManager.MdiActiveDocument = destination;
                HostApplicationServices.WorkingDatabase = destination.Database;
                using (destination.LockDocument())
                {
                    GisNewDrawingMapApi.VerifyProjection(sourceCs);
                    if (!GisNewDrawingSdf.TryVerifyUnchanged(sdf, out string changedDetail))
                        throw new InvalidOperationException(changedDetail);
                    if (Hash(profileGuard) != profileHash)
                        throw new InvalidOperationException("The import profile changed after preflight.");

                    destination.Editor.WriteMessage("\nGIS setup: loading the selected profile and importing/verifying Pipes and Structures...");
                    GisNewDrawingMapApi.ImportSummary imported = GisNewDrawingMapApi.ImportAndVerify(
                        destination, sdf, profilePath, profile);
                    if (!GisNewDrawingSdf.TryVerifyUnchanged(sdf, out changedDetail))
                        throw new InvalidOperationException(changedDetail);
                    if (Hash(profileGuard) != profileHash)
                        throw new InvalidOperationException("The shared import profile changed during import.");
                    SetSetupState(destination.Database, "VERIFIED", source.Name, sdfPath, profilePath, sourceCs);
                    destination.Editor.Regen();
                    complete = true;
                    destination.Editor.WriteMessage($"\nCLV-GIS-NEW-DRAWING verified: {cloned.CopiedCount} survey object(s) copied at original coordinates; " +
                        $"{imported.Pipes} pipe(s) and {imported.Structures} structure point(s) imported with verified Object Data. " +
                        $"Coordinate system: {sourceCs}.\nThe new drawing is unsaved. Review/save it, then run the existing GIS preparation command.");
                }
            }
            catch (System.Exception ex)
            {
                WriteDiagnostic(source, "\nCLV-GIS-NEW-DRAWING stopped: " + GisNewDrawingMapApi.ErrorMessage(ex));
            }
            finally
            {
                if (!complete)
                {
                    try
                    {
                        AcadApp.DocumentManager.MdiActiveDocument = source;
                        HostApplicationServices.WorkingDatabase = source.Database;
                        if (destination != null)
                        {
                            // Only this command's freshly created drawing is discarded.
                            destination.CloseAndDiscard();
                            WriteDiagnostic(source, "\nIncomplete new GIS drawing discarded. Source drawing and input files were not changed.");
                        }
                    }
                    catch (System.Exception closeError)
                    {
                        WriteDiagnostic(source, "\nThe incomplete new drawing could not be discarded: " + closeError.Message +
                            ". It is not verified; close it without saving. Do not run GIS preparation on it.");
                    }
                }
                try
                {
                    HostApplicationServices.WorkingDatabase = complete && destination != null ? destination.Database : originalWorkingDatabase;
                }
                catch (System.Exception restoreError)
                {
                    WriteDiagnostic(source, "\nCould not restore the working database: " + restoreError.Message);
                }
                finally { _running = false; }
            }
        }

        private static void WriteDiagnostic(Document source, string message)
        {
            try { source.Editor.WriteMessage(message); }
            catch
            {
                try { AcadApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(message); }
                catch { /* A document may have closed during host error recovery. */ }
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
