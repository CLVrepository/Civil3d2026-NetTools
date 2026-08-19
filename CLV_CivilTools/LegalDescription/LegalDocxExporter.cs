using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CLV_CivilTools.LegalDescription
{
    internal static class LegalDocxExporter
    {
        private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private static readonly string[] BoldTerms =
        {
            "COMMENCING", "POINT OF BEGINNING", "TRUE POINT OF BEGINNING", "POINT OF TERMINATION"
        };

        internal static void Export(LegalDescriptionSession session, string editorText, string outputPath)
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "CLV_LegalDocx_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            try
            {
                using Stream templateStream = EmbeddedLegalResourceService.OpenBasicTemplate();
                using var templateArchive = new ZipArchive(templateStream, ZipArchiveMode.Read, leaveOpen: false);
                ExtractArchive(templateArchive, tempRoot);
                UpdateContentTypes(tempRoot);
                UpdateDocument(tempRoot, session, editorText);
                UpdateHeaders(tempRoot, session);
                UpdateCoreProperties(tempRoot);
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
                ZipFile.CreateFromDirectory(tempRoot, outputPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            }
            finally
            {
                try { Directory.Delete(tempRoot, recursive: true); } catch (System.Exception) { }
            }
        }

        private static void ExtractArchive(ZipArchive archive, string destinationRoot)
        {
            string fullRoot = Path.GetFullPath(destinationRoot) + Path.DirectorySeparatorChar;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string destinationPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
                if (!destinationPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The embedded Word template contains an invalid archive path.");

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                string? folder = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(folder))
                    Directory.CreateDirectory(folder);
                entry.ExtractToFile(destinationPath, overwrite: true);
            }
        }

        private static void UpdateContentTypes(string root)
        {
            string path = Path.Combine(root, "[Content_Types].xml");
            XDocument doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            XNamespace ct = "http://schemas.openxmlformats.org/package/2006/content-types";
            XElement? item = doc.Root?.Elements(ct + "Override")
                .FirstOrDefault(e => string.Equals((string?)e.Attribute("PartName"), "/word/document.xml", StringComparison.OrdinalIgnoreCase));
            item?.SetAttributeValue("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml");
            doc.Save(path, SaveOptions.DisableFormatting);
        }

        private static void UpdateDocument(string root, LegalDescriptionSession session, string editorText)
        {
            string path = Path.Combine(root, "word", "document.xml");
            XDocument doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            List<XElement> paragraphs = doc.Descendants(W + "p").ToList();

            ReplaceParagraph(paragraphs, "THIS LAND DESCRIPTION DESCRIBES A PARCEL OF LAND GENERALLY LOCATED XXXXX.",
                string.IsNullOrWhiteSpace(session.ExplanationText)
                    ? "THIS LAND DESCRIPTION DESCRIBES A PARCEL OF LAND GENERALLY LOCATED [ENTER LOCATION]."
                    : session.ExplanationText);

            ReplaceParagraph(paragraphs, "BEING A PORTION OF THE XXXXX QUARTER",
                LegalLandDescriptionTemplateService.Build(session), contains: true);

            string body = BuildBoundaryParagraph(editorText, session);
            XElement? bodyParagraph = FindParagraph(paragraphs, "COMMENCING AT", contains: false)
                ?? FindParagraph(paragraphs, "BEGINNING AT", contains: true);
            if (bodyParagraph != null)
                SetParagraphRuns(bodyParagraph, body, boldLegalTerms: true);

            RemoveParagraph(paragraphs, "POINT OF BEGINNING", contains: false);
            RemoveParagraph(paragraphs, "TO THE POINT OF BEGINNING.", contains: false);

            string area = ExtractAreaParagraph(editorText);
            ReplaceParagraph(paragraphs, "CONTAINING XXX SQUARE FEET", area, contains: true);
            ReplaceParagraph(paragraphs, "GRID NORTH AS DEFINED", Normalize(session.BasisOfBearingsText, "[ENTER BASIS OF BEARINGS]."), contains: true);
            ReplaceParagraph(paragraphs, "AS SHOWN ON “EXHIBIT TO ACCOMPANY LAND DESCRIPTION”", Normalize(session.ExhibitStatement,
                "AS SHOWN ON “EXHIBIT TO ACCOMPANY LAND DESCRIPTION” ATTACHED HERETO AND MADE A PART HEREOF."), contains: true);

            doc.Save(path, SaveOptions.DisableFormatting);
        }

        private static string BuildBoundaryParagraph(string editorText, LegalDescriptionSession session)
        {
            string[] blocks = Regex.Split((editorText ?? string.Empty).Replace("\r\n", "\n"), "\n\\s*\n")
                .Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            IEnumerable<string> bodyBlocks = blocks;
            if (!string.IsNullOrWhiteSpace(LegalLandDescriptionTemplateService.Build(session)) && blocks.Length > 0)
                bodyBlocks = bodyBlocks.Skip(1);
            string[] remaining = bodyBlocks.ToArray();
            if (remaining.Length > 1)
                remaining = remaining.Take(remaining.Length - 1).ToArray();
            string body = string.Join(" ", remaining.SelectMany(b => b.Split('\n')).Select(s => s.Trim()).Where(s => s.Length > 0));
            return Normalize(body, "[LEGAL DESCRIPTION BODY NOT AVAILABLE]");
        }

        private static string ExtractAreaParagraph(string editorText)
        {
            string[] blocks = Regex.Split((editorText ?? string.Empty).Replace("\r\n", "\n"), "\n\\s*\n")
                .Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            return blocks.Length == 0 ? "[AREA STATEMENT NOT AVAILABLE]" : blocks[^1].Replace("\n", " ").Trim();
        }

        private static void UpdateHeaders(string root, LegalDescriptionSession session)
        {
            foreach (string file in new[] { "header1.xml", "header2.xml" })
            {
                string path = Path.Combine(root, "word", file);
                if (!File.Exists(path)) continue;
                XDocument doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
                foreach (XElement p in doc.Descendants(W + "p").ToList())
                {
                    string text = ParagraphText(p);
                    if (text.StartsWith("APN:", StringComparison.OrdinalIgnoreCase) && text.Contains("JANUARY 16, 2020", StringComparison.OrdinalIgnoreCase))
                        UpdateFirstPageApnDateParagraph(p,
                            Normalize(session.Apn, "[ENTER APN]"),
                            Normalize(session.PreparationDate, DateTime.Now.ToString("MMMM d, yyyy").ToUpperInvariant()));
                    else if (text.StartsWith("APN:", StringComparison.OrdinalIgnoreCase))
                        ReplaceFirstMatchingText(p, text, "APN: " + Normalize(session.Apn, "[ENTER APN]"));
                    else if (text.StartsWith("BY:", StringComparison.OrdinalIgnoreCase))
                        ReplaceHeaderValuePreservingLayout(p, "BY:  ", Normalize(session.PreparedBy, "[ENTER INITIALS]"));
                    else if (text.StartsWith("P.R. BY:", StringComparison.OrdinalIgnoreCase))
                        ReplaceHeaderValuePreservingLayout(p, "P.R. BY:  ", Normalize(session.PeerReviewedBy, "[ENTER INITIALS]"));
                    else if (text.StartsWith("PAGE ", StringComparison.OrdinalIgnoreCase))
                        SetPageFieldsPreservingLayout(p);
                }
                doc.Save(path, SaveOptions.DisableFormatting);
            }
        }

        private static void UpdateCoreProperties(string root)
        {
            string path = Path.Combine(root, "docProps", "core.xml");
            if (!File.Exists(path)) return;
            XDocument doc = XDocument.Load(path, LoadOptions.PreserveWhitespace);
            XNamespace cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
            XNamespace dc = "http://purl.org/dc/elements/1.1/";
            XElement? title = doc.Root?.Element(dc + "title");
            if (title == null && doc.Root != null) doc.Root.Add(new XElement(dc + "title", "LAND DESCRIPTION"));
            else if (title != null) title.Value = "LAND DESCRIPTION";
            doc.Save(path, SaveOptions.DisableFormatting);
        }

        private static void UpdateFirstPageApnDateParagraph(XElement p, string apn, string date)
        {
            // Preserve every original run, tab, tab stop, font, size, and paragraph property
            // from Basic Template.dotx. Only replace the visible placeholder text.
            List<XElement> texts = p.Descendants(W + "t").ToList();
            XElement? apnValue = texts.FirstOrDefault(t => (t.Value ?? string.Empty).Contains("000-00-000-000", StringComparison.OrdinalIgnoreCase));
            XElement? dateValue = texts.FirstOrDefault(t => (t.Value ?? string.Empty).Contains("JANUARY 16, 2020", StringComparison.OrdinalIgnoreCase));
            if (apnValue != null) apnValue.Value = apn.ToUpperInvariant();
            if (dateValue != null) dateValue.Value = date.ToUpperInvariant();
        }

        private static void ReplaceHeaderValuePreservingLayout(XElement p, string label, string value)
        {
            List<XElement> texts = p.Descendants(W + "t").ToList();
            XElement? labelNode = texts.FirstOrDefault(t => string.Equals(t.Value, label, StringComparison.OrdinalIgnoreCase));
            if (labelNode == null)
                labelNode = texts.FirstOrDefault(t => (t.Value ?? string.Empty).StartsWith(label.Trim(), StringComparison.OrdinalIgnoreCase));

            int labelIndex = labelNode == null ? -1 : texts.IndexOf(labelNode);
            XElement? valueNode = labelIndex >= 0
                ? texts.Skip(labelIndex + 1).FirstOrDefault()
                : texts.LastOrDefault();
            if (valueNode != null)
                valueNode.Value = value.ToUpperInvariant();
        }

        private static void ReplaceFirstMatchingText(XElement p, string oldText, string newText)
        {
            XElement? first = p.Descendants(W + "t").FirstOrDefault();
            if (first == null) return;
            first.Value = newText.ToUpperInvariant();
            foreach (XElement extra in p.Descendants(W + "t").Skip(1))
                extra.Value = string.Empty;
        }

        private static void SetPageFieldsPreservingLayout(XElement p)
        {
            // Keep the original leading tab runs and their formatting so PAGE remains
            // in the exact right-side location defined by the City Surveyor template.
            List<XElement> runs = p.Elements(W + "r").ToList();
            List<XElement> leadingTabRuns = runs.TakeWhile(r => r.Descendants(W + "tab").Any()).Select(r => new XElement(r)).ToList();
            XElement? pageRun = runs.FirstOrDefault(r => string.Concat(r.Descendants(W + "t").Select(t => t.Value)).StartsWith("PAGE", StringComparison.OrdinalIgnoreCase));
            XElement? runProperties = pageRun?.Element(W + "rPr") == null ? null : new XElement(pageRun.Element(W + "rPr")!);
            XElement? pPr = p.Element(W + "pPr");
            p.RemoveNodes();
            if (pPr != null) p.Add(pPr);
            foreach (XElement tabRun in leadingTabRuns) p.Add(tabRun);
            p.Add(MakeTextRun("PAGE ", false, runProperties));
            p.Add(MakeFieldRun("PAGE", runProperties));
            p.Add(MakeTextRun(" OF ", false, runProperties));
            p.Add(MakeFieldRun("NUMPAGES", runProperties));
        }

        private static XElement MakeFieldRun(string instruction, XElement? runProperties = null)
        {
            var run = new XElement(W + "r");
            if (runProperties != null) run.Add(new XElement(runProperties));
            run.Add(
                new XElement(W + "fldChar", new XAttribute(W + "fldCharType", "begin")),
                new XElement(W + "instrText", new XAttribute(XNamespace.Xml + "space", "preserve"), " " + instruction + " "),
                new XElement(W + "fldChar", new XAttribute(W + "fldCharType", "separate")),
                new XElement(W + "t", "1"),
                new XElement(W + "fldChar", new XAttribute(W + "fldCharType", "end")));
            return run;
        }

        private static void ReplaceParagraph(List<XElement> paragraphs, string match, string replacement, bool contains = false)
        {
            XElement? p = FindParagraph(paragraphs, match, contains);
            if (p != null) SetParagraphRuns(p, replacement, false);
        }

        private static void RemoveParagraph(List<XElement> paragraphs, string match, bool contains)
        {
            XElement? p = FindParagraph(paragraphs, match, contains);
            p?.Remove();
        }

        private static XElement? FindParagraph(IEnumerable<XElement> paragraphs, string match, bool contains)
        {
            return paragraphs.FirstOrDefault(p => contains
                ? ParagraphText(p).Contains(match, StringComparison.OrdinalIgnoreCase)
                : string.Equals(ParagraphText(p).Trim(), match, StringComparison.OrdinalIgnoreCase));
        }

        private static string ParagraphText(XElement p) => string.Concat(p.Descendants(W + "t").Select(t => t.Value));

        private static void SetParagraphRuns(XElement p, string text, bool boldLegalTerms)
        {
            XElement? pPr = p.Element(W + "pPr");
            XElement? templateRunProperties = p.Elements(W + "r")
                .Select(r => r.Element(W + "rPr"))
                .FirstOrDefault(rPr => rPr != null);
            XElement? preservedRunProperties = templateRunProperties == null ? null : new XElement(templateRunProperties);

            p.RemoveNodes();
            if (pPr != null) p.Add(pPr);
            if (!boldLegalTerms)
            {
                p.Add(MakeTextRun(text.ToUpperInvariant(), false, preservedRunProperties));
                return;
            }

            string source = text.ToUpperInvariant();
            string pattern = "(" + string.Join("|", BoldTerms.OrderByDescending(s => s.Length).Select(Regex.Escape)) + ")";
            foreach (string part in Regex.Split(source, pattern, RegexOptions.IgnoreCase).Where(s => s.Length > 0))
                p.Add(MakeTextRun(part, BoldTerms.Any(t => string.Equals(t, part, StringComparison.OrdinalIgnoreCase)), preservedRunProperties));
        }

        private static XElement MakeTextRun(string text, bool bold, XElement? templateRunProperties = null)
        {
            var run = new XElement(W + "r");
            XElement runProperties = templateRunProperties == null
                ? new XElement(W + "rPr")
                : new XElement(templateRunProperties);

            // Preserve the Arial 12-point run formatting from Basic Template.dotx.
            // Only toggle bold for approved complete legal phrases.
            runProperties.Elements(W + "b").Remove();
            runProperties.Elements(W + "bCs").Remove();
            if (bold)
            {
                runProperties.Add(new XElement(W + "b"));
                runProperties.Add(new XElement(W + "bCs"));
            }
            run.Add(runProperties);
            run.Add(new XElement(W + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), text));
            return run;
        }

        private static string Normalize(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim().ToUpperInvariant();
    }
}
