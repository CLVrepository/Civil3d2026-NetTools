using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using CLV_CivilTools.Shared;

namespace CLV_CivilTools.Ufls
{
    /// <summary>
    /// Creates and maintains AutoCAD tables backed by Pipe Top Check metadata.
    /// </summary>
    public static class PipeTopCheckTableCommands
    {
        private static readonly double[] ColumnWidths = { 0.55, 1.20, 1.20, 0.95, 1.05, 0.85 };

        [CommandMethod("UFLS-PIPE-TOP-TABLE")]
        public static void CreatePipeTopCheckTable()
        {
            Autodesk.AutoCAD.ApplicationServices.Document? doc =
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;
            List<PipeTopCheckData.Snapshot> checks = SelectChecks(ed, db, "\nSelect Pipe Top Check labels for table: ");
            if (checks.Count == 0)
                return;

            checks = SortChecks(checks);

            PromptPointResult pointResult = ed.GetPoint("\nSpecify table insertion point: ");
            if (pointResult.Status != PromptStatus.OK)
                return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                ObjectId tableStyleId = PipeTopCheckTableStyle.Ensure(db, tr, ed);
                if (tableStyleId.IsNull)
                    return;

                if (!LayerStandards.TryEnsureManagedLayer(
                        db,
                        tr,
                        ed,
                        LayerStandards.UflsPipeTopCheckLayerName))
                {
                    ed.WriteMessage($"\nPipe Top Check table: managed layer '{LayerStandards.UflsPipeTopCheckLayerName}' is not available in layer standards.");
                    return;
                }

                LayerTable layerTable =
                    (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead, false);
                ObjectId layerId = layerTable[LayerStandards.UflsPipeTopCheckLayerName];

                Table table = new Table
                {
                    TableStyle = tableStyleId,
                    LayerId = layerId,
                    Position = pointResult.Value
                };

                ConfigureTable(table, checks);

                BlockTableRecord currentSpace =
                    (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite, false);
                currentSpace.AppendEntity(table);
                tr.AddNewlyCreatedDBObject(table, true);

                PipeTopCheckTableData.Write(table, tr, checks.Select(c => c.Id));
                tr.Commit();
            }

            ed.WriteMessage($"\nCreated Pipe Top Check table with {checks.Count} point(s) on {LayerStandards.UflsPipeTopCheckLayerName}.");
        }

        [CommandMethod("UFLS-PIPE-TOP-TABLE-UPDATE")]
        public static void UpdatePipeTopCheckTable()
        {
            if (!TrySelectTable(out ObjectId tableId))
                return;

            UpdateTable(tableId);
        }

        [CommandMethod("UFLS-PIPE-TOP-TABLE-ADD")]
        public static void AddPipeTopCheckTablePoints()
        {
            if (!TrySelectTable(out ObjectId tableId))
                return;

            Autodesk.AutoCAD.ApplicationServices.Document? doc =
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;
            List<PipeTopCheckData.Snapshot> additions = SelectChecks(
                ed,
                db,
                "\nSelect Pipe Top Check labels to add to table: ");

            if (additions.Count == 0)
                return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                if (tr.GetObject(tableId, OpenMode.ForWrite, false) is not Table table)
                {
                    ed.WriteMessage("\nSelected object is not an AutoCAD table.");
                    return;
                }

                List<Guid> ids = PipeTopCheckTableData.TryRead(table, tr, out List<Guid> existing)
                    ? existing
                    : new List<Guid>();

                foreach (PipeTopCheckData.Snapshot check in additions)
                {
                    if (!ids.Contains(check.Id))
                        ids.Add(check.Id);
                }

                PipeTopCheckTableData.Write(table, tr, ids);
                tr.Commit();
            }

