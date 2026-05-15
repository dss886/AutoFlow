using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AutoFlow.App.Infrastructure;
using AutoFlow.App.Services;

namespace AutoFlow.App;

public partial class App : System.Windows.Application
{
    private TrayIconService? _trayIconService;
    private ICommand? _showMainWindowCommand;
    private ICommand? _exitApplicationCommand;

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            var window = new MainWindow();
            _showMainWindowCommand = new RelayCommand(() => ShowMainWindow(window));
            _exitApplicationCommand = new RelayCommand(() => ExitApplication(window));
            _trayIconService = new TrayIconService(_showMainWindowCommand, _exitApplicationCommand);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            HandleFatalException("应用启动", ex);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (MainWindow is MainWindow window)
        {
            window.PrepareForExit();
        }

        _trayIconService?.Dispose();
        _trayIconService = null;

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        HandleFatalException("UI 线程", e.Exception);
        e.Handled = true;
        Shutdown(-1);
    }

    private void OnCurrentDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            HandleFatalException("应用域", exception);
            return;
        }

        var fallbackException = new InvalidOperationException(
            $"捕获到未知未处理异常对象: {e.ExceptionObject}");
        HandleFatalException("应用域", fallbackException);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        HandleNonFatalException("后台任务", e.Exception);
        e.SetObserved();
    }

    private static void HandleFatalException(string source, Exception exception)
    {
        var logFilePath = ExceptionLogService.LogException(source, exception);
        System.Windows.MessageBox.Show(
            $"程序发生未处理异常，已写入日志文件：\n{logFilePath}\n\n{exception.Message}",
            "AutoFlow 启动异常",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static void HandleNonFatalException(string source, Exception exception)
    {
        var logFilePath = ExceptionLogService.LogException(source, exception);
        System.Windows.MessageBox.Show(
            $"后台任务发生异常，已写入日志文件：\n{logFilePath}\n\n{exception.Message}",
            "AutoFlow 后台异常",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void ShowMainWindow(MainWindow window)
    {
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    private void ExitApplication(MainWindow window)
    {
        window.PrepareForExit();
        window.Close();
    }
}
