## 2026-07-22 - Legal Description Builder Phases 1-2
- Added `LegalDescription/` with separated geometry, text-generation, drawing-storage, palette, and command modules.
- `LEGALDESC` selects connected LINE/ARC geometry, orders the traverse, calculates line/curve calls, reports forward closure and reverse-direction build results, and opens an editable WinForms PaletteSet.
- The course grid supports include/exclude, prefix, context, suffix, per-course geometry override, source highlighting, zoom, precision controls, and full traverse reversal.
- Sessions and edited final text are stored in the drawing Named Objects Dictionary and can be reopened with `LEGALDESC-OPEN`.
- Q4 > MAPPING > TOOLS includes `LEGAL DESCRIPTION`.
- Legal-description POC prompt registers `Same` and `seParate` as the actual keyword tokens so the `S` and `P` accelerators are valid.
- See `Docs/LEGAL_DESCRIPTION_PHASE_1_2_README.md` for current scope and limitations.

## 2026-07-21 Q4 Mapping Menu Layout
- `Survey/SurveyPalette.cs` organizes the MAPPING tab as `TRANSFORM`, `TOOLS`, `BOUNDARY`, and `SUBDIVISION LINEWORK`.
- `TRANSFORM`: `MAP TRANSFORM` (`SURVEY-BESTFIT-MAP`), `BOUNDARY TRANSFORM` (`SURVEY-TRANSFORM-CONTROL`), and `C3D TRANSFORM` (`ADETRANSFORM`).
- `TOOLS`: `DRAW TIE LINE`, `PDF VIEWER`, `PDF CLIP`, `LEGAL DESCRIPTION`, `XREF COLOR`, `OFFSET TO TEMP LAYER`, and `LINEWORK REVIEW`.
- Boundary closure commands are grouped under `BOUNDARY`; `LINEWORK REVIEW` is the final item in `TOOLS`, and there is no separate `BOUNDARY REVIEW` heading.

## 2026-07-20 - Q4 boundary import removal
- `Survey/SurveyPalette.cs`: removed `BOUNDARY IMPORT AUTO`, `BOUNDARY IMPORT MANUAL`, `ROADS IMPORT`, and `EASEMENT IMPORT` from Q4 > MAPPING > BOUNDARY.
- Removed `Shared/BoundaryCsvImportCommands.cs`, `Shared/RoadsCsvImportCommands.cs`, and `Shared/EasementCsvImportCommands.cs`; their command aliases are no longer compiled or available from the command line.
- Removed the importer-only README, prompt, and sample CSV files that supported those retired workflows.
- No LISP routines were added or changed.

## 2026-07-15 - Survey transform and temporary offset commands
- `Survey/SurveyTransformOffsetCommands.cs`: new Q4 mapping command module. `SURVEY-TRANSFORM-CONTROL` moves selected entities from a source circle center to a destination circle center, displays that translated placement as a temporary preview, then rotates them by matching source and destination Line directions. Canceling during rotation selection restores the original placement. Control circles/lines may be selected directly or nested inside blocks/xrefs. `SURVEY-OFFSET-TEMP` repeatedly creates OFFSET-style curve copies on `V-CONS-LINE-TEMP` until the user presses Enter.
- `Survey/SurveyPalette.cs`: Q4 > MAPPING now uses `TRANSFORM`, `TOOLS`, `BOUNDARY`, and `SUBDIVISION LINEWORK` sections.
- `Survey/SurveyTransformOffsetCommands.cs`: `BOUNDARY TRANSFORM` uses source circle + source line and destination circle + destination line, then shows a faded final-position preview with interactive 180-degree Flip/accept/cancel handling. `OFFSET TO TEMP LAYER` repeats for multiple source objects.
- No LISP routines were added; deployment remains entirely in the managed .NET assembly.

## 2026-07-13 - Q4 menu reorganization + distance-only line/curve labels
- `Survey/SurveyPalette.cs`: Q4 now contains `LABEL`, `MAPPING`, and new `GIS` tabs. `TOWNSHIP/RANGE` and `SECTION CORNER MARKER` moved from MAPPING to GIS.
- Q4 > LABEL: renamed `LABEL ROADS` to `STREET NAMES`; renamed the existing line/curve buttons to `2-POINT  ||  BEARING AND DIST` and `BEARING AND DIST`; added `2-POINT  ||  DIST` and `DISTANCE`; and moved `AREA SF LABEL` into a new `AREA` section immediately below `LINES AND CURVES`.
- Q4 > MAPPING now uses `TRANSFORM`, `TOOLS`, `BOUNDARY`, and `SUBDIVISION LINEWORK`; boundary closure and review commands are grouped under the single `BOUNDARY` section.
- `Survey/SurveyDimensions.cs`: added `SURVEY-LC-LABEL-2POINT-DIST` and `SURVEY-LC-LABEL-DISTANCE`. Both reuse the existing Civil 3D native label workflows and explicitly assign `R26_Distance` after placement. Existing bearing-and-distance wrappers explicitly assign `R26_Bearing + Distance`.

## 2026-07-13 - Q4 boundary cleanup + AUTO CLOSURE no-copy correction
- `Survey/SurveyPalette.cs`: removed `BOUNDARY ONLY IMPORT` from Q4 > MAPPING > BOUNDARY; the legacy import commands remain compiled for command-line compatibility.
- `Survey/SurveyAutoClosure.cs`: changed the final prompt from an apply/cancel gate into an original-reference choice. Selecting `No` now performs the closure, replaces the selected linework on its original layer, and retains no original copy. Selecting `Yes` continues to move originals to `V-SURV-MAP~-ORIG` and create adjusted geometry on `V-SURV-MAP~-ADJ~`.
- No new LISP routines were created.

# CLV_CivilTools PROJECT_MAP

## 2026-06-18 - Drop Inlet Maintenance Note
- `Ufls/UflsDropInlet.cs`: `UFLS-DROP-INLET` still inserts drop inlet blocks from the shared Survey blocks folder and places `UFLS_DI_MARK` at `DI_CENTER`. The loader now guards older drawings by detecting stale inlet block definitions that lack `DI_CENTER`, renaming the stale in-drawing definition to a `_CLV_OLD_yyyyMMdd_HHmmss` backup name, and importing the current server block before placing the new reference.


## 2026-06-15 - GIS reference layer split + menu label cleanup
- `Gis/GisReferenceLayers.cs`: Q2 GIS coordinate-zone load/unload now targets only `NV83.NCRS-LVF.layer` and `NV83.NCRS-LVHEF.layer`. Legacy commands `CLV-GIS-LOAD-REFERENCE-LAYERS` and `CLV-GIS-UNLOAD-REFERENCE-LAYERS` remain available, but they no longer load or unload `CLV_Sections`.
- `Gis/GisSectionReferenceLayers.cs`: new standalone section reference command set with `CLV-GIS-DISPLAY-SECTIONS` and `CLV-GIS-UNLOAD-SECTIONS` for `CLV_Sections.layer`.
- `Gis/GisPalette.cs`: Q2 GIS section label changed to `SECTION/COORDINATE SYSTEM`; buttons changed to `DISPLAY COORDINATE ZONES`, `UNLOAD COORDINATE ZONES`, `DISPLAY SECTIONS`, and `UNLOAD SECTIONS`.
- `Survey/SurveyPalette.cs`: Q4 Survey > Mapping label changed from `SECTION` to `TOWNSHIP/RANGE`, and button label changed from `GIS SECTION MARKER` to `SECTION CORNER MARKER` while preserving the existing `SURVEY-GIS-SECTION-MARKER` command.

## 2026-05-21 - CLVHELP Knowledge Base command
- Added `Help/ClvHelpCommands.cs` with the command-line command `CLVHELP`.
- `CLVHELP` opens the shared Knowledge Base homepage at `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\CLV CAD_KNOWLEDGE_BASE\index.html` using the user's default browser.
- No palette routing, geometry logic, GIS processing, UFLS routines, Survey routines, or LISP helper files were changed.

## 2026-05-20 - CAD palette compact standard applied to Q1-Q4
- Applied the compact Civil 3D palette standard to the in-CAD Q palettes so they use smaller, ribbon-like text and take less drawing-area real estate on 1920 x 1200 workstations.
- Current standard for new command-launcher palettes: 7.0 pt button/section text, 256 px command rows, 24 px row height, vertical scrolling enabled inside tabs, and no horizontal scrolling.
- Default/minimum palette sizes now use: Q1/UFLS `360 x 760` / `320 x 560`; Q2/GIS `340 x 700` / `300 x 540`; Q3/PCT `320 x 600` / `280 x 480`; Q4/SURVEY `340 x 700` / `300 x 540`; GIS CREATE DATA `340 x 700` / `300 x 540`.
- Updated `Ufls/UflsPalette.cs`, `Gis/GisPalette.cs`, `Gis/GisCreateDataPalette.cs`, `PoinCloud/PctPalette.cs`, and `Survey/SurveyPalette.cs`; command routing and tool behavior were not changed.

## 2026-05-20 - Q1 palette compact sizing + reduced font test
- Updated `Ufls/UflsPalette.cs` so the Q1/UFLS palette opens at `360 x 760` with a smaller `320 x 560` minimum instead of being locked to the previous larger `432 x 900` size.
- Reduced Q1 button/row width from `360` to `256` and row height from `28` to `24` so the palette behaves as a compact Civil 3D command launcher on 1920 x 1200 workstations.
- Reduced explicit Q1 button/section font sizing from 9 pt through 7.5 pt to 7.0 pt so the palette text is closer to AutoCAD ribbon text.
- Retained the existing CHECK / ADJUST / LABELS tabs, command tags, and vertical scrolling behavior; no command routing or UFLS/GIS geometry logic was changed.


## 2026-05-19 - Survey label roads height and boundary cleanup
- Updated `Survey/SurveyRoadLabels.cs` so post-`MAPLABEL2ANN` cleanup now erases newly converted road-label text whose label reference point falls outside the selected/drawn boundary. This restores boundary-only output when the visible map layer includes labels beyond the user-selected area.
- Road label text height is now forced to a `0.14` paper height by multiplying the current annotation scale factor into the stored model text height. Example: at `1" = 40'`, the resulting model height is `5.6`, so Properties should report paper text height `0.14`.
- Temporary assessor street-centerline map layers/connections are still always removed after conversion or cancellation.
- No LISP helpers were added or changed.

