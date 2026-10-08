# Create GIS Drawing

`CLV-GIS-NEW-DRAWING` (Q2 > GIS > DATA > **CREATE GIS DRAWING**) prepares a separate, unsaved drawing for the existing GIS workflow. Export the Civil network SDF first, then run this command in the original survey/network drawing. The original drawing's assigned coordinate system is authoritative.

The command does not export the network, run R5 structure conversion/cleanup, or save/overwrite a DWG. Native Civil 3D acceptance of the complete workflow remains required.

## Inputs

- Original drawing assigned exactly `NV83.NCRS-LVF` or `NV83.NCRS-LVHEF`. A missing or unsupported assignment stops before creating a drawing; there is no default.
- The exported SDF selected at the file prompt. Its raw feature coordinates are interpreted in the original drawing's assigned system. No SDF coordinate-system assignment, WKT, spatial-context association or dictionary-equivalence check is required.
- Template: `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\Drawing Templates\Blank (2026).dwt`.
- Profiles: `\\ci.las-vegas.nv.us\pw_data_depot\PW_AutoCAD_Support\2026_Civil3D\SDF to SHP`.
- Exactly one profile: LVF selects `UFLS-IMPORT-NV83.NCRS-LVF.ipf`; LVHEF selects `UFLS-IMPORT-NV83.NCRS.LVHEF.ipf`. The period before `LVHEF` is intentional.

Shared files are read-only inputs. No mapped drive, copied profile, new LISP helper or shared-profile edit is introduced.

## Workflow

1. Read the original drawing's actual Map Projection and snapshot eligible model-space survey outlines and known DI/MH blocks, including dynamic state, attributes, XYZ and attached Object Data.
2. Prompt for the SDF. Check readable template/profile/SDF inputs, the supplied profile's selected classes/mappings, and SDF schema, raw geometry and scalar fields through read-only native FDO.
3. Create a new drawing from Blank. Require empty model space, match source insertion-unit metadata, and assign the source coordinate-system code once with readback.
4. Clone eligible geometry and dependencies at native coordinates. Verify geometry, attributes, dynamic state, Object Data, mapped definitions and child topology. Incompatible named definitions stop the operation instead of replacing existing resources.
5. Initialize the native importer and load the one matching IPF. For each selected input layer, explicitly set its incoming/from coordinate-system code to the source drawing's code and read it back. The destination already has that same code.
6. Import Pipes and Structures, then compare entity counts, unique identities, mapped OD and every vertex XYZ against the raw SDF snapshot. Require zero transformation skips and an absolute coordinate difference no greater than 0.000001 drawing units. Unexpected transformed/scaled output fails verification.
7. Leave the verified drawing open and unsaved. The completion message identifies its drawing name. Use SAVEAS to choose a file name and folder before running the existing GIS preparation command.

The source is unchanged. File sharing guards and before/after hashes detect changed inputs. Failure or cancellation after destination creation attempts to discard only this command's fresh drawing and return to the original. If disposal fails, the remaining drawing is explicitly reported incomplete and must be closed without saving. Failure reporting is deferred until cleanup/restoration completes, so the failed operation stage and captured exception remain visible in the surviving document instead of being lost with a discarded drawing. A discarded attempt creates no saved output file. Cleanup failures supplement the original report. A destination-only `CLV_GIS_NEW_DRAWING_V1` Xrecord records setup status; existing conversion commands do not interpret it.

## Coordinate handling

The simplified workflow uses Map `ActiveProject.Projection` for drawing assignment/readback. It no longer loads MapGuide's CRS factory, mathematical comparator, dictionary or WKT parser. Source code chooses the profile; embedded SDF CRS labels do not choose or block the workflow.

Autodesk Map 3D 2026 documents `InputLayer.TargetCoordinateSystem` as the read/write coordinate system that incoming data is transformed **from**, corresponding to the Coordinate System column in the Import dialog. Its name does not mean the drawing's output CRS. The command explicitly sets it to the source drawing's code after `LoadImportFormat`, so incoming interpretation and destination assignment agree. `OriginalCoordinateSys` is documented unimplemented and is not used.

Both supplied IPFs enable coordinate conversion; they remain unchanged. The .NET importer does not expose a separate global conversion toggle. Giving input and destination the same source code establishes the intended identity import, and raw XYZ readback verifies the result. The filename alone is never treated as proof that imported coordinates stayed unchanged. No transform function, dictionary edit or SDF metadata write is performed by this command.

## Preserved data checks and limits

Both supplied profiles select `Civil_Schema:Pipes` and `Civil_Schema:Structures`; Alignments, Parcels and Points are off, and spatial clipping is off. FDO preflight verifies exact declared schema/class membership before accepting reader definitions, including detached reader copies. No source-file filters or writes are added.

Supported SDF geometry is Point/LineString XY/XYZ. Curved, multipart or measured geometry remains unsupported. Imported Structures must be native DBPoints; pipe curves must preserve the raw LineString vertices. Explicit null/STUB structure points are retained; this command performs no downstream conversion cleanup.

