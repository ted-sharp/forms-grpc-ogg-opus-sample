# forms-grpc-ogg-opus-sample 仕様書

## 1. 概要

NAudio で録音した音声を Concentus で Opus エンコードし、MagicOnion (gRPC) でサーバーへ送信、サーバー側で Ogg Opus ファイルとして保存するクラサバ構成のサンプルアプリケーション。クライアントから保存済みファイルを取得して再生・シーク操作も行う。

通信パターンの学習を目的に、ClientStreaming 版と Unary 版の 2 種類のクライアントを用意する (サーバーは共通)。任意機能として、WebRTC VAD で無音区間を録音時に削るパスを両クライアントに用意している。

## 2. アーキテクチャ

```
┌─────────────────────────────┐         ┌───────────────────────────┐
│ Client (.NET Framework 4.8) │         │ Server (.NET 10)          │
│  Windows Forms              │         │  ASP.NET Core +           │
│  ┌────────────────────┐     │  HTTP/2 │  MagicOnion v7            │
│  │ NAudio 録音         │     │ ◄─────► │  ┌─────────────────────┐ │
│  │  ↓ PCM 16-bit       │     │  gRPC   │  │ IRecordingService   │ │
│  │ (任意) WebRTC VAD   │     │         │  │  - SaveStreaming    │ │
│  │  ↓ voice 区間のみ    │     │         │  │  - SaveUnary        │ │
│  │ Concentus エンコード│     │         │  │  - Download         │ │
│  │ Concentus.Oggfile   │     │         │  └─────────────────────┘ │
│  │  ↓ Ogg Opus bytes   │     │         │  ┌─────────────────────┐ │
│  │ MagicOnion v4       │     │         │  │ FileSystem          │ │
│  │  (Grpc.Core)        │     │         │  │  → recording.opus   │ │
│  └────────────────────┘     │         │  │  (Opus/Ogg は       │ │
│  ┌────────────────────┐     │         │  │   非依存。バイト    │ │
│  │ NAudio 再生 (DL後)  │     │         │  │   をそのまま書く)   │ │
│  └────────────────────┘     │         │  └─────────────────────┘ │
└─────────────────────────────┘         └───────────────────────────┘
```

サーバーは Opus も Ogg も一切意識しない。クライアント側で Ogg Opus コンテナまで組み立てて送り、サーバーは届いたバイトを単一ファイルに書くだけ。これは MagicOnion v4 ↔ v7 の API 差異の影響を受ける箇所をクライアントに閉じ込めるための意図的な分担。

## 3. プロジェクト構成

```
forms-grpc-ogg-opus-sample/
├── Sample.slnx                          (.NET 10 SDK の XML 形式ソリューション)
└── src/
    ├── Sample.Shared/                   netstandard2.0   サービス契約 + DTO + VadGate + AudioConstants
    ├── Sample.Server/                   net10.0          MagicOnion v7 サーバー
    ├── Sample.Client.Streaming/         net48            ClientStreaming 版 WinForms
    └── Sample.Client.Unary/             net48            Unary 版 WinForms
```

ソリューションファイルは従来の `.sln` ではなく `.slnx` (XML)。VS / dotnet CLI 双方が解釈可能。

### 3.1 ターゲットフレームワークとパッケージ

| プロジェクト | TFM | 主要パッケージ |
|---|---|---|
| `Sample.Shared` | `netstandard2.0` | `MagicOnion.Abstractions` 4.5.2, `MessagePack` 2.5.187, `WebRtcVadSharp` 1.3.2 |
| `Sample.Server` | `net10.0` | `MagicOnion.Server` 7.0.6, `Grpc.AspNetCore` 2.71.0 |
| `Sample.Client.Streaming` | `net48` | `MagicOnion.Client` 4.5.2, `Grpc.Core` 2.46.6, `NAudio` 2.2.1, `Concentus` 1.1.7, `Concentus.OggFile` 1.0.4, `WebRtcVadSharp` 1.3.2 |
| `Sample.Client.Unary` | `net48` | 同上 |