# CLV_CivilTools PROJECT_MAP

## Current documentation note - 2026-05-05
- Restored detailed project-map content from the `Logs.zip` backup.
- Project-local `Reference/`, `bin/`, and `obj/` folders are intentionally excluded from the source package.
- LISP helpers are expected to load from the shared server LISP folder only: `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\...`.
- The retired Map 3D profile-based pipe-network export/check source file is not part of the active project structure.

- Updated `Ufls/UflsManholeAutoCreate.cs` so `1P MANHOLE - ALL` now loads and places the `UFLS_MH_MARK` center marker consistently with the 3P all and 1P single workflows.

## 2026-05-06 - Q4 survey auto-closure Phase 2
- Updated `Survey/SurveyAutoClosure.cs` with start-point selection, expanded closure QA reporting, and original/adjusted overlay output layers for paper-plan survey recreation.
- Updated `Shared/LayerStandards.cs` with managed layer definitions for `V-SURV-MAP~-ORIG` and `V-SURV-MAP~-ADJ~`; Q4 > MAPPING now has `SURVEY MAP`, `BOUNDARY`, and `REVIEW` sections; `AUTO CLOSURE` is under `BOUNDARY`.
- Phase 2 supports multiple standalone `LINE`/`ARC` entities or one open lightweight `POLYLINE` containing straight and bulged arc segments. Lightweight-polyline bulges are preserved on the adjusted overlay so curve geometry remains tangent relative to adjoining polyline segments where the source geometry permits.
- The command reports existing misclosure, total traverse length, max segment length change, and max bearing change before requiring user confirmation.
## 2026-03-25 - Q1 1P / 3P manhole split
- Updated `Ufls/UflsPalette.cs` so Q1 > CHECK > 2D LINEWORK separates the manhole tools into `3P MANHOLE - ALL`, `3P MANHOLE - SINGLE`, `1P MANHOLE - ALL`, and `1P MANHOLE - SINGLE`.
- Updated `Ufls/UflsManholeAutoCreate.cs` with `UFLS41P` / `UFLS-MH-AUTO-1P` and palette routing support for single-point manhole auto-create workflows.
- Updated `Ufls/UflsSingleManhole.cs` with `UFLS61P` for one-point manhole insertion from a selected COGO center point.
- Updated command / palette documentation for the new 1P workflows.

- `Gis/GisSurveyCacheFinalize.cs`: adds `CLV-GIS-FINALIZE-STRC` and `CLV-GIS-FINALIZE-PIPES` for compare → visualize → confirm → finalize survey-cache export. The workflow auto-detects sewer vs storm from finalized layers, resolves the drawing coordinate system through the existing GIS coordinate-system detector, attaches the matching survey-cache structure + pipe DWGs as gray preview xrefs (ACI 251), marks exact duplicates / nearby conflicts in CAD, prompts for confirmation, then appends the current finalized entities into the matching server cache DWG.
﻿- `Gis/GisSewerManhole.cs`: sewer GIS manhole conversion commands for single-point and all-points workflows. Handles nearby block explode, sewer structure layer migration, and queued OD transfer to `C-SSWR-STRC-E`.
- `Gis/GisSewerPipeOdOffset.cs`: sewer GIS pipe wrapper commands that load the server-hosted sewer ADE/LISP helper for selected or all-pipe processing.
- `Gis/GisSewerGisCommands.cs`: sewer GIS batch wrapper that runs sewer manholes first, then sewer pipes.
- `CLV_GIS_SSWR_PIPE_OD_OFFSET.lsp`: server-hosted sewer ADE/LISP helper expected at `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\CLV_GIS_SSWR_PIPE_OD_OFFSET.lsp`; reads `InsideDiameter`, handles the under-12" no-offset rule, creates sewer wall offsets for 12"+ pipes, and adjusts created wall endpoints to `C-SSWR-STRC-INNR` when possible.
- `Ufls/UflsPalette.cs`: Q1 > GIS now includes STORM DRAIN, SEWER, CLEANUP, and EXPORT sections. The new EXPORT section exposes `FINALIZE STRUCTURES` and `FINALIZE PIPES`, while the SEWER section routes `GIS PREP - ALL`, `MANHOLE`, and `PIPE` to the live sewer GIS commands.
- `CLV_CivilTools.csproj`: post-build now copies only the DLL beside the dev build for local testing; the sewer pipe helper LSP is expected at the server LISP path.


- `CLV_GIS_PIPE_OD_OFFSET.lsp`: server-hosted storm ADE/LISP helper expected at `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\CLV_GIS_PIPE_OD_OFFSET.lsp`; reads `InsideDiameter`, creates storm pipe wall offsets, and adjusts created wall endpoints to `C-STRM-STRC-INNR` when possible.

- `CLV_GIS_PIPE_OD_OFFSET.lsp`: server-hosted storm ADE/LISP helper expected at `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\CLV_GIS_PIPE_OD_OFFSET.lsp`; reads `InsideDiameter`, creates storm pipe wall offsets, and adjusts created wall endpoints to `C-STRM-STRC-INNR` when possible.
- `Gis/GisPipeConnectStorm.cs`: `CLV-GIS-PIPE-CONNECT-STRM` prompts for storm pipe wall linework plus structure inner-wall polylines, moves qualifying pipe endpoints onto the selected structure inner walls, and standardizes layers to `C-STRM-PIPE-E` / `C-STRM-STRC-INNR`.
- `CLV_GIS_PIPE_OD_OFFSET.lsp`: server-hosted storm ADE/LISP helper expected at `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\CLV_GIS_PIPE_OD_OFFSET.lsp`; reads `InsideDiameter`, creates storm pipe wall offsets, and adjusts created wall endpoints to `C-STRM-STRC-INNR` when possible.
﻿## 2026-03-18 - survey-map best-fit registration prototype

- Added `Survey/SurveyBestFitMap.cs` with `SURVEY-BESTFIT-MAP`, a rigid 2D best-fit registration routine for survey-map overlays.
- The workflow lets the user select an xref / block reference to move, then collect numbered survey-shot to map-point control pairs.
- The fit solves translation + rotation only in XY (no scaling), applies the solution to the selected block reference, and writes a CSV residual report for QA / testing.
- Added `Survey/SurveyPalette.cs` with a dedicated `Q4` survey-mapping palette and a `BEST FIT MAP` button.

## 2026-03-11 - palette width standardization + Q1 rename

- Standardized palette widths across Q1 / Q3 / Q2 so the UI is visually uniform and `TRANSPARENCY` remains on one line.
- Widened shared button rows in the Q3 point-cloud palette and centered all single-button captions across the palettes.
- Renamed the UFLS palette command back to `UFLS.Q1` and updated the palette title from `UFLS – CHECK / ADJUST` to `UFLS`.
- Renamed the Q1 layers tab from `LAYER MAINTENANCE` to `LAYERS`.

﻿## 2026-03-11 - Q11 layer maintenance + palette cleanup
- Added `Ufls/UflsLayerMaintenance.cs` for `.NET` replacements of `MERGE-STRC` and `LayerStatesUpdate`.
- Added `Ufls/UflsRedlineBlocks.cs` for Q11 redline `NOTE` and `LEADER` block insert/explode commands.
- Updated Q11 CHECK and Q3 UFLS palette layouts / captions per current field workflow.
- Updated `UFLS-DROP-INLET` source-form labels to `4 POINT` and `2 POINT`.

# CLV_CivilTools PROJECT_MAP

------------------------------------------------------------
PROJECT OVERVIEW
------------------------------------------------------------

- **Primary Host:** AutoCAD 2026 / Civil 3D 2026
- **Project Type:** .NET Class Library loaded into Civil 3D
- **Primary UI Pattern:** WinForms controls hosted inside `Autodesk.AutoCAD.Windows.PaletteSet`
- **Palette Placement Standard:** `Shared/PalettePositionHelper.cs` should be used by new PaletteSet hosts. Palette size is configured during creation. On the first show of each PaletteSet in the current Civil 3D session, the helper shows the palette, then repositions it relative to the current AutoCAD main window so stale persisted coordinates from another monitor do not win. After that first session placement, repeated command calls do not reset location, allowing the user to move the palette once and reopen it at that session location. Q1/Q2/Q3/Q4 first-open offsets are intentionally staggered so menu openings are visible instead of fully overlapping.
- **Current Focus Areas:**
  - GIS / aerial imagery loading
  - Point-cloud roadway and UFLS support tools
  - UFLS linework, structure, pipe, and adjustment workflows
  - Survey mapping / overlay alignment tools
