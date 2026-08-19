# Q4 Survey Label / Mapping Tools

## LABEL > DIMENSIONS > LINES AND CURVES

### 2-POINT
Command: `SURVEY-LC-LABEL-2POINT` / `2-POINT` / `Q42POINT`

Ensures layer `V-LABL` (yellow, Continuous, plot style S), launches Civil 3D `ADDLINEBETWEENPOINTS`, restores the user's previous current layer after the command ends/cancels/fails, then moves the newly created label object(s) from the Civil 3D default layer such as `C-ANNO` to `V-LABL` after placement. This is intended for the Line and Curve label type "Line Between Two Points" using label style `R26_Bearing + Distance` through Civil 3D command defaults.

### BEARING AND DISTANCE
Command: `SURVEY-LC-LABEL-BEARING-DISTANCE` / `BEARINGANDDISTANCE` / `Q4BDIST`

Ensures layer `V-LABL` (yellow, Continuous, plot style S), launches Civil 3D `ADDSEGMENTLABEL`, restores the user's previous current layer after the command ends/cancels/fails, then moves the newly created label object(s) from the Civil 3D default layer such as `C-ANNO` to `V-LABL` after placement. This is intended for the Line and Curve label type "Single Segment" using label style `R26_Bearing + Distance` through Civil 3D command defaults.

## MAPPING > BOUNDARY

### DRAW TIE LINE
Command: `SURVEY-DRAW-TIE-LINE` / `DRAWTIELINE` / `Q4TIE`

Sets current layer to `V-CTRL-TIES-LINE` using the CLV layer standard (yellow, HIDDEN3, XS-60) and starts AutoCAD LINE.

### AREA SF LABEL
Command: `SURVEY-AREA-SF-LABEL` / `AREASFLABEL` / `Q4AREASF`

Prompts for a point inside a closed boundary, uses AutoCAD boundary tracing, calculates the traced area, and places a centered MText label in square feet on the survey area label layer.

- DRAW TIE LINE now restores the user's previous current layer after the native LINE command ends or is cancelled.
