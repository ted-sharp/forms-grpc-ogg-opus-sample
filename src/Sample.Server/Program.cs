using Microsoft.AspNetCore.Server.Kestrel.Core;
using Sample.Server.Storage;

// 実行時のカレントディレクトリを exe (= AppContext.BaseDirectory) に固定する。
// dotnet run でプロジェクトディレクトリから起動しても、bin\Debug\net10.0\ 直下を
// CWD として扱う。recordings\ ディレクトリや appsettings.json の解決もここを起点にする。
Environment.CurrentDirectory = AppContext.BaseDirectory;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5000, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
    });
});

builder.Services.AddGrpc(options =>
{
    options.MaxReceiveMessageSize = 64 * 1024 * 1024;
    options.MaxSendMessageSize = 64 * 1024 * 1024;
});

builder.Services.AddSingleton<IRecordingStore, FileSystemRecordingStore>();
builder.Services.AddMagicOnion();

var app = builder.Build();

app.MapMagicOnionService();

app.MapGet("/", () => "Sample.Server is running. (MagicOnion over h2c on :5000)");

app.Run();