- **Current Maintenance Note:** `Survey/SurveyPhotoReview.cs` hosts the `VIEWPHOTOS` / `VIEW-PHOTOS` / `CLV-PHOTO-REVIEW` palette. The embedded photo map preview reads GPS EXIF from the linked image and downloads OpenStreetMap/Esri tiles through TLS/proxy-aware HTTP requests with tile-host retry; `OPEN MAP` continues to launch the external Google Maps pin.
- **Design Direction:** replace selected legacy LISP workflows with native .NET commands while keeping the existing palette-driven workflow familiar.
- **Current Maintenance Note:** `Ufls/UflsTopOfPipe.cs` preserves the Civil-style plan-view best-fit alignment for `UFLS1`, reverses the station/elevation slope when the fitted direction is flipped to match the user's ordered COGO picks, and now checks selected top-of-pipe shots for adjacent-segment grade breaks before creating the final 3D polyline. The grade-break review flags picked points where slope-in to slope-out changes are at least 0.5 percentage points; when no break is found, UFLS1 continues without any extra prompt.
- **Current Maintenance Note:** `Ufls/UflsLaterals.cs` now supports both SINGLE and ALL lateral creation. `UFLS-LATERAL-SINGLE` keeps the ordered COGO-pick workflow, while `UFLS-LATERAL-ALL` finds WYE shots by description, builds a search line perpendicular to the nearest main centerline, prompts for a user-defined search half-width and search half-length and gathers shots within that band over the chosen run, auto-creates one-sided lateral groups, and sends two-sided / ambiguous groups to aligned QA polygons on `V-SURV-RDLN` for manual review. Both workflows connect to the nearest main on `V-SURV-PIPE-CNTR`, add the lateral directly into the existing `-SSWR-E` sewer network, and do not split the main pipe.
- **Current Maintenance Note:** `Ufls/UflsAdjustCommands.cs` is now active. It provides live Q11 ADJUST commands for sewer-main manhole / pipe movement plus a new `UFLS-STUB` locator-marker command used for pipe stubs. Current maintenance keeps the earlier structure-move logic, adds `MH - ALL` / `PIPE - ALL` aliases for the Q11 workflow, includes both `UFLS_MH_MARK` and `UFLS_STUB_MARK` in the manhole-adjust search, and allows pipe-adjust commands to read surveyed LINE or 3D POLYLINE centerlines from `V-SURV-PIPE-CNTR`.
- **Current Maintenance Note:** `Ufls/UflsPipeMaterialSwap.cs` adds four Q1 ADJUST material-swap commands now grouped under `SWAP MATERIAL` at the bottom of the ADJUST tab. The workflow reads the selected Civil 3D pipe family and nominal size, resolves the matching target family from the active network parts list, and swaps to the same nominal size, with a reflected `AddPartSize(...)` attempt when the target size is not already exposed in the drawing parts list.
- **Current Maintenance Note:** `Survey/SurveyBestFitMap.cs` provides Q4 MAPPING > `TRANSFORM` > `MAP TRANSFORM`. `SURVEY-BESTFIT-MAP` collects fixed Survey-to-moving Map pairs for a rigid XY fit (move + rotate only, no scale), then persists the original block/xref placement and editable pair/control state in `CLV_MAP_TRANSFORM_SESSION`. Re-running the command on the same target recalls the session; the review grid supports CONTROL toggles, `Remove Selected`, and `Add Pair`, and `Apply Updated Transform` always rebuilds placement from the saved original state to avoid cumulative transforms. When a saved session is recalled, the map is temporarily returned to its original pre-transform placement for editing and the numbered Survey/Map pair markers are recreated so `Add Pair` uses the same coordinate basis and visual grouping as the initial workflow. Cancel restores the prior map placement. Each application writes a CSV residual report for QA.
- **Current Maintenance Note:** `Survey/SurveyLegendCommands.cs` adds the Q4 > LABEL > `LEGEND` workflow. `SURVEY-CREATE-LEGEND` reads `Reference/SurveyLegend.csv`, presents grouped checkbox sections for LINEWORK / SYMBOLS / ABBREVIATIONS / CONSTRUCTION NOTES, inserts `SURV_LEG_HEADER`, and stacks selected prebuilt row blocks from `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\Blocks\Survey\Legend` on `G-BRDR-ANNO` using the configured header-to-first and single/double item spacing. `SURVEY-UPDATE-LEGEND` stores generated rows as an AutoCAD group with XData metadata so selecting the legend header can reopen and rebuild the same legend later.
- **Current Maintenance Note:** `Survey/SurveyPalette.cs` adds a dedicated Q4 survey-mapping palette so survey registration workflows stay separate from UFLS underground tools. Q4 > MAPPING now includes `SURVEY MAP` with `BEST FIT MAP`, `BOUNDARY` with Phase 3 `AUTO CLOSURE` plus in-session constraint buttons, and `REVIEW` with xref color comparison plus the modelspace closure report / markers / goto tools.
- **Current Maintenance Note:** `Survey/SurveyXrefColorCommands.cs` adds `SURVEY-XREF-COLOR` / `XREFCOLOR` under Q4 > MAPPING > TOOLS. It opens a WinForms color-choice dialog with a two-column layout: standard Red, Yellow, Green, Cyan, Magenta, and Gray ACI 252 on the left, and the matching 70% transparent versions on the right. Color choices apply host-drawing xref layer overrides to the selected attached/overlaid xref's dependent layers. RESET reads the selected source xref DWG and restores dependent layer colors/transparency to the source values; RESET ALL restores layer color/transparency overrides for every attached/overlaid xref that can be resolved from the host drawing. The source DWGs are not modified.
- **2026-06-29 Layout Fix:** XREF COLOR dialog now uses explicit row heights/taller dialog so Cyan, Magenta, and Gray ACI 252 rows display above RESET / RESET ALL.
- **Current Maintenance Note:** `Survey/SurveyAutoClosure.cs` adds `SURVEY-AUTO-CLOSURE`, a LINE/ARC/open-polyline closure assistant that prompts for the traverse start point, solves closure by changing only straight segment lengths so original line bearings are preserved, reports closure bearing / PPM / relative precision / original-adjusted area / worst segment data, and creates an adjusted overlay on `V-SURV-MAP~-ADJ~` while preserving the source linework on `V-SURV-MAP~-ORIG`. Curve segments are translated with original bulge/radius values to keep existing tangent curve relationships intact where the source geometry was tangent. `Survey/SurveyClosureConstraints.cs` adds the Phase 3 WinForms constraint manager for `LOCK RADIUS`, `LOCK BEARING`, `LOCK LENGTH`, and `KEEP PARALLEL` on standalone LINE/ARC runs; selected constraint objects are highlighted while the in-session constraint set is active. `Survey/SurveyClosureReview.cs` provides the modeless report window, same-size numbered review markers, segment goto, boundary area summary, and arc radius / offset comparison columns.

------------------------------------------------------------
ENVIRONMENT / BUILD BASELINE
------------------------------------------------------------

- **Target Framework:** `net8.0-windows`
- **UseWindowsForms:** `true`
- **PlatformTarget:** `x64`
- **Nullable:** `enable`
- **Implicit Usings:** `enable`
- **SDK:** `Microsoft.NET.Sdk`

**Core AutoCAD / Civil References**
- `accoremgd.dll`
- `acdbmgd.dll`
- `acmgd.dll`
- `AeccDbMgd.dll`
- `Autodesk.Map.Platform.dll`
- `OSGeo.MapGuide.PlatformBase.dll`
- `OSGeo.MapGuide.Foundation.dll`

**Reference Handling Standard**
- `Private=False` / Copy Local disabled for Autodesk host assemblies.

**Post-Build Behavior**
- Copies the built DLL to `C:\Temp\C3DDev\` with a randomized filename suffix for dev-side loading.

**Known Project Notes**
- `Aec/AecNetworkUtils.cs` exists in the tree but is currently excluded from compilation by the project file.
- The source tree keeps the existing folder name `PoinCloud` intentionally as-is in this snapshot.
- Project is intentionally WinForms-based; no WPF conversion.
- Current maintenance pass explicitly addressed nullable warnings in the GIS / Shared / UFLS areas.
- `UFLS7` / `UFLS8` point collection now allows `Enter` to finish cleanly via `PromptPointOptions.AllowNone = true`.
- The `WindowsBase` assembly-conflict build warning is suppressed at the project level (`MSB3277`) because it is introduced by Autodesk host assemblies versus the .NET 8 reference pack, not by a user-code defect.

------------------------------------------------------------
FOLDER / CODE STRUCTURE
------------------------------------------------------------

```text
CLV_CivilTools/
  Aec/
    AecNetworkUtils.cs              (present, currently excluded from build)
  BlockUtils/
    (folder placeholder)
  Gis/
    Aerials.cs
    GisImport.cs
    GisPalette.cs
  PoinCloud/
    PctCommands.cs
    PctPalette.cs
  Shared/
    GeometryUtils.cs
    PalettePositionHelper.cs
    LayerState.cs
    PipeCatalog.cs
    PointCloudUtils.cs
    SelectionUtils.cs
    SnapState.cs
    ViewState.cs
  Survey/
    SurveyBestFitMap.cs
    SurveyLegendCommands.cs
    SurveyPalette.cs
    SurveyXrefColorCommands.cs
  Ufls/
    UflsDropInlet.cs
    UflsPipeMaterialSwap.cs
    UflsManholeAutoCreate.cs
    UflsPalette.cs
    UflsSingleManhole.cs
    UflsStructure.cs
    UflsTopOfPipe.cs
  PROJECT_MAP.md
  COMMAND_INDEX.md
  CHANGELOG.md
  CLV_CivilTools.csproj
