using MessagePack;

namespace Sample.Shared.Dto;

/// <summary>
/// Download の応答。サーバー上のファイルが存在すれば、その全バイトをそのまま返す。
/// </summary>
[MessagePackObject]
public class DownloadResult
{
    /// <summary>サーバーに録音ファイルが存在したか。<c>false</c> なら <see cref="OggOpusBytes"/> は空配列。</summary>
    [Key(0)]
    public bool Exists { get; set; }

    /// <summary>
    /// Ogg Opus 形式のファイル全体。クライアント側で <c>OpusOggReadStream</c> でデコードして再生する。
    /// 大きいファイルだと gRPC のメッセージサイズ上限に当たる可能性があるため、
    /// サーバー・クライアント双方で 64 MB に拡張している (Program.cs / RecordingClient.cs)。
    /// </summary>
    [Key(1)]
    public byte[]? OggOpusBytes { get; set; }
}
