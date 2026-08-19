# BOUNDARY IMPORT AUTO / BOUNDARY IMPORT MANUAL

These commands import Prompt 2 standalone boundary CSV files created from the POC -> POB -> boundary workflow.

## BOUNDARY IMPORT AUTO

Palette label: `BOUNDARY IMPORT AUTO`

Command: `CLV_2_BOUNDARY_IMPORT`

This workflow uses automatic curve/radial selection where possible. It supports the standalone Prompt 2 CSV with POC/POB markers, commencement/tie rows, boundary rows, and QA rows. It does not require Prompt 1 sectional/control geometry.

## BOUNDARY IMPORT MANUAL

Palette label: `BOUNDARY IMPORT MANUAL`

Command: `CLV_2A_BOUNDARY_IMPORT_INPUT`

This workflow uses the same Prompt 2 CSV format but enables manual curve/radial preview selection for review cases.

## Notes

- Prompt 1 is review-only and no longer produces import geometry.
- Prompt 4 has been removed from the workflow.
- POC/POB marker rows with `Type=POINT_MARKER` are supported.
- Commencement/tie rows and boundary rows can be imported from the same Prompt 2 CSV.
- Rows marked `DO_NOT_IMPORT` are skipped.
