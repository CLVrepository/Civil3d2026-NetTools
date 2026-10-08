# Drawing setup coordinate and resource tests

The original drawing's exact assigned code is authoritative: `NV83.NCRS-LVF` or `NV83.NCRS-LVHEF`. The setup policy assigns that code once to the new drawing, then reads it back. The resource helper selects the matching shared profile path for the later manual import. It does not read or parse IPF files.

Run:

`dotnet run --project Tests/GisNewDrawingCoordinateSystem.Tests/GisNewDrawingCoordinateSystem.Tests.csproj`

For a .NET 10-only executor use `-p:TargetFramework=net10.0 -p:GisTestTargetFramework=net10.0`.

These package-free tests link the production coordinate policy and resource helper. They cover:

- Exact blank-template and profile-folder UNC paths, with Windows separators on every test platform.
- Exact source-code/profile selection, including the intentional period before `LVHEF` in `UFLS-IMPORT-NV83.NCRS.LVHEF.ipf`.
- Missing and unsupported source-code refusal, including case changes, whitespace and punctuation aliases, with no profile fallback or destination writes.
- One destination assignment followed by readback, replacement of an empty or mismatched template code, readback mismatches, and setter/reader failures.
- New drawing save-path validation using unique temporary folders and files: fully qualified new names, explicit or implicit `.dwg` extensions, canonical dot segments, existing file/directory refusal, unavailable parents, invalid and relative paths, and cleared outputs on refusal.
- Source/template/open drawing path protection under canonical full-path, case-insensitive comparison, including nonexistent protected paths and implicit extensions. Non-rooted unsaved document display names are ignored.

The save-path policy performs read-only filesystem checks. The tests create and remove only their own unique temporary folder and verify that validation preserves existing contents and creates no destination. Validation is a snapshot, not an atomic reservation: the native caller must revalidate immediately before saving. These tests do not establish overwrite safety against concurrent filesystem changes or exercise native DWG save/reopen behavior.

No Autodesk runtime, MapGuide runtime or fake assembly is referenced or loaded. These checks need no shared-drive access and do not certify that the deployed template or profiles are accessible.

Native Civil 3D drawing setup and the later manual `MAPIMPORT` workflow still require acceptance testing. These portable tests do not exercise import, geometry or Object Data.
