using System.IO;
using System.Text.Json;

namespace Sample.Client.Unary.Configuration;

/// <summary>
/// appsettings.json を System.Text.Json で直接読む簡素な設定ローダ。
/// </summary>
public sealed class AppSettings
{
    public ServerSection Server { get; set; } = new();

    public sealed class ServerSection
    {
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 5000;
    }

    public static AppSettings Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (!File.Exists(path)) return new AppSettings();

        var json = File.ReadAllText(path);
        try
        {
            return JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            }) ?? new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }
}
