using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AutoFlow.App.Infrastructure;
using AutoFlow.App.Models;
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
    private const int MousePositionPollIntervalMs = 24;
    private const int MonitorDefaultToNearest = 2;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const int EffectiveDpi = 0;

    private readonly List<IDisposable> _eventSubscriptions = [];
    private readonly DispatcherTimer _mouseHookFallbackTimer;
    private readonly DispatcherTimer _mousePositionPollTimer;
    private IEventBus? _eventBus;
    private AppLoggerService? _logger;
    private LocalSettingsService? _localSettingsService;
    private ScreenColorService? _screenColorService;
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
    private ScreenToolColorDisplayFormat _colorDisplayFormat = ScreenToolColorDisplayFormat.Hex;
    private readonly SolidColorBrush _colorPreviewBrush = new(Colors.White);
    private Size _cachedPopupSize;
    private bool _isPopupMeasureInvalidated = true;

    public ScreenToolPopup()
    {
        InitializeComponent();

        Loaded += ScreenToolPopup_OnLoaded;
        Unloaded += ScreenToolPopup_OnUnloaded;

        _mouseHookFallbackTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(MouseHookFallbackDelayMs),
        };
        _mouseHookFallbackTimer.Tick += MouseHookFallbackTimer_OnTick;

        _mousePositionPollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(MousePositionPollIntervalMs),
        };
        _mousePositionPollTimer.Tick += MousePositionPollTimer_OnTick;

        ColorPreviewBorder.Background = _colorPreviewBrush;
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

    public void Initialize(
        IEventBus eventBus,
        AppLoggerService logger,
        LocalSettingsService localSettingsService,
        ScreenColorService screenColorService)
    {
        ArgumentNullException.ThrowIfNull(eventBus);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(localSettingsService);
        ArgumentNullException.ThrowIfNull(screenColorService);

        if (_eventBus is not null)
        {
            return;
        }

        _eventBus = eventBus;
        _logger = logger;
        _localSettingsService = localSettingsService;
        _screenColorService = screenColorService;
        _colorDisplayFormat = _localSettingsService.LoadScreenToolColorDisplayFormat();
        _eventSubscriptions.Add(_eventBus.Subscribe<MouseMovedMessage>(message => OnGlobalMouseMove(message.X, message.Y)));
        _eventSubscriptions.Add(_eventBus.Subscribe<ScreenToolRecordRequestedMessage>(_ => RecordCurrentReading()));
        _eventSubscriptions.Add(_eventBus.Subscribe<ScreenToolColorDisplayToggleRequestedMessage>(_ => ToggleColorDisplayFormat()));
        RefreshContent();
    }

    public void DisposeSubscriptions()
    {
        foreach (var subscription in _eventSubscriptions)
        {
            subscription.Dispose();
        }

        _eventSubscriptions.Clear();
        _eventBus = null;
        _logger = null;
        _localSettingsService = null;
        _screenColorService = null;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint pt, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(
        IntPtr hmonitor,
        int dpiType,
        out uint dpiX,
        out uint dpiY);

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

        if (IsToolVisible)
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
        if (IsToolVisible)
        {
            InvalidatePopupMeasure();
            PopupRoot.IsOpen = true;
            _mouseHookFallbackTimer.Start();
            UpdateMousePosition();
            return;
        }

        StopMouseTrackingTimers();
        PopupRoot.IsOpen = false;
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

        if (x == _currentMouseX && y == _currentMouseY)
        {
            return;
        }

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

        _localSettingsService?.SaveScreenToolColorDisplayFormat(_colorDisplayFormat);
        RefreshContent();
    }

    public void RecordCurrentReading()
    {
        _logger?.I(CreateCurrentReadingLogMessage());
    }

    public string CreateCurrentReadingLogMessage()
    {
        return $"鼠标位置 {FormatCoordinate(_currentMouseX, _currentMouseY)}, 颜色 {FormatColorValue(_currentColor)}";
    }

    private void RefreshContent()
    {
        var coordinateText = FormatCoordinate(_currentMouseX, _currentMouseY);
        if (!string.Equals(CoordinateTextBlock.Text, coordinateText, StringComparison.Ordinal))
        {
            CoordinateTextBlock.Text = coordinateText;
            InvalidatePopupMeasure();
        }

        var colorText = FormatColorValue(_currentColor);
        if (!string.Equals(ColorTextBlock.Text, colorText, StringComparison.Ordinal))
        {
            ColorTextBlock.Text = colorText;
            InvalidatePopupMeasure();
        }

        if (_colorPreviewBrush.Color != _currentColor)
        {
            _colorPreviewBrush.Color = _currentColor;
        }
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

    private string FormatHexColor(MediaColor color)
    {
        return _screenColorService?.FormatHexColor(color) ?? "#FFFFFF";
    }

    private static string FormatRgbColor(MediaColor color)
    {
        return $"RGB({color.R}, {color.G}, {color.B})";
    }

    private MediaColor GetScreenColor(int x, int y)
    {
        if (_screenColorService is null)
        {
            return Colors.White;
        }

        var color = _screenColorService.GetScreenColor(x, y);
        return MediaColor.FromRgb((byte)color.R, (byte)color.G, (byte)color.B);
    }

    private void UpdatePopupPosition(int x, int y)
    {
        if (!PopupRoot.IsOpen)
        {
            return;
        }

        var popupSize = GetPopupSize();

        if (TryPositionPopupInDevicePixels(x, y, popupSize))
        {
            return;
        }

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

    private Size GetPopupSize()
    {
        if (!_isPopupMeasureInvalidated && _cachedPopupSize.Width > 0 && _cachedPopupSize.Height > 0)
        {
            return _cachedPopupSize;
        }

        PopupContentRoot.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        PopupContentRoot.UpdateLayout();
        _cachedPopupSize = PopupContentRoot.DesiredSize;
        _isPopupMeasureInvalidated = false;
        return _cachedPopupSize;
    }

    private bool TryPositionPopupInDevicePixels(int x, int y, Size popupSizeDip)
    {
        var popupHandle = GetPopupHandle();
        if (popupHandle == IntPtr.Zero)
        {
            return false;
        }

        var (scaleX, scaleY) = GetMonitorScale(x, y);
        var workArea = Screen.FromPoint(new DrawingPoint(x, y)).WorkingArea;
        var popupWidth = Math.Max((int)Math.Ceiling(popupSizeDip.Width * scaleX), 1);
        var popupHeight = Math.Max((int)Math.Ceiling(popupSizeDip.Height * scaleY), 1);
        var offsetX = (int)Math.Round(PopupOffsetX * scaleX);
        var offsetY = (int)Math.Round(PopupOffsetY * scaleY);
        var paddingX = (int)Math.Round(PopupPadding * scaleX);
        var paddingY = (int)Math.Round(PopupPadding * scaleY);
        var popupLeft = ClampInt(
            x + offsetX,
            workArea.Left + paddingX,
            workArea.Right - popupWidth - paddingX);
        var popupTop = ClampInt(
            y + offsetY,
            workArea.Top + paddingY,
            workArea.Bottom - popupHeight - paddingY);

        PopupRoot.HorizontalOffset = 0;
        PopupRoot.VerticalOffset = 0;

        return SetWindowPos(
            popupHandle,
            IntPtr.Zero,
            popupLeft,
            popupTop,
            0,
            0,
            SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    private IntPtr GetPopupHandle()
    {
        return (PresentationSource.FromVisual(PopupContentRoot) as HwndSource)?.Handle ?? IntPtr.Zero;
    }

    private static (double ScaleX, double ScaleY) GetMonitorScale(int x, int y)
    {
        var monitor = MonitorFromPoint(new NativePoint { X = x, Y = y }, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return (1d, 1d);
        }

        if (GetDpiForMonitor(monitor, EffectiveDpi, out var dpiX, out var dpiY) != 0)
        {
            return (1d, 1d);
        }

        return (dpiX / 96d, dpiY / 96d);
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

    private static int ClampInt(int value, int min, int max)
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

        if (!IsToolVisible)
        {
            return;
        }

        UpdateMousePosition(_latestMouseX, _latestMouseY);
    }

    private void MousePositionPollTimer_OnTick(object? sender, EventArgs e)
    {
        if (!IsToolVisible)
        {
            return;
        }

        if (!GetCursorPos(out var point))
        {
            return;
        }

        if (point.X == _currentMouseX && point.Y == _currentMouseY)
        {
            return;
        }

        UpdateMousePosition(point.X, point.Y);
    }

    private void MouseHookFallbackTimer_OnTick(object? sender, EventArgs e)
    {
        if (!IsToolVisible)
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

    private void InvalidatePopupMeasure()
    {
        _isPopupMeasureInvalidated = true;
    }

    private void RememberObservedMousePosition(int x, int y)
    {
        _hasObservedMousePosition = true;
        _observedMouseX = x;
        _observedMouseY = y;
    }
}
