using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.Colors;
using AcColor = Autodesk.AutoCAD.Colors.Color;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

namespace CLV_CivilTools.Shared
{
    internal static class LayerStandards
    {
        private const string ServerLinetypeFile = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Linetypes\acad.lin";
        private const string PlotStyleTemplatePath = @"\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Drawing Templates\Reference Templates\Settings (2026).dwt";
        private const string ExpectedStbName = "CLV - 2026.stb";

        private static readonly LayerSpec[] GisSpecs =
        {
            new("C-SSWR-PIPE-CNTR-E", 9, "CENTER2", "XS-30", "Sanitary Sewer: Pipe: Center: Existing"),
            new("C-SSWR-PIPE-E", 106, "HIDDEN2", "SSWR-PIPE-E", "Sanitary Sewer: Pipe: Existing"),
            new("C-SSWR-STRC-E", 106, "HIDDEN3", "SSWR-STRC-E", "Sanitary Sewer: Structure: Existing"),
            new("C-SSWR-STRC-INNR", 10, "HIDDEN4", "XS-60", "Sanitary Sewer: Structure: Inner Wall"),
            new("C-STRM-PIPE-CNTR-E", 9, "CENTER2", "XS-30", "Storm Drain: Pipe: Center: Existing"),
            new("C-STRM-PIPE-E", 60, "HIDDEN2", "STRM-PIPE-E", "Storm Drain: Pipe: Existing"),
            new("C-STRM-STRC-E", 60, "HIDDEN3", "STRM-STRC-E", "Storm Drain: Structure: Existing"),
            new("C-STRM-STRC-INNR", 10, "HIDDEN4", "XS-60", "Storm Drain: Structure: Inner Wall")
        };

        private static readonly Dictionary<string, LayerSpec> GisSpecMap = GisSpecs
            .ToDictionary(s => Normalize(s.Name), s => s, StringComparer.OrdinalIgnoreCase);

        private static readonly LayerSpec SurveyDimSpec =
            new("V-ANNO-DIMS", 2, "Continuous", "M", "Annotation: Dimensions");

        internal const string SurveyMapOriginalLayerName = "V-SURV-MAP~-ORIG";
        internal const string SurveyMapAdjustedLayerName = "V-SURV-MAP~-ADJ~";
        internal const string SurveyMapReviewLayerName = "V-SURV-MAP~-REVIEW";
        internal const short SurveyMapConstraintHighlightColorIndex = 6;

        internal const string SurveyLegendLayerName = "G-BRDR-ANNO";
        internal const string SurveyLineworkReviewLayerName = "V-SURV-LWRK-REVIEW";

        internal const string UflsObjectHighlightRedLayerName = "V-SURV-HGLT-R";
        internal const string UflsObjectHighlightGreenLayerName = "V-SURV-HGLT-G";

        private const int UflsHighlightLayerTransparencyPercent = 70;

        private static readonly LayerSpec UflsObjectHighlightRedSpec =
            new(UflsObjectHighlightRedLayerName, 1, "Continuous", "M", "UFLS: Highlight overlay: red", UflsHighlightLayerTransparencyPercent);

        private static readonly LayerSpec UflsObjectHighlightGreenSpec =
            new(UflsObjectHighlightGreenLayerName, 3, "Continuous", "M", "UFLS: Highlight overlay: green", UflsHighlightLayerTransparencyPercent);

        internal const string SurveyLineCurveLabelLayerName = "V-LABL";
        internal const string SurveyAreaLabelLayerName = "V-ANNO-DIMS";
        internal const string SurveyTieLineLayerName = "V-CTRL-TIES-LINE";

        private static readonly LayerSpec SurveyRoadLabelSpec =
            new("C-LABL-STNM", 3, "Continuous", "M", "Label: Street Names");

        private static readonly LayerSpec SurveyLineCurveLabelSpec =
            new(SurveyLineCurveLabelLayerName, 2, "Continuous", "S", "Labels: line and curve geometry");

        private static readonly LayerSpec SurveyTieLineSpec =
            new(SurveyTieLineLayerName, 2, "HIDDEN3", "XS-60", "Survey Control: tie lines");

        private static readonly LayerSpec SurveyLineworkReviewSpec =
            new(SurveyLineworkReviewLayerName, 1, "Continuous", "M", "Survey: Duplicate/overlap linework review markers", 0);

