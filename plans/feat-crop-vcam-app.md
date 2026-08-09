# カメラ映像クロップ＆バーチャルカメラ出力アプリ

Issue: #1

## 背景・要件

物理カメラの映像を中央基準でクロップし、Windows仮想カメラとして出力するデスクトップアプリ。
利用シーン: 企業管理PC上でのZoom配信。**管理者への昇格が一切できない環境**での利用が前提のため、
インストール時を含め一度も管理者権限（UAC昇格）を要求してはならない。Windows 10 / 11 両対応。

## 技術調査で判明した制約（経緯）

- Windows Media Foundation の新しい仮想カメラ（`MFCreateVirtualCamera` / Frame Server方式）は
  Windows 11 (Build 22000) 以降専用、かつ Media Source の COM 登録が `HKEY_LOCAL_MACHINE` にしか
  行えず管理者権限が必須（Windows Frame Server / Frame Server Monitor サービスが別ユーザー権限で
  DLLをロードするため）。サンプル作者本人が「HKCUには登録できない」と明言しており回避不可 → **不採用**。
- クラシックな DirectShow ソースフィルタは、COMのCLSID登録を `HKEY_CURRENT_USER\Software\Classes`
  配下に行うことができ、非昇格プロセスからは `HKEY_CLASSES_ROOT` にマージされて見える
  （Windows Vista 以降のCOM仕様）。同一ユーザーセッションで動くZoom等の非昇格プロセスから
  認識可能なはず → **採用**。ただし実機・実Zoomでの動作確認は必須（ユーザー側で実施）。
- OBS Virtual Camera / Unity Capture 等の既存ツールは便利だが、対象PCに未導入かつIT承認が前提となり
  依存を増やすため不採用（ユーザー確認済み: 何も導入されていない）。

## アーキテクチャ

2つの実行主体に分かれる点が最大のポイント：DirectShowフィルタは **消費側アプリ（Zoom等）のプロセス内に
in-proc ロードされる**。したがって本体アプリ（別プロセス）とはプロセス間通信（共有メモリ）で映像を橋渡しする。

```
[物理カメラ]
     │ Media Foundation / OpenCvSharp (MSMF backend)
     ▼
[CropVCam.App (WPF, .NET, 別プロセス)]
  - カメラ列挙・キャプチャ
  - 中央クロップ＋等倍拡大 (倍率指定)
  - プレビュー表示
  - 共有メモリ (Memory-Mapped File) へフレーム書き込み + 名前付きイベントで通知
     │ Local\CropVCam.<出力名ハッシュ> という命名の共有メモリ/イベント
     ▼
[CropVCam.Filter.dll (C++ DirectShow Source Filter)]
  - Zoom等のプロセスに in-proc ロードされる
  - 共有メモリを読み、フレームを出力ピンから配信
  - CLSID登録は HKEY_CURRENT_USER のみ（管理者権限不要）
     ▼
[Zoom / Teams / ブラウザ等がカメラとして選択]
```

### なぜ共有メモリか

