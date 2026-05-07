# forms-grpc-ogg-opus-sample 仕様書

## 1. 概要

NAudio で録音した音声を Concentus で Opus エンコードし、MagicOnion (gRPC) でサーバーへ送信、サーバー側で Ogg Opus ファイルとして保存するクラサバ構成のサンプルアプリケーション。クライアントから保存済みファイルを取得して再生・シーク操作も行う。

通信パターンの学習を目的に、ClientStreaming 版と Unary 版の 2 種類のクライアントを用意する (サーバーは共通)。

## 2. アーキテクチャ

```
┌─────────────────────────────┐         ┌───────────────────────────┐
│ Client (.NET Framework 4.8) │         │ Server (.NET 10)          │
│  Windows Forms              │         │  ASP.NET Core +           │
│  ┌────────────────────┐     │  HTTP/2 │  MagicOnion v7            │
│  │ NAudio 録音         │     │ ◄─────► │  ┌─────────────────────┐ │
│  │  ↓ PCM 16-bit       │     │  gRPC   │  │ IRecordingService   │ │
│  │ Concentus エンコード│     │         │  │  - SaveStreaming    │ │
│  │  ↓ Opus packets    │     │         │  │  - SaveUnary        │ │
│  │ MagicOnion v4       │     │         │  │  - Download         │ │
│  │  (Grpc.Core)       │     │         │  └─────────────────────┘ │
│  └────────────────────┘     │         │  ┌─────────────────────┐ │
│  ┌────────────────────┐     │         │  │ Concentus.Oggfile   │ │
│  │ NAudio 再生 (DL後)  │     │         │  │  → recording.opus   │ │
│  └────────────────────┘     │         │  └─────────────────────┘ │
└─────────────────────────────┘         └───────────────────────────┘
```

## 3. プロジェクト構成

```
forms-grpc-ogg-opus-sample/
├── Sample.sln
└── src/
    ├── Sample.Shared/                   netstandard2.0   サービス契約と DTO
    ├── Sample.Server/                   net10.0          MagicOnion v7 サーバー
    ├── Sample.Client.Streaming/         net48            ClientStreaming 版 WinForms
    └── Sample.Client.Unary/             net48            Unary 版 WinForms
```

### 3.1 ターゲットフレームワークとパッケージ

| プロジェクト | TFM | 主要パッケージ |
|---|---|---|
| `Sample.Shared` | `netstandard2.0` | `MagicOnion.Abstractions` 4.5.x, `MessagePack` 2.x |
| `Sample.Server` | `net10.0` | `MagicOnion.Server` 7.0.6, `Grpc.AspNetCore` 2.71.0 |
| `Sample.Client.Streaming` | `net48` | `MagicOnion.Client` 4.5.2, `Grpc.Core` 2.46.6, `NAudio` 2.2.1, `Concentus` 1.1.7, `Concentus.OggFile` 1.0.4 |
| `Sample.Client.Unary` | `net48` | 同上 |

注: `Concentus` は 1.x 系を使用 (2.x は API が `OpusCodecFactory` ベースに変わっており、`Concentus.OggFile` 1.0.4 と整合しないため)。

### 3.2 クロスバージョン通信に関する注記 (C案)

- クライアントは MagicOnion v4 (Grpc.Core ベース)、サーバーは MagicOnion v7 (Grpc.AspNetCore ベース)。これは公式に保証された組み合わせではない。
- Unary と ClientStreaming のみ使用し、StreamingHub は使わない (StreamingHub は v5 でハートビート仕様などが変更されたため非対称構成では避ける)。
- MessagePack のメジャーバージョンは v4/v7 とも 2.x で揃えること。
- もし通信エラーが発生する場合のフォールバック順:
  1. サーバー側 MagicOnion を v4 系に下げて Grpc.Core でホスト (B案)
  2. MagicOnion をやめて生 gRPC (`.proto` + `Grpc.Tools`) に切替 (D案)

