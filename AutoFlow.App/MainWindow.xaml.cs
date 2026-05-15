using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using AutoFlow.App.Models;
using AutoFlow.App.Services;
using AutoFlow.App.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace AutoFlow.App;

public partial class MainWindow : Window
{
    private const int WhMouseLl = 14;
    private const int WmMButtonDown = 0x0207;
    private const int WmMButtonUp = 0x0208;
    private const int WmMouseMove = 0x0200;
    private const int WmXButtonDown = 0x020B;
    private const int WmXButtonUp = 0x020C;
    private const ushort XButton1 = 0x0001;
    private const ushort XButton2 = 0x0002;

    private readonly GlobalHotkeyService _hotkeyService;
    private readonly HookProc _mouseHookProc;
    private readonly WindowControlService _windowControlService;
    private IntPtr _mouseHookHandle;

    public MainWindow()
    {
        InitializeComponent();

        ViewModel = new MainWindowViewModel(Close, OpenSettingsWindow);
        Style = (Style)FindResource(typeof(Window));
        SourceInitialized += MainWindow_OnSourceInitialized;
        DataContext = ViewModel;
        _windowControlService = new WindowControlService(this, OnHotkeysChanged);

        _hotkeyService = new GlobalHotkeyService(ViewModel.AppendLogMessage);
        _hotkeyService.RunRequested += ViewModel.ExecuteRunCommand;
        _hotkeyService.StopRequested += ViewModel.ExecuteStopCommand;
        _hotkeyService.RecordRequested += ViewModel.ExecuteRecordCommand;
        _hotkeyService.ScreenToolToggleRequested += ViewModel.ExecuteToggleScreenToolCommand;
        _hotkeyService.ScreenToolRecordRequested += HandleScreenToolRecordRequested;
        _hotkeyService.ScreenToolColorDisplayToggleRequested += HandleScreenToolColorDisplayToggleRequested;
        ViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        _mouseHookProc = MouseHookCallback;
    }

    public MainWindowViewModel ViewModel { get; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookData
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MainWindow_OnSourceInitialized(object? sender, EventArgs e)
    {
        _windowControlService.ApplyStartupPlacement();
        _hotkeyService.Initialize(this);
        _hotkeyService.SetScreenToolShortcutsEnabled(ViewModel.IsScreenToolVisible);
        InstallMouseHook();
    }

    protected override void OnClosed(EventArgs e)
    {
        ViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        _hotkeyService.Dispose();
        RemoveMouseHook();
        ViewModel.Dispose();
        base.OnClosed(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_windowControlService.HandleMainWindowClosing())
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_hotkeyService.HandlePreviewKeyDown(e.Key))
        {
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        _hotkeyService.HandlePreviewKeyUp(e.Key);

        base.OnPreviewKeyUp(e);
    }

    private void InstallMouseHook()
    {
        if (_mouseHookHandle != IntPtr.Zero)
        {
            return;
        }

        var moduleName = Process.GetCurrentProcess().MainModule?.ModuleName;
        var moduleHandle = GetModuleHandle(moduleName);
        _mouseHookHandle = SetWindowsHookEx(WhMouseLl, _mouseHookProc, moduleHandle, 0);
        if (_mouseHookHandle != IntPtr.Zero)
        {
            ViewModel.AppendLogMessage("已启用全局鼠标位置监听。");
            return;
        }

        var errorCode = Marshal.GetLastWin32Error();
        ViewModel.AppendLogMessage($"全局鼠标位置监听启用失败，错误代码: {errorCode}");
    }

    private void RemoveMouseHook()
    {
        if (_mouseHookHandle == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_mouseHookHandle);
        _mouseHookHandle = IntPtr.Zero;
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
        }

        var message = wParam.ToInt32();
        var mouseData = Marshal.PtrToStructure<MouseHookData>(lParam);
        var mouseButton = ResolveShortcutMouseButton(message, mouseData.MouseData);

        if (message == WmMouseMove)
        {
            ScreenToolPopupControl.OnGlobalMouseMove(mouseData.X, mouseData.Y);
        }

        if (message is WmMButtonDown or WmXButtonDown
            && mouseButton != ShortcutMouseButton.None
            && _hotkeyService.HandleGlobalMouseButtonDown(mouseButton))
        {
            return new IntPtr(1);
        }

        if (message is WmMButtonUp or WmXButtonUp
            && mouseButton != ShortcutMouseButton.None
            && _hotkeyService.HandleGlobalMouseButtonUp(mouseButton))
        {
            return new IntPtr(1);
        }

        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    private void OpenSettingsWindow()
    {
        _windowControlService.ToggleSettingsWindow();
    }

    private void OnHotkeysChanged()
    {
        _hotkeyService.ReloadConfiguredHotkeys(showFailureMessage: true);
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsScreenToolVisible))
        {
            _hotkeyService.SetScreenToolShortcutsEnabled(ViewModel.IsScreenToolVisible);
        }
    }

    private void HandleScreenToolRecordRequested()
    {
        ViewModel.AppendLogMessage(ScreenToolPopupControl.CreateCurrentReadingLogMessage());
    }

    private void HandleScreenToolColorDisplayToggleRequested()
    {
        ScreenToolPopupControl.ToggleColorDisplayFormat();
    }

    public void PrepareForExit()
    {
        _hotkeyService.CleanupAllRegistrations();
        _windowControlService.PrepareForExit();
    }

    private static ShortcutMouseButton ResolveShortcutMouseButton(int message, uint mouseData)
    {
        return message switch
        {
            WmMButtonDown or WmMButtonUp => ShortcutMouseButton.Middle,
            WmXButtonDown or WmXButtonUp => (ushort)(mouseData >> 16) switch
            {
                XButton1 => ShortcutMouseButton.XButton1,
                XButton2 => ShortcutMouseButton.XButton2,
                _ => ShortcutMouseButton.None,
            },
            _ => ShortcutMouseButton.None,
        };
    }
}
