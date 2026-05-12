using System.Windows;
using System.Windows.Media;

namespace Sample.Client.Streaming.Ui;

/// <summary>
/// 録音中の PCM フレームから 5 ms ブロックごとのピーク値を抽出してリングバッファに溜め、
/// 中心線を挟んで上下対称に折れ線で描画するシンプルな波形ビュー。
/// FrameworkElement の OnRender(DrawingContext) で描画するので、Canvas 等を経由せず軽い。
/// </summary>
public sealed class WaveformView : FrameworkElement
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

    private readonly Brush _background;
    private readonly Brush _centerLine;
    private readonly Pen _wavePen;

    public WaveformView()
    {
        this._background = Brushes.Black;
        var fg = Color.FromRgb(50, 205, 50); // LimeGreen
        this._centerLine = new SolidColorBrush(Color.FromArgb(60, fg.R, fg.G, fg.B));
        this._wavePen = new Pen(new SolidColorBrush(fg), 1.2);
        this._background.Freeze();
        this._centerLine.Freeze();
        this._wavePen.Freeze();
    }

    /// <summary>録音開始時などに呼んで波形をクリアする。</summary>
    public void Reset()
    {
        Array.Clear(this._peaks, 0, this._peaks.Length);
        this._writeIndex = 0;
        this._filled = 0;
        this._carrySamples = 0;
        this._carryPeak = 0;
        this.InvalidateVisual();
    }

    /// <summary>16-bit signed mono PCM を流し込む。UI スレッドから呼ぶこと。</summary>
    public void AddPcm(short[] pcm, int count)
    {
        if (pcm == null || count <= 0) return;

        int i = 0;
        short blockPeak = this._carryPeak;
        int blockCount = this._carrySamples;

        while (i < count)
        {
            int remaining = SamplesPerBlock - blockCount;
            int take = Math.Min(remaining, count - i);
            for (int k = 0; k < take; k++)
            {
                short s = pcm[i + k];
                short abs = s == Int16.MinValue ? Int16.MaxValue : Math.Abs(s);
                if (abs > blockPeak) blockPeak = abs;
            }
            i += take;
            blockCount += take;

            if (blockCount >= SamplesPerBlock)
            {
                this._peaks[this._writeIndex] = blockPeak / 32768f;
                this._writeIndex = (this._writeIndex + 1) % BufferLength;
                if (this._filled < BufferLength) this._filled++;
                blockCount = 0;
                blockPeak = 0;
            }
        }

        this._carrySamples = blockCount;
        this._carryPeak = blockPeak;

        this.InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = this.ActualWidth;
        double h = this.ActualHeight;
        if (w <= 2 || h <= 2) return;

        dc.DrawRectangle(this._background, null, new Rect(0, 0, w, h));

        double midY = h / 2.0;
        dc.DrawLine(new Pen(this._centerLine, 1.0), new Point(0, midY), new Point(w, midY));

        if (this._filled == 0) return;

        // リングバッファを古い順に並べ直して N 個の点として描画する。
        int n = this._filled;
        int start = (this._writeIndex - n + BufferLength) % BufferLength;
        double maxAmp = (h / 2.0) - 2.0;

        Point? prevTop = null;
        for (int j = 0; j < n; j++)
        {
            float peak = this._peaks[(start + j) % BufferLength];
            double x = (double)j * w / Math.Max(1, BufferLength - 1);
            double dy = peak * maxAmp;
            var top = new Point(x, midY - dy);
            var bottom = new Point(x, midY + dy);

            dc.DrawLine(this._wavePen, top, bottom);
            if (prevTop.HasValue)
            {
                // 隣り合うピーク同士の上端を結ぶことで、線らしい連続感を出す。
                dc.DrawLine(this._wavePen, prevTop.Value, top);
            }
            prevTop = top;
        }
    }
}
