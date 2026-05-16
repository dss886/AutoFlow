using System.Collections.Specialized;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Threading;
using AutoFlow.App.Models;
using AutoFlow.App.ViewModels;

namespace AutoFlow.App.Views.Parts;

public partial class LogPanel : System.Windows.Controls.UserControl
{
    private INotifyCollectionChanged? _currentLogEntries;
    private MainWindowViewModel? _viewModel;

    public LogPanel()
    {
        InitializeComponent();
        DataContextChanged += LogPanel_OnDataContextChanged;
    }

    private void LogPanel_OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (_currentLogEntries is not null)
        {
            _currentLogEntries.CollectionChanged -= LogEntries_OnCollectionChanged;
            _currentLogEntries = null;
        }

        if (e.NewValue is MainWindowViewModel viewModel)
        {
            _viewModel = viewModel;
            _currentLogEntries = viewModel.LogEntries;
            _currentLogEntries.CollectionChanged += LogEntries_OnCollectionChanged;
            RebuildDocument();
            ScrollToEnd();
        }
        else
        {
            _viewModel = null;
            LogOutputTextBox.Document = CreateDocument();
        }
    }

    private void LogEntries_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            RebuildDocument();
            ScrollToEnd();
        }, DispatcherPriority.Background);
    }

    private void RebuildDocument()
    {
        var document = CreateDocument();
        if (_viewModel is not null)
        {
            foreach (var entry in _viewModel.LogEntries)
            {
                document.Blocks.Add(CreateParagraph(entry));
            }
        }

        LogOutputTextBox.Document = document;
    }

    private static FlowDocument CreateDocument()
    {
        return new FlowDocument
        {
            PagePadding = new Thickness(0),
            TextAlignment = TextAlignment.Left,
        };
    }

    private Paragraph CreateParagraph(AppLogEntry entry)
    {
        return new Paragraph(new Run(entry.Format()))
        {
            Margin = new Thickness(0, 0, 0, 2),
            Foreground = ResolveBrush(entry.Level),
        };
    }

    private System.Windows.Media.Brush ResolveBrush(LogLevel level)
    {
        return level switch
        {
            LogLevel.Debug => (System.Windows.Media.Brush)FindResource("BrushPrimaryBlue"),
            LogLevel.Info => (System.Windows.Media.Brush)FindResource("BrushPrimaryGreen"),
            LogLevel.Warning => (System.Windows.Media.Brush)FindResource("BrushPrimaryWarning"),
            LogLevel.Error => (System.Windows.Media.Brush)FindResource("BrushPrimaryDanger"),
            _ => (System.Windows.Media.Brush)FindResource("BrushWhite87"),
        };
    }

    private void ScrollToEnd()
    {
        if (LogOutputTextBox.Document?.Blocks.Count > 0)
        {
            LogOutputTextBox.ScrollToEnd();
        }
    }
}
