---
name: finding-unregister-on-exit-issue22
description: Issue #22 (UnregisterOnExit checkbox) — verified WPF shutdown ordering guarantee and .NET 8 record-default JSON deserialization behavior this feature relies on
metadata:
  type: project
---

`AppSettings.UnregisterOnExit` (positional record parameter, default `true`) gates
whether `MainViewModel.UnregisterFilter()` (static, called from `App.OnExit`) calls
`FilterRegistrar.TryUnregister`. Reviewed 2026-08-27 against a feature branch, build
confirmed clean (`dotnet build src/CropVCam.App/CropVCam.App.csproj
-p:SkipFilterDllCopy=true`, 0 warnings/errors).

**Verified: `App.OnExit` always runs after `MainWindow.Closed` (which calls
`SaveSettings()`), so `UnregisterFilter()`'s independent `SettingsStore.Load()` always
sees the fresh value, not a stale one.** No explicit `ShutdownMode` is set in
`App.xaml`, so the default `OnLastWindowClose` applies: `MainWindow` is set as
`Application.MainWindow` in `OnStartup`, and closing it triggers `Application.Shutdown()`
→ `OnExit` only after `Closing` → `Closed` (which runs `SaveSettings()` then
`Dispose()`) have both completed. This ordering holds for every close path (tray "終了",
plain × when not streaming) since they all funnel through the same `Window.Close()` →
`Closing`/`Closed` sequence. Re-verify only if `ShutdownMode` is ever set explicitly or
`OnExit`/`OnClosed` timing changes.

**Verified: .NET 8 `System.Text.Json` record-constructor deserialization does honor a
positional parameter's default value when the JSON property is absent** (confirmed via
a standalone `net8.0` repro: `{"CameraName":"Foo","Magnification":2.5,"OutputName":"Bar"}`
deserialized to `UnregisterOnExit = True`). This is the mechanism the backward-compat
comment on `AppSettings` relies on for pre-existing `settings.json` files that predate
this field — it works, not just as documented intent. Worth re-confirming only if the
project ever targets a different TFM or switches to `JsonConstructorAttribute`/source-gen
contexts, which can change this behavior.

**Second-instance exit path is unaffected and correctly guarded by pre-existing
`_isPrimaryInstance`** — a second instance never reaches `UnregisterFilter()` at all, so
this feature adds no new risk there.

Only true finding this round was in the README-diff itself: none of substance — prose
accurately describes the checkbox default, persistence, and both close paths
(tray-終了 vs plain ×). CheckBox is deliberately left un-gated by `CanEditSettings`
(unlike `SelectedCamera`/`OutputName`) since it only affects exit-time behavior, not
capture state — correct, not an oversight.