```

------------------------------------------------------------
SOURCE FILE RESPONSIBILITIES
------------------------------------------------------------

**Aec/AecNetworkUtils.cs**
- AEC / pipe-network helper scaffold.
- Present in the repository but currently excluded from compile in this snapshot.

**Gis/Aerials.cs**
- Handles Nearmap / aerial `.layer` loading.
- Unloads aerial imagery layers and now attempts to remove matching Nearmap / LasVegas map connections from the current session.
- Supports sorted aerial list handling.
- Includes nullable-safe active-document and saved-view handling.


**Gis/GisImport.cs**
- `CLV-GIS-IMPORT` cache-first GIS import for CLV parcel / roadway / sewer / storm datasets. The import dialog no longer shows the cache note text and now defaults Sewer Pipes, Sewer Structures, Storm Pipes, and Storm Structures to unchecked.
- Q2 > GIS > `DATA` > `IMPORT GIS` palette entry point. The import dialog now exposes both GIS datasets and SURVEYED cache datasets.
- Always uses prebuilt dataset DWG cache first when available, then falls back to direct SHP import.
- For structure cache imports, syncs active-drawing `PDMODE` / `PDSIZE` from the source cache DWG so DBPoint display matches the cache definition.
- Uses boundary-extents prechecks during cache clipping to skip unnecessary split operations for obviously inside / outside entities, improving import speed on large datasets.
- Syncs GIS target layers from the optional CLV GIS layer-master DWG, then enforces CLV fallback color / linetype / plot-style settings.
- Supports existing closed-polyline or temporary drawn-polygon boundaries.
- Includes `CLV-GIS-CLEANUP` duplicate-linework cleanup for imported GIS line layers and `CLV-GIS-CACHE-STATUS` for cache / layer-master status.
- Includes `CLV-GIS-OD-INSPECT`, which now proxies to an ADE/LISP helper so command-line OD diagnostics match what the Map 3D Properties palette sees. The helper is resolved only from `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\CLV_GIS_OD_HELPERS.lsp`. No local or project-tree fallback is used.
- `Gis/GisObjectDataTransfer.cs`: wrapper command that loads `CLV_GIS_OD_HELPERS.lsp` from the shared Civil 3D server path and launches the ADE/LISP manual OD transfer workflow so source/destination picks stay inside the helper.

**Gis/GisPalette.cs**
- `Q2` palette host.
- Two tabs: `AERIAL` and `GIS`.
- GIS tab now includes `DATA` > `IMPORT GIS`, coordinate-system reference tools, HTML survey report, and XDATA/cache inspection tools.
- Q1 > GIS now includes `STORM DRAIN`, `SEWER`, `OBJECT DATA`, `CLEANUP`, and `EXPORT`; the new `OBJECT DATA` section exposes `XFER OBJECT DATA` for manual ADE/LISP OD transfer from a selected source object to a selected destination object.
- UI entry point for aerial loading / unload workflow and the cache-first GIS import setup. `OD INSPECT` is no longer exposed on the Q2 palette.
- Includes nullable-safe static palette visibility handling.

**PoinCloud/PctPalette.cs**
- `PCT.Q3` point-cloud palette host with standardized wider palette/button widths and centered single-button captions.
- Two tabs: `ROADWAY` and `UFLS`.
- Establishes the current palette styling standard used as the reference for other palettes.
- Includes the transparency control row below Attach / Intensity.

**PoinCloud/PctCommands.cs**
- Main point-cloud command set.
- Hosts `PCT1` through `PCT20` plus `PCT11I`.
- Covers sample-line creation, crop / uncrop, cross-section views, quick crop, point movement, rotate / reset view, and utility marker helpers.

**Shared/GeometryUtils.cs**
- Shared geometry helpers for point-cloud workflows.

**Shared/PalettePositionHelper.cs**
- Shared WinForms PaletteSet first-open placement helper. Q1/Q2/Q3/Q4-style palettes use it to place newly created floating palettes relative to the current AutoCAD main window, clamp the point to that monitor's working area, and preserve user-moved palette positions after the first open for the current Civil 3D session.
- Tangent / perpendicular vector math.
- Rectangle orientation and projection helpers.

**Shared/LayerState.cs**
- Ensures layers exist.
- Layer on/off helpers.
- Temporary layer cleanup helpers.

**Shared/PipeCatalog.cs**
- Pipe material / size lookup support.
- Dynamic block visibility names and radius lookup helpers.

**Shared/PointCloudUtils.cs**
- Point-cloud detection helpers.
- Polyline-to-polygon helpers.
- Crop / uncrop support around `POINTCLOUDCROP` and related workflows.

**Shared/SelectionUtils.cs**
- Selection isolate / unisolate helpers.

**Shared/SnapState.cs**
- Captures and restores object-snap state.
- Used by commands that temporarily override snaps or related drafting settings.
- Converts AutoCAD system variables with null-aware guards.

**Shared/ViewState.cs**
- Named-view save / restore helpers.
- UCS / PLAN helpers.
- Zoom helpers used by view-oriented workflows.


**Ufls/UflsLayerMaintenance.cs**
- Native Q1 / Q11 `LAYERS` command file.
- `MERGE-STRC` / `UFLS-MERGE-STRC` move legacy structure entities from `V-SURV-STRC-INNER-2D` and `V-SURV-STRC-OUTER-2D` into the standard `...INNR...` / `...OUTR...` layers and then attempt to remove the old layers.
- `ReloadLayerStates` / `UFLS-LAYER-STATES-UPDATE` replace the legacy LayerStatesUpdate LISP flow by deleting and re-importing the `LateralCreatePipe` and `PipeCenter` layer states from the 2026 support folder.

**Ufls/UflsRedlineBlocks.cs**
- Native Q11 redline helper commands.
- `UFLS-REDLINE-NOTE` loads `REDLINE-MTEXT` from `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\Blocks\Survey`, inserts it, and explodes it.
- `UFLS-REDLINE-LEADER` loads `REDLINE-LEADER` from the same survey block folder, inserts it, and explodes it.

**Ufls/UflsPalette.cs**
- `UFLS.Q1` primary UFLS palette host (with `UFLS.Q11` retained as a legacy alias).
- Tabs: `CHECK` and `ADJUST`.
- Hosts wrapper / palette command routing for UFLS workflows.
- Current revision adds `STUB` under `2D LINEWORK`, keeps the newer ADJUST layout grouped as `SEWER MAIN - MOVE`, `SEWER LATERAL`, `STORM JUNCTION STRUCTURE`, `STORM DRAIN - MOVE`, and `SWAP MATERIAL`, and keeps `RESIZE JUNCTION` separated into the dedicated storm-junction section.

**Ufls/UflsTopOfPipe.cs**
- `UFLS1` top-of-pipe workflow.
- Keeps 2D best-fit alignment and station/elevation direction aligned to ordered COGO pick direction so start/end inverts do not swap when the raw PCA direction reverses.
- Checks adjacent picked-shot slopes before final line creation and opens a grade-break review dialog only when a 0.5 percentage-point or greater slope change is detected. The command zooms to the extents of the selected picks, places redline review boxes on `V-SURV-RDLN` around each flagged point, and lets the user create the pipe anyway or cancel for field-shot review.
- Includes auto-pan assist for progressive COGO selection after the first two picks, with temporary guide line creation on `V-TEMP-PIPEPICK`, first-pick zoom reuse, and dynamic search width based on the current view and recent pick spacing.
- `UFLS5` trim top-of-pipe workflow.
- `UFLS-PIPE-LABEL-3D` static start/end invert label workflow for selected 3D polylines with label rotation aligned to the selected pipe.
- Uses structure inner-wall geometry on `V-SURV-STRC-INNR-2D~~` when extending pipe linework.

**Ufls/UflsSingleManhole.cs**
- `UFLS6` create a 3P manhole from 3 COGO points.
- `UFLS61P` create a 1P manhole from 1 COGO center point.
- `UFLS6PC` create manhole from 3 picked / snapped points.
- Handles marker creation, center solving, and survey block insertion.

**Ufls/UflsManholeAutoCreate.cs**
- `UFLS4` 3P manhole auto-create / clustering workflow.
- `UFLS41P` 1P manhole auto-create / clustering workflow.
- Also exposes legacy alias command `UFLS-MH-AUTO`.
- Can be launched from the palette dialog workflow.


**Ufls/UflsAdjustCommands.cs**
- `UFLS-STUB` places a stub locator marker block used for pipe-stub adjustment.
- `UFLS-ADJ-MH-AUTO` / `UFLS-ADJ-MH-ALL` and `UFLS-ADJ-MH-SINGLE` move Civil 3D structures to nearby `UFLS_MH_MARK` and `UFLS_STUB_MARK` locator blocks by writing marker XY directly into the structure position while preserving Z, so stub / null-structure endpoints can be adjusted from the same Q11 workflow.
- `UFLS-ADJ-PIPE-AUTO` / `UFLS-ADJ-PIPE-ALL` and `UFLS-ADJ-PIPE-SINGLE` align Civil 3D pipe geometry to surveyed LINE and 3D POLYLINE centerlines on layer `V-SURV-PIPE-CNTR`.
- `UFLS-ADJ-SD-SINGLE` / `UFLS-ADJ-JNCT-SINGLE` and `UFLS-ADJ-SD-ALL` / `UFLS-ADJ-JNCT-ALL` move storm-drain junction structures to closed inside-wall footprint centers on `V-SURV-STRC-INNR-2D~~`, apply best-fit footprint rotation, and now use a safer closed-polyline center workflow with optional explicit polyline selection for single-mode targeting.
- `UFLS-ADJ-DI-SINGLE` / `UFLS-ADJ-DI-ALL` move drop-inlet structures to `UFLS_DI_MARK` marker locations and apply the marker rotation.

**Survey/SurveyBestFitMap.cs**
- Survey-map rigid-registration tool behind Q4 `MAP TRANSFORM`.
- `SURVEY-BESTFIT-MAP` prompts for an xref / block reference target, then either collects new numbered survey-shot to map-point control pairs or recalls the saved pair session already attached to that target.
- Solves a 2D least-squares rigid transform in XY (translation + rotation only, no scale), applies the transform to the selected block reference, and writes a CSV residual report for QA.
- Finalized sessions persist in the target block reference extension dictionary as `CLV_MAP_TRANSFORM_SESSION`, storing original target placement plus the editable Survey/Map pair list and CONTROL states.
- Recalled sessions reopen the existing WinForms review grid with `Add Pair`, `Remove Selected`, CONTROL checkbox editing, live residual recalculation, and `Apply Updated Transform`.
- Added map selections are converted from the map's current transformed WCS placement back into the original stored map basis before calculation. Reapplication sets the target directly from the stored original insertion/rotation plus the newly solved fit, so repeated edits do not accumulate prior transforms.

**Ufls/UflsPipeMaterialSwap.cs**
- Q1 ADJUST material-swap helpers shown under the `SWAP MATERIAL` section at the bottom of the tab.
- `UFLS-PIPE-PVC-C900` swaps one selected pipe from `CLV_PVC` to the same nominal size in `CLV_C900`.
- `UFLS-PIPE-RCP-C900` swaps one selected pipe from `CLV_RCP` to the same nominal size in `CLV_C900`.
- `UFLS-PIPE-C900-RCP` swaps one selected pipe from `CLV_C900` to the same nominal size in `CLV_RCP`.
- `UFLS-PIPE-C900-PVC` swaps one selected pipe from `CLV_C900` to the same nominal size in `CLV_PVC`.
- Reads the selected pipe family / size from the placed Civil 3D pipe, resolves the target family from the active network parts list, tries to find the same nominal size, and when needed attempts a reflected `AddPartSize(...)` call before swapping the placed pipe.


**Ufls/UflsSdJunctionSizeTest.cs**
- `SD-JUNCTION-SIZE` / `UFLS-SD-JUNCTION-SIZE` test command.
- Q11 CHECK now shows `LABEL INVERT` for `UFLS-PIPE-LABEL-3D`.
- Q1 ADJUST now separates storm commands into `STORM JUNCTION STRUCTURE` for `RESIZE JUNCTION` and `STORM DRAIN - MOVE` for `JNCT - SINGLE`, `JNCT - ALL`, `DI - SINGLE`, `DI - ALL`, `MH - SINGLE`, `MH - ALL`, `PIPE - SINGLE`, and `PIPE - ALL`.
- Reads a selected closed inner-wall polyline and computes best-fit width / length.
- Uses the selected structure's active network `PartsList` and matching `PartFamily` to build a `SizeFilterRecord`, resolve width / length contexts, choose the nearest valid built-in values from the family value lists, add the matching size to the family / parts list when needed, and attempt a swap of the selected structure to that size.
- Intended as a data-reference-safe alternative to direct placed-structure width / length overrides for SD-JUNCTION testing.

**Ufls/UflsDropInlet.cs**
- Native .NET drop-inlet workflow.
- `UFLS-DROP-INLET` command implementation.
- `DI_CENTER_TEST` helper / test command.
- Contains the related WinForms dialogs for inlet type / source collection.
- Updated with safer WinForms `Button.Tag` pattern matching to reduce nullable / unboxing warnings.

**Ufls/UflsStructure.cs**
- Native .NET replacement for the structure-footprint workflow plus related 2D helper tools.
- `UFLS7` = trace the **INSIDE** wall and auto-create the matching **OUTSIDE** wall.
- `UFLS8` = trace the **OUTSIDE** wall and auto-create the matching **INSIDE** wall.
- `UFLS7PC` = point-cloud-assisted inside-wall trace for the Q3 / W3 UFLS tab with `OSNAPZ=0` and `3DOSMODE=128`.
- `UFLS-3PCIRCLE` = 3-point circle on layer `0` at elevation `0.0` with temporary pick markers and snaps OFF.
- `UFLS-3PRECT` = 3-point orthogonal rectangle created as a closed polyline on layer `0` at elevation `0.0` with temporary pick markers and snaps OFF.
- Collects picked points with temporary marker circles so the user can see prior picks.
- `Enter` finishes the footprint point loop cleanly instead of re-prompting with `Invalid point.`
- Uses an enlarged WinForms structure-type / wall-thickness dialog to keep preset buttons and manual thickness input readable.
- Ensures the UFLS structure layers exist and attempts to apply named plot style `M` when available in STB drawings.
- Uses explicit AutoCAD color aliases to avoid `System.Drawing.Color` ambiguity.

------------------------------------------------------------
PALETTE ARCHITECTURE
------------------------------------------------------------

**Q2 — GIS Palette**
- **Command:** `Q2`
- **Title:** `GIS`
- **Tabs:** `AERIAL`, `GIS`
- **Key Actions:** unload aerials, double-click aerial layer file to load.

**Q3 — Point Cloud Palette**
- **Command:** `PCT.Q3`
- **Title:** `CLV-POINT CLOUD TOOLS`
- **Tabs:** `ROADWAY`, `UFLS`
- **Standard Size:** `300 x 900`
- **Minimum Size:** `300 x 900`
- **UFLS tab update:** the UFLS tab now uses uppercase button captions throughout.
- **UFLS tab update:** `PIPE LOCATOR` buttons now read `STEP 1 - CROP POINT CLOUD`, `STEP 2 - ROTATE VIEW`, `STEP 3 - SET UCS`, `STEP 4 - TRACE PIPE`, and `STEP 5 - RESET VIEW`.
- **UFLS tab update:** `2D LINEWORK` now shows `STRC-INNER WALL`, then `LOCATE MANHOLE`, followed by `3P CIRCLE` and `3P RECTANGLE`; `GENERAL MARKER` is centered under `TOOLS`.

**Q1 / Q11 — UFLS Palette**
- **Command:** `UFLS.Q1` (legacy alias: `UFLS.Q11`)
- **Title:** `UFLS`
- **Tabs:** `CHECK`, `ADJUST`, `LAYERS`
- **Standard Size:** `300 x 900`
- **Minimum Size:** `300 x 900`
- **This revision:** CHECK tab now places `3P MANHOLE - ALL`, `3P MANHOLE - SINGLE`, `1P MANHOLE - ALL`, and `1P MANHOLE - SINGLE` at the top of `2D LINEWORK`, then keeps `STRC-INNER WALL` / `STRC-OUTER WALL`, `STUB MARKER`, `3P CIRCLE`, `3P RECTANGLE`, and `NOTE` / `LEADER`.
- **This revision:** ADJUST is organized as `SEWER MAIN - MOVE`, `SEWER LATERAL`, `STORM JUNCTION STRUCTURE`, `STORM DRAIN - MOVE`, and `SWAP MATERIAL`; the `LAYERS` tab still provides `REMOVE DUPLICATE STRC LAYERS` and `UPDATE LAYER STANDARDS`.
- **This revision:** the four one-pick pipe material swap buttons `PVC --> C900`, `RCP --> C900`, `C900 --> RCP`, and `C900 --> PVC` are grouped under the bottom `SWAP MATERIAL` section instead of the move section.

**Palette Layout Standard**
- Root container: `FlowLayoutPanel`
- Flow direction: `TopDown`
- `WrapContents=false`
- `AutoScroll=true`
- `Padding=8`
- Standard button width: `240`
- Standard button height: `28`
- Two-button rows use a `TableLayoutPanel` split `50 / 50`.

------------------------------------------------------------
COMMAND INVENTORY (CURRENT)
------------------------------------------------------------

**Palette / Entry Commands**
- `Q2`
- `PCT.Q3`
- `UFLS.Q11`

**GIS Commands**
- `NM_AERIAL`

**Point Cloud Commands**
- `PCT1`
- `PCT2`
- `PCT2R`
- `PCT3`
- `PCT3R`
- `PCT4`
- `PCT4R`
- `PCT5`
- `PCT6`
- `PCT7`
- `PCT8`
- `PCT9`
- `PCT10`
- `PCT11`
- `PCT11I`
- `PCT12`
- `PCT13`
- `PCT14`
- `PCT15`
- `PCT16`
- `PCT17`
- `PCT17V`
- `PCT18`
- `PCT18R`
- `PCT19`
- `PCT20`

**UFLS Commands**
- `UFLS1`
- `UFLS4`
- `UFLS41P`
- `UFLS5`
- `UFLS6`
- `UFLS61P`
- `UFLS6PC`
- `UFLS7`
- `UFLS8`
- `UFLS-PIPE-PVC-C900`
- `UFLS-PIPE-RCP-C900`
- `UFLS-PIPE-C900-RCP`
- `UFLS-PIPE-C900-PVC`
- `UFLS-MERGE-STRC`
- `MERGE-STRC`
- `UFLS-LAYER-STATES-UPDATE`
- `ReloadLayerStates`
- `UFLS-REDLINE-NOTE`
- `UFLS-REDLINE-LEADER`
- `UFLS-MH-AUTO`
- `UFLS-DROP-INLET`
- `UFLS-REVCLOUD`
- `UFLS-LATERAL-SINGLE`
- `UFLS-LATERAL-ALL`
- `DI_CENTER_TEST`

------------------------------------------------------------
UFLS CHECK TAB BUTTON MAP
------------------------------------------------------------

**VERIFICATION**
- Highlight Red → `HIGHLIGHTRED` (legacy LISP)
- Highlight Green → `HIGHLIGHTGREEN` (legacy LISP)
- Object Highlight Red → `UFLS-OBJECT-HIGHLIGHT-RED`
- Object Highlight Green → `UFLS-OBJECT-HIGHLIGHT-GREEN`
  - Uses existing by-layer transparency from `V-SURV-HGLT-R` / `V-SURV-HGLT-G`; `LayerStandards` creates/updates both highlight layers with 70% layer transparency.
  - Creates 0.01-wide overlay linework for curves/lines and tight rotated background solids. Plain DBText/MText backgrounds are sent behind text; Civil 3D label-like backgrounds are moved to the front so label masks do not hide the highlight.

**REDLINE**
- Revision Cloud… → `UFLS-REVCLOUD`
- NOTE → `UFLS-REDLINE-NOTE`
- LEADER → `UFLS-REDLINE-LEADER`

**2D LINEWORK**
- 3P Manhole - All → `UFLS-MH-DIALOG` → `UflsManholeAutoCreate.RunFromPalette(...)`
- 3P Manhole - Single → `UFLS6`
- 1P Manhole - All → `UFLS-MH1P-DIALOG` → `UflsManholeAutoCreate.RunFromPalette1P(...)`
- 1P Manhole - Single → `UFLS61P`
- STRC-INNER WALL → `UFLS7`
- STRC-OUTER WALL → `UFLS8`
- STUB MARKER → `UFLS-STUB`
- Drop Inlet → `UFLS-DROP-INLET`
- 3P CIRCLE → `UFLS-3PCIRCLE`
- 3P RECTANGLE → `UFLS-3PRECT`

**3D LINEWORK**
- Top of Pipe (UFLS1) → `UFLS1`
- Trim Top of Pipe (UFLS5) → `UFLS5`
- Label Invert → `UFLS-PIPE-LABEL-3D`

**INFO**
- Pipe Info @ Point → `UFLS-PIPE-INFO`

**PIPE NETWORK**
- Lateral - Single → `UFLS-LATERAL-SINGLE`
- Create Lateral - All → `UFLS-LATERAL-ALL`

------------------------------------------------------------
KEY LAYER CONVENTIONS REFERENCED IN CODE
------------------------------------------------------------

**Point Cloud / Roadway Layers**
- `V-PNTC-SAMP`
- `V-PNTC-CROS`
- `V-PNTC-CROP`
- `V-PNTC-PLNM`
- `V-PNTC-CROS-TEMP`
- `V-PNTC-CROP-TEMP`

**UFLS / Utility / Temp Layers**
- `V-CONS-LINE-TEMP`
- `V-SURV-MRKR`
- `V-SURV-PIPE-OUTR`
- `V-SURV-CHCK`
- `V-SURV-STRC-INNR-2D~~`
- `V-SURV-STRC-OUTR-2D~~`
- `V-TEMP-PIPEPICK`
- `V-TEMP-DIPICK`
- `V-TEMP-PICKMARK`
- `V-SURV-HGLT-R` - red UFLS highlight layer, 70% layer transparency when ensured by `LayerStandards`
- `V-SURV-HGLT-G` - green UFLS highlight layer, 70% layer transparency when ensured by `LayerStandards`

**Structure Layer Defaults Introduced / Reinforced in this Revision**
- `V-SURV-STRC-OUTR-2D~~` → color `141`, linetype `CONTINUOUS`, plot style `M` when available
- `V-SURV-STRC-INNR-2D~~` → color `141`, linetype `HIDDEN4`, plot style `M` when available
- `V-TEMP-PICKMARK` → magenta temp marker layer for structure point feedback

------------------------------------------------------------
NAMED VIEW / STATE NOTES
------------------------------------------------------------

**Cross-section workflow views**
- `CROSS-SECTION_SL`
- `CROSS-SECTION_RV`
- `CROSS-SECTION_QV`
- `CROSS-SECTION_MH`

**Roadway orbit views**
- `ROADWAY_ORBIT_VIEW`
- `ROADWAY_ORBIT_VIEW_CLEAR`

------------------------------------------------------------
LEGACY LISP DEPENDENCIES STILL IN PLAY
------------------------------------------------------------

- `HIGHLIGHTRED`
- `HIGHLIGHTGREEN`
- `UFLS-OBJECT-HIGHLIGHT-RED`
- `UFLS-OBJECT-HIGHLIGHT-GREEN`
- `REVCLOUDLARGE`
- `REVCLOUDMEDIUM`
- `REVCLOUDSMALL`
- DLL loader LISP workflow

**Note on structure workflow migration**
- The legacy `STRUCTURE_FOOTPRINT` LISP workflow remains a separate legacy dependency if you still load it externally.
- This project now has native .NET replacements for that footprint workflow via `UFLS7` and `UFLS8`.

------------------------------------------------------------
INSTALL / VALIDATION CHECKLIST
------------------------------------------------------------

1. Confirm the loader still points to the correct DLL output location.
2. Confirm `Q2`, `PCT.Q3`, and `UFLS.Q1` (plus legacy alias `UFLS.Q11`) open their palettes successfully.
3. Confirm `UFLS7` traces the inner wall and auto-builds the outer wall on the expected layers.
4. Confirm `UFLS8` traces the outer wall and auto-builds the inner wall on the expected layers.
5. In an STB drawing, confirm the structure layers pick up named plot style `M` when that style exists.
6. Confirm `UFLS1` still recognizes inner structure wall geometry on `V-SURV-STRC-INNR-2D~~`.
7. Confirm aerial unload / load flow still works from the `Q2` palette.

------------------------------------------------------------
NOTES / DESIGN RULES
------------------------------------------------------------

- Keep the WinForms `PaletteSet` approach.
- Keep `net8.0-windows`, `x64`, and `nullable enable`.
- Explicitly avoid ambiguous .NET type collisions where practical (`Color`, `Exception`, etc.).
- Temporary cleanup routines should only target temp marker / temp utility layers.
- Palette buttons should continue dispatching the same command names used at the command line so UI and manual workflows stay aligned.


## Ufls/UflsLaterals.cs
- First-pass lateral workflow support.
- `UFLS-LATERAL-SINGLE`: manual sewer lateral trace from ordered COGO-point selections; the last selected point is the main-reference shot, lateral shots are offset from top-of-pipe to pipe centerline, the command auto-connects to the nearest main centerline on `V-SURV-PIPE-CNTR`, creates a 3D lateral polyline on `V-SURV-PIPE-LATR`, and then adds the lateral directly into the existing sewer network whose name contains `-SSWR-E` using family `CLV_PVC` and the 4" PVC part size from the assigned parts list without splitting the main pipe.
- `UFLS-LATERAL-ALL`: automatic lateral-creation pass that identifies WYE shots from COGO descriptions, draws a perpendicular search line from the nearest main, prompts for a user-defined search half-width and search half-length, then gathers non-WYE shots within that band for the chosen run in each direction, auto-builds groups that fall on only one side of the main, and places aligned QA polygons on `V-SURV-RDLN` for ambiguous groups that contain shots on both sides.
- Pipe-network creation / merge intentionally deferred to a later phase.


## 2026-03-09.10 Maintenance

- Resolved the `Ufls/UflsLaterals.cs` ambiguous `DBObject` compile error by explicitly using the AutoCAD `DBObject` type for parts-list size reflection.

- `Ufls/UflsLaterals.cs` depends on Civil 3D pipe-network assemblies including `AeccDbMgd` and `AecBaseMgd` for direct network pipe creation.

- `Ufls/UflsLaterals.cs`
  - Manual lateral workflow. Current network-creation path uses reflection-based Civil network access to avoid `DBObject` / `Network` compile-time casting issues while still targeting the existing `-SSWR-E` sewer network.


## 2026-03-09 architecture notes
- `Ufls/UflsSingleManhole.cs` now reads the picked COGO point descriptions to infer dynamic manhole visibility (48/60/72) instead of forcing a 60-inch default.
- `Ufls/UflsPalette.cs` CHECK/ADJUST tabs were normalized to the Q3 button height and centered button text.
- `Ufls/UflsAdjustCommands.cs` is the active sewer-adjust command surface. Current manhole-move logic follows the proven manual / LISP pattern more closely by moving the Civil structure position first and then rebuilding each connected pipe in one endpoint-set operation.


## 2026-03-10 Q11 / 3D polyline maintenance
- **Ufls/UflsPalette.cs**
  - Q11 ADJUST row order changed so SINGLE commands are on the left and ALL commands are on the right.
  - Standardized palette button captions to uppercase.
  - Added `LABEL 3D PIPE` button in the 3D LINEWORK section.
- **Ufls/UflsAdjustCommands.cs**
  - Added `UFLS-ADJ-MH-ALL` and `UFLS-ADJ-PIPE-ALL` command aliases while preserving `...-AUTO`.
  - Manhole adjust now scans both `UFLS_MH_MARK` and `UFLS_STUB_MARK` markers so null-structure stub endpoints are included.
  - Pipe adjust now reads surveyed `LINE` and `Polyline3d` endpoints from `V-SURV-PIPE-CNTR`.
- **Ufls/UflsPipeLabels.cs**
  - Added a simple 3D-polyline endpoint label command that writes start / end invert text on `V-SURV-PIPE-INVT`.


## 2026-03-10 SD-JUNCTION style note
- `Ufls/UflsSdJunctionSizeTest.cs` now preserves the source structure style during the SD-JUNCTION size-match workflow, with a best-effort attempt to push the same style onto the matched part-size definition before swapping the placed structure.


### GIS module notes
- `Gis/GisImport.cs`: prototype MAPIMPORT session-prep workflow, boundary creation, cleanup helper, automatic GIS layer creation, and explicit WinForms FlowDirection aliasing.
- `Gis/GisPalette.cs`: Q2 GIS palette with explicit WinForms FlowDirection aliasing for namespace-safe compilation.


## GIS / GisImport.cs
- Automatic CLV GIS shapefile import workflow.
- Presents dataset and boundary options dialog for Parcels, Centerlines, Sewer, and Storm shapefiles.
- Uses ManagedMapApi importer at runtime, probes drawing GeoData plus broader Map 3D coordinate/projection members to detect the active drawing CS, applies CLV source/target coordinate system handling, reassigns imported entities to CLV GIS layers, clips imported geometry to a selected boundary, and runs duplicate cleanup on imported linework.


## 2026-03-16 GIS updates
- GIS import fallback layer specs now match CLV plot styles for Parcels, Centerlines, Sewer, Storm, and Temp Boundary.


- `CLV-GIS-OD-INSPECT` = loads the ADE/LISP OD helper only from the shared Civil 3D Lisp server path and launches `CLV-GIS-OD-INSPECT-LSP` so OD diagnostics come from ADE table reads instead of the unreliable managed Map OD reflection path.


- `Gis/GisImport.cs` also includes a small drawing-focus helper used by GIS interactive commands launched from the palette so selection prompts return to the drawing view more reliably.


## 2026-03-18 Survey best-fit review update
- `Survey/SurveyBestFitMap.cs` now includes a pre-apply WinForms review dialog for `SURVEY-BESTFIT-MAP`.
- Review grid supports `Calc` and `Ref` groups per pair, row removal, live recalculation, and residual review before finalizing the move.


- `Survey/SurveyBestFitMap.cs` — survey best-fit registration command and review dialog; prompts simplified to SURVEY POINT / MAP POINT and review grid shows Survey + Map identifiers with Control toggle.

## 2026-03-23 - GIS pipe OD offset baseline
- Added `Gis/GisPipeOdOffset.cs`.
  - Added `CLV-GIS-PIPE-OFFSET-OD` as the new baseline routine for MapImport pipe centerlines.
  - Workflow: user selects imported pipe centerline entities, the command reads OD field `InsideDiameter`, and when the value is `1.0'` (12") or larger it creates two offset curves at `InsideDiameter / 2` on each side of the source linework.
  - The command currently supports `LINE`, `LWPOLYLINE`, `POLYLINE`, and `ARC` entities and keeps the offset geometry on the same layer/properties as the source entity.
