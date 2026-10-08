# Create GIS Drawing

`CLV-GIS-NEW-DRAWING` (Q2 > GIS > DATA > **CREATE GIS DRAWING**) prepares a separate drawing for **manual MAPIMPORT**, then prompts for a new DWG filename and saves/reopens it. Run it from the original survey/network drawing. It uses that drawing's assigned coordinate system and automatically finds the supported survey linework and DI/MH blocks; there is no geometry-selection or SDF prompt.

## Workflow

1. Read the original drawing's exact assigned `NV83.NCRS-LVF` or `NV83.NCRS-LVHEF` code and capture the eligible survey objects.
2. Create a new drawing from `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Drawing Templates\Blank (2026).dwt`. Require empty model space.
3. Match the original drawing's insertion units and assign its same coordinate-system code once, then verify readback.
4. Clone the eligible geometry and dependencies at native coordinates. Verify geometry, attributes, dynamic state, attached Object Data and mapped definitions; incompatible resource collisions stop setup.
5. Choose a folder and **new `.dwg` filename** in the save prompt. A missing extension is filled in. Existing files, the source/shared template, and open drawing paths are refused. Cancel leaves the verified prepared drawing open and unsaved.
6. Save a full copy and verify that the resulting DWG is readable and retains its setup marker, model-space objects and units. Only then close the prepared document and reopen the saved file. The reopened drawing is made active and its setup/CRS checked. The exact saved path and matching IPF path are printed.
7. In the reopened GIS drawing, run **MAPIMPORT**, select the exported network SDF, and load the matching profile shown by the tool. Save the drawing again after completing the manual import. Run the existing GIS preparation command after the network import.

The command does not choose, open, inspect or import an SDF. It does not load or modify an IPF. The shared template and source drawing remain unchanged. A save requires the user's filename choice; existing files are never replaced. Publication uses a non-overwriting file move after saving a uniquely named temporary full copy in the chosen folder, so a newly occupied target also causes a safe failure.

## Matching manual import profile

Profile folder: `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\SDF to SHP`

- `NV83.NCRS-LVF`: `UFLS-IMPORT-NV83.NCRS-LVF.ipf`
- `NV83.NCRS-LVHEF`: `UFLS-IMPORT-NV83.NCRS.LVHEF.ipf`

The period before `LVHEF` is intentional. Missing or unsupported source assignments stop before creating a drawing; no alias or default is substituted. Profile selection here only displays the path for the user's manual import. Its file availability, settings and resulting network data are not certified by setup completion.

## Preservation and failure behavior

The copy path retains its existing source snapshots, dependency checks, geometry/attribute/dynamic-state/OD readback, and native IdMapping verification. No explode-first, COPYBASE transform, or appearance fallback is introduced. Built-in records such as Continuous, ByLayer, ByBlock, layer 0 and Standard are reused only when their checked rendering properties agree. Matching handles in separate databases are not identity evidence.

If drawing creation, assignment, copying or setup verification fails, the command attempts to discard only its freshly created incomplete drawing and restore the source context. The cause and any cleanup error remain visible in the surviving editor. If disposal fails, the remaining drawing is explicitly reported incomplete and must be closed without saving. After the command returns its manual-import handoff, a later MAPIMPORT operation does not trigger this command's discard logic.

The completed setup is retained before the save prompt. Cancel, a rejected filename, a save error or failed disk verification never closes that prepared drawing. If closing or reopening fails after a verified save, the saved file remains at the reported path and can be opened manually. Old document/database/ObjectId wrappers are not reused after close. A failed temporary save or move can leave a temporary file; its path is reported, and the still-open prepared drawing remains the working copy. The tool never deletes a saved output during error recovery.

