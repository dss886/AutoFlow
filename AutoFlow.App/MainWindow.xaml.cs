using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using AutoFlow.App.Services;
using AutoFlow.App.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace AutoFlow.App;

public partial class MainWindow : Window
{
    private const int HcAction = 0;
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int WmMouseMove = 0x0200;
    private const int WmXButtonDown = 0x020B;
    private const int WmXButtonUp = 0x020C;
    private const uint VkR = 0x52;
    private const uint VkLShift = 0xA0;
    private const uint VkRShift = 0xA1;
    private const ushort XButton1 = 0x0001;

    private readonly HookProc _keyboardHookProc;
    private readonly HookProc _mouseHookProc;
    private bool _allowExit;
    private bool _isScreenToolRecordKeyPressed;
    private bool _isScreenToolShiftKeyPressed;
    private bool _isToggleMouseButtonPressed;
    private IntPtr _keyboardHookHandle;
    private IntPtr _mouseHookHandle;

    public MainWindow()
    {
        InitializeComponent();

        ViewModel = new MainWindowViewModel(Close);
        Style = (Style)FindResource(typeof(Window));
        SourceInitialized += MainWindow_OnSourceInitialized;
        DataContext = ViewModel;

        _keyboardHookProc = KeyboardHookCallback;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData
    {
        public uint VkCode;
        public uint ScanCode;
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
        WindowPlacementService.Apply(this);
        InstallKeyboardHook();
        InstallMouseHook();
    }

    protected override void OnClosed(EventArgs e)
    {
        RemoveKeyboardHook();
        RemoveMouseHook();
        ViewModel.Dispose();
        base.OnClosed(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowExit)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        WindowPlacementService.Save(this);
        base.OnClosing(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (TryHandleScreenToolShortcut(e.Key, isKeyDown: true))
        {
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        if (ViewModel.IsScreenToolVisible)
        {
            ReleaseScreenToolShortcutState(e.Key);
        }

        base.OnPreviewKeyUp(e);
    }

    private void InstallKeyboardHook()
    {
        if (_keyboardHookHandle != IntPtr.Zero)
        {
            return;
        }

        var moduleName = Process.GetCurrentProcess().MainModule?.ModuleName;
        var moduleHandle = GetModuleHandle(moduleName);
        _keyboardHookHandle = SetWindowsHookEx(WhKeyboardLl, _keyboardHookProc, moduleHandle, 0);
        if (_keyboardHookHandle != IntPtr.Zero)
        {
            ViewModel.AppendLogMessage("已启用屏幕工具全局快捷键监听。");
            return;
        }

        var errorCode = Marshal.GetLastWin32Error();
        ViewModel.AppendLogMessage($"屏幕工具全局快捷键监听启用失败，错误代码: {errorCode}");
    }

    private void RemoveKeyboardHook()
    {
        if (_keyboardHookHandle == IntPtr.Zero)
        {
            return;
        }

        UnhookWindowsHookEx(_keyboardHookHandle);
        _keyboardHookHandle = IntPtr.Zero;
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
            ViewModel.AppendLogMessage("已启用鼠标后退键监听。");
            return;
        }

        var errorCode = Marshal.GetLastWin32Error();
        ViewModel.AppendLogMessage($"鼠标后退键监听启用失败，错误代码: {errorCode}");
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

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode != HcAction)
        {
            return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
        }

        var message = wParam.ToInt32();
        var keyboardData = Marshal.PtrToStructure<KeyboardHookData>(lParam);
        var isKeyDown = message is WmKeyDown or WmSysKeyDown;
        var isKeyUp = message is WmKeyUp or WmSysKeyUp;

        if (!isKeyDown && !isKeyUp)
        {
            return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
        }

        if (!ViewModel.IsScreenToolVisible)
        {
            ReleaseScreenToolShortcutState(keyboardData.VkCode);

            return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
        }

        if (TryHandleScreenToolShortcut(keyboardData.VkCode, isKeyDown))
        {
            return new IntPtr(1);
        }

        if (isKeyUp)
        {
            ReleaseScreenToolShortcutState(keyboardData.VkCode);
        }

        return CallNextHookEx(_keyboardHookHandle, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
        }

        var message = wParam.ToInt32();
        var mouseData = Marshal.PtrToStructure<MouseHookData>(lParam);
        var sideButton = (ushort)(mouseData.MouseData >> 16);

        if (message == WmMouseMove)
        {
            ScreenToolPopupControl.OnGlobalMouseMove(mouseData.X, mouseData.Y);
        }

        if (sideButton == XButton1)
        {
            if (message == WmXButtonDown && !_isToggleMouseButtonPressed)
            {
                _isToggleMouseButtonPressed = true;
                Dispatcher.BeginInvoke(new Action(ViewModel.ExecuteToggleRunStateCommand));
            }
            else if (message == WmXButtonUp)
            {
                _isToggleMouseButtonPressed = false;
            }
        }

        return CallNextHookEx(_mouseHookHandle, nCode, wParam, lParam);
    }

    private bool TryHandleScreenToolShortcut(Key key, bool isKeyDown)
    {
        if (!ViewModel.IsScreenToolVisible)
        {
            return false;
        }

        return key switch
        {
            Key.R => HandleScreenToolRecordShortcut(isKeyDown),
            Key.LeftShift or Key.RightShift => HandleScreenToolShiftShortcut(isKeyDown),
            _ => false,
        };
    }

    private bool TryHandleScreenToolShortcut(uint virtualKey, bool isKeyDown)
    {
        return virtualKey switch
        {
            VkR => HandleScreenToolRecordShortcut(isKeyDown),
            VkLShift or VkRShift => HandleScreenToolShiftShortcut(isKeyDown),
            _ => false,
        };
    }

    private bool HandleScreenToolRecordShortcut(bool isKeyDown)
    {
        if (!isKeyDown)
        {
            _isScreenToolRecordKeyPressed = false;
            return true;
        }

        if (_isScreenToolRecordKeyPressed)
        {
            return true;
        }

        _isScreenToolRecordKeyPressed = true;
        Dispatcher.BeginInvoke(new Action(() =>
            ViewModel.AppendLogMessage(ScreenToolPopupControl.CreateCurrentReadingLogMessage())));
        return true;
    }

    private bool HandleScreenToolShiftShortcut(bool isKeyDown)
    {
        if (!isKeyDown)
        {
            _isScreenToolShiftKeyPressed = false;
            return true;
        }

        if (_isScreenToolShiftKeyPressed)
        {
            return true;
        }

        _isScreenToolShiftKeyPressed = true;
        Dispatcher.BeginInvoke(new Action(ScreenToolPopupControl.ToggleColorDisplayFormat));
        return true;
    }

    private void ReleaseScreenToolShortcutState(Key key)
    {
        switch (key)
        {
            case Key.R:
                _isScreenToolRecordKeyPressed = false;
                break;
            case Key.LeftShift:
            case Key.RightShift:
                _isScreenToolShiftKeyPressed = false;
                break;
        }
    }

    private void ReleaseScreenToolShortcutState(uint virtualKey)
    {
        switch (virtualKey)
        {
            case VkR:
                _isScreenToolRecordKeyPressed = false;
                break;
            case VkLShift:
            case VkRShift:
                _isScreenToolShiftKeyPressed = false;
                break;
        }
    }

    private void ResetScreenToolShortcutState()
    {
        _isScreenToolRecordKeyPressed = false;
        _isScreenToolShiftKeyPressed = false;
    }

    public void PrepareForExit()
    {
        ResetScreenToolShortcutState();
        _allowExit = true;
    }
}
