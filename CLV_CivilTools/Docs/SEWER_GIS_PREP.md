## 2026-10-13 - Verified sewer STUB cleanup S9-STUBS
- GIS PREP - ALL now removes only unchanged, uniquely archive-owned OpenNullEnd POINTs with the exact supported -STUB / UFLS-Null Structure identity and verified OD-named owned pipe terminals. Connection, unrelated, orphaned, edited, ambiguous and unsupported-metadata points remain protected; no layer/proximity sweep is used.
- Before erasure, a versioned drawing-local stub archive preserves original handle, identity, XYZ, shape fingerprint, every native OD record/field/type/value and exact linked pipe endpoint coordinates. Typed payload readback reproduces the native fingerprint, including duplicate records. The main archive upgrades to V3 with a required companion digest; missing/changed archives or live restored source handles fail verification. Existing V2 archives are read without rebuilding pipe geometry.
- Uses the same bounded native OD-only metadata guard established by S8. Pipe endpoints, walls and manhole geometry are not changed by stub cleanup; no artificial outer structure is created. All write acquisition, revalidation, archive readback, point erasure and final verification share the batch transaction.
- Added production-linked native OD codec and stub eligibility tests. Static review only in this authoring environment: executable tests, full build, native rollback/Undo and save/reopen trials remain required on a disposable drawing copy. Banner: 2026.10.13-S9-STUBS.

### S9 archive and recovery

The diagnostic POINT FCA (`L24-00066-SSWR-57+60-STUB`, `UFLS-Null Structure`) established the exact supported stub convention and native OD-only storage shape. Every candidate is re-read and proved independently; the screenshot and one diagnostic do not authorize deleting other point categories.

`CLV_SEWER_STUB_ARCHIVE_V1` is a drawing-local Xrecord containing an ASCII-safe JSON payload split into Xrecord text chunks. It preserves original source handles, names/parts, XYZ, original shape signatures, full typed native OD values (original table/field spelling and order, record boundaries and multiplicity), and owned pipe handle/name/physical terminal coordinates. Native `Character`, `Integer`, `Real`, and `Point` values are recoverable from the versioned payload; unsupported types stop cleanup. This is a data archive, not a replacement graphical structure or a copy of arbitrary point extension data. Unknown extra metadata keeps the original point live.

After the first successful stub cleanup, the main ownership record uses `CLV_SEWER_PREP_V3_S9_STUBS` and stores the required complete companion digest. Do not delete or edit either archive. V2 input archives remain supported; earlier S1-S3 restrictions remain unchanged. Prior DLL versions intentionally reject the new main archive header rather than ignoring the saved stub data. There is no automatic restore command in this revision: Undo reverses the transaction; later recovery tools can reconstruct the archived identity, XYZ and typed OD. Keep a drawing backup and validate on a copy first.

Required host trial: build/load S9; rerun GIS PREP - ALL on a copy of the reported existing batch; confirm the eligible stubs disappear, connections and unsupported points remain, and every pipe/wall endpoint is unchanged. Then rerun, Undo, save/reopen and rerun. Test locked points, changed OD/metadata, ambiguous/orphaned links, missing/altered companion archive, live restored source handles, and injected readback/erase failures. Verify full rollback rather than treating the pure tests as a native transaction test.

Pure regression commands:
- `dotnet run --project Tests/NativeOdArchive.Tests/NativeOdArchive.Tests.csproj`
- `dotnet run --project Tests/SewerPointCleanup.Tests/SewerPointCleanup.Tests.csproj`

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
# Sewer GIS preparation

`CLV-GIS-SSWR-GIS` is the managed **GIS PREP - ALL** button under Q2 > GIS > GIS TOOLS > ADD TO DATABASE > SEWER. The section now has the same two labels as Storm: **CREATE GIS DRAWING**, then **GIS PREP - ALL**.

## Operator workflow

1. In the original sewer survey/network drawing, choose SEWER > CREATE GIS DRAWING. The shared setup command creates Blank, matches source units/CRS, copies verified survey geometry and offers a new filename/save/reopen. Its separate [instructions](GIS_NEW_DRAWING.md) still apply.
2. In the prepared GIS drawing, run MAPIMPORT yourself, select the sewer SDF and load the matching IPF printed by setup. Confirm native `Pipes` and `Structures` Object Data are attached. Preparation does not choose or import an SDF.
3. On a disposable copy for the first trial, choose SEWER > GIS PREP - ALL. No geometry selection is required. The command reports its `2026.10.08-S3` preflight and either converts the complete supported batch or reports review handles before conversion.
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

Every processed pipe line ends at its verified manhole **outer** circle. At an explicitly verified open connection/null endpoint, its original terminal is preserved instead. This deliberately changes the old wall-only inner-boundary behavior: small-pipe lines and large-pipe centerlines are trimmed too. Walls use exact nonradial line/circle intersections, not a radius inset or a polygon approximation. The original Pipes record values, including Length and invert fields, remain unchanged; they describe the imported network, not a recalculated visible clipped length.

## Identity, preservation and reruns

