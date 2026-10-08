using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CLV_CivilTools.Gis
{
    /// <summary>
    /// Reads the four Map import getters whose enum outputs can be unmanaged
    /// pointers in the C++/CLI metadata. No native setter is invoked here.
    /// </summary>
    internal static class GisNewDrawingNativeOutputs
    {
        internal sealed record EnumTextOutput(Enum Mode, string? Text);
        internal sealed record ColumnMappingOutput(Enum? Mode, string? Text);

        internal static EnumTextOutput ReadPair(object target, string methodName)
        {
            string enumName = methodName switch
            {
                "LayerName" => "LayerNameType",
                "DataMapping" => "ImportDataMapping",
                "PointToBlockMapping" => "PointMappingType",
                _ => throw new ArgumentException("Unsupported native output getter: " + methodName, nameof(methodName))
            };
            ColumnMappingOutput output = Read(target, methodName, enumName, pair: true,
                allowUnwrittenForEmptyOutput: false);
            return new EnumTextOutput(output.Mode
                ?? throw new InvalidOperationException("A native pair getter did not return its required enum."), output.Text);
        }

        internal static ColumnMappingOutput ReadColumnMapping(object target, bool allowUnwrittenForEmptyOutput = false)
            => Read(target, "ColumnDataMapping", "ImportDataMapping", pair: false,
                allowUnwrittenForEmptyOutput: allowUnwrittenForEmptyOutput);

        private static ColumnMappingOutput Read(object target, string methodName, string enumName, bool pair,
            bool allowUnwrittenForEmptyOutput)
        {
            ArgumentNullException.ThrowIfNull(target);
            string enumFullName = "Autodesk.Gis.Map.ImportExport." + enumName;
            MethodInfo[] matches = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => IsGetter(method, methodName, enumFullName, pair)).ToArray();
            if (matches.Length != 1)
                throw new MissingMethodException($"Expected one public native {target.GetType().FullName}.{methodName} getter " +
                    $"with {enumFullName} pointer/by-reference output and " +
                    (pair ? "by-reference string output" : "string return") + $"; found {matches.Length}.");

            MethodInfo getter = matches[0];
            Type outputType = getter.GetParameters()[0].ParameterType;
            Type enumType = outputType.GetElementType()!;
            TypeCode storageKind = Type.GetTypeCode(Enum.GetUnderlyingType(enumType));
            int width = storageKind switch
            {
                TypeCode.SByte or TypeCode.Byte => 1,
                TypeCode.Int16 or TypeCode.UInt16 => 2,
                TypeCode.Int32 or TypeCode.UInt32 => 4,
                TypeCode.Int64 or TypeCode.UInt64 => 8,
                _ => throw new NotSupportedException("Unsupported native enum storage: " + enumType.FullName)
            };
            (Enum sentinel, ulong sentinelBits) = FindSentinel(enumType, width);
            object?[] arguments = pair ? new object?[] { sentinel, null } : new object?[] { sentinel };
            IntPtr memory = IntPtr.Zero;
            try
            {
                if (outputType.IsPointer)
                {
                    memory = Marshal.AllocHGlobal(width);
                    WriteBits(memory, width, sentinelBits);
                    // CoreCLR accepts a boxed IntPtr for a pointer parameter.
                    // Invoke this validated MethodInfo directly, without the
                    // general reflection helper's IsInstanceOfType filter.
                    arguments[0] = memory;
                }

                object? result = getter.Invoke(target, arguments);
                Enum? mode = outputType.IsPointer
                    ? ReadEnum(memory, enumType, storageKind)
                    : arguments[0] as Enum;
                string? text = pair ? (string?)arguments[1] : (string?)result;
                if (mode == null || mode.GetType() != enumType)
                    throw new InvalidOperationException($"Native {methodName} returned an invalid {enumName} output.");
                if (mode.Equals(sentinel))
                {
                    // Only a caller-identified, deliberately cleared column may
                    // use its empty destination without an enum. Keep absence
                    // explicit; never invent NoImportMapping or a table mode.
                    if (!pair && allowUnwrittenForEmptyOutput && string.IsNullOrEmpty(text))
                        return new ColumnMappingOutput(null, text);
                    throw new InvalidOperationException($"Native {methodName} did not write its {enumName} output; " +
                        $"returned text={DescribeText(text)}, empty-unmapped allowance={allowUnwrittenForEmptyOutput}.");
                }
                if (!Enum.IsDefined(enumType, mode))
                    throw new InvalidOperationException($"Native {methodName} returned an undefined {enumName} value: {mode}.");
                return new ColumnMappingOutput(mode, text);
            }
            finally
            {
                if (memory != IntPtr.Zero) Marshal.FreeHGlobal(memory);
            }
        }

        private static string DescribeText(string? value)
        {
            if (value == null) return "<null>";
            string escaped = value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n").Replace("'", "\\'");
            return "'" + (escaped.Length > 160 ? escaped.Substring(0, 160) + "..." : escaped) + "'";
        }

        private static bool IsGetter(MethodInfo method, string name, string enumFullName, bool pair)
        {
            if (method.Name != name || method.IsStatic || method.IsGenericMethod || method.ContainsGenericParameters ||
                (method.CallingConvention & CallingConventions.VarArgs) != 0 ||
                method.ReturnType != (pair ? typeof(void) : typeof(string))) return false;
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != (pair ? 2 : 1)) return false;
            Type outputType = parameters[0].ParameterType;
            if (!outputType.IsPointer && !outputType.IsByRef) return false;
            Type? enumType = outputType.GetElementType();
            return enumType?.IsEnum == true && enumType.FullName == enumFullName &&
                (!pair || parameters[1].ParameterType == typeof(string).MakeByRefType());
        }

        private static (Enum Value, ulong Bits) FindSentinel(Type enumType, int width)
        {
            // Among N + 1 distinct bit patterns, at least one is unnamed when
            // there are N declared names. Cap the search at the storage domain
            // so a fully populated byte/short enum fails instead of wrapping.
            ulong maximum = width == 8 ? ulong.MaxValue : (1UL << (width * 8)) - 1;
            ulong limit = Math.Min((ulong)Enum.GetNames(enumType).Length, maximum);
            for (ulong bits = 0; bits <= limit; bits++)
            {
                var value = (Enum)Enum.ToObject(enumType, bits);
                if (!Enum.IsDefined(enumType, value)) return (value, bits);
            }
            throw new NotSupportedException("Native enum has no unused output sentinel: " + enumType.FullName);
        }

        private static void WriteBits(IntPtr memory, int width, ulong bits)
        {
            switch (width)
            {
                case 1: Marshal.WriteByte(memory, unchecked((byte)bits)); break;
                case 2: Marshal.WriteInt16(memory, unchecked((short)bits)); break;
                case 4: Marshal.WriteInt32(memory, unchecked((int)bits)); break;
                case 8: Marshal.WriteInt64(memory, unchecked((long)bits)); break;
                default: throw new NotSupportedException("Unsupported native enum width: " + width);
            }
        }

        private static Enum ReadEnum(IntPtr memory, Type enumType, TypeCode kind)
            => (Enum)(kind switch
            {
                TypeCode.SByte => Enum.ToObject(enumType, unchecked((sbyte)Marshal.ReadByte(memory))),
                TypeCode.Byte => Enum.ToObject(enumType, Marshal.ReadByte(memory)),
                TypeCode.Int16 => Enum.ToObject(enumType, Marshal.ReadInt16(memory)),
                TypeCode.UInt16 => Enum.ToObject(enumType, unchecked((ushort)Marshal.ReadInt16(memory))),
                TypeCode.Int32 => Enum.ToObject(enumType, Marshal.ReadInt32(memory)),
                TypeCode.UInt32 => Enum.ToObject(enumType, unchecked((uint)Marshal.ReadInt32(memory))),
                TypeCode.Int64 => Enum.ToObject(enumType, Marshal.ReadInt64(memory)),
                TypeCode.UInt64 => Enum.ToObject(enumType, unchecked((ulong)Marshal.ReadInt64(memory))),
                _ => throw new NotSupportedException("Unsupported native enum storage: " + enumType.FullName)
            });
    }
}
