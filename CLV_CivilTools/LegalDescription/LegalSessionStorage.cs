using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

using Autodesk.AutoCAD.DatabaseServices;

namespace CLV_CivilTools.LegalDescription
{
    internal static class LegalSessionStorage
    {
        private const string DictionaryKey = "CLV_LEGAL_DESCRIPTION_PHASE12";
        private static readonly JsonSerializerOptions Options = new() { WriteIndented = false, PropertyNameCaseInsensitive = true };

        internal static LegalDescriptionSession? Load(Database db)
        {
            using Transaction tr = db.TransactionManager.StartOpenCloseTransaction();
            DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            if (!nod.Contains(DictionaryKey))
                return null;
            Xrecord record = (Xrecord)tr.GetObject(nod.GetAt(DictionaryKey), OpenMode.ForRead);
            var json = new StringBuilder();
            if (record.Data != null)
            {
                foreach (TypedValue value in record.Data)
                    if (value.TypeCode == (int)DxfCode.Text && value.Value is string part)
                        json.Append(part);
            }
            tr.Commit();
            try { return JsonSerializer.Deserialize<LegalDescriptionSession>(json.ToString(), Options); }
            catch (System.Exception) { return null; }
        }

        internal static void Save(Database db, LegalDescriptionSession session)
        {
            session.UpdatedLocal = DateTime.Now;
            string json = JsonSerializer.Serialize(session, Options);
            var values = new List<TypedValue>();
            for (int offset = 0; offset < json.Length; offset += 1800)
                values.Add(new TypedValue((int)DxfCode.Text, json.Substring(offset, Math.Min(1800, json.Length - offset))));

            using Transaction tr = db.TransactionManager.StartTransaction();
            DBDictionary nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            Xrecord record;
            if (nod.Contains(DictionaryKey))
                record = (Xrecord)tr.GetObject(nod.GetAt(DictionaryKey), OpenMode.ForWrite);
            else
            {
                nod.UpgradeOpen();
                record = new Xrecord();
                nod.SetAt(DictionaryKey, record);
                tr.AddNewlyCreatedDBObject(record, true);
            }
            record.Data = new ResultBuffer(values.ToArray());
            tr.Commit();
        }
    }
}
