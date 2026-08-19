# Subdivision Linework Tools

These are first-pass drafting helpers for generating subdivision linework from imported road centerlines. They are intended for review/testing and will continue to evolve.

## SITE SETTINGS

Command aliases:

- `CLV_SUBDIV_SITE_SETTINGS`
- `SUBDIV-SITE-SETTINGS`
- `SUBDIVISION-SITE-SETTINGS`

Workflow:

1. Open the settings dialog.
2. Set project defaults:
   - Typical road width.
   - Cul-de-sac radius.
   - Cul-de-sac tie-in radius.
   - Curb return radius.
3. Values are stored for the current CAD session and reused by ROADS, CUL-DE-SAC, and INTERSECTION.

## ROADS

Command aliases:

- `CLV_SUBDIV_ROADS`
- `SUBDIV-ROADS`
- `SUBDIVISION-ROADS`
- Legacy aliases still supported: `CLV_SUBDIV_ROAD_EDGES`, `SUBDIV-ROAD-EDGES`, `SUBDIVISION-ROAD-EDGES`

Workflow:

1. Run SITE SETTINGS first to confirm the typical road width.
2. Run ROADS.
3. Select all road centerline objects that use the same width.
4. The tool offsets both sides of each selected centerline by half of the typical road width.
5. Road-edge geometry is created on `V-MAPL-ROAD-ROW`.

Notes:

- Source centerlines are not modified.
- Cul-de-sacs are no longer part of ROADS and are handled by the separate CUL-DE-SAC tool.
- Continuous centerline polylines usually produce the cleanest edge results because AutoCAD offsets the whole polyline as a joined object.
- If separate line/curve pieces are selected, the created offsets may need manual cleanup where objects meet.

## CUL-DE-SAC

Command aliases:

- `CLV_SUBDIV_CULDESAC`
- `SUBDIV-CUL-DE-SAC`
- `SUBDIV-CULDESAC`
- `SUBDIVISION-CUL-DE-SAC`

Workflow:

1. Opens a cul-de-sac dialog populated from SITE SETTINGS.
2. Adjust cul-de-sac radius, tie-in radius, or road width for this bulb when needed.
3. Pick the cul-de-sac center / centerline endpoint.
4. Select the existing road-edge lines to clean, or press Enter to auto-use nearby `V-MAPL-ROAD-ROW` linework.
5. The tool creates a cul-de-sac bulb circle on `V-MAPL-ROAD-ROW` and trims/extends selected/nearby road-edge lines to the bulb.

Notes:

- This pass keeps the bulb as a full circle and cleans the straight road-edge lines so they terminate at the bulb.
- The tie-in radius is still stored/reported for future tangent tie-in refinement.

## INTERSECTION

Command aliases:

- `CLV_SUBDIV_INTERSECTION`
- `SUBDIV-INTERSECTION`
- `SUBDIVISION-INTERSECTION`
- Legacy aliases still supported: `CLV_SUBDIV_INTERSECTION_RETURNS`, `SUBDIV-INTERSECTION-RETURNS`, `SUBDIVISION-INTERSECTION-RETURNS`

Workflow:

1. Run SITE SETTINGS first to confirm road width and curb return radius.
2. Run INTERSECTION.
3. Select two straight road centerline LINE objects.
4. Select the existing road-edge lines to clean, or press Enter to auto-use nearby `V-MAPL-ROAD-ROW` linework.
5. The tool creates return arcs on `V-MAPL-ROAD-ROW`, splits long crossing edge lines where needed, and trims/extends edge endpoints to the return tangent points.

Notes:

- This is still a first-pass straight-centerline intersection helper.
- It is intended for two-road intersections where the generated road edges are already present.
- Curved-road, skewed, variable-width, and multi-leg intersection cleanup may require later revisions.

## LOT LINES

Command aliases:

- `CLV_SUBDIV_LOT_LINES`
- `SUBDIV-LOT-LINES`
- `SUBDIVISION-LOT-LINES`
- Legacy aliases still work: `CLV_SUBDIV_LOT_OFFSET`, `SUBDIV-LOT-OFFSET`, `SUBDIVISION-LOT-OFFSET`

Workflow:

