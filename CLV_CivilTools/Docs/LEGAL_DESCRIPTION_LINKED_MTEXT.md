# Legal Description Linked MText

## Purpose
The Legal Description palette keeps the editable review text one course per line. `PLACE LINKED MTEXT` converts the current review text into a single paragraph and creates MText at a user-selected point in the current drawing space.

## Link behavior
The created MText is linked to the drawing's saved legal-description session. Its handle is stored in the session, and the MText receives a `CLV_LEGAL_DESCRIPTION_LINK` extension-dictionary marker.

Linked MText is refreshed when:
- a course include/context/prefix/suffix/override field changes;
- text style or precision changes;
- `REGENERATE TEXT` is selected;
- `SAVE TO DRAWING` is selected;
- `UPDATE LINKED MTEXT` is selected;
- `REFRESH SOURCE` rereads the original source entities.

## Source geometry changes
`REFRESH SOURCE` rereads each original LINE or ARC by its stored handle. It updates endpoints, radius, delta, arc length, and curve direction while preserving the course order, include flags, and course wording fields. The legal text is regenerated and all linked MText objects are updated.

The feature does not install a permanent AutoCAD object reactor. Therefore, moving source geometry outside the palette does not rewrite MText at the exact moment of the grip edit. Use `REFRESH SOURCE`, or reopen and refresh the legal-description session, to synchronize geometry changes.

## MText formatting
- Editor: one course per line for review.
- Drawing MText: all nonblank editor lines joined into one paragraph.
- MText uses the current layer, current text style, and current `TEXTSIZE` at placement.
- Initial MText width is approximately 80 text heights and can be grip-edited normally after placement.
