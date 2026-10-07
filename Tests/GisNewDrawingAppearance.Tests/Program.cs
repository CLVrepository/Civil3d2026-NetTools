using static CLV_CivilTools.Gis.GisNewDrawingAppearance;

int passed = 0;
int failed = 0;

foreach (ColorMode mode in new[] { ColorMode.ByLayer, ColorMode.ByBlock, ColorMode.Foreground })
{
    Run($"{mode}: no ACI, RGB or absent name getter", () =>
    {
        var probe = new ColorProbe();
        probe.Forbidden.UnionWith(new[] { "ColorIndex", "RGB", "BookName", "ColorName" });
        ColorValue value = probe.Read(mode);
        Equal(mode, value.Mode);
        Equal<int?>(null, value.Aci);
        Equal<byte?>(null, value.Red);
        Equal<byte?>(null, value.Green);
        Equal<byte?>(null, value.Blue);
        Equal(false, value.HasBookName);
        Equal<string?>(null, value.BookName);
        Equal(false, value.HasColorName);
        Equal<string?>(null, value.ColorName);
        Equal("HasBookName,HasColorName", string.Join(",", probe.Reads));
    });
}

foreach (int aci in new[] { 0, 1, 7, 255, 256 })
{
    Run($"ACI {aci}: only index is read and preserved", () =>
    {
        var probe = new ColorProbe { Aci = aci };
        probe.Forbidden.UnionWith(new[] { "RGB", "BookName", "ColorName" });
        ColorValue value = probe.Read(ColorMode.Aci);
        Equal(ColorMode.Aci, value.Mode);
        Equal<int?>(aci, value.Aci);
        Equal<byte?>(null, value.Red);
        Equal<byte?>(null, value.Green);
        Equal<byte?>(null, value.Blue);
        Equal("ColorIndex,HasBookName,HasColorName", string.Join(",", probe.Reads));
    });
}

Run("RGB channels retain exact ordered bytes without index", () =>
{
    var probe = new ColorProbe { Rgb = (0, 128, 255) };
    probe.Forbidden.UnionWith(new[] { "ColorIndex", "BookName", "ColorName" });
    ColorValue value = probe.Read(ColorMode.Rgb);
    Equal(ColorMode.Rgb, value.Mode);
    Equal<int?>(null, value.Aci);
    Equal<byte?>(0, value.Red);
    Equal<byte?>(128, value.Green);
    Equal<byte?>(255, value.Blue);
    Equal("RGB,HasBookName,HasColorName", string.Join(",", probe.Reads));
});

Run("ByLayer, ByBlock, ACI, RGB and Foreground remain distinct", () =>
{
    ColorValue[] values = Enum.GetValues<ColorMode>().Select(mode => new ColorProbe().Read(mode)).ToArray();
    Equal(5, values.Length);
    Equal(5, values.Distinct().Count());
    NotEqual(new ColorProbe().Read(ColorMode.ByLayer), new ColorProbe { Aci = 256 }.Read(ColorMode.Aci));
    NotEqual(new ColorProbe().Read(ColorMode.ByBlock), new ColorProbe { Aci = 0 }.Read(ColorMode.Aci));
    NotEqual(new ColorProbe().Read(ColorMode.Foreground), new ColorProbe { Aci = 7 }.Read(ColorMode.Aci));
});

foreach (ColorMode mode in Enum.GetValues<ColorMode>())
{
    foreach (int flags in Enumerable.Range(0, 4))
    {
        bool hasBook = (flags & 1) != 0;
        bool hasName = (flags & 2) != 0;
        Run($"{mode}: name-presence flags {flags} gate only matching getters", () =>
        {
            var probe = new ColorProbe
            {
                HasBook = hasBook, HasName = hasName,
                Book = "  Civil Book Ω  ", Name = "Survey|Blue:128"
            };
            if (!hasBook) probe.Forbidden.Add("BookName");
            if (!hasName) probe.Forbidden.Add("ColorName");
            ColorValue value = probe.Read(mode);
            Equal(hasBook, value.HasBookName);
            Equal(hasName, value.HasColorName);
            Equal(hasBook ? probe.Book : null, value.BookName);
            Equal(hasName ? probe.Name : null, value.ColorName);
            Equal(1, probe.Reads.Count(name => name == "HasBookName"));
            Equal(1, probe.Reads.Count(name => name == "HasColorName"));
            Equal(hasBook ? 1 : 0, probe.Reads.Count(name => name == "BookName"));
            Equal(hasName ? 1 : 0, probe.Reads.Count(name => name == "ColorName"));
        });
    }
}

