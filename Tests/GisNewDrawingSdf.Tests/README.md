# Native SDF preflight contract tests

Run from the repository root with a .NET 8+ SDK:

```powershell
dotnet run --project Tests/GisNewDrawingSdf.Tests/GisNewDrawingSdf.Tests.csproj
```

This package-free executable links the production `GisNewDrawingSdf.cs` and profile parser. Its test assembly is deliberately named `OSGeo.FDO`, so the production runtime-reflection path resolves the small, test-only facade in `FakeFdo.cs`. **Never deploy this test executable or its `OSGeo.FDO.dll` into Civil 3D or the plugin output.** The test project is separate from the application and is not referenced by it.

The test files and geometry bytes are synthetic. This exercises the actual helper's reflection calls, explicit `ReadOnly=TRUE` gate, exact `DescribeSchema` membership before either Select, attached/detached reader identity checks, exact counts, mapped scalar preservation, duplicate-name preservation, immutable geometry/hash/XY/XYZ coordinate snapshots, rejection of curve/multipart/M geometry, file-change detection, and deterministic cleanup on success and injected failures. It does not establish the existence, ABI, loading behavior, provider behavior or SDF interoperability of Autodesk binaries.

The original drawing's exact supported CRS is authoritative, and the matching supplied IPF is the import plan. Snapshot `CoordinateSystem` records that source code; it does not certify SDF CRS metadata. For both supported source codes, absent spatial contexts and empty, mismatched, or malformed SDF CRS fields must still permit raw feature reads. Duplicate context names and unresolved geometry associations do not affect preflight. The fake throws on any `GetSpatialContexts` command or `SpatialContextAssociation` access, and every test checks that neither was attempted. Unsupported source codes and a missing profile are rejected before FDO access. The runner compiles only the profile parser and SDF helper, with no coordinate-system dictionary/parser dependency.

Identity cases include missing/duplicate/wrong-case schemas and class declarations, dependent schemas, conflicting declared or reader qualified names and schema links, exact bare names from detached readers, and actual metadata in failures. A bare reader name is accepted only after both exact classes were independently verified in `Civil_Schema` on the same guarded read-only connection, and only if both `FeatureSchema` and `Parent` are null. No suffix, alias, default-schema, or case-insensitive match is used.

Snapshot `FieldTypesByClass` retains every mapped source column's declared FDO `DataType` under the exact class and column names. The helper reuses the declaration already read to choose its scalar getter; it makes no additional native calls or inferences from feature values. Tests cover populated, empty and all-null classes, non-default declared types, unsupported types on empty classes, one native type read per mapped property, and independent immutable copies of both dictionary levels after schema or constructor-input changes. Geometry and unmapped fields are not included.

The supplied IPF fixtures are linked from the neighboring profile-test project without alteration. No packages or Autodesk references are required. Tests use a unique temporary directory and remove it on completion.

## Native Civil 3D acceptance

Run the completed `CLV-GIS-NEW-DRAWING` command in supported Civil 3D 2026 with Map/FDO installed:

1. With the source drawing's exact CRS `NV83.NCRS-LVHEF`, select the actual supplied `L24-00066-STRM-E.sdf` and the matching supplied IPF. Verify native preflight reports 13 Pipes and 22 Structures, including the one `UFLS-Null Structure` source feature, and that post-import verification agrees.
2. Exercise both supported original-drawing CRSs with their matching supplied IPFs. Empty, mismatched, or malformed SDF CRS metadata must not block preflight or replace the source drawing's code. Renaming the SDF must not affect that result. Verify the importer uses the source code and that post-import native XYZ matches the preflight snapshot.
3. Verify the actual SDF SHA256, size and timestamp remain unchanged; readers/connections release the file after either success or failure.
4. Verify an unsupported original-drawing CRS, missing/unresolvable FDO runtime, unavailable read-only property, malformed file, missing class, missing mapped property or failed feature reader produces a clear preflight error and never initiates import. Missing CRS metadata alone is permitted; invalid file, schema, scalar or geometry data is still rejected.
5. Record the actual installed reader's `Name`, `QualifiedName`, `FeatureSchema?.Name`, and `Parent?.Name` if class identity fails. The 2026 managed reference documents null links for unattached definitions; upstream SDF source returns deep copies from feature readers. The installed 2026 provider's actual values still require native verification, not inference from the facade.

## Validation status at authoring

- Production reflection member names checked against Autodesk's Map 3D 2026 `FDO_API_managed.chm`.
- Only read-only `DescribeSchema` and `Select` command types are used. Spatial-context enumeration, geometry-context association lookup and SDF CRS verification are absent from the production helper.
- Source/whitespace checks passed.
- Compilation and test execution were not run in the Linux authoring environment, which has no .NET SDK or Autodesk runtime. Run the command above before reporting the portable tests as passed. Native Civil 3D acceptance remains separate.

To run with another installed SDK target, set `-p:GisTestTargetFramework=net10.0` (or the appropriate target). Never deploy the fake Autodesk-named test assembly into Civil 3D.
