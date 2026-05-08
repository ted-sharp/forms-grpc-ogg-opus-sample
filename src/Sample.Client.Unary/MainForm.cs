using System;
using System.Windows.Forms;
using Sample.Client.Unary.Audio;
using Sample.Client.Unary.Rpc;
using Sample.Shared.Dto;

namespace Sample.Client.Unary
{
    public partial class MainForm : Form
    {
        private readonly RecordingClient _rpc;
        private readonly UnaryRecorder _recorder;
        private readonly Player _player;
        private readonly Timer _uiTimer;
        private bool _seeking;

        public MainForm()
        {
            InitializeComponent();

            _rpc = new RecordingClient();
            _recorder = new UnaryRecorder();
            _player = new Player();

            _recorder.RecordingFinished += Recorder_RecordingFinished;
            _recorder.RecordingFailed += Recorder_RecordingFailed;
            _recorder.AudioFrameAvailable += Recorder_AudioFrameAvailable;
            _player.PlaybackStopped += Player_PlaybackStopped;

            _uiTimer = new Timer { Interval = 100 };
            _uiTimer.Tick += UiTimer_Tick;
            _uiTimer.Start();

            UpdateVadLabel();
            UpdateUi();
        }

        private void tbVadAggressiveness_ValueChanged(object sender, EventArgs e)
        {
            UpdateVadLabel();
        }

        private void UpdateVadLabel()
        {
            switch (tbVadAggressiveness.Value)
            {
                case 0: lblVadAggressiveness.Text = "ゆるめ"; break;
                case 1: lblVadAggressiveness.Text = "ふつう"; break;
                case 2: lblVadAggressiveness.Text = "強め"; break;
                case 3: lblVadAggressiveness.Text = "最強"; break;
            }
        }

