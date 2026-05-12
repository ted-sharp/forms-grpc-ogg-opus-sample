using MessagePack;

namespace Sample.Shared.Dto;

/// <summary>
/// ClientStreaming で 1 メッセージ単位として送信される録音データ片。
/// 中身は既に Ogg Opus 形式に変換済みのバイト列で、サーバーはこれを解釈せず
/// そのままファイルへ追記するだけ (Opus/Ogg の知識をクライアント側に閉じ込める設計)。
/// </summary>
[MessagePackObject]
public class RecordingChunk
{
    /// <summary>
    /// Ogg Opus 形式のバイト列の一部分。Streaming では複数チャンクに分割されて連続送信される。
    /// 1 つのチャンクが Ogg ページ単位とは限らない (ChunkForwardStream のしきい値で区切られる)。
    /// </summary>
    [Key(0)]
    public byte[]? OggOpusBytes { get; set; }
}
