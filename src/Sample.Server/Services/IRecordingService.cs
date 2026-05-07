using MagicOnion;
using Sample.Shared.Dto;

namespace Sample.Server.Services;

public interface IRecordingService : IService<IRecordingService>
{
    Task<ClientStreamingResult<RecordingChunk, RecordingResult>> SaveStreaming();

    UnaryResult<RecordingResult> SaveUnary(SaveUnaryRequest request);

    UnaryResult<DownloadResult> Download(DownloadRequest request);
}
