namespace TinyNoti.App;

public partial class App : System.Windows.Application
{
    private AppController? _controller;

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown;

        _controller = new AppController();
        await _controller.StartAsync(showMainWindow: !IsStartupTaskLaunch(e));
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _controller?.Dispose();
        base.OnExit(e);
    }

    private static bool IsStartupTaskLaunch(System.Windows.StartupEventArgs e)
    {
        if (e.Args.Any(static arg => string.Equals(arg, "--silent", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        try
        {
            return global::Windows.ApplicationModel.AppInstance.GetActivatedEventArgs()?.Kind
                == global::Windows.ApplicationModel.Activation.ActivationKind.StartupTask;
        }
        catch
        {
            return false;
        }
    }
}
