using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CLV_CivilTools.Gis
{
    /// <summary>
    /// Method-specific appearance snapshots. Value readers are lazy because the
    /// native RGB, Alpha and linetype TextAt getters have mode preconditions.
    /// No exception becomes a default value; invalid modes remain review errors.
    /// </summary>
    internal static class GisNewDrawingAppearance
    {
        internal enum ColorMode { ByLayer, ByBlock, Aci, Rgb, Foreground }
        internal enum TransparencyMode { ByLayer, ByBlock, Alpha }
        internal enum LinetypeElementKind { PlainDash, Shape, Text }
        internal enum ResourcePropertyRole { Value, SymbolName, Description }

        internal sealed record ColorValue(ColorMode Mode, int? Aci, byte? Red, byte? Green, byte? Blue,
            bool HasBookName, string? BookName, bool HasColorName, string? ColorName);
        internal sealed record TransparencyValue(TransparencyMode Mode, byte? Alpha);
        internal sealed record LinetypeElement(LinetypeElementKind Kind, int ShapeNumber, string? Text);
        // Value is an exact canonical payload; Display is diagnostic text only.
        // Neither presentation escaping nor truncation may affect equivalence.
        internal sealed record ResourceProperty(string Name, string Value, string Display,
            ResourcePropertyRole Role = ResourcePropertyRole.Value);

        internal static IReadOnlyList<string> FindResourceDifferences(IEnumerable<ResourceProperty> source,
            IEnumerable<ResourceProperty> target, bool strictSource)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(target);
            var differences = new List<string>();
            Dictionary<string, ResourceProperty> sourceProperties = Index(source, "source");
            Dictionary<string, ResourceProperty> targetProperties = Index(target, "target");
            foreach (ResourceProperty expected in sourceProperties.Values)
            {
                string field = FormatResourceDisplay(expected.Name);
                if (!targetProperties.TryGetValue(expected.Name, out ResourceProperty? actual))
                {
                    differences.Add(field + ": missing target property; source " + FormatResourceDisplay(expected.Display));
                    continue;
                }
                if (expected.Role != actual.Role)
                {
                    differences.Add(field + $": property role differs; source {expected.Role}; target {actual.Role}");
                    continue;
                }
                // Description topology still matters, even when its value does not.
                if (!strictSource && expected.Role == ResourcePropertyRole.Description) continue;
                StringComparer comparer = !strictSource && expected.Role == ResourcePropertyRole.SymbolName
                    ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
                if (!comparer.Equals(expected.Value, actual.Value))
                    differences.Add(field + ": source " + FormatResourceDisplay(expected.Display) +
                        "; target " + FormatResourceDisplay(actual.Display));
            }
            foreach (ResourceProperty actual in targetProperties.Values)
                if (!sourceProperties.ContainsKey(actual.Name))
                    differences.Add(FormatResourceDisplay(actual.Name) + ": extra target property; target " +
                        FormatResourceDisplay(actual.Display));
            return differences.AsReadOnly();

            Dictionary<string, ResourceProperty> Index(IEnumerable<ResourceProperty> properties, string side)
            {
                var index = new Dictionary<string, ResourceProperty>(StringComparer.Ordinal);
                foreach (ResourceProperty? property in properties)
                {
                    if (property == null || string.IsNullOrEmpty(property.Name))
                    {
                        differences.Add(side + ": null property or empty property name");
                        continue;
                    }
                    string field = FormatResourceDisplay(property.Name);
                    if (!index.TryAdd(property.Name, property))
                        differences.Add(field + ": duplicate " + side + " property");
                    if (property.Value == null)
                        differences.Add(field + ": null " + side + " canonical value");
                    if (property.Role is not (ResourcePropertyRole.Value or ResourcePropertyRole.SymbolName or ResourcePropertyRole.Description))
                        differences.Add(field + ": invalid " + side + " property role " + property.Role);
                }
                return index;
            }
        }

        // Only entity symbol-reference tokens use this key. Resource properties
        // retain their raw spelling so same-source verification remains exact.
        internal static string SymbolNameKey(string name)
        {
            ArgumentNullException.ThrowIfNull(name);
            return name.ToUpperInvariant();
        }

        internal static string FormatResourceDisplay(string? text)
        {
            if (text == null) return "<null>";
            const int maximumLength = 160;
            var display = new StringBuilder("\"");
            foreach (char character in text)
            {
                string part = character switch
                {
                    '"' => "\\\"",
                    '\\' => "\\\\",
                    '\r' => "\\r",
                    '\n' => "\\n",
                    '\t' => "\\t",
                    _ when char.IsControl(character) || char.GetUnicodeCategory(character) is
                        UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.Surrogate
                        => "\\u" + ((int)character).ToString("X4", CultureInfo.InvariantCulture),
                    _ => character.ToString()
                };
                if (display.Length + part.Length + 4 > maximumLength)
                {
                    display.Append("...");
                    break;
                }
                display.Append(part);
            }
            return display.Append('"').ToString();
        }

        internal static ColorValue ReadColor(ColorMode mode, Func<int> readAci,
            Func<(byte Red, byte Green, byte Blue)> readRgb,
            Func<bool> hasBookName, Func<string?> readBookName, Func<bool> hasColorName, Func<string?> readColorName)
        {
            int? aci = null;
            byte? red = null, green = null, blue = null;
            switch (mode)
            {
                case ColorMode.ByLayer:
                case ColorMode.ByBlock:
                case ColorMode.Foreground:
                    break; // Inherited/sentinel colors have no numeric component payload.
                case ColorMode.Aci:
                    aci = readAci();
                    if (aci < 0 || aci > 256)
                        throw new InvalidOperationException("ACI color index must be between 0 and 256.");
                    break;
                case ColorMode.Rgb:
                    (byte r, byte g, byte b) = readRgb();
                    red = r; green = g; blue = b;
                    break;
                default:
                    throw new InvalidOperationException("Unsupported color mode: " + mode);
            }
            // The name-presence flags are safe for every color method. Preserve
            // optional metadata without asking the native API for absent names.
            bool hasBook = hasBookName();
            string? book = hasBook ? readBookName() ?? throw new InvalidOperationException("Color reports a book name but returned null.") : null;
            bool hasName = hasColorName();
            string? name = hasName ? readColorName() ?? throw new InvalidOperationException("Color reports a color name but returned null.") : null;
            return new ColorValue(mode, aci, red, green, blue, hasBook, book, hasName, name);
        }

        internal static TransparencyMode GetTransparencyMode(bool isInvalid, bool isByLayer, bool isByBlock, bool isByAlpha)
        {
            int modes = (isByLayer ? 1 : 0) + (isByBlock ? 1 : 0) + (isByAlpha ? 1 : 0);
            if (isInvalid || modes != 1)
                throw new InvalidOperationException("Invalid or conflicting native transparency method flags.");
            return isByLayer ? TransparencyMode.ByLayer : isByBlock ? TransparencyMode.ByBlock : TransparencyMode.Alpha;
        }

        internal static TransparencyValue ReadTransparency(TransparencyMode mode, Func<byte> readAlpha)
            => mode switch
            {
                TransparencyMode.ByLayer => new TransparencyValue(mode, null),
                TransparencyMode.ByBlock => new TransparencyValue(mode, null),
                TransparencyMode.Alpha => new TransparencyValue(mode, readAlpha()),
                _ => throw new InvalidOperationException("Unsupported transparency mode: " + mode)
            };

        internal static LinetypeElement ReadLinetypeElement(bool hasStyle, int shapeNumber, Func<string?> readText)
        {
            if (shapeNumber < 0 || !hasStyle && shapeNumber != 0)
                throw new InvalidOperationException("Invalid linetype shape number/style combination.");
            if (!hasStyle) return new LinetypeElement(LinetypeElementKind.PlainDash, 0, null);
            if (shapeNumber != 0) return new LinetypeElement(LinetypeElementKind.Shape, shapeNumber, null);
            return new LinetypeElement(LinetypeElementKind.Text, 0,
                readText() ?? throw new InvalidOperationException("Linetype text element returned null text."));
        }

        internal static T ReadAt<T>(string stage, Func<T> read)
        {
            try { return read(); }
            catch (System.Exception ex) { throw new InvalidOperationException(stage + ": " + ex.Message, ex); }
        }
    }
}
