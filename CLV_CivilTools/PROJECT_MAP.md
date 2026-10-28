## 2026-10-28 - Temporary surface presentation R9 candidate (unpublished)
- Builds on native-tested R8 cf8f5ffe1e972569fcf13c66dd4869c81cc0b566. R8 cloud retirement, native cropping, camera and navigation are unchanged. Only surface-presentation hooks and a surface helper are added.
- Local editable Civil surfaces receive a private no-display style during review. Saves exact original/temporary style ObjectIds in per-surface CLV_CLOUD_GRID_SURFACE_DISPLAY_R9 Xrecords; shared original style definitions are never modified. Reference surfaces are skipped. A locked local surface layer aborts setup transactionally, including clone/source visibility changes.
- End/Restore completes the existing R8 cloud/view cleanup, then restores surface styles in a separate transaction. It restores only surfaces still using the assigned temporary style; explicit user style changes are preserved. Missing original styles, malformed records and locked/reference objects needing restoration stop the surface transaction with recovery records retained. Unlock and retry CGEND. Failure here does not roll back successful R8 cloud cleanup.
- Initial display-setup failure attempts surface-only recovery; existing tagged-cloud recovery remains CGEND. Starting a new review already calls CGEND cleanup first. Document destruction attempts surface-only restoration against that document database, without cloud commands or editor view changes. Failures are traced and journals retained. A drawing saved during review may retain journals despite in-memory close cleanup; reopen and run CGEND. This does not change R7/R8 cloud-close behavior.
- Private styles are erased only when unused. R9 uses a distinct journal key, so R7 legacy R6/V1 guards remain unchanged. Rolling back to R8 while R9 journals exist will not restore them; first complete R9 CGEND or use a pre-R9 drawing copy.
- Targeted Autodesk-reference compile passed with zero warnings/errors. No native acceptance or blocked test execution claimed. Native checklist: three review/end cycles with surfaces hidden/full cloud restored and exact prior styles returned; two surfaces with different styles; explicit style change during review retained; referenced surface skipped; locked layer before setup leaves no review changes; layer locked during review leaves recovery record, unlock/CGEND restores; cancelled selection/width changes no styles; saved/reopened review copy recovers styles via CGEND; closing another drawing does not restore this drawing's surfaces. Use disposable copies and preserve the R8 working baseline.
## 2026-10-28 - Repeated-cycle cleanup candidate R8 (unpublished)
- Based on published R7 4bd5320f3e7ad8a83999067117b12a40ea6e7109. Changes only EndReview cleanup ordering: validate tagged copy/source identities, clear the review copy's crop list and display flags, commit and regenerate while that copy still exists, then restore source visibility and erase the tagged copy. Source crop setters, placement, definition, files, scan visibility and point data are not edited.
- The intermediate transaction deliberately preserves tagged recovery identity/source visibility if reset or redraw fails. CGEND can retry. Source/copy/definition handles and crop counts are logged before reset. Shared native display/cache isolation is not guaranteed; this is an unverified lifetime hypothesis, not a confirmed diagnosis or recovery for an already restricted source.
- Keeps R7 native slicing, navigation, manual surface visibility, CGFULLCLOUD confirmation and R6 journal guards unchanged. No PointCloudCrop creation/readback, new native interop, viewport-only slicing or definition reload.
- Native acceptance on a disposable pre-R6 copy: use the confirmed palette sequence End / Restore (CGEND), then Restore Full Cloud (CGFULLCLOUD); the palette queues only the command, so capture the Yes/No response (default No); perform CGREVIEW, select Columns and .25, navigate disjoint slices, CGEND, inspect full cloud, then CGFULLCLOUD/Yes only if needed. Repeat three cycles. Capture the complete command history including R8 retire lines and CGCLOUDDIAG after the first visible failure; stop without saving if display remains restricted. Also cancel during review setup and verify CGEND cleanup; test a saved/reopened tagged-review copy separately. Original crops/transform/visibility and grid corrections must match before/after, including a source with preexisting crops.
- Targeted Autodesk-reference compile passed with zero warnings/errors. No native Civil 3D acceptance or console test execution is claimed. Windows Application Control denial is preserved; no blocked executable rerun or bypass.
## 2026-10-28 - Point-cloud rollback R7 (native acceptance pending)
- Selectively restores the ground-grid review behavior from 55f10898f92b19a1b5d82ecdea07ab62c0376d8c, the historical revision immediately before surface hiding and plan-mode Next/Back changes. Does not reset repository history or revert unrelated tools. Restores CloudGridCommands/Markers/RestoreCommands and baseline geometry tests; removes the five later review/crop/sync helper files.
- Removes R6 PointCloudCrop creation/readback/reconstruction from the review/recovery call paths after the reported native application crash following width entry. Exact faulting call is not established by the screenshot/log; no native crash fix is claimed from compilation.
- Adds CGRECOVERYCHECK (read-only journal audit) and guards grid/review/end/full-restore commands when saved R6 crop or temporary-surface journals remain. Prefer a pre-R6 drawing copy. Never invoke crashing R6 CGREVIEW to recover a drawing.
- Optional CGRECOVERDISPLAY supports only a saved originally-empty crop list: validates definition/placement/current count/flags, uses the baseline clearCropping/flag setters, restores verified surface styles, and archives journals rather than deleting them. It never creates/reads PointCloudCrop objects. Nonempty original crop snapshots, corrupt journals or changed state stop without a committed recovery; use a pre-R6 backup. Native visual recovery remains unverified.
- Returning limitations: surfaces must be hidden manually; Next/Back switches to section; CGFULLCLOUD has its original Yes/No default-No confirmation; clone-based review/native crop commands and the earlier repeated-cycle restore restriction/selection problems return. Existing COGO/editing features at the chosen baseline are retained.
- Targeted compilation and patch checks do not constitute native acceptance. Use a disposable pre-R6 drawing copy for the first slice/end test; do not run blocked test executables or change Windows security controls.

