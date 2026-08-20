## 2026-08-20 - Q1 Pipe Top Check
- Added `UFLS-PIPE-TOP-CHECK` / Q1 UFLS > CHECK > INFO > `PIPE TOP CHECK`. The command selects a Civil 3D COGO point first, uses that point's X/Y for the existing PIPE INFO top-of-pipe calculation, uses the COGO point Elevation as `SURV - TOP`, calculates `DIFF = SURV - TOP - PLAN - TOP`, and places one three-line MText label on `V-SURV-CHCK`.
- `PIPE TOP CHECK` requires drawing text style `CLV-Non Anno`, uses text height `0.1`, and does not create a label if the required style is missing. The command restores the original current layer after label creation or failure.

## 2026-08-06 - Map Transform edit-mode original-position reset R3
- Updated `SURVEY-BESTFIT-MAP` / Q4 `MAP TRANSFORM` history editing so selecting a map with saved history temporarily restores the target block/xref to its original pre-transform position before the review/edit cycle. This makes `Add Pair` use the same coordinate basis as the initial point-selection workflow and prevents a prior translation from appearing as a large residual on newly added pairs.
- Canceling a recalled edit restores the map to the placement it had when the command was started. `Apply Updated Transform` continues to rebuild the final placement from the saved original state.
- Recalled sessions now recreate the numbered Survey/Map pair markers (`1`, `2`, `3`, etc.) so users can visually identify each saved point grouping while editing. Marker sets are rebuilt after pair removals before adding another pair.
- Added visible command-line revision stamp `MAP TRANSFORM revision 2026.08.06-HISTORY-R3`.
- No LISP routines were added or modified.

## 2026-08-06 - Map Transform history verification R2
- Added visible command-line revision stamp `MAP TRANSFORM revision 2026.08.06-HISTORY-R2` so testers can confirm the loaded DLL contains editable history support.
- Added immediate post-save readback verification after applying a transform. The command now reports either `Saved and verified editable Map Transform history...` or an explicit history persistence error.
- Packaging cleanup: generated main-project `bin`/`obj` artifacts are omitted from the distribution ZIP so an older compiled `CLV_CivilTools.dll` cannot be mistaken for the revised source build. Rebuild the solution before NETLOAD/deployment.

## 2026-08-06 - Map Transform editable point-pair history
- Updated `SURVEY-BESTFIT-MAP` / Q4 `MAP TRANSFORM` so a finalized transform stores its editable control-pair session on the selected map/xref block reference in the DWG.
- Re-running `MAP TRANSFORM` on the same saved map recalls the prior Survey/Map point pairs and CONTROL checkbox states instead of requiring all points to be selected again.
- Added `Add Pair` and `Remove Selected` actions to the existing review dialog. New pairs selected after the map has already moved are converted back to the stored original map coordinate basis before the fit is recalculated.
- `Apply Updated Transform` always rebuilds the block/xref placement from its saved original insertion point and rotation, preventing cumulative move/rotation error when controls are changed and the transform is reapplied.
- The saved session is stored in the block reference extension dictionary under `CLV_MAP_TRANSFORM_SESSION`; it travels with the DWG and does not require a sidecar history file.
- Existing drawings transformed with older builds have no recoverable point-pair history; the first transform performed with this build establishes the saved session for future edits.
- No new command or `New Transform` button was added. No LISP routines were created or changed.

## 2026-07-22 - Legal Description bidirectional curve tangency
- Added separate Curve IN and Curve OUT classifications for every legal-description arc.
- Evaluates tangency at both curve endpoints, including the last-to-first connection of a closed boundary.
- Adds a radial bearing to the curve beginning when the incoming connection is non-tangent.
- Adds a radial bearing to the curve ending when the outgoing connection is non-tangent.
- Recomputes both endpoint classifications and radial wording after boundary reversal.

## 2026-07-22 — Legal course relationship editor and synchronized review highlighting

## 2026-07-22 — Legal Description linked MText course highlighting
- Selecting a TIE or BOUNDARY course now highlights the matching call inside every linked legal-description MText object.
- The linked MText highlight is a temporary magenta, underlined transient overlay; it does not alter the stored MText contents or office formatting.
- Course selection continues to synchronize the magenta source-geometry overlay and yellow editor-preview highlight.

- Split course wording into explicit Travel Relationship, Travel Feature/Reference, Travel Wording Order, Destination Clause, and Destination Feature/Reference controls.
- Added BEFORE GEOMETRY and AFTER BEARING wording order for line courses so City Surveyor-style calls can place ALONG/DEPARTING language after the bearing and before the distance.
- Preserved Additional Context, Custom Prefix, Custom Suffix, and Geometry Text Override for exceptional wording.
- Added compatibility migration for saved sessions that previously stored destination phrases in the single Relationship field.
- Selecting a course row now overlays the source LINE or ARC with a temporary magenta, heavier transient highlight without changing the source entity.
- Selecting a course row also highlights the corresponding generated course text in yellow in the line-by-line review pane and scrolls it into view.
- No LISP routines were created or changed.

## 2026-07-22 — Legal Description option persistence and visible area selection fix

- Corrected Description Options so saved wording selections restore by stable phrase key, with compatibility for previously stored display names/templates.
- Description Options are now saved to the drawing immediately when OK is selected, preventing later dialog or command openings from reverting untouched wording fields.
- Expanded and made the area controls visible: SQUARE FEET, ACRES, or SQUARE FEET AND ACRES, with independent precision and computer-method wording.
- Renamed the free-text area field to CUSTOM AREA STATEMENT and clarified that it overrides the selected automatic area output only when populated.
- No LISP routines were created or changed.

## 2026-07-22 — Embedded Legal Templates

- Embedded `Basic Template.dotx` directly in the Civil Tools DLL for `EXPORT LEGAL DOCX`.
- Embedded `LegalDescriptionTextStyles.json` directly in the DLL.
- Removed the runtime requirement to deploy a `Reference` folder beside the DLL for legal-description DOCX export or text styles.
- Added `EmbeddedLegalResourceService.cs` to provide deterministic access to the embedded office resources.
- The source copies remain in the project so future template or seal changes can be made by replacing the source file and rebuilding the DLL.

## 2026-07-22 — Legal Description DOCX Export

- Added `EXPORT LEGAL DOCX` to the Legal Description palette.
- Added a structured export-options dialog for APN, preparation date, preparer, peer reviewer, explanation, basis of bearings, and exhibit statement.
- Added deterministic DOCX generation using the supplied City Surveyor `Basic Template.dotx`; Microsoft Word and GPT are not required for generation.
- Preserves the template seal, headers, footers, styles, and page layout while replacing the legal-description placeholders.
- Final legal body is exported in ALL CAPS with key legal terms bolded.
- Word PAGE and NUMPAGES fields are inserted in the template headers.
- Added the supplied legal-template reference package under `Reference/Legal Templates`.
- CAD linked MText remains the live review copy prior to Word export.

## 2026-07-22 — Legal POC keyword acceptance fix
- Corrected the POC prompt so the actual AutoCAD keyword token is `seParate`, making `P` a registered accelerator rather than only a displayed capital letter.
- The prompt now accepts both `P` and the full word `Separate`; `S` and `Same` continue to select the default same-point option.
- Confirmed `PDF CLIP` remains directly below `PDF VIEWER` under Q4 > MAPPING > TOOLS.
- No LISP routines were created or changed.

## 2026-07-22 — PDF CLIP restore and unique legal prompt shortcuts
- Restored `PDF CLIP` under Q4 > MAPPING > TOOLS directly below `PDF VIEWER`; it launches native command `PDFC`.
- Changed the legal-description POC prompt display from ambiguous `Same/Separate` to `Same/seParate`, providing unique `S` and `P` keyboard shortcuts while preserving the internal `Same` and `Separate` keyword values.
- Updated the external `Coding_Rules.md` source rules to require unique shortcut letters for every keyword in one AutoCAD prompt.
- No LISP routines were created or changed.

## 2026-07-22 - Legal Description Builder Phases 1-2
## 2026-07-22 — Legal Description Phase 1–2 compile fix
- Resolved `Font` namespace ambiguity by explicitly using `System.Drawing.Font`.
- Resolved `SaveFileDialog` namespace ambiguity by explicitly using `System.Windows.Forms.SaveFileDialog`.
- No command behavior or LISP routines changed.

- Added a new managed WinForms PaletteSet workflow for legal-description geometry and text editing.
- Added connected LINE/ARC ordering, POB selection, optional separate POC recording, line and curve call calculations, area, forward closure, reverse-direction build reporting, and complete traverse reversal.
- Added editable course context, prefix/suffix, inclusion, geometry override, live text generation, manual final-text editing, TXT export, source highlighting/zoom, and drawing-persistent JSON session storage.
- Added the `LEGAL DESCRIPTION` button to Q4 > MAPPING > TOOLS.
- Added `Docs/LEGAL_DESCRIPTION_PHASE_1_2_README.md`.
- No LISP routines were created or modified.

## 2026-07-21 - Move LINEWORK REVIEW to Tools
- Moved `LINEWORK REVIEW` from the Q4 > MAPPING > `BOUNDARY` section to the bottom of the `TOOLS` section.
- The command remains `SURVEY-LINEWORK-REVIEW`; only its palette location changed.
- No LISP routines were added or changed.

## 2026-07-21 - Q4 Boundary heading cleanup
- Removed the redundant empty `BOUNDARY` heading above the closure controls.
- Renamed the displayed `BOUNDARY CLOSURE` section to `BOUNDARY`.
- The existing closure and linework-review buttons remain unchanged under the single `BOUNDARY` heading.
- No commands or LISP routines were added, removed, or modified.

## 2026-07-21 - Q4 Mapping menu reorganization
- Renamed the Q4 > MAPPING section `SURVEY MAP` to `TRANSFORM`.
- Renamed `BEST FIT MAP (TRANSFORM)` to `MAP TRANSFORM` while retaining command `SURVEY-BESTFIT-MAP`.
- Renamed `TRANSFORM TO CONTROL` to `BOUNDARY TRANSFORM` while retaining command `SURVEY-TRANSFORM-CONTROL`.
- Added `C3D TRANSFORM`, which launches native command `ADETRANSFORM`.
- Added a `TOOLS` section after `TRANSFORM` containing, in order: `DRAW TIE LINE`, `PDF VIEWER`, `XREF COLOR`, and `OFFSET TO TEMP LAYER`.
- Removed the `BOUNDARY REVIEW` heading and moved its remaining review commands directly into the `BOUNDARY` section.
- No LISP routines were created or modified.

## 2026-07-20 - Remove Q4 boundary CSV import workflows
- Removed Q4 > MAPPING > BOUNDARY buttons `BOUNDARY IMPORT AUTO`, `BOUNDARY IMPORT MANUAL`, `ROADS IMPORT`, and `EASEMENT IMPORT`.
- Deleted the three managed importer modules and their command aliases so obsolete CSV import code is no longer part of the DLL.
- Deleted importer-only support documentation, prompts, and sample data.
- Directly generated DXF files replace these import workflows.
- No LISP routines were created.

## 2026-07-15 - Transform To Control Final Preview Revision
- Revised `TRANSFORM TO CONTROL` selection order: source circle, source rotation line, destination circle, then destination rotation line.
- The command now calculates and displays the complete translated and rotated placement before acceptance.
- Preview geometry is temporarily faded so it can be compared against destination linework.
- Added an interactive `Flip` option that rotates the preview 180 degrees; selecting Flip again returns to the original direction.
- Press Enter to accept and restore the objects' original transparency, or cancel to roll the transaction back to the original location and rotation.
- No changes were made to the working repeated `OFFSET TO TEMP LAYER` workflow.

## 2026-07-15 - Transform preview and repeating temporary offset
- Revised `SURVEY-TRANSFORM-CONTROL` / `TRANSFORMCONTROL` so the selected geometry is temporarily moved to the destination circle center and displayed before the rotation lines are selected. Canceling during the rotation prompts aborts the transaction and restores the original placement.
- Revised `SURVEY-OFFSET-TEMP` / `OFFSETTEMP` to repeat object and side selection using the same offset distance until Enter is pressed, allowing multiple objects to be offset in one command session.
- No LISP routines were created or modified.


## 2026-07-15 - Q4 Transform to Control and Temporary Offset tools
- Added `SURVEY-TRANSFORM-CONTROL` / `TRANSFORMCONTROL` in `Survey/SurveyTransformOffsetCommands.cs`.
- The command transforms a selected object, block, xref, group, or multi-object selection without scaling: select a source control circle, destination control circle, source rotation line, and destination rotation line. Circle centers provide the move points; nested lines and circles inside xrefs/blocks are supported for control selection. A Match/Reverse option resolves opposite bearing directions.
- Added `SURVEY-OFFSET-TEMP` / `OFFSETTEMP`. It prompts for offset distance, source curve, and offset side, then places the resulting geometry on `V-CONS-LINE-TEMP`.
- Added `TRANSFORM TO CONTROL` and `OFFSET TO TEMP LAYER` under Q4 > MAPPING > SURVEY MAP.
- No LISP routines were created or modified.


## 2026-07-15 - PDF Viewer Fix 15
- Removed the PLAN / PROFILE / TABLES / DETAILS / NOTES filter buttons from the PDF Viewer palette.
- Combined every mapped plan and saved reference view into one selectable list.
- Added category prefixes to list entries and moved pinned items to the top of the full list.
- Previous and Next now move through the complete combined list.

## 2026-07-15 - PDF Viewer Fix 12
- Corrected PDF/Civil 3D Y-axis conversion so mapped plan views are no longer vertically mirrored or offset.
- Added UNLOCK PDF / LOCK PDF navigation mode.
- Added mouse-wheel PDF zoom and click-drag PDF pan.
- RETURN TO CURRENT now relocks the PDF and restores model-space synchronization.
- Plan calibration now permits zooming and panning before selecting PDF control points.
- Preserved PDF aspect ratio during rendering.
- Existing Version 1 plan mappings should be removed and recreated because their stored PDF Y coordinates used the prior convention.

## 2026-07-15 - PDF Viewer Fix 11
- Fixed isolated renderer `ObjectDisposedException` when reading PDF metadata.
- The renderer now opens a separate PDF stream for page count, page size, and page rendering because PDFtoImage may dispose streams passed to its conversion methods.
- Multipage PDF documents remain supported.


## 2026-07-15 - PDF Viewer Fix 7
- Changed the isolated PDF renderer build target to publish directly into the final `PdfRenderer` deployment folder.
- Removed the hard-coded intermediate `bin` path that could vary by Visual Studio platform/configuration and caused a false “renderer was not built” error.

## 2026-07-15 — PDF Viewer V1 Fix 6
- Fixed `NETSDK1004` during rebuild when `PdfRenderHost\obj\project.assets.json` had not been generated.
- The Civil Tools project now explicitly restores and builds the isolated PDF renderer before copying its output.
- Kept PDFium and SkiaSharp outside the Civil 3D plugin dependency graph.
## 2026-07-15 — PDF Viewer V1 Fix 5 — Isolated Renderer
- Removed `PDFtoImage` and SkiaSharp from the Civil 3D plugin process after the in-process native renderer caused Civil 3D 2026 to terminate.
- Added a separate x64 helper executable, `PdfRenderer\CLV.PdfRenderHost.exe`, which owns PDFium/SkiaSharp rendering.
- Added JSON/temp-file request and PNG response communication between the palette and renderer.
- A renderer failure now reports an error in the palette without terminating Civil 3D.
- Updated deployment so the complete `PdfRenderer` folder is copied beside the Civil Tools DLL.
- Restored and retained the established `CLV_CivilTools_<short random number>.dll` development naming.
- No LISP routines were added or modified.

## 2026-07-15 — PDF Viewer V1 Fix 4
- Restored the established post-build development DLL naming format: `CLV_CivilTools_<short random number>.dll` using `%RANDOM%`.
- Removed the GUID-style randomized DLL filename introduced in Fix 3.
- Retained full PDFtoImage, SkiaSharp, `.deps.json`, and native runtime deployment.

- PDF Viewer V1 Fix 2: qualified WinForms `FlowDirection` references and corrected button field mutability in `PdfViewerControl.cs` to resolve CS0104 and CS0191 build errors.

## 2026-07-15 — PDF Viewer V1 Fix 3

- Fixed the Civil 3D runtime `FileNotFoundException` for `PDFtoImage, Version=5.2.1.0`.
- Enabled `CopyLocalLockFileAssemblies` so NuGet managed dependencies are copied for the class-library plugin.
- Replaced the post-build `xcopy` command with MSBuild `Copy` tasks that preserve the complete managed/native runtime folder structure in `C:\Temp\C3DDev`.
- Added a post-build validation error if `PDFtoImage.dll` is missing from the deployment folder.


## 2026-07-15 — PDF Viewer / Map Review Version 1

- Added `PDFVIEW` and `MAPREVIEW` commands with a separate dockable/floating WinForms PDF viewing palette.
- Added Q4 > MAPPING > SURVEY MAP > `PDF VIEWER`.
- Added multipage PDF loading and relinking with absolute and DWG-relative path storage.
- Added named two-point calibrated plan mappings with current-view model-space coverage.
- Added automatic plan-sheet switching based on the current model-space view center and PDF crop rendering that follows model-space pan/zoom.
- Added manual Profiles, Tables, Details, and Notes categories, optional pinned reference items, Previous/Next navigation, and `RETURN TO CURRENT`.
- Added per-DWG Named Object Dictionary persistence under `CLV_PDF_VIEWER_V1`.
- Added `PDFtoImage` 5.2.1 for PDFium/SkiaSharp page rendering.
- No LISP routines were added or modified.

## 2026-07-13 - Distance-only line/curve label correction
- Corrected `DISTANCE` and `2-POINT  ||  DIST` post-placement style handling so model-space objects detected by the before/after comparison also receive `R26_Distance`, even when Civil 3D does not expose the final label through the database append event.
- Changed Q4 LABEL two-point button separators from `|` to `  ||  ` for clearer menu spacing.

## 2026-07-13 - Q4 menu reorganization and distance-only label tools
- Added a Q4 `GIS` tab and moved `TOWNSHIP/RANGE` / `SECTION CORNER MARKER` into it.
- Reorganized Q4 MAPPING so `BOUNDARY CLOSURE` and `BOUNDARY REVIEW` appear above `SUBDIVISION LINEWORK`; renamed `BEST FIT MAP` to `BEST FIT MAP (TRANSFORM)`.
- Renamed LABEL menu captions to `STREET NAMES`, `2-POINT  ||  BEARING AND DIST`, and `BEARING AND DIST`.
- Moved `AREA SF LABEL` to Q4 > LABEL > AREA.
- Added distance-only line/curve label commands using style `R26_Distance`: `SURVEY-LC-LABEL-2POINT-DIST` and `SURVEY-LC-LABEL-DISTANCE`.
- Updated existing bearing-and-distance wrappers to explicitly apply `R26_Bearing + Distance` after Civil 3D label placement.
- No new LISP routines were created.

## 2026-07-13
### Changed
- Removed `BOUNDARY ONLY IMPORT` from the Q4 Survey MAPPING palette.
- Corrected `SURVEY-AUTO-CLOSURE` final prompt behavior: `No` now applies the closure without storing original reference linework; `Yes` retains the original linework on the ORIG layer and creates the adjusted overlay as before.
- When original reference linework is not retained, adjusted entities are placed on each source entity's original layer and the source entities are replaced.
- Updated `PROJECT_MAP.md` and `COMMAND_INDEX.md`.
- No new LISP routines were created.

## 2026-07-08 - Prompt 4 Rev 3 clean final import CSV

