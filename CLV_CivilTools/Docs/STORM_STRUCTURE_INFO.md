# Storm structure plan data

Use **UFLS > Adjust > STORM PLAN DATA > MANHOLE / DROP INLET INFO**, or `SDINFO`.

This is a separate storm catalog, command set, block namespace, endpoint metadata and drawing Xrecord. Existing sewer commands, catalogs and blocks are unchanged. Both catalogs may coexist in the same DWG.

## Commands

| Command | Action |
| --- | --- |
| SDINFO | Manager: import, add, edit, place, apply, refresh, export |
| SDIMPORT | Import/merge storm JSON |
| SDADD / SDEDIT | Manual record entry/edit |
| SDINSERT | Pick identifier and SDMH/SDDI type, then place a marker |
| SDAPPLY | Transfer a placed snapshot to a selected Civil structure |
| SDREFRESH | Refresh edited catalog data into a placed block |
| SDEXPORT | Export the drawing's storm JSON |

## Schema and identity

Schema: `clv.storm-structure-catalog/1.0`; system: `storm`; elevations: feet; dimensions: inches. The list is `structures` (the sewer list remains `manholes`). JSON is the CAD import format. Companion CSVs are review documents, not a CSV import path.

Names are project-SDMH-01 and project-SDDI-01. Type plus identifier is unique, so SDMH-01 and SDDI-01 can coexist. Numeric/letter identifiers such as 4A and DECATUR are supported. `sourceId` preserves a consultant label such as CB-IMN1; it need not resemble the standardized name. Source sheet/page references, notes, aliases, candidates and other extension metadata survive round trips and manual editing.

Structure fields include round diameter, rectangular width/length, inlet type, separate inlet opening length, rim/top elevation, and top-elevation basis. Opening length is not a barrel diameter or an assumed vault width. Pipe connections include flow, direction, invert, circular diameter or rectangular width/height, material code/family/specification and optional connected structure name.

`sourceTopLabel` / `sourceTopElevation` preserve a printed TC/TG value separately. TC is not automatically promoted to a rim. Unknown elevations remain null and visible as UNKNOWN. Set a verified rim, grate or top basis before network application. A flowline/unknown basis cannot be applied as a rim.

## Review and application

An `applicationHoldReason` blocks network application while allowing catalog import and marker placement. Edit the record, correct the controlling values, then explicitly check the acknowledgement to clear the hold. Historical source conflicts remain in metadata. Refresh any already placed marker before applying. Unknown rim/top still blocks application even if a hold is cleared.

Markers use CLV-SD-INFO and text style **CLV-Non Anno**, with source labels and elevation information. Marker Z remains 0; elevations are data, not insertion elevation. Do not edit block attributes directly to change the database.

Apply inherits the sewer tool's preview/confirmation, reference/layer checks, endpoint identity, automatic START=OUT / END=IN plus WCS direction matching, curved-pipe tangent checks, opposite-end conflict detection, snapshot freshness, part lookup and transaction rollback. Ambiguous/unmatched connections remain unchanged and are listed. Structure/pipe XY, arcs, connectivity and unaffected parts are verified.

Circular RCP/PVC/C900 connections use the existing exact family/numeric size rules. Round structure resizing remains within the existing structure family and preserves the frame. This does not choose an inlet family automatically.

Rectangular RCB connections may transfer elevations only when the existing native pipe already has the plan's inside width and height. Its part/material is retained and explicitly identified for manual verification. Mismatched box dimensions stop application; there is no automatic RCB part-family/size swap in this version. Rectangular inlet structure width/length is retained in data and preview, but its native structure part is not automatically resized. Select/size the correct inlet part manually before applying.

The program does not infer hydraulic junctions at lateral taps offset from access manholes, generate missing rim/inverts from a surface, convert vertical datums, or create a complete network from the catalog. Imported source metadata is a review aid.

## Validation and workstation acceptance

Targeted GIS-independent storm-source compilation against installed AutoCAD/Civil assemblies passed. Full solution build and live Civil UI/network tests remain workstation checks. Do not load the targeted compilation artifact.

1. Import a storm catalog using SDIMPORT; confirm the existing sewer catalog remains available in SSMHINFO.
2. Confirm SDMH-01 and SDDI-01 are distinct, and the consultant's labels are visible.
3. Add/edit a manual SDMH and SDDI, export, reimport and confirm dimensions, shape, material and source metadata survive.
4. Place/refresh both block types; verify CLV-Non Anno text and source TC/TG display.
5. Confirm an unresolved hold, missing top, stale snapshot and flowline basis block application.
6. On a drawing copy, apply a reviewed circular RCP record; inspect name, rim, endpoint invert and numeric part size, while confirming opposite-end invert/XY and curved plan geometry remain unchanged.
7. Check a matching rectangular box record with its correct native size. A wrong box width/height must stop; correct boxes retain their existing part/material.
8. Inspect the preview's unchanged rectangular inlet part warning and manually verify its native dimensions/family.

