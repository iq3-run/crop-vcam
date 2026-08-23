# 設定の保存（選択カメラ・倍率・出力カメラ名）

- Issue: https://github.com/iq3-run/crop-vcam/issues/15

## 背景

現状、選択したカメラ・倍率・出力カメラ名はアプリ終了とともに失われ、起動のたびに
デフォルト値（先頭のカメラ・倍率2.5倍・`Cropped Virtual Camera`）から設定し直す
必要がある。よく使う組み合わせを次回起動時に復元できるようにする。

## 設計判断

### 保存先・タイミング

- `%LOCALAPPDATA%\CropVCam\settings.json` にJSON（`System.Text.Json`、追加パッケージ不要）で保存する。
  ローミングさせる必要はなく、PCごとに接続されるカメラ構成も異なりうるため
  `LocalApplicationData` を使う（`RoamingApplicationData` は使わない）。
- 保存タイミングは**ウィンドウを閉じるとき**（`MainWindow.Closed`）の一度のみ。
  `Magnification`/`OutputName` は `UpdateSourceTrigger=PropertyChanged` でスライダー操作・
  1文字入力ごとに変化するため、変更のたびにディスクへ書くのは過剰。設定変更中に
  ストリーミングはできない（`CanEditSettings` は `!IsRunning`）ため、終了時点の値を
  一度だけ保存すれば実用上十分。
- 読み込みはアプリ起動時（`MainViewModel` コンストラクタ）に一度。

### カメラの同定方法

`CameraDevice.Index` はDirectShowの列挙順に依存し、USBの抜き差しや接続順序が変わると
ズレる可能性があるため、永続化のキーには使わない。代わりに `Name`（FriendlyName）で
保存し、起動時に現在列挙されたカメラ一覧と名前が一致するものを選択する。
一致するカメラが見つからない場合（未接続・名称変更等）は、既存動作通り
先頭のカメラ (`FirstOrDefault()`) にフォールバックする。同名カメラが複数接続されている
場合は列挙順で先に見つかったものを選ぶ（既知の制約として許容）。

### 不正値のフォールバック

- `Magnification`: 読み込み時に `MinMagnification`〜`MaxMagnification` へ `Clamp`
  （設定ファイルの手動編集や将来の範囲変更に対する防御。`OnFrameCaptured` で
  既存の倍率クランプと同じ考え方）。
- `OutputName`: 空/空白のみの場合はデフォルト値 (`Cropped Virtual Camera`) を使う。
- 設定ファイルが存在しない／壊れている／読み書き権限がない場合は、例外を握りつぶして
  デフォルト値で起動を継続する（設定の永続化はベストエフォートであり、失敗が
  アプリ起動やレジストリ登録解除等の主要フローを妨げてはならない）。

## 変更対象

- `src/CropVCam.App/Settings/AppSettings.cs`（新規）
  - 永続化する値を持つ `record`（`CameraName`, `Magnification`, `OutputName`）。
- `src/CropVCam.App/Settings/SettingsStore.cs`（新規）
  - `Load()`: ファイルが無い/壊れている場合は `null` を返す。
  - `Save(AppSettings)`: ディレクトリ作成含め書き込み、失敗は握りつぶす（ベストエフォート）。
- `src/CropVCam.App/MainViewModel.cs`
  - コンストラクタでカメラ列挙後に `SettingsStore.Load()` を呼び、
    `Magnification`/`OutputName`/`SelectedCamera` の初期値へ反映。
  - `SaveSettings()` を追加（現在の `SelectedCamera`/`Magnification`/`OutputName` を
    `SettingsStore.Save` へ渡す）。
- `src/CropVCam.App/MainWindow.xaml.cs`
  - `Closed` ハンドラで `_viewModel.Dispose()` の前に `_viewModel.SaveSettings()` を呼ぶ。

## 対象外

- 複数プロファイルの保存・切り替えは対象外（単一の「最後に使った設定」のみ）。
- 設定ファイルの手動編集を正式にサポートしない（壊れていればデフォルトへフォールバックするのみ）。
- ネイティブ側（`CropVCam.Filter`）は変更なし。
