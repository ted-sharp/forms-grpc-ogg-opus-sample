using MessagePack;

namespace Sample.Shared.Dto
{
    /// <summary>
    /// MagicOnion v4 クライアントは「引数 0 個」のメソッドを bin 8 で送るため、
    /// MagicOnion v7 サーバー側 (Nil 期待) と噛み合わずデシリアライズエラーになる。
    /// 引数を 1 つ以上持たせれば通常のオブジェクト・シリアライズ経路に乗るので、
    /// ダミーの DTO を導入してこれを回避する。
    /// </summary>
    [MessagePackObject]
    public class DownloadRequest
    {
        // 現状のサンプルでは未使用。単一ファイル前提のため。
        [Key(0)]
        public string? FileId { get; set; }
    }
}
