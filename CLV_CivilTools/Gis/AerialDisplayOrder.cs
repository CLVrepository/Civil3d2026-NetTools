using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.Gis.Map.DisplayManagement;
using Autodesk.Gis.Map.Platform;
using Autodesk.Gis.Map.Platform.Interop;
using OSGeo.MapGuide;
using DisplayMap = Autodesk.Gis.Map.DisplayManagement.Map;
using DisplayGroup = Autodesk.Gis.Map.DisplayManagement.Group;

namespace CLV_CivilTools.Gis
{
    internal static class AerialDisplayOrder
    {
        internal static void EnsureMapBaseFirst(Database db, AcMapMap map, MgLayerGroup aerialGroup, IReadOnlyList<MgLayerBase> aerials)
        {
            ObjectId displayId = AcMapDisplayManagementService.GetDMMapFromMap(map);
            ObjectId aerialId = AcMapDisplayManagementService.GetDMGroupFromGroup(aerialGroup);
            using Transaction tr = db.TransactionManager.StartTransaction();
            var display = (DisplayMap)tr.GetObject(displayId, OpenMode.ForWrite);
            DisplayGroup root = display;
            // Map overrides GetEnumerator for the flattened draw order. Use Group's
            // explicit iterator for the tree so group and element indices stay separate.
            List<ObjectId> treeBefore = Items(root.NewIterator(true, true));
            List<ObjectId> bases = treeBefore.FindAll(id => tr.GetObject(id, OpenMode.ForRead) is BaseElement);
            if (bases.Count != 1 || !treeBefore.Contains(aerialId))
                throw new InvalidOperationException("Cannot uniquely resolve Map Base and the aerial root group in Display Manager.");
            ObjectId baseId = bases[0];

            // Group view has its own item order; moving MgLayerCollection entries alone
            // does not move the aerial folder below Map Base.
            if (treeBefore[^1] != aerialId) root.MoveItem(aerialId, null);
            MoveTreeItemFirst(root, baseId);

            // Set the actual display-element draw order too, including Map Base (which
            // is a drawing element, not an FDO layer in MgLayerCollection).
            bool hadDrawOrder = display.DrawOrderDefined;
            List<ObjectId>? drawBefore = hadDrawOrder ? Items(display.GetEnumerator()) : null;
            if (!hadDrawOrder) display.ActivateDrawOrder();
            var aerialElements = new HashSet<ObjectId>();
            foreach (MgLayerBase aerial in aerials)
            {
                ObjectId elementId = AcMapDisplayManagementService.GetDMElementFromLayer(aerial);
                aerialElements.Add(elementId);
                display.MoveElement(elementId, null);
            }
            IEnumerator first = display.GetEnumerator();
            try
            {
                if (!first.MoveNext()) throw new InvalidOperationException("Display Manager draw order is empty.");
                if ((ObjectId)first.Current != baseId) display.MoveElement(baseId, first);
            }
            finally { (first as IDisposable)?.Dispose(); }

            List<ObjectId> treeAfter = Items(root.NewIterator(true, true));
            List<ObjectId> drawn = Items(display.GetEnumerator());
            if (treeAfter.Count == 0 || treeAfter[0] != baseId || treeAfter[^1] != aerialId || drawn.Count == 0 || drawn[0] != baseId)
                throw new InvalidOperationException("Map Base/aerial order did not match the requested Display Manager order.");
            if (!treeBefore.Where(id => id != baseId && id != aerialId).SequenceEqual(treeAfter.Where(id => id != baseId && id != aerialId))
                || treeBefore.Count != treeAfter.Count)
                throw new InvalidOperationException("Unrelated Display Manager groups changed order unexpectedly.");
            if (drawn.Count < aerialElements.Count || !aerialElements.SetEquals(drawn.Skip(drawn.Count - aerialElements.Count)))
                throw new InvalidOperationException("Aerial elements did not move below the other map elements.");
            if (drawBefore != null && (!drawBefore.Where(id => id != baseId && !aerialElements.Contains(id)).SequenceEqual(drawn.Where(id => id != baseId && !aerialElements.Contains(id)))
                || drawBefore.Count != drawn.Count))
                throw new InvalidOperationException("Unrelated Display Manager elements changed order unexpectedly.");
            tr.Commit();
        }

        private static void MoveTreeItemFirst(DisplayGroup root, ObjectId id)
        {
            IEnumerator first = root.NewIterator(true, true);
            try
            {
                if (!first.MoveNext()) throw new InvalidOperationException("Display Manager group order is empty.");
                if ((ObjectId)first.Current != id) root.MoveItem(id, first);
            }
            finally { (first as IDisposable)?.Dispose(); }
        }

        private static List<ObjectId> Items(IEnumerator items)
        {
            var ids = new List<ObjectId>();
            try { while (items.MoveNext()) ids.Add((ObjectId)items.Current); }
            finally { (items as IDisposable)?.Dispose(); }
            return ids;
        }
    }
}
