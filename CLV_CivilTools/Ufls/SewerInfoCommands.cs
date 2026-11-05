using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using CivilApp = Autodesk.Civil.ApplicationServices.CivilApplication;
using Pipe = Autodesk.Civil.DatabaseServices.Pipe;
using Structure = Autodesk.Civil.DatabaseServices.Structure;
using Network = Autodesk.Civil.DatabaseServices.Network;

namespace CLV_CivilTools.Ufls.SewerInfo
{
    public static class SewerInfoCommands
    {
        private sealed class CadOwner : IWin32Window { public IntPtr Handle => AcadApp.MainWindow.Handle; }
        [CommandMethod("SSMHINFO", CommandFlags.Modal)]
        public static void Manager() => Run(d =>
        {
            {
                SewerCatalog? c = ReadCatalog(d.Database);
                using var form = new SewerCatalogForm(c);
                if (AcadApp.ShowModalDialog(form) != DialogResult.OK || form.ActionName == "close") return;
                // Each action closes its database transaction before the manager reopens.
                try
                {
                    switch (form.ActionName)
                    {
                        case "import": Import(d); break;
                        case "add": Edit(d, null); break;
                        case "edit": Edit(d, form.SelectedName); break;
                        case "place": Place(d, form.SelectedName); break;
                        case "apply": Apply(d); break;
                        case "refresh": Refresh(d); break;
                        case "export": Export(d); break;
                    }
                }
                catch (System.Exception e) { d.Editor.WriteMessage("\nSEWER INFO: " + e.Message); }
            }
        });
        [CommandMethod("SSMHIMPORT", CommandFlags.Modal)] public static void ImportCommand() => Run(Import);
        [CommandMethod("SSMHEDIT", CommandFlags.Modal)] public static void EditCommand() => Run(d => { SewerManhole? m = PickRecord(d); if (m != null) Edit(d, m.Name); });
        [CommandMethod("SSMHADD", CommandFlags.Modal)] public static void AddCommand() => Run(d => Edit(d, null));
        [CommandMethod("SSMHINSERT", CommandFlags.Modal)] public static void PlaceCommand() => Run(d => Place(d, PickRecord(d)?.Name));
        [CommandMethod("SSMHAPPLY", CommandFlags.Modal)] public static void ApplyCommand() => Run(Apply);
        [CommandMethod("SSMHREFRESH", CommandFlags.Modal)] public static void RefreshCommand() => Run(Refresh);
        [CommandMethod("SSMHEXPORT", CommandFlags.Modal)] public static void ExportCommand() => Run(Export);

