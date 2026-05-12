# sample-wpf-grpc-ogg-opus 仕様書

## 1. 概要

NAudio で録音した音声を Concentus で Opus エンコードし、MagicOnion (gRPC) でサーバーへ送信、サーバー側で Ogg Opus ファイルとして保存するクラサバ構成のサンプルアプリケーション。クライアントから保存済みファイルを取得して再生・シーク操作も行う。

通信パターンの学習を目的に、ClientStreaming 版と Unary 版の 2 種類のクライアントを用意する (サーバーは共通)。任意機能として、WebRTC VAD で無音区間を録音時に削るパスを両クライアントに用意している。さらに保存済みファイルを取得して STT (sherpa-onnx / Azure Speech) で文字起こしする 3 つ目のクライアントを同梱している。

## 2. アーキテクチャ

```
┌─────────────────────────────┐         ┌───────────────────────────┐
│ Client (.NET 10, WPF, x64)  │         │ Server (.NET 10)          │
│  ┌────────────────────┐     │  HTTP/2 │  ASP.NET Core +           │
│  │ NAudio 録音         │     │  (h2c)  │  MagicOnion v7            │
│  │  ↓ PCM 16-bit       │     │ ◄─────► │  ┌─────────────────────┐ │
│  │ (任意) WebRTC VAD   │     │  gRPC   │  │ IRecordingService   │ │
│  │  ↓ voice 区間のみ    │     │         │  │  - SaveStreaming    │ │
│  │ Concentus エンコード│     │         │  │  - SaveUnary        │ │
│  │ Concentus.Oggfile   │     │         │  │  - Download         │ │
│  │  ↓ Ogg Opus bytes   │     │         │  └─────────────────────┘ │
│  │ MagicOnion v7       │     │         │  ┌─────────────────────┐ │
│  │  (Grpc.Net.Client)  │     │         │  │ FileSystem          │ │
│  └────────────────────┘     │         │  │  → recording.opus   │ │
│  ┌────────────────────┐     │         │  │  (Opus/Ogg は       │ │
│  │ NAudio 再生 (DL後)  │     │         │  │   非依存。バイト    │ │
│  └────────────────────┘     │         │  │   をそのまま書く)   │ │
└─────────────────────────────┘         └───────────────────────────┘
```

サーバーは Opus も Ogg も一切意識しない。クライアント側で Ogg Opus コンテナまで組み立てて送り、サーバーは届いたバイトを単一ファイルに書くだけ。サンプルとして「サービスの責務をどこで切るか」を見せるための意図的な分担で、技術的な制約から来るものではない。

## 3. プロジェクト構成

```
sample-wpf-grpc-ogg-opus/
├── Sample.slnx                          (.NET 10 SDK の XML 形式ソリューション)
└── src/
    ├── Sample.Shared/                   net10.0               サービス契約 + DTO + VadGate + AudioConstants
    ├── Sample.Server/                   net10.0               MagicOnion v7 サーバー
    ├── Sample.Client.Streaming/         net10.0-windows (WPF) ClientStreaming 版
    ├── Sample.Client.Unary/             net10.0-windows (WPF) Unary 版
    └── Sample.Client.Stt/               net10.0-windows (WPF) STT (sherpa-onnx / Azure) 版
```

ソリューションファイルは従来の `.sln` ではなく `.slnx` (XML)。VS / dotnet CLI 双方が解釈可能。

### 3.1 ターゲットフレームワークとパッケージ

| プロジェクト | TFM | 主要パッケージ |
|---|---|---|
| `Sample.Shared` | `net10.0` | `MagicOnion.Abstractions` 7.0.6, `MessagePack` 3.1.1, `WebRtcVadSharp` 1.3.2 |
| `Sample.Server` | `net10.0` | `MagicOnion.Server` 7.0.6, `Grpc.AspNetCore` 2.71.0 |
| `Sample.Client.Streaming` | `net10.0-windows` (WPF) | `MagicOnion.Client` 7.0.6, `Grpc.Net.Client` 2.71.0, `NAudio` 2.2.1, `Concentus` 1.1.7, `Concentus.OggFile` 1.0.4 |
| `Sample.Client.Unary` | `net10.0-windows` (WPF) | 同上 |
| `Sample.Client.Stt` | `net10.0-windows` (WPF) | `MagicOnion.Client` 7.0.6, `Grpc.Net.Client` 2.71.0, `NAudio` 2.2.1, `Concentus` 1.1.7, `Concentus.OggFile` 1.0.4, `org.k2fsa.sherpa.onnx` 1.13.0, `Microsoft.CognitiveServices.Speech` 1.49.1 |