注:

- `Concentus` は 1.x 系を使用 (2.x は API が `OpusCodecFactory` ベースに変わっており、`Concentus.OggFile` 1.0.4 と整合しないため)。
- `WebRtcVadSharp` は `WebRtcVad.dll` (ネイティブ) を要求する。AnyCPU だと `WebRtcVadSharp.targets` が警告を出して既定 x64 を使うため、`Sample.Shared` および両クライアントは `<PlatformTarget>x64</PlatformTarget>` を明示している。`Sample.Shared` 自体はアーキ非依存だが NuGet パッケージ側のターゲットを満たすため。
- 両クライアント側にも `WebRtcVadSharp` を直接参照しているのは、ネイティブ DLL を exe の `bin/` に確実にコピーさせるため (Shared だけ参照しても落ちてくれないことがある)。
- `Grpc.Core` 2.46.x は EOL だが net48 用にこれを使う。NuGet 警告は無視する方針。

### 3.2 クロスバージョン通信に関する注記 (C案)

- クライアントは MagicOnion v4 (Grpc.Core ベース)、サーバーは MagicOnion v7 (Grpc.AspNetCore ベース)。これは公式に保証された組み合わせではない。
- Unary と ClientStreaming のみ使用し、StreamingHub は使わない (StreamingHub は v5 でハートビート仕様などが変更されたため非対称構成では避ける)。
- MessagePack のメジャーバージョンは v4/v7 とも 2.x で揃えること。
- もし通信エラーが発生する場合のフォールバック順:
  1. サーバー側 MagicOnion を v4 系に下げて Grpc.Core でホスト (B案)
  2. MagicOnion をやめて生 gRPC (`.proto` + `Grpc.Tools`) に切替 (D案)

### 3.3 共有ライブラリの方針

`Sample.Shared` は MagicOnion v4 の `MagicOnion.Abstractions` を参照する形で `netstandard2.0` でビルドし、両クライアント (NetFx 4.8 + MagicOnion v4) から `ProjectReference` で参照する。

サーバー (.NET 10 + MagicOnion v7) は `Sample.Shared` を **DLL として参照しない**。理由: `MagicOnion.Abstractions` v4 と v7 で `ClientStreamingResult<,>` の `[AsyncMethodBuilder]` 属性の有無が異なり、同居させると `async ClientStreamingResult` が解決できない。

代わりにサーバーは:

- `Sample.Shared/Dto/*.cs` を **Compile Include + Link** でソース取り込み (DTO 定義と MessagePack 属性を共用)
  ```xml
  <Compile Include="..\Sample.Shared\Dto\*.cs">
      <Link>Shared\%(FileName).cs</Link>
  </Compile>
  ```
- `Sample.Server.Services.IRecordingService` を**ローカル定義**し、`Task<ClientStreamingResult<,>>` を返す v7 風シグネチャを採用

