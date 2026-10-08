using System.Reflection;
using System.Reflection.Emit;
using Autodesk.Gis.Map.ImportExport;
using CLV_CivilTools.Gis;

int passed = 0;
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { throw new Exception("FAILED " + name, ex); }
}
void Check(bool condition, string detail)
{
    if (!condition) throw new Exception(detail);
}
T Reject<T>(Action action, string detail) where T : Exception
{
    try { action(); }
    catch (T ex)
    {
        Check(ex.ToString().Contains(detail, StringComparison.Ordinal), "Missing rejection detail: " + detail);
        return ex;
    }
    throw new Exception("Expected " + typeof(T).Name + ": " + detail);
}
void Output(GisNewDrawingNativeOutputs.EnumTextOutput actual, Enum mode, string? text)
{
    Check(actual.Mode.GetType() == mode.GetType() && actual.Mode.Equals(mode), "Enum output/type differs.");
    Check(actual.Text == text, "String output differs.");
}

Test("pointer LayerName receives allocated initialized output", () =>
{
    var native = new PointerGetters();
    Output(GisNewDrawingNativeOutputs.ReadPair(native, "LayerName"), LayerNameType.LayerNameDirect, "Pipes");
    Check(native.Calls == 1, "Getter must run exactly once.");
});
Test("pointer DataMapping", () =>
    Output(GisNewDrawingNativeOutputs.ReadPair(new PointerGetters(), "DataMapping"), ImportDataMapping.NewObjectDataOnly, "Pipes"));
Test("pointer PointToBlockMapping", () =>
    Output(GisNewDrawingNativeOutputs.ReadPair(new PointerGetters(), "PointToBlockMapping"), PointMappingType.MapPointToPoint, ""));
Test("pointer ColumnDataMapping reads the return string", () =>
    Output(GisNewDrawingNativeOutputs.ReadColumnMapping(new PointerGetters()), ImportDataMapping.ExistingObjectDataOnly, "Name"));

Test("actual enum by-reference LayerName", () =>
    Output(GisNewDrawingNativeOutputs.ReadPair(new ReferenceGetters(), "LayerName"), LayerNameType.LayerNameDirect, "Structures"));
Test("actual enum by-reference DataMapping", () =>
    Output(GisNewDrawingNativeOutputs.ReadPair(new ReferenceGetters(), "DataMapping"), ImportDataMapping.NewObjectDataOnly, "Structures"));
Test("actual enum by-reference PointToBlockMapping", () =>
    Output(GisNewDrawingNativeOutputs.ReadPair(new ReferenceGetters(), "PointToBlockMapping"), PointMappingType.MapPointToPoint, ""));
Test("actual enum by-reference ColumnDataMapping", () =>
    Output(GisNewDrawingNativeOutputs.ReadColumnMapping(new ReferenceGetters()), ImportDataMapping.ExistingObjectDataOnly, "Name"));
Test("nullable pair string is left for mapping policy", () =>
    Output(GisNewDrawingNativeOutputs.ReadPair(new NullableGetters(), "LayerName"), LayerNameType.LayerNameDirect, null));
Test("nullable return string is left for mapping policy", () =>
    Output(GisNewDrawingNativeOutputs.ReadColumnMapping(new NullableGetters()), ImportDataMapping.NoImportMapping, null));

Test("unwritten pointer enum is rejected", () =>
    Reject<InvalidOperationException>(() => GisNewDrawingNativeOutputs.ReadPair(new UnwrittenPointerGetter(), "LayerName"), "did not write"));
Test("unwritten reference enum is rejected", () =>
    Reject<InvalidOperationException>(() => GisNewDrawingNativeOutputs.ReadPair(new UnwrittenReferenceGetter(), "LayerName"), "did not write"));
Test("undefined pointer enum is rejected", () =>
    Reject<InvalidOperationException>(() => GisNewDrawingNativeOutputs.ReadPair(new UndefinedPointerGetter(), "LayerName"), "undefined"));
Test("undefined reference enum is rejected", () =>
    Reject<InvalidOperationException>(() => GisNewDrawingNativeOutputs.ReadPair(new UndefinedReferenceGetter(), "LayerName"), "undefined"));
Test("unwritten column enum is rejected", () =>
    Reject<InvalidOperationException>(() => GisNewDrawingNativeOutputs.ReadColumnMapping(new UnwrittenColumnGetter()), "did not write"));

