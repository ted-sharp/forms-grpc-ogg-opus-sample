using System.Windows;

namespace Sample.Client.Streaming;

public partial class App : Application
{
    public App()
    {
        // Grpc.Net.Client から平文 HTTP/2 (h2c) で接続するために必須。
        // サーバーは Kestrel で TLS なしの HTTP/2 を待っているので、これを設定しないと
        // GrpcChannel.ForAddress("http://...") が HTTP/1.1 にフォールバックして gRPC が失敗する。
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }
}
