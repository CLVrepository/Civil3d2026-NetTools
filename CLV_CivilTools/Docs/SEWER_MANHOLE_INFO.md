## 2026-11-05 correction - incomplete networks and CLV text style

The first native test confirmed placement/edit/refresh. Apply had stopped before any edits when two plan connections were compared to one actual pipe. This correction removes that equal-count gate and the All/Rimonly scope prompt, uses explicit mapping of existing pipes with pending plan connections, and applies CLV-Non Anno to new/refreshed info-block text. Live corrected transfer/labels remain to be verified on the workstation.

# Sewer manhole information starter

## Purpose and scope

Import plan-extracted JSON or manually enter sewer manholes into a DWG catalog, place attributed info blocks, then transfer a reviewed block snapshot to one existing Civil 3D gravity structure and explicitly selected connected pipe ends. The routine does not extract PDFs inside CAD. The separately prepared JSON is the input; unzip the extraction package first. CSV is a review report, not an import file.

Q1 > ADJUST > SEWER PLAN DATA > MANHOLE INFO opens the WinForms manager. Each manager invocation performs one action and ends, so native Undo applies to that action. No new PaletteSet or external block/LISP resource is required. Actual project framework remains net10.0-windows; it is unchanged.

| Command | Purpose |
|---|---|
| SSMHINFO | Catalog list, source/details viewer and action chooser. |
| SSMHIMPORT | Import/merge a JSON catalog after a count/name/rim/size preview. |
| SSMHADD | Add a manually entered manhole; initializes a project catalog if needed. |
| SSMHEDIT | Enter an existing manhole number, then edit its data. |
| SSMHINSERT | Enter a manhole number, pick marker XY and text height, place the info block. |
| SSMHREFRESH | Refresh one placed block from its current catalog record, after old/new preview. |
| SSMHAPPLY | Pick an info block and a native Civil structure; review/map/apply data. |
| SSMHEXPORT | Export the current drawing catalog to JSON for reuse. |

## Catalog data

The naming convention is `{project}-SSMH-{number:02d}`: minimum two number digits, preserving the plan number; numbers above 99 are not truncated. Source IDs remain separate. Storm SDMH and drop inlets are outside this version. One project catalog per DWG is supported.

Accepted input schemas are `clv.sewer-manhole-catalog/0.1-draft`, `/0.2-draft` and `/1.0`. The reviewed extraction format is supported directly. Elevations must be feet and diameters inches. Unknown numbers remain JSON null/blank; measured zero and negative elevations remain valid. Positive sizes are required when supplied. Names, unique numbers, unique connection identities, finite values, invert below rim, supported schemas and maximum sizes are validated before database writes. Duplicate JSON keys, including case variants, are rejected. Import size limit is 8 MiB; maximum 10,000 manholes and 32 connections per manhole.

Records include rim, structure diameter and its basis/source wording, station, notes, source references and an arbitrary connection list. Each connection retains IN/OUT, direction, invert, pipe diameter, material code, physical material family, specification, raw material label, source metadata and optional neighboring manhole name. Material codes such as C900, PVC, RCP and SDR35 are supported as text; C-900 and SDR-35 normalize during manual entry. A specification does not automatically invent a material family or Civil parts catalog ID. Preserve BASE wording for structure size: this tool does not equate a base diameter with an access opening, barrel or outside diameter.

Unknown source fields, sheet references, datum and extraction issues round-trip through JSON extension data. Source extraction notes remain historical when editing records; manual notes/review status distinguish subsequent edits. Import replaces matching manhole numbers only after confirmation, retains manual records absent from the file, and records the latest import metadata. Different projects or declared vertical datum names cannot be merged. No elevation/datum transformation is applied. Export includes manual changes.

## Drawing and block persistence

- Catalog: Named Objects Dictionary Xrecord `CLV_SEWER_CATALOG_V1`, JSON in short DXF text chunks. It travels with a saved DWG, rather than being a process-only cache.
- Layer: `CLV-SSMH-INFO`, green ACI 3/nonplot when newly created. Existing layer settings are preserved; an off/frozen/locked/dependent layer prevents placement. Current layer is not changed.
- Definitions: `CLV_SSMH_INFO_V1_Cnn`, generated once per connection count with a small circle/cross and standard attributes. Existing unrelated definitions with the same names are refused. New and refreshed attributes use the existing **CLV-Non Anno** text style. A missing style stops placement/refresh and asks the operator to load it from the CLV template; no fallback style is used. Existing tool-generated definition attributes are updated when reused; use SSMHREFRESH on an existing placed block to update its own attribute references.
- Block: marker uses the picked WCS XY at Z=0, regardless of picked Z/UCS. Rim and inverts are data, not block insertion elevation. Text height controls the uniform block scale. Attributes show the generated name, rim, structure size/basis, source ID/station and one line per pipe. Attribute grips can reposition text.
- Snapshot: block extension Xrecord `CLV_SEWER_INFO_BLOCK_V1` holds one complete catalog record plus project metadata. Copying the block preserves its snapshot. The snapshot is independent of later imports/edits; no reactive update changes an existing block silently.
- Direct ATTEDIT changes are not authoritative. Apply refuses changed/missing/duplicate visible attributes; edit the catalog and refresh instead. Refresh replaces attributes/snapshot and resets their positions, preserving block insertion/scale/rotation/layer. Existing erased attribute IDs are ignored on readback.
- If a drawing catalog has a changed matching record, Apply requires Refresh first. A valid copied block can also be used without a drawing catalog; the full snapshot is available for review.

## Network transfer

