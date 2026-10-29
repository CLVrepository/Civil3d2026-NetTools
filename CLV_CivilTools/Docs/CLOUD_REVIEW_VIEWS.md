# Cloud views starter: plan and oblique

This tool provides coordinated navigation for inspecting a wash, basin, bank,
curb or other point-cloud feature. It does not extract points or build breaklines.
The first version uses two tiled Model-tab views: 35% plan on the left, 65%
orthographic oblique on the right. Plan remains north-up.

## Use

1. Finish any grid/slice review with its existing End/Restore command. Start on
   the Model tab, zoomed to the work area. Existing manual cloud crops remain.
2. Open Q3 > ROADWAY > CLOUD VIEWS - TEST > PLAN + OBLIQUE, or run `CGVIEWS`.
3. Pick a focus location. Cloud NODE acquisition is enabled temporarily. A
   visual pick is also allowed because this only positions the views. Confirm
   the proposed focus elevation, or enter a more appropriate ground elevation.
   Especially in plan, an unsnapped pick can have Z=0: do not accept that if
   your work is at a different elevation. Esc at either prompt cancels setup.
4. Click inside a pane before navigating or drawing there. Pan and zoom each
   pane independently; changes to drawing geometry appear in both.
5. SET FOCUS (`CGFOCUS`) picks a new shared center from the active pane and
   confirms its elevation. It retains each pane's zoom and the right pane's
   current viewing direction/twist. Activate the desired pane before invoking
   the command; switching panes during a focus prompt cancels that focus change.
6. ORBIT FOCUS (`CGORBIT`) recenters both panes, activates the right pane,
   clears selection and starts native `3DORBITCTR` around the exact shared WCS
   focus. Drag to orbit; Esc exits orbit. The left pane stays north-up.
   Ordinary Shift-wheel orbit is still native AutoCAD navigation and does not
   receive this command's explicit focus override.
7. RECENTER (`CGVIEWCENTER`) returns both panes to the last focus without
   picking another point. Ordinary pans, zooms and orbit gestures never
   automatically move or rotate the plan pane.
8. END VIEWS / RESTORE (`CGVIEWEND`) restores the previous viewport arrangement,
   including the original active pane/UCS during the live session. Run it before
   returning to the ground-grid commands. Do not combine this starter with other
   slice, crop or viewport-arrangement tools in the same active session.

The elevation prompt defines a camera target, not a measured survey observation.
No lowest-return sampling, automatic point correction, focus marker, direction
arrow, third section pane or new tracing tool is included. Existing drawing
commands remain available after exiting native orbit. Dense clouds may render
more slowly in two views; each pane can be zoomed independently.

## Return configuration and recovery

Before replacing views, the command saves every existing active tiled viewport
as a unique named configuration `CLV_CLOUD_VIEWS_RETURN_<GUID>` and prints its
name. Other named viewport configurations are retained. Cloning the saved
records restores their settings, including saved per-viewport UCS and visual
style settings; the live session additionally restores its entry active UCS.
The tool never edits point-cloud entities, definitions/files, crop boundaries,
visibility, layers, surfaces, point styles or drawing geometry.

If the drawing is saved and reopened while split, run `CGVIEWEND` to restore its
one retained return configuration. Live focus and original active-pane identity
are not persisted. Then run `CGVIEWS` to begin a new session. With multiple
retained configurations, the tool lists their names and requires deliberate
native `-VPORTS` > Restore selection. No automatic restore occurs during close.

Changing the pane arrangement externally makes focus/orbit commands refuse to
operate until `CGVIEWEND`. Undo can remove the return configuration while the
in-memory session still exists; `CGVIEWEND` then clears that stale session and
leaves the current arrangement unchanged. `-VPORTS` > Single is the native
fallback if a saved return configuration is no longer available. The tool does
not promise persistence of a focus or synchronization with Undo history.

## Validation

Targeted compilation against installed AutoCAD/Civil 3D 2026 host references
includes the new view commands, edited grid commands, all existing grid partials,
actual crop/view helpers, Q3 palette and PalettePositionHelper. Passed with zero
warnings/errors. This is not a full solution build or a live Civil 3D test.

Native acceptance remains required on a representative drawing copy:

- Start with one viewport, then an existing multi-viewport configuration. Confirm
  left plan/right oblique, chosen XYZ at both centers, no clipping or cloud crop
  changes, and no point/surface/style/geometry changes.
- Pick a real cloud NODE focus at survey coordinates; verify the printed WCS
  easting/northing/Z and orbit pivot. Test an approximate plan pick with an
  explicitly entered ground Z; check a non-world entry UCS as well.
- Zoom the panes differently, orbit right, then SET FOCUS from each pane in
  separate commands. Verify both recenter, zooms and right direction/twist stay,
  left remains north-up, and the previously active pane remains active.
- Pan either pane without any movement in the other. Test RECENTER and ORBIT
  FOCUS; Esc must exit native orbit normally and ordinary commands must work.
- Cancel both focus prompts, and test switching panes during a prompt. Verify
  no focus/arrangement change and restored OSMODE, 3DOSMODE, OSNAPZ, ORTHOMODE
  and SNAPMODE in the input pane. No UCS changes are made by focus picking.
- END VIEWS must restore the previous count, boundaries, orientation, zoom,
  styles and active UCS. Repeat start/end at least three times. Existing named
  configurations must remain intact; only this session's backup is removed.
- Test saving/reopening split view and CGVIEWEND recovery, two drawings with
  separate sessions, changed viewport layouts, Undo of setup, Undo of end, and
  normal save/close. Close performs no database or native viewport operations.
- Grid review must refuse while these views are active. After END VIEWS, verify
  the accepted grid review/end workflow still works. CGVIEWS must refuse active
  grid review clones or view clipping rather than disturbing that review.

## Autodesk API references

- [Tiled viewport creation and update](https://help.autodesk.com/cloudhelp/2026/ENU/OARX-DevGuide-Managed/files/GUID-2CEED409-0E15-4F48-9AA1-D12D246E27DB.htm)
- [3DORBITCTR](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Core/files/GUID-85427F3B-0880-4B28-9E26-85BE84FFEB80.htm)
- [Absolute WCS coordinate entry](https://help.autodesk.com/cloudhelp/2026/ENU/AutoCAD-Core/files/GUID-AE225073-2ECD-41CC-9B8A-61743E8CFBA9.htm)
