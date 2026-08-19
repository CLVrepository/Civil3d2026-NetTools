# PDF Viewer / Map Review — Version 1

## Commands

- `PDFVIEW`
- `MAPREVIEW`

The Q4 Survey palette also includes **PDF VIEWER** under **SURVEY MAP**.

## Purpose

The PDF Viewer is a separate dockable/floating WinForms `PaletteSet` used during Civil 3D map review. It displays the applicable PDF plan sheet and follows model-space pan and zoom without inserting a PDF underlay into the drawing.

## Version 1 workflow

1. Run `PDFVIEW` or select **Q4 > MAPPING > SURVEY MAP > PDF VIEWER**.
2. Select **OPEN / RELINK PDF** and choose the multipage plan PDF.
3. After calibration, define the model-space coverage using the current viewport, a rectangle, picked polygon vertices, or an existing closed polyline.
4. Select **ADD PLAN MAPPING**.
5. Enter a sheet name and PDF page number.
6. Click the first known point in the PDF, then select the matching point in model space.
7. Click the second known point in the PDF, then select the matching point in model space.
8. Pan or zoom model space. The viewer selects the mapped plan sheet containing the model-space view center and renders the matching PDF area.

## Reference categories

Use **ADD REFERENCE** for pages that do not need coordinate calibration:

- Profiles
- Tables
- Details
- Notes

Reference pages can be named and optionally pinned to the top of their category. The **Tables** category is intended for line and curve tables, whether they are on a dedicated table sheet or another selected PDF page.

## Navigation

- Category buttons filter the named sheet list.
- Previous and Next cycle through the current category.
- **RETURN TO CURRENT** switches back to Plans, re-enables automatic following, selects the plan sheet covering the current model-space location, and matches the displayed PDF area to the current view.
- Manual category or sheet selection pauses automatic following until **RETURN TO CURRENT** is selected.

## Storage

Configuration is stored in the DWG Named Object Dictionary under `CLV_PDF_VIEWER_V1`.

Stored data includes:

- Absolute PDF path
- Relative PDF path from the DWG folder
- Named sheets and categories
- PDF page numbers
- Plan calibration point pairs
- Model-space coverage polygon vertices and extents
- Pin and priority fields

The PDF itself is not embedded in the drawing.

## Rendering dependency

PDF rendering is isolated from Civil 3D. The main `CLV_CivilTools` assembly does not reference or load PDFium, `PDFtoImage`, or SkiaSharp. Instead it launches `PdfRenderer\CLV.PdfRenderHost.exe`, which performs each metadata/render request in a separate x64 process and returns a PNG image. If the renderer fails, Civil 3D remains running and the palette reports the error.

The post-build target copies the complete output tree to `C:\Temp\C3DDev`. Server deployment must include the entire `PdfRenderer` folder beside the versioned Civil Tools DLL. The PDF renderer DLLs and native runtime folders should remain inside `PdfRenderer`; they are not required in the Civil Tools root folder.

## Version 1 limitations

- Model-space-to-PDF synchronization is one direction only.
- Automatic sheet selection uses the model-space view center.
- Mapping coverage can be the current viewport, a rectangle, a picked polygon, or a selected closed lightweight polyline.
- View-twist-aware rotated rendering is not included in Version 1. The correct geographic PDF area is rendered, but a twisted model-space view may not have the same screen orientation.
- One PDF file is configured per DWG in Version 1.
- Reference pages are full-page views; individual detail/table crop regions are intentionally not required.


## Fix 5 deployment layout

```text
CLV_CivilTools_<version>.dll
CLV_CivilTools_<version>.deps.json   (when used by the server loader)
PdfRenderer\
  CLV.PdfRenderHost.exe
  CLV.PdfRenderHost.dll
  CLV.PdfRenderHost.deps.json
  CLV.PdfRenderHost.runtimeconfig.json
  PDFtoImage.dll
  SkiaSharp.dll
  runtimes\...
```

Do not move `PDFtoImage.dll`, `SkiaSharp.dll`, or the native `runtimes` content into the Civil 3D plugin root. Keeping those files under `PdfRenderer` is what prevents their native libraries from being loaded into the Civil 3D process.


## Multipage PDF support and load diagnostics
- Use the complete multipage plan-set PDF; splitting it into individual page files is not required.
- The viewer validates the file through the isolated renderer before saving it to the drawing.
- A zero-page result is treated as a load failure, and the actual renderer message is displayed.
- Common causes include a damaged PDF, password/encryption protection, an unsupported PDF structure, or missing files in the `PdfRenderer` deployment folder.


## Manual PDF navigation
- Click **UNLOCK PDF** to temporarily stop automatic following.
- Use the mouse wheel to zoom around the cursor.
- Hold the left mouse button and drag to pan.
- Click **RETURN TO CURRENT** to relock the PDF and restore the model-space synchronized plan view.
- During plan calibration, PDF navigation is automatically unlocked so control points can be selected precisely.

### Mapping compatibility
Mappings created before Fix 12 must be removed and recreated because the PDF Y-axis conversion was corrected from screen-style Y-down coordinates to drawing-style Y-up coordinates.

### Saved Reference Views
Use **ADD REFERENCE VIEW**, enter the name/category/page, then pan and zoom to the desired table, profile, detail, or notes area. Click **SAVE REFERENCE VIEW** to store the exact PDF view. Selecting that reference later restores the saved page, pan, and zoom. **RETURN TO CURRENT** returns to automatic plan-sheet tracking.

## Unified sheet and reference navigator
- The category filter buttons were removed.
- Plans, profiles, tables, details, and notes are shown together in one permanent list.
- Each entry is prefixed with its category, and selecting it opens the sheet or saved reference view immediately.
- Pinned entries sort to the top of the combined list.
- RETURN TO CURRENT restores automatic model-space plan following.

### Multiple plan sheets and coverage boundaries
Each plan sheet is calibrated independently. After the second matching model-space point, choose **Current**, **Rectangle**, **Polygon**, or **Polyline** for that sheet's coverage. Polygon mode accepts picked vertices and Enter closes the polygon. Polyline mode copies the vertices and bulges from an existing closed lightweight polyline, so the source polyline can later be removed without breaking the mapping. The active mapped sheet displays a temporary yellow boundary in model space; no drawing object is created. Use **EDIT BOUNDARY** to redefine coverage without recalibrating the PDF.

When coverage polygons overlap, automatic following chooses the smallest polygon containing the current model-space view center, then uses sheet priority and the currently displayed sheet as tie-breakers.
