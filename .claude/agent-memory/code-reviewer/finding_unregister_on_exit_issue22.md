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
`SaveSettings()`), so `UnregisterFilter()` always sees the checkbox's exact in-memory
value at exit time.** No explicit `ShutdownMode` is set in `App.xaml`, so the default
`OnLastWindowClose` applies: `MainWindow` is set as `Application.MainWindow` in
`OnStartup`, and closing it triggers `Application.Shutdown()` → `OnExit` only after
`Closing` → `Closed` (which runs `SaveSettings()` then `Dispose()`) have both completed.
This ordering holds for every close path (tray "終了", plain × when not streaming) since
they all funnel through the same `Window.Close()` → `Closing`/`Closed` sequence.
Re-verify only if `ShutdownMode` is ever set explicitly or `OnExit`/`OnClosed` timing
changes.

**CodeRabbit caught a real gap in the first version of this fix, since fixed:**
`UnregisterFilter()` originally re-read `SettingsStore.Load()?.UnregisterOnExit ?? true`
at exit time instead of using the in-memory checkbox value. `SettingsStore.Save()`
swallows `IOException`/`UnauthorizedAccessException`, so a failed write could leave disk
holding a stale or absent value that disagreed with what the checkbox actually showed —
e.g. the user unchecks it, the write fails, and the stale on-disk `true` (or the `??
true` fallback if the file never existed) still triggers unregistration. Fixed by having
`SaveSettings()` set a `private static bool s_unregisterOnExit` field alongside the disk
write, and `UnregisterFilter()` reads that field instead of touching disk at all — the
ordering guarantee above (`SaveSettings()` always runs before `UnregisterFilter()`) means
the field is always fresh, with no I/O failure mode in the loop.

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

**2026-08-27 follow-up (PR #23, addressing CodeRabbit's real finding above):**
Verified the `s_unregisterOnExit` static-field fix builds clean (0 warnings/errors) and
is actually correct — `MainWindow.OnClosed` (`_trayIcon.Dispose(); _viewModel.SaveSettings();
_viewModel.Dispose();`) still runs, synchronously on the UI thread, strictly before
`App.OnExit`, so the field is always set before `UnregisterFilter()` reads it; no
thread-safety concern since WPF is single-threaded STA and this app enforces single-instance.
First-run-before-any-save case is fine too: the field's `true` default matches both the
`AppSettings.UnregisterOnExit` record default and the pre-existing `?? true` fallback it
replaced, and `App.OnExit` only ever runs after `Application.Shutdown()`, which itself only
follows a real window close (which always calls `SaveSettings()` first) — so there's no
process-exit path that reaches `UnregisterFilter()` without having set the field first.
Second-instance path unaffected (still gated by `_isPrimaryInstance`, never reaches
`UnregisterFilter()`).

The `// Read by UnregisterFilter() below` comment on the new field is the same
caller-reference-as-invariant phrasing already blessed in [[finding-settings-persistence-issue15]]
("Called from X" stating an ordering constraint, not mere provenance) — don't re-flag this
shape of comment in this file.

**Noted only as an optional Suggestion, not a Warning:** a static field shared across
instances is a mild smell in an MVVM codebase; `App.xaml.cs` already holds a reference to
the running `MainWindow` (`Application.Current.MainWindow`), whose `DataContext` is the
live `MainViewModel`, so `OnExit` could in principle read `UnregisterOnExit` off that
instance directly instead of mirroring it into static state. Didn't raise this above
Suggestion because the app only ever has one `MainViewModel` instance (single-instance
enforced) and the static field is simpler than threading a `DataContext` cast through
`App.xaml.cs`. Revisit only if the app ever grows a second window/viewmodel or if a
future reviewer wants to remove the static field.
