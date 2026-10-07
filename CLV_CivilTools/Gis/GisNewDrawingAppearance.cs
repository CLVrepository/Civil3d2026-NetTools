using System;

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

        internal sealed record ColorValue(ColorMode Mode, int? Aci, byte? Red, byte? Green, byte? Blue,
            bool HasBookName, string? BookName, bool HasColorName, string? ColorName);
        internal sealed record TransparencyValue(TransparencyMode Mode, byte? Alpha);
        internal sealed record LinetypeElement(LinetypeElementKind Kind, int ShapeNumber, string? Text);

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
