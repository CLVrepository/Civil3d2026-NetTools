# Dictionary-anchored CRS verification contract tests

Run with a .NET 8+ SDK from the repository root:

```powershell
dotnet run --project Tests/GisNewDrawingCoordinateSystem.Tests/GisNewDrawingCoordinateSystem.Tests.csproj
```

This package-free executable links the production CRS verifier/profile parser and references the shared test-only `GisNewDrawingCoordinateSystem.FakeMapGuide` facade. The facade assembly is named `OSGeo.MapGuide.Geometry` so production reflection can exercise the exact public API names without test hooks. **Never deploy the fake assembly, either test project, or their outputs into Civil 3D or the plugin output.** Neither project is referenced by the application.

The shared fake is also suitable for the SDF contract tests: reference its project, call `GisCoordinateSystemTestSupport.FakeMapGuide.Reset()` at the start of each case, and use `FakeMapGuide.Wkt(code)` for complete fixture WKT. `RegisterWkt(wkt, code)` deliberately models a native parser snapping the provided WKT to a dictionary entry. `ParsedDefinitions` allows native-property disagreement to be injected independently. `Calls` and `LiveObjects` verify read-only member calls and wrapper cleanup.

## Verification boundary

The source must still be exactly `NV83.NCRS-LVF` or `NV83.NCRS-LVHEF`. The expected definition is obtained with the installed factory's `CreateFromCode`, and its returned code, usability, validity and projected/geodetic type are checked. Every nonempty supplied actual field is checked independently. Code-only readbacks are supported; the SDF caller separately requires a complete `GetCoordinateSystemWkt` result.

A bounded WKT1 structural guard compares actual WKT with the installed dictionary definition's own `ToString()` WKT. It ignores only the root `PROJCS` display title, insignificant whitespace, exact equivalent decimal spellings and the order of root `PARAMETER` nodes. It does not compute GIS mathematics or infer equivalent aliases. All nested identities, units, values and additional supported nodes remain significant. Unknown nodes, duplicate parameters, malformed/incomplete input and resource-limit violations fail closed. Numeric normalization is exact decimal normalization, with no floating-point rounding or numeric tolerance. Different dictionary serialization precision may therefore still require native investigation rather than an assumed match.

The native factory must also validate and parse actual WKT. The installed `MgCoordinateSystemMathComparator.SameCoordinateSystem` must agree in both directions with datum-parameter comparison enabled. Exposed native unit, projection, datum, ellipsoid, quadrant, origins, offsets, scale and active projection-parameter properties must agree exactly and be finite. `ResolvedCode` is the independently requested expected dictionary code after these checks, never the WKT title. All acquired native wrappers must dispose successfully before a result escapes.

## Why the structural guard is necessary

Autodesk's Map 3D 2026 `sdk.geo.ref.chm` documents the factory, catalog, coordinate-system and mathematical-comparator members used here. Upstream implementation review found two reasons not to accept a successful parser or comparator alone:

- `WktToDefinition` can return a dictionary entry based on the parsed coordinate-system/datum/ellipsoid names. A same-title changed numeric value could consequently be hidden by dictionary lookup.
- The mathematical comparator deliberately permits tolerances and some equivalent datum transformation descriptions. `IsSameAs`, conversely, also compares non-mathematical descriptions, groups and bounds which WKT does not reliably preserve.

Sources:

