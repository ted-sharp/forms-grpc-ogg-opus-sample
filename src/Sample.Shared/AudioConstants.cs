namespace Sample.Shared
{
    public static class AudioConstants
    {
        public const int SampleRate = 48000;

        public const int Channels = 1;

        public const int BitsPerSample = 16;

        public const int FrameMilliseconds = 20;

        public const int FrameSizePerChannel = SampleRate * FrameMilliseconds / 1000;

        public const int BytesPerSample = BitsPerSample / 8;

        public const int BytesPerSecond = SampleRate * Channels * BytesPerSample;

        public const int BitRate = 64000;
    }
}
