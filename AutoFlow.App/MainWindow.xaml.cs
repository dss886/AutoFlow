using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Forms;
using AutoFlow.App.Services;
using AutoFlow.App.ViewModels;
using MediaColor = System.Windows.Media.Color;
using DrawingPoint = System.Drawing.Point;
using WpfPoint = System.Windows.Point;

namespace AutoFlow.App;

public partial class MainWindow : Window
{
    private const int WhMouseLl = 14;
    private const int WmMouseMove = 0x0200;
    private const int WmXButtonDown = 0x020B;
    private const int WmXButtonUp = 0x020C;
    private const ushort XButton1 = 0x0001;
    private const double MousePopupOffsetX = 18;
    private const double MousePopupOffsetY = 20;
    private const double MousePopupPadding = 8;
    private const int MouseHookFallbackDelayMs = 150;

    private readonly LowLevelMouseProc _mouseHookProc;
    private readonly DispatcherTimer _mouseHookFallbackTimer;
    private readonly DispatcherTimer _mousePositionPollTimer;
    private bool _allowExit;
    private bool _hasObservedMousePosition;
    private bool _isMousePollFallbackActive;
    private bool _isMousePositionUpdateQueued;
    private bool _isToggleMouseButtonPressed;
    private int _observedMouseX;
    private int _observedMouseY;
    private int _latestMouseX;
    private int _latestMouseY;
    private IntPtr _mouseHookHandle;

