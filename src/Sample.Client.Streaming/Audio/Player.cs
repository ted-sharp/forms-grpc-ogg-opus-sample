using System;
using System.IO;
using System.Threading.Tasks;
using Concentus.Oggfile;
using Concentus.Structs;
using NAudio.Wave;
using Sample.Shared;
using Sample.Shared.Dto;

namespace Sample.Client.Streaming.Audio
{
    /// <summary>
    /// サーバーから Ogg Opus を取得し、メモリ上で全 PCM に展開して再生する。
    /// シーク・一時停止・停止に対応。
    /// </summary>
    public sealed class Player : IDisposable
    {
        private WaveOutEvent _waveOut;
        private RawSourceWaveStream _pcmStream;
        private MemoryStream _pcmBytes;

        public event EventHandler PlaybackStopped;

        public bool IsLoaded => _pcmStream != null;

        public bool IsPlaying => _waveOut != null && _waveOut.PlaybackState == PlaybackState.Playing;

        public bool IsPaused => _waveOut != null && _waveOut.PlaybackState == PlaybackState.Paused;

        public TimeSpan TotalTime => _pcmStream != null
            ? TimeSpan.FromSeconds((double)_pcmStream.Length / AudioConstants.BytesPerSecond)
            : TimeSpan.Zero;

        public TimeSpan CurrentTime
        {
            get => _pcmStream != null
                ? TimeSpan.FromSeconds((double)_pcmStream.Position / AudioConstants.BytesPerSecond)
                : TimeSpan.Zero;
            set
            {
                if (_pcmStream == null) return;
                long bytePos = (long)(value.TotalSeconds * AudioConstants.BytesPerSecond);
                if (bytePos < 0) bytePos = 0;
                if (bytePos > _pcmStream.Length) bytePos = _pcmStream.Length;
                bytePos -= bytePos % AudioConstants.BytesPerSample; // align to 16-bit boundary
                _pcmStream.Position = bytePos;
            }
        }

        public async Task LoadAsync(IRecordingService service)
        {
            var dl = await service.Download(new DownloadRequest());
            if (!dl.Exists || dl.OggOpusBytes == null || dl.OggOpusBytes.Length == 0)
            {
                throw new InvalidOperationException("サーバーに録音ファイルがありません。");
            }

            var decoder = OpusDecoder.Create(AudioConstants.SampleRate, AudioConstants.Channels);
            var pcmBytes = new MemoryStream();
            using (var oggStream = new MemoryStream(dl.OggOpusBytes))
            {
                var oggReader = new OpusOggReadStream(decoder, oggStream);
                while (oggReader.HasNextPacket)
                {
                    short[] samples = oggReader.DecodeNextPacket();
                    if (samples != null && samples.Length > 0)
                    {
                        byte[] bytes = new byte[samples.Length * AudioConstants.BytesPerSample];
                        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
                        pcmBytes.Write(bytes, 0, bytes.Length);
                    }
                }
            }
            pcmBytes.Position = 0;

            DisposeWaveOut();
            _pcmBytes?.Dispose();
            _pcmBytes = pcmBytes;
            _pcmStream = new RawSourceWaveStream(_pcmBytes, new WaveFormat(AudioConstants.SampleRate, AudioConstants.BitsPerSample, AudioConstants.Channels));
        }

        public void Play()
        {
            if (_pcmStream == null) throw new InvalidOperationException("再生対象が読み込まれていません。");

            if (_waveOut == null)
            {
                _waveOut = new WaveOutEvent();
                _waveOut.PlaybackStopped += OnWaveOutStopped;
                _waveOut.Init(_pcmStream);
            }
            _waveOut.Play();
        }

        public void Pause()
        {
            _waveOut?.Pause();
        }

        public void Stop()
        {
            if (_waveOut != null)
            {
                _waveOut.Stop();
            }
            if (_pcmStream != null)
            {
                _pcmStream.Position = 0;
            }
        }

        private void OnWaveOutStopped(object sender, StoppedEventArgs e)
        {
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }

        private void DisposeWaveOut()
        {
            if (_waveOut != null)
            {
                _waveOut.PlaybackStopped -= OnWaveOutStopped;
                try { _waveOut.Dispose(); } catch { }
                _waveOut = null;
            }
        }

        public void Dispose()
        {
            DisposeWaveOut();
            _pcmStream?.Dispose();
            _pcmStream = null;
            _pcmBytes?.Dispose();
            _pcmBytes = null;
        }
    }
}
