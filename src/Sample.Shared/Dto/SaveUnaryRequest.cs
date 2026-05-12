using MessagePack;

namespace Sample.Shared.Dto;

[MessagePackObject]
public class SaveUnaryRequest
{
    [Key(0)]
    public byte[]? OggOpusBytes { get; set; }
}
