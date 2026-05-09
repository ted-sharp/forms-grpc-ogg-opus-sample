using System;
using System.Configuration;
using System.Windows.Forms;
using Sample.Client.Streaming.Audio;
using Sample.Client.Streaming.Rpc;
using Sample.Shared.Dto;

namespace Sample.Client.Streaming
{
    public partial class MainForm : Form
    {
        private readonly RecordingClient _rpc;
        private readonly StreamingRecorder _recorder;
        private readonly Player _player;
        private readonly Timer _uiTimer;
        private bool _seeking;

        public MainForm()
        {
            this.InitializeComponent();

            var host = ConfigurationManager.AppSettings["Server.Host"];
            if (String.IsNullOrEmpty(host)) host = "localhost";
            if (!Int32.TryParse(ConfigurationManager.AppSettings["Server.Port"], out var port)) port = 5000;
            this._rpc = new RecordingClient(host, port);
            this._recorder = new StreamingRecorder();
            this._player = new Player();

            this._recorder.RecordingFinished += this.Recorder_RecordingFinished;
            this._recorder.RecordingFailed += this.Recorder_RecordingFailed;
            this._recorder.AudioFrameAvailable += this.Recorder_AudioFrameAvailable;
            this._player.PlaybackStopped += this.Player_PlaybackStopped;

            this._uiTimer = new Timer { Interval = 100 };
            this._uiTimer.Tick += this.UiTimer_Tick;
            this._uiTimer.Start();

            this.UpdateVadLabel();
            this.UpdateUi();
        }

        private void tbVadAggressiveness_ValueChanged(object sender, EventArgs e)
        {
            this.UpdateVadLabel();
        }

        private void UpdateVadLabel()
        {
            switch (this.tbVadAggressiveness.Value)
            {
                case 0: this.lblVadAggressiveness.Text = "ゆるめ"; break;
                case 1: this.lblVadAggressiveness.Text = "ふつう"; break;
                case 2: this.lblVadAggressiveness.Text = "強め"; break;
                case 3: this.lblVadAggressiveness.Text = "最強"; break;
            }
        }

