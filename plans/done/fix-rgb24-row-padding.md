# RGB24フレームの行パディング（4の倍数でないカメラ幅への対応）

Issue: #12

## 背景・要件

`CCropVCamStream::GetMediaType`/`DecideBufferSize`/`FillBuffer`
（`src/CropVCam.Filter/src/CropVCamStream.cpp`）は、DIB（`BI_RGB`）の行を
`width * 3` バイトのパック済みストライドとして扱っている。しかし `BI_RGB` の仕様上、
行は4バイト境界にパディングされている必要がある（`((width*3+3) & ~3)`）。物理カメラの
幅が4の倍数でない場合、フレームサイズが過小に計算され、行がずれた映像になる可能性がある。

一般的なUVC/Webカメラの解像度はすべて4の倍数の幅を持つため実害は低リスクだが、仕様上の
ギャップとして残っている（issue #9 のCodeRabbitレビュー指摘、意図的に見送った項目）。

## 実装方針

- 共有メモリ上のペイロード（`SharedFrameProtocol`、`SharedFrameReader`）は現状どおり
  パック済み（`stride == width*3`）のまま変更しない。C#/C++間のヘッダ・サイズ整合性に
  影響するため、ここは触らない。
- `CCropVCamStream` に行ストライド計算用のヘルパーを追加する：
  - `PackedRowBytes()` = `width_ * 3`（共有メモリ側のストライド、変更なし）
  - `PaddedRowBytes()` = `(PackedRowBytes() + 3) & ~3`（DIB仕様の4バイト境界ストライド）
  - `PackedFrameBytes()` = `PackedRowBytes() * height_`（旧`FrameBytes()`。共有メモリから
    読み取る際のサイズ・`latestFrame_`キャッシュのサイズ判定に使用）
  - `PaddedFrameBytes()` = `PaddedRowBytes() * height_`（DirectShowに公開する実際のフレーム
    サイズ。`biSizeImage`・アロケータの`cbBuffer`・`SetActualDataLength`・ビットレート計算
    (`GetStreamCaps`)に使用）
- `FillBuffer` で、`RefreshLatestFrame`（共有メモリからの読み取り、`latestFrame_`への
  パック済みコピー）は`PackedFrameBytes()`のまま。そのあと`latestFrame_`（パック済み、
  ストライド`PackedRowBytes()`）から出力サンプルバッファ（パディング済み、ストライド
  `PaddedRowBytes()`）へ行単位でコピーする`CopyRowsWithPadding`を追加し、各行のパディング
  部分は`0`で埋める。
- 4の倍数幅のカメラ（`PackedRowBytes() == PaddedRowBytes()`）では、行ごとのコピーが増える
  だけで結果は従来と同じデータになる（回帰なし）。

## 対象ファイル

- `src/CropVCam.Filter/src/CropVCamStream.h` — ヘルパーメソッドの宣言追加
- `src/CropVCam.Filter/src/CropVCamStream.cpp` — `FrameBytes()`を`PackedFrameBytes()`/
  `PaddedFrameBytes()`に分離し、行パディング処理を実装

## 手動確認

自動テストが存在しないプロジェクトのため（C++側にテストハーネスなし）、幅が4の倍数で
ない物理カメラ（実機がなければOBS仮想カメラ等で解像度を調整）またはデバッグビルドでの
ログ確認で、映像がずれずに表示されることを確認する。通常のUVCカメラ（640x480等）では
`PackedRowBytes() == PaddedRowBytes()`となり挙動は変化しないため、既存の動作確認手順
（README.md記載）で回帰がないことも確認する。
