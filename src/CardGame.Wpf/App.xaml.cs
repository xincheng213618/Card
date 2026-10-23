using System.Windows;

namespace CardGame.Wpf;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            if (MainWindow?.DataContext is ViewModels.MainViewModel model)
                model.RecordExceptionReport(args.Exception, "DispatcherUnhandledException");
            else WriteUnhandledReport(args.Exception, "DispatcherUnhandledException");
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteUnhandledReport(args.ExceptionObject as Exception ??
                new InvalidOperationException(args.ExceptionObject?.ToString()), "AppDomain.UnhandledException");
        TaskScheduler.UnobservedTaskException += (_, args) =>
            WriteUnhandledReport(args.Exception, "TaskScheduler.UnobservedTaskException");
    }

    private static void WriteUnhandledReport(Exception exception, string operation)
    {
        try { new Diagnostics.FileGameExceptionReportStore().Write(exception, operation); }
        catch { /* Preserve the original unhandled exception and process behavior. */ }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length >= 1 && e.Args[0] == "--lan")
        {
            base.OnStartup(e);
            MainWindow = new LanRoomWindow(serverAddress: e.Args.Length == 2 ? e.Args[1] : null);
            MainWindow.Show();
            return;
        }
        if (e.Args.Length > 0 && e.Args[0] == "--verify-package")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            base.OnStartup(e);
            Shutdown(e.Args.Length == 2 ? Diagnostics.PackageVerification.Run(e.Args[1]) : 2);
            return;
        }
        if (e.Args.Length == 1 && e.Args[0] == "--effects-demo")
        {
            base.OnStartup(e);
            MainWindow = new BattleEffectsPreviewWindow();
            MainWindow.Show();
            return;
        }
        StartupUri = new Uri("MainWindow.xaml", UriKind.Relative);
        base.OnStartup(e);
    }
}
