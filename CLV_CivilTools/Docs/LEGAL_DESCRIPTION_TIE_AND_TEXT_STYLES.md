# Legal Description — POC Tie Courses and Text Styles

## Separate POC workflow
1. Run `LEGALDESC`.
2. Select the connected boundary LINE and ARC entities.
3. Select the POB endpoint.
4. Choose `seParate` (`P`) at the POC prompt.
5. Select the POC endpoint.
6. Select the connected tie LINE and ARC entities running from the POC to the POB.

The tie must start at the selected POC and terminate at the selected POB within 0.10 foot. The palette identifies these as `TIE` courses (`T1`, `T2`, etc.) and places them before the `BOUNDARY` courses. Tie courses are not included in area or boundary closure calculations.

## Text styles
The palette includes a **Text style** dropdown. Built-in styles are:
- CLV Standard
- Formal Survey
- Compact

The project source file `Reference/LegalDescriptionTextStyles.json` contains office-specific styles and is embedded into the DLL during build. Each object can define:
- `Name`
- `BeginningSame`
- `Commencing`
- `TieCoursePrefix`
- `LastTieSuffix`
- `BoundaryCoursePrefix`
- `ReturnToBeginning`
- `LineDistanceSeparator`
- `FeetWord`
- `CurveTemplate`

Curve templates support `{direction}`, `{radius}`, `{delta}`, and `{length}` placeholders. A style with the same name as a built-in style replaces that built-in style. A new name adds another dropdown option.

After changing the JSON file, restart Civil 3D or reload the plugin so the style list is read again. Generated legal text always requires professional review.