注:

- `Concentus` は 1.x 系を使用 (2.x は API が `OpusCodecFactory` ベースに変わっており、`Concentus.OggFile` 1.0.4 と整合しないため)。
- `WebRtcVadSharp` は `WebRtcVad.dll` (ネイティブ) を要求する。AnyCPU だと `WebRtcVadSharp.targets` が警告を出して既定 x64 を使うため、`Sample.Shared` および全クライアントは `<PlatformTarget>x64</PlatformTarget>` を明示している。
- sherpa-onnx / Azure Speech / NAudio のネイティブ DLL も x64 のみ提供。クライアント側は一律 x64 で揃える。
- gRPC は MagicOnion v7 で統一しているのでクロスバージョンの罠は無い。クライアント側の HTTP/2 ランタイムは `Grpc.Net.Client` (`SocketsHttpHandler` ベース)。サーバー側は `Grpc.AspNetCore` (Kestrel)。

### 3.2 共有ライブラリの方針

`Sample.Shared` は `net10.0` ライブラリとして 1 か所で `IRecordingService` 契約と DTO を定義し、サーバー・クライアント双方が `ProjectReference` で参照する。MagicOnion バージョンが揃っているので追加の細工 (ソースリンクや同名インタフェースのローカル定義) は不要。

`VadGate.cs` も `Sample.Shared` に置き、両クライアント (Streaming / Unary) と STT クライアントから直接使う。`WebRtcVadSharp` の `PackageReference` は `Sample.Shared` 側にあり、各クライアントへは ProjectReference 経由で伝播するので、ネイティブ DLL も `bin/` に展開される。

## 4. オーディオパラメータ (固定)

| 項目 | 値 |
|---|---|
| サンプリングレート | 48000 Hz |
| チャンネル | 1 (モノラル) |
| PCM ビット深度 | 16-bit signed little-endian |
| Opus フレーム長 | 20 ms (= 960 サンプル) |
| Opus アプリケーションモード | `OPUS_APPLICATION_VOIP` |
| Opus ビットレート | 64000 bps (VBR) |
| 1 フレームあたりの PCM バイト数 | 1920 byte (960 sample × 2 byte) |

これらの定数は `Sample.Shared/AudioConstants.cs` に集約されている。クライアント/サーバー双方からこの 1 か所を参照すること。

## 5. ファイル管理

- サーバー側保存ファイル: 単一ファイルで上書き運用。録音実行のたびに上書きされ、バージョン管理・履歴は持たない。
- 既定の保存パス: `Sample.Server` 実行ディレクトリ配下の `recordings/recording.opus`。
- 保存先は `appsettings.json` の `Recording:Directory` / `Recording:FileName` で変更可能 (`FileSystemRecordingStore` が解決)。
- `Program.cs` 冒頭で `Environment.CurrentDirectory = AppContext.BaseDirectory;` を設定しているため、`dotnet run` でプロジェクトディレクトリから起動しても `bin\Debug\net10.0\` を起点に解決される。

## 6. サービス契約 (`Sample.Shared`)

### 6.1 `IRecordingService`

```csharp
public interface IRecordingService : IService<IRecordingService>
{
    // ClientStreaming 版: 録音中フレームを逐次送信
    // v7 ではサーバー・クライアント共通で Task<ClientStreamingResult<,>> を返す
    Task<ClientStreamingResult<RecordingChunk, RecordingResult>> SaveStreaming();

    // Unary 版: ローカルでエンコード済みの Ogg Opus を一括送信
    UnaryResult<RecordingResult> SaveUnary(SaveUnaryRequest request);

    // ダウンロード (再生用): 保存済みファイル全体を取得
    // 引数 0 個の Unary は MagicOnion / MessagePack の組合せで地味に踏みやすいのでダミー DTO を持たせる
    UnaryResult<DownloadResult> Download(DownloadRequest request);
}
```

クライアント側からの呼び出しは `var ctx = await client.SaveStreaming();` で `ClientStreamingResult<,>` を取得し、`ctx.RequestStream.WriteAsync(...)` でチャンクを送信、最後に `ctx.RequestStream.CompleteAsync()` + `await ctx.ResponseAsync` でレスポンスを受ける。

### 6.2 DTO

```csharp
[MessagePackObject]
public class RecordingChunk
{
    // クライアントで Ogg Opus 化済みのバイト列の一部分 (ストリーム上の連続したスライス)。
    // サーバーは届いた順に追記すれば最終的に妥当な Ogg Opus ファイルになる。
    [Key(0)] public byte[]? OggOpusBytes { get; set; }
}

