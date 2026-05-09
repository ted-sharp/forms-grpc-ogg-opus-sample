using System;
using WebRtcVadSharp;

namespace Sample.Shared.Audio
{
    /// <summary>
    /// WebRTC VAD で 20 ms (= 960 samples @ 48 kHz mono) フレームごとの voice/non-voice 判定を行い、
    /// プリロール / トリガー / ハングオーバーの状態機械を通して voice 区間だけ呼び出し側に流す。
    /// 入力は任意サンプル数で呼んでよい。内部で 960 サンプル境界に整列する。
    /// </summary>
    public sealed class VadGate : IDisposable
    {
        private const int Frame = AudioConstants.FrameSizePerChannel; // 960
        private const int TriggerFrames = 3;   // 60 ms 連続 voice で開く
        private const int HangoverFrames = 10; // voice が途切れても 200 ms 出力を続ける
        private const int PrerollFrames = 5;   // 開く瞬間に直近 100 ms をまとめて吐く

        private readonly WebRtcVad _vad;
        private readonly short[] _accum = new short[Frame];
        private int _accumLen;

        private readonly short[][] _preroll;
        private int _prerollHead;
        private int _prerollCount;

        private bool _isOpen;
        private int _voiceRun;
        private int _hangover;

        public VadGate(int aggressiveness)
        {
            if (aggressiveness < 0) aggressiveness = 0;
            if (aggressiveness > 3) aggressiveness = 3;

            this._vad = new WebRtcVad
            {
                OperatingMode = (OperatingMode)aggressiveness,
                SampleRate = SampleRate.Is48kHz,
                FrameLength = FrameLength.Is20ms,
            };

            this._preroll = new short[PrerollFrames][];
            for (int i = 0; i < PrerollFrames; i++) this._preroll[i] = new short[Frame];
        }

        /// <summary>
        /// PCM samples を投入する。voice として通過したフレームは <paramref name="emit"/> に
        /// (buffer, sampleCount) の形で渡される。emit 内ではバッファを即時消費すること
        /// (戻った直後にバッファは別用途で書き換えられる可能性がある)。
        /// </summary>
        public void Process(short[] input, int count, Action<short[], int> emit)
        {
            int offset = 0;
            while (offset < count)
            {
                int copy = Frame - this._accumLen;
                if (copy > count - offset) copy = count - offset;
                Buffer.BlockCopy(input, offset * sizeof(short), this._accum, this._accumLen * sizeof(short), copy * sizeof(short));
                this._accumLen += copy;
                offset += copy;

                if (this._accumLen == Frame)
                {
                    this.ProcessFrame(this._accum, emit);
                    this._accumLen = 0;
                }
            }
        }

        /// <summary>
        /// 録音停止時に呼ぶ。Open 状態で 960 未満の端数が残っていればそれだけ吐く。
        /// Closed 状態で残っているプリロールバッファは「開かなかった末尾の無音」として捨てる。
        /// </summary>
        public void Flush(Action<short[], int> emit)
        {
            if (this._isOpen && this._accumLen > 0)
            {
                emit(this._accum, this._accumLen);
                this._accumLen = 0;
            }
        }

        private void ProcessFrame(short[] frame, Action<short[], int> emit)
        {
            bool isVoice = this._vad.HasSpeech(frame);

            if (this._isOpen)
            {
                emit(frame, Frame);
                if (isVoice)
                {
                    this._hangover = HangoverFrames;
                }
                else
                {
                    this._hangover--;
                    if (this._hangover <= 0)
                    {
                        this._isOpen = false;
                        this._voiceRun = 0;
                    }
                }
                return;
            }

            // Closed: 直近フレームをリングに溜め、トリガー成立で一括フラッシュして開く。
            short[] slot = this._preroll[this._prerollHead];
            Buffer.BlockCopy(frame, 0, slot, 0, Frame * sizeof(short));
            this._prerollHead = (this._prerollHead + 1) % PrerollFrames;
            if (this._prerollCount < PrerollFrames) this._prerollCount++;

            if (isVoice)
            {
                this._voiceRun++;
                if (this._voiceRun >= TriggerFrames)
                {
                    int start = (this._prerollHead - this._prerollCount + PrerollFrames) % PrerollFrames;
                    for (int i = 0; i < this._prerollCount; i++)
                    {
                        int idx = (start + i) % PrerollFrames;
                        emit(this._preroll[idx], Frame);
                    }
                    this._prerollCount = 0;
                    this._prerollHead = 0;
                    this._isOpen = true;
                    this._hangover = HangoverFrames;
                }
            }
            else
            {
                this._voiceRun = 0;
            }
        }

        public void Dispose()
        {
            this._vad?.Dispose();
        }
    }
}
