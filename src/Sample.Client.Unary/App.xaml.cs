using System.Windows;

namespace Sample.Client.Unary;

public partial class App : Application
{
    public App()
    {
        // Grpc.Net.Client から平文 HTTP/2 (h2c) で接続するために必須。
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }
}
