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

                    destination.Add(targetFamily);
                }
                catch
                {
                    // Continue with other target families.
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
                                    $" W={FormatDimension(variant.InnerWidth)} H={FormatDimension(variant.InnerHeight)} ID={FormatDimension(variant.InnerDiameter)}");
                }
            }

            WriteCandidateReport(ed, report, target);

            ed.WriteMessage("\n\nANALYSIS STATUS");
            ed.WriteMessage("\n  No pipe, structure, Parts List, or catalog data was changed.");
            ed.WriteMessage("\n  Candidate rankings are advisory only.");
            ed.WriteMessage("\n  Target PartType compatibility is checked before the migration command can swap a part.");
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
                WriteCandidates(ed, $"S{s++:000}", legacy.SizeName, legacy.PartType, FindStructureCandidates(legacy, target.StructureFamilies));
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
            ed.WriteMessage("\nOnly confirmed, HIGH-confidence, PartType-compatible candidates can be swapped.");
            ed.WriteMessage("\nType-changing/combination structures are left untouched for manual replacement.");

            var accepted = new List<AcceptedMigration>();
            int manualRequired = 0;
            int noMatch = 0;

            int p = 1;
            foreach (PipePartGroup legacy in OrderedPipes(report))
            {
                ReviewMigrationGroup(ed, $"P{p++:000}", legacy.SizeName, legacy.PartType, legacy.ObjectIds,
                    FindPipeCandidates(legacy, target.PipeFamilies), false, accepted, ref manualRequired, ref noMatch);
            }

            int s = 1;
            foreach (StructurePartGroup legacy in OrderedStructures(report))
            {
                ReviewMigrationGroup(ed, $"S{s++:000}", legacy.SizeName, legacy.PartType, legacy.ObjectIds,
                    FindStructureCandidates(legacy, target.StructureFamilies), true, accepted, ref manualRequired, ref noMatch);
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
                ed.WriteMessage("\nMigration cancelled. Drawing unchanged.");
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

        private static void ReviewMigrationGroup(
            Editor ed,
            string groupId,
            string legacySize,
            string legacyPartType,
            List<ObjectId> objectIds,
            List<PartCandidate> candidates,
            bool isStructure,
            List<AcceptedMigration> accepted,
            ref int manualRequired,
            ref int noMatch)
        {
            if (candidates.Count == 0)
            {
                ed.WriteMessage($"\n  {groupId} | NO MATCH | '{legacySize}' -> SKIPPED");
                noMatch++;
                return;
            }

            PartCandidate top = candidates[0];
            string confidence = GetConfidence(top.Score, candidates.Count > 1 ? candidates[1].Score : int.MinValue);
            if (confidence == "NO MATCH")
            {
                ed.WriteMessage($"\n  {groupId} | NO MATCH | '{legacySize}' -> SKIPPED");
                noMatch++;
                return;
            }

            if (!PartTypesCompatible(legacyPartType, top.PartType))
            {
                ed.WriteMessage($"\n  {groupId} | MANUAL REPLACEMENT REQUIRED");
                ed.WriteMessage($"\n       Legacy: '{legacySize}' | Type={legacyPartType}");
                ed.WriteMessage($"\n       Suggested target: '{top.FamilyName}' / '{top.SizeName}' | Type={top.PartType}");
                ed.WriteMessage("\n       Civil 3D cannot safely SwapPartFamilyAndSize across these part types.");
                manualRequired++;
                return;
            }

            if (confidence != "HIGH")
            {
                ed.WriteMessage($"\n  {groupId} | {confidence} | '{legacySize}' -> SKIPPED (not HIGH confidence)");
                manualRequired++;
                return;
            }

            ed.WriteMessage($"\n\n  {groupId} | HIGH | {objectIds.Count} part(s)");
            ed.WriteMessage($"\n       FROM: '{legacySize}' | Type={legacyPartType}");
            ed.WriteMessage($"\n       TO  : '{top.FamilyName}' / '{top.SizeName}' | Type={top.PartType}");
            if (isStructure)
                ed.WriteMessage("\n       Note: Civil 3D swap preserves pipe connection levels, but inspect structure geometry after migration.");

            if (PromptYesNo(ed, $"Accept {groupId} mapping", false))
                accepted.Add(new AcceptedMigration(groupId, objectIds, top));
            else
                ed.WriteMessage($"\n       {groupId} skipped by user.");
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
            return string.IsNullOrWhiteSpace(value) ? "<invalid/unresolved>" : value;
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
                double innerDiameter = innerWidth > 0.0 && innerLength <= 0.0 ? innerWidth : (innerWidth > 0.0 ? 0.0 : outerWidth);
                group.AddVariant(innerLength, innerWidth, height, innerDiameter);
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

            public void AddVariant(double innerLength, double innerWidth, double innerHeight, double innerDiameter)
            {
                var key = new StructurePhysicalKey(Math.Round(innerLength, 6), Math.Round(innerWidth, 6), Math.Round(innerHeight, 6), Math.Round(innerDiameter, 6));
                if (!Variants.TryGetValue(key, out StructurePhysicalVariant? variant))
                {
                    variant = new StructurePhysicalVariant(innerLength, innerWidth, innerHeight, innerDiameter);
                    Variants.Add(key, variant);
                }
                variant.Count++;
            }
        }

        private readonly record struct StructurePhysicalKey(double InnerLength, double InnerWidth, double InnerHeight, double InnerDiameter);

        private sealed class StructurePhysicalVariant
        {
            public StructurePhysicalVariant(double innerLength, double innerWidth, double innerHeight, double innerDiameter)
            { InnerLength = innerLength; InnerWidth = innerWidth; InnerHeight = innerHeight; InnerDiameter = innerDiameter; }
            public double InnerLength { get; }
            public double InnerWidth { get; }
            public double InnerHeight { get; }
            public double InnerDiameter { get; }
            public int Count { get; set; }
        }
    }
}
