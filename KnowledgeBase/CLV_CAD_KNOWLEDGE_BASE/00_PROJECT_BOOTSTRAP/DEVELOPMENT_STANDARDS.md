# Development Standards / Preferences

## Environment
- Primary CAD platform: Autodesk Civil 3D 2026.
- 2027 compatibility/migration can be relevant for newer work.
- Visual Studio Community 2026 v18.3.0 context.
- .NET 8, WinForms PaletteSet, `net8.0-windows`, x64, nullable enabled.

## Implementation principles
- Preserve drawing state and existing command behavior unless the feature requires a deliberate change.
- For transform/math tools, distinguish source/original coordinates from already-transformed display coordinates. Re-editing must not accidentally apply a second transform to transformed geometry.
- Persist only the data needed to reconstruct/review a command safely; avoid hidden state that becomes invalid when drawings change.
- Dialogs should help the CAD user understand selected groups, errors, and what will change before committing.
- Temporary review graphics/labels should be clearly temporary and removable.
- Keep user-facing command/menu terminology consistent across ribbon/menu, dialogs, documentation, and release notes.

## Build/deployment preference
- Production DLLs may use randomized numeric names (for example `CLV_CivilTools_20145.dll`) when that deployment convention is in use.
- When diagnosing load/build problems, check Civil 3D version, target framework, x64, Autodesk references, copy-local behavior, and deployed DLL identity before assuming the command code itself is wrong.

## Documentation standard
A good HOWTO should normally capture:
1. Purpose / when to use the tool.
2. Menu location / command identity.
3. Preconditions (coordinate system, required objects/data, etc.).
4. Exact selection order.
5. Dialog/review behavior.
6. Final result and files/data created.
7. Cleanup, limitations, or troubleshooting if important.

Use screenshots when they materially clarify selection order or dialogs.