Run("Identical RGB keeps named color and book identity", () =>
{
    ColorValue ReadNamed(string book, string name) => new ColorProbe
    {
        Rgb = (17, 34, 51), HasBook = true, HasName = true, Book = book, Name = name
    }.Read(ColorMode.Rgb);
    ColorValue original = ReadNamed("Book A", "Blue");
    Equal(original, ReadNamed("Book A", "Blue"));
    NotEqual(original, ReadNamed("Book B", "Blue"));
    NotEqual(original, ReadNamed("Book A", "blue"));
    NotEqual(original, ReadNamed("Book A ", "Blue"));
    NotEqual(original, new ColorProbe { Rgb = (17, 34, 51) }.Read(ColorMode.Rgb));
});

Run("Empty but present names differ from absent names", () =>
{
    ColorValue absent = new ColorProbe().Read(ColorMode.Rgb);
    ColorValue book = new ColorProbe { HasBook = true, Book = "" }.Read(ColorMode.Rgb);
    ColorValue name = new ColorProbe { HasName = true, Name = "" }.Read(ColorMode.Rgb);
    Equal("", book.BookName);
    Equal("", name.ColorName);
    Equal(3, new[] { absent, book, name }.Distinct().Count());
});

foreach (int aci in new[] { -1, 257, int.MinValue, int.MaxValue })
{
    Run($"Out-of-range ACI {aci} fails closed", () =>
    {
        var probe = new ColorProbe { Aci = aci };
        InvalidOperationException error = Throws<InvalidOperationException>(() => probe.Read(ColorMode.Aci));
        Contains("ACI color index", error.Message);
        Equal("ColorIndex", string.Join(",", probe.Reads));
    });
}

foreach (int mode in new[] { -1, 5, 192, int.MaxValue })
{
    Run($"Unknown color mode {mode} fails before any getter", () =>
    {
        var probe = new ColorProbe();
        Contains("Unsupported color mode", Throws<InvalidOperationException>(() => probe.Read((ColorMode)mode)).Message);
        Equal(0, probe.Reads.Count);
    });
}

Run("Present book with null payload fails closed", () =>
{
    var probe = new ColorProbe { HasBook = true, Book = null };
    Contains("book name but returned null", Throws<InvalidOperationException>(() => probe.Read(ColorMode.Rgb)).Message);
});
Run("Present color name with null payload fails closed", () =>
{
    var probe = new ColorProbe { HasName = true, Name = null };
    Contains("color name but returned null", Throws<InvalidOperationException>(() => probe.Read(ColorMode.Rgb)).Message);
});

// Exhaust the entire native flag truth table: one method, never IsInvalid.
foreach (int flags in Enumerable.Range(0, 16))
{
    Run($"Transparency flag truth table {flags}", () =>
    {
        bool invalid = (flags & 8) != 0;
        bool layer = (flags & 1) != 0;
        bool block = (flags & 2) != 0;
        bool alpha = (flags & 4) != 0;
        int methods = (layer ? 1 : 0) + (block ? 1 : 0) + (alpha ? 1 : 0);
        if (invalid || methods != 1)
        {
            Contains("Invalid or conflicting", Throws<InvalidOperationException>(() =>
                GetTransparencyMode(invalid, layer, block, alpha)).Message);
        }
        else
        {
            Equal(layer ? TransparencyMode.ByLayer : block ? TransparencyMode.ByBlock : TransparencyMode.Alpha,
                GetTransparencyMode(invalid, layer, block, alpha));
        }
    });
}

foreach (TransparencyMode mode in new[] { TransparencyMode.ByLayer, TransparencyMode.ByBlock })
{
    Run($"{mode}: inherited transparency never reads Alpha", () =>
    {
        int calls = 0;
        TransparencyValue value = ReadTransparency(mode, () =>
        {
            calls++;
            throw new Exception("Inapplicable Alpha getter invoked.");
        });
        Equal(mode, value.Mode);
        Equal<byte?>(null, value.Alpha);
        Equal(0, calls);
    });
}

foreach (byte alpha in new byte[] { 0, 128, 255 })
{
    Run($"Explicit alpha {alpha} is retained and read once", () =>
    {
        int calls = 0;
        TransparencyValue value = ReadTransparency(TransparencyMode.Alpha, () => { calls++; return alpha; });
        Equal(TransparencyMode.Alpha, value.Mode);
        Equal<byte?>(alpha, value.Alpha);
        Equal(1, calls);
    });
}

