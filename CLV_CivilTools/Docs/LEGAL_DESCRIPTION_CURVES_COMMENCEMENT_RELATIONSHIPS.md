# Legal Description Curves, Commencement, and Relationships

## Scope
This update intentionally leaves Land Location as the existing manual introductory/situate paragraph. It adds automatic curve analysis, independent commencement wording, and reusable line relationships.

## Curve analysis
Each ARC is compared with its adjacent ordered courses. The tool classifies it as TANGENT, NON-TANGENT, COMPOUND, or REVERSE using a one-minute angular tolerance. It also calculates concavity, radial bearing at the beginning, chord bearing, and chord length. The selected legal text style supplies the applicable wording template.

Automatic classification is a drafting aid and must be professionally reviewed, especially where record intent differs from mathematical geometry.

## Description Options
The dialog now independently selects:
- Same POC/POB wording
- Commencement wording
- Final tie/POB wording
- Boundary return wording

A POC relationship field supports templates such as SAME BEING or ALSO BEING.

## Course relationships
Each tie or boundary course has a Relationship dropdown and Feature / Reference field. Prefix relationships are inserted before calculated geometry. Destination relationships such as TO A POINT ON are appended after the geometry.

The original Prefix, Along / Context, Suffix, and Override fields remain available for unusual calls.

## Embedded phrase library
`Reference/LegalDescriptionPhraseLibrary.json` is compiled into the DLL. Modify the source JSON and rebuild the DLL to change approved wording. No separate JSON deployment is required.


## Bidirectional curve tangency
Each curve is evaluated independently at its incoming and outgoing endpoints. The palette displays `Curve IN` and `Curve OUT`. Closed boundaries use the last/first course relationship when a curve occurs at the traverse wrap. A non-tangent incoming connection includes the radial bearing to the curve beginning. A non-tangent outgoing connection appends the radial bearing to the curve ending. Reversing the traverse recalculates both classifications and swaps the applicable beginning/ending radial call automatically. Radial bearings are reported from the curve center outward to the curve point.


## PRC and PCC tangency
Arc-to-arc connections are evaluated from the radial lines at the common endpoint. Collinear radial lines indicate tangency. Radials in the same direction classify the connection as COMPOUND (PCC); radials in opposite directions classify it as REVERSE (PRC). Neither condition generates a non-tangent radial call.
