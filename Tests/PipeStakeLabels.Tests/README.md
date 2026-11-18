# Pipe stake label regressions

Run from repository root:

```powershell
dotnet run --project .\Tests\PipeStakeLabels.Tests\PipeStakeLabels.Tests.csproj
```

The project directly compiles the production PipeStakeMath.cs and needs no Autodesk runtime or test packages.

43 assertions cover finite-line and actual-arc projection, reversed/clockwise/wrapped arcs, endpoints, ambiguous picks, centerline-to-invert/outside-top arithmetic, missing wall thickness, culture-invariant formatting, MText escaping, alignment versus pipe distance, warning labels and omission of slope/start/end invert. Follow-up checks cover invalid catalog getters, unknown thickness, material-source fallback/precedence and required-field error context.

Validation on 2026-11-18: all 43 passed. Changed host code also passed targeted AutoCAD/Civil native-reference compilation; the unchanged LayerStandards dependency was represented by its real method signature during that isolated compile. Full plugin build and native point-label selection, alignment equations, Civil part dimensions, label placement and refresh remain workstation checks.
