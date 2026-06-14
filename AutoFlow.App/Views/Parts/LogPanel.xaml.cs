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
    private bool _isScrollToEndQueued;

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
            QueueScrollToEnd();
        }
        else
        {
            _viewModel = null;
            LogOutputTextBox.Document = CreateDocument();
        }
    }

    private void LogEntries_OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => LogEntries_OnCollectionChanged(sender, e), DispatcherPriority.Background);
            return;
        }

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                AppendEntries(e.NewItems);
                QueueScrollToEnd();
                break;
            case NotifyCollectionChangedAction.Remove when e.OldStartingIndex == 0:
                RemoveLeadingBlocks(e.OldItems?.Count ?? 0);
                break;
            default:
                RebuildDocument();
                QueueScrollToEnd();
                break;
        }
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

    private void AppendEntries(System.Collections.IList? entries)
    {
        if (entries is null || entries.Count == 0)
        {
            return;
        }

        var document = LogOutputTextBox.Document ?? CreateDocument();
        if (!ReferenceEquals(LogOutputTextBox.Document, document))
        {
            LogOutputTextBox.Document = document;
        }

        foreach (var item in entries)
        {
            if (item is AppLogEntry entry)
            {
                document.Blocks.Add(CreateParagraph(entry));
            }
        }
    }

    private void RemoveLeadingBlocks(int count)
    {
        if (count <= 0 || LogOutputTextBox.Document is null)
        {
            return;
        }

        for (var index = 0; index < count; index++)
        {
            var firstBlock = LogOutputTextBox.Document.Blocks.FirstBlock;
            if (firstBlock is null)
            {
                break;
            }

            LogOutputTextBox.Document.Blocks.Remove(firstBlock);
        }
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

    private void QueueScrollToEnd()
    {
        if (_isScrollToEndQueued)
        {
            return;
        }

        _isScrollToEndQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            _isScrollToEndQueued = false;
            ScrollToEnd();
        }));
    }

    private void ScrollToEnd()
    {
        if (LogOutputTextBox.Document?.Blocks.Count > 0)
        {
            LogOutputTextBox.ScrollToEnd();
        }
    }
}