## 2026-07-09 - Mapping import palette naming cleanup
- Removed `1-SECTIONAL IMPORT` from the Q4 > MAPPING > BOUNDARY palette. The command code remains hidden/legacy for older files.
- Renamed palette buttons: `BOUNDARY IMPORT AUTO`, `BOUNDARY IMPORT MANUAL`, `ROADS IMPORT`, and `EASEMENT IMPORT`.
- Updated boundary import startup text for the standalone Prompt 2 POC -> POB -> boundary workflow.
- Added Prompt 2 POINT_MARKER handling for POC/POB marker rows in the boundary importer.


- Updated Prompt 4 to separate source/review tables from the clean Final Import CSV.
- Final Prompt 4 import CSV now excludes Bearing/Distance and includes only endpoint-controlled final parent lines, full-length derived control lines, and optional point markers.
- Clarified that closure must use distributed length-only adjustment, not snapping the final endpoint to the start point.
- Updated `Shared/ReferenceCsvImportCommands.cs` to accept simplified Prompt 4 final import rows, skip REVIEW_ONLY/SOURCE_ONLY/QA_ONLY rows, support point marker type aliases, and use `LineLabel` when `RoadName` is absent.


## 2026-07-08 - 1-SECTIONAL IMPORT Prompt 4 endpoint-control update

- Updated `Shared/ReferenceCsvImportCommands.cs` so Prompt 4 resolved parent boundary and derived split/section rows use CSV `StartX,StartY -> EndX,EndY` directly instead of rebuilding from rounded bearing/distance values.
- Added endpoint-control detection for `RESOLVED_PARENT_BOUNDARY_LENGTH_ADJUSTED`, `SPLIT_POINT_COORDINATE`, `INTERSECTION_COORDINATE`, `COORDINATE`, and `QA_COORDINATE` build modes.
- Added support for importing `POINT_TIE` rows with identical start/end coordinates as DBPoint geometry instead of skipping them.
- Added `Docs/Prompt_4_Sectional_Boundary_Linework.txt` with the new final coordinate overwrite pass and endpoint-control import contract.

## 2026-07-08 - 2A-BOUNDARY import input curve review
- Added Q4 > MAPPING > BOUNDARY button `BOUNDARY IMPORT MANUAL` directly below `BOUNDARY IMPORT AUTO`.
- Added commands `CLV_2A_BOUNDARY_IMPORT_INPUT`, `CLV_2A_BOUNDARY`, and `CLV-2A-BOUNDARY-IMPORT-INPUT`.
- The original `BOUNDARY IMPORT AUTO` keeps automatic curve/radial resolution. The new `2A` workflow uses the same Prompt 2 boundary CSV format but disables auto curve resolution and restores interactive Keep / FlipCurve / ReverseRadial / Both preview selection for every curve.

## 2026-07-08 - 2-BOUNDARY import automatic curve direction
- Updated `BOUNDARY IMPORT AUTO` so boundary curves are auto-resolved without prompting for Keep/Flip/ReverseRadial choices.
- The importer now tests LEFT/RIGHT and, when radial data exists, shown/reversed radial options, then selects the best scoring candidate from CSV endpoint fit, chord bearing fit, and next-line tangency.
- Kept the former interactive curve-review code in place as a commented/documented rollback path through `AutoResolveCurveDirection=false` and `CurveReview=QaOnly/All` if manual field review is needed later.


## 2026-07-07 - 4-EASEMENT rebuilt prompt schema support
- Updated `EASEMENT IMPORT` to read the rebuilt Prompt Easement CSV schema with `ImportRole`, `From`, `To`, `POINT_MARKER`, `MarkerLabel`, and `LabelText` fields.
- Added support for POC/POB marker rows from the one-file easement CSV.
- Added support for rebuilt prompt layers `V-ESMT-LINE`, `V-ESMT-TIE`, and `V-ESMT-MARK` while preserving prior CLV layer names.
- Added documentation for the exact CSV header expected by the rebuilt prompt.


## 2026-07-07 - 4-EASEMENT import layer hotfix
- Fixed 4-EASEMENT import eKeyNotFound caused by prompt-generated `V-MAPL-ESMT` layer values not existing before entity layer assignment.
- Added source-side CSV layer normalization so `V-MAPL-EASE`, `V-MAPL-ESMT`, and easement aliases resolve safely.
- Added pre-creation of CSV-referenced geometry/text/point layers before drawing easement entities.
- Updated default easement boundary layer naming to `V-MAPL-ESMT` to match current Prompt Easement CSV output.


## 2026-07-07 - 4-EASEMENT CSV Import

- Added `CLV_4_EASEMENT`, `CLV_4_EASEMENT_IMPORT`, `CLV-4-EASEMENT-IMPORT`, and `SURVEY-EASEMENT-CSV-IMPORT`.
- New importer supports text/exhibit easement legal CSV rows with commencement ties and easement boundary geometry.
- Easement boundary rows draw to `V-MAPL-EASE`; commencement/tie rows draw to control layers; QA/manual-review rows draw to `V-MAPL-QA`.
- Import report now summarizes easement closure error, perimeter, area, warnings, and skipped rows.


### 2026-07-01 — Lot Lines repeated offset update

- Renamed subdivision `LOT OFFSET` tool to `LOT LINES` in the survey mapping palette.
- Added new command aliases: `CLV_SUBDIV_LOT_LINES`, `SUBDIV-LOT-LINES`, and `SUBDIVISION-LOT-LINES`.
- Legacy `LOT OFFSET` command aliases remain active for testing/backward compatibility.
- Updated workflow to pick a furthest/stop point instead of picking an offset side.
- Tool now creates repeated lot-line offsets every user-entered spacing until reaching the selected stop point/location.


## 2026-07-01 - Subdivision intersection fillet v6
- Reworked INTERSECTION return arc construction to use internal angle-bisector tangent/tangent fillet math.
- Return tangent points now land directly on the main road edge and intersecting road edges, allowing the main road opening to trim between the two tangent points before side-road edges are trimmed.
- Cul-de-sac tangent cleanup from the previous pass was left unchanged.

## 2026-06-30 - Q4 Line/Curve Label Layer Standard Update

- Updated Q4 > SURVEY > LABEL > DIMENSIONS > LINES AND CURVES commands `2-POINT` and `BEARING AND DISTANCE` to use layer `V-LABL` for created line/curve labels.
- Added `V-LABL` layer standard: color yellow, linetype Continuous, plot style `S`, description `Labels: line and curve geometry`.
- Added post-command cleanup so new Civil 3D line/curve label objects are moved to `V-LABL` after placement, correcting drawings where Civil 3D defaults place the label on `C-ANNO`.


## 2026-06-30 - Q4 Survey Labels and Mapping Quality Tools

- Added Q4 > SURVEY > LABEL > DIMENSIONS > LINES AND CURVES section.
- Added `2-POINT` / `SURVEY-LC-LABEL-2POINT`, which launches Civil 3D `ADDLINEBETWEENPOINTS` with post-placement layer cleanup to `V-LABL`. Intended Civil 3D label style: `R26_Bearing + Distance`.
- Added `BEARING AND DISTANCE` / `SURVEY-LC-LABEL-BEARING-DISTANCE`, which launches Civil 3D `ADDSEGMENTLABEL` for single-segment line/curve labels with post-placement layer cleanup to `V-LABL`. Intended Civil 3D label style: `R26_Bearing + Distance`.
- Added Q4 > SURVEY > MAPPING > BOUNDARY > `DRAW TIE LINE`, which starts the AutoCAD LINE command on layer `V-CTRL-TIES-LINE` using the CLV layer standard: color yellow, linetype HIDDEN3, plot style XS-60.
- Added Q4 > SURVEY > MAPPING > BOUNDARY > `AREA SF LABEL`, which asks for a point inside a closed boundary, traces the boundary, calculates square-foot area, and places a centered SF MText label.
## 2026-06-30 - Prompt 2 Boundary From Sectional Framework

- Added Q4 > MAPPING > BOUNDARY button `BOUNDARY IMPORT AUTO` using command `CLV_2_BOUNDARY_IMPORT`.
- Added aliases `CLV_2_BOUNDARY`, `CLV_2_BOUNDARY_IMPORT`, and `CLV-2-BOUNDARY-IMPORT`.
- The 2-boundary workflow expects boundary CSV coordinates already tied to the Prompt 1 sectional/control POC framework. It does not move the boundary POB to 10000,10000.
- Kept the existing boundary importer curve review behavior, including LEFT/RIGHT preview, radial checks, and Keep / FlipCurve / ReverseRadial / Both choices for radial-controlled curves.
- Added `Docs/Prompt_2_Boundary_From_Sectional_Framework.txt`.

## 2026-06-30 - Sectional Import Layer 0 / Street Name Labels

- Updated `Shared/ReferenceCsvImportCommands.cs` for `1-SECTIONAL IMPORT` (removed from palette) so imported sectional/control linework is created on layer `0` regardless of CSV layer values.
- Reduced automatic sectional import labels to street names only from the `RoadName` field. Segment numbers, bearings, distances, widths, and QA text are no longer inserted as labels so users can add CAD line labels to verify actual geometry.
- Updated `Docs/REFERENCE_CSV_IMPORT_README.md` and `COMMAND_INDEX.md` to document the layer and label behavior.

# 2026-06-30 Sectional Import Forced Closure Update

- Updated `Shared/ReferenceCsvImportCommands.cs` so `1-SECTIONAL IMPORT` (removed from palette) can chain-build outer sectional/control traverse rows marked `OUTER_CONTROL_TRAVERSE` / `MAIN_CONTROL_TRAVERSE`.
- Added distributed length-only closure correction for marked outer traverse rows: bearings are held true, lengths are adjusted across the traverse, and the final CAD endpoint is forced back to the beginning point for 0.00 closure.
- Added forced-closure summary fields to the `_sectional_import_report.txt` output.
- Updated Prompt 1 text to require derived/split sectional lines to be recalculated from the resolved 0.00-closure outer framework.

# 2026-06-30 Sectional Import Rename and Prompt 1 CSV Support

- Renamed Q4 > MAPPING > BOUNDARY button `BOUNDARY CSV IMPORT` to `BOUNDARY ONLY IMPORT` for boundary-only workflows.
- Renamed Q4 > MAPPING > BOUNDARY button `REFERENCE CSV IMPORT` to `1-SECTIONAL IMPORT` (removed from palette).
- Added primary command alias `CLV_SECTIONAL_IMPORT` while keeping existing reference/sectional aliases for backward compatibility.
- Updated `Shared/ReferenceCsvImportCommands.cs` to support the new Prompt 1 sectional/control CSV fields, including `OriginalDistance`, `AdjustedDistance`, `LengthAdjustment`, and `AdjustmentMethod`.
- For Prompt 1 closure-adjusted rows, `AdjustedDistance` is used as the CAD build distance when supplied, while original map distances and length adjustments are reported for QA.
- Updated the import report name to `_sectional_import_report.txt` for the new workflow.

# 2026-06-30 Reference CSV Import Bearing/Distance Build Update

- Updated `Shared/ReferenceCsvImportCommands.cs` so Prompt 3 reference LINE/RADIAL/POINT_TIE/OFFSET rows can be built from `StartX/StartY + Bearing + Distance`, matching the boundary importer line-build behavior.
- Added optional `GeometryBuildMode` CSV support. `BEARING_DISTANCE` rows calculate EndX/EndY from the source call; `COORDINATE` / `INTERSECTION_COORDINATE` rows keep shared node coordinates for centerline/control networks.
- Added bearing-coordinate QA warnings when CSV Start/End coordinates do not match the labeled Bearing/Distance.

# 2026-06-30 Reference CSV Import Tool

- Added `Shared/ReferenceCsvImportCommands.cs` with `CLV_REFERENCE_IMPORT`, `CLV-REFERENCE-CSV-IMPORT`, `CLV-SECTIONAL-CSV-IMPORT`, and `SURVEY-REFERENCE-CSV-IMPORT` for Prompt 3 sectional / control / roadway / reference CSV files.
- Added Q4 > MAPPING > BOUNDARY button `REFERENCE CSV IMPORT` directly below `BOUNDARY CSV IMPORT`.
- The importer respects Prompt 3 StartX/StartY/EndX/EndY coordinates tied to the Prompt 2 boundary/POB coordinate system, draws reference LINE/CURVE/RADIAL/POINT_TIE rows on their CSV/default layers, places QA/manual/uncertain rows on `V-MAPL-QA`, labels imported reference geometry, checks width/ROW/offset fields for report warnings, zooms to the imported linework, and writes a `_reference_import_report.txt` beside the selected CSV.

# 2026-06-29 Boundary Import Final Layer/Color Update

- Updated boundary CSV import so accepted chain-built geometry is drawn on `V-MAPL-BNDY` instead of remaining on the red QA/review layer.
- CSV QA/manual-review flags still trigger curve review and report warnings, but no longer make every accepted segment red.
- Reserved red/orange/cyan/magenta colors for QA fallback geometry, tangency markers, and temporary curve/radial preview options.
- Final accepted labels now use `V-MAPL-BNDY-TEXT`; POB marker uses `V-MAPL-BNDY-POB`.

## 2026-06-29 - Boundary CSV Import curve preview and tangency highlight update

- Updated `CLV_BOUNDARY_IMPORT` / `CLV-MAP-CSV-IMPORT` / `CLV-BOUNDARY-CSV-IMPORT` to simplify curve review decisions.
- Preview behavior: the CSV/current curve is shown in green as `Keep / CSV`; the opposite curve is shown in red as `Flip`.
- The command zooms to the curve review area, pauses, and prompts only for `Keep`, `Flip`, or `Cancel`.
- Added an optional curve-to-line tangency highlight. If the selected curve outgoing tangent does not match the next line bearing within the chosen tolerance, the PC/PT area is circled/labeled on `V-SURV-MAP-REVIEW-TANGENCY`.
- Chain-build mode remains the default so each new segment begins at the previous segment endpoint and avoids PC/PT gaps caused by CSV coordinate rounding.
- Note: source update only; rebuild the DLL in Visual Studio before testing in Civil 3D.


## 2026-06-29 - Boundary CSV Import chain-build review update

- Updated `BoundaryCsvImportCommands.cs` to chain-build boundary imports from the previous segment endpoint by default, reducing PC/PT/AP gaps from independent segment coordinates.
- Added curve direction review prompt. Current workflow uses the simplified `Keep`, `Flip`, and `Cancel` options.
- Added curve review modes: `None`, `QAOnly`, and `All`.
- Added label modes: `None`, `Segment`, `Bearing`, and `Full`; default now avoids long Notes text in the drawing.
- Added QA warnings for CSV start/end coordinate differences versus chain-built geometry.

# 2026-06-29 - Linework Review Temporary Highlight Cleanup

- Revised `Survey/SurveyLineworkReviewCommands.cs` so LINEWORK REVIEW temporary overlay linework on `V-SURV-LWRK-REVIEW` is cleared when starting a new review, when selection is cancelled, and when the modeless review report is closed.
- Updated `REMOVE DUPLICATES` so it clears the review overlay before gathering all non-xref current-space linework and launching AutoCAD `OVERKILL`, preventing the temporary highlight geometry from being included in the cleanup selection. No LISP helpers were added or changed.

# 2026-06-29 - XREF COLOR Dialog Layout Fix

- Fixed `Survey/SurveyXrefColorCommands.cs` dialog sizing/layout so Cyan, Magenta, and Gray ACI 252 rows remain visible above the RESET / RESET ALL buttons. The color table now has explicit row heights and a taller fixed dialog window. No LISP helpers were added or changed.

# 2026-06-29 - XREF COLOR Cyan/Gray and LINEWORK REVIEW Same-Line Highlight Revision

- Verified/updated `Survey/SurveyXrefColorCommands.cs` so the dialog includes Cyan and Gray choices in both standard and 70% transparent columns. Gray uses ACI color 252.
- Revised `Survey/SurveyLineworkReviewCommands.cs` so same-path linework with different lengths no longer highlights as red error linework. Same-line length differences now highlight the longer object green and the shorter object orange.
- Offset/near-duplicate linework within the review tolerance remains red so it can be used to find possible linework errors.
- Updated LINEWORK REVIEW option/report text and project docs. No LISP helpers were added or changed.

# 2026-06-29 - XREF COLOR Dialog and Reset All Revision

- Revised `Survey/SurveyXrefColorCommands.cs` so `SURVEY-XREF-COLOR` / `XREFCOLOR` opens a WinForms color-choice dialog instead of using command-line keyword selection. The dialog is two columns wide with standard colors on the left and the matching 70% transparent variants on the right.
- Added `RESET ALL` to restore layer color/transparency overrides for every attached/overlaid xref that can be resolved from the current host drawing. `RESET` still restores only the selected xref. Both reset options change host drawing xref layer overrides only and do not edit source xref DWGs. No LISP helpers were added or changed.

# 2026-06-29 - Linework Review Duplicate Highlight Revision

- Removed `CLEAR LINEWORK HIGHLIGHT` from the Q4 > MAPPING > REVIEW palette. The command remains available from the command line for now, and the report dialog still has `Clear Highlight`.
- Revised `Survey/SurveyLineworkReviewCommands.cs` so `LINEWORK REVIEW` creates direct overlay highlights on the duplicate linework only instead of review boxes: exact duplicates use green, and near duplicates within the tolerance use red. Default review tolerance is now `0.1`, while exact duplicate grouping uses a tighter internal exact tolerance.
- Xrefs/block references are excluded from the review selection/filter and from REMOVE DUPLICATES preselection. The report button formerly labeled `Launch OVERKILL` now reads `REMOVE DUPLICATES` and starts AutoCAD `OVERKILL` with all non-xref linework in the current space preselected. No LISP helpers were added or changed.

# 2026-06-29 - Linework Review Overkill Launch Revision

- Revised `Survey/SurveyLineworkReviewCommands.cs` report dialog to remove the cleanup buttons because the first-pass cleanup results were not ready for production use.
- Kept row-based review tools: `Highlight Selected`, `Zoom Selected`, and `Clear Highlight`. The report continues to show object/layer/type detail for each issue.
- Added `Launch OVERKILL` from the report dialog. When a row is selected, the selected issue objects are highlighted/preselected before AutoCAD `OVERKILL` is launched. No LISP helpers were added or changed.

# 2026-06-29 - Linework Review Cleanup Dialog Revision

- Revised `Survey/SurveyLineworkReviewCommands.cs` report dialog to remove the non-working `Highlight Row` and `Highlight All` buttons. The report now keeps `Zoom Selected` and `Clear Highlight`.
- Added an `Object / Layer / Type` detail column so each issue shows which layer each listed object is on, keyed by object handle.
- Added first-pass cleanup actions on the selected report row: `Move Row to Review Layer` moves all objects for the selected issue to `V-SURV-LWRK-REVIEW`, and `Delete Row Extras` keeps the lowest handle and erases the remaining whole-object duplicates/overlaps after confirmation. Partial-overlap delete prompts include an extra warning. No LISP helpers were added or changed.

# 2026-06-29 - Linework Review Highlight-Only Dialog Revision

- Revised `Survey/SurveyLineworkReviewCommands.cs` so `SURVEY-LINEWORK-REVIEW` opens a WinForms options dialog for tolerance and mode before selection.
- Removed review box/label marker creation from the active workflow. The command now highlights the matching source objects directly using the current selection highlight and opens a modeless report with Highlight Row, Highlight All, Clear Highlight, and Highlight + Zoom actions.
- Updated the Q4 > MAPPING > REVIEW clear button label to `CLEAR LINEWORK HIGHLIGHT`. No LISP helpers were added or changed.

# 2026-06-29 - Linework Review Compile Fix

- Fixed `Survey/SurveyLineworkReviewCommands.cs` compile error CS0104 by fully qualifying the WinForms flow direction on the report button panel. This avoids the conflict between `Autodesk.AutoCAD.DatabaseServices.FlowDirection` and `System.Windows.Forms.FlowDirection`.

# 2026-06-29 - Linework Review Tool

