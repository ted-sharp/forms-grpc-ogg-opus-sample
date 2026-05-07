using MessagePack;

namespace Sample.Shared.Dto
{
    [MessagePackObject]
    public class DownloadResult
    {
        [Key(0)]
        public bool Exists { get; set; }

        [Key(1)]
        public byte[]? OggOpusBytes { get; set; }
    }
}
