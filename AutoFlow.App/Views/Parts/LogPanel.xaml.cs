namespace AutoFlow.App.Views.Parts;

public partial class LogPanel : System.Windows.Controls.UserControl
{
    public LogPanel()
    {
        InitializeComponent();
    }

    private void LogOutputTextBox_OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        LogOutputTextBox.ScrollToEnd();
    }
}
