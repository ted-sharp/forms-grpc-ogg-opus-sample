using MagicOnion;
using Sample.Shared.Dto;

namespace Sample.Shared
{
    public interface IRecordingService : IService<IRecordingService>
    {
        ClientStreamingResult<RecordingChunk, RecordingResult> SaveStreaming();

        UnaryResult<RecordingResult> SaveUnary(SaveUnaryRequest request);

        UnaryResult<DownloadResult> Download(DownloadRequest request);
    }
}