- Added `Survey/SurveyLineworkReviewCommands.cs` with `SURVEY-LINEWORK-REVIEW` / `LINEWORKREVIEW` for selecting linework and identifying exact duplicate, reversed duplicate, partial overlapping line, and partial overlapping arc geometry.
- Added modeless WinForms report with issue rows, layers, object types, handles, notes, and row zoom/select support. Review markers are created on `V-SURV-LWRK-REVIEW`.
- Added `SURVEY-LINEWORK-CLEAR-REVIEW` / `LINEWORKCLEAR` to clear the review markers.
- Added Q4 > MAPPING > REVIEW buttons `LINEWORK REVIEW` and `CLEAR LINEWORK REVIEW`. No LISP helpers were added or changed.

# 2026-06-29 - XREF COLOR 70% Transparency

- Changed `SURVEY-XREF-COLOR` transparent comparison options from 80% to 70% so xref comparison linework remains easier to see. Prompt choices are now `R`, `R70`, `Y`, `Y70`, `GN`, `GN70`, `C`, `C70`, `M`, `M70`, `G`, `G70`, and `RESET`.

# 2026-06-25 - XREF COLOR 80% and Gray Option

- Changed `SURVEY-XREF-COLOR` transparency variants from 50% to 80%. Prompt choices were `R`, `R80`, `Y`, `Y80`, `GN`, `GN80`, `C`, `C80`, `M`, `M80`, `G`, `G80`, and `RESET`.
- Added Gray comparison options using ACI 252. `G` = Gray and `G80` = Gray 80%. Green was moved to `GN` / `GN80` to avoid duplicate AutoCAD keyword conflicts.

# 2026-06-25 - XREF COLOR Reset Option

- Added `RESET` to `SURVEY-XREF-COLOR` / `XREFCOLOR`. This lets the user select the same xref and restore its dependent layer color/transparency values from the source xref DWG after using comparison colors.
- Reset only changes host-drawing xref dependent layer overrides; it does not edit the source xref DWG.

# 2026-06-25 - XREF COLOR Keyword Fix

- Revised `SURVEY-XREF-COLOR` prompt choices from full color-name keywords to unique short codes: `R`, `R50`, `Y`, `Y50`, `G`, `G50`, `C`, `C50`, `M`, `M50`. This fixes AutoCAD keyword shortcut conflicts where `Red` and `Red50` both resolved from `R`, preventing the 50% transparency options from being selected reliably.
- No LISP helpers were added or changed.

# CLV_CivilTools CHANGELOG

## 2026-06-25 - Xref Color Review Tool
- Added `Survey/SurveyXrefColorCommands.cs` with `SURVEY-XREF-COLOR` / `XREFCOLOR`. The command prompts for one of ten comparison color options, then prompts for an attached/overlaid xref and applies host-drawing xref layer overrides to every `XREF|Layer` belonging to that selected reference.
- Color choices are Red, Yellow, Green, Cyan, and Magenta, plus matching 50% transparency versions: Red50, Yellow50, Green50, Cyan50, and Magenta50.
- Added Q4 > MAPPING > REVIEW button `XREF COLOR`. No LISP helpers were added or changed.

## 2026-06-25 - Boundary CSV Import Tool
- Added `Shared/BoundaryCsvImportCommands.cs` with `CLV_BOUNDARY_IMPORT`, `CLV-MAP-CSV-IMPORT`, `CLV-BOUNDARY-CSV-IMPORT`, and `SURVEY-BOUNDARY-CSV-IMPORT`.
- Added Q4 > MAPPING > BOUNDARY button `BOUNDARY CSV IMPORT` for importing reviewed legal-description / record-of-survey CSV files into CAD.
- The importer creates review, text, warning, and POB layers; draws LINE rows from bearing/distance; draws CURVE rows as true arcs where complete data is available; falls back to chord-only warning geometry when needed; adds segment labels; zooms to the imported boundary; and writes an `_import_report.txt` beside the selected CSV.
- Added `Docs/BOUNDARY_CSV_IMPORT_README.md` and `SampleData/Cashman_ROS_247-98-SV_boundary_review.csv` for testing.
- No LISP helpers were added or changed.

## 2026-06-24 - UFLS1 top-of-pipe grade-break review
- Updated `Ufls/UflsTopOfPipe.cs` so `UFLS1` analyzes the ordered COGO top-of-pipe picks before creating the final 3D polyline. The check calculates adjacent segment slopes from the picked point order and flags picked points where the slope-in to slope-out change is at least 0.5 percentage points.
- Revised the modal grade-break review dialog so it lists only the flagged break pick(s), including slope in, slope out, slope change, and the adjacent segment numbers. The old `ZOOM TO BREAK` button and bottom `NOTES` section were removed.
- When a grade break is detected, the command now zooms to the extents of the selected picks and places persistent redline review boxes with `GB #` labels on `V-SURV-RDLN` around each flagged break point. If the user cancels to review the data, those redline boxes remain in the drawing.
- No LISP helpers were added or changed. `UFLS5`, pipe labels, GIS, survey closure, and palette command routing were not modified.

## 2026-06-23 - AUTO CLOSURE length-only bearing preservation
- Updated `Survey/SurveyAutoClosure.cs` so Q4 > MAPPING > BOUNDARY `AUTO CLOSURE` no longer uses the default compass-rule vertex shift that can alter segment bearings. The adjusted overlay now solves closure by changing only straight LINE segment lengths while preserving original line bearings.
- Curve segments are translated with their original bulge/radius so existing tangent curve relationships remain intact when the source geometry was already tangent. Existing in-session constraint commands remain available for standalone LINE/ARC runs.
- No LISP helpers were added or changed.

## 2026-06-18 - GIS Trim Inside Cleanup Adjustment
- Removed the experimental `TRIM PIPES` button from CREATE DATA > TOOLS and removed `Gis/GisTrimPipesCommands.cs` / `CLV-GIS-TRIM-PIPES` from the source.
- Updated `CLV-GIS-TRIM-INSIDE` so it now runs single-boundary mode directly without prompting for Single/All each time.
- Limited trim candidates to pipe wall layers only: `C-STRM-PIPE-E` and `C-SSWR-PIPE-E`, so storm/sewer centerline layers such as `C-STRM-PIPE-CNTR-E` and `C-SSWR-PIPE-CNTR-E` are not trimmed.
- No LISP helpers were added or changed.

## 2026-06-18 - Drop inlet stale block definition refresh
- Updated `Ufls/UflsDropInlet.cs` so `UFLS-DROP-INLET` detects older in-drawing drop inlet block definitions that are missing the required `DI_CENTER` attribute, preserves the stale definition under a `_CLV_OLD_yyyyMMdd_HHmmss` block name, and imports the current server DWG definition before placing the new inlet. This prevents older drawings from reusing obsolete `TYPE_*` inlet block definitions.
- Refactored drop inlet and `UFLS_DI_MARK` block loading through a shared server-DWG import helper while keeping the shared Survey block path unchanged.

# 2026-06-15 - GIS Coordinate Zone / Section Reference Split

- Updated Q2 GIS reference controls so coordinate zones and `CLV_Sections` are no longer loaded/unloaded by the same command set.
- `CLV-GIS-LOAD-REFERENCE-LAYERS` and `CLV-GIS-UNLOAD-REFERENCE-LAYERS` are retained as legacy command names, but now affect only the coordinate-zone layers (`NV83.NCRS-LVF` and `NV83.NCRS-LVHEF`). Added clearer aliases `CLV-GIS-DISPLAY-COORDINATE-ZONES` and `CLV-GIS-UNLOAD-COORDINATE-ZONES`.
- Added new standalone section reference commands in `Gis/GisSectionReferenceLayers.cs`: `CLV-GIS-DISPLAY-SECTIONS` and `CLV-GIS-UNLOAD-SECTIONS`.
- Updated Q2 GIS menu text to `SECTION/COORDINATE SYSTEM`, `DISPLAY COORDINATE ZONES`, `UNLOAD COORDINATE ZONES`, `DISPLAY SECTIONS`, and `UNLOAD SECTIONS`.
- Updated Q4 Survey > Mapping menu text from `SECTION` to `TOWNSHIP/RANGE` and from `GIS SECTION MARKER` to `SECTION CORNER MARKER`. Existing command routing remains `SURVEY-GIS-SECTION-MARKER`.
- No LISP helpers were added or changed.

# 2026-06-11 - PLSS Section Label Leading-Zero Fix

- Updated `SURVEY-PLSS-IMPORT-LABELS` / `PLSSIMPORTLABELS` section label formatting so numeric sections no longer display leading zeroes. Marker/cache values like `01` through `09` now populate block attributes as `S1` through `S9`; sections `10` and above remain unchanged.
- No LISP helpers were added or changed.

# 2026-06-11 - PLSS Section Label Scale Fix

- Fixed `SURVEY-PLSS-IMPORT-LABELS` / `PLSSIMPORTLABELS` block insertion scale when Civil 3D reports `CANNOSCALEVALUE` as a ratio such as `0.02` for a 50-scale drawing. The tool now uses `DIMSCALE` first when it is set, and otherwise inverts sub-1 `CANNOSCALEVALUE` values so inserted PLSS blocks come in at the expected drawing scale.
- No LISP helpers were added or changed.

# 2026-06-11 - PLSS Section Label Import Tool

- Added `Survey/SurveyPlssSectionLabelsCommands.cs` with `SURVEY-PLSS-IMPORT-LABELS` / `PLSSIMPORTLABELS`.
- The command prompts for a PLSS label area by window or existing closed polyline, detects the active LVF/LVHEF coordinate system, reads `GIS_SECTION_MARKER` blocks from the matching `GIS_Sections.dwg` cache, builds complete sections from marker quadrant attributes, and inserts the PLSS section label blocks from the shared server block folder.
- Section attributes are populated with the required `S` prefix, shared edge/corner label locations are de-duplicated, and inserted block references use the drawing scale. `DIMSCALE` is preferred when set; otherwise `CANNOSCALEVALUE` is normalized so ratios such as `0.02` insert at scale `50`.
- Added Q4 LABEL tab section `PLSS SECTIONS` with `IMPORT LABELS`. No LISP helpers were added or changed.

# 2026-06-11 - GIS Section Marker feature-selection fix

- Revised `SURVEY-GIS-SECTION-MARKER` / `GISSECTIONMARKER` selection handling after layer-file selections reported selected items but no APN values.
- Layer-file/FDO workflow now converts the AutoCAD selection set to a Map platform feature selection through `AcMapFeatureEntityService.GetSelection(...)` before generating the FDO filter and reading feature properties. This targets selectable `.layer` Map Features where the Properties palette shows `Feature Properties > APN`.
- MAPIMPORT workflow remains supported by reading `APN` from Object Data attached to selected imported closed polylines.
- No LISP routines were added or changed.

# 2026-06-11 - Survey GIS Section Marker Populate Tool

- Added `Survey/SurveyGisSectionMarkerCommands.cs` with `SURVEY-GIS-SECTION-MARKER` / `GISSECTIONMARKER`.
- The command prompts for a marker insertion point, lets the user window/select CLV_Sections GIS polygons, reads each selected feature's `APN` value such as `126-36`, sorts selected sections into NW/NE/SW/SE by polygon geometry center relative to the picked marker point, and inserts `GIS_SECTION_MARKER`.
- Populates `MARKER_ID`, quadrant township/section values, and quadrant section keys. Missing quadrants are allowed and left blank; `MARKER_ID` is built only from found quadrants such as `NE126-36_SE137-01`.
- Added a Q4 Survey > Mapping tab button labeled `GIS SECTION MARKER`. No LISP helpers were added or changed.

# 2026-06-10 - Survey Legend Catalog Update

- Updated `Reference/SurveyLegend.csv` for the Q4 > LABEL > `CREATE LEGEND` checklist to include `SECTION LINE`, `QUARTER SECTION LINE`, and `SIXTEENTH SECTION LINE` under LINEWORK.
- Adjusted the Survey Legend CSV lookup to also walk upward from the loaded DLL folder so local Civil 3D test builds running from `bin\Debug\net8.0-windows` can find the source-tree `Reference/SurveyLegend.csv` without needing a manual file copy.
- No LISP helpers were added or changed.

# 2026-06-10 - Survey Legend Builder

- Added `Survey/SurveyLegendCommands.cs` with `SURVEY-CREATE-LEGEND` / `CREATELEGEND` and `SURVEY-UPDATE-LEGEND` / `UPDATELEGEND`. The create command opens a WinForms checklist from `Reference/SurveyLegend.csv`, prompts for the `SURV_LEG_HEADER` insertion point, loads legend-row DWG blocks from `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\Blocks\Survey\Legend`, and stacks selected items by Sort Order using the single/double spacing rules.
- Added the Q4 > LABEL > `LEGEND` menu section with `CREATE LEGEND` and `UPDATE LEGEND` buttons.
- Added `G-BRDR-ANNO` survey legend layer support in `Shared/LayerStandards.cs`; existing layer settings are preserved if the layer already exists.
- Embedded `Reference/SurveyLegend.csv` in the DLL as a fallback catalog while still allowing an external CSV beside the loaded DLL/source tree to override it.
- Updated `PROJECT_MAP.md` and `COMMAND_INDEX.md`. No LISP helpers were added or changed.

# 2026-06-09 - UFLS Highlight Layer Transparency Standard Fix

- Updated `Shared/LayerStandards.cs` so the existing UFLS highlight layers `V-SURV-HGLT-R` and `V-SURV-HGLT-G` are created/updated with 70% layer transparency.
- This keeps `UFLS-OBJECT-HIGHLIGHT-RED` / `UFLS-OBJECT-HIGHLIGHT-GREEN` consistent with the legacy `HIGHLIGHTRED` / `HIGHLIGHTGREEN` tools when the object highlight commands are the first tools to create the highlight layers in a drawing.
- Object highlight entities remain ByLayer for transparency; no object-level transparency is assigned.
- No LISP helpers were added or changed.

# 2026-06-04 - UFLS Object Highlight Label Mask Draw Order Fix

- Updated `Ufls/UflsObjectHighlight.cs` so plain `DBText`/`MText` highlight backgrounds are still sent behind the selected text, but Civil 3D label-like annotation backgrounds are moved to the front.
- Rationale: UFLS label text uses masking, and a background solid placed behind the label can be hidden by the label mask. Bringing label highlight solids to the front keeps the highlight visible while retaining the tight rotated extents behavior from the previous build.
- Retained `0.01` highlight width, ByLayer transparency, and existing highlight layers `V-SURV-HGLT-R` / `V-SURV-HGLT-G`.
- No LISP helpers were added or changed.

# 2026-06-04 - UFLS Object Highlight Nested Label Fix

- Updated `Ufls/UflsObjectHighlight.cs` so annotation-like objects now try exploded text-component highlighting before falling back to overall object extents.
- Added recursive handling for nested `BlockReference` objects returned by label/block explode operations. This is intended to avoid the oversized axis-aligned highlight rectangle around Civil 3D label graphics and instead create tight rotated highlight solids around the actual DBText/MText components.
- Retained `0.01` highlight width for line/curve/polyline overlays and retained ByLayer transparency on `V-SURV-HGLT-R` / `V-SURV-HGLT-G`.
- Updated `PROJECT_MAP.md` and `COMMAND_INDEX.md`. No LISP helpers were added or changed.

# 2026-06-04 - UFLS Object Highlight Label Refinement

- Reduced `Ufls/UflsObjectHighlight.cs` object-highlight line/curve/polyline overlay width from `0.05` to `0.01`.
- Refined Civil 3D/Aecc label highlighting so label entities are exploded into text components when possible, then tight rotated background solids are created from the actual text extents instead of using the oversized overall label extents.
- Kept highlight object color/linetype/lineweight ByLayer and continued using existing `V-SURV-HGLT-R` / `V-SURV-HGLT-G` layer transparency.

# 2026-06-04 - UFLS Object Highlight Refinement

- Updated `Ufls/UflsObjectHighlight.cs` so object highlight entities no longer force object-level transparency; they now remain ByLayer and rely on the existing `V-SURV-HGLT-R` / `V-SURV-HGLT-G` layer transparency.
- Reduced object-highlight line/curve/polyline overlay width from the previous wide check highlight to `0.01` so selected mapping linework is only slightly emphasized.
- Reworked DBText/MText highlight backgrounds to use a text-rotation-aligned extents solid with smaller padding, reducing the oversized axis-aligned rectangle around rotated labels. Non-text label/block/dimension fallback backgrounds still use compact extents and are sent behind the source object.
- Updated `PROJECT_MAP.md` and `COMMAND_INDEX.md`. No LISP helpers were added or changed.

# 2026-06-04 - UFLS Object Highlight Overlay Update

- Updated `Ufls/UflsObjectHighlight.cs` so `UFLS-OBJECT-HIGHLIGHT-RED` and `UFLS-OBJECT-HIGHLIGHT-GREEN` now create new highlight geometry instead of cloning selected objects directly. Source objects are not modified.
- Reused the existing legacy highlight layers `V-SURV-HGLT-R` and `V-SURV-HGLT-G`; removed the previously introduced `V-SURV-HILITE-RED` / `V-SURV-HILITE-GREEN` layer standards from code/documentation.
- Line, arc, circle, polyline, and generic curve selections receive wide translucent overlay polylines. Text, labels, dimensions, and blocks receive translucent extents-based background solids that are sent behind the source annotation for readability.
- Updated `Shared/LayerStandards.cs`, `PROJECT_MAP.md`, and `COMMAND_INDEX.md`. No LISP helpers were added or changed.

# 2026-06-04 - UFLS Q1 highlight buttons and curve-tool retirement

- Updated `Ufls/UflsPalette.cs` so Q1 > CHECK > VERIFICATION now includes `OBJECT HIGHLIGHT RED` and `OBJECT HIGHLIGHT GREEN` below the existing legacy draw-over highlight tools.
- Added `Ufls/UflsObjectHighlight.cs` with `UFLS-OBJECT-HIGHLIGHT-RED` and `UFLS-OBJECT-HIGHLIGHT-GREEN`; the commands copy selected entities to dedicated red/green overlay layers instead of modifying the source objects.
- Added shared UFLS highlight layer names/specs in `Shared/LayerStandards.cs`: `V-SURV-HGLT-R` and `V-SURV-HGLT-G`.
- Removed the Q1 CHECK buttons and command code for the retired curve tools: `TOP OF PIPE - CURVE` / `UFLS1C` and `LABEL INVERT - CURVE` / `UFLS-PIPE-LABEL-FL`.
- Renamed the Q1 `PIPE INFO` button and Pipe Info dialog title to `PIPE INFO @ POINT`.
- Updated `PROJECT_MAP.md` and `COMMAND_INDEX.md`. No LISP helpers were added or changed.

# 2026-05-21 - CLVHELP Knowledge Base launcher

- Added `Help/ClvHelpCommands.cs` with command-line command `CLVHELP`.
- The command opens `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\CLV CAD_KNOWLEDGE_BASE\index.html` through the Windows default browser.
- No LISP helpers were added or changed. Existing palettes and tool routines were not modified.

# 2026-05-21 - Survey Photo Review Map Preview Fix

- Updated `Survey/SurveyPhotoReview.cs` so the embedded map preview uses an explicit HTTP request path for OpenStreetMap tiles, with an Esri World Imagery fallback, instead of the older default `WebClient` request behavior that can fail under Civil 3D / Windows network settings.
- The tile request now sets TLS 1.2, image accept headers, a CLV user agent, default Windows proxy credentials, short timeouts, and retries the standard OSM tile host, the `a`, `b`, and `c` subdomains, and an Esri imagery endpoint before reporting failure.
- `OPEN MAP` remains unchanged and still launches the external browser Google Maps pin.
- No LISP helpers were added or changed.

# 2026-05-21 - Palette Stagger and Photo Review Alias Update

