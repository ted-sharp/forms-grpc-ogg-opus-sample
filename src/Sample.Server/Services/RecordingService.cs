using MagicOnion;
using MagicOnion.Server;
using Sample.Server.Storage;
using Sample.Shared;
using Sample.Shared.Dto;

namespace Sample.Server.Services;

public class RecordingService : ServiceBase<IRecordingService>, IRecordingService
{
    private readonly IRecordingStore _store;
    private readonly ILogger<RecordingService> _logger;

    public RecordingService(IRecordingStore store, ILogger<RecordingService> logger)
    {
        this._store = store;
        this._logger = logger;
    }

    public async Task<ClientStreamingResult<RecordingChunk, RecordingResult>> SaveStreaming()
    {
        // GetClientStreamingContext は MagicOnion v7 が提供する「クライアントからのストリームを受け取り始める」合図。
        // これを取得した時点で gRPC レイヤーが受信状態に入る。
        var streamingContext = this.GetClientStreamingContext<RecordingChunk, RecordingResult>();

        long total = 0;
        try
        {
            // サーバーは Opus も Ogg も解釈しない。受け取った byte[] をそのままファイルへ追記するだけ。
            // Opus/Ogg の知識はクライアント側に閉じ込めて、サーバー側ロジックを単純化している。
            await using (var output = this._store.OpenWrite())
            {
                // MoveNext が false を返すまで Current にチャンクが入ってくる。
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

            this._logger.LogInformation("SaveStreaming: wrote {Bytes} bytes to {Path}", total, this._store.SavedPath);

            return streamingContext.Result(new RecordingResult
            {
                Success = true,
                SavedPath = this._store.SavedPath,
                ByteSize = total,
            });
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "SaveStreaming failed");
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
            await this._store.WriteAllAsync(bytes);

            this._logger.LogInformation("SaveUnary: wrote {Bytes} bytes to {Path}", bytes.Length, this._store.SavedPath);

            return new RecordingResult
            {
                Success = true,
                SavedPath = this._store.SavedPath,
                ByteSize = bytes.Length,
            };
        }
        catch (Exception ex)
        {
            this._logger.LogError(ex, "SaveUnary failed");
            return new RecordingResult
            {
                Success = false,
                ErrorMessage = ex.Message,
            };
        }
    }

    public async UnaryResult<DownloadResult> Download(DownloadRequest request)
    {
        // request.FileId は単一ファイル前提のサンプルなので未使用。引数自体は将来の拡張余地。
        _ = request;
        var bytes = await this._store.ReadAllAsync();
        return new DownloadResult
        {
            Exists = bytes is { Length: > 0 },
            OggOpusBytes = bytes ?? Array.Empty<byte>(),
        };
    }
}
