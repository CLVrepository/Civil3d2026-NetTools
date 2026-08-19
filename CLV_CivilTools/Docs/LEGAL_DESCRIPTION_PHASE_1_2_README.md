# Legal Description Builder — Phases 1 and 2

## Commands

- `LEGALDESC` / `CLV-LEGAL-DESCRIPTION`: select one connected traverse made from individual AutoCAD `LINE` and `ARC` entities, choose the desired Point of Beginning endpoint, optionally identify a separate Point of Commencement, and open the legal-description palette.
- `LEGALDESC-OPEN`: reopen the most recently saved legal-description session stored in the current drawing.

## Phase 1 geometry functions

- Orders an unordered selection into one connected traverse using a 0.10-foot endpoint tolerance.
- Preserves source `LINE` and `ARC` entities; no replacement polyline is created.
- Supports reversal of the complete traverse from the palette.
- Calculates line bearings and distances.
- Calculates curve direction, radius, central angle, and arc length.
- Reports traverse length, enclosed area, forward misclosure, and an independently sequenced reverse-direction build result.
- Selecting a course row highlights its source entity; `SELECT / ZOOM COURSE` centers the view on it.

## Phase 2 text editor

Each course provides editable fields for:

- Include/exclude
- Prefix
- Along/context wording
- Suffix
- Complete geometry-text override

`REGENERATE TEXT` rebuilds the legal text from the course data. The full preview remains editable for final wording. `SAVE TO DRAWING` stores the complete session and edited text in the Named Objects Dictionary. `EXPORT TXT` writes the displayed description to a text file.

## Current scope and limitations

- The selected geometry must be one connected traverse of individual `LINE` and `ARC` entities.
- Polylines, splines, ellipses, Civil 3D parcels, and alignment entities are not converted in this phase.
- A separate POC is recorded and identified in the opening wording, but Phase 1-2 does not automatically calculate or write the commencement tie from the POC to the POB.
- Street names, intersections, PLSS callouts, monument descriptions, record documents, and adjoining ownership are entered manually in the context or override fields. Structured reference assignment is reserved for later phases.
- Generated wording is a drafting aid and requires review by the responsible survey/legal professional.

## Description option persistence and area output

Description Options now restores all previously selected phrase keys from the current drawing session. Opening the dialog to change one field will not reset untouched commencement, POB, or boundary-return wording.

Area output can be set to SQUARE FEET, ACRES, or SQUARE FEET AND ACRES. Separate precision values are retained for each unit, along with the optional “AS DETERMINED BY COMPUTER METHODS” wording. A manual area-statement override still takes precedence.