[MessagePackObject]
public class RecordingResult
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public string? SavedPath { get; set; }
    [Key(2)] public long ByteSize { get; set; }      // 受信して保存した Ogg Opus のバイト数
    [Key(3)] public string? ErrorMessage { get; set; }
}

[MessagePackObject]
public class SaveUnaryRequest
{
    // クライアントで Ogg Opus コンテナ化済みのファイル全体
    [Key(0)] public byte[]? OggOpusBytes { get; set; }
}

[MessagePackObject]
public class DownloadRequest
{
    // 現状未使用 (単一ファイル前提)。引数 0 個メソッドを避けるためのダミー。
    [Key(0)] public string? FileId { get; set; }
}

[MessagePackObject]
public class DownloadResult
{
    [Key(0)] public bool Exists { get; set; }
    [Key(1)] public byte[]? OggOpusBytes { get; set; }
}
```

継続時間 (再生長) は DTO に含めていない。クライアント側で `OpusOggReadStream` でデコードした後の PCM サイズ ÷ `BytesPerSecond` から算出する。

### 6.3 パケット化方針

クライアント側で `OpusOggWriteStream` (Concentus.Oggfile) を用いて **Ogg Opus 形式までエンコードしてから送る**。サーバーは Opus/Ogg を一切意識せずバイト列をそのまま保存するだけ。

- **ClientStreaming 版**: 録音中に `OpusOggWriteStream` の出力先として `ChunkForwardStream` (独自実装の `Stream`) を渡す。`Write` のたびに内部バッファに溜め、しきい値 (32 KB) を超えたら同期的に gRPC `RequestStream.WriteAsync(new RecordingChunk{ OggOpusBytes = ... })` を呼ぶ。
- **Unary 版**: `MemoryStream` を出力先にして録音終了まで Ogg Opus を組み立て、`MemoryStream.ToArray()` を Unary で一括送信。

この設計により、サーバー側は Ogg 構造を理解する必要がない (受信した byte をそのまま `recording.opus` に書くだけ)。

## 7. サーバー仕様 (`Sample.Server`)

### 7.1 ホスティング

- ASP.NET Core (`Microsoft.AspNetCore.App`) + Kestrel
- HTTP/2 平文 (h2c) で listen (TLS なし、サンプルのため)
- ポート: `http://0.0.0.0:5000` (`Program.cs` の `ListenAnyIP(5000, ...)` が `launchSettings.json` の `applicationUrl` より優先)
- `MagicOnion.Server` のサービスを `MapMagicOnionService()` で登録
- ルート (`/`) には GET で文字列レスポンスを返すだけのエンドポイントが入っている (起動確認用)

### 7.2 `RecordingService` 実装ポイント

- **`SaveStreaming`** (`Task<ClientStreamingResult<RecordingChunk, RecordingResult>>`):
  - `GetClientStreamingContext<RecordingChunk, RecordingResult>()` でコンテキスト取得
  - `IRecordingStore.OpenWrite()` で `recordings/recording.opus` を `FileMode.Create` (上書き) で開く
  - `while (await ctx.MoveNext()) { ... }` で受信した各 `OggOpusBytes` を `FileStream.WriteAsync` でそのまま追記
  - 終端で `FileStream` を `await using` のスコープ外で Dispose し、`RecordingResult` に `SavedPath` と `ByteSize` を入れて `ctx.Result(...)` で返す
  - 例外時は `Success=false` + `ErrorMessage` を返す
- **`SaveUnary`** (`UnaryResult<RecordingResult>`):
  - `IRecordingStore.WriteAllAsync(bytes)` でそのまま `recordings/recording.opus` に書く (上書き)
  - 同様に `RecordingResult` を返す
- **`Download`** (`UnaryResult<DownloadResult>`):
  - 引数の `DownloadRequest` は現状参照しない (単一ファイル前提)
  - `IRecordingStore.ReadAllAsync()` でファイル全体を読み、`Exists` と `OggOpusBytes` を返す
  - メッセージサイズが gRPC 既定上限 (4 MB) を超える可能性があるため、`AddGrpc` の `MaxReceiveMessageSize` / `MaxSendMessageSize` を 64 MB に上げている

### 7.3 Storage 抽象