### 3.3 共有ライブラリの方針

`Sample.Shared` は MagicOnion v4 の `MagicOnion.Abstractions` を参照する形で `netstandard2.0` でビルドし、両クライアント (NetFx 4.8 + MagicOnion v4) から DLL 参照する。

サーバー (.NET 10 + MagicOnion v7) は `Sample.Shared` を **DLL として参照しない**。理由: `MagicOnion.Abstractions` v4 と v7 で `ClientStreamingResult<,>` の `[AsyncMethodBuilder]` 属性の有無が異なり、同居させると `async ClientStreamingResult` が解決できない。

代わりにサーバーは:

- `Sample.Shared/Dto/*.cs` を **Compile Link** (`<Compile Include="..\Sample.Shared\Dto\*.cs"><Link>...</Link></Compile>`) でソース取り込み (DTO 定義と MessagePack 属性を共用)
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

## 5. ファイル管理

- サーバー側保存ファイル: 単一ファイルで上書き運用。
- 保存パス: `Sample.Server` 実行ディレクトリ配下の `recordings/recording.opus` (固定)。
- 録音実行のたびに上書きされる。バージョン管理・履歴は持たない。

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
    UnaryResult<DownloadResult> Download();
}
```

### 6.2 DTO

```csharp
[MessagePackObject]
public class RecordingChunk
{
    // クライアントで Ogg Opus 化済みのバイト列の一部分 (ストリーム上の連続したスライス)。
    // サーバーは届いた順に追記すれば最終的に妥当な Ogg Opus ファイルになる。
    [Key(0)] public byte[] OggOpusBytes { get; set; }
}

[MessagePackObject]
public class RecordingResult
{
    [Key(0)] public bool Success { get; set; }
    [Key(1)] public string SavedPath { get; set; }
    [Key(2)] public long DurationMs { get; set; }
    [Key(3)] public string ErrorMessage { get; set; }
}

[MessagePackObject]
public class SaveUnaryRequest
{
    // クライアントで Ogg Opus コンテナ化済みのファイル全体
    [Key(0)] public byte[] OggOpusBytes { get; set; }
}

[MessagePackObject]
public class DownloadResult
{
    [Key(0)] public bool Exists { get; set; }
    [Key(1)] public byte[] OggOpusBytes { get; set; }
    [Key(2)] public long DurationMs { get; set; }
}
```

### 6.3 パケット化方針

クライアント側で `OpusOggWriteStream` (Concentus.Oggfile) を用いて **Ogg Opus 形式までエンコードしてから送る**。サーバーは Opus/Ogg を一切意識せずバイト列をそのまま保存するだけ。

- **ClientStreaming 版**: 録音中に `OpusOggWriteStream` の出力先として「内部バッファに溜まった分を gRPC chunk として送るカスタム `Stream`」を渡す。`WriteSamples` のたびにオンザフライで Ogg Opus バイトが流れていく。
- **Unary 版**: `MemoryStream` を出力先にして録音終了まで Ogg Opus を組み立て、`MemoryStream.ToArray()` を Unary で一括送信。

この設計により、サーバー側は Ogg 構造を理解する必要がない (受信した byte をそのまま `recording.opus` に書くだけ)。

## 7. サーバー仕様 (`Sample.Server`)

### 7.1 ホスティング

- ASP.NET Core (`Microsoft.AspNetCore.App`) + Kestrel
- HTTP/2 平文 (h2c) で listen (TLS なし、サンプルのため)
- ポート: `http://0.0.0.0:5000` (固定)
- `MagicOnion.Server` のサービスを `MapMagicOnionService()` で登録

### 7.2 `RecordingService` 実装ポイント

