using System;
using System.IO;
using System.Threading.Tasks;
using Concentus.Enums;
using Concentus.Oggfile;
using Concentus.Structs;
using NAudio.Wave;
using Sample.Shared;
using Sample.Shared.Dto;

namespace Sample.Client.Unary.Audio
{
    /// <summary>
    /// NAudio で録音 → Concentus で Opus + Ogg 化 (MemoryStream 上に蓄積) →
    /// 録音停止時に SaveUnary で全バイトを一括送信する。
    /// </summary>
    public sealed class UnaryRecorder : IDisposable
    {
        private WaveInEvent _waveIn;
        private OpusEncoder _encoder;
        private OpusOggWriteStream _oggWriter;
        private MemoryStream _buffer;
        private IRecordingService _service;
        private DateTime _startUtc;
        private TaskCompletionSource<object> _stopTcs;

        public bool IsRecording { get; private set; }

        public TimeSpan Elapsed => IsRecording ? DateTime.UtcNow - _startUtc : TimeSpan.Zero;

        public event EventHandler<RecordingResult> RecordingFinished;
        public event EventHandler<Exception> RecordingFailed;

        public Task StartAsync(IRecordingService service)
        {
            if (IsRecording) throw new InvalidOperationException("Already recording");

            _service = service ?? throw new ArgumentNullException(nameof(service));
            _encoder = OpusEncoder.Create(AudioConstants.SampleRate, AudioConstants.Channels, OpusApplication.OPUS_APPLICATION_VOIP);
            _encoder.Bitrate = AudioConstants.BitRate;

            _buffer = new MemoryStream();
            _oggWriter = new OpusOggWriteStream(_encoder, _buffer);

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
            return Task.CompletedTask;
        }

        /// <summary>
        /// 録音を停止し、サーバーへの送信完了 (SaveUnary レスポンス受領) まで待機する Task を返す。
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
                _oggWriter.WriteSamples(pcm, 0, sampleCount);
            }
            catch (Exception ex)
            {
                RecordingFailed?.Invoke(this, ex);
                _waveIn?.StopRecording();
            }
        }

        private async void OnRecordingStopped(object sender, StoppedEventArgs e)
        {
            Exception captured = null;
            try
            {
                // 1) Ogg トレーラを書き出す。残サンプルがパディングされて _buffer に書き込まれる。
                _oggWriter?.Finish();

                // 2) MemoryStream の中身を独立した byte[] にコピー (この後 _buffer を Dispose しても安全)。
                byte[] bytes = _buffer != null ? _buffer.ToArray() : Array.Empty<byte>();

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
                var result = await _service.SaveUnary(new SaveUnaryRequest { OggOpusBytes = bytes });
                RecordingFinished?.Invoke(this, result);
            }
            catch (Exception ex)
            {
                captured = ex;
                RecordingFailed?.Invoke(this, ex);
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
            try { _buffer?.Dispose(); } catch { }
            _buffer = null;
            _oggWriter = null;
            _encoder = null;
        }

        public void Dispose()
        {
            if (IsRecording)
            {
                try { _waveIn?.StopRecording(); } catch { }
                // 注意: フォーム閉じ等で呼ばれる経路。OnRecordingStopped の送信完了は待たない。
            }
            CleanUp();
        }
    }
}