foreach (object invalid in new object[]
{
    new ByValueGetter(), new WrongStringGetter(), new PrimitivePointerGetter(), new WrongNamespaceGetter(),
    new WrongEnumGetter(), new WrongReturnGetter(), new GenericGetter(), new StaticGetter(),
    new PrivateGetter(), new PointerToPointerGetter(), new ByReferencePointerGetter()
})
{
    Test("reject before invocation: " + invalid.GetType().Name, () =>
        Reject<MissingMethodException>(() => GisNewDrawingNativeOutputs.ReadPair(invalid, "LayerName"), "found 0"));
}
Test("ambiguous pointer/reference overloads are rejected", () =>
    Reject<MissingMethodException>(() => GisNewDrawingNativeOutputs.ReadPair(new AmbiguousGetter(), "LayerName"), "found 2"));
Test("column return must be string", () =>
    Reject<MissingMethodException>(() => GisNewDrawingNativeOutputs.ReadColumnMapping(new WrongColumnReturn()), "found 0"));
Test("column enum must be an output", () =>
    Reject<MissingMethodException>(() => GisNewDrawingNativeOutputs.ReadColumnMapping(new ByValueColumn()), "found 0"));
Test("column must use the documented enum", () =>
    Reject<MissingMethodException>(() => GisNewDrawingNativeOutputs.ReadColumnMapping(new WrongColumnEnum()), "found 0"));
foreach (string unknown in new[] { "SetLayerName", "layername", "Unknown", "" })
    Test("only exact getter names are allowed: " + unknown, () =>
        Reject<ArgumentException>(() => GisNewDrawingNativeOutputs.ReadPair(new PointerGetters(), unknown), "Unsupported native output getter"));
Test("null target is rejected before native work", () =>
    Reject<ArgumentNullException>(() => GisNewDrawingNativeOutputs.ReadPair(null!, "LayerName"), "target"));

Test("native exception retains original instance and runs once", () =>
{
    var native = new ThrowingGetter();
    TargetInvocationException failure = Reject<TargetInvocationException>(
        () => GisNewDrawingNativeOutputs.ReadPair(native, "LayerName"), "fake native getter failure");
    Check(ReferenceEquals(failure.InnerException, native.Failure), "Original exception must remain available.");
    Check(native.Calls == 1, "Failed getters must not be retried or replaced with defaults.");
    Output(GisNewDrawingNativeOutputs.ReadPair(new PointerGetters(), "LayerName"), LayerNameType.LayerNameDirect, "Pipes");
});
Test("repeated pointer calls do not reuse stale enum outputs", () =>
{
    var native = new PointerGetters();
    for (int i = 0; i < 1000; i++)
        Output(GisNewDrawingNativeOutputs.ReadPair(native, "LayerName"), LayerNameType.LayerNameDirect, "Pipes");
    Check(native.Calls == 1000, "Each request must invoke exactly once.");
});

(Type Underlying, object Value)[] widths =
{
    (typeof(sbyte), (sbyte)-7), (typeof(byte), (byte)250),
    (typeof(short), (short)-1234), (typeof(ushort), (ushort)65000),
    (typeof(int), -123456), (typeof(uint), 0xf1234567u),
    (typeof(long), long.MinValue + 123), (typeof(ulong), ulong.MaxValue - 123)
};
foreach ((Type underlying, object expectedValue) in widths)
{
    Test("pointer enum storage preserves " + underlying.Name, () =>
    {
        object native = EmittedGetter(underlying, expectedValue, fullByteDomain: false);
        GisNewDrawingNativeOutputs.EnumTextOutput output = GisNewDrawingNativeOutputs.ReadPair(native, "LayerName");
        Check(Enum.GetUnderlyingType(output.Mode.GetType()) == underlying, "Underlying type differs.");
        Check(Equals(Convert.ChangeType(output.Mode, underlying), expectedValue), "High bits or signedness changed.");
        Check(output.Text == "emitted" && (int)native.GetType().GetField("Calls")!.GetValue(native)! == 1,
            "Emitted getter must run once and return its string.");
    });
}
Test("fully defined enum fails before allocation or invocation", () =>
{
    object native = EmittedGetter(typeof(byte), (byte)250, fullByteDomain: true);
    Reject<NotSupportedException>(() => GisNewDrawingNativeOutputs.ReadPair(native, "LayerName"), "no unused output sentinel");
    Check((int)native.GetType().GetField("Calls")!.GetValue(native)! == 0, "A getter without a safe sentinel must not run.");
});
Test("test target has no Autodesk assembly dependency", () =>
    Check(!typeof(GisNewDrawingNativeOutputs).Assembly.GetReferencedAssemblies()
        .Any(assembly => assembly.Name?.StartsWith("Autodesk", StringComparison.Ordinal) == true),
        "The focused reader tests must not load Autodesk assemblies."));

