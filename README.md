# crop-vcam

物理カメラの映像を中央基準でクロップし、Windows仮想カメラとして出力するデスクトップアプリ。
管理者権限を一切要求しない（企業管理PCでの利用を想定）。詳しい背景・技術方針は
[plans/feat-crop-vcam-app.md](plans/feat-crop-vcam-app.md) を参照。

## 構成

```
src/
  CropVCam.App/     WPFアプリ本体（.NET 8, C#）。カメラキャプチャ・クロップ処理・プレビューUI
  CropVCam.Filter/  DirectShowソースフィルタ（C++）。Zoom等のプロセス内にロードされ、
                     CropVCam.App が共有メモリへ書き込んだフレームをカメラ映像として配信する
```

CropVCam.App と CropVCam.Filter は別プロセスで動作し、共有メモリ（Memory-Mapped File）経由で
フレームをやり取りする（理由は plan ファイル参照）。

## ビルド

### 1. ネイティブフィルタ (CropVCam.Filter)

Visual Studio（C++ デスクトップ開発ワークロード、CMake コンポーネント）が必要。

```bash
cd src/CropVCam.Filter
cmake -S . -B build -G Ninja
cmake --build build
```

Visual Studio Developer Command Prompt / PowerShell（`vcvars64.bat` 実行後）で実行すること。

### 2. WPFアプリ (CropVCam.App)

.NET 8 SDK が必要。

```bash
dotnet build src/CropVCam.App/CropVCam.App.csproj
```

`CropVCam.Filter/build/CropVCamFilter.dll` が存在すれば、ビルド時に自動で
`CropVCam.App` の出力ディレクトリへコピーされる（`CropVCam.App.csproj` 参照）。
そのため **フィルタを先にビルドしてから** アプリをビルド/実行すること。

## 実行

```bash
dotnet run --project src/CropVCam.App/CropVCam.App.csproj
```

「開始」を押すと初回のみ仮想カメラフィルタを `HKEY_CURRENT_USER` に登録する
（管理者権限は不要）。以後、Zoom等のカメラ選択リストに出力名（既定:
`Cropped Virtual Camera`）が表示されるはず。「停止」で映像配信を止める
（登録自体は解除されない）。

## 既知の制約 / 未検証事項

- 仮想カメラの出力解像度は 1280x720 固定（`SharedFrameProtocol` で定義）。
  クロップは物理カメラの解像度・アスペクト比に対して中央基準で行われるが、
  出力はこの固定キャンバスへリサイズされる。
- 出力ピクセルフォーマットは RGB24 固定。Zoom側で実際にカメラとして認識されるかは
  未検証（アーキテクチャ上は妥当なはずだが実機・実Zoomでの確認が必要）。
- アプリは多重起動不可（2つ目の起動は既存インスタンスをフォアグラウンドに
  activate して自身は終了する）。共有メモリ名の衝突はこの制約により発生しない。
