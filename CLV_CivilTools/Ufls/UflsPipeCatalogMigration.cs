using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDbObject = Autodesk.AutoCAD.DatabaseServices.DBObject;

namespace CLV_CivilTools.Ufls
{
    /// <summary>
    /// Non-destructive inventory used as the first phase of Pipe Network catalog migration.
    /// It inventories the parts actually present in the current drawing, grouped by
    /// family/size and, for structures, by actual physical dimensions. No drawing data
    /// or Parts List is modified by this command.
    /// </summary>
    public static class UflsPipeCatalogMigrationCommands
    {
        [CommandMethod("UFLS", "UFLS-PIPE-CATALOG-ANALYZE", CommandFlags.Modal)]
        [CommandMethod("UFLS", "MIGRATE-PIPE-CATALOG-ANALYZE", CommandFlags.Modal)]
        public static void AnalyzePipeCatalogMigration()
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null)
                return;

            Database db = doc.Database;
            Editor ed = doc.Editor;

            try
            {
                using (doc.LockDocument())
                using (Transaction tr = db.TransactionManager.StartTransaction())
                {
                    CivilDocument civilDoc = CivilApplication.ActiveDocument;
                    ObjectIdCollection networkIds = civilDoc.GetPipeNetworkIds();

                    if (networkIds.Count == 0)
                    {
                        ed.WriteMessage("\nPIPE CATALOG ANALYZE: no pipe networks found in the current drawing.");
                        return;
                    }

                    var report = new PipeCatalogMigrationInventory();

                    foreach (ObjectId networkId in networkIds)
                    {
                        if (tr.GetObject(networkId, OpenMode.ForRead, false) is not Network network)
                            continue;

                        string networkName = GetStringProperty(network, "Name");
                        string partsListName = ResolvePartsListName(tr, network);
                        report.Networks.Add(new NetworkInventory(
                            networkId,
                            networkName,
                            partsListName));

                        foreach (ObjectId pipeId in network.GetPipeIds())
                        {
                            if (tr.GetObject(pipeId, OpenMode.ForRead, false) is not Pipe pipe)
                                continue;

                            report.AddPipe(pipe, networkName);
                        }

                        foreach (ObjectId structureId in network.GetStructureIds())
                        {
                            if (tr.GetObject(structureId, OpenMode.ForRead, false) is not Structure structure)
                                continue;

                            report.AddStructure(structure, networkName);
                        }
                    }

                    WriteReport(ed, report);
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPIPE CATALOG ANALYZE error: {ex.Message}");
            }
        }

