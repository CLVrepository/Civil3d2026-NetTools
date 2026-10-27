# Ground grid / cloud slice starter

This is a first native Civil 3D trial, not a production extraction workflow. Start on a disposable drawing copy with a roughly 20 x 20 test area. The initial test uses one attached RCP/RCS point cloud and an existing temporary TIN derived from that cloud. It does not build the temporary TIN, classify raw returns, or build the final deliverable surface.

## Commands and Q3 buttons

Q3 > ROADWAY > GROUND GRID - TEST:

- CGRID / CREATE GRID: select temporary TIN and closed straight-sided horizontal polyline; pick grid origin and X-axis direction; enter spacing in Civil drawing units. Default 1 means one foot in a feet drawing or one meter in a meter drawing. Check the displayed units. Confirm point count with Yes; No is the default.
- CGREVIEW / REVIEW GRID: pick one generated AutoCAD POINT and the cloud. Choose Rows or Columns and total slice thickness. Defaults are 0.5 feet or 0.1524 meters (six inches total, three inches each side).
- CGNEXT / CGBACK: next/previous populated grid row or column; stops at the ends. No elevations are resampled.
- CGPLAN / CGSECTION: original overview versus current side-on review slice. The overview retains the cloud's current slice crop; END fully restores the original display.
- CGADDPOINT / ADD POINT IN SLICE: during active CGSECTION review, add native DBPoints between grid intersections (for example a missed crown in a 5- or 10-foot grid). Cloud mode enables nearest cloud NODE snapping; wait for the snap glyph before clicking. A click without a glyph remains a visual pick, not a verified raw return. Visual mode disables snaps for choosing an envelope directly. Picked world Z and along-line position are retained; transverse XY is projected exactly onto the current row/column centerline. Output therefore need not coincide with the original 3D cloud return. The temporary TIN is not sampled. Enter/Esc ends placement; earlier committed additions remain. Points outside the original job polygon and included points with duplicate XY are rejected. Snap/Ortho/grid-snap/OSNAPZ/UCS settings restore on exit.
- CGPOINTSIZE / RESIZE POINTS: during review, sets temporary marker diameter as a percentage of view height (default 1%, up to 10%). Outside review it opens native PTYPE. Section markers are view-facing circles with crosses, so they do not flatten edge-on. Zoom updates their size; plan uses the native point display. Markers clear on CGPLAN, CGEND, document switching/closing and are not saved or used as surface data. Original DBPoint normals/styles/XYZ are unchanged. Marker colors: sampled yellow, accepted green, adjusted/manual cyan, excluded red. These are overlay colors; native point colors/layers are unchanged.
- CGMOVEZ / MOVE Z: click one or more generated points, or type Window and specify two corners; press Enter, then enter a relative WORLD Z offset such as -0.04 to lower four hundredths in a feet drawing. Positive raises. XY remains exact. Works in the rotated section, CGPLAN and ordinary plan views before review. During section review only the current job/row is eligible; in CGPLAN the current job is eligible. Before review, the first pick/window chooses one job. Repeating a click toggles that point out of the set; windows add points. Esc cancels the whole selection. Excluded points retain their exclusion when moved.
- CGSELECT / SELECT POINTS: same point-only picker, leaves just those generated DBPoints selected for Properties or other commands. Those other commands still follow their own UCS rules; use CGMOVEZ for guaranteed world-Z offsets.
- CGSETZ: pick a generated POINT from the current job and slice; enter a new absolute Z. XY stays fixed and original sampled Z stays in metadata. This starter uses numeric absolute elevation entry, not automatic cloud-return snapping or screen-picked heights. CGMOVEZ provides relative changes.
- CGTOGGLE: exclude/restore a current-row generated point. Excluded points are red and remain recoverable on the job's nonplot EXCLUDED layer.
- CGACCEPT: mark remaining Sampled points in the current row Accepted; adjusted/excluded states are retained.
- CGFULLCLOUD / RESTORE FULL CLOUD: after CGEND, removes ALL crop boundaries from a chosen visible model-space cloud, resets crop inversion, disables view front/back clipping and regenerates. Selects the cloud by a numbered handle/layer list if more than one is visible; no native selection prompt. Explicit Yes confirmation defaults to No. Includes pre-review crops, so use only when the full uncropped display is desired. Does not change grid points, cloud file data, hidden scans/regions or limit boxes; zoom out as needed. If a cloud refuses crop clearing, check its Lock property. No automatic unlocking or layer-setting change.
- CGEND / END / RESTORE: remove tagged review cloud copies and restore original cloud visibility. During the current session it also restores the original UCS/view. Grid points, corrections, exclusions and metadata remain.

## What is created

