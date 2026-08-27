# CLV CAD Knowledge Base — New Account Bootstrap Prompt

I am migrating an existing ChatGPT project named **CLV CAD Knowledge Base** from another ChatGPT account to this account.

The attached `CLV_CAD_KNOWLEDGE_BASE_BOOTSTRAP_2026_08_24.zip` contains the durable project context, standards, history, current work, and the 2026-06-15 CLV Civil Tools Knowledge Base website/reference snapshot from the previous project.

## Your first task

1. Read `00_PROJECT_BOOTSTRAP/START_HERE.md`.
2. Treat `00_PROJECT_BOOTSTRAP/PROJECT_INSTRUCTIONS.md` as the standing project instructions unless I give newer instructions.
3. Read `PROJECT_HISTORY.md`, `DEVELOPMENT_STANDARDS.md`, `TOOLS_AND_COMMANDS.md`, and `CURRENT_WORK.md` so you understand prior decisions and unfinished work.
4. Inspect the included Knowledge Base HTML/assets as reference material when relevant.
5. Do not treat this as a brand-new CAD project merely because this is our first chat on this account.
6. Preserve established CLV terminology, Civil 3D workflows, menu/tool behavior, and development conventions unless I explicitly change them.
7. Later decisions in the bootstrap files supersede older terminology that may remain in the June website snapshot.
8. New files/source code/screenshots I provide can supersede this bootstrap snapshot; identify important conflicts rather than silently choosing an old version.

## Project context

This project supports CLV civil/survey CAD workflows, primarily Autodesk Civil 3D, and the custom CLV Civil Tools ecosystem. It covers both software/tool development and the user-facing Knowledge Base: commands, menus, workflows, standards, HOWTO pages, screenshots, debugging, feature changes, and deployment/reference material.

I normally communicate naturally and may say things such as:
- “Update the Map Transform documentation.”
- “We need to change how this command works.”
- “Add this to the Knowledge Base.”
- “Review this source project.”
- “Make a HOWTO for this.”
- “We changed this tool again.”

Use the bootstrap context to understand those requests without making me re-explain the whole project.

## Initial response

After reading the package, give me a concise **Project Loaded** report containing:
1. What you understand this project to be.
2. The major CLV tool families/workflows you recognize.
3. Important development/documentation standards you found.
4. Current/open work you identified, especially Map Transform editing/history.
5. Any missing files that would materially prevent continuing implementation work.

Do not redesign or reorganize the project during this initial load unless you find a concrete problem that needs my attention.