- **`SaveStreaming`**:
  - `ClientStreamingContext<RecordingChunk, RecordingResult>` を取得
  - 出力先 `recordings/recording.opus` を `FileStream` (上書き) で開く
  - `MoveNext` ループで受信した各 `OggOpusBytes` を `FileStream.Write` でそのまま追記
  - クライアント側の入力ストリーム終端 (`MoveNext` が `false`) で `Finish` 相当 (FileStream Dispose)
  - `RecordingResult` に保存パスと推定継続時間を入れて返す
- **`SaveUnary`**:
  - 受信した `OggOpusBytes` をそのまま `recordings/recording.opus` に書く (上書き)
- **`Download`**:
  - 保存ファイルを読んで返す
  - メッセージサイズが gRPC 既定上限 (4 MB) を超える可能性があるため、サーバーの `MaxReceiveMessageSize` / `MaxSendMessageSize` を 64 MB 程度に設定する

### 7.3 サーバーは Opus/Ogg 非依存

§6.3 の方針により、サーバーは Concentus も `OpusOggWriteStream` も使わない。`SaveStreaming` も `SaveUnary` も「届いたバイトをファイルに書く」だけ。これにより v4 ↔ v7 の API 差や Ogg 仕様の検証ポイントを最小化する。

継続時間の算出は受信完了後に保存ファイルを `OpusOggReadStream` でなめて granule position から求めても良いが、サンプルでは省略し、クライアントが計測してリクエストに添えても良い (今回はサーバー側で計算しない)。

## 8. クライアント共通仕様 (`Sample.Client.Streaming` / `Sample.Client.Unary`)

### 8.1 UI (Windows Forms)

```
┌──────────────────────────────────────────┐
│  [● Record] [▶ Play] [⏸ Pause] [■ Stop]  │
│                                          │
│  ├──────●──────────────────────┤  00:12 / 00:34
│  (シークバー)                              (経過 / 全長)
│                                          │
│  状態: 待機中 / 録音中 / 再生中 / 一時停止  │
└──────────────────────────────────────────┘
```

| コントロール | 役割 |
|---|---|
| 録音ボタン (`btnRecord`) | NAudio 録音開始 / 停止トグル |
| 再生ボタン (`btnPlay`) | サーバーから DL → デコード → 再生開始 / 一時停止解除 |
| 一時停止ボタン (`btnPause`) | 再生中の `WaveOutEvent` を `Pause()` |
| 停止ボタン (`btnStop`) | 録音 or 再生を停止 |
| シークバー (`tbSeek`, `TrackBar`) | 再生位置の表示・操作。録音中は無効化 |
| 経過/全長ラベル (`lblTime`) | `mm:ss / mm:ss` 形式で再生時間を表示 |

### 8.2 録音処理 (両クライアント共通)

1. `WaveInEvent` を `WaveFormat = new WaveFormat(48000, 16, 1)` で初期化
2. `BufferMilliseconds = 20` (Opus フレームと一致させる)
3. `DataAvailable` で受け取った PCM (`byte[]`) を `short[]` に変換
4. `OpusEncoder.Encode(short[] pcm, 0, 960, byte[] outBuffer, 0, outBuffer.Length)` で 1 フレーム分エンコード
5. 出力した Opus パケット (frame) を後述のパスで送信
6. `RecordingStop` イベントで残バッファをフラッシュ

### 8.3 ClientStreaming 版送信パス (`Sample.Client.Streaming`)

- 録音開始時に `client.SaveStreaming()` を呼んで `ClientStreamingResult` を取得
- 出力先として `ChunkForwardStream`(独自実装の `Stream`) を生成。`Write(byte[],int,int)` 内で内部バッファに貯めて、一定サイズ (例: 16 KB) を超えたら `RequestStream.WriteAsync(new RecordingChunk{ OggOpusBytes = ... })` で送信
- `OpusOggWriteStream(encoder, chunkForwardStream)` を生成
- `WaveInEvent.DataAvailable` で来た PCM (`byte[]` → `short[]`) を `OpusOggWriteStream.WriteSamples` に流す
- 録音停止時:
  1. `OpusOggWriteStream.Finish()` (Ogg トレーラを書き、フラッシュ)
  2. `ChunkForwardStream.Flush()` で残バッファを送信
  3. `ClientStreamingResult.CompleteAsync()` でストリーム終端
  4. `ResponseAsync` で `RecordingResult` 受領