Point selection in CGREVIEW (the initial grid point), CGSETZ, CGTOGGLE, CGMOVEZ and CGSELECT uses projected point centers, bypassing the cloud entity entirely. Click near the POINT center, or use Window for batches. The picker respects visible, thawed, unlocked point layers, orthographic view bounds and front/back clipping. Ambiguous projected centers require zooming or a narrower slice. Point markers can be enlarged with PTYPE. Snap and Ortho settings are temporarily disabled for picking and restored even on cancel. No cloud lock or global selection setting is changed: ordinary clicks outside these tools can still select the cloud. Cloud density does not need to be reduced for these picks. Single-point picks ignore cloud returns and grid guides.

Each run creates a NEW job, with unique V-PNTC-GRID-<job>-POINTS, EXCLUDED and GUIDES layers. It does not update or replace earlier jobs.

The output consists of native DBPoint objects with actual XYZ. There are no COGO points, block conversions, labels or zero-elevation placeholders. Each point's lightweight XData records job ID, grid row/column, original sampled Z and review status. Supplemental manually added points append a 1070 flag of 1 to the existing tag; for those points OriginalZ means the first picked elevation, and status begins Adjusted. This added provenance survives later edits and exclusion/restoration. Between-intersection points use the reserved int.MinValue index for the opposite grid direction, so switching review directions never creates a fictitious faraway slice. Use this updated DLL when resuming jobs containing added points. A cloned boundary on GUIDES stores the job frame, units and source surface handle. Retain that tagged boundary so CGREVIEW can resume after reopening the drawing.

The nonplot GUIDES layer contains the boundary copy and bounding grid lines at Z=0. Guides can extend beyond a concave boundary; generated POINTS are clipped to the actual boundary. The starter accepts only straight-sided, non-self-intersecting horizontal closed 2D polylines. Arcs and nested/xref boundaries are rejected.

The prototype stops if the bounding grid would exceed 10,000 candidates. Use small tiles for testing. Native surface out-of-range nodes are skipped and counted. Sampling is from the existing TIN, not a direct laser return. Surface interpolation may bridge sparse or missing cloud data even where a valid TIN elevation exists; those cases still need visual review.

## Review reference and restoration

Review clones only the cloud entity/reference, sharing the existing definition/data; it does not copy or edit the RCP/RCS files. It temporarily hides the selected original cloud, crops only the tagged review copy, and sets an orthographic side view with front/back clipping centered on the row. Original cloud crops remain untouched. Review uncrops the COPY before each strip, so its original crop restrictions do not limit the slice.

Hide other attached clouds before review. The selected cloud must be visible on a thawed/unlocked layer. Keep hardware acceleration enabled. Row navigation replaces the crop on the same review copy rather than accumulating copies. Snap settings are temporarily disabled for exact crop corners and then restored.

IMPORTANT: Run CGEND before saving, closing, switching to a different review drawing, or removing a cloud reference. Do not manually erase the review copy or source during review. CGEND can also identify a saved tagged review copy after reopening and restore its source visibility; the earlier UCS/view is only available while the in-memory review session remains open. If recovery cannot verify the original cloud handle and shared cloud definition, it stops without erasing anything. A recovery after reopen may require a manual PLAN/zoom.

Undo of geometry edits and recovery must be tested in the host. If Undo removes review objects, run CGEND and restart CGREVIEW. Do not rely on the in-memory row index surviving Undo. Closing/reloading the DLL does not preserve row index; select any generated point to start a new review.

If generated points are hard to see, use the drawing's usual PTYPE/PDMODE/PDSIZE controls; the starter does not change these drawing-wide settings. Row framing uses the initial generated elevation range and no vertical exaggeration. You can zoom/pan within a slice; stepping resets the frame.

## Final surface handoff

Finish review with CGEND. Build the final TIN from your breaklines plus ONLY the desired job's POINTS-layer DBPoints. Explicitly exclude EXCLUDED, GUIDES and review objects from the surface selection. Moving a point to EXCLUDED does not automatically remove it from an already-existing surface definition; create/rebuild/reselect the final surface deliberately after review.

The temporary sampling surface remains separate. CGRID is the only elevation-sampling operation. Navigation never projects corrected points back onto it. No ground/grid reduction or coordinate-system transformation is applied.

## Build-reference correction

The follow-up picker fix removes the Editor.PointToScreen dependency on System.Windows.Point / WindowsBase that caused CS7069 in the WinForms build. Projection now uses AutoCAD geometry and SCREENSIZE. No WPF enablement, framework change or additional deployment dependency is required. The revised host code compiles with core, WinForms and Autodesk references only; verify the full solution build and live picks in both plan and rotated section views.

## Verification and first trial