Console.WriteLine($"All {passed} native output reader checks passed. Civil 3D integration remains a separate acceptance check.");

// Emit distinct assemblies so all eight underlying types can use the exact
// documented enum full name without replacing the production signature checks.
static object EmittedGetter(Type underlying, object expectedValue, bool fullByteDomain)
{
    AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
        new AssemblyName("NativeOutputWidth_" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.RunAndCollect);
    ModuleBuilder module = assembly.DefineDynamicModule("Getters");
    EnumBuilder enumeration = module.DefineEnum("Autodesk.Gis.Map.ImportExport.LayerNameType", TypeAttributes.Public, underlying);
    enumeration.DefineLiteral("Expected", expectedValue);
    if (fullByteDomain)
        for (int i = 0; i <= byte.MaxValue; i++) enumeration.DefineLiteral("Value" + i, (byte)i);
    Type enumType = enumeration.CreateTypeInfo()!.AsType();
    TypeBuilder target = module.DefineType("EmittedNativeGetter", TypeAttributes.Public | TypeAttributes.Class);
    target.DefineDefaultConstructor(MethodAttributes.Public);
    FieldBuilder calls = target.DefineField("Calls", typeof(int), FieldAttributes.Public);
    MethodBuilder getter = target.DefineMethod("LayerName", MethodAttributes.Public, typeof(void),
        new[] { enumType.MakePointerType(), typeof(string).MakeByRefType() });
    ILGenerator il = getter.GetILGenerator();
    il.Emit(OpCodes.Ldarg_0);
    il.Emit(OpCodes.Dup);
    il.Emit(OpCodes.Ldfld, calls);
    il.Emit(OpCodes.Ldc_I4_1);
    il.Emit(OpCodes.Add);
    il.Emit(OpCodes.Stfld, calls);
    il.Emit(OpCodes.Ldarg_1);
    long bits = underlying == typeof(ulong) ? unchecked((long)(ulong)expectedValue) : Convert.ToInt64(expectedValue);
    if (underlying == typeof(long) || underlying == typeof(ulong))
    {
        il.Emit(OpCodes.Ldc_I8, bits);
        il.Emit(OpCodes.Stind_I8);
    }
    else
    {
        il.Emit(OpCodes.Ldc_I4, unchecked((int)bits));
        il.Emit(underlying == typeof(byte) || underlying == typeof(sbyte) ? OpCodes.Stind_I1 :
            underlying == typeof(short) || underlying == typeof(ushort) ? OpCodes.Stind_I2 : OpCodes.Stind_I4);
    }
    il.Emit(OpCodes.Ldarg_2);
    il.Emit(OpCodes.Ldstr, "emitted");
    il.Emit(OpCodes.Stind_Ref);
    il.Emit(OpCodes.Ret);
    return Activator.CreateInstance(target.CreateTypeInfo()!.AsType())!;
}

public unsafe sealed class PointerGetters
{
    public int Calls { get; private set; }
    public void LayerName(LayerNameType* mode, out string? text)
    {
        Calls++;
        if (mode == null || Enum.IsDefined(typeof(LayerNameType), *mode)) throw new InvalidOperationException("Expected non-null storage initialized to an undefined enum sentinel.");
        *mode = LayerNameType.LayerNameDirect;
        text = "Pipes";
    }
    public void DataMapping(ImportDataMapping* mode, out string? text)
    {
        if (mode == null) throw new InvalidOperationException("Null enum output.");
        *mode = ImportDataMapping.NewObjectDataOnly;
        text = "Pipes";
    }
    public void PointToBlockMapping(PointMappingType* mode, out string? text)
    {
        if (mode == null) throw new InvalidOperationException("Null enum output.");
        *mode = PointMappingType.MapPointToPoint;
        text = "";
    }
    public string ColumnDataMapping(ImportDataMapping* mode)
    {
        if (mode == null) throw new InvalidOperationException("Null enum output.");
        *mode = ImportDataMapping.ExistingObjectDataOnly;
        return "Name";
    }
}

public sealed class ReferenceGetters
{
    public void LayerName(ref LayerNameType mode, out string? text) { mode = LayerNameType.LayerNameDirect; text = "Structures"; }
    public void DataMapping(out ImportDataMapping mode, out string? text) { mode = ImportDataMapping.NewObjectDataOnly; text = "Structures"; }
    public void PointToBlockMapping(ref PointMappingType mode, out string? text) { mode = PointMappingType.MapPointToPoint; text = ""; }
    public string ColumnDataMapping(ref ImportDataMapping mode) { mode = ImportDataMapping.ExistingObjectDataOnly; return "Name"; }
}

