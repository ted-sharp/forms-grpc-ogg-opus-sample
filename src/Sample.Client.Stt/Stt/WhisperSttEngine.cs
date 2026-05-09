using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sample.Client.Stt.Configuration;
using SherpaOnnx;

namespace Sample.Client.Stt.Stt
{
    /// <summary>
    /// sherpa-onnx + Whisper large-v3 (int8 量子化) でオフライン文字起こしを行うエンジン。
    /// モデルは models/sherpa-onnx-whisper-large-v3/ に配置されている前提。
    /// 多言語対応で日本語精度が高い反面、推論が重い (encoder + decoder で int8 でも 1.8GB)。
    /// </summary>
    public sealed class WhisperSttEngine : ISttEngine
    {
        private readonly OfflineRecognizer _recognizer;

        public SttEngineKind Kind => SttEngineKind.WhisperLargeV3;

        public WhisperSttEngine(SttSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var dir = settings.ResolveModelDir(settings.Models.WhisperDir);
            if (!Directory.Exists(dir))
            {
                throw new DirectoryNotFoundException(
                    $"Whisper モデルディレクトリが見つかりません: {dir}\n" +
                    "ソリューションルートで `task download-models` を実行してください。");
            }

            var encoder = Path.Combine(dir, "large-v3-encoder.int8.onnx");
            var decoder = Path.Combine(dir, "large-v3-decoder.int8.onnx");
            var tokens = Path.Combine(dir, "large-v3-tokens.txt");

            EnsureFile(encoder);
            EnsureFile(decoder);
            EnsureFile(tokens);

            var config = new OfflineRecognizerConfig();
            // Whisper も SampleRate=16000 / FeatureDim=80 (sherpa-onnx 内部で Whisper 用 80→128 変換)。
            // 公式 dotnet-examples の設定に合わせる。
            config.FeatConfig.SampleRate = 16000;
            config.FeatConfig.FeatureDim = 80;
            config.ModelConfig.Whisper.Encoder = encoder;
            config.ModelConfig.Whisper.Decoder = decoder;
            config.ModelConfig.Whisper.Language = "ja";
            config.ModelConfig.Whisper.Task = "transcribe";
            config.ModelConfig.Tokens = tokens;
            config.ModelConfig.NumThreads = 4;
            config.ModelConfig.Provider = "cpu";
            config.ModelConfig.Debug = 0;
            config.DecodingMethod = "greedy_search";

            this._recognizer = new OfflineRecognizer(config);
        }

        public Task<string> TranscribeAsync(AudioInput input, IProgress<string> progress, CancellationToken ct)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));

            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                using (var stream = this._recognizer.CreateStream())
                {
                    stream.AcceptWaveform(16000, input.Pcm16kMono);
                    ct.ThrowIfCancellationRequested();
                    this._recognizer.Decode(stream);
                    return stream.Result.Text ?? String.Empty;
                }
            }, ct);
        }

        public void Dispose()
        {
            this._recognizer?.Dispose();
        }

        private static void EnsureFile(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"Whisper モデルファイルが見つかりません: {path}", path);
            }
        }
    }
}
