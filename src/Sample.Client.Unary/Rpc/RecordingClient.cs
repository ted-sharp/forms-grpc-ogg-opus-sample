using Grpc.Net.Client;
using MagicOnion.Client;
using Sample.Shared;

namespace Sample.Client.Unary.Rpc;

/// <summary>
/// MagicOnion v7 + Grpc.Net.Client (.NET 10) でサーバーへ接続する RPC クライアント。
/// </summary>
public sealed class RecordingClient : IDisposable
{
    private readonly GrpcChannel _channel;

    public RecordingClient(string host = "localhost", int port = 5000)
    {
        var address = $"http://{host}:{port}";
        this._channel = GrpcChannel.ForAddress(address, new GrpcChannelOptions
        {
            MaxReceiveMessageSize = 64 * 1024 * 1024,
            MaxSendMessageSize = 64 * 1024 * 1024,
        });
        this.Service = MagicOnionClient.Create<IRecordingService>(this._channel);
    }

    public IRecordingService Service { get; }

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
