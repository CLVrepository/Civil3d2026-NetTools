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
using Autodesk.Civil.DatabaseServices.Styles;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;
using AcDbObject = Autodesk.AutoCAD.DatabaseServices.DBObject;

namespace CLV_CivilTools.Ufls
{
    /// <summary>
    /// Non-destructive inventory used as the first phase of Pipe Network catalog migration.
    /// It inventories the parts actually present in the current drawing, grouped by
    /// family/size and, for structures, by actual physical dimensions. It also inventories
    /// a user-selected target Parts List so later phases can map legacy parts only to parts
    /// explicitly allowed by that target list. No drawing data or Parts List is modified.
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

                    ObjectId targetPartsListId = SelectTargetPartsList(ed, civilDoc, tr);
                    if (targetPartsListId.IsNull)
                    {
                        ed.WriteMessage("\nPIPE CATALOG ANALYZE: target Parts List selection cancelled.");
                        return;
                    }

                    TargetPartsListInventory targetInventory = BuildTargetPartsListInventory(tr, targetPartsListId);
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

                    WriteReport(ed, report, targetInventory);
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPIPE CATALOG ANALYZE error: {ex.Message}");
            }
        }

        private static ObjectId SelectTargetPartsList(Editor ed, CivilDocument civilDoc, Transaction tr)
        {
            PartsListCollection collection = civilDoc.Styles.PartsListSet;
            var choices = new List<(ObjectId Id, string Name)>();

            foreach (ObjectId id in collection)
            {
                try
                {
                    if (tr.GetObject(id, OpenMode.ForRead, false) is PartsList partsList)
                        choices.Add((id, partsList.Name));
                }
                catch
                {
                    // Ignore an unreadable/stale Parts List entry rather than aborting selection.
                }
            }

            choices = choices
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (choices.Count == 0)
            {
                ed.WriteMessage("\nPIPE CATALOG ANALYZE: no usable Parts Lists were found in the drawing.");
                return ObjectId.Null;
            }

            ed.WriteMessage("\n\nSELECT TARGET PARTS LIST");
            ed.WriteMessage("\nThe selected list is the authority for migration candidates; the full catalog is not searched.");

            for (int i = 0; i < choices.Count; i++)
                ed.WriteMessage($"\n  [{i + 1}] {choices[i].Name}");

            var options = new PromptIntegerOptions("\nEnter target Parts List number: ")
            {
                AllowNone = false,
                AllowNegative = false,
                AllowZero = false,
                LowerLimit = 1,
                UpperLimit = choices.Count
            };

            PromptIntegerResult result = ed.GetInteger(options);
            if (result.Status != PromptStatus.OK)
                return ObjectId.Null;

            return choices[result.Value - 1].Id;
        }

        private static TargetPartsListInventory BuildTargetPartsListInventory(Transaction tr, ObjectId partsListId)
        {
            if (tr.GetObject(partsListId, OpenMode.ForRead, false) is not PartsList partsList)
                throw new InvalidOperationException("Selected target Parts List could not be opened.");

            var inventory = new TargetPartsListInventory(partsListId, partsList.Name);
            AddTargetFamilies(tr, partsList, DomainType.Pipe, inventory.PipeFamilies);
            AddTargetFamilies(tr, partsList, DomainType.Structure, inventory.StructureFamilies);
            return inventory;
        }

        private static void AddTargetFamilies(
            Transaction tr,
            PartsList partsList,
            DomainType domain,
            List<TargetPartFamilyInventory> destination)
        {
            ObjectIdCollection familyIds = partsList.GetPartFamilyIdsByDomain(domain);

            foreach (ObjectId familyId in familyIds)
            {
                try
                {
                    if (tr.GetObject(familyId, OpenMode.ForRead, false) is not PartFamily family)
                        continue;

                    var familyInventory = new TargetPartFamilyInventory(
                        familyId,
                        GetPartIdentityProperty(family, "Name"),
                        domain.ToString(),
                        domain == DomainType.Pipe
                            ? GetStringProperty(family, "SweptShape")
                            : GetStringProperty(family, "BoundingShape"));

                    for (int i = 0; i < family.PartSizeCount; i++)
                    {
                        try
                        {
                            ObjectId sizeId = family[i];
                            if (tr.GetObject(sizeId, OpenMode.ForRead, false) is PartSize size)
                            {
                                familyInventory.Sizes.Add(new TargetPartSizeInventory(
                                    sizeId,
                                    GetPartIdentityProperty(size, "Name"),
                                    GetStringProperty(size, "Description")));
                            }
                        }
                        catch
                        {
                            // Keep inventorying other sizes if one target size is unreadable.
                        }
                    }

                    destination.Add(familyInventory);
                }
                catch
                {
                    // Keep inventorying other families if one target family is unreadable.
                }
            }
        }

        private static void WriteReport(
            Editor ed,
            PipeCatalogMigrationInventory report,
            TargetPartsListInventory target)
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

                ed.WriteMessage($"\n  {network.Name} | Parts List: {partsList}");
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

            WriteTargetPartsListReport(ed, target);

            ed.WriteMessage("\n\nANALYSIS STATUS");
            ed.WriteMessage("\n  No pipe, structure, Parts List, or catalog data was changed.");
            ed.WriteMessage("\n  The target Parts List was selected interactively and is the authority for future migration candidates.");
            ed.WriteMessage("\n  Family + size combinations are grouped so the next phase can map once per legacy part identity.");
            ed.WriteMessage("\n  Structure physical dimensions are recorded separately so custom box dimensions are preserved.");
            ed.WriteMessage("\n  Unresolvable legacy family/size names are retained as <invalid/unresolved> so one bad catalog reference does not abort the scan.");
            ed.WriteMessage("\n  Shape-specific structure dimensions that Civil 3D does not expose for a given structure are left blank instead of aborting the scan.");
            ed.WriteMessage("\n  This phase deliberately does not change parts or guess ambiguous mappings.");
            ed.WriteMessage("\n");
        }

        private static void WriteTargetPartsListReport(Editor ed, TargetPartsListInventory target)
        {
            int pipeSizeCount = target.PipeFamilies.Sum(f => f.Sizes.Count);
            int structureSizeCount = target.StructureFamilies.Sum(f => f.Sizes.Count);

            ed.WriteMessage("\n\nTARGET PARTS LIST INVENTORY");
            ed.WriteMessage($"\n  Target: {target.Name}");
            ed.WriteMessage($"\n  Pipe families: {target.PipeFamilies.Count} | Pipe sizes: {pipeSizeCount}");
            ed.WriteMessage($"\n  Structure families: {target.StructureFamilies.Count} | Structure sizes: {structureSizeCount}");

            ed.WriteMessage("\n\n  TARGET PIPE FAMILIES / SIZES");
            WriteTargetFamilies(ed, target.PipeFamilies);

            ed.WriteMessage("\n\n  TARGET STRUCTURE FAMILIES / SIZES");
            WriteTargetFamilies(ed, target.StructureFamilies);
        }

        private static void WriteTargetFamilies(Editor ed, IEnumerable<TargetPartFamilyInventory> families)
        {
            var ordered = families
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (ordered.Count == 0)
            {
                ed.WriteMessage("\n    <none>");
                return;
            }

            foreach (TargetPartFamilyInventory family in ordered)
            {
                string shape = string.IsNullOrWhiteSpace(family.Shape)
                    ? string.Empty
                    : $" | Shape={family.Shape}";

                ed.WriteMessage($"\n    Family='{family.Name}' | Sizes={family.Sizes.Count}{shape}");

                foreach (TargetPartSizeInventory size in family.Sizes
                             .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase))
                {
                    ed.WriteMessage($"\n      - {size.Name}");
                }
            }
        }

        private static string ResolvePartsListName(Transaction tr, Network network)
        {
            string directName = GetStringProperty(network, "PartsListName");
            if (!string.IsNullOrWhiteSpace(directName))
                return directName;

            ObjectId partsListId = GetObjectIdProperty(network, "PartsListId");
            if (partsListId.IsNull)
                return string.Empty;

            try
            {
                if (tr.GetObject(partsListId, OpenMode.ForRead, false) is AcDbObject partsListObject)
                    return GetStringProperty(partsListObject, "Name");
            }
            catch
            {
                // Older drawings can retain a stale or invalid PartsListId.
            }

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

        private static double GetDoubleProperty(object source, string propertyName)
        {
            try
            {
                object? value = source.GetType().GetProperty(propertyName)?.GetValue(source);
                return value is double number ? number : 0.0;
            }
            catch
            {
                return 0.0;
            }
        }

        private static string GetPartIdentityProperty(object source, string propertyName)
        {
            string value = GetStringProperty(source, propertyName);
            return string.IsNullOrWhiteSpace(value) ? "<invalid/unresolved>" : value;
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

                string familyName = GetPartIdentityProperty(pipe, "PartFamilyName");
                string sizeName = GetPartIdentityProperty(pipe, "PartSizeName");

                var key = new PipeGroupKey(
                    Normalize(familyName),
                    Normalize(sizeName),
                    Normalize(pipe.CrossSectionalShape.ToString()),
                    Round(pipe.InnerDiameterOrWidth),
                    Round(pipe.InnerHeight));

                if (!PipeGroups.TryGetValue(key, out PipePartGroup? group))
                {
                    group = new PipePartGroup(
                        familyName,
                        sizeName,
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

                string familyName = GetPartIdentityProperty(structure, "PartFamilyName");
                string sizeName = GetPartIdentityProperty(structure, "PartSizeName");

                var key = new StructureGroupKey(
                    Normalize(familyName),
                    Normalize(sizeName));

                if (!StructureGroups.TryGetValue(key, out StructurePartGroup? group))
                {
                    group = new StructurePartGroup(familyName, sizeName);
                    StructureGroups.Add(key, group);
                }

                group.Count++;
                group.NetworkNames.Add(networkName);

                double innerLength = GetDoubleProperty(structure, "InnerLength");
                double innerWidth = GetDoubleProperty(structure, "InnerDiameterOrWidth");
                double height = GetDoubleProperty(structure, "Height");
                double outerDiameterOrWidth = GetDoubleProperty(structure, "DiameterOrWidth");
                double innerDiameter = innerWidth > 0.0 && innerLength <= 0.0
                    ? innerWidth
                    : (innerWidth > 0.0 ? 0.0 : outerDiameterOrWidth);

                group.AddVariant(innerLength, innerWidth, height, innerDiameter);
            }

            private static string Normalize(string value)
                => value?.Trim().ToUpperInvariant() ?? string.Empty;

            private static double Round(double value)
                => Math.Round(value, 6, MidpointRounding.AwayFromZero);
        }

        private sealed class TargetPartsListInventory
        {
            public TargetPartsListInventory(ObjectId id, string name)
            {
                Id = id;
                Name = name;
            }

            public ObjectId Id { get; }
            public string Name { get; }
            public List<TargetPartFamilyInventory> PipeFamilies { get; } = new();
            public List<TargetPartFamilyInventory> StructureFamilies { get; } = new();
        }

        private sealed class TargetPartFamilyInventory
        {
            public TargetPartFamilyInventory(ObjectId id, string name, string domain, string shape)
            {
                Id = id;
                Name = name;
                Domain = domain;
                Shape = shape;
            }

            public ObjectId Id { get; }
            public string Name { get; }
            public string Domain { get; }
            public string Shape { get; }
            public List<TargetPartSizeInventory> Sizes { get; } = new();
        }

        private sealed record TargetPartSizeInventory(ObjectId Id, string Name, string Description);
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

        private readonly record struct StructureGroupKey(string FamilyName, string SizeName);

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

            public void AddVariant(double innerLength, double innerWidth, double innerHeight, double innerDiameter)
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
