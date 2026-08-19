## 2026-07-22 - Legal Description Builder Phases 1-2
- `LEGALDESC` / `CLV-LEGAL-DESCRIPTION` - Builds an ordered legal traverse from selected LINE and ARC entities, prompts for POB and optional POC, calculates line/curve calls, forward closure, reverse build, length, and area, then opens the editable legal-description palette.
- `LEGALDESC-OPEN` - Reopens the legal-description session saved in the current drawing.
- Legal Description palette: `PLACE LINKED MTEXT` creates paragraph-form MText at a selected point; `UPDATE LINKED MTEXT` refreshes linked text; `REFRESH SOURCE` rereads the original LINE/ARC geometry and updates calculated calls.
- Legal Description course selection: synchronizes a magenta source-entity overlay, yellow editor call highlight, and temporary magenta/underlined highlighting of the matching call in all linked MText objects.
- `LEGALDESC` POC choice uses registered AutoCAD keywords `Same` and `seParate`, accepting `S`/`Same` or `P`/`Separate`.
- Added `LEGAL DESCRIPTION` under Q4 > MAPPING > TOOLS.
- No LISP routines were created or changed.

## Q4 MAPPING menu layout (2026-07-21)
- `TRANSFORM`: `MAP TRANSFORM` -> `SURVEY-BESTFIT-MAP`; `BOUNDARY TRANSFORM` -> `SURVEY-TRANSFORM-CONTROL`; `C3D TRANSFORM` -> native `ADETRANSFORM`.
- `TOOLS`: `DRAW TIE LINE`, `PDF VIEWER`, `PDF CLIP`, `LEGAL DESCRIPTION`, `XREF COLOR`, `OFFSET TO TEMP LAYER`, and `LINEWORK REVIEW`.
- `BOUNDARY`: closure and review commands are combined under one section; the separate `BOUNDARY REVIEW` heading was removed.

## 2026-07-20 - Removed obsolete Q4 boundary import tools
- Removed Q4 > MAPPING > BOUNDARY buttons `BOUNDARY IMPORT AUTO`, `BOUNDARY IMPORT MANUAL`, `ROADS IMPORT`, and `EASEMENT IMPORT`.
- Removed their managed command implementations and aliases from the assembly. DXF geometry is now supplied directly rather than imported through these CSV workflows.
- Remaining Q4 boundary tools begin with `DRAW TIE LINE`, followed by closure and review tools.

## 2026-07-15 - Q4 Transform / Temporary Construction Offset
- `SURVEY-TRANSFORM-CONTROL` / `TRANSFORMCONTROL` - Q4 > MAPPING > TRANSFORM > `BOUNDARY TRANSFORM`. Selects one or more transformable drawing objects, then captures the source control circle and source rotation line before capturing the destination control circle and destination rotation line. Direct and nested block/xref circles and lines are supported. Displays the complete moved-and-rotated result at the destination with temporary faded transparency. Choose Flip to preview a 180-degree reversal, press Enter to accept, or cancel to restore the original geometry. Translation and rotation only; no scale change.
- `SURVEY-OFFSET-TEMP` / `OFFSETTEMP` - Q4 > MAPPING > TOOLS > `OFFSET TO TEMP LAYER`. Uses one OFFSET-style distance, then repeatedly prompts for a curve and side point and creates each result on `V-CONS-LINE-TEMP` until Enter is pressed.

## 2026-07-13 - Q4 Menu Reorganization + Distance-Only Labels
- `SURVEY-LC-LABEL-2POINT` / `2-POINT` / `Q42POINT` - Q4 > LABEL > LINES AND CURVES > `2-POINT  ||  BEARING AND DIST`; launches `ADDLINEBETWEENPOINTS`, moves created label objects to `V-LABL`, and assigns `R26_Bearing + Distance`.
- `SURVEY-LC-LABEL-2POINT-DIST` / `Q42POINTDIST` - Q4 > LABEL > LINES AND CURVES > `2-POINT  ||  DIST`; uses the same two-point Civil 3D workflow and assigns `R26_Distance`.
- `SURVEY-LC-LABEL-BEARING-DISTANCE` / `SURVEY-BEARING-DISTANCE` / `BEARINGANDDISTANCE` / `Q4BDIST` - Q4 > LABEL > LINES AND CURVES > `BEARING AND DIST`; launches `ADDSEGMENTLABEL` and assigns `R26_Bearing + Distance`.
- `SURVEY-LC-LABEL-DISTANCE` / `SURVEY-SEGMENT-DISTANCE` / `Q4LCDIST` - Q4 > LABEL > LINES AND CURVES > `DISTANCE`; uses the same single-segment Civil 3D workflow and assigns `R26_Distance`.
- `SURVEY-AREA-SF-LABEL` moved to Q4 > LABEL > AREA. `SURVEY-LABEL-ROADS` is now displayed as `STREET NAMES`.
- Q4 > MAPPING now displays `TRANSFORM`, `TOOLS`, `BOUNDARY`, and `SUBDIVISION LINEWORK`; closure and review commands are within the single `BOUNDARY` section.
- Q4 includes a new `GIS` tab containing `TOWNSHIP/RANGE` > `SECTION CORNER MARKER`.

## 2026-07-13 - Q4 Boundary Menu + AUTO CLOSURE Output Fix
- Removed the `BOUNDARY ONLY IMPORT` button from Q4 > MAPPING > BOUNDARY. Legacy command aliases remain available at the command line for compatibility.
- Updated `SURVEY-AUTO-CLOSURE` so the final Yes/No prompt controls only whether original linework is retained on `V-SURV-MAP~-ORIG`.
- `Yes` keeps the existing original/reference + adjusted-overlay workflow. `No` now replaces the selected source geometry with the adjusted closure on its original layer and does not retain an original copy.

# CLV_CivilTools COMMAND_INDEX


## 2026-06-30 - Q4 Survey Label / Boundary Mapping Additions
- `SURVEY-LC-LABEL-2POINT` / `2-POINT` / `Q42POINT` - Q4 Survey > LABEL tab > LINES AND CURVES > `2-POINT  ||  BEARING AND DIST`; launches Civil 3D `ADDLINEBETWEENPOINTS` for a Line and Curve label between two picked points. The wrapper ensures `V-LABL`, launches the Civil command, and moves the newly created label object(s) to `V-LABL` after placement because Civil 3D label defaults may place them on `C-ANNO`. Assigned label style: `R26_Bearing + Distance` after placement.
- `SURVEY-LC-LABEL-BEARING-DISTANCE` / `SURVEY-BEARING-DISTANCE` / `BEARINGANDDISTANCE` / `Q4BDIST` - Q4 Survey > LABEL tab > LINES AND CURVES > `BEARING AND DIST`; launches Civil 3D `ADDSEGMENTLABEL` for a single line/curve segment label. The wrapper ensures `V-LABL`, launches the Civil command, and moves the newly created label object(s) to `V-LABL` after placement because Civil 3D label defaults may place them on `C-ANNO`. Assigned label style: `R26_Bearing + Distance` after placement.
- `SURVEY-DRAW-TIE-LINE` / `DRAWTIELINE` / `Q4TIE` - Q4 Survey > MAPPING tab > TOOLS > `DRAW TIE LINE`; sets current layer to `V-CTRL-TIES-LINE` using the CLV layer standard (yellow, HIDDEN3, XS-60) and starts AutoCAD LINE.
- `SURVEY-AREA-SF-LABEL` / `AREASFLABEL` / `Q4AREASF` - Q4 Survey > LABEL tab > AREA > `AREA SF LABEL`; prompts for a point inside a closed boundary, traces the enclosing boundary, calculates area in square feet, and places a centered SF label.


## 2026-06-29 - Linework Review Tool
- `SURVEY-LINEWORK-REVIEW` / `LINEWORKREVIEW` - Q4 Survey > MAPPING tab > TOOLS > `LINEWORK REVIEW`; opens a WinForms options dialog, selects non-xref LINE, ARC, CIRCLE, LWPOLYLINE, and 2D POLYLINE objects, then reports duplicate/near-duplicate issues. Exact duplicates are overlaid/highlighted in green; same-line length differences highlight the longer object green and the shorter object orange; offset/possible-error duplicates within the review tolerance are overlaid/highlighted in red. The modeless report shows object/layer/type detail and provides Highlight Selected, Zoom Selected, Clear Highlight, and REMOVE DUPLICATES actions. Review overlay linework is cleared when the report closes, when a new review starts/cancels, and before REMOVE DUPLICATES launches AutoCAD OVERKILL with all non-xref current-space linework preselected.
- `SURVEY-LINEWORK-CLEAR-REVIEW` / `LINEWORKCLEAR` - Command line only; clears the current LINEWORK REVIEW overlay highlights. The separate Q4 menu button was removed.

