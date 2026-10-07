# Storm structure matching tests

This zero-package console test runner compiles the actual production
`CLV_CivilTools/Gis/StormStructureMatching.cs` file without Autodesk assemblies.
It validates classification and conservative role-aware one-to-one planning.
It also links the actual `StormStructureVisibility.cs` production traversal.
The cleanup planner and OD fingerprint helper are linked as production sources too.
The pure terminal-trim planner is linked directly as well.
Managed pipe-preparation rule tests link `StormPipePreparationRules.cs` directly.

Run from the repository root with an installed .NET 8 SDK:

```sh
dotnet run --project Tests/StormStructureMatching.Tests/StormStructureMatching.Tests.csproj
```

If only a newer SDK/runtime is installed, the target can be overridden, for example:

```sh
dotnet run --project Tests/StormStructureMatching.Tests/StormStructureMatching.Tests.csproj -p:TargetFramework=net10.0
```

Exit code is 0 only when every test passes. No NuGet package dependencies are used.
Tests cover centered access/box pairs, standalone structures, DI evidence, unknown
and conflicting roles, duplicate identities, duplicate entity IDs, eccentric pairs
associated by exact unique base name while independently matching their own
role-specific geometry, candidate ties, mutual uniqueness, invalid data,
enumeration-order independence, and repeatable non-mutating planning. DI/access
and targets without a footprint retain 0.10 XY matching. Junction footprints use
inside/on-polygon containment with numerical boundary slack of 1e-8 and the
legacy 25-unit center search radius. Tests cover skew/concave polygons, off-center
sources, boundary/outside cases, overlapping footprints, competing sources, invalid
footprints, survey coordinates, and prevention of relaxed DI/access matching.
Nested-outline tests also cover skew/offset inners, partly outside inners,
concave-boundary edge and vertex crossings, identical boundaries, and invalid rings.
Explicit non-graphic pipe-end tests cover only the verified `UFLS-Null Structure`
description with a nonempty identity ending in `-STUB` (case/outer-whitespace
insensitive). Valid entries appear in `PreservedPipeEndSourceIds` and never demand
or consume geometry. Conflicts, duplicate IDs/names, missing OD and nonfinite
coordinates remain review issues. A source with the verified SDDI name and NDOT
TYPE 2 description is tested for DI classification only, with no geometry mapping
inferred from its name.
Existing DI/box outline tests exercise the explicit `IsExistingOutline` opt-in:
an unbound outline is one physical target shared by both source roles, while a
known role binds it to that OD category. Containment uses the 25-unit radius and
never reclassifies a DI source as a box. Tests cover cross-role competition, bound
roles, invalid flags/footprints, overlapping outlines, duplicate physical IDs and
unchanged centered-DI/access behavior. Merely supplying a footprint does not opt
an ordinary DI target into this path.
Completion-ownership planning tests cover one logical completed source reserving
all of its outputs, secondary-output overlap, duplicate source/output claims,
case-insensitive collisions, malformed claims, disjoint valid claims, order
independence, and immutable snapshots of accepted output IDs. The host must remove
every accepted output from ordinary candidates before adding its one logical
completed target; rejected claims reserve nothing and remain review-only.
Visibility tests use the observed 48/60/72-inch evaluated-block manifest, selecting
only the visible 60-inch inner/outer pair. Additional modeled 48/72 active-state
variations assert exactly their matching inner/outer pair and correct radii; only
the 60-inch state is supported by the native probe. They cover hidden ancestors/root,
all-hidden contents, empty containers versus leaves, callback failure propagation,
32-level nesting/cycle bounds, traversal order and null callback validation.
Native evaluated-block lookup, transforms and resulting Civil geometry still
require host validation; these tests exercise the production traversal itself.
Cleanup tests cover the observed DI/MH marker anchors, exact marker name/layer and
empty-OD gates, unresolved/unknown geometric competitors, completion-ownership
conflicts, duplicate live/archive names and IDs, ambiguous retained cohorts,
recorded versus newly inserted markers at archived anchors, and no-op reruns.
Verified owner IDs include both live and archived safe completion owners, while
point-erasure IDs remain the live subset; unresolved or ambiguous owners are excluded.
The planner returns archive-then-erase eligibility only; host archive persistence,
fresh native OD/geometry verification, entity types and atomic deletion require
Civil 3D tests. Explicit null/STUB sources remain retained.
Fingerprint tests include independent golden values, ordering/multiplicity,
input immutability, culture invariance, exact Unicode and delimiter framing,
invalid UTF-16 rejection and strict versioned-digest syntax validation. A valid
digest alone never proves that native OD is present or correctly transferred.
Terminal-trim tests cover both directions, skew boxes, proper corner crossings,
tangency, outside/already-trimmed paths, boundary overlaps, through/inside-only
paths, multiple contacts, segment-index parameters, survey coordinates, read-only
inputs, repeat/no-op behavior, invalid rings/paths, and winding independence.
Native curves, bulges, widths, Z/elevation, OD transfer and transaction behavior
must still be excluded or verified by the host adapter and Civil 3D trial.
Trim host role gates include verified straight closed DI/NDOT and junction-box
outlines without changing their native OD role; curved DI/access boundaries and
polyline width handling still require native-host checks.
Pipe rule tests cover the exact 1-foot (12-inch) threshold, invalid numeric
diameters, single-line/no-wall and two-distinct-side ownership, duplicate/source
ID exclusion, missing sides, layer hints versus utility/OD evidence, sewer names,
and exact completion-record comparison across source/OD/geometry/side/order changes.
The production comparison can support skipping an unchanged record rewrite only
after host validation; it does not itself prove live ObjectIds, native geometry,
OD transfer or safe reuse. `InspectPipeInsideDiameter` stays native-only: its typed
field parsing, cross-table conflicts, full fingerprint and read-failure behavior
require Civil 3D fixtures and are not linked into this pure runner.
The outward terminal-gap probe is tested for true interior ahead, a box behind,
parallel misses, tangent versus entering corners, boundary-only overlap, interior
following a concave overlap, distance bounds, multiple interior intervals, vector
normalization, invalid input, survey coordinates and unchanged input data. It is
read-only review evidence and never extends a pipe; any null-end exemption is a
separate source-owned correspondence check, not a nearest-wall exception.
The production null-terminal exemption predicate has separate coverage for both
perpendicular wall sides, both original source endpoints, path reversal, tangent
agreement, diameter/radial bounds, middle/split endpoints, ambiguous original
terminals, physical/unknown competitors, global duplicate identities, invalid
geometry, unlocatable anchors, strict null/STUB convention and survey coordinates.
These checks require host-proven wall/source ownership before use; they do not
authorize a generic nearby wall or every source on a null-containing network.
Verified physical footprints veto the null exemption when they contain the actual
source terminal inside/on, including an off-center box whose point is farther than
0.10 away; invalid footprints fail closed. Outside valid footprints remain allowed.
Utility-classification tests distinguish sewer-only exclusion from explicit mixed
storm/sewer evidence requiring review, use exact documented layer/table tokens,
preserve neutral/generic candidates, and exercise incomplete metadata, case,
ordering and culture. Host read failures or invalid diameter/OD must be handled
before utility classification; a Candidate result alone never authorizes mutation.
Repeatability here does not
claim drawing-level rerun safety: persistent output ownership, OD copy/readback,
rollback, actual block centers, and Civil 3D integration require host tests.
