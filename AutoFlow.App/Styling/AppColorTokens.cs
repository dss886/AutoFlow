using System.Globalization;
using DrawingColor = System.Drawing.Color;
using MediaBrush = System.Windows.Media.Brush;
using MediaColor = System.Windows.Media.Color;
using MediaSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace AutoFlow.App.Styling;

public static class AppColorTokens
{
    public static MediaColor BackgroundDarkColor { get; } = ParseHex("#33333D");

    public static MediaColor BackgroundColor { get; } = ParseHex("#42424C");

    public static MediaColor PrimaryColor { get; } = ParseHex("#2196F3");

    public static MediaColor PrimaryButtonColor { get; } = ParseHex("#2563EB");

    public static MediaColor ButtonDisabledForegroundColor { get; } = ParseHex("#94A3B8");

    public static MediaColor ButtonDisabledBackgroundColor { get; } = ParseHex("#E2E8F0");

    public static MediaColor SecondaryButtonForegroundColor { get; } = ParseHex("#1E3A8A");

    public static MediaColor SecondaryButtonBackgroundColor { get; } = ParseHex("#EFF6FF");

    public static MediaColor SecondaryButtonBorderColor { get; } = ParseHex("#D0E2FF");

    public static MediaColor DangerColor { get; } = ParseHex("#DC2626");

    public static MediaColor AccentActiveForegroundColor { get; } = ParseHex("#0F172A");

    public static MediaColor AccentActiveBackgroundColor { get; } = ParseHex("#DBEAFE");

    public static MediaColor AccentActiveBorderColor { get; } = ParseHex("#93C5FD");

    public static MediaColor ForegroundPrimaryColor { get; } = ParseHex("#FFFFFF");

    public static MediaColor ForegroundMutedColor { get; } = ParseHex("#DEFFFFFF");

    public static MediaColor MenuBorderColor { get; } = ParseHex("#585868");

    public static MediaColor MenuHoverColor { get; } = ParseHex("#484856");

    public static MediaColor MenuDisabledForegroundColor { get; } = ParseHex("#80FFFFFF");

    public static MediaBrush BackgroundDarkBrush { get; } = CreateFrozenBrush(BackgroundDarkColor);

    public static MediaBrush BackgroundBrush { get; } = CreateFrozenBrush(BackgroundColor);

    public static MediaBrush PrimaryBrush { get; } = CreateFrozenBrush(PrimaryColor);

    public static MediaBrush PrimaryButtonBrush { get; } = CreateFrozenBrush(PrimaryButtonColor);

    public static MediaBrush ButtonDisabledForegroundBrush { get; } = CreateFrozenBrush(ButtonDisabledForegroundColor);

    public static MediaBrush ButtonDisabledBackgroundBrush { get; } = CreateFrozenBrush(ButtonDisabledBackgroundColor);

    public static MediaBrush SecondaryButtonForegroundBrush { get; } = CreateFrozenBrush(SecondaryButtonForegroundColor);

    public static MediaBrush SecondaryButtonBackgroundBrush { get; } = CreateFrozenBrush(SecondaryButtonBackgroundColor);

    public static MediaBrush SecondaryButtonBorderBrush { get; } = CreateFrozenBrush(SecondaryButtonBorderColor);

    public static MediaBrush DangerBrush { get; } = CreateFrozenBrush(DangerColor);

    public static MediaBrush AccentActiveForegroundBrush { get; } = CreateFrozenBrush(AccentActiveForegroundColor);

    public static MediaBrush AccentActiveBackgroundBrush { get; } = CreateFrozenBrush(AccentActiveBackgroundColor);

    public static MediaBrush AccentActiveBorderBrush { get; } = CreateFrozenBrush(AccentActiveBorderColor);

    public static MediaBrush ForegroundPrimaryBrush { get; } = CreateFrozenBrush(ForegroundPrimaryColor);

    public static MediaBrush ForegroundMutedBrush { get; } = CreateFrozenBrush(ForegroundMutedColor);

    public static MediaBrush MenuBorderBrush { get; } = CreateFrozenBrush(MenuBorderColor);

    public static MediaBrush MenuHoverBrush { get; } = CreateFrozenBrush(MenuHoverColor);

    public static MediaBrush MenuDisabledForegroundBrush { get; } = CreateFrozenBrush(MenuDisabledForegroundColor);

    public static DrawingColor BackgroundDarkDrawingColor { get; } = ToDrawingColor(BackgroundDarkColor);

    public static DrawingColor ForegroundPrimaryDrawingColor { get; } = ToDrawingColor(ForegroundPrimaryColor);

    public static DrawingColor MenuBorderDrawingColor { get; } = ToDrawingColor(MenuBorderColor);

    public static DrawingColor MenuHoverDrawingColor { get; } = ToDrawingColor(MenuHoverColor);

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
