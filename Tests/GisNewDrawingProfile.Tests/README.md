# GIS new-drawing profile preflight tests

This zero-package console runner links the actual production
`CLV_CivilTools/Gis/GisNewDrawingProfile.cs` without Autodesk dependencies.

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

LoadedProfileName is metadata only: the helper preserves it and never follows
that path or loads an alternate profile. Actual deployment paths are the two
explicit server profiles, with the observed period in the LVHEF filename.

These tests do not prove native Map importer behavior, SDF record counts or
schema, coordinate conversion, AutoCAD document creation, OD attachment, native
geometry, save/undo, or template compatibility. The supplied IPFs say
`NoODTable` / `ImportMappingInvalid` despite explicit `MappedToOD` columns;
production deliberately reports and preserves that ambiguity. Native import
must verify actual OD results before claiming success.