- [Factory and WKT conversion implementation](https://github.com/jumpinjackie/mapguide/blob/28dce9ed7c3d4d8e15b0f25c45f674f0051ce881/MgDev/Common/CoordinateSystem/CoordSysFormatConverter.cpp)
- [Mathematical comparison implementation](https://github.com/jumpinjackie/mapguide/blob/28dce9ed7c3d4d8e15b0f25c45f674f0051ce881/MgDev/Common/CoordinateSystem/CoordSysMathComparator.cpp)
- [Coordinate-system accessors, IsSameAs and 1-based projection parameters](https://github.com/jumpinjackie/mapguide/blob/28dce9ed7c3d4d8e15b0f25c45f674f0051ce881/MgDev/Common/CoordinateSystem/CoordSys.cpp)

The upstream source explains the defensive checks. It does not prove the behavior or ABI of the user's installed Autodesk binaries.

## Native acceptance still required

Inside Civil 3D 2026, verify the actual LVHEF SDF against the installed dictionary, then reject it against LVF. Confirm the actual source/destination readbacks, import counts and SDF hash in the end-to-end command. In a separate read-only in-memory probe, exercise whitespace/numeric/PARAMETER-order changes and same-title changes to false easting, scale, units, datum, ellipsoid and projection. The test facades cannot establish that the installed dictionary's WKT serialization exactly matches the supplied SDF or that native construction/loading succeeds.

At authoring, tree-sitter C# syntax checks and whitespace checks passed. The cloud authoring environment has no .NET SDK or Autodesk runtime, so compilation, execution and native acceptance were not performed there.

The shared fake MapGuide reference uses the same GisTestTargetFramework property as the runner, so a .NET 10-only build does not accidentally restore the reference for a different target. Never deploy either fake Autodesk-named test assembly into Civil 3D.

## Installed managed lifetime contract

The Civil/Map 3D 2026 metadata probe confirmed that all four acquired wrapper types have no interfaces (including `IDisposable`), and each declares public virtual non-final instance `void Dispose()`, overriding `MgObject.Dispose()` through the base chain. Their base chain is `MgGuardDisposable` → `MgDisposable` → `MgObject` → `Object`. The shared fake now follows that contract instead of adding an interface absent from the installed API. The verifier pre-binds the declared return type's disposal method before allocation/acquisition, tracks managed reference identity, and invokes each managed wrapper's method once in reverse acquisition order. Void return is expected and is not routed through the non-null value-reader helper.

Autodesk-authored upstream wrapper glue uses native `Release()` for managed disposal. `GetCatalog` returns an added reference to the cached native catalog; factory `Create`/`CreateFromCode` return detached caller references and `GetMathComparator` allocates a comparator. Releasing these managed references therefore preserves shared native owners. Distinct managed wrappers remain separately owned even if the native definition is cached. No native-pointer deduplication or global catalog deletion is performed.

Sources for ownership (the installed metadata probe, not this upstream mirror, establishes the deployed managed signatures):

- [Factory acquisitions and shared catalog ownership](https://github.com/jumpinjackie/mapguide/blob/28dce9ed7c3d4d8e15b0f25c45f674f0051ce881/MgDev/Common/Geometry/CoordinateSystem/CoordinateSystemFactory.cpp)
- [Catalog comparator acquisition](https://github.com/jumpinjackie/mapguide/blob/28dce9ed7c3d4d8e15b0f25c45f674f0051ce881/MgDev/Common/CoordinateSystem/CoordSysCatalog.cpp)
- [Geometry wrapper generation uses native Release](https://github.com/jumpinjackie/mapguide/blob/28dce9ed7c3d4d8e15b0f25c45f674f0051ce881/MgDev/Web/src/DotNetUnmanagedApi/Geometry/GeometryApi.vcxproj)
- [Managed disposal typemap](https://github.com/jumpinjackie/mapguide/blob/28dce9ed7c3d4d8e15b0f25c45f674f0051ce881/MgDev/Oem/SWIGEx/Lib/csharp/csharp.swg)

The regression suite checks no-interface disposal for every returned wrapper, exact reverse order with distinct wrapper IDs, code-only and WKT paths, repeated catalog acquisitions preserving a modeled shared owner, null returns and cleanup draining all wrappers when any disposal throws. Repeated calls inside an isolated native host remain necessary to verify deployed runtime behavior.

## Native hidden-method resolution

The complete installed-wrapper candidate enumeration found two duplicated parameter signatures: native `MgCoordinateSystem.ToString()` (string, nonvirtual) plus virtual `Object.ToString()`, and native integer `GetType()` plus `Object.GetType()` returning `System.Type`. Both native methods are hidden declarations rather than overrides. All four wrappers' `Dispose()` methods are virtual non-final overrides; the remaining 25 invoked API methods are virtual non-final new slots declared directly on their documented wrapper classes.

The fake now matches those shapes. A regression explicitly requires flattened reflection to expose both ToString candidates, then checks that the production resolver selects the native declaration. The resolver walks public instance DeclaredOnly members from the receiver class upward, filters the exact expected return type and compatible parameters, selects the sole nearest match, and rejects same-level ambiguity. System.Object is excluded entirely, so a missing native WKT/type accessor cannot silently become a CLR display/type value. Tests also cover inherited/hidden native declarations, wrong-return/static/generic/private/by-ref rejection, and the complete measured API signature/slot table. Existing hostile-WKT and lifecycle tests remain in place.

The optional isolated native fixture shares this resolver through linked production source rather than copying its own lookup logic. It remains source-only until startup isolation and host execution are separately validated.
