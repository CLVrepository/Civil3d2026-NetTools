# Automatic 3D polylines from vertex elevation points

Command: `PCT3DFROMPOINTS` (alias `PL3DFROMPOINTS`).
Palette: Q3 > ROADWAY > POLYLINE TOOLS > 3D POLYLINE FROM POINTS.

Trace the top, toe or flow line in 2D and place your measured elevation points at
its vertices. This routine automatically finds the eligible AutoCAD POINTs and
Civil COGO points in model space; you do not select the point set. No temporary
surface, point-cloud resampling or manual point-by-point picking is required.

## Use

1. Select one or several straight-segment lightweight 2D polylines, then Enter.
2. Set a fallback XY tolerance in drawing units (default 0.001).
3. Choose Keep (default) or Replace for the original 2D traces.
4. Choose the 3D destination layer (default `V-DTM~GBRK`). It must already exist
   with your drawing's layer standards.
5. When keeping originals, choose their separate layer (default `V-PNTC-TRACE-2D`).
6. The command scans direct model-space POINTs/COGO points automatically. At each
   vertex, exact WCS XY matches take priority over every nearby non-exact shot.
   Only when there is no exact match does the circular tolerance search apply.
7. Review the matched line count, exact/fallback counts and Z range. Answer Yes
   to create all 3D lines. Enter/No/Esc cancels without changes.
8. Inspect the selected results. Add them to your final surface as breaklines
   when ready. The command never moves, erases or changes the elevation points.

The output keeps exact source vertex X/Y in WCS, source order and closure, and
reads Z from the matched measured point. The new 3D parent and its vertices are assigned to the chosen destination layer.
Color, linetype, lineweight, transparency and plot style on the 3D parent use
ByLayer rather than source overrides. Each result has a new handle; custom
metadata, extension dictionaries and external associations are not transferred.
Keep moves the selected originals to the separate retained-trace layer without
changing their geometry or other properties. Replace erases only those traces
after every output passes XYZ/count/closure/layer readback. All writes commit together.

## Layer destinations

- The default 3D layer is **V-DTM~GBRK**, confirmed by Mark. You can enter another
  existing local layer. The routine does not create or restyle a DTM layer;
  import/create a missing destination using your drawing's standards before retrying.
- With Keep, the original 2D traces move to **V-PNTC-TRACE-2D** by default. Enter
  another name to use a different retained-trace layer. It must differ from the
  3D destination. A missing retained-trace layer is created only after final Yes,
  using gray ACI 8 and nonplotting settings. Existing layer properties are preserved.
- Destination layers must be local, on, thawed and unlocked. The routine does not
  turn on/thaw/unlock layers. Source-point layers and current layer are unchanged.
- The last successful destinations are saved per drawing in the
  CLV_PCT3DFROMPOINTS_LAYERS_V1 named-object Xrecord. They return as editable defaults
  on the next conversion and after save/reopen. Cancel, No, missing matches or
  failures do not save new choices. Replace skips the retained-layer prompt and
  does not create a retained-trace layer.
- Creation, retained-source relocation/erasure, optional trace-layer creation and
  saved preferences share one transaction. Native Undo/Redo still needs live testing.

## Nearby lines and ambiguous shots

An exact shot at a vertex outranks non-exact points beside a neighboring trace,
even if those points are within the fallback radius and have different Z.
Multiple exact shots at the same XY must agree in Z within 1e-7 drawing unit.
If none is exact, all shots within the fallback radius must agree to that same
limit. Otherwise the vertex is reported as conflicting; the routine never
chooses the higher/lower shot or averages elevations.

For agreeing shots, closest XY wins, then stable scan identity breaks ties.
One shot cannot silently supply multiple different vertices within one trace;
such reuse is reported. Shared endpoints on separate traces may use the same
shot. Missing matches never receive an interpolated or zero fallback elevation.

Any invalid trace, missing match or ambiguity stops the entire selected batch.
The command reports source handles, 1-based vertex numbers and WCS E/N for up to
20 issues, plus the remaining count. Correct the points, turn off unwanted point
layers, reduce an overly broad fallback tolerance, or select a smaller trace
batch and retry. No partial 3D output or original-trace erasure occurs.

## Scope and limitations

