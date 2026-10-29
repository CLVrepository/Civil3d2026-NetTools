using System;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using AcColor = Autodesk.AutoCAD.Colors.Color;

namespace CLV_CivilTools.PointCloud
{
    public static partial class PolylineFromPointsCommands
    {
        private const string LayerPreferencesKey = "CLV_PCT3DFROMPOINTS_LAYERS_V1";
        private const string DefaultBreaklineLayer = "V-DTM~GBRK";
        private const string DefaultRetainedLayer = "V-PNTC-TRACE-2D";
        private sealed record LayerChoices(string Output, string Retained);

        private static LayerChoices ReadLayerChoices(Database db)
        {
            using Transaction tr = db.TransactionManager.StartOpenCloseTransaction();
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (nod.Contains(LayerPreferencesKey))
            {
                if (tr.GetObject(nod.GetAt(LayerPreferencesKey), OpenMode.ForRead) is not Xrecord record)
                    throw new InvalidOperationException("The converter's saved layer preferences have an unexpected object type.");
                using ResultBuffer? data = record.Data;
                TypedValue[] values = data?.AsArray() ?? Array.Empty<TypedValue>();
                if (values.Length == 3 && values[0].Value is string version && version == "V1"
                    && values[1].Value is string output && !string.IsNullOrWhiteSpace(output)
                    && values[2].Value is string retained && !string.IsNullOrWhiteSpace(retained))
                    return new LayerChoices(output, retained);
            }
            return new LayerChoices(DefaultBreaklineLayer, DefaultRetainedLayer);
        }

        private static string? PromptLayerName(Editor ed, string label, string defaultName)
        {
            while (true)
            {
                var options = new PromptStringOptions("\n" + label + " <" + defaultName + ">: ")
                { AllowSpaces = true, DefaultValue = defaultName, UseDefaultValue = true };
                PromptResult result = ed.GetString(options);
                if (result.Status != PromptStatus.OK && result.Status != PromptStatus.None) return null;
                string name = string.IsNullOrWhiteSpace(result.StringResult) ? defaultName : result.StringResult.Trim();
                try { SymbolUtilityServices.ValidateSymbolName(name, false); return name; }
                catch (System.Exception e) { ed.WriteMessage("\nInvalid layer name: " + e.Message); }
            }
        }

        private static ObjectId RequireExistingDestination(Database db, Transaction tr, string name)
        {
            var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!layers.Has(name))
                throw new InvalidOperationException("3D destination layer '" + name
                    + "' is missing. Create/import it with your drawing's layer standards, then retry. No objects were changed.");
            var layer = (LayerTableRecord)tr.GetObject(layers[name], OpenMode.ForRead);
            if (layer.IsDependent || layer.IsLocked || layer.IsOff || layer.IsFrozen)
                throw new InvalidOperationException("Destination layer '" + name
                    + "' must be local, on, thawed and unlocked. Its settings were not changed.");
            return layer.ObjectId;
        }

        private static bool ValidateRetainedDestination(Database db, Transaction tr, string name, bool replace)
        {
            if (replace) return false;
            var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (layers.Has(name)) { RequireExistingDestination(db, tr, name); return false; }
            // A missing retained-trace layer is created only after final Yes.
            return true;
        }

        private static ObjectId GetRetainedDestination(Database db, Transaction tr, string name)
        {
            var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (layers.Has(name)) return RequireExistingDestination(db, tr, name);
            layers.UpgradeOpen();
            using var layer = new LayerTableRecord
            {
                Name = name,
                Color = AcColor.FromColorIndex(ColorMethod.ByAci, 8),
                IsPlottable = false,
                Description = "Point cloud: retained 2D breakline traces"
            };
            ObjectId id = layers.Add(layer); tr.AddNewlyCreatedDBObject(layer, true);
            return id;
        }

        private static void ApplyBreaklineLayer(Polyline3d output, ObjectId layerId)
        {
            output.LayerId = layerId;
            // Standard layer properties control the new breakline, rather than
            // explicit overrides inherited from the original Layer 0 trace.
            output.ColorIndex = 256;
            output.Linetype = "ByLayer";
            output.LineWeight = LineWeight.ByLayer;
            output.Transparency = new Transparency(TransparencyMethod.ByLayer);
            output.PlotStyleName = "ByLayer";
        }

        private static void SaveLayerChoices(Database db, Transaction tr, LayerChoices choices)
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            Xrecord record;
            if (nod.Contains(LayerPreferencesKey))
                record = tr.GetObject(nod.GetAt(LayerPreferencesKey), OpenMode.ForWrite) as Xrecord
                    ?? throw new InvalidOperationException("The converter's layer preference record has an unexpected type.");
            else
            {
                nod.UpgradeOpen(); record = new Xrecord();
                nod.SetAt(LayerPreferencesKey, record); tr.AddNewlyCreatedDBObject(record, true);
            }
            using var data = new ResultBuffer(new TypedValue((int)DxfCode.Text, "V1"),
                new TypedValue((int)DxfCode.Text, choices.Output), new TypedValue((int)DxfCode.Text, choices.Retained));
            record.Data = data;
        }
    }
}
