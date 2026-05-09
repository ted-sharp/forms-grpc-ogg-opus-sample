using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Sample.Client.Stt.Configuration;

namespace Sample.Client.Stt.Stt
{
    /// <summary>
    /// Azure Speech SDK でクラウド側 STT を行うエンジン。
    /// 長尺対応のため StartContinuousRecognitionAsync を使い、
    /// Recognized イベントごとに progress.Report で部分結果を UI に逐次転送する。
    /// </summary>
    public sealed class AzureSttEngine : ISttEngine
    {
        private readonly SttSettings _settings;

        public SttEngineKind Kind => SttEngineKind.Azure;

        public AzureSttEngine(SttSettings settings)
        {
            this._settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        public async Task<string> TranscribeAsync(AudioInput input, IProgress<string> progress, CancellationToken ct)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (String.IsNullOrEmpty(this._settings.Azure.Key))
            {
                throw new InvalidOperationException(
                    "Azure キーが未設定です。AZURE_SPEECH_KEY 環境変数か appsettings.json の Azure:Key を設定してください。");
            }
            if (String.IsNullOrEmpty(this._settings.Azure.Region))
            {
                throw new InvalidOperationException(
                    "Azure リージョンが未設定です。AZURE_SPEECH_REGION 環境変数か appsettings.json の Azure:Region を設定してください。");
            }

            var speechConfig = SpeechConfig.FromSubscription(this._settings.Azure.Key, this._settings.Azure.Region);
            speechConfig.SpeechRecognitionLanguage = "ja-JP";

            using (var audioConfig = AudioConfig.FromWavFileInput(input.WavPath))
            using (var recognizer = new SpeechRecognizer(speechConfig, audioConfig))
            {
                var sb = new StringBuilder();
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                recognizer.Recognized += (s, e) =>
                {
                    if (e.Result.Reason == ResultReason.RecognizedSpeech && !String.IsNullOrEmpty(e.Result.Text))
                    {
                        sb.AppendLine(e.Result.Text);
                        progress?.Report(e.Result.Text);
                    }
                };

                recognizer.SessionStopped += (s, e) =>
                {
                    tcs.TrySetResult(true);
                };

                recognizer.Canceled += (s, e) =>
                {
                    if (e.Reason == CancellationReason.Error)
                    {
                        tcs.TrySetException(new InvalidOperationException(
                            $"Azure 認識エラー: {e.ErrorCode} {e.ErrorDetails}"));
                    }
                    else
                    {
                        tcs.TrySetResult(true);
                    }
                };

                using (ct.Register(() => tcs.TrySetCanceled()))
                {
                    await recognizer.StartContinuousRecognitionAsync().ConfigureAwait(false);
                    try
                    {
                        await tcs.Task.ConfigureAwait(false);
                    }
                    finally
                    {
                        try
                        {
                            await recognizer.StopContinuousRecognitionAsync().ConfigureAwait(false);
                        }
                        catch
                        {
                            // 停止失敗は無視
                        }
                    }
                }

                return sb.ToString().TrimEnd();
            }
        }

        public void Dispose()
        {
            // SpeechConfig / Recognizer は TranscribeAsync 内でスコープ管理するため、
            // ここで保持・破棄する状態は無い。
        }
    }
}
