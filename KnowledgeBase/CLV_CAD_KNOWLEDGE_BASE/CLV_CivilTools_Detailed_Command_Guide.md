# CLV CivilTools — Detailed Command Guide

This version removes command-code references from the main content and focuses on:
- **Button Name**
- **Quick Description**
- **Procedure (Detailed)**

> This is a first detailed pass. Procedures are based on the current command documentation, current project workflows, and the uploaded LISP/DCL menu source files where applicable.

## Q1 — UFLS Palette

### CHECK tab

#### VERIFICATION

##### HIGHLIGHT RED

**Quick Description:**
Verification helper to highlight redline / fail-condition items.

**Procedure (Detailed):**

---

##### HIGHLIGHT GREEN

**Quick Description:**
Verification helper to highlight pass / verified items.

**Procedure (Detailed):**

---

#### REDLINE

##### REVISION CLOUD...

**Quick Description:**
Converts polyline into a revision cloud and puts on correct layer.

**Procedure (Detailed):**
1. Start the tool.
2. Create polyline around revision area.
3. Choose the revision cloud size when prompted:
   - **Large**
   - **Medium**
   - **Small**
4. Select polyline
5. Continue with any note or leader tools if needed.

---

##### NOTE

**Quick Description:**
Inserts the REDLINE-MTEXT survey text block.

**Procedure (Detailed):**

---

##### LEADER

**Quick Description:**
Inserts the REDLINE-LEADER survey note.

**Procedure (Detailed):**

---

#### 2D LINEWORK

##### 3P MANHOLE - ALL

**Quick Description:**
Creates manholes from survey shots in groups of 3.

**Procedure (Detailed):**
1. Start the tool.
2. Choose the applicable source/workflow option when prompted by the dialog.
 - IN-HOUSE (Uses CLV Survey codes)
 - OTHERS (Allows user to enter raw codes for search parameters)
3. The routine groups nearby shots into manhole clusters.
4. For each cluster, the routine fits a 3-point circle where possible and computes the center.
5. The matching manhole block and marker block are inserted at the computed center.

---

##### 3P MANHOLE - SINGLE

**Quick Description:**
Creates one manhole from three selected survey shots.

**Procedure (Detailed):**
1. Start the tool.
2. Select the first survey shot on the interior of the manhole.
3. Select the second survey shot.
4. Select the third survey shot.
5. The routine solves the 3-point circle center from the three picks.
6. The manhole block and marker block are inserted at the computed center.

---

##### 1P MANHOLE - ALL

**Quick Description:**
Opens the 1-point manhole batch workflow dialog, then creates manholes from single-point centers.

**Procedure (Detailed):**
1. Start the tool.
2. Choose the applicable source/workflow option if prompted.
 - IN-HOUSE (Uses CLV Survey codes)
 - OTHERS (Allows user to enter raw codes for search parameters)
3. The routine locates all points with proper coding.
4. A center is determined from each point.
5. The manhole block and marker block are inserted at the computed center.

---

##### 1P MANHOLE - SINGLE

**Quick Description:**
Creates one manhole from one selected center point.

**Procedure (Detailed):**
1. Start the tool.
2. Select the single point representing the manhole location.
3. The routine uses that pick as the structure center.
4. The manhole block and marker block are inserted at the computed center.

---

##### STRC-INNER WALL

**Quick Description:**
Traces the inner wall footprint and auto-builds the matching outer wall footprint.

**Procedure (Detailed):**
1. Start the tool.
2. Select or trace the structure location using the required picks for the workflow.
3. Specify wall thickness via dialog box.
4. The routine creates the **inner wall** footprint on the standard structure layer.
5. The companion outer wall geometry is generated automatically where supported.

---

##### STRC-OUTER WALL

**Quick Description:**
Traces the outer wall footprint and auto-builds the matching inner wall footprint.

**Procedure (Detailed):**
1. Start the tool.
2. Select or trace the structure location using the required picks for the workflow.
3. Specify wall thickness via dialog box.
4. The routine creates the **outer wall** footprint on the standard structure layer.
5. The companion inner wall geometry is generated automatically where supported.

---

##### STUB MARKER

**Quick Description:**
Places a stub locator marker block for pipe-stub adjustment workflows.

**Procedure (Detailed):**
1. Start the tool.
2. Select the endpoint of the 3d polyline that represents the pipe stub location.
3. The routine places the stub marker block at that location.

---

##### DROP INLET

**Quick Description:**
Runs the drop inlet placement workflow with the type / size dialogs.

**Procedure (Detailed):**
1. Start the tool.
2. Choose the drop inlet type/size from the dialog.
3. Choose the source type:
   - **IN-HOUSE**
   - **OTHERS**
4. Follow the required pick sequence for that source:
   - IN-HOUSE typically uses the inlet corner/box (4) picks.
   - OTHERS typically uses the back-of-curb (2) picks.
5. Pick the **street side** point so the routine can determine outward orientation.
6. The routine inserts the correct block, rotation, and visibility state.

---

##### 3P CIRCLE

**Quick Description:**
Creates a 3-point circle on layer 0 with temporary pick markers.

