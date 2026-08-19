using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

using Autodesk.AutoCAD.DatabaseServices;

namespace CLV_CivilTools.PdfViewer
{
    internal static class PdfViewerStorage
    {
        private const string DictionaryKey = "CLV_PDF_VIEWER_V1";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false,
            PropertyNameCaseInsensitive = true
        };

        public static PdfViewerDrawingState Load(Database database)
        {
            string jsonText = string.Empty;

            using (Transaction transaction = database.TransactionManager.StartOpenCloseTransaction())
            {
                DBDictionary nod = (DBDictionary)transaction.GetObject(database.NamedObjectsDictionaryId, OpenMode.ForRead);
                if (nod.Contains(DictionaryKey))
                {
                    Xrecord record = (Xrecord)transaction.GetObject(nod.GetAt(DictionaryKey), OpenMode.ForRead);
                    StringBuilder json = new();
                    ResultBuffer? data = record.Data;
                    if (data != null)
                    {
                        foreach (TypedValue value in data)
                        {
                            if (value.TypeCode == (int)DxfCode.Text && value.Value is string text)
                                json.Append(text);
                        }
                    }

                    jsonText = json.ToString();
                }

                transaction.Commit();
            }

            if (string.IsNullOrEmpty(jsonText))
                return new PdfViewerDrawingState();

            try
            {
                return JsonSerializer.Deserialize<PdfViewerDrawingState>(jsonText, JsonOptions)
                    ?? new PdfViewerDrawingState();
            }
            catch (System.Exception)
            {
                return new PdfViewerDrawingState();
            }
        }

        public static void Save(Database database, PdfViewerDrawingState state)
        {
            string json = JsonSerializer.Serialize(state, JsonOptions);
            List<TypedValue> values = new();
            const int chunkSize = 1800;
            for (int offset = 0; offset < json.Length; offset += chunkSize)
            {
                int length = Math.Min(chunkSize, json.Length - offset);
                values.Add(new TypedValue((int)DxfCode.Text, json.Substring(offset, length)));
            }

            using Transaction transaction = database.TransactionManager.StartTransaction();
            DBDictionary nod = (DBDictionary)transaction.GetObject(database.NamedObjectsDictionaryId, OpenMode.ForRead);
            Xrecord record;

            if (nod.Contains(DictionaryKey))
            {
                record = (Xrecord)transaction.GetObject(nod.GetAt(DictionaryKey), OpenMode.ForWrite);
            }
            else
            {
                nod.UpgradeOpen();
                record = new Xrecord();
                nod.SetAt(DictionaryKey, record);
                transaction.AddNewlyCreatedDBObject(record, true);
            }

            record.Data = new ResultBuffer(values.ToArray());
            transaction.Commit();
        }

        public static string ResolvePdfPath(PdfViewerDrawingState state, Database database)
        {
            if (!string.IsNullOrWhiteSpace(state.PdfPath) && File.Exists(state.PdfPath))
                return state.PdfPath;

            string drawingPath = database.Filename;
            if (!string.IsNullOrWhiteSpace(drawingPath) && !string.IsNullOrWhiteSpace(state.RelativePdfPath))
            {
                string? drawingFolder = Path.GetDirectoryName(drawingPath);
                if (!string.IsNullOrWhiteSpace(drawingFolder))
                {
                    string candidate = Path.GetFullPath(Path.Combine(drawingFolder, state.RelativePdfPath));
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            return string.Empty;
        }

        public static void SetPdfPath(PdfViewerDrawingState state, Database database, string pdfPath)
        {
            state.PdfPath = pdfPath;
            state.RelativePdfPath = string.Empty;

            string drawingPath = database.Filename;
            string? drawingFolder = string.IsNullOrWhiteSpace(drawingPath) ? null : Path.GetDirectoryName(drawingPath);
            if (!string.IsNullOrWhiteSpace(drawingFolder))
            {
                try
                {
                    state.RelativePdfPath = Path.GetRelativePath(drawingFolder, pdfPath);
                }
                catch (System.Exception)
                {
                    state.RelativePdfPath = Path.GetFileName(pdfPath);
                }
            }
        }
    }
}
