# 終了時のレジストリ削除をチェックボックスで選択可能にする

Issue: #22

## 背景・要件

現状 `App.OnExit` は（プライマリインスタンスなら）無条件に
`MainViewModel.UnregisterFilter()` → `FilterRegistrar.TryUnregister` を呼び、
`HKEY_CURRENT_USER` 配下のフィルタ登録を毎回削除している。

レジストリを削除するとアプリの痕跡は残らないが、次回起動時に再登録が必要になるため
「先にcrop-vcamを起動してからZoom等を開く」という起動順に縛られる。レジストリを残せば
この制約はなくなるが、アプリの痕跡（登録エントリ）が残り続ける。

この削除有無をユーザーがチェックボックスで選べるようにする。

## 実装方針

- `AppSettings` に `bool UnregisterOnExit = true` を追加する（デフォルト値付きの
  positional parameter）。System.Text.Json はレコードの構造体コンストラクタ経由の
  デシリアライズ時、JSON側にプロパティが無ければパラメータの既定値を使うため、
  既存の `settings.json`（このフィールドを持たない）を読んでも `true`
  （＝現行動作＝削除する）にフォールバックする。
- `MainViewModel` に `[ObservableProperty] private bool unregisterOnExit = true;` を追加し、
  コンストラクタで `savedSettings.UnregisterOnExit` を復元、`SaveSettings()` で保存する
  （`Magnification`/`OutputName` と同じ扱い）。
- `MainViewModel.UnregisterFilter()`（`App.OnExit` から呼ばれる静的メソッド）の中で
  `SettingsStore.Load()?.UnregisterOnExit ?? true` を見て、`true` の場合のみ
  `FilterRegistrar.TryUnregister` を呼ぶ。`MainWindow.OnClosed` が `SaveSettings()` を
  `Closed` イベントで呼んだ後に `App.OnExit` が走るため、ディスクの設定は常に終了時点の
  値になっている（インスタンスをApp側に持たせる必要がない）。
- `MainWindow.xaml` にチェックボックス（ラベル例:
  「終了時にレジストリ登録を解除する」）を追加し、`IsChecked="{Binding UnregisterOnExit}"`
  でバインドする。ストリーミング中かどうかに関わらず変更可能（終了時にしか参照されない
  設定のため、`CanEditSettings` で無効化する必要はない）。ツールチップで
  「チェックを外すとレジストリに登録が残るが、次回以降Zoom等を先に開いても認識される」旨を
  補足する。
- README.md の「アプリ自体を正常終了すると、レジストリ登録の解除を試みる」の記述を、
  チェックボックスで選択可能である旨に更新する。
- README.txt（エンドユーザー向け）にも該当箇所があれば同様に補足する。

## 対象ファイル

- `src/CropVCam.App/Settings/AppSettings.cs` — `UnregisterOnExit` プロパティ追加
- `src/CropVCam.App/MainViewModel.cs` — プロパティ追加・読込/保存・`UnregisterFilter()` の条件分岐
- `src/CropVCam.App/MainWindow.xaml` — チェックボックス追加
- `README.md` / `README.txt` — 記述更新
