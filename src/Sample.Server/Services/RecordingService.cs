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
        var streamingContext = GetClientStreamingContext<RecordingChunk, RecordingResult>();

        long total = 0;
        try
        {
            await using (var output = _store.OpenWrite())
            {
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
        _ = request;
        var bytes = await _store.ReadAllAsync();
        return new DownloadResult
        {
            Exists = bytes is { Length: > 0 },
            OggOpusBytes = bytes ?? Array.Empty<byte>(),
        };
    }
}
