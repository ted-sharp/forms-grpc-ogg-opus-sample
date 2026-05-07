using MessagePack;

namespace Sample.Shared.Dto
{
    [MessagePackObject]
    public class RecordingChunk
    {
        [Key(0)]
        public byte[]? OggOpusBytes { get; set; }
    }
}
