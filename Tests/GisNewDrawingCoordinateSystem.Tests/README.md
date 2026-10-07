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