- Updated `Gis/GisImport.cs`.
  - Added an internal OD helper so other GIS routines can read a field value from any attached Map Object Data table without duplicating the reflection-based OD access code.
- Updated `Gis/GisPalette.cs`.
  - Added `PIPE OD OFFSET` under `Q2 > GIS > GIS TOOLS`.


## GIS Updates
- `Gis/GisPipeOdOffset.cs`: wrapper command for `CLV-GIS-PIPE-OFFSET-OD`; loads the shared server helper LISP and launches the ADE-based offset routine.
- `Gis/GisDropInletExplodeToGis.cs`: `CLV-GIS-DI-EXPLODE` explodes a selected drop inlet block, deletes marker / curb / text remnants, remaps exploded inner and outer linework to `C-STRM-STRC-INNR` and `C-STRM-STRC-E`, then queues ADE/LISP OD copy from the aligned imported structure point to all eligible exploded outer entities created from that block.
- `Gis/GisJunctionStructureFromPoint.cs`: `CLV-GIS-JS-FROM-POINT` prompts for a single imported `Structures` point, automatically finds the paired closed junction-structure outer and inner polylines around that point, moves them to `C-STRM-STRC-E` and `C-STRM-STRC-INNR`, then queues ADE/LISP OD copy from the point to the detected outer polyline.


