using Grpc.Net.Client;
using MagicOnion.Client;
using Sample.Shared;

namespace Sample.Client.Streaming.Rpc;

/// <summary>
/// MagicOnion v7 + Grpc.Net.Client (.NET 10) でサーバーへ接続する RPC クライアント。
/// h2c での接続は App.xaml.cs で <c>Http2UnencryptedSupport</c> スイッチを有効にしている前提。
/// </summary>
public sealed class RecordingClient : IDisposable
{
    private readonly GrpcChannel _channel;

    public RecordingClient(string host = "localhost", int port = 5000)
    {
        var address = $"http://{host}:{port}";
        this._channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            // サーバー側と同じ 64 MB に揃える (Download で長尺ファイルが返るケース用)。
            MaxReceiveMessageSize = 64 * 1024 * 1024,
            MaxSendMessageSize = 64 * 1024 * 1024,
        });
        this.Service = MagicOnionClient.Create<IRecordingService>(this._channel);
    }

    public IRecordingService Service { get; }

    /// <summary>
    /// HTTP/2 コネクションを事前に確立する。録音開始の最初の ClientStreaming WriteAsync が
    /// コネクション確立待ちで遅延するのを避ける目的で、念のため事前接続させている。
    /// </summary>
    public async Task ConnectAsync(TimeSpan? timeout = null)
    {
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));
        await this._channel.ConnectAsync(cts.Token).ConfigureAwait(false);
    }

    public void Dispose()
    {
        try
        {
            this._channel.ShutdownAsync().GetAwaiter().GetResult();
            this._channel.Dispose();
        }
        catch
        {
            // ignore shutdown errors
        }
    }
}
