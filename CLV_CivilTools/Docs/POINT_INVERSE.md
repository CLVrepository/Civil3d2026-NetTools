# Point Inverse — trial build

Run POINTINVERSE or SURVEY-POINT-INVERSE, or use Q4 > MAPPING > TOOLS > POINT INVERSE.

## Pick workflow

1. Pick the record/reference COGO point's label or marker (FROM).
2. Pick the surveyed/comparison COGO point's label or marker (TO).
3. Read the point identities and comparison in the floating WinForms palette.

A dragged point label selects its own COGO point. The label's visual location and rounded label text are not measurement inputs. The code reads the actual COGO point Northing, Easting and Elevation. This works with coincident markers and separated labels; no nearest-point search is used. Exploded text, generic blocks and points inside xrefs are not supported.

If labels overlap, type N (Number) at either pick prompt and enter the exact COGO point number. Selecting the same point twice is rejected; different point identities at identical coordinates are supported. Each pick echoes its point number, raw description and coordinates.

## Results

FROM is reference/record and TO is comparison/surveyed by convention. All signed differences are TO minus FROM: positive northing = north, positive easting = east, positive elevation = higher. Horizontal offset is always a nonnegative magnitude, not a signed alignment offset. The same tool can compare two shots along a road or ramp.

The report lists point number, raw description, northing, easting and elevation for both points, horizontal offset/distance, signed N/E/Z differences, 3D slope distance, quadrant bearing, clockwise azimuth from drawing north (+Y), and grade percent (rise divided by horizontal run).

Linear values are in the Civil drawing's configured units; the feet convention is shown when applicable. There is no unit conversion, projection, geodetic bearing calculation or ground/grid reduction. Changing UCS does not change the stored coordinate calculation. Linear values and grade show three decimals; bearings show hundredths of a second. Small coordinate differences are calculated before rounding.

An unset elevation is N/A, never zero: horizontal results remain available; elevation difference, slope distance and grade are N/A. Coincident XY makes bearing and grade N/A, while vertical-only 3D distance is valid. Two distinct coincident points are allowed. The FROM/TO preview uses green/red rings and a yellow line; with any unset elevation the preview is explicitly plan-only at Z=0.

## Palette actions

- PICK NEW PAIR: select both points again.
- KEEP FROM / PICK NEXT: retain the reference and select another comparison point.
- REVERSE: exchange FROM/TO and recalculate signed differences and direction.
- REFRESH: re-read the current point objects, including edits made since the last calculation.
- COPY REPORT: re-read points and copy a tab-separated report with drawing name and timestamp.
- INSERT TABLE: pick a Model-tab insertion position and positive text height; re-read the points and create an AutoCAD table on layer 0 using the current table style.
- CLEAR: clear results and graphics.

Tables and clipboard reports are dated snapshots. They are not linked to the COGO points and do not update after later point edits. Copy, refresh and table output re-read the source rather than exporting old cached coordinates. The existing report must be replaced manually if a new snapshot is needed.

Closing the palette or changing/closing the drawing clears the session and temporary preview. Reopen the tool and pick a new pair. Selection cancellation creates no table; temporary graphics never become drawing entities. Only INSERT TABLE writes persistent drawing content.

## Validation and acceptance

The calculation/report tests passed 45/45 checks, including the 30/40/1 example, nearly coincident record/survey shots, exact overlaps, vertical-only pairs, missing elevations, reversal, all quadrants, DMS carry, signed zero, finite inputs and snapshot output. New host/UI sources compile against installed AutoCAD/Civil 3D 2026 assemblies and .NET 10/WinForms reference assemblies. Existing project framework and dependencies were not changed. Full solution build and native host interaction remain to be tested on the work computer.

Run:
dotnet run --project .\Tests\PointInverse.Tests\PointInverse.Tests.csproj
dotnet build .\CLV_CivilTools.slnx -c Debug

Then NETLOAD the newly built randomized DLL and test on a disposable drawing:
1. Pick separately dragged labels of nearly coincident record/survey points; verify correct point numbers and source coordinates.
2. Try coincident markers with Number entry. Try selecting the FROM point again and cancel at each prompt.
3. Check a known pair with delta N=30, E=40, Z=1: horizontal 50.000; 3D 50.010; grade +2.000%; bearing N 53°07′48.37″ E.
4. Reverse: magnitudes unchanged, differences/grade sign reversed, bearing S 53°07′48.37″ W.
5. Test missing elevations, vertical-only pairs and two different points with identical XYZ.
6. Move a source point, then REFRESH/COPY/INSERT; verify new source values. Existing inserted tables must remain unchanged.
7. Test a rotated UCS: coordinates/results stay the same; table placement follows the picked location.
8. Insert, cancel and Undo a table. Check special characters in raw descriptions, text height and table layout.
9. Switch/close drawings and hide the palette; verify no stale export or leftover temporary graphics.
10. Test both feet conventions and a meter drawing. Source points, labels and styles must remain unchanged.