Production geometry tests: rotated and large-coordinate frames, concave/boundary inclusion, self-intersection rejection, negative indices, caps, row/column strip thickness and empty grids. 25/25 checks passed. New host code and its actual existing crop/view helpers compile against installed Autodesk assemblies. Full solution build, attached-cloud cloning/cropping, display clipping, large-coordinate cloud transformations, live editing and Undo remain native acceptance tests. The relative-Z/point-only selection revision compiles with the actual crop/view helpers; screen picking and cancellation remain host acceptance checks.

Build:
dotnet run --project .\Tests\CloudGrid.Tests\CloudGrid.Tests.csproj
dotnet build .\CLV_CivilTools.slnx -c Debug

Load the newest randomized development DLL, then:
1. Draw a small closed rectangle on a copy of the project. Run CGRID using the temporary cloud-derived TIN. Use 1.0 spacing in a feet drawing.
2. Inspect several POINT properties. Compare XYZ to the source TIN, including boundary nodes. Check no points appear outside the chosen boundary. Exercise a surface hole/out-of-range area.
3. Run CGREVIEW; select a generated point, the attached cloud, Rows and 0.5 total thickness. Confirm a six-inch band and a side-on elevation view at correct project coordinates.
4. Click next/back several times. Verify cloud crop, generated POINT visibility and row correspondence. Test Columns and a rotated grid in separate trials.
5. In the rotated section, run CGMOVEZ, pick one point and press Enter, then enter -0.04 in a feet drawing. Verify actual WCS X/Y unchanged and Z reduced by 0.04 in Properties. Use +0.04 to restore. Repeat with Window and several points, including an excluded point; verify original sampled Z/status metadata and exclusions. Navigate away/back and check corrections persist. Also test CGSETZ absolute elevation.
   Test picks through a dense cloud without changing cloud density. Test initial CGREVIEW picking, CGSETZ, CGTOGGLE and CGSELECT. Test Escape during point/window selection and offset entry: no points move and snap/Ortho settings restore. In CGPLAN, select points across multiple rows of the current job. After CGEND, test CGMOVEZ/CGSELECT in plan. Hide/freeze/lock a point layer and confirm its points are ineligible; other jobs/off-slice points must remain unchanged. Test zoomed/rotated views and overlapping projected centers.
6. Exclude and restore a point with CGTOGGLE. Check its layer/color and original Z metadata. Accept a row.
7. Run CGEND. Verify original cloud crop/visibility and UCS/view are restored; generated points remain. Save/reopen and restart review by picking a generated point.
8. On a separate disposable copy only, save while reviewing, reopen and run CGEND to verify recovery before trusting that path.
9. Test command cancellation and Undo. Check existing points, breaklines, source TIN, cloud data and unrelated clouds remain unchanged.
10. Return the full F2 log plus a screenshot of a slice showing the ground returns and generated points. This is the main acceptance check before larger-area use.

Full-cloud recovery acceptance: after CGEND, run CGFULLCLOUD and Yes. Verify cropped display is restored and view clipping off, points/metadata unchanged; test cancellation, multiple cloud numbering, a locked cloud, Undo and an active review (must require CGEND). Pre-review crops are deliberately removed only by this explicit command.

Add-point / marker acceptance: use a drawing copy with 5- or 10-foot spacing. In CGSECTION add a missed crown in both Cloud and Visual modes. Confirm the NODE glyph when snapping; verify the new point retains the chosen WCS Z, its along-line station, and lies on the exact reviewed row/column. Try a rotated/large-coordinate grid, outside-boundary clicks, duplicate XY, cancellation after one committed point and an added point at a grid intersection. Edit/exclude/restore additions, navigate, save/reopen after CGEND, and switch Rows/Columns: off-intersection additions must not create fake opposite-direction slices. Confirm 1% circular/cross markers face the side view, follow zoom, refresh after edits and Undo, disappear in plan/end/other documents, and do not appear in saved drawings or final surfaces. RESIZE POINTS should change review overlays without changing native point normals or XYZ; without review it should open PTYPE. Undo and cloud snapping remain native acceptance tests.

## Persistent sliced-display diagnostic

If CGEND/CGFULLCLOUD leave a narrow cloud with no ribbon crop indication, run CGCLOUDDIAG while the problem is visible. Copy the complete F2 block from CLOUD DISPLAY DIAGNOSTIC R1 through END CLOUD DISPLAY DIAGNOSTIC. It is read-only and reports both editor and stored viewport clipping, live sectioning, all model-space cloud IDs/crop flags and extents. Existing restore already saves/restores the initial view; this log is needed before attributing the symptom to a particular display restriction. No limit-box or scan/region settings are reset. Nested/xref clouds are not enumerated.