    public MainWindow()
    {
        InitializeComponent();

        ViewModel = new MainWindowViewModel(Close);
        Style = (Style)FindResource(typeof(Window));
        SourceInitialized += MainWindow_OnSourceInitialized;
        IsVisibleChanged += MainWindow_OnIsVisibleChanged;
        DataContext = ViewModel;
        ViewModel.PropertyChanged += ViewModel_OnPropertyChanged;

        _mouseHookProc = MouseHookCallback;
        _mouseHookFallbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(MouseHookFallbackDelayMs),
        };
        _mouseHookFallbackTimer.Tick += MouseHookFallbackTimer_OnTick;
        _mousePositionPollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _mousePositionPollTimer.Tick += MousePositionPollTimer_OnTick;
    }

    public MainWindowViewModel ViewModel { get; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr hDc, int x, int y);

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
        StopMouseTrackingTimers();
        _mouseHookFallbackTimer.Tick -= MouseHookFallbackTimer_OnTick;
        _mousePositionPollTimer.Stop();
        _mousePositionPollTimer.Tick -= MousePositionPollTimer_OnTick;
        RemoveMouseHook();
        IsVisibleChanged -= MainWindow_OnIsVisibleChanged;
        ViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        ViewModel.Dispose();
        base.OnClosed(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowExit)
        {
            e.Cancel = true;
            StopMouseTrackingTimers();
            MousePositionPopup.IsOpen = false;
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

    private void UpdateMousePosition()
    {
        if (!GetCursorPos(out var point))
        {
            return;
        }

        UpdateMousePosition(point.X, point.Y);
    }

    private void UpdateMousePosition(int x, int y)
    {
        RememberObservedMousePosition(x, y);
        ViewModel.UpdateMousePosition(x, y);
        UpdateMousePositionPopup(x, y, GetScreenColor(x, y));
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

        if (message == WmMouseMove)
        {
            OnMouseHookMove(mouseData.X, mouseData.Y);
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

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainWindowViewModel.IsScreenToolVisible))
        {
            return;
        }

        SyncMousePositionTracking();
    }

    public void PrepareForExit()
    {
        _allowExit = true;
    }

    private void SyncMousePositionTracking()
    {
        if (ViewModel.IsScreenToolVisible && IsVisible)
        {
            MousePositionPopup.IsOpen = true;
            _mouseHookFallbackTimer.Start();
            UpdateMousePosition();
            return;
        }

        StopMouseTrackingTimers();
        MousePositionPopup.IsOpen = false;
    }

    private void MainWindow_OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        SyncMousePositionTracking();
    }

    private static MediaColor GetScreenColor(int x, int y)
    {
        var desktopDc = GetDC(IntPtr.Zero);
        if (desktopDc == IntPtr.Zero)
        {
            return Colors.White;
        }

        try
        {
            var pixel = GetPixel(desktopDc, x, y);
            if (pixel == 0xFFFFFFFF)
            {
                return Colors.White;
            }

            var red = (byte)(pixel & 0x000000FF);
            var green = (byte)((pixel & 0x0000FF00) >> 8);
            var blue = (byte)((pixel & 0x00FF0000) >> 16);
            return MediaColor.FromRgb(red, green, blue);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, desktopDc);
        }
    }

    private void UpdateMousePositionPopup(int x, int y, MediaColor color)
    {
        if (!MousePositionPopup.IsOpen)
        {
            return;
        }

        MousePositionOverlayPopupControl.UpdateContent(x, y, color);
        MousePositionOverlayPopupControl.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        MousePositionOverlayPopupControl.UpdateLayout();

        var popupSize = MousePositionOverlayPopupControl.DesiredSize;
        var cursorPosition = TransformFromDevicePixels(x, y);
        var workArea = Screen.FromPoint(new DrawingPoint(x, y)).WorkingArea;
        var workAreaTopLeft = TransformFromDevicePixels(workArea.Left, workArea.Top);
        var workAreaBottomRight = TransformFromDevicePixels(workArea.Right, workArea.Bottom);
        var popupLeft = Clamp(cursorPosition.X + MousePopupOffsetX, workAreaTopLeft.X + MousePopupPadding, workAreaBottomRight.X - popupSize.Width - MousePopupPadding);
        var popupTop = Clamp(cursorPosition.Y + MousePopupOffsetY, workAreaTopLeft.Y + MousePopupPadding, workAreaBottomRight.Y - popupSize.Height - MousePopupPadding);

        MousePositionPopup.HorizontalOffset = popupLeft;
        MousePositionPopup.VerticalOffset = popupTop;
    }

    private WpfPoint TransformFromDevicePixels(double x, double y)
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null)
        {
            return new WpfPoint(x, y);
        }

        return source.CompositionTarget.TransformFromDevice.Transform(new WpfPoint(x, y));
    }

    private static double Clamp(double value, double min, double max)
    {
        if (min > max)
        {
            return min;
        }

        return Math.Min(Math.Max(value, min), max);
    }

    private void QueueMousePositionUpdate(int x, int y)
    {
        _latestMouseX = x;
        _latestMouseY = y;

        if (_isMousePositionUpdateQueued)
        {
            return;
        }

        _isMousePositionUpdateQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(FlushQueuedMousePositionUpdate));
    }

    private void FlushQueuedMousePositionUpdate()
    {
        _isMousePositionUpdateQueued = false;

        if (!ViewModel.IsScreenToolVisible || !IsVisible)
        {
            return;
        }

        UpdateMousePosition(_latestMouseX, _latestMouseY);
    }

    private void MousePositionPollTimer_OnTick(object? sender, EventArgs e)
    {
        if (!ViewModel.IsScreenToolVisible || !IsVisible)
        {
            return;
        }

        UpdateMousePosition();
    }

    private void MouseHookFallbackTimer_OnTick(object? sender, EventArgs e)
    {
        if (!ViewModel.IsScreenToolVisible || !IsVisible)
        {
            return;
        }

        if (!GetCursorPos(out var point))
        {
            return;
        }

        if (!_hasObservedMousePosition)
        {
            RememberObservedMousePosition(point.X, point.Y);
            return;
        }

        var hasMouseMoved = point.X != _observedMouseX || point.Y != _observedMouseY;
        if (!hasMouseMoved)
        {
            if (_isMousePollFallbackActive)
            {
                _isMousePollFallbackActive = false;
                _mousePositionPollTimer.Stop();
            }

            return;
        }

        if (!_isMousePollFallbackActive)
        {
            _isMousePollFallbackActive = true;
            _mousePositionPollTimer.Start();
            UpdateMousePosition(point.X, point.Y);
        }
    }

    private void OnMouseHookMove(int x, int y)
    {
        RememberObservedMousePosition(x, y);

        if (_isMousePollFallbackActive)
        {
            _isMousePollFallbackActive = false;
            _mousePositionPollTimer.Stop();
        }

        if (ViewModel.IsScreenToolVisible && IsVisible)
        {
            QueueMousePositionUpdate(x, y);
        }
    }

    private void StopMouseTrackingTimers()
    {
        _hasObservedMousePosition = false;
        _isMousePollFallbackActive = false;
        _mouseHookFallbackTimer.Stop();
        _mousePositionPollTimer.Stop();
    }

    private void RememberObservedMousePosition(int x, int y)
    {
        _hasObservedMousePosition = true;
        _observedMouseX = x;
        _observedMouseY = y;
    }
}
