using System;
using System.Threading.Tasks;
using Concentus.Enums;
using Concentus.Oggfile;
using Concentus.Structs;
using MagicOnion;
using NAudio.Wave;
using Sample.Shared;
using Sample.Shared.Audio;
using Sample.Shared.Dto;

namespace Sample.Client.Streaming.Audio
{
    /// <summary>
    /// NAudio で録音 → Concentus で Opus + Ogg 化 → ChunkForwardStream 経由で
    /// MagicOnion ClientStreaming にバイト列を流し込む。
    /// </summary>
    public sealed class StreamingRecorder : IDisposable
    {
        private WaveInEvent _waveIn;
        private OpusEncoder _encoder;
        private OpusOggWriteStream _oggWriter;
        private ChunkForwardStream _forwardStream;
        private ClientStreamingResult<RecordingChunk, RecordingResult> _streamCall;
        private VadGate _vadGate;
        private DateTime _startUtc;
        private TaskCompletionSource<object> _stopTcs;

        public bool IsRecording { get; private set; }

        /// <summary>VAD で無音区間をカットするか。StartAsync 前に設定すること。</summary>
        public bool EnableVad { get; set; }

        /// <summary>WebRTC VAD のアグレッシブ度 (0..3、3 が最も厳しい)。StartAsync 前に設定すること。</summary>
        public int VadAggressiveness { get; set; } = 2;

        public TimeSpan Elapsed => IsRecording ? DateTime.UtcNow - _startUtc : TimeSpan.Zero;

        public event EventHandler<RecordingResult> RecordingFinished;
        public event EventHandler<Exception> RecordingFailed;
        /// <summary>
        /// マイクから到着した PCM フレームを波形表示用に通知する。
        /// VAD ゲート前の生のサンプルを渡すので、無音カット中でもメーターは反応する。
        /// イベントハンドラはオーディオキャプチャスレッドから呼ばれるため UI 反映時は Invoke 必須。
        /// 渡す配列はイベントごとに新規確保した使い切りバッファ。
        /// </summary>
        public event EventHandler<AudioFrameEventArgs> AudioFrameAvailable;

        public async Task StartAsync(IRecordingService service)
        {
            if (IsRecording) throw new InvalidOperationException("Already recording");

            _encoder = OpusEncoder.Create(AudioConstants.SampleRate, AudioConstants.Channels, OpusApplication.OPUS_APPLICATION_VOIP);
            _encoder.Bitrate = AudioConstants.BitRate;

            // MagicOnion v4 client: SaveStreaming() returns ClientStreamingResult<,> synchronously.
            _streamCall = service.SaveStreaming();
            await Task.Yield();

            _forwardStream = new ChunkForwardStream(async bytes =>
            {
                System.Diagnostics.Debug.WriteLine($"[StreamingRecorder] WriteAsync chunk={bytes.Length}");
                // ConfigureAwait(false) が必須。Flush() は ChunkForwardStream で .GetAwaiter().GetResult()
                // で同期ブロックされる経路があり (UI スレッドからの warm-up Flush や OnRecordingStopped の Finish)、
                // 内側の await が UI SyncContext を取りに戻ろうとするとデッドロックする。
                await _streamCall.RequestStream.WriteAsync(new RecordingChunk { OggOpusBytes = bytes })
                    .ConfigureAwait(false);
            });
            _oggWriter = new OpusOggWriteStream(_encoder, _forwardStream);

            _vadGate = EnableVad ? new VadGate(VadAggressiveness) : null;

            // OpusOggWriteStream 構築時に OpusHead/OpusTags が ChunkForwardStream に書かれているはずなので、
            // 即フラッシュして gRPC ストリームに最初の WriteAsync を打ち込んでおく。これで遅延起動による
            // 不安定さを排除する。
            System.Diagnostics.Debug.WriteLine("[StreamingRecorder] initial flush to warm up gRPC stream");
            _forwardStream.Flush();

            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(AudioConstants.SampleRate, AudioConstants.BitsPerSample, AudioConstants.Channels),
                BufferMilliseconds = AudioConstants.FrameMilliseconds,
            };
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;

            _startUtc = DateTime.UtcNow;
            IsRecording = true;
            _waveIn.StartRecording();
        }