1. Select the source lot line to offset repeatedly.
2. Pick the furthest point / stop location in the direction the lot lines should be generated.
3. Enter the typical lot spacing / offset distance.
4. Optionally select trim/extend boundary lot lines near both ends.
5. The tool creates repeated lot line offsets every specified distance until it reaches the selected stop point/location.

Notes:

- The stop point determines the offset side and approximate maximum distance.
- Straight line offsets can be trimmed/extended to selected nearby lot/boundary lines.
- Arc/polyline offsets are supported as basic repeated offsets, but curved road lot fan layouts and cul-de-sac lot tools are still design-in-progress.

### 2026-07-01 refinement notes

**CUL-DE-SAC** now uses a centerline-first workflow. Select the cul-de-sac road centerline, then pick the endpoint/bulb center. The tool searches for the nearby generated road-edge lines on `V-MAPL-ROAD-ROW`, trims them, trims the bulb circle into a bulb arc, and creates tie-in fillet arcs using the cul-de-sac tie-in radius stored in SITE SETTINGS.

**INTERSECTION** now asks for the `MAIN` road centerline first, then the `INTERSECTING` road centerline. It automatically detects the nearby generated road-edge lines, trims/splits the main road edge opening where needed, and creates curb return arcs using the stored curb return radius.

These tools are still design-in-progress and currently target straight centerline / straight road-edge cases first.

## 2026-07-01 refinement notes

### CUL-DE-SAC
The cul-de-sac routine now attempts to keep the bulb tie-in fillets on the correct left/right road-edge side. The intended result is: offset road edges stop at the bulb transition, the bulb circle is trimmed to a bulb arc, and the tie-in radius connects the straight road edges to the bulb arc without crossing inward.

### INTERSECTION
The intersection routine is now MAIN-road / INTERSECTING-road oriented. Pick the main through-road centerline first, then pick the intersecting road centerline near the side that joins the main road. The tool trims the main road edge opening first, then creates the two curb return arcs and trims the intersecting road edges to the radius tangent points.


## 2026-07-01 - Subdivision cleanup v4
- CUL-DE-SAC: revised tie-in construction to use a deterministic tangent/radius method. The tool offsets each detected road edge outward from the selected centerline side, creates a fillet circle tangent to that road edge and the cul-de-sac bulb, trims road edges to tangent points, and exports the remaining bulb arc instead of leaving the full circle.
- INTERSECTION: revised MAIN/INTERSECTING cleanup to use the actual generated road-edge lines for tangent/tangent return creation. The main road edge opening is trimmed between the two return tangent points, and intersecting-road edges are trimmed back to the return tangency points.
- This pass is still focused on straight centerline / straight road-edge cases first.


### Intersection cleanup v6 notes
The INTERSECTION tool now creates the two curb returns with angle-bisector tangent/tangent fillet geometry. The intended sequence is: select MAIN centerline, select INTERSECTING centerline near the side that joins, detect the main road edge on that side, compute the two edge corners, trim the main road edge opening between the two return tangent points, then trim the intersecting road edges back to the returns. This pass is still targeted at straight-centerline / straight-road-edge T-intersections.

### Curved centerline support pass
- **ROADS** can offset selected line, arc, or polyline centerlines through AutoCAD curve offsets.
- **CUL-DE-SAC** now allows a line, arc, or polyline centerline selection. The tool uses the local tangent at the picked bulb endpoint to find matching road-edge curves.
- **INTERSECTION** now allows line, arc, or polyline centerline selection. For curved roads, it uses the local tangent at the centerline intersection to create first-pass returns.
- This is intended for simple curves and tangent connections first. Compound curves, reverse curves, and highly skewed curved intersections should be reviewed after creation.

### INTERSECTION curved-road refinement note
The INTERSECTION tool now uses the selected MAIN and INTERSECTING centerlines to find the join point, then finds the actual generated road-edge curves near the join. Return arcs are now based on the intersection and local tangent of the actual road-edge line/arc geometry rather than only the theoretical centerline offset frame. This should improve simple curved centerline intersections, skewed intersections, and non-perpendicular joins. Complex compound/reverse curve intersections may still require manual cleanup.

### Curved-road intersection note
For curved centerlines, run ROADS again after this update. Offset road-edge polylines are now exploded into individual line/arc segments so INTERSECTION can trim and fillet the actual edge pieces. This helps avoid small gaps where a return arc meets a curved road edge.
