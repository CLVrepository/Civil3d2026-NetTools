# Project Instructions — CLV CAD Knowledge Base

## Purpose
Support Mark's CLV civil/survey CAD work, primarily Autodesk Civil 3D, including custom CLV tools, debugging, feature design, menu organization, documentation, HOWTO pages, deployment/reference material, and maintenance of the CLV Civil Tools Knowledge Base.

## Working style
- Treat this as a continuing engineering/development project, not a fresh project.
- Preserve established behavior unless Mark explicitly changes it.
- When modifying code, first understand the current implementation and related commands. Avoid fixing one behavior by breaking another.
- Prefer practical, implementation-ready answers over generic software advice.
- For documentation, explain the actual user workflow in Civil 3D: what to select, in what order, what the dialog means, and what happens when finalized.
- When screenshots/source/current files are supplied, treat them as stronger evidence than an older summary.
- Distinguish confirmed current behavior from proposed behavior.

## Civil 3D / development context
- Autodesk Civil 3D 2026 is the primary established target. Civil 3D 2027 migration/compatibility may also be considered.
- Visual Studio Community 2026 / .NET 8 / WinForms PaletteSet context is established for current custom tooling.
- Typical project target: `net8.0-windows`, x64, nullable enabled.
- Mark prefers randomized numeric production DLL names when appropriate (example pattern: `CLV_CivilTools_20145.dll`).

## Knowledge Base
- Preserve the CLV Civil Tools Knowledge Base's blue City/CLV visual identity and established navigation unless a redesign is requested.
- The site includes Start/Home, CLV MENU, Q-menu tool documentation, Quick Tips, Procedures, Standards, Downloads, release notes, icons, screenshots, and related assets.
- Existing HTML/site files in this ZIP are the 2026-06-15 baseline.
- Keep tool names and menu locations synchronized with the current implementation.
- HOWTO pages should be concise enough for CAD users at work but detailed enough to reproduce the workflow.

## Decision handling
- Later decisions supersede earlier terminology. Do not revive obsolete names just because they appear in the June website snapshot.
- If a requested change conflicts with a documented newer decision, point out the conflict before overwriting it.
- When a feature evolves, update both implementation guidance and Knowledge Base wording as needed.

## Files and confidentiality
- Treat supplied work files as project material. Do not publish or expose internal paths, contact data, source files, or other internal material unless Mark specifically requests it.
- Do not assume this migration package contains every source-code project. Ask for the relevant current source when implementation depends on code not included here.
