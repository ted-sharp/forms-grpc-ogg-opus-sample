using Concentus.Enums;
using Concentus.Oggfile;
using Concentus.Structs;
using MagicOnion;
using NAudio.Wave;
using Sample.Shared;
using Sample.Shared.Audio;
using Sample.Shared.Dto;

namespace Sample.Client.Streaming.Audio;

/// <summary>
/// NAudio で録音 → Concentus で Opus + Ogg 化 → ChunkForwardStream 経由で
/// MagicOnion ClientStreaming にバイト列を流し込む。
/// </summary>
public sealed class StreamingRecorder : IDisposable
{
    private WaveInEvent? _waveIn;
    private OpusEncoder? _encoder;
    private OpusOggWriteStream? _oggWriter;
    private ChunkForwardStream? _forwardStream;
    private ClientStreamingResult<RecordingChunk, RecordingResult>? _streamCall;
    private VadGate? _vadGate;
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

    /// <summary>
    /// マイクから到着した PCM フレームを波形表示用に通知する。
    /// VAD ゲート前の生のサンプルを渡すので、無音カット中でもメーターは反応する。
    /// イベントハンドラはオーディオキャプチャスレッドから呼ばれるため UI 反映時は Dispatcher 必須。
    /// 渡す配列はイベントごとに新規確保した使い切りバッファ。
    /// </summary>
    public event EventHandler<AudioFrameEventArgs>? AudioFrameAvailable;

    public async Task StartAsync(IRecordingService service)
    {
        if (this.IsRecording) throw new InvalidOperationException("Already recording");

        this._encoder = OpusEncoder.Create(AudioConstants.SampleRate, AudioConstants.Channels, OpusApplication.OPUS_APPLICATION_VOIP);
        this._encoder.Bitrate = AudioConstants.BitRate;

        // MagicOnion v7: SaveStreaming() は Task<ClientStreamingResult<,>> を返すので await で受ける。
        this._streamCall = await service.SaveStreaming().ConfigureAwait(true);
        var streamCall = this._streamCall.Value;

        this._forwardStream = new ChunkForwardStream(async bytes =>
        {
            // ConfigureAwait(false) が必須。Flush() は ChunkForwardStream で .GetAwaiter().GetResult()
            // で同期ブロックされる経路があり (UI スレッドからの warm-up Flush や OnRecordingStopped の Finish)、
            // 内側の await が UI SyncContext を取りに戻ろうとするとデッドロックする。
            await streamCall.RequestStream.WriteAsync(new RecordingChunk { OggOpusBytes = bytes })
                .ConfigureAwait(false);
        });
        this._oggWriter = new OpusOggWriteStream(this._encoder, this._forwardStream);

        this._vadGate = this.EnableVad ? new VadGate(this.VadAggressiveness) : null;

        // OpusOggWriteStream 構築時に OpusHead/OpusTags が ChunkForwardStream に書かれているはずなので、
        // 即フラッシュして gRPC ストリームに最初の WriteAsync を打ち込んでおく。これで遅延起動による
        // 不安定さを排除する。
        this._forwardStream.Flush();

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
    }

    /// <summary>
    /// 録音を停止し、サーバーへの送信完了 (RequestStream を Complete してレスポンス受領) まで待機する Task を返す。
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
            RecordingFailed?.Invoke(this, new InvalidOperationException($"[Step=OnDataAvailable] {ex.GetType().Name}: {ex.Message}", ex));
            this._waveIn?.StopRecording();
        }
    }

    private async void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        Exception? captured = null;
        string lastStep = "(start)";
        try
        {
            lastStep = "VadGate.Flush";
            // VAD ゲートが Open 状態のまま録音終了した場合の端数フレームを吐き出す。
            // Finish() の後に WriteSamples を呼ぶと内部 Stream が Close 済みで死ぬので必ず Finish() の前に行う。
            this._vadGate?.Flush((buf, n) => this._oggWriter!.WriteSamples(buf, 0, n));

            lastStep = "OggWriter.Finish";
            // OpusOggWriteStream.Finish() は内部で _outputStream.Flush() + Close() を呼ぶ。
            // 我々の ChunkForwardStream.Flush() は同期的に gRPC WriteAsync を完了させるので、
            // この時点で Ogg Opus データはサーバーへの request stream にすべて流し込み終わっている。
            this._oggWriter?.Finish();

            lastStep = "RequestStream.CompleteAsync (END_STREAM)";
            var streamCall = this._streamCall!.Value;
            if (streamCall.RequestStream != null)
            {
                await streamCall.RequestStream.CompleteAsync();
            }

            lastStep = "ResponseAsync (await server response)";
            var result = await streamCall.ResponseAsync;

            if (e.Exception != null)
            {
                captured = e.Exception;
                RecordingFailed?.Invoke(this, e.Exception);
            }
            else
            {
                RecordingFinished?.Invoke(this, result);
            }
        }
        catch (Exception ex)
        {
            captured = new InvalidOperationException($"[Step={lastStep}] {ex.GetType().Name}: {ex.Message}", ex);
            RecordingFailed?.Invoke(this, captured);
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
        try { this._forwardStream?.Dispose(); } catch { }
        this._forwardStream = null;
        try { this._streamCall?.Dispose(); } catch { }
        this._streamCall = null;
        this._oggWriter = null;
        this._encoder = null;
    }

    public void Dispose()
    {
        if (this.IsRecording)
        {
            try { this._waveIn?.StopRecording(); } catch { }
            // ウィンドウ閉じ等の経路。OnRecordingStopped の完了は待たない (pending の送信は失われる)。
        }
        this.CleanUp();
    }
}