- Kept the approved session placement behavior: each PaletteSet is forced to the CAD-window-relative startup location only once per Civil 3D session, then user-moved positions are preserved on later opens during that session.
- Adjusted first-open startup offsets so Q2/GIS, Q3/PCT, and Q4/SURVEY stagger down/right from Q1/UFLS instead of Q1 and Q2 opening at the same location.
- Adjusted GIS CREATE DATA startup offset to continue opening separately from the main Q2/GIS palette.
- Added `VIEWPHOTOS` and `VIEW-PHOTOS` command aliases to the existing SURVEY PHOTO REVIEW PaletteSet path so CLV menu launches use the same CAD-window-relative first-open and session-preserved location behavior.
- No processing logic or LISP helpers were changed.

## 2026-05-21 - Palette Session Placement Test

- Updated `Shared/PalettePositionHelper.cs` so each PaletteSet is only forced to the CAD-window-relative startup location once per Civil 3D session.
- After first open, repeated palette command calls only show the existing PaletteSet and do not reset its location, allowing users to move a menu once during that CAD session and reopen it where they placed it.
- Adjusted startup offsets for Q1/UFLS, Q2/GIS, CREATE DATA, Q3/PCT, Q4/SURVEY, and SURVEY PHOTO REVIEW so first-open menu positions are staggered and less likely to cover each other, the ribbon, or left-side tool palettes.
- No command routing, processing logic, or LISP helpers were changed.

## 2026-05-21 - Palette Placement Follow-up

- Updated `Shared/PalettePositionHelper.cs` so CLV palettes set size during creation but apply the CAD-window-relative floating location after `PaletteSet.Visible = true`.
- Updated Q1/UFLS, Q2/GIS, CREATE DATA, Q3/PCT, Q4/SURVEY, and SURVEY PHOTO REVIEW palette show paths to use the shared `ShowNearAutoCadWindow(...)` helper.
- Rationale: AutoCAD/PaletteSet can restore persisted floating coordinates during show; applying location after show is intended to override stale saved coordinates from another monitor.
- No LISP changes. No command removals beyond the prior JOIN CENTERLINES retirement.

# 2026-05-21 - CAD-window-relative palette placement test

- Added `Shared/PalettePositionHelper.cs` to place newly created floating PaletteSet windows relative to the current AutoCAD main window instead of using stale absolute screen coordinates from another monitor.
- Updated Q1/UFLS, Q2/GIS, Q3/PCT, Q4/SURVEY, GIS CREATE DATA, and SURVEY PHOTO REVIEW palette hosts to apply the shared initial placement helper while keeping the approved compact palette sizes.
- This is a UI-placement-only change; command routing, tool logic, GIS processing, UFLS routines, Survey routines, Point Cloud routines, and LISP helpers were not changed.
- No LISP helpers were added or changed.

# 2026-05-21 - Retire GIS Join Centerlines

- Removed the retired `CLV-GIS-JOIN-CENTERLINES` command definition and its dedicated join workflow code from `Gis/GisImport.cs`.
- Removed the `JOIN CENTERLINES` button from the Q2 GIS palette.
- Updated `PROJECT_MAP.md` and `COMMAND_INDEX.md` so no active menu or command listing references the retired workflow.
- No LISP helpers were added or changed.

# 2026-05-20 - CAD palette compact standard applied to all Q menus
- Applied the approved Q1 compact text/row standard to the remaining in-CAD command palettes: Q2 GIS, Q3 Point Cloud, Q4 Survey, and the GIS CREATE DATA palette.
- Standardized command-launcher palette controls around `7.0 pt` button/section text, `256 px` command rows, `24 px` row height, per-tab vertical scrolling, and compact default/minimum PaletteSet sizes.
- Updated Q2 to open at `340 x 700` with a `300 x 540` minimum, Q3 at `320 x 600` with a `280 x 480` minimum, Q4 at `340 x 700` with a `300 x 540` minimum, and GIS CREATE DATA at `340 x 700` with a `300 x 540` minimum. Q1 remains `360 x 760` with a `320 x 560` minimum.
- No command routing, geometry logic, GIS processing, point-cloud processing, survey tools, or LISP routines were changed.

# 2026-05-20 - Q1 palette ribbon-text refinement
- Refined `Ufls/UflsPalette.cs` after another on-screen review so Q1 button/section text uses `7.0 pt` instead of `7.5 pt`. Palette size, button width, button height, vertical scrolling, and command routing are unchanged.

# 2026-05-20 - Q1 palette button text reduction
- Refined `Ufls/UflsPalette.cs` after on-screen review so Q1 button/section text uses `7.5 pt` instead of `8.25 pt`, and button/row height uses `24 px` instead of `26 px`. Default palette size remains `360 x 760`, minimum size remains `320 x 560`, and command routing is unchanged.

# 2026-05-20 - Q1 palette compact sizing + font test

- Updated `Ufls/UflsPalette.cs` to make Q1 less overwhelming on 1920 x 1200 CAD workstations. The palette now opens at `360 x 760` and can resize down to `320 x 560` instead of being fixed at `432 x 900`.
- Reduced Q1 command button/row width from `360` to `256`, reduced row height from `28` to `26`, reduced explicit button/section fonts from 9 pt to 8.25 pt, and retained vertical scrolling inside each tab.
- No command behavior, LISP routines, UFLS geometry logic, GIS tools, Q2, Q3, or Q4 files were modified.

# 2026-05-19 - LABEL ROADS paper height and boundary cleanup

- Updated `Survey/SurveyRoadLabels.cs` so converted road labels are forced to `0.14` paper text height by applying the current annotation scale factor to the stored model text height.
- Added post-`MAPLABEL2ANN` outside-boundary cleanup for the automated `LABEL ROADS` workflow. Newly converted DBText/MText outside the selected/drawn boundary is erased; inside-boundary text is standardized as before.
- Kept the previous prompt-removal behavior: the temporary assessor street-centerline map layer/connection is always removed after conversion or cancellation.
- No LISP helpers were added or changed.

# 2026-05-19 - LABEL ROADS prompt removal

- Removed the `Unload the temporary road layer after conversion?` Yes/No prompt from `Survey/SurveyRoadLabels.cs`.
- `LABEL ROADS` now behaves as Yes every time: temporary assessor street-centerline map layers/connections are removed after MAPLABEL2ANN finishes, and also removed if the user cancels at the final continue/cancel prompt before conversion.
- No LISP helpers were added or changed.

# 2026-05-11 - UFLS1 top-of-pipe pick-direction elevation fix

- Updated `Ufls/UflsTopOfPipe.cs` so UFLS1 reverses the station/elevation slope whenever the fitted plan-view direction is reversed to match the user's COGO pick order. This preserves the recent Civil-style 2D best-fit alignment correction while preventing start/end invert elevations from being swapped in drawings where the PCA direction naturally points opposite the pick direction.
- No LISP helpers were added or changed. No palette layout, UFLS1C curve, UFLS5 trim, label, GIS, or survey-closure routines were modified.

# 2026-05-07 - Survey Auto Closure Area Report

- Updated `Survey/SurveyAutoClosure.cs` so AUTO CLOSURE computes original, adjusted, and delta boundary area values for the selected LINE/ARC or open lightweight POLYLINE boundary. Area calculations include bulged arc/circular-segment area and close small open misclosures for reporting only.
- Updated `Survey/SurveyClosureReview.cs` so the modeless closure report summary displays original area, adjusted area, and area delta in square feet and acres.
- No LISP, Photo Review, Best Fit, or split-view review code changed.

- Revised Phase 3 constraint workflow: `SURVEY-CLOSURE-ADD-CONSTRAINT` now opens the WinForms constraint manager instead of the keyword prompt, avoiding the keyword fallback that caused every choice to become `LOCK RADIUS`.
- Added `SURVEY-CLOSURE-CONSTRAINTS` dialog with constraint type dropdown, Add/Pick, Clear All, and live constraint list.
- Constraint-selected objects are highlighted using the centralized survey-map constraint highlight color index in `LayerStandards.cs`; Clear All restores original entity color indexes while the session is active.
- Simplified Q4 > MAPPING > BOUNDARY to `AUTO CLOSURE` and `CONSTRAINTS`; command-line list/clear commands remain available.
## 2026-05-06 - Q4 AUTO CLOSURE Phase 3 constraints
- Added `Survey/SurveyClosureConstraints.cs` with in-session constraint commands: `SURVEY-CLOSURE-ADD-CONSTRAINT`, `SURVEY-CLOSURE-LIST-CONSTRAINTS`, and `SURVEY-CLOSURE-CLEAR-CONSTRAINTS`.
- Previous Phase 3 command-line add/list/clear workflow has been replaced on the Q4 palette by the `CONSTRAINTS` dialog button; list/clear commands remain callable from the command line.
- Updated `Survey/SurveyAutoClosure.cs` so standalone LINE/ARC closure runs can consider session constraints before output is created. Initial supported constraints: lock radius, lock bearing, lock length, and keep parallel.
- Constraints are temporary only and do not add XData. No LISP changes.

# 2026-05-06 - Survey Auto Closure Phase 2C Fix 1

- Corrected bulge tangent math used by Phase 2C. AutoCAD bulge tangent directions are now evaluated as start = chord - theta/2 and end = chord + theta/2. This fixes blank tangency report fields and allows originally tangent arcs to be detected/preserved.
- Corrected the tangent-preserving bulge solve to use the same signed tangent convention.
- No XData, LISP, Photo Review, Best Fit, or old split-view review commands were modified.

# 2026-05-06 - Survey Auto Closure Phase 2C

- Rebuilt Phase 2C from the Phase 2B radius/marker package.
- Added tangent-preserving arc adjustment for arcs that were originally tangent to adjacent boundary segments within tolerance.
- Added tangency status values to closure review data and report window columns.
- Adjusted arc overlay copies now use the solved tangent-preserving bulge when applicable.
- No XData, LISP, Photo Review, Best Fit, or old split-view review commands were modified.

# 2026-05-06 - Survey Auto Closure Phase 2B

- Updated `Survey/SurveyClosureReview.cs` so `SURVEY-CLOSURE-MARKERS` creates one consistent marker size for the entire closure review run instead of scaling each marker by segment length.
- Added arc radius columns to the modeless closure report: original radius, adjusted radius, and radius delta. Straight-line segments leave radius values blank.


# 2026-05-06 - Survey Auto Closure Phase 2A

- Revised `SURVEY-AUTO-CLOSURE` selection filter to allow standalone AutoCAD `ARC` entities in addition to `LINE` and lightweight `POLYLINE`.
- Added mixed `LINE`/`ARC` ordering for boundary chains. Standalone arcs are moved to `V-SURV-MAP~-ORIG`; adjusted arc overlays are created as one-segment lightweight polylines with the original bulge preserved on `V-SURV-MAP~-ADJ~`.
- No Photo Review, Best Fit, LISP, or split-viewport review code changed.

# 2026-05-06 - Survey Auto Closure Phase 2

- Moved Q4 > MAPPING `AUTO CLOSURE` out of `SURVEY MAP` and into a new `BOUNDARY` section between `SURVEY MAP` and `REVIEW`.
- Updated `Survey/SurveyAutoClosure.cs` so one open lightweight `POLYLINE` may contain bulged arc segments. The adjusted overlay preserves the source bulges while closing the endpoint chain.
- Updated closure report/review data to calculate arc segment lengths from bulge/chord values and label curve rows as `ARC` in the modeless report.
- Did not add XData or LISP helpers. Existing Photo Review and Best Fit Map source files were not modified.

# 2026-05-06 - Survey Closure Report Window Cleanup

- Removed abandoned split-view closure review commands/buttons and source implementation.
- Updated the WinForms closure report so `ZOOM TO SELECTED` and double-click zoom keep the report window open for continued review.

# CLV_CivilTools CHANGELOG

## 2026-05-06 - Q4 closure review runtime fix
- Added command-level exception handling so review-tool failures report to the command line instead of opening a Windows JIT dialog.
- No Photo Review files, existing photo commands, or LISP routines were modified.

## 2026-05-06 - Q4 survey closure review viewport isolation fix
- Left review viewport freezes `V-SURV-MAP~-ADJ~` so it shows only `V-SURV-MAP~-ORIG`; right review viewport freezes `V-SURV-MAP~-ORIG` so it shows only `V-SURV-MAP~-ADJ~`.
- No LISP helpers were added or changed.

# 2026-05-06 - Survey Auto Closure Phase 1

- Updated `Survey/SurveyAutoClosure.cs` so `SURVEY-AUTO-CLOSURE` now prompts for a traverse start point after selection.
- Updated the report to include start point, end point, closure vector bearing, relative precision ratio, PPM error, worst adjusted segment, and total absolute bearing adjustment.
- Changed Phase 1 output behavior so original selected linework is moved to `V-SURV-MAP~-ORIG` and adjusted overlay copies are created on `V-SURV-MAP~-ADJ~` for visual verification.
- Added the new survey map closure layers only through `Shared/LayerStandards.cs` so future routines can use the same managed layer definitions.
- Added `Survey/SurveyAutoClosure.cs` with `SURVEY-AUTO-CLOSURE`.
- Added `AUTO CLOSURE` to Q4 > MAPPING under the existing `SURVEY MAP` section.
- Phase 1 supports multiple straight `LINE` entities or one open straight-segment lightweight `POLYLINE`.
- The command orders selected linework into one chain, calculates the misclosure, distributes correction by cumulative traverse length, reports max length and bearing changes, and requires confirmation before modifying selected geometry.
- Arc/bulge/curve support is intentionally not modified in this phase; those entities are rejected so future tangent-preserving curve logic can be added safely.
- No LISP helper was added or changed.

# CLV_CivilTools CHANGELOG

## 2026-05-05 - restore project documentation and remove retired GIS profile workflow
- Restored the fuller historical documentation from `Logs.zip` backup instead of the overly-short generated docs from the prior cleanup package.
- Removed the retired Map 3D profile-based pipe-network GIS export/check workflow from the active source package.
- Removed local build-output and duplicate-support folders (`bin/`, `obj/`, and project-local `Reference/`) from the deliverable zip so source files remain the only maintained project content.
- Confirmed LISP helper references remain server-only under `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\...`; no new LISP routines were created.

## 2026-03-31 - manual GIS object data transfer
- Added `Gis/GisObjectDataTransfer.cs` with `CLV-GIS-OD-XFER`, a .NET wrapper that loads `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\CLV_GIS_OD_HELPERS.lsp` and launches the ADE/LISP `CLV-GIS-OD-COPY` workflow so source/destination selection stays inside the helper routine.
- Updated `Ufls/UflsPalette.cs` so Q1 > GIS now includes a new `OBJECT DATA` section beneath `SEWER` with the `XFER OBJECT DATA` button.
- Updated `PROJECT_MAP.md` and `COMMAND_INDEX.md` for the new manual GIS OD transfer entry point.

- Updated `Ufls/UflsManholeAutoCreate.cs`.
  - Fixed `1P MANHOLE - ALL` so it ensures the `UFLS_MH_MARK` block definition is loaded before placement, matching the center-marker behavior used by the other manhole commands.
## 2026-03-25 - Q1 1P / 3P manhole split
- Updated `Ufls/UflsPalette.cs`.
  - Renamed `MANHOLE - ALL` to `3P MANHOLE - ALL`.
  - Renamed `MANHOLE - SINGLE` to `3P MANHOLE - SINGLE`.
  - Added `1P MANHOLE - ALL` and `1P MANHOLE - SINGLE` directly beneath the 3P manhole buttons in Q1 > CHECK > 2D LINEWORK.
- Updated `Ufls/UflsManholeAutoCreate.cs`.
  - Added `UFLS41P` and `UFLS-MH-AUTO-1P` for single-point manhole auto-create workflows.
  - Added `RunFromPalette1P(...)` and palette routing support so the new 1P auto workflow uses the same survey-source dialog / cluster-tolerance prompts as the 3P version.
  - Added a mode switch inside the shared auto-create core so 1P auto workflows use the clustered point centroid directly instead of solving a 3-point circle.
- Updated `Ufls/UflsSingleManhole.cs`.
  - Added `UFLS61P` for single-point manhole creation from one selected COGO center point.
  - Reused the existing block / layer / visibility logic so 1P single manholes still honor 48 / 60 / 72 visibility states when present in the selected point description.
- Updated `PROJECT_MAP.md` and `COMMAND_INDEX.md` for the new Q1 labels and command entries.

- Added `Gis/GisSurveyCacheFinalize.cs` with `CLV-GIS-FINALIZE-STRC` and `CLV-GIS-FINALIZE-PIPES` for survey-cache compare → visualize → confirm → finalize export. The workflow auto-detects sewer vs storm from finalized layers, resolves the drawing coordinate system, attaches the matching survey-cache structures + pipes as gray preview xrefs (ACI 251), draws CAD conflict markers for exact duplicates / nearby conflicts, prompts before export, then appends the current finalized entities into the matching survey cache DWG. Updated `Ufls/UflsPalette.cs` so Q1 > GIS adds an `EXPORT` section with `FINALIZE STRUCTURES` and `FINALIZE PIPES`. No new LISP routines were created.
- Updated `Gis/GisSewerPipeOdOffset.cs` and `CLV_CivilTools.csproj` so the sewer pipe helper loads only from the server LISP path and is no longer copied into the build output folder. Updated Q2 > GIS so `DATA` replaces `CC GIS DATA`, restored Sewer / Storm GIS dataset selections, and added parallel SURVEYED dataset selections that load the new coordinate-system-specific survey cache DWGs (`Survey_Sewer_Pipes.dwg`, `Survey_Sewer_Structures.dwg`, `Survey_Storm_Pipes.dwg`, `Survey_Storm_Structures.dwg`).
﻿## 2026-03-25 - sewer GIS automation
- Added `Gis/GisSewerManhole.cs` with `CLV-GIS-SSWR-MH` and `CLV-GIS-SSWR-MH-ALL` for sewer manhole conversion from imported `Structures` points. The workflow can explode nearby structure blocks when found, migrate source inner / outer linework from `V-SURV-STRC-INNR-2D~~` and `V-SURV-STRC-OUTR-2D~~` to `C-SSWR-STRC-INNR` and `C-SSWR-STRC-E`, and queue ADE/LISP Object Data copy to the sewer outer structure objects.
- Added `Gis/GisSewerPipeOdOffset.cs` plus server-hosted helper `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\CLV_GIS_SSWR_PIPE_OD_OFFSET.lsp` for sewer pipe conversion. The single and all commands read `InsideDiameter` through ADE/LISP, move under-12" source lines directly to `C-SSWR-PIPE-E`, offset 12"+ pipes by `InsideDiameter / 2`, move source centerlines to `C-SSWR-PIPE-CNTR-E`, place offset walls on `C-SSWR-PIPE-E`, and adjust created walls back to `C-SSWR-STRC-INNR` when intersections are found.
- Added `Gis/GisSewerGisCommands.cs` with `CLV-GIS-SSWR-GIS` to run sewer manholes first and sewer pipes second for the new Q1 > GIS > SEWER `GIS PREP - ALL` workflow.
- Updated `Ufls/UflsPalette.cs` so the Q1 > GIS > SEWER buttons now run the live sewer commands instead of placeholders.
- Updated `CLV_CivilTools.csproj` post-build copy so only the DLL is copied beside the dev DLL; the sewer pipe helper now loads from the server LISP location.
- Updated `PROJECT_MAP.md` and `COMMAND_INDEX.md` for the sewer GIS workflow additions.

## 2026-03-25 - Q1 GIS sewer workflow palette labels
- Updated `Ufls/UflsPalette.cs` so the Q1 > GIS tab now uses the requested storm-drain labels: `GIS PREP - ALL`, `JUNCTIONS AND INLETS - ALL`, `DROP INLET - SINGLE`, `JUNCTION STRUCTURE - SINGLE`, and `PIPE`.
- Added new `SEWER` and `CLEANUP` sections on the Q1 > GIS tab.
- Added placeholder sewer buttons for `GIS PREP - ALL`, `MANHOLE`, and `PIPE`.
- Moved `ERASE POINTS` under the new `CLEANUP` section.
- Updated `PROJECT_MAP.md` and `COMMAND_INDEX.md` to reflect the revised Q1 GIS layout.