**Procedure (Detailed):**
1. Start the tool.
2. Pick the first point.
3. Pick the second point.
4. Pick the third point.
5. The routine solves the circle through the three picks and creates the circle.
6. Temporary pick markers may be shown during the workflow for clarity.

---

##### 3P RECTANGLE

**Quick Description:**
Creates a 3-point orthogonal rectangle on layer 0 with temporary pick markers.

**Procedure (Detailed):**
1. Start the tool.
2. Pick the first point/corner.
3. Pick the second point to establish the primary direction.
4. Pick the third point to establish the width.
5. The routine creates the orthogonal rectangle from those three picks.
6. Review orientation before continuing.

---

#### 3D LINEWORK

##### TOP OF PIPE

**Quick Description:**
Builds a 3D top-of-pipe polyline from ordered COGO picks and structure intersections.

**Procedure (Detailed):**
1. Start the tool.
2. Select the first pipe shot at the upstream end.
3. Select the second pipe shot at the downstream direction to establish the alignment.
4. Continue selecting intermediate pipe shots in order along the run.
5. Press **Enter** when finished.
6. The routine computes the best-fit 3D polyline from the ordered picks.
7. The resulting line is extended or trimmed to the nearest structure wall intersections where applicable.

---

##### TRIM TOP OF PIPE

**Quick Description:**
Trims an existing top-of-pipe 3D polyline back to the nearest inner-structure wall on the selected side.

**Procedure (Detailed):**
1. Start the tool.
2. Select the existing top-of-pipe 3D polyline on the side closest to the structure where the line is too long.
3. The routine calculates the proper trim location at the structure boundary.
4. The selected top-of-pipe line is trimmed back to the computed limit.

---

##### LABEL INVERT

**Quick Description:**
Labels pipe invert information from 3D surveyed pipe geometry.

**Procedure (Detailed):**
1. Start the tool.
2. Select the pipe linework to label.
3. The routine reads the relevant invert information from the 3D geometry or associated data.
4. Labels are placed at the required locations.
5. Review placement and move labels manually if needed for readability.

---

### ADJUST tab

#### SEWER MAIN - MOVE

##### MH - SINGLE

**Quick Description:**
Moves one selected manhole / structure to its matched surveyed footprint target.

**Procedure (Detailed):**
1. Start the tool.
2. Select the single structure to move.
3. Select or confirm the matching surveyed footprint/target marker if prompted.
4. The routine updates the structure location to the matched survey target.
5. Connected pipes are preserved where supported by the workflow.

---

##### MH - ALL

**Quick Description:**
Batch-moves nearby manholes / structures to matched surveyed footprint targets.

**Procedure (Detailed):**
1. Start the tool.
2. The routine matches each structure to its surveyed footprint/target.
3. Each matched structure is repositioned to the computed target.
4. Review unmatched or questionable items separately.

---

##### PIPE - SINGLE

**Quick Description:**
Moves one selected Civil 3D pipe to the best matched surveyed pipe centerline.

**Procedure (Detailed):**
1. Start the tool.
2. Select the single Civil 3D pipe to adjust.
3. Select or confirm the matching surveyed pipe centerline if prompted.
4. The routine computes the best target position from the survey geometry.
5. The pipe is adjusted to match the surveyed alignment.
6. Review the new position and connected structures.

---

##### PIPE - ALL

**Quick Description:**
Batch-moves Civil 3D pipes to matched surveyed pipe centerlines.

**Procedure (Detailed):**
1. Start the tool.
2. The routine attempts to match each pipe to surveyed centerline geometry.
3. Matching pipes are adjusted automatically.
4. Review any questionable or unmatched pipes manually.

---

#### SEWER LATERAL

##### CREATE LATERAL

**Quick Description:**
Creates one sewer lateral from ordered survey shots and connects it to the nearest main centerline.

**Procedure (Detailed):**
1. Start the tool.
2. Select the surveyed lateral shots in order.
3. Select or confirm the connection to the main line when prompted.
4. The routine computes the best connection point on the main centerline.
5. The lateral geometry is created from the surveyed picks.
6. The lateral pipe network is added to the main project pipe network.

---

##### CREATE LATERAL - ALL

**Quick Description:**
Automatically builds laterals from WYE and lateral-shot patterns, and sends ambiguous groups to QA.

**Procedure (Detailed):**
1. Start the tool.
2. Select the survey data set to process.
3. The routine searches for WYE shots and associated lateral shot patterns.
4. Candidate groups are built automatically.
5. Clear matches are created as laterals.
6. Ambiguous groups are flagged for QA review.
7. Review the output carefully before accepting the full result.

---

#### STORM JUNCTION STRUCTURE

##### RESIZE JUNCTION

**Quick Description:**
Resizes a storm junction structure when the target size exists in the selected structure family / parts list.

**Procedure (Detailed):**
1. Start the tool.
2. Select the storm junction structure to resize. Structure should already have been moved to the correct location centered above the 2D linework.
3. Choose or confirm the target structure size when prompted.
4. The routine checks whether that size exists in the current structure family/parts list.
5. If valid, the junction is resized.
6. Review the resulting part size and connected pipes.

---

#### STORM DRAIN - MOVE

