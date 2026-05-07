# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## プロジェクトの位置付け

学習用のクラサバ構成サンプル。NAudio で録音した PCM を Concentus で Ogg Opus 化し、MagicOnion (gRPC) でサーバーに送って単一ファイル `recordings/recording.opus` として保存する。同じサーバーに対して **ClientStreaming 版**と **Unary 版**の 2 種類の WinForms クライアントが用意されており、通信パターンの差を体感することが目的。`SPEC.md` が一次資料で、設計の根拠と既知の地雷が網羅されている。コードに手を入れる前に必ず参照すること。

## ビルドと実行

ソリューションは `Sample.slnx` (XML 形式の新ソリューションファイル)。`.sln` ではない。

```powershell
# 全体ビルド
dotnet build Sample.slnx

# サーバー起動 (h2c, http://0.0.0.0:5000 で listen)
dotnet run --project src/Sample.Server/Sample.Server.csproj
# Properties/launchSettings.json の applicationUrl は無視され、Program.cs の ListenAnyIP(5000) が優先される

# クライアント (Windows Forms, net48) はサーバー起動後に別プロセスで実行
dotnet run --project src/Sample.Client.Streaming/Sample.Client.Streaming.csproj
dotnet run --project src/Sample.Client.Unary/Sample.Client.Unary.csproj
```

VS の SwitchStartupProject 拡張を使うなら `Sample.slnx.startup.json` に `Server + Client(Streaming)` / `Server + Client(Unary)` の複数スタートアップ構成が定義済み。

テストプロジェクトは存在しない (サンプルなので疎通確認は実機で行う)。

## アーキテクチャ上の重要な決定事項

### TFM とパッケージのバージョン構成 (動かすうえでクリティカル)

| プロジェクト | TFM | MagicOnion | 備考 |
|---|---|---|---|
| `Sample.Shared` | `netstandard2.0` | `Abstractions` 4.5.2 | 両クライアントから参照される |
| `Sample.Server` | `net10.0` | `Server` 7.0.6 | `Sample.Shared` を **DLL 参照しない** |
| `Sample.Client.Streaming` / `Sample.Client.Unary` | `net48` | `Client` 4.5.2 + `Grpc.Core` 2.46.6 | `NAudio` 2.2.1, `Concentus` 1.1.7, `Concentus.OggFile` 1.0.4 |

- **Concentus は 1.x 固定**。2.x は `OpusCodecFactory` ベースに変わっており `Concentus.OggFile` 1.0.4 と整合しない。
- **MessagePack は v4 / v7 とも 2.x で揃える**。
- **`Grpc.Core` 2.46.x は EOL** だが net48 用にこれを使う。NuGet 警告は無視する方針。

### MagicOnion v4 ↔ v7 のクロスバージョン構成 (本サンプルの肝)

クライアント (v4) とサーバー (v7) は公式にサポートされた組み合わせではない。動かすために以下の制約が入っている:

1. **`Sample.Shared` をサーバーから DLL 参照しない**。`Sample.Server.csproj` は `Sample.Shared/Dto/*.cs` のみ `<Compile Include="..\Sample.Shared\Dto\*.cs">` で **ソースリンク**で取り込む。理由: `MagicOnion.Abstractions` v4 / v7 で `ClientStreamingResult<,>` の `[AsyncMethodBuilder]` 属性が異なり、両方 DLL に居ると `async ClientStreamingResult` の解決が壊れる。
2. **サーバー側 `IRecordingService` は `Sample.Server.Services` 名前空間にローカル再定義**する (`src/Sample.Server/Services/IRecordingService.cs`)。シグネチャは v7 風で `Task<ClientStreamingResult<,>>` を返す。クライアント側 (`Sample.Shared.IRecordingService`) は v4 風で `ClientStreamingResult<,>` を直接返す。gRPC のメソッド名解決は型名 `IRecordingService` + メソッド名で行われるので名前空間が違っても疎通する。
3. **StreamingHub は使わない**。v5 以降でハートビート仕様が変わっているため、v4↔v7 の非対称構成では避ける。Unary と ClientStreaming のみ。
4. **引数 0 個の Unary は禁止**。v4 クライアントは `bin 8` (空 byte 配列, 0xC4) を送るが v7 サーバーは `Nil` (0xC0) を期待し `MessagePackSerializationException: Unexpected msgpack code 196` で落ちる。新規メソッドを追加する際は必ずダミー DTO を引数に取らせる。`Download(DownloadRequest)` はこの理由で引数を持たされている。

