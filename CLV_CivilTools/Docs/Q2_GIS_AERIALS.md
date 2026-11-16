# Q2 GIS street names and aerial unload

## 2026-11-16 follow-up: Map Base order and saved zoom

Mark confirmed the Q2 menu placement and Unload Aerials work. The emailed before/desired examples show Aerial Imagery above Map Base; the requested result has Map Base first and the aerial folder beneath it. The earlier MgLayerCollection reorder did not include Map Base's native drawing element or the group's tree position.

AerialDisplayOrder uses the public Map DisplayManagement interop API to resolve the native display map/group/element IDs. It locates Map Base by BaseElement type (not a localized display name), moves the aerial root group to the bottom and Map Base to the top of the Groups tree, activates explicit draw order if needed, moves the aerial elements to the back and Map Base to the front. Readback checks both orders and confirms unrelated group/element relative order remains intact. It does not rename, regroup or erase Map Base, change its visibility, or toggle the Default child. A native ordering failure is reported rather than claiming the requested order was established.

Palette aerial loads now use ExecuteInCommandContextAsync; repeat clicks are disabled while a request runs. Both palette and NM_AERIAL paths save the complete current ViewTableRecord, temporarily enable AcMapMap synchronous loading, initialize/group/order/refresh the imagery, restore the prior loading mode in finally and restore the saved viewport. The one-idle follow-up remains bounded and canceled by later commands. Readback checks center, width/height, target, twist, perspective and normalized view direction after restoration. This addresses the first idle occurring before asynchronous layer initialization/auto-extents had finished. No permanent map setting, saved named view or ZOOM Previous stack operation is used.

The combined changed sources compile against installed AutoCAD/Map 3D 2026 references, including ManagedMapApi. Full-solution build, both live native orders and the before/after zoom still require workstation acceptance. Earlier one-idle-only text below is superseded by this load-completion change.

Retest on a drawing copy: zoom tightly to a recognizable feature, load an aerial, wait until it finishes and confirm the same center/scale/orientation. Check Groups has Map Base above Aerial Imagery, Draw Order has Map Base above the image, and linework is visible. Repeat with another date, an already configured draw order, rotated view/UCS, load failure and another drawing. Pan/zoom afterward to confirm no stale restore. Unload/reload and save/reopen to check retained grouping. Existing drawings with an aerial already loaded can unload and reload once to apply the automatic order.

## Roadway tools

Q2 > GIS now begins with ROADWAY > STREET NAMES, above ADDRESS. It queues the existing `SURVEY-LABEL-ROADS` command in the active drawing, exactly as Q4 > LABEL > ROADWAY > STREET NAMES does. The Q4 button and all road-label boundaries, styles, conversion and GIS-source behavior remain unchanged.

## Aerial load/unload correction

The prior unload checked only `MgLayerBase.GetName()` for LasVegas/Nearmap. Group assignment also required that internal name to equal the .layer filename. Map internal names and visible legend labels are separate, so a successfully loaded aerial could miss both tests. No explicit Map repaint was performed after removal.

Loading now snapshots Map layer object IDs before `LoadLayer` and groups every layer actually added by that import into Aerial Imagery, regardless of internal name. Imported layers are moved behind existing Map layers, made visible and, for a single layer, given the file's display label. Existing CAD draw-order behavior is retained. If no new layers are added, the command reports it.

Unload resolves ownership before changing the layer collection, then removes candidates in reverse index order and verifies their IDs are absent. It recognizes the Aerial Imagery group (including nested descendant layers), internal or visible Nearmap identities, resource IDs and historic `(2026 - January) LasVegas` labels. The old broad match of any name containing LasVegas is replaced with a bounded historical-label match, protecting names such as LasVegasRoads and LasVegas_Parcels. A tool group is removed only when it has no layer or child-group references.

The button queues `NM_UNLOAD_AERIALS` so Map changes run in AutoCAD command context with a document lock. Both load and unload force a Map repaint and CAD Regen. Unload reports an actual removal count, explicitly reports no matching layers, or reports a failure. Map operations are not a CAD transaction: if the native API fails after some removals, earlier removals may have completed. Inspect Display Manager and retry after resolving that error; a failure is not presented as successful unload.

Shared FDO connection definitions, layer files, external data, raster entities and unrelated GIS layers are retained. The prior recursive reflection over arbitrary connection/service objects is removed; it could silently do nothing or traverse unrelated services. The command unloads the aerial display layers, not the underlying shared connection definitions. An untagged legacy layer with no recognizable group/name/legend/resource cannot safely be inferred to be an aerial; inspect its Display Manager identity if no match is reported.

View restoration now uses the originating document's full ViewTableRecord, restores immediately after loading and makes one bounded Idle restoration attempt for deferred Map auto-zoom. The saved view is disposed and the hooks detached after that attempt. A new command cancels the pending attempt before it can undo a user zoom. It never waits for a later user ZOOM or applies another drawing's saved view; unload cancels its pending restore. The old global CommandEnded subscription and indefinitely pending ZOOM restore are removed.

## Validation

- Host-independent identity checks: `dotnet run --project Tests/AerialLayers.Tests/AerialLayers.Tests.csproj`.
- The console test project restored and compiled here, but Windows Application Control blocked launching its test executable. The 30 test checks have not been executed in this environment; run them on the workstation if allowed. No security-policy changes or alternate execution route were attempted.
- Changed production sources compile against installed AutoCAD/Map 3D 2026 and .NET 10 references. Unchanged palette dependencies are compile-only stand-ins in this targeted check; this is not a full-solution build or a live Map test. No standalone check DLL should be loaded in Civil 3D.
- Full workstation build: `dotnet build .\CLV_CivilTools\CLV_CivilTools.csproj --configuration Debug --property:Platform=x64 --verbosity:minimal`. Close Civil 3D first. Verify the built/deployed DLL hashes and restart the existing loader for `C:\Temp\C3DDev\CLV_CivilTools.dll`.

### Native acceptance (still required)

1. Open Q2 > GIS. Confirm ROADWAY/STREET NAMES appears before ADDRESS and runs the same workflow as Q4. Test cancellation and selected/drawn boundaries from both buttons.
2. Load an aerial whose filename differs from its internal Map layer name. Confirm it joins Aerial Imagery, is behind CAD entities and preserves the current view. Confirm an import that creates multiple layers groups all of them.
3. Click Unload Aerials. Confirm all aerials disappear immediately from the drawing and Display Manager, the removal count is correct and unrelated roads/parcels/coordinate-zone/section layers remain. Repeat unload: it should report no matches. Run `NM_UNLOAD_AERIALS` directly too.
4. Test an existing legacy aerial with generic internal name and recognizable visible date/LasVegas label; test renamed aerials still within the group and nested groups. Confirm LasVegasRoads/LasVegas_Parcels are retained.
5. Load, unload, reload another date, save/reopen and unload. Shared source definitions can remain in Data Connect; reload must still work. Test offline/missing W: layer folder; unloading must not depend on accessing that folder.
6. Load in drawing A, switch to B, pan/zoom in both and close A. No stale callback should change B's view or undo a later user zoom. Repeat loading and unloading to check event cleanup. Check native errors are visible and no success count is issued for an incomplete operation.
