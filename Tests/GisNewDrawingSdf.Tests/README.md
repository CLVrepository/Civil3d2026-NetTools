# Native SDF preflight contract tests

Run from the repository root with a .NET 8+ SDK:

```powershell
dotnet run --project Tests/GisNewDrawingSdf.Tests/GisNewDrawingSdf.Tests.csproj
```

This package-free executable links the production `GisNewDrawingSdf.cs` and profile parser. Its test assembly is deliberately named `OSGeo.FDO`, so the production runtime-reflection path resolves the small, test-only facade in `FakeFdo.cs`. **Never deploy this test executable or its `OSGeo.FDO.dll` into Civil 3D or the plugin output.** The test project is separate from the application and is not referenced by it.

The test files and geometry bytes are synthetic. This exercises the actual helper's reflection calls, explicit `ReadOnly=TRUE` gate, per-class native CRS/context checks, exact counts, mapped scalar preservation, duplicate-name preservation, immutable geometry/hash/XY/XYZ coordinate snapshots, rejection of curve/multipart/M geometry, file-change detection, and deterministic cleanup on success and injected failures. It does not establish the existence, ABI, loading behavior, provider behavior or SDF interoperability of Autodesk binaries.

The supplied IPF fixtures are linked from the neighboring profile-test project without alteration. No packages or Autodesk references are required. Tests use a unique temporary directory and remove it on completion.

## Native Civil 3D acceptance

Run the completed `CLV-GIS-NEW-DRAWING` command in supported Civil 3D 2026 with Map/FDO installed:

1. With the source drawing's exact CRS `NV83.NCRS-LVHEF`, select the actual supplied `L24-00066-STRM-E.sdf` and the matching supplied IPF. Verify native preflight reports 13 Pipes and 22 Structures, including the one `UFLS-Null Structure` source feature, and that post-import verification agrees.
2. Use the same SDF with source CRS `NV83.NCRS-LVF`. It must stop before import with an actual-SDF/source-CRS mismatch. Renaming the SDF must not affect that result.
3. Verify the actual SDF SHA256, size and timestamp remain unchanged; readers/connections release the file after either success or failure.
4. Verify a missing/unresolvable FDO runtime, unavailable read-only property, malformed file, missing class, missing mapped property, unresolved spatial context, missing native CRS/WKT or failed reader produces a clear preflight error and never initiates import.

## Validation status at authoring

- Production reflection member names checked against Autodesk's Map 3D 2026 `FDO_API_managed.chm`.
- Only read-only `GetSpatialContexts` and `Select` command types are used.
- Source/whitespace checks passed.
- Compilation and test execution were not run in the Linux authoring environment, which has no .NET SDK or Autodesk runtime. Run the command above before reporting the portable tests as passed. Native Civil 3D acceptance remains separate.
