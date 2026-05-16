using System.Runtime.InteropServices;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;

namespace AutoFlow.App.Services;

public sealed class ScreenColorService
{
    private const uint ClrInvalid = 0xFFFFFFFF;

    public string GetScreenColorHex(int x, int y)
    {
        if (!TryGetScreenColor(x, y, out var color))
        {
            throw new InvalidOperationException("读取屏幕颜色失败。");
        }

        return FormatHexColor(color);
    }

    public MediaColor GetScreenColorOrDefault(int x, int y, MediaColor fallback)
    {
        return TryGetScreenColor(x, y, out var color) ? color : fallback;
    }

    public bool TryGetScreenColor(int x, int y, out MediaColor color)
    {
        var desktopDc = GetDC(IntPtr.Zero);
        if (desktopDc == IntPtr.Zero)
        {
            color = default;
            return false;
        }

        try
        {
            var pixel = GetPixel(desktopDc, x, y);
            if (pixel == ClrInvalid)
            {
                color = default;
                return false;
            }

            var red = (byte)(pixel & 0x000000FF);
            var green = (byte)((pixel & 0x0000FF00) >> 8);
            var blue = (byte)((pixel & 0x00FF0000) >> 16);
            color = MediaColor.FromRgb(red, green, blue);
            return true;
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, desktopDc);
        }
    }

    public string FormatHexColor(MediaColor color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr hDc, int x, int y);
}