### 8.4 Unary 版送信パス (`Sample.Client.Unary`)

- 録音中: `MemoryStream` に対して `OpusOggWriteStream` で書き込み
- 録音停止時: `OpusOggWriteStream.Finish()` → `MemoryStream.ToArray()` を `SaveUnary` に渡す
- 単発の `UnaryResult<RecordingResult>` を await
- 録音時間が長いとメッセージが膨らむため、`MaxSendMessageSize` を 64 MB 等に設定

### 8.5 再生処理 (両クライアント共通)

1. `client.Download()` でサーバーから Ogg Opus を取得
2. `OpusOggReadStream` で Ogg をデコードしながら全 PCM (16-bit, 48kHz, mono) をメモリに展開
3. `MemoryStream` + `RawSourceWaveStream(stream, new WaveFormat(48000, 16, 1))` で `WaveStream` 化
4. `WaveOutEvent` で再生
5. シークバー操作 (`tbSeek.Scroll`) は `RawSourceWaveStream.Position = ...` で実現
   - `Position` は byte 単位。`bytesPerSecond = 48000 * 2 = 96000` を係数として位置を計算
6. 再生中は `Timer` (100ms 周期) で `Position` を読んでシークバーと時刻ラベルを更新
7. `PlaybackStopped` で再生完了処理 (シークバー先頭に戻すなど)

### 8.6 状態遷移

```
[待機中] ──録音ボタン──> [録音中] ──録音/停止ボタン──> [待機中]
   │
   └──再生ボタン──> (DL中) ──> [再生中] ⇄ [一時停止]  ──停止──> [待機中]
                                  │
                                  └──末尾到達──> [待機中]
```

排他: 録音中は再生ボタン無効、再生中は録音ボタン無効。

## 9. gRPC 設定

- `MaxReceiveMessageSize` / `MaxSendMessageSize`: 64 MB (`64 * 1024 * 1024`)
  - サーバー: `services.AddMagicOnion()` の channel options
  - クライアント: `Channel` 構築時の `ChannelOption`
- 平文 HTTP/2 (h2c)
  - サーバー: Kestrel `ListenAnyIP(5000, o => o.Protocols = HttpProtocols.Http2)`
  - クライアント: `new Channel("localhost", 5000, ChannelCredentials.Insecure)`

## 10. ディレクトリ構造 (実装時の目安)

```
src/
├── Sample.Shared/
│   ├── Sample.Shared.csproj
│   ├── IRecordingService.cs
│   └── Dto/
│       ├── RecordingChunk.cs
│       ├── RecordingResult.cs
│       ├── SaveUnaryRequest.cs
│       └── DownloadResult.cs
│
├── Sample.Server/
│   ├── Sample.Server.csproj          (Compile Link で ../Sample.Shared/Dto/*.cs を取り込む)
│   ├── Program.cs
│   ├── appsettings.json
│   ├── Services/
│   │   ├── IRecordingService.cs      (サーバーローカル定義: Task<ClientStreamingResult<,>>)
│   │   └── RecordingService.cs
│   └── Storage/
│       ├── IRecordingStore.cs
│       └── FileSystemRecordingStore.cs
│
├── Sample.Client.Streaming/
│   ├── Sample.Client.Streaming.csproj
│   ├── Program.cs
│   ├── MainForm.cs / MainForm.Designer.cs / MainForm.resx
│   ├── Audio/
│   │   ├── Recorder.cs              (NAudio + Concentus)
│   │   ├── Player.cs                (NAudio + Concentus.Oggfile)
│   │   └── OpusFrameBuffer.cs
│   └── Rpc/
│       └── RecordingClient.cs       (MagicOnion クライアント wrap)
│
└── Sample.Client.Unary/
    ├── Sample.Client.Unary.csproj
    ├── Program.cs
    ├── MainForm.cs / MainForm.Designer.cs / MainForm.resx
    ├── Audio/
    │   ├── Recorder.cs
    │   ├── Player.cs
    │   └── LocalOggOpusWriter.cs    (メモリ上に Ogg 組み立て)
    └── Rpc/
        └── RecordingClient.cs
```