## 2026-10-28 - R6 finite cloud-crop repair candidate (native acceptance pending)
- R5 synchronized live/stored viewport state without changing the visible cloud. R6 no longer relies on viewport clipping for cloud restriction: it assigns one closed, finite PointCloudCrop polygon using documented Create(IntPtr.Zero)/addCroppingBoundary APIs. Camera setup and the prior UI improvements are preserved.
- Uses the existing source entity's temporary drawing-display crop state, with no shallow clone, attachment/definition replacement, RCS/RCP writes, or scan/region-visibility edits. This intentionally changes the selected entity's display crops during review; source-file data are not edited. It does not claim independent renderer/cache isolation.
- Before any crop, serializes every original boundary (type/inside/inversion/plane basis/vertices), display flags, definition handle and placement into a per-cloud Xrecord. Each replacement validates the previous expected state and updates its recovery journal in the same transaction. CGEND restores original crop objects/flags, validates readback, and removes only its journal. Missing/changed/corrupt state stops recovery without committing further crop changes and retains evidence.
- Converts finite WCS strips to native cloud coordinates using inverse translation/rotation/scale; validates the source transform against native/geometric bounds before modifying display. Handles the reported 3.280833 scale without changing drawing units. Generated polygons include the required repeated first vertex.
- Journal recovery participates in CGEND, error cleanup and document-close cleanup; saved/reopened journals are recoverable by CGEND. CGFULLCLOUD is guarded while review/journals remain, without adding a Yes/No confirmation. Grid points, COGO data, corrections and exclusions are retained.
- Native acceptance must prove visible slicing AND original display restoration over repeated disjoint slices, Rows/Columns, plan/section, cancellation and save/reopen. Compilation/readback alone is not acceptance. Viewport clips remain for ordinary geometry; finite endpoints now come from the cloud polygon. See the leading R6 acceptance notes.

## 2026-10-28 - Controlled viewport synchronization experiment R5 (native acceptance pending)
- New native report shows live front/back clips enabled at +/-0.125, atEye=false, orthographic, VIEWMODE=22 and full source/definition bounds. This does not reproduce the earlier restricted native entity, and it does not establish an incorrect camera angle.
- Adds optional CGVIEWSYNCTEST only; leaves CGREVIEW/CGSECTION camera, clipping arithmetic and the three independent UI changes unchanged. Logs live and stored camera/clip state plus Editor.ActiveViewportId and Database.CurrentViewportTableRecordId.
- Exports live tiled views with UpdateTiledViewportsInDatabase, verifies the active stored record matches, then reloads using UpdateTiledViewportsFromDatabase and regenerates. Stops without importing if export does not match; restores/re-synchronizes the original live view if reload changes it. No cloud or surface operation.
- This is a native experiment, not a confirmed repair or proof that the cloud renderer requires database synchronization. After testing, CGEND then CGVIEWSYNCTEST synchronizes the restored overview; do not save the clipped review view. Compare ordinary out-of-slab geometry with the cloud.

## 2026-10-28 - Review presentation and navigation revision (native acceptance pending)
- New reviews leave the attached cloud reference and its crop/display state unchanged: no clone, native POINTCLOUDCROP/UNCROP, source hiding, definition reload or detach. Section thickness uses editor front/back clipping. This avoids the old shared-reference crop path; it is a preventive change, not proof of the cause or recovery of an already restricted native cloud.
- CGPLAN shows the existing full-cloud presentation plus a transient cyan slice outline, centered on the active row/column. CGNEXT/CGBACK detect top plan view and retain plan/section mode and active slice; CGSECTION returns to the selected slice. Review starts at the selected point's populated row/column.
- Editable Civil surfaces receive a private no-display style during review, with original/temporary style ObjectIds saved in per-surface Xrecords. CGEND restores styles, preserves explicit user style changes, removes journals and attempts cleanup of unused private styles. Shared style definitions are not edited. Data-reference surfaces are skipped; locked surface layers abort setup transactionally. Failed setup/error cleanup and document close attempt restoration; CGEND supports saved/reopened journals.
- CGFULLCLOUD no longer asks Yes/No. Multiple visible clouds still require a numbered target choice; the review-active guard remains. Restore R3 otherwise retains the R2 reset and diagnostics. A cloud already restricted with crops=0/ShowCropped=False remains an unresolved native recovery case.
- Geometry/navigation console checks and targeted host compilation are available; native repeated-cycle, cancel, Undo/save/reopen and actual visual cloud acceptance remain required. No claim of native acceptance or full solution build. See Docs/CLOUD_GRID_TEST.md.

## 2026-10-27 - Explicitly turn cropped display off during full-cloud restore
- CGFULLCLOUD Restore R2 now calls clearCropping even with an empty crop list, sets ShowCropped=false and CroppingInverted=false, verifies zero remaining crop boundaries / cropped display off before committing, and prints the before/after crop state. Earlier restore only removed listed boundaries and left ShowCropped enabled; the user's R2 reports showed this remained true after reload.
- Keeps existing cloud handle/definition, location/rotation/scale, scan/region visibility, point edits and RCS data. No internal limit-box setters, native API interop, reattachment or definition replacement. View clipping is still disabled.
- Updated command compiles against AutoCAD references without WPF dependencies. Whether this resolves the reported persistent sliced display still requires native confirmation; diagnostic output and an independent blank-drawing attachment are the next checks if it persists.

## 2026-10-27 - Continue cloud diagnostics past missing optional objects
- Fixes CGCLOUDDIAG R1 aborting with eNullObjectId after stored viewport output. R2 guards null viewport/section-manager/definition IDs and isolates each report block and model object so an unavailable optional item does not suppress the cloud scan.
- R2 reports per-cloud crop flags, review tag, placement, elevation display settings, source/active file identities and bounds. The user's R1 output confirmed editor/stored viewport clipping off and VIEWMODE=0, but did not reach cloud data; the persistent sliced display cause remains unconfirmed.
- Diagnostic is read-only and compiled against AutoCAD references without WPF dependencies. Runtime verification on the affected drawing remains required; no cloud display repair is claimed by this change.

