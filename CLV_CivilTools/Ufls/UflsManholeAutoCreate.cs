using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;

using Autodesk.Civil.ApplicationServices;
using Autodesk.Civil.DatabaseServices;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace CLV_CivilTools
{
    public class UflsManholeAutoCreate
    {
        // ------------------------------------------------------------
        // Constants
        // ------------------------------------------------------------

        private const string BlockManholeCircular = "UFLS-GIS-MH-CIRCULAR";
        private const string BlockManholeMark = "UFLS_MH_MARK";

        // External source folder for manhole circular block (UNC path)
        private const string BlockSourceFolder =
            @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\Blocks\Survey";

        // Both footprint and locator on same check layer
        private const string LyrManholeCheck = "V-SURV-CHCK";

        // Default clustering distance in drawing units (feet)
        private const double DefaultClusterTolerance = 6.0;

        // Supported in-block manhole diameters (inches) that map
        // to the dynamic visibility options.
        private static readonly int[] KnownDiameters = { 48, 60, 72 };

        // ------------------------------------------------------------
        // Helper types
        // ------------------------------------------------------------

        private class MhShot
        {
            public ObjectId Id { get; set; }
            public Point3d Position { get; set; }
            public string Raw { get; set; } = string.Empty;
            public string Full { get; set; } = string.Empty;
        }

        // ------------------------------------------------------------
        // Command entry (command-line prompts)
        // ------------------------------------------------------------

        [CommandMethod("UFLS", "UFLS4", CommandFlags.Modal)]
        [CommandMethod("UFLS-MH-AUTO", CommandFlags.Modal)]
        public static void UflsManholeAutoCreateCommand()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            // 1) Ask for survey source
            var pKeyOpts = new PromptKeywordOptions(
                "\nSelect survey source for manhole coding")
            {
                AllowNone = false
            };
            pKeyOpts.Keywords.Add("InHouse", "In-House", "In-House survey (CLV codes)");
            pKeyOpts.Keywords.Add("Others", "Others", "Other consultant survey");

            PromptResult pKeyRes = ed.GetKeywords(pKeyOpts);
            if (pKeyRes.Status != PromptStatus.OK)
                return;

            bool isInHouse = string.Equals(pKeyRes.StringResult, "InHouse",
                StringComparison.OrdinalIgnoreCase);

            // 2) If "Others", ask for raw description prefixes
            List<string> rawPrefixes = new List<string>();

            if (!isInHouse)
            {
                var pStrOpts = new PromptStringOptions(
                    "\nEnter raw description code(s) to search for (comma-separated, e.g. MH, MHB): ")
                {
                    AllowSpaces = false
                };
                PromptResult pStrRes = ed.GetString(pStrOpts);
                if (pStrRes.Status != PromptStatus.OK)
                    return;

                rawPrefixes = ParseRawPrefixes(pStrRes.StringResult ?? string.Empty);
                if (rawPrefixes.Count == 0)
                {
                    ed.WriteMessage("\nNo valid raw code prefixes supplied. Command cancelled.");
                    return;
                }
            }

            // 3) Ask for clustering distance
            double? clusterTol = PromptClusterTolerance(ed);
            if (!clusterTol.HasValue)
                return;

            RunCore(isInHouse, rawPrefixes, clusterTol.Value, useSinglePointCenters: false);
        }

        // ------------------------------------------------------------
        // Palette entry point (called from UI dialog)
        // ------------------------------------------------------------

        public static void RunFromPalette(bool isInHouse, string otherCodesCsv)
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            Editor? ed = doc?.Editor;

            List<string> rawPrefixes = new List<string>();
            if (!isInHouse)
            {
                rawPrefixes = ParseRawPrefixes(otherCodesCsv ?? string.Empty);
                if (rawPrefixes.Count == 0)
                {
                    ed?.WriteMessage(
                        "\nUFLS-MH-AUTO: No valid raw code prefixes supplied. Operation cancelled.");
                    return;
                }
            }

            // Prompt for cluster distance here as well so palette and command behave the same.
            if (ed == null)
                return;

            double? clusterTol = PromptClusterTolerance(ed);
            if (!clusterTol.HasValue)
                return;

            RunCore(isInHouse, rawPrefixes, clusterTol.Value, useSinglePointCenters: false);
        }


        [CommandMethod("UFLS", "UFLS41P", CommandFlags.Modal)]
        [CommandMethod("UFLS-MH-AUTO-1P", CommandFlags.Modal)]
        public static void UflsManholeAutoCreateSinglePointCommand()
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            Editor ed = doc.Editor;

            var pKeyOpts = new PromptKeywordOptions(
                "\nSelect survey source for 1P manhole coding")
            {
                AllowNone = false
            };
            pKeyOpts.Keywords.Add("InHouse", "In-House", "In-House survey (CLV codes)");
            pKeyOpts.Keywords.Add("Others", "Others", "Other consultant survey");

            PromptResult pKeyRes = ed.GetKeywords(pKeyOpts);
            if (pKeyRes.Status != PromptStatus.OK)
                return;

            bool isInHouse = string.Equals(pKeyRes.StringResult, "InHouse",
                StringComparison.OrdinalIgnoreCase);

            List<string> rawPrefixes = new List<string>();

            if (!isInHouse)
            {
                var pStrOpts = new PromptStringOptions(
                    "\nEnter raw description code(s) to search for (comma-separated, e.g. MH, MHB): ")
                {
                    AllowSpaces = false
                };
                PromptResult pStrRes = ed.GetString(pStrOpts);
                if (pStrRes.Status != PromptStatus.OK)
                    return;

                rawPrefixes = ParseRawPrefixes(pStrRes.StringResult ?? string.Empty);
                if (rawPrefixes.Count == 0)
                {
                    ed.WriteMessage("\nNo valid raw code prefixes supplied. Command cancelled.");
                    return;
                }
            }

            double? clusterTol = PromptClusterTolerance(ed);
            if (!clusterTol.HasValue)
                return;

            RunCore(isInHouse, rawPrefixes, clusterTol.Value, useSinglePointCenters: true);
        }

        public static void RunFromPalette1P(bool isInHouse, string otherCodesCsv)
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            Editor? ed = doc?.Editor;

            List<string> rawPrefixes = new List<string>();
            if (!isInHouse)
            {
                rawPrefixes = ParseRawPrefixes(otherCodesCsv ?? string.Empty);
                if (rawPrefixes.Count == 0)
                {
                    ed?.WriteMessage(
                        "\nUFLS-MH-AUTO-1P: No valid raw code prefixes supplied. Operation cancelled.");
                    return;
                }
            }

            if (ed == null)
                return;

            double? clusterTol = PromptClusterTolerance(ed);
            if (!clusterTol.HasValue)
                return;

            RunCore(isInHouse, rawPrefixes, clusterTol.Value, useSinglePointCenters: true);
        }

        private static List<string> ParseRawPrefixes(string csv)
        {
            List<string> result = new List<string>();
            string input = (csv ?? string.Empty).ToUpperInvariant();

            foreach (string token in input.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string trimmed = token.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    result.Add(trimmed);
            }

            return result;
        }

        private static double? PromptClusterTolerance(Editor ed)
        {
            var pDouble = new PromptDoubleOptions(
                $"\nEnter clustering distance for manhole shots <{DefaultClusterTolerance}>:")
            {
                AllowNegative = false,
                AllowZero = false,
                AllowNone = true,
                DefaultValue = DefaultClusterTolerance,
                UseDefaultValue = true
            };

            PromptDoubleResult res = ed.GetDouble(pDouble);
            if (res.Status == PromptStatus.None)
            {
                // User pressed Enter → use default
                return DefaultClusterTolerance;
            }
            if (res.Status != PromptStatus.OK)
                return null;

            if (res.Value <= 0.0)
                return DefaultClusterTolerance;

            return res.Value;
        }

        // ------------------------------------------------------------
        // Main implementation
        // ------------------------------------------------------------

        private static void RunCore(bool isInHouse, List<string> rawPrefixes, double clusterTolerance, bool useSinglePointCenters)
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            Editor ed = doc.Editor;
            Database db = doc.Database;
            CivilDocument civDoc = CivilApplication.ActiveDocument;

            using (DocumentLock docLock = doc.LockDocument())
            {
                try
                {
                    // ------------------------------------------------
                    // 0) Ensure required block definitions exist
                    // ------------------------------------------------
                    EnsureBlockDefinition(db, BlockManholeCircular);
                    EnsureBlockDefinition(db, BlockManholeMark);

                    // ------------------------------------------------
                    // 1) Main transaction
                    // ------------------------------------------------
                    using (Transaction tr = db.TransactionManager.StartTransaction())
                    {
                        // 1a) Collect matching CogoPoints
                        List<MhShot> shots = new List<MhShot>();

                        CogoPointCollection cogoPoints = civDoc.CogoPoints;

                        foreach (ObjectId id in cogoPoints)
                        {
                            CogoPoint cp = (CogoPoint)tr.GetObject(id, OpenMode.ForRead);

                            string raw = (cp.RawDescription ?? string.Empty).ToUpperInvariant();
                            string full = (cp.FullDescription ?? string.Empty).ToUpperInvariant();

                            bool match = false;

                            if (isInHouse)
                            {
                                // In-house logic:
                                // - Raw starts with MHB
                                // - Full description contains "MANHOLE BASE/BARREL/RISER"
                                if (raw.StartsWith("MHB"))
                                    match = true;

                                if (!match && full.Contains("MANHOLE BASE/BARREL/RISER".ToUpperInvariant()))
                                    match = true;
                            }
                            else
                            {
                                foreach (string prefix in rawPrefixes)
                                {
                                    if (raw.StartsWith(prefix))
                                    {
                                        match = true;
                                        break;
                                    }
                                }
                            }

                            if (!match)
                                continue;

                            MhShot shot = new MhShot
                            {
                                Id = id,
                                Position = cp.Location,
                                Raw = raw,
                                Full = full
                            };

                            shots.Add(shot);
                        }

                        if (shots.Count == 0)
                        {
                            ed.WriteMessage("\nUFLS-MH-AUTO: No manhole shots found matching the specified coding.");
                            tr.Commit();
                            return;
                        }

                        ed.WriteMessage(
                            $"\nUFLS-MH-AUTO: Found {shots.Count} manhole shot point(s). " +
                            $"Clustering by proximity (tol = {clusterTolerance:0.###})...");

                        // 1b) Cluster shots into individual manholes
                        List<List<MhShot>> clusters = ClusterShots(shots, clusterTolerance);

                        ed.WriteMessage($"\nUFLS-MH-AUTO: Created {clusters.Count} manhole cluster(s).");

                        // 1c) Ensure layer
                        EnsureLayer(db, tr, LyrManholeCheck);

                        // 1d) Check which blocks are now available
                        BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                        bool hasMhCircular = bt.Has(BlockManholeCircular);
                        bool hasMhMark = bt.Has(BlockManholeMark);

                        if (!hasMhCircular)
                        {
                            ed.WriteMessage(
                                $"\nUFLS-MH-AUTO: Block \"{BlockManholeCircular}\" not found (even after checking {BlockSourceFolder}). Manhole circular symbol will not be placed.");
                        }

                        if (!hasMhMark)
                        {
                            ed.WriteMessage(
                                $"\nUFLS-MH-AUTO: Block \"{BlockManholeMark}\" not found. Locator mark will not be placed.");
                        }

                        // 1e) For each cluster: compute center & insert blocks
                        int createdCount = 0;

                        foreach (List<MhShot> cluster in clusters)
                        {
                            if (cluster.Count == 0)
                                continue;

                            Point3d center;
                            if (useSinglePointCenters)
                            {
                                center = ComputeCentroidFlattened(cluster);
                            }
                            else if (!TryComputeClusterCircleCenter(cluster, out center))
                            {
                                center = ComputeCentroidFlattened(cluster);
                            }

                            // Determine size (48, 60, 72) from descriptions.
                            // If none found, leave visibility as default (48" MANHOLE) and do not scale.
                            int diameterInches = InferDiameterInches(cluster);
                            string? visibilityOption = MapDiameterToVisibility(diameterInches);

                            // Insert footprint/locator on same check layer, no scaling
                            if (hasMhCircular)
                            {
                                InsertBlockReference(
                                    db,
                                    tr,
                                    bt[BlockManholeCircular],
                                    center,
                                    LyrManholeCheck,
                                    1.0,
                                    visibilityOption);
                            }

                            if (hasMhMark)
                            {
                                InsertBlockReference(
                                    db,
                                    tr,
                                    bt[BlockManholeMark],
                                    center,
                                    LyrManholeCheck,
                                    1.0,
                                    null); // no visibility on locator
                            }

                            createdCount++;
                        }

                        string modeLabel = useSinglePointCenters ? "UFLS-MH-AUTO-1P" : "UFLS-MH-AUTO";
                        ed.WriteMessage($"\n{modeLabel}: Created manhole circulars/marks for {createdCount} cluster(s).");

                        tr.Commit();
                    }
                }
                catch (System.Exception ex)
                {
                    string modeLabel = useSinglePointCenters ? "UFLS-MH-AUTO-1P" : "UFLS-MH-AUTO";
                    ed.WriteMessage($"\nError in {modeLabel}: {ex.Message}");
                }
            }
        }

        // ------------------------------------------------------------
        // Clustering & geometry
        // ------------------------------------------------------------

        private static List<List<MhShot>> ClusterShots(List<MhShot> shots, double tol)
        {
            List<List<MhShot>> clusters = new List<List<MhShot>>();
            HashSet<ObjectId> visited = new HashSet<ObjectId>();

            foreach (MhShot shot in shots)
            {
                if (visited.Contains(shot.Id))
                    continue;

                List<MhShot> cluster = new List<MhShot>();
                Queue<MhShot> queue = new Queue<MhShot>();

                queue.Enqueue(shot);
                visited.Add(shot.Id);

                while (queue.Count > 0)
                {
                    MhShot current = queue.Dequeue();
                    cluster.Add(current);

                    foreach (MhShot other in shots)
                    {
                        if (visited.Contains(other.Id))
                            continue;

                        // Use XY distance only; ignore any Z noise
                        double dx = current.Position.X - other.Position.X;
                        double dy = current.Position.Y - other.Position.Y;
                        double dist = Math.Sqrt(dx * dx + dy * dy);

                        if (dist <= tol)
                        {
                            visited.Add(other.Id);
                            queue.Enqueue(other);
                        }
                    }
                }

                clusters.Add(cluster);
            }

            return clusters;
        }

        /// <summary>
        /// Try to compute a true circle center from the cluster using
        /// AutoCAD's CircularArc3d (CIRCLE 3P behavior). We pick three
        /// reasonably separated points: first, middle, last. If those
        /// are degenerate/collinear, this returns false.
        /// </summary>
        private static bool TryComputeClusterCircleCenter(
            List<MhShot> cluster,
            out Point3d center)
        {
            center = Point3d.Origin;

            if (cluster == null || cluster.Count < 3)
                return false;

            // Flatten all cluster points to Z=0
            var flatPts = new List<Point3d>(cluster.Count);
            foreach (var s in cluster)
            {
                flatPts.Add(new Point3d(s.Position.X, s.Position.Y, 0.0));
            }

            // Choose 3 reasonably spaced points: first, middle, last
            Point3d p1 = flatPts[0];
            Point3d p3 = flatPts[flatPts.Count - 1];
            Point3d p2 = flatPts[flatPts.Count / 2];

            try
            {
                using (var arc = new CircularArc3d(p1, p2, p3))
                {
                    var c = arc.Center;
                    center = new Point3d(c.X, c.Y, 0.0);
                    return true;
                }
            }
            catch
            {
                // Points nearly collinear or otherwise invalid
                center = Point3d.Origin;
                return false;
            }
        }

        private static Point3d ComputeCentroidFlattened(List<MhShot> cluster)
        {
            if (cluster == null || cluster.Count == 0)
                return Point3d.Origin;

            double sx = 0.0;
            double sy = 0.0;

            foreach (MhShot shot in cluster)
            {
                sx += shot.Position.X;
                sy += shot.Position.Y;
            }

            double n = cluster.Count;
            return new Point3d(sx / n, sy / n, 0.0);
        }

        private static int InferDiameterInches(List<MhShot> cluster)
        {
            if (cluster == null || cluster.Count == 0)
                return 0;

            foreach (MhShot shot in cluster)
            {
                int val = TryParseDiameterFromText(shot.Raw);
                if (val > 0)
                    return val;

                val = TryParseDiameterFromText(shot.Full);
                if (val > 0)
                    return val;
            }

            // 0 means "no size found" → keep block default visibility (48")
            return 0;
        }

        private static int TryParseDiameterFromText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return 0;

            MatchCollection matches = Regex.Matches(raw, @"\d+");
            foreach (Match m in matches)
            {
                if (int.TryParse(m.Value, out int val))
                {
                    foreach (int known in KnownDiameters)
                    {
                        if (val == known)
                            return val;
                    }
                }
            }

            return 0;
        }

        private static string? MapDiameterToVisibility(int diameterInches)
        {
            return diameterInches switch
            {
                48 => "48\" MANHOLE",
                60 => "60\" MANHOLE",
                72 => "72\" MANHOLE",
                _ => null
            };
        }

        // ------------------------------------------------------------
        // Layers & blocks
        // ------------------------------------------------------------

        private static void EnsureLayer(Database db, Transaction tr, string layerName)
        {
            LayerTable lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            if (!lt.Has(layerName))
            {
                lt.UpgradeOpen();

                LayerTableRecord ltr = new LayerTableRecord
                {
                    Name = layerName
                };

                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }
        }

        /// <summary>
        /// Ensures the given block name exists in the current drawing.
        /// If missing, attempts to load it from BlockSourceFolder\[name].dwg.
        /// This method does its own quick check transaction and only
        /// calls db.Insert when the BlockTable is *not* open.
        /// </summary>
        private static void EnsureBlockDefinition(Database db, string blockName)
        {
            Editor? ed = AcadApp.DocumentManager.MdiActiveDocument?.Editor;

            // Quick check: does block already exist?
            bool hasBlock = false;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                hasBlock = bt.Has(blockName);
                tr.Commit();
            }

            if (hasBlock)
                return;

            try
            {
                string dwgName = blockName + ".dwg";
                string dwgPath = Path.Combine(BlockSourceFolder, dwgName);

                if (!File.Exists(dwgPath))
                {
                    ed?.WriteMessage(
                        $"\nUFLS-MH-AUTO: Block file \"{dwgName}\" not found in {BlockSourceFolder}.");
                    return;
                }

                // Load external DWG into a temporary database, then insert definition.
                using (Database srcDb = new Database(false, true))
                {
                    srcDb.ReadDwgFile(dwgPath, FileShare.Read, true, null);
                    db.Insert(blockName, srcDb, false);
                }

                ed?.WriteMessage(
                    $"\nUFLS-MH-AUTO: Loaded block \"{blockName}\" from {dwgPath}.");
            }
            catch (System.Exception ex)
            {
                ed?.WriteMessage(
                    $"\nUFLS-MH-AUTO: Error loading block \"{blockName}\" from {BlockSourceFolder}: {ex.Message}");
            }
        }

        private static ObjectId InsertBlockReference(
            Database db,
            Transaction tr,
            ObjectId blockDefId,
            Point3d position,
            string layerName,
            double scale,
            string? visibilityOption)
        {
            BlockTableRecord space =
                (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

            BlockReference br = new BlockReference(position, blockDefId)
            {
                Layer = layerName,
                ScaleFactors = new Scale3d(scale)
            };

            ObjectId id = space.AppendEntity(br);
            tr.AddNewlyCreatedDBObject(br, true);

            if (!string.IsNullOrEmpty(visibilityOption) && br.IsDynamicBlock)
            {
                try
                {
                    DynamicBlockReferencePropertyCollection props =
                        br.DynamicBlockReferencePropertyCollection;

                    foreach (DynamicBlockReferenceProperty prop in props)
                    {
                        if (prop.ReadOnly)
                            continue;

                        if (!prop.PropertyName.Equals("Visibility1", StringComparison.OrdinalIgnoreCase))
                            continue;

                        foreach (object allowed in prop.GetAllowedValues())
                        {
                            if (allowed is string s &&
                                s.Equals(visibilityOption, StringComparison.OrdinalIgnoreCase))
                            {
                                prop.Value = allowed;
                                break;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore dynamic prop issues; leave default visibility.
                }
            }

            return id;
        }
    }
}