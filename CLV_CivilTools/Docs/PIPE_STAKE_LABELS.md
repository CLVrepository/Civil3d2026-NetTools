# Pipe information labels at staking check shots

Run **UFLS-PIPE-STAKE-LABEL** from Q1 > CHECK > INFO > PIPE STAKE LABEL, or Q1 > TOP CHECK > LABEL > PIPE STAKE LABEL.

Select a Civil gravity pipe/box, optionally select a Civil alignment, then select the first check point. Set the text height (model drawing feet; default 0.1) after that point, then place the label. Continue selecting points and placing labels with that height. Enter finishes; Esc cancels the current command.

- COGO labels/markers and AutoCAD POINT objects supply their actual WCS coordinates, not a dragged label position.
- Number selects a COGO point by its exact number.
- Location uses a manually picked WCS-transformed location with no point identity.
- Pipe and Alignment change those selections between shots. Height requests a new text height after the next valid point.

## Label contents

Each MText label contains the check point identity, pipe name, native inside size, material/family, station or along-pipe distance, shot offset, invert, outside top elevation and modeled wall/slab thickness. Slope and start/end inverts are omitted. A selected point's elevation is not treated as a measured pipe top or a staking cut/fill value.

With an alignment selected, **PIPE STA [alignment]** is evaluated at the nearest point on the selected pipe centerline, not at the offset stake or text insertion point. Civil's station-equation formatter supplies the station string. Outside the alignment's supported range, station is UNAVAILABLE with a warning; the routine does not fabricate a project station.

Without an alignment, **PIPE DIST** is the distance from this pipe's start. It is not alignment/project stationing.

**OFFSET FROM PIPE CL** is horizontal distance from the check shot to the nearest pipe location. Straight pipes use a finite-line projection; curved pipes use the actual finite plan arc with native endpoint orientation. Arc-center/equal-endpoint ambiguity or invalid native geometry stops that label.

A shot projecting beyond the pipe uses its nearest end. The label explicitly changes OFFSET to **DIST TO PIPE END** and adds a red **BEYOND PIPE LIMITS - NEAREST END VALUES** warning. The existing info-dialog screenshot's clamp warning must not be silently hidden.

## Elevation and unit basis

This command requires Civil drawing units Feet. Pipe inside dimensions and native wall thickness are read in drawing units. Circular diameter and wall thickness are displayed in inches; a rectangular box is displayed as width x height in feet. A native width of 10 feet therefore displays 10 feet, not 10 inches. No magnitude-based feet/inches guessing or catalog wall-thickness substitution is used.

At the projected pipe location, modeled centerline Z is interpolated by plan distance along the line/arc. Invert is centerline minus half the native inside height. Outside top is invert + inside height + modeled wall thickness. This assumes the native wall parameter represents the modeled roof thickness; confirm the Civil part's parameter against the box/slab design. If wall thickness is unavailable/zero/non-finite, WALL and TOP remain UNKNOWN. An inside crown is not silently labeled as an outside roof.

There is no ground/grid, datum or unit transformation, and no network/point elevation modification. Existing UFLS-PIPE-INFO and UFLS-PIPE-TOP-CHECK calculations are unchanged.

## Snapshots and refresh

Labels use CLV-Non Anno on V-SURV-CHCK. Turn on/thaw/unlock the layer and load the text style before use. Annotation is placed in model-space WCS XY at Z=0; source points and pipes keep their elevations.

Labels are snapshots, not live Civil labels. Run **UFLS-PIPE-STAKE-REFRESH** (TOP CHECK > REFRESH PIPE STAKE LABEL) after changing a pipe, point or alignment. Refresh re-reads the original sources and keeps the label insertion position, height and style. A location-only label reuses its original picked location.

Each label retains a versioned Xrecord with source handles, drawing fingerprint, sampled WCS/pipe-foot XY, rendered text and measurement UTC. Missing/deleted/wrong-type sources and copied labels from a different drawing refuse refresh. Old sources are not replaced by nearest-neighbor matching.

This metadata is separate from the existing top-check label/table format. These labels are not automatically included in the older comparison table or tolerance tools.

## Catalog metadata failure handling (2026-11-18 follow-up)

The workstation error "Part Family is invalid" occurred after a point was selected, during measurement. The original command directly read PartFamilyName for its material text. That optional getter is now guarded, along with description/size/name and wall-thickness display reads. A recognized valid family takes precedence; description or size text can supply a material code if the family is unavailable. If no supported metadata is readable, material is UNKNOWN. No catalog repair or material/geometry substitution is performed.

A failed wall-thickness getter leaves outside top UNKNOWN. Required cross-section, endpoint, plan-arc and inside-dimension reads retain strict validation and identify the specific failing field and native error. A failure of required geometry still stops that label. This protects reporting from unavailable optional catalog data; it does not claim an invalid Civil part/catalog has been repaired.

Initial prompt order is pipe, alignment, point, text height, label location. The point keyword list appears once. Current pure regressions: all 43 passed, including throwing optional getters, fallback material sources and required-field diagnostics. Live re-test of the reported drawing remains required.

## Validation

The production-linked host-independent tests exercise line/arc projection, clockwise/reversed/wrapped arcs, endpoint clamps and ambiguous picks, centerline/invert/outside-top arithmetic, missing wall data, invariant text formatting, MText escaping and requested field selection.

Targeted native-reference compilation uses the real installed AutoCAD/Civil assemblies and a signature-only stub for the unchanged LayerStandards dependency. It is not a full plugin build or a deployable DLL.

Workstation acceptance:
1. Place beside a real COGO shot by clicking its dragged label; repeat with Number, an AutoCAD POINT and Location. Confirm actual source XY is used.
2. Compare a straight 10 x 6 RCB: native width/height, invert, modeled 11-inch wall and outside top. Confirm the part's actual slab parameter.
3. Pick near a curved pipe midpoint with a known 20-foot radial offset. Confirm the perpendicular foot/arc station and offset against Civil geometry.
4. Test a location past each pipe end: nearest-end warning and DIST TO PIPE END must appear.
5. Select the project's pipe alignment. Check a known station, a station equation and an out-of-range case. Confirm station is at pipe foot, while offset is shot-to-pipe CL.
6. Edit a source pipe/point and refresh. Confirm new values, preserved label placement/style/height and no point/network changes.
7. Verify missing thickness produces UNKNOWN outside top; test missing sources, locked annotation layer and different-drawing copied labels.
