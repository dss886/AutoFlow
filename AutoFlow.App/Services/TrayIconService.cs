using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using DrawingFont = System.Drawing.Font;
using DrawingFontStyle = System.Drawing.FontStyle;
using DrawingGraphicsUnit = System.Drawing.GraphicsUnit;
using DrawingPoint = System.Drawing.Point;
using DrawingSize = System.Drawing.Size;
using Forms = System.Windows.Forms;

namespace AutoFlow.App.Services;

internal sealed class TrayIconService : IDisposable
{
    private const int ContextMenuOuterPadding = 16;
    private const int MenuItemWidth = 240;
    private const int MenuItemHeight = 72;

    internal static readonly Color MenuBackgroundColor = Color.FromArgb(51, 51, 61);
    internal static readonly Color MenuTextColor = Color.White;

    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Icon _notifyIconImage;
    private readonly DrawingFont _notifyMenuFont;

    public TrayIconService(Action onOpenMainWindow, Action onExitApplication)
    {
        _notifyMenuFont = new DrawingFont("Microsoft YaHei", 10F, DrawingFontStyle.Regular, DrawingGraphicsUnit.Point);

        var openMenuItem = CreateNotifyMenuItem("打开主窗口");
        openMenuItem.Click += (_, _) => onOpenMainWindow();

        var exitMenuItem = CreateNotifyMenuItem("退出程序");
        exitMenuItem.Click += (_, _) => onExitApplication();

        var contextMenu = new RoundedContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Font = _notifyMenuFont,
            MinimumSize = new DrawingSize(MenuItemWidth + (ContextMenuOuterPadding * 2), MenuItemHeight * 2 + (ContextMenuOuterPadding * 2)),
            BackColor = MenuBackgroundColor,
            ForeColor = MenuTextColor,
        };

        contextMenu.Items.Add(openMenuItem);
        contextMenu.Items.Add(exitMenuItem);
        ApplyContextMenuPadding(contextMenu);