## 2026-06-25 - Xref Color Review Tool
- `SURVEY-XREF-COLOR` / `XREFCOLOR` - Q4 Survey > MAPPING tab > TOOLS > `XREF COLOR`; opens a WinForms color-choice dialog with standard colors on the left and 70% transparent versions on the right. Choices include Red, Yellow, Green, Cyan, Magenta, Gray ACI 252, RESET selected xref, and RESET ALL xrefs. Color choices apply host drawing xref-layer overrides to the selected xref's dependent layers for linework comparison; RESET reads the selected source xref DWG and restores those dependent layer colors/transparency back to the source values; RESET ALL applies that reset workflow to every attached/overlaid xref found in the drawing.
- **2026-06-29 Layout Fix:** XREF COLOR dialog now uses explicit row heights/taller dialog so Cyan, Magenta, and Gray ACI 252 rows display above RESET / RESET ALL.

## 2026-06-18 - Drop Inlet Block Definition Refresh
- `UFLS-DROP-INLET` - Q1 UFLS > GIS > `DROP INLET - SINGLE`; now checks the active drawing's selected drop inlet block definition for the required `DI_CENTER` attribute before insertion. If an older definition is found, the command renames/preserves it with a `_CLV_OLD_yyyyMMdd_HHmmss` suffix and imports the current block definition from `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\Blocks\Survey`.

## 2026-05-20 - Q1 palette compact sizing + reduced font test
- `UFLS.Q1` / legacy `UFLS.Q11` still open the same UFLS palette and command set. Only the Q1 palette presentation changed: default size is now `360 x 760`, minimum size is `320 x 560`, button/row width is `256`, row height is `24`, and explicit button/section font size is `7.0 pt` with vertical scrolling retained per tab.


## 2026-05-19 - Survey label roads height and boundary cleanup
- `SURVEY-LABEL-ROADS` / `Q4LABELROADS` - Q4 LABEL tab `STREET NAMES`; prompts for boundary selection/drawing, loads assessor street centerlines for MAPLABEL2ANN conversion, standardizes labels to `C-LABL-STNM` / `CLV-Standard.14`, forces a `0.14` paper text height using the current annotation scale, erases newly converted labels outside the selected/drawn boundary, and always unloads the temporary road layer/connection after the conversion workflow.
- `SURVEY-LABEL-ROADS-CONVERT` - Manual convert-only helper remains available for post-MAPLABEL2ANN cleanup when labels were already converted; boundary deletion only applies to the automated boundary workflow because the convert-only helper has no boundary context.

# CLV_CivilTools COMMAND_INDEX

## 2026-06-11 - Survey Section Corner Marker Populate Tool
- `SURVEY-GIS-SECTION-MARKER` / `GISSECTIONMARKER` - Q4 Survey > MAPPING tab `SECTION CORNER MARKER`; places the `GIS_SECTION_MARKER` block at a picked point, reads selected CLV_Sections Map Feature `APN` values by converting the AutoCAD selection set to a Map platform feature selection for layer-file/FDO selections, or imported closed polyline Object Data `APN` values from MAPIMPORT, auto-sorts selected sections into NW/NE/SW/SE from the picked point, allows missing quadrants, and fills `MARKER_ID`, township, section, and section-key attributes.



## 2026-06-11 - PLSS Section Label Import Tool
- `SURVEY-PLSS-IMPORT-LABELS` / `PLSSIMPORTLABELS` - Q4 Survey > LABEL tab `PLSS SECTIONS` > `IMPORT LABELS`; prompts for a window or existing closed-polyline area, reads the matching `GIS_Sections.dwg` marker cache from the active LVF/LVHEF coordinate-system folder, builds complete sections from `GIS_SECTION_MARKER` quadrant attributes, inserts the PLSS section label blocks from the shared `Blocks\Survey\PLSS Sections` folder, formats section attributes with the required `S` prefix and removes numeric leading zeroes (`01` -> `S1`), uses the drawing scale (`DIMSCALE` preferred; sub-1 `CANNOSCALEVALUE` values inverted), and skips duplicate shared edge/corner label locations.

## Current documentation note - 2026-05-05
- Restored detailed command history/index content from the `Logs.zip` backup.
- Retired commands from the old Map 3D profile-based pipe-network export/check test workflow are intentionally not listed as active commands.
- Current active commands should be verified from `[CommandMethod(...)]` attributes in source when publishing a release.

- Updated `Ufls/UflsManholeAutoCreate.cs` so `UFLS41P` / `UFLS-MH-AUTO-1P` now ensures the `UFLS_MH_MARK` block is loaded before placing the center marker.
## 2026-03-25 - Q1 1P / 3P manhole split
- Updated `Ufls/UflsPalette.cs` so Q1 > CHECK > 2D LINEWORK now shows `3P MANHOLE - ALL`, `3P MANHOLE - SINGLE`, `1P MANHOLE - ALL`, and `1P MANHOLE - SINGLE`.
- Added `UFLS41P` / `UFLS-MH-AUTO-1P` in `Ufls/UflsManholeAutoCreate.cs` for single-point manhole auto-create workflows using survey center-point shots.
- Added `UFLS61P` in `Ufls/UflsSingleManhole.cs` for single-point manhole creation from one selected COGO center point.
- Updated palette routing notes and command entries for the new 1P workflows.

- `CLV-GIS-FINALIZE-STRC` - Auto-detects sewer vs storm from finalized structure layers, detects the drawing coordinate system, previews the matching survey-cache structures + pipes as gray xrefs, marks exact duplicates / nearby conflicts in CAD, confirms with the user, then appends the current finalized structure entities into the matching survey cache DWG.
- `CLV-GIS-FINALIZE-PIPES` - Same compare → visualize → confirm → finalize workflow for finalized pipe layers, targeting the matching survey pipe cache DWG while previewing both structures and pipes in gray.
﻿- `CLV-GIS-SSWR-GIS` - Runs the sewer GIS prep sequence in order: sewer manholes first, then sewer pipes.
- `CLV-GIS-SSWR-MH` - Prompts for one imported `Structures` point, explodes nearby sewer manhole block linework when present, migrates detected inner / outer structure geometry to `C-SSWR-STRC-INNR` / `C-SSWR-STRC-E`, and queues ADE/LISP Object Data copy from the point to the outer sewer structure object(s).
- `CLV-GIS-SSWR-MH-ALL` - Loops all imported `Structures` points and runs the sewer manhole conversion workflow for batch GIS prep.
- `CLV-GIS-SSWR-PIPE` - Loads the server-hosted sewer ADE/LISP helper, lets the user select one or more sewer pipe centerlines, reads `InsideDiameter`, moves under-12" lines directly to `C-SSWR-PIPE-E`, and offsets 12"+ pipes into `C-SSWR-PIPE-E` while moving the source centerline to `C-SSWR-PIPE-CNTR-E`.
- `CLV-GIS-SSWR-PIPE-ALL` - Runs the same sewer pipe conversion logic against modelspace pipe geometry for batch GIS prep.


- `CLV-GIS-PIPE-OFFSET-OD`: server helper syntax corrected; command should load again after replacing server `CLV_GIS_PIPE_OD_OFFSET.lsp`.

- `CLV-GIS-PIPE-OFFSET-OD`: offsets qualifying storm pipes, moves centerline/wall layers, and now guards endpoint intersection adjustment against invalid point data before trimming/extending to structure inner walls.
- `CLV-GIS-PIPE-CONNECT-STRM` - Prompts for storm pipe wall linework and structure inner-wall polylines, then moves qualifying pipe endpoints to the nearest selected structure inner wall and standardizes layers to `C-STRM-PIPE-E` and `C-STRM-STRC-INNR`.
- `CLV-GIS-PIPE-OFFSET-OD` - Shared ADE/LISP pipe offset helper now moves qualifying source centerlines to `C-STRM-PIPE-CNTR-E`, places created offset wall linework on `C-STRM-PIPE-E`, and automatically adjusts created wall endpoints to apparent intersections with nearby `C-STRM-STRC-INNR` structure inner walls.
﻿## 2026-03-18 - survey-map best-fit registration prototype
- Added `Survey/SurveyBestFitMap.cs` with `SURVEY-BESTFIT-MAP` for rigid XY best-fit registration of survey-map xrefs / blocks from numbered control pairs.
- Added `Survey/SurveyPalette.cs` so Q4 survey-mapping palette includes `BEST FIT MAP`.
- Updated `Survey/SurveyAutoClosure.cs` and `Shared/LayerStandards.cs` so Q4 MAPPING `AUTO CLOSURE` prompts for a start point, outputs original/adjusted overlay layers, and expands the closure QA report.