## 2026-10-27 - Optional COGO breakline points in slices
- Adds CGADDCOGO / Q3 ADD BREAKLINE POINT alongside native CGADDPOINT. Creates Civil 3D COGO points with a user-entered raw description (TC/FL/LP/custom), next-point-number settings and existing description-key/parameter matching, using the current Cloud/Visual pick and exact slice-line projection. No automatic breakline connections or new point styles/groups.
- BREAKLINE-COGO / BREAKLINE-EXCLUDED layers keep those entities separate from the lightweight ground POINTS layer. Tagged COGO points participate in review markers, initial review picking, world-Z edits, selection and exclusion/restoration; point number/description and added provenance persist. Unrelated survey points are ineligible.
- Point-only picking adds Type > All/Ground/Breakline (A/G/B) to distinguish coincident AutoCAD/COGO markers; W/T selects Window/Type. Changing type clears the current selection. Duplicate COGO description/XYZ is checked within the current job; coincident ground DBPoint / COGO identities are allowed. Exclusion layers do not remove COGO membership from Civil point groups or existing surfaces. Revised host sources and Q3 palette compile with core/WinForms/Autodesk references; native numbering, description keys, label styles, creation/Undo/reopen still require host acceptance. See Docs/CLOUD_GRID_TEST.md.

## 2026-10-27 - Diagnose persistent sliced cloud display
- Adds read-only CGCLOUDDIAG after a user reports CGFULLCLOUD still leaves a sliced display with no ribbon crop indication. Reports editor/stored viewport clipping, VIEWMODE, section manager/live section and model-space section objects, cloud identities/visibility/crop flags, review tags, layers and native/geometric/definition bounds.
- Existing CGEND already restores the saved UCS/view; this diagnostic distinguishes view clipping, live sections, wrong/remaining cloud references and further cloud display/cache restrictions before changing restoration behavior again. It does not reset limit boxes, scan/region visibility, live sectioning, references or source files.
- New diagnostic source compiles against AutoCAD references without WPF dependencies. Runtime F2 output from the affected drawing is needed to identify the remaining cause; no fix for that cause is claimed yet.

## 2026-10-27 - Supplemental slice points and visible section markers
- Adds CGADDPOINT / Q3 ADD POINT IN SLICE. Cloud NODE or Visual picks retain input world Z/along-line station and project transverse XY onto the active grid line. Creates native DBPoints on the job POINTS layer without sampling the TIN. Boundary/duplicate-XY checks, added-point provenance and opposite-direction nonmember indices support later editing/review without fictitious slices. Earlier additions survive Enter/Esc; snap/UCS settings restore.
- Adds CGPOINTSIZE / RESIZE POINTS. During review it sizes temporary view-facing circle/cross overlays, default 1% of view height, so markers remain visible in side view and follow zoom. Colors indicate sampled/accepted/adjusted/excluded states without changing native point display properties. Overlays clear in plan, at end, during document changes/close; outside review the button opens PTYPE.
- Geometry checks passed 35/35, including supplementary row/column projection, rotated large coordinates and direction membership. Revised host sources with actual crop/view helpers and Q3 palette compile with core/WinForms/Autodesk references. Full solution build and live snap/overlay/zoom/Undo/reopen behavior remain host acceptance checks. See Docs/CLOUD_GRID_TEST.md.

## 2026-10-27 - Direct full-cloud display restoration
- Adds CGFULLCLOUD and Q3 GROUND GRID - TEST > RESTORE FULL CLOUD for the reported invalid selection from POINTCLOUDUNCROP. Enumerates visible model-space PointCloudEx references directly; multiple clouds are chosen by numbered handle/layer list rather than native entity picking.
- Requires CGEND first if a tagged review copy remains. After explicit Yes, clears all crops on the chosen cloud via clearCropping, resets crop inversion, disables view front/back clipping and regenerates. Includes pre-review crops; keeps point geometry/status and source RCP/RCS data. Does not change hidden scans/regions or limit boxes. CGEND continues to preserve the source's prior crop state.
- New command compiled against Autodesk assemblies using core/WinForms references; full solution build, native crop recovery, locked-cloud behavior and multi-cloud selection remain workstation checks.

## 2026-10-27 - Fix ground-grid picker build reference failure
- Fixes the reported CloudGridEditing.cs CS7069 errors for System.Windows.Point / WindowsBase. Replaces Editor.PointToScreen calls with orthographic WCS-to-view projection using AutoCAD Matrix3d/Point2d and SCREENSIZE pixel scaling. Keeps the existing WinForms project, target framework and host references unchanged.
- Verified the previous picker fails when WPF references are absent and the revised host code plus actual crop/view helpers compiles with the same core/WinForms/Autodesk reference set. Full solution build and live rotated-view picking remain workstation acceptance checks. No change to relative world-Z editing, original-Z metadata or cloud handling.

## 2026-10-27 - Grid world-Z adjustment and point-only picking
- Q3 GROUND GRID - TEST adds MOVE Z (CGMOVEZ) and SELECT POINTS (CGSELECT). Relative offsets such as -0.04 modify native DBPoint world Z directly, independent of UCS/view, preserving exact XY, original sampled Z and excluded state. Selection supports multiple clicks and Window, with one job per operation and current-row restriction in section review; CGPLAN permits the current job across rows. No-review plan editing is supported.
- Initial CGREVIEW point selection, CGSETZ and CGTOGGLE now use point-center screen picking through the visible cloud. Picks bypass cloud/guide entities, respect point layers and orthographic view clipping, reject ambiguous projected centers, and restore snap/Ortho settings on cancel. No cloud locking or global selection setting changes; ordinary native selection remains unchanged. See Docs/CLOUD_GRID_TEST.md for host acceptance steps.
- Host code and Q3 palette compile against installed Autodesk assemblies; live picking, batch offsets, Undo and display behavior still require Civil 3D acceptance testing.

