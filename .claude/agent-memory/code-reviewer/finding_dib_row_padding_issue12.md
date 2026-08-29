---
name: finding-dib-row-padding-issue12
description: Issue #12 fix (branch fix/rgb24-row-padding) — CCropVCamStream split FrameBytes() into Packed/Padded row+frame helpers to satisfy BI_RGB's 4-byte row alignment; reviewed clean
metadata:
  type: project
---

`CCropVCamStream` (`src/CropVCam.Filter/src/CropVCamStream.cpp/.h`) previously treated every DIB row as tightly packed (`width*3`), but `BI_RGB` requires each row padded to a 4-byte boundary. Fixed by splitting the old single `FrameBytes()` into four helpers:
- `PackedRowBytes()` = `width_*3` (matches shared-memory stride, unchanged)
- `PaddedRowBytes()` = `(PackedRowBytes()+3) & ~3` (DIB-visible stride)
- `PackedFrameBytes()` — used only for the shared-memory read size (`RefreshLatestFrame`/`WaitAndCopyFrame`)
- `PaddedFrameBytes()` — used everywhere DirectShow sees the size: `GetStreamCaps` bitrate, `GetMediaType`'s `biSizeImage`, `DecideBufferSize`'s `cbBuffer`, `FillBuffer`'s size check/`SetActualDataLength`

A new `CopyRowsWithPadding` expands `latestFrame_` (packed) into the output sample (padded stride) row-by-row, zero-filling the padding tail.

**Why this split is correct (verified 2026-08-27):** the `(x+3)&~3` idiom already exists in the vendored `baseclasses/checkbmi.h:71` and `baseclasses/vtrans.cpp:141-142` for the identical purpose — this isn't a new pattern in the codebase, just reused correctly outside `baseclasses/`. Bounds-checked both ends of the row-copy loop: destination max write offset is `(height_-1)*paddedRowBytes + packedRowBytes ≤ PaddedFrameBytes()` (matches the `pSample->GetSize()` guard in `FillBuffer`); source max read offset is `PackedFrameBytes() ≤ kMaxFrameBufferBytes` (since `width_`/`height_` are bounded by `kMaxWidth`/`kMaxHeight` via `SharedFrameReader::TryReadValidHeader`). The shared-memory protocol itself (`SharedFrameProtocol.h/.cs`, `SharedFrameReader`) intentionally stays packed and untouched — this fix is entirely on the DirectShow-facing side, so [[finding_shared_region_size_mismatch]] does not apply here (no MMF capacity changed).

**How to apply:** if a future diff touches `CCropVCamStream`'s size/stride helpers again, re-verify the same three things: (1) every DirectShow-visible size (`biSizeImage`, `cbBuffer`, `SetActualDataLength`, bitrate calc) uses `PaddedFrameBytes()`/`PaddedRowBytes()`, never packed; (2) the shared-memory read size passed to `RefreshLatestFrame`/`WaitAndCopyFrame` stays on the packed value, since `SharedFrameProtocol`'s stride is defined as `width*3` on both language sides; (3) any new per-row copy loop's max offset on both source and destination stays proven in-bounds the same way (don't just trust the size-check gate at the top of `FillBuffer` — trace the actual last-row arithmetic).
