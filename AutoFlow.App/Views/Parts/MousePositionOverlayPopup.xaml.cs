using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;

namespace AutoFlow.App.Views.Parts;

public partial class MousePositionOverlayPopup : System.Windows.Controls.UserControl
{
    public MousePositionOverlayPopup()
    {
        InitializeComponent();
    }

    public void UpdateContent(int x, int y, MediaColor color)
    {
        CoordinateTextBlock.Text = $"X {x}, Y {y}";
        ColorTextBlock.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        ColorPreviewBorder.Background = new SolidColorBrush(color);
    }
}
