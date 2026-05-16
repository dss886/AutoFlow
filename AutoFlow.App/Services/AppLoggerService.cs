using System.Collections.ObjectModel;
using System.Windows.Threading;
using AutoFlow.App.Models;

namespace AutoFlow.App.Services;

public sealed class AppLoggerService
{
    private const int MaxLogLineCount = 3000;
    private static readonly Lazy<AppLoggerService> _lazyInstance = new(() => new AppLoggerService());

    private readonly Dispatcher _dispatcher;
    private readonly ObservableCollection<AppLogEntry> _entries = new();

    private AppLoggerService()
    {
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        Entries = new ReadOnlyObservableCollection<AppLogEntry>(_entries);
    }

    public static AppLoggerService Instance => _lazyInstance.Value;

    public ReadOnlyObservableCollection<AppLogEntry> Entries { get; }

    public void V(string message, LogSource source = LogSource.System)
    {
        Append(message, LogLevel.Verbose, source);
    }

    public void D(string message, LogSource source = LogSource.System)
    {
        Append(message, LogLevel.Debug, source);
    }

    public void I(string message, LogSource source = LogSource.System)
    {
        Append(message, LogLevel.Info, source);
    }

    public void W(string message, LogSource source = LogSource.System)
    {
        Append(message, LogLevel.Warning, source);
    }

    public void E(string message, LogSource source = LogSource.System)
    {
        Append(message, LogLevel.Error, source);
    }

    private void Append(string message, LogLevel level, LogSource source)
    {
        Append(new AppLogEntry(DateTime.Now, source, level, message ?? string.Empty));
    }

    private void Append(AppLogEntry entry)
    {
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Invoke(() => Append(entry));
            return;
        }

        _entries.Add(entry);
        while (_entries.Count > MaxLogLineCount)
        {
            _entries.RemoveAt(0);
        }
    }
}
