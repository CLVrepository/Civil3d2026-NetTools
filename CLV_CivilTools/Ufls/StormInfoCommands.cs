using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
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
using PipeSubType = Autodesk.Civil.DatabaseServices.PipeSubEntityType;
using PipeCurveGeometry = Autodesk.Civil.DatabaseServices.PipeCurveGeometry;

namespace CLV_CivilTools.Ufls.StormInfo
{
    public static class StormInfoCommands
    {
        private sealed class CadOwner : IWin32Window { public IntPtr Handle => AcadApp.MainWindow.Handle; }
        [CommandMethod("SDINFO", CommandFlags.Modal)]
        public static void Manager() => Run(d =>
        {
            {
                StormCatalog? c = ReadCatalog(d.Database);
                using var form = new StormCatalogForm(c);
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
                catch (System.Exception e) { d.Editor.WriteMessage("\nSTORM INFO: " + e.Message); }
            }
        });
        [CommandMethod("SDIMPORT", CommandFlags.Modal)] public static void ImportCommand() => Run(Import);
        [CommandMethod("SDEDIT", CommandFlags.Modal)] public static void EditCommand() => Run(d => { StormStructure? m = PickRecord(d); if (m != null) Edit(d, m.Name); });
        [CommandMethod("SDADD", CommandFlags.Modal)] public static void AddCommand() => Run(d => Edit(d, null));
        [CommandMethod("SDINSERT", CommandFlags.Modal)] public static void PlaceCommand() => Run(d => Place(d, PickRecord(d)?.Name));
        [CommandMethod("SDAPPLY", CommandFlags.Modal)] public static void ApplyCommand() => Run(Apply);
        [CommandMethod("SDREFRESH", CommandFlags.Modal)] public static void RefreshCommand() => Run(Refresh);
        [CommandMethod("SDEXPORT", CommandFlags.Modal)] public static void ExportCommand() => Run(Export);

