using System.Windows;

namespace CardGame.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length > 0 && e.Args[0] == "--verify-package")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            Shutdown(e.Args.Length == 2 ? Diagnostics.PackageVerification.Run(e.Args[1]) : 2);
            return;
        }
        StartupUri = new Uri("MainWindow.xaml", UriKind.Relative);
        base.OnStartup(e);
    }
}