## 2026-10-27 - Ground grid / cloud slice starter
- Q3 ROADWAY adds GROUND GRID - TEST: CGRID, CGREVIEW, CGNEXT/CGBACK, CGPLAN/CGSECTION, CGSETZ, CGTOGGLE, CGACCEPT and CGEND. Uses an existing temporary TIN, straight closed boundary and rotated grid frame to create native AutoCAD POINTs at sampled XYZ, without COGO objects. Count confirmation, 10,000-candidate cap and out-of-surface skipping support small trials.
- Job-specific POINTS/EXCLUDED/GUIDES layers and lightweight original-Z/status metadata persist. Review uses a tagged cloud-reference clone and original-source visibility restoration; native crop commands operate on the clone only. Row/column navigation changes crop/view without resampling corrected elevations. CGEND is required before saving/closing; saved tagged copies support verified recovery. See Docs/CLOUD_GRID_TEST.md for limits and final breakline/POINT surface handoff.
- Geometry tests passed 25/25 and new host code compiled with actual Autodesk crop/view helpers. Full solution build and native cloud/crop/view/Undo/edit acceptance remain required. No automatic temporary/final surface creation, raw-return snapping or point-cloud accuracy guarantee is included in this starter.

## 2026-10-26 - Point inverse / record-to-survey comparison
- Adds POINTINVERSE / SURVEY-POINT-INVERSE and Q4 > MAPPING > TOOLS > POINT INVERSE. Pick FROM record/reference and TO surveyed/comparison COGO labels or markers, including dragged labels of coincident markers; exact point-number entry is available for overlapping labels. Reads actual point identities/coordinates, never label positions or nearest neighbors.
- WinForms PaletteSet presents both point records, horizontal offset, signed TO-minus-FROM N/E/Z differences, 3D distance, bearing, azimuth and grade. Supports keep-FROM/next, reverse, explicit refresh, clipboard report and dated snapshot AutoCAD table output. Uses the existing PalettePositionHelper. No ground/grid or unit conversion; source coordinates/elevations and Civil drawing unit convention are used.
- Missing elevation is N/A; coincident XY has undefined bearing/grade. Different coincident point identities are accepted; same-point double-picks are rejected. Temporary rings/line clear on hide/document changes; only INSERT TABLE creates persistent geometry. Tables are snapshots, not linked reports.
- Production calculation/report checks passed 45/45; new host/UI sources compile against installed AutoCAD/Civil 3D assemblies. Full solution build and native dragged-label, table, Undo and multi-document acceptance remain required. See Docs/POINT_INVERSE.md and Tests/PointInverse.Tests.

## 2026-10-21 - Sewer native representation payload S11-TAG-METADATA
- Supersedes the value-2 compatibility patch: the next reported circle F42 has Int32 value 3. Treat the native 1071 signed Int32 as opaque metadata rather than a fixed enum; no inner/outer/stub role or undocumented numeric range is inferred.
- Exact four-item code/type/application/version/self-handle validation, evaluated dynamic Circle ownership, native self-resolution, empty OD and absent leaf extension dictionary remain required. Capture/sign the actual integer and require clones to retain that exact value before removing only the proved native tag on new output. Source definitions remain untouched.
- Added regression coverage for the F42 case, values beyond 3 and Int32 boundaries, altered clone values, wrong types, unrelated XData, ownership and metadata failures. The production parser and tests compiled offline against installed .NET 8 reference assemblies with warnings as errors; 189/189 checks passed. Normal project restore was unavailable because NuGet could not be reached. Full plugin build and native Civil 3D geometry/OD/Undo/rerun tests remain required.
- Banner: 2026.10.21-S11-TAG-METADATA. Existing S10 stub/connection cleanup, storm processing, clipping tolerances and archive formats remain unchanged. Test on a disposable DWG copy; the reported preflight run applied no geometry or cleanup.

## 2026-10-21 - Sewer observed representation tag value 2
- Recognizes the reported AcDbBlockRepETag Int32 value 2 alongside 0 and 1 for verified dynamic circle leaves. Exact payload codes/types, evaluated owner, self-handle resolution, empty native OD and absent extension dictionary remain required. No ring or stub role is inferred from this integer.
- Clone verification preserves the actual source value and rejects changed values. Unknown values still require review. Fixes the first preflight rejection for L25-01338-SSMH-01/02 (leaf F41); the pipe endpoint reviews must be retested after those manholes can be planned.
- Stub/connection rules, storm behavior, geometry tolerances and archive formats are unchanged. Added regression coverage for value 2 source/clone acceptance, changed clone values and ownership/metadata guards.
- Static review completed; executable tests were blocked by denied .NET first-run sentinel access in the authoring environment. Full plugin build and native Civil 3D tests remain required on a disposable drawing copy.

## 2026-10-20 - Storm null connection recognition R6-CONNECTIONS
- Storm GIS PREP accepts a nonempty name ending exactly -CONN with exact UFLS-Null Structure PartSizeName, using the same role-conflict, identity, duplicate and coordinate checks as -STUB. These non-graphic source points remain retained and do not consume structure geometry.
- Fixes UnknownRole for L24-01007-STRM-80+67-CONN, which stopped ALL before managed pipe conversion. Genuine review items still block downstream processing.
- Open-wall gap exemption remains restricted to -STUB; connection names do not inherit it. No endpoint movement, tolerance, cleanup archive format, command alias or framework changes.
- Added production-linked regression coverage for the reported record, case/trim handling, duplicates, invalid coordinates, conflicts and rejection of connection gap exemption.
- Static review completed. C# tests, full plugin build and native Civil 3D rerun remain required; no .NET SDK or Autodesk host is available in the authoring workspace.

