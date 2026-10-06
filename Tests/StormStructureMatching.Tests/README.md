# Storm structure matching tests

This zero-package console test runner compiles the actual production
`CLV_CivilTools/Gis/StormStructureMatching.cs` file without Autodesk assemblies.
It validates classification and conservative role-aware one-to-one planning.

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
and conflicting roles, duplicate identities, duplicate entity IDs, offset pairs,
candidate ties, mutual uniqueness, radius limits, invalid data, enumeration-order
independence, and repeatable non-mutating planning. Repeatability here does not
claim drawing-level rerun safety: persistent output ownership, OD copy/readback,
rollback, actual block centers, and Civil 3D integration require host tests.