##### JNCT - SINGLE

**Quick Description:**
Moves one selected storm-drain junction structure to the matched inside-wall footprint and rotates it to fit.

**Procedure (Detailed):**
1. Start the tool.
2. Select the storm junction structure.
3. Select or confirm the matching inside-wall footprint if prompted.
4. The routine moves and rotates the junction to fit the surveyed structure footprint.
5. Review the fit and orientation before continuing.

---

##### JNCT - ALL

**Quick Description:**
Batch-moves storm-drain junction structures to matched inside-wall footprints and rotates them to fit.

**Procedure (Detailed):**
1. Start the tool.
2. Select the junction structures to process, or allow the routine to scan the target area.
3. The routine matches each structure to surveyed inside-wall footprints.
4. Matching structures are moved and rotated automatically.
5. Review any questionable matches separately.

---

##### DI - SINGLE

**Quick Description:**
Moves one selected drop inlet to the nearest UFLS_DI_MARK marker and applies the marker rotation.

**Procedure (Detailed):**
1. Start the tool.
2. Select the single drop inlet structure to move.
3. The routine finds the nearest matching **UFLS_DI_MARK** target.
4. The structure is moved to that target and rotated to the stored marker direction.
5. Review the final orientation.

---

##### DI - ALL

**Quick Description:**
Batch-moves drop inlet structures to nearby UFLS_DI_MARK targets and applies marker rotation.

**Procedure (Detailed):**
1. Start the tool.
2. The routine selects the drop inlet structures to process.
3. The routine looks for nearby **UFLS_DI_MARK** targets.
4. Matching inlets are moved and rotated automatically.
5. Review the batch result and fix any questionable matches manually.

---

##### MH - SINGLE

**Quick Description:**
Moves one selected manhole / structure to its matched surveyed footprint target.

**Procedure (Detailed):**
1. Start the tool.
2. Select the single structure to move.
3. Select or confirm the matching surveyed footprint/target marker if prompted.
4. The routine updates the structure location to the matched survey target instead of using a raw CAD move.
5. Connected pipes are preserved where supported by the workflow.
6. Review the moved structure before continuing.

---

##### MH - ALL

**Quick Description:**
Batch-moves nearby manholes / structures to matched surveyed footprint targets.

**Procedure (Detailed):**
1. Start the tool.
2. Select the structures to process, or allow the routine to scan the applicable area.
3. The routine matches each structure to its surveyed footprint/target.
4. Each matched structure is repositioned to the computed target.
5. Review unmatched or questionable items separately.

---

##### PIPE - SINGLE

**Quick Description:**
Moves one selected Civil 3D pipe to the best matched surveyed pipe centerline.

**Procedure (Detailed):**
1. Start the tool.
2. Select the single Civil 3D pipe to adjust.
3. Select or confirm the matching surveyed pipe centerline if prompted.
4. The routine computes the best target position from the survey geometry.
5. The pipe is adjusted to match the surveyed alignment.
6. Review the new position and connected structures.

---

##### PIPE - ALL

**Quick Description:**
Batch-moves Civil 3D pipes to matched surveyed pipe centerlines.

**Procedure (Detailed):**
1. Start the tool.
2. Select the pipes to process, or let the routine evaluate the applicable data set.
3. The routine attempts to match each pipe to surveyed centerline geometry.
4. Matching pipes are adjusted automatically.
5. Review any questionable or unmatched pipes manually.

---

#### SWAP MATERIAL

##### PVC --> C900

**Quick Description:**
Swaps one selected pipe from CLV_PVC to the same nominal size in CLV_C900.

**Procedure (Detailed):**
1. Start the tool.
2. Select the pipe to swap.
3. The routine reads the existing nominal size.
4. The current material/part is replaced with the matching **C900** part of the same nominal size.
5. Review the resulting pipe size and part family.

---

##### RCP --> C900

**Quick Description:**
Swaps one selected pipe from CLV_RCP to the same nominal size in CLV_C900.

**Procedure (Detailed):**
1. Start the tool.
2. Select the pipe to swap.
3. The routine reads the existing nominal size.
4. The current **RCP** part is replaced with the matching **C900** part of the same nominal size.
5. Review the resulting pipe size and part family.

---

##### C900 --> RCP

**Quick Description:**
Swaps one selected pipe from CLV_C900 to the same nominal size in CLV_RCP.

**Procedure (Detailed):**
1. Start the tool.
2. Select the pipe to swap.
3. The routine reads the existing nominal size.
4. The current **C900** part is replaced with the matching **RCP** part of the same nominal size.
5. Review the resulting pipe size and part family.

---

##### C900 --> PVC

**Quick Description:**
Swaps one selected pipe from CLV_C900 to the same nominal size in CLV_PVC.

**Procedure (Detailed):**
1. Start the tool.
2. Select the pipe to swap.
3. The routine reads the existing nominal size.
4. The current **C900** part is replaced with the matching **PVC** part of the same nominal size.
5. Review the resulting pipe size and part family.

---

### LAYERS tab

#### LAYERS

##### REMOVE DUPLICATE STRC LAYERS

