using System;
using Grpc.Core;
using MagicOnion.Client;
using Sample.Shared;

namespace Sample.Client.Unary.Rpc
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
            _channel = new Channel(host, port, ChannelCredentials.Insecure, options);
            Service = MagicOnionClient.Create<IRecordingService>(_channel);
        }

        public IRecordingService Service { get; }

        public void Dispose()
        {
            try
            {
                _channel.ShutdownAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // ignore shutdown errors
            }
        }
    }
}
