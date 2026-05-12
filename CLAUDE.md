# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## プロジェクトの位置付け

学習用のクラサバ構成サンプル。NAudio で録音した PCM を Concentus で Ogg Opus 化し、MagicOnion (gRPC) でサーバーに送って単一ファイル `recordings/recording.opus` として保存する。サーバー・クライアント・共有ライブラリすべて **.NET 10 / MagicOnion v7** で揃え、クライアントは **WPF**。同じサーバーに対して **ClientStreaming 版**と **Unary 版**の 2 種類の WPF クライアントが用意されており、通信パターンの差を体感することが目的。さらに保存済みファイルを STT (sherpa-onnx / Azure Speech) で文字起こしする 3 つ目のクライアントを同梱している。`SPEC.md` が一次資料で、設計の根拠と既知の地雷が網羅されている。コードに手を入れる前に必ず参照すること。

## ビルドと実行

ソリューションは `Sample.slnx` (XML 形式の新ソリューションファイル)。`.sln` ではない。

```powershell
# 全体ビルド
dotnet build Sample.slnx

# サーバー起動 (h2c, http://0.0.0.0:5000 で listen)
dotnet run --project src/Sample.Server/Sample.Server.csproj
# Properties/launchSettings.json の applicationUrl は無視され、Program.cs の ListenAnyIP(5000) が優先される

# クライアント (WPF, net10.0-windows) はサーバー起動後に別プロセスで実行
dotnet run --project src/Sample.Client.Streaming/Sample.Client.Streaming.csproj
dotnet run --project src/Sample.Client.Unary/Sample.Client.Unary.csproj
dotnet run --project src/Sample.Client.Stt/Sample.Client.Stt.csproj
```

VS の SwitchStartupProject 拡張を使うなら `Sample.slnx.startup.json` に `Server + Client(Streaming)` / `Server + Client(Unary)` / `Server + Client(Stt)` の複数スタートアップ構成が定義済み。

テストプロジェクトは存在しない (サンプルなので疎通確認は実機で行う)。

## アーキテクチャ上の重要な決定事項

### TFM とパッケージのバージョン構成

| プロジェクト | TFM | MagicOnion | gRPC ランタイム | 備考 |
|---|---|---|---|---|
| `Sample.Shared` | `net10.0` | `Abstractions` 7.0.6 | — | `MessagePack` 3.1.1, `WebRtcVadSharp` 1.3.2 |
| `Sample.Server` | `net10.0` | `Server` 7.0.6 | `Grpc.AspNetCore` 2.71.0 | `Sample.Shared` を ProjectReference |
| `Sample.Client.Streaming` / `Sample.Client.Unary` | `net10.0-windows` (WPF) | `Client` 7.0.6 | `Grpc.Net.Client` 2.71.0 | `NAudio` 2.2.1, `Concentus` 1.1.7, `Concentus.OggFile` 1.0.4 |
| `Sample.Client.Stt` | `net10.0-windows` (WPF) | `Client` 7.0.6 | `Grpc.Net.Client` 2.71.0 | 上記 + `org.k2fsa.sherpa.onnx` 1.13.0, `Microsoft.CognitiveServices.Speech` 1.49.1 |

- **Concentus は 1.x 固定**。2.x は `OpusCodecFactory` ベースに変わっており `Concentus.OggFile` 1.0.4 と整合しない。
- **MessagePack は 3.x**。MagicOnion 7.0.6 が `MessagePack >= 3.1.1` を要求する。
- ネイティブ DLL を要求するパッケージ (`WebRtcVadSharp`, `org.k2fsa.sherpa.onnx`, Azure Speech, NAudio の一部) がいずれも x64 のみなので、全クライアントは `<PlatformTarget>x64</PlatformTarget>` を明示。

### 統一構成の利点

MagicOnion v7 でサーバー・クライアント・共有 DLL を揃えているので、過去にあった v4↔v7 クロスバージョン問題 (Abstractions の `[AsyncMethodBuilder]` 差異、引数 0 個 Unary のワイヤーフォーマット差異など) は無い。`Sample.Shared.IRecordingService` をそのまま両端で使えるし、DTO 定義も `ProjectReference` 1 本で共有できる。