Use the Model tab, an editable local gravity structure and pipes, and Civil drawing units Feet. Data-reference/read-only parts and locked/dependent layers are refused. Pick the block and destination structure. There is no transfer-scope prompt. Name/rim are always included and AutomaticRimSurfaceAdjustment is disabled for the plan-controlled rim.

A known rim is required, but plan/native connection counts may differ. A mapping form lists each actual pipe connected to the selected structure, with its name/handle, START/END, current invert and existing part. Explicitly choose its plan connection or Leave this existing pipe unchanged. No choice is preselected. The same plan connection cannot be assigned twice, and an assigned invert must be known. Connections not mapped to an actual pipe are retained as pending plan data; they do not prevent name/rim updates. A structure with zero pipes can receive name/rim, with all plan connections pending. Pipes attached at both ends to the selected structure are refused when mapped. This starter supports straight circular gravity pipes. It does not infer hydraulic IN/OUT from a pipe's native start/end ordering or use direction text to guess a pipe. The operator explicitly maps each connection.

Preview shows old/new name and rim, the automatic rim setting, plan structure size, current native family/size, each mapped START/END, current and desired invert, modeled inner height and plan material/specification, plus the full source record. The confirmation button remains disabled until the review acknowledgement is checked. Cancel/Esc aborts before writes.

Invert convention: apply the plan value at the CURRENT modeled pipe endpoint. Compute centerline Z as `plan invert + actual native InnerHeight / 2`, retaining endpoint XY and the opposite endpoint's Z. Nominal plan diameter is not substituted for the existing part's actual inner height. There is NO assumed inner-wall/outer-wall/structure-center invert correction. If project callouts require such a correction, establish that convention before use; structure diameter is retained for a subsequent explicitly defined calculation. The other endpoint may need a later transfer from its own manhole, so inspect final slopes/flow after applying both ends.

The preview lists mapped changes, every pending plan connection and each existing pipe explicitly left unchanged. All maps and changes for one structure are one transaction. Readback checks requested endpoint elevations, all network pipe XY, untouched Z, actual inner heights, connectivity and family/size IDs. Structure XY and family/size IDs must remain unchanged; adjacent structure name/rim/sump/automatic-rim properties are checked. The selected structure may natively adjust its sump as its connected inverts/rim change; the routine does not assign a sump elevation. A differing native result or collateral edit aborts the transaction. No ApplyRules call, structure/pipe creation, automatic size selection or material swap is performed.

The destination stores the reviewed plan record/source block handle/mapping in `CLV_SEWER_APPLIED_INFO_V1`. Mapped pipe ends store connection data separately in `CLV_SEWER_PLAN_START_V1` or `CLV_SEWER_PLAN_END_V1`, retaining the other end's metadata. The structure audit also stores pending connection IDs and existing pipe handles left unchanged. The completion message reports actual verified native name/rim, applied endpoint count, pending plan connections and unchanged native pipes; it lists each pending connection. Structure/mapped pipe graphics are marked modified and the view regenerated after commit. These support a later explicit parts-mapping workflow. Structure base size and pipe material/specification are reference data at this stage, not edits to native parts.

## Workstation acceptance on a drawing copy

1. Rebuild the full solution with Civil closed; verify the deployed stable DLL and fresh randomized DLL SHA256 match. Restart using the existing loader for `C:\Temp\C3DDev\CLV_CivilTools.dll`; do not NETLOAD a second DLL copy.
2. Import the extracted JSON. Check record counts, multiple IN rows, base sizes and material/specification fields, source metadata and blank incomplete records. Test Cancel at import and reimport a matching record while retaining an extra manually entered record.
3. Add a test record manually (including zero/negative invert values if appropriate), save, export/reimport and save/reopen the DWG. Check source/units/name and all dimensions remain separate.
4. Place records with 0, 2 and 3 connections; test a rotated UCS, text height, attribute grips, save/reopen, COPY and WBLOCK into a copy drawing. Marker XY should be WCS-correct and marker Z zero. Inspect info-layer settings and nonplot behavior. Verify CLV-Non Anno on each new attribute and after refreshing an old Standard-style block. In a test drawing missing that style, placement/refresh must fail without creating objects or changing source data.
5. Edit a record. Apply must refuse its stale placed block; Refresh must show old/new, retain block transform, reset attributes, and work again after changing the connection count. Test repeated refresh and direct ATTEDIT refusal.
6. Build a representative native network, including a multi-IN structure. Apply, mapping each actual connected pipe to its plan record; inspect actual Civil structure/pipe Properties and native labels, not only the generated block. Verify requested rim, native START/END invert convention, retained XY/opposite-end elevations and final slopes after both manholes are updated.
7. Reproduce the reported two-plan/one-native case: map the single outgoing native pipe to the OUT plan connection; verify name/rim and its invert update while the incoming stub is explicitly pending. Test zero native pipes, extra native pipes explicitly left unchanged, unknown pending inverts, duplicate mappings and unselected rows. Test Auto Rim True -> False, duplicate destination names, missing rim/assigned inverts, locked layers, DREFs and curved/noncircular mapped pipes. Cancellation or failure must leave the network unchanged.
8. Test native minimum-cover/rim/sump constraints; refusal must roll back all edits/metadata. Test Undo and Redo for import, manual edit, placement, refresh and transfer. Check unrelated network geometry and applied metadata.

Host-independent tests: `dotnet run --project Tests/SewerInfo.Tests/SewerInfo.Tests.csproj`. An optional JSON path after `--` tests the supplied extraction file through the actual parser and serialization round trip. These tests do not execute Autodesk code or validate live transaction/geometry behavior.

Full-solution build and live Civil 3D acceptance remain workstation tasks. Targeted source compilation and actual parser regressions are recorded separately in the release handoff.

