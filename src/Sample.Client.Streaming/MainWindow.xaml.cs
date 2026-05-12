using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Sample.Client.Streaming.Audio;
using Sample.Client.Streaming.Configuration;
using Sample.Client.Streaming.Rpc;
using Sample.Shared.Dto;

namespace Sample.Client.Streaming;

public partial class MainWindow : Window
{
    private readonly RecordingClient _rpc;
    private readonly StreamingRecorder _recorder;
    private readonly Player _player;
    private readonly DispatcherTimer _uiTimer;
    private bool _seeking;
    private bool _suppressSeekEvent;

    public MainWindow()
    {
        this.InitializeComponent();

        var settings = AppSettings.Load();
        this._rpc = new RecordingClient(settings.Server.Host, settings.Server.Port);
        this._recorder = new StreamingRecorder();
        this._player = new Player();

        this._recorder.RecordingFinished += this.Recorder_RecordingFinished;
        this._recorder.RecordingFailed += this.Recorder_RecordingFailed;
        this._recorder.AudioFrameAvailable += this.Recorder_AudioFrameAvailable;
        this._player.PlaybackStopped += this.Player_PlaybackStopped;

        this._uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        this._uiTimer.Tick += this.UiTimer_Tick;
        this._uiTimer.Start();

        this.UpdateVadLabel();
        this.UpdateUi();
    }

    private void tbVadAggressiveness_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        this.UpdateVadLabel();
    }

    private void UpdateVadLabel()
    {
        if (this.lblVadAggressiveness == null) return;
        this.lblVadAggressiveness.Text = ((int)this.tbVadAggressiveness.Value) switch
        {
            0 => "ゆるめ",
            1 => "ふつう",
            2 => "強め",
            3 => "最強",
            _ => String.Empty,
        };
    }

    private async void btnRecord_Click(object sender, RoutedEventArgs e)
    {
        if (this._recorder.IsRecording) return;

        try
        {
            this._player.Stop();
            this.SetStatus("サーバー接続中...");
            await this._rpc.ConnectAsync();
            this.SetStatus("録音開始中...");
            this._recorder.EnableVad = this.chkRemoveSilence.IsChecked == true;
            this._recorder.VadAggressiveness = (int)this.tbVadAggressiveness.Value;
            this.waveformView.Reset();
            await this._recorder.StartAsync(this._rpc.Service);
            this.SetStatus("録音中");
            this.UpdateUi();
        }
        catch (Exception ex)
        {
            this.SetStatus("録音開始エラー: " + ex.Message);
            MessageBox.Show(this, ex.ToString(), "録音開始エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void btnPlay_Click(object sender, RoutedEventArgs e)
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
                this.btnPlay.IsEnabled = false;
                await this._player.LoadAsync(this._rpc.Service);
                this.btnPlay.IsEnabled = true;
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
            this.btnPlay.IsEnabled = true;
            this.SetStatus("再生エラー: " + ex.Message);
            MessageBox.Show(this, ex.ToString(), "再生エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void btnPause_Click(object sender, RoutedEventArgs e)
    {
        if (this._player.IsPlaying)
        {
            this._player.Pause();
            this.SetStatus("一時停止");
            this.UpdateUi();
        }
    }

    private async void btnStop_Click(object sender, RoutedEventArgs e)
    {
        if (this._recorder.IsRecording)
        {
            this.SetStatus("録音停止処理中 (送信完了待ち)...");
            this.btnStop.IsEnabled = false;
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

    private void tbSeek_PreviewMouseDown(object sender, MouseButtonEventArgs e) => this._seeking = true;

    private void tbSeek_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        this._seeking = false;
        this.ApplySeek();
    }

    private void tbSeek_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // タイマーが値を書き戻す時はシークを起こさない (UiTimer で _suppressSeekEvent を立てる)。
        if (this._suppressSeekEvent) return;
        // ドラッグ中はマウスを離した瞬間に ApplySeek するため、ここでは反映しない。
        if (this._seeking) return;
        this.ApplySeek();
    }

    private void ApplySeek()
    {
        if (!this._player.IsLoaded) return;
        var total = this._player.TotalTime;
        if (total <= TimeSpan.Zero) return;
        var ratio = this.tbSeek.Value / this.tbSeek.Maximum;
        this._player.CurrentTime = TimeSpan.FromSeconds(total.TotalSeconds * ratio);
    }

    private void Recorder_RecordingFinished(object? sender, RecordingResult result)
    {
        this.Dispatcher.BeginInvoke(() =>
        {
            if (result != null && result.Success)
            {
                this.SetStatus($"録音完了 ({result.ByteSize:N0} byte) → {result.SavedPath}");
            }
            else
            {
                this.SetStatus("録音失敗: " + (result?.ErrorMessage ?? "(不明)"));
            }
            this.UpdateUi();
        });
    }

    private void Recorder_RecordingFailed(object? sender, Exception ex)
    {
        this.Dispatcher.BeginInvoke(() =>
        {
            this.SetStatus("録音エラー: " + ex.Message);
            this.UpdateUi();
        });
    }

    private void Recorder_AudioFrameAvailable(object? sender, AudioFrameEventArgs e)
    {
        // NAudio のキャプチャスレッドから呼ばれる。20 ms ごとの高頻度イベントなので
        // BeginInvoke でキューに積み、ウィンドウ破棄後の例外は握り潰す。
        try
        {
            this.Dispatcher.BeginInvoke(() => this.waveformView.AddPcm(e.Pcm, e.SampleCount));
        }
        catch (TaskCanceledException) { }
        catch (InvalidOperationException) { }
    }

    private void Player_PlaybackStopped(object? sender, EventArgs e)
    {
        this.Dispatcher.BeginInvoke(() =>
        {
            this.SetStatus("待機中");
            this.UpdateUi();
        });
    }

    private void UiTimer_Tick(object? sender, EventArgs e)
    {
        if (this._recorder.IsRecording)
        {
            this.lblTime.Text = $"録音中 {Format(this._recorder.Elapsed)}";
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
                    var newValue = ratio * this.tbSeek.Maximum;
                    if (newValue < this.tbSeek.Minimum) newValue = this.tbSeek.Minimum;
                    if (newValue > this.tbSeek.Maximum) newValue = this.tbSeek.Maximum;
                    if (Math.Abs(newValue - this.tbSeek.Value) > 0.5)
                    {
                        this._suppressSeekEvent = true;
                        try { this.tbSeek.Value = newValue; }
                        finally { this._suppressSeekEvent = false; }
                    }
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
        bool recording = this._recorder.IsRecording;
        bool playing = this._player.IsPlaying;
        bool paused = this._player.IsPaused;
        bool loaded = this._player.IsLoaded;

        this.btnRecord.IsEnabled = !recording && !playing && !paused;
        this.btnPlay.IsEnabled = !recording && !playing;
        this.btnPause.IsEnabled = !recording && playing;
        this.btnStop.IsEnabled = recording || playing || paused;
        this.tbSeek.IsEnabled = !recording && loaded;
        this.chkRemoveSilence.IsEnabled = !recording;
        this.tbVadAggressiveness.IsEnabled = !recording;
    }

    private void SetStatus(string text)
    {
        this.lblStatus.Text = "状態: " + text;
    }

    private static string Format(TimeSpan ts)
    {
        if (ts < TimeSpan.Zero) ts = TimeSpan.Zero;
        return $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}";
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        try { this._uiTimer?.Stop(); } catch { }
        try { this._recorder?.Dispose(); } catch { }
        try { this._player?.Dispose(); } catch { }
        try { this._rpc?.Dispose(); } catch { }
    }
}