# CLV_CivilTools COMMAND_INDEX

------------------------------------------------------------
COMMAND INDEX
------------------------------------------------------------

Command | File | Access Location | Purpose
---|---|---|---
CLVHELP | Help/ClvHelpCommands.cs | Command Line | Opens the shared CLV Civil Tools Knowledge Base homepage in the default browser.
Q2 | Gis/GisPalette.cs | Command Line | Opens the GIS / aerial palette. GIS tab now labels the reference area as `SECTION/COORDINATE SYSTEM` and separates coordinate-zone display from section display.
PCT.Q3 | PoinCloud/PctPalette.cs | Command Line | Opens the point-cloud tools palette.
UFLS.Q1 / UFLS.Q11 | Ufls/UflsPalette.cs | Command Line | Opens the UFLS palette. Q1 is the primary command; Q11 is retained as a legacy alias.
Q4 / SURVEY.Q4 | Survey/SurveyPalette.cs | Command Line | Opens the survey mapping palette.
CLV-GIS-DISPLAY-COORDINATE-ZONES / CLV-GIS-LOAD-REFERENCE-LAYERS | Gis/GisReferenceLayers.cs | Command Line / Q2 GIS (`SECTION/COORDINATE SYSTEM` > `DISPLAY COORDINATE ZONES`) | Displays only the NV83.NCRS-LVF and NV83.NCRS-LVHEF coordinate-zone reference layer files. The legacy load command is retained but no longer loads `CLV_Sections`.
CLV-GIS-UNLOAD-COORDINATE-ZONES / CLV-GIS-UNLOAD-REFERENCE-LAYERS | Gis/GisReferenceLayers.cs | Command Line / Q2 GIS (`SECTION/COORDINATE SYSTEM` > `UNLOAD COORDINATE ZONES`) | Removes only the coordinate-zone reference layers and their map-session connections.
CLV-GIS-DISPLAY-SECTIONS | Gis/GisSectionReferenceLayers.cs | Command Line / Q2 GIS (`SECTION/COORDINATE SYSTEM` > `DISPLAY SECTIONS`) | Displays the `CLV_Sections` reference layer as its own command set.
CLV-GIS-UNLOAD-SECTIONS | Gis/GisSectionReferenceLayers.cs | Command Line / Q2 GIS (`SECTION/COORDINATE SYSTEM` > `UNLOAD SECTIONS`) | Removes only the `CLV_Sections` reference layer and matching map-session connections.
SURVEY-CREATE-LEGEND / CREATELEGEND / CREATE-LEGEND | Survey/SurveyLegendCommands.cs | Command Line / Q4 LABEL (`LEGEND`) | Opens the survey legend checklist from `Reference/SurveyLegend.csv`, inserts `SURV_LEG_HEADER`, then inserts selected legend row blocks from the shared server legend block folder on `G-BRDR-ANNO` using Sort Order and the configured single/double spacing rules.
SURVEY-UPDATE-LEGEND / UPDATELEGEND / UPDATE-LEGEND | Survey/SurveyLegendCommands.cs | Command Line / Q4 LABEL (`LEGEND`) | Selects the header from a generated survey legend, reopens the checklist with the previous row-block selections checked, erases the stored legend group, and rebuilds the legend at the original header insertion point.
SURVEY-PLSS-IMPORT-LABELS / PLSSIMPORTLABELS | Survey/SurveyPlssSectionLabelsCommands.cs | Command Line / Q4 LABEL (`PLSS SECTIONS`) | Selects a window or closed-polyline area, reads the matching LVF/LVHEF `GIS_Sections.dwg` marker cache, builds complete PLSS sections from `GIS_SECTION_MARKER` corner markers, and inserts the PLSS section/corner/quarter/sixteenth label blocks at drawing scale with populated section attributes (`01` -> `S1`, `09` -> `S9`, `10` -> `S10`).
MERGE-STRC / UFLS-MERGE-STRC | Ufls/UflsLayerMaintenance.cs | Command Line / Q1 LAYERS | Moves entities from legacy structure layers `V-SURV-STRC-INNER-2D` and `V-SURV-STRC-OUTER-2D` into the standard `V-SURV-STRC-INNR-2D~~` and `V-SURV-STRC-OUTR-2D~~` layers and then attempts to remove the legacy layers.
ReloadLayerStates / UFLS-LAYER-STATES-UPDATE | Ufls/UflsLayerMaintenance.cs | Command Line / Q1 LAYERS | Replaces the legacy LayerStatesUpdate LISP flow by deleting the `LateralCreatePipe` and `PipeCenter` layer states and re-importing them from the standard 2026 Civil 3D `.las` files.
UFLS-REDLINE-NOTE | Ufls/UflsRedlineBlocks.cs | Command Line / Q1 CHECK (`REDLINE`) | Inserts `REDLINE-MTEXT` from the survey blocks folder, then explodes the block in-place.
UFLS-REDLINE-LEADER | Ufls/UflsRedlineBlocks.cs | Command Line / Q1 CHECK (`REDLINE`) | Inserts `REDLINE-LEADER` from the survey blocks folder, then explodes the block in-place.
UFLS-OBJECT-HIGHLIGHT-RED | Ufls/UflsObjectHighlight.cs | Command Line / Q1 CHECK (`VERIFICATION`) | Creates new red highlight overlay geometry on `V-SURV-HGLT-R` without modifying selected source objects. The layer is ensured through `LayerStandards` with 70% layer transparency. Lines/curves/polylines get 0.01-wide by-layer overlay polylines. Plain DBText/MText backgrounds are sent behind text; Civil 3D label-like exploded text backgrounds are moved to the front so label masks do not hide the highlight.
UFLS-OBJECT-HIGHLIGHT-GREEN | Ufls/UflsObjectHighlight.cs | Command Line / Q1 CHECK (`VERIFICATION`) | Creates new green highlight overlay geometry on `V-SURV-HGLT-G` without modifying selected source objects. The layer is ensured through `LayerStandards` with 70% layer transparency. Lines/curves/polylines get 0.01-wide by-layer overlay polylines. Plain DBText/MText backgrounds are sent behind text; Civil 3D label-like exploded text backgrounds are moved to the front so label masks do not hide the highlight.
UFLS-STUB | Ufls/UflsAdjustCommands.cs | Command Line / Q1 CHECK | Places a stub locator marker block on V-SURV-CHCK for pipe-stub adjustment workflows.
UFLS-ADJ-MH-AUTO | Ufls/UflsAdjustCommands.cs | Command Line / Q1 ADJUST | Legacy alias for UFLS-ADJ-MH-ALL. Moves nearby manhole / null structures to the nearest UFLS_MH_MARK or UFLS_STUB_MARK locator block by writing marker XY into the Civil 3D structure position while preserving Z, then updates each attached pipe.
UFLS-ADJ-MH-SINGLE | Ufls/UflsAdjustCommands.cs | Command Line / Q1 ADJUST | User selects one structure; nearest UFLS_MH_MARK or UFLS_STUB_MARK is found automatically and the structure is repositioned in XY while preserving Z, then attached pipes are updated.
UFLS-ADJ-MH-ALL | Ufls/UflsAdjustCommands.cs | Command Line / Q1 ADJUST | Batch version of MH adjust. Includes stub/null-structure markers by scanning both UFLS_MH_MARK and UFLS_STUB_MARK blocks.
UFLS-ADJ-PIPE-ALL | Ufls/UflsAdjustCommands.cs | Command Line / Q1 ADJUST | Batch version of pipe adjust. Supports LINE and 3D POLYLINE surveyed centerlines on V-SURV-PIPE-CNTR.
UFLS-ADJ-PIPE-AUTO | Ufls/UflsAdjustCommands.cs | Command Line / Q1 ADJUST | Legacy alias for UFLS-ADJ-PIPE-ALL. Matches Civil 3D pipes to surveyed LINE or 3D POLYLINE centerlines on layer `V-SURV-PIPE-CNTR`.
UFLS-ADJ-PIPE-SINGLE | Ufls/UflsAdjustCommands.cs | Command Line / Q1 ADJUST | User selects one pipe; the best surveyed LINE or 3D POLYLINE centerline on `V-SURV-PIPE-CNTR` is found automatically and applied.
UFLS-PIPE-PVC-C900 | Ufls/UflsPipeMaterialSwap.cs | Command Line / Q1 ADJUST (`SWAP MATERIAL`) | Selects one Civil 3D pipe, confirms it is currently in family `CLV_PVC`, resolves the same nominal size in family `CLV_C900`, and swaps the pipe while keeping the size.
UFLS-PIPE-RCP-C900 | Ufls/UflsPipeMaterialSwap.cs | Command Line / Q1 ADJUST (`SWAP MATERIAL`) | Selects one Civil 3D pipe, confirms it is currently in family `CLV_RCP`, resolves the same nominal size in family `CLV_C900`, and swaps the pipe while keeping the size.
UFLS-PIPE-C900-RCP | Ufls/UflsPipeMaterialSwap.cs | Command Line / Q1 ADJUST (`SWAP MATERIAL`) | Selects one Civil 3D pipe, confirms it is currently in family `CLV_C900`, resolves the same nominal size in family `CLV_RCP`, and swaps the pipe while keeping the size.
UFLS-PIPE-C900-PVC | Ufls/UflsPipeMaterialSwap.cs | Command Line / Q1 ADJUST (`SWAP MATERIAL`) | Selects one Civil 3D pipe, confirms it is currently in family `CLV_C900`, resolves the same nominal size in family `CLV_PVC`, and swaps the pipe while keeping the size.
SURVEY-BESTFIT-MAP | Survey/SurveyBestFitMap.cs | Command Line / Q4 MAPPING (`TRANSFORM`) | Selects an xref / block reference, collects numbered fixed-survey to moving-map control pairs, previews running fit values, and applies a rigid XY best-fit solution. Finalized transforms save the original block/xref placement plus point-pair/control history in the DWG; selecting the same map later recalls that session so controls can be checked/unchecked, pairs removed, or new pairs added before `Apply Updated Transform`. When saved history is recalled, the map temporarily returns to its original pre-transform placement and the numbered Survey/Map pair markers are recreated; this keeps newly added pairs on the original coordinate basis. Cancel restores the placement that existed when editing began. Reapplication always starts from the saved original placement and writes a new CSV residual report.
CLV_SECTIONAL_IMPORT / CLV_REFERENCE_IMPORT / CLV-REFERENCE-CSV-IMPORT / CLV-SECTIONAL-CSV-IMPORT / SURVEY-REFERENCE-CSV-IMPORT | Shared/ReferenceCsvImportCommands.cs | Command Line / Q4 MAPPING (`BOUNDARY`) | Imports Prompt 1 sectional/control framework CSV files, preserving Prompt 1/Prompt 4 orange-POC / 10000,10000 anchored coordinates and length-adjustment QA fields; draws endpoint-controlled LINE/CURVE geometry directly from CSV Start/End coordinates when supplied, imports POINT_TIE rows as points, and draws RADIAL, OFFSET, and NOTE rows on layer `0`, creates street-name labels only, and writes a `_sectional_import_report.txt`.
SURVEY-AUTO-CLOSURE | Survey/SurveyAutoClosure.cs | Command Line / Q4 MAPPING (`BOUNDARY`) | Phase 3 boundary auto-closure. Select multiple standalone LINE/ARC entities or one open lightweight POLYLINE with straight/bulged arc segments, pick the traverse start point, review expanded closure QA/tangency/area report and tolerance warnings, then create adjusted overlay copies on `V-SURV-MAP~-ADJ~` while moving originals to `V-SURV-MAP~-ORIG`. Default adjustment preserves original line bearings and applies closure correction only by changing straight segment lengths. Curve bulges/radii are held and translated with the adjusted chain. In-session constraints may be applied to standalone LINE/ARC runs before adjusted output is created.

