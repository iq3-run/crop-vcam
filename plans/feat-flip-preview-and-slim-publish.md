# feat: プレビュー左右反転 (#27) とpublish出力の整理

## プレビュー反転
- `AppSettings.FlipPreview` を追加し、設定ファイルに保存・復元する。
- `MainWindow.xaml` にチェックボックスを追加し、`Image` に `ScaleTransform(ScaleX=-1)` をDataTriggerで適用する。
- 反転はビュー側のみ。`WriteFrame` に渡すフレームは変更しないので出力映像は反転されない。

## publish出力の整理
- csprojで `PublishSingleFile` + `IncludeNativeLibrariesForSelfExtract` + `EnableCompressionInSingleFile` を有効化し、ランタイム/依存DLLをexeへ同梱する。
- `SatelliteResourceLanguages=en` で言語別フォルダを出力しない。Release構成ではpdbも出力しない。
- `CropVCamFilter.dll` はレジストリに絶対パスが登録されるため、exeと同じ場所に残す。
