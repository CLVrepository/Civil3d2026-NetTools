# Current / Open Work at Migration

## 1. Map Transform history / edit mode — active design issue
Most recent discussed behavior:
- Running Map Transform again should offer the prior transform/control-pair history rather than immediately forcing a brand-new survey-point selection.
- The user should be able to **uncheck/remove existing control pairs** and **add new pairs**.
- Existing group labels (such as **1, 2, 3**) should be shown again during later editing so the user knows which survey/map pair belongs together.
- The first memory/history implementation partially worked, but adding a new point after the map had already been transformed produced a very large error.

### Critical math requirement
When editing an existing transform, new control pairs must be evaluated against the **original/pre-transform map coordinate state**, not against geometry that has already been transformed. Conceptually, edit mode needs to reconstruct or temporarily reason from the original source transform state before recalculating all pairs. Otherwise the newly added map point is effectively compared in a different coordinate frame and errors become huge.

### Desired UX
Re-running the command should feel like returning to the earlier pre-finalization review state:
- recall selected map objects / transform identity where safe;
- show prior control pairs;
- show numbered grouping labels;
- allow check/uncheck;
- allow adding pairs;
- recompute from a consistent original coordinate frame;
- show live error;
- apply the revised transform once finalized.

Current source code is not included merely by this summary; obtain the latest source/project before coding if it is not separately uploaded.

## 2. Knowledge Base synchronization
The included site is dated 2026-06-15, while tool names and designs continued evolving afterward. In particular, older BEST FIT MAP / SURVEY MAP wording may need synchronization with newer TRANSFORM / MAP TRANSFORM terminology once the current production menu is verified.

## 3. Xref path updater
A utility/LISP for old-to-new project xref paths was attempted, but the search reportedly failed to find expected legacy paths. Revisit with actual sample paths/drawings and current code before considering it solved.

## 4. Civil 3D 2027
2026 remains the established primary environment in this bootstrap, but compatibility/migration to Civil 3D 2027 has been contemplated. Verify target version for any new build/deployment work.