## 2026-10-13 - Verified sewer connection-point cleanup S10-CONNECTIONS
- Extends the approved source-point cleanup to unchanged, uniquely owned OpenConnectionEnd points with the exact sewer -CONN identity. Each requires a verified OD-named owned pipe endpoint and the same native-OD-only metadata gate. Unknown, unrelated, orphaned, edited or metadata-bearing points remain protected.
- Connection endpoints retain their original 1e-8 XYZ/radial-XY association rule; the STUB 0.10 XY allowance is never applied to connections. No pipe endpoint, wall, manhole geometry or native pipe OD is changed by cleanup.
- Complete original typed OD, source identity/XYZ and actual pipe endpoint links are archived/read back before erasure. Missing versus blank connection PartSizeName remains represented exactly in the typed payload. The new companion format explicitly records NullStub/Connection kind and OpenNullEnd/OpenConnectionEnd source role.
- Existing S9 V1 companion JSON and its V3 main-record digest are verified before conversion. The first actual connection cleanup atomically upgrades to companion V2 and main V4; all prior STUB records are retained. Older DLLs intentionally reject V4. Reverting only the DLL is insufficient: retain the pre-S10 DWG backup or Undo the entire cleanup transaction.
- Banner: 2026.10.13-S10-CONNECTIONS. Added production-linked kind/role, exact connection geometry, optional-part and archive-upgrade regression tests. Static review only; executable C# tests, full build and native CAD rollback/Undo/save-reopen trials remain unrun in this environment.

## 2026-10-13 - Verified sewer STUB cleanup S9-STUBS
- GIS PREP - ALL now removes only unchanged, uniquely archive-owned OpenNullEnd POINTs with the exact supported -STUB / UFLS-Null Structure identity and verified OD-named owned pipe terminals. Connection, unrelated, orphaned, edited, ambiguous and unsupported-metadata points remain protected; no layer/proximity sweep is used.
- Before erasure, a versioned drawing-local stub archive preserves original handle, identity, XYZ, shape fingerprint, every native OD record/field/type/value and exact linked pipe endpoint coordinates. Typed payload readback reproduces the native fingerprint, including duplicate records. The main archive upgrades to V3 with a required companion digest; missing/changed archives or live restored source handles fail verification. Existing V2 archives are read without rebuilding pipe geometry.
- Uses the same bounded native OD-only metadata guard established by S8. Pipe endpoints, walls and manhole geometry are not changed by stub cleanup; no artificial outer structure is created. All write acquisition, revalidation, archive readback, point erasure and final verification share the batch transaction.
- Added production-linked native OD codec and stub eligibility tests. Static review only in this authoring environment: executable tests, full build, native rollback/Undo and save/reopen trials remain required on a disposable drawing copy. Banner: 2026.10.13-S9-STUBS.

