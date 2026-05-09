using System;
using System.Threading.Tasks;
using Grpc.Core;
using MagicOnion.Client;
using Sample.Shared;

namespace Sample.Client.Streaming.Rpc
{
    public sealed class RecordingClient : IDisposable
    {
        private readonly Channel _channel;

        public RecordingClient(string host = "localhost", int port = 5000)
        {
            var options = new[]
            {
                new ChannelOption(ChannelOptions.MaxReceiveMessageLength, 64 * 1024 * 1024),
                new ChannelOption(ChannelOptions.MaxSendMessageLength, 64 * 1024 * 1024),
            };
            this._channel = new Channel(host, port, ChannelCredentials.Insecure, options);
            this.Service = MagicOnionClient.Create<IRecordingService>(this._channel);
        }

        public IRecordingService Service { get; }

        /// <summary>
        /// HTTP/2 コネクションを事前に確立する。ClientStreaming で初回 WriteAsync が
        /// 遅延した場合に gRPC.Core が状態を不安定にすることを避ける目的。
        /// </summary>
        public Task ConnectAsync(TimeSpan? deadline = null)
        {
            var dl = DateTime.UtcNow.Add(deadline ?? TimeSpan.FromSeconds(5));
            return this._channel.ConnectAsync(dl);
        }

        public void Dispose()
        {
            try
            {
                this._channel.ShutdownAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // ignore shutdown errors
            }
        }
    }
}
