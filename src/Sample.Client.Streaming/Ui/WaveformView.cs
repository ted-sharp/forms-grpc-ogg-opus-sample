using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Sample.Client.Streaming.Ui
{
    /// <summary>
    /// 録音中の PCM フレームから 5 ms ブロックごとのピーク値を抽出してリングバッファに溜め、
    /// 中心線を挟んで上下対称に折れ線で描画するシンプルな波形ビュー。
    /// </summary>
    public sealed class WaveformView : Control
    {
        // 5 ms@48kHz = 240 sample をひとブロックとし、ブロックごとに 1 ピークをリングに積む。
        // 1 ブロック=1 px なら 1 px ≒ 5 ms。バッファ 480 で約 2.4 秒分の履歴を表示する。
        private const int SamplesPerBlock = 240;
        private const int BufferLength = 480;

        private readonly float[] _peaks = new float[BufferLength];
        private int _writeIndex;
        private int _filled;
        private int _carrySamples;
        private short _carryPeak;

        public WaveformView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw, true);
            BackColor = Color.Black;
            ForeColor = Color.LimeGreen;
        }

        /// <summary>録音開始時などに呼んで波形をクリアする。</summary>
        public void Reset()
        {
            Array.Clear(_peaks, 0, _peaks.Length);
            _writeIndex = 0;
            _filled = 0;
            _carrySamples = 0;
            _carryPeak = 0;
            Invalidate();
        }

        /// <summary>16-bit signed mono PCM を流し込む。UI スレッドから呼ぶこと。</summary>
        public void AddPcm(short[] pcm, int count)
        {
            if (pcm == null || count <= 0) return;

            int i = 0;
            short blockPeak = _carryPeak;
            int blockCount = _carrySamples;

            while (i < count)
            {
                int remaining = SamplesPerBlock - blockCount;
                int take = Math.Min(remaining, count - i);
                for (int k = 0; k < take; k++)
                {
                    short s = pcm[i + k];
                    short abs = s == short.MinValue ? short.MaxValue : Math.Abs(s);
                    if (abs > blockPeak) blockPeak = abs;
                }
                i += take;
                blockCount += take;

                if (blockCount >= SamplesPerBlock)
                {
                    _peaks[_writeIndex] = blockPeak / 32768f;
                    _writeIndex = (_writeIndex + 1) % BufferLength;
                    if (_filled < BufferLength) _filled++;
                    blockCount = 0;
                    blockPeak = 0;
                }
            }

            _carrySamples = blockCount;
            _carryPeak = blockPeak;

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            int w = Width;
            int h = Height;
            if (w <= 2 || h <= 2) return;

            float midY = h / 2f;

            using (var centerPen = new Pen(Color.FromArgb(60, ForeColor)))
            {
                g.DrawLine(centerPen, 0, midY, w, midY);
            }

            if (_filled == 0) return;

            // リングバッファを古い順に並べ直して N 個の点として描画する。
            int n = _filled;
            int start = (_writeIndex - n + BufferLength) % BufferLength;
            float maxAmp = (h / 2f) - 2f;

            using (var pen = new Pen(ForeColor, 1.2f))
            {
                PointF? prev = null;
                for (int j = 0; j < n; j++)
                {
                    float peak = _peaks[(start + j) % BufferLength];
                    float x = (float)j * w / Math.Max(1, BufferLength - 1);
                    float dy = peak * maxAmp;
                    var top = new PointF(x, midY - dy);
                    var bottom = new PointF(x, midY + dy);

                    g.DrawLine(pen, top, bottom);
                    if (prev.HasValue)
                    {
                        // 隣り合うピーク同士の上端を結ぶことで、線らしい連続感を出す。
                        g.DrawLine(pen, prev.Value, top);
                    }
                    prev = top;
                }
            }
        }
    }
}