**Quick Description:**
Moves entities from legacy structure layers into the current standard structure layers and removes the old layers when possible.

**Procedure (Detailed):**
1. Start the tool.
2. The routine scans for entities on legacy or duplicate structure layers.
3. Matching entities are moved to the current standard structure layers.
4. Legacy layers are removed when they are no longer needed and can be safely deleted.
5. Review layer assignments after completion.

---

##### UPDATE LAYER STANDARDS

**Quick Description:**
Reloads the standard layer states used by the UFLS workflows.

**Procedure (Detailed):**
1. Start the tool.
2. The routine reloads the current UFLS layer states and standards.
3. Existing layer settings are updated to the latest standard where applicable.
4. Review the layer list if you need to confirm color, state, or naming updates.

---

### GIS tab

#### STORM DRAIN

##### GIS PREP - ALL

**Quick Description:**
Runs the storm GIS prep sequence: storm structures automation first, then the all-pipes storm pipe OD offset pass.

**Procedure (Detailed):**
1. Start the tool.
2. Select the target GIS dataset or applicable geometry if prompted.
3. The routine runs the full preparation workflow for the category shown in that section.
4. Structure processing is typically performed first.
5. Pipe processing follows using the matching OD/geometry routines.
6. Review the prepared output before exporting/finalizing.

---

##### JUNCTIONS AND INLETS - ALL

**Quick Description:**
Batch storm-structure automation for supported drop inlets and remaining junction structures, including OD transfer queueing.

**Procedure (Detailed):**
1. Start the tool.
2. Select the imported storm structure data to process.
3. The routine identifies supported drop inlet and junction structure types.
4. Matching structures are converted to standard GIS linework.
5. OD transfer operations are queued where applicable.
6. Review converted structures before final export.

---

##### DROP INLET - SINGLE

**Quick Description:**
Explodes one drop inlet block into GIS structure linework and queues OD copy from the aligned structure point.

**Procedure (Detailed):**
1. Start the tool.
2. Select the single drop inlet block or target object.
3. The routine explodes/converts that inlet into GIS structure linework.
4. The matching OD copy is queued from the aligned point object where available.
5. Review the result and repeat for the next inlet if needed.

---

##### JUNCTION STRUCTURE - SINGLE

**Quick Description:**
Converts one imported storm structure point into GIS inner / outer junction structure linework and queues OD copy.

**Procedure (Detailed):**
1. Start the tool.
2. Select the imported storm structure point.
3. The routine converts that point into standard inner/outer GIS junction structure linework.
4. The matching OD copy is queued.
5. Review the generated linework before continuing.

---

##### PIPE

**Quick Description:**
Reads storm pipe OD, offsets qualifying pipes to wall lines, moves centerlines to the correct layer, and adjusts endpoints to nearby structure walls.

**Procedure (Detailed):**
1. Start the tool.
2. Select the pipe centerlines to process.
3. The routine reads the available OD and geometry.
4. It converts or offsets the selected pipe geometry to the proper GIS output layer(s).
5. Endpoints are adjusted where supported by the workflow.
6. Review the resulting geometry before finalizing.

---

#### SEWER

##### GIS PREP - ALL

**Quick Description:**
Runs the sewer GIS prep sequence: sewer manholes first, then sewer pipes.

**Procedure (Detailed):**
1. Start the tool.
2. Select the target GIS dataset or applicable geometry if prompted.
3. The routine runs the full preparation workflow for the category shown in that section.
4. Structure processing is typically performed first.
5. Pipe processing follows using the matching OD/geometry routines.
6. Review the prepared output before exporting/finalizing.

---

##### MANHOLE

**Quick Description:**
Converts one imported sewer structure point into sewer manhole GIS linework and queues OD copy.

**Procedure (Detailed):**
1. Start the tool.
2. Select the imported sewer structure point.
3. The routine converts the point into sewer manhole GIS linework.
4. The associated OD copy is queued where supported.
5. Review the result before exporting/finalizing.

---

##### PIPE

**Quick Description:**
Processes selected sewer pipe centerlines using the sewer ADE/LISP helper and places them on the correct sewer GIS layers.

**Procedure (Detailed):**
1. Start the tool.
2. Select the pipe centerlines to process.
3. The routine reads the available OD and geometry.
4. It converts or offsets the selected pipe geometry to the proper GIS output layer(s).
5. Endpoints are adjusted where supported by the workflow.
6. Review the resulting geometry before finalizing.

---

#### CLEANUP

##### ERASE POINTS

**Quick Description:**
Erases imported map structure point objects on layer Structures.

**Procedure (Detailed):**
1. Start the tool.
2. Select the imported structure points to remove, or allow the routine to process the standard import layer.
3. The routine erases the map structure point objects on the import layer.
4. Confirm only the intended temporary points were removed.

---

#### EXPORT

##### FINALIZE STRUCTURES

**Quick Description:**
Previews, compares, and appends finalized structure entities into the matching survey-cache DWG.

**Procedure (Detailed):**
1. Start the tool.
2. Select or confirm the structures to finalize.
3. The routine previews and compares the pending output to the destination survey-cache file.
4. The finalized structure entities are appended to the matching cache drawing.
5. Review the destination file after export.

