using System.IO;
using Concentus.Oggfile;
using Concentus.Structs;
using NAudio.Wave;
using Sample.Shared;
using Sample.Shared.Dto;

namespace Sample.Client.Streaming.Audio;

/// <summary>
/// サーバーから Ogg Opus を取得し、メモリ上で全 PCM に展開して再生する。
/// シーク・一時停止・停止に対応。
/// </summary>
public sealed class Player : IDisposable
{
    private WaveOutEvent? _waveOut;
    private RawSourceWaveStream? _pcmStream;
    private MemoryStream? _pcmBytes;

    public event EventHandler? PlaybackStopped;

    public bool IsLoaded => this._pcmStream != null;

    public bool IsPlaying => this._waveOut != null && this._waveOut.PlaybackState == PlaybackState.Playing;

    public bool IsPaused => this._waveOut != null && this._waveOut.PlaybackState == PlaybackState.Paused;

    public TimeSpan TotalTime => this._pcmStream != null
        ? TimeSpan.FromSeconds((double)this._pcmStream.Length / AudioConstants.BytesPerSecond)
        : TimeSpan.Zero;

    public TimeSpan CurrentTime
    {
        get => this._pcmStream != null
            ? TimeSpan.FromSeconds((double)this._pcmStream.Position / AudioConstants.BytesPerSecond)
            : TimeSpan.Zero;
        set
        {
            if (this._pcmStream == null) return;
            long bytePos = (long)(value.TotalSeconds * AudioConstants.BytesPerSecond);
            if (bytePos < 0) bytePos = 0;
            if (bytePos > this._pcmStream.Length) bytePos = this._pcmStream.Length;
            bytePos -= bytePos % AudioConstants.BytesPerSample; // align to 16-bit boundary
            this._pcmStream.Position = bytePos;
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

        this.DisposeWaveOut();
        this._pcmBytes?.Dispose();
        this._pcmBytes = pcmBytes;
        this._pcmStream = new RawSourceWaveStream(this._pcmBytes, new WaveFormat(AudioConstants.SampleRate, AudioConstants.BitsPerSample, AudioConstants.Channels));
    }

    public void Play()
    {
        if (this._pcmStream == null) throw new InvalidOperationException("再生対象が読み込まれていません。");

        if (this._waveOut == null)
        {
            this._waveOut = new WaveOutEvent();
            this._waveOut.PlaybackStopped += this.OnWaveOutStopped;
            this._waveOut.Init(this._pcmStream);
        }
        this._waveOut.Play();
    }

    public void Pause()
    {
        this._waveOut?.Pause();
    }

    public void Stop()
    {
        this._waveOut?.Stop();
        if (this._pcmStream != null)
        {
            this._pcmStream.Position = 0;
        }
    }

    private void OnWaveOutStopped(object? sender, StoppedEventArgs e)
    {
        PlaybackStopped?.Invoke(this, EventArgs.Empty);
    }

    private void DisposeWaveOut()
    {
        if (this._waveOut != null)
        {
            this._waveOut.PlaybackStopped -= this.OnWaveOutStopped;
            try { this._waveOut.Dispose(); } catch { }
            this._waveOut = null;
        }
    }

    public void Dispose()
    {
        this.DisposeWaveOut();
        this._pcmStream?.Dispose();
        this._pcmStream = null;
        this._pcmBytes?.Dispose();
        this._pcmBytes = null;
    }
}
