# 1-SECTIONAL IMPORT

Palette label: `1-SECTIONAL IMPORT`

Primary command:

- `CLV_SECTIONAL_IMPORT`

Backward-compatible aliases retained:

- `CLV_REFERENCE_IMPORT`
- `CLV-REFERENCE-CSV-IMPORT`
- `CLV-SECTIONAL-CSV-IMPORT`
- `SURVEY-REFERENCE-CSV-IMPORT`

## Purpose

Imports the new Prompt 1 sectional/control framework CSV. This is the first import step for maps where sectional/control data is built before the parcel boundary.

The importer expects the Prompt 1 CSV fields:

```text
FeatureID,FeatureType,ReferenceClass,Layer,Segment,Type,StartX,StartY,EndX,EndY,Bearing,Distance,OriginalDistance,AdjustedDistance,LengthAdjustment,AdjustmentMethod,CurveDirection,CurveBuildMethod,Radius,Delta,ArcLength,Tangent,ChordBearing,ChordLength,RoadName,FromPoint,ToPoint,MonumentName,MonumentType,CoordinateAnchor,AnchorMethod,AnchorSourceSegment,AnchorSourcePoint,GeometryBuildMode,SourceSheet,SourceLabel,CorrectedLabel,Confidence,ImportStatus,QAStatus,Notes
```

## Distance behavior

For ordinary Prompt 1 rows, `AdjustedDistance` controls the CAD geometry when supplied. If `AdjustedDistance` is blank, the importer uses `Distance`.

For outer sectional/control traverse rows, add `ControlRole=OUTER_CONTROL_TRAVERSE` in `Notes` or include `OUTER_CONTROL_TRAVERSE` / `MAIN_CONTROL_TRAVERSE` in `QAStatus`. The importer will then chain-build those rows from endpoint to endpoint and apply a distributed length-only closure correction so the CAD geometry closes at 0.00 while holding all bearings true.

`OriginalDistance`, `AdjustedDistance`, `LengthAdjustment`, and `AdjustmentMethod` are preserved for QA/reporting so the CAD import does not hide where lengths were adjusted to close.

## GeometryBuildMode

Supported values:

- `BEARING_DISTANCE` - build from `StartX` / `StartY` plus `Bearing` and `AdjustedDistance` when present; otherwise `Distance`.
- `COORDINATE` - build from `StartX` / `StartY` to `EndX` / `EndY`.
- `INTERSECTION_COORDINATE` - build from shared node coordinates.
- `OFFSET` - draw from exported coordinates and report offset/QA fields.
- `CURVE_TANGENT` / `CURVE_RADIAL` - create curve rows where enough start/end/radius data is available.
- `QA_COORDINATE` - draw coordinate geometry on QA layers for visual review.
- `UNKNOWN` - insufficient geometry source; review manually.

## Layers

For `1-SECTIONAL IMPORT`, created linework is intentionally placed on layer `0` regardless of the CSV `Layer` value. The CSV layer/confidence/QA fields are still used for reporting and warnings.

Rows marked `IMPORT_QA_LAYER`, `MANUAL_REVIEW`, `UNCERTAIN`, `APPROXIMATE`, or with conflict/review QA statuses are reported as QA/warnings, but the created linework remains on layer `0`.

## Labels

Automatic labels are limited to the `RoadName` field only. Segment numbers, bearings, distances, widths, and QA text are not placed as drawing labels. Users should add normal CAD line labels when they want to verify the actual imported geometry.

## Report

The importer writes a `_sectional_import_report.txt` beside the selected CSV. The report lists imported geometry, skipped rows, QA warnings, bearing/coordinate conflicts, forced closed-traverse row counts, initial closure error, final closure error, and Prompt 1 length-only closure adjustments.

Older Prompt 3 reference CSVs remain importable through the same command aliases for backward compatibility.

## 2026-07-08 Endpoint-Control Update

For Prompt 4 sectional boundary CSV files, `1-SECTIONAL IMPORT` now treats resolved parent boundary, split-point, intersection, coordinate, and QA-coordinate rows as endpoint-controlled geometry. When `StartX`, `StartY`, `EndX`, and `EndY` are populated, those rows are drawn directly from CSV endpoint to CSV endpoint instead of being rebuilt from rounded bearing/distance text. Legacy rows explicitly marked `GeometryBuildMode=BEARING_DISTANCE` still build from bearing/distance. `POINT_TIE` rows with identical start/end coordinates are imported as DBPoint geometry instead of being skipped.


## Prompt 4 Rev 3 Clean Final Import CSV

Prompt 4 Rev 3 may output a simplified `MapName_Prompt4_Final_Import.csv` with this header:

`FeatureID,FeatureType,Layer,Segment,Type,ImportRole,LineLabel,FromPoint,ToPoint,StartX,StartY,EndX,EndY,ImportStatus,QAStatus,Notes`

For this clean final import format, `1-SECTIONAL IMPORT` uses only `StartX,StartY -> EndX,EndY` for LINE rows. Bearing and distance fields are intentionally not required. Rows marked `DO_NOT_IMPORT`, `REVIEW_ONLY`, `SOURCE_ONLY`, or `QA_ONLY` are skipped. Point rows may use `POINT_TIE`, `POINT_MARKER`, `CONTROL_POINT`, or `POINT` type when Start/End coordinates match.
