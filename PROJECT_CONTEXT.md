# Project Context

## Repository Purpose

`Civil3d2026-NetTools` contains CLV Civil Tools, a managed .NET toolset for AutoCAD 2026 / Civil 3D 2026. The project replaces selected legacy LISP and manual drafting workflows with Civil 3D commands and palette-driven WinForms tools while keeping familiar Q1/Q2/Q3/Q4 operator workflows.

## Current Application Shape

- Solution: `CLV_CivilTools.slnx`.
- Main project: `CLV_CivilTools/CLV_CivilTools.csproj`.
- Target framework: `net8.0-windows`.
- Platform target: x64.
- Nullable reference types and implicit usings are enabled.
- Primary UI pattern: WinForms controls inside AutoCAD `PaletteSet` windows.
- Development build output is copied to `C:\Temp\C3DDev\` with a randomized DLL filename for Civil 3D `NETLOAD` testing.

## Major Feature Areas

- `CLV_CivilTools/Ufls/`: Q1 UFLS check, adjust, label, GIS-prep, pipe, structure, lateral, redline, and layer-maintenance workflows.
- `CLV_CivilTools/Gis/`: Q2 GIS, aerial, coordinate-zone, section-reference, cleanup, object-data, sewer/storm, and cache-finalize workflows.
- `CLV_CivilTools/PoinCloud/`: Q3 point-cloud roadway and UFLS-support workflows. The folder name is intentionally spelled `PoinCloud` in this snapshot.
- `CLV_CivilTools/Survey/`: Q4 survey mapping, labeling, closure, transform, legend, PLSS, photo-review, xref-color, and linework-review workflows.
- `CLV_CivilTools/LegalDescription/`: Legal-description traverse, wording, linked MText, DOCX export, and drawing-persistent session workflows.
- `CLV_CivilTools/PdfViewer/` and `CLV_CivilTools/PdfRenderHost/`: PDF viewing, calibration, synchronization, and rendering support.
- `CLV_CivilTools/Shared/`: shared geometry, layers, palette placement, pipe catalog, selection, snap, view, and CSV import utilities.
- `CLV_CivilTools/Reference/`: source reference data and templates that are embedded or used by specific workflows.
- `CLV_CivilTools/Docs/`: feature-specific operator notes, plans, and prompt/workflow documentation.

## Documentation Map

- `PROJECT_INSTRUCTIONS.md`: concise repo-level instructions for AI-assisted and human contributors.
- `PROJECT_CONTEXT.md`: current project overview and architectural context.
- `DEVELOPMENT.md`: local development, validation, and release-packaging guidance.
- `CLV_CivilTools/PROJECT_MAP.md`: detailed architecture history and current implementation notes.
- `CLV_CivilTools/COMMAND_INDEX.md`: active command list, aliases, palette locations, and command purposes.
- `CLV_CivilTools/CHANGELOG.md`: chronological user-facing change history.

## Key Constraints

- Preserve the WinForms PaletteSet approach.
- Preserve Civil 3D 2026 / AutoCAD 2026 host assumptions.
- Keep Autodesk host DLL references as host-provided references with copy-local disabled.
- Avoid unrelated application-code edits when updating documentation.
- Use server paths for LISP and shared CAD support assets unless a task explicitly changes deployment design.
- New PaletteSet hosts should use `Shared/PalettePositionHelper.cs` to avoid stale off-screen palette placement.

## Change Workflow

1. Inspect relevant documentation and source files before editing.
2. Identify the smallest safe change that satisfies the task.
3. Keep documentation and command indexes synchronized with behavior changes.
4. Build or run the closest available validation checks when application code changes.
5. Note environment limitations when full Civil 3D validation is not possible outside AutoCAD/Civil 3D.
