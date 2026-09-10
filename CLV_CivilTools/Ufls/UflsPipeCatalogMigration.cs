using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

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
    /// Pipe Network catalog migration analysis and controlled migration.
    /// The user-selected Parts List is the authority for all target candidates.
    /// </summary>
    public static class UflsPipeCatalogMigrationCommands
    {
        private const string UnresolvedPart = "<invalid/unresolved>";

        private static readonly string[] StopWords =
        {
            "PIPE", "STRUCTURE", "INCH", "INCHES", "FOOT", "FEET", "WALL", "WALLS",
            "WITH", "AND", "THE", "GENERAL", "CUSTOM", "SIZE", "TYPE"
        };

        [CommandMethod("UFLS", "UFLS-PIPE-CATALOG-ANALYZE", CommandFlags.Modal)]
        [CommandMethod("UFLS", "MIGRATE-PIPE-CATALOG-ANALYZE", CommandFlags.Modal)]
        public static void AnalyzePipeCatalogMigration()
        {
            RunCatalogTool(false);
        }

        [CommandMethod("UFLS", "UFLS-PIPE-CATALOG-MIGRATE", CommandFlags.Modal)]
        [CommandMethod("UFLS", "MIGRATE-PIPE-CATALOG", CommandFlags.Modal)]
        public static void MigratePipeCatalog()
        {
            RunCatalogTool(true);
        }

        private static void RunCatalogTool(bool migrate)
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
                        ed.WriteMessage("\nPIPE CATALOG: no pipe networks found in the current drawing.");
                        return;
                    }

                    ObjectId targetPartsListId = SelectTargetPartsList(ed, civilDoc, tr);
                    if (targetPartsListId.IsNull)
                    {
                        ed.WriteMessage("\nPIPE CATALOG: target Parts List selection cancelled.");
                        return;
                    }

                    TargetPartsListInventory target = BuildTargetPartsListInventory(tr, targetPartsListId);
                    PipeCatalogMigrationInventory report = BuildDrawingInventory(tr, networkIds);

                    if (!migrate)
                    {
                        WriteAnalysisReport(ed, report, target);
                        tr.Commit();
                        return;
                    }

                    RunControlledMigration(ed, tr, report, target);
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\nPIPE CATALOG error: {ex.Message}");
            }
        }

        private static PipeCatalogMigrationInventory BuildDrawingInventory(Transaction tr, ObjectIdCollection networkIds)
        {
            var report = new PipeCatalogMigrationInventory();

            foreach (ObjectId networkId in networkIds)
            {
                if (tr.GetObject(networkId, OpenMode.ForRead, false) is not Network network)
                    continue;

                string networkName = GetStringProperty(network, "Name");
                report.Networks.Add(new NetworkInventory(networkId, networkName, ResolvePartsListName(tr, network)));

                foreach (ObjectId pipeId in network.GetPipeIds())
                {
                    if (tr.GetObject(pipeId, OpenMode.ForRead, false) is Pipe pipe)
                        report.AddPipe(pipe, networkName);
                }

                foreach (ObjectId structureId in network.GetStructureIds())
                {
                    if (tr.GetObject(structureId, OpenMode.ForRead, false) is Structure structure)
                        report.AddStructure(structure, networkName);
                }
            }

            return report;
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
                    // Ignore stale/unreadable list entries.
                }
            }

            choices = choices.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (choices.Count == 0)
                return ObjectId.Null;

            ed.WriteMessage("\n\nSELECT TARGET PARTS LIST");
            ed.WriteMessage("\nThe selected Parts List is the only authority for target parts.");
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
            return result.Status == PromptStatus.OK ? choices[result.Value - 1].Id : ObjectId.Null;
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

        private static void AddTargetFamilies(Transaction tr, PartsList partsList, DomainType domain, List<TargetPartFamilyInventory> destination)
        {
            foreach (ObjectId familyId in partsList.GetPartFamilyIdsByDomain(domain))
            {
                try
                {
                    if (tr.GetObject(familyId, OpenMode.ForRead, false) is not PartFamily family)
                        continue;

                    var targetFamily = new TargetPartFamilyInventory(
                        familyId,
                        GetPartIdentityProperty(family, "Name"),
                        domain.ToString(),
                        GetStringProperty(family, "PartType"),
                        domain == DomainType.Pipe ? GetStringProperty(family, "SweptShape") : GetStringProperty(family, "BoundingShape"));

                    RefreshTargetFamilySizes(tr, targetFamily);
                    destination.Add(targetFamily);
                }
                catch
                {
                    // Continue with other target families.
                }
            }
        }

        private static void RefreshTargetFamilySizes(Transaction tr, TargetPartFamilyInventory targetFamily)
        {
            targetFamily.Sizes.Clear();
            if (tr.GetObject(targetFamily.Id, OpenMode.ForRead, false) is not PartFamily family)
                return;

            for (int i = 0; i < family.PartSizeCount; i++)
            {
                try
                {
                    ObjectId sizeId = family[i];
                    if (tr.GetObject(sizeId, OpenMode.ForRead, false) is PartSize size)
                    {
                        targetFamily.Sizes.Add(new TargetPartSizeInventory(
                            sizeId,
                            GetPartIdentityProperty(size, "Name"),
                            GetStringProperty(size, "Description")));
                    }
                }
                catch
                {
                    // Continue with other target sizes.
                }
            }
        }

        private static void WriteAnalysisReport(Editor ed, PipeCatalogMigrationInventory report, TargetPartsListInventory target)
        {
            ed.WriteMessage("\n\n============================================================");
            ed.WriteMessage("\nCLV PIPE CATALOG MIGRATION - NON-DESTRUCTIVE ANALYSIS");
            ed.WriteMessage("\n============================================================");
            ed.WriteMessage($"\nNetworks:   {report.Networks.Count}");
            ed.WriteMessage($"\nPipes:      {report.PipeInstances}");
            ed.WriteMessage($"\nStructures: {report.StructureInstances}");
            ed.WriteMessage($"\nPipe groups:      {report.PipeGroups.Count}");
            ed.WriteMessage($"\nStructure groups: {report.StructureGroups.Count}");

            ed.WriteMessage("\n\nNETWORKS");
            foreach (NetworkInventory network in report.Networks)
                ed.WriteMessage($"\n  {network.Name} | Parts List: {network.PartsListName}");

            ed.WriteMessage("\n\nPIPE PART GROUPS");
            int p = 1;
            foreach (PipePartGroup group in OrderedPipes(report))
            {
                ed.WriteMessage($"\n  P{p++:000} | Count={group.Count} | Family='{group.FamilyName}' | Size='{group.SizeName}'" +
                                $" | PartType={group.PartType} | Shape={group.Shape} | ID/W={FormatDimension(group.InnerDiameterOrWidth)} | H={FormatDimension(group.InnerHeight)}");
            }

            ed.WriteMessage("\n\nSTRUCTURE PART GROUPS");
            int s = 1;
            foreach (StructurePartGroup group in OrderedStructures(report))
            {
                ed.WriteMessage($"\n  S{s++:000} | Count={group.Count} | Family='{group.FamilyName}' | Size='{group.SizeName}'" +
                                $" | PartType={group.PartType} | Physical variants={group.Variants.Count}");
                foreach (StructurePhysicalVariant variant in group.Variants.Values)
                {
                    ed.WriteMessage($"\n       Count={variant.Count} | Inner L={FormatDimension(variant.InnerLength)}" +
                                    $" W={FormatDimension(variant.InnerWidth)} H={FormatDimension(variant.InnerHeight)}" +
                                    $" ID={FormatDimension(variant.InnerDiameter)} Wall={FormatDimension(variant.WallThickness)}");
                }
            }

            WriteCandidateReport(ed, report, target);

            ed.WriteMessage("\n\nANALYSIS STATUS");
            ed.WriteMessage("\n  No pipe, structure, Parts List, or catalog data was changed.");
            ed.WriteMessage("\n  Candidate rankings are advisory only.");
            ed.WriteMessage("\n  Unresolved legacy structures require user classification during migration.");
            ed.WriteMessage("\n  A structure swap requires a target size matching the legacy physical dimensions.");
        }

        private static void WriteCandidateReport(Editor ed, PipeCatalogMigrationInventory report, TargetPartsListInventory target)
        {
            ed.WriteMessage("\n\n============================================================");
            ed.WriteMessage("\nMIGRATION CANDIDATE MAPPING - NO CHANGES MADE");
            ed.WriteMessage("\n============================================================");
            ed.WriteMessage($"\nTarget Parts List: {target.Name}");

            ed.WriteMessage("\n\nPIPE CANDIDATES");
            int p = 1;
            foreach (PipePartGroup legacy in OrderedPipes(report))
                WriteCandidates(ed, $"P{p++:000}", legacy.SizeName, legacy.PartType, FindPipeCandidates(legacy, target.PipeFamilies));

            ed.WriteMessage("\n\nSTRUCTURE CANDIDATES");
            int s = 1;
            foreach (StructurePartGroup legacy in OrderedStructures(report))
            {
                string id = $"S{s++:000}";
                if (IsUnresolved(legacy.FamilyName))
                {
                    ed.WriteMessage($"\n  {id} | MANUAL CLASSIFICATION REQUIRED | Legacy='{legacy.SizeName}'");
                    ed.WriteMessage("\n       Old/default Civil 3D structures may represent a junction, drop inlet, access structure, or other current part.");
                    continue;
                }

                WriteCandidates(ed, id, legacy.SizeName, legacy.PartType, FindStructureCandidates(legacy, target.StructureFamilies));
            }
        }

        private static void WriteCandidates(Editor ed, string id, string legacySize, string legacyPartType, List<PartCandidate> candidates)
        {
            if (candidates.Count == 0)
            {
                ed.WriteMessage($"\n  {id} | NO MATCH | Legacy='{legacySize}'");
                return;
            }

            PartCandidate top = candidates[0];
            int secondScore = candidates.Count > 1 ? candidates[1].Score : int.MinValue;
            string confidence = GetConfidence(top.Score, secondScore);
            bool compatible = PartTypesCompatible(legacyPartType, top.PartType);

            if (confidence == "NO MATCH")
            {
                ed.WriteMessage($"\n  {id} | NO MATCH | Legacy='{legacySize}'");
                ed.WriteMessage($"\n       Closest rejected candidate: Family='{top.FamilyName}' | Size='{top.SizeName}' | Score={top.Score}");
                return;
            }

            string migrationStatus = compatible ? "SWAP-COMPATIBLE" : "MANUAL REPLACEMENT REQUIRED";
            ed.WriteMessage($"\n  {id} | {confidence} | {migrationStatus} | Legacy='{legacySize}'" +
                            $"\n       BEST: Family='{top.FamilyName}' | Size='{top.SizeName}' | TargetType={top.PartType} | Score={top.Score}" +
                            $"\n       Why: {string.Join("; ", top.Reasons)}");

            foreach (PartCandidate alt in candidates.Skip(1).Take(2))
                ed.WriteMessage($"\n       ALT : Family='{alt.FamilyName}' | Size='{alt.SizeName}' | TargetType={alt.PartType} | Score={alt.Score}");
        }

        private static void RunControlledMigration(Editor ed, Transaction tr, PipeCatalogMigrationInventory report, TargetPartsListInventory target)
        {
            ed.WriteMessage("\n\n============================================================");
            ed.WriteMessage("\nCLV PIPE CATALOG MIGRATION - CONTROLLED MIGRATION");
            ed.WriteMessage("\n============================================================");
            ed.WriteMessage($"\nTarget Parts List: {target.Name}");
            ed.WriteMessage("\nPipes and clearly identified structures can be swapped after confirmation.");
            ed.WriteMessage("\nUnresolved/default legacy structures require the user to choose the current target family.");
            ed.WriteMessage("\nIf the chosen family is a different Civil 3D PartType, the tool leaves it for manual replacement/reconnect.");
            ed.WriteMessage("\nFor like-for-like structure swaps, the target size must match the legacy physical dimensions before swapping.");

            var accepted = new List<AcceptedMigration>();
            int manualRequired = 0;
            int noMatch = 0;

            int p = 1;
            foreach (PipePartGroup legacy in OrderedPipes(report))
            {
                ReviewPipeMigrationGroup(ed, $"P{p++:000}", legacy,
                    FindPipeCandidates(legacy, target.PipeFamilies), accepted, ref manualRequired, ref noMatch);
            }

            int s = 1;
            foreach (StructurePartGroup legacy in OrderedStructures(report))
            {
                ReviewStructureMigrationGroup(ed, tr, $"S{s++:000}", legacy, target,
                    accepted, ref manualRequired, ref noMatch);
            }

            if (accepted.Count == 0)
            {
                ed.WriteMessage("\n\nNo automatic swaps were selected. Drawing unchanged.");
                ed.WriteMessage($"\nManual replacement groups: {manualRequired} | No-match groups: {noMatch}");
                return;
            }

            ed.WriteMessage("\n\nMIGRATION SUMMARY BEFORE COMMIT");
            ed.WriteMessage($"\n  Confirmed groups: {accepted.Count}");
            ed.WriteMessage($"\n  Parts to swap: {accepted.Sum(x => x.ObjectIds.Count)}");
            ed.WriteMessage($"\n  Manual replacement groups left untouched: {manualRequired}");
            ed.WriteMessage($"\n  No-match groups left untouched: {noMatch}");
            ed.WriteMessage($"\n  Networks will be assigned to Parts List: {target.Name}");

            if (!PromptYesNo(ed, "Proceed with these swaps and update network Parts List", false))
            {
                ed.WriteMessage("\nMigration cancelled. Drawing unchanged except any target sizes explicitly added during review.");
                return;
            }

            foreach (NetworkInventory networkInfo in report.Networks)
            {
                if (tr.GetObject(networkInfo.Id, OpenMode.ForWrite, false) is Network network)
                    network.PartsListId = target.Id;
            }

            int swapped = 0;
            int failed = 0;
            foreach (AcceptedMigration migration in accepted)
            {
                foreach (ObjectId id in migration.ObjectIds)
                {
                    try
                    {
                        if (tr.GetObject(id, OpenMode.ForWrite, false) is Part part)
                        {
                            part.SwapPartFamilyAndSize(migration.Candidate.FamilyId, migration.Candidate.SizeId);
                            swapped++;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        failed++;
                        ed.WriteMessage($"\n  FAILED {migration.GroupId} object {id.Handle}: {ex.Message}");
                    }
                }
            }

            ed.WriteMessage("\n\nMIGRATION RESULT");
            ed.WriteMessage($"\n  Swapped successfully: {swapped}");
            ed.WriteMessage($"\n  Swap failures: {failed}");
            ed.WriteMessage($"\n  Manual replacement groups untouched: {manualRequired}");
            ed.WriteMessage($"\n  No-match groups untouched: {noMatch}");
            ed.WriteMessage($"\n  Network Parts List: {target.Name}");
            if (failed > 0)
                ed.WriteMessage("\n  Review failed parts before saving the drawing.");
        }

        private static void ReviewPipeMigrationGroup(
            Editor ed,
            string groupId,
            PipePartGroup legacy,
            List<PartCandidate> candidates,
            List<AcceptedMigration> accepted,
            ref int manualRequired,
            ref int noMatch)
        {
            if (!TryGetHighCompatibleCandidate(ed, groupId, legacy.SizeName, legacy.PartType, candidates, out PartCandidate? top, ref manualRequired, ref noMatch))
                return;

            ed.WriteMessage($"\n\n  {groupId} | HIGH | {legacy.ObjectIds.Count} pipe(s)");
            ed.WriteMessage($"\n       FROM: '{legacy.SizeName}' | Type={legacy.PartType}");
            ed.WriteMessage($"\n       TO  : '{top!.FamilyName}' / '{top.SizeName}' | Type={top.PartType}");

            if (PromptYesNo(ed, $"Accept {groupId} mapping", false))
                accepted.Add(new AcceptedMigration(groupId, legacy.ObjectIds, top));
            else
                ed.WriteMessage($"\n       {groupId} skipped by user.");
        }

        private static void ReviewStructureMigrationGroup(
            Editor ed,
            Transaction tr,
            string groupId,
            StructurePartGroup legacy,
            TargetPartsListInventory target,
            List<AcceptedMigration> accepted,
            ref int manualRequired,
            ref int noMatch)
        {
            TargetPartFamilyInventory? selectedFamily;
            PartCandidate? suggested = null;

            if (IsUnresolved(legacy.FamilyName))
            {
                ed.WriteMessage($"\n\n  {groupId} | MANUAL CLASSIFICATION REQUIRED | {legacy.ObjectIds.Count} structure(s)");
                ed.WriteMessage($"\n       Legacy: '{legacy.SizeName}' | Type={legacy.PartType}");
                WriteLegacyStructureDimensions(ed, legacy);
                ed.WriteMessage("\n       This old/default part may have been used for a different modern structure type.");

                if (!PromptYesNo(ed, $"Choose the current target family for {groupId}", false))
                {
                    manualRequired++;
                    return;
                }

                selectedFamily = SelectTargetStructureFamily(ed, target.StructureFamilies);
                if (selectedFamily == null)
                {
                    ed.WriteMessage($"\n       {groupId} target-family selection cancelled.");
                    manualRequired++;
                    return;
                }
            }
            else
            {
                List<PartCandidate> candidates = FindStructureCandidates(legacy, target.StructureFamilies);
                if (candidates.Count == 0)
                {
                    ed.WriteMessage($"\n  {groupId} | NO MATCH | '{legacy.SizeName}' -> SKIPPED");
                    noMatch++;
                    return;
                }

                suggested = candidates[0];
                string confidence = GetConfidence(suggested.Score, candidates.Count > 1 ? candidates[1].Score : int.MinValue);
                if (confidence != "HIGH")
                {
                    ed.WriteMessage($"\n  {groupId} | {confidence} | '{legacy.SizeName}' -> MANUAL REVIEW");
                    manualRequired++;
                    return;
                }

                selectedFamily = target.StructureFamilies.FirstOrDefault(f => f.Id == suggested.FamilyId);
                if (selectedFamily == null)
                {
                    noMatch++;
                    return;
                }
            }

            if (!PartTypesCompatible(legacy.PartType, selectedFamily.PartType))
            {
                ed.WriteMessage($"\n  {groupId} | MANUAL REPLACEMENT REQUIRED");
                ed.WriteMessage($"\n       Legacy Type : {legacy.PartType}");
                ed.WriteMessage($"\n       Chosen Family: {selectedFamily.Name} | Type={selectedFamily.PartType}");
                ed.WriteMessage("\n       The modern part is a different Civil 3D PartType. Replace/reconnect this structure manually using the plans.");
                manualRequired++;
                return;
            }

            if (!TryGetSingleHorizontalSize(legacy, out StructurePhysicalVariant? representative))
            {
                ed.WriteMessage($"\n  {groupId} | MANUAL REVIEW REQUIRED");
                ed.WriteMessage("\n       This legacy group contains more than one horizontal structure size. It cannot be swapped as one group.");
                manualRequired++;
                return;
            }

            ObjectId matchingSizeId = FindMatchingStructureSize(tr, selectedFamily.Id, representative!);
            if (matchingSizeId.IsNull)
            {
                ed.WriteMessage($"\n  {groupId} | TARGET SIZE MISSING");
                WriteRequiredTargetSize(ed, representative!);

                if (!PromptYesNo(ed, $"Add this matching size to '{selectedFamily.Name}' in the target Parts List", true))
                {
                    ed.WriteMessage("\n       Size was not added; structure group left untouched.");
                    manualRequired++;
                    return;
                }

                matchingSizeId = TryAddMatchingStructureSize(ed, tr, selectedFamily, representative!);
                if (matchingSizeId.IsNull)
                {
                    ed.WriteMessage("\n       Civil 3D did not create a matching target size. Add the size manually in the Parts List, then rerun migration.");
                    manualRequired++;
                    return;
                }

                RefreshTargetFamilySizes(tr, selectedFamily);
            }

            string targetSizeName = GetObjectName(tr, matchingSizeId);
            var finalCandidate = new PartCandidate(
                selectedFamily.Id,
                matchingSizeId,
                selectedFamily.Name,
                targetSizeName,
                selectedFamily.PartType,
                suggested?.Score ?? 100,
                suggested?.Reasons ?? new List<string> { "user-selected target family; physical size matched" });

            ed.WriteMessage($"\n\n  {groupId} | READY | {legacy.ObjectIds.Count} structure(s)");
            ed.WriteMessage($"\n       FROM: '{legacy.SizeName}' | Type={legacy.PartType}");
            ed.WriteMessage($"\n       TO  : '{selectedFamily.Name}' / '{targetSizeName}' | Type={selectedFamily.PartType}");
            WriteRequiredTargetSize(ed, representative!);
            ed.WriteMessage("\n       Height is preserved as an instance condition; it is not used to create the catalog size.");

            if (PromptYesNo(ed, $"Accept {groupId} mapping", false))
                accepted.Add(new AcceptedMigration(groupId, legacy.ObjectIds, finalCandidate));
            else
                ed.WriteMessage($"\n       {groupId} skipped by user.");
        }

        private static bool TryGetHighCompatibleCandidate(
            Editor ed,
            string groupId,
            string legacySize,
            string legacyPartType,
            List<PartCandidate> candidates,
            out PartCandidate? top,
            ref int manualRequired,
            ref int noMatch)
        {
            top = null;
            if (candidates.Count == 0)
            {
                ed.WriteMessage($"\n  {groupId} | NO MATCH | '{legacySize}' -> SKIPPED");
                noMatch++;
                return false;
            }

            top = candidates[0];
            string confidence = GetConfidence(top.Score, candidates.Count > 1 ? candidates[1].Score : int.MinValue);
            if (confidence == "NO MATCH")
            {
                ed.WriteMessage($"\n  {groupId} | NO MATCH | '{legacySize}' -> SKIPPED");
                noMatch++;
                return false;
            }

            if (!PartTypesCompatible(legacyPartType, top.PartType))
            {
                ed.WriteMessage($"\n  {groupId} | MANUAL REPLACEMENT REQUIRED | Type {legacyPartType} -> {top.PartType}");
                manualRequired++;
                return false;
            }

            if (confidence != "HIGH")
            {
                ed.WriteMessage($"\n  {groupId} | {confidence} | '{legacySize}' -> SKIPPED (not HIGH confidence)");
                manualRequired++;
                return false;
            }

            return true;
        }

        private static TargetPartFamilyInventory? SelectTargetStructureFamily(Editor ed, IEnumerable<TargetPartFamilyInventory> families)
        {
            List<TargetPartFamilyInventory> choices = families
                .Where(f => !IsSeparatorFamily(f))
                .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (choices.Count == 0)
                return null;

            ed.WriteMessage("\n\n       TARGET STRUCTURE FAMILIES");
            for (int i = 0; i < choices.Count; i++)
                ed.WriteMessage($"\n         [{i + 1}] {choices[i].Name} | Type={choices[i].PartType} | Shape={choices[i].Shape}");

            var options = new PromptIntegerOptions("\n       Enter target family number: ")
            {
                AllowNone = false,
                AllowNegative = false,
                AllowZero = false,
                LowerLimit = 1,
                UpperLimit = choices.Count
            };

            PromptIntegerResult result = ed.GetInteger(options);
            return result.Status == PromptStatus.OK ? choices[result.Value - 1] : null;
        }

        private static bool TryGetSingleHorizontalSize(StructurePartGroup legacy, out StructurePhysicalVariant? representative)
        {
            representative = null;
            List<StructurePhysicalVariant> variants = legacy.Variants.Values.ToList();
            if (variants.Count == 0)
                return false;

            StructurePhysicalVariant first = variants[0];
            representative = first;
            return variants.All(v =>
                NearlyEqual(v.InnerLength, first.InnerLength) &&
                NearlyEqual(v.InnerWidth, first.InnerWidth) &&
                NearlyEqual(v.InnerDiameter, first.InnerDiameter) &&
                NearlyEqual(v.WallThickness, first.WallThickness));
        }

        private static ObjectId FindMatchingStructureSize(Transaction tr, ObjectId familyId, StructurePhysicalVariant legacy)
        {
            if (tr.GetObject(familyId, OpenMode.ForRead, false) is not PartFamily family)
                return ObjectId.Null;

            for (int i = 0; i < family.PartSizeCount; i++)
            {
                try
                {
                    ObjectId id = family[i];
                    if (tr.GetObject(id, OpenMode.ForRead, false) is PartSize size && StructureSizeMatches(size, legacy))
                        return id;
                }
                catch
                {
                    // Continue searching.
                }
            }

            return ObjectId.Null;
        }

        private static bool StructureSizeMatches(PartSize size, StructurePhysicalVariant legacy)
        {
            double wall = GetPartSizeValue(size, PartContextType.WallThickness);

            if (legacy.InnerLength > 0.0)
            {
                double innerLength = GetPartSizeValue(size, PartContextType.StructInnerLength);
                double innerWidth = GetPartSizeValue(size, PartContextType.StructInnerWidth);

                if (innerLength <= 0.0)
                {
                    double outerLength = GetPartSizeValue(size, PartContextType.StructLength);
                    if (outerLength > 0.0 && wall > 0.0)
                        innerLength = outerLength - (2.0 * wall);
                }

                if (innerWidth <= 0.0)
                {
                    double outerWidth = GetPartSizeValue(size, PartContextType.StructWidth);
                    if (outerWidth > 0.0 && wall > 0.0)
                        innerWidth = outerWidth - (2.0 * wall);
                }

                return innerLength > 0.0 && innerWidth > 0.0 &&
                       DimensionsMatchEitherOrientation(innerLength, innerWidth, legacy.InnerLength, legacy.InnerWidth) &&
                       WallMatchesWhenKnown(wall, legacy.WallThickness);
            }

            double innerDiameter = GetPartSizeValue(size, PartContextType.StructInnerDiameter);
            if (innerDiameter <= 0.0)
            {
                double outerDiameter = GetPartSizeValue(size, PartContextType.StructDiameter);
                if (outerDiameter > 0.0 && wall > 0.0)
                    innerDiameter = outerDiameter - (2.0 * wall);
            }

            return legacy.InnerDiameter > 0.0 && innerDiameter > 0.0 &&
                   NearlyEqual(innerDiameter, legacy.InnerDiameter) &&
                   WallMatchesWhenKnown(wall, legacy.WallThickness);
        }

        private static ObjectId TryAddMatchingStructureSize(
            Editor ed,
            Transaction tr,
            TargetPartFamilyInventory targetFamily,
            StructurePhysicalVariant legacy)
        {
            try
            {
                if (tr.GetObject(targetFamily.Id, OpenMode.ForWrite, false) is not PartFamily family)
                    return ObjectId.Null;

                int before = family.PartSizeCount;
                using var filter = new SizeFilterRecord(family);

                bool dimensionSet;
                if (legacy.InnerLength > 0.0)
                {
                    bool lengthSet = TrySetFilterValue(filter, PartContextType.StructInnerLength, legacy.InnerLength);
                    bool widthSet = TrySetFilterValue(filter, PartContextType.StructInnerWidth, legacy.InnerWidth);

                    if (!lengthSet && legacy.WallThickness > 0.0)
                        lengthSet = TrySetFilterValue(filter, PartContextType.StructLength, legacy.InnerLength + 2.0 * legacy.WallThickness);
                    if (!widthSet && legacy.WallThickness > 0.0)
                        widthSet = TrySetFilterValue(filter, PartContextType.StructWidth, legacy.InnerWidth + 2.0 * legacy.WallThickness);

                    dimensionSet = lengthSet && widthSet;
                }
                else
                {
                    bool diameterSet = TrySetFilterValue(filter, PartContextType.StructInnerDiameter, legacy.InnerDiameter);
                    if (!diameterSet && legacy.WallThickness > 0.0)
                        diameterSet = TrySetFilterValue(filter, PartContextType.StructDiameter, legacy.InnerDiameter + 2.0 * legacy.WallThickness);
                    dimensionSet = diameterSet;
                }

                if (legacy.WallThickness > 0.0)
                    TrySetFilterValue(filter, PartContextType.WallThickness, legacy.WallThickness);

                if (!dimensionSet)
                {
                    ed.WriteMessage("\n       Target family does not expose writable size parameters for the required dimensions.");
                    return ObjectId.Null;
                }

                family.AddPartSize(filter);
                int after = family.PartSizeCount;
                ed.WriteMessage($"\n       Parts List family size count: {before} -> {after}");

                ObjectId match = FindMatchingStructureSize(tr, targetFamily.Id, legacy);
                if (!match.IsNull)
                    ed.WriteMessage($"\n       Added/found matching size: {GetObjectName(tr, match)}");
                return match;
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n       Unable to add target size automatically: {ex.Message}");
                return ObjectId.Null;
            }
        }

        private static bool TrySetFilterValue(SizeFilterRecord filter, PartContextType context, double value)
        {
            try
            {
                SizeFilterField field = filter.GetParamByContextAndIndex(context, 0);
                if (field == null || field.IsReadOnly)
                    return false;
                field.Value = value;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static double GetPartSizeValue(PartSize size, PartContextType context)
        {
            try
            {
                using PartDataRecord record = size.SizeDataRecord;
                PartDataField field = record.GetDataFieldBy(context);
                if (field?.Value == null)
                    return 0.0;
                return Convert.ToDouble(field.Value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0.0;
            }
        }

        private static void WriteLegacyStructureDimensions(Editor ed, StructurePartGroup legacy)
        {
            foreach (StructurePhysicalVariant variant in legacy.Variants.Values)
                WriteRequiredTargetSize(ed, variant);
        }

        private static void WriteRequiredTargetSize(Editor ed, StructurePhysicalVariant variant)
        {
            if (variant.InnerLength > 0.0)
            {
                ed.WriteMessage($"\n       Required size: Inner L={FormatDimension(variant.InnerLength)} ft" +
                                $" x W={FormatDimension(variant.InnerWidth)} ft" +
                                $" | Wall={FormatDimension(variant.WallThickness)} ft");
            }
            else
            {
                ed.WriteMessage($"\n       Required size: Inner Diameter={FormatDimension(variant.InnerDiameter)} ft" +
                                $" | Wall={FormatDimension(variant.WallThickness)} ft");
            }
        }

        private static bool DimensionsMatchEitherOrientation(double aL, double aW, double bL, double bW)
            => (NearlyEqual(aL, bL) && NearlyEqual(aW, bW)) ||
               (NearlyEqual(aL, bW) && NearlyEqual(aW, bL));

        private static bool WallMatchesWhenKnown(double targetWall, double legacyWall)
            => legacyWall <= 0.0 || targetWall <= 0.0 || NearlyEqual(targetWall, legacyWall);

        private static bool NearlyEqual(double a, double b)
            => Math.Abs(a - b) <= 0.01;

        private static string GetObjectName(Transaction tr, ObjectId id)
        {
            try
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is AcDbObject obj)
                    return GetPartIdentityProperty(obj, "Name");
            }
            catch
            {
                // Ignore.
            }
            return "<unresolved target size>";
        }

        private static bool PromptYesNo(Editor ed, string message, bool defaultYes)
        {
            var options = new PromptKeywordOptions($"\n{message} [Yes/No] <{(defaultYes ? "Yes" : "No")}>: ")
            {
                AllowNone = true
            };
            options.Keywords.Add("Yes");
            options.Keywords.Add("No");
            options.Keywords.Default = defaultYes ? "Yes" : "No";

            PromptResult result = ed.GetKeywords(options);
            if (result.Status == PromptStatus.None)
                return defaultYes;
            return result.Status == PromptStatus.OK && result.StringResult.Equals("Yes", StringComparison.OrdinalIgnoreCase);
        }

        private static List<PartCandidate> FindPipeCandidates(PipePartGroup legacy, IEnumerable<TargetPartFamilyInventory> families)
        {
            var results = new List<PartCandidate>();
            double legacyDiameterInches = legacy.InnerDiameterOrWidth * 12.0;
            string legacyText = $"{legacy.FamilyName} {legacy.SizeName}";
            string material = DetectPipeMaterial(legacyText);

            foreach (TargetPartFamilyInventory family in families)
            {
                foreach (TargetPartSizeInventory size in family.Sizes)
                {
                    int score = 0;
                    var reasons = new List<string>();
                    string targetText = $"{family.Name} {size.Name} {size.Description}";

                    if (NamesEqual(legacy.SizeName, size.Name)) { score += 90; reasons.Add("exact normalized size name"); }
                    if (!string.IsNullOrEmpty(material) && ContainsWholeToken(targetText, material)) { score += 55; reasons.Add($"material/type keyword {material}"); }
                    if (ShapesCompatible(legacy.Shape, family.Shape)) { score += 20; reasons.Add("shape compatible"); }

                    double? targetInches = ExtractPrimaryInches(size.Name);
                    if (targetInches.HasValue && Math.Abs(targetInches.Value - legacyDiameterInches) < 0.11)
                    {
                        score += 60;
                        reasons.Add($"diameter {legacyDiameterInches:0.###} in matches");
                    }

                    score += TokenOverlapScore(legacyText, targetText, 6, 30, reasons);
                    if (family.Name.Equals("UNKNOWN Pipe", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(material) && material != "UNKNOWN")
                        score -= 30;

                    if (score > 0)
                        results.Add(new PartCandidate(family.Id, size.Id, family.Name, size.Name, family.PartType, score, reasons));
                }
            }

            return OrderCandidates(results);
        }

        private static List<PartCandidate> FindStructureCandidates(StructurePartGroup legacy, IEnumerable<TargetPartFamilyInventory> families)
        {
            var results = new List<PartCandidate>();
            string legacyText = $"{legacy.FamilyName} {legacy.SizeName}";
            string expectedShape = legacy.Variants.Values.Any(v => v.InnerLength > 0.0) ? "Box" : "Cylinder";
            string kind = DetectStructureKind(legacyText);
            bool legacySddi = ContainsWholeToken(legacyText, "SDDI");

            foreach (TargetPartFamilyInventory family in families)
            {
                if (IsSeparatorFamily(family))
                    continue;
                if (!string.IsNullOrWhiteSpace(family.Shape) && !ShapesCompatible(expectedShape, family.Shape))
                    continue;

                foreach (TargetPartSizeInventory size in family.Sizes)
                {
                    int score = 0;
                    var reasons = new List<string>();
                    string targetText = $"{family.Name} {size.Name} {size.Description}";

                    if (NamesEqual(legacy.FamilyName, family.Name)) { score += 100; reasons.Add("exact normalized family name"); }
                    if (NamesEqual(legacy.SizeName, size.Name)) { score += 90; reasons.Add("exact normalized size name"); }
                    if (ShapesCompatible(expectedShape, family.Shape)) { score += 20; reasons.Add("bounding shape compatible"); }
                    score += TokenOverlapScore(legacyText, targetText, 9, 63, reasons);

                    if (!string.IsNullOrEmpty(kind) && ContainsWholeToken(targetText, kind)) { score += 35; reasons.Add($"structure type keyword {kind}"); }
                    if (kind == "JUNCTION" && NamesEqual(family.Name, "GENERAL JUNCTION STRUCTURE")) { score += 85; reasons.Add("legacy generic junction favors GENERAL JUNCTION STRUCTURE"); }
                    if (legacySddi && NamesEqual(family.Name, "GENERAL INLET")) { score += 85; reasons.Add("legacy SDDI favors GENERAL INLET"); }
                    if (kind == "JUNCTION" && ContainsWholeToken(targetText, "MANHOLE") && !ContainsWholeToken(targetText, "JUNCTION")) score -= 35;

                    if (score > 0)
                        results.Add(new PartCandidate(family.Id, size.Id, family.Name, size.Name, family.PartType, score, reasons));
                }
            }

            return OrderCandidates(results);
        }

        private static List<PartCandidate> OrderCandidates(IEnumerable<PartCandidate> candidates)
            => candidates.OrderByDescending(c => c.Score)
                         .ThenBy(c => c.FamilyName, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(c => c.SizeName, StringComparer.OrdinalIgnoreCase)
                         .Take(5)
                         .ToList();

        private static string GetConfidence(int bestScore, int secondScore)
        {
            if (bestScore < 25) return "NO MATCH";
            int margin = secondScore == int.MinValue ? bestScore : bestScore - secondScore;
            if (bestScore >= 100 && margin >= 25) return "HIGH";
            if (bestScore >= 65 && margin >= 15) return "MEDIUM";
            return "AMBIGUOUS";
        }

        private static bool PartTypesCompatible(string legacyPartType, string targetPartType)
        {
            if (string.IsNullOrWhiteSpace(legacyPartType) || string.IsNullOrWhiteSpace(targetPartType))
                return false;
            return NamesEqual(legacyPartType, targetPartType);
        }

        private static bool IsUnresolved(string value)
            => NamesEqual(value, UnresolvedPart);

        private static bool IsSeparatorFamily(TargetPartFamilyInventory family)
        {
            string name = NormalizeName(family.Name);
            if (family.Sizes.Count == 0 || name.StartsWith("-----", StringComparison.Ordinal))
                return true;
            return family.Sizes.Count == 1 && NamesEqual(family.Name, family.Sizes[0].Name) && name.Contains("-----");
        }

        private static int TokenOverlapScore(string source, string target, int pointsPerToken, int maximum, List<string> reasons)
        {
            HashSet<string> sourceTokens = GetMeaningfulTokens(source);
            HashSet<string> targetTokens = GetMeaningfulTokens(target);
            List<string> common = sourceTokens.Intersect(targetTokens, StringComparer.OrdinalIgnoreCase).ToList();
            if (common.Count == 0) return 0;
            reasons.Add($"shared keywords: {string.Join(",", common.Take(6))}");
            return Math.Min(maximum, common.Count * pointsPerToken);
        }

        private static HashSet<string> GetMeaningfulTokens(string value)
        {
            string normalized = Regex.Replace(NormalizeName(value), "[^A-Z0-9]+", " ");
            return normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(t => t.Length > 1)
                .Where(t => !StopWords.Contains(t, StringComparer.OrdinalIgnoreCase))
                .Where(t => !double.TryParse(t, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private static string DetectPipeMaterial(string text)
        {
            string n = NormalizeName(text);
            if (ContainsWholeToken(n, "HERCP")) return "HERCP";
            if (ContainsWholeToken(n, "RCP")) return "RCP";
            if (ContainsWholeToken(n, "RCB")) return "RCB";
            if (ContainsWholeToken(n, "C900")) return "C900";
            if (ContainsWholeToken(n, "PVC")) return "PVC";
            if (ContainsWholeToken(n, "HDPE")) return "HDPE";
            if (ContainsWholeToken(n, "ACP")) return "ACP";
            if (n.Contains("ABANDON", StringComparison.OrdinalIgnoreCase)) return "ABANDONED";
            if (ContainsWholeToken(n, "UNKNOWN")) return "UNKNOWN";
            return string.Empty;
        }

        private static string DetectStructureKind(string text)
        {
            string n = NormalizeName(text);
            if (n.Contains("DROP INLET", StringComparison.OrdinalIgnoreCase)) return "INLET";
            if (ContainsWholeToken(n, "JUNCTION")) return "JUNCTION";
            if (ContainsWholeToken(n, "MANHOLE")) return "MANHOLE";
            if (ContainsWholeToken(n, "INLET")) return "INLET";
            if (ContainsWholeToken(n, "ACCESS")) return "ACCESS";
            return string.Empty;
        }

        private static bool ContainsWholeToken(string text, string token)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token)) return false;
            string normalizedText = Regex.Replace(NormalizeName(text), "[^A-Z0-9]+", " ");
            string normalizedToken = Regex.Replace(NormalizeName(token), "[^A-Z0-9]+", " ").Trim();
            return Regex.IsMatch(normalizedText, $@"(?:^|\s){Regex.Escape(normalizedToken)}(?:\s|$)", RegexOptions.IgnoreCase);
        }

        private static bool ShapesCompatible(string legacyShape, string targetShape)
        {
            if (string.IsNullOrWhiteSpace(legacyShape) || string.IsNullOrWhiteSpace(targetShape)) return false;
            string a = NormalizeName(legacyShape);
            string b = NormalizeName(targetShape);
            if (a == b) return true;
            return (a.Contains("CIRC", StringComparison.OrdinalIgnoreCase) || a.Contains("CYL", StringComparison.OrdinalIgnoreCase)) &&
                   (b.Contains("CIRC", StringComparison.OrdinalIgnoreCase) || b.Contains("CYL", StringComparison.OrdinalIgnoreCase));
        }

        private static double? ExtractPrimaryInches(string value)
        {
            Match inchWord = Regex.Match(value, @"(?<![0-9.])([0-9]+(?:\.[0-9]+)?)\s*(?:INCH|IN\b)", RegexOptions.IgnoreCase);
            if (inchWord.Success && double.TryParse(inchWord.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double inches))
                return inches;

            Match quote = Regex.Match(value, @"(?<![0-9.])([0-9]+(?:\.[0-9]+)?)\s*(?:''|"")");
            if (quote.Success && double.TryParse(quote.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out inches))
                return inches;
            return null;
        }

        private static IEnumerable<PipePartGroup> OrderedPipes(PipeCatalogMigrationInventory report)
            => report.PipeGroups.Values.OrderBy(g => g.FamilyName).ThenBy(g => g.SizeName);

        private static IEnumerable<StructurePartGroup> OrderedStructures(PipeCatalogMigrationInventory report)
            => report.StructureGroups.Values.OrderBy(g => g.FamilyName).ThenBy(g => g.SizeName);

        private static bool NamesEqual(string a, string b)
            => string.Equals(NormalizeName(a), NormalizeName(b), StringComparison.OrdinalIgnoreCase);

        private static string NormalizeName(string value)
            => Regex.Replace((value ?? string.Empty).Trim().ToUpperInvariant(), @"\s+", " ");

        private static string ResolvePartsListName(Transaction tr, Network network)
        {
            string direct = GetStringProperty(network, "PartsListName");
            if (!string.IsNullOrWhiteSpace(direct)) return direct;
            ObjectId id = GetObjectIdProperty(network, "PartsListId");
            if (id.IsNull) return "<unable to resolve>";
            try
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is AcDbObject obj)
                    return GetStringProperty(obj, "Name");
            }
            catch { }
            return "<unable to resolve>";
        }

        private static ObjectId GetObjectIdProperty(object source, string propertyName)
        {
            try
            {
                object? value = source.GetType().GetProperty(propertyName)?.GetValue(source);
                return value is ObjectId id ? id : ObjectId.Null;
            }
            catch { return ObjectId.Null; }
        }

        private static string GetStringProperty(object source, string propertyName)
        {
            try { return source.GetType().GetProperty(propertyName)?.GetValue(source)?.ToString() ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static double GetDoubleProperty(object source, string propertyName)
        {
            try
            {
                object? value = source.GetType().GetProperty(propertyName)?.GetValue(source);
                return value is double number ? number : 0.0;
            }
            catch { return 0.0; }
        }

        private static string GetPartIdentityProperty(object source, string propertyName)
        {
            string value = GetStringProperty(source, propertyName);
            return string.IsNullOrWhiteSpace(value) ? UnresolvedPart : value;
        }

        private static string FormatDimension(double value)
            => Math.Abs(value) < 1e-9 ? "-" : value.ToString("0.###", CultureInfo.InvariantCulture);

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
                string partType = GetPartIdentityProperty(pipe, "PartType");
                string shape = GetStringProperty(pipe, "CrossSectionalShape");
                double width = GetDoubleProperty(pipe, "InnerDiameterOrWidth");
                double height = GetDoubleProperty(pipe, "InnerHeight");

                var key = new PipeGroupKey(NormalizeName(familyName), NormalizeName(sizeName), NormalizeName(partType), NormalizeName(shape), Math.Round(width, 6), Math.Round(height, 6));
                if (!PipeGroups.TryGetValue(key, out PipePartGroup? group))
                {
                    group = new PipePartGroup(familyName, sizeName, partType, shape, width, height);
                    PipeGroups.Add(key, group);
                }
                group.Count++;
                group.NetworkNames.Add(networkName);
                group.ObjectIds.Add(pipe.ObjectId);
            }

            public void AddStructure(Structure structure, string networkName)
            {
                StructureInstances++;
                string familyName = GetPartIdentityProperty(structure, "PartFamilyName");
                string sizeName = GetPartIdentityProperty(structure, "PartSizeName");
                string partType = GetPartIdentityProperty(structure, "PartType");
                var key = new StructureGroupKey(NormalizeName(familyName), NormalizeName(sizeName), NormalizeName(partType));

                if (!StructureGroups.TryGetValue(key, out StructurePartGroup? group))
                {
                    group = new StructurePartGroup(familyName, sizeName, partType);
                    StructureGroups.Add(key, group);
                }

                group.Count++;
                group.NetworkNames.Add(networkName);
                group.ObjectIds.Add(structure.ObjectId);

                double innerLength = GetDoubleProperty(structure, "InnerLength");
                double innerWidth = GetDoubleProperty(structure, "InnerDiameterOrWidth");
                double height = GetDoubleProperty(structure, "Height");
                double outerWidth = GetDoubleProperty(structure, "DiameterOrWidth");
                double wallThickness = GetDoubleProperty(structure, "WallThickness");
                double innerDiameter = innerWidth > 0.0 && innerLength <= 0.0 ? innerWidth : (innerWidth > 0.0 ? 0.0 : outerWidth);
                group.AddVariant(innerLength, innerWidth, height, innerDiameter, wallThickness);
            }
        }

        private sealed class TargetPartsListInventory
        {
            public TargetPartsListInventory(ObjectId id, string name) { Id = id; Name = name; }
            public ObjectId Id { get; }
            public string Name { get; }
            public List<TargetPartFamilyInventory> PipeFamilies { get; } = new();
            public List<TargetPartFamilyInventory> StructureFamilies { get; } = new();
        }

        private sealed class TargetPartFamilyInventory
        {
            public TargetPartFamilyInventory(ObjectId id, string name, string domain, string partType, string shape)
            { Id = id; Name = name; Domain = domain; PartType = partType; Shape = shape; }
            public ObjectId Id { get; }
            public string Name { get; }
            public string Domain { get; }
            public string PartType { get; }
            public string Shape { get; }
            public List<TargetPartSizeInventory> Sizes { get; } = new();
        }

        private sealed record TargetPartSizeInventory(ObjectId Id, string Name, string Description);
        private sealed record NetworkInventory(ObjectId Id, string Name, string PartsListName);
        private sealed record PartCandidate(ObjectId FamilyId, ObjectId SizeId, string FamilyName, string SizeName, string PartType, int Score, List<string> Reasons);
        private sealed record AcceptedMigration(string GroupId, List<ObjectId> ObjectIds, PartCandidate Candidate);

        private readonly record struct PipeGroupKey(string FamilyName, string SizeName, string PartType, string Shape, double InnerDiameterOrWidth, double InnerHeight);

        private sealed class PipePartGroup
        {
            public PipePartGroup(string familyName, string sizeName, string partType, string shape, double width, double height)
            { FamilyName = familyName; SizeName = sizeName; PartType = partType; Shape = shape; InnerDiameterOrWidth = width; InnerHeight = height; }
            public string FamilyName { get; }
            public string SizeName { get; }
            public string PartType { get; }
            public string Shape { get; }
            public double InnerDiameterOrWidth { get; }
            public double InnerHeight { get; }
            public int Count { get; set; }
            public HashSet<string> NetworkNames { get; } = new(StringComparer.OrdinalIgnoreCase);
            public List<ObjectId> ObjectIds { get; } = new();
        }

        private readonly record struct StructureGroupKey(string FamilyName, string SizeName, string PartType);

        private sealed class StructurePartGroup
        {
            public StructurePartGroup(string familyName, string sizeName, string partType)
            { FamilyName = familyName; SizeName = sizeName; PartType = partType; }
            public string FamilyName { get; }
            public string SizeName { get; }
            public string PartType { get; }
            public int Count { get; set; }
            public HashSet<string> NetworkNames { get; } = new(StringComparer.OrdinalIgnoreCase);
            public List<ObjectId> ObjectIds { get; } = new();
            public Dictionary<StructurePhysicalKey, StructurePhysicalVariant> Variants { get; } = new();

            public void AddVariant(double innerLength, double innerWidth, double innerHeight, double innerDiameter, double wallThickness)
            {
                var key = new StructurePhysicalKey(
                    Math.Round(innerLength, 6),
                    Math.Round(innerWidth, 6),
                    Math.Round(innerHeight, 6),
                    Math.Round(innerDiameter, 6),
                    Math.Round(wallThickness, 6));

                if (!Variants.TryGetValue(key, out StructurePhysicalVariant? variant))
                {
                    variant = new StructurePhysicalVariant(innerLength, innerWidth, innerHeight, innerDiameter, wallThickness);
                    Variants.Add(key, variant);
                }
                variant.Count++;
            }
        }

        private readonly record struct StructurePhysicalKey(double InnerLength, double InnerWidth, double InnerHeight, double InnerDiameter, double WallThickness);

        private sealed class StructurePhysicalVariant
        {
            public StructurePhysicalVariant(double innerLength, double innerWidth, double innerHeight, double innerDiameter, double wallThickness)
            { InnerLength = innerLength; InnerWidth = innerWidth; InnerHeight = innerHeight; InnerDiameter = innerDiameter; WallThickness = wallThickness; }
            public double InnerLength { get; }
            public double InnerWidth { get; }
            public double InnerHeight { get; }
            public double InnerDiameter { get; }
            public double WallThickness { get; }
            public int Count { get; set; }
        }
    }
}
