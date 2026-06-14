using System.Windows;
using System.Windows.Threading;
using AutoFlow.App.Infrastructure;
using AutoFlow.App.Models;
using AutoFlow.App.Services;
using AutoFlow.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AutoFlow.App;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;
    private readonly List<IDisposable> _eventSubscriptions = [];

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
            _serviceProvider = CreateServiceProvider();
            var eventBus = _serviceProvider.GetRequiredService<IEventBus>();
            var window = _serviceProvider.GetRequiredService<MainWindow>();
            _ = _serviceProvider.GetRequiredService<TrayIconService>();
            SubscribeApplicationMessages(eventBus, window);
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

        foreach (var subscription in _eventSubscriptions)
        {
            subscription.Dispose();
        }

        _eventSubscriptions.Clear();
        _serviceProvider?.Dispose();
        _serviceProvider = null;

        base.OnExit(e);
    }

    private static ServiceProvider CreateServiceProvider()
    {
        var services = new ServiceCollection();
        // Services
        services.AddSingleton<AppLoggerService>();
        services.AddSingleton<AppSoundService>();
        services.AddSingleton<AutomationInputService>();
        services.AddSingleton<ExceptionLogService>();
        services.AddSingleton<GlobalHotkeyService>();
        services.AddSingleton<GlobalMouseHookService>();
        services.AddSingleton<LocalSettingsService>();
        services.AddSingleton<LuaRuntimeService>();
        services.AddSingleton<PathService>();
        services.AddSingleton<ScreenCaptureService>();
        services.AddSingleton<ScreenColorService>();
        services.AddSingleton<ScreenNumberRecognitionService>();
        services.AddSingleton<ScriptCatalogService>();
        services.AddSingleton<ScriptRunnerService>();
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<WindowControlService>();
        // Utils and view models
        services.AddSingleton<IEventBus, EventBus>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider();
    }

    private void SubscribeApplicationMessages(IEventBus eventBus, MainWindow window)
    {
        _eventSubscriptions.Add(eventBus.Subscribe<ShowMainWindowRequestedMessage>(_ => ShowMainWindow(window)));
        _eventSubscriptions.Add(eventBus.Subscribe<ExitApplicationRequestedMessage>(_ => ExitApplication(window)));
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

    private void HandleFatalException(string source, Exception exception)
    {
        var logFilePath = GetExceptionLogService().LogException(source, exception);
        System.Windows.MessageBox.Show(
            $"程序发生未处理异常，已写入日志文件：\n{logFilePath}\n\n{exception.Message}",
            "AutoFlow 启动异常",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void HandleNonFatalException(string source, Exception exception)
    {
        var logFilePath = GetExceptionLogService().LogException(source, exception);
        System.Windows.MessageBox.Show(
            $"后台任务发生异常，已写入日志文件：\n{logFilePath}\n\n{exception.Message}",
            "AutoFlow 后台异常",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private ExceptionLogService GetExceptionLogService()
    {
        if (_serviceProvider is not null)
        {
            return _serviceProvider.GetRequiredService<ExceptionLogService>();
        }

        return new ExceptionLogService(new PathService());
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