        _notifyIconImage = LoadNotifyIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "AutoFlow",
            Icon = _notifyIconImage,
            Visible = true,
            ContextMenuStrip = contextMenu,
        };

        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
            {
                onOpenMainWindow();
            }
        };
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _notifyIconImage.Dispose();
        _notifyMenuFont.Dispose();
    }

    private static Forms.ToolStripMenuItem CreateNotifyMenuItem(string text)
    {
        return new Forms.ToolStripMenuItem(text)
        {
            AutoSize = false,
            Size = new DrawingSize(MenuItemWidth, MenuItemHeight),
            Margin = new Forms.Padding(0),
            Padding = new Forms.Padding(16, 0, 16, 0),
            ForeColor = MenuTextColor,
        };
    }

    private static void ApplyContextMenuPadding(Forms.ContextMenuStrip contextMenu)
    {
        for (var index = 0; index < contextMenu.Items.Count; index++)
        {
            var item = contextMenu.Items[index];
            var top = index == 0 ? ContextMenuOuterPadding : 0;
            var bottom = index == contextMenu.Items.Count - 1 ? ContextMenuOuterPadding : 0;
            item.Margin = new Forms.Padding(ContextMenuOuterPadding, top, ContextMenuOuterPadding, bottom);
        }
    }

    private static Icon LoadNotifyIcon()
    {
        var resourceInfo = System.Windows.Application.GetResourceStream(new Uri("Assets/AppIcon.png", UriKind.Relative));
        if (resourceInfo?.Stream is null)
        {
            return (Icon)SystemIcons.Application.Clone();
        }

        using var stream = resourceInfo.Stream;
        using var bitmap = new Bitmap(stream);
        using var resizedBitmap = new Bitmap(bitmap, new DrawingSize(32, 32));

        var handle = resizedBitmap.GetHicon();
        try
        {
            using var tempIcon = Icon.FromHandle(handle);
            return (Icon)tempIcon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

internal sealed class RoundedContextMenuStrip : Forms.ContextMenuStrip
{
    private const int CornerRadius = 12;

    public RoundedContextMenuStrip()
    {
        Renderer = new RoundedMenuRenderer();
        BackColor = TrayIconService.MenuBackgroundColor;
        ForeColor = TrayIconService.MenuTextColor;
    }

    protected override void OnOpening(System.ComponentModel.CancelEventArgs e)
    {
        base.OnOpening(e);
        UpdateRoundedRegion();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateRoundedRegion();
    }

    private void UpdateRoundedRegion()
    {
        if (Width <= 0 || Height <= 0)
        {
            return;
        }

        using var path = CreateRoundedPath(new Rectangle(0, 0, Width, Height), CornerRadius);
        var previousRegion = Region;
        Region = new Region(path);
        previousRegion?.Dispose();
    }

    private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        var arc = new Rectangle(bounds.Location, new DrawingSize(diameter, diameter));

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class RoundedMenuRenderer : Forms.ToolStripProfessionalRenderer
{
    private static readonly Color MenuBorderColor = Color.FromArgb(88, 88, 104);
    private static readonly Color MenuHoverColor = Color.FromArgb(72, 72, 86);

    public RoundedMenuRenderer() : base(new RoundedMenuColorTable())
    {
    }

    protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(DrawingPoint.Empty, e.ToolStrip.Size);
        bounds.Width -= 1;
        bounds.Height -= 1;

        using var backgroundPath = CreateRoundedPath(bounds, 12);
        using var backgroundBrush = new SolidBrush(e.ToolStrip.BackColor);
        using var borderPen = new Pen(MenuBorderColor);

        e.Graphics.FillPath(backgroundBrush, backgroundPath);
        e.Graphics.DrawPath(borderPen, backgroundPath);
    }

    protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected)
        {
            return;
        }

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(DrawingPoint.Empty, e.Item.Size);
        bounds.Inflate(-2, 0);

        using var path = CreateRoundedPath(bounds, 8);
        using var brush = new SolidBrush(MenuHoverColor);
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        var textBounds = new Rectangle(
            e.Item.Padding.Left,
            0,
            Math.Max(0, e.Item.Width - e.Item.Padding.Horizontal),
            e.Item.Height);

        var textColor = e.TextColor.IsEmpty ? TrayIconService.MenuTextColor : e.TextColor;
        Forms.TextRenderer.DrawText(
            e.Graphics,
            e.Text,
            e.TextFont,
            textBounds,
            textColor,
            Forms.TextFormatFlags.Left |
            Forms.TextFormatFlags.VerticalCenter |
            Forms.TextFormatFlags.EndEllipsis |
            Forms.TextFormatFlags.NoPrefix |
            Forms.TextFormatFlags.SingleLine);
    }

    protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
    {
        
    }

    private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        var arc = new Rectangle(bounds.Location, new DrawingSize(diameter, diameter));

        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class RoundedMenuColorTable : Forms.ProfessionalColorTable
{
    public override Color ToolStripDropDownBackground => TrayIconService.MenuBackgroundColor;
    public override Color MenuBorder => Color.Transparent;
    public override Color MenuItemBorder => Color.Transparent;
    public override Color MenuItemSelected => Color.Transparent;
    public override Color MenuItemSelectedGradientBegin => Color.Transparent;
    public override Color MenuItemSelectedGradientEnd => Color.Transparent;
    public override Color MenuItemPressedGradientBegin => Color.Transparent;
    public override Color MenuItemPressedGradientMiddle => Color.Transparent;
    public override Color MenuItemPressedGradientEnd => Color.Transparent;
    public override Color ImageMarginGradientBegin => TrayIconService.MenuBackgroundColor;
    public override Color ImageMarginGradientMiddle => TrayIconService.MenuBackgroundColor;
    public override Color ImageMarginGradientEnd => TrayIconService.MenuBackgroundColor;
    public override Color SeparatorDark => Color.Transparent;
    public override Color SeparatorLight => Color.Transparent;
}
