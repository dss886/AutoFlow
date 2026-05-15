using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;
using AutoFlow.App.Views;
using Point = System.Windows.Point;

namespace AutoFlow.App.Services;

public sealed class WindowControlService
{
    private const int MinimumVisibleWidth = 100;
    private const int MinimumVisibleHeight = 60;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const double SettingsWindowGap = 16;

    private readonly Window _mainWindow;
    private readonly Action _hotkeysChanged;
    private bool _allowExit;
    private bool _isSettingsWindowOnLeft;
    private SettingsWindow? _settingsWindow;

    public WindowControlService(Window mainWindow, Action hotkeysChanged)
    {
        _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        _hotkeysChanged = hotkeysChanged ?? throw new ArgumentNullException(nameof(hotkeysChanged));

        _mainWindow.LocationChanged += (_, _) => UpdateSettingsWindowBounds();
        _mainWindow.SizeChanged += (_, _) => UpdateSettingsWindowBounds();
    }

    public void ApplyStartupPlacement()
    {
        Apply(_mainWindow);
    }

    public bool HandleMainWindowClosing()
    {
        Save(_mainWindow);

        if (_allowExit)
        {
            CloseSettingsWindow();
            return false;
        }

        CloseSettingsWindow();
        _mainWindow.Hide();
        return true;
    }

