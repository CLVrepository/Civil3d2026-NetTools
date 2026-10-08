# GIS new-drawing profile preflight tests

This zero-package console runner links the actual production
`CLV_CivilTools/Gis/GisNewDrawingProfile.cs` and
`CLV_CivilTools/Gis/GisNewDrawingObjectDataPlan.cs` without Autodesk dependencies.

Run from the repository root with a .NET 8 SDK/runtime:

```sh
dotnet run --project Tests/GisNewDrawingProfile.Tests/GisNewDrawingProfile.Tests.csproj
```

If only a newer SDK/runtime is installed, override the target, for example:

```sh
dotnet run --project Tests/GisNewDrawingProfile.Tests/GisNewDrawingProfile.Tests.csproj -p:TargetFramework=net10.0
```

Exit code is zero only when every test passes. No NuGet test packages are used.

`Fixtures/` contains byte-for-byte copies of the two user-supplied IPFs. Golden
SHA-256 assertions ensure they are not silently regenerated or normalized:

- LVF: `f9119c60aed9a1cb4a4d881c3d63578f5b3513678a2303f8d0847267b252003d`
- LVHEF: `3333477e3af9ee3a605e6e7c02c73189f636ec840cfdfa7ff74b2e6a591b7057`

The fixture-local `.gitattributes` disables newline conversion for these exact
copies when checking out on either Windows or Linux.

The tests cover exact CRS/profile resolution without aliases or fallback,
read-only fixture loading, selection of only Pipes/Structures, CAD layers,
original/new CRS, nine pipe and two structure OD column mappings, unchanged
ambiguous OD table flags with diagnostics, immutable snapshots, repeated/order-
independent preflight, invalid/missing XML, DTD/external-entity prohibition,
document-size limits, providers, duplicate/unknown settings, namespace tricks,
source filename/path/connection overrides, spatial/attribute filters, class and
column conflicts, and unsupported block/classification/unique-key settings.

The pure runtime OD plan tests cover exact explicit-field allowlists, schema-
derived types independent of feature rows, New versus compatible Existing table
selection, reordered existing fields, independent choices for the two targets,
unrelated tables, missing/extra schema fields and classes, duplicate/case-aliased
names, incompatible existing types, rejection of unapproved FDO conversions,
failure without a partial plan, and immutable snapshots. Approved FDO mappings
are String to Character, Int16/Int32 to Integer, and Single/Double to Real.
Byte, Int64, Boolean, DateTime, and other unsupported types are rejected.

Column readback tests exercise both New and Existing plans. Exact mapped fields
require the planned mode and output name. Every other native column, including a
case-only source alias, must have empty output and either `NoImportMapping` or
the planned layer mode, matching the native API's documented column semantics.
Unexpected modes, outputs, case changes, and whitespace outputs are rejected.
The native caller must additionally check that all required exact source names
were present; accepting a cleared alias does not satisfy that requirement.

LoadedProfileName is metadata only: the helper preserves it and never follows
that path or loads an alternate profile. Actual deployment paths are the two
explicit server profiles, with the observed period in the LVHEF filename.

These tests do not prove native Map importer behavior, SDF record counts or
schema, coordinate conversion, AutoCAD document creation, OD attachment, native
geometry, save/undo, or template compatibility. The supplied IPFs say
`NoODTable` / `ImportMappingInvalid` despite explicit `MappedToOD` columns;
profile parsing deliberately reports and preserves that ambiguity. The pure
runtime plan interprets only the explicit column mappings and does not change
either fixture. Native import must apply and read back the plan, then verify
actual schemas and OD results before claiming success. These tests do not prove
when Map creates a new table or how it handles empty input classes or null values.
