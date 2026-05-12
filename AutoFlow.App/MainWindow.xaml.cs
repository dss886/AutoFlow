using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AutoFlow.App.Services;
using AutoFlow.App.ViewModels;

namespace AutoFlow.App;

public partial class MainWindow : Window
{
    private const int WhMouseLl = 14;
    private const int WmXButtonDown = 0x020B;
    private const int WmXButtonUp = 0x020C;
    private const ushort XButton1 = 0x0001;

    private readonly DispatcherTimer _mousePositionTimer;
    private readonly LowLevelMouseProc _mouseHookProc;
    private bool _allowExit;
    private bool _isToggleMouseButtonPressed;
    private IntPtr _mouseHookHandle;

    public MainWindow()
    {
        InitializeComponent();

        ViewModel = new MainWindowViewModel(Close);
        Style = (Style)FindResource(typeof(Window));
        SourceInitialized += MainWindow_OnSourceInitialized;
        DataContext = ViewModel;
        ViewModel.PropertyChanged += ViewModel_OnPropertyChanged;

        _mouseHookProc = MouseHookCallback;
        _mousePositionTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _mousePositionTimer.Tick += MousePositionTimer_OnTick;
    }

    public MainWindowViewModel ViewModel { get; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

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
        WindowPlacementService.Apply(this);
        InstallMouseHook();
    }

    protected override void OnClosed(EventArgs e)
    {
        _mousePositionTimer.Stop();
        RemoveMouseHook();
        ViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
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

    private void MousePositionTimer_OnTick(object? sender, EventArgs e)
    {
        UpdateMousePosition();
    }

    private void UpdateMousePosition()
    {
        if (!GetCursorPos(out var point))
        {
            return;
        }

        ViewModel.UpdateMousePosition(point.X, point.Y);
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
        var sideButton = (ushort)(mouseData.MouseData >> 16);

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

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsMousePositionVisible))
        {
            return;
        }

        if (ViewModel.IsMousePositionVisible)
        {
            UpdateMousePosition();
            _mousePositionTimer.Start();
            return;
        }

        _mousePositionTimer.Stop();
    }

    public void PrepareForExit()
    {
        _allowExit = true;
    }
}