SURVEY-CLOSURE-CONSTRAINTS | Survey/SurveyClosureConstraints.cs | Command Line / Q4 MAPPING (`BOUNDARY`) | Opens the WinForms Closure Constraints dialog. Supports `LOCK RADIUS`, `LOCK BEARING`, `LOCK LENGTH`, `KEEP PARALLEL`, `PARALLEL TO REFERENCE`, `OFFSET TO REFERENCE` (current or user-specified offset), and `PERPENDICULAR TO REFERENCE`; selected/control objects are highlighted for review.
SURVEY-CLOSURE-ADD-CONSTRAINT | Survey/SurveyClosureConstraints.cs | Command Line | Alias command that opens the Closure Constraints dialog.
SURVEY-CLOSURE-LIST-CONSTRAINTS | Survey/SurveyClosureConstraints.cs | Command Line | Lists in-session boundary constraints that will be considered by the next standalone LINE/ARC AUTO CLOSURE run.
SURVEY-CLOSURE-CLEAR-CONSTRAINTS | Survey/SurveyClosureConstraints.cs | Command Line | Clears in-session boundary constraints and restores constraint-highlighted entity color indexes where possible.

NM_AERIAL | Gis/Aerials.cs | Command Line / GIS Palette | Loads aerial imagery from the configured `.layer` workflow. Unload now also attempts to remove matching Nearmap / LasVegas map connections from the active map session.
PCT1 | PoinCloud/PctCommands.cs | Command Line / Q3 Palette | Creates sample lines.
PCT2 | PoinCloud/PctCommands.cs | Command Line / Q3 Palette | Crops point cloud to selected sample-line workflow.
PCT2R | PoinCloud/PctCommands.cs | Command Line / Q3 Palette | Uncrops the sample-line crop workflow.
PCT3 | PoinCloud/PctCommands.cs | Command Line / Q3 Palette | Creates the cross-section view workflow.
PCT3R | PoinCloud/PctCommands.cs | Command Line / Q3 Palette | Resets the cross-section view workflow.
PCT4 | PoinCloud/PctCommands.cs | Command Line / Q3 Palette | Quick sample-line crop workflow.
PCT4R | PoinCloud/PctCommands.cs | Command Line / Q3 Palette | Resets quick sample-line crop state.
PCT5 | PoinCloud/PctCommands.cs | Command Line / Q3 Palette | Moves selected points to a sample line.
PCT6 | PoinCloud/PctCommands.cs | Command Line / Q3 Palette | Move-points-plus-add-vertex workflow for single / multiple points.
PCT7 | PoinCloud/PctCommands.cs | Command Line / Q3 Palette | Turns point cloud display ON.
PCT8 | PoinCloud/PctCommands.cs | Command Line / Q3 Palette | Turns point cloud display OFF.
PCT9 | PoinCloud/PctCommands.cs | Command Line / Q3 Palette | Moves selected points to a target polyline vertex.
PCT10 | PoinCloud/PctCommands.cs | Command Line / Q3 Palette | Identifies / labels vertices.
PCT11 | PoinCloud/PctCommands.cs | Command Line / Q3 Palette | Attach point-cloud workflow.
PCT11I | PoinCloud/PctCommands.cs | Command Line / Q3 Palette | Point-cloud intensity helper.
PCT12 | PoinCloud/PctCommands.cs | Command Line / Q3 Palette (UFLS tab) | Locate pipe workflow.
PCT13 | PoinCloud/PctCommands.cs | Command Line / Q3 Palette (UFLS tab) | Sets UCS for the UFLS locator workflow.
PCT14 | PoinCloud/PctCommands.cs | Command Line / Q3 Palette | General crop command used in roadway and UFLS flows.
PCT15 | PoinCloud/PctCommands.cs | Command Line / Q3 Palette (UFLS tab) | Rotates the view for the UFLS locator workflow.
PCT16 | PoinCloud/PctCommands.cs | Command Line / Q3 Palette | General reset / uncrop command used in roadway and UFLS flows.
PCT17 | PoinCloud/PctCommands.cs | Command Line / Q3 Palette | Adjacent move-points-plus-add-vertices workflow.
PCT17V | PoinCloud/PctCommands.cs | Command Line / Q3 Palette | Copies a vertex from one polyline to another.
PCT18 | PoinCloud/PctCommands.cs | Command Line / Q3 Palette | 3D rotate / orbit view workflow.
PCT18R | PoinCloud/PctCommands.cs | Command Line / Q3 Palette | Resets the 3D rotate / orbit view workflow.
PCT19 | PoinCloud/PctCommands.cs | Command Line / Q3 Palette | General marker helper.
PCT20 | PoinCloud/PctCommands.cs | Command Line / Q3 Palette | Adds a vertex at a crossing.
UFLS1 | Ufls/UflsTopOfPipe.cs | Command Line / Q1 Palette | Creates top-of-pipe 3D linework from ordered COGO picks, keeps the Civil-style 2D best-fit alignment, aligns station/elevation direction to pick order, checks adjacent picked-shot slopes for 0.5 percentage-point grade changes before line creation, redline-boxes flagged break points on `V-SURV-RDLN`, extends to structure inner-wall geometry, and auto-pans ahead after the first two picks using the first pick-area zoom level and a dynamic search width based on current view size and recent pick spacing.
UFLS4 | Ufls/UflsManholeAutoCreate.cs | Command Line / Q1 Palette dialog | Auto-creates 3P manholes from clustered survey shots.
UFLS41P | Ufls/UflsManholeAutoCreate.cs | Command Line / Q1 Palette dialog | Auto-creates 1P manholes from clustered survey center-point shots.
UFLS-MH-AUTO | Ufls/UflsManholeAutoCreate.cs | Command Line / Legacy Alias | Alias to the UFLS4 manhole auto-create workflow.
UFLS-MH-AUTO-1P | Ufls/UflsManholeAutoCreate.cs | Command Line / Legacy Alias | Alias to the UFLS41P single-point manhole auto-create workflow.
UFLS5 | Ufls/UflsTopOfPipe.cs | Command Line / Q1 Palette | Trims top-of-pipe linework.
UFLS-PIPE-LABEL-3D | Ufls/UflsPipeLabels.cs | Command Line / Q1 Palette (`LABEL INVERT`) | Selects one 3D polyline and places START INVERT / END INVERT elevation text on layer V-SURV-PIPE-INVT with the label rotation aligned to the pipe direction.
UFLS-PIPE-INFO | Ufls/UflsPipeInfo.cs | Command Line / Q1 Palette (`PIPE INFO @ POINT`) | Reports Civil 3D pipe information and computed elevation values at the picked point.
UFLS6 | Ufls/UflsSingleManhole.cs | Command Line / Q1 Palette | Creates a 3P manhole from 3 COGO points.
UFLS61P | Ufls/UflsSingleManhole.cs | Command Line / Q1 Palette | Creates a 1P manhole from 1 COGO center point.
UFLS6PC | Ufls/UflsSingleManhole.cs | Command Line / Q3 Palette (UFLS tab) | Creates a manhole from 3 picked / snapped points.
UFLS7 | Ufls/UflsStructure.cs | Command Line / Q1 Palette | Traces the INNER wall footprint and auto-creates the OUTER wall using the selected wall thickness.
UFLS7PC | Ufls/UflsStructure.cs | Command Line / Q3 Palette (UFLS tab) | Traces the INSIDE wall footprint using point-cloud 3D osnap settings, then auto-creates the OUTSIDE wall.
UFLS8 | Ufls/UflsStructure.cs | Command Line / Q1 Palette | Traces the OUTER wall footprint and auto-creates the INNER wall using the selected wall thickness.
UFLS-DROP-INLET | Ufls/UflsDropInlet.cs | Command Line / Q1 Palette | Native .NET drop-inlet creation workflow.
UFLS-3PCIRCLE | Ufls/UflsStructure.cs | Command Line / Q3 Palette (UFLS tab) | Creates a 3-point circle on layer 0 at elevation 0.0 with temporary pick markers and snaps OFF.
UFLS-3PRECT | Ufls/UflsStructure.cs | Command Line / Q3 Palette (UFLS tab) | Creates a square 3-point orthogonal closed polyline on layer 0 at elevation 0.0 with temporary pick markers and snaps OFF.
DI_CENTER_TEST | Ufls/UflsDropInlet.cs | Command Line | Helper / test command for drop-inlet center logic.
UFLS-REVCLOUD | Ufls/UflsPalette.cs | Command Line / Q1 Palette | Wrapper command that prompts for cloud size and then calls the matching legacy revcloud routine.
UFLS-LATERAL-SINGLE | Ufls/UflsLaterals.cs | Command Line / Q1 Palette | Single lateral workflow: ordered COGO-point selections from lot toward main, uses the last selected point as main reference only, offsets only the lateral shots, projects the connection to the nearest main centerline on V-SURV-PIPE-CNTR, creates a 3D lateral centerline, and adds the lateral directly into the existing sewer network whose name contains -SSWR-E using CLV_PVC 4" PVC parts from the assigned parts list.
UFLS-LATERAL-ALL | Ufls/UflsLaterals.cs | Command Line / Q1 ADJUST Palette | Auto lateral workflow: finds WYE shots by description, builds a perpendicular search line from the nearest main, prompts for a user-defined search half-width and search half-length, auto-creates one-sided groups, and sends ambiguous two-sided groups to aligned QA polygons on V-SURV-RDLN for manual review.
SD-JUNCTION-SIZE | Ufls/UflsSdJunctionSizeTest.cs | Command Line / Q1 ADJUST (`STORM JUNCTION STRUCTURE`) | Test workflow for SD-JUNCTION structures: reads a closed inner-wall polyline, computes width / length, ensures the closest matching built-in size exists in the selected structure family / parts list, and attempts to swap the selected structure to that size.
UFLS-ADJ-SD-SINGLE / UFLS-ADJ-JNCT-SINGLE | Ufls/UflsAdjustCommands.cs | Command Line / Q1 ADJUST | Moves one selected storm-drain junction structure to the matched closed inside-wall polyline target on `V-SURV-STRC-INNR-2D~~`, applies best-fit footprint rotation, and allows an optional explicit target-polyline pick for alignment.
UFLS-ADJ-SD-ALL / UFLS-ADJ-JNCT-ALL | Ufls/UflsAdjustCommands.cs | Command Line / Q1 ADJUST | Scans closed inside-wall polylines on `V-SURV-STRC-INNR-2D~~`, finds nearby storm-drain junction structures, and moves / rotates them to the footprint targets.
UFLS-ADJ-DI-SINGLE | Ufls/UflsAdjustCommands.cs | Command Line / Q1 ADJUST | Moves one selected drop inlet structure to the nearest `UFLS_DI_MARK` marker and applies the marker rotation.
UFLS-ADJ-DI-ALL | Ufls/UflsAdjustCommands.cs | Command Line / Q1 ADJUST | Scans `UFLS_DI_MARK` markers, finds nearby drop inlet structures, and moves / rotates them to those marker targets.



