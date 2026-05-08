using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Sample.Client.Stt.Audio;
using Sample.Client.Stt.Configuration;
using Sample.Client.Stt.Rpc;
using Sample.Client.Stt.Stt;
using Sample.Shared.Dto;

namespace Sample.Client.Stt
{
    public partial class MainForm : Form
    {
        private readonly SttSettings _settings;
        private readonly RecordingClient _rpc;
        private ISttEngine _engine;
        private SttEngineKind? _engineKind;
        private CancellationTokenSource _cts;
        private System.Windows.Forms.Timer _progressTimer;
        private DateTime _progressStartUtc;
        private double _progressTotalSeconds;

        public MainForm()
        {
            this.InitializeComponent();

            WavWriter.CleanupOldTempFiles();

            this._settings = SttSettings.Load();
            this._rpc = new RecordingClient(this._settings.Server.Host, this._settings.Server.Port);

            this.rbMoonshine.CheckedChanged += this.OnEngineRadioChanged;
            this.rbWhisper.CheckedChanged += this.OnEngineRadioChanged;
            this.rbAzure.CheckedChanged += this.OnEngineRadioChanged;
        }

        private SttEngineKind GetSelectedKind()
        {
            if (this.rbWhisper.Checked) return SttEngineKind.WhisperLargeV3;
            if (this.rbAzure.Checked) return SttEngineKind.Azure;
            return SttEngineKind.Moonshine;
        }

        private void OnEngineRadioChanged(object sender, EventArgs e)
        {
            // CheckedChanged は古い RadioButton の解除と新しい RadioButton の選択で 2 回発火する。
            // Checked == true の側だけ拾う。
            if (sender is RadioButton rb && !rb.Checked) return;
            // 実際のインスタンス再生成は次の文字起こし開始時に行う (重い初期化を遅延)。
        }

        private async Task<ISttEngine> EnsureEngineAsync(SttEngineKind kind, CancellationToken ct)
        {
            if (this._engine != null && this._engineKind == kind)
            {
                return this._engine;
            }

            // 旧エンジンの破棄も Dispose() が onnxruntime のセッション解放で時間を食う場合があるので
            // モデルロードと同じ Task.Run の中でまとめてバックグラウンド実行する。
            var old = this._engine;
            var settings = this._settings;
            this._engine = null;
            this._engineKind = null;

            var newEngine = await Task.Run<ISttEngine>(() =>
            {
                try { old?.Dispose(); } catch { /* ignore */ }
                ct.ThrowIfCancellationRequested();
                switch (kind)
                {
                    case SttEngineKind.Moonshine:
                        return new MoonshineSttEngine(settings);
                    case SttEngineKind.WhisperLargeV3:
                        return new WhisperSttEngine(settings);
                    case SttEngineKind.Azure:
                        return new AzureSttEngine(settings);
                    default:
                        throw new InvalidOperationException($"未対応のエンジン: {kind}");
                }
            }, ct).ConfigureAwait(true);

            this._engine = newEngine;
            this._engineKind = kind;
            return newEngine;
        }

