# Storm GIS PREP-ALL: centered trial

Revision: 2026.10.06-R2. Use a disposable copy of the test drawing first.

## R2 native-reader correction

The first native trial of 2026.10.06 reported a MapException on the first point, then null-reference errors on the remaining points, with zero conversions. The safety gate retained all source points and did not start pipe offset or cleanup.

Installed Autodesk 2026 API inspection establishes that `ProjectModel.ODTables` returns a cached wrapper constructed with `AutoDelete=false`, while the table indexer creates owned table wrappers. R2 no longer disposes the borrowed cached ODTables wrapper. It keeps normal owned-wrapper disposal. The installed point getter is `MapValue.Point`, not `Point3dValue`; this is also corrected. The first exception's precise native cause was not recorded by the old diagnostics, so it must not be represented as conclusively reproduced or solved solely by compilation.

R2 reads identity from Structures only. Full OD copy uses only records attached to the entity; it does not scan all project table names. Autodesk documents that the table-name collection can include attached drawings: https://blog.autodesk.io/want-to-know-which-object-data-tables-are-in-the-current-drawing-file/ . Native record-call fourth parameters are `skipSubObj` (false includes subobjects), not creation flags. Empty record collections are returned before enumerating.

Any native preflight failure now stops before geometry matching and reports the failing operation, handle/table/field, active/working database agreement, native ErrorCode/HResult and original exception chain/stack. Missing or conflicting identity data remains a normal review item. Use F2 to copy the full diagnostic if a native failure remains.

Restart Civil 3D before trying R2 on a disposable drawing copy: the previous DLL may already have disposed a cached Map wrapper in its session. Do not keep trying within that same potentially invalid session. Native OD reads, copy/readback on uncommitted destination entities, repeated runs and rollback still require an actual hosted smoke test.

## Scope

This change addresses the supplied centered access/junction combination and keeps the existing DI block workflow. It does not place offset access features automatically. Automatic source/target matching uses XY within 0.10 drawing units, without the old 0.50/1.50 nearest-neighbor fallback.

The supported input is imported DBPoint entities on the `Structures` layer with native `Structures` OD. Other entity types on that layer are retained and reported. Name and PartSizeName must be readable together from the same complete record; conflicting repeated records stop that source.

- `L24-00066-SDDI-04` / `7.50 Foot, Type CM2 >6`: drop inlet.
- `L24-00066-SDMH-08` / `USD TYPE IA ACCESS STRUCTURE (60 inch BARREL / 24 inch FRAME)`: access.
- `L24-00066-SDMH-08-JS` / `USD TYPE II SD MANHOLE-PIPE (405.2) (L=65 inch x W=69 inch)`: junction box. The exact trailing `-JS` takes precedence over generic MANHOLE wording.

A unique exact base-name access/box pair must have centered source points within tolerance. Standalone explicitly identified access and boxes remain eligible. Duplicate names, multiple candidate blocks/outlines, unknown roles, offset pairs, and missing geometry are reported without guessing.

Box candidates are closed, straight four-sided rectangular polylines on the established survey outer layers; existing final-layer box geometry requires box OD proof. Inner box outlines are optional, but multiple centered inner candidates require review. Supported DI block names and `UFLS-GIS-MH-CIRCULAR` remain in use. DI_CENTER is used before insertion-point fallback. DI outer footprints can contain the anchor even when asymmetric; curb/reference text and marker geometry are removed only from a successful block conversion.

## Data preservation and reruns

The Structures source DBPoints are never erased by this workflow, including after successful transfer. Native Map OD is copied synchronously to new replacement/exploded entities and all typed fields/records are read back before the original block or box outline is erased. Character, Integer, Real, and Point OD are supported; an unavailable API, incompatible signature, unsupported type, conflicting target OD, or failed readback stops that conversion. No queued LISP result is treated as proof of an OD copy.

