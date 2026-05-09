using System;
using System.IO;
using NAudio.Wave;

namespace Sample.Client.Stt.Audio
{
    /// <summary>
    /// 16kHz mono の float PCM を、Azure Speech SDK が確実に受け付ける
    /// PCM 16-bit / 16000 / mono の WAV ファイルとして %TEMP% に書き出す。
    /// </summary>
    public static class WavWriter
    {
        private const int SampleRate = 16000;
        private const int BitsPerSample = 16;
        private const int Channels = 1;

        public static string Write16kMonoWav(float[] pcm16kMono)
        {
            if (pcm16kMono == null) throw new ArgumentNullException(nameof(pcm16kMono));

            var path = Path.Combine(Path.GetTempPath(), $"sample-stt-{Guid.NewGuid():N}.wav");

            var byteBuffer = new byte[pcm16kMono.Length * 2];
            for (var i = 0; i < pcm16kMono.Length; i++)
            {
                var v = (int)Math.Round(pcm16kMono[i] * 32767f);
                if (v > Int16.MaxValue) v = Int16.MaxValue;
                if (v < Int16.MinValue) v = Int16.MinValue;
                var s = (short)v;
                byteBuffer[i * 2] = (byte)(s & 0xFF);
                byteBuffer[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
            }

            using (var w = new WaveFileWriter(path, new WaveFormat(SampleRate, BitsPerSample, Channels)))
            {
                w.Write(byteBuffer, 0, byteBuffer.Length);
            }
            return path;
        }

        /// <summary>
        /// 起動時に呼ぶ。24 時間以上前の sample-stt-*.wav を %TEMP% から掃除する。
        /// クラッシュ時に残ったゴミを回収するための軽量ハウスキーパー。
        /// </summary>
        public static void CleanupOldTempFiles()
        {
            try
            {
                var temp = Path.GetTempPath();
                var threshold = DateTime.UtcNow.AddHours(-24);
                foreach (var file in Directory.EnumerateFiles(temp, "sample-stt-*.wav"))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < threshold)
                        {
                            File.Delete(file);
                        }
                    }
                    catch
                    {
                        // 個別ファイルの失敗は無視
                    }
                }
            }
            catch
            {
                // 掃除失敗は無視 (起動を妨げない)
            }
        }
    }
}
