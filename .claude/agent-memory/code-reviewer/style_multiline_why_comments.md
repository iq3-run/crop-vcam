---
name: style-multiline-why-comments
description: crop-vcam's C# files consistently use 3-5 line WHY comment blocks, in tension with CLAUDE.md's "one short line is the cap" rule — treat as an established, deliberate repo convention, not a violation
metadata:
  type: project
---

Throughout `src/CropVCam.App` (e.g. `FilterRegistrar.IsRegistered`, `App.ActivateMainWindow`, and — as of the 2026-08-10 `feat/unregister-filter-on-exit` review — the new `FilterRegistrar.TryUnregister` / `App._isPrimaryInstance` / `MainViewModel.UnregisterFilter` comments) the team writes multi-line (3-5 line) comment blocks explaining non-obvious WHY (threading/shutdown races, idempotency guarantees, cross-file invariants). CLAUDE.md's comment section says "one short line is the cap" outside published-package JSDoc, but this repo has never followed that literally for WHY comments — every reviewed PR so far uses short paragraphs instead.

**Why:** The content in every instance checked so far is genuine non-obvious WHY (not WHAT), and splitting it into a single line would lose real explanatory content (e.g. the deadlock-avoidance reasoning in `ActivateMainWindow`, or why `TryUnregister` is safe to call unconditionally). The surrounding code consistently justifies the length.

**How to apply:** Do not flag multi-line WHY comment blocks in this repo as a rule violation on their own. Still flag a comment if it explains WHAT instead of WHY, references the current task/PR/issue number, or if a *new* one-off block breaks from this established multi-line-WHY pattern in an inconsistent way.

**Also covers XML `<summary>` doc blocks on internal (non-public) classes.** Every top-level class in `src/CropVCam.App` — `SingleInstance`, `CameraCapture`, `CameraDevice`, `CameraEnumerator`, `CenterCropScaler`, `AppSettings`, `SettingsStore`, `FilterRegistrar`, `SharedFrameProtocol`, `SharedFrameWriter`, and — as of the 2026-08-23 `feat/minimize-to-tray-while-streaming` (#16) review — the new `TrayIcon` — carries a 2-4 line `/// <summary>` block even though none of these classes are part of a published package. CLAUDE.md's "no multi-line comment blocks outside published-package JSDoc" reads as a violation here too, but it is the repo-wide norm for every internal class, not an exception. Do not flag a new internal class's summary doc comment for this reason alone; only flag it if the summary explains WHAT (restates the class name/members) rather than WHY (purpose, non-obvious constraint), or is a single new outlier that breaks an otherwise-consistent per-file style.
