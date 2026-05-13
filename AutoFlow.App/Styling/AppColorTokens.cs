using System.Globalization;
using DrawingColor = System.Drawing.Color;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace AutoFlow.App.Styling;

public static class AppColorTokens
{
    /*
    * App Color Tokens
    */

    public static MediaColor ColorBackgroundDark { get; } = ParseHex("#33333D");

    public static MediaColor ColorBackground { get; } = ParseHex("#42424C");

    public static MediaColor ColorPrimaryGreen { get; } = ParseHex("#1EB980");

    public static MediaColor ColorPrimaryYellow { get; } = ParseHex("#FFCF44");

    public static MediaColor ColorPrimaryOrange { get; } = ParseHex("#FF6859");

    public static MediaColor ColorWhite { get; } = ParseHex("#FFFFFF");

    public static MediaColor ColorWhite87 { get; } = ParseHex("#DEFFFFFF");

    public static MediaColor ColorWhite54 { get; } = ParseHex("#8AFFFFFF");

    public static MediaColor ColorBlack { get; } = ParseHex("#000000");

    public static MediaColor ColorBlack87 { get; } = ParseHex("#DE000000");

    public static MediaColor ColorBlack54 { get; } = ParseHex("#8A000000");

    /*
    * App Brush Tokens
    */

    public static MediaBrush BrushBackgroundDark { get; } = CreateFrozenBrush(ColorBackgroundDark);

    public static MediaBrush BrushBackground { get; } = CreateFrozenBrush(ColorBackground);

    public static MediaBrush BrushPrimaryGreen { get; } = CreateFrozenBrush(ColorPrimaryGreen);

    public static MediaBrush BrushPrimaryWarning { get; } = CreateFrozenBrush(ColorPrimaryYellow);

    public static MediaBrush BrushPrimaryDanger { get; } = CreateFrozenBrush(ColorPrimaryOrange);

    public static MediaBrush BrushWhite { get; } = CreateFrozenBrush(ColorWhite);

    public static MediaBrush BrushWhite87 { get; } = CreateFrozenBrush(ColorWhite87);

    public static MediaBrush BrushWhite54 { get; } = CreateFrozenBrush(ColorWhite54);

    public static MediaBrush BrushBlack { get; } = CreateFrozenBrush(ColorBlack);

    public static MediaBrush BrushBlack87 { get; } = CreateFrozenBrush(ColorBlack87);

    public static MediaBrush BrushBlack54 { get; } = CreateFrozenBrush(ColorBlack54);

    /*
    *  App Drawing Color for WinForms
    */

    public static DrawingColor DrawingColorBackgroundDark { get; } = ToDrawingColor(ColorBackgroundDark);

    public static DrawingColor DrawingColorWhite { get; } = ToDrawingColor(ColorWhite);

    public static DrawingColor DrawingColorBackground { get; } = ToDrawingColor(ColorBackground);

    private static MediaSolidColorBrush CreateFrozenBrush(MediaColor color)
    {
        var brush = new MediaSolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static DrawingColor ToDrawingColor(MediaColor color)
    {
        return DrawingColor.FromArgb(color.A, color.R, color.G, color.B);
    }

    private static MediaColor ParseHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            throw new ArgumentException("颜色值不能为空。", nameof(hex));
        }

        var normalized = hex[0] == '#' ? hex[1..] : hex;
        return normalized.Length switch
        {
            6 => MediaColor.FromRgb(
                byte.Parse(normalized[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(normalized[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(normalized[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture)),
            8 => MediaColor.FromArgb(
                byte.Parse(normalized[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(normalized[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(normalized[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(normalized[6..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture)),
            _ => throw new FormatException($"不支持的颜色格式: {hex}")
        };
    }
}