## 2026-03-24 fix24
- Fixed malformed-list load error in `CLV_GIS_PIPE_OD_OFFSET.lsp` by closing the endpoint-intersection helper expression correctly.

- Fixed CLV_GIS_PIPE_OD_OFFSET.lsp endpoint-connection error (`bad argument type: consp`) by hardening point validation before apparent-intersection adjustment.
- `CLV_GIS_PIPE_OD_OFFSET.lsp`: server-hosted storm ADE/LISP helper expected at `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\CLV_GIS_PIPE_OD_OFFSET.lsp`; reads `InsideDiameter`, creates storm pipe wall offsets, and adjusts created wall endpoints to `C-STRM-STRC-INNR` when possible.
- Updated the storm pipe OD offset helper source so qualifying source centerlines move to `C-STRM-PIPE-CNTR-E` and created offset wall lines move to `C-STRM-PIPE-E`.
- Added `CLV-GIS-PIPE-CONNECT-STRM` to move selected storm pipe wall endpoints onto selected structure inner-wall polylines and standardize those layers for storm structure cleanup.
- Added `CLV-GIS-JS-FROM-POINT` to let the user select one imported junction-structure point while the routine automatically finds the matching outer and inner closed polylines, moves them to `C-STRM-STRC-E` / `C-STRM-STRC-INNR`, and copies Object Data to the outer polyline through the existing ADE/LISP helper path.
- Fixed `Gis/GisPipeOdOffset.cs` to load the shared server helper exactly like `CLV_GIS_OD_HELPERS.lsp`: queue the LISP load first, then launch `CLV-GIS-PIPE-OFFSET-OD-LSP` as a separate command-line command. This removes the bad inline wrapper invocation that caused both the compile error and the helper-not-defined runtime behavior.
- Fixed `CLV-GIS-PIPE-OFFSET-OD` helper launch so the .NET wrapper now loads the shared server LISP file and then runs the helper by command name on the AutoCAD command line. This avoids the `no function definition: CLV-GIS-PIPE-OFFSET-OD-LSP` error caused by trying to invoke the helper as a raw AutoLISP function symbol.
- Fixed `CLV-GIS-PIPE-OFFSET-OD` to use a dedicated ADE Object Data AutoLISP helper for reading `InsideDiameter` from Map-import pipe centerlines. This avoids the false `noOD=1` result seen when the .NET reflection path did not see the same OD records that `CLV-GIS-OD-INSPECT` could read.
- Revised `CLV-GIS-PIPE-OFFSET-OD` to load `CLV_GIS_PIPE_OD_OFFSET.lsp` only from the shared server support path.
﻿## 2026-03-18 (Survey Best-Fit Map QA Update)
- **Survey/SurveyBestFitMap.cs**
  - Updated `SURVEY-BESTFIT-MAP` so the workflow is explicit about fixed survey shots versus moving map points.
  - Added `Undo` during pair collection and running preview feedback after the second pair and later.
  - Added second-pick support for either free point picks or direct selection of a point entity (`CogoPoint`, `DBPoint`, or `BlockReference`).
  - Added Forward / Reverse apply choice before modifying the selected xref / block reference so field testing can confirm transform direction.
  - Updated CSV reporting to record the applied direction.

﻿## [2026-03-18.1]

### Added
- **Survey/SurveyBestFitMap.cs**
  - Added `SURVEY-BESTFIT-MAP`, a survey-map rigid-registration prototype for xref / block overlays.
  - The command prompts for a target xref / block reference, collects numbered survey-shot to map-point control pairs, solves a least-squares XY move + rotate fit with no scaling, applies the transform to the selected reference, and writes a CSV residual report for QA.

### Changed
- **Survey/SurveyPalette.cs**
  - Added a dedicated `Q4` survey-mapping palette with a `BEST FIT MAP` button that routes to `SURVEY-BESTFIT-MAP`.
- **Ufls/UflsPalette.cs**
  - Removed the survey-map registration prototype from the UFLS Q1 palette so mapping tools stay separate from underground workflows.

### Documentation
- **PROJECT_MAP.md**
  - Updated architecture and source-responsibility notes for the new survey-map best-fit workflow.
- **COMMAND_INDEX.md**
  - Added the `SURVEY-BESTFIT-MAP` command entry and updated Q1 / Q4 palette notes.
- **CHANGELOG.md**
  - Added this maintenance entry.

## 2026-03-17 - GIS palette cleanup + import dialog defaults
- Updated `Gis/GisPalette.cs`.
  - Q2 > GIS now keeps `DATA` for `IMPORT GIS`, adds a separate `GIS TOOLS` section, moves `JOIN CENTERLINES` into that section, and removes the `OD INSPECT` palette button.
- Updated `Gis/GisImport.cs`.
  - Removed the `Prebuilt DWG cache is always used first when available.` note from the import dialog.
  - Changed the default dataset selection so `Sewer Pipes`, `Sewer Structures`, `Storm Pipes`, and `Storm Structures` start unchecked.
- Updated documentation: `PROJECT_MAP.md`, `COMMAND_INDEX.md`, and `CHANGELOG.md`.

﻿## 2026-03-17 - GIS OD helper server path
- Updated `Gis/GisImport.cs` so `CLV-GIS-OD-INSPECT` and the centerline OD-copy helper now resolve `CLV_GIS_OD_HELPERS.lsp` only from `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\CLV_GIS_OD_HELPERS.lsp`. Local deployment, current-directory, and project-tree fallback paths were removed so runtime behavior matches shared-user deployment expectations.
- Updated `PROJECT_MAP.md` and `COMMAND_INDEX.md` to document the server-only helper resolution path.

## 2026-03-17 - GIS join prompt focus
- Updated `Gis/GisImport.cs` so `CLV-GIS-JOIN-CENTERLINES` and `CLV-GIS-OD-INSPECT` now attempt to force focus back to the drawing view before interactive prompts. This reduces the extra click needed after launching the command from the GIS palette so the pickbox/crosshair appears immediately.

## 2026-03-17 - GIS ADE helper pivot
- Pivoted GIS OD helper loading to the shared Lisp server path so ADE-based OD inspection/copy no longer depends on local deployment copies.
- Updated `Gis/GisImport.cs` so `CLV-GIS-OD-INSPECT` now loads the ADE/LISP helper and launches `CLV-GIS-OD-INSPECT-LSP`, which reads Object Data through ADE (`ade_odgettables`, `ade_odgetfield`, etc.) instead of the unreliable managed Map OD reflection path.
- Updated `Gis/GisImport.cs` so `CLV-GIS-JOIN-CENTERLINES` now prefers retaining an existing LWPOLYLINE from the selected set when available, converts LINE entities to temporary LWPOLYLINE geometry without erasing the original source until after join, and can trigger the ADE/LISP OD copy helper when the retained base had to start from a LINE.
- Updated `Gis/GisPalette.cs` to expose `OD INSPECT` on the GIS tab.
- Updated `PROJECT_MAP.md` and `COMMAND_INDEX.md` for the ADE-helper workflow.

## 2026-03-17 - GIS OD API read fix
- Updated `Gis/GisImport.cs` so Map Object Data record reads now prefer the actual Map 3D Object Data open-mode enum when calling `GetObjectTableRecords(...)`, instead of relying only on AutoCAD database open modes. This targets the issue where the Properties palette showed `OD:Street_Centerlines` but `CLV-GIS-OD-INSPECT` reported no attached OD.
- Expanded OD record attach calls to try both `(record, entityId)` and `(entityId, record)` signatures so snapshot reapply is more tolerant of Map 3D API overload differences.
- Updated `PROJECT_MAP.md` and `COMMAND_INDEX.md` to reflect the tightened Map OD inspection / restore path.

﻿## 2026-03-17 - GIS OD inspect + snapshot restore
- Added `CLV-GIS-OD-INSPECT` in `Gis/GisImport.cs` to dump all visible Map Object Data tables, records, and field/value pairs on a selected entity for command-line troubleshooting.
- Updated `CLV-GIS-JOIN-CENTERLINES` so it snapshots the full `Street_Centerlines` Map Object Data record set from the selected base/source entities before any LINE-to-LWPOLYLINE conversion, then reapplies that snapshot to the retained joined object after conversion / join.
- Updated `PROJECT_MAP.md` and `COMMAND_INDEX.md` for the new OD inspection command and the revised full-snapshot join behavior.

## 2026-03-17 - GIS OD reattach hardening
- Updated `Gis/GisImport.cs` so `CLV-GIS-JOIN-CENTERLINES` now removes any existing `Street_Centerlines` object-data record from the retained entity, creates a fresh record, writes `STREET`, reattaches it, and verifies the write-back after join / conversion.
- Added command-line diagnostics for the retained object OD state and the OD restore steps so intermittent street-centerline object-data loss is easier to troubleshoot.

- GIS: object-data lookup now resolves Map OD table names more robustly, including `OD:` prefixes and `_OD`/dataset-name variants, and the GIS dataset mappings now target the live Map object-data table names (`Parcels`, `Street_Centerlines`, `SS_Pipes`, `SS_Structures`, `SD_Pipes`, `SD_Structures`) so restored street OD can be found after join/conversion.
- GIS import: coordinate-system detection now probes drawing GeoData first, then broader Map 3D reflection paths (`AcMapMap`, active project, and related coordinate/projection members) so cache selection follows the active LVF vs LVHEF drawing CS more reliably.
- GIS import: drawing coordinate-system detection now normalizes LVF vs LVHEF before selecting cache DWGs, and `CLV-GIS-CACHE-STATUS` reports both the normalized drawing CS and the raw Map 3D CS string when available.
- GIS: `CLV-GIS-JOIN-CENTERLINES` now reads `Street_Centerlines.STREET` from the selected source centerlines and reapplies it to the retained joined polyline after any required LINE-to-LWPOLYLINE conversion so trimmed / converted centerlines keep their street object data.
- GIS: `CLV-GIS-JOIN-CENTERLINES` now accepts LINE or LWPOLYLINE street centerlines as the base object, converts LINE entities to LWPOLYLINE before joining, and reports when conversion happened so trimmed / mixed-type GIS centerlines can be joined more reliably.
- GIS: rewrote `CLV-GIS-JOIN-CENTERLINES` as an interactive retained-base join workflow. Users now pick the street-centerline polyline to keep first, then select additional `GIS-ROAD-CNTR` segments to join into it so the kept base object retains its `STREET` object data.
﻿## 2026-03-17 - Q1 ADJUST menu regrouping
- Updated `Ufls/UflsPalette.cs`.
  - Renamed `SEWER MAIN` to `SEWER MAIN - MOVE`.
  - Added `STORM JUNCTION STRUCTURE` beneath `SEWER LATERAL` and moved `RESIZE JUNCTION` into that dedicated section.
  - Renamed `STORM DRAIN` to `STORM DRAIN - MOVE`.
  - Added a bottom `SWAP MATERIAL` section and moved `PVC --> C900`, `RCP --> C900`, `C900 --> RCP`, and `C900 --> PVC` into that section.
- Updated `PROJECT_MAP.md`, `COMMAND_INDEX.md`, and `CHANGELOG.md` to reflect the revised Q1 ADJUST layout.

## 2026-03-16
- GIS import: structure-point cache imports now sync `PDMODE` and `PDSIZE` from the source cache DWG so imported sewer / storm structure points match the cache drawing display.
- GIS import: cache clipping now skips expensive curve-splitting work for entities whose extents are clearly fully inside or fully outside the boundary, improving cache import speed on large datasets.
- GIS import: restored the cache-first workflow in `Gis/GisImport.cs` and removed the user cache checkbox so prebuilt dataset DWGs are always used first when available.
- GIS import: enlarged the WinForms options dialog so the full dataset list and `OK` / `CANCEL` buttons are visible.
- GIS import: added optional layer-master sync from `\\ci.las-vegas.nv.us\pw_data_depot\PW_GRID_City\000-City\GIS\_CLV_CACHE\_MASTER\CLV_GIS_LAYER_MASTER.dwg`, then enforced fallback CLV layer settings matching the GIS layer colors / plot-style expectations (`GIS-ROAD-CNTR` red, sewer layers 106, storm layers 60, utility layers `UTIL-GREEN-E`).
- GIS import: added cache / layer-master status reporting to `CLV-GIS-CACHE-STATUS`.
- Aerials: `Unload Aerials` still removes active imagery layers and now also attempts best-effort cleanup of matching Nearmap / LasVegas map connections in the current session.

## 2026-03-12
- GIS import: added document locking around automatic import / cleanup to prevent eLockViolation from palette runs.
- GIS import: clarified current boundary handling in command-line logging.

## 2026-03-12 - GIS import final automation
- Replaced the GIS import prototype with automatic ManagedMapApi-driven SHP import.
- IMPORT GIS now imports selected shapefiles directly into the active drawing, reassigns CLV GIS layers, clips to the selected boundary, and runs duplicate cleanup automatically.
- Added direct ManagedMapApi reference in the project file for Map 3D import/export support.

﻿## 2026-03-12 - GIS import session folder fallback
- Updated `Gis/GisImport.cs`.
  - Hardened the GIS import session-folder creation so unsaved drawings or protected install folders do not cause an `UnauthorizedAccessException`.
  - The command now tries the drawing folder first, then falls back to `Documents\CLV_CivilTools\GIS\Import`, then `%TEMP%\CLV_CivilTools\GIS\Import`.
  - This keeps the guided MAPIMPORT workflow usable even when the active drawing name resolves under `C:\Program Files\Autodesk\AutoCAD 2026`.

## 2026-03-12 - GIS import setup prototype
- Added `Gis/GisImport.cs`.
  - Added `CLV-GIS-IMPORT` for guided GIS import setup from the CLV shapefile folders for Parcels, Street Centerlines, Sewer Pipes, Sewer Structures, Storm Pipes, and Storm Structures.
  - Added a WinForms options dialog with separate dataset checkboxes plus boundary-mode selection for `Select existing closed polyline` or `Draw temporary polygon`.
  - Added session-summary output for MAPIMPORT using source coordinate system `NAD_1983_StatePlane_Nevada_East_FIPS_2701_Feet`, CLV target GIS layers, clip-to-boundary workflow notes, and Object Data import reminders.
  - Added `CLV-GIS-CLEANUP` to remove exact duplicate imported GIS linework from the CLV GIS line layers after review.
- Updated `Gis/GisPalette.cs`.
  - Replaced the GIS placeholder tab content with `DATA` > `IMPORT GIS`.
- Updated documentation: `PROJECT_MAP.md`, `COMMAND_INDEX.md`, and `CHANGELOG.md`.

- UFLS1 (CREATE TOP OF PIPE): updated auto-pan to use a dynamic search half-width instead of a fixed 5'. The width now scales from the current view and recent point spacing, while still reusing the first manual pick-area zoom level. Removed the `Last` zoom-back behavior so the command only pans forward to the next shot grouping and keeps the temporary guide line on V-TEMP-PIPEPICK.
## 2026-03-12 - Sewer main pipe material swap commands
- Added `Ufls/UflsPipeMaterialSwap.cs`.
- Added four new sewer-main pipe swap commands:
  - `UFLS-PIPE-PVC-C900`
  - `UFLS-PIPE-RCP-C900`
  - `UFLS-PIPE-C900-RCP`
  - `UFLS-PIPE-C900-PVC`
- Each command lets the user pick one Civil 3D pipe, validates the expected source family, reads the current nominal size, resolves the matching family from the active network parts list, and swaps to the same size in the target family.
- Added a reflected fallback that attempts `AddPartSize(...)` on the target pipe family when the matching nominal size is not already exposed in the active parts list.
- Updated `Ufls/UflsPalette.cs` so Q1 ADJUST > SEWER MAIN now shows buttons for `PVC --> C900`, `RCP --> C900`, `C900 --> RCP`, and `C900 --> PVC`.
- Updated `PROJECT_MAP.md` and `COMMAND_INDEX.md` for the new pipe material swap workflow.

# CHANGELOG

## 2026-07-22 - PRC Arc-to-Arc Tangency Fix
- Corrected arc-to-arc tangency detection to compare radial-line collinearity at the shared point.
- Opposite radial directions now classify as a tangent REVERSE curve (PRC), while matching radial directions classify as a tangent COMPOUND curve (PCC).
- Prevented false NON-TANGENT classifications and unnecessary radial calls at valid PRC/PCC connections.
- The result remains stable when the legal-description traverse direction is reversed.

## [2026-03-12.2]

### Changed
- **Ufls/UflsLaterals.cs**
  - Updated `UFLS-LATERAL-ALL` so the perpendicular search line half-length is now user-defined at runtime instead of being fixed at `50'`.
  - Set the new default search line half-length to `75'` to better handle longer cul-de-sac lateral runs while still allowing the user to override the value per drawing condition.
  - Completion messaging now reports both the selected search width and selected search length.
- **Ufls/UflsPipeLabels.cs**
  - Updated `UFLS-PIPE-LABEL-3D` so `LABEL INVERT` text rotates to match the selected 3D polyline direction instead of remaining horizontal in world coordinates.
  - Start and end invert labels now offset normal to the pipe direction so the text stays visually tied to the labeled 3D linework.

### Documentation
- **PROJECT_MAP.md**
  - Updated the lateral and invert-label notes to reflect the new user-defined search-length prompt and pipe-aligned label rotation.
- **COMMAND_INDEX.md**
  - Updated the `UFLS-LATERAL-ALL` and `UFLS-PIPE-LABEL-3D` command entries for the new behavior.
- **CHANGELOG.md**
  - Added this maintenance entry.

## [2026-03-12.1]

### Changed
- **Ufls/UflsLaterals.cs**
  - Updated `UFLS-LATERAL-ALL` so the perpendicular search half-width is now user-defined at runtime instead of being locked to `2.0'`.
  - Added a prompt that defaults to `2.0'` but allows larger values such as `3.0'` or `4.0'` when field conditions require a wider grouping pass.
  - Passed the selected search width through candidate grouping, QA polygon sizing, and completion messaging so the ALL workflow stays consistent with the chosen band.

### Documentation
- **PROJECT_MAP.md**
  - Updated the lateral workflow notes to document the user-defined search-width prompt for `UFLS-LATERAL-ALL`.

- **COMMAND_INDEX.md**
  - Updated the `UFLS-LATERAL-ALL` entry to note that the search half-width is prompted from the user at runtime.

- **CHANGELOG.md**
  - Added this maintenance entry.

## 2026-03-12 - UFLS lateral ALL prototype
- Updated `Ufls/UflsLaterals.cs`.
  - Added `UFLS-LATERAL-ALL` as a Q1 ADJUST prototype for automatic sewer-lateral creation.
  - The new workflow finds WYE COGO shots by description text, projects each WYE to the nearest main centerline on `V-SURV-PIPE-CNTR`, builds a perpendicular search line, and prompts for a user-defined search half-width, then gathers non-WYE shots within that band for 50' in each direction.
  - If the grouped shots fall on only one side of the WYE, the command reuses the existing lateral-create path to build the 3D lateral on `V-SURV-PIPE-LATR` and add pipe segments directly into the target `-SSWR-E` sewer network.
  - If grouped shots fall on both sides of the WYE, the command skips auto-creation and places an aligned QA polygon on `V-SURV-RDLN` for manual review.
- Updated `Ufls/UflsPalette.cs`.
  - Q1 ADJUST > `SEWER LATERAL` now uses a two-button row with `CREATE LATERAL` and `CREATE LATERAL - ALL`.
- Updated documentation: `PROJECT_MAP.md`, `COMMAND_INDEX.md`, and `CHANGELOG.md`.