`IRecordingStore` (`OpenWrite` / `WriteAllAsync` / `ReadAllAsync` / `SavedPath`) を `FileSystemRecordingStore` で実装。`appsettings.json` から保存ディレクトリ・ファイル名を読む。サーバーは Concentus も `OpusOggWriteStream` も依存しない。

## 8. クライアント共通仕様 (`Sample.Client.Streaming` / `Sample.Client.Unary`)

### 8.1 UI (WPF / `MainWindow.xaml`)

```
┌──────────────────────────────────────────────────────┐
│  [● 録音] [▶ 再生] [⏸ 一時停止] [■ 停止]              │
│                                                      │
│  ├──────●──────────────────────┤                     │
│  (シークスライダー)                                   │
│                                                      │
│  00:12 / 00:34                                       │
│  状態: 待機中 / 録音中 / 再生中 / 一時停止             │
│                                                      │
│  [☐ 無音をカットする (VAD)]   精度: ──●──── (ゆるめ)  │
│                                                      │
│  ┌────────────────────────────────────────────────┐ │
│  │ (WaveformView: 録音中の波形をリアルタイム描画) │ │
│  └────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────┘
```

| コントロール | 役割 |
|---|---|
| 録音ボタン (`btnRecord`) | NAudio 録音開始 |
| 再生ボタン (`btnPlay`) | サーバーから DL → デコード → 再生開始 / 一時停止解除 |
| 一時停止ボタン (`btnPause`) | 再生中の `WaveOutEvent` を `Pause()` |
| 停止ボタン (`btnStop`) | 録音 or 再生を停止 |
| シークスライダー (`tbSeek`, WPF `Slider`, 0..1000) | 再生位置の表示・操作。録音中は無効化 |
| 経過/全長ラベル (`lblTime`) | `mm:ss / mm:ss` 形式で再生時間を表示 |
| ステータスラベル (`lblStatus`) | "状態: ..." を表示 |
| 無音除去チェック (`chkRemoveSilence`) | VAD によるリアルタイム無音カットの有効/無効。録音中は無効化 |
| 精度スライダー (`tbVadAggressiveness`, 0..3) | VAD aggressiveness。0=ゆるめ (既定) / 1=ふつう / 2=強め / 3=最強。録音中は無効化 |
| 波形ビュー (`waveformView`) | `FrameworkElement` を継承した独自描画コントロール (`OnRender(DrawingContext)`) |

両クライアントの UI は実質同一 (タイトルだけ "Sample.Client.Streaming" / "Sample.Client.Unary" で区別)。

### 8.2 録音処理 (両クライアント共通)

1. `WaveInEvent` を `WaveFormat = new WaveFormat(48000, 16, 1)` で初期化
2. `BufferMilliseconds = 20` (Opus フレームと一致させる)
3. `DataAvailable` で受け取った PCM (`byte[]`) を `short[]` に変換
4. **VAD 有効時**: `VadGate.Process(pcm, count, emit)` で voice 区間だけを `OpusOggWriteStream.WriteSamples` へ流す。**VAD 無効時**: `OpusOggWriteStream.WriteSamples` に直接流す
5. `OpusOggWriteStream` が内部で 1 フレーム (960 サンプル) ごとに `OpusEncoder.Encode` し、Ogg ページに詰めて出力先 Stream へ書く
6. `RecordingStop` イベントで VAD の端数フレームを Flush → `OpusOggWriteStream.Finish()` でトレーラを書き出す

### 8.3 ClientStreaming 版送信パス (`Sample.Client.Streaming`)

`StreamingRecorder` + `ChunkForwardStream` + `RecordingClient` の 3 クラス構成。

**録音開始 (`StartAsync`)**:

1. `OpusEncoder.Create(48000, 1, OPUS_APPLICATION_VOIP)` 生成、Bitrate 設定
2. `var ctx = await service.SaveStreaming();` で `ClientStreamingResult<,>` を取得 (v7 では Task で包まれているので await する)
3. `ChunkForwardStream` を生成。送信デリゲート内で `ctx.RequestStream.WriteAsync(new RecordingChunk{ OggOpusBytes = bytes }).ConfigureAwait(false)` を呼ぶ
4. `OpusOggWriteStream(encoder, chunkForwardStream)` を生成。これだけで OpusHead/OpusTags が ChunkForwardStream に書き込まれる
5. その場で `_forwardStream.Flush()` を呼んで gRPC ストリームの最初の `WriteAsync` を打ち込み、HTTP/2 ストリームを温めておく (初回 WriteAsync 遅延による不安定さ回避)
6. `WaveInEvent.StartRecording()`