PDFVIEW | PdfViewer/PdfViewerCommands.cs | Command Line / Q4 Palette (`PDF VIEWER`) | Opens the dockable Map Review PDF Viewer, supports named plan mappings and reference categories, follows model-space pan/zoom, and provides Return to Current.
PDFC | Native AutoCAD command | Q4 > MAPPING > TOOLS (`PDF CLIP`) | Starts the native PDF clipping command.
MAPREVIEW | PdfViewer/PdfViewerCommands.cs | Command Line | Alias for `PDFVIEW`.

------------------------------------------------------------
PALETTE NOTES
------------------------------------------------------------

- `UFLS.Q1` / `Q1` (legacy alias `UFLS.Q11`), `Q2`, `PCT.Q3`, and `Q4` are the primary in-CAD palette entry commands.
- Palette buttons dispatch the same command names through `SendStringToExecute`, keeping command-line and palette access aligned.
- Current command-launcher palette standard for future Q-menu work: 7.0 pt button/section text, 256 px command rows, 24 px row height, per-tab vertical scrolling enabled, and no horizontal scrolling. Default/minimum sizes are Q1 `360 x 760` / `320 x 560`, Q2 `340 x 700` / `300 x 540`, Q3 `320 x 600` / `280 x 480`, Q4 `340 x 700` / `300 x 540`, and GIS CREATE DATA `340 x 700` / `300 x 540`. New PaletteSet hosts should call `Shared/PalettePositionHelper.ConfigureSize(...)` during creation and `ShowNearAutoCadWindow(...)` when opened. The helper applies the CAD-window-relative startup position only once per Civil 3D session for each PaletteSet, then leaves the palette at the user-moved session location on later command calls. First-open offsets are staggered by menu group: Q1/UFLS starts at the base point, Q2/GIS offsets down/right, Q3/PCT offsets farther down/right, Q4/SURVEY offsets farther down/right again, and GIS CREATE DATA opens farther right of Q2.
- Q1 now includes a `LAYERS` tab with `REMOVE DUPLICATE STRC LAYERS` and `UPDATE LAYER STANDARDS`.
- `UFLS-MH-DIALOG` is palette-internal routing handled in `UflsPalette.cs`; it launches the 3P manhole options dialog and then calls `UflsManholeAutoCreate.RunFromPalette(...)`.
- `UFLS-MH1P-DIALOG` is palette-internal routing handled in `UflsPalette.cs`; it launches the 1P manhole options dialog and then calls `UflsManholeAutoCreate.RunFromPalette1P(...)`.
- `UFLS-DROP-INLET` is handled as a direct native .NET call from the palette button, even though the same command is also callable from the command line.
- Q1 ADJUST now uses `SEWER MAIN - MOVE`, `SEWER LATERAL`, `STORM JUNCTION STRUCTURE`, `STORM DRAIN - MOVE`, and `SWAP MATERIAL`, with `RESIZE JUNCTION` separated from the storm-drain move tools and the four pipe material swap commands grouped together. Q4 now hosts the survey-map registration prototype. 
- Q1 CHECK now labels the manhole buttons as `3P MANHOLE - ALL`, `3P MANHOLE - SINGLE`, `1P MANHOLE - ALL`, and `1P MANHOLE - SINGLE`, keeps `STRC-INNER WALL` / `STRC-OUTER WALL` directly beneath them, retains `STUB MARKER`, and keeps `3P CIRCLE`, `3P RECTANGLE`, `NOTE`, and `LEADER`.

