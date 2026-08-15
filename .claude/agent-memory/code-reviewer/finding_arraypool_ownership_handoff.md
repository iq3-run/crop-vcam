---
name: finding-arraypool-ownership-handoff
description: How MainViewModel's dedicated ArrayPool<byte> buffer handoff between OnFrameCaptured and the WPF Dispatcher is sized and kept leak/double-return-free — reviewed and confirmed correct 2026-08-15 (issue #13)
metadata:
  type: project
---

`MainViewModel.OnFrameCaptured` rents a frame buffer from a dedicated
`FrameBufferPool` (not `ArrayPool<byte>.Shared` — Shared's ~1MiB bucket cap
would never actually pool a 6-24MiB frame), writes pixels into it, passes it
synchronously to `SharedFrameWriter.WriteFrame` (which copies out before
returning — no lingering reference), then to `UpdatePreview`. `UpdatePreview`
returns `bool` for whether it took ownership: `true` means it queued a
`Dispatcher.BeginInvoke` callback that builds a `BitmapSource` (which copies
pixels internally) and returns the buffer to the pool in its own `finally`;
`false` means the frame was dropped by the existing `_previewUpdatePending`
throttle and `OnFrameCaptured`'s `finally` must return it itself.

**Why `maxArraysPerBucket: 2` is correct, not a guess:** `OnFrameCaptured` is
called synchronously one-at-a-time from a single dedicated capture thread
(confirmed invariant), and `_previewUpdatePending` allows at most one
dispatched-but-not-yet-rendered preview frame at a time. So at any instant at
most 2 buffers of the same size bucket are outstanding: the one owned by a
pending dispatcher callback, plus the one being rented/copied/returned
synchronously within the currently-executing `OnFrameCaptured` call. If a
future change relaxes either invariant (e.g., allows concurrent capture
callbacks, or queues more than one pending preview frame), this bucket size
must be revisited.

**How to apply:** On a future diff touching this handoff, re-verify: (1) no
new code path holds a reference to the rented array past the point
`WriteFrame`/the dispatched `BitmapSource.Create` call returns, (2) any
`ArrayPool.Rent`/`Return` pair still has exactly one `Return` per `Rent`
reachable from every exception path, (3) if camera resolution can change
mid-session, note that switching frame size moves the pool to a different
size bucket — old-size buffers become unrecoverable garbage until GC, not a
correctness bug, just memory not maximally reused (not raised as a finding
in the #13 review since it's pre-existing scope, not new).
