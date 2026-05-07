using MessagePack;

namespace Sample.Shared.Dto
{
    [MessagePackObject]
    public class RecordingResult
    {
        [Key(0)]
        public bool Success { get; set; }

        [Key(1)]
        public string? SavedPath { get; set; }

        [Key(2)]
        public long ByteSize { get; set; }

        [Key(3)]
        public string? ErrorMessage { get; set; }
    }
}