### GIS / shared helper path update
- `Gis/GisPipeOdOffset.cs`
  - Wrapper command for `CLV-GIS-PIPE-OFFSET-OD`.
- Loads `CLV_GIS_PIPE_OD_OFFSET.lsp` only from the shared support location: `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp`.


- `CLV-GIS-PIPE-OFFSET-OD`
  - The .NET wrapper now uses the same helper-loading pattern as `CLV-GIS-OD-INSPECT`: it queues the shared server LISP load first, then launches `CLV-GIS-PIPE-OFFSET-OD-LSP` as a separate command-line command.

- `CLV-GIS-DI-EXPLODE`
- `CLV-GIS-JS-FROM-POINT`
  - Selects one drop inlet block only.
  - Explodes the block in place.
  - Deletes exploded marker / curb / text remnants.
  - Remaps `V-SURV-STRC-INNER-2D~~` to `C-STRM-STRC-INNER-E` and `V-SURV-OUTR-2D~~` to `C-STRM-STRC-E`.
  - Finds the nearest imported structure point and queues ADE/LISP OD copy to the remapped outer structure polyline.


## GIS batch automation

- `Gis/GisTrimInsideBoundary.cs`
  - Command: `CLV-GIS-TRIM-INSIDE`
  - Palette: CREATE DATA > TOOLS > `TRIM INSIDE`.
  - Select one closed structure boundary, then trims only storm/sewer pipe wall linework (`C-STRM-PIPE-E`, `C-SSWR-PIPE-E`) inside that boundary.
  - Runs single-boundary mode directly without the previous Single/All prompt. Centerline layers such as `C-STRM-PIPE-CNTR-E` and `C-SSWR-PIPE-CNTR-E` are ignored.
  - Does not use or create LISP helpers.

