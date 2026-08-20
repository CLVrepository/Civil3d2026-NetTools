using System;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using CLV_CivilTools.Shared;

namespace CLV_CivilTools.Ufls
{
    internal static class PipeTopCheckTableStyle
    {
        internal const string StyleName = "CLV-Pipe Top Check";
        internal const string TextStyleName = "CLV-Non Anno";
        internal const double DataTextHeight = 0.10;
        internal const double HeaderTextHeight = 0.10;
        internal const double DataRowHeight = 0.20;
        internal const double HeaderRowHeight = 0.26;

        internal static ObjectId Ensure(Database db, Transaction tr, Editor ed)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (tr == null) throw new ArgumentNullException(nameof(tr));
            if (ed == null) throw new ArgumentNullException(nameof(ed));

            TextStyleTable textStyles =
                (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead, false);

            if (!textStyles.Has(TextStyleName))
            {
                ed.WriteMessage($"\nPipe Top Check table: text style '{TextStyleName}' is not loaded. Load it from the CLV template and try again.");
                return ObjectId.Null;
            }

            ObjectId textStyleId = textStyles[TextStyleName];
            DBDictionary tableStyles =
                (DBDictionary)tr.GetObject(db.TableStyleDictionaryId, OpenMode.ForRead, false);

            TableStyle style;
            ObjectId styleId;

            if (tableStyles.Contains(StyleName))
            {
                styleId = tableStyles.GetAt(StyleName);
                style = (TableStyle)tr.GetObject(styleId, OpenMode.ForWrite, false);
            }
            else
            {
                tableStyles.UpgradeOpen();
                style = new TableStyle();
                styleId = tableStyles.SetAt(StyleName, style);
                tr.AddNewlyCreatedDBObject(style, true);
            }

            int allRows = (int)(RowType.TitleRow | RowType.HeaderRow | RowType.DataRow);
            style.SetTextStyle(textStyleId, allRows);
            style.SetTextHeight(HeaderTextHeight, (int)RowType.HeaderRow);
            style.SetTextHeight(DataTextHeight, (int)RowType.DataRow);
            style.SetColor(
                Color.FromColorIndex(ColorMethod.ByAci, 6),
                allRows);
            style.SetGridColor(
                Color.FromColorIndex(ColorMethod.ByAci, 6),
                (int)GridLineType.AllGridLines,
                allRows);
            style.TitleSuppressed = true;
            style.HeaderSuppressed = false;

            return styleId;
        }
    }
}
