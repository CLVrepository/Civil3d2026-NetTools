# Aerial layer identity regressions

Run `dotnet run --project Tests/AerialLayers.Tests/AerialLayers.Tests.csproj` from the repository root. No Autodesk host or NuGet packages are required.

Exercises the production identity rule, historic date/LasVegas labels, URI-encoded resource identifiers, generic/renamed layer names, tool group names/legends and unrelated GIS exclusions. This does not execute Map layer removal, repaint, modeless command dispatch or native load/view restoration; see `CLV_CivilTools/Docs/Q2_GIS_AERIALS.md` for live checks.
