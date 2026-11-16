# Q2 GIS street names and aerial unload

## 2026-11-16 - Aerial R7: keep restoration through Map command registration
- Mark's R6 transcript explicitly records end=canceled by command DMUI_MAPREGISTERCMDSCMD before _ZOOM / _EXTENTS, with view-released=False. The command allowlist incorrectly ended the bounded view guard during Map's deferred registration step.
- Add that exact observed registration command to deferred native activity. It now logs the command and resets the quiet interval, preserving restoration through the following zoom/task-pane commands.
- Retain R6's five-second minimum watch, two-second quiet verification, input release, document/new-load cancellation and deadline. Grouping/replacement/draw-order code is unchanged.
- Runtime marker is [AERIAL-R7]. Retest without input until settled: registration must be logged as deferred command activity, not cancellation, followed by original-view restoration after ZOOM Extents.
- Targeted compilation against installed Autodesk references passed. Live Civil 3D acceptance and full solution build remain workstation checks.

## 2026-11-16 R6: restore after deferred Map zoom commands

Mark's R5 transcript confirms the correct Aerial Imagery parent, Map Base first in TREE and DRAW, and successful original-view readback in both initial and finish passes. It then shows a separate _ZOOM / _EXTENTS followed by _MW / MAPWSPACE AFTER end=completed. The later command overwrites the restored view; group placement is now working.

Keep the saved ViewTableRecord and event subscriptions after the first finish command. Native ZOOM/PAN/REGEN and the observed Map task-pane commands mark pending view work. A timer only queues request-bound NM_AERIAL_FINISH commands while no command is running. Subsequent passes restore the saved view without re-running grouping/order/Map refresh, avoiding a refresh-to-auto-zoom loop. Final verification requires at least five seconds after the first successful finish and two seconds without relevant native changes. Internal finish/state commands do not reset that quiet interval. The existing 60-second deadline, document/new-load cancellation and input ownership release remain.

This is a bounded settling guard, not a guarantee against arbitrary commands issued much later. Physical keyboard/mouse input releases view ownership; the first release message code is retained. Runtime messages use [AERIAL-R6], record deferred command names and finish-applied, and reserve settled for the final quiet verification. Group/replacement/order implementation remains unchanged.

Retest: zoom to a recognizable site, load an aerial and do not type/navigate until [AERIAL-R6] settled (normally about five seconds after the first finish). Verify the original site/scale remains after the task pane appears. Switch dates and confirm one image inside one Aerial Imagery folder, Map Base first, with the same site view. After settling, pan/zoom normally and confirm the routine does not pull the view back. If it fails, send the full transcript plus NM_AERIAL_STATE, including commands after the final status.

Validation: changed GIS sources compiled against the installed AutoCAD/Map assemblies with SDK-equivalent implicit usings. Full solution build and live Civil 3D acceptance remain workstation checks; no live viewport test is claimed.

## 2026-11-16 R5: execute essential finishing in CAD commands

Mark's R4 diagnostic proves the selected layer had no parent, Aerial Imagery was empty, draw order still used group order, and the session was returned=True / native-finished=True / pending=True / disposed=True / view-released=False. The supplied output does not retain the exact cancellation/deadline reason. It establishes that native loading completed but pending finishing did not complete; it does not prove one exclusive cause for disposal.

Grouping/order/restore now get an initial application directly inside the actual load command when imported layers exist. The palette no longer awaits the pending session's TaskCompletionSource after ExecuteInCommandContextAsync. That wait and the timer's command-idle gate formed a possible context dependency; essential work no longer relies on either.

After load, a request-token-bound NM_AERIAL_FINISH command is queued without activating another document. It performs the completion pass in a real document command context, checking native completion/readiness and always restoring the saved viewport in finally, even if order repair fails. A timer only queues retries/handles the bounded deadline; it no longer performs database/view finishing itself. Stale request tokens and different-document commands are ignored. The loader's own finishing command is allowed by the command-start guard. The native AllLayerLoadingFinished signal takes precedence over optional GetIsLoaded fallback reads.

Runtime messages now use [AERIAL-R5]. The retained status includes initial-applied, last stage, saved viewport, last exception and end reason. NM_AERIAL_STATE prints the complete retained load trace alongside group/layer parents and native orders. Explicit stop reasons distinguish completion, other commands, document changes, deadline, caller disposal and finalization failure. This remains available after the saved ViewTableRecord is disposed.

Targeted compilation with installed AutoCAD/Map references and SDK-equivalent implicit namespaces passes. The public Autodesk managed guide confirms SendStringToExecute queues commands until the current .NET command ends: https://help.autodesk.com/cloudhelp/2026/DEU/OARX-DevGuide-Managed/files/GUID-F4A36181-39FB-4923-A2AF-3333945DB289.htm . Full solution build and live CAD acceptance remain unverified.

Retest after close/rebuild/restart: zoom to a small recognizable area, load one aerial and check the initial pass actually groups the image and restores the original view. Then wait for settled/end=completed. Confirm Map Base above the expanded Aerial Imagery folder and the image inside it. Replace dates and unload/reload to retain working behavior. If anything fails, run NM_AERIAL_STATE once and send all output; it now carries the saved-view/stage/error/end trace automatically. Do not manually run the internal token-bound finishing command.

## 2026-11-16 R4: input-property correction and pre-queue view capture

The latest native screenshot shows the selected aerial at the map root and an empty Aerial Imagery folder. Date replacement and duplicate-folder removal are now confirmed working by Mark. The screenshot does not include command-line completion/error output.

