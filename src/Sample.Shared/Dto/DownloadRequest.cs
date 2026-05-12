using MessagePack;

namespace Sample.Shared.Dto;

/// <summary>
/// Download のダミー入力 DTO。単一ファイル前提のサンプルでは中身を使っていないが、
/// 「引数 0 個の Unary」は MagicOnion / MessagePack の組合せで踏みやすい地雷なので、
/// 念のため 1 個以上のフィールドを持つ DTO を引数に取らせている。将来 FileId 等を
/// 渡したくなったときに拡張する場所。
/// </summary>
[MessagePackObject]
public class DownloadRequest
{
    [Key(0)]
    public string? FileId { get; set; }
}
