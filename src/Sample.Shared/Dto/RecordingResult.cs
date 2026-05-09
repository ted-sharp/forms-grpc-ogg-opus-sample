using MessagePack;

namespace Sample.Shared.Dto
{
    /// <summary>
    /// SaveStreaming / SaveUnary の応答。サーバーでの保存結果を返す。
    /// </summary>
    [MessagePackObject]
    public class RecordingResult
    {
        /// <summary>保存に成功したか。失敗時は <see cref="ErrorMessage"/> を参照。</summary>
        [Key(0)]
        public bool Success { get; set; }

        /// <summary>保存先ファイルの絶対パス (例: <c>C:\...\recordings\recording.opus</c>)。</summary>
        [Key(1)]
        public string? SavedPath { get; set; }

        /// <summary>
        /// 受信した Ogg Opus バイト数の合計。
        /// Streaming の場合は全チャンクの合算、Unary の場合は単一メッセージの長さ。
        /// </summary>
        [Key(2)]
        public long ByteSize { get; set; }

        /// <summary>失敗時のエラーメッセージ。<see cref="Success"/> が <c>false</c> のときのみ意味を持つ。</summary>
        [Key(3)]
        public string? ErrorMessage { get; set; }
    }
}