------------------------------------------------------------
NEW IN THIS REVISION
------------------------------------------------------------

- Added shared CAD-window-relative initial placement for Q1/UFLS, Q2/GIS, Q3/PCT, Q4/SURVEY, GIS CREATE DATA, and SURVEY PHOTO REVIEW palettes through `Shared/PalettePositionHelper.cs`. Q2/Q3/Q4 first-open offsets are staggered so newly opened menus are easier to see. SURVEY PHOTO REVIEW also exposes `VIEWPHOTOS` and `VIEW-PHOTOS` aliases for CLV menu launch paths.
- Applied the compact CAD palette sizing/text standard to Q1, Q2, Q3, Q4, and GIS CREATE DATA. This is a presentation-only change to PaletteSet defaults, command-row sizing, and control fonts; command routing is unchanged.

- Added `Ufls/UflsLayerMaintenance.cs` with `.NET` replacements for the legacy `MERGE-STRC` and `LayerStatesUpdate` LISP routines.
- Added `Ufls/UflsRedlineBlocks.cs` with `UFLS-REDLINE-NOTE` and `UFLS-REDLINE-LEADER` block insert-and-explode commands.
- Added Q11 `LAYER MAINTENANCE` tab with `REMOVE DUPLICATE STRC LAYERS` and `UPDATE LAYER STANDARDS` buttons.
- Updated Q11 CHECK `REDLINE` with `NOTE` and `LEADER`, moved structure trace buttons into `2D LINEWORK`, renamed `STUB` to `STUB MARKER`, and added `3P CIRCLE` / `3P RECTANGLE`.
- Updated Q3 so all button captions are uppercase, `GENERAL MARKER` is centered on the UFLS tab, `TRACE INSIDE` now reads `STRC-INNER WALL`, and `LOCATE MANHOLE` now sits directly below it.
- Updated `UFLS-DROP-INLET` source-form button text from `IN-HOUSE` / `OTHERS` to `4 POINT` / `2 POINT`.

### Maintenance Notes

- Current maintenance pass focused on compile cleanup, nullable cleanup, Autodesk reference-warning suppression, and the `UFLS7` / `UFLS8` Enter-key point-input completion fix without changing command names or palette routing.

- Maintenance: `UFLS-LATERAL-SINGLE` was compile-stabilized after initial introduction by removing the `Label` type ambiguity and correcting `Polyline3d` point collection creation.

- Maintenance: `UFLS-LATERAL-SINGLE` now auto-selects the nearest main centerline on `V-SURV-PIPE-CNTR`, creates the 3D lateral centerline, and adds the lateral directly into the existing sewer network without creating a temporary lateral network or splitting the main pipe.


## 2026-03-09.10 Maintenance

- Resolved the `Ufls/UflsLaterals.cs` ambiguous `DBObject` compile error by explicitly using the AutoCAD `DBObject` type for parts-list size reflection.

- `UFLS-LATERAL-SINGLE` - Creates 3D lateral centerline geometry and adds pipe segments directly into the target sewer network (`-SSWR-E`) using Civil 3D pipe-network APIs.

- `UFLS-LATERAL-SINGLE`
  - Manual lateral workflow that creates the 3D lateral centerline and adds pipe segments into the existing sewer network. Current implementation uses reflection-based network access for Civil 3D compile compatibility.


## 2026-03-09 UI / Manhole / Adjust updates
- `UFLS6` — 3P single manhole create from 3 picked COGO points; infers 48/60/72 visibility from picked point descriptions when available.
- `UFLS61P` — 1P single manhole create from one picked COGO center point; uses the picked point directly as the insertion center and infers 48/60/72 visibility from that point description when available.
- `UFLS-ADJ-PIPE-AUTO` / `UFLS-ADJ-PIPE-ALL` — Auto-matches Civil 3D pipes to surveyed LINE or 3D POLYLINE centerlines on `V-SURV-PIPE-CNTR`.
- `UFLS-ADJ-PIPE-SINGLE` — User selects one Civil 3D pipe; the best surveyed LINE or 3D POLYLINE centerline on `V-SURV-PIPE-CNTR` is found automatically and applied.
- `UFLS-ADJ-MH-AUTO` / `UFLS-ADJ-MH-ALL` — Auto-moves nearby sewer structures, including stub/null-structure targets, to `UFLS_MH_MARK` and `UFLS_STUB_MARK` blocks using direct structure-position writes.
- `UFLS-ADJ-MH-SINGLE` — User selects one sewer structure; nearest `UFLS_MH_MARK` or `UFLS_STUB_MARK` is found automatically and the structure XY is updated while preserving Z.

- `SD-JUNCTION-SIZE` / `UFLS-SD-JUNCTION-SIZE` - Reads a closed inner-wall polyline, ensures a matching SD-JUNCTION part size in the active structure family, preserves the source structure style where possible, and swaps the selected structure to the matched size.


## 2026-03-11 - palette width standardization + Q1 rename
- Standardized palette widths across Q2 / Q3 / Q1 so button layouts stay visually uniform.
- Widened the Q3 button rows so the `TRANSPARENCY` label stays on one line.
- Renamed the primary UFLS palette command from `UFLS.Q11` to `UFLS.Q1` and kept `UFLS.Q11` as a legacy alias.
- Renamed the UFLS layers tab from `LAYER MAINTENANCE` to `LAYERS`.
- Centered all single-button captions across the palette UIs.


