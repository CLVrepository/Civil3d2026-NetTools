# Point inverse regression tests

Run `dotnet run --project Tests/PointInverse.Tests/PointInverse.Tests.csproj`.

Links the production PointInverseMath.cs directly. Checks cover record/survey near-coincidence, zero horizontal run, missing Z, reversal, cardinal/quadrant bearings, DMS rollover and snapshot formatting. Host selection, palette callbacks, native table insertion, transient cleanup and Undo require Civil 3D acceptance; see CLV_CivilTools/Docs/POINT_INVERSE.md.
