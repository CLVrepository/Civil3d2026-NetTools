# Sewer GIS preparation tests

This package-free console suite links the production `SewerCircleTrim.cs` and
`SewerPreparationRules.cs` files directly. It does not load Autodesk assemblies.

Run from the repository root with .NET 8:

```sh
dotnet run --project Tests/SewerGisPreparation.Tests/SewerGisPreparation.Tests.csproj
```

With a .NET 10 SDK/runtime:

```sh
dotnet run --project Tests/SewerGisPreparation.Tests/SewerGisPreparation.Tests.csproj -p:TargetFramework=net10.0
```

The process exits 0 only when every case passes. Tests cover the exact 12-inch
diameter threshold, small source LINEs, large centerlines and both walls, exact
nonradial circular crossings, different terminal radii, outer versus inner radius,
independent permitted open ends, off-center inside endpoints, direction reversal,
rotation, State Plane coordinate translation, already-trimmed/no-op reruns,
absolute drawing-unit boundary tolerance, tangent and ambiguous contacts, gaps,
through crossings, interior reentry, consumed/short intervals, overlapping or
touching circles, duplicate circle identities and invalid/nonfinite input.

The caller must prove the terminal-to-MH association and supply the actual outer
circle. A null circle is accepted only as the caller's explicit verified open/null
end, never inferred by this helper. A `Review` result always carries the original
`[0,1]` parameter interval and must prevent all mutation for that pipe.

These are pure straight-LINE XY rules. Native curve exclusion, evaluated dynamic
block visibility, real inner/outer ring classification, original source ownership,
OD transfer and readback, transaction rollback, Z handling, and drawing-level rerun
behavior require separate host validation. This suite does not authorize curved
sewer pipes, laterals or any extension of disconnected linework.

The production-linked utility gate runs on already-read layer, table and Character
identity evidence before the host requires a sewer `Pipes` record or diameter.
Cases cover explicit storm layers, exact `SD_Pipes` and `SS_Pipes` table names,
bounded utility words and numbered identities, terminal-field evidence, conflicting
utilities across records, unrelated substrings, invalid/missing collections,
case/culture/order independence, and generic candidates. Empty evidence lists and
blank Character identities convey no evidence; null collections/entries and blank
table names require review. A candidate is not a verified sewer pipe: the host must
still validate the full native schema, diameter and connection ownership. These
tests do not execute native OD inspection, mixed-drawing discovery or archive reruns.

Insertion-unit cases accept only Autodesk `INSUNITS` 2 (Feet) and 21 (US Survey
Feet), rejecting unitless, incompatible and unknown values. The host must read
the actual database setting before edits; a block's insertion-unit label alone
does not establish the drawing's units. The numerical contract is documented in
[Autodesk INSUNITS](https://help.autodesk.com/cloudhelp/2022/ENU/AutoCAD-Core/files/GUID-A58A87BB-482B-4042-A00A-EEF55A2B4FD8.htm).

Utility diagnostic cases verify the exact storm/sewer evidence, field indices,
invalid-input locations and escaped control characters without relaxing the gate.
The native reader separately labels each identity index with its table, record
and field name; those labels still require native validation.

Native dictionary regression: rerun the six MHs that failed `84dca7b` and verify
metadata capture no longer casts `System.Collections.DictionaryEntry` to
`DBDictionaryEntry`. Production snapshots the typed AutoCAD foreach entries before
sorting. A pure fake-enumerator demonstration would not execute that production
native path, so no such test is claimed here. Also verify unknown nested metadata
still stops conversion and changed metadata still changes the captured signature.

Connection cases cover exact corrected-sewer `-CONN` suffix recognition, rejection
of arbitrary or conflicted names, the observed FBC naming conflict, corrected
identity acceptance, small/large lines with an untrimmed connection terminal,
reversed orientation and two permitted open ends. The host still must prove a
unique native Structures target and matching endpoint XYZ (production-linked cases reject nearby 0.01-unit offsets, Z differences, nonfinite coordinates and diagonal tolerance overflow). Native acceptance must
cover absent/blank PartSizeName, retained connection POINT/full OD and archive
reruns. The pure name rule never proves native ownership by itself.
