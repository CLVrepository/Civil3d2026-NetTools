# Create GIS Drawing

`CLV-GIS-NEW-DRAWING` (Q2 > GIS > DATA > **CREATE GIS DRAWING**) prepares a separate, unsaved drawing for **manual MAPIMPORT**. Run it from the original survey/network drawing. It uses that drawing's assigned coordinate system and automatically finds the supported survey linework and DI/MH blocks; there is no geometry-selection or SDF prompt.

## Workflow

1. Read the original drawing's exact assigned `NV83.NCRS-LVF` or `NV83.NCRS-LVHEF` code and capture the eligible survey objects.
2. Create a new drawing from `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Drawing Templates\Blank (2026).dwt`. Require empty model space.
3. Match the original drawing's insertion units and assign its same coordinate-system code once, then verify readback.
4. Clone the eligible geometry and dependencies at native coordinates. Verify geometry, attributes, dynamic state, attached Object Data and mapped definitions; incompatible resource collisions stop setup.
5. Leave the prepared drawing active and unsaved. The completion message identifies its drawing name and the exact matching IPF path.
6. In that drawing, run **MAPIMPORT**, select the exported network SDF, and load the matching profile shown by the tool. Complete the import manually, then use **SAVEAS** to choose the drawing name and folder. Run the existing GIS preparation command after the network import.

The command does not choose, open, inspect or import an SDF. It does not load or modify an IPF. The shared template and source drawing remain unchanged. No output file is automatically saved or overwritten.

## Matching manual import profile

Profile folder: `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\SDF to SHP`

- `NV83.NCRS-LVF`: `UFLS-IMPORT-NV83.NCRS-LVF.ipf`
- `NV83.NCRS-LVHEF`: `UFLS-IMPORT-NV83.NCRS.LVHEF.ipf`

The period before `LVHEF` is intentional. Missing or unsupported source assignments stop before creating a drawing; no alias or default is substituted. Profile selection here only displays the path for the user's manual import. Its file availability, settings and resulting network data are not certified by setup completion.

## Preservation and failure behavior

The copy path retains its existing source snapshots, dependency checks, geometry/attribute/dynamic-state/OD readback, and native IdMapping verification. No explode-first, COPYBASE transform, or appearance fallback is introduced. Built-in records such as Continuous, ByLayer, ByBlock, layer 0 and Standard are reused only when their checked rendering properties agree. Matching handles in separate databases are not identity evidence.

If drawing creation, assignment, copying or setup verification fails, the command attempts to discard only its freshly created incomplete drawing and restore the source context. The cause and any cleanup error remain visible in the surviving editor. If disposal fails, the remaining drawing is explicitly reported incomplete and must be closed without saving. Once setup completes, the command has ended; a later manual MAPIMPORT operation does not trigger this command's discard logic.

The destination-only `CLV_GIS_NEW_DRAWING_V2` Xrecord records `SETUP_READY_MANUAL_IMPORT`. It marks verified drawing setup, not verified network import. Existing conversion commands do not interpret that record.

## Why automatic import is paused

The final automatic trial, commit `c15fbce02ce4a18b11e07a1033f4f3b3071df28f`, passed the source/Blank/CRS/copy stages and then stopped before calling Import. The exact failing field was `Civil_Schema:Pipes.Name`: the native `ColumnDataMapping` getter returned `Name` while leaving its `ImportDataMapping` enum output unwritten. Because Name was a required mapped field, verification correctly refused to guess a mode. The incomplete destination was discarded, no output was saved, and the source remained unchanged.

On 2026-10-08 the user directed that automatic-import debugging stop and requested this setup-only workflow. The automatic-import adapter, SDF reader, IPF parser, runtime OD planner, native-output helper, and their obsolete test projects/fixtures have been removed from the active code. The prior automatic version remains recoverable through Git history and recovery refs. The active clone, appearance, coordinate-system and resource-selection helpers remain.

## Validation

The setup-only change has static review and reference checks. Its executable tests, full plugin build and native completion path remain unrun in the authoring environment because the .NET SDK and Autodesk runtime are unavailable. Successful copy stages in earlier trials do not establish a complete pass for this update.

Retained relevant tests:

- `Tests/GisNewDrawingCoordinateSystem.Tests/GisNewDrawingCoordinateSystem.Tests.csproj`: exact source codes, one assignment/readback and exact shared template/profile path selection.
- `Tests/GisNewDrawingAppearance.Tests/GisNewDrawingAppearance.Tests.csproj`: the active appearance/resource comparison rules used by clone verification.
- `Tests/StormStructureMatching.Tests/StormStructureMatching.Tests.csproj`: existing independent storm preparation regressions.

Build the plugin against the installed Civil 3D 2026/Map assemblies. Native acceptance should verify that both supported source codes produce an active unsaved Blank drawing with matching units/CRS and preserved copied geometry, attributes, dynamic state and OD; no SDF dialog or import is launched; the correct IPF path is printed; and setup failure, repeated invocation and unavailable-template handling preserve the source. Manual MAPIMPORT and its results remain a separate user operation.