            UpdateTable(tableId);
        }

        [CommandMethod("UFLS-PIPE-TOP-TABLE-REMOVE")]
        public static void RemovePipeTopCheckTablePoints()
        {
            if (!TrySelectTable(out ObjectId tableId))
                return;

            Autodesk.AutoCAD.ApplicationServices.Document? doc =
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Editor ed = doc.Editor;
            Database db = doc.Database;
            List<PipeTopCheckData.Snapshot> removals = SelectChecks(
                ed,
                db,
                "\nSelect Pipe Top Check labels to remove from table: ");

            if (removals.Count == 0)
                return;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                if (tr.GetObject(tableId, OpenMode.ForWrite, false) is not Table table ||
                    !PipeTopCheckTableData.TryRead(table, tr, out List<Guid> ids))
                {
                    ed.WriteMessage("\nSelected table is not a Pipe Top Check table.");
                    return;
                }

                foreach (PipeTopCheckData.Snapshot check in removals)
                    ids.Remove(check.Id);

                PipeTopCheckTableData.Write(table, tr, ids);
                tr.Commit();
            }

            UpdateTable(tableId);
        }

        private static bool TrySelectTable(out ObjectId tableId)
        {
            tableId = ObjectId.Null;
            Autodesk.AutoCAD.ApplicationServices.Document? doc =
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return false;

            PromptEntityOptions options = new PromptEntityOptions("\nSelect Pipe Top Check table: ");
            options.SetRejectMessage("\nPlease select an AutoCAD table.");
            options.AddAllowedClass(typeof(Table), true);

            PromptEntityResult result = doc.Editor.GetEntity(options);
            if (result.Status != PromptStatus.OK)
                return false;

            tableId = result.ObjectId;
            return true;
        }

        private static void UpdateTable(ObjectId tableId)
        {
            Autodesk.AutoCAD.ApplicationServices.Document? doc =
                Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                if (tr.GetObject(tableId, OpenMode.ForWrite, false) is not Table table ||
                    !PipeTopCheckTableData.TryRead(table, tr, out List<Guid> ids))
                {
                    ed.WriteMessage("\nSelected table is not a Pipe Top Check table.");
                    return;
                }

                ObjectId tableStyleId = PipeTopCheckTableStyle.Ensure(db, tr, ed);
                if (tableStyleId.IsNull)
                    return;

                if (!LayerStandards.TryEnsureManagedLayer(
                        db,
                        tr,
                        ed,
                        LayerStandards.UflsPipeTopCheckLayerName))
                {
                    ed.WriteMessage($"\nPipe Top Check table: managed layer '{LayerStandards.UflsPipeTopCheckLayerName}' is not available in layer standards.");
                    return;
                }

                LayerTable layerTable =
                    (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead, false);
                table.TableStyle = tableStyleId;
                table.LayerId = layerTable[LayerStandards.UflsPipeTopCheckLayerName];

                Dictionary<Guid, PipeTopCheckData.Snapshot> checks = FindChecksById(db, tr, ids);
                List<PipeTopCheckData.Snapshot> ordered = ids
                    .Where(checks.ContainsKey)
                    .Select(id => checks[id])
                    .OrderBy(c => ParseExhibitId(c.ExhibitId))
                    .ThenBy(c => c.ExhibitId, StringComparer.Ordinal)
                    .ThenByDescending(c => c.CheckPointLocation.Y)
                    .ThenBy(c => c.CheckPointLocation.X)
                    .ToList();

                ConfigureTable(table, ordered);
                PipeTopCheckTableData.Write(table, tr, ordered.Select(c => c.Id));
                tr.Commit();

                ed.WriteMessage($"\nUpdated Pipe Top Check table: {ordered.Count} point(s).");
            }
        }

        private static void ConfigureTable(Table table, IReadOnlyList<PipeTopCheckData.Snapshot> checks)
        {
            table.SetSize(checks.Count + 1, 6);

            for (int column = 0; column < ColumnWidths.Length; column++)
                table.Columns[column].Width = ColumnWidths[column];

            // AutoCAD creates the first row as a merged title row by default.
            // This table uses that row as its actual header instead.
            if (table.Rows[0].IsMerged == true)
                table.UnmergeCells(table.Rows[0]);

            table.Cells[0, -1].Style = "_HEADER";
            for (int row = 1; row < checks.Count + 1; row++)
                table.Cells[row, -1].Style = "_DATA";

            table.Rows[0].Height = PipeTopCheckTableStyle.HeaderRowHeight;
            for (int row = 1; row < checks.Count + 1; row++)
                table.Rows[row].Height = PipeTopCheckTableStyle.DataRowHeight;

            SetHeader(table);
            for (int row = 0; row < checks.Count; row++)
                SetDataRow(table, row + 1, checks[row]);

            ApplyCellFormatting(table);
            table.GenerateLayout();
        }

        private static void ApplyCellFormatting(Table table)
        {
            for (int row = 0; row < table.Rows.Count; row++)
            {
                for (int column = 0; column < table.Columns.Count; column++)
                {
                    Cell cell = table.Cells[row, column];
                    cell.TextHeight = row == 0
                        ? PipeTopCheckTableStyle.HeaderTextHeight
                        : PipeTopCheckTableStyle.DataTextHeight;
                    cell.Alignment = CellAlignment.MiddleCenter;
                }
            }
        }

        private static List<PipeTopCheckData.Snapshot> SelectChecks(Editor ed, Database db, string prompt)
        {
            PromptSelectionOptions options = new PromptSelectionOptions
            {
                MessageForAdding = prompt,
                AllowDuplicates = false,
                RejectObjectsOnLockedLayers = true
            };

            SelectionFilter filter = new SelectionFilter(new[]
            {
                new TypedValue((int)DxfCode.Start, "MTEXT")
            });

            PromptSelectionResult selection = ed.GetSelection(options, filter);
            if (selection.Status != PromptStatus.OK)
                return new List<PipeTopCheckData.Snapshot>();

            List<PipeTopCheckData.Snapshot> checks = new List<PipeTopCheckData.Snapshot>();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                foreach (SelectedObject? selected in selection.Value)
                {
                    if (selected == null)
                        continue;

                    if (tr.GetObject(selected.ObjectId, OpenMode.ForRead, false) is MText label &&
                        PipeTopCheckData.TryRead(label, tr, out PipeTopCheckData.Snapshot snapshot))
                    {
                        checks.Add(snapshot);
                    }
                }

                tr.Commit();
            }

            return checks;
        }

        private static Dictionary<Guid, PipeTopCheckData.Snapshot> FindChecksById(
            Database db,
            Transaction tr,
            IEnumerable<Guid> ids)
        {
            HashSet<Guid> wanted = new HashSet<Guid>(ids);
            Dictionary<Guid, PipeTopCheckData.Snapshot> found = new Dictionary<Guid, PipeTopCheckData.Snapshot>();

            BlockTable blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId blockId in blockTable)
            {
                BlockTableRecord block = (BlockTableRecord)tr.GetObject(blockId, OpenMode.ForRead);
                foreach (ObjectId objectId in block)
                {
                    if (tr.GetObject(objectId, OpenMode.ForRead, false) is MText label &&
                        PipeTopCheckData.TryRead(label, tr, out PipeTopCheckData.Snapshot snapshot) &&
                        wanted.Contains(snapshot.Id))
                    {
                        found[snapshot.Id] = snapshot;
                    }
                }
            }

            return found;
        }

        private static List<PipeTopCheckData.Snapshot> SortChecks(List<PipeTopCheckData.Snapshot> checks)
            => checks
                .OrderBy(c => ParseExhibitId(c.ExhibitId))
                .ThenBy(c => c.ExhibitId, StringComparer.Ordinal)
                .ThenByDescending(c => c.CheckPointLocation.Y)
                .ThenBy(c => c.CheckPointLocation.X)
                .ToList();

        private static int ParseExhibitId(string value)
            => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
                ? number
                : int.MaxValue;

        private static void SetHeader(Table table)
        {
            string[] headers = { "POINT", "PLAN - TOP", "SURV - TOP", "DIFF", "PIPE STA.", "OFFSET" };
            for (int column = 0; column < headers.Length; column++)
                table.Cells[0, column].TextString = headers[column];
        }

        private static void SetDataRow(Table table, int row, PipeTopCheckData.Snapshot check)
        {
            table.Cells[row, 0].TextString = check.ExhibitId;
            table.Cells[row, 1].TextString = FormatElevation(check.PlanTopElevation);
            table.Cells[row, 2].TextString = FormatElevation(check.SurveyTopElevation);
            table.Cells[row, 3].TextString = FormatDifference(check.Difference);
            table.Cells[row, 4].TextString = FormatNumber(check.Station);
            table.Cells[row, 5].TextString = FormatNumber(check.Offset);
        }

        private static string FormatElevation(double value)
            => double.IsNaN(value) ? "-" : value.ToString("0.000", CultureInfo.InvariantCulture);

        private static string FormatDifference(double value)
            => double.IsNaN(value) ? "-" : value.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);

        private static string FormatNumber(double value)
            => double.IsNaN(value) ? "-" : value.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
