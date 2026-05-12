using System.IO;
using Concentus.Enums;
using Concentus.Oggfile;
using Concentus.Structs;
using NAudio.Wave;
using Sample.Shared;
using Sample.Shared.Audio;
using Sample.Shared.Dto;

namespace Sample.Client.Unary.Audio;

/// <summary>
/// NAudio で録音 → Concentus で Opus + Ogg 化 (MemoryStream 上に蓄積) →
/// 録音停止時に SaveUnary で全バイトを一括送信する。
/// </summary>
public sealed class UnaryRecorder : IDisposable
{
    private WaveInEvent? _waveIn;
    private OpusEncoder? _encoder;
    private OpusOggWriteStream? _oggWriter;
    private MemoryStream? _buffer;
    private VadGate? _vadGate;
    private IRecordingService? _service;
    private DateTime _startUtc;
    private TaskCompletionSource<object?>? _stopTcs;

    public bool IsRecording { get; private set; }

    /// <summary>VAD で無音区間をカットするか。StartAsync 前に設定すること。</summary>
    public bool EnableVad { get; set; }

    /// <summary>WebRTC VAD のアグレッシブ度 (0..3、3 が最も厳しい)。StartAsync 前に設定すること。</summary>
    public int VadAggressiveness { get; set; }

    public TimeSpan Elapsed => this.IsRecording ? DateTime.UtcNow - this._startUtc : TimeSpan.Zero;

    public event EventHandler<RecordingResult>? RecordingFinished;
    public event EventHandler<Exception>? RecordingFailed;
    public event EventHandler<AudioFrameEventArgs>? AudioFrameAvailable;

    public Task StartAsync(IRecordingService service)
    {
        if (this.IsRecording) throw new InvalidOperationException("Already recording");

        this._service = service ?? throw new ArgumentNullException(nameof(service));
        this._encoder = OpusEncoder.Create(AudioConstants.SampleRate, AudioConstants.Channels, OpusApplication.OPUS_APPLICATION_VOIP);
        this._encoder.Bitrate = AudioConstants.BitRate;

        this._buffer = new MemoryStream();
        this._oggWriter = new OpusOggWriteStream(this._encoder, this._buffer);

        this._vadGate = this.EnableVad ? new VadGate(this.VadAggressiveness) : null;

        this._waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(AudioConstants.SampleRate, AudioConstants.BitsPerSample, AudioConstants.Channels),
            BufferMilliseconds = AudioConstants.FrameMilliseconds,
        };
        this._waveIn.DataAvailable += this.OnDataAvailable;
        this._waveIn.RecordingStopped += this.OnRecordingStopped;

        this._startUtc = DateTime.UtcNow;
        this.IsRecording = true;
        this._waveIn.StartRecording();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 録音を停止し、サーバーへの送信完了 (SaveUnary レスポンス受領) まで待機する Task を返す。
    /// </summary>
    public Task StopAsync()
    {
        if (!this.IsRecording) return Task.CompletedTask;
        var tcs = new TaskCompletionSource<object?>();
        this._stopTcs = tcs;
        this._waveIn?.StopRecording();
        return tcs.Task;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;
        try
        {
            int sampleCount = e.BytesRecorded / AudioConstants.BytesPerSample;
            short[] pcm = new short[sampleCount];
            Buffer.BlockCopy(e.Buffer, 0, pcm, 0, e.BytesRecorded);

            AudioFrameAvailable?.Invoke(this, new AudioFrameEventArgs(pcm, sampleCount));

            if (this._vadGate != null)
            {
                this._vadGate.Process(pcm, sampleCount, (buf, n) => this._oggWriter!.WriteSamples(buf, 0, n));
            }
            else
            {
                this._oggWriter!.WriteSamples(pcm, 0, sampleCount);
            }
        }
        catch (Exception ex)
        {
            RecordingFailed?.Invoke(this, ex);
            this._waveIn?.StopRecording();
        }
    }

    private async void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        Exception? captured = null;
        try
        {
            // 1) VAD ゲートが Open 状態の端数フレームを先に吐き出す (Finish 後は WriteSamples 不可)。
            this._vadGate?.Flush((buf, n) => this._oggWriter!.WriteSamples(buf, 0, n));

            // 2) Ogg トレーラを書き出す。残サンプルがパディングされて _buffer に書き込まれる。
            this._oggWriter?.Finish();

            byte[] bytes = this._buffer != null ? this._buffer.ToArray() : Array.Empty<byte>();

            if (e.Exception != null)
            {
                captured = e.Exception;
                RecordingFailed?.Invoke(this, e.Exception);
                return;
            }

            if (bytes.Length == 0)
            {
                captured = new InvalidOperationException("録音データが空です。");
                RecordingFailed?.Invoke(this, captured);
                return;
            }

            // 3) Unary で一括送信し、サーバー応答 (= 保存完了) を待つ。
            var result = await this._service!.SaveUnary(new SaveUnaryRequest { OggOpusBytes = bytes });
            RecordingFinished?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            captured = ex;
            RecordingFailed?.Invoke(this, ex);
        }
        finally
        {
            this.IsRecording = false;
            this.CleanUp();
            var tcs = this._stopTcs;
            this._stopTcs = null;
            if (tcs != null)
            {
                if (captured != null) tcs.TrySetException(captured);
                else tcs.TrySetResult(null);
            }
        }
    }

    private void CleanUp()
    {
        try { this._waveIn?.Dispose(); } catch { }
        this._waveIn = null;
        try { this._vadGate?.Dispose(); } catch { }
        this._vadGate = null;
        try { this._buffer?.Dispose(); } catch { }
        this._buffer = null;
        this._oggWriter = null;
        this._encoder = null;
    }

    public void Dispose()
    {
        if (this.IsRecording)
        {
            try { this._waveIn?.StopRecording(); } catch { }
        }
        this.CleanUp();
    }
}
