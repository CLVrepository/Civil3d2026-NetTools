# Source drawing coordinate policy tests

The original drawing's exact assigned code is authoritative: `NV83.NCRS-LVF` or `NV83.NCRS-LVHEF`. The production policy assigns that code once to the new drawing, then reads it back. For each selected import layer it explicitly replaces the incoming/from coordinate-system setting with that same source code and reads it back. SDF coordinate-system names/WKT are not inputs to this policy.

Run:

`dotnet run --project Tests/GisNewDrawingCoordinateSystem.Tests/GisNewDrawingCoordinateSystem.Tests.csproj`

For a .NET 10-only executor use `-p:TargetFramework=net10.0 -p:GisTestTargetFramework=net10.0`.

These package-free tests link the production policy/profile code. They test the exact two supported source codes, missing/unsupported source refusal before writes, one assignment followed by readback, replacement of empty/mismatched/malformed incoming labels, readback mismatches, and setter/reader failures. No MapGuide runtime or fake assembly is referenced or loaded.

The old dictionary/WKT comparison and reflection-wrapper suite were removed because the user's intended workflow does not require SDF CRS validation. MapGuide parser/dictionary ownership tests are no longer relevant to this command. This does not claim that incoming SDF labels are correct; the user-selected SDF's raw coordinates are intentionally interpreted in the original drawing's assigned CRS.

The importer still verifies every imported vertex against the raw SDF XYZ coordinates (absolute tolerance 0.000001 drawing units), as well as counts, identities and mapped OD. A native transform/scale that changes coordinates therefore fails verification and the incomplete new drawing is discarded. These portable tests cannot establish native import behavior or end-to-end acceptance.

API evidence: Autodesk Map 3D 2026 `sdk.arx.net.ref.chm`, `Autodesk.Gis.Map.ImportExport.InputLayer.TargetCoordinateSystem`, documents it as the read/write incoming/from CRS shown in the Import dialog's Coordinate System column. `OriginalCoordinateSys` is unimplemented and always empty; this command never uses it as evidence. The supplied profiles remain unchanged.
