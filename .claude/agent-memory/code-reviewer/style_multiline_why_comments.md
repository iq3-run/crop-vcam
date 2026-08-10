---
name: style-multiline-why-comments
description: crop-vcam's C# files consistently use 3-5 line WHY comment blocks, in tension with CLAUDE.md's "one short line is the cap" rule — treat as an established, deliberate repo convention, not a violation
metadata:
  type: project
---

Throughout `src/CropVCam.App` (e.g. `FilterRegistrar.IsRegistered`, `App.ActivateMainWindow`, and — as of the 2026-08-10 `feat/unregister-filter-on-exit` review — the new `FilterRegistrar.TryUnregister` / `App._isPrimaryInstance` / `MainViewModel.UnregisterFilter` comments) the team writes multi-line (3-5 line) comment blocks explaining non-obvious WHY (threading/shutdown races, idempotency guarantees, cross-file invariants). CLAUDE.md's comment section says "one short line is the cap" outside published-package JSDoc, but this repo has never followed that literally for WHY comments — every reviewed PR so far uses short paragraphs instead.

**Why:** The content in every instance checked so far is genuine non-obvious WHY (not WHAT), and splitting it into a single line would lose real explanatory content (e.g. the deadlock-avoidance reasoning in `ActivateMainWindow`, or why `TryUnregister` is safe to call unconditionally). The surrounding code consistently justifies the length.

**How to apply:** Do not flag multi-line WHY comment blocks in this repo as a rule violation on their own. Still flag a comment if it explains WHAT instead of WHY, references the current task/PR/issue number, or if a *new* one-off block breaks from this established multi-line-WHY pattern in an inconsistent way.