    public void ToggleSettingsWindow()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            CloseSettingsWindow();
            return;
        }

        if (_settingsWindow is { IsLoaded: true })
        {
            UpdateSettingsWindowBounds();
            _settingsWindow.Show();
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_hotkeysChanged)
        {
            Owner = _mainWindow,
        };
        _settingsWindow.Closed += SettingsWindow_OnClosed;

        _isSettingsWindowOnLeft = ShouldOpenSettingsWindowOnLeft(_settingsWindow.Width);
        UpdateSettingsWindowBounds();
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public void PrepareForExit()
    {
        _allowExit = true;
    }

    private void CloseSettingsWindow()
    {
        if (_settingsWindow is null)
        {
            return;
        }

        _settingsWindow.Closed -= SettingsWindow_OnClosed;
        _settingsWindow.Close();
        _settingsWindow = null;
    }

    private void SettingsWindow_OnClosed(object? sender, EventArgs e)
    {
        if (_settingsWindow is not null)
        {
            _settingsWindow.Closed -= SettingsWindow_OnClosed;
            _settingsWindow = null;
        }
    }

    private void UpdateSettingsWindowBounds()
    {
        if (_settingsWindow is null)
        {
            return;
        }

        var settingsWidth = _settingsWindow.Width;

        _settingsWindow.Height = _mainWindow.ActualHeight;
        _settingsWindow.Top = _mainWindow.Top;
        _settingsWindow.Left = _isSettingsWindowOnLeft
            ? _mainWindow.Left - settingsWidth - SettingsWindowGap
            : _mainWindow.Left + _mainWindow.ActualWidth + SettingsWindowGap;
    }

    private bool ShouldOpenSettingsWindowOnLeft(double settingsWidth)
    {
        var handle = new WindowInteropHelper(_mainWindow).Handle;
        var screen = Screen.FromHandle(handle);
        var workingArea = screen.WorkingArea;
        var source = PresentationSource.FromVisual(_mainWindow);
        if (source?.CompositionTarget is null)
        {
            var fallbackWorkingArea = new Rect(
                SystemParameters.WorkArea.Left,
                SystemParameters.WorkArea.Top,
                SystemParameters.WorkArea.Width,
                SystemParameters.WorkArea.Height);
            var fallbackPreferredRight = _mainWindow.Left + _mainWindow.ActualWidth + SettingsWindowGap;
            return fallbackPreferredRight + settingsWidth > fallbackWorkingArea.Right
                && _mainWindow.Left - settingsWidth - SettingsWindowGap >= fallbackWorkingArea.Left;
        }

        var transform = source.CompositionTarget.TransformFromDevice;
        var topLeft = transform.Transform(new Point(workingArea.Left, workingArea.Top));
        var bottomRight = transform.Transform(new Point(workingArea.Right, workingArea.Bottom));
        var preferredRight = _mainWindow.Left + _mainWindow.ActualWidth + SettingsWindowGap;
        var preferredLeft = _mainWindow.Left - settingsWidth - SettingsWindowGap;
        var workingAreaDip = new Rect(topLeft, bottomRight);
        return preferredRight + settingsWidth > workingAreaDip.Right
            && preferredLeft >= workingAreaDip.Left;
    }

    private static void Apply(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var defaultBounds = GetDefaultBounds(window);
        var startupBounds = ResolveStartupBounds(defaultBounds);

        SetWindowPos(
            handle,
            IntPtr.Zero,
            startupBounds.Left,
            startupBounds.Top,
            startupBounds.Width,
            startupBounds.Height,
            SwpNoZOrder | SwpNoActivate);
    }

    private static void Save(Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var bounds = GetNormalBounds(handle);
            if (!IsReasonableBounds(bounds))
            {
                return;
            }

            var placement = new LocalWindowPlacement(
                bounds.Left,
                bounds.Top,
                bounds.Width,
                bounds.Height);
            LocalSettingsService.SaveWindowPlacement(placement);
        }
        catch
        {
            // Keep shutdown resilient even when local config cannot be written.
        }
    }

    private static Rectangle? LoadBounds()
    {
        try
        {
            var placement = LocalSettingsService.LoadWindowPlacement();
            if (placement is null)
            {
                return null;
            }

            var bounds = new Rectangle(
                placement.Left,
                placement.Top,
                placement.Width,
                placement.Height);

            return IsReasonableBounds(bounds) ? bounds : null;
        }
        catch
        {
            return null;
        }
    }

    private static Rectangle ResolveStartupBounds(Rectangle defaultBounds)
    {
        var savedBounds = LoadBounds();
        if (savedBounds is not null && IsVisibleOnAnyScreen(savedBounds.Value))
        {
            return savedBounds.Value;
        }

        return GetCenteredBoundsOnPrimaryScreen(defaultBounds);
    }

    private static Rectangle GetCenteredBoundsOnPrimaryScreen(Rectangle defaultBounds)
    {
        var targetWidth = Math.Max(defaultBounds.Width, 1);
        var targetHeight = Math.Max(defaultBounds.Height, 1);
        var workingArea = Screen.AllScreens[0].WorkingArea;

        var left = workingArea.Left + ((workingArea.Width - targetWidth) / 2);
        var top = workingArea.Top + ((workingArea.Height - targetHeight) / 2);

        return new Rectangle(left, top, targetWidth, targetHeight);
    }

    private static Rectangle GetDefaultBounds(Window window)
    {
        var source = PresentationSource.FromVisual(window);
        var transform = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;

        var width = Math.Max((int)Math.Round(window.Width * transform.M11), 1);
        var height = Math.Max((int)Math.Round(window.Height * transform.M22), 1);

        return new Rectangle(0, 0, width, height);
    }

    private static Rectangle GetNormalBounds(IntPtr handle)
    {
        var placement = new WindowPlacementNative
        {
            length = Marshal.SizeOf<WindowPlacementNative>(),
        };

        if (!GetWindowPlacement(handle, ref placement))
        {
            throw new InvalidOperationException("无法读取窗口位置。");
        }

        var normalPosition = placement.rcNormalPosition;
        return Rectangle.FromLTRB(
            normalPosition.Left,
            normalPosition.Top,
            normalPosition.Right,
            normalPosition.Bottom);
    }

    private static bool IsVisibleOnAnyScreen(Rectangle bounds)
    {
        if (!IsReasonableBounds(bounds))
        {
            return false;
        }

        return Screen.AllScreens.Any(screen =>
        {
            var workingArea = screen.WorkingArea;
            var intersection = Rectangle.Intersect(bounds, workingArea);
            return !intersection.IsEmpty
                && intersection.Width >= MinimumVisibleWidth
                && intersection.Height >= MinimumVisibleHeight;
        });
    }

    private static bool IsReasonableBounds(Rectangle bounds)
    {
        return bounds.Width >= MinimumVisibleWidth
            && bounds.Height >= MinimumVisibleHeight;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WindowPlacementNative lpwndpl);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct PointNative
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacementNative
    {
        public int length;
        public int flags;
        public int showCmd;
        public PointNative ptMinPosition;
        public PointNative ptMaxPosition;
        public RectNative rcNormalPosition;
    }
}