        private async void btnTranscribe_Click(object sender, EventArgs e)
        {
            var kind = this.GetSelectedKind();
            this.SetBusy(true);
            this.txtResult.Clear();
            this.SetStatus($"エンジン初期化中... ({kind} のモデル読み込み)");
            this.StartIndeterminateProgress();

            this._cts?.Dispose();
            this._cts = new CancellationTokenSource();
            var ct = this._cts.Token;

            string wavPath = null;
            try
            {
                ISttEngine engine;
                try
                {
                    // ここで await に到達することで UI スレッドが解放され、
                    // 上で設定した SetStatus / StartIndeterminateProgress の描画が即座に反映される。
                    // モデルファイル (Moonshine/Whisper の .onnx 等) のロードはバックグラウンドで進む。
                    engine = await this.EnsureEngineAsync(kind, ct).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("[Stt] エンジン初期化エラー: " + ex);
                    this.SetStatus("エンジン初期化エラー: " + ex.Message);
                    MessageBox.Show(this, ex.ToString(), "エンジン初期化エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                this.SetStatus("サーバーから取得中...");
                var dl = await this._rpc.Service.Download(new DownloadRequest()).ResponseAsync.ConfigureAwait(true);
                if (!dl.Exists || dl.OggOpusBytes == null || dl.OggOpusBytes.Length == 0)
                {
                    this.SetStatus("サーバーに録音ファイルがありません。先に Unary/Streaming クライアントで録音してください。");
                    return;
                }

                ct.ThrowIfCancellationRequested();

                AudioInput input = null;
                await Task.Run(() =>
                {
                    this.SetStatusFromBackground("デコード中...");
                    var pcm48 = OpusFileDecoder.DecodeOggOpusToPcm48kMono(dl.OggOpusBytes);
                    ct.ThrowIfCancellationRequested();

                    this.SetStatusFromBackground("リサンプル中...");
                    var pcm16 = Resampler.To16kFloatMono(pcm48);
                    ct.ThrowIfCancellationRequested();

                    this.SetStatusFromBackground("一時 WAV 書き出し中...");
                    wavPath = WavWriter.Write16kMonoWav(pcm16);
                    input = new AudioInput(wavPath, pcm16);
                }, ct).ConfigureAwait(true);

                this.SetStatus($"認識中... ({kind})");
                if (kind == SttEngineKind.Azure)
                {
                    // Azure は連続認識のリアルタイム比 ≒ 1.0 を仮定して経過秒/総秒数で擬似的に進捗を出す。
                    // 実際のレイテンシは変動するので 95% で頭打ちにし、完了時に 100% を打つ。
                    double totalSeconds = (double)input.Pcm16kMono.Length / 16000.0;
                    this.StartDeterminateProgress(totalSeconds);
                }
                var progress = new Progress<string>(text =>
                {
                    this.txtResult.AppendText(text + Environment.NewLine);
                });

                var result = await engine.TranscribeAsync(input, progress, ct).ConfigureAwait(true);
                if (kind != SttEngineKind.Azure)
                {
                    // Sherpa は最終結果のみ返るので一括表示
                    this.txtResult.Text = result;
                }
                this.CompleteProgress();
                this.SetStatus("完了");
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("[Stt] 文字起こしキャンセル");
                this.SetStatus("キャンセルされました");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("[Stt] 文字起こしエラー: " + ex);
                this.SetStatus("エラー: " + ex.Message);
                MessageBox.Show(this, ex.ToString(), "文字起こしエラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                if (wavPath != null)
                {
                    try { File.Delete(wavPath); } catch { /* ignore */ }
                }
                this.StopProgress();
                this.SetBusy(false);
            }
        }

        private void StartIndeterminateProgress()
        {
            // sherpa-onnx の Decode は中間進捗を返さないので Marquee で「動いている」ことだけ示す。
            this._progressTimer?.Stop();
            this.progressBar.Visible = true;
            this.progressBar.Style = ProgressBarStyle.Marquee;
            this.progressBar.MarqueeAnimationSpeed = 30;
            this.UseWaitCursor = true;
        }

        private void StartDeterminateProgress(double totalSeconds)
        {
            this._progressTimer?.Stop();
            this.progressBar.Style = ProgressBarStyle.Continuous;
            this.progressBar.MarqueeAnimationSpeed = 0;
            this.progressBar.Minimum = 0;
            this.progressBar.Maximum = 100;
            this.progressBar.Value = 0;
            this.progressBar.Visible = true;
            this._progressStartUtc = DateTime.UtcNow;
            this._progressTotalSeconds = Math.Max(1.0, totalSeconds);
            if (this._progressTimer == null)
            {
                this._progressTimer = new System.Windows.Forms.Timer { Interval = 200 };
                this._progressTimer.Tick += this.ProgressTimer_Tick;
            }
            this._progressTimer.Start();
        }

        private void ProgressTimer_Tick(object sender, EventArgs e)
        {
            var elapsed = (DateTime.UtcNow - this._progressStartUtc).TotalSeconds;
            // 入力長 ≒ 認識処理時間という仮定なので 95% で頭打ちにして完了時に 100% を打つ。
            int pct = (int)Math.Min(95.0, elapsed / this._progressTotalSeconds * 100.0);
            if (pct > this.progressBar.Value)
            {
                this.progressBar.Value = pct;
            }
        }

        private void CompleteProgress()
        {
            this._progressTimer?.Stop();
            if (this.progressBar.Style == ProgressBarStyle.Continuous)
            {
                this.progressBar.Value = this.progressBar.Maximum;
            }
        }

        private void StopProgress()
        {
            this._progressTimer?.Stop();
            this.progressBar.Visible = false;
            this.progressBar.Style = ProgressBarStyle.Continuous;
            this.progressBar.MarqueeAnimationSpeed = 0;
            this.progressBar.Value = 0;
            this.UseWaitCursor = false;
        }

        private void btnCancel_Click(object sender, EventArgs e)
        {
            try { this._cts?.Cancel(); } catch { /* ignore */ }
        }

        private void SetBusy(bool busy)
        {
            this.btnTranscribe.Enabled = !busy;
            this.btnCancel.Enabled = busy;
            this.rbMoonshine.Enabled = !busy;
            this.rbWhisper.Enabled = !busy;
            this.rbAzure.Enabled = !busy;
        }

        private void SetStatus(string text)
        {
            this.lblStatus.Text = "状態: " + text;
        }

        private void SetStatusFromBackground(string text)
        {
            if (this.IsHandleCreated)
            {
                this.BeginInvoke(new Action(() => this.SetStatus(text)));
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            try { this._cts?.Cancel(); } catch { }
            try { this._cts?.Dispose(); } catch { }
            try { this._progressTimer?.Stop(); } catch { }
            try { this._progressTimer?.Dispose(); } catch { }
            try { this._engine?.Dispose(); } catch { }
            try { this._rpc?.Dispose(); } catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.components?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
