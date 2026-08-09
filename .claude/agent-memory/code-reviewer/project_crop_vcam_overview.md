---
name: project-crop-vcam-overview
description: What crop-vcam is, its two-language structure, and the hard non-admin constraint — read before reviewing this repo
metadata:
  type: project
---

crop-vcam is a Windows desktop app with two parts that must be reviewed together because they share a byte-level protocol:
- `src/CropVCam.App` — C#/.NET 8 WPF app (camera capture via OpenCvSharp, crop/scale math, single-instance enforcement, registry registration of the filter).
- `src/CropVCam.Filter` — native C++ DirectShow source filter, loaded in-process inside the *consumer* app (e.g. Zoom), not inside CropVCam.App. It is built on Microsoft's vendored MIT-licensed DirectShow base classes under `src/CropVCam.Filter/baseclasses/` — that directory is untouched third-party code and MUST be excluded from style review every time (still fine to grep it for context, e.g. to check for symbol collisions with vendored `dllsetup.cpp`).

**Why:** The app targets a locked-down corporate PC where the user cannot elevate at all, so it is a hard, explicitly-confirmed requirement that the app NEVER trigger UAC, not even at first-run filter registration. This is why `Registration.cpp` and `FilterRegistrar.cs` write only to `HKEY_CURRENT_USER\Software\Classes\...` and never call into the vendored `AMovieDllRegisterServer`/`dllsetup.cpp` path (which uses `HKEY_CLASSES_ROOT` and would silently require admin). As of the 2026-08-09 review this separation was verified correct and deliberate — Registration.cpp defines its own `DllRegisterServer`/`DllUnregisterServer` and never calls the base classes' HKCR-based helpers, so there's no accidental elevation path and no symbol collision either.

**How to apply:** On every future review of this repo, if `Registration.cpp`, `FilterRegistrar.cs`, or the CMakeLists/link setup change, MUST re-verify no new code path touches `HKEY_LOCAL_MACHINE`/`HKEY_CLASSES_ROOT` or calls the vendored `AMovieDllRegisterServer` family. Also: the C# (`SharedFrameProtocol.cs`) and C++ (`SharedFrameProtocol.h`) sides define a shared-memory frame protocol that MUST stay byte-layout- and **size**-compatible — always check the actual `MemoryMappedFile.CreateOrOpen` capacity on the C# side against whatever size the C++ side passes to `MapViewOfFile`, not just the header field offsets. See [[finding-shared-region-size-mismatch]].
