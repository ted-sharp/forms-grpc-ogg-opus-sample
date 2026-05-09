using MagicOnion;
using MagicOnion.Server;
using Sample.Shared.Dto;
using Sample.Server.Storage;

namespace Sample.Server.Services;

public class RecordingService : ServiceBase<IRecordingService>, IRecordingService
{
    private readonly IRecordingStore _store;
    private readonly ILogger<RecordingService> _logger;

    public RecordingService(IRecordingStore store, ILogger<RecordingService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<ClientStreamingResult<RecordingChunk, RecordingResult>> SaveStreaming()
    {
        // GetClientStreamingContext は MagicOnion v7 が提供する「クライアントからのストリームを受け取り始める」合図。
        // これを取得した時点で gRPC レイヤーが受信状態に入る。
        var streamingContext = GetClientStreamingContext<RecordingChunk, RecordingResult>();

        long total = 0;
        try
        {
            // サーバーは Opus も Ogg も解釈しない。受け取った byte[] をそのままファイルへ追記するだけ。
            // これは v4↔v7 の API 差異を最小化するための意図的な設計 (CLAUDE.md 参照)。
            await using (var output = _store.OpenWrite())
            {
                // v7 の受信ループ: MoveNext が false を返すまで Current にチャンクが入ってくる。
                // (v4 にあった ReadAllAsync は v7 で廃止されているのでこの形で書く)
                while (await streamingContext.MoveNext())
                {
                    var chunk = streamingContext.Current;
                    if (chunk?.OggOpusBytes is { Length: > 0 } bytes)
                    {
                        await output.WriteAsync(bytes, 0, bytes.Length);
                        total += bytes.Length;
                    }
                }
            }

            _logger.LogInformation("SaveStreaming: wrote {Bytes} bytes to {Path}", total, _store.SavedPath);

            return streamingContext.Result(new RecordingResult
            {
                Success = true,
                SavedPath = _store.SavedPath,
                ByteSize = total,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SaveStreaming failed");
            return streamingContext.Result(new RecordingResult
            {
                Success = false,
                ErrorMessage = ex.Message,
            });
        }
    }

    public async UnaryResult<RecordingResult> SaveUnary(SaveUnaryRequest request)
    {
        try
        {
            var bytes = request?.OggOpusBytes ?? Array.Empty<byte>();
            await _store.WriteAllAsync(bytes);

            _logger.LogInformation("SaveUnary: wrote {Bytes} bytes to {Path}", bytes.Length, _store.SavedPath);

            return new RecordingResult
            {
                Success = true,
                SavedPath = _store.SavedPath,
                ByteSize = bytes.Length,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SaveUnary failed");
            return new RecordingResult
            {
                Success = false,
                ErrorMessage = ex.Message,
            };
        }
    }

    public async UnaryResult<DownloadResult> Download(DownloadRequest request)
    {
        // request.FileId は単一ファイル前提のサンプルなので未使用。
        // 引数自体は「v4 クライアントが空引数 Unary を bin8 で送って v7 サーバー側のデシリアライズが
        // 失敗する」問題を避けるためのダミーで、省略はできない。詳細は DownloadRequest 参照。
        _ = request;
        var bytes = await _store.ReadAllAsync();
        return new DownloadResult
        {
            Exists = bytes is { Length: > 0 },
            OggOpusBytes = bytes ?? Array.Empty<byte>(),
        };
    }
}