        private static void Run(Action<Document> action)
        {
            Document? d = AcadApp.DocumentManager.MdiActiveDocument; if (d == null) return;
            try { if (!d.Database.TileMode) throw new InvalidOperationException("Use the Model tab."); action(d); }
            catch (Autodesk.AutoCAD.Runtime.Exception e) when (e.ErrorStatus == ErrorStatus.UserBreak) { }
            catch (System.Exception e) { d.Editor.WriteMessage("\nSEWER INFO: " + e.Message); }
        }
        private static SewerCatalog? ReadCatalog(Database db)
        { using Transaction tr = db.TransactionManager.StartTransaction(); return SewerInfoDrawing.Catalog(db, tr); }
        private static void SaveCatalog(Database db, SewerCatalog c)
        { using Transaction tr = db.TransactionManager.StartTransaction(); SewerInfoDrawing.Save(db, tr, c); tr.Commit(); }
        private static SewerManhole? PickRecord(Document d)
        {
            SewerCatalog c = ReadCatalog(d.Database) ?? throw new InvalidOperationException("Import a catalog or use SSMHADD first.");
            PromptIntegerResult r = d.Editor.GetInteger(new PromptIntegerOptions("\nManhole number: ") { AllowNegative = false, AllowZero = false });
            if (r.Status != PromptStatus.OK) return null;
            return c.Manholes.SingleOrDefault(m => m.Number == r.Value) ?? throw new InvalidOperationException("Manhole number is not in this drawing's catalog.");
        }
        private static bool Confirm(string title, string text, string acknowledgement)
        { using var f = new SewerConfirmForm(title, text, acknowledgement); return AcadApp.ShowModalDialog(f) == DialogResult.OK; }
        private static void Import(Document d)
        {
            using var file = new System.Windows.Forms.OpenFileDialog { Title = "Import sewer manhole catalog", Filter = "JSON catalogs (*.json)|*.json", CheckFileExists = true };
            if (file.ShowDialog(new CadOwner()) != DialogResult.OK) return;
            if (new FileInfo(file.FileName).Length > SewerInfoModel.MaxJsonLength) throw new InvalidOperationException("Catalog exceeds 8 MiB.");
            SewerCatalog incoming = SewerInfoModel.Parse(File.ReadAllText(file.FileName, Encoding.UTF8));
            SewerCatalog? existing = ReadCatalog(d.Database);
            if (existing != null && existing.ProjectNumber != incoming.ProjectNumber)
                throw new InvalidOperationException("This drawing already has project " + existing.ProjectNumber + ". Use a separate drawing for another project's catalog.");
            SewerCatalog merged = SewerInfoModel.Merge(existing, incoming);
            int replacing = existing == null ? 0 : incoming.Manholes.Count(m => existing.Manholes.Any(x => x.Number == m.Number));
            SewerInfoModel.Validate(merged);
            SewerCatalog importMetadata = SewerInfoModel.Copy(incoming); importMetadata.Manholes.Clear();
            string summary = "Project: " + incoming.ProjectNumber + "\r\nIncoming records: " + incoming.Manholes.Count + "\r\nReplace matching records: " + replacing
                + "\r\nRecords after merge: " + merged.Manholes.Count + "\r\nManual records absent from this import are retained.\r\nPlaced blocks keep their snapshots until refreshed.\r\n\r\n"
                + string.Join("\r\n", incoming.Manholes.OrderBy(m => m.Number).Select(m => m.Name + " | RIM " + SewerInfoModel.Elevation(m.RimElevation) + " | STRUCT " + SewerInfoModel.Number(m.StructureDiameterInches) + " in | " + m.Connections.Count + " pipes"))
                + "\r\n\r\nPROJECT METADATA / DATUM\r\n" + SewerInfoModel.Serialize(importMetadata);
            if (!Confirm("IMPORT SEWER CATALOG", summary, "Import these records and replace the matching catalog entries shown above.")) return;
            SaveCatalog(d.Database, merged); d.Editor.WriteMessage("\nImported catalog into DWG: " + merged.Manholes.Count + " manholes. Placed blocks were not changed.");
        }
        private static void Edit(Document d, string? name)
        {
            SewerCatalog? c = ReadCatalog(d.Database);
            SewerManhole? m = name == null ? null : c?.Manholes.SingleOrDefault(x => x.Name == name);
            using var form = new SewerEditForm(c, m);
            if (AcadApp.ShowModalDialog(form) != DialogResult.OK || form.ResultCatalog == null) return;
            SaveCatalog(d.Database, form.ResultCatalog); d.Editor.WriteMessage("\nSaved manhole catalog. Use SSMHREFRESH for any already placed copies of edited records.");
        }
        private static void Export(Document d)
        {
            SewerCatalog c = ReadCatalog(d.Database) ?? throw new InvalidOperationException("No catalog in this drawing.");
            using var file = new System.Windows.Forms.SaveFileDialog { Title = "Export sewer catalog", Filter = "JSON catalog (*.json)|*.json", FileName = c.ProjectNumber + "-sewer-catalog.json", OverwritePrompt = true };
            if (file.ShowDialog(new CadOwner()) == DialogResult.OK) { File.WriteAllText(file.FileName, SewerInfoModel.Serialize(c), new UTF8Encoding(false)); d.Editor.WriteMessage("\nExported sewer catalog JSON."); }
        }
        private static void Place(Document d, string? name)
        {
            if (name == null) return;
            SewerCatalog c = ReadCatalog(d.Database) ?? throw new InvalidOperationException("No catalog in this drawing.");
            SewerManhole m = c.Manholes.Single(x => x.Name == name);
            PromptPointResult p = d.Editor.GetPoint("\nPick manhole info marker location (plan marker only): "); if (p.Status != PromptStatus.OK) return;
            Point3d wcs = p.Value.TransformBy(d.Editor.CurrentUserCoordinateSystem);
            PromptDoubleResult h = d.Editor.GetDouble(new PromptDoubleOptions("\nInformation text height in drawing units: ") { DefaultValue = 1, UseDefaultValue = true, AllowZero = false, AllowNegative = false });
            if (h.Status != PromptStatus.OK) return;
            if (!double.IsFinite(h.Value) || h.Value < 0.001 || h.Value > 10000) throw new InvalidOperationException("Text height must be 0.001 to 10000 drawing units.");
            using Transaction tr = d.Database.TransactionManager.StartTransaction();
            BlockReference b = SewerInfoDrawing.Place(d.Database, tr, c, m, new Point3d(wcs.X, wcs.Y, 0), h.Value);
            ObjectId id = b.ObjectId; tr.Commit(); d.Editor.SetImpliedSelection(new[] { id }); d.Editor.Regen();
            d.Editor.WriteMessage("\nPlaced " + m.Name + " with a stored snapshot. Marker Z is 0; rim/inverts are stored attributes, not marker elevation.");
        }
        private static ObjectId Pick(Editor ed, string prompt, Type type)
        {
            var options = new PromptEntityOptions(prompt); options.SetRejectMessage("\nSelect the requested object type."); options.AddAllowedClass(type, false);
            PromptEntityResult result = ed.GetEntity(options); return result.Status == PromptStatus.OK ? result.ObjectId : ObjectId.Null;
        }
        private static void Refresh(Document d)
        {
            ObjectId id = Pick(d.Editor, "\nSelect manhole info block to refresh: ", typeof(BlockReference)); if (id.IsNull) return;
            SewerCatalog c = ReadCatalog(d.Database) ?? throw new InvalidOperationException("Import this block's project catalog first.");
            SewerCatalog old;
            using (Transaction read = d.Database.TransactionManager.StartTransaction()) old = SewerInfoDrawing.Snapshot((BlockReference)read.GetObject(id, OpenMode.ForRead), read);
            if (old.ProjectNumber != c.ProjectNumber) throw new InvalidOperationException("Block and drawing catalog projects differ.");
            SewerManhole m = c.Manholes.SingleOrDefault(x => x.Name == old.Manholes[0].Name) ?? throw new InvalidOperationException("Record not found in the drawing catalog.");
            if (!Confirm("REFRESH MANHOLE INFO", "OLD SNAPSHOT\r\n" + SewerInfoModel.Serialize(old.Manholes[0]) + "\r\n\r\nNEW RECORD\r\n" + SewerInfoModel.Serialize(m), "Replace this block's attributes and snapshot with the current record. Attribute positions will reset.")) return;
            using Transaction tr = d.Database.TransactionManager.StartTransaction();
            SewerInfoDrawing.Refresh((BlockReference)tr.GetObject(id, OpenMode.ForRead), d.Database, tr, c, m); tr.Commit(); d.Editor.Regen();
        }

