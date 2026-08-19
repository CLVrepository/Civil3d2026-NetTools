# Civil 3D PDF Viewer — Living Ultimate Plan

This file is the running feature plan for the PDF Viewer / Map Review tool. Update it as review ideas are accepted, changed, deferred, or removed.

## Product direction

- Keep the PDF in a separate dockable/floating palette rather than inserting it as a drawing overlay.
- Civil 3D remains the working window; the PDF palette is the synchronized reference window.
- Every feature should reduce clicks or save meaningful review time.

## Version 1 — Map review foundation

### Viewer and storage

- [x] WinForms `PaletteSet` compatible with the existing Civil Tools architecture.
- [x] Separate dockable/floating PDF viewer.
- [x] Multipage PDF selection and relinking.
- [x] Absolute and DWG-relative PDF paths.
- [x] Per-DWG configuration in the Named Object Dictionary.
- [x] Named sheet entries rather than page-number-only navigation.

### Plan sheet mapping

- [x] Two-point PDF-to-model-space calibration.
- [x] Translation, rotation, and uniform scale calculation.
- [x] Define mapped-sheet coverage from the current viewport, a rectangle, picked polygon vertices, or an existing closed polyline.
- [x] Automatic sheet selection using the current model-space view center.
- [x] PDF crop rendering that follows model-space pan and zoom.
- [x] Warning when no plan mapping covers the current view.

### Manual categories

- [x] Plans.
- [x] Profiles.
- [x] Tables.
- [x] Details.
- [x] Notes.
- [x] Named list selection within each category.
- [x] Previous/Next navigation within a category.
- [x] Optional pinned reference items.
- [x] Tables support for commonly referenced line and curve tables, whether they are on a dedicated sheet or another PDF page.

### Return to Current

- [x] A prominent **RETURN TO CURRENT** button.
- [x] Leaves manually selected Profiles, Tables, Details, or Notes.
- [x] Returns to Plans.
- [x] Re-enables automatic model-space following.
- [x] Selects the mapped plan sheet covering the current model-space location.
- [x] Restores the PDF crop matching the current Civil 3D view.

## Near-term improvements after field testing

- [ ] Edit an existing sheet name, page, category, pin state, and priority.
- [x] Redefine plan coverage without recalibrating the PDF page.
- [ ] Recalibrate an existing plan mapping.
- [x] Show mapping coverage temporarily in model space for setup/review.
- [ ] Add a mapping manager with duplicate/invalid mapping warnings.
- [ ] Add recently viewed sheet shortcuts if normal category navigation proves too slow.
- [ ] Add a lightweight Plan/Profile toggle after the desired linking workflow is proven.
- [ ] Support profile pages located on separate PDF sheets through a simple manual plan/profile pairing.
- [ ] Support plan and profile portions on the same sheet without requiring detailed region setup from normal users.

## Deferred / only if real use justifies it

- [ ] Bidirectional PDF-to-model-space navigation.
- [ ] Automatic Civil 3D ProfileView detection.
- [ ] Alignment/station-based profile selection.
- [ ] Individual detail identification or detailed crop setup.
- [ ] Automatic extraction of PDF sheet names/bookmarks.
- [ ] Multiple PDF files per DWG.
- [ ] View-twist-aware arbitrary rotation matching.
- [ ] GeoPDF metadata support.
- [ ] Markups or discrepancy notes.
- [ ] Transparent drawing overlay mode.

## Explicitly excluded from Version 1

- PDF underlay insertion.
- Opacity slider, because the PDF is displayed in a separate palette.
- Individual detail-region mapping.
- Automatic plan/profile switching.
- Civil 3D profile-object linking.
- LISP helpers.

## Unified sheet and reference navigator
- The category filter buttons were removed.
- Plans, profiles, tables, details, and notes are shown together in one permanent list.
- Each entry is prefixed with its category, and selecting it opens the sheet or saved reference view immediately.
- Pinned entries sort to the top of the combined list.
- RETURN TO CURRENT restores automatic model-space plan following.

## Implemented: multiple plan-sheet coverage
- Each plan sheet is independently calibrated and scaled.
- Coverage can use the current viewport, a rectangle, picked polygon vertices, or an existing closed lightweight polyline.
- Selected-polyline geometry is copied into the mapping data so deleting the source object does not break the setup.
- Curved polyline bulges are preserved for the temporary active-sheet highlight.
- EDIT BOUNDARY changes coverage without repeating PDF calibration.
- Automatic following uses point-in-polygon testing and chooses the smallest containing coverage when sheets overlap.
