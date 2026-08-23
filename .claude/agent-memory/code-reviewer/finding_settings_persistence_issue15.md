---
name: finding-settings-persistence-issue15
description: Settings/AppSettings.cs and Settings/SettingsStore.cs (issue #15) — patterns confirmed on first review, re-check only if the shape changes
metadata:
  type: project
---

`src/CropVCam.App/Settings/AppSettings.cs` and `SettingsStore.cs` implement JSON
settings persistence (`%LOCALAPPDATA%\CropVCam\settings.json`) per
`plans/feat-settings-persistence.md`. Reviewed clean on first pass (build green,
0 warnings). Notes for future diffs touching this area:

- **Caller-reference comments are pre-existing house style, not new drift.**
  `MainViewModel.SaveSettings()`'s comment opens with "Called from
  MainWindow.Closed, before Dispose -" — this literally matches the global
  CLAUDE.md "never reference callers in comments" rule, but `UnregisterFilter()`
  a few lines below (untouched, pre-existing) uses the identical "Called from
  App.OnExit, ..." phrasing. Treat this phrasing as established file convention
  when it states an ordering/invariant constraint (not mere provenance), don't
  flag it as new drift. See [[style_multiline_why_comments]].
- **`AppSettings.OutputName` is typed non-nullable `string` but .NET 8
  `System.Text.Json` does not enforce that on deserialize** (no
  `RespectNullableAnnotations`, that's .NET 9+) — a JSON file missing the field
  deserializes it to `null` silently. Not a bug here: `MainViewModel` reads it
  via `string.IsNullOrWhiteSpace(savedSettings.OutputName)` which treats null as
  falsy and falls back to the default. Confirmed intentional/safe; only worth
  re-flagging if a future diff reads `OutputName` without a null-safe check.
- `SettingsStore.Load()` narrows its catch to `IOException or
  UnauthorizedAccessException or JsonException`; `Save()` only catches
  `IOException or UnauthorizedAccessException` (no `JsonException` — Serialize
  of this simple record can't throw one in practice). Asymmetry is fine, not a
  finding.
- No test project exists for `CropVCam.App` (WPF, no `test/` dir at all) — don't
  expect/demand unit tests for this project the way `testing-conventions` would
  for a TS package; note missing coverage only as an optional Suggestion.