- Direct model-space lightweight 2D traces with straight segments only. Heavy
  2D polylines, arc segments, blocks/xrefs and 3D source lines are unsupported.
  Arcs are refused rather than silently chorded.
- Traces must be visible on on/thawed/unlocked layers. Width/thickness becomes
  a 3D centerline, with a message when that applies. Non-world source OCS is
  resolved to WCS before matching.
- Eligible point objects are direct model-space AutoCAD POINTs or Civil COGO
  points whose entity is visible and layer is on/thawed. Locked point layers
  can be read. Grid-tagged Excluded points and non-finite/unset elevations are
  skipped. Block-contained points, cloud returns and label locations are not
  candidate points. Model-space points are scanned regardless of screen extent.
- Zero and negative measured elevations are valid. A COGO point with unset
  elevation returns NaN and is skipped.
- A tagged grid-job boundary cannot be replaced; use Keep or a separate trace.
- Existing PCT9 remains unchanged and moves COGO points only. Native POINTs can
  be read by this routine after being positioned at trace vertices by your workflow.
- This creates native 3D polylines, not Civil feature lines. No surface definitions,
  point-cloud crops/views or existing point-editing commands are changed.
- Conversion is a snapshot. Later point edits do not automatically update it.

## Validation

The command, layer helper, actual matcher, Q3 palette and PalettePositionHelper compile
against installed Autodesk/Civil 3D 2026 references with zero warnings/errors.
Full solution and live Civil 3D acceptance remain pending.

Tests/VertexElevations.Tests links production matching logic. Cases include
large survey coordinates, measured zero/negative Z, radial boundaries, exact
priority beside neighboring lines, coincident conflicts, fallback conflicts,
stable duplicates, source-point reuse, numeric validation and a 10,000-vertex
trace. Previous execution could not complete because this workspace's NuGet
restore access is restricted; no executable-test pass is claimed. Run on an
approved development host:

```powershell
dotnet run --project .\Tests\VertexElevations.Tests\VertexElevations.Tests.csproj
```

Native acceptance on a drawing copy:

1. Test AutoCAD POINTs, COGO points and mixed model-space data. Select only the
   traces: there must be no point-selection prompt. Check each output XYZ.
2. Use two close top/toe traces with different Z and exact vertex shots within
   each other's tolerance. Confirm each uses its own exact shot. Check shared
   endpoints across separate traces.
3. Remove an exact shot; verify an unambiguous nearby shot supplies the fallback.
   Add conflicting nearby shots with no exact shot, or different-Z exact shots.
   All selected traces must remain unchanged when any vertex fails.
4. Test several traces together, Keep/Replace, final Yes/No/Enter/Esc, valid K/R/Y/N
   accelerators and full keywords. Verify points/COGO identifiers/styles/tags unchanged.
5. Check open/closed traces, a non-world UCS/OCS, large coordinates, locked point
   layers, hidden/off/frozen/Excluded points, unset COGO elevation and neighboring
   draft points on unwanted layers. Turning those layers off must exclude them.
6. Confirm refusal of arcs, degenerate segments, locked source lines and replacement
   of tagged job boundaries. Check the issue count and the entire-batch rollback.
7. Undo/Redo Keep and Replace; verify all intended outputs/originals and no point
   edits. Test adding resulting 3D lines to a disposable surface as breaklines.

Additional layer acceptance checks:

1. Convert a Layer 0 trace with source color/linetype overrides. Confirm the
   3D parent and all vertex subentities use V-DTM~GBRK; parent display properties
   must be ByLayer. Inspect a kept 2D trace on the separate retained layer.
2. Test Keep with a new trace layer and with an existing custom trace layer.
   Existing layer properties must remain unchanged. Test custom 3D/2D names.
3. Cancel at each layer prompt or answer No. No layers, source changes, output
   geometry or saved-preference updates should result. Test missing/conflicting
   point matches with newly typed trace-layer names.
4. Test missing DTM, locked/off/frozen/dependent destinations and choosing the
   same destination for 3D/2D. All must refuse safely, without layer flag changes.
5. Replace must erase only selected originals and skip 2D-layer creation.
   Verify saved defaults independently in two drawings and after save/reopen.
6. Undo/Redo successful Keep and Replace, including original layers, created
   outputs/trace layer and saved layer choices. Point objects must remain untouched.