---

##### FINALIZE PIPES

**Quick Description:**
Previews, compares, and appends finalized pipe entities into the matching survey-cache DWG.

**Procedure (Detailed):**
1. Start the tool.
2. Select or confirm the pipe output to finalize.
3. The routine previews and compares the pending output to the destination survey-cache file.
4. Finalized pipe entities are appended to the matching cache drawing.
5. Review the destination file after export.

---

## Q2 — GIS Palette

### AERIAL tab

#### AERIAL MANAGEMENT

##### UNLOAD AERIALS

**Quick Description:**
Removes loaded aerial imagery and attempts to remove matching Nearmap / Las Vegas map connections from the active map session.

**Procedure (Detailed):**
1. Open the **Q2 → AERIAL** tab.
2. Click **UNLOAD AERIALS**.
3. The routine unloads/removes the active aerial imagery and associated map connections for the drawing session.
4. Confirm the imagery is no longer displayed before proceeding with other map tasks.

---

### GIS tab

#### DATA

##### IMPORT GIS

**Quick Description:**
Opens the GIS import workflow for supported GIS and surveyed cache datasets, with coordinate-system-aware import / transform handling.

**Procedure (Detailed):**
1. Open the **Q2 → GIS** tab.
2. Confirm the source coordinate system and target drawing coordinate system.
3. Draw polyline boundary around area to import.
4. Click **IMPORT GIS**.
5. In the import workflow, choose the dataset/category you want to bring in.
6. Run the import.
7. Review the imported geometry, object data, and layer placement.

---

#### GIS TOOLS

##### JOIN CENTERLINES

**Quick Description:**
Interactive street-centerline join tool that keeps an existing polyline when possible so OD survives, joins selected centerline segments, and cleans up the source geometry.

**Procedure (Detailed):**
1. Open the **Q2 → GIS** tab.
2. Click **JOIN CENTERLINES**.
3. Select the street centerline segments to join. First line selected should not be the most outer edge. Outer edge objects that are trimmed via the map trim tools loose their object data. The first line selected should not be at the very edge.
4. Follow the prompts to keep the preferred existing polyline when possible.
5. The routine joins the selected geometry while preserving OD where supported.
6. Review the joined centerline and remove any remaining cleanup items if needed.

---

## Q3 — Point Cloud Palette

### ROADWAY tab

#### POINT CLOUD DISPLAY

##### ATTACH

**Quick Description:**
Starts the point-cloud attach workflow.

**Procedure (Detailed):**
1. Select ATTACH.
2. Path to point-cloud location on computer/server. Files should be copied locally for quicker loading.
3. Select correct point cloud to load.

---

##### INTENSITY

**Quick Description:**
Runs the point-cloud intensity helper.

**Procedure (Detailed):**
1. Open the point cloud menu tab.
2. Select the intensity level.
3. Select apply when done.

---

##### ON

**Quick Description:**
Turns point cloud display on.

**Procedure (Detailed):**

---

##### OFF

**Quick Description:**
Turns point cloud display off.

**Procedure (Detailed):**

---

#### SAMPLE LINES

##### CREATE SAMPLE LINES

**Quick Description:**
Creates sample lines.

**Procedure (Detailed):**
1. Open **Q3 → ROADWAY**.
2. Click **CREATE SAMPLE LINES**.
3. Select the controlling alignment or target geometry if prompted.
4. Input correct length, thickness, and interval values as prompted in command line.
5. The routine creates the roadway sample line geometry on the standard layer.

---

#### CROP SAMPLE LINES

##### CROP SL

**Quick Description:**
Crops the point cloud to the selected sample-line workflow.

**Procedure (Detailed):**
1. Click **CROP SL** after sample lines have been created.
2. Select the sample line when prompted.
3. The point cloud is cropped to the selected sample line area.
4. Review the cropped view before moving to cross section.

---

##### UNCROP SL

**Quick Description:**
Removes the sample-line crop / quick sample-line crop state.

**Procedure (Detailed):**
1. Click **UNCROP SL**.
2. The active sample-line crop state is removed.

---

#### QUICK SAMPLE LINE

##### QUICK SL

**Quick Description:**
Runs the quick sample-line crop workflow.

**Procedure (Detailed):**
1. Click **QUICK SL**.
2. Follow the prompts to define the temporary quick sample line, usually by selecting 2 points.
3. The routine creates and applies the quick sample-line crop/workflow.

---

##### UNCROP SL

**Quick Description:**
Removes the sample-line crop / quick sample-line crop state.

**Procedure (Detailed):**
1. Click **UNCROP SL**.
2. The active sample-line crop state is removed.
3. Confirm the full point cloud view is restored before starting the next operation.

---

#### GENERAL CROP

##### CROP

**Quick Description:**
Crops the point cloud to selected polyline or area.

**Procedure (Detailed):**
1. Click **CROP**.
2. Define the crop boundary by following the prompts and selecting the required points or polygon.
3. The point cloud is cropped to the selected area.
4. Review the cropped view and proceed with tracing or structure work.

---

##### UNCROP

**Quick Description:**
Removes the active general point-cloud crop.

