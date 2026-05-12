using System.Windows;
using MediaFontFamily = System.Windows.Media.FontFamily;

namespace AutoFlow.App.Styling;

public static class AppMenuTokens
{
    public const string FontFamilyName = "Microsoft YaHei";

    public static MediaFontFamily WpfFontFamily { get; } = new(FontFamilyName);

    public static double WpfFontSize => 14D;

    public static float TrayFontSize => 10F;

    public static int ContextMenuCornerRadiusValue => 12;

    public static int MenuItemCornerRadiusValue => 8;

    public static CornerRadius ContextMenuCornerRadius { get; } = new(ContextMenuCornerRadiusValue);

    public static CornerRadius MenuItemCornerRadius { get; } = new(MenuItemCornerRadiusValue);

    public static Thickness MenuBorderThickness { get; } = new(1);

    public static Thickness MenuItemBorderThickness { get; } = new(0);

    public static double WpfContextMenuMinWidth => 140D;

    public static Thickness WpfContextMenuPadding { get; } = new(8);

    public static double WpfMenuItemHeight => 40D;

    public static Thickness WpfMenuItemPadding { get; } = new(16, 0, 16, 0);

    public static int TrayContextMenuOuterPadding => 16;

    public static int TrayMenuItemWidth => 240;

    public static int TrayMenuItemHeight => 72;

    public static int TrayMenuItemHorizontalPadding => 16;

    public static int TrayContextMenuMinWidth => TrayMenuItemWidth + (TrayContextMenuOuterPadding * 2);

    public static int GetTrayContextMenuMinHeight(int itemCount)
    {
        return (TrayMenuItemHeight * itemCount) + (TrayContextMenuOuterPadding * 2);
    }

}