        private static void WriteReport(Editor ed, PipeCatalogMigrationInventory report)
        {
            ed.WriteMessage("\n");
            ed.WriteMessage("\n============================================================");
            ed.WriteMessage("\nCLV PIPE CATALOG MIGRATION - NON-DESTRUCTIVE ANALYSIS");
            ed.WriteMessage("\n============================================================");
            ed.WriteMessage($"\nNetworks:   {report.Networks.Count}");
            ed.WriteMessage($"\nPipes:      {report.PipeInstances}");
            ed.WriteMessage($"\nStructures: {report.StructureInstances}");
            ed.WriteMessage($"\nPipe groups:      {report.PipeGroups.Count}");
            ed.WriteMessage($"\nStructure groups: {report.StructureGroups.Count}");

            ed.WriteMessage("\n\nNETWORKS");
            foreach (NetworkInventory network in report.Networks)
            {
                string partsList = string.IsNullOrWhiteSpace(network.PartsListName)
                    ? "<unable to resolve>"
                    : network.PartsListName;

                ed.WriteMessage(
                    $"\n  {network.Name} | Parts List: {partsList}");
            }

            ed.WriteMessage("\n\nPIPE PART GROUPS");
            if (report.PipeGroups.Count == 0)
            {
                ed.WriteMessage("\n  <none>");
            }
            else
            {
                int index = 1;
                foreach (PipePartGroup group in report.PipeGroups.Values
                             .OrderBy(g => g.FamilyName)
                             .ThenBy(g => g.SizeName))
                {
                    ed.WriteMessage(
                        $"\n  P{index++:000} | Count={group.Count} | Family='{group.FamilyName}' | Size='{group.SizeName}'" +
                        $" | Shape={group.Shape} | ID/W={FormatDimension(group.InnerDiameterOrWidth)} | H={FormatDimension(group.InnerHeight)}");

                    if (group.NetworkNames.Count > 0)
                        ed.WriteMessage($" | Networks={string.Join(", ", group.NetworkNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}");
                }
            }

            ed.WriteMessage("\n\nSTRUCTURE PART GROUPS");
            if (report.StructureGroups.Count == 0)
            {
                ed.WriteMessage("\n  <none>");
            }
            else
            {
                int index = 1;
                foreach (StructurePartGroup group in report.StructureGroups.Values
                             .OrderBy(g => g.FamilyName)
                             .ThenBy(g => g.SizeName))
                {
                    ed.WriteMessage(
                        $"\n  S{index++:000} | Count={group.Count} | Family='{group.FamilyName}' | Size='{group.SizeName}'" +
                        $" | Physical variants={group.Variants.Count}");

                    foreach (StructurePhysicalVariant variant in group.Variants.Values
                                 .OrderBy(v => v.InnerLength)
                                 .ThenBy(v => v.InnerWidth)
                                 .ThenBy(v => v.InnerDiameter)
                                 .ThenBy(v => v.InnerHeight))
                    {
                        ed.WriteMessage(
                            $"\n       Count={variant.Count}" +
                            $" | Inner L={FormatDimension(variant.InnerLength)}" +
                            $" W={FormatDimension(variant.InnerWidth)}" +
                            $" H={FormatDimension(variant.InnerHeight)}" +
                            $" ID={FormatDimension(variant.InnerDiameter)}");
                    }

                    if (group.NetworkNames.Count > 0)
                        ed.WriteMessage($"\n       Networks={string.Join(", ", group.NetworkNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}");
                }
            }

            ed.WriteMessage("\n\nANALYSIS STATUS");
            ed.WriteMessage("\n  No pipe, structure, Parts List, or catalog data was changed.");
            ed.WriteMessage("\n  Family + size combinations are grouped so the next phase can map once per legacy part identity.");
            ed.WriteMessage("\n  Structure physical dimensions are recorded separately so custom box dimensions are preserved.");
            ed.WriteMessage("\n  This phase deliberately does not guess whether a dimension is standard or custom; that comparison belongs to target-family mapping.");
            ed.WriteMessage("\n");
        }

        private static string ResolvePartsListName(Transaction tr, Network network)
        {
            ObjectId partsListId = GetObjectIdProperty(network, "PartsListId");
            if (partsListId.IsNull)
                return string.Empty;

            if (tr.GetObject(partsListId, OpenMode.ForRead, false) is AcDbObject partsListObject)
                return GetStringProperty(partsListObject, "Name");

            return string.Empty;
        }

        private static ObjectId GetObjectIdProperty(object source, string propertyName)
        {
            try
            {
                object? value = source.GetType().GetProperty(propertyName)?.GetValue(source);
                return value is ObjectId id ? id : ObjectId.Null;
            }
            catch
            {
                return ObjectId.Null;
            }
        }