Run("Both inherited modes and explicit alpha 0, 128, 255 remain distinct", () =>
{
    TransparencyValue[] values =
    {
        ReadTransparency(TransparencyMode.ByLayer, () => throw new Exception("Unexpected Alpha")),
        ReadTransparency(TransparencyMode.ByBlock, () => throw new Exception("Unexpected Alpha")),
        ReadTransparency(TransparencyMode.Alpha, () => 0),
        ReadTransparency(TransparencyMode.Alpha, () => 128),
        ReadTransparency(TransparencyMode.Alpha, () => 255)
    };
    Equal(5, values.Distinct().Count());
});

foreach (int mode in new[] { -1, 3, int.MaxValue })
{
    Run($"Unknown transparency mode {mode} fails before Alpha", () =>
    {
        int calls = 0;
        Contains("Unsupported transparency mode", Throws<InvalidOperationException>(() =>
            ReadTransparency((TransparencyMode)mode, () => { calls++; return 0; })).Message);
        Equal(0, calls);
    });
}

Run("Plain dash never reads TextAt", () =>
{
    int calls = 0;
    LinetypeElement value = ReadLinetypeElement(false, 0, () =>
    {
        calls++;
        throw new Exception("Inapplicable TextAt getter invoked.");
    });
    Equal(new LinetypeElement(LinetypeElementKind.PlainDash, 0, null), value);
    Equal(0, calls);
});
foreach (int shape in new[] { 1, 128, int.MaxValue })
{
    Run($"Shape {shape} never reads TextAt", () =>
    {
        int calls = 0;
        LinetypeElement value = ReadLinetypeElement(true, shape, () =>
        {
            calls++;
            throw new Exception("Inapplicable TextAt getter invoked.");
        });
        Equal(new LinetypeElement(LinetypeElementKind.Shape, shape, null), value);
        Equal(0, calls);
    });
}
foreach (string text in new[] { "", "GAS", "  Ω|:  " })
{
    Run($"Text linetype retains exact payload '{text}'", () =>
    {
        int calls = 0;
        LinetypeElement value = ReadLinetypeElement(true, 0, () => { calls++; return text; });
        Equal(new LinetypeElement(LinetypeElementKind.Text, 0, text), value);
        Equal(1, calls);
        NotEqual(new LinetypeElement(LinetypeElementKind.PlainDash, 0, null), value);
    });
}
foreach ((bool style, int shape) in new[] { (false, -1), (true, -1), (false, 1), (false, int.MaxValue) })
{
    Run($"Invalid linetype style={style}, shape={shape} fails before TextAt", () =>
    {
        int calls = 0;
        Contains("shape number/style", Throws<InvalidOperationException>(() =>
            ReadLinetypeElement(style, shape, () => { calls++; return "bad"; })).Message);
        Equal(0, calls);
    });
}
Run("Text element returning null fails closed", () =>
    Contains("returned null text", Throws<InvalidOperationException>(() => ReadLinetypeElement(true, 0, () => null)).Message));