**録音中 (`OnDataAvailable`)**:

- PCM → `VadGate` (任意) → `OpusOggWriteStream.WriteSamples`
- ChunkForwardStream のしきい値 (32 KB) 超過時に同期送信 (NAudio スレッドから `GetAwaiter().GetResult()`)

**録音停止 (`OnRecordingStopped`)**:

1. `_vadGate?.Flush(...)` で VAD の Open 状態の端数フレームを WriteSamples (Finish 後は WriteSamples 不可なので順番厳守)
2. `_oggWriter.Finish()` で Ogg トレーラ書き出し + 内部 Stream Close
3. `ctx.RequestStream.CompleteAsync()` で gRPC レイヤの END_STREAM を送る (これは ChunkForwardStream の Close とは別物。HTTP/2 ストリーム末端を相手に通知する)
4. `await ctx.ResponseAsync` で `RecordingResult` 受領
5. `RecordingFinished` / `RecordingFailed` イベントを発火し、`StopAsync` の `TaskCompletionSource` を完了させる

### 8.4 Unary 版送信パス (`Sample.Client.Unary`)

`UnaryRecorder` + `RecordingClient` の 2 クラス構成。

**録音開始 (`StartAsync`)**: encoder + `MemoryStream` + `OpusOggWriteStream(encoder, memoryStream)` を生成、`WaveInEvent.StartRecording()`。

**録音中**: PCM → `VadGate` (任意) → `OpusOggWriteStream.WriteSamples`。出力先は `MemoryStream` なのでネットワーク I/O は発生しない。

**録音停止**:

1. `_vadGate?.Flush(...)`
2. `_oggWriter.Finish()`
3. `_buffer.ToArray()` で独立した `byte[]` を取得 (この後 MemoryStream は Dispose されるが取得済みコピーは安全)
4. `await _service.SaveUnary(new SaveUnaryRequest { OggOpusBytes = bytes })` で一括送信
5. `RecordingFinished` / `RecordingFailed` 発火

録音時間が長いとメッセージが膨らむため、`MaxSendMessageSize` を 64 MB に設定。

### 8.5 再生処理 (両クライアント共通)

1. `client.Download(new DownloadRequest())` でサーバーから Ogg Opus を取得
2. `OpusDecoder.Create(48000, 1)` + `OpusOggReadStream` で Ogg をデコードしながら全 PCM (16-bit, 48kHz, mono) を `MemoryStream` に展開
3. `RawSourceWaveStream(memoryStream, new WaveFormat(48000, 16, 1))` で `WaveStream` 化
4. `WaveOutEvent.Init(...)` → `Play()` で再生
5. シークスライダー操作 (`Slider.PreviewMouseDown` / `PreviewMouseUp` / `ValueChanged`) は `RawSourceWaveStream.Position = (long)(seconds * BytesPerSecond)` で実現
   - `BytesPerSecond = 48000 * 1 * 2 = 96000`
   - 16-bit 境界に揃えるため `bytePos -= bytePos % 2`
6. 再生中は `DispatcherTimer` (100ms 周期) で `Position` を読んでスライダーと時刻ラベルを更新
7. `PlaybackStopped` で再生完了処理 (UI 状態を待機中に戻す)

ドラッグ中 (`PreviewMouseDown` ↔ `PreviewMouseUp`) は `_seeking = true` で UI タイマー側の値書き換えを抑止し、ドラッグ操作と競合しないようにする。タイマーが値を書き戻すときは `_suppressSeekEvent` フラグで `ValueChanged` ハンドラの再帰を遮断する。

### 8.6 状態遷移

```
[待機中] ──録音ボタン──> [録音中] ──停止ボタン──> [待機中]
   │
   └──再生ボタン──> (DL中) ──> [再生中] ⇄ [一時停止] ──停止/末尾──> [待機中]
```

排他: 録音中は再生・一時停止・シークスライダー無効、再生/一時停止中は録音ボタン無効、録音中は VAD コントロールも変更不可。

## 9. VAD (任意 — 録音時無音除去)

WebRTC VAD (`WebRtcVadSharp` 1.3.2) を使い、20 ms フレーム単位で voice/non-voice を判定し、voice と判定された区間のみを Opus エンコードに流す。**無音をそのままエンコードしない**方式なので、生成される Ogg Opus ファイルのサイズと再生時間が直接縮む (sox 等の後段トリミングとは異なる)。

実装本体は `Sample.Shared/Audio/VadGate.cs`。状態機械は以下:

