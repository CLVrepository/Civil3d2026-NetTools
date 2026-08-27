# Project History and Durable Decisions

This is a compact continuity record for the migrated ChatGPT Project. It intentionally captures decisions that may not exist in the 2026-06-15 website snapshot.

## Knowledge Base / menu organization
- The CLV Civil Tools Knowledge Base was built as a work reference for custom CAD/Civil 3D commands, workflows, standards, and support material.
- Established visual direction: blue CLV theme, large search, CLV seal/logo assets, tool-family navigation, and dedicated quick-tip/HOWTO pages.
- Q-menu organization has evolved. Common tool families include UFLS, GIS, Point Cloud, Survey/Mapping/Labels, plus the main CLV MENU.
- Frequently discussed tools include CREATE/UPDATE LEGEND, OBJECT HIGHLIGHT RED/GREEN, PIPE INFO @ POINT, PDF VIEWER, PDF CLIP, DRAW TIE LINE, XREF COLOR, OFFSET TO TEMP LAYER, and LINEWORK REVIEW.

## Naming changes made after earlier versions
The project evolved away from some older names. Use the newest terminology when it applies:
- **SURVEY MAP → TRANSFORM**
- **BEST FIT MAP → MAP TRANSFORM**
- **TRANSFORM TO CONTROL → BOUNDARY TRANSFORM**
- **BOUNDARY CLOSURE → BOUNDARY**
- BOUNDARY REVIEW was moved/reorganized during the boundary-tool cleanup.

Older HTML in the June snapshot may still say BEST FIT MAP. Treat that as historical documentation unless Mark says the production UI still uses it.

## Map Transform / Transform workflow
Established workflow for the transform tool:
1. Select the map/linework first.
2. Build control pairs by selecting the **survey destination point first**, then the corresponding **map starting point**.
3. Repeat for multiple pairs.
4. Review pairs with live error feedback.
5. Finalize the transform.
6. Best-fit/transform data has used a `BestFit` XML file in the drawing folder.

Later requested enhancement: the command should be editable/re-runnable rather than one-and-done. See `CURRENT_WORK.md`.

## Survey Legend tool
The legend is data-driven from `SurveyLegend.csv`, with fields including:
- Category
- Block
- Menu Description
- Spacing Type
- Sort Order

Durable layout rules discussed:
- `SURV_LEG_HEADER` first.
- Header → first item spacing: **0.2671**.
- Single → Single: **0.3054**.
- Single ↔ Double: **0.39**.
- Double → Double: **0.4746**.
- Numeric sort order is used.
- Menu location was established under Q4 / LABEL / LEGEND in the relevant generation.
- Later legend entries included SECTION LINE, QUARTER SECTION LINE, and SIXTEENTH SECTION LINE.

## Boundary Generator / legal workflows
A GPT-assisted boundary generator was developed/discussed with these durable requirements:
- Target subdivision/detail: **1/16 per section** rather than the older 1/64 approach.
- Output boundary geometry should be **lines/arcs only**.
- Merge consecutive segments with the same bearing where appropriate.
- Outer section line only once on `V-PROP-SECT`.
- No unnecessary points or labels in generated boundary geometry.
- Street names and POC may be labeled when supplied.
- Closure adjustment should be **distance-only** when that is the selected strategy.
- Perform a reverse-build/check.
- Support a legal-description-only case.
- A compact `00_GPT_INSTRUCTIONS` file was targeted at under 8,000 characters for portable instructions.

## Legal Description Tool
Established design goals include:
- Line-by-line editor.
- ALL CAPS legal output.
- Templates for COMMENCING, POINT OF BEGINNING, closing language, and area.
- Numeric area formatted with commas.
- POC/POB tie handling.
- Tangent/non-tangent detection.
- PRC checks and radial callouts.
- MText placement with auto-update behavior.
- DOCX export requested.
- Header fields such as BY / PR BY / PAGE right aligned.
- AERIAL 12 font was specified in the relevant document/output context.
- Distance-only closure adjustment and reverse-build checking.
- Lines/arcs-only geometry and legal-only scenarios.

## PDF Viewer
A custom PDF Viewer concept/project has included:
- Palette-based PDF viewing tied to Civil 3D/model pan and zoom.
- Multiple sheets.
- Plan/profile toggle.
- Named mapping.
- Pinned tables.
- Polygon boundary highlighting.
- A **return to current** control.
- Lists should show all relevant mapped items rather than hiding items unexpectedly.

## Xref path maintenance
Project folder numbering/path structure changed from an older `10000–100999` style to ranges including `100000–100499` and `100500–100999`. A LISP/tool was requested to update legacy xref paths automatically. A prior attempt did not find expected paths, so this should be treated as an unresolved implementation area unless newer source is provided.