// Model the same lazy ReadAt adapters used by GisNewDrawingClone. The original
// native exception must survive as InnerException with the property in Message.
foreach (string property in new[] { "ColorIndex", "RGB", "HasBookName", "BookName", "HasColorName", "ColorName" })
{
    Run($"Actual {property} getter failure retains property stage and exception", () =>
    {
        var probe = new ColorProbe { HasBook = true, HasName = true, ThrowOn = property };
        InvalidOperationException error = Throws<InvalidOperationException>(() =>
            probe.Read(property == "ColorIndex" ? ColorMode.Aci : ColorMode.Rgb));
        Contains("Entity handle AA.Color." + property, error.Message);
        Contains("eInvalidKey", error.Message);
        True(ReferenceEquals(probe.NativeFailure, error.InnerException), "Native exception was replaced or discarded.");
        Equal(1, probe.Reads.Count(name => name == property));
    });
}
foreach (string channel in new[] { "Red", "Green", "Blue" })
{
    Run($"RGB component {channel} failure retains its exact stage", () =>
    {
        var native = new Exception("eInvalidKey");
        byte ReadChannel(string property) => ReadAt("Entity handle AA.Color." + property,
            () => property == channel ? throw native : (byte)128);
        InvalidOperationException error = Throws<InvalidOperationException>(() => ReadColor(ColorMode.Rgb,
            () => throw new Exception("Unexpected ACI"),
            () => (ReadChannel("Red"), ReadChannel("Green"), ReadChannel("Blue")),
            () => false, () => throw new Exception("Unexpected BookName"),
            () => false, () => throw new Exception("Unexpected ColorName")));
        Contains("Entity handle AA.Color." + channel, error.Message);
        True(ReferenceEquals(native, error.InnerException));
    });
}
Run("Actual Alpha failure retains property stage and native exception", () =>
{
    var native = new Exception("eInvalidKey");
    InvalidOperationException error = Throws<InvalidOperationException>(() =>
        ReadTransparency(TransparencyMode.Alpha, () => ReadAt<byte>("Layer handle AB.Transparency.Alpha", () => throw native)));
    Equal("Layer handle AB.Transparency.Alpha: eInvalidKey", error.Message);
    True(ReferenceEquals(native, error.InnerException));
});
Run("Actual TextAt failure retains nested resource and property stages", () =>
{
    var native = new Exception("eInvalidKey");
    InvalidOperationException outer = Throws<InvalidOperationException>(() =>
        ReadAt("Resource handle AC dash[2]: classify", () =>
            ReadLinetypeElement(true, 0, () => ReadAt<string?>("Resource handle AC dash[2].TextAt", () => throw native))));
    Contains("Resource handle AC dash[2]: classify", outer.Message);
    Contains("Resource handle AC dash[2].TextAt", outer.Message);
    True(outer.InnerException is InvalidOperationException);
    True(ReferenceEquals(native, outer.InnerException!.InnerException));
});
Run("ReadAt success returns the original result and invokes once", () =>
{
    object expected = new object();
    int calls = 0;
    object actual = ReadAt("read", () => { calls++; return expected; });
    True(ReferenceEquals(expected, actual));
    Equal(1, calls);
});
Run("ReadAt does not turn a successful null into an invented value", () =>
    Equal<string?>(null, ReadAt<string?>("optional value", () => null)));

Console.WriteLine($"\n{passed} passed; {failed} failed.");
return failed == 0 ? 0 : 1;

void Run(string name, Action test)
{
    try { test(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception error) { failed++; Console.Error.WriteLine("FAIL " + name + "\n" + error); }
}
static void True(bool value, string message = "Expected true.")
{
    if (!value) throw new Exception(message);
}
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected <{expected}>; actual <{actual}>.");
}
static void NotEqual<T>(T left, T right)
{
    if (EqualityComparer<T>.Default.Equals(left, right))
        throw new Exception($"Values unexpectedly compare equal: <{left}>.");
}
static void Contains(string expected, string actual)
{
    if (!actual.Contains(expected, StringComparison.Ordinal))
        throw new Exception($"Expected <{actual}> to contain <{expected}>.");
}
static T Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T error) { return error; }
    catch (Exception error) { throw new Exception($"Expected {typeof(T).Name}, got {error.GetType().Name}.", error); }
    throw new Exception($"Expected {typeof(T).Name}; no exception was thrown.");
}

sealed class ColorProbe
{
    public int Aci { get; init; } = 7;
    public (byte Red, byte Green, byte Blue) Rgb { get; init; } = (17, 34, 51);
    public bool HasBook { get; init; }
    public bool HasName { get; init; }
    public string? Book { get; init; } = "Book";
    public string? Name { get; init; } = "Color";
    public string? ThrowOn { get; init; }
    public Exception NativeFailure { get; } = new Exception("eInvalidKey");
    public HashSet<string> Forbidden { get; } = new(StringComparer.Ordinal);
    public List<string> Reads { get; } = new();

    public ColorValue Read(ColorMode mode) => ReadColor(mode,
        () => ReadProperty("ColorIndex", Aci), () => ReadProperty("RGB", Rgb),
        () => ReadProperty("HasBookName", HasBook), () => ReadProperty("BookName", Book),
        () => ReadProperty("HasColorName", HasName), () => ReadProperty("ColorName", Name));

    private T ReadProperty<T>(string property, T value) => ReadAt("Entity handle AA.Color." + property, () =>
    {
        Reads.Add(property);
        if (Forbidden.Contains(property)) throw new Exception("Inapplicable getter invoked: " + property);
        if (ThrowOn == property) throw NativeFailure;
        return value;
    });
}
