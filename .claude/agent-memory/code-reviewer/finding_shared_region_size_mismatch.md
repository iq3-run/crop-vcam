---
name: finding-shared-region-size-mismatch
description: PR #2 shipped with the C++ reader requesting a larger MapViewOfFile view than the C# writer's mapping actually is — verify this stays fixed
metadata:
  type: project
---

In the PR #2 review (2026-08-09), `SharedFrameProtocol.cs` sized the memory-mapped file from `OutputWidth`/`OutputHeight` (1280x720, ~2.76MB) while `SharedFrameProtocol.h` sized `kSharedRegionBytes` from `kMaxWidth`/`kMaxHeight` (1920x1080, ~6.22MB), and `SharedFrameReader::EnsureOpen()` called `MapViewOfFile(..., kSharedRegionBytes)`. Requesting a view larger than the underlying section's actual committed size makes `MapViewOfFile` fail every time, so the filter would never successfully open the mapping and the virtual camera would never deliver a real frame — a total feature break, not a partial degradation.

**Why:** The C++ side's `kMaxWidth`/`kMaxHeight` constants exist for aspirational future-proofing ("sized once for the largest resolution we support") but the C# side never actually implements that — it only ever allocates for the fixed 1280x720 canvas. The two sides drifted because the size constant was defined independently in each language instead of being derived from one shared source of truth.

**How to apply:** When reviewing changes to either `SharedFrameProtocol.cs` or `SharedFrameProtocol.h`, or to `SharedFrameWriter.cs`/`SharedFrameReader.cpp`, MUST re-check that whatever size the C# writer passes to `MemoryMappedFile.CreateOrOpen` is `>=` whatever size the C++ reader passes to `MapViewOfFile` (equal is what the design intends). This class of bug (byte-layout fields matching but overall region *size* not matching) is easy to miss because the header field offsets were correctly kept in sync — only the total capacity drifted.

**2026-08-15 re-check (`feat/input-size-output-and-preview`):** The fixed 1280x720 canvas was replaced with dynamic output tracking the physical camera's resolution, clamped to a new 4K UHD cap. Renamed `OutputWidth/Height` → `MaxWidth/MaxHeight` (C#) and `kOutputWidth/Height` → `kMaxWidth/kMaxHeight` (C++), both set to 3840x2160, with `SharedRegionBytes`/`kSharedRegionBytes` computed as `HeaderSize + MaxWidth*MaxHeight*3` identically on both sides — confirmed byte-for-byte equal (24,883,232 bytes). This class of bug has NOT recurred here; the two sides stayed in lockstep. Both files also added a comment cross-referencing this memory file directly (`.claude/agent-memory/code-reviewer/finding_shared_region_size_mismatch` by name) — see [[style-agent-memory-path-in-comments]] for why that specific pointer (not the size-sync practice itself) is worth flagging next time.
