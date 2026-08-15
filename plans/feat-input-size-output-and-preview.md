# 出力解像度の入力追従 & 開始前プレビュー

- Issue: https://github.com/iq3-run/crop-vcam/issues/8
- User Prompt: `docs/20260812_追加仕様.txt` 参照
  ```
  ・入力のサイズで出力する
  ・開始前から、プレビューを見たい
  　起動後は選択しているカメラを他のアプリで開けないが、それは許容
  ```

## 要件

1. 仮想カメラの出力解像度を、固定キャンバス（現状 1280x720）ではなく **物理カメラの入力解像度そのまま** にする。
2. 「開始」ボタンを押す前からプレビューを見られるようにする。カメラ選択（起動時のデフォルト選択含む）と同時にプレビューが始まり、以後アプリ終了までその物理カメラを排他的に保持する（他アプリから開けなくなる点は許容）。

## 設計判断（ユーザー確認済み）

共有メモリ領域のサイズは C#/C++ 両側で一致させる必要がある固定値（過去のPR#2レビューで、この不整合により機能全体が壊れた実績あり — `.claude/agent-memory/code-reviewer/finding_shared_region_size_mismatch.md`）。よって領域を動的にリサイズするのではなく、**4K UHD (3840x2160) を上限**とする定数を両側に定義し、領域サイズはその上限から算出する。上限を超える解像度のカメラは、アスペクト比を保ったまま上限内へ縮小してから出力する。

## 変更詳細

### C# 側

- `SharedFrameProtocol.cs`
  - `OutputWidth`/`OutputHeight`（1280x720固定）を撤廃し、`MaxWidth`/`MaxHeight`（3840x2160）に置き換え。`SharedRegionBytes` はこの上限から算出。
  - フィルタ未接続時のフォールバック解像度用に `DefaultWidth`/`DefaultHeight`（1280x720）をドキュメント目的で残す（C#側では未使用、C++側の対応定数とのペア関係を明示するコメントのみ）。
- `SharedFrameWriter.cs`
  - `WriteFrame` のシグネチャを `WriteFrame(byte[] bgr24Pixels, int width, int height)` に変更。ヘッダーへ実際の width/height/stride を書き込む。
  - 検証を「固定サイズと完全一致」から「`MaxWidth`/`MaxHeight` 以内」へ変更。
- `MainViewModel.cs`
  - `CameraCapture` の生成・破棄を「開始/停止」から切り離し、プレビュー用ライフサイクルとして独立させる：
    - コンストラクタでの初期カメラ選択後、および `SelectedCamera` 変更時（`OnSelectedCameraChanged`）にプレビュー用キャプチャを再作成する。
    - `StartStreaming`（開始）は物理カメラのキャプチャには触れず、フィルタ登録 (`FilterRegistrar`) と `SharedFrameWriter` の生成のみを行う。
    - `StopStreaming`（停止）は `SharedFrameWriter` の破棄のみ行い、プレビュー用キャプチャは維持する。
    - `Dispose()`（アプリ終了時）でプレビュー用キャプチャも含めて完全に停止する。
  - `OnFrameCaptured` の `CenterCropScaler.CropAndScale` 呼び出しを、固定定数ではなく実際のフレームサイズ（`MaxWidth`/`MaxHeight` を超える場合はアスペクト比を保って縮小）に変更。

### C++ 側（`CropVCam.Filter`）

- `SharedFrameProtocol.h`
  - `kOutputWidth`/`kOutputHeight` を `kMaxWidth`/`kMaxHeight`（3840x2160）に置き換え、`kSharedRegionBytes` を再計算。
  - ピン未接続時のフォールバック用に `kDefaultWidth`/`kDefaultHeight`（1280x720）を追加。
- `SharedFrameReader.h`/`.cpp`
  - 新規 `TryPeekFrameSize(int* outWidth, int* outHeight)`：フォーマットネゴシエーション時（`GetMediaType`/`GetStreamCaps`/`DecideBufferSize`）に、共有メモリのヘッダーから現在のカメラ解像度を（フル待機なしで）覗き見る。書き込み側が起動していなければ false。
  - `WaitAndCopyFrame` は、期待する width/height（呼び出し側がネゴシエーション時に確定させた値）を引数に取り、一致しないフレームは破棄（＝直前のフレームを保持）するように変更。厳密な定数一致チェックから「期待値との一致」チェックへ。
- `CropVCamStream.h`/`.cpp`
  - ピン接続時に一度だけ解像度を確定させ（`TryPeekFrameSize` が成功するまでは `kDefaultWidth`/`kDefaultHeight` を返す）、以後はその接続の生存期間中は変更しない（DirectShow は動的再ネゴシエーションを前提としないため、既存設計を踏襲）。
  - `GetMediaType`/`GetStreamCaps`/`DecideBufferSize`/`FillBuffer` を、この確定済み解像度ベースに変更。
  - `latestFrame_` バッファは `kMaxWidth * kMaxHeight * 3` で確保しておき、実際に使うのは確定済み解像度分のみとする（再確保を避ける）。

### ドキュメント

- `README.md` の「既知の制約 / 未検証事項」を更新（固定1280x720の記述を撤廃し、入力解像度追従・4K上限・プレビューの排他制御について記載）。

## 既知の残存制約

- ネイティブフィルタ側は DirectShow のピン接続時に一度だけフォーマットをネゴシエートする。CropVCam.App が一度も起動していない状態でフォーマット照会が来た場合はフォールバック解像度（1280x720）が使われ、その後実際のカメラ解像度と異なっていても再ネゴシエーションはしない（既存アーキテクチャの制約を踏襲。通常運用ではプレビューがアプリ起動と同時に始まるため、この状況は稀）。
