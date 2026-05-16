using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using AutoFlow.App.Infrastructure;
using System.Windows.Input;
using AutoFlow.App.Styling;
using DrawingFont = System.Drawing.Font;
using DrawingFontStyle = System.Drawing.FontStyle;
using DrawingGraphicsUnit = System.Drawing.GraphicsUnit;
using DrawingPoint = System.Drawing.Point;
using DrawingSize = System.Drawing.Size;
using Forms = System.Windows.Forms;
using AutoFlow.App.Models;

namespace AutoFlow.App.Services;

internal static class TrayMenuStyle
{
    // 这里读取 Colors.xaml 中的颜色值比较绕且有性能损失，直接使用颜色值了
    public static readonly Color BackgroundDark = Color.FromArgb(0x33, 0x33, 0x3D);
    public static readonly Color Background = Color.FromArgb(0x42, 0x42, 0x4C);
    public static readonly Color Foreground = Color.White;
}

internal sealed class TrayIconService : IDisposable
{
    private readonly IEventBus _eventBus;
    private readonly IDisposable _trayInfoSubscription;
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Icon _notifyIconImage;
    private readonly DrawingFont _notifyMenuFont;
    private bool _isDisposed;

    public TrayIconService(IEventBus eventBus)
    {
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _trayInfoSubscription = _eventBus.Subscribe<TrayInfoRequestedMessage>(message =>
            ShowInfo(message.Title, message.Message, message.TimeoutMilliseconds));
        _notifyMenuFont = new DrawingFont(AppMenuTokens.FontFamilyName, AppMenuTokens.TrayFontSize, DrawingFontStyle.Regular, DrawingGraphicsUnit.Point);

        var openMenuItem = CreateNotifyMenuItem("打开主窗口");
        openMenuItem.Click += (_, _) => _eventBus.Publish(new ShowMainWindowRequestedMessage());

        var exitMenuItem = CreateNotifyMenuItem("退出程序");
        exitMenuItem.Click += (_, _) => _eventBus.Publish(new ExitApplicationRequestedMessage());

        var contextMenu = new RoundedContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = false,
            Font = _notifyMenuFont,
            MinimumSize = new DrawingSize(AppMenuTokens.TrayContextMenuMinWidth, AppMenuTokens.GetTrayContextMenuMinHeight(2)),
            BackColor = TrayMenuStyle.BackgroundDark,
            ForeColor = TrayMenuStyle.Foreground,
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
                _eventBus.Publish(new ShowMainWindowRequestedMessage());
            }
        };
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _trayInfoSubscription.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _notifyIconImage.Dispose();
        _notifyMenuFont.Dispose();
    }

    public void ShowInfo(string title, string message, int timeoutMilliseconds = 3000)
    {
        _notifyIcon.BalloonTipTitle = title;
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(timeoutMilliseconds);
    }

    private static Forms.ToolStripMenuItem CreateNotifyMenuItem(string text)
    {
        return new Forms.ToolStripMenuItem(text)
        {
            AutoSize = false,
            Size = new DrawingSize(AppMenuTokens.TrayMenuItemWidth, AppMenuTokens.TrayMenuItemHeight),
            Margin = new Forms.Padding(0),
            Padding = new Forms.Padding(AppMenuTokens.TrayMenuItemHorizontalPadding, 0, AppMenuTokens.TrayMenuItemHorizontalPadding, 0),
            ForeColor = TrayMenuStyle.Foreground,
        };
    }

    private static void ApplyContextMenuPadding(Forms.ContextMenuStrip contextMenu)
    {
        for (var index = 0; index < contextMenu.Items.Count; index++)
        {
            var item = contextMenu.Items[index];
            var top = index == 0 ? AppMenuTokens.TrayContextMenuOuterPadding : 0;
            var bottom = index == contextMenu.Items.Count - 1 ? AppMenuTokens.TrayContextMenuOuterPadding : 0;
            item.Margin = new Forms.Padding(AppMenuTokens.TrayContextMenuOuterPadding, top, AppMenuTokens.TrayContextMenuOuterPadding, bottom);
        }
    }

    private static Icon LoadNotifyIcon()
    {
        var resourceInfo = System.Windows.Application.GetResourceStream(new Uri("Assets/Icons/AppIcon.png", UriKind.Relative));
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
    public RoundedContextMenuStrip()
    {
        Renderer = new RoundedMenuRenderer();
        BackColor = TrayMenuStyle.BackgroundDark;
        ForeColor = TrayMenuStyle.Foreground;
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

        using var path = CreateRoundedPath(new Rectangle(0, 0, Width, Height), AppMenuTokens.ContextMenuCornerRadiusValue);
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
    public RoundedMenuRenderer() : base(new RoundedMenuColorTable())
    {
    }

    protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var bounds = new Rectangle(DrawingPoint.Empty, e.ToolStrip.Size);
        bounds.Width -= 1;
        bounds.Height -= 1;

        using var backgroundPath = CreateRoundedPath(bounds, AppMenuTokens.ContextMenuCornerRadiusValue);
        using var backgroundBrush = new SolidBrush(e.ToolStrip.BackColor);
        using var borderPen = new Pen(TrayMenuStyle.Background);

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

        using var path = CreateRoundedPath(bounds, AppMenuTokens.MenuItemCornerRadiusValue);
        using var brush = new SolidBrush(TrayMenuStyle.Background);
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    {
        var textBounds = new Rectangle(
            e.Item.Padding.Left,
            0,
            Math.Max(0, e.Item.Width - e.Item.Padding.Horizontal),
            e.Item.Height);

        var textColor = e.TextColor.IsEmpty ? TrayMenuStyle.Foreground : e.TextColor;
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
    public override Color ToolStripDropDownBackground => TrayMenuStyle.BackgroundDark;
    public override Color MenuBorder => Color.Transparent;
    public override Color MenuItemBorder => Color.Transparent;
    public override Color MenuItemSelected => Color.Transparent;
    public override Color MenuItemSelectedGradientBegin => Color.Transparent;
    public override Color MenuItemSelectedGradientEnd => Color.Transparent;
    public override Color MenuItemPressedGradientBegin => Color.Transparent;
    public override Color MenuItemPressedGradientMiddle => Color.Transparent;
    public override Color MenuItemPressedGradientEnd => Color.Transparent;
    public override Color ImageMarginGradientBegin => TrayMenuStyle.BackgroundDark;
    public override Color ImageMarginGradientMiddle => TrayMenuStyle.BackgroundDark;
    public override Color ImageMarginGradientEnd => TrayMenuStyle.BackgroundDark;
    public override Color SeparatorDark => Color.Transparent;
    public override Color SeparatorLight => Color.Transparent;
}
