using Microsoft.AspNetCore.Server.Kestrel.Core;
using Sample.Server.Storage;

// 実行時のカレントディレクトリを exe (= AppContext.BaseDirectory) に固定する。
// dotnet run でプロジェクトディレクトリから起動しても、bin\Debug\net10.0\ 直下を
// CWD として扱う。recordings\ ディレクトリや appsettings.json の解決もここを起点にする。
Environment.CurrentDirectory = AppContext.BaseDirectory;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options =>
{
    // ここで明示的に Listen することで Properties/launchSettings.json の applicationUrl を上書きする。
    // 0.0.0.0:5000 で待ち受けるので LAN 内の他マシンからもアクセス可能。
    // Protocols = Http2 のみ = 平文 HTTP/2 (h2c)。TLS なし。
    // gRPC のためには HTTP/2 が必須だが、h2c は学習サンプル限定の構成。本番では TLS を必ず付けること。
    options.ListenAnyIP(5000, listenOptions =>
    {
        listenOptions.Protocols = HttpProtocols.Http2;
    });
});

builder.Services.AddGrpc(options =>
{
    // gRPC のメッセージサイズ上限を 64 MB に拡張 (既定は 4 MB)。
    // Download メソッドが Ogg Opus ファイル全体を 1 メッセージで返すため、長尺録音だと既定値を超える。
    // クライアント側 (RecordingClient) でも同じ値を設定して両端を揃えている。
    options.MaxReceiveMessageSize = 64 * 1024 * 1024;
    options.MaxSendMessageSize = 64 * 1024 * 1024;
});

builder.Services.AddSingleton<IRecordingStore, FileSystemRecordingStore>();
builder.Services.AddMagicOnion();

var app = builder.Build();

app.MapMagicOnionService();

app.MapGet("/", () => "Sample.Server is running. (MagicOnion over h2c on :5000)");

app.Run();
