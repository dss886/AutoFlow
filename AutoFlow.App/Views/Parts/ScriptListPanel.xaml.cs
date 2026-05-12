using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AutoFlow.App.Views.Parts;

public partial class ScriptListPanel : System.Windows.Controls.UserControl
{
    public ScriptListPanel()
    {
        InitializeComponent();
    }

    private void ScriptList_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is not { } item)
        {
            return;
        }

        item.IsSelected = true;
        item.Focus();
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T matched)
            {
                return matched;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