After verifying the copied data, the command marks only those destination entities graphics-modified and queues their graphics inside a destination transaction. It commits and releases the document lock before REGEN and UpdateScreen. This follows the existing storm-preparation graphics sequence. Entity visibility, layer on/frozen/locked state, transparency and dynamic properties remain unchanged. A graphics-only failure is reported as a display warning while retaining the verified unsaved drawing; layer locks are never overridden for refresh. Autodesk documents [graphics modification on entity close](https://help.autodesk.com/cloudhelp/2026/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_Entity_RecordGraphicsModified__MarshalAsUnmanagedType_U1__bool.html) and [queuing modified transaction-resident entities](https://help.autodesk.com/cloudhelp/2026/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_TransactionManager_QueueForGraphicsFlush.html).

The destination-only `CLV_GIS_NEW_DRAWING_V2` Xrecord records `SETUP_READY_MANUAL_IMPORT`. It marks verified drawing setup, not verified network import. Existing conversion commands do not interpret that record.

## Why automatic import is paused

The final automatic trial, commit `c15fbce02ce4a18b11e07a1033f4f3b3071df28f`, passed the source/Blank/CRS/copy stages and then stopped before calling Import. The exact failing field was `Civil_Schema:Pipes.Name`: the native `ColumnDataMapping` getter returned `Name` while leaving its `ImportDataMapping` enum output unwritten. Because Name was a required mapped field, verification correctly refused to guess a mode. The incomplete destination was discarded, no output was saved, and the source remained unchanged.

On 2026-10-08 the user directed that automatic-import debugging stop and requested this setup-only workflow. The automatic-import adapter, SDF reader, IPF parser, runtime OD planner, native-output helper, and their obsolete test projects/fixtures have been removed from the active code. The prior automatic version remains recoverable through Git history and recovery refs. The active clone, appearance, coordinate-system and resource-selection helpers remain.

## Validation

The user ran setup-only commit `16e46b4`: it reported 42 copied/verified survey objects and an active unsaved drawing with the source CRS. The objects were initially invisible but appeared after saving, closing and reopening; this is the user's corrected observation. Explicit graphics registration in `c5f36c9` still did not resolve initial normal display, although selection highlighted the objects. The user then requested this save/reopen workflow as the practical workaround. The underlying display cause has not been established.

The save/reopen change has static checks and focused path-policy tests. Executable tests, the full plugin build and the automated save/reopen lifecycle remain unrun in the authoring environment because the .NET SDK and Autodesk runtime are unavailable. Native acceptance must cover successful new-file save/reopen and visible geometry, cancel, protected/existing/invalid paths, disk-save failure, failed verification and failed reopen. The source must remain unchanged in every case. Autodesk documents the [full-copy SaveAs option](https://help.autodesk.com/cloudhelp/2026/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_Database_SaveAs_string__MarshalAsUnmanagedType_U1__bool_DwgVersion_Autodesk_AutoCAD_DatabaseServices_SecurityParameters.html) and [native save-file prompt](https://help.autodesk.com/cloudhelp/2026/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_EditorInput_Editor_GetFileNameForSave_PromptSaveFileOptions.html).

Retained relevant tests:

- `Tests/GisNewDrawingCoordinateSystem.Tests/GisNewDrawingCoordinateSystem.Tests.csproj`: exact source codes, one assignment/readback, shared template/profile selection and new-DWG path protection.
- `Tests/GisNewDrawingAppearance.Tests/GisNewDrawingAppearance.Tests.csproj`: the active appearance/resource comparison rules used by clone verification.
- `Tests/StormStructureMatching.Tests/StormStructureMatching.Tests.csproj`: existing independent storm preparation regressions.

Build the plugin against the installed Civil 3D 2026/Map assemblies. Native acceptance should verify that both supported source codes produce a separate drawing with matching units/CRS and preserved copied geometry, attributes, dynamic state and OD; no SDF dialog or import is launched; the correct IPF path is printed; and setup/save/reopen failure, repeated invocation and unavailable-template handling preserve the source. Manual MAPIMPORT and its results remain a separate user operation.