## 2026-03-11 - palette width standardization + Q1 rename
- Standardized all palette widths so Q2 / Q3 / Q1 use the same wider menu width.
- Widened the Q3 shared-row controls so `TRANSPARENCY` stays on one line and the double-button rows remain proportionate.
- Renamed the UFLS palette title from `UFLS – CHECK / ADJUST` to `UFLS`.
- Restored the primary UFLS palette command to `UFLS.Q1` and retained `UFLS.Q11` as a legacy alias for compatibility.
- Renamed the UFLS layers tab from `LAYER MAINTENANCE` to `LAYERS`.
- Centered all single-button captions across the palette UIs.

﻿## 2026-03-11 - Q11 layer maintenance + palette cleanup
- Added `Ufls/UflsLayerMaintenance.cs`.
  - Added `MERGE-STRC` and `UFLS-MERGE-STRC` as native layer-maintenance commands to move legacy structure entities from `V-SURV-STRC-INNER-2D` / `V-SURV-STRC-OUTER-2D` into `V-SURV-STRC-INNR-2D~~` / `V-SURV-STRC-OUTR-2D~~`.
  - Added `ReloadLayerStates` and `UFLS-LAYER-STATES-UPDATE` to replace the legacy LayerStatesUpdate LISP flow by deleting and re-importing the `LateralCreatePipe` and `PipeCenter` layer states from the standard 2026 support folder.
- Added `Ufls/UflsRedlineBlocks.cs`.
  - Added `UFLS-REDLINE-NOTE` to insert `REDLINE-MTEXT` from `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\Blocks\Survey` and immediately explode it.
  - Added `UFLS-REDLINE-LEADER` to insert `REDLINE-LEADER` from the same survey blocks folder and immediately explode it.
- Updated `Ufls/UflsPalette.cs`.
  - Added a new `LAYER MAINTENANCE` tab under Q11.
  - Added `REMOVE DUPLICATE STRC LAYERS` and `UPDATE LAYER STANDARDS` buttons to that new tab.
  - Added `NOTE` and `LEADER` buttons under Q11 CHECK > `REDLINE`.
  - Moved `TRACE INSIDE` / `TRACE OUTSIDE` into Q11 CHECK > `2D LINEWORK` and renamed them to `STRC-INNER WALL` / `STRC-OUTER WALL`.
  - Renamed `STUB` to `STUB MARKER`.
  - Added `3P CIRCLE` and `3P RECTANGLE` to the bottom of Q11 CHECK > `2D LINEWORK`.
- Updated `PoinCloud/PctPalette.cs`.
  - Converted Q3 button captions to uppercase.
  - Centered `GENERAL MARKER` on the UFLS tab.
  - Renamed `TRACE INSIDE` to `STRC-INNER WALL`.
  - Moved `LOCATE MANHOLE` directly under `STRC-INNER WALL`.
- Updated `Ufls/UflsDropInlet.cs`.
  - Revised the final source-form button text from `IN-HOUSE` / `OTHERS` to `4 POINT` / `2 POINT`.
- Updated documentation: `PROJECT_MAP.md`, `COMMAND_INDEX.md`, and `CHANGELOG.md`.

## 2026-03-10 - Q11 Storm Drain pipe buttons
- Added `PIPE - SINGLE` and `PIPE - ALL` under Q11 > ADJUST > `STORM DRAIN`, placed beneath the duplicated `MH - SINGLE` / `MH - ALL` row.
- Reused the existing `UFLS-ADJ-PIPE-SINGLE` and `UFLS-ADJ-PIPE-ALL` commands from the `SEWER MAIN` section so no new command logic was required.

- 2026-03-10
  - Q11 ADJUST > STORM DRAIN renamed `SD - SINGLE` / `SD - ALL` to `JNCT - SINGLE` / `JNCT - ALL`.
  - Added `MH - SINGLE` and `MH - ALL` under the Q11 ADJUST > STORM DRAIN section beneath the DI buttons.
  - Updated storm-drain junction move targeting to use a safer closed-polyline center calculation, including a region-centroid attempt with fallback logic.
  - `UFLS-ADJ-JNCT-SINGLE` now allows an optional explicit inside-wall polyline selection so a specific footprint can drive the move and rotation.

## 2026-03-10 - Storm drain adjust tools + Q11 text cleanup
- Updated `Ufls/UflsPalette.cs`:
  - Q11 CHECK > 3D LINEWORK button text changed from `LABEL 3D PIPE` to `LABEL INVERT`.
  - Q11 ADJUST section label changed from `STRUCTURE` to `STORM DRAIN`.
  - Q11 ADJUST storm-drain resize button text changed from `SD JUNCTION SIZE` to `RESIZE JUNCTION`.
  - Added new Q11 ADJUST storm-drain move buttons:
    - `SD - SINGLE` / `UFLS-ADJ-SD-SINGLE`
    - `SD - ALL` / `UFLS-ADJ-SD-ALL`
    - `DI - SINGLE` / `UFLS-ADJ-DI-SINGLE`
    - `DI - ALL` / `UFLS-ADJ-DI-ALL`
- Updated `Ufls/UflsAdjustCommands.cs`:
  - Added storm-drain junction move commands that use closed inside-wall polylines on `V-SURV-STRC-INNR-2D~~` as move/rotation targets.
  - Added drop-inlet move commands that use `UFLS_DI_MARK` block locations and rotation for move/rotation targets.
  - Added best-effort structure filtering helpers for drop-inlets vs storm-drain junctions using structure family / size / description text.
  - Reused the existing structure-move / connected-pipe logic so pipe connectivity follows the moved structure in the same way as the sewer manhole adjust tools.
- Updated documentation: `PROJECT_MAP.md`, `COMMAND_INDEX.md`, and `CHANGELOG.md`.

﻿# CLV_CivilTools CHANGELOG

## [2026-03-10.1]

### Changed
- **Ufls/UflsPalette.cs**
  - Reordered Q11 ADJUST rows so `MH - SINGLE` / `PIPE - SINGLE` are on the left and `MH - ALL` / `PIPE - ALL` are on the right.
  - Converted Q11 button captions to uppercase and added `LABEL 3D PIPE` to the 3D LINEWORK section.
- **Ufls/UflsAdjustCommands.cs**
  - Added `UFLS-ADJ-MH-ALL` and `UFLS-ADJ-PIPE-ALL` aliases while keeping the existing `...-AUTO` commands available.
  - Updated MH adjust to search both `UFLS_MH_MARK` and `UFLS_STUB_MARK` so null-structure stub markers are included.
  - Updated pipe adjust to read surveyed `LINE` and `Polyline3d` endpoints from `V-SURV-PIPE-CNTR`.

### Added
- **Ufls/UflsPipeLabels.cs**
  - Added `UFLS-PIPE-LABEL-3D` to place static START INVERT / END INVERT elevation text for a selected 3D polyline on layer `V-SURV-PIPE-INVT`.

### Notes
- This change intentionally stays on the earlier structure-move path and does not include the later experimental pipe-rebuild revision that was disconnecting pipes again.

## [2026-03-10.1]

### Fixed
- **Ufls/UflsAdjustCommands.cs**
  - Reworked the Q11 structure-move path to follow the proven manual / LISP behavior more closely: the structure is now moved by writing the marker XY directly to the Civil 3D structure `Position` property when available, while preserving the original Z.
  - Updated connected-pipe handling so each attached pipe is rebuilt with a single `SetStartAndEndPoints(...)` call after the structure move, instead of updating one pipe end at a time. This is intended to reduce the Civil 3D pipe-network disconnects seen during sewer-main structure moves.
  - Added a safe fallback to `Location` when a Civil structure type does not expose `Position`, keeping the reflection-based implementation compatible with the current project architecture.

### Documentation
- **PROJECT_MAP.md**
  - Updated the active maintenance note and UFLS adjust-command notes to document the new structure-position / single-call pipe-update strategy.

- **COMMAND_INDEX.md**
  - Updated the `UFLS-ADJ-MH-AUTO` and `UFLS-ADJ-MH-SINGLE` descriptions so they no longer read like placeholder / first-pass behavior.

- **CHANGELOG.md**
  - Added this maintenance entry.

## [2026-03-09.14]

### Changed
- **Ufls/UflsPalette.cs**
  - Added `STUB` under the Q11 CHECK tab `2D LINEWORK` section.
  - Renamed the ADJUST sewer section to `SEWER MAIN`.
  - Reordered the ADJUST buttons so the manhole move tools appear above the pipe move tools.
  - Renamed the manhole move buttons to `MH - AUTO` and `MH - SINGLE`.

### Added
- **Ufls/UflsAdjustCommands.cs**
  - Replaced the placeholder ADJUST commands with live implementations for `UFLS-ADJ-MH-AUTO`, `UFLS-ADJ-MH-SINGLE`, and the main-pipe alignment commands `UFLS-ADJ-PIPE-AUTO` / `UFLS-ADJ-PIPE-SINGLE` using surveyed LINE centerlines on `V-SURV-PIPE-CNTR`.
  - Added `UFLS-STUB` to place a stub locator marker block for pipe-stub adjustment workflows.
  - Added on-the-fly creation of a fallback `UFLS_STUB_MARK` block definition when an external survey block is not available.

### Documentation
- **PROJECT_MAP.md**
  - Updated the active-maintenance notes, UFLS palette notes, and UFLS adjust-command responsibilities.

- **COMMAND_INDEX.md**
  - Added the Q11 stub and ADJUST command entries and updated palette-access notes.

- **CHANGELOG.md**
  - Added this maintenance entry.

## 2026-03-09 Compile Fix
- Added `AecBaseMgd` reference in `CLV_CivilTools.csproj` so Civil 3D network base types resolve in Visual Studio.
- Updated `Ufls/UflsLaterals.cs` to avoid compile-time `DBObject` to `Network` cast issues by resolving target network name, parts list, and `AddLinePipe` through reflection on the Civil network object.

﻿# CHANGELOG

## 2026-07-22 - PRC Arc-to-Arc Tangency Fix
- Corrected arc-to-arc tangency detection to compare radial-line collinearity at the shared point.
- Opposite radial directions now classify as a tangent REVERSE curve (PRC), while matching radial directions classify as a tangent COMPOUND curve (PCC).
- Prevented false NON-TANGENT classifications and unnecessary radial calls at valid PRC/PCC connections.
- The result remains stable when the legal-description traverse direction is reversed.

## [2026-03-09.10]

### Fixed
- **Ufls/UflsLaterals.cs**
  - Resolved `CS0104` ambiguous `DBObject` compile error by explicitly binding laterals part-size reflection helpers to `Autodesk.AutoCAD.DatabaseServices.DBObject` instead of the conflicting Civil type.

### Documentation
- **PROJECT_MAP.md**
  - Added maintenance note for the laterals `DBObject` namespace disambiguation fix.

- **COMMAND_INDEX.md**
  - Command inventory unchanged; added maintenance note for the laterals compile fix.

- **CHANGELOG.md**
  - Added this maintenance entry.

## 2026-03-09 (Lateral network creation / Auto removed)

- Removed the `Auto Fit Laterals` button from the Q11 palette and parked the `UFLS-LATERAL-AUTO` workflow for now because point grouping was producing too many false positives in production drawings.
- Updated `UFLS-LATERAL-SINGLE` to keep the existing ordered COGO-point selection workflow, then create the lateral directly inside the existing sewer network instead of building a temporary lateral network for later merge.
- `UFLS-LATERAL-SINGLE` now:
  - builds the 3D lateral centerline on `V-SURV-PIPE-LATR`
  - finds the target sewer network automatically by looking for the existing network whose name contains `-SSWR-E`
  - reads the assigned parts list from that network
  - looks for family `CLV_PVC` and the 4" PVC size in that parts list
  - adds each lateral segment directly to the target sewer network with `Network.AddLinePipe(...)` so the main pipe stays continuous and is not split
- Updated `PROJECT_MAP.md` and `COMMAND_INDEX.md` to remove the parked AUTO command from the active command inventory and document the direct-to-network SINGLE workflow.

# CLV_CivilTools CHANGELOG

## [2026-03-09.9]

### Changed
- **Ufls/UflsLaterals.cs**
  - Updated `UFLS-LATERAL-SINGLE` point collection to select actual COGO point entities instead of free-picked points so lateral shots and the final main-reference shot use the exact survey point coordinates.
  - Kept the 4-inch PVC lateral shot offset based on the D3034 catalog values used by the shared pipe catalog, while leaving the main tie-in point unadjusted.
  - Updated `UFLS-LATERAL-AUTO` grouping logic to rely on spatial proximity plus same-main association instead of point-number sequencing, so non-sequential field shots can still be grouped correctly.
  - Updated AUTO QA notes to list actual point numbers ordered from the lot side back toward the main rather than showing a misleading numeric point range.
  - Updated the AUTO options dialog note text to reflect the new grouping / labeling logic.

- **Ufls/UflsStructure.cs**
  - Updated `UFLS-3PCIRCLE` so created circles are written at elevation `0.0` on layer `0`.
  - Updated `UFLS-3PRECT` so picked points are flattened to elevation `0.0` during creation and the finished result is a single closed polyline on layer `0` instead of four separate line entities.

- **Shared/PipeCatalog.cs**
  - Aligned the PVC 18-inch and 21-inch catalog values with the uploaded pipe-material spreadsheet.

### Documentation
- **PROJECT_MAP.md**
  - Updated current maintenance notes to reflect exact COGO-point selection for SINGLE laterals, same-main spatial grouping for AUTO laterals, and zero-elevation closed-polyline behavior for the 3-point linework tools.

- **COMMAND_INDEX.md**
  - Updated command descriptions for the lateral and 3-point linework tools to reflect exact point-entity selection, same-main AUTO grouping, and zero-elevation output behavior.

- **CHANGELOG.md**
  - Added this maintenance entry.

------------------------------------------------------------
CHANGELOG
------------------------------------------------------------


## [2026-03-09.7]

### Changed
- **Ufls/UflsLaterals.cs**
  - Updated `UFLS-LATERAL-SINGLE` main-connection math to use a plan-view projection (`Vector3d.ZAxis`) onto the nearest main centerline so the tie point is perpendicular in XY instead of being influenced by 3D distance along the main.
  - Reads the final Z back from the main curve after the XY projection so the created connection still lands on the actual 3D main line.
  - Replaced AUTO review circles with compact bounding boxes around each flagged group.
  - Added embedded multiline box labels showing note, point range, and main-distance range so QA results are easier to read.

### Documentation
- **PROJECT_MAP.md**
  - Updated lateral maintenance notes to reflect perpendicular-in-plan main connection logic and boxed AUTO QA markers.

- **COMMAND_INDEX.md**
  - Updated lateral command descriptions to reflect boxed QA output and improved main connection math.

- **CHANGELOG.md**
  - Added this maintenance entry.

## [2026-03-09.6]

### Changed
- **Ufls/UflsLaterals.cs**
  - Updated `UFLS-LATERAL-SINGLE` so the final picked main shot is used only as a geometry reference.
  - Removed the manual main-selection prompt from SINGLE mode and now auto-finds the nearest main centerline on `V-SURV-PIPE-CNTR`.
  - Connects the created lateral directly to the calculated 3D point on the main centerline instead of using the picked main shot as the endpoint.
  - Applies the 4-inch PVC top-of-pipe to centerline adjustment only to lateral shots, not to the main connection point.
  - Updated SINGLE mode prompt text so the user knows the last pick is the main reference shot.

- **Ufls/UflsLaterals.cs / Laterals Auto dialog**
  - Enlarged the AUTO options dialog and made the second-stage prompt area readable with a multiline filter box and expanded notes.
  - Updated in-house matching logic to use positive and negative filters.
  - Added grouping by proximity plus point-number sequence.
  - Added filtering to reduce false positives from shots along the main and around manholes.
  - Added review-cloud labeling so ambiguous clusters can be identified as `CHECK`.

### Documentation
- **PROJECT_MAP.md**
  - Updated maintenance notes to reflect the new SINGLE auto-main connection behavior and AUTO grouping / filtering logic.

- **COMMAND_INDEX.md**
  - Updated lateral command descriptions to reflect automatic main-centerline connection and grouped AUTO review behavior.

- **CHANGELOG.md**
  - Added this maintenance entry.

## [2026-03-09.5]

### Fixed
- **Ufls/UflsLaterals.cs**
  - Resolved `CS0104` ambiguous `Label` compile errors by explicitly using WinForms label types in the laterals options dialog.
  - Resolved `CS1503` polyline creation compile error by converting the picked point list to an AutoCAD `Point3dCollection` before creating the `Polyline3d`.

### Documentation
- **PROJECT_MAP.md**
  - Added maintenance note for the initial `UflsLaterals` compile fixes after the first lateral framework pass.

- **COMMAND_INDEX.md**
  - Command inventory unchanged; added maintenance note for `UFLS-LATERAL-SINGLE` / `UFLS-LATERAL-AUTO` compile stabilization.

- **CHANGELOG.md**
  - Added this maintenance entry.

## 2026-03-09 - Palette and 2D linework expansion

### Added
- Added `UFLS7PC` in `Ufls/UflsStructure.cs` for point-cloud-assisted inside-wall tracing with `OSNAPZ=0` and `3DOSMODE=128`.
- Added `UFLS-3PCIRCLE` in `Ufls/UflsStructure.cs` for 3-point circle creation on layer `0` with temporary pick markers and snaps OFF.
- Added `UFLS-3PRECT` in `Ufls/UflsStructure.cs` for 3-point orthogonal rectangle creation on layer `0` with temporary pick markers and snaps OFF.

### Changed
- Updated `Ufls/UflsPalette.cs` so the `2D STRUCTURE` row now reads `TRACE INSIDE` and `TRACE OUTSIDE`.
- Increased the 2-button row height in `Ufls/UflsPalette.cs` so the new labels remain readable.
- Enlarged the structure wall-thickness form in `Ufls/UflsStructure.cs` so the preset buttons and manual input area are readable without clipping.
- Updated `PoinCloud/PctPalette.cs` UFLS tab to rename `STRUCTURE LOCATOR` to `2D LINEWORK`.
- Updated `PoinCloud/PctPalette.cs` UFLS tab to add `TRACE INSIDE`, `3P CIRCLE`, and `3P RECTANGLE` buttons while keeping `LOCATE MANHOLE` available.

### Notes
- `UFLS7` remains the standard inside-wall trace from the main UFLS palette.
- `UFLS7PC` is the Q3 / W3 point-cloud-specific version intended for snapped picks against the point cloud.
- `UFLS-3PCIRCLE` and `UFLS-3PRECT` both use the same temporary marker-circle feedback pattern used by the structure tracing workflow.

## [2026-03-09.3]

### Fixed
- **Ufls/UflsStructure.cs**
  - Updated multi-point collection for `UFLS7` / `UFLS8` so pressing **Enter** cleanly ends point input instead of looping with `Invalid point.`
  - Enabled `PromptPointOptions.AllowNone = true` for both the first-point cancel case and subsequent finish case.

- **Shared/SnapState.cs**
  - Corrected nullable annotations for `Editor` locals returned from `doc?.Editor` to remove `CS8600` warnings.

- **Gis/GisPalette.cs**
  - Corrected nullable annotations on static palette UI fields (`_ps`, `_lstAerials`, `_btnUnload`, `_tabControl`) to remove `CS8618` warnings under `nullable enable`.
  - Initialized `AerialItem.DisplayName` and `AerialItem.FullPath` to empty strings to remove `CS8618` property warnings.

### Documentation
- **PROJECT_MAP.md**
  - Updated maintenance notes to reflect the `UFLS7` / `UFLS8` Enter-key input fix and remaining nullable cleanup in GIS / Shared.

- **COMMAND_INDEX.md**
  - Kept command inventory unchanged; added maintenance note for the `UFLS7` / `UFLS8` point-entry completion fix.

- **CHANGELOG.md**
  - Added this maintenance entry.


## [2026-03-09.2]

