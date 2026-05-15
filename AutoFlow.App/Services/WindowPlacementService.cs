using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Media;

namespace AutoFlow.App.Services;

public static class WindowPlacementService
{
    private const int MinimumVisibleWidth = 100;
    private const int MinimumVisibleHeight = 60;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    public static void Apply(Window window)
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

    public static void Save(Window window)
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
