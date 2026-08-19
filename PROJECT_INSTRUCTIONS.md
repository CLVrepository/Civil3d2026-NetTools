# Project Instructions

Use these streamlined instructions when working on the CLV Civil Tools repository.

## Operating Rules

- Inspect the existing repository before changing files.
- Preserve the current Civil 3D 2026 architecture unless the requested task explicitly changes it.
- Do not make unrelated changes, broad formatting passes, or application-code edits when the task is documentation-only.
- Keep changes small, reviewable, and limited to the requested scope.
- Update project documentation whenever behavior, commands, palette layout, build requirements, or deployment assumptions change.

## Git Workflow

- Inspect the current branch and working tree before making changes.
- Keep each task limited to its requested scope.
- Review the complete diff before committing.
- Keep commits focused and use a descriptive commit message.
- Do not push, create, or merge pull requests unless explicitly requested.
- Do not discard existing user changes.

## Required Context Files

Before changing application code, review:

1. `PROJECT_CONTEXT.md` for the current project summary, architecture, constraints, and documentation workflow.
2. `DEVELOPMENT.md` for environment, build, validation, and release-packaging guidance.
3. `CLV_CivilTools/PROJECT_MAP.md` for current architecture notes and recent technical changes.
4. `CLV_CivilTools/COMMAND_INDEX.md` for command names, aliases, palette locations, and command purposes.
5. `CLV_CivilTools/CHANGELOG.md` for recent user-facing changes.
6. Relevant source files in the feature area being modified.

## Architecture Guardrails

- Target host: AutoCAD 2026 / Civil 3D 2026.
- Project type: managed .NET class library loaded into Civil 3D.
- UI standard: WinForms controls hosted in `Autodesk.AutoCAD.Windows.PaletteSet`.
- Framework/platform: `net8.0-windows`, x64, nullable enabled.
- Keep Autodesk host assembly references copy-local disabled.
- Use `Shared/PalettePositionHelper.cs` for new PaletteSet hosts.
- Do not convert the project to WPF or introduce a second UI framework without explicit direction.
- Preserve the intentional `PoinCloud` folder name.

## Coding Standards

- Avoid ambiguous type references such as `Color`, `Exception`, `Font`, and `SaveFileDialog`; qualify namespaces when needed.
- Do not wrap imports/usings in try/catch blocks.
- For AutoCAD keyword prompts, every option in a single prompt must have a unique accelerator letter.
- Register the unique accelerator in the actual keyword token passed to `PromptKeywordOptions.Keywords.Add(...)`, not only in display text.
- Verify one-letter accelerators, full typed keywords, and `StringResult` mapping for every new or changed keyword prompt.

## LISP and Shared Resources

- Referenced LISP routines must load from the shared server path, not a local helper copy:
  `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\...`
- Call out any new LISP routine explicitly so it can be copied to the shared server location.
- Shared blocks, layer files, templates, and other resources should keep existing server-path conventions unless explicitly changed.

## Documentation Expectations

When application behavior changes, update the applicable documentation:

- `CLV_CivilTools/PROJECT_MAP.md` for architecture, workflow, and implementation notes.
- `CLV_CivilTools/COMMAND_INDEX.md` for commands, aliases, and palette routing.
- `CLV_CivilTools/CHANGELOG.md` for user-facing changes.
- Feature-specific documentation in `CLV_CivilTools/Docs/` when a workflow needs operator instructions or release notes.

Documentation-only changes do not require application-code edits.
