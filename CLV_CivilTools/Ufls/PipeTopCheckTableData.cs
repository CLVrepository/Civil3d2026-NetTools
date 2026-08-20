using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

namespace CLV_CivilTools.Ufls
{
    /// <summary>
    /// Persists the Pipe Top Check IDs that a generated table represents.
    /// This allows the table to be updated, expanded, or reduced without
    /// relying on the table's visible text.
    /// </summary>
    internal static class PipeTopCheckTableData
    {
        private const string RecordName = "CLV_PIPE_TOP_CHECK_TABLE";
        private const string Marker = "PIPE_TOP_CHECK_TABLE";
        private const int SchemaVersion = 1;

        internal static void Write(DBObject owner, Transaction tr, IEnumerable<Guid> checkIds)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));

            if (owner.ExtensionDictionary.IsNull)
                owner.CreateExtensionDictionary();

            DBDictionary extensionDictionary =
                (DBDictionary)tr.GetObject(owner.ExtensionDictionary, OpenMode.ForWrite, false);

            Xrecord record = extensionDictionary.Contains(RecordName)
                ? (Xrecord)tr.GetObject(extensionDictionary.GetAt(RecordName), OpenMode.ForWrite, false)
                : new Xrecord();

            string ids = string.Join(";", checkIds);
            record.Data = new ResultBuffer(
                new TypedValue((int)DxfCode.Text, Marker),
                new TypedValue((int)DxfCode.Int32, SchemaVersion),
                new TypedValue((int)DxfCode.Text + 1, ids));

            if (record.ObjectId.IsNull)
            {
                extensionDictionary.SetAt(RecordName, record);
                tr.AddNewlyCreatedDBObject(record, true);
            }
        }

        internal static bool TryRead(DBObject owner, Transaction tr, out List<Guid> checkIds)
        {
            checkIds = new List<Guid>();

            if (owner == null || owner.ExtensionDictionary.IsNull)
                return false;

            DBDictionary extensionDictionary =
                (DBDictionary)tr.GetObject(owner.ExtensionDictionary, OpenMode.ForRead, false);

            if (!extensionDictionary.Contains(RecordName))
                return false;

            Xrecord record = (Xrecord)tr.GetObject(extensionDictionary.GetAt(RecordName), OpenMode.ForRead, false);
            ResultBuffer? data = record.Data;
            if (data == null)
                return false;

            string marker = string.Empty;
            int schema = 0;
            string idText = string.Empty;

            foreach (TypedValue value in data)
            {
                switch (value.TypeCode)
                {
                    case (int)DxfCode.Text:
                        marker = value.Value?.ToString() ?? string.Empty;
                        break;
                    case (int)DxfCode.Int32:
                        schema = Convert.ToInt32(value.Value);
                        break;
                    case (int)DxfCode.Text + 1:
                        idText = value.Value?.ToString() ?? string.Empty;
                        break;
                }
            }

            if (!string.Equals(marker, Marker, StringComparison.Ordinal) || schema != SchemaVersion)
                return false;

            foreach (string part in idText.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                if (Guid.TryParse(part, out Guid id))
                    checkIds.Add(id);
            }

            return true;
        }
    }
}