- `Gis/GisStormStructureAuto.cs`
  - Command: `CLV-GIS-STRM-AUTO`
  - Batch two-pass automation for storm structures.
  - Pass 1 finds supported drop inlet blocks (`TYPE_A-USD_411`, `TYPE_A_MOD-USD_411.1`, `TYPE_C-USD_413`, `TYPE_CM-USD_422`, `TYPE_CM2-USD_412.1`, `TYPE_D-USD_414`, `TYPE_DM2-USD_412.1`), explodes/cleans/remaps linework, and queues OD copy from matched `Structures` points to outer structure entities.
  - Pass 2 scans remaining `Structures` points and auto-detects junction structure outer/inner closed polylines, remaps layers, and queues OD copy to the outer polyline.

- `CLV_GIS_PIPE_OD_OFFSET.lsp`: server-hosted storm ADE/LISP helper expected at `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\CLV_GIS_PIPE_OD_OFFSET.lsp`; reads `InsideDiameter`, creates storm pipe wall offsets, and adjusts created wall endpoints to `C-STRM-STRC-INNR` when possible.


## 2026-03-24 GIS / Q1 updates
- Q1 (UFLS) palette now includes a new GIS tab without replacing the existing CHECK / ADJUST / LAYERS tabs.
- Q1 > GIS now groups tools into STORM DRAIN, SEWER, CLEANUP, and EXPORT.
- Q1 > GIS > STORM DRAIN includes GIS PREP - ALL, JUNCTIONS AND INLETS - ALL, DROP INLET - SINGLE, JUNCTION STRUCTURE - SINGLE, and PIPE.
- Q1 > GIS > SEWER includes GIS PREP - ALL, MANHOLE, and PIPE using the live sewer GIS commands.
- Q1 > GIS > CLEANUP includes ERASE POINTS.
- Q1 > GIS > EXPORT includes FINALIZE STRUCTURES and FINALIZE PIPES for survey-cache compare / preview / append export.
- Q2 > GIS no longer exposes the retired JOIN CENTERLINES workflow.
- Added CLV-GIS-STORM-GIS to queue CLV-GIS-STRM-AUTO followed by CLV-GIS-PIPE-OFFSET-OD-ALL.
- Added CLV-GIS-PIPE-OFFSET-OD-ALL-LSP to the GIS pipe offset helper LISP for all-pipes processing.

- `Gis/GisPipeWallAdjust.cs`
  - Adds `CLV-GIS-PIPE-EXTEND` and `CLV-GIS-PIPE-TRIM` cleanup commands for single-click pipe-wall endpoint adjustment to the nearest `C-STRM-STRC-INNR` wall.


- Gis/GisEraseImportedStructurePoints.cs
  - `CLV-GIS-ERASE-POINTS`: Erases imported map structure DBPoint entities on layer `Structures`.

### Survey Auto Closure Review Additions

- `Survey/SurveyClosureReview.cs`
  - Stores the latest AUTO CLOSURE review data in memory only.
  - Provides report window, numbered modelspace markers, goto-segment zoom/select, and marker cleanup commands.
- `Survey/SurveyAutoClosure.cs`
  - Now populates in-session review records when adjusted overlay geometry is created.
- `Shared/LayerStandards.cs`
  - Adds managed review marker layer `V-SURV-MAP~-REVIEW`.
  - Retains `V-SURV-LWRK-REVIEW` as the managed linework review layer for prior/manual review workflows. The active LINEWORK REVIEW workflow uses direct overlay highlighting instead of marker boxes and no longer moves or deletes issue objects. Exact duplicates highlight green, same-line length differences highlight the longer object green and shorter object orange, and offset/possible-error duplicates within tolerance highlight red.
- `Survey/SurveyPalette.cs`
  - Adds Q4 > MAPPING > REVIEW buttons for report, markers, goto, and clear review.


## Phase 2C tangent-preserving arc notes

- `Survey/SurveyAutoClosure.cs` now detects originally tangent arc transitions and solves adjusted bulges to preserve those tangent relationships where practical.
- `Survey/SurveyClosureReview.cs` shows Tangency In / Tangency Out / Tangency Status columns in the report window.
- Old split-view review commands remain removed; Q4 > MAPPING uses BOUNDARY and REVIEW report tools.

- Phase 2C Fix 2 tangent fillet refinement: `Survey/SurveyAutoClosure.cs` rebuilds standalone tangent ARC corners as true adjusted fillets when bounded by LINE segments.

- Phase 3 boundary constraints: updated `Survey/SurveyClosureConstraints.cs` with a WinForms constraint manager dialog and simplified Q4 > MAPPING > BOUNDARY to `AUTO CLOSURE` and `CONSTRAINTS`. Constraints are session-only, highlight selected objects, and do not write XData.

### Survey Auto Closure Phase 3A
- `Survey/SurveyAutoClosure.cs`: combined constraint-state pass for stackable LOCK BEARING / LOCK LENGTH / LOCK RADIUS / KEEP PARALLEL constraints.
- `Survey/SurveyClosureReview.cs`: adds Constraint State column to the modeless closure report.

### AUTO CLOSURE Phase 3A Fix 1
- `Survey/SurveyAutoClosure.cs`: final locked bearing/length constraints are reapplied after tangent-arc processing so fixed-vector rows remain fixed in the report. Tangent fillet rebuilds do not trim length-locked neighboring lines.


### Survey Auto Closure Phase 3B reference constraints

- `Survey/SurveyClosureConstraints.cs` now includes reference-based constraints for parallel, offset, and perpendicular control lines.
- `Survey/SurveyAutoClosure.cs` consumes the in-session reference constraints during standalone LINE/ARC boundary adjustment.
- Reference objects are highlighted for review but are not modified by AUTO CLOSURE.


### AUTO CLOSURE Phase 3B Fix 2
- `Survey/SurveyClosureConstraints.cs`: OFFSET TO REFERENCE now supports current measured offset or user-specified offset distance with side control.
- `Survey/SurveyAutoClosure.cs` / `Survey/SurveyClosureReview.cs`: closure report includes target/actual/delta offset values for offset-to-reference constraints.

- `Survey/SurveyGisSectionMarkerCommands.cs`: Q4 Survey section corner marker population command (`SURVEY-GIS-SECTION-MARKER` / `GISSECTIONMARKER`) that inserts `GIS_SECTION_MARKER` and fills quadrant township/section attributes from selected CLV_Sections Map Feature `APN` values. Supports layer-file/FDO Map Feature selection by converting the AutoCAD selection set through `AcMapFeatureEntityService.GetSelection(...)` before using the Map feature selection/filter reader, MAPIMPORT closed-polylines with Object Data fallback, window selection, auto quadrant sorting, missing quadrants, and marker IDs like `NE126-36_SE137-01`.


## 2026-06-11 - PLSS Section Label Import
- `Survey/SurveyPlssSectionLabelsCommands.cs` contains `SURVEY-PLSS-IMPORT-LABELS` / `PLSSIMPORTLABELS`, which reads `GIS_SECTION_MARKER` blocks from `\\ci.las-vegas.nv.us\pw_data_depot\PW_GRID_City\000-City\GIS\_CLV_CACHE\<active coordinate system>\GIS_Sections.dwg`, builds complete PLSS sections, and inserts the PLSS section/corner/quarter/sixteenth label blocks from `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\Blocks\Survey\PLSS Sections` using the current drawing scale (`DIMSCALE` preferred; sub-1 `CANNOSCALEVALUE` values inverted), and formats numeric section attributes without leading zeroes (`01` -> `S1`).
- `Survey/SurveyPalette.cs` adds Q4 LABEL > `PLSS SECTIONS` > `IMPORT LABELS` for the new command.