**Procedure (Detailed):**

---

#### CROSS SECTION

##### CROSS SECTION

**Quick Description:**
Creates the cross-section view workflow.

**Procedure (Detailed):**
1. Click **CROSS SECTION**.
2. Select the sample line/crop geometry that controls the section view as prompted in command line.
3. The routine saves the current state, rotates the view, and sets the UCS as required.
4. Work in the section view.
5. Use **RESET CS** when finished.

---

##### RESET CS

**Quick Description:**
Resets the cross-section view workflow.

**Procedure (Detailed):**
1. Click **RESET CS** after finishing the cross-section work.
2. The routine restores the saved view and UCS.
3. Confirm you are back in the normal drawing orientation before continuing.

---

#### VIEW CONTROL

##### 3D ROTATE

**Quick Description:**
Runs the 3D rotate / rotated-view workflow.

**Procedure (Detailed):**
1. Click **3D ROTATE**.
2. Select the controlling geometry or direction references when prompted.
3. The routine rotates the view to the working orientation and saves the previous state.
4. Perform the needed drafting or tracing in that rotated view.
5. Use **RESET VIEW** when finished.

---

##### RESET VIEW

**Quick Description:**
Restores the saved view / UCS for the current workflow.

**Procedure (Detailed):**

---

#### MOVE POINTS

##### TO SAMPLE LINE

**Quick Description:**
Moves selected points to a sample line. Useful when extracting points along a roadway.

**Procedure (Detailed):**
1. Click **TO SAMPLE LINE**.
2. Select the points to move.
3. Select the target sample line.
4. The routine moves the selected points to the sample line target.
5. Review the moved points before continuing.

---

##### MOVE POINTS TO VERTEX

**Quick Description:**
Moves selected points to a target polyline vertex.

**Procedure (Detailed):**
1. Click **MOVE POINTS TO VERTEX**.
2. Select the points to move. The adjacent polyline must already contain vertex points.
3. Select the target polyline.
4. The routine moves the selected points to that target polyline.

---

#### MOVE POINTS + ADD VERTICES

##### SINGLE/MULTIPLE

**Quick Description:**
Moves points and adds vertices for single or multiple picks.

**Procedure (Detailed):**
1. Click **SINGLE/MULTIPLE**.
2. Choose the point movement/add-vertex option as prompted.
3. Select the source point(s).
4. Select the target location(s).
5. The routine moves points and adds vertices based on the selected mode.
6. Review the updated polyline geometry.

---

##### ADJACENT

**Quick Description:**
Moves points and adds vertices to adjacent polylines as part of the roadway workflow.

**Procedure (Detailed):**
1. Click **ADJACENT**.
2. Select the points/geometry involved in the adjacent-vertex workflow.
3. Follow the prompts to identify the neighboring segment or location.
4. The routine moves points and adds adjacent vertices where needed.

---

#### POLYLINE TOOLS

##### COPY VERTEX FROM PL TO PL

**Quick Description:**
Copies a vertex from one polyline to another.

**Procedure (Detailed):**
1. Click **COPY VERTEX FROM PL TO PL**.
2. Select the source polyline.
3. Select the source vertex.
4. Select the destination polyline.
5. The routine copies/adds the vertex to the destination polyline.
6. Review the resulting geometry.

---

##### ADD VERTEX @ CROSSING

**Quick Description:**
Adds a vertex at a crossing condition.

**Procedure (Detailed):**
1. Click **ADD VERTEX @ CROSSING**.
2. Select the target polyline.
3. Select the crossing location/geometry.
4. The routine inserts a vertex at the crossing condition.
5. Review the updated polyline.

---

##### IDENTIFY VERTICES

**Quick Description:**
Identifies / labels polyline vertices.

**Procedure (Detailed):**
1. Click **IDENTIFY VERTICES**.
2. Select the target polyline.
3. The routine labels or identifies the vertices in sequence.
4. Review the output and erase labels later if they were only needed temporarily.

---

##### GENERAL MARKER

**Quick Description:**
Places a general marker.

**Procedure (Detailed):**

---

### UFLS tab

#### POINT CLOUD DISPLAY

##### ATTACH

**Quick Description:**
Starts the point-cloud attach workflow.

**Procedure (Detailed):**
1. Open the point cloud menu tab.
2. Click the button.
3. Follow the command line prompts for the selected display action.
4. Confirm the point cloud display updates as expected before moving to the next step in the workflow.

---

##### INTENSITY

**Quick Description:**
Runs the point-cloud intensity helper.

**Procedure (Detailed):**
1. Open the point cloud menu tab.
2. Click the button.
3. Follow the command line prompts for the selected display action.
4. Confirm the point cloud display updates as expected before moving to the next step in the workflow.

---

##### ON

**Quick Description:**
Turns point cloud display on.

**Procedure (Detailed):**

---

##### OFF

**Quick Description:**
Turns point cloud display off.

**Procedure (Detailed):**

---

#### PIPE LOCATOR

##### STEP 1 - CROP POINT CLOUD

**Quick Description:**
Crops the point cloud around the target pipe area.