## 11. 既知の課題・実装時に検証すべき項目

1. **MagicOnion v4 ↔ v7 のクロスバージョン互換性**
   実装初期に Unary 1 メソッドだけ通して疎通確認すること。失敗した場合は §3.2 のフォールバック手順へ。
2. **`OpusOggWriteStream` の出力先が任意の `Stream` を許容するか**
   ClientStreaming 版で重要。許容するはずだが、実装着手時に確認 (内部で `Seek` を要求しないこと)。要求する場合は `MemoryStream` をワンクッション挟んで定期フラッシュする形に変更する。
3. **NetFx 4.8 + `Grpc.Core` のサポート**
   `Grpc.Core` 2.46.x は EOL だが、NuGet からは取得可能。ビルド警告は無視する。
4. **シークバーの粒度**
   PCM 全展開方式のため長時間ファイルでメモリを食う。サンプルでは数分程度を上限と想定し、それ以上は対象外とする。
5. **NAudio の録音バッファサイズ**
   `BufferMilliseconds = 20` は最小に近い。OS によってはアンダーラン気味になるので、必要なら 40ms / 60ms に上げて受信側で 20ms フレームに再分割する。
6. **gRPC メッセージサイズ**
   再生用 `Download` で長時間ファイルが 4 MB を超える可能性。`MaxReceiveMessageSize` を上げるか、必要なら ServerStreaming に切り替えて分割送信する設計に変更。

## 11.1 実装で発見した事項 (建付けの根拠)

実装着手後に判明した事項。設計判断の理由として記録する。

- **`MagicOnion.Abstractions` v4 / v7 の互換性は保てない**
  v4 の `ClientStreamingResult<TReq, TRes>` は `[AsyncMethodBuilder]` 属性付きで `async` 戻り型として使えるが、v7 では同属性が外されている。共通 DLL を介した型共有は破綻するため、サーバーは Sample.Shared を DLL 参照せず、DTO のみソースリンクする構成にした (§3.3)。
- **MagicOnion v4 クライアントの ClientStreaming 起動は同期呼び出し**
  `client.SaveStreaming()` は `ClientStreamingResult<,>` を直接返す (await すると `[AsyncMethodBuilder]` の `GetAwaiter` 経由で `ResponseAsync` の結果型に化けて型エラーになる)。`var stream = client.SaveStreaming();` のように代入で受ける必要がある。
- **MagicOnion v7 サーバーの ClientStreaming 戻り型は `Task<ClientStreamingResult<,>>`**
  v7 の `ClientStreamingResult<,>` は task-like ではないため、`async ClientStreamingResult<,>` は書けない。`Task<>` で包む。
- **MagicOnion v7 の `ClientStreamingContext` には `ReadAllAsync` がない**
  `while (await ctx.MoveNext()) { var item = ctx.Current; ... }` のループで読む。
- **`Concentus.OggFile` 1.0.4 は `Concentus` 1.x 前提**
  `Concentus` 2.x は `OpusEncoder.Create` 静的メソッドを廃止し `OpusCodecFactory.CreateEncoder` に移行している。`Concentus.OggFile` 1.0.4 は 1.x の `OpusEncoder` クラスを直接受け取る API のため、整合性が取れる 1.1.7 を採用。