Required OD fields:

- Pipes: Name, InsideDiameter, Length, Slope, StartInvert, EndInvert, StructureStart, StructureEnd, PartSizeName
- Structures: Name, PartSizeName

NetworkName, RimElevation, OutsideDiameter and Autogenerated_SDF_ID remain intentionally unmapped in the supplied profiles.

The IPFs contain `MappedToOD` column entries alongside table-level `NoODTable`, `ImportMappingInvalid` and empty ObjectDataName values. The command does not invent mappings from these conflicting XML fields. After loading, the native importer must expose effective OD mapping to Pipes/Structures with the expected columns. Otherwise it stops before import with the actual mapping. That native profile behavior remains an acceptance item.

Clone checks continue to distinguish inherited/ACI/RGB color and transparency modes without invoking unsupported getters. Generated anonymous block names may change between drawings only when exact IdMapping/reference/geometry/topology checks establish their identity. Named block collisions remain guarded.

## Built-in resource collisions

An empty Blank drawing still has built-in records such as Continuous, ByLayer, ByBlock, layer 0 and Standard. The command checks their captured rendering properties before reusing a compatible record; their names alone are not an exemption, and equal handles from separate databases are not compared as identity.

Linetype Comments is a description shown in dialogs, not its pattern definition. It remains part of exact source-unchanged checks but is excluded from cross-drawing appearance comparison. Symbol-name case follows native case-insensitive table identity consistently in resource and entity references. Pattern length/count, scaling, every dash/shape/text setting and style dependency remain checked. Text-style FlagBits controls mirrored text and remains exact, as do font paths, font descriptors, text content and color/book names. No filename-only or blanket built-in fallback is used.

A genuine conflict reports the differing property names and source/target values before cloning, or during destination readback if cloning changed a resource. Named block collisions and non-built-in linetype reuse remain unsupported. Existing material/viewport-override restrictions are unchanged; this does not add support for richer resource types.

The latest native trial identified a Continuous collision before any clone but did not expose the actual differing field. Description/name-case acceptance is based on the documented contracts; the trial must still verify the actual definitions and output. The original shared template is never edited.

## Native import-setting readback

The four native mapping getters (`LayerName`, `DataMapping`, `PointToBlockMapping`, and `ColumnDataMapping`) return enum values through native output pointers in the published Map 2026 API. They require writable enum storage; null arguments are not managed `out` enum slots. The dedicated reader checks the actual signature, supplies initialized storage of the enum's exact underlying size, validates that a defined value was written, and frees the storage on every exit. A managed enum-reference shape is handled only when the reflected signature actually declares it.

The command continues to inspect the loaded profile's real CAD-layer, OD-table, column and point mappings. These getters are not replaced with setters or guessed mappings. Layers/columns are obtained after `LoadImportFormat`, which can invalidate earlier iterators when its schema changes. The borrowed importer singleton remains alive, and returned wrappers follow the existing cleanup.

## Validation

Run the existing profile, appearance, SDF, source-coordinate policy and storm regression suites, then build the actual plugin against installed Civil 3D 2026/Map assemblies. For a .NET 10-only test executor, use `-p:TargetFramework=net10.0 -p:GisTestTargetFramework=net10.0`. Do not retarget the production project as part of this feature.

- `Tests/GisNewDrawingProfile.Tests/GisNewDrawingProfile.Tests.csproj`
- `Tests/GisNewDrawingAppearance.Tests/GisNewDrawingAppearance.Tests.csproj`
- `Tests/GisNewDrawingSdf.Tests/GisNewDrawingSdf.Tests.csproj`
- `Tests/GisNewDrawingCoordinateSystem.Tests/GisNewDrawingCoordinateSystem.Tests.csproj`
- `Tests/GisNewDrawingNativeOutputs.Tests/GisNewDrawingNativeOutputs.Tests.csproj`
- `Tests/StormStructureMatching.Tests/StormStructureMatching.Tests.csproj`

The SDF runner deliberately uses the test assembly name `OSGeo.FDO`; never deploy it or other test outputs into Civil 3D. The MapGuide fake and obsolete semantic-CRS tests were removed. Coordinate policy tests exercise source-code assignment/readback without a MapGuide dependency. The separate native-output suite uses unsafe fake getter signatures only inside its test project; the production reader uses safe C# and no Autodesk test binaries are deployed.

Native acceptance should verify the actual source drawing and template, copied XYZ/dynamic/attribute/OD state, imported counts/fields/vertices, missing/unsupported source assignment, disconnected UNC files, unchanged source inputs, cancellation/partial failure and repeated invocation. Test LVF and LVHEF separately. The supplied sample is expected to contain 13 pipes and 22 structures including one null/STUB; establish those counts through the complete native run before calling them an acceptance result. Its 22 imported structures differ from the existing conversion's 21 real structures.
