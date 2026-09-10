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
    /// Non-destructive inventory and candidate analysis for Pipe Network catalog migration.
    /// The target Parts List selected by the user is the authority for candidate parts.
    /// No drawing data or Parts List is modified by this command.
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
                        report.Networks.Add(new NetworkInventory(networkId, networkName, partsListName));

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
                    // Ignore stale/unreadable entries.
                }
            }

            choices = choices.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
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
            return result.Status == PromptStatus.OK
                ? choices[result.Value - 1].Id
                : ObjectId.Null;
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
            foreach (ObjectId familyId in partsList.GetPartFamilyIdsByDomain(domain))
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
                            // Continue with other target sizes.
                        }
                    }

                    destination.Add(familyInventory);
                }
                catch
                {
                    // Continue with other target families.
                }
            }
        }

        private static void WriteReport(Editor ed, PipeCatalogMigrationInventory report, TargetPartsListInventory target)
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
            {
                string partsList = string.IsNullOrWhiteSpace(network.PartsListName)
                    ? "<unable to resolve>"
                    : network.PartsListName;
                ed.WriteMessage($"\n  {network.Name} | Parts List: {partsList}");
            }

            ed.WriteMessage("\n\nPIPE PART GROUPS");
            int pipeIndex = 1;
            foreach (PipePartGroup group in report.PipeGroups.Values
                         .OrderBy(g => g.FamilyName)
                         .ThenBy(g => g.SizeName))
            {
                ed.WriteMessage(
                    $"\n  P{pipeIndex++:000} | Count={group.Count} | Family='{group.FamilyName}' | Size='{group.SizeName}'" +
                    $" | Shape={group.Shape} | ID/W={FormatDimension(group.InnerDiameterOrWidth)} | H={FormatDimension(group.InnerHeight)}");
                if (group.NetworkNames.Count > 0)
                    ed.WriteMessage($" | Networks={string.Join(", ", group.NetworkNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))}");
            }

            ed.WriteMessage("\n\nSTRUCTURE PART GROUPS");
            int structureIndex = 1;
            foreach (StructurePartGroup group in report.StructureGroups.Values
                         .OrderBy(g => g.FamilyName)
                         .ThenBy(g => g.SizeName))
            {
                ed.WriteMessage(
                    $"\n  S{structureIndex++:000} | Count={group.Count} | Family='{group.FamilyName}' | Size='{group.SizeName}'" +
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

            WriteCandidateReport(ed, report, target);

            ed.WriteMessage("\n\nANALYSIS STATUS");
            ed.WriteMessage("\n  No pipe, structure, Parts List, or catalog data was changed.");
            ed.WriteMessage("\n  The selected target Parts List is the only authority for migration candidates.");
            ed.WriteMessage("\n  Candidate rankings are advisory only; ambiguous matches require confirmation before migration.");
            ed.WriteMessage("\n  Instance structure heights/dimensions remain separately recorded for later preservation.");
            ed.WriteMessage("\n");
        }

        private static void WriteCandidateReport(Editor ed, PipeCatalogMigrationInventory report, TargetPartsListInventory target)
        {
            ed.WriteMessage("\n\n============================================================");
            ed.WriteMessage("\nMIGRATION CANDIDATE MAPPING - NO CHANGES MADE");
            ed.WriteMessage("\n============================================================");
            ed.WriteMessage($"\nTarget Parts List: {target.Name}");

            ed.WriteMessage("\n\nPIPE CANDIDATES");
            int pipeIndex = 1;
            foreach (PipePartGroup legacy in report.PipeGroups.Values
                         .OrderBy(g => g.FamilyName)
                         .ThenBy(g => g.SizeName))
            {
                List<PartCandidate> candidates = FindPipeCandidates(legacy, target.PipeFamilies);
                WriteCandidates(ed, $"P{pipeIndex++:000}", legacy.SizeName, candidates);
            }

            ed.WriteMessage("\n\nSTRUCTURE CANDIDATES");
            int structureIndex = 1;
            foreach (StructurePartGroup legacy in report.StructureGroups.Values
                         .OrderBy(g => g.FamilyName)
                         .ThenBy(g => g.SizeName))
            {
                List<PartCandidate> candidates = FindStructureCandidates(legacy, target.StructureFamilies);
                WriteCandidates(ed, $"S{structureIndex++:000}", legacy.SizeName, candidates);
            }
        }

        private static void WriteCandidates(Editor ed, string id, string legacySize, List<PartCandidate> candidates)
        {
            if (candidates.Count == 0)
            {
                ed.WriteMessage($"\n  {id} | NO MATCH | Legacy='{legacySize}'");
                return;
            }

            PartCandidate top = candidates[0];
            int secondScore = candidates.Count > 1 ? candidates[1].Score : int.MinValue;
            string confidence = GetConfidence(top.Score, secondScore);

            ed.WriteMessage(
                $"\n  {id} | {confidence} | Legacy='{legacySize}'" +
                $"\n       BEST: Family='{top.FamilyName}' | Size='{top.SizeName}' | Score={top.Score}" +
                $"\n       Why: {string.Join("; ", top.Reasons)}");

            foreach (PartCandidate alternate in candidates.Skip(1).Take(2))
            {
                ed.WriteMessage(
                    $"\n       ALT : Family='{alternate.FamilyName}' | Size='{alternate.SizeName}' | Score={alternate.Score}");
            }
        }

        private static string GetConfidence(int bestScore, int secondScore)
        {
            if (bestScore < 25)
                return "NO MATCH";

            int margin = secondScore == int.MinValue ? bestScore : bestScore - secondScore;
            if (bestScore >= 100 && margin >= 25)
                return "HIGH";
            if (bestScore >= 65 && margin >= 15)
                return "MEDIUM";
            return "AMBIGUOUS";
        }

        private static List<PartCandidate> FindPipeCandidates(
            PipePartGroup legacy,
            IEnumerable<TargetPartFamilyInventory> targetFamilies)
        {
            var results = new List<PartCandidate>();
            double legacyDiameterInches = legacy.InnerDiameterOrWidth * 12.0;
            string legacyText = $"{legacy.FamilyName} {legacy.SizeName}";
            string material = DetectPipeMaterial(legacyText);

            foreach (TargetPartFamilyInventory family in targetFamilies)
            {
                foreach (TargetPartSizeInventory size in family.Sizes)
                {
                    int score = 0;
                    var reasons = new List<string>();
                    string targetText = $"{family.Name} {size.Name} {size.Description}";

                    if (NamesEqual(legacy.SizeName, size.Name))
                    {
                        score += 90;
                        reasons.Add("exact normalized size name");
                    }

                    if (!string.IsNullOrEmpty(material) && ContainsWholeToken(targetText, material))
                    {
                        score += 55;
                        reasons.Add($"material/type keyword {material}");
                    }

                    if (ShapesCompatible(legacy.Shape, family.Shape))
                    {
                        score += 20;
                        reasons.Add("shape compatible");
                    }

                    double? targetInches = ExtractPrimaryInches(size.Name);
                    if (targetInches.HasValue && Math.Abs(targetInches.Value - legacyDiameterInches) < 0.11)
                    {
                        score += 60;
                        reasons.Add($"diameter {legacyDiameterInches:0.###} in matches");
                    }

                    score += TokenOverlapScore(legacyText, targetText, 6, 30, reasons);

                    if (family.Name.Equals("UNKNOWN Pipe", StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrEmpty(material) &&
                        !material.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase))
                    {
                        score -= 30;
                        reasons.Add("UNKNOWN family de-prioritized because legacy material is identifiable");
                    }

                    if (score > 0)
                        results.Add(new PartCandidate(family.Name, size.Name, score, reasons));
                }
            }

            return results
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.FamilyName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.SizeName, StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();
        }

        private static List<PartCandidate> FindStructureCandidates(
            StructurePartGroup legacy,
            IEnumerable<TargetPartFamilyInventory> targetFamilies)
        {
            var results = new List<PartCandidate>();
            string legacyText = $"{legacy.FamilyName} {legacy.SizeName}";
            string expectedShape = legacy.Variants.Values.Any(v => v.InnerLength > 0.0) ? "Box" : "Cylinder";
            string kind = DetectStructureKind(legacyText);
            bool legacySddi = ContainsWholeToken(legacyText, "SDDI");

            foreach (TargetPartFamilyInventory family in targetFamilies)
            {
                if (IsSeparatorFamily(family))
                    continue;

                bool hasKnownTargetShape = !string.IsNullOrWhiteSpace(family.Shape);
                if (hasKnownTargetShape && !ShapesCompatible(expectedShape, family.Shape))
                    continue;

                foreach (TargetPartSizeInventory size in family.Sizes)
                {
                    int score = 0;
                    var reasons = new List<string>();
                    string targetText = $"{family.Name} {size.Name} {size.Description}";

                    if (NamesEqual(legacy.FamilyName, family.Name))
                    {
                        score += 100;
                        reasons.Add("exact normalized family name");
                    }

                    if (NamesEqual(legacy.SizeName, size.Name))
                    {
                        score += 90;
                        reasons.Add("exact normalized size name");
                    }

                    if (ShapesCompatible(expectedShape, family.Shape))
                    {
                        score += 20;
                        reasons.Add("bounding shape compatible");
                    }

                    score += TokenOverlapScore(legacyText, targetText, 9, 63, reasons);

                    if (!string.IsNullOrEmpty(kind) && ContainsWholeToken(targetText, kind))
                    {
                        score += 35;
                        reasons.Add($"structure type keyword {kind}");
                    }

                    if (kind == "JUNCTION" && NamesEqual(family.Name, "GENERAL JUNCTION STRUCTURE"))
                    {
                        score += 85;
                        reasons.Add("legacy generic junction favors GENERAL JUNCTION STRUCTURE");
                    }

                    if (legacySddi && NamesEqual(family.Name, "GENERAL INLET"))
                    {
                        score += 85;
                        reasons.Add("legacy SDDI favors GENERAL INLET");
                    }

                    if (kind == "JUNCTION" && ContainsWholeToken(targetText, "MANHOLE") &&
                        !ContainsWholeToken(targetText, "JUNCTION"))
                    {
                        score -= 35;
                    }

                    if (score > 0)
                        results.Add(new PartCandidate(family.Name, size.Name, score, reasons));
                }
            }

            return results
                .OrderByDescending(c => c.Score)
                .ThenBy(c => c.FamilyName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.SizeName, StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .ToList();
        }

        private static bool IsSeparatorFamily(TargetPartFamilyInventory family)
        {
            string name = NormalizeName(family.Name);
            if (family.Sizes.Count == 0)
                return true;

            if (name.StartsWith("-----", StringComparison.Ordinal))
                return true;

            return family.Sizes.Count == 1 && NamesEqual(family.Name, family.Sizes[0].Name) && name.Contains("-----");
        }

        private static int TokenOverlapScore(
            string source,
            string target,
            int pointsPerToken,
            int maximum,
            List<string> reasons)
        {
            HashSet<string> sourceTokens = GetMeaningfulTokens(source);
            HashSet<string> targetTokens = GetMeaningfulTokens(target);
            List<string> common = sourceTokens.Intersect(targetTokens, StringComparer.OrdinalIgnoreCase).ToList();
            if (common.Count == 0)
                return 0;

            int score = Math.Min(maximum, common.Count * pointsPerToken);
            reasons.Add($"shared keywords: {string.Join(",", common.Take(6))}");
            return score;
        }

        private static HashSet<string> GetMeaningfulTokens(string value)
        {
            string normalized = Regex.Replace(NormalizeName(value), "[^A-Z0-9]+", " ");
            return normalized
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
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
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token))
                return false;

            string normalizedText = Regex.Replace(NormalizeName(text), "[^A-Z0-9]+", " ");
            string normalizedToken = Regex.Replace(NormalizeName(token), "[^A-Z0-9]+", " ").Trim();
            return Regex.IsMatch(normalizedText, $@"(?:^|\s){Regex.Escape(normalizedToken)}(?:\s|$)", RegexOptions.IgnoreCase);
        }

        private static bool ShapesCompatible(string legacyShape, string targetShape)
        {
            if (string.IsNullOrWhiteSpace(legacyShape) || string.IsNullOrWhiteSpace(targetShape))
                return false;

            string a = NormalizeName(legacyShape);
            string b = NormalizeName(targetShape);
            if (a == b)
                return true;

            return (a.Contains("CIRC", StringComparison.OrdinalIgnoreCase) || a.Contains("CYL", StringComparison.OrdinalIgnoreCase)) &&
                   (b.Contains("CIRC", StringComparison.OrdinalIgnoreCase) || b.Contains("CYL", StringComparison.OrdinalIgnoreCase));
        }

        private static double? ExtractPrimaryInches(string value)
        {
            Match inchWord = Regex.Match(value, @"(?<![0-9.])([0-9]+(?:\.[0-9]+)?)\s*(?:INCH|IN\b)", RegexOptions.IgnoreCase);
            if (inchWord.Success && double.TryParse(inchWord.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double inches))
                return inches;

            Match doubleQuote = Regex.Match(value, @"(?<![0-9.])([0-9]+(?:\.[0-9]+)?)\s*(?:''|"")");
            if (doubleQuote.Success && double.TryParse(doubleQuote.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out inches))
                return inches;

            return null;
        }

        private static bool NamesEqual(string a, string b)
            => string.Equals(NormalizeName(a), NormalizeName(b), StringComparison.OrdinalIgnoreCase);

        private static string NormalizeName(string value)
            => Regex.Replace((value ?? string.Empty).Trim().ToUpperInvariant(), @"\s+", " ");

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
                // Older drawings can retain stale PartsListId references.
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
                    NormalizeName(familyName),
                    NormalizeName(sizeName),
                    NormalizeName(pipe.CrossSectionalShape.ToString()),
                    Math.Round(pipe.InnerDiameterOrWidth, 6),
                    Math.Round(pipe.InnerHeight, 6));

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
                var key = new StructureGroupKey(NormalizeName(familyName), NormalizeName(sizeName));

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
        private sealed record PartCandidate(string FamilyName, string SizeName, int Score, List<string> Reasons);

        private readonly record struct PipeGroupKey(
            string FamilyName,
            string SizeName,
            string Shape,
            double InnerDiameterOrWidth,
            double InnerHeight);

        private sealed class PipePartGroup
        {
            public PipePartGroup(string familyName, string sizeName, string shape, double innerDiameterOrWidth, double innerHeight)
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
                    Math.Round(innerLength, 6),
                    Math.Round(innerWidth, 6),
                    Math.Round(innerHeight, 6),
                    Math.Round(innerDiameter, 6));

                if (!Variants.TryGetValue(key, out StructurePhysicalVariant? variant))
                {
                    variant = new StructurePhysicalVariant(innerLength, innerWidth, innerHeight, innerDiameter);
                    Variants.Add(key, variant);
                }

                variant.Count++;
            }
        }

        private readonly record struct StructurePhysicalKey(
            double InnerLength,
            double InnerWidth,
            double InnerHeight,
            double InnerDiameter);

        private sealed class StructurePhysicalVariant
        {
            public StructurePhysicalVariant(double innerLength, double innerWidth, double innerHeight, double innerDiameter)
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
