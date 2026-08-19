using System;
using System.Collections.Generic;
using System.Linq;

using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace CLV_CivilTools.LegalDescription
{
    internal static class LegalMTextService
    {
        private const string LinkRecordKey = "CLV_LEGAL_DESCRIPTION_LINK";

        internal static ObjectId CreateLinkedMText(Database db, Point3d insertionPoint, LegalDescriptionSession session, string editorText)
        {
            using Transaction tr = db.TransactionManager.StartTransaction();
            BlockTableRecord space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

            double textHeight = db.Textsize > 0.0 ? db.Textsize : 0.1;
            var mtext = new MText
            {
                Location = insertionPoint,
                Attachment = AttachmentPoint.TopLeft,
                TextHeight = textHeight,
                Width = textHeight * 80.0,
                Contents = BuildFinalMText(session, editorText),
                LayerId = db.Clayer,
                TextStyleId = db.Textstyle
            };

            ObjectId id = space.AppendEntity(mtext);
            tr.AddNewlyCreatedDBObject(mtext, true);
            WriteLinkRecord(mtext, tr);
            tr.Commit();
            return id;
        }

        internal static int UpdateLinkedMText(Database db, LegalDescriptionSession session, string editorText)
        {
            if (session.LinkedMTextHandles == null || session.LinkedMTextHandles.Count == 0)
                return 0;

            string paragraph = BuildFinalMText(session, editorText);
            var validHandles = new List<string>();
            int updated = 0;

            using Transaction tr = db.TransactionManager.StartTransaction();
            foreach (string handleText in session.LinkedMTextHandles.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!TryGetObjectId(db, handleText, out ObjectId id) || id.IsErased)
                    continue;

                DBObject obj;
                try { obj = tr.GetObject(id, OpenMode.ForWrite, false); }
                catch (System.Exception) { continue; }

                if (obj is not MText mtext || !HasLinkRecord(mtext, tr))
                    continue;

                mtext.Contents = paragraph;
                validHandles.Add(handleText);
                updated++;
            }
            tr.Commit();
            session.LinkedMTextHandles = validHandles;
            return updated;
        }


        internal static List<MText> CreateCourseHighlightClones(
            Database db,
            LegalDescriptionSession session,
            IEnumerable<string> searchCandidates)
        {
            var clones = new List<MText>();
            if (session.LinkedMTextHandles == null || session.LinkedMTextHandles.Count == 0)
                return clones;

            string[] candidates = searchCandidates
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(value => value.Length)
                .ToArray();
            if (candidates.Length == 0)
                return clones;

            using Transaction tr = db.TransactionManager.StartOpenCloseTransaction();
            foreach (string handleText in session.LinkedMTextHandles.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!TryGetObjectId(db, handleText, out ObjectId id) || id.IsErased)
                    continue;

                DBObject obj;
                try { obj = tr.GetObject(id, OpenMode.ForRead, false); }
                catch (System.Exception) { continue; }

                if (obj is not MText source || !HasLinkRecord(source, tr))
                    continue;

                string contents = source.Contents ?? string.Empty;
                if (!TryApplyCourseHighlight(contents, candidates, out string highlightedContents))
                    continue;

                var clone = (MText)source.Clone();
                clone.Contents = highlightedContents;
                clones.Add(clone);
            }
            tr.Commit();
            return clones;
        }

        private static bool TryApplyCourseHighlight(string contents, IEnumerable<string> candidates, out string highlightedContents)
        {
            highlightedContents = contents;
            foreach (string candidate in candidates)
            {
                int index = contents.IndexOf(candidate, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                    continue;

                string matched = contents.Substring(index, candidate.Length);
                // ACI 6 is magenta. Underline makes the selected call visible even when
                // the drawing background or text color reduces the color contrast.
                string formatted = "{\\C6;\\L" + matched + "\\l}";
                highlightedContents = contents.Substring(0, index) + formatted + contents.Substring(index + candidate.Length);
                return true;
            }
            return false;
        }

        internal static string BuildFinalMText(LegalDescriptionSession session, string editorText)
        {
            if (string.IsNullOrWhiteSpace(editorText))
                return string.Empty;

            LegalTextStyle style = LegalTextStyleService.GetStyle(session.TextStyleName);
            string normalized = editorText
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n');

            string[] blocks = normalized.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
            var paragraphs = new List<string>();
            foreach (string block in blocks)
            {
                string[] lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (lines.Length > 0)
                    paragraphs.Add(string.Join(" ", lines));
            }

            string result = string.Join("\\P\\P", paragraphs);
            return style.AllCaps ? result.ToUpperInvariant() : result;
        }

        private static void WriteLinkRecord(MText mtext, Transaction tr)
        {
            if (mtext.ExtensionDictionary.IsNull)
                mtext.CreateExtensionDictionary();

            DBDictionary dictionary = (DBDictionary)tr.GetObject(mtext.ExtensionDictionary, OpenMode.ForWrite);
            Xrecord record;
            if (dictionary.Contains(LinkRecordKey))
                record = (Xrecord)tr.GetObject(dictionary.GetAt(LinkRecordKey), OpenMode.ForWrite);
            else
            {
                record = new Xrecord();
                dictionary.SetAt(LinkRecordKey, record);
                tr.AddNewlyCreatedDBObject(record, true);
            }
            record.Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, "PRIMARY"));
        }

        private static bool HasLinkRecord(MText mtext, Transaction tr)
        {
            if (mtext.ExtensionDictionary.IsNull)
                return false;
            try
            {
                DBDictionary dictionary = (DBDictionary)tr.GetObject(mtext.ExtensionDictionary, OpenMode.ForRead);
                return dictionary.Contains(LinkRecordKey);
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        private static bool TryGetObjectId(Database db, string handleText, out ObjectId id)
        {
            id = ObjectId.Null;
            try
            {
                long value = Convert.ToInt64(handleText, 16);
                id = db.GetObjectId(false, new Handle(value), 0);
                return !id.IsNull && id.IsValid;
            }
            catch (System.Exception)
            {
                return false;
            }
        }
    }
}