| パラメータ | 既定値 | 意味 |
|---|---|---|
| トリガー | 60 ms (= 3 フレーム連続 voice) | ゲートを Open する条件 |
| プリロール | 100 ms (= 5 フレーム) | Open 瞬間に直前バッファを一括出力 (語頭の子音切り落としを防ぐ) |
| ハングオーバー | 200 ms (= 10 フレーム) | voice が途切れても出力を続ける時間 (息継ぎ・小さな間で切れない) |
| Aggressiveness | 0..3 (既定 0 = ゆるめ) | `WebRtcVad.OperatingMode`。値が大きいほど voice 判定が厳しくなる |

`Process(short[] input, int count, Action<short[],int> emit)` は任意サンプル数で呼んでよい。内部で 960 サンプル境界に整列し、voice 判定結果に応じて `emit` を呼ぶ。`emit` 内ではバッファを即時消費すること (戻った直後に内部で書き換えられる可能性がある)。録音停止時は `Flush(emit)` を呼んで Open 状態の端数フレームを吐き出す (Closed 状態のプリロールは「開かなかった末尾の無音」として捨てる)。

UI 側は両クライアントとも `chkRemoveSilence` (有効/無効) と `tbVadAggressiveness` (0..3) を持ち、`StartAsync` 直前にレコーダのプロパティ (`EnableVad`, `VadAggressiveness`) に反映する。録音中は変更不可。

VAD は録音パイプライン内で完結しているので、Streaming/Unary どちらの送信経路にもそのまま効く。サーバー側には既に詰めた後の Ogg Opus が届くだけで、サーバーは VAD の存在を知らない。

## 10. gRPC 設定

- `MaxReceiveMessageSize` / `MaxSendMessageSize`: 64 MB (`64 * 1024 * 1024`)
  - サーバー: `builder.Services.AddGrpc(o => { o.MaxReceiveMessageSize = ...; o.MaxSendMessageSize = ...; })`
  - クライアント: `GrpcChannel.ForAddress(url, new GrpcChannelOptions { MaxReceiveMessageSize = ..., MaxSendMessageSize = ... })`
- 平文 HTTP/2 (h2c)
  - サーバー: `Kestrel` の `ListenAnyIP(5000, o => o.Protocols = HttpProtocols.Http2)`
  - クライアント: `GrpcChannel.ForAddress("http://host:5000", ...)` + `App.xaml.cs` で `AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true)` を有効化 (TLS なしの HTTP/2 を `SocketsHttpHandler` が許可するため必須)
- Streaming クライアントは録音開始前に `GrpcChannel.ConnectAsync(...)` を呼んで HTTP/2 コネクションを事前確立している。初回 `WriteAsync` 遅延による不安定さ回避。

## 11. ディレクトリ構造 (実体)

