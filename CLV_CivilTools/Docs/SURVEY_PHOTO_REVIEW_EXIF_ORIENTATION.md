# Survey Photo Review — EXIF Orientation

## 2026-09-16

`Survey/SurveyPhotoReview.cs` now applies the JPEG EXIF Orientation tag when loading a field photo into the Survey Photo Review palette.

- The viewer reads EXIF Orientation from the image in memory.
- Orientations 2–8 are mapped to the corresponding `RotateFlipType` operation.
- The original photo file is not modified.
- `OPEN IMAGE` continues to open the original file, preserving the existing workflow.
- GPS latitude, longitude, and image heading handling are unchanged.

This corrects the case where a field photo displays rotated in the Civil 3D palette while Windows or another image viewer displays the same file in the expected orientation.

## Validation

Build the Civil 3D project and test `VIEWPHOTOS` / `CLV-PHOTO-REVIEW` with field photos captured in multiple device orientations. Compare the palette image with the image opened through `OPEN IMAGE` or Windows Explorer.