        private static readonly LayerSpec SurveyMapOriginalSpec =
            new(SurveyMapOriginalLayerName, 8, "Continuous", "M", "Survey Map: Auto Closure Original Linework");

        private static readonly LayerSpec SurveyMapAdjustedSpec =
            new(SurveyMapAdjustedLayerName, 7, "Continuous", "M", "Survey Map: Auto Closure Adjusted Overlay");

        private static readonly LayerSpec SurveyMapReviewSpec =
            new(SurveyMapReviewLayerName, 6, "Continuous", "M", "Survey Map: Auto Closure Review Markers");

        private static readonly Dictionary<string, LayerSpec> ManagedSpecMap = BuildManagedSpecMap();

        internal static void EnsureGisLayers(Database db, Editor ed)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (ed == null) throw new ArgumentNullException(nameof(ed));

            EnsureManagedLayers(db, ed, GisSpecs);
        }

        internal static void EnsureSurveyDimLayer(Database db, Editor ed)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (ed == null) throw new ArgumentNullException(nameof(ed));

            EnsureManagedLayers(db, ed, new[] { SurveyDimSpec });
        }

        internal static void EnsureSurveyRoadLabelLayer(Database db, Editor ed)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (ed == null) throw new ArgumentNullException(nameof(ed));

            EnsureManagedLayers(db, ed, new[] { SurveyRoadLabelSpec });
        }

        internal static void EnsureSurveyLineCurveLabelLayer(Database db, Editor ed)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (ed == null) throw new ArgumentNullException(nameof(ed));

            EnsureManagedLayers(db, ed, new[] { SurveyLineCurveLabelSpec });
        }

        internal static void EnsureSurveyAreaLabelLayer(Database db, Editor ed)
        {
            EnsureSurveyDimLayer(db, ed);
        }

        internal static void EnsureSurveyTieLineLayer(Database db, Editor ed)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (ed == null) throw new ArgumentNullException(nameof(ed));

            EnsureManagedLayers(db, ed, new[] { SurveyTieLineSpec });
        }

        internal static void EnsureSurveyLegendLayer(Database db, Transaction tr, Editor ed)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (ed == null) throw new ArgumentNullException(nameof(ed));

            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(SurveyLegendLayerName))
                return;

            lt.UpgradeOpen();
            var ltr = new LayerTableRecord
            {
                Name = SurveyLegendLayerName,
                Color = AcColor.FromColorIndex(ColorMethod.ByAci, 7),
                IsPlottable = true,
                Description = "Survey: Legend annotation"
            };

            lt.Add(ltr);
            tr.AddNewlyCreatedDBObject(ltr, true);

            var linetypeId = EnsureLinetypeLoaded(db, tr, ed, "Continuous");
            if (!linetypeId.IsNull)
                ltr.LinetypeObjectId = linetypeId;
        }

        internal static void EnsureUflsObjectHighlightLayer(Database db, Transaction tr, Editor ed, bool isRed)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (ed == null) throw new ArgumentNullException(nameof(ed));

            EnsureLayer(db, tr, ed, isRed ? UflsObjectHighlightRedSpec : UflsObjectHighlightGreenSpec);
        }

        internal static void EnsureSurveyMapClosureLayers(Database db, Editor ed)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (ed == null) throw new ArgumentNullException(nameof(ed));

            EnsureManagedLayers(db, ed, new[] { SurveyMapOriginalSpec, SurveyMapAdjustedSpec, SurveyMapReviewSpec });
        }

        internal static void EnsureSurveyLineworkReviewLayer(Database db, Transaction tr, Editor ed)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (ed == null) throw new ArgumentNullException(nameof(ed));

            EnsureLayer(db, tr, ed, SurveyLineworkReviewSpec);
        }

        internal static bool TryEnsureManagedLayer(Database db, Transaction tr, Editor ed, string layerName)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (ed == null) throw new ArgumentNullException(nameof(ed));

            if (!ManagedSpecMap.TryGetValue(Normalize(layerName), out var spec))
                return false;

            EnsureLayer(db, tr, ed, spec);
            return true;
        }

        internal static bool TryEnsureManagedGisLayer(Database db, Transaction tr, Editor ed, string layerName)
        {
            if (!GisSpecMap.ContainsKey(Normalize(layerName)))
                return false;

            return TryEnsureManagedLayer(db, tr, ed, layerName);
        }

        private static void EnsureManagedLayers(Database db, Editor ed, IReadOnlyCollection<LayerSpec> specs)
        {
            if (specs.Count == 0)
                return;

            var requiredPlotStyles = specs
                .Select(s => s.PlotStyleName)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            TryImportMissingPlotStyles(db, ed, requiredPlotStyles);

            using var tr = db.TransactionManager.StartTransaction();
            foreach (var linetype in specs.Select(s => s.LinetypeName).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                EnsureLinetypeLoaded(db, tr, ed, linetype);
            }

            foreach (var spec in specs)
            {
                EnsureLayer(db, tr, ed, spec);
            }

            tr.Commit();
        }

        private static void EnsureLayer(Database db, Transaction tr, Editor ed, LayerSpec spec)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

            LayerTableRecord ltr;
            if (lt.Has(spec.Name))
            {
                ltr = (LayerTableRecord)tr.GetObject(lt[spec.Name], OpenMode.ForWrite);
            }
            else
            {
                lt.UpgradeOpen();
                ltr = new LayerTableRecord { Name = spec.Name };
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }

            ltr.Color = AcColor.FromColorIndex(ColorMethod.ByAci, (short)spec.ColorIndex);
            ltr.IsPlottable = true;
            ltr.Description = spec.Description;

            if (spec.TransparencyPercent.HasValue)
                ltr.Transparency = TransparencyFromPercent(spec.TransparencyPercent.Value);

            var linetypeId = EnsureLinetypeLoaded(db, tr, ed, spec.LinetypeName);
            if (!linetypeId.IsNull)
                ltr.LinetypeObjectId = linetypeId;

            TryAssignNamedPlotStyle(db, tr, ed, ltr, spec.PlotStyleName);
        }

        private static ObjectId EnsureLinetypeLoaded(Database db, Transaction tr, Editor ed, string linetypeName)
        {
            var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (ltt.Has(linetypeName))
                return ltt[linetypeName];

            foreach (var source in new[] { ServerLinetypeFile, "acad.lin", "acadiso.lin" })
            {
                try
                {
                    db.LoadLineTypeFile(linetypeName, source);
                    ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                    if (ltt.Has(linetypeName))
                    {
                        if (string.Equals(source, ServerLinetypeFile, StringComparison.OrdinalIgnoreCase))
                            ed.WriteMessage($"\nLayer standards: loaded linetype '{linetypeName}' from server LIN file.");
                        return ltt[linetypeName];
                    }
                }
                catch
                {
                    // try next source
                }
            }

            ed.WriteMessage($"\nLayer standards: failed to load linetype '{linetypeName}'. Layer may keep its current linetype.");
            return ltt.Has("Continuous") ? ltt["Continuous"] : ObjectId.Null;
        }

        private static void TryImportMissingPlotStyles(Database targetDb, Editor ed, IReadOnlyCollection<string> requiredStyles)
        {
            if (requiredStyles.Count == 0)
                return;

            var missingBefore = GetMissingPlotStyles(targetDb, requiredStyles);
            if (missingBefore.Count == 0)
                return;

            try
            {
                using var sourceDb = new Database(false, true);
                sourceDb.ReadDwgFile(PlotStyleTemplatePath, System.IO.FileShare.Read, true, string.Empty);
                sourceDb.CloseInput(true);

                using var sourceTr = sourceDb.TransactionManager.StartTransaction();
                using var targetTr = targetDb.TransactionManager.StartTransaction();

                var sourceDict = (DBDictionary)sourceTr.GetObject(sourceDb.PlotStyleNameDictionaryId, OpenMode.ForRead);
                var targetDict = (DBDictionary)targetTr.GetObject(targetDb.PlotStyleNameDictionaryId, OpenMode.ForRead);

                var sourceMap = BuildDictionaryNameMap(sourceDict);
                var targetMap = BuildDictionaryNameMap(targetDict);
                var idsToClone = new ObjectIdCollection();

                foreach (var required in missingBefore)
                {
                    if (!sourceMap.TryGetValue(Normalize(required), out var sourceId))
                        continue;

                    if (!targetMap.ContainsKey(Normalize(required)))
                        idsToClone.Add(sourceId);
                }

                if (idsToClone.Count > 0)
                {
                    var mapping = new IdMapping();
                    sourceDb.WblockCloneObjects(idsToClone, targetDb.PlotStyleNameDictionaryId, mapping, DuplicateRecordCloning.Ignore, false);
                }

                targetTr.Commit();
                sourceTr.Commit();
            }
            catch (Exception ex)
            {
                ed.WriteMessage($"\nLayer standards: unable to import missing plot styles from reference template -> {PlotStyleTemplatePath}");
                ed.WriteMessage($"\nLayer standards: plot style import detail: {ex.Message}");
                return;
            }

            var missingAfter = GetMissingPlotStyles(targetDb, requiredStyles);
            if (missingAfter.Count == 0)
            {
                ed.WriteMessage($"\nLayer standards: required named plot styles imported from reference template '{PlotStyleTemplatePath}'.");
            }
            else
            {
                ed.WriteMessage($"\nLayer standards: some named plot styles are still missing after import from '{PlotStyleTemplatePath}'. Missing: {string.Join(", ", missingAfter)}");
            }
        }

        private static List<string> GetMissingPlotStyles(Database db, IReadOnlyCollection<string> requiredStyles)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var dict = (DBDictionary)tr.GetObject(db.PlotStyleNameDictionaryId, OpenMode.ForRead);
            var map = BuildDictionaryNameMap(dict);
            tr.Commit();

            return requiredStyles
                .Where(r => !map.ContainsKey(Normalize(r)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void TryAssignNamedPlotStyle(Database db, Transaction tr, Editor ed, LayerTableRecord ltr, string plotStyleName)
        {
            if (string.IsNullOrWhiteSpace(plotStyleName))
                return;

            try
            {
                var psDict = (DBDictionary)tr.GetObject(db.PlotStyleNameDictionaryId, OpenMode.ForRead);
                var map = BuildDictionaryNameMap(psDict);
                if (map.TryGetValue(Normalize(plotStyleName), out var styleId))
                {
                    ltr.PlotStyleNameId = styleId;
                    return;
                }

                var available = string.Join(", ", map.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
                ed.WriteMessage($"\nLayer standards: named plot style '{plotStyleName}' was not found in this drawing's plot style dictionary for '{ExpectedStbName}'. Layer '{ltr.Name}' kept its current plot style. Available styles: {available}");
            }
            catch (Exception ex)
            {
                ed.WriteMessage($"\nLayer standards: failed assigning named plot style '{plotStyleName}' to layer '{ltr.Name}': {ex.Message}");
            }
        }

        private static Dictionary<string, ObjectId> BuildDictionaryNameMap(DBDictionary dict)
        {
            var map = new Dictionary<string, ObjectId>(StringComparer.OrdinalIgnoreCase);
            foreach (DBDictionaryEntry entry in dict)
            {
                var key = Normalize(entry.Key);
                if (!map.ContainsKey(key))
                    map.Add(key, entry.Value);
            }
            return map;
        }

        private static Dictionary<string, LayerSpec> BuildManagedSpecMap()
        {
            var map = new Dictionary<string, LayerSpec>(StringComparer.OrdinalIgnoreCase);

            foreach (var spec in GisSpecs)
            {
                string key = Normalize(spec.Name);
                if (!map.ContainsKey(key))
                    map.Add(key, spec);
            }

            string surveyKey = Normalize(SurveyDimSpec.Name);
            if (!map.ContainsKey(surveyKey))
                map.Add(surveyKey, SurveyDimSpec);

            string surveyRoadKey = Normalize(SurveyRoadLabelSpec.Name);
            if (!map.ContainsKey(surveyRoadKey))
                map.Add(surveyRoadKey, SurveyRoadLabelSpec);

            foreach (var spec in new[] { SurveyLineCurveLabelSpec, SurveyTieLineSpec, SurveyMapOriginalSpec, SurveyMapAdjustedSpec, SurveyMapReviewSpec, SurveyLineworkReviewSpec, UflsObjectHighlightRedSpec, UflsObjectHighlightGreenSpec })
            {
                string key = Normalize(spec.Name);
                if (!map.ContainsKey(key))
                    map.Add(key, spec);
            }

            return map;
        }

        private static Transparency TransparencyFromPercent(int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 90) percent = 90;

            // AutoCAD transparency percent: 0 = opaque, higher = more transparent.
            // Transparency alpha: 255 = opaque, lower = more transparent.
            byte alpha = (byte)Math.Max(0, 255 - (percent * 255 / 100));
            return new Transparency(alpha);
        }

        private static string Normalize(string name) => (name ?? string.Empty).Trim();

        private readonly record struct LayerSpec(
            string Name,
            int ColorIndex,
            string LinetypeName,
            string PlotStyleName,
            string Description,
            int? TransparencyPercent = null);
    }

}
