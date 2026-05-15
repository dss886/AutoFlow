using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Threading;
using AutoFlow.App.Services;
using DrawingPoint = System.Drawing.Point;
using MediaColor = System.Windows.Media.Color;
using Size = System.Windows.Size;
using WpfPoint = System.Windows.Point;

namespace AutoFlow.App.Views.Parts;

public partial class ScreenToolPopup : System.Windows.Controls.UserControl
{
    private const double PopupOffsetX = 18;
    private const double PopupOffsetY = 20;
    private const double PopupPadding = 8;
    private const int MouseHookFallbackDelayMs = 150;

    private readonly DispatcherTimer _mouseHookFallbackTimer;
    private readonly DispatcherTimer _mousePositionPollTimer;
    private Window? _hostWindow;
    private bool _hasObservedMousePosition;
    private bool _isMousePollFallbackActive;
    private bool _isMousePositionUpdateQueued;
    private int _latestMouseX;
    private int _latestMouseY;
    private int _currentMouseX;
    private int _currentMouseY;
    private int _observedMouseX;
    private int _observedMouseY;
    private MediaColor _currentColor = Colors.White;
    private readonly ScreenToolColorDisplayFormat _defaultColorDisplayFormat = LocalSettingsService.LoadScreenToolColorDisplayFormat();
    private ScreenToolColorDisplayFormat _colorDisplayFormat;

    public ScreenToolPopup()
    {
        InitializeComponent();
        _colorDisplayFormat = _defaultColorDisplayFormat;

        Loaded += ScreenToolPopup_OnLoaded;
        Unloaded += ScreenToolPopup_OnUnloaded;

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

        RefreshContent();
    }

    public static readonly DependencyProperty IsToolVisibleProperty =
        DependencyProperty.Register(
            nameof(IsToolVisible),
            typeof(bool),
            typeof(ScreenToolPopup),
            new PropertyMetadata(false, OnIsToolVisibleChanged));

    public bool IsToolVisible
    {
        get => (bool)GetValue(IsToolVisibleProperty);
        set => SetValue(IsToolVisibleProperty, value);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr hDc, int x, int y);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    public void OnGlobalMouseMove(int x, int y)
    {
        RememberObservedMousePosition(x, y);

        if (_isMousePollFallbackActive)
        {
            _isMousePollFallbackActive = false;
            _mousePositionPollTimer.Stop();
        }

        if (IsToolVisible && IsHostWindowVisible())
        {
            QueueMousePositionUpdate(x, y);
        }
    }

    private static void OnIsToolVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((ScreenToolPopup)d).SyncMousePositionTracking();
    }

    private void ScreenToolPopup_OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachHostWindow();
        SyncMousePositionTracking();
    }

    private void ScreenToolPopup_OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopMouseTrackingTimers();
        PopupRoot.IsOpen = false;
        DetachHostWindow();
    }

    private void AttachHostWindow()
    {
        var hostWindow = Window.GetWindow(this);
        if (ReferenceEquals(_hostWindow, hostWindow))
        {
            return;
        }

        DetachHostWindow();
        _hostWindow = hostWindow;

        if (_hostWindow is not null)
        {
            _hostWindow.IsVisibleChanged += HostWindow_OnIsVisibleChanged;
        }
    }

    private void DetachHostWindow()
    {
        if (_hostWindow is null)
        {
            return;
        }

        _hostWindow.IsVisibleChanged -= HostWindow_OnIsVisibleChanged;
        _hostWindow = null;
    }

    private void HostWindow_OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        SyncMousePositionTracking();
    }

    private void SyncMousePositionTracking()
    {
        if (IsToolVisible && IsHostWindowVisible())
        {
            PopupRoot.IsOpen = true;
            _mouseHookFallbackTimer.Start();
            UpdateMousePosition();
            return;
        }

        StopMouseTrackingTimers();
        PopupRoot.IsOpen = false;
    }

    private bool IsHostWindowVisible()
    {
        return _hostWindow?.IsVisible == true;
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
        UpdateContent(x, y, GetScreenColor(x, y));
        UpdatePopupPosition(x, y);
    }

    private void UpdateContent(int x, int y, MediaColor color)
    {
        _currentMouseX = x;
        _currentMouseY = y;
        _currentColor = color;
        RefreshContent();
    }

    public void ToggleColorDisplayFormat()
    {
        _colorDisplayFormat = _colorDisplayFormat == ScreenToolColorDisplayFormat.Hex
            ? ScreenToolColorDisplayFormat.Rgb
            : ScreenToolColorDisplayFormat.Hex;

        LocalSettingsService.SaveScreenToolColorDisplayFormat(_colorDisplayFormat);
        RefreshContent();
    }

    public string CreateCurrentReadingLogMessage()
    {
        return $"鼠标位置 {FormatCoordinate(_currentMouseX, _currentMouseY)}, 颜色 {FormatColorValue(_currentColor)}";
    }

    private void RefreshContent()
    {
        CoordinateTextBlock.Text = FormatCoordinate(_currentMouseX, _currentMouseY);
        ColorTextBlock.Text = FormatColorValue(_currentColor);
        ColorPreviewBorder.Background = new SolidColorBrush(_currentColor);
    }

    private string FormatColorValue(MediaColor color)
    {
        return _colorDisplayFormat == ScreenToolColorDisplayFormat.Hex
            ? FormatHexColor(color)
            : FormatRgbColor(color);
    }

    private static string FormatCoordinate(int x, int y)
    {
        return $"({x}, {y})";
    }

    private static string FormatHexColor(MediaColor color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static string FormatRgbColor(MediaColor color)
    {
        return $"RGB({color.R}, {color.G}, {color.B})";
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

    private void UpdatePopupPosition(int x, int y)
    {
        if (!PopupRoot.IsOpen)
        {
            return;
        }

        PopupContentRoot.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        PopupContentRoot.UpdateLayout();

        var popupSize = PopupContentRoot.DesiredSize;
        var cursorPosition = TransformFromDevicePixels(x, y);
        var workArea = Screen.FromPoint(new DrawingPoint(x, y)).WorkingArea;
        var workAreaTopLeft = TransformFromDevicePixels(workArea.Left, workArea.Top);
        var workAreaBottomRight = TransformFromDevicePixels(workArea.Right, workArea.Bottom);
        var popupLeft = Clamp(
            cursorPosition.X + PopupOffsetX,
            workAreaTopLeft.X + PopupPadding,
            workAreaBottomRight.X - popupSize.Width - PopupPadding);
        var popupTop = Clamp(
            cursorPosition.Y + PopupOffsetY,
            workAreaTopLeft.Y + PopupPadding,
            workAreaBottomRight.Y - popupSize.Height - PopupPadding);

        PopupRoot.HorizontalOffset = popupLeft;
        PopupRoot.VerticalOffset = popupTop;
    }

    private WpfPoint TransformFromDevicePixels(double x, double y)
    {
        Visual visual = _hostWindow as Visual ?? this;
        
        var source = PresentationSource.FromVisual(visual);
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

        if (!IsToolVisible || !IsHostWindowVisible())
        {
            return;
        }

        UpdateMousePosition(_latestMouseX, _latestMouseY);
    }

    private void MousePositionPollTimer_OnTick(object? sender, EventArgs e)
    {
        if (!IsToolVisible || !IsHostWindowVisible())
        {
            return;
        }

        UpdateMousePosition();
    }

    private void MouseHookFallbackTimer_OnTick(object? sender, EventArgs e)
    {
        if (!IsToolVisible || !IsHostWindowVisible())
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
