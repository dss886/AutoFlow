using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MediaColor = System.Windows.Media.Color;

namespace AutoFlow.App.Services;

public sealed class ScreenColorService
{
    private const int BytesPerPixel = 4;
    private static readonly Rectangle SinglePixelBounds = new(0, 0, 1, 1);
    private readonly ScreenCaptureService _screenCaptureService;

    public ScreenColorService(ScreenCaptureService screenCaptureService)
    {
        _screenCaptureService = screenCaptureService ?? throw new ArgumentNullException(nameof(screenCaptureService));
    }

    public IReadOnlyList<(int R, int G, int B, string Hex)> GetScreenColors(IReadOnlyList<(int X, int Y)> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        if (points.Count == 0)
        {
            return Array.Empty<(int R, int G, int B, string Hex)>();
        }

        var captureBounds = GetCaptureBounds(points);
        using var capture = _screenCaptureService.CaptureRegion(
            captureBounds.X,
            captureBounds.Y,
            captureBounds.Width,
            captureBounds.Height);
        return ReadColorsFromCapture(capture, captureBounds, points);
    }

    public (int R, int G, int B, string Hex) GetScreenColor(int x, int y)
    {
        using var capture = _screenCaptureService.CaptureRegion(x, y, 1, 1);
        var bitmapData = capture.LockBits(
            SinglePixelBounds,
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            var pixel = Marshal.ReadInt32(bitmapData.Scan0);
            var blue = pixel & 0xFF;
            var green = (pixel >> 8) & 0xFF;
            var red = (pixel >> 16) & 0xFF;

            return (red, green, blue, FormatHexColor(red, green, blue));
        }
        finally
        {
            capture.UnlockBits(bitmapData);
        }
    }

    private IReadOnlyList<(int R, int G, int B, string Hex)> ReadColorsFromCapture(
        Bitmap capture,
        Rectangle captureBounds,
        IReadOnlyList<(int X, int Y)> points)
    {
        var bitmapBounds = new Rectangle(0, 0, capture.Width, capture.Height);
        var bitmapData = capture.LockBits(
            bitmapBounds,
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        try
        {
            var stride = Math.Abs(bitmapData.Stride);
            var buffer = new byte[stride * capture.Height];
            Marshal.Copy(bitmapData.Scan0, buffer, 0, buffer.Length);

            var colors = new (int R, int G, int B, string Hex)[points.Count];
            for (var index = 0; index < points.Count; index++)
            {
                var point = points[index];
                var relativeX = point.X - captureBounds.X;
                var relativeY = point.Y - captureBounds.Y;
                var pixelOffset = relativeY * stride + relativeX * BytesPerPixel;
                var red = buffer[pixelOffset + 2];
                var green = buffer[pixelOffset + 1];
                var blue = buffer[pixelOffset];

                colors[index] = (
                    red,
                    green,
                    blue,
                    FormatHexColor(red, green, blue));
            }

            return colors;
        }
        finally
        {
            capture.UnlockBits(bitmapData);
        }
    }

    private static Rectangle GetCaptureBounds(IReadOnlyList<(int X, int Y)> points)
    {
        var minX = points[0].X;
        var minY = points[0].Y;
        var maxX = points[0].X;
        var maxY = points[0].Y;

        for (var index = 1; index < points.Count; index++)
        {
            var point = points[index];
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        return Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
    }

    public string FormatHexColor(MediaColor color)
    {
        return FormatHexColor(color.R, color.G, color.B);
    }

    public string FormatHexColor(int r, int g, int b)
    {
        return $"#{r:X2}{g:X2}{b:X2}";
    }

}
