# Sewer GIS preparation

`CLV-GIS-SSWR-GIS` is the managed **GIS PREP - ALL** button under Q2 > GIS > GIS TOOLS > ADD TO DATABASE > SEWER. The section now has the same two labels as Storm: **CREATE GIS DRAWING**, then **GIS PREP - ALL**.

## Operator workflow

1. In the original sewer survey/network drawing, choose SEWER > CREATE GIS DRAWING. The shared setup command creates Blank, matches source units/CRS, copies verified survey geometry and offers a new filename/save/reopen. Its separate [instructions](GIS_NEW_DRAWING.md) still apply.
2. In the prepared GIS drawing, run MAPIMPORT yourself, select the sewer SDF and load the matching IPF printed by setup. Confirm native `Pipes` and `Structures` Object Data are attached. Preparation does not choose or import an SDF.
3. On a disposable copy for the first trial, choose SEWER > GIS PREP - ALL. No geometry selection is required. The command reports its `2026.10.08-S1` preflight and either converts the complete supported batch or reports review handles before conversion.
4. Inspect the result and OD, then save. A second unchanged run should verify its recorded output without adding walls or deleting more objects.

## Geometry and layer rules

InsideDiameter uses the existing sewer LISP convention of feet: `0.666667` is approximately 8 inches, and `1.0` is exactly 12 inches. Drawing insertion units must identify Feet or US Survey Feet. No coordinate/unit conversion is performed.

| Output | Rule | Layer | Linetype | ACI |
|---|---|---|---|---|
| Pipe below 12 inches | Retained original LINE, clipped | `C-SSWR-PIPE-E` | HIDDEN2 | 106 |
| Pipe at least 12 inches | Retained original centerline, clipped | `C-SSWR-PIPE-CNTR-E` | CENTER2 | 9 |
| Both large-pipe walls | Offset original path by half InsideDiameter, independently clipped | `C-SSWR-PIPE-E` | HIDDEN2 | 106 |
| Manhole outer circle | Actual evaluated visible outer circle | `C-SSWR-STRC-E` | HIDDEN3 | 106 |
| Manhole inner circles | Actual evaluated visible inner circles | `C-SSWR-STRC-INNR` | HIDDEN4 | 10 |

These are the existing managed sewer layer definitions used with the legacy workflow. Entity-level overrides remain overrides. A required missing linetype is not silently replaced by Continuous.

Every processed pipe line ends at the connected **outer** circle. This deliberately changes the old wall-only inner-boundary behavior: small-pipe lines and large-pipe centerlines are trimmed too. Walls use exact nonradial line/circle intersections, not a radius inset or a polygon approximation. The original Pipes record values, including Length and invert fields, remain unchanged; they describe the imported network, not a recalculated visible clipped length.

## Identity, preservation and reruns

- Native Structures identity selects sewer manholes. Names such as `SSMH`/`SSWR` and supported barrel/frame parts establish sewer evidence; explicit storm conflicts require review. One structure point maps to one `UFLS-GIS-MH-CIRCULAR`; no storm-style `-JS` pairing is assumed.
- Matching uses unique source/block/circle anchors within 0.10 XY units, compatible elevations, and the reference's current evaluated visible geometry with complete transforms. Known survey outer/inner layers, including `~~` suffixes, identify ring roles. Nonuniform transforms, extra unsupported visible geometry and ByBlock-dependent circle appearance require review.
- Pipe StructureStart/StructureEnd resolve to unique verified structure identities. Either LINE direction is accepted when it proves one connection orientation. Horizontal LINEs at a common nonzero elevation are supported when their points/circles agree. Varying Z, tilted geometry, curves/polylines/laterals, gaps needing extension, tangent-only contacts and consumed intervals are retained for review; no flattening occurs.
- Full typed native OD is preserved on the retained source LINE and copied/read back on both large walls and the manhole outer circle. Inner circles receive no invented OD. Exact recognized null/STUB endpoints remain at their original coordinates.
- Only verified matched source blocks, eligible imported points and uniquely owned `UFLS_MH_MARK` instances are retired. Source points with additional XData or an extension dictionary stay in place and are reported by handle. Their complete OD also appears on the outer circle. Unknown nested/leaf metadata prevents destructive block conversion.
- One transaction covers geometry, OD proof, ownership archive and source cleanup. Planning reviews stop the batch before those changes. A later failure aborts that transaction. Native Map rollback and one-step UNDO still require host testing.
- A versioned drawing-local archive records final entity references, geometry/appearance snapshots, complete OD fingerprints, original pipe geometry and retired structure identity. Readback precedes cleanup and verification repeats afterward. Missing/edited outputs or new untracked sewer batches require review; they are not silently repaired or offset again. Initially excluded storm inputs remain excluded on rerun. Retained null/metadata-bearing points are recorded explicitly.

The ALL route does not call broad cleanup or a LISP helper. Standalone sewer MH/pipe commands still exist and retain legacy behavior. Keep `CLV_GIS_SSWR_PIPE_OD_OFFSET.lsp` available for those commands. Keep `CLV_GIS_OD_HELPERS.lsp` for its other callers. The master startup loader and live server files were not inspected or changed; there is no blanket file-deletion recommendation.

## Validation status and first native trial

The supplied DWGs were materialized with verified readable AC1032 headers and preserved unchanged, but the cloud readers did not decode their geometry/OD. The user's native screenshots confirm one 8-inch C900 LINE on Pipes with InsideDiameter `0.666667`, Structures point identities, the evaluated 48-inch circular MH block and co-located `UFLS_MH_MARK`. They do not establish every entity's XYZ or every native field type. The command validates those in the drawing; it does not hardcode sample coordinates.

Static code review, project-link checks and whitespace checks are available. `Tests/SewerGisPreparation.Tests` directly links the production circle/size/utility rules. The C# tests, complete plugin build and end-to-end Civil 3D/Map trial are **UNRUN** in the authoring environment. Its .NET compiler/Autodesk runtime are unavailable, and the registered native execution task failed setup before commands ran. A separate numerical equation probe is not a C# or native test pass.

Build the final source locally using the established Civil 3D 2026 references, then load the new randomized DLL in a fresh Civil 3D session. Trial on a copy containing the unprocessed imported OD and manhole blocks together:

1. Verify 8-inch pipes remain one line ending on the solid outer ring; inspect native pipe and outer-circle OD.
2. Include a controlled exactly-12-inch case and one larger case: each must keep its centerline and two correctly offset walls, all ending on the outer rings.
3. Verify reversed LINE direction, the actual dynamic visibility state, layer/linetype display and retained original OD values.
4. Rerun, save/reopen, and rerun again: entity counts and owned geometry should stay unchanged. Exercise UNDO on a copy and confirm source geometry/OD return together.
5. On separate disposable fixtures, test an edited/missing output, duplicate identity/marker, locked source/target layer, unknown source metadata and incompatible Z. Reviews/failures must preserve the pre-run drawing state rather than partially converting or removing sources.

Storm R5 and the setup/save/reopen implementation remain separate and require regression coverage after the shared OD-reader extraction. Automatic SDF import remains paused.
