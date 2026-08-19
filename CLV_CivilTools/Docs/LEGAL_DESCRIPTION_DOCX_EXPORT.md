# Legal Description DOCX Export

## Workflow

1. Build and review the legal description in the line-by-line palette editor.
2. Place or update linked MText to review the final paragraph-form legal inside CAD.
3. Select `EXPORT LEGAL DOCX`.
4. Confirm APN, date, preparer, peer reviewer, explanation, basis of bearings, and exhibit statement.
5. Select the output `.docx` path.

## Output model

- The DWG stores the editable legal-description session.
- Linked MText is the live CAD review copy.
- DOCX is the formatted City Surveyor deliverable.
- TXT remains available as a plain-text utility export.

## Template

The exporter uses:

`Reference\Legal Templates\Basic Template.dotx` (project source; embedded into the DLL during build)

The template is copied into the build output. The exporter opens it as an Open XML package, changes it from a Word template content type to a normal Word document content type, replaces the approved placeholders, and packages the result as `.docx`.

Microsoft Word does not need to be running. GPT is not part of the production export.

## Formatting

- Final legal content is ALL CAPS.
- `COMMENCING`, `BEGINNING`, `POINT OF BEGINNING`, `TRUE POINT OF BEGINNING`, and `POINT OF TERMINATION` are bolded in the Word legal body.
- The City Surveyor template retains its seal, headers, footers, standard styles, and page layout.
- PAGE and NUMPAGES fields are used in headers; Word may update them when the document is opened, printed, or fields are refreshed.

## Review limitation

The CAD MText previews the legal content, not exact Word pagination, seal placement, or header/footer geometry. The exported DOCX should be opened for final page-layout review before issuance.

## Deployment

The City Surveyor DOTX and legal-description style JSON are embedded resources in `CLV_CivilTools.dll`. Only the normal Civil Tools runtime package is required; a separate `Reference` folder is not required for legal DOCX export. To update the template or seal, replace the source DOTX in the project and rebuild the DLL.

## City Surveyor template layout update

- The APN remains at the left side of the continuation header while the date is inserted at the original right tab stop.
- `BY` and `P.R. BY` are inserted at the original right tab stop rather than rebuilt as left-aligned paragraphs.
- Automatic bolding is phrase-based. `POINT OF BEGINNING`, `TRUE POINT OF BEGINNING`, and `POINT OF TERMINATION` may be bold; a generic occurrence of `BEGINNING` is not.
- Description Options provides editable values for the red-variable portions of the standard Land Description paragraph: first quarter name/code, second quarter name/code, Section, Township, and Range.
- A complete custom paragraph remains available as an override for descriptions that do not fit the standard template.

## Exact template formatting preservation

The exporter uses the embedded `Basic Template.dotx` as the actual document package. Replacement text inherits the existing run properties from the matching template paragraph, including Arial 12-point formatting. The first-page APN/date, BY, P.R. BY, and page-number paragraphs retain the template's original tab runs and tab-stop layout; only placeholder values are replaced.