**Procedure (Detailed):**
1. Start the UFLS point-cloud locator workflow.
2. Click **STEP 1 - CROP POINT CLOUD**.
3. Define the crop around the pipe area you want to inspect.
4. Confirm the crop isolates the target pipe area clearly before moving to Step 2.

---

##### STEP 2 - ROTATE VIEW

**Quick Description:**
Rotates the view for the UFLS pipe locator workflow.

**Procedure (Detailed):**
1. After cropping, click **STEP 2 - ROTATE VIEW**.
2. Select the direction references required by the prompt.
3. The routine rotates the view so the pipe can be traced in the working orientation.
4. Confirm the rotated view is correct before moving to Step 3.

---

##### STEP 3 - SET UCS

**Quick Description:**
Sets UCS for the UFLS pipe locator workflow.

**Procedure (Detailed):**
1. Click **STEP 3 - SET UCS**.
2. The routine sets the UCS for the tracing workflow, using the rotated view context.
3. Confirm you are ready to trace in the working orientation before moving to Step 4.

---

##### STEP 4 - TRACE PIPE

**Quick Description:**
Runs the point-cloud pipe locator / tracing workflow.

**Procedure (Detailed):**
1. Click **STEP 4 - TRACE PIPE**.
2. Select the required trace points within the cropped point cloud.
3. The routine fits the pipe location based on the point-cloud picks.
4. Review the traced result before moving to Step 5.

---

##### STEP 5 - RESET VIEW

**Quick Description:**
Restores the UFLS locator view and clears the temporary state.

**Procedure (Detailed):**
1. Click **STEP 5 - RESET VIEW** when tracing is complete.
2. The routine restores the saved view and clears the temporary workflow state.
3. Confirm the drawing is back in the original working orientation.

---

#### 2D LINEWORK

##### STRC-INNER WALL

**Quick Description:**
Traces the inner wall footprint using point-cloud snap settings.

**Procedure (Detailed):**
1. Start the tool.
2. Select or trace the structure location using the required picks for the workflow.
3. The routine creates the **inner wall** footprint on the standard structure layer.
4. The companion outer wall geometry is generated automatically where supported.
5. Review the result and adjust manually if the field evidence indicates a different footprint.

---

##### LOCATE MANHOLE

**Quick Description:**
Places a manhole block and marker point from three picked point-cloud points.

**Procedure (Detailed):**
1. Click **LOCATE MANHOLE**.
2. Pick the first point-cloud point on the interior of the structure.
3. Pick the second point.
4. Pick the third point.
5. The routine computes the manhole center from the three picks and inserts the manhole block.
6. Review the resulting location.

---

##### 3P CIRCLE

**Quick Description:**
Creates a 3-point circle on layer 0 with temporary pick markers.

**Procedure (Detailed):**
1. Start the tool.
2. Pick the first point.
3. Pick the second point.
4. Pick the third point.
5. The routine solves the circle through the three picks and creates the circle.
6. Temporary pick markers may be shown during the workflow for clarity.

---

##### 3P RECTANGLE

**Quick Description:**
Creates a 3-point orthogonal rectangle on layer 0 with temporary pick markers.

**Procedure (Detailed):**
1. Start the tool.
2. Pick the first point/corner.
3. Pick the second point to establish the primary direction.
4. Pick the third point to establish the width.
5. The routine creates the orthogonal rectangle from those three picks.
6. Review orientation before continuing.

---

#### VIEW CONTROL

##### 3D ROTATE

**Quick Description:**
Runs the 3D rotate / rotated-view workflow.

**Procedure (Detailed):**
1. Click **3D ROTATE**.
2. Select the controlling geometry or direction references when prompted.
3. The routine rotates the view to the working orientation and saves the previous state.
4. Perform the needed drafting or tracing in that rotated view.
5. Use **RESET VIEW** when finished.

---

##### RESET VIEW

**Quick Description:**
Restores the saved view / UCS for the current workflow.

**Procedure (Detailed):**

---

#### QUICK CROP

##### CROP

**Quick Description:**
Runs the general crop workflow used in roadway and UFLS flows.

**Procedure (Detailed):**
1. Click **CROP**.
2. Define the crop boundary by following the prompts and selecting the required points or polygon.
3. The point cloud is cropped to the selected area.
4. Review the cropped view and proceed with tracing or structure work.

---

##### UNCROP

**Quick Description:**
Removes the active general point-cloud crop.

**Procedure (Detailed):**

---

#### TOOLS

##### GENERAL MARKER

**Quick Description:**
Places a general marker.

**Procedure (Detailed):**

---

## Q4 — Survey Palette

### MAPPING tab

#### SURVEY MAP

##### BEST FIT MAP

**Quick Description:**
Moves a selected xref or block using numbered survey-to-map control pairs, previews the fit, then applies the best-fit transform and writes a CSV residual report.

**Procedure (Detailed):**
1. Open **Q4 → MAPPING**.
2. Click **BEST FIT MAP**.
3. Select the xref or block to transform.
4. Select the matching survey control point.
5. Select the matching map control point.
6. Continue adding survey/map control pairs as prompted.
7. Finish the pair selection when enough control points have been added.
8. The routine previews and computes the best-fit transformation.
9. Accept the result to move the selected reference into the best-fit mapped position.
10. Review the residual report/output if generated.

