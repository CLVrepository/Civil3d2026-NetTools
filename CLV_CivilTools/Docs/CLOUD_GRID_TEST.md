# Ground grid / cloud slice starter

This is a first native Civil 3D trial, not a production extraction workflow. Start on a disposable drawing copy with a roughly 20 x 20 test area. The initial test uses one attached RCP/RCS point cloud and an existing temporary TIN derived from that cloud. It does not build the temporary TIN, classify raw returns, or build the final deliverable surface.

## Commands and Q3 buttons

Q3 > ROADWAY > GROUND GRID - TEST:

- CGRID / CREATE GRID: select temporary TIN and closed straight-sided horizontal polyline; pick grid origin and X-axis direction; enter spacing in Civil drawing units. Default 1 means one foot in a feet drawing or one meter in a meter drawing. Check the displayed units. Confirm point count with Yes; No is the default.
- CGREVIEW / REVIEW GRID: pick one generated AutoCAD POINT and the cloud. Choose Rows or Columns and total slice thickness. Defaults are 0.5 feet or 0.1524 meters (six inches total, three inches each side).
- CGNEXT / CGBACK: next/previous populated grid row or column; stops at the ends. No elevations are resampled.
- CGPLAN / CGSECTION: original overview versus current side-on review slice. The overview retains the cloud's current slice crop; END fully restores the original display.
- CGSETZ: pick a generated POINT from the current job and slice; enter a new absolute Z. XY stays fixed and original sampled Z stays in metadata. This starter uses numeric elevation entry, not automatic cloud-return snapping or screen-picked heights.
- CGTOGGLE: exclude/restore a current-row generated point. Excluded points are red and remain recoverable on the job's nonplot EXCLUDED layer.
- CGACCEPT: mark remaining Sampled points in the current row Accepted; adjusted/excluded states are retained.
- CGEND / END / RESTORE: remove tagged review cloud copies and restore original cloud visibility. During the current session it also restores the original UCS/view. Grid points, corrections, exclusions and metadata remain.

## What is created

Each run creates a NEW job, with unique V-PNTC-GRID-<job>-POINTS, EXCLUDED and GUIDES layers. It does not update or replace earlier jobs.

The output consists of native DBPoint objects with actual XYZ. There are no COGO points, block conversions, labels or zero-elevation placeholders. Each point's lightweight XData records job ID, grid row/column, original sampled Z and review status. A cloned boundary on GUIDES stores the job frame, units and source surface handle. Retain that tagged boundary so CGREVIEW can resume after reopening the drawing.

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

## Verification and first trial

Production geometry tests: rotated and large-coordinate frames, concave/boundary inclusion, self-intersection rejection, negative indices, caps, row/column strip thickness and empty grids. 25/25 checks passed. New host code and its actual existing crop/view helpers compile against installed Autodesk assemblies. Full solution build, attached-cloud cloning/cropping, display clipping, large-coordinate cloud transformations, live editing and Undo remain native acceptance tests.

Build:
dotnet run --project .\Tests\CloudGrid.Tests\CloudGrid.Tests.csproj
dotnet build .\CLV_CivilTools.slnx -c Debug

Load the newest randomized development DLL, then:
1. Draw a small closed rectangle on a copy of the project. Run CGRID using the temporary cloud-derived TIN. Use 1.0 spacing in a feet drawing.
2. Inspect several POINT properties. Compare XYZ to the source TIN, including boundary nodes. Check no points appear outside the chosen boundary. Exercise a surface hole/out-of-range area.
3. Run CGREVIEW; select a generated point, the attached cloud, Rows and 0.5 total thickness. Confirm a six-inch band and a side-on elevation view at correct project coordinates.
4. Click next/back several times. Verify cloud crop, generated POINT visibility and row correspondence. Test Columns and a rotated grid in separate trials.
5. Adjust a point with CGSETZ. Check XY unchanged and Z correct; move away and back to verify persistence.
6. Exclude and restore a point with CGTOGGLE. Check its layer/color and original Z metadata. Accept a row.
7. Run CGEND. Verify original cloud crop/visibility and UCS/view are restored; generated points remain. Save/reopen and restart review by picking a generated point.
8. On a separate disposable copy only, save while reviewing, reopen and run CGEND to verify recovery before trusting that path.
9. Test command cancellation and Undo. Check existing points, breaklines, source TIN, cloud data and unrelated clouds remain unchanged.
10. Return the full F2 log plus a screenshot of a slice showing the ground returns and generated points. This is the main acceptance check before larger-area use.
