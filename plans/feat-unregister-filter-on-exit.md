# アプリ終了時にレジストリ登録を解除する

Issue: #4

## 背景・要件

`FilterRegistrar.EnsureRegistered` は初回の「開始」ボタン押下時に `DllRegisterServer` を呼び出し、
`HKEY_CURRENT_USER\Software\Classes\CLSID\...` 配下にDirectShowフィルタを登録する。アプリを終了しても
この登録は解除されず、レジストリに残り続ける。

アプリ終了時にレジストリをきれいにする（登録したエントリを解除する）。

## 実装方針

- `FilterRegistrar` に `Unregister(string filterDllPath)` を追加する。
  `EnsureRegistered`/`RunDllRegisterServer` と同様の手順（`LoadLibrary` → エクスポート関数呼び出し →
  `FreeLibrary`）で、フィルタDLLに既存実装済みの `DllUnregisterServer`（`Registration.cpp`）を呼び出す。
- `MainViewModel` に、終了時に呼び出す `UnregisterFilter()` のようなメソッドを追加する
  （`FilterDllFileName` 定数と `filterDllPath` の組み立てが `StartStreaming` に既にあるため、それと同じ
  組み立てロジックを使う）。
- `App.xaml.cs` の `OnExit` から、MainWindow経由でViewModelの解除処理をベストエフォート呼び出しする。
  - ファイル未検出・DLLロード失敗・`DllUnregisterServer` 失敗などいずれの例外もキャッチし、アプリの
    終了処理自体はブロック・失敗させない（ログ的な扱いはせず、単に握りつぶす）。
  - `DllUnregisterServer` 自体は「未登録」を成功として扱う実装済みなので、一度も「開始」を押していない
    session でも安全に呼べる。
- 「停止」ボタン（`StopStreaming`）では解除しない。配信停止とレジストリ解除は別ライフサイクルのまま
  （終了時のみ解除）。
- README.md の「開始」を押すと初回のみ登録される旨の記述（既知の制約の「登録自体は解除されない」の部分）
  を更新し、アプリ終了時に解除される旨を反映する。

## 対象ファイル

- `src/CropVCam.App/VirtualCamera/FilterRegistrar.cs` — `Unregister` メソッド追加
- `src/CropVCam.App/MainViewModel.cs` — 終了時解除用メソッド追加
- `src/CropVCam.App/App.xaml.cs` — `OnExit` から呼び出し
- `README.md` — 記述更新
