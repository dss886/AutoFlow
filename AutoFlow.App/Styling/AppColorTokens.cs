using System.Globalization;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace AutoFlow.App.Styling;

public static class AppColorTokens
{
    public static MediaBrush BrushBackgroundDark { get; } = CreateFrozenBrush("#33333D");

    public static MediaBrush BrushBackground { get; } = CreateFrozenBrush("#42424C");

    public static MediaBrush BrushPrimaryGreen { get; } = CreateFrozenBrush("#1EB980");

    public static MediaBrush BrushPrimaryWarning { get; } = CreateFrozenBrush("#FFCF44");

    public static MediaBrush BrushPrimaryDanger { get; } = CreateFrozenBrush("#FF6859");

    public static MediaBrush BrushWhite { get; } = CreateFrozenBrush("#FFFFFF");

    public static MediaBrush BrushWhite87 { get; } = CreateFrozenBrush("#DEFFFFFF");

    public static MediaBrush BrushWhite54 { get; } = CreateFrozenBrush("#8AFFFFFF");

    public static MediaBrush BrushBlack { get; } = CreateFrozenBrush("#000000");

    public static MediaBrush BrushBlack87 { get; } = CreateFrozenBrush("#DE000000");

    public static MediaBrush BrushBlack54 { get; } = CreateFrozenBrush("#8A000000");

    private static MediaSolidColorBrush CreateFrozenBrush(string hex)
    {
        var color = ParseHex(hex);
        var brush = new MediaSolidColorBrush(color);
        brush.Freeze();
        return brush;
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