- Native Structures identity selects sewer manholes. Names such as `SSMH`/`SSWR` and supported barrel/frame parts establish sewer evidence; explicit storm conflicts require review. One structure point maps to one `UFLS-GIS-MH-CIRCULAR`; no storm-style `-JS` pairing is assumed.
- Matching uses unique source/block/circle anchors within 0.10 XY units, compatible elevations, and the reference's current evaluated visible geometry with complete transforms. Known survey outer/inner layers, including `~~` suffixes, identify ring roles. Nonuniform transforms, extra unsupported visible geometry and ByBlock-dependent circle appearance require review.
- Pipe StructureStart/StructureEnd resolve to unique verified structure identities. Either LINE direction is accepted when it proves one connection orientation. Horizontal LINEs at a common nonzero elevation are supported when their points/circles agree. Varying Z, tilted geometry, curves/polylines/laterals, gaps needing extension, tangent-only contacts and consumed intervals are retained for review; no flattening occurs.
- Full typed native OD is preserved on the retained source LINE and copied/read back on both large walls and the manhole outer circle. Inner circles receive no invented OD. Exact recognized null/STUB endpoints remain at their original coordinates. An exact corrected-sewer `-CONN` name is also an open tie-in to an existing network of unknown physical type. It needs no manhole/block/radius or physical PartSizeName; its unique imported point and complete OD are retained. Centerline and offset-wall endpoints stay untrimmed at the connection, while the other manhole end is clipped normally.
- Only verified matched source blocks, eligible imported points and uniquely owned `UFLS_MH_MARK` instances are retired. Source points with additional XData or an extension dictionary stay in place and are reported by handle. Their complete OD also appears on the outer circle. Unknown nested/leaf metadata prevents destructive block conversion.
- One transaction covers geometry, OD proof, ownership archive and source cleanup. Planning reviews stop the batch before those changes. A later failure aborts that transaction. Native Map rollback and one-step UNDO still require host testing.
- A versioned drawing-local archive records final entity references, geometry/appearance snapshots, complete OD fingerprints, original pipe geometry and retired structure identity. Readback precedes cleanup and verification repeats afterward. Missing/edited outputs or new untracked sewer batches require review; they are not silently repaired or offset again. Initially excluded storm inputs remain excluded on rerun. Retained connection/null/metadata-bearing points are recorded explicitly.

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

## First native trial and S2 correction

The user ran `84dca7b`. Six matched MHs stopped while their dynamic extension dictionaries were captured: the non-generic LINQ enumerator yielded `System.Collections.DictionaryEntry`, which could not be cast to `DBDictionaryEntry`. Their connected pipes then lacked verified structure plans. No sewer geometry or cleanup was applied. The dictionary path now uses Autodesk's typed foreach entries before applying the original ordinal sort. See Autodesk's [DBDictionary.GetEnumerator](https://help.autodesk.com/cloudhelp/2026/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_DBDictionary_GetEnumerator.html) and [typed Current property](https://help.autodesk.com/cloudhelp/2026/ENU/OARX-ManagedRefGuide/files/OARX-ManagedRefGuide-Autodesk_AutoCAD_DatabaseServices_DbDictionaryEnumerator_Current.html).

Pipe FBC independently reached the utility-conflict gate. The old message did not identify the triggering field. S2 keeps the same rules and prints the exact conflicting layer/table/identity values plus native record/field labels. The subsequent native inspection found sewer Pipe8 starting at SSMH01 but referring to `L24-00066-STRM-64+88-CONN`. The user identified the STRM connection name as a mistake, corrected the source and re-exported. The utility guard remains intact; this tool does not rename or repair imported data. The user separately confirmed Create GIS Drawing, save/reopen and manual MAPIMPORT worked. S2 executable tests, full build and native acceptance remain unrun in the authoring environment.

## S3 connection-end policy and trial

The user defines exact `-CONN` names as ties to an existing network; the physical target may be a pipe or MH and no type information is promised. A connection needs corrected sewer utility evidence, one imported Structures identity with that exact name, finite coordinates and a coincident pipe endpoint within an absolute 1e-8 drawing-unit XY/elevation tolerance. A nearby point within the manhole 0.10 XY tolerance is insufficient for a connection; no endpoint is relocated to make it fit. Missing/blank PartSizeName is accepted only for this exact connection policy. Actual conflicting utility text and explicit DI/junction role evidence remain review conditions. A generic access/manhole part alone does not determine the physical type or radius of an otherwise verified sewer connection. No physical boundary is invented or selected by proximity.

The source pipe endpoint stays exactly where imported at a connection. Large-pipe walls remain at their original offset terminal station there. The connection POINT and all native OD remain unchanged, are rechecked before commit and are included in rerun ownership. No block/marker is erased for a connection. Duplicate/missing identities, inconsistent coordinates and mixed utility evidence still leave the batch unchanged. Existing null/STUB conventions are not broadened.

Use the corrected/re-exported SDF in the prepared test drawing. Verify Pipe8 reaches its connection point without trimming or extension at that end, its other end meets the SSMH01 outer circle, and the connection point/OD remains. Verify reversed orientation and a 12-inch-or-larger connection fixture, then rerun and save/reopen/rerun without duplicates. The new pure cases cover exact suffix/utility and clipping behavior; native point ownership, optional PartSizeName, OD retention and full S3 integration remain unrun.

