# Storm structure matching tests

This zero-package console test runner compiles the actual production
`CLV_CivilTools/Gis/StormStructureMatching.cs` file without Autodesk assemblies.
It validates classification and conservative role-aware one-to-one planning.
It also links the actual `StormStructureVisibility.cs` production traversal.

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
Repeatability here does not
claim drawing-level rerun safety: persistent output ownership, OD copy/readback,
rollback, actual block centers, and Civil 3D integration require host tests.