        private static void Run(Action<Document> action)
        {
            Document? d = AcadApp.DocumentManager.MdiActiveDocument; if (d == null) return;
            try { if (!d.Database.TileMode) throw new InvalidOperationException("Use the Model tab."); action(d); }
            catch (Autodesk.AutoCAD.Runtime.Exception e) when (e.ErrorStatus == ErrorStatus.UserBreak) { }
            catch (System.Exception e) { d.Editor.WriteMessage("\nSTORM INFO: " + e.Message); }
        }
        private static StormCatalog? ReadCatalog(Database db)
        { using Transaction tr = db.TransactionManager.StartTransaction(); return StormInfoDrawing.Catalog(db, tr); }
        private static void SaveCatalog(Database db, StormCatalog c)
        { using Transaction tr = db.TransactionManager.StartTransaction(); StormInfoDrawing.Save(db, tr, c); tr.Commit(); }
        private static StormStructure? PickRecord(Document d)
        {
            StormCatalog c = ReadCatalog(d.Database) ?? throw new InvalidOperationException("Import a catalog or use SDADD first.");
            PromptResult r = d.Editor.GetString(new PromptStringOptions("\nStructure identifier (for example 6 or 6A): ") { AllowSpaces = true });
            if (r.Status != PromptStatus.OK) return null;
            StormStructureNumber number = StormStructureNumber.Parse(r.StringResult);
            var options = new PromptKeywordOptions("\nStructure type [Manhole/DropInlet] <Manhole>: ") { AllowNone = true };
            options.Keywords.Add("Manhole"); options.Keywords.Add("DropInlet"); options.Keywords.Default = "Manhole";
            PromptResult type = d.Editor.GetKeywords(options);
            if (type.Status != PromptStatus.OK && type.Status != PromptStatus.None) return null;
            string kind = type.StringResult == "DropInlet" ? "SDDI" : "SDMH";
            return c.Structures.SingleOrDefault(m => m.Number == number && m.Type == kind) ?? throw new InvalidOperationException("Structure number is not in this drawing's catalog.");
        }
        private static bool Confirm(string title, string text, string acknowledgement)
        { using var f = new StormConfirmForm(title, text, acknowledgement); return AcadApp.ShowModalDialog(f) == DialogResult.OK; }
        private static void Import(Document d)
        {
            using var file = new System.Windows.Forms.OpenFileDialog { Title = "Import storm structure catalog", Filter = "JSON catalogs (*.json)|*.json", CheckFileExists = true };
            if (file.ShowDialog(new CadOwner()) != DialogResult.OK) return;
            if (new FileInfo(file.FileName).Length > StormInfoModel.MaxJsonLength) throw new InvalidOperationException("Catalog exceeds 8 MiB.");
            StormCatalog incoming = StormInfoModel.Parse(File.ReadAllText(file.FileName, Encoding.UTF8));
            StormCatalog? existing = ReadCatalog(d.Database);
            if (existing != null && existing.ProjectNumber != incoming.ProjectNumber)
                throw new InvalidOperationException("This drawing already has project " + existing.ProjectNumber + ". Use a separate drawing for another project's catalog.");
            StormCatalog merged = StormInfoModel.Merge(existing, incoming);
            int replacing = existing == null ? 0 : incoming.Structures.Count(m => existing.Structures.Any(x => x.Name == m.Name));
            StormInfoModel.Validate(merged);
            StormCatalog importMetadata = StormInfoModel.Copy(incoming); importMetadata.Structures.Clear();
            string summary = "Project: " + incoming.ProjectNumber + "\r\nIncoming records: " + incoming.Structures.Count + "\r\nReplace matching records: " + replacing
                + "\r\nRecords after merge: " + merged.Structures.Count + "\r\nManual records absent from this import are retained.\r\nPlaced blocks keep their snapshots until refreshed.\r\n\r\n"
                + string.Join("\r\n", incoming.Structures.OrderBy(m => m.Number).Select(m => m.Name + " | RIM " + StormInfoModel.Elevation(m.RimElevation) + " | STRUCT " + StormInfoModel.Number(m.StructureDiameterInches) + " in | " + m.Connections.Count + " pipes"))
                + "\r\n\r\nPROJECT METADATA / DATUM\r\n" + StormInfoModel.Serialize(importMetadata);
            if (!Confirm("IMPORT STORM CATALOG", summary, "Import these records and replace the matching catalog entries shown above.")) return;
            SaveCatalog(d.Database, merged); d.Editor.WriteMessage("\nImported catalog into DWG: " + merged.Structures.Count + " structures. Placed blocks were not changed.");
        }
        private static void Edit(Document d, string? name)
        {
            StormCatalog? c = ReadCatalog(d.Database);
            StormStructure? m = name == null ? null : c?.Structures.SingleOrDefault(x => x.Name == name);
            using var form = new StormEditForm(c, m);
            if (AcadApp.ShowModalDialog(form) != DialogResult.OK || form.ResultCatalog == null) return;
            SaveCatalog(d.Database, form.ResultCatalog); d.Editor.WriteMessage("\nSaved structure catalog. Use SDREFRESH for any already placed copies of edited records.");
        }
        private static void Export(Document d)
        {
            StormCatalog c = ReadCatalog(d.Database) ?? throw new InvalidOperationException("No catalog in this drawing.");
            using var file = new System.Windows.Forms.SaveFileDialog { Title = "Export storm catalog", Filter = "JSON catalog (*.json)|*.json", FileName = c.ProjectNumber + "-storm-catalog.json", OverwritePrompt = true };
            if (file.ShowDialog(new CadOwner()) == DialogResult.OK) { File.WriteAllText(file.FileName, StormInfoModel.Serialize(c), new UTF8Encoding(false)); d.Editor.WriteMessage("\nExported storm catalog JSON."); }
        }
        private static void Place(Document d, string? name)
        {
            if (name == null) return;
            StormCatalog c = ReadCatalog(d.Database) ?? throw new InvalidOperationException("No catalog in this drawing.");
            StormStructure m = c.Structures.Single(x => x.Name == name);
            PromptPointResult p = d.Editor.GetPoint("\nPick structure info marker location (plan marker only): "); if (p.Status != PromptStatus.OK) return;
            Point3d wcs = p.Value.TransformBy(d.Editor.CurrentUserCoordinateSystem);
            PromptDoubleResult h = d.Editor.GetDouble(new PromptDoubleOptions("\nInformation text height in drawing units: ") { DefaultValue = 1, UseDefaultValue = true, AllowZero = false, AllowNegative = false });
            if (h.Status != PromptStatus.OK) return;
            if (!double.IsFinite(h.Value) || h.Value < 0.001 || h.Value > 10000) throw new InvalidOperationException("Text height must be 0.001 to 10000 drawing units.");
            using Transaction tr = d.Database.TransactionManager.StartTransaction();
            BlockReference b = StormInfoDrawing.Place(d.Database, tr, c, m, new Point3d(wcs.X, wcs.Y, 0), h.Value);
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
            ObjectId id = Pick(d.Editor, "\nSelect structure info block to refresh: ", typeof(BlockReference)); if (id.IsNull) return;
            StormCatalog c = ReadCatalog(d.Database) ?? throw new InvalidOperationException("Import this block's project catalog first.");
            StormCatalog old;
            using (Transaction read = d.Database.TransactionManager.StartTransaction()) old = StormInfoDrawing.Snapshot((BlockReference)read.GetObject(id, OpenMode.ForRead), read);
            if (old.ProjectNumber != c.ProjectNumber) throw new InvalidOperationException("Block and drawing catalog projects differ.");
            StormStructure m = c.Structures.SingleOrDefault(x => x.Name == old.Structures[0].Name) ?? throw new InvalidOperationException("Record not found in the drawing catalog.");
            if (!Confirm("REFRESH MANHOLE INFO", "OLD SNAPSHOT\r\n" + StormInfoModel.Serialize(old.Structures[0]) + "\r\n\r\nNEW RECORD\r\n" + StormInfoModel.Serialize(m), "Replace this block's attributes and snapshot with the current record. Attribute positions will reset.")) return;
            using Transaction tr = d.Database.TransactionManager.StartTransaction();
            StormInfoDrawing.Refresh((BlockReference)tr.GetObject(id, OpenMode.ForRead), d.Database, tr, c, m); tr.Commit(); d.Editor.Regen();
        }