- **`.NET 10 SDK` のソリューションファイルは `.slnx` (XML 形式)**
  従来の `.sln` ではなく `Sample.slnx` として作成される。VS / dotnet CLI 双方が解釈可能。
- **`Concentus.Oggfile` の `OpusOggWriteStream.Finish()` は渡された Stream を `Close()` する**
  `leaveOpen` 相当のオプションがなく、`Finish()` の最後で `_outputStream.Close()` (= `Dispose`) を呼んでくる。Stream を引数で受け取るラッパー型としては作法から外れた挙動なので注意。本サンプルでは `ChunkForwardStream` がこれの影響を受けて、`Finish()` 直後に内部 `MemoryStream` が解放されてしまい、後続の `ChunkForwardStream.CompleteAsync()` で `MemoryStream.Length` を触って `ObjectDisposedException` → catch → `finally` の `CleanUp()` で `_streamCall.Dispose()` → gRPC コールが中断 → サーバー側 Kestrel に「The client reset the request stream (RST_STREAM)」として観測される、という連鎖を引き起こしていた。サーバーログだけ見ると「クライアントが切断した」としか読めず原因が見えにくいが、本当の原因はクライアント側の `ObjectDisposedException`。
  **対処**:
  1. `OpusOggWriteStream.Finish()` の中で既に `Flush()` まで走り、同期ブロックする `ChunkForwardStream.Flush()` 経由で gRPC `WriteAsync` も完了している。よって `Finish()` 後の追加 `CompleteAsync` 呼び出しは不要 (削除)。
  2. `ChunkForwardStream.Dispose()` で内部 `MemoryStream` を解放しないようにし、フラグだけ立てて GC 任せに変更。これで Concentus.Oggfile に勝手に Close されても二重 Flush で壊れない。
- **ClientStreaming 側はバックグラウンド・ポンプ・タスクではなく同期送信に**
  当初 `ChunkForwardStream` はバッファ溢れ時に `BlockingCollection<byte[]>` 経由で別タスクから `WriteAsync` する設計だったが、上記 Concentus.Oggfile の Close 問題と相まって停止時のタイミング起因の不安定さがあったため、ポンプを廃止し NAudio スレッドで `WriteAsync.GetAwaiter().GetResult()` で同期ブロックする形にした。各チャンクは数十 KB なのでサンプル用途では十分。
- **同期 `GetAwaiter().GetResult()` 経路では `ConfigureAwait(false)` 必須**
  ChunkForwardStream の同期送信は UI スレッド (録音停止時) からも呼ばれるパスがあるので、`_sendAsync` ラムダ内の `await _streamCall.RequestStream.WriteAsync(...)` は `ConfigureAwait(false)` を付けないと SynchronizationContext デッドロックになる。UI スレッドが `.GetResult()` でブロックしている間に、await の継続が UI スレッドへポストされて永久に動かない、という典型的なパターン。
- **MagicOnion v4 ↔ v7 では「引数 0 個 Unary」のワイヤーフォーマットが非互換**
  v4 クライアントは引数なしメソッドのリクエストを `bin 8` (空 byte 配列, msgpack code 0xC4) として送るが、v7 サーバーは `Nil` (0xC0) として読もうとして `MessagePackSerializationException: Unexpected msgpack code 196` で失敗する。**回避策: 引数 0 個メソッドにダミー DTO 引数を持たせる**。本サンプルでは `Download()` → `Download(DownloadRequest request)` に変更して回避。`SaveStreaming()` (ClientStreaming) は最初に request メッセージを送らないため影響なし。`SaveUnary` は元から引数ありなので影響なし。

## 12. 非対象 (このサンプルで扱わないこと)

- 認証・認可
- TLS
- 複数録音の管理 / 一覧表示
- ノイズ抑制・エコーキャンセル
- メタデータ (録音日時タグ等) の永続化
- マルチクライアント排他制御
