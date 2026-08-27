# Repository Baseline

**Baseline branch:** `main`

**Baseline established:** 2026-08-27

## Current state

`main` is the authoritative, merged baseline for the Civil3D 2026 .NET project. The prior `work` branch was merged into `main` after the Knowledge Base and project-bootstrap migration work was completed.

The repository contains the current application source, project documentation, Knowledge Base, embedded Legal Description resources, and project reference material.

## Branching rule

Use `main` as the known-good baseline. New application or documentation work should normally be performed on a focused development branch and merged back to `main` after review and the closest practical validation.

Do not reset, overwrite, or revert existing user work without explicit direction.

## Reference-material policy

Reference files that explain or establish engineering behavior may be retained in the repository even when they are not runtime dependencies. Runtime resources should be clearly identified as embedded or externally deployed.

## Legal Description

The Legal Description template and supporting JSON resources are embedded into the Civil Tools assembly. Updated official reference documents should be supplied for comparison when the source material changes; do not introduce an external runtime dependency merely to retain a reference document.

## UFLS Pipe Materials

The UFLS pipe-material workbook is engineering reference/provenance material. The implemented UFLS commands should remain self-contained and should not require the workbook at runtime.

## Validation

A full application build and Civil 3D runtime test remain environment-dependent. Changes affecting Civil 3D behavior should be validated in the actual AutoCAD/Civil 3D 2026 environment when available.