- `Survey/SurveyLineworkReviewCommands.cs`
  - Commands: `SURVEY-LINEWORK-REVIEW` / `LINEWORKREVIEW`, `SURVEY-LINEWORK-CLEAR-REVIEW` / `LINEWORKCLEAR`. The report dialog includes Highlight Selected, Zoom Selected, Clear Highlight, and REMOVE DUPLICATES; REMOVE DUPLICATES launches OVERKILL for all non-xref current-space linework.
  - Q4 > MAPPING > REVIEW duplicate/near-duplicate finder for selected non-xref LINE, ARC, CIRCLE, LWPOLYLINE, and 2D POLYLINE geometry.
  - Opens an options dialog, creates direct linework overlay highlights on `V-SURV-LWRK-REVIEW` only for duplicate/near-duplicate objects, uses green for exact duplicates, orange for shorter same-line length differences, and red for near-offset duplicates within tolerance. The modeless WinForms report includes layer/object detail, Highlight Selected, Zoom Selected, Clear Highlight, and REMOVE DUPLICATES. Temporary review overlays are cleared when the report closes, when a new review starts/cancels, and before REMOVE DUPLICATES starts AutoCAD OVERKILL with all non-xref current-space linework preselected. The separate palette button for clear highlight was removed.

## PDF Viewer / Map Review

| File | Responsibility |
|---|---|
| `PdfViewer/PdfViewerCommands.cs` | Hosts the `PDF VIEWER` WinForms PaletteSet and exposes `PDFVIEW` / `MAPREVIEW`. |
| `PdfViewer/PdfViewerControl.cs` | Viewer UI, PDF rendering, plan calibration workflow, category navigation, automatic model-space following, and Return to Current. |
| `PdfViewer/PdfViewerModels.cs` | Per-DWG state, named sheet/category mappings, coverage data, and two-point similarity transform. |
| `PdfViewer/PdfViewerStorage.cs` | Named Object Dictionary JSON persistence, PDF path resolution, and relinking support. |
| `Docs/PDF_VIEWER_V1_README.md` | Setup, operation, deployment, and Version 1 limitations. |
| `Docs/PDF_VIEWER_ULTIMATE_PLAN.md` | Living accepted/deferred feature roadmap for the viewer. |

The viewer remains a separate palette rather than a drawing underlay. PDFium/SkiaSharp rendering is performed only by the separate `PdfRenderer/CLV.PdfRenderHost.exe` process; the Civil 3D plugin communicates with it through temporary JSON request/response files and PNG output.


### PdfRenderHost (PDF Viewer isolation)
- `PdfRenderHost/CLV.PdfRenderHost.csproj` — separate .NET 8 x64 executable project containing the `PDFtoImage` package reference.
- `PdfRenderHost/Program.cs` — metadata/page-render request processor. Native PDFium and SkiaSharp load only in this helper process.
- `PdfViewer/PdfRenderClient.cs` — Civil 3D-side process client. Starts the helper, enforces a timeout, reads responses, and converts returned PNG files into WinForms bitmaps.
- Deployment requirement: preserve the entire `PdfRenderer` subfolder beside the loaded Civil Tools DLL.

### PDF Viewer multi-sheet coverage
- `PdfViewer/PdfViewerModels.cs`: stores independent calibration and copied polygon coverage geometry for each mapped plan sheet; curved lightweight-polyline segments retain their bulges.
- `PdfViewer/PdfViewerControl.cs`: offers Current View, Rectangle, or Polygon coverage, supports EDIT BOUNDARY without recalibration, automatically selects the smallest containing mapped sheet, and displays a temporary polygon highlight for the active sheet.

### Legal Description — POC Tie and Text Style Extension (2026-07-22)
- `LegalDescription/LegalDescriptionCommands.cs`: when POC and POB are separate, prompts for a connected LINE/ARC tie traverse from POC to POB.
- `LegalDescription/LegalGeometryService.cs`: orders and validates tie and boundary traverses independently; only boundary courses contribute to closure and area.
- `LegalDescription/LegalDescriptionModels.cs`: stores `TieCourses`, course group, selected text style, and text-style templates.
- `LegalDescription/LegalTextStyleService.cs`: loads built-in wording presets and the office preset library embedded in the DLL.
- `LegalDescription/LegalTextGenerator.cs`: emits calculated tie calls before boundary calls and applies the selected wording preset.
- `LegalDescription/LegalDescriptionPalette.cs`: displays TIE and BOUNDARY groups together, with tie rows identified as T1, T2, etc., and provides a Text Style list.
- `Reference/LegalDescriptionTextStyles.json`: source copy of the office wording presets; embedded into the DLL during build.

### LegalDescription/LegalMTextService.cs
- Creates final paragraph-form MText from the line-by-line legal-description editor.
- Stores a persistent link marker on each MText and tracks linked handles in the drawing session.
- Updates all valid linked MText objects when the legal wording or source geometry is refreshed.

## 2026-07-22 - Legal description verbiage formatting
- `LegalDescription/LegalTextGenerator.cs`: ALL CAPS, introductory paragraph, configurable POC/POB calls, dedicated final-tie template, and area-statement templates.
- `LegalDescription/LegalDescriptionPalette.cs`: `DESCRIPTION OPTIONS` editor for reusable legal context fields.
- `LegalDescription/LegalMTextService.cs`: paragraph-form linked MText with blank-line separation between final sections.
- `Reference/LegalDescriptionTextStyles.json`: source wording library embedded into the DLL during build.

### Legal Description DOCX Export
- `LegalDescription/LegalDocxExporter.cs` — creates a `.docx` from the approved City Surveyor `.dotx` without Word automation or GPT.
- `LegalDescription/EmbeddedLegalResourceService.cs`: opens the embedded City Surveyor DOTX and legal wording JSON resources from the DLL.
- `Reference/Legal Templates/Basic Template.dotx` — source Word template embedded into the DLL and used by `EXPORT LEGAL DOCX`.
- `Reference/Legal Templates/LD Example Language.docx` and sample PDFs — office wording/format references retained with the project source.
- CAD linked MText is the live review representation; DOCX is the formatted deliverable.

### Legal Description phrase and curve system
- `LegalDescription/LegalCurveAnalysisService.cs` - analyzes ARC tangency, compound/reverse relationships, concavity, radial bearing, and chord data.
- `LegalDescription/LegalPhraseLibrary.cs` - loads reusable commencement, POB, return, and line-relationship phrases.
- `Reference/LegalDescriptionPhraseLibrary.json` - embedded office phrase library; rebuild the DLL after approved wording changes.
- Land Location is still maintained through the manual introductory/situate paragraph pending a later structured PLSS phase.

## Legal Description option persistence and area units (2026-07-22)
- Description Options restores the current session's commencement, final-tie/POB, same-point beginning, and return-call selections rather than defaulting untouched fields.
- Area output supports square feet, acres, or both, with saved precision and computer-method wording settings.

- Legal Description Options persist wording keys immediately and expose visible automatic area-unit selection (square feet, acres, or both).
- Legal Description area output formats square-foot values with thousands separators across CAD preview and exports.

### LEGALDESC saved-session startup

`LEGALDESC` checks the drawing Named Objects Dictionary for the saved legal-description session. If one exists, it prompts `[Open/New] <Open>` before any entity selection. `Open` restores the session in the palette; `New` begins a replacement session. `LEGALDESC-OPEN` remains available as the direct reopen command.


### LEGALDESC synchronized course review and relationship fields (2026-07-22)
- Course rows now separate travel wording from destination wording: Travel Relationship, Travel Feature/Reference, Travel Wording Order, Destination Clause, and Destination Feature/Reference.
- `AFTER BEARING` supports calls such as `THENCE NORTH ... WEST, ALONG ..., 802.38 FEET`; `BEFORE GEOMETRY` retains the conventional `THENCE ALONG ..., NORTH ... WEST, 802.38 FEET` order.
- Selecting a row creates a temporary magenta transient overlay on the source LINE/ARC and yellow-highlights the corresponding generated text in the review pane. Source entity properties are not modified.


### Legal-description synchronized linked-MText review
- `LegalDescription/LegalDescriptionPalette.cs` coordinates a selected course across the grid, source geometry, editor preview, and linked CAD MText.
- `LegalDescription/LegalMTextService.cs` creates temporary MText clones with only the matching course call formatted magenta and underlined. These clones are displayed through AutoCAD transient graphics and are erased when the selected row changes.
- The original linked MText database objects and their stored contents are not modified by review highlighting.

- Legal Description curve analysis now records independent incoming/outgoing tangency and start/end radial bearings so wording is direction-independent.

### Legal Description — City Surveyor Land Description Template
- `LegalDescription/LegalLandDescriptionTemplateService.cs` builds the standard City Surveyor land-location paragraph from editable quarter, section, township, and range fields.
- `LegalDescription/LegalDescriptionModels.cs` stores the structured Land Description fields in the drawing session.
- `LegalDescription/LegalDescriptionPalette.cs` exposes only the variable template portions while retaining a full-paragraph override.
- `LegalDescription/LegalDocxExporter.cs` preserves the Word template's right-tab header layout and limits bolding to approved complete phrases.

### Legal DOCX template preservation update (2026-07-22)
- `LegalDescription/LegalDocxExporter.cs` now preserves the embedded City Surveyor template's original header tabs, paragraph properties, Arial 12-point run formatting, seal placement, and page layout while replacing only approved placeholder values.


### Map Transform test revision
- `Survey/SurveyBestFitMap.cs` emits `MAP TRANSFORM revision 2026.08.06-HISTORY-R3` when the revised editable-history implementation is actually loaded. Recalled sessions temporarily restore the map to its saved original placement and recreate numbered pair markers during editing; Cancel restores the prior placement. The command also verifies the saved XRecord can be read back immediately after Finalize.