gRPC のサービス名は `Type.Name` (= "IRecordingService") + メソッド名で決まるため、クライアントとサーバーで namespace が違っても疎通する。MessagePack の DTO は同一ソース由来なのでバイナリ互換も保たれる。

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
    ClientStreamingResult<RecordingChunk, RecordingResult> SaveStreaming();

    // Unary 版: ローカルでエンコード済みの Ogg Opus を一括送信
    UnaryResult<RecordingResult> SaveUnary(SaveUnaryRequest request);

    // ダウンロード (再生用): 保存済みファイル全体を取得
    // 引数 0 個メソッドは v4 ↔ v7 で msgpack ワイヤーフォーマット非互換のためダミー DTO を持たせる (§11.1)
    UnaryResult<DownloadResult> Download(DownloadRequest request);
}
```

サーバー側は `Sample.Server.Services.IRecordingService` でローカル再定義。シグネチャは v7 風で `SaveStreaming` の戻り値だけ `Task<ClientStreamingResult<,>>` に包む (§3.3, §11.1)。

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
    // 現状未使用 (単一ファイル前提)。
    // MagicOnion v4 → v7 の引数 0 個メソッド非互換 (§11.1) を回避するためのダミー引数。
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

### 8.1 UI (Windows Forms)

```
┌──────────────────────────────────────────────────────┐
│  [● 録音] [▶ 再生] [⏸ 一時停止] [■ 停止]              │
│                                                      │
│  ├──────●──────────────────────┤                     │
│  (シークバー)                                         │
│                                                      │
│  00:12 / 00:34                                       │
│  状態: 待機中 / 録音中 / 再生中 / 一時停止             │
│                                                      │
│  [☐ 無音をカットする (VAD)]   精度: ──●──── (強め)    │
└──────────────────────────────────────────────────────┘
```

| コントロール | 役割 |
|---|---|
| 録音ボタン (`btnRecord`) | NAudio 録音開始 |
| 再生ボタン (`btnPlay`) | サーバーから DL → デコード → 再生開始 / 一時停止解除 |
| 一時停止ボタン (`btnPause`) | 再生中の `WaveOutEvent` を `Pause()` |
| 停止ボタン (`btnStop`) | 録音 or 再生を停止 |
| シークバー (`tbSeek`, `TrackBar`, 0..1000) | 再生位置の表示・操作。録音中は無効化 |
| 経過/全長ラベル (`lblTime`) | `mm:ss / mm:ss` 形式で再生時間を表示 |
| ステータスラベル (`lblStatus`) | "状態: ..." を表示 |
| 無音除去チェック (`chkRemoveSilence`) | VAD によるリアルタイム無音カットの有効/無効。録音中は無効化 |
| 精度スライダー (`tbVadAggressiveness`, 0..3) | VAD aggressiveness。0=ゆるめ / 1=ふつう / 2=強め (既定) / 3=最強。録音中は無効化 |

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
2. `service.SaveStreaming()` で `ClientStreamingResult<,>` を取得 (v4 クライアントは同期返却)
3. `ChunkForwardStream` を生成。送信デリゲート内で `_streamCall.RequestStream.WriteAsync(new RecordingChunk{ OggOpusBytes = bytes }).ConfigureAwait(false)` を呼ぶ
4. `OpusOggWriteStream(encoder, chunkForwardStream)` を生成。これだけで OpusHead/OpusTags が ChunkForwardStream に書き込まれる
5. その場で `_forwardStream.Flush()` を呼んで gRPC ストリームの最初の `WriteAsync` を打ち込み、HTTP/2 ストリームを温めておく (初回 WriteAsync 遅延による不安定さ回避)
6. `WaveInEvent.StartRecording()`

**録音中 (`OnDataAvailable`)**:

- PCM → `VadGate` (任意) → `OpusOggWriteStream.WriteSamples`
- ChunkForwardStream のしきい値 (32 KB) 超過時に同期送信 (NAudio スレッドから `GetAwaiter().GetResult()`)

**録音停止 (`OnRecordingStopped`)**:

1. `_vadGate?.Flush(...)` で VAD の Open 状態の端数フレームを WriteSamples (Finish 後は WriteSamples 不可なので順番厳守)
2. `_oggWriter.Finish()` で Ogg トレーラ書き出し + 内部 Stream Close
3. `_streamCall.RequestStream.CompleteAsync()` で gRPC レイヤの END_STREAM を送る (これは ChunkForwardStream の Close とは別物。HTTP/2 ストリーム末端を相手に通知する)
4. `await _streamCall.ResponseAsync` で `RecordingResult` 受領
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

録音時間が長いとメッセージが膨らむため、`MaxSendMessageLength` を 64 MB に設定。

### 8.5 再生処理 (両クライアント共通)

1. `client.Download(new DownloadRequest())` でサーバーから Ogg Opus を取得
2. `OpusDecoder.Create(48000, 1)` + `OpusOggReadStream` で Ogg をデコードしながら全 PCM (16-bit, 48kHz, mono) を `MemoryStream` に展開
3. `RawSourceWaveStream(memoryStream, new WaveFormat(48000, 16, 1))` で `WaveStream` 化
4. `WaveOutEvent.Init(...)` → `Play()` で再生
5. シークバー操作 (`tbSeek.Scroll` / `MouseUp`) は `RawSourceWaveStream.Position = (long)(seconds * BytesPerSecond)` で実現
   - `BytesPerSecond = 48000 * 1 * 2 = 96000`
   - 16-bit 境界に揃えるため `bytePos -= bytePos % 2`
6. 再生中は UI スレッドの `Timer` (100ms 周期) で `Position` を読んでシークバーと時刻ラベルを更新
7. `PlaybackStopped` で再生完了処理 (UI 状態を待機中に戻す)

ドラッグ中 (`MouseDown` ↔ `MouseUp`) は `_seeking = true` で UI タイマー側の値書き換えを抑止し、ドラッグ操作と競合しないようにする。

### 8.6 状態遷移

```
[待機中] ──録音ボタン──> [録音中] ──停止ボタン──> [待機中]
   │
   └──再生ボタン──> (DL中) ──> [再生中] ⇄ [一時停止] ──停止/末尾──> [待機中]