DirectShowフィルタは `CoCreateInstance` した側（＝Zoomのプロセス）の中で動く。GUIアプリのプロセスとは
別なので、フレームを渡すには何らかのIPCが要る。共有メモリ＋名前付きイベントは低遅延・実装コストの面で
実績のある方式（OBS Virtual Camera や Unity Capture も同様の方式）。名前付きオブジェクトは `Local\` 名前空間
（同一セッション内、管理者権限不要）を使う。

### 倍率とクロップの仕様

- 倍率 N のとき、元フレーム (W×H) の中央から `W/N × H/N` の領域を切り出し、W×H にリサイズして出力する
  （＝一般的なデジタルズームの解釈）。中心座標は動かさない。
- UIのスライダー範囲・刻みはモックアップに準拠（例: 1.0〜4.0、初期値2.5）。

### 出力映像フォーマット

- 実装の単純さと後方互換性を優先し、初期実装は **RGB24 (BI_RGB, top-down)** 固定。
  OpenCvSharpのBGR Matと画素順が一致するため変換コストが低い。
  Zoom側で認識されない場合は NV12 / YUY2 への切り替えを次イテレーションで検討（Items to Confirm に記載）。
- 解像度はキャプチャデバイスの選択解像度に追従。フレームレートは既定 30fps、共有メモリ更新が
  間に合わない場合は直前フレームを再送してストリームを継続させる（コンシューマ側のタイムアウト対策）。

## コンポーネント構成

```
crop-vcam/
  CropVCam.sln
  src/
    CropVCam.App/            WPF アプリ (.NET 8, C#)
      Camera/CameraEnumerator.cs
      Camera/CameraCapture.cs
      Processing/CenterCropScaler.cs
      VirtualCamera/SharedFrameWriter.cs
      VirtualCamera/FilterRegistrar.cs
      MainWindow.xaml(.cs), MainViewModel.cs
    CropVCam.Filter/         DirectShow ソースフィルタ (C++, vcpkg: directshowbaseclasses)
      CropVCamFilter.h/.cpp   CSource派生
      CropVCamStream.h/.cpp   CSourceStream派生（出力ピン、FillBuffer）
      SharedFrameReader.h/.cpp
      Registration.cpp        DllRegisterServer/DllUnregisterServer（HKCUのみ）
      dllmain.cpp / CropVCamFilter.def
      CMakeLists.txt, vcpkg.json
    CropVCam.Shared/         フレームヘッダ等、両言語で仕様を共有するドキュメント/定数
  plans/feat-crop-vcam-app.md
```

## 実装フェーズ

1. WPFアプリ: カメラ列挙・キャプチャ・クロップ処理・プレビューUI・開始/停止状態管理・出力名ロック
2. 共有メモリ書き込み側（C#）
3. ネイティブDirectShowフィルタ（C++、vcpkg directshowbaseclassesベース）＋HKCU限定登録
4. 開始ボタン押下時にフィルタDLLをその場でロードしHKCU登録 → キャプチャ/ブリッジ開始。停止で配信停止
   （登録自体は初回以降維持し、毎回の登録/解除は行わない）
5. ビルド検証（`dotnet build` / cmake+vcpkg）、README整備

## Items to Confirm（レビュー観点）

- RGB24固定の映像自体をZoomが実際に受け付けるかは未検証。ただし「デバイス一覧には出るが実際には開けない」
  問題自体は原因判明・修正済み（下記の追記を参照）。認識されない場合はNV12/YUY2対応の追加実装が
  必要になる可能性がある。
- 共有メモリの命名・多重起動時の衝突回避（出力名を名前空間に含めるが、同名で複数インスタンス起動時の挙動は
  未定義とする）。
- フィルタの登録はStart時に自動で行うが、アンインストール手段（HKCUからの削除）をどう提供するかは
  今回のスコープでは最小限（アプリ内に「登録解除」導線は設けず、必要ならレジストリ手動削除）。

## 追記: 「一覧には出るが開けない」問題の原因と修正

実機検証で、レジストリ登録は正しく行われ、カメラ一覧にも表示されるが、実際に選択すると
「カメラを起動できません」というエラーになる不具合が見つかった。

`ffmpeg -f dshow` で同じ症状を再現したところ、"Could not find output pin from video capture
device." というエラーであることが判明。ffmpegのdshow実装のソースを確認した結果、キャプチャ用の
出力ピンとして認識されるには以下の両方が必要と判明した（`CSourceStream`はどちらもデフォルトでは
実装しない）:

- `IKsPropertySet`（`AMPROPSETID_Pin` / `AMPROPERTY_PIN_CATEGORY` に `PIN_CATEGORY_CAPTURE` を返す）
- `IAMStreamConfig`（フォーマットの列挙・取得に対応する。今回は固定1フォーマットのみ対応）

両方を`CCropVCamStream`に実装したところ、`ffmpeg -f dshow -i video="Cropped Virtual Camera"`
での映像取得に成功した。Zoomでの最終確認はユーザー側で継続実施中。
