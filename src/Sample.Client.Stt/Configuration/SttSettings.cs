using System.IO;
using System.Text.Json;

namespace Sample.Client.Stt.Configuration;

/// <summary>
/// appsettings.json + 環境変数オーバーライドで構築する設定。
/// .NET 10 標準の System.Text.Json で直接デシリアライズしている。
/// </summary>
public sealed class SttSettings
{
    public AzureSection Azure { get; set; } = new AzureSection();
    public ModelsSection Models { get; set; } = new ModelsSection();
    public ServerSection Server { get; set; } = new ServerSection();

    public sealed class AzureSection
    {
        public string Key { get; set; } = String.Empty;
        public string Region { get; set; } = "japaneast";
    }

    public sealed class ModelsSection
    {
        public string RootDir { get; set; } = String.Empty;
        public string MoonshineDir { get; set; } = "sherpa-onnx-moonshine-base-ja-quantized-2026-02-27";
        public string WhisperDir { get; set; } = "sherpa-onnx-whisper-large-v3";
    }

    public sealed class ServerSection
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 5000;
    }

    public static SttSettings Load()
    {
        var settings = new SttSettings();

        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<SttSettings>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                });
                if (loaded != null) settings = loaded;
            }
            catch (JsonException)
            {
                // 壊れた JSON はデフォルトに退避
            }
        }

        // 環境変数で上書き (.env 風の単純な命名規約に揃える)
        var key = Environment.GetEnvironmentVariable("AZURE_SPEECH_KEY");
        if (!String.IsNullOrEmpty(key)) settings.Azure.Key = key;
        var region = Environment.GetEnvironmentVariable("AZURE_SPEECH_REGION");
        if (!String.IsNullOrEmpty(region)) settings.Azure.Region = region;

        return settings;
    }

    /// <summary>
    /// Sherpa モデルのディレクトリを解決する。
    /// 1. Models:RootDir が指定されていればそれを優先
    /// 2. AppContext.BaseDirectory から上に向かって "models" フォルダを探索
    ///    (bin/Debug/net10.0-windows → bin/Debug → bin → プロジェクト → src → ソリューションルート の 6 階層必要なので余裕を持って 10 階層まで)
    /// 3. なければ AppContext.BaseDirectory/models
    /// </summary>
    public string ResolveModelDir(string subDir)
    {
        if (String.IsNullOrEmpty(subDir))
        {
            throw new ArgumentException("サブディレクトリ名が指定されていません。", nameof(subDir));
        }

        string root;
        if (!String.IsNullOrEmpty(this.Models.RootDir))
        {
            root = Path.IsPathRooted(this.Models.RootDir)
                ? this.Models.RootDir
                : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, this.Models.RootDir));
        }
        else
        {
            root = FindModelsRootByWalkingUp() ?? Path.Combine(AppContext.BaseDirectory, "models");
        }

        return Path.Combine(root, subDir);
    }

    private static string? FindModelsRootByWalkingUp()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir != null; i++)
        {
            var candidate = Path.Combine(dir.FullName, "models");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        return null;
    }
}
