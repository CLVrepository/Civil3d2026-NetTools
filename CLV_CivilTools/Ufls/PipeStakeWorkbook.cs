using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace CLV_CivilTools.Ufls
{
    internal static class PipeStakeWorkbook
    {
        internal sealed record Row(PipeStakeMath.LabelValues Values, double? ShotElevation, DateTime MeasuredUtc);

        private static readonly XNamespace Sheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace Office = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/package/2006/relationships";
        private static readonly XNamespace Content = "http://schemas.openxmlformats.org/package/2006/content-types";

        internal static void Write(string path, string drawing, IEnumerable<Row> rows)
        {
            var data = new XElement(Sheet + "sheetData",
                MakeRow("Point", "Survey elevation (ft)", "Pipe", "Inside size", "Material",
                    "Alignment", "Station", "Distance from pipe start (ft)", "Horizontal distance (ft)",
                    "Distance basis", "Design invert (ft)", "Design outside top (ft)", "Wall thickness (ft)",
                    "Station note", "Captured (UTC)", "Drawing"));

            foreach (Row row in rows.OrderBy(r => r.MeasuredUtc))
            {
                var values = row.Values;
                data.Add(MakeRow(values.Point, row.ShotElevation, values.Pipe, values.Size, values.Material,
                    values.Alignment, values.Station, values.Along, values.Offset,
                    values.Clamped ? "Nearest pipe end" : "Pipe centerline offset",
                    values.Invert, values.Top, values.Wall, values.StationWarning,
                    row.MeasuredUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), drawing));
            }

            // Finish the workbook before replacing a previously exported file.
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    Add(zip, "[Content_Types].xml", new XElement(Content + "Types",
                        new XElement(Content + "Default", new XAttribute("Extension", "rels"),
                            new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
                        new XElement(Content + "Override", new XAttribute("PartName", "/xl/workbook.xml"),
                            new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),
                        new XElement(Content + "Override", new XAttribute("PartName", "/xl/worksheets/sheet1.xml"),
                            new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"))));
                    Add(zip, "_rels/.rels", MakeRelationship("officeDocument", "xl/workbook.xml"));
                    Add(zip, "xl/_rels/workbook.xml.rels", MakeRelationship("worksheet", "worksheets/sheet1.xml"));
                    Add(zip, "xl/workbook.xml", new XElement(Sheet + "workbook",
                        new XElement(Sheet + "sheets", new XElement(Sheet + "sheet",
                            new XAttribute("name", "Pipe Stake Checks"), new XAttribute("sheetId", 1),
                            new XAttribute(Office + "id", "rId1")))));
                    Add(zip, "xl/worksheets/sheet1.xml", new XElement(Sheet + "worksheet", data));
                }
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        private static XElement MakeRow(params object?[] cells) =>
            new(Sheet + "row", cells.Select(value => value switch
            {
                double number when double.IsFinite(number) => new XElement(Sheet + "c",
                    new XElement(Sheet + "v", number.ToString("R", CultureInfo.InvariantCulture))),
                string text => new XElement(Sheet + "c", new XAttribute("t", "inlineStr"),
                    new XElement(Sheet + "is", new XElement(Sheet + "t",
                        new XAttribute(XNamespace.Xml + "space", "preserve"), text))),
                _ => new XElement(Sheet + "c")
            }));

        private static XElement MakeRelationship(string type, string target) =>
            new(Relationships + "Relationships", new XElement(Relationships + "Relationship",
                new XAttribute("Id", "rId1"), new XAttribute("Type", Office.NamespaceName + "/" + type),
                new XAttribute("Target", target)));

        private static void Add(ZipArchive zip, string path, XElement contents)
        {
            using var stream = zip.CreateEntry(path).Open();
            using var writer = XmlWriter.Create(stream);
            contents.Save(writer);
        }
    }
}