## 2026-10-12 - Retire standalone manual OD transfer and broad point erasure
- Removed the OBJECT DATA / XFER OBJECT DATA and CLEANUP / ERASE POINTS sections from ADD TO DATABASE, together with the unused standalone CLV-GIS-OD-XFER and CLV-GIS-ERASE-POINTS command implementations. Storm and sewer GIS PREP - ALL already perform managed native OD transfer and verified owned-source cleanup directly.
- The native OD readers/copy/verification helpers in GisImport.cs, verified storm/sewer cleanup, OD inspection and shared CLV_GIS_OD_HELPERS.lsp remain required. Retained single-structure commands still use that shared helper.
- CREATE GIS DRAWING, both GIS PREP - ALL buttons, FINALIZE STRUCTURES/PIPES and all other TOOLS buttons retain their existing routing. External scripts invoking the two retired command names need updating; those wrappers are no longer registered.
## 2026-10-12 - Verified sewer manhole source-point cleanup S8-POINTS
- CLV-GIS-SSWR-GIS now uses one exact archive-based source-point cleanup path for both fresh conversion and existing managed S4-S7 V2 results. Existing results and absence of new/untracked inputs are verified before cleanup; converted pipe/lateral geometry is not rebuilt.
- Only a unique RetainedSourcePoint -> original source handle/provenance -> exact ManholeOuter ID/name can be erased. Complete current native OD must match both archive fingerprints and pass source-to-owned-outer readback. Source geometry and identity must remain verified; the original 0.10 XY manhole association allowance is retained. Inner circles intentionally lack OD and are not transfer destinations.
- Additional XData, foreign reactors, mixed/nested/unknown dictionaries and unsupported storage are preserved and reported. The newly observed native OD-only layout is narrowly recognized using an owned exact DBDictionary containing one *A1 entry with DXF class IRD_OBJ_RECORD, owned child, no additional XData/extensions, and only its dictionary reactor. A matching dictionary key alone is insufficient. No native dictionary is cleared separately.
- All cleanup point write access and revalidation precede an in-place archive rewrite/readback, erasure and final remaining-output verification in the same transaction. Only erased POINT live-output entries are removed; source handles, original block states, anchors and pipe provenance remain. Reruns without eligible retained points do no cleanup writes. Existing DBPoint Shape hashes and V2 archive format remain compatible.
- Working S7 lateral classification and endpoint handling are preserved. Connection/null/review/unrelated points are not swept by layer or proximity. Banner: 2026.10.12-S8-POINTS. Production-linked pure tests cover metadata/proof guards, archive-link uniqueness and write/readback/erase ordering with injected failures; native rollback, Undo, save/reopen and host OD recognition require a trial on a copy.
## 2026-10-12 - Structureless sewer lateral segments S7-LATERALS
- The sewer batch accepts exactly one present Character StructureStart/StructureEnd field containing blank text. Missing, duplicate, unavailable or incorrectly typed fields still fail preflight; nonblank names still require unique verified structures. Complete original typed OD, including the blank text, is preserved.
- Otherwise generic blank-end candidates require one per-run Yes/No classification prompt showing the drawing, count and candidate handles/names. No is the default; No, Enter or Cancel aborts the batch before changes. Explicit storm or conflicting utility evidence cannot be approved. Confirmation binds only the captured source geometry and OD for this transaction; both are revalidated before writes.
- Both-blank LINE segments preserve their source direction and endpoints without invented structures, snapping, connection searches or joining. A named endpoint still clips only at its verified manhole. Under-12-inch segments remain separate single centerlines; existing larger-pipe wall rules remain. Isolated segments and drafting gaps do not require graph connectivity.
- CLV-GIS-SSWR-DIAG now reports present blank endpoints, orientation feasibility and whether generic batch classification is needed; it never grants approval. Existing ownership/archive, appearance, diameter and planar checks remain. Point cleanup is unchanged pending actual retained-point metadata evidence. Banner: 2026.10.12-S7-LATERALS.
## 2026-10-12 - Sewer OD-bound circular endpoint orientation S6
- Ordinary sewer pipe shots may terminate off center, including at the manhole inner wall. The host now proves one forward/reverse association using the two OD-named, verified outer circles and the existing circular clipping planner, instead of requiring each endpoint within 0.10 XY of the imported structure POINT. Centerlines and applicable walls still clip to the exact outer wall; no point is moved or gap extended.
- Exact connection XYZ and the existing null-end 0.10 XY / 1e-8 Z point rule, existing point/circle/pipe elevation checks, finite horizontal LINE geometry, diameter rules, ownership, OD and archive checks remain required. The unchanged S4 V2 archive schema is retained. Banner: 2026.10.12-S6.
- CLV-GIS-SSWR-DIAG is a separate read-only batch report: OD endpoint names, WCS XYZ, four point deltas, old center-gate results, current circular orientation, center/wall clipping probes in both directions, physical boundary/elevation differences and layer checks. It creates no geometry/layers, writes no archive, and does not commit a transaction. Copy its command-history output for diagnosis if preparation still stops.
## 2026-10-12 - Sewer representation tag payload and diagnostics S5
- Adds only the second observed AcDbBlockRepETag Int32 1071 value, 1 (F40 outer circle), alongside 0 (F3F inner circle). These observations do not establish general inner/outer semantics. All existing dynamic-owner, exact-circle, self-handle, empty-extension and empty-native-OD checks remain required.
- Source signatures retain the actual payload. Clone normalization requires the exact same 1071 value as its source; changed or unknown values stop conversion. Only the validated application on new output is removed; source definitions are preserved.
- Metadata reviews identify failed predicates, native type, evaluated owner/context, extension handle and XData code/type summaries. Empty native OD is reported as a successful state, not a rejection explanation. S5 banner is 2026.10.12-S5. S4 V2 archive schema is unchanged; earlier S1-S3 archives still require an untouched input copy.
## 2026-10-12 - Sewer evaluated circle representation tag S4
- Accepts only the observed exact AcDbBlockRepETag payload (1070 short 1, 1071 int 0, 1005 self handle) on a circle directly owned by a verified evaluated dynamic block. The self handle must resolve to that leaf. Unknown XData, extension data, native OD and unsupported geometry still require review.
- Captures the source leaf payload and evaluated owner/context in the block signature, rechecked before retirement. Source definition metadata is never edited.
- Newly cloned ordinary GIS circles normalize only that validated application, with readback; empty-source/unexpected-clone tags fail. Output XData must stay empty. Output extension topology is archived after native OD transfer, while complete OD fingerprints are checked separately. Source/clone tag removal and rollback need a native disposable drawing trial.
- S4 writes an explicit V2 metadata-signature header under the existing archive key. Earlier S1-S3 archives stop with a version diagnostic directing the operator to an untouched input copy; archives and existing outputs are not migrated, deleted or reprocessed.
- Sewer banner is 2026.10.12-S4. Pipe clipping, exact -CONN/null identity, cache finalization and standalone/Storm routes are unchanged.
## 2026-10-08 - Explicit sewer connection policy S3
- `SewerPreparationRules.IsExplicitSewerConnectionName` recognizes only corrected sewer-utility names ending exactly `-CONN`. `GisSewerManholePlanner` represents connection versus existing null/STUB endpoints explicitly with `SewerOpenEndKind`, without requesting or consuming MH geometry.
- The sewer-only identity-reader entry point permits missing/blank Character PartSizeName for that exact connection convention. The original Structures reader keeps its strict behavior for Storm/other callers. Available part text still participates in explicit utility-conflict checks; generic physical-part descriptions do not select a connection geometry.
- The host retains/rechecks connection point XYZ/full OD, preserves open pipe/wall terminals and archives the retained point under OpenConnectionEnd. Normal unique-ID/orientation/elevation checks precede clipping. Rerun ownership includes those retained points; no endpoint rename or data repair occurs.

## 2026-10-08 - Sewer native enumeration correction S2
- `GisSewerManholePlan.AddDynamicMetadataState` replaces `DBDictionary.Cast<DBDictionaryEntry>()` with a typed foreach snapshot, then the same ordinal sort. Autodesk's typed enumerator exposes DBDictionaryEntry; the non-generic enumeration used by LINQ yielded DictionaryEntry in the installed host. Other typed dictionary loops are unchanged.
- `SewerPreparationRules` adds an explanatory overload without changing classification tokens/precedence. `GisImport.TryReadPipeUtilityEvidence` keeps the same Character field selection/order and records table/record/field labels. `GisSewerPreparation` includes that evidence in review diagnostics. Tests cover the pure diagnostics; actual dictionary traversal/native OD remains a host regression.