A specific defect was verified against the installed assemblies: AutoCAD PreTranslateMessageEventArgs.Message returns System.Windows.Interop.MSG, which has a public message PROPERTY and no public fields. R3 called GetField("message"), received null, then disposed the entire load session from the catch. This can prevent both grouping and delayed view restoration. R4 reads the property with explicit validation. An optional input-adapter failure now disables only its observer/releases saved-view ownership; it never cancels group/order completion.

The original viewport is now captured under a short document lock in the palette double-click path BEFORE ExecuteInCommandContextAsync is queued. A session-owned clone survives the asynchronous completion. The public one-argument LoadAerialFromFile entry remains compatible; command-line loads still capture at load entry. The established replacement, grouping, cleanup and native order logic are retained.

Messages now use [AERIAL-R4]. NM_AERIAL_STATE additionally reports returned/native-finished/pending/disposed/view-released session flags. This distinguishes a canceled or failed session from an actual settled/verified load.

Validation: installed SDK metadata confirmed the actual host return type and the property-only MSG shape; the production fix was compiled with installed AutoCAD/Map references and SDK-equivalent WinForms implicit namespaces. No full solution or live CAD grouping/zoom test was performed. Console regression execution remains blocked by the previously observed Application Control policy. Microsoft API contract: https://learn.microsoft.com/dotnet/api/system.windows.interop.msg

Retest: close/rebuild/restart, zoom to a recognizable location and double-click an aerial. Wait for [AERIAL-R4] settled without navigating. Expand Aerial Imagery to confirm the image is its child and compare the original location/zoom. Repeat with another date, verify one group/no duplicates, then test user pan/zoom. If grouping or zoom is still wrong, send the complete R4 messages plus NM_AERIAL_STATE output. The latest email screenshot has been inspected; no claim is made that it contains an exception trace.

## 2026-11-16 R3: replacement folders and native completion

The second native test still showed Map Base below imagery, an extents zoom and accumulating unnamed folders. The replacement screenshot confirms multiple blank containers alongside Aerial Imagery. Code inspection found that reordering removed live layers from MgLayerCollection and reinserted them after assigning their group. That removal/reinsertion can disturb the native group association; it is now eliminated. RemoveAt remains only for actual unload, and empty-group pruning.

Replacement retains one existing Aerial Imagery group instead of deleting/recreating it each date change. It resolves ownership by internal name OR legend, creates a native AcMapLayerGroup when needed, ensures the canonical label and root parent, then reparents the new layers without removing/readding them. Pruning covers empty unnamed legacy containers and newly imported containers, plus redundant empty owned groups. A group must have neither FDO layer/child-group references nor any native DisplayManagement item before deletion; unverified containers, nonempty folders and unrelated visibly named folders are retained.

Native reordering now uses only iterators positioned at existing anchor items. No null or exhausted iterator is used as a MoveItem/MoveElement destination. Non-aerial items after the aerial anchor move before it, retaining their relative order; Map Base is then placed first. Both native orders and layer parents are checked.

AerialLoadSession replaces the one-idle completion assumption. It captures the full viewport before LoadLayer, subscribes to LayerAdded/LayerInitialized/AllLayerLoadingFinished and waits for native completion or all imported AcMapLayer.GetIsLoaded flags plus a bounded quiet period. Final grouping/cleanup/order/refresh and viewport restoration run with a document lock while the editor is idle. Ordering errors cannot skip the view-restoration finally. Pending loader-generated ZOOM is allowed to finish before restoration instead of canceling the restore. Physical user navigation releases the saved-view restore while allowing cleanup/order to finish; unrelated new commands, document switch/closure, unload and a subsequent load cancel the pending session. Native input observation reads only the MSG message number through the host's PreTranslateMessage event; no permanent input blocking or WPF framework change is introduced. A 150 ms UI timer supplies checks without blocking; the maximum wait is 60 seconds. All event/timer/input hooks and saved view are released on completion/cancel/failure. The previous synchronous-loading preference is still restored in finally. Palette date selection stays disabled until session completion/cancellation.

Runtime messages begin with [AERIAL-R3], include the actual DLL location, saved center/width/height, native-event/loaded-state status and final verification or failure. Imported/waiting is distinct from settled/verified. NM_AERIAL_STATE reports the runtime path/current view, all Map group/layer names, labels, IDs and parents, and native TREE/DRAW item identities/order. If native behavior remains wrong, collect the full R3 output and this diagnostic instead of inferring success from compilation.

Targeted source compilation against installed AutoCAD/Map/.NET references passes, including the normal WinForms project's implicit-using compatibility (Timer is explicitly aliased). This is not a live native test or a full solution build. Earlier regression execution remains blocked by Application Control; no alternate execution or policy change was attempted.

Native retest: close/rebuild/restart, then on a drawing copy zoom to a recognizable feature and load a date. Wait without panning/typing for [AERIAL-R3] settled or a specific error. Confirm one named Aerial Imagery folder below Map Base, no new blank folders, unchanged original view and visible CAD linework. Replace with at least three dates, checking only the chosen image remains. Test unload twice and reload, saved/reopened data, rotated views, different drawings, and user pan/zoom during loading. Empty legacy blank folders may be pruned; nonempty ones are deliberately retained. If no R3 marker appears, the running DLL is older. If finalization fails or times out, run NM_AERIAL_STATE and send the full output plus the Groups/Draw Order screenshots.

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
