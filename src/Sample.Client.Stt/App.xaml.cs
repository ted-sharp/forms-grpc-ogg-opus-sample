using System.Windows;

namespace Sample.Client.Stt;

public partial class App : Application
{
    public App()
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
    }
}
