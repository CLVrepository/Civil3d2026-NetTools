# GIS appearance capture regression tests

This zero-package console runner links the actual production
`CLV_CivilTools/Gis/GisNewDrawingAppearance.cs`. It has no Autodesk dependencies
and does not copy/reimplement the production helper.

From the repository root with a .NET 8 SDK/runtime:

```sh
dotnet run --project Tests/GisNewDrawingAppearance.Tests/GisNewDrawingAppearance.Tests.csproj
```

If only .NET 10 is installed (including on the native validation computer):

```sh
dotnet run --project Tests/GisNewDrawingAppearance.Tests/GisNewDrawingAppearance.Tests.csproj -p:TargetFramework=net10.0
```

Exit code is zero only if every assertion passes. No test framework packages,
AutoCAD session, network data, installation, or publication are required.

## Regression coverage

- ByLayer, ByBlock and Foreground never invoke ACI/RGB getters. ACI reads only
  the index; RGB reads only its channels. Inapplicable delegates throw if called.
- All five color modes remain distinct, including ACI 0/256 versus inherited
  modes. RGB bytes, named color/book identity, whitespace, case, empty names and
  independent presence flags are retained without reading absent names.
- Unknown modes, out-of-range ACI values and a present name returning null fail
  closed, rather than becoming a default snapshot.
- All 16 transparency flag combinations are checked. Exactly one method with
  IsInvalid=false is accepted. Inherited modes never read Alpha; explicit alpha
  0, 128 and 255 are exact and distinct from each other and inherited values.
- Plain dashes and shape elements never invoke TextAt. Text elements read it
  once and preserve their exact text. Invalid shape/style combinations and null
  text fail closed.
- Simulated actual getter failures retain the precise property stage and the
  original exception, including individual RGB channels, Alpha and nested
  linetype classification/TextAt. ReadAt success preserves its original result.
- Resource comparisons retain exact canonical values independently of display
  text. Strict source verification compares description and symbol-name values
  ordinally. Cross-drawing verification ignores only Description values and
  compares SymbolName values with OrdinalIgnoreCase, without trimming.
- Missing, extra, duplicate, invalid, or differently classified properties fail
  closed in both modes, including description fields. Property order does not
  matter, field names remain exact, and all field differences are returned.
- Pattern length, dash count, alignment, shape/text payloads, font filenames and
  paths, font descriptors, FlagBits (including 64), color, transparency, and layer
  state remain exact. A built-in resource name does not bypass these checks.
- Diagnostic text is quoted, escaped and truncated without changing comparison
  values. Entity symbol keys use invariant uppercase only; resource values keep
  their original spelling for strict source checks.

## Verification boundary

These portable tests prove the pure helper's gating, snapshot and field-comparison
behavior. They use simulated native failures and the same ReadAt adapter pattern as
GisNewDrawingClone; they do not compile or execute that Autodesk-facing class.
They therefore do not verify Autodesk enum values, the availability or safety of
native name-presence flags, color reconstruction, native linetype style/shape
classification, correct adapter field names/roles, capture completion, clone
equivalence, OD, entity geometry, rollback, saving, or deployment. Native build and controlled Civil 3D capture
trials remain required, especially for the reported eInvalidKey and Continuous
resource-collision failures. Passing helper tests alone does not establish which
native source/template fields differ or prove successful cloning.