## 2026-10-08 - Managed sewer preparation S1
- `Gis/GisSewerGisCommands.cs` now calls `GisSewerPreparation.Run` synchronously. The Sewer ALL route no longer queues legacy MH/pipe LISP or broad cleanup. Standalone commands and Storm R5 remain unchanged.
- `Gis/GisSewerManholePlan.cs` reads same-record native Structures identity, matches a unique evaluated `UFLS-GIS-MH-CIRCULAR` and optional `UFLS_MH_MARK`, and validates actual visible circular outer/inner geometry, transforms and XYZ alignment. It does not infer ring roles from radius or pair a sewer MH with a storm `-JS` record.
- `Gis/SewerCircleTrim.cs` and `SewerPreparationRules.cs` provide production-linked exact circle intersection, feet-based size/unit checks and utility filtering. `GisSewerPreparation.cs` stages straight-line clips at both connected OUTER circles for the small-pipe line, large centerline and both offset walls, then applies all geometry/typed-OD/owned-source cleanup in one transaction. It archives original pipe state and final ownership before retirement; unchanged reruns verify without duplicating geometry.
- Imported points with extra XData/extension data remain in place and are reported. Ambiguous ownership, unsupported/nonplanar geometry, ByBlock-dependent circle appearance, unknown nested data, incompatible units or changed/untracked prior results stop before geometry conversion. There is no automatic flattening, endpoint extension or automatic MAPIMPORT.
- `GisImport.cs` adds strict sewer identity/connection reading and read-only utility evidence on the existing native typed OD adapter. The existing diameter-consensus loop is extracted without changing its Storm behavior. `GisNewDrawingClone.cs` exposes its existing primitive snapshot reader; its clone workflow is unchanged.
- `GisCreateDataPalette.cs` now presents CREATE GIS DRAWING then GIS PREP - ALL under both STORM DRAIN and SEWER. Both setup buttons route to the existing setup/save/reopen command. See `Docs/SEWER_GIS_PREP.md` and `Tests/SewerGisPreparation.Tests`.
- Static review completed; executable tests, full build, native OD rollback and end-to-end sewer sample validation remain unrun in the authoring environment.

## 2026-10-08 - Add to Database palette layout
- `Gis/GisPalette.cs` moves the renamed ADD TO DATABASE launcher into GIS TOOLS. `Gis/GisCreateDataPalette.cs` places CREATE GIS DRAWING first under STORM DRAIN, retains storm/sewer GIS PREP - ALL, and removes only the six individual storm/sewer buttons.
- Palette/window/tab/access-display titles use ADD TO DATABASE. Existing command identifiers, access checks/users file, 256 x 24 button rows, 7 pt font, 340 x 700 default and 300 x 540 minimum sizes are retained. Backing routines and sewer behavior are unchanged.

## 2026-10-08 - Separate GIS drawing setup for manual import
- `Gis/GisNewDrawingCommands.cs`: Session-context Blank/source-CRS/units/verified copy, new-filename prompt, full-copy save with non-overwriting publication, disk setup verification and close/reopen. Cancel/save failure retains the prepared drawing; reopen failure preserves the saved file. Manual MAPIMPORT/IPF instructions remain; no SDF picker or automatic import.
- `Gis/GisNewDrawingCoordinateSystem.cs`: active source-code policy and one assignment/readback; no SDF metadata, WKT or dictionary logic.
- `Gis/GisNewDrawingResources.cs`: exact source codes/shared UNC paths and read-only new-DWG path validation protecting source/template/open files. No IPF parsing or import field schema remains.
- `Gis/GisNewDrawingMapApi.cs`: only the borrowed Map project Projection reader/setter used by setup.
- `Gis/GisNewDrawingClone.cs` and `Gis/GisNewDrawingAppearance.cs`: active geometry/block/attribute/OD preservation, dependency collision checks and rendering comparisons.
- `Gis/GisPalette.cs`: GIS TOOLS launches `ADD TO DATABASE`. `Gis/GisCreateDataPalette.cs` queues the unchanged `CREATE GIS DRAWING` command from its STORM DRAIN section. Existing R5 preparation remains independent. Workflow and final automatic-trial failure: `Docs/GIS_NEW_DRAWING.md`.
- Retained tests: `Tests/GisNewDrawingCoordinateSystem.Tests` and `Tests/GisNewDrawingAppearance.Tests`; the unused profile/SDF/native-output test projects and their fake/fixture files were removed with the automatic import implementation. Shared Autodesk references used by other features remain.

## 2026-10-07 - Managed storm pipeline and durable cleanup R5
- `Gis/GisStormGisCommands.cs`: calls committed structure preparation, then one synchronous managed transaction for pipe preparation, trim and cleanup. Legacy standalone LISP-backed commands are unchanged.
- `Gis/GisStormPipePreparation.cs`: preflights positive agreed native InsideDiameter and supported planar geometry; stages native offset sides before database writes; copies/verifies complete OD on every owned wall; preserves source geometry/OD and writes exact side ownership plus post-trim geometry fingerprints onto the retained source. Existing recorded pipes are verified and reused; untracked processed centerlines are rejected.
- `Gis/GisTrimInsideBoundary.cs` and `Gis/StormTerminalTrim.cs`: the new nonprompt storm-only batch stages immutable terminal clipping and preserves Line/LWPOLYLINE identity, widths and native OD. It takes explicit verified boundary/wall IDs, rejects ambiguous/unsupported geometry before mutation, and requires caller rollback on any review/error. Existing manual code remains unchanged.
- `Gis/StormPipePreparationRules.cs`: production-linked threshold, utility-conflict, side-ownership and completion rules. Source-owned terminal contexts use the legacy 25+half-diameter search bound; explicit null exemptions require tangent/perpendicular-offset correspondence, unique identity and no competing anchor/footprint. `StormTerminalTrim.ProbeTerminalGap` reports positive-length interior continuations for review without extending geometry.
- `Gis/GisStormStructureCleanup.cs` / `Gis/StormStructureCleanup.cs`: model-space cleanup reads all live/archive ownership claims, verifies complete committed structure OD, and resolves exact marker names/layer/roles within 0.10 with mutual uniqueness. Unknown/unverified neighbors remain competitors. Exact archived marker IDs prevent future deletion by proximity alone.
- `Gis/GisStormPrepArchive.cs`: a drawing-local versioned dictionary retains each deleted source's identity, original XYZ anchor, all output soft pointers, full typed-OD fingerprint and known marker handles. Atomic write/readback precedes erasure. Existing R1–R4 point-owned completion records remain readable and migrate only on successful cleanup. Archives survive deletion of any output and are validated before candidate aggregation.
- `Gis/GisImport.cs` / `Gis/StormObjectDataFingerprint.cs`: strictly read-only complete transfer proofs and canonical versioned fingerprints, plus positive finite InsideDiameter consensus across attached native records. The existing native OD writer/lifetime fixes are preserved. Standalone legacy helpers still have callers and are not removed.