通信に問題が出たときのフォールバック順 (SPEC §3.2): サーバーを v4 に下げる → MagicOnion をやめて生 gRPC + `.proto`。

### Ogg Opus の境界 (誰が何を知っているか)

- **クライアント側で完結**: NAudio (PCM) → `OpusEncoder` → `OpusOggWriteStream` で **Ogg Opus コンテナまで作って**サーバーに送る。
- **サーバーは Opus も Ogg も知らない**。受信した `byte[]` をそのまま `recordings/recording.opus` に書き込むだけ。`Sample.Server` には Concentus 依存も無い。これは v4↔v7 の API 差異を最小化するための意図的な設計。
- ストリーミング版は `ChunkForwardStream` (独自 `Stream` 実装) を `OpusOggWriteStream` の出力先に渡し、`Write` 内で gRPC `RequestStream.WriteAsync` に転送する。Unary 版は `MemoryStream` に組み立ててから一括送信。

### ClientStreaming 実装で踏み抜いた地雷 (既に対処済み — 触るときは気をつけること)

`SPEC.md §11.1` に詳細あり。要約:

- **`OpusOggWriteStream.Finish()` は渡された Stream を `Close()` してくる** (`leaveOpen` オプションなし)。`ChunkForwardStream.Dispose()` で内部 `MemoryStream` を解放しない実装にしてあるのはこのため。GC 任せでよい。
- **`Finish()` 後に追加で `CompleteAsync` を呼ばないこと**。`Finish()` 内で同期 `Flush()` まで完了している。二重実行すると `ObjectDisposedException` → gRPC ストリームが `RST_STREAM` で異常終了してサーバー側に「クライアントが切断した」とだけ見えてデバッグが地獄になる。
- **MagicOnion v4 の ClientStreaming 起動は同期**: `var stream = client.SaveStreaming();` のように代入で受ける。`await` すると `[AsyncMethodBuilder]` 経由で型が `RecordingResult` に化けてコンパイルエラーになる。
- **MagicOnion v7 サーバーの ClientStreaming は `Task<ClientStreamingResult<,>>` 戻り値**。v7 の `ClientStreamingResult<,>` は task-like ではないので `async ClientStreamingResult<,>` は書けない。
- **v7 の `ClientStreamingContext` には `ReadAllAsync` がない**。`while (await ctx.MoveNext()) { var item = ctx.Current; ... }` で読む。
- **`ChunkForwardStream` の同期送信パスは `ConfigureAwait(false)` 必須**。UI スレッドが `GetAwaiter().GetResult()` でブロックしているところに await の継続を UI スレッドへポストすると典型的なデッドロック。

## 固定オーディオパラメータ

48 kHz / 16-bit signed LE / mono / 20 ms フレーム (= 960 サンプル = 1920 byte) / VOIP モード / 64 kbps VBR。NAudio の `BufferMilliseconds = 20` で Opus フレームと一致させている。これらはコード全体に散在しているが共通定数は `src/Sample.Shared/AudioConstants.cs` にある。

## サーバーの実行時挙動

- **CWD 固定**: `Program.cs` 冒頭で `Environment.CurrentDirectory = AppContext.BaseDirectory;` している。`dotnet run` でプロジェクトディレクトリから起動しても、`bin\Debug\net10.0\` を起点に `recordings\` を作る。`appsettings.json` の `Recording.Directory` (既定 `recordings`) と `FileName` (既定 `recording.opus`) はこの位置から相対解決される。
- gRPC `MaxReceiveMessageSize` / `MaxSendMessageSize` はサーバー・クライアント双方で **64 MB**。`Download` で長時間ファイルを返すケースに備えて。
- 平文 HTTP/2 (h2c)。TLS なし。サンプル用途のみ。