### Fixed
- **Ufls/UflsStructure.cs**
  - Resolved ambiguous `Color` compile errors by explicitly using AutoCAD color aliases for layer creation logic.

- **Gis/GisPalette.cs**
  - Added nullable-safe palette visibility check to avoid `CS8602` on the static palette instance.

- **Gis/Aerials.cs**
  - Added nullable-safe document and view handling for active-document access and saved-view storage.
  - Updated Map layer group / target declarations to use nullable-aware locals where `null` is an expected transient state.

- **Shared/SnapState.cs**
  - Added null guards when reading AutoCAD system variables before conversion to integers.

- **Ufls/UflsDropInlet.cs**
  - Reworked WinForms button click handlers to use pattern matching against `Button.Tag` instead of direct unboxing / casts.
  - Added nullable-safe handling around temporary `CMDECHO` system-variable storage and restoration.

### Build / Warnings
- **CLV_CivilTools.csproj**
  - Added `MSB3277` suppression for the `WindowsBase` reference-conflict warning caused by Autodesk host assemblies versus the .NET 8 reference pack.
  - Kept `net8.0-windows`, `UseWindowsForms=true`, `x64`, and `nullable enable` unchanged.

### Documentation
- **PROJECT_MAP.md**
  - Updated build notes and source responsibilities to reflect the nullable / compile-warning cleanup pass.

- **COMMAND_INDEX.md**
  - Retained the current command inventory and palette-entry structure for this maintenance revision.

- **CHANGELOG.md**
  - Added this maintenance entry.


## [2026-03-09.1]

### Added
- **Ufls/UflsStructure.cs**
  - Added new native .NET UFLS structure-footprint workflow.
  - Added `UFLS7` to trace the **INNER** wall and auto-create the matching **OUTER** wall.
  - Added `UFLS8` to trace the **OUTER** wall and auto-create the matching **INNER** wall.
  - Added point-collection feedback markers on `V-TEMP-PICKMARK`.
  - Added a WinForms wall-thickness / structure-type dialog that mirrors the legacy preset-thickness workflow from the existing LISP/DCL tool.
  - Added layer-creation logic for `V-SURV-STRC-INNR-2D~~` and `V-SURV-STRC-OUTR-2D~~`, including an attempt to apply named plot style `M` when available in STB drawings.

### Changed
- **Ufls/UflsPalette.cs**
  - Added a new `2D STRUCTURE` section to the UFLS CHECK tab.
  - Added side-by-side `INNER` and `OUTER` buttons mapped to `UFLS7` and `UFLS8`.
  - Removed the old single `Structure Footprint` palette button from the active .NET palette workflow.
  - Added a reusable two-button row helper so the UFLS palette layout matches the existing Q3 palette style more closely.

### Documentation
- **PROJECT_MAP.md**
  - Rebuilt / refreshed the project map for this project.
  - Added current source-file responsibilities, command inventory, layer conventions, palette architecture, and the new UFLS structure-command details.

- **COMMAND_INDEX.md**
  - Added a project-specific command index for CLV_CivilTools.
  - Documented all current command entry points plus the new `UFLS7` / `UFLS8` structure commands.

- **CHANGELOG.md**
  - Added this project-specific changelog.

### Notes
- This revision keeps the WinForms `PaletteSet` approach, `net8.0-windows`, `x64`, and `nullable enable` intact.
- `UFLS7` / `UFLS8` are intended to be the native .NET replacement path for the structure-footprint workflow that had previously been handled through external LISP.
- I could not validate the commands inside a live Civil 3D session here, so this revision is based on source-level coordination and compile-safe patterns.
## 2026-03-09 – Palette text cleanup + first-pass laterals
- Updated `Ufls/UflsPalette.cs` CHECK tab button text:
  - `Auto-Create Manholes` -> `Manhole - Auto`
  - `Create Manhole` -> `Manhole - Single`
- Added Q11 PIPE NETWORK buttons:
  - `Lateral - Single` -> `UFLS-LATERAL-SINGLE`
  - `Auto Fit Laterals` -> `UFLS-LATERAL-AUTO`
- Updated `PoinCloud/PctPalette.cs` UFLS tab labels:
  - PIPE LOCATOR step labels now read `Step 1` through `Step 5`
  - `3P Circle` -> `3P CIRCLE`
  - `3P Rectangle` -> `3P RECTANGLE`
  - `Create Manhole` -> centered `LOCATE MANHOLE`
- Added `Ufls/UflsLaterals.cs`.
  - `UFLS-LATERAL-SINGLE` now creates a first-pass 3D lateral centerline from ordered COGO-point selections, using the final selected COGO point as the main-reference shot.
  - `UFLS-LATERAL-AUTO` now provides a first-pass QA review mode that groups matched lateral survey shots by spatial proximity and same-main association, then labels QA boxes with ordered point numbers.
- Deferred full pipe-network creation / merge for later implementation so the current pass stays compile-safe and reviewable.

## 2026-03-09 - Fix 11b
- Added `AecBaseMgd` project reference in `CLV_CivilTools.csproj` to satisfy Civil 3D pipe-network API types used by `UflsLaterals.cs`.
- Updated `Ufls/UflsLaterals.cs` to use explicit `as Network` casts instead of pattern matching for target sewer network discovery, avoiding compile-time type-handling errors when Autodesk references are resolved by Visual Studio.


## 2026-03-09 - Q11 palette and manhole sizing update
- Fixed UFLS6 single-manhole block sizing to infer 48/60/72 visibility from the selected COGO point descriptions.
- Moved lateral creation to the Q11 ADJUST tab under `SEWER LATERAL` and renamed the button to `Create Lateral`.
- Removed command IDs from the Q11 3D LINEWORK button captions.
- Normalized Q11 button heights to match Q3 and centered button text.
- Added starter ADJUST tab commands for sewer pipe/manhole workflows as placeholders for the next implementation pass.

## 2026-03-09 - Pipe move behavior update
- Updated `Ufls/UflsAdjustCommands.cs` so `UFLS-ADJ-PIPE-SINGLE` and `UFLS-ADJ-PIPE-AUTO` still match surveyed centerlines on `V-SURV-PIPE-CNTR`, but now call the Civil 3D `SetStartAndEndPoints(...)` method so both pipe ends update together, matching the working LISP behavior more closely and avoiding the prior disconnect-prone separate-endpoint update path.
- This is intended to keep Civil 3D pipe connections from dropping when an endpoint lands exactly at the structure wall / connection tolerance boundary after a move.
## 2026-03-10 - SD-JUNCTION-SIZE fix 3
- Updated `Ufls/UflsSdJunctionSizeTest.cs` to stop using reflection against ambiguous `PartFamily.Item` indexers.
- Switched the family resolution path to explicit Civil 3D `PartsList` / `PartFamily` traversal for `DomainType.Structure`, using the selected structure's active network parts list.
- Updated part-size iteration to use the typed `PartFamily.PartSizeCount` and `family[i]` indexer path so built-in size matching does not fail with the runtime `Ambiguous match found` reflection error.


- Fixed `UflsSdJunctionSizeTest.cs` compile issue by importing `Autodesk.Civil.DatabaseServices.Styles` so `PartFamily` resolves correctly.

## 2026-03-10 - SD-JUNCTION style preservation
- Updated `Ufls/UflsSdJunctionSizeTest.cs` so `SD-JUNCTION-SIZE` captures the original selected structure style, attempts to apply that style to the resolved/matched part-size definition, and then reapplies the original structure style after the swap as a fallback when no writable part-size style target is exposed.


## 2026-03-12 - GIS import fix
- Resolved WinForms FlowDirection ambiguity in GisImport.cs and GisPalette.cs using a System.Windows.Forms alias.
- Added automatic GIS layer creation for GIS-CC-PRCL, GIS-ROAD-CNTR, GIS-SSWR-STRC-E, GIS-SSWR-PIPE-E, GIS-STRM-STRC-E, GIS-STRM-PIPE-E, and GIS-TEMP-BOUNDARY during CLV-GIS-IMPORT setup.

- GIS import: changed clip/import result counters from `init` to mutable `set` properties so runtime clip accounting compiles cleanly in the automatic import workflow.


## 2026-03-16
- GIS: updated fallback GIS layer plot styles to match CLV screenshot settings (`S`, `L`, `SSWR-PIPE-E`, `SSWR-STRC-E`, `STRM-PIPE-E`, `STRM-STRC-E`, `Normal`).


## 2026-03-17 - GIS centerline OD snapshot fix
- Updated `Gis/GisImport.cs` so `CLV-GIS-JOIN-CENTERLINES` now snapshots `Street_Centerlines.STREET` from the selected base object and selected source entities before any LINE-to-LWPOLYLINE conversion happens.
- Added clearer command-line diagnostics so the command now reports whether OD was found on the base object, on source objects, which `STREET` value was chosen, and whether the final reattach succeeded.
- This is intended to make LINE-first centerline joins behave more reliably and to remove the misleading prior message that only referenced source entities even when the base polyline already carried the OD.


## 2026-03-18 - Survey best-fit review dialog
- Added a review dialog to `SURVEY-BESTFIT-MAP` before finalizing movement.
- Added per-pair `Calc` and `Ref` grouping toggles.
- Added row removal and live fit recalculation with RMS / max residual summary.
- CSV output now records calc/reference membership for each pair.

- SURVEY-BESTFIT-MAP review dialog simplified: CONTROL-only checkbox workflow, forward-only finalize, selected point display, compact residual grid.

- Updated SURVEY-BESTFIT-MAP prompts and review grid labels: simplified to SURVEY POINT / MAP POINT prompts, MAP POINT defaults to Entity, and review grid now shows Survey and Map point identifiers with Control checkbox casing adjusted.
- Updated SURVEY-BESTFIT-MAP to prefer Civil 3D COGO point `Name` for review labels when available, falling back to point number when blank.
- Simplified command-line selection prompts to `SELECT SURVEY POINT` and `SELECT MAP POINT`.
- Removed the extra finish / mode guidance text from the survey and map selection prompts for a cleaner command-line workflow.

## 2026-03-23 - GIS pipe OD offset baseline
- Added `Gis/GisPipeOdOffset.cs`.
  - Added `CLV-GIS-PIPE-OFFSET-OD` to create basic left/right offsets from MapImport pipe centerlines using OD field `InsideDiameter`.
  - The command skips entities without OD, skips diameters below `1.0'`, and reports parse/geometry issues in the command line summary.
- Updated `Gis/GisImport.cs` with an internal reusable OD field-value helper.
- Updated `Gis/GisPalette.cs` to add `PIPE OD OFFSET` on the GIS palette.
- Updated `PROJECT_MAP.md` and `COMMAND_INDEX.md` for the new routine.

## 2026-03-23 - GIS pipe OD offset shared helper path
- Updated `Gis/GisPipeOdOffset.cs` so `CLV-GIS-PIPE-OFFSET-OD` now checks only the shared server helper at `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\CLV_GIS_PIPE_OD_OFFSET.lsp`.
- Revised `CLV-GIS-PIPE-OFFSET-OD` to use only the common network LISP location for all users, with no local packaged helper fallback.


## 2026-03-23 - GIS drop inlet explode baseline
- Added `Gis/GisDropInletExplodeToGis.cs`.
  - Added `CLV-GIS-DI-EXPLODE` as a first-pass drop inlet conversion routine for imported structure workflows.
  - Workflow: user selects one drop inlet block, the command explodes it in place, deletes marker / curb / text remnants, remaps exploded inner and outer structure linework to `C-STRM-STRC-INNER-E` and `C-STRM-STRC-E`, then queues ADE/LISP Object Data copy from the nearest imported structure point to the outer polyline.
- Updated `Gis/GisPalette.cs`.
  - Added `DROP INLET EXPLODE` under `Q2 > GIS > GIS TOOLS`.
- Updated `PROJECT_MAP.md` and `COMMAND_INDEX.md` for the new routine.

## 2026-03-24 - Drop inlet OD transfer robustness
- Revised `CLV-GIS-DI-EXPLODE` to prefer the block `DI_CENTER`/insertion point for `Structures` point matching with tight tolerances only.
- Changed inner target layer to `C-STRM-STRC-INNR`.
- Changed OD transfer to queue ADE/LISP copy onto all eligible exploded outer structure entities from the selected block instead of one guessed outer handle.


## 2026-03-24
- Added `CLV-GIS-STRM-AUTO` batch automation for storm structures.
- New file `Gis/GisStormStructureAuto.cs`.
- Q2 GIS palette adds `STORM STRUCTURES AUTO`.
- Batch workflow runs drop inlet blocks first, then junction structures from remaining `Structures` points.

- 2026-03-24 fix27: Reworked `CLV_GIS_PIPE_OD_OFFSET.lsp` endpoint updates to use DXF/`entmod` edits for LINE and LWPOLYLINE instead of COM property writes, to avoid the `incorrect object to bind: T` failure during automatic structure-wall connection.


## 2026-03-24 - Q1 GIS tab / STORM GIS
- Updated `Ufls/UflsPalette.cs` to add a new GIS tab to the existing Q1 UFLS palette.
- Added `STORM DRAIN` buttons on Q1 > GIS in this order: `STORM GIS`, `STORM STRUCTURES AUTO`, `DROP INLET EXPLODE`, `JUNCTION FROM POINT`, `PIPE OD OFFSET`.
- Updated `Gis/GisPalette.cs` so Q2 > GIS > GIS TOOLS only shows `JOIN CENTERLINES`.
- Added `Gis/GisStormGisCommands.cs` with `CLV-GIS-STORM-GIS` to queue storm structures auto followed by all-pipes pipe OD offset.
- Updated `Gis/GisPipeOdOffset.cs` with `CLV-GIS-PIPE-OFFSET-OD-ALL`.
- `CLV_GIS_PIPE_OD_OFFSET.lsp`: server-hosted storm ADE/LISP helper expected at `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\CLV_GIS_PIPE_OD_OFFSET.lsp`; reads `InsideDiameter`, creates storm pipe wall offsets, and adjusts created wall endpoints to `C-STRM-STRC-INNR` when possible.

- Renamed Q1 > GIS section from `STROM DRAIN` to `STORM DRAIN`.
- Added `PIPE EXTEND` and `PIPE TRIM` buttons at the bottom of Q1 > GIS > STORM DRAIN.
- Added `CLV-GIS-PIPE-EXTEND` and `CLV-GIS-PIPE-TRIM` single-click cleanup commands to move the nearest selected pipe-wall endpoint to the closest `C-STRM-STRC-INNR` wall.


## 2026-03-24 fix42
- Q1 > GIS section renamed from `STROM DRAIN` to `STORM DRAIN`.
- Q1 > GIS button renamed from `STORM GIS` to `GIS ALL`.
- Removed `PIPE EXTEND` and `PIPE TRIM` buttons from Q1 > GIS.
- Added `ERASE POINTS` button/command (`CLV-GIS-ERASE-POINTS`) to remove imported structure point objects on layer `Structures`.

## 2026-05-06 - Survey Auto Closure Review Dashboard

- Added in-session modelspace review tools for AUTO CLOSURE Phase 1:
  - `SURVEY-CLOSURE-REPORT` opens a WinForms table showing segment-by-segment original vs adjusted data.
  - `SURVEY-CLOSURE-MARKERS` creates numbered modelspace review markers matching the report segment numbers.
  - `SURVEY-CLOSURE-GOTO` prompts for a segment number, selects the original/adjusted pair, and zooms to that segment.
  - `SURVEY-CLOSURE-CLEAR-REVIEW` removes numbered review markers.
- Added `V-SURV-MAP~-REVIEW` to `LayerStandards.cs` so marker layer creation stays centralized.
- Updated Q4 > MAPPING > REVIEW with report/marker/goto/clear review buttons.
- No XData storage added; review data is stored only in memory for the current AutoCAD session after AUTO CLOSURE runs.
- Report window supports row double-click or `ZOOM TO SELECTED` to zoom/select the matching original/adjusted segment pair.


## 2026-05-06 - AUTO CLOSURE Phase 2C Fix 2
- Revised tangent arc handling for standalone LINE/ARC closure chains.
- Detects intended original tangency using survey-map tolerance, but reports adjusted tangency with stricter 1-second tolerance.
- For ARC segments tangent to LINEs on both sides, rebuilds adjusted arc as a true fillet between adjusted adjacent lines and trims/extends adjacent adjusted line endpoints to tangent points.
- No LISP changes. No Photo Review, Best Fit, or split-view review code changes.

## AUTO CLOSURE Phase 3A - Stackable constraints
- Updated AUTO CLOSURE constraint handling so LOCK BEARING + LOCK LENGTH on the same segment is treated as a fixed vector.
- Added combined constraint-state reporting for closure report rows.
- Residual closure adjustment now avoids fully locked vector segments when free segments remain available.

## AUTO CLOSURE Phase 3A Fix 1 - Constraint priority over tangent trim
- Fixed stackable constraint enforcement when a tangent arc fillet rebuild attempted to trim/extend a neighboring length-locked line.
- Length-locked and fixed-vector segments now have priority over tangent-fillet trimming. If tangency cannot be preserved without changing a locked length, the report may show a tangency warning instead of changing the locked segment.
- Reapplies locked bearing/length constraints after tangent processing so the final adjusted report values match the selected constraints.
- No LISP changes. No Photo Review, Best Fit, or split-view review code changes.


## AUTO CLOSURE Phase 3B - Reference constraints

- Added constraint dialog options: `PARALLEL TO REFERENCE`, `OFFSET TO REFERENCE`, and `PERPENDICULAR TO REFERENCE`.
- Reference geometry is used as control only and is not adjusted or copied into the closure boundary.
- `OFFSET TO REFERENCE` records the current signed perpendicular offset at pick time and attempts to preserve that offset during the adjusted boundary solve.
- Constraint report state now identifies reference-based constraints as `PARALLEL REF`, `OFFSET REF`, or `PERP REF`.
- No LISP changes.

## AUTO CLOSURE Phase 3B Fix 2 - Specified offset-to-reference distance

- Updated `OFFSET TO REFERENCE` constraints to let the user choose `Current` or `Specify` during constraint creation.
- When `Specify` is selected, the user can enter the intended offset distance and choose side as `Auto`, `Left`, or `Right` relative to the reference line direction.
- Closure report now includes `Target Offset`, `Actual Offset`, and `Δ Offset` columns for reference-offset QA.
- No LISP changes. No Photo Review changes.

## 2026-06-29 - Boundary CSV Import Radial Curve Control

- Added radial-controlled curve construction to the boundary CSV import workflow.
- Curves with a start radial bearing now orient from the PC radial instead of only from the previous tangent.
- Curve preview Keep/Flip options now use radial-controlled construction when radial data is available.
- Import report flags curves built from RADIAL or RADIAL_REVERSED orientation.
- End-radial-only notes are intentionally ignored as start radial control.

## 2026-06-29 - Boundary Import Radial Review Correction
- Updated boundary CSV curve review for curves with RadialBearing values.
- Radial-controlled curves now show four temporary previews: keep CSV/radial, flip curve only, reverse radial only, and reverse both.
- The selected radial orientation is held through the final chain-build instead of being re-selected automatically from stale CSV endpoint scoring.
- Import summary now reports the radial option used for radial-controlled curves.

## 2026-06-29 Boundary CSV Import Defaults / Tangency Marker Cleanup

- Boundary CSV import now uses fixed default settings without setup prompts:
  - Label Text = 8
  - Build Continuously = Yes
  - Curve Direction Review = QAOnly
  - Label Mode = Bearing
  - Import MANUAL_REVIEW Rows = Yes
- Removed the tangency highlight prompt and tangency tolerance prompt from the import workflow.
- Disabled orange tangency marker/label creation during import and curve preview.
- Accepted chain-built geometry continues to draw on `V-MAPL-BNDY`; QA fallback geometry remains on `V-MAPL-QA`.