## 2026-10-06 - Evaluated access geometry and graphics R4
- `Gis/GisStormStructureAuto.cs`: access-only materialization reads the reference's evaluated `BlockTableRecord`, not its authoring `DynamicBlockTableRecord`, and clones simple visible leaves using the accumulated parent/child `BlockTransform`. It suppresses hidden branches and rejects active unsupported complex geometry or external references. The existing DI `ExplodeRecursive` path remains unchanged.
- `Gis/StormStructureVisibility.cs`: pure visibility-first traversal shared by the actual host path and unit tests; hidden ancestors are not traversed, callbacks propagate failures, and nesting is bounded. The existing exactly-one outer access validator and native OD transaction gates remain.
- Verified new geometry and completion-linked rerun outputs receive `RecordGraphicsModified(true)` and `QueueForGraphicsFlush` while transaction-resident. A batch `Regen`/`UpdateScreen` follows commit. This is a graphics-registration correction for trial, not a claim of verified native display behavior.

## 2026-10-06 - Independent access/box centers R3
- `Gis/StormStructureMatching.cs`: removes the cross-role source-to-source distance gate. Exact unique base names establish the association. DI/access keep independent 0.10 center matching; box targets use tested footprint containment within the original 25-unit bound. Closed straight survey polylines no longer need mathematically perfect right angles. `GisStormStructureAuto.cs` refreshes display after committed batch work; no layer visibility changes or DI conversion rewrite.

- `IsExistingOutline` marks one physical survey outline as unbound DI/box geometry, or binds it to an existing native OD role. The shared candidate graph enforces cross-role mutual uniqueness; `Match.Role` always remains the source role. Host conversion dispatches by physical outline/block pathway rather than coercing source identity.
- Explicit null pipe ends are reported in `PreservedPipeEndSourceIds` after normal duplicate/coordinate validation; the host retains those points and prints their identity without adding a false missing-geometry review.

- `PlanCompletions` validates all output ownership before host target aggregation, including secondary DI outer footprints; accepted disjoint cohorts reserve every output and expose one logical target. Read-only outline OD inspection distinguishes empty metadata from invalid data and verifies the original records before replacement.

## 2026-10-06 - Storm OD adapter R2 ownership and scope
- `Gis/GisImport.cs`: borrows the Map project's cached ODTables collection without disposing it; retains disposal of owned table/record/value wrappers. Structures identity uses table-specific records, while full typed copy uses entity-scoped `Tables.GetObjectRecords`. The fourth native Boolean is `skipSubObj`; it is not a record-creation flag. Installed MapValue exposes `Point`.
- `Gis/GisStormStructureAuto.cs`: distinguishes identity-data problems from native reader failures and stops before geometry planning/mutation on the first native failure; revision stamp `2026.10.06-R2`. Diagnostics preserve native code, operation, handle/table/field and full exception chain. The first-trial Git ref is retained as `backup/storm-gis-prep-2026-10-06-first-trial`.

## 2026-10-06 - Centered storm GIS PREP-ALL trial
- `Gis/StormStructureMatching.cs`: pure OD-role classifier and order-independent, mutual one-to-one planner. Exact `-JS` suffix identifies a junction box before generic manhole wording; unsuffixed explicit `ACCESS STRUCTURE` identifies access; known SDDI/type tokens identify drop inlets. Duplicate identities, conflicting roles, nonfinite coordinates, competing matches, and offsets beyond 0.10 drawing units are review items.
- `Gis/GisStormStructureAuto.cs`: snapshots same-record Structures identity and source geometry before edits; plans rectangular junctions while access/DI blocks remain intact; reserves destinations; preserves DI_CENTER footprint matching; synchronously verifies full typed native OD before retiring original geometry. All imported source points remain. Completion Xrecords on source points link outputs for rerun verification and survive XData cleanup.
- `Gis/GisImport.cs`: bounded fail-closed OD adapter using existing ManagedMapApi loading. Reads Name and PartSizeName from the same Structures record; copies all Character/Integer/Real/Point fields; verifies full typed source/destination records; never overwrites differing native destination OD.
- `Gis/GisStormGisCommands.cs` and `Gis/GisPipeOdOffset.cs`: ALL calls structures synchronously, stops downstream work on any review/failure, then queues the existing pipe helper without broad managed cleanup. Standalone pipe commands keep their existing cleanup behavior.
- `Gis/GisSurveyCacheFinalize.cs`: exact duplicate checks for nonlinear structures now require matching entity/outline shape and extents, not center proximity alone. Circle/curved-access output cannot be suppressed by a co-located straight box outline. Nearby differences remain review conflicts.
- No new LISP helper or shared-server deployment is required. See `Docs/STORM_GIS_PREP_CENTERED_TRIAL.md` for trial steps and validation limits.

## 2026-08-20 - Q1 Pipe Top Check
- `Ufls/UflsPipeInfo.cs`: added `UFLS-PIPE-TOP-CHECK`, a COGO-point-driven pipe top verification command. It reuses the existing `UFLS-PIPE-INFO` pipe geometry/interpolation/top-of-pipe calculation by passing the selected COGO point X/Y into the shared calculation path, while using the selected COGO point Elevation only as `SURV - TOP`.
- `Ufls/UflsPalette.cs`: added `PIPE TOP CHECK` under Q1 UFLS > CHECK > INFO.
- `Shared/LayerStandards.cs`: added managed layer standard `V-SURV-CHCK` (ACI 6 magenta, Continuous, plot style `M`, description `Survey: Check shots or linework`) for the new check label. Labels require `CLV-Non Anno`, use text height `0.1`, are one MText object, and restore the original current layer after completion/failure.

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


