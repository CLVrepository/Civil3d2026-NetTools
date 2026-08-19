# Legal Description Verbiage Library

- Editor remains one course per line for review.
- Final linked MText combines each section into paragraph form and preserves blank lines between the introductory paragraph, legal calls, and area statement.
- All built-in office styles output ALL CAPS.
- Use DESCRIPTION OPTIONS to enter the introductory/situate paragraph, POC description, POB description, or an area-statement override.
- `Reference/LegalDescriptionTextStyles.json` defines reusable office wording presets and is embedded into the DLL during build.

Supported tokens:
- `{POC_DESCRIPTION}`
- `{POB_DESCRIPTION}`
- `{COURSE_TEXT}` in `LastTieTemplate`
- `{AREA_SF}` rounded to whole square feet
- `{AREA_SF_2}` shown to two decimal places
- `{DIRECTION}`, `{RADIUS}`, `{DELTA}`, `{LENGTH}` in curve templates

Initial presets:
- CLV Old Standard: final tie ends `TO A POINT, SAID POINT BEING THE POINT OF BEGINNING;`
- True POB
- Direct POB
