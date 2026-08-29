---
name: finding-tray-minimize-issue16
description: Issue #16 (minimize-to-tray while streaming) — verified-safe NotifyIcon disposal idiom, confirmed-correct Closing/IsVisibleChanged/ActivateMainWindow interaction, and the one real finding (oversized MainWindow constructor)
metadata:
  type: project
---

`MainWindow.xaml.cs` now cancels `Closing` and hides to a `TrayIcon` (new file, thin `NotifyIcon` wrapper) while `MainViewModel.IsRunning`, only actually exiting via the tray's "終了" (which sets `_exitRequested` before calling `Close()`). Reviewed 2026-08-23 against `main`, build confirmed clean (`dotnet build -p:SkipFilterDllCopy=true`, 0 warnings/errors).

**NotifyIcon.Dispose() called from inside a handler chain that originated from that same NotifyIcon's own context-menu Click event is safe, not a bug.** `MainWindow`'s `Closed` handler calls `_trayIcon.Dispose()`, and the only path that reaches `Closed` while `IsRunning` is true is the tray's "終了" click → `Close()` → `Closing` (not cancelled because `_exitRequested`) → `Closed`. By the time `ToolStripItem.Click` fires, the native popup-menu tracking loop (`TrackPopupMenuEx`) has already unwound — Click delivery happens after the dropdown closes itself — so disposing the `NotifyIcon` synchronously inside that call chain does not reenter any nested Win32 menu loop. This is a standard, widely-used idiom in NotifyIcon-based tray apps.

**Why:** Worth recording because "is it safe to dispose X from inside X's own event handler" is exactly the kind of question a reviewer (or a bot) will re-raise every time this file is touched, without rechecking the actual Win32 delivery-order semantics.

**How to apply:** Don't flag `_trayIcon.Dispose()` in the `Closed` handler as a reentrancy risk. Do re-check if the dispose call ever moves to fire *during* menu tracking (e.g. inside the `Click` lambda itself, before `Close()` returns) rather than after — that would be a different, actually-risky shape.

**Closing/IsVisibleChanged/`_exitRequested`/`App.ActivateMainWindow` interaction is correct and race-free as of this review.** Traced every path: normal close while not streaming (unchanged, exits immediately), close-while-streaming (`Hide()` + tray shown), tray "開く"/double-click restore (`RestoreFromTray`: `Show()` → `IsVisibleChanged` fires → hides tray → explicit `WindowState = Normal` → `Activate()`), and second-instance relaunch (`App.ActivateMainWindow`, dispatched via `Dispatcher.BeginInvoke` from the listener thread: normalizes `WindowState` if `Minimized`, then `Show()` → same `IsVisibleChanged` hide-tray path → `Activate()`/`SetForegroundWindow`). `IsRunning` is only ever mutated on the UI thread (via the `StartStop` `RelayCommand`), so the `Closing` handler's read of it can't race a background thread. Taskbar-minimize (the `_` button) never touches `IsVisibleChanged` because WPF's `IsVisible` doesn't change on `WindowState.Minimized`, only on `Visibility` — so it's correctly unaffected, matching the plan's explicit "対象外" note.

**How to apply:** If this area changes again, re-verify the same four paths (close-while-streaming, tray restore, second-instance relaunch, taskbar-minimize) still don't regress into showing a stale tray icon or losing the streaming-continues guarantee — don't just diff-review the new lines.

**Resolved as of the 2026-08-27 (#22) review:** `MainWindow()` constructor now calls `WireTrayIcon()` and `WireWindowLifecycle()`, exactly the split recommended below. Confirmed by reading the current file — no further action needed, don't re-flag.

Original finding (kept for history): `MainWindow()` constructor wired five things inline (`RestoreRequested`, `ExitRequested`, `IsVisibleChanged`, `Closing`, `Closed`) and ran ~30 physical lines / ~21 statement lines, over CLAUDE.md's 20-line function cap.