        private async void btnRecord_Click(object sender, EventArgs e)
        {
            if (this._recorder.IsRecording) return;

            try
            {
                this._player.Stop();
                this.SetStatus("サーバー接続中...");
                await this._rpc.ConnectAsync();
                this.SetStatus("録音開始中...");
                this._recorder.EnableVad = this.chkRemoveSilence.Checked;
                this._recorder.VadAggressiveness = this.tbVadAggressiveness.Value;
                this.waveformView.Reset();
                await this._recorder.StartAsync(this._rpc.Service);
                this.SetStatus("録音中");
                this.UpdateUi();
            }
            catch (Exception ex)
            {
                this.SetStatus("録音開始エラー: " + ex.Message);
                MessageBox.Show(this, ex.ToString(), "録音開始エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void btnPlay_Click(object sender, EventArgs e)
        {
            if (this._recorder.IsRecording) return;

            try
            {
                if (this._player.IsPaused)
                {
                    this._player.Play();
                    this.SetStatus("再生中");
                    this.UpdateUi();
                    return;
                }

                if (!this._player.IsLoaded)
                {
                    this.SetStatus("サーバーから取得中...");
                    this.btnPlay.Enabled = false;
                    await this._player.LoadAsync(this._rpc.Service);
                    this.btnPlay.Enabled = true;
                }

                if (this._player.IsLoaded)
                {
                    this._player.CurrentTime = TimeSpan.Zero;
                    this._player.Play();
                    this.SetStatus("再生中");
                    this.UpdateUi();
                }
            }
            catch (Exception ex)
            {
                this.btnPlay.Enabled = true;
                this.SetStatus("再生エラー: " + ex.Message);
                MessageBox.Show(this, ex.ToString(), "再生エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnPause_Click(object sender, EventArgs e)
        {
            if (this._player.IsPlaying)
            {
                this._player.Pause();
                this.SetStatus("一時停止");
                this.UpdateUi();
            }
        }

        private async void btnStop_Click(object sender, EventArgs e)
        {
            if (this._recorder.IsRecording)
            {
                this.SetStatus("録音停止処理中 (送信完了待ち)...");
                this.btnStop.Enabled = false;
                try
                {
                    await this._recorder.StopAsync();
                }
                catch
                {
                    // RecordingFailed イベントで通知済み
                }
                finally
                {
                    this.UpdateUi();
                }
                return;
            }
            if (this._player.IsPlaying || this._player.IsPaused)
            {
                this._player.Stop();
                this.SetStatus("待機中");
                this.UpdateUi();
            }
        }

        private void tbSeek_MouseDown(object sender, MouseEventArgs e) => this._seeking = true;

        private void tbSeek_MouseUp(object sender, MouseEventArgs e)
        {
            this.ApplySeek();
            this._seeking = false;
        }

        private void tbSeek_Scroll(object sender, EventArgs e)
        {
            if (this._seeking) return;
            this.ApplySeek();
        }

        private void ApplySeek()
        {
            if (!this._player.IsLoaded) return;
            var total = this._player.TotalTime;
            if (total <= TimeSpan.Zero) return;
            var ratio = this.tbSeek.Value / (double)this.tbSeek.Maximum;
            this._player.CurrentTime = TimeSpan.FromSeconds(total.TotalSeconds * ratio);
        }

        private void Recorder_RecordingFinished(object sender, RecordingResult result)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => this.Recorder_RecordingFinished(sender, result)));
                return;
            }
            if (result != null && result.Success)
            {
                this.SetStatus($"録音完了 ({result.ByteSize:N0} byte) → {result.SavedPath}");
            }
            else
            {
                this.SetStatus("録音失敗: " + (result?.ErrorMessage ?? "(不明)"));
            }
            this.UpdateUi();
        }

        private void Recorder_RecordingFailed(object sender, Exception ex)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => this.Recorder_RecordingFailed(sender, ex)));
                return;
            }
            this.SetStatus("録音エラー: " + ex.Message);
            this.UpdateUi();
        }

        private void Recorder_AudioFrameAvailable(object sender, Sample.Client.Streaming.Audio.AudioFrameEventArgs e)
        {
            // NAudio のキャプチャスレッドから呼ばれるので UI スレッドへマーシャルする。
            // 高頻度 (20 ms ごと) なので Invoke ではなく BeginInvoke を使い、フォーム破棄後の例外は無視。
            if (this.IsDisposed) return;
            try
            {
                if (this.InvokeRequired)
                {
                    this.BeginInvoke(new Action(() => this.waveformView.AddPcm(e.Pcm, e.SampleCount)));
                }
                else
                {
                    this.waveformView.AddPcm(e.Pcm, e.SampleCount);
                }
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void Player_PlaybackStopped(object sender, EventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.BeginInvoke(new Action(() => this.Player_PlaybackStopped(sender, e)));
                return;
            }
            this.SetStatus("待機中");
            this.UpdateUi();
        }

        private void UiTimer_Tick(object sender, EventArgs e)
        {
            if (this._recorder.IsRecording)
            {
                var elapsed = this._recorder.Elapsed;
                this.lblTime.Text = $"録音中 {Format(elapsed)}";
            }
            else if (this._player.IsLoaded)
            {
                if (!this._seeking)
                {
                    var total = this._player.TotalTime;
                    var current = this._player.CurrentTime;
                    this.lblTime.Text = $"{Format(current)} / {Format(total)}";
                    if (total > TimeSpan.Zero)
                    {
                        var ratio = current.TotalSeconds / total.TotalSeconds;
                        var newValue = (int)(ratio * this.tbSeek.Maximum);
                        if (newValue < this.tbSeek.Minimum) newValue = this.tbSeek.Minimum;
                        if (newValue > this.tbSeek.Maximum) newValue = this.tbSeek.Maximum;
                        if (newValue != this.tbSeek.Value) this.tbSeek.Value = newValue;
                    }
                }
            }
            else
            {
                this.lblTime.Text = "00:00 / 00:00";
            }
        }

        private void UpdateUi()
        {
            if (this.InvokeRequired) { this.BeginInvoke(new Action(this.UpdateUi)); return; }

            bool recording = this._recorder.IsRecording;
            bool playing = this._player.IsPlaying;
            bool paused = this._player.IsPaused;
            bool loaded = this._player.IsLoaded;

            this.btnRecord.Enabled = !recording && !playing && !paused;
            this.btnPlay.Enabled = !recording && !playing;
            this.btnPause.Enabled = !recording && playing;
            this.btnStop.Enabled = recording || playing || paused;
            this.tbSeek.Enabled = !recording && loaded;
            this.chkRemoveSilence.Enabled = !recording;
            this.tbVadAggressiveness.Enabled = !recording;
        }

        private void SetStatus(string text)
        {
            if (this.InvokeRequired) { this.BeginInvoke(new Action(() => this.SetStatus(text))); return; }
            this.lblStatus.Text = "状態: " + text;
        }

        private static string Format(TimeSpan ts)
        {
            if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
            return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            try { this._uiTimer?.Stop(); } catch { }
            try { this._recorder?.Dispose(); } catch { }
            try { this._player?.Dispose(); } catch { }
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
