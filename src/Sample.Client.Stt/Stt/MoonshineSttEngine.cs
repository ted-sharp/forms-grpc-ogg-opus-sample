using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sample.Client.Stt.Configuration;
using SherpaOnnx;

namespace Sample.Client.Stt.Stt
{
    /// <summary>
    /// sherpa-onnx + Moonshine ja base モデルでオフライン文字起こしを行うエンジン。
    /// モデルは models/sherpa-onnx-moonshine-base-ja-quantized-2026-02-27/ に配置されている前提。
    /// </summary>
    public sealed class MoonshineSttEngine : ISttEngine
    {
        private readonly OfflineRecognizer _recognizer;

        public SttEngineKind Kind => SttEngineKind.Moonshine;

        public MoonshineSttEngine(SttSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var dir = settings.ResolveModelDir(settings.Models.MoonshineDir);
            if (!Directory.Exists(dir))
            {
                throw new DirectoryNotFoundException(
                    $"Moonshine モデルディレクトリが見つかりません: {dir}\n" +
                    "ソリューションルートで `task download-models` を実行してください。");
            }

            // Moonshine v2 (2026-02-27 リリース) は encoder + merged_decoder の 2 ファイル構成、
            // 拡張子は .ort (ONNX Runtime 最適化形式)。v1 の Preprocessor/UncachedDecoder/CachedDecoder は使わない。
            var encoder = Path.Combine(dir, "encoder_model.ort");
            var decoderMerged = Path.Combine(dir, "decoder_model_merged.ort");
            var tokens = Path.Combine(dir, "tokens.txt");

            EnsureFile(encoder);
            EnsureFile(decoderMerged);
            EnsureFile(tokens);

            var config = new OfflineRecognizerConfig();
            config.FeatConfig.SampleRate = 16000;
            config.FeatConfig.FeatureDim = 80;
            config.ModelConfig.Moonshine.Encoder = encoder;
            config.ModelConfig.Moonshine.MergedDecoder = decoderMerged;
            config.ModelConfig.Tokens = tokens;
            config.ModelConfig.NumThreads = 1;
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
                    $"Moonshine モデルファイルが見つかりません: {path}", path);
            }
        }
    }
}