### Ogg Opus の境界 (誰が何を知っているか)

- **クライアント側で完結**: NAudio (PCM) → `OpusEncoder` → `OpusOggWriteStream` で **Ogg Opus コンテナまで作って**サーバーに送る。
- **サーバーは Opus も Ogg も知らない**。受信した `byte[]` をそのまま `recordings/recording.opus` に書き込むだけ。`Sample.Server` には Concentus 依存も無い。サンプルとして「サービスの責務を切る場所」を見せる意図的な設計。
- ストリーミング版は `ChunkForwardStream` (独自 `Stream` 実装) を `OpusOggWriteStream` の出力先に渡し、`Write` 内で gRPC `RequestStream.WriteAsync` に転送する。Unary 版は `MemoryStream` に組み立ててから一括送信。

### h2c (平文 HTTP/2) 接続

クライアント側は `Grpc.Net.Client` の `GrpcChannel.ForAddress("http://host:5000", ...)` で接続。`SocketsHttpHandler` は既定で TLS なしの HTTP/2 を拒否するので、各クライアントの `App.xaml.cs` コンストラクタで:

```csharp
AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
```

を有効化している。サンプル用途専用 — 本番は必ず TLS を付けてこのスイッチを外すこと。

### ClientStreaming 実装で踏み抜いた地雷 (既に対処済み — 触るときは気をつけること)

`SPEC.md §13` に詳細あり。要約:

- **`OpusOggWriteStream.Finish()` は渡された Stream を `Close()` してくる** (`leaveOpen` オプションなし)。`ChunkForwardStream.Dispose()` で内部 `MemoryStream` を解放しない実装にしてあるのはこのため。GC 任せでよい。
- **`Finish()` 後に追加で `CompleteAsync` を呼ばないこと**。`Finish()` 内で同期 `Flush()` まで完了している。二重実行すると `ObjectDisposedException` → gRPC ストリームが `RST_STREAM` で異常終了してサーバー側に「クライアントが切断した」とだけ見えてデバッグが地獄になる。ただし **gRPC レイヤの `ctx.RequestStream.CompleteAsync()` は別物で必須** (END_STREAM 送出)。両者は名前が似ているので混同しないこと。
- **MagicOnion v7 クライアントの ClientStreaming 起動は非同期**: `var ctx = await client.SaveStreaming();` で `ClientStreamingResult<,>` を受ける。
- **v7 の `ClientStreamingContext` には `ReadAllAsync` がない**。`while (await ctx.MoveNext()) { var item = ctx.Current; ... }` で読む。
- **`ChunkForwardStream` の同期送信パスは `ConfigureAwait(false)` 必須**。UI スレッドが `GetAwaiter().GetResult()` でブロックしているところに await の継続を UI スレッドへポストすると典型的なデッドロック。

## 固定オーディオパラメータ

48 kHz / 16-bit signed LE / mono / 20 ms フレーム (= 960 サンプル = 1920 byte) / VOIP モード / 64 kbps VBR。NAudio の `BufferMilliseconds = 20` で Opus フレームと一致させている。これらはコード全体に散在しているが共通定数は `src/Sample.Shared/AudioConstants.cs` にある。

## サーバーの実行時挙動

- **CWD 固定**: `Program.cs` 冒頭で `Environment.CurrentDirectory = AppContext.BaseDirectory;` している。`dotnet run` でプロジェクトディレクトリから起動しても、`bin\Debug\net10.0\` を起点に `recordings\` を作る。`appsettings.json` の `Recording.Directory` (既定 `recordings`) と `FileName` (既定 `recording.opus`) はこの位置から相対解決される。
- gRPC `MaxReceiveMessageSize` / `MaxSendMessageSize` はサーバー・クライアント双方で **64 MB**。`Download` で長時間ファイルを返すケースに備えて。
- 平文 HTTP/2 (h2c)。TLS なし。サンプル用途のみ。
