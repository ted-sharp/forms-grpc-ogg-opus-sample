namespace Sample.Shared
{
    /// <summary>
    /// このサンプル全体で固定しているオーディオパラメータ。
    /// 「48 kHz / 16-bit signed LE / モノラル / 20 ms フレーム」を前提に、
    /// NAudio の録音バッファ・Concentus の Opus エンコーダ・サーバーへの送信単位を
    /// 揃えている。値を変える場合はこの 4 箇所すべての整合を取る必要がある。
    /// </summary>
    public static class AudioConstants
    {
        /// <summary>サンプリング周波数 (Hz)。Opus がフルバンドを扱える 48 kHz に固定。</summary>
        public const int SampleRate = 48000;

        /// <summary>チャネル数。サンプル簡略化のためモノラル固定。</summary>
        public const int Channels = 1;

        /// <summary>1 サンプルのビット数。NAudio の WaveFormat も Concentus の入力もこれに合わせる。</summary>
        public const int BitsPerSample = 16;

        /// <summary>
        /// 1 フレームあたりのミリ秒。Opus が扱える代表的な値 (2.5/5/10/20/40/60 ms) のうち、
        /// VOIP 用途で標準的な 20 ms を採用。NAudio の <c>BufferMilliseconds</c> もこれと一致させて、
        /// 「録音バッファ 1 つ = Opus フレーム 1 つ」になるようにしている。
        /// </summary>
        public const int FrameMilliseconds = 20;

        /// <summary>
        /// 1 フレームあたりのサンプル数 (1 チャネル分)。
        /// 48000 Hz × 20 ms ÷ 1000 = <b>960 サンプル</b>。
        /// この値は Opus エンコーダに渡すフレームサイズと一致しなければならない。
        /// </summary>
        public const int FrameSizePerChannel = SampleRate * FrameMilliseconds / 1000;

        /// <summary>1 サンプルのバイト数 (= 16 bit ÷ 8 = 2 byte)。PCM バイト列とサンプル数の換算に使う。</summary>
        public const int BytesPerSample = BitsPerSample / 8;

        /// <summary>
        /// 1 秒あたりの PCM バイト数 (= 48000 × 1ch × 2 byte = 96000 byte/s)。
        /// 再生時にバイトオフセットから経過秒数を求める際の除数として使う (Player のシークバー計算)。
        /// </summary>
        public const int BytesPerSecond = SampleRate * Channels * BytesPerSample;

        /// <summary>Opus エンコーダのビットレート (bps)。VOIP 用途で十分な 64 kbps に固定。</summary>
        public const int BitRate = 64000;
    }
}