        private static string GetStringProperty(object source, string propertyName)
        {
            try
            {
                object? value = source.GetType().GetProperty(propertyName)?.GetValue(source);
                return value?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string FormatDimension(double value)
        {
            if (Math.Abs(value) < 1e-9)
                return "-";

            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private sealed class PipeCatalogMigrationInventory
        {
            public List<NetworkInventory> Networks { get; } = new();
            public Dictionary<PipeGroupKey, PipePartGroup> PipeGroups { get; } = new();
            public Dictionary<StructureGroupKey, StructurePartGroup> StructureGroups { get; } = new();

            public int PipeInstances { get; private set; }
            public int StructureInstances { get; private set; }

            public void AddPipe(Pipe pipe, string networkName)
            {
                PipeInstances++;

                var key = new PipeGroupKey(
                    Normalize(pipe.PartFamilyName),
                    Normalize(pipe.PartSizeName),
                    Normalize(pipe.CrossSectionalShape.ToString()),
                    Round(pipe.InnerDiameterOrWidth),
                    Round(pipe.InnerHeight));

                if (!PipeGroups.TryGetValue(key, out PipePartGroup? group))
                {
                    group = new PipePartGroup(
                        pipe.PartFamilyName,
                        pipe.PartSizeName,
                        pipe.CrossSectionalShape.ToString(),
                        pipe.InnerDiameterOrWidth,
                        pipe.InnerHeight);
                    PipeGroups.Add(key, group);
                }

                group.Count++;
                group.NetworkNames.Add(networkName);
            }

            public void AddStructure(Structure structure, string networkName)
            {
                StructureInstances++;

                var key = new StructureGroupKey(
                    Normalize(structure.PartFamilyName),
                    Normalize(structure.PartSizeName));

                if (!StructureGroups.TryGetValue(key, out StructurePartGroup? group))
                {
                    group = new StructurePartGroup(
                        structure.PartFamilyName,
                        structure.PartSizeName);
                    StructureGroups.Add(key, group);
                }

                group.Count++;
                group.NetworkNames.Add(networkName);
                group.AddVariant(
                    structure.InnerLength,
                    structure.InnerDiameterOrWidth,
                    structure.InnerHeight,
                    structure.InnerDiameterOrWidth > 0.0 ? 0.0 : structure.DiameterOrWidth);
            }

            private static string Normalize(string value)
                => value?.Trim().ToUpperInvariant() ?? string.Empty;

            private static double Round(double value)
                => Math.Round(value, 6, MidpointRounding.AwayFromZero);
        }

        private sealed record NetworkInventory(ObjectId Id, string Name, string PartsListName);

        private readonly record struct PipeGroupKey(
            string FamilyName,
            string SizeName,
            string Shape,
            double InnerDiameterOrWidth,
            double InnerHeight);

        private sealed class PipePartGroup
        {
            public PipePartGroup(
                string familyName,
                string sizeName,
                string shape,
                double innerDiameterOrWidth,
                double innerHeight)
            {
                FamilyName = familyName;
                SizeName = sizeName;
                Shape = shape;
                InnerDiameterOrWidth = innerDiameterOrWidth;
                InnerHeight = innerHeight;
            }

            public string FamilyName { get; }
            public string SizeName { get; }
            public string Shape { get; }
            public double InnerDiameterOrWidth { get; }
            public double InnerHeight { get; }
            public int Count { get; set; }
            public HashSet<string> NetworkNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private readonly record struct StructureGroupKey(
            string FamilyName,
            string SizeName);

        private sealed class StructurePartGroup
        {
            public StructurePartGroup(string familyName, string sizeName)
            {
                FamilyName = familyName;
                SizeName = sizeName;
            }

            public string FamilyName { get; }
            public string SizeName { get; }
            public int Count { get; set; }
            public HashSet<string> NetworkNames { get; } = new(StringComparer.OrdinalIgnoreCase);
            public Dictionary<StructurePhysicalKey, StructurePhysicalVariant> Variants { get; } = new();

            public void AddVariant(
                double innerLength,
                double innerWidth,
                double innerHeight,
                double innerDiameter)
            {
                var key = new StructurePhysicalKey(
                    Round(innerLength),
                    Round(innerWidth),
                    Round(innerHeight),
                    Round(innerDiameter));

                if (!Variants.TryGetValue(key, out StructurePhysicalVariant? variant))
                {
                    variant = new StructurePhysicalVariant(
                        innerLength,
                        innerWidth,
                        innerHeight,
                        innerDiameter);
                    Variants.Add(key, variant);
                }

                variant.Count++;
            }

            private static double Round(double value)
                => Math.Round(value, 6, MidpointRounding.AwayFromZero);
        }

        private readonly record struct StructurePhysicalKey(
            double InnerLength,
            double InnerWidth,
            double InnerHeight,
            double InnerDiameter);

        private sealed class StructurePhysicalVariant
        {
            public StructurePhysicalVariant(
                double innerLength,
                double innerWidth,
                double innerHeight,
                double innerDiameter)
            {
                InnerLength = innerLength;
                InnerWidth = innerWidth;
                InnerHeight = innerHeight;
                InnerDiameter = innerDiameter;
            }

            public double InnerLength { get; }
            public double InnerWidth { get; }
            public double InnerHeight { get; }
            public double InnerDiameter { get; }
            public int Count { get; set; }
        }
    }
}
