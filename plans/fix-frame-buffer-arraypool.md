# フレームバッファの ArrayPool 化による GC 負荷軽減

- Issue: https://github.com/iq3-run/crop-vcam/issues/13

## 背景

`MainViewModel.ToBgr24Bytes` は、キャプチャしたフレームごとに新しい `byte[]` を確保している。4K (3840x2160) 出力時は1フレームあたり約23.7MiB、30fpsで約712MiB/秒のラージオブジェクトヒープ確保が発生し、GCによる映像のもたつき・フレームドロップを招く可能性がある（#9 のレビューで指摘、意図的に見送った項目）。

## 設計判断

### 専用の `ArrayPool<byte>` インスタンスを使う（`ArrayPool<byte>.Shared` は使わない）

`ArrayPool<byte>.Shared` の既定バケット上限は要素数 1,048,576（≒1MiB）で、これを超えるサイズを `Rent` すると **プールされず毎回 `new byte[]` される**（`Return` も no-op）。本アプリのフレームは 1080p でも約6.2MiB、4Kでは約23.7MiBと、いずれもこの上限を超えるため、`ArrayPool<byte>.Shared` をそのまま使っても実質的に何もプールされず、issue の狙い（GC負荷軽減）を達成できない。

よって `ArrayPool<byte>.Create(maxArrayLength: SharedFrameProtocol.MaxPayloadBytes, maxArraysPerBucket: 2)` で専用プールを `MainViewModel` に static に持つ。`maxArraysPerBucket` は実際に同時生存しうるバッファ数（後述のフロー上、常に高々2個）に合わせて最小限にする。

### バッファのライフタイム管理

`OnFrameCaptured` は `CameraCapture` 専用の同期バックグラウンドスレッドから呼ばれる（1フレームずつ順次処理、並行呼び出しはない）。

1. `FrameBufferPool.Rent(byteCount)` でレンタルし、`Marshal.Copy` でピクセルデータを書き込む。
2. `SharedFrameWriter.WriteFrame` は呼び出し内で `MemoryMappedViewAccessor` へ同期的にコピーして返るため、戻ってきた時点でこのバッファへの依存はない。
3. `UpdatePreview` は `Application.Current.Dispatcher.BeginInvoke` で非同期に `BitmapSource.Create`（内部でピクセルデータを別バッファへコピーする）を呼ぶため、そのコールバックが完了するまでバッファの生存を延長する必要がある。
   - 既存の `_previewUpdatePending`（前フレームの描画未完了なら新フレームを破棄する間引き）と組み合わせ、`UpdatePreview` が「ディスパッチした（バッファの所有権を引き継いだ）か／間引いて即座に不要になったか」を `bool` で呼び出し元に返す。
   - ディスパッチした場合は、コールバックの `finally` で `BitmapSource.Create` 完了後に `FrameBufferPool.Return` する。
   - 間引いた場合は `OnFrameCaptured` 側で直後に `FrameBufferPool.Return` する。
4. `OnFrameCaptured` 全体を `try/finally` で囲み、`WriteFrame`/`UpdatePreview` が例外を投げてもバッファがプールに返却されないままリークしないようにする（例外は `CameraCapture.RaiseFrameCaptured` が捕捉し `FrameProcessingFailed` として通知、キャプチャループ自体は継続する既存動作を維持）。

この結果、同時に生存しうるレンタル済みバッファは「今まさに `OnFrameCaptured` が処理中の1個」＋「直前フレームの `UpdatePreview` ディスパッチがまだ完了していない場合の1個」の最大2個。

### `SharedFrameWriter.WriteFrame` 側の対応

レンタル配列は要求サイズちょうどではなく、それ以上の長さを持ちうる（プールの実装依存）。現状の検証・書き込みは配列長 (`bgr24Pixels.Length`) に依存しているため、そのままではプール配列の余剰分まで共有メモリへ書き込んでしまう。

- サイズ検証を「配列長が期待値と完全一致 (`!=`)」から「配列長が期待値以上 (`<`)」に変更。
- `_view.WriteArray` に渡す長さを `bgr24Pixels.Length` ではなく、計算済みの `payloadBytes`（width × height × 3）に変更。

共有メモリへの書き込み自体は既に同期処理でバッファを保持し続ける必要がないため、`SharedFrameWriter` 側でバッファを共有・保持する設計にはしない（issue の「検討事項」に対する結論）。

## 変更対象

- `src/CropVCam.App/MainViewModel.cs`
  - `ToBgr24Bytes` を撤廃し、`OnFrameCaptured` 内で `ArrayPool<byte>` によるレンタル/返却を行う。
  - `UpdatePreview` の戻り値を `bool`（バッファの所有権を引き継いだか）に変更。
- `src/CropVCam.App/VirtualCamera/SharedFrameWriter.cs`
  - `WriteFrame` のサイズ検証とコピー長をプール配列（実長 ≥ 要求長）に対応させる。

## 対象外

- ネイティブ側（`CropVCam.Filter`）は共有メモリのレイアウト・プロトコルに変更がないため対象外。
