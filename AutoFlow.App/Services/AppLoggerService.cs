using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Threading;
using System.Windows.Threading;
using AutoFlow.App.Models;

namespace AutoFlow.App.Services;

public sealed class AppLoggerService
{
    private const int MaxLogLineCount = 3000;
    private const int FlushBatchSize = 200;
    private const int TrimBatchSize = 200;

    private readonly Dispatcher _dispatcher;
    private readonly ObservableCollection<AppLogEntry> _entries = new();
    private readonly ConcurrentQueue<AppLogEntry> _pendingEntries = new();
    private int _isFlushScheduled;

    public AppLoggerService()
    {
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        Entries = new ReadOnlyObservableCollection<AppLogEntry>(_entries);
    }

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
        _pendingEntries.Enqueue(entry);
        ScheduleFlush();
    }

    private void ScheduleFlush()
    {
        if (Interlocked.Exchange(ref _isFlushScheduled, 1) == 1)
        {
            return;
        }

        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(FlushPendingEntries));
    }

    private void FlushPendingEntries()
    {
        if (!_dispatcher.CheckAccess())
        {
            ScheduleFlush();
            return;
        }

        try
        {
            var flushedCount = 0;
            while (flushedCount < FlushBatchSize && _pendingEntries.TryDequeue(out var entry))
            {
                _entries.Add(entry);
                flushedCount++;
            }

            TrimEntriesIfNeeded();
        }
        finally
        {
            Interlocked.Exchange(ref _isFlushScheduled, 0);
        }

        if (!_pendingEntries.IsEmpty)
        {
            ScheduleFlush();
        }
    }

    private void TrimEntriesIfNeeded()
    {
        if (_entries.Count <= MaxLogLineCount + TrimBatchSize)
        {
            return;
        }

        var removeCount = _entries.Count - MaxLogLineCount;
        for (var index = 0; index < removeCount; index++)
        {
            _entries.RemoveAt(0);
        }
    }
}