        private async void btnRecord_Click(object sender, EventArgs e)
        {
            if (_recorder.IsRecording) return;

            try
            {
                _player.Stop();
                SetStatus("録音開始中...");
                _recorder.EnableVad = chkRemoveSilence.Checked;
                _recorder.VadAggressiveness = tbVadAggressiveness.Value;
                waveformView.Reset();
                await _recorder.StartAsync(_rpc.Service);
                SetStatus("録音中");
                UpdateUi();
            }
            catch (Exception ex)
            {
                SetStatus("録音開始エラー: " + ex.Message);
                MessageBox.Show(this, ex.ToString(), "録音開始エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async void btnPlay_Click(object sender, EventArgs e)
        {
            if (_recorder.IsRecording) return;

            try
            {
                if (_player.IsPaused)
                {
                    _player.Play();
                    SetStatus("再生中");
                    UpdateUi();
                    return;
                }

                if (!_player.IsLoaded)
                {
                    SetStatus("サーバーから取得中...");
                    btnPlay.Enabled = false;
                    await _player.LoadAsync(_rpc.Service);
                    btnPlay.Enabled = true;
                }

                if (_player.IsLoaded)
                {
                    _player.CurrentTime = TimeSpan.Zero;
                    _player.Play();
                    SetStatus("再生中");
                    UpdateUi();
                }
            }
            catch (Exception ex)
            {
                btnPlay.Enabled = true;
                SetStatus("再生エラー: " + ex.Message);
                MessageBox.Show(this, ex.ToString(), "再生エラー", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void btnPause_Click(object sender, EventArgs e)
        {
            if (_player.IsPlaying)
            {
                _player.Pause();
                SetStatus("一時停止");
                UpdateUi();
            }
        }

        private async void btnStop_Click(object sender, EventArgs e)
        {
            if (_recorder.IsRecording)
            {
                SetStatus("録音停止処理中 (サーバー送信完了待ち)...");
                btnStop.Enabled = false;
                try
                {
                    await _recorder.StopAsync();
                }
                catch
                {
                    // RecordingFailed イベントで通知済み
                }
                finally
                {
                    UpdateUi();
                }
                return;
            }
            if (_player.IsPlaying || _player.IsPaused)
            {
                _player.Stop();
                SetStatus("待機中");
                UpdateUi();
            }
        }

        private void tbSeek_MouseDown(object sender, MouseEventArgs e) => _seeking = true;

        private void tbSeek_MouseUp(object sender, MouseEventArgs e)
        {
            ApplySeek();
            _seeking = false;
        }

        private void tbSeek_Scroll(object sender, EventArgs e)
        {
            if (_seeking) return;
            ApplySeek();
        }

        private void ApplySeek()
        {
            if (!_player.IsLoaded) return;
            var total = _player.TotalTime;
            if (total <= TimeSpan.Zero) return;
            var ratio = tbSeek.Value / (double)tbSeek.Maximum;
            _player.CurrentTime = TimeSpan.FromSeconds(total.TotalSeconds * ratio);
        }

        private void Recorder_RecordingFinished(object sender, RecordingResult result)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => Recorder_RecordingFinished(sender, result)));
                return;
            }
            if (result != null && result.Success)
            {
                SetStatus($"録音完了 ({result.ByteSize:N0} byte) → {result.SavedPath}");
            }
            else
            {
                SetStatus("録音失敗: " + (result?.ErrorMessage ?? "(不明)"));
            }
            UpdateUi();
        }

        private void Recorder_RecordingFailed(object sender, Exception ex)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => Recorder_RecordingFailed(sender, ex)));
                return;
            }
            SetStatus("録音エラー: " + ex.Message);
            UpdateUi();
        }

        private void Recorder_AudioFrameAvailable(object sender, Sample.Client.Unary.Audio.AudioFrameEventArgs e)
        {
            // NAudio のキャプチャスレッドから呼ばれるので UI スレッドへマーシャルする。
            // 高頻度 (20 ms ごと) なので Invoke ではなく BeginInvoke を使い、フォーム破棄後の例外は無視。
            if (IsDisposed) return;
            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => waveformView.AddPcm(e.Pcm, e.SampleCount)));
                }
                else
                {
                    waveformView.AddPcm(e.Pcm, e.SampleCount);
                }
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        private void Player_PlaybackStopped(object sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => Player_PlaybackStopped(sender, e)));
                return;
            }
            SetStatus("待機中");
            UpdateUi();
        }

        private void UiTimer_Tick(object sender, EventArgs e)
        {
            if (_recorder.IsRecording)
            {
                var elapsed = _recorder.Elapsed;
                lblTime.Text = $"録音中 {Format(elapsed)}";
            }
            else if (_player.IsLoaded)
            {
                if (!_seeking)
                {
                    var total = _player.TotalTime;
                    var current = _player.CurrentTime;
                    lblTime.Text = $"{Format(current)} / {Format(total)}";
                    if (total > TimeSpan.Zero)
                    {
                        var ratio = current.TotalSeconds / total.TotalSeconds;
                        var newValue = (int)(ratio * tbSeek.Maximum);
                        if (newValue < tbSeek.Minimum) newValue = tbSeek.Minimum;
                        if (newValue > tbSeek.Maximum) newValue = tbSeek.Maximum;
                        if (newValue != tbSeek.Value) tbSeek.Value = newValue;
                    }
                }
            }
            else
            {
                lblTime.Text = "00:00 / 00:00";
            }
        }

        private void UpdateUi()
        {
            if (InvokeRequired) { BeginInvoke(new Action(UpdateUi)); return; }

            bool recording = _recorder.IsRecording;
            bool playing = _player.IsPlaying;
            bool paused = _player.IsPaused;
            bool loaded = _player.IsLoaded;

            btnRecord.Enabled = !recording && !playing && !paused;
            btnPlay.Enabled = !recording && !playing;
            btnPause.Enabled = !recording && playing;
            btnStop.Enabled = recording || playing || paused;
            tbSeek.Enabled = !recording && loaded;
            chkRemoveSilence.Enabled = !recording;
            tbVadAggressiveness.Enabled = !recording;
        }

        private void SetStatus(string text)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => SetStatus(text))); return; }
            lblStatus.Text = "状態: " + text;
        }

        private static string Format(TimeSpan ts)
        {
            if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
            return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            try { _uiTimer?.Stop(); } catch { }
            try { _recorder?.Dispose(); } catch { }
            try { _player?.Dispose(); } catch { }
            try { _rpc?.Dispose(); } catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                components?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