        /// <summary>
        /// 録音を停止し、サーバーへの送信完了 (RequestStream を Complete してレスポンス受領) まで待機する Task を返す。
        /// </summary>
        public Task StopAsync()
        {
            if (!IsRecording) return Task.CompletedTask;
            var tcs = new TaskCompletionSource<object>();
            _stopTcs = tcs;
            _waveIn?.StopRecording();
            return tcs.Task;
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded <= 0) return;
            try
            {
                int sampleCount = e.BytesRecorded / AudioConstants.BytesPerSample;
                short[] pcm = new short[sampleCount];
                Buffer.BlockCopy(e.Buffer, 0, pcm, 0, e.BytesRecorded);

                AudioFrameAvailable?.Invoke(this, new AudioFrameEventArgs(pcm, sampleCount));

                if (_vadGate != null)
                {
                    _vadGate.Process(pcm, sampleCount, (buf, n) => _oggWriter.WriteSamples(buf, 0, n));
                }
                else
                {
                    _oggWriter.WriteSamples(pcm, 0, sampleCount);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[StreamingRecorder] OnDataAvailable failed: {ex}");
                RecordingFailed?.Invoke(this, new InvalidOperationException($"[Step=OnDataAvailable] {ex.GetType().Name}: {ex.Message}", ex));
                _waveIn?.StopRecording();
            }
        }

        private async void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            Exception captured = null;
            string lastStep = "(start)";
            try
            {
                lastStep = "VadGate.Flush";
                System.Diagnostics.Debug.WriteLine($"[StreamingRecorder] {lastStep}");
                // VAD ゲートが Open 状態のまま録音終了した場合の端数フレームを吐き出す。
                // Finish() の後に WriteSamples を呼ぶと内部 Stream が Close 済みで死ぬので
                // 必ず Finish() の前に行う。
                _vadGate?.Flush((buf, n) => _oggWriter.WriteSamples(buf, 0, n));

                lastStep = "OggWriter.Finish";
                System.Diagnostics.Debug.WriteLine($"[StreamingRecorder] {lastStep}");
                // OpusOggWriteStream.Finish() は内部で _outputStream.Flush() + Close() を
                // 呼ぶ。我々の ChunkForwardStream.Flush() は同期的に gRPC WriteAsync を完了
                // させるので、この時点で Ogg Opus データはサーバーへの request stream に
                // すべて流し込み終わっている。したがって追加の CompleteAsync 呼び出しは不要。
                _oggWriter?.Finish();

                lastStep = "RequestStream.CompleteAsync (END_STREAM)";
                System.Diagnostics.Debug.WriteLine($"[StreamingRecorder] {lastStep}");
                if (_streamCall.RequestStream != null)
                {
                    await _streamCall.RequestStream.CompleteAsync();
                }

                lastStep = "ResponseAsync (await server response)";
                System.Diagnostics.Debug.WriteLine($"[StreamingRecorder] {lastStep}");
                var result = await _streamCall.ResponseAsync;
                System.Diagnostics.Debug.WriteLine($"[StreamingRecorder] response received: success={result?.Success}");

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
                System.Diagnostics.Debug.WriteLine($"[StreamingRecorder] FAIL at step '{lastStep}': {ex}");
                captured = new InvalidOperationException($"[Step={lastStep}] {ex.GetType().Name}: {ex.Message}", ex);
                RecordingFailed?.Invoke(this, captured);
            }
            finally
            {
                IsRecording = false;
                CleanUp();
                var tcs = _stopTcs;
                _stopTcs = null;
                if (tcs != null)
                {
                    if (captured != null) tcs.TrySetException(captured);
                    else tcs.TrySetResult(null);
                }
            }
        }

        private void CleanUp()
        {
            try { _waveIn?.Dispose(); } catch { }
            _waveIn = null;
            try { _vadGate?.Dispose(); } catch { }
            _vadGate = null;
            try { _forwardStream?.Dispose(); } catch { }
            _forwardStream = null;
            try { _streamCall.Dispose(); } catch { }
            _oggWriter = null;
            _encoder = null;
        }

        public void Dispose()
        {
            if (IsRecording)
            {
                try { _waveIn?.StopRecording(); } catch { }
                // OnRecordingStopped の完了を待たずに破棄するパス。
                // フォーム閉じ時など。pending の送信は失われる。
            }
            CleanUp();
        }
    }
}
