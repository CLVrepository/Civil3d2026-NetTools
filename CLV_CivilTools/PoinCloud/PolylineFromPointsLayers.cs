using System;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;

namespace CLV_CivilTools.PointCloud
{
    public static partial class PolylineFromPointsCommands
    {
        private const string BreaklineLayerName = "V-DTM~-GBRK";
        private const string RetainedLayerName = "0";

        private static ObjectId RequireExistingDestination(Database db, Transaction tr, string name)
        {
            var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!layers.Has(name))
                throw new InvalidOperationException("Destination layer '" + name
                    + "' is missing. Create/import it with your drawing's layer standards, then retry. No objects were changed.");
            var layer = (LayerTableRecord)tr.GetObject(layers[name], OpenMode.ForRead);
            if (layer.IsDependent || layer.IsLocked || layer.IsOff || layer.IsFrozen)
                throw new InvalidOperationException("Destination layer '" + name
                    + "' must be local, on, thawed and unlocked. Its settings were not changed.");
            return layer.ObjectId;
        }

        private static void ApplyBreaklineLayer(Polyline3d output, ObjectId layerId)
        {
            output.LayerId = layerId;
            output.ColorIndex = 256;
            output.Linetype = "ByLayer";
            output.LineWeight = LineWeight.ByLayer;
            output.Transparency = new Transparency(TransparencyMethod.ByLayer);
            output.PlotStyleName = "ByLayer";
        }
    }
}