New geometry is created inside the conversion transaction. A failed conversion aborts it. Original source points and source geometry remain available. Retained points receive an extension-dictionary completion record only after verified conversion. Reruns validate that identity, saved center, linked geometry, and actual OD still agree; changed/missing outputs require review rather than blind recreation. Moving a completed source point must not assign it to a new block.

`CLV-GIS-STORM-GIS` calls structure preparation synchronously. Any review item prevents downstream pipe work. With a clean result it queues the existing server pipe helper without broad managed cleanup. A missing pipe helper is reported separately. Pipe helper execution itself must still be checked in its own command output; merely queueing it is not a completion claim. Standalone pipe offset and cleanup commands retain their previous behavior.

Do not run the separate imported-point erasure command while unresolved points remain. Automatic broad cleanup was removed from this storm flow; this release does not claim that stripping arbitrary XData preserves every native OD implementation.

## Build and validation

The checked-in project currently targets `net10.0-windows` (older general project notes still describe net8). Keep its target unchanged. Build on Windows with the required Autodesk 2026/Civil 3D/Map references, using a compatible stable SDK. The normal repository build has PDF-renderer publish and `C:\Temp\C3DDev` post-build copying; disable those targets only in a separate temporary validation project when a no-deployment compile is desired.

Pure production-matcher tests (no NuGet packages or Autodesk references):

    dotnet run --project Tests/StormStructureMatching.Tests/StormStructureMatching.Tests.csproj

With only SDK/runtime 10 available:

    dotnet run --project Tests/StormStructureMatching.Tests/StormStructureMatching.Tests.csproj -p:TargetFramework=net10.0

These tests cover classification, exact base pairing, duplicate/ambiguous assignments, strict tolerance, and ordering/one-to-one invariants. They do not validate native geometry transactions, Map OD, helper execution, or drawing output. Source review is not a substitute for native runtime validation.

## Disposable-drawing trial checklist

1. Make a separate DWG copy. Keep the original supplied drawing untouched. NETLOAD the newly built development DLL into a fresh Civil 3D 2026 session so an old loaded assembly cannot mask the result.
2. Run `CLV-GIS-STRM-AUTO` first. Confirm the `2026.10.06-R2` revision stamp, converted/OD-verified count, retained-point count, and any REVIEW source/destination handles.
3. At SDMH-08 verify two distinct outer assets: circular access with the unsuffixed access OD, and rectangular box with the `-JS` box OD. Inspect every field on each target, not just Name. Confirm the DI retains its existing intended outline and OD and excludes embedded curb/reference geometry.
4. Confirm all original imported source points still exist with unchanged native OD. For unknown/conflicting/missing/offset sources, confirm the source block/box geometry also remains untouched.
5. Run again. Confirm no duplicate geometry and an already-verified result. In a separate copy, move a completed source or change a target OD field; rerun must report review without creating a second output.
6. Test duplicate import records, two competing access blocks, unknown PartSizeName, absent Structures table, and offsets of 0.50/1.50. Confirm no nearest fallback or generic leftover junction conversion occurs.
7. Test a native OD failure in a disposable setup. Verify no successful-conversion count, original geometry/points intact, and no pipe/cleanup queued by ALL. Verify Map can attach/read records on new entities before the conversion transaction commits; if not, this trial must stop with its explicit error rather than release unverified output.
8. Run ALL only after reviewing the structure-only pass. With review items, pipe work must not start. With clean results, inspect pipe-helper output. With its shared file unavailable, structures must remain intact and the missing-helper message must appear without broad cleanup.
9. Use cache COMPARE in a disposable validation context: an existing rectangle at the access circle center must not classify the circle as an exact duplicate. Different shapes/extents remain nearby conflicts. This is a conservative shape guard, not full cross-drawing OD identity matching.

## Rollback

The pre-edit Git backup is `backup/storm-gis-prep-2026-10-06`, pointing to `45f1316361d1946ec14e35beb7a4cf69a221dcfc`. Use a new branch or a normal revert to restore code; do not force-reset shared branches. Retain the pre-trial drawing copy separately. No new shared LISP file, production deployment, or drawing save is part of this source change.
