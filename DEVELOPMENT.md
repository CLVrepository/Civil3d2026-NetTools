# Development Guide

## Prerequisites

- Windows development environment with AutoCAD 2026 / Civil 3D 2026 installed.
- .NET SDK capable of building `net8.0-windows` projects.
- Access to Autodesk and Civil 3D managed assemblies referenced by `CLV_CivilTools/CLV_CivilTools.csproj`.
- Access to CLV shared CAD support paths for blocks, layer files, templates, LISP helpers, and deployment resources.

## Repository Layout

```text
CLV_CivilTools.slnx
CLV_CivilTools/
  Aec/
  Docs/
  Gis/
  Help/
  LegalDescription/
  PdfRenderHost/
  PdfViewer/
  PoinCloud/
  Reference/
  Shared/
  Survey/
  Ufls/
  CHANGELOG.md
  CLV_CivilTools.csproj
  COMMAND_INDEX.md
  PROJECT_MAP.md
```

## Build Baseline

The main Civil 3D plugin project uses:

- SDK-style project format.
- `net8.0-windows` target framework.
- `UseWindowsForms=true`.
- `PlatformTarget=x64`.
- nullable reference types enabled.
- implicit usings enabled.
- Autodesk/Civil host assemblies referenced with copy-local disabled.

A successful development build copies the generated DLL to `C:\Temp\C3DDev\` with a randomized suffix so the assembly can be loaded into Civil 3D without file-lock conflicts.

## Suggested Validation

For documentation-only changes:

```bash
git diff --check
```

For source changes, run the closest available build from a Windows machine with the required Autodesk assemblies installed:

```powershell
dotnet build CLV_CivilTools.slnx -c Debug
```

When validating inside Civil 3D:

1. Start AutoCAD 2026 / Civil 3D 2026.
2. Use `NETLOAD` to load the development DLL from `C:\Temp\C3DDev\`.
3. Open the affected palette or run the affected command aliases from `CLV_CivilTools/COMMAND_INDEX.md`.
4. Verify command prompts, keyword accelerators, layer/resource paths, output geometry, and cleanup behavior for the modified workflow.

## AutoCAD Keyword Prompt Rule

Every `PromptKeywordOptions` / `GetKeywords` prompt must use unique accelerator letters for all options shown in that prompt. Encode the accelerator in the actual registered keyword token, not only in a display label. For example, use registered tokens such as `Same` and `seParate` when the desired accelerators are `S` and `P`.

Before release, verify:

- each one-letter accelerator works;
- each full typed keyword works;
- `StringResult` maps to the intended internal option;
- no two options in the same prompt share the same accelerator.

## LISP and External Resource Policy

Referenced LISP helpers must use the shared server folder:

```text
\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Lisp\Lisp\...
```

Do not introduce a local helper-copy deployment pattern unless explicitly requested. If a new LISP routine is created or required, document it in the change summary so it can be placed on the shared server path.

## Documentation Updates

When a change affects behavior, commands, palette placement, deployment, or operator workflow, update the matching documentation:

- `CLV_CivilTools/PROJECT_MAP.md` for technical implementation and architecture notes.
- `CLV_CivilTools/COMMAND_INDEX.md` for command aliases and palette access.
- `CLV_CivilTools/CHANGELOG.md` for user-facing release notes.
- `CLV_CivilTools/Docs/` for feature-specific operator instructions.

Keep documentation entries factual and scoped to the actual change. Avoid duplicating large blocks of historical notes unless necessary for context.

## Release Package Expectations

When preparing a source package, include all new and modified files plus any unchanged files required to keep the project structure working. Do not include generated `bin/` or `obj/` artifacts in source packages unless explicitly requested.

## Non-Goals Without Explicit Direction

- Do not convert palettes from WinForms to WPF.
- Do not rename existing command aliases or palette entries for cleanup only.
- Do not rename the `PoinCloud` folder.
- Do not replace server-hosted LISP or shared-resource pathing with local copies.
- Do not make broad formatting-only edits to application source files.