        private sealed record ArcState(double Bulge, Point2d Center, double Radius, Point2d Start, Point2d End, bool Clockwise, double Sweep);
        private sealed record PipeState(ObjectId Id, Point3d Start, Point3d End, ObjectId StartStructure, ObjectId EndStructure, double InnerHeight, ObjectId Family, ObjectId Size, PipeSubType SubType, ArcState? Arc);
        private sealed record StructureState(ObjectId Id, Point3d Position, string Name, double Rim, bool AutoRim, double Sump, ObjectId Family, ObjectId Size);
        private sealed record Mapping(StormConnection Connection, ObjectId PipeId, bool AtStart, StormInfoParts.Target Part);
        private static PipeState State(Pipe p)
        {
            ArcState? arc = null;
            if (p.SubEntityType == PipeSubType.Curved)
            {
                using PipeCurveGeometry geometry = p.CurveGeometry;
                using CircularArc2d plan = geometry.GetArc2d();
                arc = new ArcState(geometry.Bulge, plan.Center, plan.Radius, plan.StartPoint, plan.EndPoint, plan.IsClockWise, plan.EndAngle - plan.StartAngle);
                if (!double.IsFinite(arc.Bulge) || !double.IsFinite(arc.Radius) || arc.Radius <= 0)
                    throw new InvalidOperationException("Pipe " + p.Handle + " has invalid plan arc geometry.");
            }
            return new PipeState(p.ObjectId, p.StartPoint, p.EndPoint, p.StartStructureId, p.EndStructureId, p.InnerHeight, p.PartFamilyId, p.GetPartSizeId(), p.SubEntityType, arc);
        }
        private static StormInfoModel.NativeConnection DirectionAtStructure(PipeState p, ObjectId structureId)
        {
            bool start = p.StartStructure == structureId;
            if (p.Arc == null) return new StormInfoModel.NativeConnection(start,
                start ? p.End.X - p.Start.X : p.Start.X - p.End.X, start ? p.End.Y - p.Start.Y : p.Start.Y - p.End.Y);
            Point3d selected = start ? p.Start : p.End;
            var point = new Point2d(selected.X, selected.Y);
            bool atArcStart = PlanDistance(point, p.Arc.Start) <= 1e-6, atArcEnd = PlanDistance(point, p.Arc.End) <= 1e-6;
            if (atArcStart == atArcEnd) throw new InvalidOperationException("Pipe " + p.Id.Handle + ": cannot identify the selected end on its plan arc; no data changed.");
            var tangent = StormInfoModel.CurveOutwardTangent(point.X - p.Arc.Center.X, point.Y - p.Arc.Center.Y, p.Arc.Clockwise, atArcStart);
            return new StormInfoModel.NativeConnection(start, tangent.Dx, tangent.Dy);
        }
        private static void CheckPlanGeometry(PipeState old, PipeState actual)
        {
            if (old.SubType != actual.SubType || (old.Arc == null) != (actual.Arc == null))
                throw new InvalidOperationException("Pipe " + old.Id.Handle + " changed geometry type; the transfer was rolled back.");
            if (old.Arc == null || actual.Arc == null) return;
            const double planTolerance = 1e-6;
            if (PlanDistance(old.Arc.Center, actual.Arc.Center) > planTolerance || Math.Abs(old.Arc.Radius - actual.Arc.Radius) > planTolerance
                || PlanDistance(old.Arc.Start, actual.Arc.Start) > planTolerance || PlanDistance(old.Arc.End, actual.Arc.End) > planTolerance
                || old.Arc.Clockwise != actual.Arc.Clockwise || Math.Abs(old.Arc.Bulge - actual.Arc.Bulge) > 1e-10 || Math.Abs(old.Arc.Sweep - actual.Arc.Sweep) > 1e-10)
                throw new InvalidOperationException("Pipe " + old.Id.Handle + " plan arc changed; the transfer was rolled back.");
        }
        private static double PlanDistance(Point2d a, Point2d b)
        { double dx = a.X - b.X, dy = a.Y - b.Y; return Math.Sqrt(dx * dx + dy * dy); }
        private static StructureState State(Structure s) => new(s.ObjectId, s.Position, s.Name, s.RimElevation, s.AutomaticRimSurfaceAdjustment, s.SumpElevation, s.PartFamilyId, s.GetPartSizeId());
        private static void Editable(Autodesk.Civil.DatabaseServices.Part p, Transaction tr)
        {
            StormInfoDrawing.Writable(p, tr);
            if (p.IsReferenceObject || p.IsReferenceSubObject || p.IsReadOnlyReferenceObject)
                throw new InvalidOperationException("Data reference and read-only network parts cannot be edited.");
        }
        private static void Apply(Document d)
        {
            ObjectId blockId = Pick(d.Editor, "\nSelect source structure info block: ", typeof(BlockReference)); if (blockId.IsNull) return;
            StormCatalog snapshot;
            using (Transaction read = d.Database.TransactionManager.StartTransaction())
            {
                var b = (BlockReference)read.GetObject(blockId, OpenMode.ForRead);
                StormInfoDrawing.Writable(b, read); snapshot = StormInfoDrawing.Snapshot(b, read); StormInfoDrawing.CheckDisplay(b, snapshot, read);
                StormCatalog? current = StormInfoDrawing.Catalog(d.Database, read);
                if (current != null)
                {
                    if (current.ProjectNumber != snapshot.ProjectNumber) throw new InvalidOperationException("Block and catalog projects differ.");
                    StormStructure? record = current.Structures.SingleOrDefault(m => m.Name == snapshot.Structures[0].Name);
                    if (record != null && StormInfoModel.Serialize(record) != StormInfoModel.Serialize(snapshot.Structures[0]))
                        throw new InvalidOperationException("Catalog record has changed. Use SDREFRESH before applying this block.");
                }
            }
            StormStructure mh = snapshot.Structures[0];
            if (!string.IsNullOrWhiteSpace(mh.ApplicationHoldReason)) throw new InvalidOperationException("Plan review required before applying: " + mh.ApplicationHoldReason);
            if (!mh.RimElevation.HasValue) throw new InvalidOperationException("Rim/top elevation is unknown. Complete the record first.");
            if (mh.TopElevationBasis is "unknown" or "flowline") throw new InvalidOperationException("Confirm the structure top as rim, grate or top before applying. A gutter flowline elevation must not be assigned to the structure rim.");
            if (CivilApp.ActiveDocument.Settings.DrawingSettings.UnitZoneSettings.DrawingUnits.ToString() != "Feet")
                throw new InvalidOperationException("This starter requires Civil drawing units set to Feet. No conversion or datum transformation is performed.");
            ObjectId structureId = Pick(d.Editor, "\nSelect destination Civil 3D storm structure: ", typeof(Structure)); if (structureId.IsNull) return;
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
                + ". Automatically matching START=OUT / END=IN and available WCS compass directions.");
            StormInfoModel.AutomaticPlan automatic = StormInfoModel.MatchAutomatically(mh.Connections, connected.Select(p => DirectionAtStructure(p, structureId)).ToArray());
            for (int i = 0; i < connected.Length; i++) if (connected[i].Arc != null) automatic.Evidence[i] += " Bearing uses the curved pipe's endpoint tangent.";
            StormInfoModel.AssignmentPlan assignmentPlan = automatic.Plan;
            var mappings = new List<Mapping>();
            foreach (StormInfoModel.ConnectionAssignment assignment in assignmentPlan.Assignments)
            {
                StormConnection connection = mh.Connections[assignment.ConnectionIndex];
                PipeState before = connected[assignment.PipeIndex]; ObjectId pipeId = before.Id;
                if (before.StartStructure == before.EndStructure) throw new InvalidOperationException("A pipe connects both ends to this structure; mapping is ambiguous.");
                var pipe = (Pipe)tr.GetObject(pipeId, OpenMode.ForRead); Editable(pipe, tr);
                if (pipe.CrossSectionalShape.ToString() is not ("Circular" or "Rectangular") || !double.IsFinite(pipe.InnerHeight) || pipe.InnerHeight <= 0)
                    throw new InvalidOperationException("Only circular and rectangular gravity pipes are supported.");
                if (pipe.SubEntityType is not (PipeSubType.Straight or PipeSubType.Curved))
                    throw new InvalidOperationException("Pipe " + pipe.Handle + " has unsupported subtype " + pipe.SubEntityType + ". Straight and curved circular gravity pipes are supported.");
                bool atStart = before.StartStructure == structureId;
                string? otherJson = StormInfoDrawing.Attached(pipe, atStart ? "CLV_STORM_PLAN_END_V1" : "CLV_STORM_PLAN_START_V1", tr);
                if (otherJson != null)
                {
                    using JsonDocument other = JsonDocument.Parse(otherJson);
                    if (other.RootElement.GetProperty("projectNumber").GetString() != snapshot.ProjectNumber)
                        throw new InvalidOperationException("Pipe " + pipe.Handle + " has opposite-end plan data from a different project.");
                    StormConnection otherConnection = JsonSerializer.Deserialize<StormConnection>(other.RootElement.GetProperty("connection").GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                        ?? throw new InvalidOperationException("Invalid opposite-end pipe plan data.");
                    // Use revised catalog data when available, so correcting a plan conflict does
                    // not leave two historical endpoint snapshots permanently blocking each other.
                    StormCatalog? latest = StormInfoDrawing.Catalog(d.Database, tr);
                    string? otherName = other.RootElement.GetProperty("structureName").GetString();
                    StormConnection? revised = latest?.Structures.SingleOrDefault(x => x.Name == otherName)?.Connections.SingleOrDefault(x => x.ConnectionId == otherConnection.ConnectionId);
                    if (latest?.ProjectNumber == snapshot.ProjectNumber && revised != null) otherConnection = revised;
                    StormInfoPartRules.CheckOtherEnd(connection, otherConnection);
                }
                mappings.Add(new Mapping(connection, pipeId, atStart, StormInfoParts.Pipe(pipe, network, connection, tr)));
            }
            StormInfoParts.Target structurePart = StormInfoParts.Structure(structure, mh, tr);
            var preview = new StringBuilder();
            preview.AppendLine("PROJECT: " + snapshot.ProjectNumber + " | elevations: feet | no datum conversion");
            preview.AppendLine("BLOCK: " + blockId.Handle + " | STRUCTURE: " + structureId.Handle + " | NETWORK: " + network.Name);
            preview.AppendLine("NAME: " + structure.Name + " -> " + mh.Name);
            preview.AppendLine("RIM: " + StormInfoModel.Elevation(structure.RimElevation) + " -> " + StormInfoModel.Elevation(mh.RimElevation));
            preview.AppendLine("DIRECTION FALLBACK WARNINGS: " + automatic.DirectionFallbackPipes.Length + "; review AUTO MATCH evidence below.");
            preview.AppendLine("Automatic rim surface adjustment: " + structure.AutomaticRimSurfaceAdjustment + " -> False");
            preview.AppendLine("PLAN STRUCTURE SIZE: " + StormInfoModel.Number(mh.StructureDiameterInches) + " in " + mh.StructureDiameterBasis);
            preview.AppendLine("Existing structure part: " + structure.PartFamilyName + " / " + structure.PartSizeName);
            preview.AppendLine("TARGET STRUCTURE PART: " + structurePart.Description);
            preview.AppendLine("Invert values will be set at the CURRENT pipe endpoints. No structure wall/center offset correction is assumed.");
            preview.AppendLine("Parts are swapped BEFORE setting inverts. New actual modeled inner height converts invert to centerline Z. Opposite-end INVERT is retained; centerline Z may change with inside diameter. Slopes may change.");
            foreach (Mapping map in mappings)
            {
                var p = (Pipe)tr.GetObject(map.PipeId, OpenMode.ForRead);
                double old = (map.AtStart ? p.StartPoint.Z : p.EndPoint.Z) - p.InnerHeight / 2;
                preview.AppendLine("\r\nPLAN: " + StormInfoModel.PipeLabel(map.Connection));
                preview.AppendLine("CAD: " + p.Name + " [" + p.Handle + "] " + (map.AtStart ? "START" : "END") + " | INV " + StormInfoModel.Elevation(old) + " -> " + StormInfoModel.Elevation(map.Connection.InvertElevation));
                preview.AppendLine("Existing part: " + p.PartFamilyName + " / " + p.PartSizeName + " | modeled ID " + StormInfoModel.Number(p.InnerHeight * 12) + " in");
                preview.AppendLine("TARGET PIPE PART: " + map.Part.Description);
                preview.AppendLine("AUTO MATCH: " + automatic.Evidence[Array.FindIndex(connected, x => x.Id == map.PipeId)]);
                ArcState? arc = connected.Single(x => x.Id == map.PipeId).Arc;
                if (arc != null) preview.AppendLine("CURVED PIPE: plan radius " + StormInfoModel.Number(arc.Radius) + " ft; preserve XY arc, bulge and rotation direction while updating endpoint elevation.");
            }
            preview.AppendLine("\r\nPENDING PLAN CONNECTIONS: " + assignmentPlan.PendingConnections.Length);
            foreach (int i in assignmentPlan.PendingConnections) preview.AppendLine("PENDING - " + StormInfoModel.PipeLabel(mh.Connections[i]));
            preview.AppendLine("UNRESOLVED EXISTING PIPES (UNCHANGED): " + assignmentPlan.UnchangedPipes.Length);
            foreach (int i in assignmentPlan.UnchangedPipes) preview.AppendLine("UNCHANGED - " + ((Pipe)tr.GetObject(connected[i].Id, OpenMode.ForRead)).Name + " [" + connected[i].Id.Handle + "] | " + automatic.Evidence[i]);
            preview.AppendLine("Name/rim and every mapped pipe end will be applied. Pending connections remain stored plan data until their pipes are modeled/mapped.");
            preview.AppendLine("\r\nRECORD, PROJECT DATUM AND SOURCE METADATA\r\n" + StormInfoModel.Serialize(snapshot));
            if (!Confirm("APPLY STORM STRUCTURE INFO - REVIEW AUTOMATIC MATCHES", preview.ToString(), "I reviewed the source values, automatic matches, pending/unresolved rows and endpoint convention. Apply these changes.")) return;
            structure.UpgradeOpen();
            if (structure.PartFamilyId != structurePart.Family || structure.GetPartSizeId() != structurePart.Size)
                structure.SwapPartFamilyAndSize(structurePart.Family, structurePart.Size);
            var appliedHeight = new Dictionary<ObjectId, double>();
            foreach (Mapping map in mappings)
            {
                var p = (Pipe)tr.GetObject(map.PipeId, OpenMode.ForWrite);
                PipeState before = connected.Single(x => x.Id == map.PipeId);
                if (p.PartFamilyId != map.Part.Family || p.GetPartSizeId() != map.Part.Size)
                    p.SwapPartFamilyAndSize(map.Part.Family, map.Part.Size);
                string expectedShape = map.Connection.Shape == "rectangular" ? "Rectangular" : "Circular";
                if (p.CrossSectionalShape.ToString() != expectedShape || !double.IsFinite(p.InnerHeight) || p.InnerHeight <= 0)
                    throw new InvalidOperationException("Native pipe shape/height does not match the plan connection; changes rolled back.");
                StormInfoParts.VerifyPipeDimensions(p, map.Part);
                double height = p.InnerHeight; appliedHeight.Add(p.ObjectId, height);
                double startInvert = map.AtStart ? map.Connection.InvertElevation!.Value : before.Start.Z - before.InnerHeight / 2;
                double endInvert = map.AtStart ? before.End.Z - before.InnerHeight / 2 : map.Connection.InvertElevation!.Value;
                var start = new Point3d(before.Start.X, before.Start.Y, StormInfoPartRules.Centerline(startInvert, height));
                var end = new Point3d(before.End.X, before.End.Y, StormInfoPartRules.Centerline(endInvert, height));
                if (before.Arc != null)
                {
                    // Preserve the native XY arc definition explicitly; only endpoint Z changes.
                    using var curve = new PipeCurveGeometry(start, end, before.Arc.Bulge);
                    p.CurveGeometry = curve;
                }
                else { p.StartPoint = start; p.EndPoint = end; }
            }
            structure.AutomaticRimSurfaceAdjustment = false; structure.RimElevation = mh.RimElevation.Value; structure.Name = mh.Name;
            // Refuse native rule/resize side effects instead of committing partial or collateral edits.
            foreach (PipeState old in pipeBefore)
            {
                Pipe p = (Pipe)tr.GetObject(old.Id, OpenMode.ForRead); Mapping? map = mappings.SingleOrDefault(m => m.PipeId == old.Id);
                bool changedStart = map?.AtStart == true, changedEnd = map != null && !map.AtStart;
                double height = map == null ? old.InnerHeight : appliedHeight[old.Id];
                CheckXY(p.StartPoint, old.Start); CheckXY(p.EndPoint, old.End);
                Check(p.StartPoint.Z, changedStart ? StormInfoPartRules.Centerline(map!.Connection.InvertElevation!.Value, height) : old.Start.Z + (height - old.InnerHeight) / 2);
                Check(p.EndPoint.Z, changedEnd ? StormInfoPartRules.Centerline(map!.Connection.InvertElevation!.Value, height) : old.End.Z + (height - old.InnerHeight) / 2);
                Check(p.InnerHeight, height);
                if (map != null) StormInfoParts.VerifyPipeDimensions(p, map.Part);
                CheckPlanGeometry(old, State(p));
                if (p.StartStructureId != old.StartStructure || p.EndStructureId != old.EndStructure || p.PartFamilyId != (map?.Part.Family ?? old.Family) || p.GetPartSizeId() != (map?.Part.Size ?? old.Size))
                    throw new InvalidOperationException("Pipe connectivity/part changed unexpectedly; transfer rolled back.");
            }
            foreach (StructureState old in structureBefore)
            {
                Structure s = (Structure)tr.GetObject(old.Id, OpenMode.ForRead); CheckXY(s.Position, old.Position);
                if (s.PartFamilyId != old.Family || s.GetPartSizeId() != (old.Id == structureId ? structurePart.Size : old.Size)) throw new InvalidOperationException("Structure part changed unexpectedly.");
                if (old.Id == structureId)
                { Check(s.RimElevation, mh.RimElevation.Value); if (structurePart.FrameInches.HasValue) Check(s.FrameDiameter * 12, structurePart.FrameInches.Value); if (!double.IsFinite(s.SumpElevation) || s.Name != mh.Name || s.AutomaticRimSurfaceAdjustment) throw new InvalidOperationException("Structure name/rim verification failed."); }
                else
                { Check(s.RimElevation, old.Rim); Check(s.SumpElevation, old.Sump); if (s.Name != old.Name || s.AutomaticRimSurfaceAdjustment != old.AutoRim) throw new InvalidOperationException("Adjacent structure changed unexpectedly."); }
            }
            StormInfoDrawing.Attach(structure, "CLV_STORM_APPLIED_INFO_V1", StormInfoModel.Serialize(new
            {
                version = 2, projectNumber = snapshot.ProjectNumber, sourceBlockHandle = blockId.Handle.ToString(),
                reviewedAtUtc = DateTime.UtcNow, scope = "automatic_available_connections", record = mh,
                matchingConvention = "START_OUT_END_IN_WCS_COMPASS",
                matchingEvidence = connected.Select((p, i) => new { pipeHandle = p.Id.Handle.ToString(), evidence = automatic.Evidence[i] }).ToArray(),
                pendingConnectionIds = assignmentPlan.PendingConnections.Select(i => mh.Connections[i].ConnectionId).ToArray(),
                unchangedPipeHandles = assignmentPlan.UnchangedPipes.Select(i => connected[i].Id.Handle.ToString()).ToArray(),
                endpointConvention = "current_pipe_endpoint_no_wall_offset_correction",
                structurePart = structurePart.Description,
                pipeMappings = mappings.Select(m => new { connectionId = m.Connection.ConnectionId, pipeHandle = m.PipeId.Handle.ToString(), endpoint = m.AtStart ? "start" : "end", targetPart = m.Part.Description, actualInnerHeightFeet = appliedHeight[m.PipeId] }).ToArray()
            }), tr);
            foreach (Mapping map in mappings)
                StormInfoDrawing.Attach(tr.GetObject(map.PipeId, OpenMode.ForWrite), map.AtStart ? "CLV_STORM_PLAN_START_V1" : "CLV_STORM_PLAN_END_V1",
                    StormInfoModel.Serialize(new { projectNumber = snapshot.ProjectNumber, structureName = mh.Name, sourceBlockHandle = blockId.Handle.ToString(), connection = map.Connection }), tr);
            structure.RecordGraphicsModified(true);
            foreach (Mapping map in mappings) ((Pipe)tr.GetObject(map.PipeId, OpenMode.ForWrite)).RecordGraphicsModified(true);
            string actualName = structure.Name; double actualRim = structure.RimElevation;
            tr.Commit(); d.Editor.Regen();
            d.Editor.WriteMessage("\nUpdated native structure: " + actualName + "; RIM=" + StormInfoModel.Elevation(actualRim)
                + " ft; applied " + mappings.Count + " pipe-end inverts. See the reviewed part checks and retained-part warnings. Pending plan connections: " + assignmentPlan.PendingConnections.Length
                + "; existing pipes left unchanged: " + assignmentPlan.UnchangedPipes.Length + ". Use UNDO to undo this command.");
            foreach (int i in automatic.DirectionFallbackPipes) d.Editor.WriteMessage("\nAPPLIED WITH DIRECTION WARNING pipe [" + connected[i].Id.Handle + "]: " + automatic.Evidence[i]);
            foreach (int i in assignmentPlan.PendingConnections) d.Editor.WriteMessage("\nPENDING (not applied to a native pipe): " + StormInfoModel.PipeLabel(mh.Connections[i]));
            foreach (int i in assignmentPlan.UnchangedPipes) d.Editor.WriteMessage("\nUNRESOLVED pipe [" + connected[i].Id.Handle + "]: " + automatic.Evidence[i]);
        }
        private static void Check(double actual, double expected)
        { if (!double.IsFinite(actual) || Math.Abs(actual - expected) > 1e-7) throw new InvalidOperationException("Native readback differed from the requested values; the transfer was rolled back."); }
        private static void CheckXY(Point3d actual, Point3d expected) { Check(actual.X, expected.X); Check(actual.Y, expected.Y); }
    }
}





