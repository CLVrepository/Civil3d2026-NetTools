# 3D polyline from measured vertex points

Command: `PCT3DFROMPOINTS` (alias `PL3DFROMPOINTS`).
Palette: Q3 > ROADWAY > MOVE POINTS > 3D POLYLINE FROM POINTS.

Use this after tracing a top/toe/flow line in plan, placing measured elevation
points from the point cloud, and moving those points to the trace's vertices.
The command creates a native AutoCAD 3D polyline directly from that line and
those points. It does not create, query or drape onto a surface, or snap to the
cloud itself. Existing PCT9 Move Points to Vertex remains unchanged and currently
moves COGO points; this new command can read both COGO and AutoCAD POINT objects.

## Workflow

1. Finish any point editing and select the traced lightweight 2D polyline.
2. Enter an XY matching tolerance in drawing units, default 0.001. This is a
   coordinate tolerance, not a screen pick radius or point-cloud search radius.
3. Select only the elevation POINTs/COGO points intended for that trace. A window
   selection can collect them while the selection filter ignores cloud objects.
4. Choose Keep (default) or Replace for the original 2D line.
5. The command checks every vertex. Missing/conflicting matches are reported by
   1-based vertex number and WCS easting/northing. Nothing is created or replaced
   unless every vertex has an unambiguous measured elevation.
6. Review the matched vertex count and Z range, then answer Yes to create the
   3D line. Enter/No/Esc leaves the drawing unchanged.
7. Inspect the selected 3D result. Add it to your final surface as a breakline
   when ready. Source points are retained and never moved or edited.

The output keeps the source vertex order, exact WCS X/Y, and open/closed state.
Each output Z is read from its matching point. It copies standard entity display
properties such as layer/color/linetype. It is a new object with a new handle;
source custom data, extension dictionaries and associations are not transferred.
Keep preserves the original for comparison. Replace erases only that original,
after output vertex/closure readback succeeds, in the same transaction as creation.
Native Undo behavior still requires acceptance testing.

## Matching and limitations

- Only a direct model-space lightweight 2D polyline with straight segments is
  supported. Heavy 2D polylines, blocks, xrefs, 3D source lines and arc segments
  are refused. Arcs are not silently replaced by straight chords.
- The trace must be visible on an on, thawed, unlocked layer. Its width/thickness
  are not carried into the 3D centerline; a message identifies that situation.
- Input POINT and COGO locations are read in WCS. COGO point label locations are
  not used. Point objects on locked layers can supply elevations because they
  are only read. Hidden/off/frozen and grid-tagged Excluded points are skipped,
  as are COGO points without a finite elevation and non-finite coordinates.
- Matches use a circular XY radius. A point outside it is never used as a
  nearest fallback. Extra selected points outside vertex radii are unused.
- Multiple candidates within a vertex radius must agree in elevation within
  1e-7 drawing unit. Otherwise conversion stops, even if one candidate is closer.
  For agreeing candidates, the closest is selected; exact distance ties use
  deterministic selected-point identity. Elevations are never averaged.
- One measured point cannot silently supply two different vertices. If it would
  be reused, those vertices are reported for attention. The routine does not
  guess a different assignment among neighboring duplicate shots.
- Consecutive duplicate XY vertices, including the closing segment, are refused.
  A tagged grid-job boundary cannot be replaced; retain it with Keep or use a
  separate trace. No surfaces or existing breakline definitions are edited.
- This is a snapshot conversion. Later point changes do not update the 3D line.
  No lower-return/bush filtering or intermediate vertex interpolation is included.

## Validation

The command, actual matching helper, edited Q3 palette and actual
PalettePositionHelper compiled against installed Autodesk/Civil 3D 2026 references
with zero warnings/errors. Full solution and native Civil 3D testing are pending.

`Tests/VertexElevations.Tests` links the production matching helper and covers
survey coordinates, selection order, measured zero/negative Z, radial tolerance,
missing points, conflicting/coincident elevations, stable duplicate handling,
point reuse, invalid numeric data and a 10,000-vertex trace. Attempted execution
was blocked during dependency restore by restricted NuGet access in this workspace;
no passing executable-test result is claimed. On an approved development host:

```powershell
dotnet run --project .\Tests\VertexElevations.Tests\VertexElevations.Tests.csproj
```

Native acceptance on a drawing copy:

1. Use a three-vertex open trace and points with distinct measured Z, including
   AutoCAD POINTs, COGO points and a mixed selection. Check each output XYZ in
   Properties/LIST and verify exact source X/Y, point Z and vertex order.
2. Repeat with a closed trace and non-world UCS/source OCS, large survey
   coordinates, near-boundary tolerance and points on locked layers.
3. Remove one matching point; add conflicting same-XY/different-Z points; test
   one point near two vertices. Confirm no partial output or source erasure.
4. Test Keep/Replace, Yes/No/Enter/Esc at each prompt, and unique K/R/Y/N keyword
   accelerators plus full words. Keep must preserve the source; Replace must
   affect only the selected original. Verify point numbers/styles/tags unchanged.
5. Confirm refusal of arcs, degenerate segments, unsupported/locked source lines
   and replacement of a tagged grid-job boundary. Check excluded/hidden points
   and unset COGO elevations are skipped.
6. Undo/Redo both Keep and Replace, confirming source/3D line/point identities
   and that only this command's intended edits reverse. Add the resulting 3D
   line to a disposable final surface as a breakline and verify the expected Z.