```
src/
├── Sample.Shared/
│   ├── Sample.Shared.csproj
│   ├── AudioConstants.cs                共通オーディオ定数 (48k/16bit/mono/20ms/64kbps)
│   ├── IRecordingService.cs             サービス契約 (v7 共通シグネチャ)
│   ├── Audio/
│   │   └── VadGate.cs                   WebRTC VAD ゲート (プリロール/トリガー/ハングオーバー)
│   └── Dto/
│       ├── RecordingChunk.cs
│       ├── RecordingResult.cs           Success / SavedPath / ByteSize / ErrorMessage
│       ├── SaveUnaryRequest.cs
│       ├── DownloadRequest.cs           引数 0 個 Unary を避けるためのダミー DTO
│       └── DownloadResult.cs            Exists / OggOpusBytes
│
├── Sample.Server/
│   ├── Sample.Server.csproj             Sample.Shared を ProjectReference
│   ├── Program.cs                       Kestrel h2c :5000, AddMagicOnion, MaxMessageSize=64MB
│   ├── appsettings.json                 Recording:Directory / Recording:FileName
│   ├── Services/
│   │   └── RecordingService.cs          ServiceBase<IRecordingService>
│   └── Storage/
│       ├── IRecordingStore.cs
│       └── FileSystemRecordingStore.cs  recordings/recording.opus への上書き保存
│
├── Sample.Client.Streaming/
│   ├── Sample.Client.Streaming.csproj   net10.0-windows, UseWPF=true, PlatformTarget=x64
│   ├── App.xaml / App.xaml.cs           h2c 用 Http2UnencryptedSupport を有効化
│   ├── MainWindow.xaml / MainWindow.xaml.cs
│   ├── appsettings.json                 Server:Host / Server:Port
│   ├── Configuration/
│   │   └── AppSettings.cs               System.Text.Json で読む簡素な設定ローダ
│   ├── Audio/
│   │   ├── StreamingRecorder.cs         NAudio + Concentus + ChunkForwardStream の連結
│   │   ├── ChunkForwardStream.cs        Stream 派生。32KB 超で同期 WriteAsync
│   │   └── Player.cs                    Download → OpusOggReadStream → RawSourceWaveStream → WaveOutEvent
│   ├── Rpc/
│   │   └── RecordingClient.cs           GrpcChannel + MagicOnionClient.Create<IRecordingService>
│   └── Ui/
│       └── WaveformView.cs              FrameworkElement + OnRender(DrawingContext) による波形描画
│
├── Sample.Client.Unary/
│   ├── Sample.Client.Unary.csproj       同上
│   ├── App.xaml / App.xaml.cs
│   ├── MainWindow.xaml / MainWindow.xaml.cs
│   ├── appsettings.json
│   ├── Configuration/AppSettings.cs
│   ├── Audio/
│   │   ├── UnaryRecorder.cs             NAudio + Concentus を MemoryStream に組み立て、Stop 時に SaveUnary
│   │   └── Player.cs                    Streaming 版と同等
│   ├── Rpc/RecordingClient.cs
│   └── Ui/WaveformView.cs
│
└── Sample.Client.Stt/
    ├── Sample.Client.Stt.csproj         + sherpa-onnx + Azure Speech
    ├── App.xaml / App.xaml.cs
    ├── MainWindow.xaml / MainWindow.xaml.cs
    ├── appsettings.json                 Azure キー、モデルパス、サーバー接続先
    ├── Configuration/SttSettings.cs
    ├── Audio/                            Ogg Opus → PCM48k → PCM16k float → WAV の一連
    ├── Rpc/RecordingClient.cs
    └── Stt/                              ISttEngine + Moonshine / Whisper / Azure 実装
```

## 12. 既知の制約

1. **シークバーの粒度 / メモリ**
   再生は PCM 全展開方式 (`MemoryStream` に全サンプルを展開) のため、長時間ファイルでメモリを食う。サンプルでは数分程度を上限と想定し、それ以上は対象外。
2. **NAudio の録音バッファサイズ**
   `BufferMilliseconds = 20` は最小に近い。OS によってはアンダーラン気味になるので、必要なら 40 ms / 60 ms に上げて受信側で 20 ms フレームに再分割する (現状はそのまま使用)。
3. **gRPC メッセージサイズ**
   `Download` で長時間ファイルが 64 MB を超える可能性は仕様上残る。本サンプルでは追求しない。必要なら ServerStreaming に切り替えて分割送信する。
4. **VAD の側面**
   WebRTC VAD は雑音耐性に限界があり、定常的な背景ノイズや音楽下では誤判定が出る。Aggressiveness を上げると無音はよく削れるが語頭/語尾の取りこぼしも増える。本サンプルは「動く例」を提供するに留め、調整は呼び出し側に任せる。
5. **平文 HTTP/2 (h2c) は学習目的のみ**
   サンプルは TLS なしで動かしている。本番では Kestrel に証明書を渡し、クライアント側も `https://` で接続すること。`Http2UnencryptedSupport` スイッチも本番では外す。

## 13. 実装で発見した事項 (建付けの根拠)

実装着手後に判明した事項。設計判断の理由として記録する。

- **`Concentus.OggFile` 1.0.4 は `Concentus` 1.x 前提**
  `Concentus` 2.x は `OpusEncoder.Create` 静的メソッドを廃止し `OpusCodecFactory.CreateEncoder` に移行している。`Concentus.OggFile` 1.0.4 は 1.x の `OpusEncoder` クラスを直接受け取る API のため、整合性が取れる 1.1.7 を採用。

- **`Concentus.Oggfile` の `OpusOggWriteStream.Finish()` は渡された Stream を `Close()` する**
  `leaveOpen` 相当のオプションがなく、`Finish()` の最後で `_outputStream.Close()` (= `Dispose`) を呼んでくる。Stream を引数で受け取るラッパー型としては作法から外れた挙動なので注意。本サンプルでは `ChunkForwardStream` がこれの影響を受けて、`Finish()` 直後に内部 `MemoryStream` が解放されてしまい、後続コードで触ったときに `ObjectDisposedException` → catch → `finally` の `CleanUp()` で gRPC コールが中断 → サーバー側 Kestrel に「The client reset the request stream (RST_STREAM)」として観測される、という連鎖を引き起こしていた。サーバーログだけ見ると「クライアントが切断した」としか読めず原因が見えにくいが、本当の原因はクライアント側の `ObjectDisposedException`。
  **対処**: `ChunkForwardStream.Dispose()` で内部 `MemoryStream` を解放しないようにし、`_closed` フラグだけ立てて GC 任せに変更。これで Concentus.Oggfile に勝手に Close されても二重 Flush で壊れない。

