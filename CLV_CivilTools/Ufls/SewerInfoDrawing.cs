using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace CLV_CivilTools.Ufls.SewerInfo
{
    internal static class SewerInfoDrawing
    {
        private const string CatalogKey = "CLV_SEWER_CATALOG_V1";
        private const string SnapshotKey = "CLV_SEWER_INFO_BLOCK_V1";
        private const string DefinitionKey = "CLV_SEWER_INFO_DEFINITION_V1";
        internal const string InfoLayer = "CLV-SSMH-INFO";
        internal const string InfoTextStyle = "CLV-Non Anno";
        internal static SewerCatalog? Catalog(Database db, Transaction tr)
        {
            string? data = Read((DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead), CatalogKey, tr);
            return data == null ? null : SewerInfoModel.Parse(data);
        }
        internal static void Save(Database db, Transaction tr, SewerCatalog c)
        {
            SewerInfoModel.Validate(c);
            Write((DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead), CatalogKey, SewerInfoModel.Serialize(c), tr);
        }
        internal static SewerCatalog Snapshot(BlockReference b, Transaction tr)
        {
            if (b.ExtensionDictionary.IsNull) throw new InvalidOperationException("Select a manhole info block created by SSMHINSERT.");
            string? json = Read((DBDictionary)tr.GetObject(b.ExtensionDictionary, OpenMode.ForRead), SnapshotKey, tr);
            if (json == null) throw new InvalidOperationException("Selected block has no sewer information snapshot.");
            SewerCatalog c = SewerInfoModel.Parse(json);
            if (c.Manholes.Count != 1) throw new InvalidOperationException("Invalid block snapshot.");
            return c;
        }
        internal static void CheckDisplay(BlockReference b, SewerCatalog snapshot, Transaction tr)
        {
            Dictionary<string, string> expected = SewerInfoModel.Attributes(snapshot, snapshot.Manholes[0]);
            var actual = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (ObjectId id in b.AttributeCollection)
            {
                if (id.IsErased) continue;
                var a = (AttributeReference)tr.GetObject(id, OpenMode.ForRead);
                if (!actual.TryAdd(a.Tag, a.TextString)) throw new InvalidOperationException("Duplicate block attribute.");
            }
            if (actual.Count != expected.Count || expected.Any(k => !actual.TryGetValue(k.Key, out string? value) || value != k.Value))
                throw new InvalidOperationException("Block attributes differ from stored values. Edit the catalog and use SSMHREFRESH; direct attribute edits are not applied to a network.");
        }
        internal static BlockReference Place(Database db, Transaction tr, SewerCatalog catalog, SewerManhole mh, Point3d point, double height)
        {
            ObjectId layer = Layer(db, tr);
            ObjectId definition = Definition(db, tr, mh.Connections.Count);
            var model = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var b = new BlockReference(point, definition);
            b.SetDatabaseDefaults(db); b.LayerId = layer; b.ScaleFactors = new Scale3d(height);
            model.AppendEntity(b); tr.AddNewlyCreatedDBObject(b, true);
            Fill(b, db, tr, catalog, mh);
            return b;
        }
        internal static void Refresh(BlockReference b, Database db, Transaction tr, SewerCatalog catalog, SewerManhole mh)
        {
            Writable(b, tr);
            b.UpgradeOpen();
            foreach (ObjectId id in b.AttributeCollection) if (!id.IsErased) tr.GetObject(id, OpenMode.ForWrite).Erase();
            b.BlockTableRecord = Definition(db, tr, mh.Connections.Count);
            Fill(b, db, tr, catalog, mh);
        }
        private static void Fill(BlockReference b, Database db, Transaction tr, SewerCatalog catalog, SewerManhole mh)
        {
            ObjectId style = RequiredTextStyle(db, tr);
            Dictionary<string, string> values = SewerInfoModel.Attributes(catalog, mh);
            var definition = (BlockTableRecord)tr.GetObject(b.BlockTableRecord, OpenMode.ForRead);
            foreach (ObjectId id in definition)
            {
                if (tr.GetObject(id, OpenMode.ForRead) is not AttributeDefinition ad) continue;
                var ar = new AttributeReference(); ar.SetAttributeFromBlock(ad, b.BlockTransform);
                ar.TextString = values[ad.Tag]; ar.LayerId = b.LayerId; ar.TextStyleId = style;
                b.AttributeCollection.AppendAttribute(ar); tr.AddNewlyCreatedDBObject(ar, true);
                if (ar.TextStyleId != style) throw new InvalidOperationException("Info-block text-style verification failed.");
            }
            SewerCatalog snapshot = SewerInfoModel.Copy(catalog);
            snapshot.Manholes = new List<SewerManhole> { SewerInfoModel.Copy(mh) };
            if (b.ExtensionDictionary.IsNull) b.CreateExtensionDictionary();
            Write((DBDictionary)tr.GetObject(b.ExtensionDictionary, OpenMode.ForRead), SnapshotKey, SewerInfoModel.Serialize(snapshot), tr);
            CheckDisplay(b, snapshot, tr);
        }
        private static ObjectId Definition(Database db, Transaction tr, int count)
        {
            ObjectId style = RequiredTextStyle(db, tr);
            string name = "CLV_SSMH_INFO_V1_C" + count.ToString("D2");
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(name))
            {
                var existing = (BlockTableRecord)tr.GetObject(bt[name], OpenMode.ForRead);
                string? signature = existing.ExtensionDictionary.IsNull ? null : Read((DBDictionary)tr.GetObject(existing.ExtensionDictionary, OpenMode.ForRead), DefinitionKey, tr);
                if (signature != "1:" + count || existing.IsFromExternalReference)
                    throw new InvalidOperationException("Block definition name is already used by a different definition: " + name);
                foreach (ObjectId id in existing)
                    if (tr.GetObject(id, OpenMode.ForRead) is AttributeDefinition ad && ad.TextStyleId != style)
                    { ad.UpgradeOpen(); ad.TextStyleId = style; ad.RecordGraphicsModified(true); }
                return existing.ObjectId;
            }
            bt.UpgradeOpen(); var def = new BlockTableRecord { Name = name, Origin = Point3d.Origin };
            bt.Add(def); tr.AddNewlyCreatedDBObject(def, true);
            using var circle = new Circle(Point3d.Origin, Vector3d.ZAxis, 0.4) { Layer = "0", ColorIndex = 256 };
            def.AppendEntity(circle); tr.AddNewlyCreatedDBObject(circle, true);
            using var x = new Line(new Point3d(-0.6, 0, 0), new Point3d(0.6, 0, 0)) { Layer = "0", ColorIndex = 256 };
            def.AppendEntity(x); tr.AddNewlyCreatedDBObject(x, true);
            using var y = new Line(new Point3d(0, -0.6, 0), new Point3d(0, 0.6, 0)) { Layer = "0", ColorIndex = 256 };
            def.AppendEntity(y); tr.AddNewlyCreatedDBObject(y, true);
            string[] tags = new[] { "MH_NAME", "RIM", "STRUCT_SIZE", "SOURCE" }
                .Concat(Enumerable.Range(1, count).Select(i => "CONN_" + i.ToString("D2"))).ToArray();
            for (int i = 0; i < tags.Length; i++)
            {
                using var ad = new AttributeDefinition
                {
                    Tag = tags[i], Prompt = tags[i], TextString = "?", Height = 1,
                    Position = new Point3d(2, -i * 1.5, 0), Layer = "0", ColorIndex = 256,
                    TextStyleId = style
                };
                def.AppendEntity(ad); tr.AddNewlyCreatedDBObject(ad, true);
            }
            def.CreateExtensionDictionary();
            Write((DBDictionary)tr.GetObject(def.ExtensionDictionary, OpenMode.ForRead), DefinitionKey, "1:" + count, tr);
            return def.ObjectId;
        }
        private static ObjectId RequiredTextStyle(Database db, Transaction tr)
        {
            var styles = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (!styles.Has(InfoTextStyle)) throw new InvalidOperationException("Text style '" + InfoTextStyle + "' is missing. Load it from the CLV drawing template, then retry. No fallback style was used.");
            return styles[InfoTextStyle];
        }
        private static ObjectId Layer(Database db, Transaction tr)
        {
            var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (layers.Has(InfoLayer))
            {
                var existing = (LayerTableRecord)tr.GetObject(layers[InfoLayer], OpenMode.ForRead);
                if (existing.IsLocked || existing.IsOff || existing.IsFrozen || existing.IsDependent)
                    throw new InvalidOperationException(InfoLayer + " must be local, on, thawed and unlocked.");
                return existing.ObjectId;
            }
            layers.UpgradeOpen(); var layer = new LayerTableRecord { Name = InfoLayer, IsPlottable = false };
            layer.Color = Autodesk.AutoCAD.Colors.Color.FromColorIndex(Autodesk.AutoCAD.Colors.ColorMethod.ByAci, 3);
            layers.Add(layer); tr.AddNewlyCreatedDBObject(layer, true); return layer.ObjectId;
        }
        internal static void Writable(Entity e, Transaction tr)
        {
            var layer = (LayerTableRecord)tr.GetObject(e.LayerId, OpenMode.ForRead);
            if (e.OwnerId != SymbolUtilityServices.GetBlockModelSpaceId(e.Database) || layer.IsLocked || layer.IsDependent)
                throw new InvalidOperationException("Use direct editable model-space objects on unlocked local layers.");
        }
        internal static void Attach(DBObject target, string key, string json, Transaction tr)
        {
            if (!target.IsWriteEnabled) target.UpgradeOpen();
            if (target.ExtensionDictionary.IsNull) target.CreateExtensionDictionary();
            Write((DBDictionary)tr.GetObject(target.ExtensionDictionary, OpenMode.ForRead), key, json, tr);
        }
        private static string? Read(DBDictionary dict, string key, Transaction tr)
        {
            if (!dict.Contains(key)) return null;
            if (tr.GetObject(dict.GetAt(key), OpenMode.ForRead) is not Xrecord record) throw new InvalidOperationException("Unexpected data at " + key);
            using ResultBuffer? data = record.Data;
            TypedValue[] values = data?.AsArray() ?? Array.Empty<TypedValue>();
            if (values.Any(v => v.TypeCode != (int)DxfCode.Text || v.Value is not string)) throw new InvalidOperationException("Invalid sewer Xrecord.");
            string result = string.Concat(values.Select(v => (string)v.Value));
            if (result.Length > SewerInfoModel.MaxJsonLength) throw new InvalidOperationException("Stored catalog is too large.");
            return result;
        }
        private static void Write(DBDictionary dict, string key, string value, Transaction tr)
        {
            if (value.Length > SewerInfoModel.MaxJsonLength) throw new InvalidOperationException("Stored data exceeds 8 MiB.");
            Xrecord record;
            if (dict.Contains(key)) record = (Xrecord)tr.GetObject(dict.GetAt(key), OpenMode.ForWrite);
            else { dict.UpgradeOpen(); record = new Xrecord(); dict.SetAt(key, record); tr.AddNewlyCreatedDBObject(record, true); }
            // Serializer escapes non-ASCII; short chunks also stay below DXF text byte limits.
            var parts = new List<TypedValue>();
            for (int i = 0; i < value.Length; i += 200) parts.Add(new TypedValue((int)DxfCode.Text, value.Substring(i, Math.Min(200, value.Length - i))));
            using var buffer = new ResultBuffer(parts.ToArray()); record.Data = buffer;
        }
    }
}