        private sealed record PipeState(ObjectId Id, Point3d Start, Point3d End, ObjectId StartStructure, ObjectId EndStructure, double InnerHeight, ObjectId Family, ObjectId Size);
        private sealed record StructureState(ObjectId Id, Point3d Position, string Name, double Rim, bool AutoRim, double Sump, ObjectId Family, ObjectId Size);
        private sealed record Mapping(SewerConnection Connection, ObjectId PipeId, bool AtStart);
        private static PipeState State(Pipe p) => new(p.ObjectId, p.StartPoint, p.EndPoint, p.StartStructureId, p.EndStructureId, p.InnerHeight, p.PartFamilyId, p.GetPartSizeId());
        private static StructureState State(Structure s) => new(s.ObjectId, s.Position, s.Name, s.RimElevation, s.AutomaticRimSurfaceAdjustment, s.SumpElevation, s.PartFamilyId, s.GetPartSizeId());
        private static void Editable(Autodesk.Civil.DatabaseServices.Part p, Transaction tr)
        {
            SewerInfoDrawing.Writable(p, tr);
            if (p.IsReferenceObject || p.IsReferenceSubObject || p.IsReadOnlyReferenceObject)
                throw new InvalidOperationException("Data reference and read-only network parts cannot be edited.");
        }
        private static void Apply(Document d)
        {
            ObjectId blockId = Pick(d.Editor, "\nSelect source manhole info block: ", typeof(BlockReference)); if (blockId.IsNull) return;
            SewerCatalog snapshot;
            using (Transaction read = d.Database.TransactionManager.StartTransaction())
            {
                var b = (BlockReference)read.GetObject(blockId, OpenMode.ForRead);
                SewerInfoDrawing.Writable(b, read); snapshot = SewerInfoDrawing.Snapshot(b, read); SewerInfoDrawing.CheckDisplay(b, snapshot, read);
                SewerCatalog? current = SewerInfoDrawing.Catalog(d.Database, read);
                if (current != null)
                {
                    if (current.ProjectNumber != snapshot.ProjectNumber) throw new InvalidOperationException("Block and catalog projects differ.");
                    SewerManhole? record = current.Manholes.SingleOrDefault(m => m.Name == snapshot.Manholes[0].Name);
                    if (record != null && SewerInfoModel.Serialize(record) != SewerInfoModel.Serialize(snapshot.Manholes[0]))
                        throw new InvalidOperationException("Catalog record has changed. Use SSMHREFRESH before applying this block.");
                }
            }
            SewerManhole mh = snapshot.Manholes[0];
            if (!mh.RimElevation.HasValue) throw new InvalidOperationException("Rim elevation is unknown. Complete the record first.");
            if (CivilApp.ActiveDocument.Settings.DrawingSettings.UnitZoneSettings.DrawingUnits.ToString() != "Feet")
                throw new InvalidOperationException("This starter requires Civil drawing units set to Feet. No conversion or datum transformation is performed.");
            ObjectId structureId = Pick(d.Editor, "\nSelect destination Civil 3D sewer structure: ", typeof(Structure)); if (structureId.IsNull) return;
            using Transaction tr = d.Database.TransactionManager.StartTransaction();
            var structure = (Structure)tr.GetObject(structureId, OpenMode.ForRead); Editable(structure, tr);
            var network = (Network)tr.GetObject(structure.NetworkId, OpenMode.ForRead);
            ObjectId[] pipeIds = network.GetPipeIds().Cast<ObjectId>().ToArray();
            ObjectId[] structureIds = network.GetStructureIds().Cast<ObjectId>().ToArray();
            if (structureIds.Any(id => id != structureId && string.Equals(((Structure)tr.GetObject(id, OpenMode.ForRead)).Name, mh.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Another structure already uses " + mh.Name + " in this network.");
            var pipeBefore = pipeIds.Select(id => State((Pipe)tr.GetObject(id, OpenMode.ForRead))).ToArray();
            var structureBefore = structureIds.Select(id => State((Structure)tr.GetObject(id, OpenMode.ForRead))).ToArray();
            PipeState[] connected = pipeBefore.Where(p => p.StartStructure == structureId || p.EndStructure == structureId).ToArray();
            d.Editor.WriteMessage("\nPlan connections: " + mh.Connections.Count + "; existing connected pipes: " + connected.Length
                + ". Name/rim are included; map the existing pipes below. Missing plan connections remain pending.");
            SewerInfoModel.AssignmentPlan assignmentPlan;
            if (connected.Length == 0) assignmentPlan = SewerInfoModel.PlanAssignments(mh.Connections, Array.Empty<int?>());
            else
            {
                string[] labels = connected.Select(old =>
                {
                    var p = (Pipe)tr.GetObject(old.Id, OpenMode.ForRead);
                    bool start = old.StartStructure == structureId;
                    return p.Name + " [" + p.Handle + "] " + (start ? "START" : "END")
                        + " | INV " + SewerInfoModel.Elevation((start ? old.Start.Z : old.End.Z) - old.InnerHeight / 2)
                        + " ft | " + p.PartFamilyName + " / " + p.PartSizeName;
                }).ToArray();
                using var mapForm = new SewerMapForm(mh, labels);
                if (AcadApp.ShowModalDialog(mapForm) != DialogResult.OK || mapForm.MappingPlan == null) return;
                assignmentPlan = mapForm.MappingPlan;
            }
            var mappings = new List<Mapping>();
            foreach (SewerInfoModel.ConnectionAssignment assignment in assignmentPlan.Assignments)
            {
                SewerConnection connection = mh.Connections[assignment.ConnectionIndex];
                PipeState before = connected[assignment.PipeIndex]; ObjectId pipeId = before.Id;
                if (before.StartStructure == before.EndStructure) throw new InvalidOperationException("A pipe connects both ends to this structure; mapping is ambiguous.");
                var pipe = (Pipe)tr.GetObject(pipeId, OpenMode.ForRead); Editable(pipe, tr);
                if (pipe.CrossSectionalShape.ToString() != "Circular" || !double.IsFinite(pipe.InnerHeight) || pipe.InnerHeight <= 0)
                    throw new InvalidOperationException("This starter transfers inverts to circular gravity pipes only.");
                using (Curve2d curve = pipe.Curve2d)
                    if (curve is not LineSegment2d) throw new InvalidOperationException("Curved pipes are outside this starter. Use straight circular pipe runs.");
                mappings.Add(new Mapping(connection, pipeId, before.StartStructure == structureId));
            }
            var preview = new StringBuilder();
            preview.AppendLine("PROJECT: " + snapshot.ProjectNumber + " | elevations: feet | no datum conversion");
            preview.AppendLine("BLOCK: " + blockId.Handle + " | STRUCTURE: " + structureId.Handle + " | NETWORK: " + network.Name);
            preview.AppendLine("NAME: " + structure.Name + " -> " + mh.Name);
            preview.AppendLine("RIM: " + SewerInfoModel.Elevation(structure.RimElevation) + " -> " + SewerInfoModel.Elevation(mh.RimElevation));
            preview.AppendLine("Automatic rim surface adjustment: " + structure.AutomaticRimSurfaceAdjustment + " -> False");
            preview.AppendLine("PLAN STRUCTURE SIZE: " + SewerInfoModel.Number(mh.StructureDiameterInches) + " in " + mh.StructureDiameterBasis);
            preview.AppendLine("Existing structure part: " + structure.PartFamilyName + " / " + structure.PartSizeName);
            preview.AppendLine("Structure size/material fields are reference data; parts are not resized or swapped.");
            preview.AppendLine("Invert values will be set at the CURRENT pipe endpoints. No structure wall/center offset correction is assumed.");
            preview.AppendLine("Actual modeled inner height is used to convert endpoint invert to centerline Z. Other ends retain their elevations; slopes change.");
            foreach (Mapping map in mappings)
            {
                var p = (Pipe)tr.GetObject(map.PipeId, OpenMode.ForRead);
                double old = (map.AtStart ? p.StartPoint.Z : p.EndPoint.Z) - p.InnerHeight / 2;
                preview.AppendLine("\r\nPLAN: " + SewerInfoModel.PipeLabel(map.Connection));
                preview.AppendLine("CAD: " + p.Name + " [" + p.Handle + "] " + (map.AtStart ? "START" : "END") + " | INV " + SewerInfoModel.Elevation(old) + " -> " + SewerInfoModel.Elevation(map.Connection.InvertElevation));
                preview.AppendLine("Existing part: " + p.PartFamilyName + " / " + p.PartSizeName + " | modeled ID " + SewerInfoModel.Number(p.InnerHeight * 12) + " in");
            }
            preview.AppendLine("\r\nPENDING PLAN CONNECTIONS: " + assignmentPlan.PendingConnections.Length);
            foreach (int i in assignmentPlan.PendingConnections) preview.AppendLine("PENDING - " + SewerInfoModel.PipeLabel(mh.Connections[i]));
            preview.AppendLine("EXISTING PIPES EXPLICITLY LEFT UNCHANGED: " + assignmentPlan.UnchangedPipes.Length);
            foreach (int i in assignmentPlan.UnchangedPipes) preview.AppendLine("UNCHANGED - " + ((Pipe)tr.GetObject(connected[i].Id, OpenMode.ForRead)).Name + " [" + connected[i].Id.Handle + "]");
            preview.AppendLine("Name/rim and every mapped pipe end will be applied. Pending connections remain stored plan data until their pipes are modeled/mapped.");
            preview.AppendLine("\r\nRECORD, PROJECT DATUM AND SOURCE METADATA\r\n" + SewerInfoModel.Serialize(snapshot));
            if (!Confirm("APPLY MANHOLE INFO - REVIEW ALL CHANGES", preview.ToString(), "I reviewed the source values, units/datum, pipe mapping and endpoint convention. Apply these changes.")) return;
            foreach (Mapping map in mappings)
            {
                var p = (Pipe)tr.GetObject(map.PipeId, OpenMode.ForWrite);
                Point3d old = map.AtStart ? p.StartPoint : p.EndPoint;
                var target = new Point3d(old.X, old.Y, map.Connection.InvertElevation!.Value + p.InnerHeight / 2);
                if (map.AtStart) p.StartPoint = target; else p.EndPoint = target;
            }
            structure.UpgradeOpen(); structure.AutomaticRimSurfaceAdjustment = false; structure.RimElevation = mh.RimElevation.Value; structure.Name = mh.Name;
            // Refuse native rule/resize side effects instead of committing partial or collateral edits.
            foreach (PipeState old in pipeBefore)
            {
                Pipe p = (Pipe)tr.GetObject(old.Id, OpenMode.ForRead); Mapping? map = mappings.SingleOrDefault(m => m.PipeId == old.Id);
                bool changedStart = map?.AtStart == true, changedEnd = map != null && !map.AtStart;
                CheckXY(p.StartPoint, old.Start); CheckXY(p.EndPoint, old.End);
                Check(p.StartPoint.Z, changedStart ? map!.Connection.InvertElevation!.Value + old.InnerHeight / 2 : old.Start.Z);
                Check(p.EndPoint.Z, changedEnd ? map!.Connection.InvertElevation!.Value + old.InnerHeight / 2 : old.End.Z);
                Check(p.InnerHeight, old.InnerHeight);
                if (p.StartStructureId != old.StartStructure || p.EndStructureId != old.EndStructure || p.PartFamilyId != old.Family || p.GetPartSizeId() != old.Size)
                    throw new InvalidOperationException("Pipe connectivity/part changed unexpectedly; transfer rolled back.");
            }
            foreach (StructureState old in structureBefore)
            {
                Structure s = (Structure)tr.GetObject(old.Id, OpenMode.ForRead); CheckXY(s.Position, old.Position);
                if (s.PartFamilyId != old.Family || s.GetPartSizeId() != old.Size) throw new InvalidOperationException("Structure part changed unexpectedly.");
                if (old.Id == structureId)
                { Check(s.RimElevation, mh.RimElevation.Value); if (!double.IsFinite(s.SumpElevation) || s.Name != mh.Name || s.AutomaticRimSurfaceAdjustment) throw new InvalidOperationException("Structure name/rim verification failed."); }
                else
                { Check(s.RimElevation, old.Rim); Check(s.SumpElevation, old.Sump); if (s.Name != old.Name || s.AutomaticRimSurfaceAdjustment != old.AutoRim) throw new InvalidOperationException("Adjacent structure changed unexpectedly."); }
            }
            SewerInfoDrawing.Attach(structure, "CLV_SEWER_APPLIED_INFO_V1", SewerInfoModel.Serialize(new
            {
                version = 1, projectNumber = snapshot.ProjectNumber, sourceBlockHandle = blockId.Handle.ToString(),
                reviewedAtUtc = DateTime.UtcNow, scope = "available_connections", record = mh,
                pendingConnectionIds = assignmentPlan.PendingConnections.Select(i => mh.Connections[i].ConnectionId).ToArray(),
                unchangedPipeHandles = assignmentPlan.UnchangedPipes.Select(i => connected[i].Id.Handle.ToString()).ToArray(),
                endpointConvention = "current_pipe_endpoint_no_wall_offset_correction",
                pipeMappings = mappings.Select(m => new { connectionId = m.Connection.ConnectionId, pipeHandle = m.PipeId.Handle.ToString(), endpoint = m.AtStart ? "start" : "end" }).ToArray()
            }), tr);
            foreach (Mapping map in mappings)
                SewerInfoDrawing.Attach(tr.GetObject(map.PipeId, OpenMode.ForWrite), map.AtStart ? "CLV_SEWER_PLAN_START_V1" : "CLV_SEWER_PLAN_END_V1",
                    SewerInfoModel.Serialize(new { projectNumber = snapshot.ProjectNumber, structureName = mh.Name, sourceBlockHandle = blockId.Handle.ToString(), connection = map.Connection }), tr);
            structure.RecordGraphicsModified(true);
            foreach (Mapping map in mappings) ((Pipe)tr.GetObject(map.PipeId, OpenMode.ForWrite)).RecordGraphicsModified(true);
            string actualName = structure.Name; double actualRim = structure.RimElevation;
            tr.Commit(); d.Editor.Regen();
            d.Editor.WriteMessage("\nUpdated native structure: " + actualName + "; RIM=" + SewerInfoModel.Elevation(actualRim)
                + " ft; applied " + mappings.Count + " pipe-end inverts. Pending plan connections: " + assignmentPlan.PendingConnections.Length
                + "; existing pipes left unchanged: " + assignmentPlan.UnchangedPipes.Length + ". Use UNDO to undo this command.");
            foreach (int i in assignmentPlan.PendingConnections) d.Editor.WriteMessage("\nPENDING (not applied to a native pipe): " + SewerInfoModel.PipeLabel(mh.Connections[i]));
        }
        private static void Check(double actual, double expected)
        { if (!double.IsFinite(actual) || Math.Abs(actual - expected) > 1e-7) throw new InvalidOperationException("Native readback differed from the requested values; the transfer was rolled back."); }
        private static void CheckXY(Point3d actual, Point3d expected) { Check(actual.X, expected.X); Check(actual.Y, expected.Y); }
    }
}