```

排他: 録音中は再生・一時停止・シークバー無効、再生/一時停止中は録音ボタン無効、録音中は VAD コントロールも変更不可。

## 9. VAD (任意 — 録音時無音除去)

WebRTC VAD (`WebRtcVadSharp` 1.3.2) を使い、20 ms フレーム単位で voice/non-voice を判定し、voice と判定された区間のみを Opus エンコードに流す。**無音をそのままエンコードしない**方式なので、生成される Ogg Opus ファイルのサイズと再生時間が直接縮む (sox 等の後段トリミングとは異なる)。

実装本体は `Sample.Shared/Audio/VadGate.cs`。状態機械は以下:

| パラメータ | 既定値 | 意味 |
|---|---|---|
| トリガー | 60 ms (= 3 フレーム連続 voice) | ゲートを Open する条件 |
| プリロール | 100 ms (= 5 フレーム) | Open 瞬間に直前バッファを一括出力 (語頭の子音切り落としを防ぐ) |
| ハングオーバー | 200 ms (= 10 フレーム) | voice が途切れても出力を続ける時間 (息継ぎ・小さな間で切れない) |
| Aggressiveness | 0..3 (既定 2) | `WebRtcVad.OperatingMode`。値が大きいほど voice 判定が厳しくなる |

`Process(short[] input, int count, Action<short[],int> emit)` は任意サンプル数で呼んでよい。内部で 960 サンプル境界に整列し、voice 判定結果に応じて `emit` を呼ぶ。`emit` 内ではバッファを即時消費すること (戻った直後に内部で書き換えられる可能性がある)。録音停止時は `Flush(emit)` を呼んで Open 状態の端数フレームを吐き出す (Closed 状態のプリロールは「開かなかった末尾の無音」として捨てる)。

UI 側は両クライアントとも `chkRemoveSilence` (有効/無効) と `tbVadAggressiveness` (0..3) を持ち、`StartAsync` 直前にレコーダのプロパティ (`EnableVad`, `VadAggressiveness`) に反映する。録音中は変更不可。

VAD は録音パイプライン内で完結しているので、Streaming/Unary どちらの送信経路にもそのまま効く。サーバー側には既に詰めた後の Ogg Opus が届くだけで、サーバーは VAD の存在を知らない。

## 10. gRPC 設定

- `MaxReceiveMessageSize` / `MaxSendMessageSize`: 64 MB (`64 * 1024 * 1024`)
  - サーバー: `builder.Services.AddGrpc(o => { o.MaxReceiveMessageSize = ...; o.MaxSendMessageSize = ...; })`
  - クライアント: `Channel` 構築時の `ChannelOption(ChannelOptions.MaxReceiveMessageLength, ...)` / `MaxSendMessageLength`
- 平文 HTTP/2 (h2c)
  - サーバー: `Kestrel` の `ListenAnyIP(5000, o => o.Protocols = HttpProtocols.Http2)`
  - クライアント: `new Channel("localhost", 5000, ChannelCredentials.Insecure)`
- クライアント側で録音開始前に `Channel.ConnectAsync(deadline)` を呼んで HTTP/2 コネクションを事前確立している。ClientStreaming で初回 `WriteAsync` が遅延すると Grpc.Core が状態を不安定にすることがあるため。

## 11. ディレクトリ構造 (実体)

```
src/
├── Sample.Shared/
│   ├── Sample.Shared.csproj
│   ├── AudioConstants.cs                共通オーディオ定数 (48k/16bit/mono/20ms/64kbps)
│   ├── IRecordingService.cs             v4 風シグネチャのサービス契約
│   ├── Audio/
│   │   └── VadGate.cs                   WebRTC VAD ゲート (プリロール/トリガー/ハングオーバー)
│   └── Dto/
│       ├── RecordingChunk.cs
│       ├── RecordingResult.cs           Success / SavedPath / ByteSize / ErrorMessage
│       ├── SaveUnaryRequest.cs
│       ├── DownloadRequest.cs           v4↔v7 引数 0 個 Unary 非互換のためのダミー DTO
│       └── DownloadResult.cs            Exists / OggOpusBytes
│
├── Sample.Server/
│   ├── Sample.Server.csproj             Compile Include + Link で ../Sample.Shared/Dto/*.cs を取り込む
│   ├── Program.cs                       Kestrel h2c :5000, AddMagicOnion, MaxMessageSize=64MB
│   ├── appsettings.json                 Recording:Directory / Recording:FileName
│   ├── Services/
│   │   ├── IRecordingService.cs         サーバーローカル定義 (v7 風: Task<ClientStreamingResult<,>>)
│   │   └── RecordingService.cs          ServiceBase<IRecordingService>
│   └── Storage/
│       ├── IRecordingStore.cs
│       └── FileSystemRecordingStore.cs  recordings/recording.opus への上書き保存
│
├── Sample.Client.Streaming/
│   ├── Sample.Client.Streaming.csproj   PlatformTarget=x64
│   ├── Program.cs
│   ├── MainForm.cs / MainForm.Designer.cs
│   ├── Audio/
│   │   ├── StreamingRecorder.cs         NAudio + Concentus + ChunkForwardStream の連結
│   │   ├── ChunkForwardStream.cs        Stream 派生。32KB 超で同期 WriteAsync
│   │   └── Player.cs                    Download → OpusOggReadStream → RawSourceWaveStream → WaveOutEvent
│   └── Rpc/
│       └── RecordingClient.cs           Channel + MagicOnionClient.Create<IRecordingService>
│
└── Sample.Client.Unary/
    ├── Sample.Client.Unary.csproj       PlatformTarget=x64
    ├── Program.cs
    ├── MainForm.cs / MainForm.Designer.cs
    ├── Audio/
    │   ├── UnaryRecorder.cs             NAudio + Concentus を MemoryStream に組み立て、Stop 時に SaveUnary
    │   └── Player.cs                    Streaming 版と同等
    └── Rpc/
        └── RecordingClient.cs
```

## 12. 既知の制約

1. **シークバーの粒度 / メモリ**
   再生は PCM 全展開方式 (`MemoryStream` に全サンプルを展開) のため、長時間ファイルでメモリを食う。サンプルでは数分程度を上限と想定し、それ以上は対象外。
2. **NAudio の録音バッファサイズ**
   `BufferMilliseconds = 20` は最小に近い。OS によってはアンダーラン気味になるので、必要なら 40 ms / 60 ms に上げて受信側で 20 ms フレームに再分割する (現状はそのまま使用)。
3. **gRPC メッセージサイズ**
   `Download` で長時間ファイルが 64 MB を超える可能性は仕様上残る。本サンプルでは追求しない。必要なら ServerStreaming に切り替えて分割送信する。
4. **`Grpc.Core` 2.46.x は EOL**
   net48 用に他に選択肢が乏しいため採用。NuGet 警告は無視。
5. **VAD の側面**
   WebRTC VAD は雑音耐性に限界があり、定常的な背景ノイズや音楽下では誤判定が出る。Aggressiveness を上げると無音はよく削れるが語頭/語尾の取りこぼしも増える。本サンプルは「動く例」を提供するに留め、調整は呼び出し側に任せる。

## 13. 実装で発見した事項 (建付けの根拠)

実装着手後に判明した事項。設計判断の理由として記録する。

- **`MagicOnion.Abstractions` v4 / v7 の互換性は保てない**
  v4 の `ClientStreamingResult<TReq, TRes>` は `[AsyncMethodBuilder]` 属性付きで `async` 戻り型として使えるが、v7 では同属性が外されている。共通 DLL を介した型共有は破綻するため、サーバーは Sample.Shared を DLL 参照せず、DTO のみソースリンクする構成にした (§3.3)。

- **MagicOnion v4 クライアントの ClientStreaming 起動は同期呼び出し**
  `client.SaveStreaming()` は `ClientStreamingResult<,>` を直接返す (await すると `[AsyncMethodBuilder]` の `GetAwaiter` 経由で `ResponseAsync` の結果型に化けて型エラーになる)。`var stream = client.SaveStreaming();` のように代入で受ける必要がある。

- **MagicOnion v7 サーバーの ClientStreaming 戻り型は `Task<ClientStreamingResult<,>>`**
  v7 の `ClientStreamingResult<,>` は task-like ではないため、`async ClientStreamingResult<,>` は書けない。`Task<>` で包む。

- **MagicOnion v7 の `ClientStreamingContext` には `ReadAllAsync` がない**
  `while (await ctx.MoveNext()) { var item = ctx.Current; ... }` のループで読む。

- **MagicOnion v4 ↔ v7 では「引数 0 個 Unary」のワイヤーフォーマットが非互換**
  v4 クライアントは引数なしメソッドのリクエストを `bin 8` (空 byte 配列, msgpack code 0xC4) として送るが、v7 サーバーは `Nil` (0xC0) として読もうとして `MessagePackSerializationException: Unexpected msgpack code 196` で失敗する。**回避策: 引数 0 個メソッドにダミー DTO 引数を持たせる**。本サンプルでは `Download()` を `Download(DownloadRequest request)` に変更して回避。`SaveStreaming()` (ClientStreaming) は最初に request メッセージを送らないため影響なし。`SaveUnary` は元から引数ありなので影響なし。

- **`Concentus.OggFile` 1.0.4 は `Concentus` 1.x 前提**
  `Concentus` 2.x は `OpusEncoder.Create` 静的メソッドを廃止し `OpusCodecFactory.CreateEncoder` に移行している。`Concentus.OggFile` 1.0.4 は 1.x の `OpusEncoder` クラスを直接受け取る API のため、整合性が取れる 1.1.7 を採用。

- **`.NET 10 SDK` のソリューションファイルは `.slnx` (XML 形式)**
  従来の `.sln` ではなく `Sample.slnx` として作成される。VS / dotnet CLI 双方が解釈可能。

- **`Concentus.Oggfile` の `OpusOggWriteStream.Finish()` は渡された Stream を `Close()` する**
  `leaveOpen` 相当のオプションがなく、`Finish()` の最後で `_outputStream.Close()` (= `Dispose`) を呼んでくる。Stream を引数で受け取るラッパー型としては作法から外れた挙動なので注意。本サンプルでは `ChunkForwardStream` がこれの影響を受けて、`Finish()` 直後に内部 `MemoryStream` が解放されてしまい、後続コードで触ったときに `ObjectDisposedException` → catch → `finally` の `CleanUp()` で `_streamCall.Dispose()` → gRPC コールが中断 → サーバー側 Kestrel に「The client reset the request stream (RST_STREAM)」として観測される、という連鎖を引き起こしていた。サーバーログだけ見ると「クライアントが切断した」としか読めず原因が見えにくいが、本当の原因はクライアント側の `ObjectDisposedException`。
  **対処**: `ChunkForwardStream.Dispose()` で内部 `MemoryStream` を解放しないようにし、`_closed` フラグだけ立てて GC 任せに変更。これで Concentus.Oggfile に勝手に Close されても二重 Flush で壊れない。

- **`Finish()` 後に呼ぶべき "CompleteAsync" は gRPC `RequestStream.CompleteAsync` のみ**
  `OpusOggWriteStream.Finish()` 内で `ChunkForwardStream.Flush()` まで走り、その同期パスで gRPC `WriteAsync` も完了している。よって `ChunkForwardStream` 自体に対する追加フラッシュは不要。一方で **gRPC レイヤの `_streamCall.RequestStream.CompleteAsync()` は別物で、これを呼ばないと HTTP/2 ストリームの END_STREAM がサーバーに届かず `MoveNext` のループが抜けない**。両者は名前が似ているので混同しないこと。

- **ClientStreaming 側はバックグラウンド・ポンプ・タスクではなく同期送信に**
  当初 `ChunkForwardStream` はバッファ溢れ時に `BlockingCollection<byte[]>` 経由で別タスクから `WriteAsync` する設計だったが、Concentus.Oggfile の Close 問題と相まって停止時のタイミング起因で不安定だったため、ポンプを廃止し NAudio スレッドで `WriteAsync.GetAwaiter().GetResult()` で同期ブロックする形にした。各チャンクは数十 KB なのでサンプル用途では十分。

- **同期 `GetAwaiter().GetResult()` 経路では `ConfigureAwait(false)` 必須**
  ChunkForwardStream の同期送信は UI スレッド (録音停止時の警告フラッシュ等) からも呼ばれるパスがあるので、`_sendAsync` ラムダ内の `await _streamCall.RequestStream.WriteAsync(...)` は `ConfigureAwait(false)` を付けないと SynchronizationContext デッドロックになる。UI スレッドが `.GetResult()` でブロックしている間に await の継続が UI スレッドへポストされて永久に動かない、という典型的なパターン。

- **`OpusOggWriteStream` 構築直後に Ogg ヘッダ (OpusHead/OpusTags) が出力先 Stream に書き込まれる**
  これを利用して、録音 (`WaveInEvent.StartRecording`) 前に `ChunkForwardStream.Flush()` を 1 回呼んで gRPC ストリームを温めている。初回 `WriteAsync` が録音開始から数百 ms 遅れて発生すると Grpc.Core が状態を一時的に不安定にし、最初のチャンクが落ちるケースがあったため。

- **VAD と `Finish()` の順序**
  `VadGate.Flush(emit)` は `OpusOggWriteStream.WriteSamples` を呼ぶ。`OpusOggWriteStream.Finish()` 後は内部 Stream が Close 済みで WriteSamples が落ちるので、必ず `vadGate.Flush(...)` → `oggWriter.Finish()` の順に呼ぶこと。両レコーダの `OnRecordingStopped` でこの順序を守っている。

- **`WebRtcVadSharp` はネイティブ DLL を要求し AnyCPU では警告を出す**
  `WebRtcVadSharp.targets` が「プラットフォーム明示が必要」と警告し既定 x64 を使う。`Sample.Shared` および両クライアントの csproj に `<PlatformTarget>x64</PlatformTarget>` を明示。両クライアントは Shared 経由ではなく直接 `WebRtcVadSharp` を `PackageReference` する (ネイティブ `WebRtcVad.dll` を exe の `bin/` に確実にコピーさせるため)。

## 14. 非対象 (このサンプルで扱わないこと)

- 認証・認可
- TLS
- 複数録音の管理 / 一覧表示
- ノイズ抑制・エコーキャンセル (VAD のみ)
- メタデータ (録音日時タグ等) の永続化
- マルチクライアント排他制御
- 自動テスト (サンプルのため疎通確認は実機で行う)