---

## CLV — Web / CAD Services Menu

Menu command: `CLV`

### CLV APS

#### BENCHMARKS

**Quick Description:**
Opens the City benchmark web map/application.

**Procedure (Detailed):**

---

#### CLV GIS

**Quick Description:**
Opens the CLV GIS infrastructure web experience.

**Procedure (Detailed):**

---

#### CONTENT MANAGER

**Quick Description:**
Opens the public content manager/drawing search website.

**Procedure (Detailed):**

---

#### INFOR

**Quick Description:**
Opens the CLV Infor site.

**Procedure (Detailed):**

---

### CLV COORDINATE SYSTEMS

#### NV83.NCRS-LVF

**Quick Description:**
Assigns the main CLV coordinate system and restores the matching named view.

**Procedure (Detailed):**

---

#### NV83.NCRS-LVHEF

**Quick Description:**
Assigns the high-elevation CLV coordinate system and restores the matching named view.

**Procedure (Detailed):**

---

### CAD SUPPORT

#### UPDATE ALIAS

**Quick Description:**
Runs the support batch file used to update alias/configuration support per the user.

**Procedure (Detailed):**

---

#### RELOAD RIBBON

**Quick Description:**
Unloads and reloads the CLV ribbon/CUIX support files.

**Procedure (Detailed):**

---

### SURVEY DATABASE

#### PROJECTS

**Quick Description:**
Opens the protected Projects database after PIN entry.

**Procedure (Detailed):**

---

#### TECHNICAL REVIEWS

**Quick Description:**
Opens the protected Technical Reviews database after PIN entry.

**Procedure (Detailed):**

---

### FIELD PHOTOS

#### IMPORT PHOTOS

**Quick Description:**
Scans a selected folder for geotagged photos and places photo-location blocks with hyperlinks.

**Procedure (Detailed):**
1. Open the **CLV** menu.
2. Click **IMPORT PHOTOS**.
3. Browse to and select the folder that contains the project photos.
4. The routine scans the folder for photos with valid geotag data.
5. For each geotagged photo, a photo location block is inserted in the drawing on the image reference layer.
6. A hyperlink to the photo file is attached to each inserted block.
7. Review the inserted photo points and confirm the geolocation looks correct.

---

#### VIEW PHOTOS

**Quick Description:**
Opens the photo hyperlink stored on a selected photo location block.

**Procedure (Detailed):**

---

### WEB SERVICES

#### GISMO

**Quick Description:**
Opens the Clark County GISMO web map.

**Procedure (Detailed):**

---

#### GOOGLE MAPS

**Quick Description:**
Converts a picked drawing point to latitude/longitude and opens the location in Google Maps.

**Procedure (Detailed):**

---

#### RECORDERS OFFICE

**Quick Description:**
Opens the Clark County Recorder's Office website.

**Procedure (Detailed):**

---

### DESIGN STANDARDS

#### USD

**Quick Description:**
Opens the USD design standards website.

**Procedure (Detailed):**

---

#### UDACS

**Quick Description:**
Opens the UDACS PDF reference.

**Procedure (Detailed):**

---

#### DCSWCS

**Quick Description:**
Opens the DCSWCS web/PDF reference.

**Procedure (Detailed):**

---

#### NDOT

**Quick Description:**
Opens the NDOT standards/specifications page.

**Procedure (Detailed):**

---

## DOCS — Documents Menu

Menu command: `DOCS`

### PLAN SAMPLES

#### 30% PLAN SAMPLE

**Quick Description:**
Opens the 30% plan sample PDF.

**Procedure (Detailed):**

---

#### 70% PLAN SAMPLE

**Quick Description:**
Opens the 70% plan sample PDF.

**Procedure (Detailed):**

---

#### S-CROSS SECTIONS

**Quick Description:**
Opens the S-Cross Sections reference PDF.

**Procedure (Detailed):**

---

### HATCHING & EXHIBITS

#### EASEMENT HATCHING

**Quick Description:**
Opens the easement hatching reference PDF.

**Procedure (Detailed):**

---

#### ROADWAY HATCHING

**Quick Description:**
Opens the roadway hatching reference PDF.

**Procedure (Detailed):**

---

### CAD STANDARDS

#### CAD STANDARDS MANUAL

**Quick Description:**
Opens the engineering CAD standards manual PDF.

**Procedure (Detailed):**

---

#### CAD CHECKLIST

**Quick Description:**
Opens the CAD checklist PDF.

**Procedure (Detailed):**

---

#### PLAN SET CHECKLIST

**Quick Description:**
Opens the plan set checklist PDF.

**Procedure (Detailed):**

---

### REFERENCE

#### PIPE MATERIALS (EXCEL)

**Quick Description:**
Opens the Pipe Materials Excel workbook.

**Procedure (Detailed):**

---

#### SIGN CATALOG

**Quick Description:**
Opens the sign catalog PDF.

**Procedure (Detailed):**

---

#### TRAINING VIDEOS

**Quick Description:**
Opens the training video playlist PDF.

**Procedure (Detailed):**

---