public unsafe sealed class NullableGetters
{
    public void LayerName(LayerNameType* mode, out string? text) { *mode = LayerNameType.LayerNameDirect; text = null; }
    public string? ColumnDataMapping(ImportDataMapping* mode) { *mode = ImportDataMapping.NoImportMapping; return null; }
}
public unsafe sealed class UnwrittenPointerGetter
{
    public void LayerName(LayerNameType* mode, out string? text) { text = "Pipes"; }
}
public sealed class UnwrittenReferenceGetter
{
    public void LayerName(ref LayerNameType mode, out string? text) { text = "Pipes"; }
}
public unsafe sealed class UndefinedPointerGetter
{
    public void LayerName(LayerNameType* mode, out string? text) { *mode = (LayerNameType)12345; text = "Pipes"; }
}
public sealed class UndefinedReferenceGetter
{
    public void LayerName(ref LayerNameType mode, out string? text) { mode = (LayerNameType)12345; text = "Pipes"; }
}
public unsafe sealed class UnwrittenColumnGetter
{
    public string ColumnDataMapping(ImportDataMapping* mode) => "Name";
}
public unsafe sealed class ThrowingGetter
{
    public int Calls { get; private set; }
    public InvalidOperationException Failure { get; } = new InvalidOperationException("fake native getter failure");
    public void LayerName(LayerNameType* mode, out string? text) { Calls++; throw Failure; }
}

// These bodies throw a different exception if signature filtering accidentally
// invokes them. Merely asserting a failure would not detect that regression.
public sealed class ByValueGetter
{
    public void LayerName(LayerNameType mode, out string? text) => throw new Exception("Must not invoke.");
}
public unsafe sealed class WrongStringGetter
{
    public void LayerName(LayerNameType* mode, string? text) => throw new Exception("Must not invoke.");
}
public unsafe sealed class PrimitivePointerGetter
{
    public void LayerName(int* mode, out string? text) => throw new Exception("Must not invoke.");
}
public unsafe sealed class WrongNamespaceGetter
{
    public void LayerName(OtherNamespace.LayerNameType* mode, out string? text) => throw new Exception("Must not invoke.");
}
public unsafe sealed class WrongEnumGetter
{
    public void LayerName(ImportDataMapping* mode, out string? text) => throw new Exception("Must not invoke.");
}
public unsafe sealed class WrongReturnGetter
{
    public string LayerName(LayerNameType* mode, out string? text) => throw new Exception("Must not invoke.");
}
public unsafe sealed class GenericGetter
{
    public void LayerName<T>(LayerNameType* mode, out string? text) => throw new Exception("Must not invoke.");
}
public unsafe sealed class StaticGetter
{
    public static void LayerName(LayerNameType* mode, out string? text) => throw new Exception("Must not invoke.");
}
public unsafe sealed class PrivateGetter
{
    private void LayerName(LayerNameType* mode, out string? text) => throw new Exception("Must not invoke.");
}
public unsafe sealed class PointerToPointerGetter
{
    public void LayerName(LayerNameType** mode, out string? text) => throw new Exception("Must not invoke.");
}
public unsafe sealed class ByReferencePointerGetter
{
    public void LayerName(ref LayerNameType* mode, out string? text) => throw new Exception("Must not invoke.");
}
public unsafe sealed class AmbiguousGetter
{
    public void LayerName(LayerNameType* mode, out string? text) => throw new Exception("Must not invoke.");
    public void LayerName(ref LayerNameType mode, out string? text) => throw new Exception("Must not invoke.");
}
public unsafe sealed class WrongColumnReturn
{
    public int ColumnDataMapping(ImportDataMapping* mode) => throw new Exception("Must not invoke.");
}
public sealed class ByValueColumn
{
    public string ColumnDataMapping(ImportDataMapping mode) => throw new Exception("Must not invoke.");
}
public unsafe sealed class WrongColumnEnum
{
    public string ColumnDataMapping(LayerNameType* mode) => throw new Exception("Must not invoke.");
}

namespace Autodesk.Gis.Map.ImportExport
{
    public enum LayerNameType { LayerNameDirect = 0, LayerNameIndirect = 1 }
    public enum ImportDataMapping { NoImportMapping = 0, NewObjectDataOnly = 1, ExistingObjectDataOnly = 2, LinkTemplate = 3, LinkOnly = 4 }
    public enum PointMappingType { MapPointInvalid = 0, MapPointToPoint = 1, MapPointToNamedBlock = 2, MapPointToBlockFromData = 3, MapPointToTextFromData = 4 }
}
namespace OtherNamespace
{
    public enum LayerNameType { LayerNameDirect = 1 }
}
