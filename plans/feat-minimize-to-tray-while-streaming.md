# 配信中はタスクトレイに最小化して終了しないように

- Issue: https://github.com/iq3-run/crop-vcam/issues/16

## 背景

仮想カメラ配信中（「開始」を押した状態）にメインウィンドウの×ボタンで閉じると、
現状は即座にアプリ全体が終了する（`App.OnExit` でフィルタのレジストリ登録解除まで
実行される）。Zoom等の会議中に誤ってウィンドウを閉じてしまうと、配信していた
映像がその場で止まってしまう。

## 要件（ユーザーとの合意事項）

- 配信中（`IsRunning == true`）に×ボタンで閉じた場合、配信を継続したままタスクトレイへ
  最小化し、アプリ自体は終了しない。
- 完全に終了するには、タスクトレイアイコンの右クリックメニューから「終了」を選ぶ。
- 配信していない時（`IsRunning == false`）は従来通り×ボタンで即終了。
- タスクトレイアイコンの画像はシステム既定アイコン（`SystemIcons.Application`）を使う
  （本リポジトリにアイコンアセットが存在しないため。専用アイコンの用意は本issueの
  スコープ外）。

## 設計判断

### タスクトレイアイコンの実装方式

WPF自体にはタスクトレイアイコンのAPIが無いため、`System.Windows.Forms.NotifyIcon` を
利用する（`CropVCam.App.csproj` に `UseWindowsForms=true` を追加。追加のNuGetパッケージは
不要）。`UseWPF` と `UseWindowsForms` を同時に有効化すると、両フレームワークで同名の型
（`Application`/`MessageBox`等）が存在するため暗黙的global usingの衝突が起こりうるので、
ビルドで確認する。衝突する場合はWinForms側の型を各ファイル内で完全修飾する。

新規クラス `src/CropVCam.App/TrayIcon.cs`（`NotifyIcon` のラッパー、`IDisposable`）を
`SingleInstance` と同様のコールバック公開スタイルで実装する。「開く」「終了」の
コンテキストメニューと、アイコンのダブルクリックでの復元を持つ。

### ウィンドウのClosing/Closedの扱い

`MainWindow` に `Closing` イベントハンドラを追加する。

- `_viewModel.IsRunning` が `true` かつタスクトレイ経由の明示的な終了要求
  （後述の `_exitRequested` フラグ）でない場合、`e.Cancel = true` として
  `Window.Hide()` し、タスクトレイアイコンを表示する。
- それ以外（配信していない、またはタスクトレイの「終了」経由）は何もせず、
  既存の `Closed` ハンドラ（`SaveSettings()` → `Dispose()`）へそのまま進む。

タスクトレイの「終了」メニューは `_exitRequested = true` をセットしてから
`Window.Close()` を呼ぶ（`Closing` ハンドラでフラグを見てキャンセルしないようにする）。
これにより既存の `App.OnExit`（フィルタのレジストリ登録解除）を含む終了フローは
変更しない。

タスクトレイからの復元（「開く」メニュー or ダブルクリック）は、タスクトレイアイコンを
非表示にしてから `Window.Show()` / `WindowState = Normal` / `Activate()` する。

### 対象外にする挙動

- 最小化ボタン（_）を押した場合の挙動は変更しない（既存通りタスクバーに最小化されるのみ、
  タスクトレイには入らない）。今回の対象は×ボタン（Closing）のみ。
- 配信していない状態でのタスクトレイ常駐は行わない（要件通り「配信中のみ」）。

## 変更対象

- `src/CropVCam.App/CropVCam.App.csproj`: `UseWindowsForms` を追加。
- `src/CropVCam.App/TrayIcon.cs`（新規）: `NotifyIcon` ラッパー。
- `src/CropVCam.App/MainWindow.xaml.cs`: `Closing` ハンドラ追加、タスクトレイの
  イベント（復元・終了）配線。
- `README.md` / `README.txt`: 配信中に×ボタンで閉じた場合の挙動を追記。