## 2026-03-12 - GIS import setup
- `Q2` - Opens the GIS palette. The GIS tab includes `DATA` > `IMPORT GIS`, coordinate-system reference tools, HTML survey report, and XDATA/cache inspection tools. The import dialog offers both GIS datasets (Parcels, Street Centerlines, Sewer Pipes, Sewer Structures, Storm Pipes, Storm Structures) and SURVEYED cache datasets (Sewer Pipes, Sewer Structures, Storm Pipes, Storm Structures). `OD INSPECT` is no longer shown on the palette.
- `CLV-GIS-IMPORT` - Imports selected CLV GIS datasets into the current drawing. The command always uses prebuilt dataset DWG cache first when available, then falls back to direct SHP import, clips to the selected boundary, syncs / corrects GIS layer settings, and runs duplicate cleanup.
- `CLV-GIS-CACHE-STATUS` - Reports expected cache DWG paths for the active drawing coordinate system, shows the raw Map 3D coordinate-system value when available, and confirms whether the optional GIS layer-master DWG is available.
- `CLV-GIS-CLEANUP` - Removes exact duplicate imported GIS linework on the CLV GIS line layers after import.
- `CLV-GIS-OD-INSPECT` - Loads `CLV_GIS_OD_HELPERS.lsp` only from `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp` and launches the ADE-backed `CLV-GIS-OD-INSPECT-LSP` routine so OD diagnostics reflect the same Object Data tables and fields shown in the Map 3D Properties palette.
- `CLV-GIS-OD-COPY` - ADE/LISP helper command resolved only from the shared Civil 3D Lisp server path for manual Object Data copy from one entity to another when users want to verify or repair GIS OD outside the join routine.
- `CLV-GIS-OD-XFER` - .NET wrapper that loads `CLV_GIS_OD_HELPERS.lsp` from the shared Civil 3D server path, then launches the ADE/LISP `CLV-GIS-OD-COPY` workflow so users pick a source object and then a destination object entirely through the helper routine.
- `CLV-GIS-DI-EXPLODE` - Prompts for a drop inlet block, explodes it in place, deletes marker / curb / text remnants, moves the exploded inner/outer linework onto `C-STRM-STRC-INNR` and `C-STRM-STRC-E`, then queues ADE/LISP OD copy from the aligned imported `Structures` point to all eligible exploded outer structure entities created from the selected drop inlet block.
- `CLV-GIS-JS-FROM-POINT` - Prompts for a single imported `Structures` point, automatically finds the paired closed junction-structure outer and inner polylines around that point, moves them to `C-STRM-STRC-E` and `C-STRM-STRC-INNR`, then queues ADE/LISP OD copy from the selected point to the detected outer polyline.


### GIS import notes
- CLV-GIS-IMPORT now defaults to cache-first behavior with no cache checkbox in the dialog.
- The import dialog no longer shows the cache note text and now starts with Sewer Pipes, Sewer Structures, Storm Pipes, and Storm Structures unchecked.
- Cache selection now probes drawing GeoData first and then broader Map 3D coordinate/projection members, normalizes the detected drawing coordinate system to the CLV cache folders (`NV83.NCRS-LVF` vs `NV83.NCRS-LVHEF`), and then chooses the dataset DWG.
- The import dialog was enlarged so the dataset list and OK / CANCEL buttons are visible in the palette workflow.
- GIS layers are synced from the optional CLV GIS layer-master DWG at `\ci.las-vegas.nv.us\pw_data_depot\PW_GRID_City\000-City\GIS\_CLV_CACHE\_MASTER\CLV_GIS_LAYER_MASTER.dwg` when available, then corrected with fallback CLV layer settings.
- Structure cache imports now sync `PDMODE` and `PDSIZE` from the source cache DWG so imported points display like the cache/source drawing.
- Cache clipping uses boundary-extents prechecks to reduce unnecessary curve splitting and improve large-dataset import speed.
- GIS UI WinForms layout uses explicit System.Windows.Forms.FlowDirection aliases to avoid AutoCAD namespace conflicts.



- GIS palette commands that require interactive picks now attempt to push focus back to the drawing view before prompting so the pickbox appears without an extra click in most cases.


## SURVEY-AUTO-CLOSURE
- Q4 Survey Mapping Phase 3 boundary closure assistant.
- Palette: Q4 > MAPPING > BOUNDARY > `AUTO CLOSURE`.
- Phase 3 adds Q4 > MAPPING > BOUNDARY button: `CONSTRAINTS`. The dialog manages in-session constraints, highlights constrained LINE/ARC geometry, and lists the active rules before running AUTO CLOSURE.
- Supports selected straight AutoCAD LINE chains or one open lightweight POLYLINE with straight and bulged arc segments.
- Prompts for a traverse start point after linework selection.
- Default adjustment preserves original line bearings and applies the closure correction by changing only straight segment lengths; curve bulges/radii are held and translated with the adjusted chain.
- Reports existing misclosure, traverse length, start point, end point, closure vector bearing, relative precision ratio, PPM error, max segment length change, max bearing change, worst adjusted segment, and total absolute bearing adjustment before asking for confirmation.
- Moves original selected linework to `V-SURV-MAP~-ORIG` and creates the adjusted overlay on `V-SURV-MAP~-ADJ~` instead of directly altering the original geometry.
- Does not use or create LISP helpers.

- Q4 Survey Mapping review tools for AUTO CLOSURE output.
- Palette: Q4 > MAPPING > REVIEW.
- Provides in-session modelspace report, marker, goto, and clear-review tools for the last AUTO CLOSURE run. The report includes original/adjusted/delta area values, arc radius values, offset constraint values, and numbered review markers using one consistent size per closure run.
- Does not use or create LISP helpers.

## SURVEY-BESTFIT-MAP
- Q4 `MAP TRANSFORM` routine for rigid XY best fit of a survey map/xref to field control points.
- The initial run collects Survey/Map point pairs and stores the original map/xref insertion point and rotation when the transform is finalized.
- Finalized pair history is persisted in the selected block reference extension dictionary as `CLV_MAP_TRANSFORM_SESSION`, including Survey/Map coordinates, labels, survey entity handle when available, and the CONTROL state.
- Selecting a map/xref that already has saved history opens the same review grid with the prior pairs restored. CONTROL rows can be checked/unchecked, `Remove Selected` deletes unwanted pairs, and `Add Pair` returns to Civil 3D for one additional Survey/Map selection before reopening review.
- New map points added after a prior transform are inverse-mapped to the stored original map basis so all pairs remain in one consistent coordinate system.
- `Apply Updated Transform` recomputes and sets the map/xref from its saved original placement rather than transforming the already-transformed placement again, preventing cumulative transform error.
- At least two CONTROL rows are required. Each completed application writes the normal CSV residual report.
- No separate redo/new-transform command or button is used.

