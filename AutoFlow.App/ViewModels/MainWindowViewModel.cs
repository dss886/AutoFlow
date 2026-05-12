using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AutoFlow.App.Infrastructure;
using AutoFlow.App.Models;
using AutoFlow.App.Services;

namespace AutoFlow.App.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private const int MaxLogLineCount = 3000;

    private readonly Action _closeWindow;
    private readonly ScriptCatalogService _catalogService;
    private readonly ScriptRunnerService _runnerService;
    private readonly FileSystemWatcher _fileSystemWatcher;
    private readonly DispatcherTimer _scriptRefreshTimer;
    private readonly Queue<string> _logEntries = new();
    private ScriptDefinition? _selectedScript;
    private string _logOutput = string.Empty;
    private string _mousePositionText = "鼠标位置: X 0, Y 0";
    private bool _isMousePositionVisible;
    private bool _isDisposed;

    public MainWindowViewModel(Action closeWindow)
    {
        _closeWindow = closeWindow ?? throw new ArgumentNullException(nameof(closeWindow));

        ScriptsDirectory = PathService.ResolveScriptsDirectory();
        PathService.EnsureDirectory(ScriptsDirectory);

        _catalogService = new ScriptCatalogService();
        _runnerService = new ScriptRunnerService(new LuaAutomationRuntime(new AutomationInputService()));
        _runnerService.LogGenerated += RunnerService_OnLogGenerated;
        _runnerService.ScriptStateChanged += RunnerService_OnScriptStateChanged;

        _scriptRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200),
        };
        _scriptRefreshTimer.Tick += ScriptRefreshTimer_OnTick;

        _fileSystemWatcher = new FileSystemWatcher(ScriptsDirectory, "*.lua")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true,
        };
        _fileSystemWatcher.Created += FileSystemWatcher_OnChanged;
        _fileSystemWatcher.Changed += FileSystemWatcher_OnChanged;
        _fileSystemWatcher.Deleted += FileSystemWatcher_OnChanged;
        _fileSystemWatcher.Renamed += FileSystemWatcher_OnChanged;

        ToggleRunStateCommand = new AsyncRelayCommand(ToggleRunStateAsync);
        OpenScriptsFolderCommand = new RelayCommand(OpenScriptsFolder);
        OpenInEditorCommand = new RelayCommand(OpenInEditor);
        ToggleMousePositionCommand = new RelayCommand(ToggleMousePosition);
        CloseWindowCommand = new RelayCommand(_closeWindow);

        LoadScripts();
        AppendLog("应用已启动。");
        AppendLog($"脚本目录: {ScriptsDirectory}");
    }

    public ObservableCollection<ScriptDefinition> Scripts { get; } = new();

    public string ScriptsDirectory { get; }

    public ScriptDefinition? SelectedScript
    {
        get => _selectedScript;
        set
        {
            if (_selectedScript == value)
            {
                return;
            }

            _selectedScript = value;
            OnPropertyChanged();
        }
    }

    public string LogOutput
    {
        get => _logOutput;
        private set
        {
            if (_logOutput == value)
            {
                return;
            }

            _logOutput = value;
            OnPropertyChanged();
        }
    }

    public bool IsScriptRunning => _runnerService.IsRunning;

    public string RunControlButtonText => IsScriptRunning ? "停止脚本（鼠标后退键）" : "运行脚本（鼠标后退键）";

    public bool IsMousePositionVisible
    {
        get => _isMousePositionVisible;
        private set
        {
            if (_isMousePositionVisible == value)
            {
                return;
            }

            _isMousePositionVisible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(MousePositionToggleButtonText));
        }
    }

    public string MousePositionText
    {
        get => _mousePositionText;
        private set
        {
            if (_mousePositionText == value)
            {
                return;
            }

            _mousePositionText = value;
            OnPropertyChanged();
        }
    }

    public string MousePositionToggleButtonText => IsMousePositionVisible ? "隐藏鼠标位置" : "显示鼠标位置";

    public ICommand ToggleRunStateCommand { get; }

    public ICommand OpenScriptsFolderCommand { get; }

    public ICommand OpenInEditorCommand { get; }

    public ICommand ToggleMousePositionCommand { get; }

    public ICommand CloseWindowCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ExecuteToggleRunStateCommand()
    {
        if (ToggleRunStateCommand.CanExecute(null))
        {
            ToggleRunStateCommand.Execute(null);
        }
    }

    public void UpdateMousePosition(int x, int y)
    {
        MousePositionText = $"鼠标位置: X {x}, Y {y}";
    }

    public void AppendLogMessage(string message)
    {
        AppendLog(message);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _scriptRefreshTimer.Stop();
        _scriptRefreshTimer.Tick -= ScriptRefreshTimer_OnTick;

        _fileSystemWatcher.Created -= FileSystemWatcher_OnChanged;
        _fileSystemWatcher.Changed -= FileSystemWatcher_OnChanged;
        _fileSystemWatcher.Deleted -= FileSystemWatcher_OnChanged;
        _fileSystemWatcher.Renamed -= FileSystemWatcher_OnChanged;
        _fileSystemWatcher.Dispose();

        _runnerService.LogGenerated -= RunnerService_OnLogGenerated;
        _runnerService.ScriptStateChanged -= RunnerService_OnScriptStateChanged;
        _runnerService.Stop();
    }

    private async Task ToggleRunStateAsync()
    {
        if (IsScriptRunning)
        {
            _runnerService.Stop();
            return;
        }

        await RunSelectedScriptAsync();
    }

    private async Task RunSelectedScriptAsync()
    {
        if (SelectedScript is null)
        {
            System.Windows.MessageBox.Show("请先选择一个脚本。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!EnsureSelectedScriptExists())
        {
            return;
        }

        try
        {
            await _runnerService.StartAsync(SelectedScript);
        }
        catch (Exception ex)
        {
            AppendLog($"启动脚本失败: {ex.Message}");
        }
    }

    private void OpenScriptsFolder()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = ScriptsDirectory,
            UseShellExecute = true,
        });
    }

    private void OpenInEditor()
    {
        if (SelectedScript is null)
        {
            System.Windows.MessageBox.Show("请先选择一个脚本。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!EnsureSelectedScriptExists())
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = SelectedScript.FilePath,
            UseShellExecute = true,
        });
    }

    private void ToggleMousePosition()
    {
        IsMousePositionVisible = !IsMousePositionVisible;
    }

    private void LoadScripts()
    {
        var selectedPath = SelectedScript?.FilePath;
        var scripts = _catalogService.LoadScripts(ScriptsDirectory);

        Scripts.Clear();
        foreach (var script in scripts)
        {
            script.IsRunning = string.Equals(script.FilePath, _runnerService.RunningScript?.FilePath, StringComparison.OrdinalIgnoreCase);
            Scripts.Add(script);
        }

        SelectedScript = Scripts.FirstOrDefault(item =>
            string.Equals(item.FilePath, selectedPath, StringComparison.OrdinalIgnoreCase))
            ?? Scripts.FirstOrDefault();
    }

    private bool EnsureSelectedScriptExists()
    {
        var selectedScript = SelectedScript;
        if (selectedScript is null)
        {
            return false;
        }

        if (File.Exists(selectedScript.FilePath))
        {
            return true;
        }

        LoadScripts();
        AppendLog($"脚本文件不存在，已跳过操作: {selectedScript.FileName}");
        System.Windows.MessageBox.Show("所选脚本文件已不存在，列表已自动刷新。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    private void AppendLog(string message)
    {
        var timestamped = $"[{DateTime.Now:HH:mm:ss}] {message}";
        _logEntries.Enqueue(timestamped);

        while (_logEntries.Count > MaxLogLineCount)
        {
            _logEntries.Dequeue();
        }

        LogOutput = string.Join(Environment.NewLine, _logEntries);
    }

    private void RunnerService_OnLogGenerated(string message)
    {
        GetUiDispatcher().Invoke(() => AppendLog(message));
    }

    private void RunnerService_OnScriptStateChanged(ScriptDefinition? script, bool isRunning)
    {
        GetUiDispatcher().Invoke(() =>
        {
            if (script is not null)
            {
                var item = Scripts.FirstOrDefault(candidate =>
                    string.Equals(candidate.FilePath, script.FilePath, StringComparison.OrdinalIgnoreCase));

                if (item is not null)
                {
                    item.IsRunning = isRunning;
                }
            }

            OnPropertyChanged(nameof(IsScriptRunning));
            OnPropertyChanged(nameof(RunControlButtonText));
        });
    }

    private void ScriptRefreshTimer_OnTick(object? sender, EventArgs e)
    {
        _scriptRefreshTimer.Stop();
        LoadScripts();
        AppendLog("检测到脚本目录变化，已自动刷新。");
    }

    private void FileSystemWatcher_OnChanged(object sender, FileSystemEventArgs e)
    {
        GetUiDispatcher().InvokeAsync(() =>
        {
            _scriptRefreshTimer.Stop();
            _scriptRefreshTimer.Start();
        });
    }

    private static Dispatcher GetUiDispatcher()
    {
        return System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