- **`Finish()` 後に呼ぶべき "CompleteAsync" は gRPC `RequestStream.CompleteAsync` のみ**
  `OpusOggWriteStream.Finish()` 内で `ChunkForwardStream.Flush()` まで走り、その同期パスで gRPC `WriteAsync` も完了している。よって `ChunkForwardStream` 自体に対する追加フラッシュは不要。一方で **gRPC レイヤの `ctx.RequestStream.CompleteAsync()` は別物で、これを呼ばないと HTTP/2 ストリームの END_STREAM がサーバーに届かず `MoveNext` のループが抜けない**。両者は名前が似ているので混同しないこと。

- **ClientStreaming 側はバックグラウンド・ポンプ・タスクではなく同期送信に**
  当初 `ChunkForwardStream` はバッファ溢れ時に `BlockingCollection<byte[]>` 経由で別タスクから `WriteAsync` する設計だったが、Concentus.Oggfile の Close 問題と相まって停止時のタイミング起因で不安定だったため、ポンプを廃止し NAudio スレッドで `WriteAsync.GetAwaiter().GetResult()` で同期ブロックする形にした。各チャンクは数十 KB なのでサンプル用途では十分。

- **同期 `GetAwaiter().GetResult()` 経路では `ConfigureAwait(false)` 必須**
  ChunkForwardStream の同期送信は UI スレッド (録音停止時の警告フラッシュ等) からも呼ばれるパスがあるので、`_sendAsync` ラムダ内の `await ctx.RequestStream.WriteAsync(...)` は `ConfigureAwait(false)` を付けないと SynchronizationContext デッドロックになる。UI スレッドが `.GetResult()` でブロックしている間に await の継続が UI スレッドへポストされて永久に動かない、という典型的なパターン。

- **`OpusOggWriteStream` 構築直後に Ogg ヘッダ (OpusHead/OpusTags) が出力先 Stream に書き込まれる**
  これを利用して、録音 (`WaveInEvent.StartRecording`) 前に `ChunkForwardStream.Flush()` を 1 回呼んで gRPC ストリームを温めている。初回 `WriteAsync` が録音開始から数百 ms 遅れて発生すると gRPC レイヤが状態を一時的に不安定にし、最初のチャンクが落ちるケースがあったため。

- **VAD と `Finish()` の順序**
  `VadGate.Flush(emit)` は `OpusOggWriteStream.WriteSamples` を呼ぶ。`OpusOggWriteStream.Finish()` 後は内部 Stream が Close 済みで WriteSamples が落ちるので、必ず `vadGate.Flush(...)` → `oggWriter.Finish()` の順に呼ぶこと。両レコーダの `OnRecordingStopped` でこの順序を守っている。

- **`WebRtcVadSharp` はネイティブ DLL を要求し AnyCPU では警告を出す**
  `WebRtcVadSharp.targets` が「プラットフォーム明示が必要」と警告し既定 x64 を使う。`Sample.Shared` および全クライアントの csproj に `<PlatformTarget>x64</PlatformTarget>` を明示。

- **`Grpc.Net.Client` の h2c は `Http2UnencryptedSupport` スイッチが必須**
  `SocketsHttpHandler` は既定で TLS なしの HTTP/2 を許可しない。`AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);` を `GrpcChannel.ForAddress("http://...")` より前に呼んでおく (各クライアントの `App.xaml.cs` コンストラクタで設定済み)。

- **`.NET 10 SDK` のソリューションファイルは `.slnx` (XML 形式)**
  従来の `.sln` ではなく `Sample.slnx` として作成される。VS / dotnet CLI 双方が解釈可能。

## 14. 非対象 (このサンプルで扱わないこと)

- 認証・認可
- TLS
- 複数録音の管理 / 一覧表示
- ノイズ抑制・エコーキャンセル (VAD のみ)
- メタデータ (録音日時タグ等) の永続化
- マルチクライアント排他制御
- 自動テスト (サンプルのため疎通確認は実機で行う)