## 2026-06-30 - Q4 Line/Curve Label Layer Restore Fix
- Revised `2-POINT` and `BEARING AND DISTANCE` label commands so they no longer leave the drawing current layer on `V-LABL`.
- The user's pre-command current layer is restored after the native Civil 3D label command ends, is cancelled, or fails.
- Added object-appended tracking plus post-command modelspace scan so Civil 3D labels that default to `C-ANNO` are moved to `V-LABL` after placement.
- `V-LABL` layer standard remains yellow, Continuous, plot style `S`, description `Labels: line and curve geometry`.

- DRAW TIE LINE now restores the user's previous current layer after the native LINE command ends or is cancelled.

## 2026-06-30 - Prompt 3 Roads Import

- Added Q4 > SURVEY > MAPPING > BOUNDARY button `ROADS IMPORT`.
- Added command aliases `CLV_ROADS_IMPORT`, `CLV_3_ROADS_IMPORT`, `CLV-3-ROADS-IMPORT`, and `SURVEY-ROADS-CSV-IMPORT`.
- Added `Shared/RoadsCsvImportCommands.cs` to import Prompt 3 road centerline / ROW / private-street framework CSV files.
- Road geometry uses CSV/default road layers instead of layer 0:
  - `V-MAPL-ROAD-CL` for road centerlines.
  - `V-MAPL-ROAD-ROW` for ROW/private street edges and road curves.
  - `V-MAPL-ROAD-CE` for road/common-element outlines.
  - `V-MAPL-CNTRL` for roadway tie/control geometry.
  - `V-MAPL-QA` for QA/manual-review geometry.
- Road importer creates only street-name labels from `RoadName`; users can add Civil 3D line/curve labels separately for bearing/distance verification.
- Added `Docs/ROADS_CSV_IMPORT_README.md` and included Prompt 3 text in Docs.

## 2026-07-01 - Prompt 3 centerline import and subdivision linework tools

- Updated `ROADS IMPORT` for the revised Prompt 3 centerline-only CSV workflow.
  - Imports only road centerline rows.
  - Skips road edges, ROW, road common-element outlines, roadway ties, notes, and non-centerline QA reference rows.
  - Keeps automatic labels limited to road names.
- Added first-pass subdivision linework tools:
  - `CENTERLINE TO EDGES` / `CLV_SUBDIV_ROAD_EDGES`
  - `INTERSECTION RETURNS` / `CLV_SUBDIV_INTERSECTION_RETURNS`
  - `LOT OFFSET` / `CLV_SUBDIV_LOT_OFFSET`
- Added `Docs/SUBDIVISION_LINEWORK_TOOLS_README.md`.

## 2026-07-01 - Subdivision linework tool refinement

- Renamed the CENTERLINE TO EDGES palette action to ROADS and removed cul-de-sac creation from that workflow.
- Added SITE SETTINGS for project defaults: typical road width, cul-de-sac radius, cul-de-sac tie-in radius, and curb return radius.
- Added a separate CUL-DE-SAC tool that uses editable defaults and creates the bulb circle from a picked centerline endpoint.
- Renamed INTERSECTION RETURNS to INTERSECTION and uses SITE SETTINGS defaults for preliminary return arcs.
- Kept legacy command aliases for ROADS and INTERSECTION so existing buttons/macros still resolve during testing.

## 2026-07-01 - Subdivision cul-de-sac and intersection cleanup refinement

- Updated `CUL-DE-SAC` so it can trim/extend selected or nearby `V-MAPL-ROAD-ROW` road-edge lines to the created bulb circle.
- Updated `INTERSECTION` so it creates return arcs, splits long crossing road-edge lines where needed, and trims/extends existing road-edge linework to the return tangent points.
- Both tools still target first-pass straight-centerline geometry and remain design-in-progress for curved, skewed, variable-width, and multi-leg conditions.

## 2026-07-01 - Subdivision Cul-de-sac / Intersection Cleanup Refinement

- Updated CUL-DE-SAC workflow:
  - User selects the cul-de-sac centerline and endpoint/bulb center.
  - Tool automatically detects nearby V-MAPL-ROAD-ROW edge lines instead of asking for extra edge selection.
  - Trims detected road edges to tie-in tangent points.
  - Trims the bulb circle into a bulb arc and adds tie-in fillet arcs using the stored cul-de-sac tie-in radius.
- Updated INTERSECTION workflow:
  - Prompts for MAIN road centerline first, then INTERSECTING road centerline.
  - Automatically detects nearby V-MAPL-ROAD-ROW edges for both roads.
  - Uses site road width and curb return radius to create returns and trim/split edge lines.
  - Removed extra road-edge selection prompt.

### 2026-07-01 - Subdivision road cleanup refinement v3
- Revised CUL-DE-SAC tie-in fillet selection so bulb tie-in radii stay on the correct road-edge side instead of creating inward/pinched radius geometry.
- Revised INTERSECTION workflow to treat selected roads as MAIN and INTERSECTING/T-road geometry.
- INTERSECTION now trims the MAIN road edge opening first between the two intersecting-road edge tangent points, then trims the intersecting road edges to the curb-return tangency points.
- INTERSECTION now creates only the two curb returns on the side where the intersecting road joins the main road, instead of attempting all four corners.


## 2026-07-01 - Subdivision cleanup v4
- CUL-DE-SAC: revised tie-in construction to use a deterministic tangent/radius method. The tool offsets each detected road edge outward from the selected centerline side, creates a fillet circle tangent to that road edge and the cul-de-sac bulb, trims road edges to tangent points, and exports the remaining bulb arc instead of leaving the full circle.
- INTERSECTION: revised MAIN/INTERSECTING cleanup to use the actual generated road-edge lines for tangent/tangent return creation. The main road edge opening is trimmed between the two return tangent points, and intersecting-road edges are trimmed back to the return tangency points.
- This pass is still focused on straight centerline / straight road-edge cases first.

## 2026-07-01 - Subdivision cul-de-sac restore after intersection fix
- Restored the previously working cul-de-sac tangent/bulb trim helpers from the v4 tangent cleanup pass.
- Left the improved intersection cleanup from v6 intact.

## 2026-07-01 - Subdivision curved centerline support pass
- Updated subdivision ROADS/CUL-DE-SAC/INTERSECTION workflows to accept selected Curve objects instead of Line-only centerlines.
- ROADS already offsets lines/arcs/polylines using AutoCAD curve offsets; documentation now reflects curved centerline support.
- CUL-DE-SAC now accepts curved centerlines and uses local tangent direction at the picked endpoint to identify generated road-edge curves near the bulb.
- INTERSECTION now accepts curved centerlines and uses the local tangent direction at the centerline intersection for first-pass return geometry.
- Added first-pass trimming support for arc road edges where possible; complex skewed/compound curved intersections may still need manual review.

### 2026-07-01 - Subdivision intersection curved-edge cleanup refinement
- Updated INTERSECTION to calculate return corners from actual generated road-edge curve intersections instead of theoretical tangent offsets only.
- Uses local tangents on actual line/arc road-edge geometry for return arcs, improving simple curved-road and skewed-road cleanup.
- Keeps the working cul-de-sac logic unchanged.

## 2026-07-01 - Curved road edge intersection cleanup support
- Updated ROADS offset creation to explode offset polyline road edges into individual line/arc segments.
- This improves INTERSECTION cleanup on curved centerlines because the return tool can trim actual arc/line edge pieces instead of one continuous polyline that cannot be split cleanly.
- Existing workflow note: rerun ROADS after this update before testing curved intersections so the road edges are created as clean trim-ready segments.


## 2026-07-01 - Restore Stable Intersection Cleanup

- Restored the subdivision INTERSECTION workflow to the previous stable cleanup behavior after native FILLET-only testing caused straight-road intersections to regress.
- Kept curved centerline selection support, cul-de-sac edge detection fixes, LOT LINES repeated-offset workflow, and Prompt 3 centerline-only importer updates from the prior working set.
- Reapplied layer color standards: V-MAPL-LOT = color 220, V-MAPL-ROAD-ROW = color 83, V-MAPL-ROAD-CL = color 11.
- Native FILLET-only intersection experiment removed from this package.


## 2026-07-07 - Q4 Palette Easement Button
- Added `EASEMENT IMPORT` button under the Q4 > MAPPING > BOUNDARY section, directly below `ROADS IMPORT`.
- Button launches `CLV_4_EASEMENT_IMPORT`.

## 2026-07-07 - 4-EASEMENT_IMPORT curve/radial support

- Updated `Shared/EasementCsvImportCommands.cs` to read the expanded Prompt Easement curve/radial fields: tangent, chord bearing/length, radial bearing, start/end radial bearing, and radial direction used.
- Curve import now evaluates endpoint/radius candidates and radial-controlled candidates, including shown vs reversed radial testing when radial data is supplied.
- Added curve QA warning output for radial reversal, both-radial testing, left/right testing, tangency conflicts, radial conflicts, curve direction conflicts, curve math conflicts, incomplete curve data, unknown curve control, and manual review flags.
- Rows flagged as review/conflict/incomplete/unknown curve control route to the QA layer unless `ImportStatus=DO_NOT_IMPORT`, which is skipped.
- Updated the easement import README and bundled Prompt Easement rebuilt document to Rev 2 curve/radial language.

## 2026-07-08 - 2A Boundary Input Compile Fix
- Fixed `BoundaryCsvImportCommands.cs` compile error CS7036 by passing `workflowName` into `CreateDefaultImportOptions(workflowName)`.
- This preserves the split behavior: `BOUNDARY IMPORT AUTO` remains auto-curve, while `BOUNDARY IMPORT MANUAL` prompts for curve/radial options.

### PDF Viewer V1 Fix 8
- Fixed a Civil 3D hard crash caused by recursive `RefreshForActiveDocument` / `SyncToCurrentView` calls.
- Tracks the active `Document` rather than comparing transient `Database` wrapper references.
- Added refresh and synchronization re-entry guards.
- Changed PDF viewer read storage to `StartOpenCloseTransaction()` with an explicit commit before disposal.


### PDF Viewer V1 Fix 10
- Confirmed support for multipage PDF plan sets; individual page files are not required.
- Fixed the Open PDF workflow so renderer failures are no longer overwritten by a misleading `0 pages` success message.
- A PDF path is now saved to the DWG only after the isolated renderer returns a positive page count.
- Added explicit zero-page validation and clearer diagnostics for damaged, protected, unsupported, missing-runtime, or renderer-process failures.
- The previous working PDF configuration remains unchanged when a newly selected PDF fails to load.

## 2026-07-15 - PDF Viewer Fix 13
- Fixed mouse-wheel PDF zoom by explicitly focusing the PDF panel on mouse entry and click.
- Reordered plan calibration so users pan/zoom first, then explicitly arm each PDF point with PICK FIRST/SECOND PDF POINT.
- Returns to free navigation after the first CAD/PDF point pair before selecting the second pair.

## 2026-07-15 - PDF Viewer Fix 14
- Changed ADD REFERENCE to ADD REFERENCE VIEW.
- Reference setup now opens the selected PDF page in unlocked navigation mode.
- Users can pan and zoom before saving the exact table/profile/detail/notes view.
- Saved reference views restore their stored PDF crop when selected.
- RETURN TO CURRENT cancels unfinished reference setup and restores plan tracking.

## 2026-07-15 - PDF Viewer Fix 16
- Added per-plan-sheet model-space coverage boundary selection after two-point calibration.
- Press Enter during boundary setup to use the current model-space viewport as coverage.
- Added temporary yellow boundary highlighting whenever a mapped plan sheet is selected or automatically activated.
- Automatic multi-sheet switching now uses each sheet's saved coverage boundary.

## 2026-07-15 - PDF Viewer Fix 17 Polygon Coverage
- Replaced rectangle-only plan-sheet coverage with selectable Current View, Rectangle, Polygon, or existing closed Polyline coverage.
- Stores copied polygon/polyline vertices in the DWG configuration rather than depending on the source polyline ObjectId.
- Preserves lightweight-polyline bulges for curved boundary highlighting and tessellates curved segments for point-in-polygon sheet selection.
- Automatic multi-sheet selection uses polygon containment; overlapping sheets prefer the smallest containing coverage, then priority, then the currently displayed sheet.
- Added EDIT BOUNDARY so a mapped plan sheet can be given a new coverage polygon without recalibrating the PDF.
- Active plan coverage highlights as a temporary yellow polygon/polyline and creates no drawing objects.


## 2026-07-15 - PDF Viewer Fix 18 Boundary Prompt and Highlight
- Removed the Polyline coverage keyword to eliminate the duplicate `P` shortcut conflict with Polygon.
- Irregular sheet limits are now defined with the Polygon option.
- Changed the active-sheet transient boundary from yellow to light orange.
- Increased highlight lineweight slightly and applied partial transparency for better visibility without obscuring drawing linework.
## 2026-07-22 — Legal Description POC Tie Courses and Text Styles
- Updated `LEGALDESC` so a separate Point of Commencement requires selection of connected LINE/ARC tie geometry from the POC to the POB.
- Added independent `TIE` and `BOUNDARY` course groups in the Legal Description palette; tie courses are numbered T1, T2, etc. and are excluded from boundary area and closure calculations.
- Added validation that the tie starts at the selected POC and ends at the selected POB within the legal-description endpoint tolerance.
- Replaced the generic `Thence to the Point of Beginning;` placeholder with calculated tie-course bearings, distances, and curve calls.
- Added a Text Style selector with built-in CLV Standard, Formal Survey, and Compact wording presets.
- Added optional office-editable `Reference/LegalDescriptionTextStyles.json`; styles in that file can replace a built-in style by name or add new named styles.
- Text-style selection and tie-course edits are persisted in the drawing legal-description session.
- No LISP routines were created or modified.

## 2026-07-22 — Linked Legal Description MText
- Added `PLACE LINKED MTEXT` to the Legal Description palette. The user selects an insertion point and the current editor text is converted from review lines into one paragraph-form MText object.
- Added `UPDATE LINKED MTEXT` for manually refreshing every MText object linked to the saved legal-description session.
- Linked MText now updates when course/context fields, text style, precision, or regenerated legal wording changes, and when `SAVE TO DRAWING` is used.
- Added `REFRESH SOURCE` to reread the original LINE and ARC entities by handle, rebuild their current bearings/distances/curve values, regenerate the legal wording, and update linked MText.
- Linked MText handles are stored with the drawing session, and each linked MText carries an extension-dictionary marker so unrelated MText cannot be overwritten.
- The review editor remains one course per line; only the placed MText is converted to final paragraph form.
- No LISP routines were created or modified.

## 2026-07-22 - Legal description ALL CAPS + verbiage library
- Added ALL CAPS generation for editor output, TXT export, and linked MText.
- Added `DESCRIPTION OPTIONS` for the introductory/situate paragraph, POC description, POB description, and area-statement override.
- Expanded `Reference/LegalDescriptionTextStyles.json` with reusable `CLV Old Standard`, `True POB`, and `Direct POB` wording presets.
- The last tie course now uses a dedicated template so POB wording can vary by office style.
- Linked MText preserves separate final paragraphs while the editor remains one course per line.
- No LISP routines were created or modified.

## 2026-07-22 - Legal Description compile fix

- Fixed CS0104 in `LegalDescription/LegalDescriptionPalette.cs` by fully qualifying `System.Windows.Forms.FlowDirection.RightToLeft`, avoiding the AutoCAD DatabaseServices `FlowDirection` name collision.

## 2026-07-22 - Legal description curve, commencement, and relationship library
- Added automatic ARC classification for tangent, non-tangent, compound, and reverse curves using adjacent course tangent directions.
- Added curve concavity, radial bearing, chord bearing, and chord length calculations for wording tokens and review.
- Added independent selectable wording for same-point beginnings, commencement clauses, final tie/POB calls, and boundary return calls.
- Added course-level relationship dropdowns and feature/reference text for right-of-way, centerline, lot, parcel, section, easement, point-on, intersection, and custom calls.
- Added embedded `LegalDescriptionPhraseLibrary.json`; no workstation support file is required.
- Land Location remains a manual introductory paragraph and was intentionally not changed in this update.

# 2026-07-22 - Legal Description Options Persistence and Area Units

- Corrected the Legal Description Options phrase dropdowns so saved session keys are explicitly restored whenever the dialog reopens; unchanged options no longer fall back to the first item.
- Added area output choices for square feet, acres, or square feet and acres.
- Added independent square-foot and acre precision settings and an option to include or omit “AS DETERMINED BY COMPUTER METHODS.”
- Area selections are stored with the drawing legal-description session and flow to the editor, linked MText, TXT, and DOCX outputs.
# 2026-07-22 - Legal Description Square-Foot Thousands Separators

- Updated generated square-foot area values to use thousands separators in automatic area statements and `{AREA_SF}` / `{AREA_SF_2}` template tokens.
- Examples now display as `46,666 SQUARE FEET` or `46,666.25 SQUARE FEET` according to the selected square-foot precision.
- The formatting applies consistently to the editor preview, linked MText, TXT export, and DOCX export.
- No commands or LISP routines were added or changed.


## 2026-07-22 — LEGALDESC Saved-Session Reopen Workflow

- Updated `LEGALDESC` and `CLV-LEGAL-DESCRIPTION` to check the current drawing for an existing saved legal-description session before requesting new geometry.
- When a session exists, the command now prompts `[Open/New] <Open>`:
  - `Open` restores the saved tie courses, boundary courses, wording selections, area settings, course edits, final preview text, and linked MText handles.
  - `New` starts the original geometry-selection workflow and replaces the drawing's current saved session when saved.
- Retained `LEGALDESC-OPEN` as a direct command for opening the saved session without the startup choice.
- Added a command-line reminder to use `REFRESH SOURCE` when the source LINE or ARC geometry has changed.
- No LISP routines were created or modified.

## 2026-07-22 - Legal Description course-review compile fix
- Corrected the Legal Description review pane to use `System.Windows.Forms.RichTextBoxScrollBars.Both`.
- Removed the unsupported `RichTextBox.AcceptsReturn` assignment; multiline Return input remains native RichTextBox behavior.
- Retained a direct reference to the introductory label when applying its column span, avoiding a nullable control lookup.

## 2026-07-22 — Legal DOCX Template Layout and Structured Land Description

- Corrected the City Surveyor DOCX header population so the preparation date remains on the right side of the APN line and `BY` / `P.R. BY` remain at the right tab stop defined by the embedded Word template.
- Limited automatic bold formatting to approved complete legal phrases. Standalone `BEGINNING` is no longer bolded; `POINT OF BEGINNING`, `TRUE POINT OF BEGINNING`, `POINT OF TERMINATION`, and `COMMENCING` retain phrase-specific formatting.
- Added structured City Surveyor Land Description fields for the two quarter names/codes, Section, Township, and Range.
- The standard paragraph retains the template wording for SOUTH, EAST, M.D.M., CITY OF LAS VEGAS, CLARK COUNTY, NEVADA, and the closing `MORE PARTICULARLY DESCRIBED AS FOLLOWS:` clause.
- Retained an optional complete Land Description paragraph override for uncommon descriptions.
- Structured Land Description values are saved with the drawing session and used by the CAD review text, linked MText, TXT export, and DOCX export.
- No commands or LISP routines were added or changed.

## 2026-07-22 - Legal DOCX exact template formatting fix
- Updated `LegalDocxExporter` to preserve the original run and paragraph formatting from the embedded `Basic Template.dotx` instead of rebuilding replacement text with unformatted Word runs.
- Preserves the template's Arial 12-point font, paragraph spacing, tab stops, seal layout, and other run formatting.
- Corrected first-page `BY:`, `P.R. BY:`, and `PAGE X OF Y` placement by retaining the original leading tab runs from the City Surveyor template.
- APN and date replacement now changes only the placeholder text nodes and leaves all original header layout controls intact.
- Page-number fields are inserted after the preserved template tabs and inherit the original PAGE run formatting.
