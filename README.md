# sample-wpf-grpc-ogg-opus

WPF から録音した音声を Ogg Opus に変換し、MagicOnion (gRPC) でサーバーに送って保存するサンプル。サーバー・クライアント・共有ライブラリすべて .NET 10 / MagicOnion v7 で揃えている。

## 構成

- **Sample.Server** (net10.0) — 受信した音声を `recordings/recording.opus` に保存するだけ
- **Sample.Client.Streaming** (net10.0-windows / WPF) — ClientStreaming で逐次送信
- **Sample.Client.Unary** (net10.0-windows / WPF) — Unary で一括送信
- **Sample.Client.Stt** (net10.0-windows / WPF) — 保存済みファイルを取得して sherpa-onnx / Azure Speech で文字起こし
- **Sample.Shared** (net10.0) — DTO とインターフェース定義 (`IRecordingService`)、`VadGate`、`AudioConstants`

- **送信**: NAudio で録音 → (任意) WebRTC VAD で無音除去 → Concentus で Ogg Opus 化 → gRPC で送信
- **受信**: gRPC でサーバーから Ogg Opus をダウンロード → Concentus でデコード → NAudio で再生 (再生・一時停止・停止・シーク対応)

サーバーは Opus の中身を知らない。エンコード/デコードはすべてクライアント側。

## VAD (無音除去)

`WebRtcVadSharp` で 20 ms フレームごとに voice/non-voice を判定し、voice 区間だけ Opus エンコーダに流す。無音をそもそもエンコードしない方式なので、ファイルサイズと長さがそのまま縮む (後段で sox などを掛けるのとは違う)。

実体は `Sample.Shared/Audio/VadGate.cs` の状態機械。

- **トリガー**: 60 ms 連続で voice → ゲートを開く
- **プリロール**: 開いた瞬間に直前 100 ms をまとめて出力 (語頭の子音を切り落とさないため)
- **ハングオーバー**: voice が途切れても 200 ms は出力を続ける (息継ぎや小さな間で切れない)

UI 側では両クライアントとも「無音除去」チェックと「精度」スライダー (0=ゆるめ … 3=最強) で aggressiveness を切り替えられる。値が大きいほど voice 判定が厳しくなり、無音をしっかり削るが語頭/語尾の取りこぼしも増える。録音中は変更不可。

VAD は録音パイプラインの中だけで完結しているので、Streaming/Unary どちらの送信経路でもそのまま効く。サーバーには既に詰めたあとの Ogg Opus が届くだけ。

## 実行

```powershell
dotnet build Sample.slnx

# サーバー (http://0.0.0.0:5000)
dotnet run --project src/Sample.Server/Sample.Server.csproj

# クライアント (いずれか)
dotnet run --project src/Sample.Client.Streaming/Sample.Client.Streaming.csproj
dotnet run --project src/Sample.Client.Unary/Sample.Client.Unary.csproj
dotnet run --project src/Sample.Client.Stt/Sample.Client.Stt.csproj
```

## 詳細

設計の根拠・地雷・パラメータは `SPEC.md` を参照。
