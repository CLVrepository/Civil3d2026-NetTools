# Legal Description Course Review and Relationship Fields

## Course wording fields

Each TIE or BOUNDARY row now has separate controls for:

- **Travel relationship** — ALONG, CONTINUING ALONG, DEPARTING, and related movement wording.
- **Travel feature / reference** — the right-of-way, centerline, lot line, section line, or other feature used by the travel relationship.
- **Travel wording order** — `BEFORE GEOMETRY` or `AFTER BEARING`. The latter supports City Surveyor wording where the bearing appears before the relationship and the distance follows it.
- **Destination clause** — `TO A POINT ON` or `TO THE INTERSECTION WITH`.
- **Destination feature / reference** — the feature reached at the end of the course.
- **Additional context**, **Custom prefix**, **Custom suffix**, and **Geometry text override** remain available for unusual descriptions.

## Synchronized review highlighting

Selecting a row in the course grid:

1. Draws a temporary magenta, heavier transient overlay on the linked source LINE or ARC.
2. Highlights the corresponding generated course sentence in yellow in the line-by-line review pane.
3. Scrolls the selected text into view.

The transient overlay does not modify the source entity's layer, color, lineweight, or other database properties. Selecting another row or loading another session removes the prior overlay.

## Linked MText synchronization
When linked legal-description MText exists, selecting a course also creates a temporary transient clone over each linked MText. The matching course call is displayed in magenta and underlined. The database MText is not edited, and the transient is removed as soon as another course is selected or the palette loads another session.
