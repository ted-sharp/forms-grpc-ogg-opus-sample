namespace Sample.Client.Unary.Audio;

/// <summary>マイクから届いた 1 フレーム分の 16-bit signed mono PCM を運ぶイベント引数。</summary>
public sealed class AudioFrameEventArgs : EventArgs
{
    public AudioFrameEventArgs(short[] pcm, int sampleCount)
    {
        this.Pcm = pcm;
        this.SampleCount = sampleCount;
    }

    public short[] Pcm { get; }
    public int SampleCount { get; }
}
