using System.Drawing;
using System.Drawing.Imaging;

namespace AutoFlow.App.Services;

public sealed class ScreenCaptureService
{
    public Bitmap CaptureRegion(int x, int y, int width, int height)
    {
        ValidateBounds(width, height);

        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height), CopyPixelOperation.SourceCopy);
        return bitmap;
    }

    private static void ValidateBounds(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            throw new InvalidOperationException("截图区域宽高必须大于 0。");
        }
    }
}
