using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace CLV_CivilTools.Gis
{
    /// <summary>
    /// Read-only, deliberately bounded verification against the host CS-Map dictionary.
    /// API members are documented in the Map 3D 2026 sdk.geo.ref.chm references for
    /// MgCoordinateSystemFactory, MgCoordinateSystem and MgCoordinateSystemMathComparator.
    /// No dictionary mutation, coordinate transformation, private wrapper or WKT-title
    /// lookup is used. A successful result applies only to these input definitions.
    /// </summary>
    internal static class GisNewDrawingCoordinateSystem
    {
        private const string FactoryType = "OSGeo.MapGuide.MgCoordinateSystemFactory";
        private const int MaximumDefinitionLength = 32768;
        private const int MaximumProjectionParameters = 24;
        private const int ProjectedCoordinateSystem = 3; // MgCoordinateSystemType.Projected

        internal static GisNewDrawingCoordinateSystemVerification Verify(string expectedCode,
            string? actualCoordinateSystem, string? actualWkt)
        {
            if (!GisNewDrawingProfile.IsSupportedCoordinateSystem(expectedCode))
                throw new InvalidDataException("The source drawing must specify an exact supported CLV coordinate system.");
            string observed = Bounded(actualCoordinateSystem, "GetCoordinateSystem");
            string wkt = Bounded(actualWkt, "GetCoordinateSystemWkt");
            if (observed.Length == 0 && wkt.Length == 0)
                throw new InvalidDataException("The SDF spatial context has no coordinate-system definition.");

            using var scope = new NativeScope();
            object factory = scope.Create(FindFactoryType());
            object catalog = scope.Acquire(factory, "GetCatalog");
            object expected = scope.Acquire(factory, "CreateFromCode", expectedCode);
            if (ReadText(expected, "GetCsCode") != expectedCode)
                throw new InvalidDataException($"The installed coordinate-system dictionary did not resolve the exact source code '{expectedCode}'.");
            if (!ReadBoolean(expected, "IsUsable", catalog))
                throw new InvalidDataException($"The installed coordinate-system dictionary cannot use '{expectedCode}'.");
            Definition expectedDefinition = ReadDefinition(expected);
            string expectedBody = ReadText(expected, "ToString");
            WktGuard.VerifyEquivalent(expectedBody, expectedBody);
            object comparator = scope.Acquire(catalog, "GetMathComparator");
            if (!ReadBoolean(comparator, "GetCompareInternalDatumOldParameters"))
                throw new InvalidDataException("The installed CRS comparator is not checking datum transformation parameters.");

            string observedCode = string.Empty;
            if (observed.Length != 0)
            {
                if (HasWktSyntax(observed))
                {
                    observedCode = VerifyWkt(scope, factory, comparator, expected, expectedDefinition,
                        expectedBody, expectedCode, observed, "GetCoordinateSystem");
                }
                else
                {
                    // A supplied code is resolved as that code, never guessed from a
                    // WKT title, filename, profile name or a partial-name search.
                    object named = scope.Acquire(factory, "CreateFromCode", observed);
                    observedCode = ReadText(named, "GetCsCode");
                    if (GisNewDrawingProfile.IsSupportedCoordinateSystem(observedCode) && observedCode != expectedCode)
                        throw new InvalidDataException($"Actual SDF coordinate system '{observedCode}' does not match source drawing '{expectedCode}'.");
                    VerifyDefinition(comparator, expected, expectedDefinition, named, expectedCode, "GetCoordinateSystem code");
                    WktGuard.VerifyEquivalent(expectedBody, ReadText(named, "ToString"));
                }
            }
            if (wkt.Length != 0)
            {
                string parsedCode = VerifyWkt(scope, factory, comparator, expected, expectedDefinition,
                    expectedBody, expectedCode, wkt, "GetCoordinateSystemWkt");
                if (observedCode.Length == 0) observedCode = parsedCode;
            }

            // ObservedCode is diagnostic metadata only. ResolvedCode is assigned
            // from the independently requested dictionary code after every proof.
            return new GisNewDrawingCoordinateSystemVerification(expectedCode, observedCode, expectedCode,
                "Installed Map dictionary; complete canonical WKT structure; native mathematical comparator; exact unit/projection/datum/ellipsoid identity. No transform.");
        }

        private static string VerifyWkt(NativeScope scope, object factory, object comparator, object expected,
            Definition expectedDefinition, string expectedBody, string expectedCode, string wkt, string field)
        {
            // Essential defense against native WKT parsing which can return the
            // named dictionary entry. Math comparison alone would then compare the
            // dictionary entry to itself and conceal altered WKT parameters.
            // Only insignificant whitespace, numeric spelling, PARAMETER order and the
            // top PROJCS display title are ignored. Every numeric value and every
            // other node must match the official dictionary's WKT. We never derive
            // coordinate-system mathematics or resolve aliases from this structure.
            try { WktGuard.VerifyEquivalent(expectedBody, wkt); }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException($"{field}: CRS equivalence to '{expectedCode}' is unverified/incompatible. " + ex.Message +
                    " No transform or alias fallback was applied.", ex);
            }
            if (!ReadBoolean(factory, "IsValid", wkt))
                throw new InvalidDataException($"{field}: the installed Map coordinate-system factory rejected the complete WKT.");
            object parsed = scope.Acquire(factory, "Create", wkt);
            VerifyDefinition(comparator, expected, expectedDefinition, parsed, expectedCode, field);
            return ReadText(parsed, "GetCsCode");
        }

        private static void VerifyDefinition(object comparator, object expected, Definition expectedDefinition,
            object actual, string expectedCode, string field)
        {
            Definition definition = ReadDefinition(actual);
            foreach (var item in new (string Name, object Expected, object Actual)[]
            {
                ("GetType", expectedDefinition.Type, definition.Type),
                ("GetUnitCode", expectedDefinition.UnitCode, definition.UnitCode),
                ("GetUnitScale", expectedDefinition.UnitScale, definition.UnitScale),
                ("GetProjectionCode", expectedDefinition.ProjectionCode, definition.ProjectionCode),
                ("GetQuadrant", expectedDefinition.Quadrant, definition.Quadrant),
                ("GetProjectionParameterCount", expectedDefinition.ParameterCount, definition.ParameterCount),
                ("GetDatum", expectedDefinition.Datum, definition.Datum),
                ("GetEllipsoid", expectedDefinition.Ellipsoid, definition.Ellipsoid)
            })
                if (!Equals(item.Expected, item.Actual))
                    throw new InvalidDataException($"{field}: parsed {item.Name} differs from '{expectedCode}': " +
                        $"expected '{item.Expected}', actual '{item.Actual}'. CRS equivalence is unverified/incompatible.");
            foreach (var value in expectedDefinition.Numbers)
                if (definition.Numbers[value.Key] != value.Value)
                    throw new InvalidDataException($"{field}: parsed {value.Key} differs from '{expectedCode}': expected " +
                        value.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ", actual " +
                        definition.Numbers[value.Key].ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
                        ". CRS equivalence is unverified/incompatible.");
            // Unlike IsSameAs (which also compares descriptions/groups/bounds), the
            // documented mathematical comparator checks active projection parameters,
            // origins, offsets, map scale, reduction scale, datum and ellipsoid math.
            // The full-WKT guard above separately rejects numeric edits which the
            // native comparator's built-in tolerances could otherwise permit.
            if (!ReadBoolean(comparator, "SameCoordinateSystem", expected, actual) ||
                !ReadBoolean(comparator, "SameCoordinateSystem", actual, expected))
                throw new InvalidDataException($"{field}: the native mathematical CRS comparison does not match source drawing '{expectedCode}'.");
        }

        private static Definition ReadDefinition(object definition)
        {
            if (!ReadBoolean(definition, "IsValid") || !ReadBoolean(definition, "IsGeodetic"))
                throw new InvalidDataException("Map returned an invalid or non-geodetic CRS definition.");
            int type = ReadInteger(definition, "GetType");
            int count = ReadInteger(definition, "GetProjectionParameterCount");
            double unitScale = ReadNumber(definition, "GetUnitScale");
            if (type != ProjectedCoordinateSystem || count < 0 || count > MaximumProjectionParameters || unitScale <= 0)
                throw new InvalidDataException("Map returned an unsupported CRS type, unit scale or projection-parameter count.");
            var numbers = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (string method in new[] { "GetOriginLongitude", "GetOriginLatitude", "GetOffsetX", "GetOffsetY", "GetScaleReduction", "GetMapScale" })
                numbers.Add(method, ReadNumber(definition, method));
            // CS-Map's documented projection accessor uses indices 1 through 24.
            for (int index = 1; index <= count; index++)
                numbers.Add("GetProjectionParameter[" + index + "]", ReadNumber(definition, "GetProjectionParameter", index));
            string datum = ReadText(definition, "GetDatum");
            string ellipsoid = ReadText(definition, "GetEllipsoid");
            if (datum.Length == 0 || ellipsoid.Length == 0)
                throw new InvalidDataException("Map returned a CRS with an unresolved datum or ellipsoid.");
            return new Definition(type, ReadInteger(definition, "GetUnitCode"), unitScale,
                ReadInteger(definition, "GetProjectionCode"), ReadInteger(definition, "GetQuadrant"), count, datum, ellipsoid, numbers);
        }

        private sealed record Definition(int Type, int UnitCode, double UnitScale, int ProjectionCode,
            int Quadrant, int ParameterCount, string Datum, string Ellipsoid, IReadOnlyDictionary<string, double> Numbers);

        private static bool HasWktSyntax(string value) => value.IndexOfAny(new[] { '[', ']', '(', ')', '"' }) >= 0;

        private static string Bounded(string? value, string field)
        {
            if (value == null) return string.Empty;
            if (value.Length > MaximumDefinitionLength || value.IndexOf('\0') >= 0)
                throw new InvalidDataException($"{field} contains an oversized or invalid CRS definition.");
            return value.Trim();
        }

        // A bounded WKT1 structural guard, not a coordinate-system engine. It
        // compares only definitions emitted by the installed dictionary and never
        // computes projection math or guesses equivalent names. Decimal tokens are
        // normalized exactly (without floating-point rounding or fuzzy tolerances).
        private sealed class WktGuard
        {
            private const int MaximumNodes = 128;
            private const int MaximumDepth = 8;
            private const int MaximumValues = 512;
            private readonly string input;
            private int position;
            private int nodes;
            private int values;
            private sealed record Value(string Kind, string Text, Node? Child = null, string? Original = null);
            private sealed record Node(string Name, List<Value> Values);

            internal WktGuard(string input) { this.input = input; }

            internal static void VerifyEquivalent(string expected, string actual)
            {
                Node left = new WktGuard(Bounded(expected, "dictionary WKT")).ReadRoot();
                Node right = new WktGuard(Bounded(actual, "actual WKT")).ReadRoot();
                Compare(left, right, "PROJCS", root: true);
            }

            private Node ReadRoot()
            {
                Node root = ReadNode(0);
                White();
                if (position != input.Length || root.Name != "PROJCS") Fail("a complete projected PROJCS definition is required");
                Validate(root);
                return root;
            }

            private Node ReadNode(int depth)
            {
                if (++nodes > MaximumNodes || depth > MaximumDepth) Fail("definition nesting/count exceeds the verification limit");
                string name = Identifier();
                Expect('[');
                var result = new List<Value>();
                do
                {
                    if (++values > MaximumValues) Fail("definition value count exceeds the verification limit");
                    White();
                    if (position >= input.Length) Fail("incomplete definition");
                    char next = input[position];
                    if (next == '\"') result.Add(new Value("string", Quoted()));
                    else if (next is '+' or '-' or '.' || char.IsAsciiDigit(next))
                    {
                        int start = position;
                        string number = Number();
                        result.Add(new Value("number", number, Original: input[start..position]));
                    }
                    else
                    {
                        int saved = position;
                        string identifier = Identifier();
                        White();
                        if (position < input.Length && input[position] == '[')
                        {
                            position = saved;
                            result.Add(new Value("node", string.Empty, ReadNode(depth + 1)));
                        }
                        else result.Add(new Value("identifier", identifier));
                    }
                    White();
                    if (position >= input.Length) Fail("incomplete node");
                    if (input[position] != ',') break;
                    position++;
                } while (true);
                Expect(']');
                return new Node(name, result);
            }

            private static void Validate(Node node)
            {
                foreach (Value value in node.Values) if (value.Child != null) Validate(value.Child);
                switch (node.Name)
                {
                    case "PROJCS":
                        Shape(node, "string", "node", "node");
                        RequireChildren(node, ("GEOGCS", 1, 1), ("PROJECTION", 1, 1), ("PARAMETER", 1, 24),
                            ("UNIT", 1, 1), ("AXIS", 0, 2), ("AUTHORITY", 0, 1));
                        var names = node.Values.Where(value => value.Child?.Name == "PARAMETER")
                            .Select(value => value.Child!.Values[0].Text).ToArray();
                        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length) Fail("duplicate projection parameter");
                        break;
                    case "GEOGCS":
                        Shape(node, "string", "node", "node", "node");
                        RequireChildren(node, ("DATUM", 1, 1), ("PRIMEM", 1, 1), ("UNIT", 1, 1), ("AXIS", 0, 2), ("AUTHORITY", 0, 1));
                        break;
                    case "DATUM":
                        Shape(node, "string", "node");
                        RequireChildren(node, ("SPHEROID", 1, 1), ("TOWGS84", 0, 1), ("AUTHORITY", 0, 1));
                        break;
                    case "SPHEROID": Leaf(node, true, "string", "number", "number"); break;
                    case "PRIMEM":
                    case "UNIT": Leaf(node, true, "string", "number"); break;
                    case "PROJECTION": Leaf(node, true, "string"); break;
                    case "PARAMETER": Leaf(node, false, "string", "number"); break;
                    case "AUTHORITY": Leaf(node, false, "string", "string"); break;
                    case "AXIS":
                        Leaf(node, false, "string", "identifier");
                        if (!new[] { "NORTH", "SOUTH", "EAST", "WEST", "UP", "DOWN", "OTHER" }.Contains(node.Values[1].Text))
                            Fail("unsupported axis direction");
                        break;
                    case "TOWGS84":
                        if (node.Values.Count != 3 && node.Values.Count != 7 || node.Values.Any(value => value.Kind != "number"))
                            Fail("TOWGS84 must contain three or seven numeric values");
                        break;
                    default: Fail($"unsupported WKT1 node '{node.Name}'"); break;
                }
            }

            private static void RequireChildren(Node node, params (string Name, int Min, int Max)[] permitted)
            {
                if (node.Values.Skip(1).Any(value => value.Child == null)) Fail($"unexpected scalar in {node.Name}");
                foreach (Value value in node.Values.Skip(1))
                    if (!permitted.Any(item => item.Name == value.Child!.Name)) Fail($"unexpected {value.Child!.Name} in {node.Name}");
                foreach (var item in permitted)
                {
                    int count = node.Values.Count(value => value.Child?.Name == item.Name);
                    if (count < item.Min || count > item.Max || item.Name == "AXIS" && count == 1)
                        Fail($"missing, repeated or unsupported {item.Name} in {node.Name}");
                }
            }

            private static void Shape(Node node, params string[] kinds)
            {
                if (node.Values.Count < kinds.Length || kinds.Where((kind, index) => node.Values[index].Kind != kind).Any())
                    Fail($"invalid {node.Name} structure");
            }

            private static void Leaf(Node node, bool authority, params string[] kinds)
            {
                Shape(node, kinds);
                int extra = node.Values.Count - kinds.Length;
                if (extra != 0 && !(authority && extra == 1 && node.Values[^1].Child?.Name == "AUTHORITY"))
                    Fail($"unexpected values in {node.Name}");
            }

            private static Value[] Ordered(Node node, bool root)
            {
                if (!root) return node.Values.ToArray();
                // Sorting is limited to the root's named PARAMETER children.
                // All other nodes and their order remain part of the proof.
                return node.Values.Skip(1).Where(value => value.Child?.Name != "PARAMETER")
                    .Concat(node.Values.Where(value => value.Child?.Name == "PARAMETER")
                        .OrderBy(value => value.Child!.Values[0].Text, StringComparer.Ordinal)).ToArray();
            }

            private static void Compare(Node expected, Node actual, string path, bool root = false)
            {
                if (expected.Name != actual.Name) Fail($"{path}: expected node '{expected.Name}', actual '{actual.Name}'");
                Value[] left = Ordered(expected, root), right = Ordered(actual, root);
                if (left.Length != right.Length) Fail($"{path}: expected {left.Length} definition values, actual {right.Length}");
                for (int index = 0; index < left.Length; index++)
                {
                    Value expectedValue = left[index], actualValue = right[index];
                    if (expectedValue.Child != null && actualValue.Child != null)
                    {
                        string childPath = path + "/" + expectedValue.Child.Name;
                        if (expectedValue.Child.Name == "PARAMETER") childPath += "[" + expectedValue.Child.Values[0].Text + "]";
                        Compare(expectedValue.Child, actualValue.Child, childPath);
                    }
                    else if (expectedValue.Kind != actualValue.Kind || expectedValue.Text != actualValue.Text)
                        Fail($"{path} value {index + 1}: expected '{Display(expectedValue)}', actual '{Display(actualValue)}'");
                }
            }
            private static string Display(Value value)
            {
                string text = value.Child?.Name ?? value.Original ?? value.Text;
                return text.Length <= 128 ? text : text[..128] + "...";
            }

            private string Quoted()
            {
                Expect('\"');
                var result = new StringBuilder();
                while (position < input.Length)
                {
                    char character = input[position++];
                    if (char.IsControl(character)) Fail("control character in quoted text");
                    if (character != '\"') { result.Append(character); continue; }
                    if (position < input.Length && input[position] == '\"') { result.Append('\"'); position++; continue; }
                    return result.ToString();
                }
                Fail("unterminated quoted text");
                return string.Empty;
            }

            private string Number()
            {
                int start = position;
                bool negative = input[position] == '-';
                if (input[position] is '+' or '-') position++;
                var digits = new StringBuilder();
                while (position < input.Length && char.IsAsciiDigit(input[position])) digits.Append(input[position++]);
                int decimals = 0;
                if (position < input.Length && input[position] == '.')
                {
                    position++;
                    while (position < input.Length && char.IsAsciiDigit(input[position])) { digits.Append(input[position++]); decimals++; }
                }
                if (digits.Length == 0 || digits.Length > 128) Fail("invalid or oversized numeric value");
                int exponent = 0;
                if (position < input.Length && input[position] is 'e' or 'E')
                {
                    position++;
                    bool exponentNegative = position < input.Length && input[position] == '-';
                    if (position < input.Length && input[position] is '+' or '-') position++;
                    int exponentStart = position;
                    while (position < input.Length && char.IsAsciiDigit(input[position]))
                    {
                        exponent = checked(exponent * 10 + input[position++] - '0');
                        if (exponent > 1024) Fail("numeric exponent exceeds the verification limit");
                    }
                    if (position == exponentStart) Fail("missing numeric exponent");
                    if (exponentNegative) exponent = -exponent;
                }
                if (position == start) Fail("missing numeric value");
                string significant = digits.ToString().TrimStart('0');
                if (significant.Length == 0) return "0";
                exponent -= decimals;
                while (significant.EndsWith("0", StringComparison.Ordinal)) { significant = significant[..^1]; exponent++; }
                return (negative ? "-" : "") + significant + "e" + exponent.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            private string Identifier()
            {
                White();
                int start = position;
                while (position < input.Length && (input[position] is >= 'A' and <= 'Z' || input[position] == '_' || position > start && char.IsAsciiDigit(input[position]))) position++;
                if (position == start) Fail("unsupported token or identifier");
                return input[start..position];
            }
            private void Expect(char value)
            {
                White();
                if (position >= input.Length || input[position++] != value) Fail($"expected '{value}'");
            }
            private void White()
            {
                while (position < input.Length && input[position] is ' ' or '\t' or '\r' or '\n') position++;
            }
            private static void Fail(string detail) => throw new InvalidDataException("CRS WKT verification failed: " + detail + ".");
        }

        private static Type FindFactoryType()
        {
            // Geometry is the documented factory's managed assembly. Some Map
            // releases expose/forward the same public type through PlatformBase.
            foreach (string assemblyName in new[] { "OSGeo.MapGuide.Geometry", "OSGeo.MapGuide.PlatformBase" })
            {
                Assembly? assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(candidate => candidate.GetName().Name == assemblyName);
                if (assembly == null)
                {
                    try { assembly = Assembly.Load(new AssemblyName(assemblyName)); }
                    catch (FileNotFoundException) { continue; }
                }
                Type? factory = assembly.GetType(FactoryType, throwOnError: false, ignoreCase: false);
                if (factory != null) return factory;
            }
            throw new InvalidOperationException("The installed Map MgCoordinateSystemFactory could not be loaded. " +
                "Run inside supported Civil 3D with its coordinate-system dictionary available; no replacement runtime or dictionary is used.");
        }

        private static MethodInfo FindMethod(object target, string name, object[] arguments)
        {
            MethodInfo[] matches = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(candidate => candidate.Name == name && !candidate.ContainsGenericParameters &&
                    candidate.ReturnType != typeof(Type) && candidate.GetParameters().Length == arguments.Length &&
                    candidate.GetParameters().Select((parameter, index) =>
                        parameter.ParameterType.IsInstanceOfType(arguments[index])).All(isMatch => isMatch)).ToArray();
            if (matches.Length != 1)
                throw new MissingMethodException($"Map {target.GetType().FullName}.{name} must expose one exact public instance signature; found {matches.Length}.");
            return matches[0];
        }
        private static object Call(object target, string name, params object[] arguments)
            => FindMethod(target, name, arguments).Invoke(target, arguments)
                ?? throw new InvalidDataException($"Map {target.GetType().FullName}.{name} returned null.");
        private static string ReadText(object target, string method) => Call(target, method) is string text ? text
            : throw new InvalidDataException($"Map {method} returned an unexpected non-string value.");
        private static bool ReadBoolean(object target, string method, params object[] arguments) => Call(target, method, arguments) is bool value ? value
            : throw new InvalidDataException($"Map {method} returned an unexpected non-Boolean value.");
        private static int ReadInteger(object target, string method) => Call(target, method) switch
        {
            int value => value,
            short value => value,
            _ => throw new InvalidDataException($"Map {method} returned an unexpected integer type.")
        };
        private static double ReadNumber(object target, string method, params object[] arguments)
        {
            object value = Call(target, method, arguments);
            if (value is not double number || !double.IsFinite(number))
                throw new InvalidDataException($"Map {method} returned a nonfinite or unexpected numeric value.");
            return number;
        }

        private sealed class NativeScope : IDisposable
        {
            private sealed record OwnedWrapper(object Value, MethodInfo DisposeMethod, string Acquisition);
            private readonly List<OwnedWrapper> owned = new();

            // Map 3D 2026's four managed CRS wrappers expose public void Dispose()
            // but implement no System.IDisposable interface. Bind the verified
            // managed method, never native Release/Close or private destructors.
            // Validate the acquisition's declared wrapper contract BEFORE calling
            // native code, so a missing disposal API cannot strand a new wrapper.
            internal object Create(Type type)
            {
                MethodInfo dispose = FindDispose(type);
                return Track(Activator.CreateInstance(type), dispose, type.FullName + " constructor");
            }
            internal object Acquire(object target, string name, params object[] arguments)
            {
                MethodInfo method = FindMethod(target, name, arguments);
                MethodInfo dispose = FindDispose(method.ReturnType);
                return Track(method.Invoke(target, arguments), dispose, target.GetType().FullName + "." + name);
            }
            private object Track(object? value, MethodInfo dispose, string acquisition)
            {
                if (value == null) throw new InvalidDataException($"Map {acquisition} returned null.");
                if (!owned.Any(item => ReferenceEquals(item.Value, value))) owned.Add(new OwnedWrapper(value, dispose, acquisition));
                return value;
            }
            private static MethodInfo FindDispose(Type type)
            {
                MethodInfo[] matches = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(method => method.Name == "Dispose" && !method.ContainsGenericParameters &&
                        method.ReturnType == typeof(void) && method.GetParameters().Length == 0).ToArray();
                if (matches.Length != 1)
                    throw new InvalidDataException($"Map CRS wrapper '{type.FullName}' must expose one public instance void Dispose(); found {matches.Length}.");
                return matches[0];
            }
            public void Dispose()
            {
                var failures = new List<System.Exception>();
                for (int index = owned.Count - 1; index >= 0; index--)
                {
                    OwnedWrapper wrapper = owned[index];
                    // Dispose is void: null from MethodInfo.Invoke is normal here.
                    // Each acquisition owns only its managed native-reference wrapper;
                    // releasing a GetCatalog wrapper does not destroy the cached catalog.
                    try { wrapper.DisposeMethod.Invoke(wrapper.Value, Array.Empty<object>()); }
                    catch (System.Exception ex)
                    {
                        failures.Add(new InvalidOperationException($"Map CRS wrapper disposal failed after {wrapper.Acquisition}.", ex));
                    }
                }
                owned.Clear();
                if (failures.Count != 0)
                    throw new AggregateException("Map CRS resources could not be fully disposed; import must not proceed.", failures);
            }
        }
    }

    internal sealed record GisNewDrawingCoordinateSystemVerification(
        string ExpectedCode, string ObservedCode, string ResolvedCode, string Evidence);
}