## GIS
- `CLV-GIS-PIPE-OFFSET-OD`
  - Select imported pipe centerlines, read Map Object Data field `InsideDiameter`, and create offsets on both sides at `InsideDiameter / 2` when the diameter is `1.0'` (12") or larger.

- `CLV-GIS-PIPE-OFFSET-OD` loads the shared ADE OD helper from the server path and uses ADE OD access instead of relying on the .NET OD reflection path.


- `CLV-GIS-PIPE-OFFSET-OD`
  - Runs the basic Map OD pipe offset routine through the shared helper at `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\CLV_GIS_PIPE_OD_OFFSET.lsp`, with no local packaged fallback. The .NET wrapper now follows the same pattern as `CLV-GIS-OD-INSPECT`: it queues the helper LISP load first, then launches `CLV-GIS-PIPE-OFFSET-OD-LSP` as a separate command-line command.


- `CLV-GIS-STRM-AUTO` — Batch storm-structure automation. Pass 1 converts supported drop inlet blocks to GIS structure linework and queues OD copy. Pass 2 processes remaining junction structures from remaining `Structures` points.

- `CLV-GIS-PIPE-OFFSET-OD` — reads GIS Object Data `InsideDiameter`, offsets qualifying pipe centerlines to both sides, moves centerline to `C-STRM-PIPE-CNTR-E`, moves created wall offsets to `C-STRM-PIPE-E`, and attempts automatic endpoint connection to nearby `C-STRM-STRC-INNR` structure walls.


- `CLV-GIS-STORM-GIS` - Runs storm structures auto, then runs the all-pipes storm pipe OD offset pass.
- `CLV-GIS-PIPE-OFFSET-OD-ALL` - Loads the shared pipe OD helper and runs the all-pipes storm pipe offset command.
- `Q1` - UFLS palette GIS tab now groups buttons under `STORM DRAIN`, `SEWER`, `OBJECT DATA`, `CLEANUP`, and `EXPORT`. The storm buttons are `GIS PREP - ALL`, `JUNCTIONS AND INLETS - ALL`, `DROP INLET - SINGLE`, `JUNCTION STRUCTURE - SINGLE`, and `PIPE`; the sewer section exposes `GIS PREP - ALL`, `MANHOLE`, and `PIPE`; the new `OBJECT DATA` section exposes `XFER OBJECT DATA`; `ERASE POINTS` remains under `CLEANUP`; and `FINALIZE STRUCTURES` / `FINALIZE PIPES` live under the `EXPORT` section.
- `Q2` - Retired `JOIN CENTERLINES` is no longer defined or shown on the GIS palette.

- `CLV-GIS-TRIM-INSIDE` - CREATE DATA > TOOLS > `TRIM INSIDE`. Select one closed structure boundary, then trims only storm/sewer pipe wall linework on `C-STRM-PIPE-E` and `C-SSWR-PIPE-E` inside that boundary. Runs single-boundary mode directly; centerline layers such as `C-STRM-PIPE-CNTR-E` and `C-SSWR-PIPE-CNTR-E` are ignored.
- `CLV-GIS-PIPE-EXTEND` - Select one pipe wall near the end to adjust; moves the nearest endpoint to the closest `C-STRM-STRC-INNR` wall.
- `CLV-GIS-PIPE-TRIM` - Select one pipe wall near the end to adjust; moves the nearest endpoint to the closest `C-STRM-STRC-INNR` wall.


- `CLV-GIS-ERASE-POINTS` — Erase imported structure point objects on layer `Structures`.

### Survey Auto Closure Review

| Command | Palette | Description |
|---|---|---|
| `SURVEY-CLOSURE-ADD-CONSTRAINT` | Q4 > MAPPING > BOUNDARY | Adds an in-session constraint for the next standalone LINE/ARC AUTO CLOSURE run. |
| `SURVEY-CLOSURE-LIST-CONSTRAINTS` | Q4 > MAPPING > BOUNDARY | Lists current in-session closure constraints. |
| `SURVEY-CLOSURE-CLEAR-CONSTRAINTS` | Q4 > MAPPING > BOUNDARY | Clears current in-session closure constraints. |
| `SURVEY-CLOSURE-REPORT` | Q4 > MAPPING > REVIEW | Opens the in-session AUTO CLOSURE segment report window. |
| `SURVEY-CLOSURE-MARKERS` | Q4 > MAPPING > REVIEW | Creates same-size numbered review markers on `V-SURV-MAP~-REVIEW`. |
| `SURVEY-CLOSURE-GOTO` | Q4 > MAPPING > REVIEW | Prompts for a segment number, selects original/adjusted geometry, and zooms to it. |
| `SURVEY-CLOSURE-CLEAR-REVIEW` | Q4 > MAPPING > REVIEW | Removes review marker objects from `V-SURV-MAP~-REVIEW`. |

- `SURVEY-AUTO-CLOSURE` - Phase 2C Fix 2: tangent ARC corners between LINEs are adjusted as true fillets for review output.

- `SURVEY-CLOSURE-ADD-CONSTRAINT` / `SURVEY-CLOSURE-LIST-CONSTRAINTS` / `SURVEY-CLOSURE-CLEAR-CONSTRAINTS` - Phase 3 in-session closure constraints for standalone LINE/ARC boundary runs.

### Survey Auto Closure Phase 3A Notes
- `SURVEY-CLOSURE-CONSTRAINTS` supports stackable in-session constraints. Applying LOCK BEARING and LOCK LENGTH to the same segment now creates a fixed-vector segment. Phase 3B adds reference constraints for parallel, fixed offset, and perpendicular control geometry.

### AUTO CLOSURE Phase 3A Fix 1
- `SURVEY-CLOSURE-CONSTRAINTS`: stackable `LOCK BEARING` + `LOCK LENGTH` fixed-vector behavior now has priority over tangent arc fillet trimming during `SURVEY-AUTO-CLOSURE`.

- `CLV-PHOTO-REVIEW` / `VIEWPHOTOS` / `VIEW-PHOTOS` - Opens SURVEY PHOTO REVIEW using the shared CAD-window-relative first-open/session-preserved PaletteSet placement. The photo-review map preview uses OpenStreetMap/Esri tile downloads with TLS/proxy-aware request handling and keeps `OPEN MAP` routed to the external Google Maps pin.


- DRAW TIE LINE now restores the user's previous current layer after the native LINE command ends or is cancelled.

## Prompt 3 / Subdivision linework updates

- `CLV_SUBDIV_SITE_SETTINGS` / `SUBDIV-SITE-SETTINGS` / `SUBDIVISION-SITE-SETTINGS` — Opens subdivision project defaults for typical road width, cul-de-sac radius, cul-de-sac tie-in radius, and curb return radius.
- `CLV_SUBDIV_ROADS` / `SUBDIV-ROADS` / `SUBDIVISION-ROADS` — Generates road-edge offsets from selected centerline geometry using the current SITE SETTINGS typical road width. Legacy road-edge aliases remain supported.
- `CLV_SUBDIV_CULDESAC` / `SUBDIV-CUL-DE-SAC` / `SUBDIV-CULDESAC` / `SUBDIVISION-CUL-DE-SAC` — Creates a cul-de-sac bulb circle from a picked centerline endpoint using editable defaults.
- `CLV_SUBDIV_INTERSECTION` / `SUBDIV-INTERSECTION` / `SUBDIVISION-INTERSECTION` — First-pass straight-road intersection return arc helper using the SITE SETTINGS curb return radius. Legacy intersection-return aliases remain supported.
- `CLV_SUBDIV_LOT_LINES` / `SUBDIV-LOT-LINES` / `SUBDIVISION-LOT-LINES` — Creates repeated lot-line offsets at a typical spacing toward a selected stop point. Legacy LOT LINES aliases remain available.

### LEGALDESC / CLV-LEGAL-DESCRIPTION — POC Tie and Text Styles (updated 2026-07-22)
When POC and POB are separate, the command now prompts for connected LINE/ARC tie courses from the selected POC endpoint to the POB. The palette lists tie courses as T1, T2, etc. before boundary courses and generates the commencement calls from that geometry. The Text Style dropdown selects built-in or office-defined wording embedded in the DLL from the project source file `Reference/LegalDescriptionTextStyles.json`. Tie courses do not affect boundary closure or area.

### LEGALDESC wording library update (2026-07-22)
- `DESCRIPTION OPTIONS` stores the introductory paragraph, POC description, POB description, and optional area-statement override.
- Text-style presets control commencement, final tie/POB, boundary closing, curve, and area wording.
- Generated legal text and linked MText are ALL CAPS.

### Legal Description palette actions
- `EXPORT LEGAL DOCX` — prompts for document metadata and creates a formatted Word document from the City Surveyor template. This is a palette action within `LEGALDESC`, not a separate AutoCAD command.

### LEGALDESC wording enhancements
The existing `LEGALDESC` / `CLV-LEGAL-DESCRIPTION` workflow now includes automatic curve classification, independent commencement/POB wording selections, and course-level relationship phrases. No new command alias was added.

### LEGALDESC option update (2026-07-22)
- Existing `LEGALDESC` palette now retains Description Options selections and supports SF, ACRES, or BOTH for area output. No command aliases changed.

- `LEGALDESC`: Description Options retain saved wording selections and provide square-feet/acres/both area output choices.
- `LEGALDESC` area output uses thousands separators for generated square-foot values.

### LEGALDESC saved-session startup

`LEGALDESC` checks the drawing Named Objects Dictionary for the saved legal-description session. If one exists, it prompts `[Open/New] <Open>` before any entity selection. `Open` restores the session in the palette; `New` begins a replacement session. `LEGALDESC-OPEN` remains available as the direct reopen command.


### LEGALDESC palette review update (2026-07-22)
- Existing `LEGALDESC` and `LEGALDESC-OPEN` commands now provide split travel/destination relationship fields and synchronized CAD/text highlighting. No command aliases were added.

- LEGALDESC curve review now displays independent Curve IN and Curve OUT tangency classifications and reports start/end radial bearings when the applicable connection is non-tangent.

### LEGALDESC — Structured City Surveyor Land Description and DOCX Formatting
- Description Options now provides structured quarter, section, township, and range values for the standard City Surveyor Land Description paragraph.
- DOCX export preserves right-side date, BY, and P.R. BY placement from the embedded Word template.
- Standalone `BEGINNING` text is no longer automatically bolded; complete approved phrases remain formatted.

### Legal Description DOCX formatting note (2026-07-22)
- `LEGALDESC` / `CLV-LEGAL-DESCRIPTION`: `EXPORT LEGAL DOCX` now preserves the exact embedded City Surveyor Word-template layout and Arial 12-point formatting.


### MAP TRANSFORM history verification
- `SURVEY-BESTFIT-MAP` / `UFLS-BESTFIT-MAP`: revised editable-history build identifies itself as `2026.08.06-HISTORY-R3` at command start, temporarily restores recalled maps to their original pre-transform placement while editing, recreates numbered saved-pair markers, and verifies history persistence after Finalize.
