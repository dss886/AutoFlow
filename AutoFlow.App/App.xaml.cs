using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using AutomationHost.App.Services;
using Forms = System.Windows.Forms;

namespace AutomationHost.App;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _notifyIcon;
    private Icon? _notifyIconImage;

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
            InitializeNotifyIcon(window);
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
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }

        _notifyIconImage?.Dispose();
        _notifyIconImage = null;

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
            "AutomationHost 启动异常",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static void HandleNonFatalException(string source, Exception exception)
    {
        var logFilePath = ExceptionLogService.LogException(source, exception);
        System.Windows.MessageBox.Show(
            $"后台任务发生异常，已写入日志文件：\n{logFilePath}\n\n{exception.Message}",
            "AutomationHost 后台异常",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private void InitializeNotifyIcon(MainWindow window)
    {
        var exitMenuItem = new Forms.ToolStripMenuItem("退出");
        exitMenuItem.Click += (_, _) => ExitApplication(window);

        var contextMenu = new Forms.ContextMenuStrip();
        contextMenu.Items.Add(exitMenuItem);

        _notifyIconImage = LoadNotifyIcon();

        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "AutoFlow",
            Icon = _notifyIconImage,
            Visible = true,
            ContextMenuStrip = contextMenu,
        };

        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                ShowMainWindow(window);
            }
        };
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

    private Icon LoadNotifyIcon()
    {
        var resourceInfo = GetResourceStream(new Uri("Assets/AppIcon.png", UriKind.Relative));
        if (resourceInfo?.Stream is null)
        {
            return (Icon)SystemIcons.Application.Clone();
        }

        using var stream = resourceInfo.Stream;
        using var bitmap = new Bitmap(stream);
        using var resizedBitmap = new Bitmap(bitmap, new System.Drawing.Size(32, 32));

        var handle = resizedBitmap.GetHicon();
        try
        {
            using var tempIcon = Icon.FromHandle(handle);
            return (Icon)tempIcon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
