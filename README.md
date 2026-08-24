# crop-vcam

物理カメラの映像を中央基準でクロップし、Windows仮想カメラとして出力するデスクトップアプリ。
管理者権限を一切要求しない（企業管理PCでの利用を想定）。詳しい背景・技術方針は
[plans/feat-crop-vcam-app.md](plans/feat-crop-vcam-app.md) を参照。バージョンごとの変更点は
[CHANGELOG.md](CHANGELOG.md) を参照。

## 構成

```text
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

物理カメラを選択すると（起動時のデフォルト選択を含む）、「開始」を押す前からプレビューが
表示される。以後アプリを終了するまでその物理カメラを排他的に保持するため、他アプリから
同じカメラを開くことはできなくなる。

「開始」を押すと初回のみ仮想カメラフィルタを `HKEY_CURRENT_USER` に登録する
（管理者権限は不要）。以後、Zoom等のカメラ選択リストに出力名（既定:
`Cropped Virtual Camera`）が表示されるはず。「停止」で映像配信を止める
（登録自体は解除されない、プレビューは継続する）。アプリ自体を正常終了すると、
レジストリ登録の解除を試みる（ベストエフォートのため、失敗しても終了処理自体は継続する）。

選択カメラ・倍率・出力カメラ名は、ウィンドウを閉じる際に `%LOCALAPPDATA%\CropVCam\settings.json`
へ保存され、次回起動時に復元される（カメラは名前で照合するため、未接続等で見つからない場合は
先頭のカメラにフォールバックする）。

配信中（「開始」を押した状態）に×ボタンでウィンドウを閉じた場合は、配信を継続したまま
タスクトレイへ最小化し、アプリ自体は終了しない（誤って閉じても会議中の映像が止まらないように
するため）。タスクトレイアイコンをダブルクリックまたは「開く」で復元、「終了」で完全に
終了する（この場合のみレジストリ登録解除まで実行される）。配信していない状態での×ボタンは
従来通り即座にアプリを終了する。

## 配布（リリースzip）

エンドユーザー向けの使い方ガイドは [README.txt](README.txt)（サポート窓口がPC初心者に説明する想定の
平易な文面）。配布用zipを作成する際は、ビルド成果物一式（`CropVCam.App.exe` とその出力ディレクトリ）と
一緒に `README.txt` を同梱すること。

フィルタ（Release構成でビルド済みであること）とアプリを、self-contained版（.NETランタイム同梱、対象PCに
.NET未インストールでも動作）と framework-dependent版（[.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
が対象PCに必要、配布サイズが小さい）の2種類でpublishする:

```bash
dotnet publish src/CropVCam.App/CropVCam.App.csproj -c Release -r win-x64 --self-contained true -o publish/self-contained
dotnet publish src/CropVCam.App/CropVCam.App.csproj -c Release -r win-x64 --self-contained false -o publish/framework-dependent
```

`dotnet publish` はcsprojの `None Include` 経由で `CropVCam.Filter/build/CropVCamFilter.dll`（Debug構成の
既定出力先）をコピーするため、Release構成のフィルタを配布する場合は publish 後に
`CropVCam.Filter/build-release/CropVCamFilter.dll` で上書きすること。

## 既知の制約 / 未検証事項

- 仮想カメラの出力解像度は、物理カメラの入力解像度にそのまま追従する（倍率による
  センタークロップ後、元の解像度へ戻すデジタルズーム）。ただし共有メモリ領域は
  4K UHD (3840x2160) を上限に固定サイズで確保しているため（`SharedFrameProtocol`
  の `MaxWidth`/`MaxHeight`）、これを超える解像度の物理カメラは、アスペクト比を
  保ったまま上限内へ縮小してから出力する。
- ネイティブフィルタは DirectShow のピン接続時に一度だけ出力解像度をネゴシエートし、
  以後その接続の生存期間中は変更しない。CropVCam.App が一度も起動していない状態で
  ネゴシエーションが行われた場合はフォールバック解像度（1280x720）が使われ、その後
  実際のカメラ解像度と異なっていても再ネゴシエーションはしない（通常運用ではプレビューが
  カメラ選択と同時に始まるため、この状況は稀）。
- 出力ピクセルフォーマットは RGB24 固定。
- 物理カメラのキャプチャは「開始」ボタンではなくカメラ選択と同時に始まり、アプリ終了まで
  継続する（プレビュー表示のため）。これにより、アプリ起動中は選択中の物理カメラを他アプリ
  から開けなくなる。
- 入力解像度追従・開始前プレビューは実装レベルでの確認まで（Zoom等の実機での動作確認は未実施）。
- フィルタは `IKsPropertySet`（ピンカテゴリ = `PIN_CATEGORY_CAPTURE`）と
  `IAMStreamConfig`（フォーマット列挙・ネゴシエーション）を実装している。
  どちらか一方でも欠けると、デバイス一覧には表示されるのに実際に開こうとすると
  失敗する（ffmpegの `dshow` 入力で実際にこの症状を再現・修正済み）。
- アプリは多重起動不可（2つ目の起動は既存インスタンスをフォアグラウンドに
  activate して自身は終了する）。共有メモリ名の衝突はこの制約により発生しない。
